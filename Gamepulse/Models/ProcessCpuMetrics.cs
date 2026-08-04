namespace Gamepulse.Models
{
    public class ProcessCpuMetrics
    {
        public string ProcessName { get; }
        public double CpuPercent { get; }

        public ProcessCpuMetrics(string processName, double cpuPercent)
        {
            ProcessName = processName;
            CpuPercent = cpuPercent;
        }
    }
}