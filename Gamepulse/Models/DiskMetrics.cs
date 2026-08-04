namespace Gamepulse.Models
{
    public class DiskMetrics
    {
        public double ActivePercent { get; }
        public double ReadMBps { get; }
        public double WriteMBps { get; }

        public DiskMetrics(double activePercent, double readMBps, double writeMBps)
        {
            ActivePercent = activePercent;
            ReadMBps = readMBps;
            WriteMBps = writeMBps;
        }
    }
}