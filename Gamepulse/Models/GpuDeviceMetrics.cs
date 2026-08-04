namespace Gamepulse.Models
{
    public class GpuDeviceMetrics
    {
        public string Name { get; }
        public double UsagePercent { get; }
        public double? VramUsedMb { get; }
        public double? TemperatureC { get; }

        public GpuDeviceMetrics(
            string name,
            double usagePercent,
            double? vramUsedMb,
            double? temperatureC)
        {
            Name = name;
            UsagePercent = usagePercent;
            VramUsedMb = vramUsedMb;
            TemperatureC = temperatureC;
        }
    }
}