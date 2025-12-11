using System;
using System.IO;

namespace SherafyVideoMaker.Models
{
    public class ProjectSettings
    {
        public string AudioPath { get; set; } = string.Empty;
        public string SrtPath { get; set; } = string.Empty;
        public string ClipsFolder { get; set; } = string.Empty;
        public string AspectRatio { get; set; } = "16:9";
        public int Fps { get; set; } = 30;
        public string TempFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "temp");
        public string OutputFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "output");
        public string FfmpegFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "ffmpeg");
        public string LogFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "logs");

        public ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
            {
                return ValidationResult.Fail("Select a valid audio file.");
            }

            if (string.IsNullOrWhiteSpace(SrtPath) || !File.Exists(SrtPath))
            {
                return ValidationResult.Fail("Select a valid SRT file.");
            }

            if (string.IsNullOrWhiteSpace(ClipsFolder) || !Directory.Exists(ClipsFolder))
            {
                return ValidationResult.Fail("Select a valid clips folder.");
            }

            if (Fps <= 0)
            {
                return ValidationResult.Fail("FPS must be greater than zero.");
            }

            return ValidationResult.Success();
        }
    }

    public record ValidationResult(bool IsValid, string Message)
    {
        public static ValidationResult Success() => new(true, "");

        public static ValidationResult Fail(string message) => new(false, message);
    }
}
