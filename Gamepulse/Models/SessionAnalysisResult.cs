using System.Collections.Generic;

namespace Gamepulse.Models
{
    public class SessionAnalysisResult
    {
        public bool HasFrameData { get; set; }

        public double AverageFps { get; set; }
        public double MedianFps { get; set; }
        public double LowestFps { get; set; }

        public double AverageCpuPercent { get; set; }
        public double PeakCpuPercent { get; set; }

        public double AverageRamPercent { get; set; }
        public double PeakRamPercent { get; set; }

        public double AverageGpu0UsagePercent { get; set; }
        public double PeakGpu0UsagePercent { get; set; }

        public double AverageGpu1UsagePercent { get; set; }
        public double PeakGpu1UsagePercent { get; set; }

        public double AverageDiskActivePercent { get; set; }
        public double PeakDiskActivePercent { get; set; }

        public int TotalStutters { get; set; }

        public string OverallBottleneck { get; set; } = "Unknown";
        public string OverallSummary { get; set; } = "";

        public List<PerformanceIssue> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
}