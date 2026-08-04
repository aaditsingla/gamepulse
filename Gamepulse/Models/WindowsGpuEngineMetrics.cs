namespace Gamepulse.Models
{
    public class WindowsGpuEngineMetrics
    {
        public double TotalUsagePercent { get; }

        public WindowsGpuEngineMetrics(double totalUsagePercent)
        {
            TotalUsagePercent = totalUsagePercent;
        }
    }
}