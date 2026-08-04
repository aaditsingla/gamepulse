using System;

namespace Gamepulse.Models
{
    public class TelemetrySample
    {
        public string SessionId { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int ElapsedSeconds { get; set; }
        public string GameName { get; set; } = "";

        public double CpuPercent { get; set; }
        public double RamPercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }

        public double? DiskReadMBps { get; set; }
        public double? DiskWriteMBps { get; set; }
        public double? DiskActivePercent { get; set; }

        public string Gpu0Name { get; set; } = "";
        public double? Gpu0UsagePercent { get; set; }
        public double? Gpu0VramUsedMb { get; set; }
        public double? Gpu0TemperatureC { get; set; }

        public string Gpu1Name { get; set; } = "";
        public double? Gpu1UsagePercent { get; set; }
        public double? Gpu1VramUsedMb { get; set; }
        public double? Gpu1TemperatureC { get; set; }

        public string ActiveGameProcessName { get; set; } = "";
        public double? GameCpuPercent { get; set; }
        public double? GameRamMb { get; set; }

        public double? Fps { get; set; }
        public double? AverageFrameTimeMs { get; set; }
        public double? WorstFrameTimeMs { get; set; }
        public double? OnePercentLowFps { get; set; }
        public double? PointOnePercentLowFps { get; set; }
        public int? StutterCount { get; set; }

        public string TopRamProcesses { get; set; } = "";
    }
}