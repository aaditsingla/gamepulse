using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class TopRamProcessCollector
    {
        public List<ProcessMemoryMetrics> CollectTopProcesses(int count)
        {
            try
            {
                return Process.GetProcesses()
                    .Select(CreateProcessMemoryMetric)
                    .Where(metric => metric != null)
                    .Select(metric => metric!)
                    .GroupBy(metric => metric.ProcessName)
                    .Select(group => new ProcessMemoryMetrics(
                        group.Key,
                        group.Sum(metric => metric.RamMb)
                    ))
                    .OrderByDescending(metric => metric.RamMb)
                    .Take(count)
                    .ToList();
            }
            catch
            {
                return new List<ProcessMemoryMetrics>();
            }
        }

        private static ProcessMemoryMetrics? CreateProcessMemoryMetric(Process process)
        {
            try
            {
                string processName = process.ProcessName;
                double ramMb = process.WorkingSet64 / 1024.0 / 1024.0;

                return new ProcessMemoryMetrics(processName, ramMb);
            }
            catch
            {
                return null;
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}