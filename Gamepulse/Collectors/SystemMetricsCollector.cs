using System;
using System.Diagnostics;
using System.Management;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class SystemMetricsCollector : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;

        public SystemMetricsCollector()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

            // The first PerformanceCounter reading is often wrong, so we prime it once.
            _cpuCounter.NextValue();
        }

        public SystemMetrics Collect()
        {
            double cpuPercent = GetCpuPercent();
            MemoryMetrics memory = GetMemoryMetrics();

            return new SystemMetrics(cpuPercent, memory);
        }

        private double GetCpuPercent()
        {
            try
            {
                return _cpuCounter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        private static MemoryMetrics GetMemoryMetrics()
        {
            using ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (ManagementObject result in searcher.Get())
            {
                double totalKb = Convert.ToDouble(result["TotalVisibleMemorySize"]);
                double freeKb = Convert.ToDouble(result["FreePhysicalMemory"]);

                double usedKb = totalKb - freeKb;

                double totalGb = totalKb / 1024.0 / 1024.0;
                double usedGb = usedKb / 1024.0 / 1024.0;
                double usedPercentage = totalGb > 0 ? usedGb / totalGb * 100.0 : 0;

                return new MemoryMetrics(totalGb, usedGb, usedPercentage);
            }

            return new MemoryMetrics(0, 0, 0);
        }

        public void Dispose()
        {
            _cpuCounter.Dispose();
        }
    }
}