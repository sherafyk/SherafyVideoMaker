using System;

namespace SherafyVideoMaker.Models
{
    public enum FitMode
    {
        Auto,
        Trim,
        Slow,
        Speed,
        Loop
    }

    public class Segment
    {
        public int Index { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? AssignedClip { get; set; }
        public double Speed { get; set; } = 1.0;
        public FitMode FitMode { get; set; } = FitMode.Auto;

        public TimeSpan Duration => End - Start;
    }
}
