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
        public void ProcessSegment(ProjectSettings settings, Segment segment, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(segment.AssignedClip))
            {
                throw new InvalidOperationException($"Segment {segment.Index} has no clip assignment.");
            }

            var ffmpegPath = Path.Combine(settings.FfmpegFolder, "ffmpeg.exe");
            var inputClip = Path.Combine(settings.ClipsFolder, segment.AssignedClip);
            var outputClip = Path.Combine(settings.TempFolder, $"clip_{segment.Index}.mp4");
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

            var targetSize = GetTargetSize(settings.AspectRatio);
            var filters = $"scale={targetSize.width}:{targetSize.height}:force_original_aspect_ratio=cover,crop={targetSize.width}:{targetSize.height}";
            var slotText = slot.ToString("0.###", CultureInfo.InvariantCulture);
            string args;

            switch (mode)
            {
                case FitMode.Trim:
                    segment.Speed = 1.0;
                    args = $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} \"{outputClip}\"";
                    break;
                case FitMode.Slow:
                    if (clipDuration < slot)
                    {
                        segment.Speed = clipDuration / slot;
                        if (segment.Speed <= 0)
                        {
                            throw new InvalidOperationException("Calculated speed factor must be positive.");
                        }

                        filters += $",setpts={(1.0 / segment.Speed).ToString(CultureInfo.InvariantCulture)}*PTS";
                    }
                    else
                    {
                        segment.Speed = 1.0;
                        log("Slow mode selected but clip already meets or exceeds slot length; leaving speed unchanged.");
                    }

                    args = $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} \"{outputClip}\"";
                    break;
                case FitMode.Speed:
                    if (clipDuration > slot)
                    {
                        segment.Speed = clipDuration / slot;
                        if (segment.Speed <= 0)
                        {
                            throw new InvalidOperationException("Calculated speed factor must be positive.");
                        }

                        filters += $",setpts={(1.0 / segment.Speed).ToString(CultureInfo.InvariantCulture)}*PTS";
                    }
                    else
                    {
                        segment.Speed = 1.0;
                        log("Speed mode selected but clip already fits within slot; leaving speed unchanged.");
                    }

                    args = $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} \"{outputClip}\"";
                    break;
                case FitMode.Loop:
                    segment.Speed = 1.0;
                    var loopFrames = Math.Max(1, (int)Math.Ceiling(clipDuration * settings.Fps));
                    filters = $"loop=-1:size={loopFrames}:start=0,{filters}";
                    args = $"-y -i \"{inputClip}\" -t {slotText} -an -vf \"{filters}\" -r {settings.Fps} \"{outputClip}\"";
                    break;
                default:
                    throw new InvalidOperationException($"Unknown fit mode: {mode}");
            }

            log($"FFmpeg args (segment {segment.Index}): {args}");
            RunProcess(ffmpegPath, args, log);
        }

        public string? ConcatAndMux(ProjectSettings settings, IList<Segment> segments, Action<string> log)
        {
            var ffmpegPath = Path.Combine(settings.FfmpegFolder, "ffmpeg.exe");
            var concatPath = Path.Combine(settings.TempFolder, "concat_list.txt");

            using (var sw = new StreamWriter(concatPath))
            {
                foreach (var seg in segments)
                {
                    var clipPath = Path.Combine(settings.TempFolder, $"clip_{seg.Index}.mp4");
                    sw.WriteLine($"file '{clipPath.Replace("\\", "/")}'");
                }
            }

            var outputFile = Path.Combine(settings.OutputFolder, "final_output.mp4");
            var concatArgs = $"-y -f concat -safe 0 -i \"{concatPath}\" -i \"{settings.AudioPath}\" -map 0:v -map 1:a -c:v libx264 -c:a aac -shortest \"{outputFile}\"";
            log("Running final concat + audio...");
            log($"FFmpeg args (final): {concatArgs}");

            var code = RunProcess(ffmpegPath, concatArgs, log);
            return code == 0 ? outputFile : null;
        }

        private static (int width, int height) GetTargetSize(string aspect)
        {
            return aspect switch
            {
                "9:16" => (1080, 1920),
                "1:1" => (1080, 1080),
                _ => (1920, 1080)
            };
        }

        private static int RunProcess(string exePath, string arguments, Action<string> log)
        {
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("ffmpeg.exe not found. Place it in the ffmpeg folder.", exePath);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
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

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
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
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"",
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
                var message = string.IsNullOrWhiteSpace(error) ? "Unable to parse media duration from ffprobe." : error.Trim();
                throw new InvalidOperationException(message);
            }

            log($"Detected media duration: {duration.ToString("0.###", CultureInfo.InvariantCulture)}s");
            return duration;
        }
    }
}
