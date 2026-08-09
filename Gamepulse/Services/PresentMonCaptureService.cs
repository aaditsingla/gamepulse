using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class PresentMonCaptureService
    {
        private const string PresentMonPath = @"C:\Tools\PresentMon\PresentMon.exe";
        private const string PresentMonFolder = @"C:\Tools\PresentMon";

        private Process? _presentMonProcess;
        private readonly StringBuilder _log = new();

        public string? CurrentOutputFilePath { get; private set; }
        public string LastStatusMessage { get; private set; } = "";
        public string TargetProcessName { get; private set; } = "";

        public bool StartPhase1Capture(SessionInfo session, string targetProcessName)
        {
            Cleanup();

            _log.Clear();
            LastStatusMessage = "";
            CurrentOutputFilePath = null;
            TargetProcessName = NormalizeProcessName(targetProcessName);

            if (string.IsNullOrWhiteSpace(TargetProcessName))
            {
                LastStatusMessage = "No valid target process was provided.";
                AddLog(LastStatusMessage);
                return false;
            }

            if (!File.Exists(PresentMonPath))
            {
                LastStatusMessage = $"PresentMon not found at {PresentMonPath}";
                AddLog(LastStatusMessage);
                return false;
            }

            Directory.CreateDirectory(PresentMonFolder);

            CurrentOutputFilePath = Path.Combine(
                PresentMonFolder,
                $"gamepulse_presentmon_{session.SessionId}.csv"
            );

            try
            {
                if (File.Exists(CurrentOutputFilePath))
                {
                    File.Delete(CurrentOutputFilePath);
                }

                RunPresentMonCleanupCommand();

                string arguments =
                    $"--process_name {TargetProcessName} " +
                    $"--output_file {CurrentOutputFilePath}";

                AddLog($"Capture command: \"{PresentMonPath}\" {arguments}");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PresentMonPath,
                    Arguments = arguments,
                    WorkingDirectory = PresentMonFolder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _presentMonProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                _presentMonProcess.OutputDataReceived += (_, data) =>
                {
                    if (!string.IsNullOrWhiteSpace(data.Data))
                    {
                        LastStatusMessage = data.Data;
                        AddLog("OUT: " + data.Data);
                    }
                };

                _presentMonProcess.ErrorDataReceived += (_, data) =>
                {
                    if (!string.IsNullOrWhiteSpace(data.Data))
                    {
                        LastStatusMessage = data.Data;
                        AddLog("ERR: " + data.Data);
                    }
                };

                bool started = _presentMonProcess.Start();

                if (!started)
                {
                    LastStatusMessage = "PresentMon did not start.";
                    AddLog(LastStatusMessage);
                    return false;
                }

                _presentMonProcess.BeginOutputReadLine();
                _presentMonProcess.BeginErrorReadLine();

                LastStatusMessage = $"PresentMon full-session capture started for {TargetProcessName}.";
                AddLog(LastStatusMessage);

                return true;
            }
            catch (Exception exception)
            {
                LastStatusMessage = exception.Message;
                AddLog("Exception: " + exception.Message);
                return false;
            }
        }

        public Task<string> StopAndGetPhase1StatusAsync()
        {
            return Task.Run(StopAndGetPhase1Status);
        }

        private string StopAndGetPhase1Status()
        {
            try
            {
                if (_presentMonProcess != null)
                {
                    AddLog("Stopping PresentMon full-session capture.");

                    if (!_presentMonProcess.HasExited)
                    {
                        try
                        {
                            _presentMonProcess.Kill(true);
                            _presentMonProcess.WaitForExit(5000);
                            AddLog("PresentMon process stopped.");
                        }
                        catch (Exception killException)
                        {
                            AddLog("Stop/kill failed: " + killException.Message);
                        }
                    }
                    else
                    {
                        AddLog($"PresentMon had already exited with code {_presentMonProcess.ExitCode}.");
                    }

                    _presentMonProcess.Dispose();
                    _presentMonProcess = null;

                    Thread.Sleep(1500);
                }
            }
            catch (Exception exception)
            {
                AddLog("Stop exception: " + exception.Message);
            }

            return GetOutputFileStatus();
        }

        public void Cleanup()
        {
            try
            {
                if (_presentMonProcess != null && !_presentMonProcess.HasExited)
                {
                    _presentMonProcess.Kill(true);
                    _presentMonProcess.WaitForExit(3000);
                }

                _presentMonProcess?.Dispose();
                _presentMonProcess = null;
            }
            catch
            {
                _presentMonProcess = null;
            }
        }

        private void RunPresentMonCleanupCommand()
        {
            try
            {
                AddLog("Running PresentMon cleanup command first.");

                ProcessStartInfo cleanupStartInfo = new ProcessStartInfo
                {
                    FileName = PresentMonPath,
                    Arguments = "--terminate_existing_session",
                    WorkingDirectory = PresentMonFolder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process cleanupProcess = new Process
                {
                    StartInfo = cleanupStartInfo
                };

                cleanupProcess.Start();

                string output = cleanupProcess.StandardOutput.ReadToEnd();
                string error = cleanupProcess.StandardError.ReadToEnd();

                cleanupProcess.WaitForExit(5000);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    AddLog("Cleanup OUT: " + output.Trim());
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    AddLog("Cleanup ERR: " + error.Trim());
                }

                AddLog($"Cleanup exited with code {cleanupProcess.ExitCode}.");

                Thread.Sleep(1000);
            }
            catch (Exception exception)
            {
                AddLog("Cleanup command failed: " + exception.Message);
            }
        }

        private string GetOutputFileStatus()
        {
            StringBuilder status = new();

            status.AppendLine("PresentMon Capture Status");
            status.AppendLine();

            if (string.IsNullOrWhiteSpace(CurrentOutputFilePath))
            {
                status.AppendLine("Output path is empty.");
                return status.ToString();
            }

            if (!File.Exists(CurrentOutputFilePath))
            {
                status.AppendLine("PresentMon output file was not created.");
                status.AppendLine($"Target Process: {TargetProcessName}");
                status.AppendLine($"Expected path: {CurrentOutputFilePath}");
                status.AppendLine($"Last status: {LastStatusMessage}");
                return status.ToString();
            }

            FileInfo fileInfo = new FileInfo(CurrentOutputFilePath);

            status.AppendLine("PresentMon output file created.");
            status.AppendLine($"Target Process: {TargetProcessName}");
            status.AppendLine($"Path: {CurrentOutputFilePath}");
            status.AppendLine($"Size: {fileInfo.Length} bytes");

            return status.ToString();
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

        private void AddLog(string message)
        {
            _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}