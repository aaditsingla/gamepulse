using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class WindowsGpuEngineCollector : IDisposable
    {
        private readonly List<PerformanceCounter> _gpuEngineCounters = new();

        public WindowsGpuEngineCollector()
        {
            try
            {
                PerformanceCounterCategory category = new PerformanceCounterCategory("GPU Engine");
                string[] instanceNames = category.GetInstanceNames();

                foreach (string instanceName in instanceNames)
                {
                    if (!ShouldTrackGpuEngine(instanceName))
                    {
                        continue;
                    }

                    PerformanceCounter counter = new PerformanceCounter(
                        "GPU Engine",
                        "Utilization Percentage",
                        instanceName
                    );

                    counter.NextValue();
                    _gpuEngineCounters.Add(counter);
                }
            }
            catch
            {
                // If Windows GPU Engine counters are unavailable, we leave the list empty.
            }
        }

        public WindowsGpuEngineMetrics Collect()
        {
            double totalUsage = 0;

            foreach (PerformanceCounter counter in _gpuEngineCounters)
            {
                try
                {
                    totalUsage += counter.NextValue();
                }
                catch
                {
                    // Ignore counters that disappear when apps close.
                }
            }

            if (totalUsage < 0)
            {
                totalUsage = 0;
            }

            if (totalUsage > 100)
            {
                totalUsage = 100;
            }

            return new WindowsGpuEngineMetrics(totalUsage);
        }

        private static bool ShouldTrackGpuEngine(string instanceName)
        {
            return instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                   instanceName.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase) ||
                   instanceName.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ||
                   instanceName.Contains("engtype_VideoProcessing", StringComparison.OrdinalIgnoreCase) ||
                   instanceName.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            foreach (PerformanceCounter counter in _gpuEngineCounters)
            {
                counter.Dispose();
            }

            _gpuEngineCounters.Clear();
        }
    }
}