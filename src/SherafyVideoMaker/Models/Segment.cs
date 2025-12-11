using System;

namespace SherafyVideoMaker.Models
{
    public class Segment
    {
        public int Index { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? AssignedClip { get; set; }
        public double Speed { get; set; } = 1.0;

        public TimeSpan Duration => End - Start;
    }
}
