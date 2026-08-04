using System;
using System.Diagnostics;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class DiskMetricsCollector : IDisposable
    {
        private readonly PerformanceCounter _diskActiveCounter;
        private readonly PerformanceCounter _diskReadCounter;
        private readonly PerformanceCounter _diskWriteCounter;

        public DiskMetricsCollector()
        {
            _diskActiveCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

            // Prime counters. First PerformanceCounter readings can be inaccurate.
            _diskActiveCounter.NextValue();
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();
        }

        public DiskMetrics Collect()
        {
            double activePercent = GetDiskActivePercent();
            double readMBps = GetDiskReadMBps();
            double writeMBps = GetDiskWriteMBps();

            return new DiskMetrics(activePercent, readMBps, writeMBps);
        }

        private double GetDiskActivePercent()
        {
            try
            {
                double value = _diskActiveCounter.NextValue();

                if (value < 0)
                {
                    return 0;
                }

                if (value > 100)
                {
                    return 100;
                }

                return value;
            }
            catch
            {
                return 0;
            }
        }

        private double GetDiskReadMBps()
        {
            try
            {
                double bytesPerSecond = _diskReadCounter.NextValue();
                return bytesPerSecond / 1024.0 / 1024.0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetDiskWriteMBps()
        {
            try
            {
                double bytesPerSecond = _diskWriteCounter.NextValue();
                return bytesPerSecond / 1024.0 / 1024.0;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            _diskActiveCounter.Dispose();
            _diskReadCounter.Dispose();
            _diskWriteCounter.Dispose();
        }
    }
}