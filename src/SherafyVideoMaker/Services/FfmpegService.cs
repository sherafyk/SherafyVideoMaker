using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SherafyVideoMaker.Models;

namespace SherafyVideoMaker.Services
{
    public class FfmpegService
    {
        private string? _selectedVideoEncoder;
        private bool? _lastUseGpuSetting;

        public void ProcessSegment(ProjectSettings settings, Segment segment, string clipPath, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(segment.AssignedClip))
            {
                throw new InvalidOperationException($"Segment {segment.Index} has no clip assignment.");
            }

            var ffmpegPath = Path.Combine(settings.FfmpegFolder, "ffmpeg.exe");
            var inputClip = clipPath;
            var outputClip = Path.Combine(settings.TempFolder, $"clip_{segment.Index}.mp4");

            if (!File.Exists(inputClip))
            {
                log($"Expected input clip missing for segment {segment.Index}: {inputClip}");
                throw new FileNotFoundException($"Input clip for segment {segment.Index} not found.", inputClip);
            }

            var slot = segment.Duration.TotalSeconds;
            var clipDuration = GetClipDuration(settings, inputClip, log);

            var mode = segment.FitMode;
            if (mode == FitMode.Auto)
            {
                mode = clipDuration >= slot ? FitMode.Trim : FitMode.Slow;
                log($"Auto fit mode resolved to {mode} for segment {segment.Index}.");
            }
            else
            {
                log($"Using fit mode {mode} for segment {segment.Index}.");
            }

            var (targetWidth, targetHeight) = GetTargetSize(settings.AspectRatio);
            var videoEncoder = GetVideoEncoder(settings, log);

            // IMPORTANT FIX: use "increase" instead of invalid "cover"
            // This scales up to fill the frame, then crops.
            var filters =
                $"scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=increase,crop={targetWidth}:{targetHeight}";

            var slotText = slot.ToString("0.###", CultureInfo.InvariantCulture);
            string args;

            switch (mode)
            {
                case FitMode.Trim:
                    // Just cut to the slot duration, keep original playback speed
                    segment.Speed = 1.0;
                    args =
                        $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} -c:v {videoEncoder} \"{outputClip}\"";
                    break;

                case FitMode.Slow:
                    // If clip is shorter than slot, slow it down to try and fill the slot
                    if (clipDuration < slot)
                    {
                        segment.Speed = clipDuration / slot; // e.g. 5 / 10 = 0.5x speed
                        if (segment.Speed <= 0)
                        {
                            throw new InvalidOperationException("Calculated speed factor must be positive.");
                        }

                        var ptsFactor = (1.0 / segment.Speed).ToString("0.########", CultureInfo.InvariantCulture);
                        filters += $",setpts={ptsFactor}*PTS";
                        log(
                            $"Slow mode: segment {segment.Index}, original {clipDuration:0.###}s, slot {slot:0.###}s, speed={segment.Speed:0.###}x, setpts={ptsFactor}*PTS");
                    }
                    else
                    {
                        segment.Speed = 1.0;
                        log(
                            $"Slow mode selected but clip for segment {segment.Index} already meets or exceeds slot length; leaving speed unchanged.");
                    }

                    args =
                        $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} -c:v {videoEncoder} \"{outputClip}\"";
                    break;

                case FitMode.Speed:
                    // If clip is longer than slot, speed it up to try and fit the slot
                    if (clipDuration > slot)
                    {
                        segment.Speed = clipDuration / slot; // e.g. 20 / 10 = 2x speed
                        if (segment.Speed <= 0)
                        {
                            throw new InvalidOperationException("Calculated speed factor must be positive.");
                        }

                        var ptsFactor = (1.0 / segment.Speed).ToString("0.########", CultureInfo.InvariantCulture);
                        filters += $",setpts={ptsFactor}*PTS";
                        log(
                            $"Speed mode: segment {segment.Index}, original {clipDuration:0.###}s, slot {slot:0.###}s, speed={segment.Speed:0.###}x, setpts={ptsFactor}*PTS");
                    }
                    else
                    {
                        segment.Speed = 1.0;
                        log(
                            $"Speed mode selected but clip for segment {segment.Index} already fits within slot; leaving speed unchanged.");
                    }

                    args =
                        $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} -c:v {videoEncoder} \"{outputClip}\"";
                    break;

                case FitMode.Loop:
                    // Loop frames until -t duration is reached
                    segment.Speed = 1.0;
                    var loopFrames = Math.Max(1, (int)Math.Ceiling(clipDuration * settings.Fps));
                    // loop=-1 means loop indefinitely over the first "size" frames; -t will cap the duration
                    filters = $"loop=-1:size={loopFrames}:start=0,{filters}";
                    log(
                        $"Loop mode: segment {segment.Index}, original {clipDuration:0.###}s, frames={loopFrames}, slot={slot:0.###}s.");

                    args =
                        $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} -c:v {videoEncoder} \"{outputClip}\"";
                    break;

                default:
                    throw new InvalidOperationException($"Unknown fit mode: {mode}");
            }

            log($"FFmpeg args (segment {segment.Index}): {args}");

            var exitCode = RunProcess(ffmpegPath, args, log);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"FFmpeg failed for segment {segment.Index} with exit code {exitCode}. See log for details above.");
            }

            if (!File.Exists(outputClip))
            {
                throw new FileNotFoundException(
                    $"FFmpeg reported success, but output clip for segment {segment.Index} was not found.",
                    outputClip);
            }
        }

        public string? ConcatAndMux(ProjectSettings settings, IList<Segment> segments, Action<string> log)
        {
            var ffmpegPath = Path.Combine(settings.FfmpegFolder, "ffmpeg.exe");
            var concatPath = Path.Combine(settings.TempFolder, "concat_list.txt");

            // Ensure all expected segment clips exist before trying concat
            using (var sw = new StreamWriter(concatPath, false))
            {
                foreach (var seg in segments.OrderBy(s => s.Index))
                {
                    var clipPath = Path.Combine(settings.TempFolder, $"clip_{seg.Index}.mp4");
                    if (!File.Exists(clipPath))
                    {
                        throw new FileNotFoundException(
                            $"Expected rendered clip for segment {seg.Index} not found. Aborting concat.",
                            clipPath);
                    }

                    // FFmpeg concat demuxer prefers forward slashes
                    var normalizedPath = clipPath.Replace("\\", "/");
                    sw.WriteLine($"file '{normalizedPath}'");
                }
            }

            var outputFile = Path.Combine(settings.OutputFolder, "final_output.mp4");
            var videoEncoder = GetVideoEncoder(settings, log);
            var concatArgs =
                $"-y -f concat -safe 0 -i \"{concatPath}\" -i \"{settings.AudioPath}\" -map 0:v -map 1:a -c:v {videoEncoder} -c:a aac -shortest \"{outputFile}\"";

            log("Running final concat + audio...");
            log($"FFmpeg args (final): {concatArgs}");

            var code = RunProcess(ffmpegPath, concatArgs, log);
            if (code != 0)
            {
                log($"Final FFmpeg concat failed with exit code {code}.");
                return null;
            }

            if (!File.Exists(outputFile))
            {
                log("FFmpeg reported success but final output file was not found.");
                return null;
            }

            return outputFile;
        }

        private static (int width, int height) GetTargetSize(string aspect)
        {
            return aspect switch
            {
                "9:16" => (1080, 1920),
                "1:1" => (1080, 1080),
                _ => (1920, 1080) // default 16:9
            };
        }

        private static int RunProcess(string exePath, string arguments, Action<string> log)
        {
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("ffmpeg.exe not found. Place it in the ffmpeg folder.", exePath);
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = false
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    log(e.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process: {exePath}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return process.ExitCode;
        }

        private string GetVideoEncoder(ProjectSettings settings, Action<string> log)
        {
            if (!string.IsNullOrWhiteSpace(_selectedVideoEncoder) && _lastUseGpuSetting == settings.UseGpuWhenAvailable)
            {
                return _selectedVideoEncoder!;
            }

            _selectedVideoEncoder = null;
            _lastUseGpuSetting = settings.UseGpuWhenAvailable;
            var ffmpegPath = Path.Combine(settings.FfmpegFolder, "ffmpeg.exe");
            const string fallbackEncoder = "libx264";

            if (settings.UseGpuWhenAvailable)
            {
                if (EncoderAvailable(ffmpegPath, "h264_nvenc", log))
                {
                    _selectedVideoEncoder = "h264_nvenc";
                    log("Using GPU encoder: h264_nvenc.");
                    return _selectedVideoEncoder;
                }

                log("GPU encoder h264_nvenc not available; falling back to libx264.");
            }
            else
            {
                log("GPU acceleration disabled; using libx264.");
            }

            _selectedVideoEncoder = fallbackEncoder;
            return _selectedVideoEncoder;
        }

        private static bool EncoderAvailable(string ffmpegPath, string encoderName, Action<string> log)
        {
            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException("ffmpeg.exe not found. Place it in the ffmpeg folder.", ffmpegPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -loglevel error -encoders",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            if (!process.Start())
            {
                log("Failed to start ffmpeg while probing encoders; defaulting to CPU encoder.");
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                log(string.IsNullOrWhiteSpace(error)
                    ? "ffmpeg returned a non-zero exit code while probing encoders; defaulting to CPU encoder."
                    : $"ffmpeg error while probing encoders: {error.Trim()} - defaulting to CPU encoder.");
                return false;
            }

            var isAvailable = output.Contains(encoderName, StringComparison.OrdinalIgnoreCase);
            return isAvailable;
        }

        private static double GetClipDuration(ProjectSettings settings, string clipPath, Action<string> log)
        {
            var ffprobePath = Path.Combine(settings.FfmpegFolder, "ffprobe.exe");
            return GetMediaDuration(ffprobePath, clipPath, log);
        }

        public double GetAudioDuration(ProjectSettings settings, Action<string> log)
        {
            var ffprobePath = Path.Combine(settings.FfmpegFolder, "ffprobe.exe");
            if (string.IsNullOrWhiteSpace(settings.AudioPath))
            {
                throw new InvalidOperationException("Audio path is not set.");
            }

            return GetMediaDuration(ffprobePath, settings.AudioPath, log);
        }

        private static double GetMediaDuration(string ffprobePath, string mediaPath, Action<string> log)
        {
            if (!File.Exists(ffprobePath))
            {
                throw new FileNotFoundException("ffprobe.exe not found. Place it in the ffmpeg folder.", ffprobePath);
            }

            if (!File.Exists(mediaPath))
            {
                throw new FileNotFoundException("Media file not found.", mediaPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments =
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffprobe.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
            {
                var message = string.IsNullOrWhiteSpace(error)
                    ? "Unable to parse media duration from ffprobe."
                    : error.Trim();
                throw new InvalidOperationException(message);
            }

            log($"Detected media duration: {duration.ToString("0.###", CultureInfo.InvariantCulture)}s");
            return duration;
        }
    }
}
