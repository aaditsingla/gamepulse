namespace Gamepulse.Models
{
    public class PerformanceIssue
    {
        public int StartSecond { get; set; }
        public int EndSecond { get; set; }

        public string IssueType { get; set; } = "";
        public string Severity { get; set; }

        public string LikelyCause { get; set; } = "";
        public string ContributingFactors { get; set; } = "";
        public string Confidence { get; set; } = "";

        public double? Fps { get; set; }
        public double? AverageFrameTimeMs { get; set; }
        public double? WorstFrameTimeMs { get; set; }

        public double? CpuPercent { get; set; }
        public double? GameCpuPercent { get; set; }
        public double? RamPercent { get; set; }
        public double? DiskActivePercent { get; set; }
        public double? DiskReadMBps { get; set; }
        public double? DiskWriteMBps { get; set; }
        public double? Gpu0UsagePercent { get; set; }
        public double? Gpu1UsagePercent { get; set; }

        public string TopCpuProcesses { get; set; } = "";
        public string TopRamProcesses { get; set; } = "";

        public string Evidence { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }
}