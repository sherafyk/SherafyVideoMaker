using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SherafyVideoMaker.Models;

namespace SherafyVideoMaker.Services
{
    public class SrtParser
    {
        private static readonly Regex TimeRegex = new(
            @"(?<start>\d{2}:\d{2}:\d{2},\d{3})\s+-->\s+(?<end>\d{2}:\d{2}:\d{2},\d{3})",
            RegexOptions.Compiled);

        public IEnumerable<Segment> Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("SRT path is required", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("SRT file not found", path);

            var lines = File.ReadAllLines(path);
            var buffer = new List<string>();
            var index = 0;
            TimeSpan? start = null;
            TimeSpan? end = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (start.HasValue && end.HasValue && buffer.Count > 0)
                    {
                        index++;
                        yield return new Segment
                        {
                            Index = index,
                            Start = start.Value,
                            End = end.Value,
                            Text = string.Join(" ", buffer).Trim()
                        };
                    }

                    buffer.Clear();
                    start = null;
                    end = null;
                    continue;
                }

                var match = TimeRegex.Match(line);
                if (match.Success)
                {
                    start = ParseTimestamp(match.Groups["start"].Value);
                    end = ParseTimestamp(match.Groups["end"].Value);
                    continue;
                }

                if (char.IsDigit(line[0]) && !line.Contains("-->"))
                {
                    continue;
                }

                buffer.Add(line.Trim());
            }

            if (start.HasValue && end.HasValue && buffer.Count > 0)
            {
                index++;
                yield return new Segment
                {
                    Index = index,
                    Start = start.Value,
                    End = end.Value,
                    Text = string.Join(" ", buffer).Trim()
                };
            }
        }

        private static TimeSpan ParseTimestamp(string text)
        {
            return TimeSpan.ParseExact(text, @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
        }
    }
}
