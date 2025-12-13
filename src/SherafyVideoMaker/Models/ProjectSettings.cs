using System;
using System.IO;

namespace SherafyVideoMaker.Models
{
    public class ProjectSettings
    {
        public string AudioPath { get; set; } = string.Empty;
        public string SrtPath { get; set; } = string.Empty;
        public string ClipsFolder { get; set; } = string.Empty;
        public string ClipUrl { get; set; } = string.Empty;
        public string AspectRatio { get; set; } = "16:9";
        public int Fps { get; set; } = 30;
        public string TempFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "temp");
        public string OutputFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "output");
        public string FfmpegFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "ffmpeg");
        public string LogFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "logs");
        public string DownloadsFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "clips_downloads");
        public bool UseGpuWhenAvailable { get; set; } = true;
        public bool EnableWatermark { get; set; }
        public string WatermarkPath { get; set; } = string.Empty;
        public double WatermarkOpacity { get; set; } = 0.75;
        public int WatermarkPadding { get; set; } = 24;
        public WatermarkPosition WatermarkPosition { get; set; } = WatermarkPosition.BottomRight;

        public ValidationResult Validate(bool hasSegmentClipUrls = false)
        {
            if (string.IsNullOrWhiteSpace(AudioPath) || !File.Exists(AudioPath))
            {
                return ValidationResult.Fail("Select a valid audio file.");
            }

            if (string.IsNullOrWhiteSpace(SrtPath) || !File.Exists(SrtPath))
            {
                return ValidationResult.Fail("Select a valid SRT file.");
            }

            Directory.CreateDirectory(DownloadsFolder);

            if ((string.IsNullOrWhiteSpace(ClipsFolder) || !Directory.Exists(ClipsFolder))
                && string.IsNullOrWhiteSpace(ClipUrl)
                && !hasSegmentClipUrls)
            {
                return ValidationResult.Fail("Select a valid clips folder or provide a clip URL.");
            }

            if (Fps <= 0)
            {
                return ValidationResult.Fail("FPS must be greater than zero.");
            }

            if (EnableWatermark)
            {
                if (string.IsNullOrWhiteSpace(WatermarkPath) || !File.Exists(WatermarkPath))
                {
                    return ValidationResult.Fail("Select a valid watermark image when watermarking is enabled.");
                }

                if (WatermarkOpacity < 0 || WatermarkOpacity > 1)
                {
                    return ValidationResult.Fail("Watermark opacity must be between 0 and 1.");
                }

                if (WatermarkPadding < 0)
                {
                    return ValidationResult.Fail("Watermark padding must be zero or positive.");
                }
            }

            return ValidationResult.Success();
        }
    }

    public enum WatermarkPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public record ValidationResult(bool IsValid, string Message)
    {
        public static ValidationResult Success() => new(true, "");

        public static ValidationResult Fail(string message) => new(false, message);
    }
}
