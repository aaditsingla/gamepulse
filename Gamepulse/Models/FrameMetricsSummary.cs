using System.Collections.Generic;

namespace Gamepulse.Models
{
    public class FrameMetricsSummary
    {
        public string SourceFilePath { get; set; } = "";
        public string TargetProcessName { get; set; } = "";
        public int FrameCount { get; set; }
        public double CaptureDurationSeconds { get; set; }
        public double AverageFrameTimeMs { get; set; }
        public double AverageFps { get; set; }
        public double WorstFrameTimeMs { get; set; }
        public double OnePercentLowFps { get; set; }
        public double ZeroPointOnePercentLowFps { get; set; }

        public List<FrameSecondMetrics> PerSecondMetrics { get; set; } = new();
    }
}