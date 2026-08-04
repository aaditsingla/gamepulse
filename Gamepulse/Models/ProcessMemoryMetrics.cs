namespace Gamepulse.Models
{
    public class ProcessMemoryMetrics
    {
        public string ProcessName { get; }
        public double RamMb { get; }

        public ProcessMemoryMetrics(string processName, double ramMb)
        {
            ProcessName = processName;
            RamMb = ramMb;
        }
    }
}