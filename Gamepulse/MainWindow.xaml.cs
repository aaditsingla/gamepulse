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
        private readonly GpuMetricsCollector _gpuMetricsCollector;
        private readonly WindowsGpuEngineCollector _windowsGpuEngineCollector;
        private readonly SessionManager _sessionManager;
        private readonly CsvTelemetryWriter _csvTelemetryWriter;

        private readonly List<TelemetrySample> _samples = new();

        public MainWindow()
        {
            InitializeComponent();

            _systemMetricsCollector = new SystemMetricsCollector();
            _diskMetricsCollector = new DiskMetricsCollector();
            _gpuMetricsCollector = new GpuMetricsCollector();
            _windowsGpuEngineCollector = new WindowsGpuEngineCollector();
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
            GpuMetrics gpuMetrics = _gpuMetricsCollector.Collect();
            WindowsGpuEngineMetrics windowsGpuEngineMetrics = _windowsGpuEngineCollector.Collect();

            GpuDeviceMetrics? dedicatedGpu = GetDedicatedGpu(gpuMetrics);
            GpuDeviceMetrics? integratedGpu = GetIntegratedGpu(gpuMetrics);

            if (integratedGpu == null)
            {
                integratedGpu = CreateEstimatedIntelGpu(windowsGpuEngineMetrics, dedicatedGpu);
            }

            GpuDeviceMetrics? gpu0 = integratedGpu;
            GpuDeviceMetrics? gpu1 = dedicatedGpu;

            CpuText.Text = $"{systemMetrics.CpuPercent:0}%";
            RamText.Text = $"{systemMetrics.Memory.UsedPercentage:0}%";
            RamDetailsText.Text = $"{systemMetrics.Memory.UsedGb:0.0} GB / {systemMetrics.Memory.TotalGb:0.0} GB";

            DiskText.Text = $"{diskMetrics.ActivePercent:0}%";
            DiskDetailsText.Text = $"Read {diskMetrics.ReadMBps:0.0} MB/s | Write {diskMetrics.WriteMBps:0.0} MB/s";

            UpdateGpuCards(gpu0, gpu1);

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

                    Gpu0Name = gpu0?.Name ?? "",
                    Gpu0UsagePercent = gpu0?.UsagePercent,
                    Gpu0VramUsedMb = gpu0?.VramUsedMb,
                    Gpu0TemperatureC = gpu0?.TemperatureC,

                    Gpu1Name = gpu1?.Name ?? "",
                    Gpu1UsagePercent = gpu1?.UsagePercent,
                    Gpu1VramUsedMb = gpu1?.VramUsedMb,
                    Gpu1TemperatureC = gpu1?.TemperatureC,

                    TopRamProcesses = ""
                };

                _samples.Add(sample);
                _csvTelemetryWriter.WriteSample(sample);

                SamplesText.Text = $"Samples Recorded: {_samples.Count}";
            }
        }

        private static GpuDeviceMetrics? GetIntegratedGpu(GpuMetrics gpuMetrics)
        {
            return gpuMetrics.Devices
                .FirstOrDefault(gpu =>
                    gpu.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase));
        }

        private static GpuDeviceMetrics? GetDedicatedGpu(GpuMetrics gpuMetrics)
        {
            return gpuMetrics.Devices
                .FirstOrDefault(gpu =>
                    gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                    gpu.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
        }

        private static GpuDeviceMetrics? CreateEstimatedIntelGpu(
            WindowsGpuEngineMetrics windowsGpuEngineMetrics,
            GpuDeviceMetrics? dedicatedGpu)
        {
            double estimatedIntelUsage = windowsGpuEngineMetrics.TotalUsagePercent;

            if (dedicatedGpu != null)
            {
                estimatedIntelUsage -= dedicatedGpu.UsagePercent;
            }

            if (estimatedIntelUsage < 0)
            {
                estimatedIntelUsage = 0;
            }

            if (estimatedIntelUsage > 100)
            {
                estimatedIntelUsage = 100;
            }

            return new GpuDeviceMetrics(
                "Intel Integrated GPU estimate from Windows GPU Engine",
                estimatedIntelUsage,
                null,
                null
            );
        }

        private void UpdateGpuCards(GpuDeviceMetrics? gpu0, GpuDeviceMetrics? gpu1)
        {
            if (gpu0 == null)
            {
                Gpu0Text.Text = "0%";
                Gpu0DetailsText.Text = "Not detected";
            }
            else
            {
                Gpu0Text.Text = $"{gpu0.UsagePercent:0}%";
                Gpu0DetailsText.Text = FormatGpuDetails(gpu0);
            }

            if (gpu1 == null)
            {
                Gpu1Text.Text = "0%";
                Gpu1DetailsText.Text = "Not detected";
            }
            else
            {
                Gpu1Text.Text = $"{gpu1.UsagePercent:0}%";
                Gpu1DetailsText.Text = FormatGpuDetails(gpu1);
            }
        }

        private static string FormatGpuDetails(GpuDeviceMetrics gpu)
        {
            string temperatureText = gpu.TemperatureC.HasValue
                ? $" | {gpu.TemperatureC.Value:0}°C"
                : "";

            string vramText = gpu.VramUsedMb.HasValue
                ? $" | VRAM {gpu.VramUsedMb.Value:0} MB"
                : "";

            return $"{gpu.Name}{temperatureText}{vramText}";
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

            double? averageGpu0 = AverageNullable(_samples.Select(sample => sample.Gpu0UsagePercent));
            double? peakGpu0 = MaxNullable(_samples.Select(sample => sample.Gpu0UsagePercent));

            double? averageGpu1 = AverageNullable(_samples.Select(sample => sample.Gpu1UsagePercent));
            double? peakGpu1 = MaxNullable(_samples.Select(sample => sample.Gpu1UsagePercent));

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
                $"Average GPU 0: {FormatNullablePercent(averageGpu0)}\n" +
                $"Peak GPU 0: {FormatNullablePercent(peakGpu0)}\n" +
                $"Average GPU 1: {FormatNullablePercent(averageGpu1)}\n" +
                $"Peak GPU 1: {FormatNullablePercent(peakGpu1)}\n" +
                $"Samples Recorded: {_samples.Count}\n" +
                $"Session Duration: {duration:hh\\:mm\\:ss}\n" +
                $"CSV File: {_csvTelemetryWriter.CurrentFilePath}";
        }

        private static double? AverageNullable(IEnumerable<double?> values)
        {
            List<double> validValues = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            if (validValues.Count == 0)
            {
                return null;
            }

            return validValues.Average();
        }

        private static double? MaxNullable(IEnumerable<double?> values)
        {
            List<double> validValues = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            if (validValues.Count == 0)
            {
                return null;
            }

            return validValues.Max();
        }

        private static string FormatNullablePercent(double? value)
        {
            return value.HasValue ? $"{value.Value:0}%" : "N/A";
        }

        protected override void OnClosed(EventArgs e)
        {
            _systemMetricsCollector.Dispose();
            _diskMetricsCollector.Dispose();
            _gpuMetricsCollector.Dispose();
            _windowsGpuEngineCollector.Dispose();
            base.OnClosed(e);
        }
    }
}