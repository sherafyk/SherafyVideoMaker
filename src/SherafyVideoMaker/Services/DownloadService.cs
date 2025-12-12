using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using SherafyVideoMaker.Models;

namespace SherafyVideoMaker.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient = new();

        public async Task<string> DownloadAsync(
            ProjectSettings settings,
            string clipUrl,
            Action<double> reportProgress,
            Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(clipUrl))
            {
                throw new InvalidOperationException("No clip URL provided.");
            }

            Directory.CreateDirectory(settings.DownloadsFolder);

            using var response = await _httpClient.GetAsync(clipUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("video", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unexpected content type: {contentType}");
            }

            var fileName = GetFileName(response.Content.Headers.ContentDisposition, clipUrl);
            var destinationPath = Path.Combine(settings.DownloadsFolder, fileName);
            destinationPath = EnsureUniquePath(destinationPath);
            var tempPath = destinationPath + ".tmp";

            var totalBytes = response.Content.Headers.ContentLength;
            var lastReported = 0d;

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                int bytesRead;
                long totalRead = 0;
                while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes > 0)
                    {
                        var progress = (double)totalRead / totalBytes.Value * 100;
                        if (progress - lastReported >= 1)
                        {
                            lastReported = progress;
                            reportProgress(progress);
                        }
                    }
                }

                await fileStream.FlushAsync();
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            log($"Downloaded clip to {destinationPath}.");

            return destinationPath;
        }

        private static string GetFileName(ContentDispositionHeaderValue? contentDisposition, string url)
        {
            var name = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileName(new Uri(url).LocalPath);
            }

            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('.') < 0)
            {
                name = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            }

            var extension = Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                name += ".mp4";
            }

            return name;
        }

        private static string EnsureUniquePath(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            if (string.IsNullOrEmpty(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            var candidate = path;
            var counter = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter}{extension}");
                counter++;
            }

            return candidate;
        }
    }
}
