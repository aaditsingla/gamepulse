using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Gamepulse.Models;

namespace Gamepulse.Collectors
{
    public class ActiveGameDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        public ActiveProcessMetrics Collect()
        {
            try
            {
                IntPtr foregroundWindowHandle = GetForegroundWindow();

                if (foregroundWindowHandle == IntPtr.Zero)
                {
                    return new ActiveProcessMetrics("", "", 0);
                }

                GetWindowThreadProcessId(foregroundWindowHandle, out uint processId);

                if (processId == 0)
                {
                    return new ActiveProcessMetrics("", "", 0);
                }

                using Process process = Process.GetProcessById((int)processId);

                string processName = process.ProcessName;
                string windowTitle = GetForegroundWindowTitle(foregroundWindowHandle);
                double ramMb = process.WorkingSet64 / 1024.0 / 1024.0;

                return new ActiveProcessMetrics(processName, windowTitle, ramMb);
            }
            catch
            {
                return new ActiveProcessMetrics("", "", 0);
            }
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