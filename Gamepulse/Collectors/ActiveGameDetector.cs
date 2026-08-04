using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class ActiveGameDetector
    {
        private readonly int _processorCount;

        private int? _previousProcessId;
        private TimeSpan _previousProcessorTime;
        private DateTime _previousTimestamp;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        public ActiveGameDetector()
        {
            _processorCount = Environment.ProcessorCount;
            _previousTimestamp = DateTime.UtcNow;
        }

        public ActiveProcessMetrics Collect()
        {
            try
            {
                IntPtr foregroundWindowHandle = GetForegroundWindow();

                if (foregroundWindowHandle == IntPtr.Zero)
                {
                    return new ActiveProcessMetrics("", "", 0, null);
                }

                GetWindowThreadProcessId(foregroundWindowHandle, out uint processId);

                if (processId == 0)
                {
                    return new ActiveProcessMetrics("", "", 0, null);
                }

                using Process process = Process.GetProcessById((int)processId);

                string processName = process.ProcessName;
                string windowTitle = GetForegroundWindowTitle(foregroundWindowHandle);
                double ramMb = process.WorkingSet64 / 1024.0 / 1024.0;

                TimeSpan currentProcessorTime = process.TotalProcessorTime;
                DateTime currentTimestamp = DateTime.UtcNow;

                double? cpuPercent = CalculateCpuPercent(
                    (int)processId,
                    currentProcessorTime,
                    currentTimestamp
                );

                _previousProcessId = (int)processId;
                _previousProcessorTime = currentProcessorTime;
                _previousTimestamp = currentTimestamp;

                return new ActiveProcessMetrics(
                    processName,
                    windowTitle,
                    ramMb,
                    cpuPercent
                );
            }
            catch
            {
                return new ActiveProcessMetrics("", "", 0, null);
            }
        }

        private double? CalculateCpuPercent(
            int currentProcessId,
            TimeSpan currentProcessorTime,
            DateTime currentTimestamp)
        {
            if (_previousProcessId == null)
            {
                return null;
            }

            if (_previousProcessId.Value != currentProcessId)
            {
                return null;
            }

            double elapsedSeconds = (currentTimestamp - _previousTimestamp).TotalSeconds;
            double cpuTimeDeltaSeconds = (currentProcessorTime - _previousProcessorTime).TotalSeconds;

            if (elapsedSeconds <= 0 || cpuTimeDeltaSeconds < 0)
            {
                return null;
            }

            double cpuPercent = cpuTimeDeltaSeconds / elapsedSeconds / _processorCount * 100.0;

            if (cpuPercent < 0)
            {
                return 0;
            }

            if (cpuPercent > 100)
            {
                return 100;
            }

            return cpuPercent;
        }

        private static string GetForegroundWindowTitle(IntPtr windowHandle)
        {
            const int maxCharacters = 256;

            StringBuilder titleBuilder = new StringBuilder(maxCharacters);
            GetWindowText(windowHandle, titleBuilder, maxCharacters);

            return titleBuilder.ToString();
        }
    }
}