using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace SherafyVideoMaker.Services
{
    public class TranscriptionService
    {
        private const string DefaultModelFileName = "ggml-base.en.bin";
        private const string DefaultModelUrl =
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin?download=1";

        public async Task<string> TranscribeToSrtAsync(
            string audioPath,
            string modelsFolder,
            string outputFolder,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(audioPath))
            {
                throw new ArgumentException("Audio path is required", nameof(audioPath));
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("Audio file not found", audioPath);
            }

            Directory.CreateDirectory(modelsFolder);
            Directory.CreateDirectory(outputFolder);

            var modelPath = await EnsureModelAsync(modelsFolder, log, cancellationToken).ConfigureAwait(false);

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            await using var processor = whisperFactory.CreateBuilder().Build();

            var segments = new List<SegmentData>();
            await foreach (var segment in processor.ProcessAsync(audioPath, cancellationToken: cancellationToken)
                               .ConfigureAwait(false))
            {
                segments.Add(segment);
            }

            var outputPath = Path.Combine(
                outputFolder,
                Path.GetFileNameWithoutExtension(audioPath) + ".srt");

            await WriteSrtAsync(segments, outputPath, cancellationToken).ConfigureAwait(false);
            return outputPath;
        }

        private static async Task<string> EnsureModelAsync(
            string modelsFolder,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var targetPath = Path.Combine(modelsFolder, DefaultModelFileName);
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            log($"Downloading Whisper model to {targetPath} (first run only)...");

            using var client = new HttpClient();
            using var response = await client.GetAsync(DefaultModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(targetPath);

            var totalBytes = response.Content.Headers.ContentLength;
            var buffer = new byte[81920];
            long read = 0;
            int bytesRead;
            while ((bytesRead = await downloadStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                read += bytesRead;

                if (totalBytes is not null && totalBytes > 0)
                {
                    var progress = read * 100d / totalBytes.Value;
                    log($"Model download progress: {progress:0.#}%");
                }
            }

            log("Model download complete.");
            return targetPath;
        }

        private static async Task WriteSrtAsync(
            IReadOnlyList<SegmentData> segments,
            string outputPath,
            CancellationToken cancellationToken)
        {
            await using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                await writer.WriteLineAsync((i + 1).ToString(CultureInfo.InvariantCulture))
                    .ConfigureAwait(false);
                await writer.WriteLineAsync($"{ToTimestamp(segment.Start)} --> {ToTimestamp(segment.End)}")
                    .ConfigureAwait(false);
                await writer.WriteLineAsync(segment.Text.Trim())
                    .ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private static string ToTimestamp(TimeSpan timeSpan)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00},{3:000}",
                (int)timeSpan.TotalHours,
                timeSpan.Minutes,
                timeSpan.Seconds,
                timeSpan.Milliseconds);
        }
    }
}
