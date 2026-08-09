using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
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

        public bool StartPhase1Capture(SessionInfo session)
        {
            Cleanup();

            _log.Clear();
            LastStatusMessage = "";
            CurrentOutputFilePath = null;

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
                    "--process_name chrome.exe " +
                    $"--output_file {CurrentOutputFilePath} " +
                    "--timed 10";

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

                LastStatusMessage = "PresentMon Phase 1 capture started for chrome.exe.";
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

        public string StopAndGetPhase1Status()
        {
            try
            {
                if (_presentMonProcess != null)
                {
                    AddLog("Waiting for PresentMon timed capture to finish.");

                    bool exited = _presentMonProcess.WaitForExit(15000);

                    if (exited)
                    {
                        AddLog($"PresentMon exited with code {_presentMonProcess.ExitCode}.");
                    }
                    else
                    {
                        AddLog("PresentMon did not exit by itself after timed capture.");

                        try
                        {
                            AddLog("Killing PresentMon after timed capture so it can flush and release.");
                            _presentMonProcess.Kill(true);
                            _presentMonProcess.WaitForExit(3000);
                        }
                        catch (Exception killException)
                        {
                            AddLog("Kill failed: " + killException.Message);
                        }
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

            status.AppendLine("Phase 1 PresentMon Status");
            status.AppendLine();

            if (string.IsNullOrWhiteSpace(CurrentOutputFilePath))
            {
                status.AppendLine("Output path is empty.");
                status.AppendLine();
                status.AppendLine("Log:");
                status.AppendLine(_log.ToString());
                return status.ToString();
            }

            if (!File.Exists(CurrentOutputFilePath))
            {
                status.AppendLine("PresentMon output file was not created.");
                status.AppendLine($"Expected path: {CurrentOutputFilePath}");
                status.AppendLine($"Last status: {LastStatusMessage}");
                status.AppendLine();
                status.AppendLine("Log:");
                status.AppendLine(_log.ToString());
                return status.ToString();
            }

            FileInfo fileInfo = new FileInfo(CurrentOutputFilePath);

            status.AppendLine("PresentMon output file created.");
            status.AppendLine($"Path: {CurrentOutputFilePath}");
            status.AppendLine($"Size: {fileInfo.Length} bytes");
            status.AppendLine($"Header Preview: {GetHeaderPreview(CurrentOutputFilePath)}");
            status.AppendLine($"Last status: {LastStatusMessage}");
            status.AppendLine();
            status.AppendLine("Log:");
            status.AppendLine(_log.ToString());

            return status.ToString();
        }

        private static string GetHeaderPreview(string filePath)
        {
            try
            {
                using FileStream fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );

                using StreamReader reader = new StreamReader(fileStream);

                string? header = reader.ReadLine();

                if (string.IsNullOrWhiteSpace(header))
                {
                    return "File exists, but header is empty.";
                }

                return header.Length > 220
                    ? header.Substring(0, 220) + "..."
                    : header;
            }
            catch (Exception exception)
            {
                return "Could not read header: " + exception.Message;
            }
        }

        private void AddLog(string message)
        {
            _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}