namespace Gamepulse.Models
{
    public class ActiveProcessMetrics
    {
        public string ProcessName { get; }
        public string WindowTitle { get; }
        public double RamMb { get; }

        public ActiveProcessMetrics(string processName, string windowTitle, double ramMb)
        {
            ProcessName = processName;
            WindowTitle = windowTitle;
            RamMb = ramMb;
        }
    }
}