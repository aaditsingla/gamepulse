namespace Gamepulse.Models
{
    public class SystemMetrics
    {
        public double CpuPercent { get; }
        public MemoryMetrics Memory { get; }

        public SystemMetrics(double cpuPercent, MemoryMetrics memory)
        {
            CpuPercent = cpuPercent;
            Memory = memory;
        }
    }
}