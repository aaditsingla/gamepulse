using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class PresentMonFrameMetricsParser
    {
        private class RawFrameRecord
        {
            public string Application { get; set; } = "";
            public double TimeInMs { get; set; }
            public double MsBetweenPresents { get; set; }
        }

        public FrameMetricsSummary Parse(string? filePath)
        {
            FrameMetricsSummary summary = new();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return summary;
            }

            summary.SourceFilePath = filePath;

            if (!File.Exists(filePath))
            {
                return summary;
            }

            List<RawFrameRecord> records = ReadFrameRecords(filePath);

            if (records.Count == 0)
            {
                return summary;
            }

            double firstTimeMs = records.Min(record => record.TimeInMs);
            double lastTimeMs = records.Max(record => record.TimeInMs);

            List<double> frameTimes = records
                .Select(record => record.MsBetweenPresents)
                .Where(value => value > 0)
                .ToList();

            if (frameTimes.Count == 0)
            {
                return summary;
            }

            summary.TargetProcessName = records
                .GroupBy(record => record.Application)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault() ?? "";

            summary.FrameCount = records.Count;
            summary.CaptureDurationSeconds = Math.Max(0, (lastTimeMs - firstTimeMs) / 1000.0);
            summary.AverageFrameTimeMs = frameTimes.Average();
            summary.AverageFps = CalculateFpsFromFrameTime(summary.AverageFrameTimeMs);
            summary.WorstFrameTimeMs = frameTimes.Max();
            summary.OnePercentLowFps = CalculateLowFps(frameTimes, 0.01);
            summary.ZeroPointOnePercentLowFps = CalculateLowFps(frameTimes, 0.001);

            summary.PerSecondMetrics = BuildPerSecondMetrics(records, firstTimeMs);

            return summary;
        }

        public string FormatSummary(FrameMetricsSummary summary)
        {
            if (summary.FrameCount == 0)
            {
                return
                    "PresentMon Frame Metrics\n\n" +
                    "No valid frame rows were parsed yet.\n" +
                    $"Source File: {summary.SourceFilePath}";
            }

            StringBuilder builder = new();

            builder.AppendLine("PresentMon Frame Metrics");
            builder.AppendLine();
            builder.AppendLine($"Target Process: {summary.TargetProcessName}");
            builder.AppendLine($"Frame Rows Parsed: {summary.FrameCount}");
            builder.AppendLine($"Capture Duration: {summary.CaptureDurationSeconds:0.00} sec");
            builder.AppendLine($"Average FPS: {summary.AverageFps:0.0}");
            builder.AppendLine($"Average Frame Time: {summary.AverageFrameTimeMs:0.00} ms");
            builder.AppendLine($"Worst Frame Time: {summary.WorstFrameTimeMs:0.00} ms");
            builder.AppendLine($"1% Low FPS Estimate: {summary.OnePercentLowFps:0.0}");
            builder.AppendLine($"0.1% Low FPS Estimate: {summary.ZeroPointOnePercentLowFps:0.0}");
            builder.AppendLine();
            builder.AppendLine("Per-Second Bucket Preview:");

            foreach (FrameSecondMetrics second in summary.PerSecondMetrics.Take(10))
            {
                builder.AppendLine(
                    $"Second {second.Second}: " +
                    $"{second.FrameCount} frames | " +
                    $"Avg FPS {second.AverageFps:0.0} | " +
                    $"Avg FT {second.AverageFrameTimeMs:0.00} ms | " +
                    $"Worst FT {second.WorstFrameTimeMs:0.00} ms"
                );
            }

            if (summary.PerSecondMetrics.Count > 10)
            {
                builder.AppendLine($"... {summary.PerSecondMetrics.Count - 10} more second buckets");
            }

            builder.AppendLine();
            builder.AppendLine($"Source File: {summary.SourceFilePath}");

            return builder.ToString();
        }

        private static List<RawFrameRecord> ReadFrameRecords(string filePath)
        {
            List<RawFrameRecord> records = new();

            using FileStream fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            using StreamReader reader = new StreamReader(fileStream);

            string? headerLine = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(headerLine))
            {
                return records;
            }

            List<string> headers = SplitCsvLine(headerLine);

            int applicationIndex = FindColumnIndex(headers, "Application");
            int timeInMsIndex = FindColumnIndex(headers, "TimeInMs");
            int msBetweenPresentsIndex = FindColumnIndex(headers, "MsBetweenPresents");

            if (applicationIndex < 0 || timeInMsIndex < 0 || msBetweenPresentsIndex < 0)
            {
                return records;
            }

            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                List<string> values = SplitCsvLine(line);

                if (values.Count <= Math.Max(applicationIndex, Math.Max(timeInMsIndex, msBetweenPresentsIndex)))
                {
                    continue;
                }

                string application = values[applicationIndex].Trim();

                if (!TryParseDouble(values[timeInMsIndex], out double timeInMs))
                {
                    continue;
                }

                if (!TryParseDouble(values[msBetweenPresentsIndex], out double msBetweenPresents))
                {
                    continue;
                }

                if (msBetweenPresents <= 0)
                {
                    continue;
                }

                records.Add(new RawFrameRecord
                {
                    Application = application,
                    TimeInMs = timeInMs,
                    MsBetweenPresents = msBetweenPresents
                });
            }

            return records;
        }

        private static List<FrameSecondMetrics> BuildPerSecondMetrics(
            List<RawFrameRecord> records,
            double firstTimeMs)
        {
            return records
                .GroupBy(record => (int)Math.Floor((record.TimeInMs - firstTimeMs) / 1000.0))
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    List<double> frameTimes = group
                        .Select(record => record.MsBetweenPresents)
                        .Where(value => value > 0)
                        .ToList();

                    double averageFrameTime = frameTimes.Count > 0
                        ? frameTimes.Average()
                        : 0;

                    return new FrameSecondMetrics
                    {
                        Second = group.Key,
                        FrameCount = group.Count(),
                        AverageFrameTimeMs = averageFrameTime,
                        AverageFps = CalculateFpsFromFrameTime(averageFrameTime),
                        WorstFrameTimeMs = frameTimes.Count > 0 ? frameTimes.Max() : 0,
                        OnePercentLowFps = CalculateLowFps(frameTimes, 0.01),
                        ZeroPointOnePercentLowFps = CalculateLowFps(frameTimes, 0.001)
                    };
                })
                .ToList();
        }

        private static double CalculateFpsFromFrameTime(double frameTimeMs)
        {
            if (frameTimeMs <= 0)
            {
                return 0;
            }

            return 1000.0 / frameTimeMs;
        }

        private static double CalculateLowFps(List<double> frameTimesMs, double slowestFraction)
        {
            if (frameTimesMs.Count == 0)
            {
                return 0;
            }

            int count = Math.Max(1, (int)Math.Ceiling(frameTimesMs.Count * slowestFraction));

            List<double> slowestFrameTimes = frameTimesMs
                .OrderByDescending(value => value)
                .Take(count)
                .ToList();

            double averageSlowFrameTime = slowestFrameTimes.Average();

            return CalculateFpsFromFrameTime(averageSlowFrameTime);
        }

        private static int FindColumnIndex(List<string> headers, string columnName)
        {
            return headers.FindIndex(header =>
                string.Equals(header.Trim(), columnName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result
            );
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> values = new();
            StringBuilder current = new();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char character = line[i];

                if (character == '"')
                {
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (character == ',' && !insideQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            values.Add(current.ToString());

            return values;
        }
    }
}