using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Gamepulse.Collectors;
using Gamepulse.Models;
using Gamepulse.Services;

namespace Gamepulse
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly SystemMetricsCollector _systemMetricsCollector;
        private readonly DiskMetricsCollector _diskMetricsCollector;
        private readonly SessionManager _sessionManager;
        private readonly CsvTelemetryWriter _csvTelemetryWriter;

        private readonly List<TelemetrySample> _samples = new();

        public MainWindow()
        {
            InitializeComponent();

            _systemMetricsCollector = new SystemMetricsCollector();
            _diskMetricsCollector = new DiskMetricsCollector();
            _sessionManager = new SessionManager();
            _csvTelemetryWriter = new CsvTelemetryWriter();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            SystemMetrics systemMetrics = _systemMetricsCollector.Collect();
            DiskMetrics diskMetrics = _diskMetricsCollector.Collect();

            CpuText.Text = $"{systemMetrics.CpuPercent:0}%";
            RamText.Text = $"{systemMetrics.Memory.UsedPercentage:0}%";
            RamDetailsText.Text = $"{systemMetrics.Memory.UsedGb:0.0} GB / {systemMetrics.Memory.TotalGb:0.0} GB";

            DiskText.Text = $"{diskMetrics.ActivePercent:0}%";
            DiskDetailsText.Text = $"Read {diskMetrics.ReadMBps:0.0} MB/s | Write {diskMetrics.WriteMBps:0.0} MB/s";

            if (_sessionManager.IsRunning && _sessionManager.CurrentSession != null)
            {
                TimeSpan duration = _sessionManager.GetDuration();

                DurationText.Text = $"Session Duration: {duration:hh\\:mm\\:ss}";

                TelemetrySample sample = new TelemetrySample
                {
                    SessionId = _sessionManager.CurrentSession.SessionId,
                    Timestamp = DateTime.Now,
                    ElapsedSeconds = _sessionManager.GetElapsedSeconds(),
                    GameName = _sessionManager.CurrentSession.GameName,

                    CpuPercent = systemMetrics.CpuPercent,
                    RamPercent = systemMetrics.Memory.UsedPercentage,
                    RamUsedGb = systemMetrics.Memory.UsedGb,
                    RamTotalGb = systemMetrics.Memory.TotalGb,

                    DiskReadMBps = diskMetrics.ReadMBps,
                    DiskWriteMBps = diskMetrics.WriteMBps,
                    DiskActivePercent = diskMetrics.ActivePercent,

                    TopRamProcesses = ""
                };

                _samples.Add(sample);
                _csvTelemetryWriter.WriteSample(sample);

                SamplesText.Text = $"Samples Recorded: {_samples.Count}";
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _samples.Clear();

            _sessionManager.StartSession("Unknown");
            _csvTelemetryWriter.CreateSessionFile(_sessionManager.CurrentSession!);

            StatusText.Text = "Status: Session running";
            DurationText.Text = "Session Duration: 00:00:00";
            SamplesText.Text = "Samples Recorded: 0";
            SummaryText.Text = "Session is recording. Summary will appear after you stop the session.";
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager.StopSession();

            StatusText.Text = "Status: Session stopped";

            GenerateSessionSummary();
        }

        private void GenerateSessionSummary()
        {
            if (_samples.Count == 0)
            {
                SummaryText.Text = "No samples were recorded. Start a session and let it run for a few seconds.";
                return;
            }

            double averageCpu = _samples.Average(sample => sample.CpuPercent);
            double peakCpu = _samples.Max(sample => sample.CpuPercent);

            double averageRam = _samples.Average(sample => sample.RamPercent);
            double peakRam = _samples.Max(sample => sample.RamPercent);

            double averageDiskActive = _samples
                .Where(sample => sample.DiskActivePercent.HasValue)
                .Average(sample => sample.DiskActivePercent!.Value);

            double peakDiskActive = _samples
                .Where(sample => sample.DiskActivePercent.HasValue)
                .Max(sample => sample.DiskActivePercent!.Value);

            double peakDiskRead = _samples
                .Where(sample => sample.DiskReadMBps.HasValue)
                .Max(sample => sample.DiskReadMBps!.Value);

            double peakDiskWrite = _samples
                .Where(sample => sample.DiskWriteMBps.HasValue)
                .Max(sample => sample.DiskWriteMBps!.Value);

            TimeSpan duration = _sessionManager.GetDuration();

            SummaryText.Text =
                $"Average CPU: {averageCpu:0}%\n" +
                $"Peak CPU: {peakCpu:0}%\n" +
                $"Average RAM: {averageRam:0}%\n" +
                $"Peak RAM: {peakRam:0}%\n" +
                $"Average Disk Active: {averageDiskActive:0}%\n" +
                $"Peak Disk Active: {peakDiskActive:0}%\n" +
                $"Peak Disk Read: {peakDiskRead:0.0} MB/s\n" +
                $"Peak Disk Write: {peakDiskWrite:0.0} MB/s\n" +
                $"Samples Recorded: {_samples.Count}\n" +
                $"Session Duration: {duration:hh\\:mm\\:ss}\n" +
                $"CSV File: {_csvTelemetryWriter.CurrentFilePath}";
        }

        protected override void OnClosed(EventArgs e)
        {
            _systemMetricsCollector.Dispose();
            _diskMetricsCollector.Dispose();
            base.OnClosed(e);
        }
    }
}