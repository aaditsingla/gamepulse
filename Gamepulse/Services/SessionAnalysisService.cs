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

        private const double FrameTimeSpikeAbsoluteMs = 50.0;
        private const double SevereHitchFrameTimeMs = 100.0;
        private const double FrameTimeSpikeBaselineMultiplier = 4.0;
        private const double StutterAverageFrameTimeMultiplier = 1.5;
        private const double SevereHitchAverageFrameTimeMultiplier = 1.8;

        private const double CaptureGapFpsThreshold = 5.0;
        private const double CaptureGapAverageFrameTimeMs = 500.0;
        private const double CaptureGapWorstFrameTimeMs = 500.0;

        private const double HighGpuUsagePercent = 90.0;
        private const double VeryHighGpuUsagePercent = 97.0;

        private const double HighCpuUsagePercent = 85.0;
        private const double VeryHighCpuUsagePercent = 95.0;

        private const double HighGameCpuUsagePercent = 70.0;
        private const double VeryHighGameCpuUsagePercent = 85.0;

        private const double HighRamUsagePercent = 90.0;
        private const double VeryHighRamUsagePercent = 95.0;

        private const double HighDiskActivePercent = 80.0;
        private const double MediumDiskActivePercent = 60.0;

        private const double HighDiskReadWriteMBps = 100.0;
        private const double MediumDiskReadWriteMBps = 50.0;

        private const int EventWindowSeconds = 3;
        private const int IgnoreStartSeconds = 3;
        private const int IgnoreEndSeconds = 2;

        private const int MaxMajorIssuesToReport = 6;
        private const int MaxMinorIssuesToReport = 6;

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
                    "GamePulse can summarize CPU, RAM, disk, GPU, and process activity, " +
                    "but FPS drop and frame-time analysis requires PresentMon frame data.";

                result.OverallBottleneck = DetectOverallSystemPressure(result);
                result.Recommendations = BuildOverallRecommendations(result);

                return result;
            }

            List<TelemetrySample> analysisFrameSamples = RemoveStartAndEndNoise(frameSamples);

            result.CaptureGapCount = CountCaptureGaps(analysisFrameSamples);

            if (result.CaptureGapCount > 0)
            {
                result.TechnicalNotes.Add(
                    $"{result.CaptureGapCount} capture gap/no-render sample(s) were detected and excluded from normal bottleneck classification. " +
                    "These usually happen during alt-tab, loading screens, app focus changes, or when the game stops presenting frames."
                );
            }

            List<TelemetrySample> usableFrameSamples = analysisFrameSamples
                .Where(sample => !IsCaptureGap(sample))
                .ToList();

            List<PerformanceIssue> detectedMajorIssues = new();
            List<PerformanceIssue> detectedMinorIssues = new();

            detectedMajorIssues.AddRange(DetectFpsDrops(samples, usableFrameSamples, result));

            List<PerformanceIssue> frameTimeIssues = DetectFrameTimeProblems(samples, usableFrameSamples, result);

            detectedMajorIssues.AddRange(frameTimeIssues.Where(issue => issue.Severity != "Info"));
            detectedMinorIssues.AddRange(frameTimeIssues.Where(issue => issue.Severity == "Info"));

            result.Issues = MergeAndRankIssues(detectedMajorIssues)
                .Take(MaxMajorIssuesToReport)
                .ToList();

            result.MinorIssues = MergeAndRankIssues(detectedMinorIssues)
                .Take(MaxMinorIssuesToReport)
                .ToList();

            result.MajorIssueCount = result.Issues.Count;
            result.MinorFrameTimeSpikeCount = detectedMinorIssues.Count;

            result.OverallBottleneck = DetectOverallBottleneck(result.Issues, result);
            result.Recommendations = BuildOverallRecommendations(result);
            result.OverallSummary = BuildOverallSummary(result);

            AddSessionCautionNotes(result);

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

            double baselineFrameTimeMs = GetBaselineFrameTimeMs(result);
            builder.AppendLine($"Baseline Frame Time: {baselineFrameTimeMs:0.00} ms");

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
            builder.AppendLine($"Total Stutter Frames: {result.TotalStutters}");
            builder.AppendLine();

            if (result.Issues.Count == 0)
            {
                builder.AppendLine("Major Issues:");
                builder.AppendLine("No major FPS drops, stutters, or gameplay-impacting severe hitches were detected by the current rules.");
                builder.AppendLine();
            }
            else
            {
                builder.AppendLine("Major Issues:");

                foreach (PerformanceIssue issue in result.Issues)
                {
                    AppendMajorIssue(builder, issue);
                }
            }

            if (result.MinorIssues.Count > 0)
            {
                builder.AppendLine("Minor / Diagnostic Frame-Time Events:");
                builder.AppendLine(
                    "These are short frame-time spikes where the full-second average stayed mostly normal. " +
                    "They are logged for diagnostics, but they may not be noticeable during gameplay."
                );

                foreach (PerformanceIssue issue in result.MinorIssues)
                {
                    AppendMinorIssue(builder, issue);
                }
            }

            if (result.TechnicalNotes.Count > 0)
            {
                builder.AppendLine("Technical Notes:");

                foreach (string note in result.TechnicalNotes)
                {
                    builder.AppendLine($"- {note}");
                }

                builder.AppendLine();
            }

            AppendRecommendations(builder, result.Recommendations);

            return builder.ToString();
        }

        private static void AppendMajorIssue(StringBuilder builder, PerformanceIssue issue)
        {
            string secondRange = issue.StartSecond == issue.EndSecond
                ? $"Second {issue.StartSecond}"
                : $"Seconds {issue.StartSecond}-{issue.EndSecond}";

            builder.AppendLine($"- {secondRange}: {issue.IssueType} [{issue.Severity}]");
            builder.AppendLine($"  Primary Cause: {issue.LikelyCause}");

            if (!string.IsNullOrWhiteSpace(issue.ContributingFactors))
            {
                builder.AppendLine($"  Contributing Factors: {issue.ContributingFactors}");
            }

            builder.AppendLine($"  Confidence: {issue.Confidence}");
            builder.AppendLine($"  Evidence: {issue.Evidence}");
            builder.AppendLine($"  Recommendation: {issue.Recommendation}");
            builder.AppendLine();
        }

        private static void AppendMinorIssue(StringBuilder builder, PerformanceIssue issue)
        {
            string secondRange = issue.StartSecond == issue.EndSecond
                ? $"Second {issue.StartSecond}"
                : $"Seconds {issue.StartSecond}-{issue.EndSecond}";

            builder.AppendLine($"- {secondRange}: {issue.IssueType} [{issue.Severity}]");
            builder.AppendLine("  Impact: Diagnostic frame-time event. No major bottleneck confirmed from this event alone.");

            if (!string.IsNullOrWhiteSpace(issue.ContributingFactors))
            {
                builder.AppendLine($"  Possible Contributors: {issue.ContributingFactors}");
            }
            else
            {
                builder.AppendLine("  Possible Contributors: Not strong enough to classify.");
            }

            builder.AppendLine($"  Confidence: {issue.Confidence}");
            builder.AppendLine($"  Evidence: {issue.Evidence}");
            builder.AppendLine($"  Note: {issue.Recommendation}");
            builder.AppendLine();
        }

        private static List<TelemetrySample> RemoveStartAndEndNoise(List<TelemetrySample> frameSamples)
        {
            if (frameSamples.Count == 0)
            {
                return frameSamples;
            }

            int firstSecond = frameSamples.First().ElapsedSeconds;
            int lastSecond = frameSamples.Last().ElapsedSeconds;

            return frameSamples
                .Where(sample =>
                    sample.ElapsedSeconds >= firstSecond + IgnoreStartSeconds &&
                    sample.ElapsedSeconds <= lastSecond - IgnoreEndSeconds)
                .ToList();
        }

        private static int CountCaptureGaps(List<TelemetrySample> frameSamples)
        {
            return frameSamples.Count(IsCaptureGap);
        }

        private static bool IsCaptureGap(TelemetrySample sample)
        {
            double fps = sample.Fps ?? 0;
            double averageFrameTimeMs = sample.AverageFrameTimeMs ?? 0;
            double worstFrameTimeMs = sample.WorstFrameTimeMs ?? 0;

            return
                (fps > 0 && fps < CaptureGapFpsThreshold) ||
                averageFrameTimeMs >= CaptureGapAverageFrameTimeMs ||
                worstFrameTimeMs >= CaptureGapWorstFrameTimeMs;
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
                .Where(sample => !IsCaptureGap(sample))
                .Select(sample => sample.Fps!.Value)
                .Where(value => value > 0)
                .OrderBy(value => value)
                .ToList();

            if (fpsValues.Count == 0)
            {
                return;
            }

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

            double baselineFps = GetBaselineFps(result);

            if (baselineFps <= 0)
            {
                return issues;
            }

            double dropThreshold = baselineFps * FpsDropThresholdMultiplier;

            List<TelemetrySample> dropSamples = frameSamples
                .Where(sample =>
                    sample.Fps.HasValue &&
                    sample.Fps.Value >= CaptureGapFpsThreshold &&
                    sample.Fps.Value < dropThreshold)
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
                    result: result
                ));
            }

            return issues;
        }

        private static List<PerformanceIssue> DetectFrameTimeProblems(
            List<TelemetrySample> allSamples,
            List<TelemetrySample> frameSamples,
            SessionAnalysisResult result)
        {
            List<PerformanceIssue> issues = new();

            double baselineFrameTimeMs = GetBaselineFrameTimeMs(result);

            if (baselineFrameTimeMs <= 0)
            {
                return issues;
            }

            foreach (TelemetrySample sample in frameSamples)
            {
                string issueType = ClassifyFrameTimeIssue(sample, result);

                if (string.IsNullOrWhiteSpace(issueType))
                {
                    continue;
                }

                List<TelemetrySample> window = GetWindowSamples(
                    allSamples,
                    sample.ElapsedSeconds,
                    EventWindowSeconds
                );

                issues.Add(BuildIssueFromWindow(
                    issueType: issueType,
                    eventSample: sample,
                    window: window,
                    result: result
                ));
            }

            return issues;
        }

        private static string ClassifyFrameTimeIssue(
            TelemetrySample sample,
            SessionAnalysisResult result)
        {
            if (IsCaptureGap(sample))
            {
                return "";
            }

            if (!sample.WorstFrameTimeMs.HasValue)
            {
                return "";
            }

            double baselineFrameTimeMs = GetBaselineFrameTimeMs(result);

            if (baselineFrameTimeMs <= 0)
            {
                return "";
            }

            double worstFrameTimeMs = sample.WorstFrameTimeMs.Value;
            double averageFrameTimeMs = sample.AverageFrameTimeMs ?? 0;
            double fps = sample.Fps ?? 0;

            double spikeThreshold = Math.Max(
                FrameTimeSpikeAbsoluteMs,
                baselineFrameTimeMs * FrameTimeSpikeBaselineMultiplier
            );

            bool verySlowSingleFrame = worstFrameTimeMs >= SevereHitchFrameTimeMs;

            bool frameTimeSpike =
                worstFrameTimeMs >= spikeThreshold;

            bool averageFramePacingWorse =
                averageFrameTimeMs >= baselineFrameTimeMs * StutterAverageFrameTimeMultiplier;

            bool severeAverageFramePacingWorse =
                averageFrameTimeMs >= baselineFrameTimeMs * SevereHitchAverageFrameTimeMultiplier;

            bool fpsAlsoDropped =
                fps >= CaptureGapFpsThreshold &&
                fps < GetBaselineFps(result) * FpsDropThresholdMultiplier;

            if (verySlowSingleFrame && (severeAverageFramePacingWorse || fpsAlsoDropped))
            {
                return "Severe Hitch";
            }

            if (verySlowSingleFrame)
            {
                return "Severe Frame-Time Spike";
            }

            if (frameTimeSpike && averageFramePacingWorse)
            {
                return "Stutter";
            }

            if (frameTimeSpike && fpsAlsoDropped)
            {
                return "Stutter";
            }

            if (frameTimeSpike)
            {
                return "Frame-Time Spike";
            }

            return "";
        }

        private static PerformanceIssue BuildIssueFromWindow(
            string issueType,
            TelemetrySample eventSample,
            List<TelemetrySample> window,
            SessionAnalysisResult result)
        {
            List<TelemetrySample> usableWindow = window
                .Where(sample => !IsCaptureGap(sample))
                .ToList();

            if (usableWindow.Count == 0)
            {
                usableWindow = window;
            }

            List<CauseScore> causeScores = new()
            {
                ScoreGpuCause(usableWindow),
                ScoreCpuCause(usableWindow),
                ScoreRamCause(usableWindow),
                ScoreDiskCause(usableWindow),
                ScoreBackgroundCause(usableWindow, eventSample)
            };

            bool isMinorInfoIssue =
                issueType == "Frame-Time Spike" ||
                issueType == "Severe Frame-Time Spike";

            CauseScore primaryCause = isMinorInfoIssue
                ? new CauseScore("Not classified for diagnostic spike", 0, "Low", 999)
                : PickPrimaryCause(causeScores);

            List<CauseScore> contributingCauses = PickContributingCauses(
                causeScores,
                primaryCause,
                isMinorInfoIssue
            );

            string severity = DetermineSeverity(issueType, eventSample, result);
            string evidence = BuildEvidence(eventSample, usableWindow, primaryCause, contributingCauses, result);
            string recommendation = BuildRecommendation(primaryCause.Cause, contributingCauses, isMinorInfoIssue);

            return new PerformanceIssue
            {
                StartSecond = eventSample.ElapsedSeconds,
                EndSecond = eventSample.ElapsedSeconds,
                IssueType = issueType,
                Severity = severity,

                LikelyCause = primaryCause.Cause,
                ContributingFactors = string.Join(
                    ", ",
                    contributingCauses.Select(cause => cause.Cause)
                ),
                Confidence = isMinorInfoIssue ? "Low" : primaryCause.Confidence,

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

        private static CauseScore PickPrimaryCause(List<CauseScore> causeScores)
        {
            CauseScore bestCause = causeScores
                .OrderByDescending(score => score.Score)
                .ThenBy(score => score.Priority)
                .First();

            if (bestCause.Score < 4)
            {
                return new CauseScore(
                    "Mixed or unclear",
                    bestCause.Score,
                    "Low",
                    999
                );
            }

            return bestCause;
        }

        private static List<CauseScore> PickContributingCauses(
            List<CauseScore> causeScores,
            CauseScore primaryCause,
            bool isMinorInfoIssue)
        {
            if (!isMinorInfoIssue && primaryCause.Cause == "Mixed or unclear")
            {
                return new List<CauseScore>();
            }

            int threshold = isMinorInfoIssue ? 5 : 4;

            return causeScores
                .Where(score =>
                    score.Cause != primaryCause.Cause &&
                    score.Score >= threshold)
                .OrderByDescending(score => score.Score)
                .Take(2)
                .ToList();
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
                        Math.Abs(existingIssue.EndSecond - issue.StartSecond) <= 3);

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
                    recentSimilarIssue.ContributingFactors = issue.ContributingFactors;
                    recentSimilarIssue.Confidence = issue.Confidence;
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
            else if (issue.Severity == "Info")
            {
                rank += 20;
            }

            if (issue.IssueType.Contains("Severe Hitch", StringComparison.OrdinalIgnoreCase))
            {
                rank += 30;
            }

            if (issue.IssueType.Contains("Stutter", StringComparison.OrdinalIgnoreCase))
            {
                rank += 20;
            }

            if (issue.IssueType.Contains("FPS", StringComparison.OrdinalIgnoreCase))
            {
                rank += 15;
            }

            if (issue.IssueType.Contains("Frame-Time", StringComparison.OrdinalIgnoreCase))
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
            SessionAnalysisResult result)
        {
            if (issueType == "Severe Hitch")
            {
                return "Severe";
            }

            if (issueType == "Stutter")
            {
                return "Warning";
            }

            if (issueType == "Severe Frame-Time Spike")
            {
                return "Info";
            }

            if (issueType == "Frame-Time Spike")
            {
                return "Info";
            }

            if (issueType == "FPS Drop")
            {
                double baselineFps = GetBaselineFps(result);

                if (baselineFps > 0 && eventSample.Fps.HasValue)
                {
                    double ratio = eventSample.Fps.Value / baselineFps;

                    if (ratio <= 0.40)
                    {
                        return "Severe";
                    }
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

            double peakCpu = Max(window.Select(sample => sample.CpuPercent));
            double peakDiskActive = MaxNullable(window.Select(sample => sample.DiskActivePercent));

            int score = 0;

            if (peakGpu >= VeryHighGpuUsagePercent)
            {
                score += 6;
            }
            else if (peakGpu >= HighGpuUsagePercent)
            {
                score += 5;
            }
            else if (peakGpu >= 80)
            {
                score += 2;
            }

            if (peakCpu < 80 && peakGpu >= HighGpuUsagePercent)
            {
                score += 1;
            }

            if (peakDiskActive < MediumDiskActivePercent && peakGpu >= HighGpuUsagePercent)
            {
                score += 1;
            }

            string confidence = score >= 6 ? "High" : score >= 4 ? "Medium" : "Low";

            return new CauseScore("GPU-bound", score, confidence, 1);
        }

        private static CauseScore ScoreCpuCause(List<TelemetrySample> window)
        {
            double peakCpu = Max(window.Select(sample => sample.CpuPercent));
            double peakGameCpu = MaxNullable(window.Select(sample => sample.GameCpuPercent));
            double peakGpu = Math.Max(
                MaxNullable(window.Select(sample => sample.Gpu0UsagePercent)),
                MaxNullable(window.Select(sample => sample.Gpu1UsagePercent))
            );

            int score = 0;

            if (peakCpu >= VeryHighCpuUsagePercent)
            {
                score += 6;
            }
            else if (peakCpu >= HighCpuUsagePercent)
            {
                score += 5;
            }
            else if (peakCpu >= 75)
            {
                score += 2;
            }

            if (peakGameCpu >= VeryHighGameCpuUsagePercent)
            {
                score += 4;
            }
            else if (peakGameCpu >= HighGameCpuUsagePercent)
            {
                score += 3;
            }
            else if (peakGameCpu >= 50)
            {
                score += 1;
            }

            if (peakGpu < 85 && peakCpu >= HighCpuUsagePercent)
            {
                score += 1;
            }

            string confidence = score >= 7 ? "High" : score >= 4 ? "Medium" : "Low";

            return new CauseScore("CPU-bound", score, confidence, 2);
        }

        private static CauseScore ScoreRamCause(List<TelemetrySample> window)
        {
            double peakRam = Max(window.Select(sample => sample.RamPercent));
            double peakGameRamMb = MaxNullable(window.Select(sample => sample.GameRamMb));

            int score = 0;

            if (peakRam >= VeryHighRamUsagePercent)
            {
                score += 6;
            }
            else if (peakRam >= HighRamUsagePercent)
            {
                score += 5;
            }
            else if (peakRam >= 88)
            {
                score += 3;
            }

            if (peakGameRamMb >= 8000)
            {
                score += 2;
            }
            else if (peakGameRamMb >= 5000)
            {
                score += 1;
            }

            string confidence = score >= 6 ? "High" : score >= 4 ? "Medium" : "Low";

            return new CauseScore("Memory pressure", score, confidence, 3);
        }

        private static CauseScore ScoreDiskCause(List<TelemetrySample> window)
        {
            double peakDiskActive = MaxNullable(window.Select(sample => sample.DiskActivePercent));
            double peakRead = MaxNullable(window.Select(sample => sample.DiskReadMBps));
            double peakWrite = MaxNullable(window.Select(sample => sample.DiskWriteMBps));

            int score = 0;

            bool highDiskActivity = peakDiskActive >= HighDiskActivePercent;
            bool mediumDiskActivity = peakDiskActive >= MediumDiskActivePercent;
            bool highReadWrite = peakRead >= HighDiskReadWriteMBps || peakWrite >= HighDiskReadWriteMBps;
            bool mediumReadWrite = peakRead >= MediumDiskReadWriteMBps || peakWrite >= MediumDiskReadWriteMBps;

            if (highDiskActivity && highReadWrite)
            {
                score += 7;
            }
            else if (highDiskActivity && mediumReadWrite)
            {
                score += 5;
            }
            else if (mediumDiskActivity && mediumReadWrite)
            {
                score += 4;
            }
            else if (highDiskActivity)
            {
                score += 2;
            }

            string confidence = score >= 6 ? "High" : score >= 4 ? "Medium" : "Low";

            return new CauseScore("Disk or asset-loading spike", score, confidence, 4);
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
                    score += 3;
                    break;
                }
            }

            foreach (TelemetrySample sample in window)
            {
                if (ContainsNonGameTopProcess(sample.TopRamProcesses, activeProcess))
                {
                    score += 2;
                    break;
                }
            }

            string confidence = score >= 5 ? "Medium" : score >= 3 ? "Low" : "Low";

            return new CauseScore("Background process impact", score, confidence, 5);
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

            if (IsIgnoredBackgroundProcess(firstProcessName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(activeProcess))
            {
                return true;
            }

            string normalizedActive = RemoveExe(activeProcess);
            string normalizedTop = RemoveExe(firstProcessName);

            if (normalizedActive.Contains(normalizedTop, StringComparison.OrdinalIgnoreCase) ||
                normalizedTop.Contains(normalizedActive, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.Equals(
                normalizedActive,
                normalizedTop,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsIgnoredBackgroundProcess(string processName)
        {
            string normalized = RemoveExe(processName);

            string[] ignored =
            {
                "Idle",
                "System",
                "Registry",
                "Memory Compression",
                "Gamepulse",
                "GameBar",
                "GameBarFTServer",
                "ApplicationFrameHost",
                "dwm",
                "explorer"
            };

            return ignored.Any(value =>
                string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
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
            CauseScore primaryCause,
            List<CauseScore> contributingCauses,
            SessionAnalysisResult result)
        {
            double baselineFps = GetBaselineFps(result);
            double baselineFrameTimeMs = GetBaselineFrameTimeMs(result);

            double peakGpu1 = MaxNullable(window.Select(sample => sample.Gpu1UsagePercent));
            double peakGpu0 = MaxNullable(window.Select(sample => sample.Gpu0UsagePercent));
            double peakCpu = Max(window.Select(sample => sample.CpuPercent));
            double peakGameCpu = MaxNullable(window.Select(sample => sample.GameCpuPercent));
            double peakRam = Max(window.Select(sample => sample.RamPercent));
            double peakDiskActive = MaxNullable(window.Select(sample => sample.DiskActivePercent));
            double peakDiskRead = MaxNullable(window.Select(sample => sample.DiskReadMBps));
            double peakDiskWrite = MaxNullable(window.Select(sample => sample.DiskWriteMBps));

            List<string> evidence = new();

            evidence.Add($"baseline FPS {baselineFps:0.0}");
            evidence.Add($"baseline frame time {baselineFrameTimeMs:0.00} ms");

            if (eventSample.Fps.HasValue)
            {
                evidence.Add($"event FPS {eventSample.Fps.Value:0.0}");
            }

            if (eventSample.AverageFrameTimeMs.HasValue)
            {
                evidence.Add($"event avg frame time {eventSample.AverageFrameTimeMs.Value:0.00} ms");

                if (baselineFrameTimeMs > 0)
                {
                    double averageFrameTimeRatio = eventSample.AverageFrameTimeMs.Value / baselineFrameTimeMs;
                    evidence.Add($"avg frame time ratio {averageFrameTimeRatio:0.0}x");
                }
            }

            if (eventSample.WorstFrameTimeMs.HasValue)
            {
                evidence.Add($"event worst frame time {eventSample.WorstFrameTimeMs.Value:0.00} ms");

                if (baselineFrameTimeMs > 0)
                {
                    double worstFrameTimeRatio = eventSample.WorstFrameTimeMs.Value / baselineFrameTimeMs;
                    evidence.Add($"worst frame time ratio {worstFrameTimeRatio:0.0}x");
                }
            }

            evidence.Add($"peak CPU {peakCpu:0.0}%");
            evidence.Add($"peak game CPU {peakGameCpu:0.0}%");
            evidence.Add($"peak RAM {peakRam:0.0}%");
            evidence.Add($"peak GPU 0 {peakGpu0:0.0}%");
            evidence.Add($"peak GPU 1 {peakGpu1:0.0}%");
            evidence.Add($"peak disk active {peakDiskActive:0.0}%");
            evidence.Add($"peak disk read {peakDiskRead:0.0} MB/s");
            evidence.Add($"peak disk write {peakDiskWrite:0.0} MB/s");

            if (primaryCause.Cause != "Not classified for diagnostic spike")
            {
                evidence.Add($"primary score: {primaryCause.Cause} ({primaryCause.Score})");
            }

            if (contributingCauses.Count > 0)
            {
                evidence.Add(
                    "possible contributor scores: " +
                    string.Join(", ", contributingCauses.Select(cause => $"{cause.Cause} ({cause.Score})"))
                );
            }

            if (!string.IsNullOrWhiteSpace(eventSample.TopCpuProcesses))
            {
                evidence.Add($"top CPU process: {eventSample.TopCpuProcesses.Split('|')[0].Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(eventSample.TopRamProcesses))
            {
                evidence.Add($"top RAM process: {eventSample.TopRamProcesses.Split('|')[0].Trim()}");
            }

            return string.Join("; ", evidence);
        }

        private static string BuildRecommendation(
            string primaryCause,
            List<CauseScore> contributingCauses,
            bool isMinorInfoIssue)
        {
            if (isMinorInfoIssue)
            {
                if (contributingCauses.Count == 0)
                {
                    return "This looks like a short diagnostic frame-time spike. No immediate setting change is recommended unless this repeats often or becomes noticeable.";
                }

                return
                    "This was a diagnostic frame-time spike, not a confirmed major bottleneck. " +
                    "Possible contributors were detected, but no immediate setting change is recommended unless similar spikes repeat often.";
            }

            List<string> recommendations = new()
            {
                GetRecommendationForCause(primaryCause)
            };

            foreach (CauseScore contributingCause in contributingCauses)
            {
                string recommendation = GetRecommendationForCause(contributingCause.Cause);

                if (!recommendations.Contains(recommendation))
                {
                    recommendations.Add(recommendation);
                }
            }

            return string.Join(" Also, ", recommendations);
        }

        private static string GetRecommendationForCause(string cause)
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
                    "Check for game asset loading, downloads, antivirus scans, or slow storage. Moving the game to a faster SSD may reduce similar hitches.",

                "Background process impact" =>
                    "Close or limit background applications that appear in top CPU/RAM lists during frame drops or frame-time spikes.",

                "Mixed or unclear" =>
                    "No single resource clearly explained the issue. Review the surrounding telemetry and compare repeated sessions for a stronger pattern.",

                _ =>
                    "Review the surrounding telemetry and reduce the most active resource category during the affected seconds."
            };
        }

        private static string DetectOverallBottleneck(
            List<PerformanceIssue> issues,
            SessionAnalysisResult result)
        {
            if (issues.Count == 0)
            {
                return "No major bottleneck detected";
            }

            return issues
                .Where(issue => issue.LikelyCause != "Mixed or unclear")
                .Where(issue => issue.LikelyCause != "Not classified for diagnostic spike")
                .GroupBy(issue => issue.LikelyCause)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Count(issue => issue.Confidence == "High"))
                .Select(group => group.Key)
                .FirstOrDefault() ?? "Mixed or unclear";
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
                recommendations.Add("Check for storage spikes, downloads, antivirus scans, or asset loading. A faster SSD may reduce disk-related hitches.");
            }

            if (result.Issues.Any(issue => issue.LikelyCause == "Background process impact") ||
                result.Issues.Any(issue => issue.ContributingFactors.Contains("Background process impact")))
            {
                recommendations.Add("Close or limit background apps that appear in top CPU/RAM process lists during frame drops or frame-time spikes.");
            }

            if (recommendations.Count == 0)
            {
                recommendations.Add("No major bottleneck was detected. No immediate setting change is recommended from this session.");
            }

            return recommendations.Distinct().ToList();
        }

        private static void AddSessionCautionNotes(SessionAnalysisResult result)
        {
            if (result.PeakRamPercent >= VeryHighRamUsagePercent)
            {
                result.TechnicalNotes.Add(
                    $"RAM usage was very high during the session, peaking at {result.PeakRamPercent:0.0}%. " +
                    "This did not necessarily cause a frame issue in this run, but it may reduce headroom in longer sessions."
                );
            }
            else if (result.PeakRamPercent >= HighRamUsagePercent)
            {
                result.TechnicalNotes.Add(
                    $"RAM usage was high during the session, peaking at {result.PeakRamPercent:0.0}%. " +
                    "Monitor this if stutters appear in longer sessions."
                );
            }

            if (result.PeakDiskActivePercent >= HighDiskActivePercent && result.Issues.Count == 0)
            {
                result.TechnicalNotes.Add(
                    $"Disk active time peaked at {result.PeakDiskActivePercent:0.0}%. " +
                    "Since no major frame issue was detected, this is logged as a caution rather than a confirmed bottleneck."
                );
            }
        }

        private static string BuildOverallSummary(SessionAnalysisResult result)
        {
            int fpsDropCount = result.Issues.Count(issue =>
                issue.IssueType.Contains("FPS", StringComparison.OrdinalIgnoreCase));

            int stutterCount = result.Issues.Count(issue =>
                issue.IssueType.Contains("Stutter", StringComparison.OrdinalIgnoreCase));

            int severeHitchCount = result.Issues.Count(issue =>
                issue.IssueType.Contains("Severe Hitch", StringComparison.OrdinalIgnoreCase));

            int severeFrameTimeSpikeCount = result.MinorIssues.Count(issue =>
                issue.IssueType.Contains("Severe Frame-Time Spike", StringComparison.OrdinalIgnoreCase));

            if (result.Issues.Count == 0)
            {
                return
                    $"No major FPS drops, stutters, or gameplay-impacting severe hitches were detected. " +
                    $"{result.MinorFrameTimeSpikeCount} minor/diagnostic frame-time spike sample(s) were logged, " +
                    $"including {severeFrameTimeSpikeCount} severe single-frame spike sample(s). " +
                    $"{result.CaptureGapCount} capture gap/no-render sample(s) were excluded from normal bottleneck analysis.";
            }

            string repeatedPattern = result.Issues
                .Where(issue => issue.LikelyCause != "Mixed or unclear")
                .Where(issue => issue.LikelyCause != "Not classified for diagnostic spike")
                .GroupBy(issue => issue.LikelyCause)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Key} appeared in {group.Count()} major issue(s)")
                .FirstOrDefault() ?? "No repeated cause pattern was strong enough to classify.";

            return
                $"Detected {result.Issues.Count} major performance issue(s), including " +
                $"{fpsDropCount} FPS drop event(s), " +
                $"{stutterCount} stutter event(s), and " +
                $"{severeHitchCount} gameplay-impacting severe hitch event(s). " +
                $"{result.MinorFrameTimeSpikeCount} minor/diagnostic frame-time spike sample(s) were logged separately, " +
                $"including {severeFrameTimeSpikeCount} severe single-frame spike sample(s). " +
                $"{result.CaptureGapCount} capture gap/no-render sample(s) were excluded from normal bottleneck analysis. " +
                $"{repeatedPattern}.";
        }

        private static void AppendRecommendations(StringBuilder builder, List<string> recommendations)
        {
            builder.AppendLine("Recommendations:");

            foreach (string recommendation in recommendations)
            {
                builder.AppendLine($"- {recommendation}");
            }
        }

        private static double GetBaselineFps(SessionAnalysisResult result)
        {
            return result.MedianFps > 0
                ? result.MedianFps
                : result.AverageFps;
        }

        private static double GetBaselineFrameTimeMs(SessionAnalysisResult result)
        {
            double baselineFps = GetBaselineFps(result);

            if (baselineFps <= 0)
            {
                return 0;
            }

            return 1000.0 / baselineFps;
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
            public int Priority { get; }

            public CauseScore(string cause, int score, string confidence, int priority)
            {
                Cause = cause;
                Score = score;
                Confidence = confidence;
                Priority = priority;
            }
        }
    }
}