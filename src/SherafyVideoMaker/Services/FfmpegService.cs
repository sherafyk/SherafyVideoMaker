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
            var durationSeconds = segment.Duration.TotalSeconds;

            var targetSize = GetTargetSize(settings.AspectRatio);
            var filters = $"scale={targetSize.width}:{targetSize.height}:force_original_aspect_ratio=cover,crop={targetSize.width}:{targetSize.height}";
            if (Math.Abs(segment.Speed - 1.0) > 0.001)
            {
                var factor = (1.0 / segment.Speed).ToString(CultureInfo.InvariantCulture);
                filters += $",setpts={factor}*PTS";
            }

            var args = $"-y -i \"{inputClip}\" -t {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} -an -vf \"{filters}\" -r {settings.Fps} \"{outputClip}\"";
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
    }
}
