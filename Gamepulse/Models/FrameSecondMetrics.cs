namespace Gamepulse.Models
{
    public class FrameSecondMetrics
    {
        public int Second { get; set; }
        public int FrameCount { get; set; }
        public double AverageFrameTimeMs { get; set; }
        public double AverageFps { get; set; }
        public double WorstFrameTimeMs { get; set; }
        public double OnePercentLowFps { get; set; }
        public double ZeroPointOnePercentLowFps { get; set; }
        public int StutterCount { get; set; }
    }
}