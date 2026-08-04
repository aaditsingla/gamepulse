using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class TopCpuProcessCollector
    {
        private readonly Dictionary<int, ProcessCpuSnapshot> _previousSnapshots = new();
        private readonly int _processorCount;

        public TopCpuProcessCollector()
        {
            _processorCount = Environment.ProcessorCount;
        }

        public List<ProcessCpuMetrics> CollectTopProcesses(int count)
        {
            DateTime currentTimestamp = DateTime.UtcNow;
            Dictionary<int, ProcessCpuSnapshot> currentSnapshots = new();
            List<ProcessCpuMetrics> cpuMetrics = new();

            Process[] processes;

            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return new List<ProcessCpuMetrics>();
            }

            foreach (Process process in processes)
            {
                try
                {
                    int processId = process.Id;
                    string processName = process.ProcessName;
                    TimeSpan totalProcessorTime = process.TotalProcessorTime;

                    ProcessCpuSnapshot currentSnapshot = new ProcessCpuSnapshot(
                        processId,
                        processName,
                        totalProcessorTime,
                        currentTimestamp
                    );

                    currentSnapshots[processId] = currentSnapshot;

                    if (_previousSnapshots.TryGetValue(processId, out ProcessCpuSnapshot? previousSnapshot))
                    {
                        double elapsedSeconds = (currentTimestamp - previousSnapshot.Timestamp).TotalSeconds;
                        double cpuTimeDeltaSeconds = (totalProcessorTime - previousSnapshot.TotalProcessorTime).TotalSeconds;

                        if (elapsedSeconds > 0 && cpuTimeDeltaSeconds >= 0)
                        {
                            double cpuPercent = cpuTimeDeltaSeconds / elapsedSeconds / _processorCount * 100.0;

                            if (cpuPercent < 0)
                            {
                                cpuPercent = 0;
                            }

                            if (cpuPercent > 100)
                            {
                                cpuPercent = 100;
                            }

                            cpuMetrics.Add(new ProcessCpuMetrics(processName, cpuPercent));
                        }
                    }
                }
                catch
                {
                    // Some system processes cannot be accessed. Ignore them.
                }
                finally
                {
                    process.Dispose();
                }
            }

            _previousSnapshots.Clear();

            foreach (KeyValuePair<int, ProcessCpuSnapshot> snapshot in currentSnapshots)
            {
                _previousSnapshots[snapshot.Key] = snapshot.Value;
            }

            return cpuMetrics
                .GroupBy(metric => metric.ProcessName)
                .Select(group => new ProcessCpuMetrics(
                    group.Key,
                    group.Sum(metric => metric.CpuPercent)
                ))
                .OrderByDescending(metric => metric.CpuPercent)
                .Take(count)
                .ToList();
        }

        private class ProcessCpuSnapshot
        {
            public int ProcessId { get; }
            public string ProcessName { get; }
            public TimeSpan TotalProcessorTime { get; }
            public DateTime Timestamp { get; }

            public ProcessCpuSnapshot(
                int processId,
                string processName,
                TimeSpan totalProcessorTime,
                DateTime timestamp)
            {
                ProcessId = processId;
                ProcessName = processName;
                TotalProcessorTime = totalProcessorTime;
                Timestamp = timestamp;
            }
        }
    }
}