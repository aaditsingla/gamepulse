using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Gamepulse.Collectors;
using Gamepulse.Models;
using Gamepulse.Services;

namespace Gamepulse
{
    public partial class MainWindow : Window
    {
        private readonly SystemMetricsCollector _systemMetricsCollector;
        private readonly DiskMetricsCollector _diskMetricsCollector;
        private readonly GpuMetricsCollector _gpuMetricsCollector;
        private readonly WindowsGpuEngineCollector _windowsGpuEngineCollector;
        private readonly ActiveGameDetector _activeGameDetector;
        private readonly TopRamProcessCollector _topRamProcessCollector;
        private readonly TopCpuProcessCollector _topCpuProcessCollector;
        private readonly PresentMonCaptureService _presentMonCaptureService;
        private readonly PresentMonFrameMetricsParser _presentMonFrameMetricsParser;
        private readonly SessionManager _sessionManager;
        private readonly CsvTelemetryWriter _csvTelemetryWriter;

        private readonly List<TelemetrySample> _samples = new();

        private CancellationTokenSource? _samplingCancellationTokenSource;
        private Task? _samplingTask;

        private List<ProcessMemoryMetrics> _cachedTopRamProcesses = new();
        private List<ProcessCpuMetrics> _cachedTopCpuProcesses = new();
        private DateTime _lastTopProcessRefreshTime = DateTime.MinValue;

        private ActiveProcessMetrics? _latestActiveProcessMetrics;
        private string _lockedFrameTargetProcessName = "";

        private const int TopProcessRefreshSeconds = 5;
        private const int FrameTargetDetectionDelaySeconds = 3;

        private static readonly HashSet<string> IgnoredFrameTargetProcesses = new(
            StringComparer.OrdinalIgnoreCase)
        {
            "Gamepulse",
            "Gamepulse.exe",
            "explorer",
            "explorer.exe",
            "dwm",
            "dwm.exe",
            "ApplicationFrameHost",
            "ApplicationFrameHost.exe",
            "ShellExperienceHost",
            "ShellExperienceHost.exe",
            "SearchHost",
            "SearchHost.exe",
            "TextInputHost",
            "TextInputHost.exe",
            "StartMenuExperienceHost",
            "StartMenuExperienceHost.exe",
            "SystemSettings",
            "SystemSettings.exe",
            "devenv",
            "devenv.exe",
            "taskmgr",
            "taskmgr.exe",
            "cmd",
            "cmd.exe",
            "powershell",
            "powershell.exe",
            "conhost",
            "conhost.exe",
            "WindowsTerminal",
            "WindowsTerminal.exe",
            "Discord",
            "Discord.exe",
            "Teams",
            "Teams.exe"
        };

        private class TelemetrySnapshot
        {
            public SystemMetrics SystemMetrics { get; set; } = null!;
            public DiskMetrics DiskMetrics { get; set; } = null!;
            public GpuDeviceMetrics? Gpu0 { get; set; }
            public GpuDeviceMetrics? Gpu1 { get; set; }
            public ActiveProcessMetrics ActiveProcessMetrics { get; set; } = null!;
            public int SampleCount { get; set; }
            public TimeSpan Duration { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();

            _systemMetricsCollector = new SystemMetricsCollector();
            _diskMetricsCollector = new DiskMetricsCollector();
            _gpuMetricsCollector = new GpuMetricsCollector();
            _windowsGpuEngineCollector = new WindowsGpuEngineCollector();
            _activeGameDetector = new ActiveGameDetector();
            _topRamProcessCollector = new TopRamProcessCollector();
            _topCpuProcessCollector = new TopCpuProcessCollector();
            _presentMonCaptureService = new PresentMonCaptureService();
            _presentMonFrameMetricsParser = new PresentMonFrameMetricsParser();
            _sessionManager = new SessionManager();
            _csvTelemetryWriter = new CsvTelemetryWriter();

            StopButton.IsEnabled = false;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _samples.Clear();
            _cachedTopRamProcesses.Clear();
            _cachedTopCpuProcesses.Clear();
            _lastTopProcessRefreshTime = DateTime.MinValue;
            _latestActiveProcessMetrics = null;
            _lockedFrameTargetProcessName = "";

            _sessionManager.StartSession("Unknown");
            _csvTelemetryWriter.CreateSessionFile(_sessionManager.CurrentSession!);

            StartTelemetrySampling();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            StatusText.Text = "Status: Session running. Waiting for frame target...";
            DurationText.Text = "Session Duration: 00:00:00";
            SamplesText.Text = "Samples Recorded: 0";

            SummaryText.Text = "Session is recording. System telemetry will appear here after stopping.";
            FrameSummaryText.Text =
                $"Frame target detection starts in {FrameTargetDetectionDelaySeconds} seconds.\n\n" +
                "After clicking Start Session, switch to the game or rendering app you want to capture.\n" +
                "GamePulse will lock onto that foreground process for this session.";

            await Task.Delay(TimeSpan.FromSeconds(FrameTargetDetectionDelaySeconds));

            if (!_sessionManager.IsRunning || _sessionManager.CurrentSession == null)
            {
                return;
            }

            string? targetProcessName = ResolveFrameTargetProcessName();

            if (string.IsNullOrWhiteSpace(targetProcessName))
            {
                StatusText.Text = "Status: Session running, no valid frame target detected";

                FrameSummaryText.Text =
                    "Frame capture did not start because no valid target process was detected.\n\n" +
                    "Try again and switch to your game window within 3 seconds after clicking Start Session.";

                return;
            }

            _lockedFrameTargetProcessName = targetProcessName;

            bool presentMonStarted = _presentMonCaptureService.StartPhase1Capture(
                _sessionManager.CurrentSession,
                _lockedFrameTargetProcessName
            );

            StatusText.Text = presentMonStarted
                ? $"Status: Session running with frame target {_lockedFrameTargetProcessName}"
                : "Status: Session running, PresentMon failed";

            FrameSummaryText.Text = presentMonStarted
                ? $"Frame capture locked onto target:\n{_lockedFrameTargetProcessName}\n\nPresentMon raw CSV target:\n{_presentMonCaptureService.CurrentOutputFilePath}"
                : $"Frame capture failed to start for target:\n{_lockedFrameTargetProcessName}\n\nReason:\n{_presentMonCaptureService.LastStatusMessage}";
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;

            StatusText.Text = "Status: Stopping session...";
            SummaryText.Text = "Stopping session and finalizing system telemetry.";
            FrameSummaryText.Text = string.IsNullOrWhiteSpace(_lockedFrameTargetProcessName)
                ? "No frame capture target was locked for this session."
                : $"Finalizing PresentMon frame capture for {_lockedFrameTargetProcessName}. The app should stay responsive.";

            _sessionManager.StopSession();

            await StopTelemetrySamplingAsync();

            GenerateSessionSummary();

            await _presentMonCaptureService.StopAndGetPhase1StatusAsync();

            FrameMetricsSummary frameMetricsSummary = _presentMonFrameMetricsParser.Parse(
                _presentMonCaptureService.CurrentOutputFilePath
            );

            FrameSummaryText.Text = FormatFrameSummaryPanel(frameMetricsSummary);

            StatusText.Text = "Status: Session stopped";

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private string? ResolveFrameTargetProcessName()
        {
            string processName = _latestActiveProcessMetrics?.ProcessName ?? "";

            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            string normalized = NormalizeProcessName(processName);

            if (IgnoredFrameTargetProcesses.Contains(processName) ||
                IgnoredFrameTargetProcesses.Contains(normalized))
            {
                return null;
            }

            return normalized;
        }

        private static string NormalizeProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return "";
            }

            string normalized = processName.Trim();

            if (!normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                normalized += ".exe";
            }

            return normalized;
        }

        private void StartTelemetrySampling()
        {
            _samplingCancellationTokenSource?.Cancel();
            _samplingCancellationTokenSource?.Dispose();

            _samplingCancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _samplingCancellationTokenSource.Token;

            _samplingTask = RunTelemetrySamplingLoopAsync(token);
        }

        private async Task StopTelemetrySamplingAsync()
        {
            CancellationTokenSource? cancellationTokenSource = _samplingCancellationTokenSource;
            Task? samplingTask = _samplingTask;

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }

            if (samplingTask != null)
            {
                try
                {
                    await samplingTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            }

            cancellationTokenSource?.Dispose();
            _samplingCancellationTokenSource = null;
            _samplingTask = null;
        }

        private async Task RunTelemetrySamplingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                try
                {
                    TelemetrySnapshot snapshot = await Task.Run(
                        CollectTelemetrySnapshot,
                        token
                    );

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ApplyTelemetrySnapshot(snapshot);
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"Status: Sampling error - {exception.Message}";
                    });
                }

                stopwatch.Stop();

                int delayMs = 1000 - (int)stopwatch.ElapsedMilliseconds;

                if (delayMs < 50)
                {
                    delayMs = 50;
                }

                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private TelemetrySnapshot CollectTelemetrySnapshot()
        {
            SystemMetrics systemMetrics = _systemMetricsCollector.Collect();
            DiskMetrics diskMetrics = _diskMetricsCollector.Collect();
            GpuMetrics gpuMetrics = _gpuMetricsCollector.Collect();
            WindowsGpuEngineMetrics windowsGpuEngineMetrics = _windowsGpuEngineCollector.Collect();
            ActiveProcessMetrics activeProcessMetrics = _activeGameDetector.Collect();

            RefreshTopProcessCachesIfNeeded();

            string topRamProcessesText = FormatTopRamProcesses(_cachedTopRamProcesses);
            string topCpuProcessesText = FormatTopCpuProcesses(_cachedTopCpuProcesses);

            GpuDeviceMetrics? dedicatedGpu = GetDedicatedGpu(gpuMetrics);
            GpuDeviceMetrics? integratedGpu = GetIntegratedGpu(gpuMetrics);

            if (integratedGpu == null)
            {
                integratedGpu = CreateEstimatedIntelGpu(windowsGpuEngineMetrics, dedicatedGpu);
            }

            GpuDeviceMetrics? gpu0 = integratedGpu;
            GpuDeviceMetrics? gpu1 = dedicatedGpu;

            if (_sessionManager.IsRunning && _sessionManager.CurrentSession != null)
            {
                TelemetrySample sample = new TelemetrySample
                {
                    SessionId = _sessionManager.CurrentSession.SessionId,
                    Timestamp = DateTime.Now,
                    ElapsedSeconds = _sessionManager.GetElapsedSeconds(),
                    GameName = string.IsNullOrWhiteSpace(activeProcessMetrics.ProcessName)
                        ? _sessionManager.CurrentSession.GameName
                        : activeProcessMetrics.ProcessName,

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

                    ActiveGameProcessName = activeProcessMetrics.ProcessName,
                    GameCpuPercent = activeProcessMetrics.CpuPercent,
                    GameRamMb = activeProcessMetrics.RamMb,

                    TopRamProcesses = topRamProcessesText,
                    TopCpuProcesses = topCpuProcessesText
                };

                _samples.Add(sample);
                _csvTelemetryWriter.WriteSample(sample);
            }

            return new TelemetrySnapshot
            {
                SystemMetrics = systemMetrics,
                DiskMetrics = diskMetrics,
                Gpu0 = gpu0,
                Gpu1 = gpu1,
                ActiveProcessMetrics = activeProcessMetrics,
                SampleCount = _samples.Count,
                Duration = _sessionManager.GetDuration()
            };
        }

        private void RefreshTopProcessCachesIfNeeded()
        {
            bool shouldRefresh =
                _cachedTopRamProcesses.Count == 0 ||
                _cachedTopCpuProcesses.Count == 0 ||
                (DateTime.Now - _lastTopProcessRefreshTime).TotalSeconds >= TopProcessRefreshSeconds;

            if (!shouldRefresh)
            {
                return;
            }

            _cachedTopRamProcesses = _topRamProcessCollector.CollectTopProcesses(5);
            _cachedTopCpuProcesses = _topCpuProcessCollector.CollectTopProcesses(5);
            _lastTopProcessRefreshTime = DateTime.Now;
        }

        private void ApplyTelemetrySnapshot(TelemetrySnapshot snapshot)
        {
            _latestActiveProcessMetrics = snapshot.ActiveProcessMetrics;

            CpuText.Text = $"{snapshot.SystemMetrics.CpuPercent:0}%";

            RamText.Text = $"{snapshot.SystemMetrics.Memory.UsedPercentage:0}%";
            RamDetailsText.Text =
                $"{snapshot.SystemMetrics.Memory.UsedGb:0.0} GB / {snapshot.SystemMetrics.Memory.TotalGb:0.0} GB";

            DiskText.Text = $"{snapshot.DiskMetrics.ActivePercent:0}%";
            DiskDetailsText.Text =
                $"Read {snapshot.DiskMetrics.ReadMBps:0.0} MB/s | Write {snapshot.DiskMetrics.WriteMBps:0.0} MB/s";

            UpdateGpuCards(snapshot.Gpu0, snapshot.Gpu1);
            UpdateActiveProcessCard(snapshot.ActiveProcessMetrics);

            DurationText.Text = $"Session Duration: {snapshot.Duration:hh\\:mm\\:ss}";
            SamplesText.Text = $"Samples Recorded: {snapshot.SampleCount}";
        }

        private static string FormatTopRamProcesses(List<ProcessMemoryMetrics> processes)
        {
            if (processes.Count == 0)
            {
                return "";
            }

            return string.Join(" | ", processes.Select(process =>
                $"{process.ProcessName}: {process.RamMb:0} MB"));
        }

        private static string FormatTopCpuProcesses(List<ProcessCpuMetrics> processes)
        {
            if (processes.Count == 0)
            {
                return "";
            }

            return string.Join(" | ", processes.Select(process =>
                $"{process.ProcessName}: {process.CpuPercent:0.0}%"));
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

        private static GpuDeviceMetrics CreateEstimatedIntelGpu(
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

        private void UpdateActiveProcessCard(ActiveProcessMetrics activeProcessMetrics)
        {
            if (string.IsNullOrWhiteSpace(activeProcessMetrics.ProcessName))
            {
                ActiveProcessText.Text = "None";
                ActiveProcessDetailsText.Text = "CPU N/A | RAM 0 MB";
                return;
            }

            string cpuText = activeProcessMetrics.CpuPercent.HasValue
                ? $"{activeProcessMetrics.CpuPercent.Value:0.0}%"
                : "N/A";

            ActiveProcessText.Text = activeProcessMetrics.ProcessName;
            ActiveProcessDetailsText.Text =
                $"CPU {cpuText} | RAM {activeProcessMetrics.RamMb:0} MB | Window: {activeProcessMetrics.WindowTitle}";
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

        private static string FormatFrameSummaryPanel(FrameMetricsSummary summary)
        {
            if (summary.FrameCount == 0)
            {
                return
                    "FPS Summary: No valid PresentMon frame rows parsed\n\n" +
                    $"Source File: {summary.SourceFilePath}";
            }

            StringBuilder builder = new();

            int totalBuckets = summary.PerSecondMetrics.Count;
            int firstBucket = totalBuckets > 0 ? summary.PerSecondMetrics.First().Second : 0;
            int lastBucket = totalBuckets > 0 ? summary.PerSecondMetrics.Last().Second : 0;

            builder.AppendLine($"FPS Summary: {summary.AverageFps:0.0} FPS | Avg FT {summary.AverageFrameTimeMs:0.00} ms | 1% Low {summary.OnePercentLowFps:0.0} FPS");
            builder.AppendLine();
            builder.AppendLine($"Locked Target Process: {summary.TargetProcessName}");
            builder.AppendLine($"Frame Rows Parsed: {summary.FrameCount}");
            builder.AppendLine($"Frame Capture Duration: {summary.CaptureDurationSeconds:0.00} sec");
            builder.AppendLine($"Average FPS: {summary.AverageFps:0.0}");
            builder.AppendLine($"Average Frame Time: {summary.AverageFrameTimeMs:0.00} ms");
            builder.AppendLine($"Worst Frame Time: {summary.WorstFrameTimeMs:0.00} ms");
            builder.AppendLine($"1% Low FPS Estimate: {summary.OnePercentLowFps:0.0}");
            builder.AppendLine($"0.1% Low FPS Estimate: {summary.ZeroPointOnePercentLowFps:0.0}");
            builder.AppendLine();
            builder.AppendLine($"Per-Second Buckets: {totalBuckets}");
            builder.AppendLine($"First Captured Bucket: Second {firstBucket}");
            builder.AppendLine($"Last Captured Bucket: Second {lastBucket}");
            builder.AppendLine();
            builder.AppendLine("All Per-Second Buckets:");

            foreach (FrameSecondMetrics second in summary.PerSecondMetrics)
            {
                builder.AppendLine(
                    $"Second {second.Second}: " +
                    $"{second.FrameCount} frames | " +
                    $"Avg FPS {second.AverageFps:0.0} | " +
                    $"Avg FT {second.AverageFrameTimeMs:0.00} ms | " +
                    $"Worst FT {second.WorstFrameTimeMs:0.00} ms | " +
                    $"1% Low {second.OnePercentLowFps:0.0}"
                );
            }

            builder.AppendLine();
            builder.AppendLine($"Source File: {summary.SourceFilePath}");

            return builder.ToString();
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

            double? averageDiskActive = AverageNullable(_samples.Select(sample => sample.DiskActivePercent));
            double? peakDiskActive = MaxNullable(_samples.Select(sample => sample.DiskActivePercent));

            double? peakDiskRead = MaxNullable(_samples.Select(sample => sample.DiskReadMBps));
            double? peakDiskWrite = MaxNullable(_samples.Select(sample => sample.DiskWriteMBps));

            double? averageGpu0 = AverageNullable(_samples.Select(sample => sample.Gpu0UsagePercent));
            double? peakGpu0 = MaxNullable(_samples.Select(sample => sample.Gpu0UsagePercent));

            double? averageGpu1 = AverageNullable(_samples.Select(sample => sample.Gpu1UsagePercent));
            double? peakGpu1 = MaxNullable(_samples.Select(sample => sample.Gpu1UsagePercent));

            double? averageGameCpu = AverageNullable(_samples.Select(sample => sample.GameCpuPercent));
            double? peakGameCpu = MaxNullable(_samples.Select(sample => sample.GameCpuPercent));

            string mostCommonActiveProcess = _samples
                .Where(sample => !string.IsNullOrWhiteSpace(sample.ActiveGameProcessName))
                .GroupBy(sample => sample.ActiveGameProcessName)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault() ?? "N/A";

            double peakGameRamMb = _samples
                .Where(sample => sample.GameRamMb.HasValue)
                .Select(sample => sample.GameRamMb!.Value)
                .DefaultIfEmpty(0)
                .Max();

            string mostCommonTopRamProcess = GetMostCommonTopRamProcess();
            string mostCommonTopCpuProcess = GetMostCommonTopCpuProcess();

            TimeSpan duration = _sessionManager.GetDuration();

            SummaryText.Text =
                $"Average CPU: {averageCpu:0}%\n" +
                $"Peak CPU: {peakCpu:0}%\n" +
                $"Average RAM: {averageRam:0}%\n" +
                $"Peak RAM: {peakRam:0}%\n" +
                $"Average Disk Active: {FormatNullablePercent(averageDiskActive)}\n" +
                $"Peak Disk Active: {FormatNullablePercent(peakDiskActive)}\n" +
                $"Peak Disk Read: {FormatNullableNumber(peakDiskRead)} MB/s\n" +
                $"Peak Disk Write: {FormatNullableNumber(peakDiskWrite)} MB/s\n" +
                $"Average GPU 0: {FormatNullablePercent(averageGpu0)}\n" +
                $"Peak GPU 0: {FormatNullablePercent(peakGpu0)}\n" +
                $"Average GPU 1: {FormatNullablePercent(averageGpu1)}\n" +
                $"Peak GPU 1: {FormatNullablePercent(peakGpu1)}\n" +
                $"Most Common Active Process: {mostCommonActiveProcess}\n" +
                $"Average Active Process CPU: {FormatNullablePercent(averageGameCpu)}\n" +
                $"Peak Active Process CPU: {FormatNullablePercent(peakGameCpu)}\n" +
                $"Peak Active Process RAM: {peakGameRamMb:0} MB\n" +
                $"Most Common Top RAM Process: {mostCommonTopRamProcess}\n" +
                $"Most Common Top CPU Process: {mostCommonTopCpuProcess}\n" +
                $"Samples Recorded: {_samples.Count}\n" +
                $"Session Duration: {duration:hh\\:mm\\:ss}\n" +
                $"CSV File: {_csvTelemetryWriter.CurrentFilePath}";
        }

        private string GetMostCommonTopRamProcess()
        {
            List<string> firstProcesses = _samples
                .Where(sample => !string.IsNullOrWhiteSpace(sample.TopRamProcesses))
                .Select(sample => sample.TopRamProcesses.Split('|')[0].Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (firstProcesses.Count == 0)
            {
                return "N/A";
            }

            return firstProcesses
                .GroupBy(value => value)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .First();
        }

        private string GetMostCommonTopCpuProcess()
        {
            List<string> firstProcesses = _samples
                .Where(sample => !string.IsNullOrWhiteSpace(sample.TopCpuProcesses))
                .Select(sample => sample.TopCpuProcesses.Split('|')[0].Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (firstProcesses.Count == 0)
            {
                return "N/A";
            }

            return firstProcesses
                .GroupBy(value => value)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .First();
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
            return value.HasValue ? $"{value.Value:0.0}%" : "N/A";
        }

        private static string FormatNullableNumber(double? value)
        {
            return value.HasValue ? $"{value.Value:0.0}" : "N/A";
        }

        protected override void OnClosed(EventArgs e)
        {
            _samplingCancellationTokenSource?.Cancel();

            try
            {
                _samplingTask?.Wait(2000);
            }
            catch
            {
            }

            _presentMonCaptureService.Cleanup();
            _systemMetricsCollector.Dispose();
            _diskMetricsCollector.Dispose();
            _gpuMetricsCollector.Dispose();
            _windowsGpuEngineCollector.Dispose();

            base.OnClosed(e);
        }
    }
}