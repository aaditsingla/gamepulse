using System.Collections.Generic;

namespace Gamepulse.Models
{
    public class GpuMetrics
    {
        public List<GpuDeviceMetrics> Devices { get; }

        public GpuMetrics(List<GpuDeviceMetrics> devices)
        {
            Devices = devices;
        }
    }
}