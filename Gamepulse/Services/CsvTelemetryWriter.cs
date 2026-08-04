using System;
using System.Globalization;
using System.IO;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class CsvTelemetryWriter
    {
        public string? CurrentFilePath { get; private set; }

        public string CreateSessionFile(SessionInfo session)
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string sessionsFolder = Path.Combine(documentsFolder, "GamePulseSessions");

            Directory.CreateDirectory(sessionsFolder);

            string fileName = $"session_{session.SessionId}.csv";
            string filePath = Path.Combine(sessionsFolder, fileName);

            File.WriteAllText(filePath, GetHeader() + Environment.NewLine);

            CurrentFilePath = filePath;

            return filePath;
        }

        public void WriteSample(TelemetrySample sample)
        {
            if (string.IsNullOrWhiteSpace(CurrentFilePath))
            {
                return;
            }

            File.AppendAllText(CurrentFilePath, FormatSample(sample) + Environment.NewLine);
        }

        private static string GetHeader()
        {
            return string.Join(",",
                "SessionId",
                "Timestamp",
                "ElapsedSeconds",
                "GameName",
                "CpuPercent",
                "RamPercent",
                "RamUsedGb",
                "RamTotalGb",
                "DiskReadMBps",
                "DiskWriteMBps",
                "DiskActivePercent",
                "Gpu0Name",
                "Gpu0UsagePercent",
                "Gpu0VramUsedMb",
                "Gpu0TemperatureC",
                "Gpu1Name",
                "Gpu1UsagePercent",
                "Gpu1VramUsedMb",
                "Gpu1TemperatureC",
                "ActiveGameProcessName",
                "GameCpuPercent",
                "GameRamMb",
                "Fps",
                "AverageFrameTimeMs",
                "WorstFrameTimeMs",
                "OnePercentLowFps",
                "PointOnePercentLowFps",
                "StutterCount",
                "TopRamProcesses"
            );
        }

        private static string FormatSample(TelemetrySample sample)
        {
            return string.Join(",",
                Escape(sample.SessionId),
                Escape(sample.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                sample.ElapsedSeconds.ToString(CultureInfo.InvariantCulture),
                Escape(sample.GameName),
                FormatDouble(sample.CpuPercent),
                FormatDouble(sample.RamPercent),
                FormatDouble(sample.RamUsedGb),
                FormatDouble(sample.RamTotalGb),
                FormatNullableDouble(sample.DiskReadMBps),
                FormatNullableDouble(sample.DiskWriteMBps),
                FormatNullableDouble(sample.DiskActivePercent),
                Escape(sample.Gpu0Name),
                FormatNullableDouble(sample.Gpu0UsagePercent),
                FormatNullableDouble(sample.Gpu0VramUsedMb),
                FormatNullableDouble(sample.Gpu0TemperatureC),
                Escape(sample.Gpu1Name),
                FormatNullableDouble(sample.Gpu1UsagePercent),
                FormatNullableDouble(sample.Gpu1VramUsedMb),
                FormatNullableDouble(sample.Gpu1TemperatureC),
                Escape(sample.ActiveGameProcessName),
                FormatNullableDouble(sample.GameCpuPercent),
                FormatNullableDouble(sample.GameRamMb),
                FormatNullableDouble(sample.Fps),
                FormatNullableDouble(sample.AverageFrameTimeMs),
                FormatNullableDouble(sample.WorstFrameTimeMs),
                FormatNullableDouble(sample.OnePercentLowFps),
                FormatNullableDouble(sample.PointOnePercentLowFps),
                sample.StutterCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                Escape(sample.TopRamProcesses)
            );
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatNullableDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.0", CultureInfo.InvariantCulture)
                : "";
        }

        private static string Escape(string value)
        {
            string safeValue = value.Replace("\"", "\"\"");
            return $"\"{safeValue}\"";
        }
    }
}