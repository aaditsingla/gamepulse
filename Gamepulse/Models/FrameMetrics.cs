namespace Gamepulse.Models
{
    public class FrameMetrics
    {
        public double? Fps { get; }
        public double? AverageFrameTimeMs { get; }
        public double? WorstFrameTimeMs { get; }
        public double? OnePercentLowFps { get; }
        public double? PointOnePercentLowFps { get; }
        public int? StutterCount { get; }

        public FrameMetrics(
            double? fps,
            double? averageFrameTimeMs,
            double? worstFrameTimeMs,
            double? onePercentLowFps,
            double? pointOnePercentLowFps,
            int? stutterCount)
        {
            Fps = fps;
            AverageFrameTimeMs = averageFrameTimeMs;
            WorstFrameTimeMs = worstFrameTimeMs;
            OnePercentLowFps = onePercentLowFps;
            PointOnePercentLowFps = pointOnePercentLowFps;
            StutterCount = stutterCount;
        }

        public static FrameMetrics Empty()
        {
            return new FrameMetrics(null, null, null, null, null, null);
        }
    }
}