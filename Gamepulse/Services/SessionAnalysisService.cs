using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class SessionAnalysisService
    {
        private const double FpsDropThresholdMultiplier = 0.70;
        private const double WarningStutterFrameTimeMs = 50.0;
        private const double SevereStutterFrameTimeMs = 100.0;

        private const double HighGpuUsagePercent = 90.0;
        private const double HighCpuUsagePercent = 85.0;
        private const double HighGameCpuUsagePercent = 70.0;
        private const double HighRamUsagePercent = 90.0;
        private const double HighDiskActivePercent = 80.0;
        private const double HighDiskReadWriteMBps = 100.0;

        private const int EventWindowSeconds = 3;
        private const int MaxIssuesToReport = 12;

        public SessionAnalysisResult Analyze(List<TelemetrySample> samples)
        {
            SessionAnalysisResult result = new();

            if (samples.Count == 0)
            {
                result.OverallSummary = "No telemetry samples were available for analysis.";
                return result;
            }

            List<TelemetrySample> frameSamples = samples
                .Where(sample => sample.Fps.HasValue && sample.Fps.Value > 0)
                .OrderBy(sample => sample.ElapsedSeconds)
                .ToList();

            result.HasFrameData = frameSamples.Count > 0;

            FillSessionBaselines(result, samples, frameSamples);

            if (!result.HasFrameData)
            {
                result.OverallSummary =
                    "System telemetry was captured, but no valid frame data was available. " +
                    "GamePulse can still summarize CPU, RAM, disk, GPU, and process activity, " +
                    "but FPS drop analysis requires PresentMon frame data.";

                result.OverallBottleneck = DetectOverallSystemPressure(result);
                result.Recommendations = BuildOverallRecommendations(result);

                return result;
            }

            List<PerformanceIssue> detectedIssues = new();

            detectedIssues.AddRange(DetectFpsDrops(samples, frameSamples, result));
            detectedIssues.AddRange(DetectStutters(samples, frameSamples));

            result.Issues = MergeAndRankIssues(detectedIssues)
                .Take(MaxIssuesToReport)
                .ToList();

            result.OverallBottleneck = DetectOverallBottleneck(result.Issues, result);
            result.Recommendations = BuildOverallRecommendations(result);

            result.OverallSummary = BuildOverallSummary(result);

            return result;
        }

        public string FormatAnalysis(SessionAnalysisResult result)
        {
            StringBuilder builder = new();

            builder.AppendLine("Performance Analysis");
            builder.AppendLine();

            if (!result.HasFrameData)
            {
                builder.AppendLine(result.OverallSummary);
                builder.AppendLine();
                builder.AppendLine($"Overall Bottleneck: {result.OverallBottleneck}");
                builder.AppendLine();

                AppendRecommendations(builder, result.Recommendations);

                return builder.ToString();
            }

            builder.AppendLine($"Overall Bottleneck: {result.OverallBottleneck}");
            builder.AppendLine(result.OverallSummary);
            builder.AppendLine();

            builder.AppendLine("Baseline:");
            builder.AppendLine($"Average FPS: {result.AverageFps:0.0}");
            builder.AppendLine($"Median FPS: {result.MedianFps:0.0}");
            builder.AppendLine($"Lowest FPS: {result.LowestFps:0.0}");
            builder.AppendLine($"Average CPU: {result.AverageCpuPercent:0.0}%");
            builder.AppendLine($"Peak CPU: {result.PeakCpuPercent:0.0}%");
            builder.AppendLine($"Average RAM: {result.AverageRamPercent:0.0}%");
            builder.AppendLine($"Peak RAM: {result.PeakRamPercent:0.0}%");
            builder.AppendLine($"Average GPU 0: {result.AverageGpu0UsagePercent:0.0}%");
            builder.AppendLine($"Peak GPU 0: {result.PeakGpu0UsagePercent:0.0}%");
            builder.AppendLine($"Average GPU 1: {result.AverageGpu1UsagePercent:0.0}%");
            builder.AppendLine($"Peak GPU 1: {result.PeakGpu1UsagePercent:0.0}%");
            builder.AppendLine($"Average Disk Active: {result.AverageDiskActivePercent:0.0}%");
            builder.AppendLine($"Peak Disk Active: {result.PeakDiskActivePercent:0.0}%");
            builder.AppendLine($"Total Stutters: {result.TotalStutters}");
            builder.AppendLine();

            if (result.Issues.Count == 0)
            {
                builder.AppendLine("Detected Issues:");
                builder.AppendLine("No major FPS drops or stutters were detected from the current rules.");
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine("Detected Issues:");

                foreach (PerformanceIssue issue in result.Issues)
                {
                    builder.AppendLine(
                        $"- Second {issue.StartSecond}: {issue.IssueType} [{issue.Severity}]"
                    );

                    builder.AppendLine($"  Likely Cause: {issue.LikelyCause}");
                    builder.AppendLine($"  Confidence: {issue.Confidence}");
                    builder.AppendLine($"  Evidence: {issue.Evidence}");
                    builder.AppendLine($"  Recommendation: {issue.Recommendation}");
                    builder.AppendLine();
                }
            }

            AppendRecommendations(builder, result.Recommendations);

            return builder.ToString();
        }

        private static void FillSessionBaselines(
            SessionAnalysisResult result,
            List<TelemetrySample> samples,
            List<TelemetrySample> frameSamples)
        {
            result.AverageCpuPercent = Average(samples.Select(sample => sample.CpuPercent));
            result.PeakCpuPercent = Max(samples.Select(sample => sample.CpuPercent));

            result.AverageRamPercent = Average(samples.Select(sample => sample.RamPercent));
            result.PeakRamPercent = Max(samples.Select(sample => sample.RamPercent));

            result.AverageGpu0UsagePercent = AverageNullable(samples.Select(sample => sample.Gpu0UsagePercent));
            result.PeakGpu0UsagePercent = MaxNullable(samples.Select(sample => sample.Gpu0UsagePercent));

            result.AverageGpu1UsagePercent = AverageNullable(samples.Select(sample => sample.Gpu1UsagePercent));
            result.PeakGpu1UsagePercent = MaxNullable(samples.Select(sample => sample.Gpu1UsagePercent));

            result.AverageDiskActivePercent = AverageNullable(samples.Select(sample => sample.DiskActivePercent));
            result.PeakDiskActivePercent = MaxNullable(samples.Select(sample => sample.DiskActivePercent));

            result.TotalStutters = samples
                .Where(sample => sample.StutterCount.HasValue)
                .Sum(sample => sample.StutterCount!.Value);

            if (frameSamples.Count == 0)
            {
                return;
            }

            List<double> fpsValues = frameSamples
                .Select(sample => sample.Fps!.Value)
                .Where(value => value > 0)
                .OrderBy(value => value)
                .ToList();

            result.AverageFps = fpsValues.Average();
            result.MedianFps = Median(fpsValues);
            result.LowestFps = fpsValues.Min();
        }

        private static List<PerformanceIssue> DetectFpsDrops(
            List<TelemetrySample> allSamples,
            List<TelemetrySample> frameSamples,
            SessionAnalysisResult result)
        {
            List<PerformanceIssue> issues = new();

            double baselineFps = result.MedianFps > 0
                ? result.MedianFps
                : result.AverageFps;

            if (baselineFps <= 0)
            {
                return issues;
            }

            double dropThreshold = baselineFps * FpsDropThresholdMultiplier;

            List<TelemetrySample> dropSamples = frameSamples
                .Where(sample => sample.Fps.HasValue && sample.Fps.Value < dropThreshold)
                .OrderBy(sample => sample.ElapsedSeconds)
                .ToList();

            foreach (TelemetrySample sample in dropSamples)
            {
                List<TelemetrySample> window = GetWindowSamples(
                    allSamples,
                    sample.ElapsedSeconds,
                    EventWindowSeconds
                );

                issues.Add(BuildIssueFromWindow(
                    issueType: "FPS Drop",
                    eventSample: sample,
                    window: window,
                    baselineFps: baselineFps
                ));
            }

            return issues;
        }

        private static List<PerformanceIssue> DetectStutters(
            List<TelemetrySample> allSamples,
            List<TelemetrySample> frameSamples)
        {
            List<PerformanceIssue> issues = new();

            List<TelemetrySample> stutterSamples = frameSamples
                .Where(sample =>
                    sample.WorstFrameTimeMs.HasValue &&
                    sample.WorstFrameTimeMs.Value >= WarningStutterFrameTimeMs)
                .OrderByDescending(sample => sample.WorstFrameTimeMs!.Value)
                .ToList();

            foreach (TelemetrySample sample in stutterSamples)
            {
                List<TelemetrySample> window = GetWindowSamples(
                    allSamples,
                    sample.ElapsedSeconds,
                    EventWindowSeconds
                );

                issues.Add(BuildIssueFromWindow(
                    issueType: sample.WorstFrameTimeMs >= SevereStutterFrameTimeMs
                        ? "Severe Stutter"
                        : "Stutter",
                    eventSample: sample,
                    window: window,
                    baselineFps: null
                ));
            }

            return issues;
        }

        private static PerformanceIssue BuildIssueFromWindow(
            string issueType,
            TelemetrySample eventSample,
            List<TelemetrySample> window,
            double? baselineFps)
        {
            CauseScore gpuCause = ScoreGpuCause(window);
            CauseScore cpuCause = ScoreCpuCause(window);
            CauseScore ramCause = ScoreRamCause(window);
            CauseScore diskCause = ScoreDiskCause(window);
            CauseScore backgroundCause = ScoreBackgroundCause(window, eventSample);

            List<CauseScore> causeScores = new()
            {
                gpuCause,
                cpuCause,
                ramCause,
                diskCause,
                backgroundCause
            };

            CauseScore bestCause = causeScores
                .OrderByDescending(score => score.Score)
                .First();

            string severity = DetermineSeverity(issueType, eventSample, baselineFps);
            string evidence = BuildEvidence(eventSample, window, bestCause);
            string recommendation = BuildRecommendation(bestCause.Cause);

            return new PerformanceIssue
            {
                StartSecond = eventSample.ElapsedSeconds,
                EndSecond = eventSample.ElapsedSeconds,
                IssueType = issueType,
                Severity = severity,
                LikelyCause = bestCause.Cause,
                Confidence = bestCause.Confidence,

                Fps = eventSample.Fps,
                AverageFrameTimeMs = eventSample.AverageFrameTimeMs,
                WorstFrameTimeMs = eventSample.WorstFrameTimeMs,

                CpuPercent = eventSample.CpuPercent,
                GameCpuPercent = eventSample.GameCpuPercent,
                RamPercent = eventSample.RamPercent,
                DiskActivePercent = eventSample.DiskActivePercent,
                DiskReadMBps = eventSample.DiskReadMBps,
                DiskWriteMBps = eventSample.DiskWriteMBps,
                Gpu0UsagePercent = eventSample.Gpu0UsagePercent,
                Gpu1UsagePercent = eventSample.Gpu1UsagePercent,

                TopCpuProcesses = eventSample.TopCpuProcesses,
                TopRamProcesses = eventSample.TopRamProcesses,

                Evidence = evidence,
                Recommendation = recommendation
            };
        }

        private static List<PerformanceIssue> MergeAndRankIssues(List<PerformanceIssue> issues)
        {
            List<PerformanceIssue> mergedIssues = new();

            foreach (PerformanceIssue issue in issues
                         .OrderBy(issue => issue.StartSecond)
                         .ThenBy(issue => issue.IssueType))
            {
                PerformanceIssue? recentSimilarIssue = mergedIssues
                    .LastOrDefault(existingIssue =>
                        existingIssue.IssueType == issue.IssueType &&
                        existingIssue.LikelyCause == issue.LikelyCause &&
                        Math.Abs(existingIssue.EndSecond - issue.StartSecond) <= 2);

                if (recentSimilarIssue == null)
                {
                    mergedIssues.Add(issue);
                    continue;
                }

                recentSimilarIssue.EndSecond = issue.StartSecond;

                if (IsIssueMoreSevere(issue, recentSimilarIssue))
                {
                    recentSimilarIssue.Severity = issue.Severity;
                    recentSimilarIssue.Fps = issue.Fps;
                    recentSimilarIssue.AverageFrameTimeMs = issue.AverageFrameTimeMs;
                    recentSimilarIssue.WorstFrameTimeMs = issue.WorstFrameTimeMs;
                    recentSimilarIssue.CpuPercent = issue.CpuPercent;
                    recentSimilarIssue.GameCpuPercent = issue.GameCpuPercent;
                    recentSimilarIssue.RamPercent = issue.RamPercent;
                    recentSimilarIssue.DiskActivePercent = issue.DiskActivePercent;
                    recentSimilarIssue.DiskReadMBps = issue.DiskReadMBps;
                    recentSimilarIssue.DiskWriteMBps = issue.DiskWriteMBps;
                    recentSimilarIssue.Gpu0UsagePercent = issue.Gpu0UsagePercent;
                    recentSimilarIssue.Gpu1UsagePercent = issue.Gpu1UsagePercent;
                    recentSimilarIssue.TopCpuProcesses = issue.TopCpuProcesses;
                    recentSimilarIssue.TopRamProcesses = issue.TopRamProcesses;
                    recentSimilarIssue.Evidence = issue.Evidence;
                    recentSimilarIssue.Recommendation = issue.Recommendation;
                }
            }

            return mergedIssues
                .OrderByDescending(GetIssueRank)
                .ThenBy(issue => issue.StartSecond)
                .ToList();
        }

        private static bool IsIssueMoreSevere(PerformanceIssue candidate, PerformanceIssue current)
        {
            int candidateRank = GetIssueRank(candidate);
            int currentRank = GetIssueRank(current);

            if (candidateRank != currentRank)
            {
                return candidateRank > currentRank;
            }

            double candidateWorstFrameTime = candidate.WorstFrameTimeMs ?? 0;
            double currentWorstFrameTime = current.WorstFrameTimeMs ?? 0;

            if (candidateWorstFrameTime != currentWorstFrameTime)
            {
                return candidateWorstFrameTime > currentWorstFrameTime;
            }

            double candidateFps = candidate.Fps ?? double.MaxValue;
            double currentFps = current.Fps ?? double.MaxValue;

            return candidateFps < currentFps;
        }

        private static int GetIssueRank(PerformanceIssue issue)
        {
            int rank = 0;

            if (issue.Severity == "Severe")
            {
                rank += 100;
            }
            else if (issue.Severity == "Warning")
            {
                rank += 50;
            }

            if (issue.IssueType.Contains("Stutter", StringComparison.OrdinalIgnoreCase))
            {
                rank += 20;
            }

            if (issue.IssueType.Contains("FPS", StringComparison.OrdinalIgnoreCase))
            {
                rank += 10;
            }

            if (issue.Confidence == "High")
            {
                rank += 5;
            }
            else if (issue.Confidence == "Medium")
            {
                rank += 3;
            }

            return rank;
        }

        private static string DetermineSeverity(
            string issueType,
            TelemetrySample eventSample,
            double? baselineFps)
        {
            if (eventSample.WorstFrameTimeMs.HasValue &&
                eventSample.WorstFrameTimeMs.Value >= SevereStutterFrameTimeMs)
            {
                return "Severe";
            }

            if (issueType.Contains("Stutter", StringComparison.OrdinalIgnoreCase))
            {
                return "Warning";
            }

            if (baselineFps.HasValue &&
                baselineFps.Value > 0 &&
                eventSample.Fps.HasValue)
            {
                double ratio = eventSample.Fps.Value / baselineFps.Value;

                if (ratio <= 0.40)
                {
                    return "Severe";
                }

                return "Warning";
            }

            return "Warning";
        }

        private static List<TelemetrySample> GetWindowSamples(
            List<TelemetrySample> allSamples,
            int centerSecond,
            int windowSeconds)
        {
            int startSecond = centerSecond - windowSeconds;
            int endSecond = centerSecond + windowSeconds;

            return allSamples
                .Where(sample =>
                    sample.ElapsedSeconds >= startSecond &&
                    sample.ElapsedSeconds <= endSecond)
                .OrderBy(sample => sample.ElapsedSeconds)
                .ToList();
        }

        private static CauseScore ScoreGpuCause(List<TelemetrySample> window)
        {
            double peakGpu1 = MaxNullable(window.Select(sample => sample.Gpu1UsagePercent));
            double peakGpu0 = MaxNullable(window.Select(sample => sample.Gpu0UsagePercent));
            double peakGpu = Math.Max(peakGpu0, peakGpu1);

            int score = 0;

            if (peakGpu >= 98)
            {
                score += 5;
            }
            else if (peakGpu >= HighGpuUsagePercent)
            {
                score += 4;
            }
            else if (peakGpu >= 80)
            {
                score += 2;
            }

            return new CauseScore(
                "GPU-bound",
                score,
                score >= 4 ? "High" : score >= 2 ? "Medium" : "Low"
            );
        }

        private static CauseScore ScoreCpuCause(List<TelemetrySample> window)
        {
            double peakCpu = Max(window.Select(sample => sample.CpuPercent));
            double peakGameCpu = MaxNullable(window.Select(sample => sample.GameCpuPercent));

            int score = 0;

            if (peakCpu >= 95)
            {
                score += 5;
            }
            else if (peakCpu >= HighCpuUsagePercent)
            {
                score += 4;
            }
            else if (peakCpu >= 75)
            {
                score += 2;
            }

            if (peakGameCpu >= HighGameCpuUsagePercent)
            {
                score += 3;
            }
            else if (peakGameCpu >= 50)
            {
                score += 1;
            }

            return new CauseScore(
                "CPU-bound",
                score,
                score >= 5 ? "High" : score >= 3 ? "Medium" : "Low"
            );
        }

        private static CauseScore ScoreRamCause(List<TelemetrySample> window)
        {
            double peakRam = Max(window.Select(sample => sample.RamPercent));

            int score = 0;

            if (peakRam >= 95)
            {
                score += 5;
            }
            else if (peakRam >= HighRamUsagePercent)
            {
                score += 4;
            }
            else if (peakRam >= 85)
            {
                score += 2;
            }

            return new CauseScore(
                "Memory pressure",
                score,
                score >= 4 ? "High" : score >= 2 ? "Medium" : "Low"
            );
        }

        private static CauseScore ScoreDiskCause(List<TelemetrySample> window)
        {
            double peakDiskActive = MaxNullable(window.Select(sample => sample.DiskActivePercent));
            double peakRead = MaxNullable(window.Select(sample => sample.DiskReadMBps));
            double peakWrite = MaxNullable(window.Select(sample => sample.DiskWriteMBps));

            int score = 0;

            if (peakDiskActive >= HighDiskActivePercent)
            {
                score += 4;
            }
            else if (peakDiskActive >= 60)
            {
                score += 2;
            }

            if (peakRead >= HighDiskReadWriteMBps || peakWrite >= HighDiskReadWriteMBps)
            {
                score += 3;
            }
            else if (peakRead >= 50 || peakWrite >= 50)
            {
                score += 1;
            }

            return new CauseScore(
                "Disk or asset-loading spike",
                score,
                score >= 5 ? "High" : score >= 3 ? "Medium" : "Low"
            );
        }

        private static CauseScore ScoreBackgroundCause(
            List<TelemetrySample> window,
            TelemetrySample eventSample)
        {
            string activeProcess = eventSample.ActiveGameProcessName ?? "";

            int score = 0;

            foreach (TelemetrySample sample in window)
            {
                if (ContainsNonGameTopProcess(sample.TopCpuProcesses, activeProcess))
                {
                    score += 2;
                    break;
                }
            }

            foreach (TelemetrySample sample in window)
            {
                if (ContainsNonGameTopProcess(sample.TopRamProcesses, activeProcess))
                {
                    score += 1;
                    break;
                }
            }

            return new CauseScore(
                "Background process impact",
                score,
                score >= 3 ? "Medium" : score >= 1 ? "Low" : "Low"
            );
        }

        private static bool ContainsNonGameTopProcess(string processList, string activeProcess)
        {
            if (string.IsNullOrWhiteSpace(processList))
            {
                return false;
            }

            string firstProcess = processList.Split('|')[0].Trim();

            if (string.IsNullOrWhiteSpace(firstProcess))
            {
                return false;
            }

            string firstProcessName = firstProcess.Split(':')[0].Trim();

            if (string.IsNullOrWhiteSpace(firstProcessName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(activeProcess))
            {
                return true;
            }

            string normalizedActive = RemoveExe(activeProcess);
            string normalizedTop = RemoveExe(firstProcessName);

            return !string.Equals(
                normalizedActive,
                normalizedTop,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string RemoveExe(string processName)
        {
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return processName[..^4];
            }

            return processName;
        }

        private static string BuildEvidence(
            TelemetrySample eventSample,
            List<TelemetrySample> window,
            CauseScore bestCause)
        {
            double peakGpu1 = MaxNullable(window.Select(sample => sample.Gpu1UsagePercent));
            double peakGpu0 = MaxNullable(window.Select(sample => sample.Gpu0UsagePercent));
            double peakCpu = Max(window.Select(sample => sample.CpuPercent));
            double peakGameCpu = MaxNullable(window.Select(sample => sample.GameCpuPercent));
            double peakRam = Max(window.Select(sample => sample.RamPercent));
            double peakDiskActive = MaxNullable(window.Select(sample => sample.DiskActivePercent));
            double peakDiskRead = MaxNullable(window.Select(sample => sample.DiskReadMBps));
            double peakDiskWrite = MaxNullable(window.Select(sample => sample.DiskWriteMBps));

            List<string> evidence = new();

            if (eventSample.Fps.HasValue)
            {
                evidence.Add($"FPS {eventSample.Fps.Value:0.0}");
            }

            if (eventSample.AverageFrameTimeMs.HasValue)
            {
                evidence.Add($"avg frame time {eventSample.AverageFrameTimeMs.Value:0.00} ms");
            }

            if (eventSample.WorstFrameTimeMs.HasValue)
            {
                evidence.Add($"worst frame time {eventSample.WorstFrameTimeMs.Value:0.00} ms");
            }

            evidence.Add($"peak CPU {peakCpu:0.0}%");
            evidence.Add($"peak game CPU {peakGameCpu:0.0}%");
            evidence.Add($"peak RAM {peakRam:0.0}%");
            evidence.Add($"peak GPU 0 {peakGpu0:0.0}%");
            evidence.Add($"peak GPU 1 {peakGpu1:0.0}%");
            evidence.Add($"peak disk active {peakDiskActive:0.0}%");
            evidence.Add($"peak disk read {peakDiskRead:0.0} MB/s");
            evidence.Add($"peak disk write {peakDiskWrite:0.0} MB/s");

            if (!string.IsNullOrWhiteSpace(eventSample.TopCpuProcesses))
            {
                evidence.Add($"top CPU process: {eventSample.TopCpuProcesses.Split('|')[0].Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(eventSample.TopRamProcesses))
            {
                evidence.Add($"top RAM process: {eventSample.TopRamProcesses.Split('|')[0].Trim()}");
            }

            evidence.Add($"highest scored cause: {bestCause.Cause}");

            return string.Join("; ", evidence);
        }

        private static string BuildRecommendation(string cause)
        {
            return cause switch
            {
                "GPU-bound" =>
                    "Lower GPU-heavy settings such as shadows, effects, anti-aliasing, render scale, or texture quality if VRAM is high.",

                "CPU-bound" =>
                    "Close CPU-heavy background apps and lower CPU-heavy settings such as simulation detail, view distance, crowd density, or physics-heavy options.",

                "Memory pressure" =>
                    "Close memory-heavy applications and lower texture quality or background app usage if RAM pressure remains high.",

                "Disk or asset-loading spike" =>
                    "Check for game asset loading, downloads, antivirus scans, or slow storage. Moving the game to a faster SSD may reduce similar stutters.",

                "Background process impact" =>
                    "Close or limit background applications that appear in top CPU/RAM lists during drops or stutters.",

                _ =>
                    "Review the surrounding telemetry and reduce the most active resource category during the affected seconds."
            };
        }

        private static string DetectOverallBottleneck(
            List<PerformanceIssue> issues,
            SessionAnalysisResult result)
        {
            if (issues.Count > 0)
            {
                return issues
                    .GroupBy(issue => issue.LikelyCause)
                    .OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Count(issue => issue.Confidence == "High"))
                    .Select(group => group.Key)
                    .First();
            }

            return DetectOverallSystemPressure(result);
        }

        private static string DetectOverallSystemPressure(SessionAnalysisResult result)
        {
            if (result.PeakGpu1UsagePercent >= HighGpuUsagePercent ||
                result.PeakGpu0UsagePercent >= HighGpuUsagePercent)
            {
                return "GPU-bound";
            }

            if (result.PeakCpuPercent >= HighCpuUsagePercent)
            {
                return "CPU-bound";
            }

            if (result.PeakRamPercent >= HighRamUsagePercent)
            {
                return "Memory pressure";
            }

            if (result.PeakDiskActivePercent >= HighDiskActivePercent)
            {
                return "Disk or asset-loading spike";
            }

            return "No major bottleneck detected";
        }

        private static List<string> BuildOverallRecommendations(SessionAnalysisResult result)
        {
            List<string> recommendations = new();

            if (result.Issues.Any(issue => issue.LikelyCause == "GPU-bound") ||
                result.OverallBottleneck == "GPU-bound")
            {
                recommendations.Add("Lower GPU-heavy settings such as shadows, effects, anti-aliasing, render scale, or texture quality.");
            }

            if (result.Issues.Any(issue => issue.LikelyCause == "CPU-bound") ||
                result.OverallBottleneck == "CPU-bound")
            {
                recommendations.Add("Close CPU-heavy background apps and reduce CPU-heavy game settings such as view distance, simulation detail, or physics-heavy options.");
            }

            if (result.Issues.Any(issue => issue.LikelyCause == "Memory pressure") ||
                result.OverallBottleneck == "Memory pressure")
            {
                recommendations.Add("Close memory-heavy apps and lower texture quality if RAM usage stays high.");
            }

            if (result.Issues.Any(issue => issue.LikelyCause == "Disk or asset-loading spike") ||
                result.OverallBottleneck == "Disk or asset-loading spike")
            {
                recommendations.Add("Check for storage spikes, downloads, antivirus scans, or asset loading. A faster SSD may reduce disk-related stutters.");
            }

            if (result.Issues.Any(issue => issue.LikelyCause == "Background process impact"))
            {
                recommendations.Add("Close or limit background apps that appear in top CPU/RAM process lists during frame drops.");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("No major bottleneck was detected. Keep monitoring longer sessions to capture rare frame drops or stutters.");
            }

            return recommendations.Distinct().ToList();
        }

        private static string BuildOverallSummary(SessionAnalysisResult result)
        {
            if (result.Issues.Count == 0)
            {
                return
                    "No major FPS drops or stutters were detected by the current rules. " +
                    "The session appears stable based on the captured frame and system telemetry.";
            }

            int fpsDropCount = result.Issues.Count(issue =>
                issue.IssueType.Contains("FPS", StringComparison.OrdinalIgnoreCase));

            int stutterCount = result.Issues.Count(issue =>
                issue.IssueType.Contains("Stutter", StringComparison.OrdinalIgnoreCase));

            return
                $"Detected {result.Issues.Count} notable performance issue(s), including " +
                $"{fpsDropCount} FPS drop event(s) and {stutterCount} stutter event(s). " +
                $"The most common likely cause was {result.OverallBottleneck}.";
        }

        private static void AppendRecommendations(StringBuilder builder, List<string> recommendations)
        {
            builder.AppendLine("Recommendations:");

            foreach (string recommendation in recommendations)
            {
                builder.AppendLine($"- {recommendation}");
            }
        }

        private static double Average(IEnumerable<double> values)
        {
            List<double> validValues = values.ToList();

            if (validValues.Count == 0)
            {
                return 0;
            }

            return validValues.Average();
        }

        private static double Max(IEnumerable<double> values)
        {
            List<double> validValues = values.ToList();

            if (validValues.Count == 0)
            {
                return 0;
            }

            return validValues.Max();
        }

        private static double AverageNullable(IEnumerable<double?> values)
        {
            List<double> validValues = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            if (validValues.Count == 0)
            {
                return 0;
            }

            return validValues.Average();
        }

        private static double MaxNullable(IEnumerable<double?> values)
        {
            List<double> validValues = values
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            if (validValues.Count == 0)
            {
                return 0;
            }

            return validValues.Max();
        }

        private static double Median(List<double> sortedValues)
        {
            if (sortedValues.Count == 0)
            {
                return 0;
            }

            int middle = sortedValues.Count / 2;

            if (sortedValues.Count % 2 == 1)
            {
                return sortedValues[middle];
            }

            return (sortedValues[middle - 1] + sortedValues[middle]) / 2.0;
        }

        private class CauseScore
        {
            public string Cause { get; }
            public int Score { get; }
            public string Confidence { get; }

            public CauseScore(string cause, int score, string confidence)
            {
                Cause = cause;
                Score = score;
                Confidence = confidence;
            }
        }
    }
}