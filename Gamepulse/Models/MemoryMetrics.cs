namespace Gamepulse.Models
{
    public class MemoryMetrics
    {
        public double TotalGb { get; }
        public double UsedGb { get; }
        public double UsedPercentage { get; }

        public MemoryMetrics(double totalGb, double usedGb, double usedPercentage)
        {
            TotalGb = totalGb;
            UsedGb = usedGb;
            UsedPercentage = usedPercentage;
        }
    }
}