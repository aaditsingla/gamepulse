using System;
using System.Collections.Generic;
using System.Linq;
using Gamepulse.Models;

namespace Gamepulse.Services
{
    public class FrameMetricsCalculator
    {
        public FrameMetrics Calculate(List<double> frameTimesMs)
        {
            if (frameTimesMs.Count == 0)
            {
                return FrameMetrics.Empty();
            }

            List<double> validFrameTimes = frameTimesMs
                .Where(frameTime => frameTime > 0)
                .ToList();

            if (validFrameTimes.Count == 0)
            {
                return FrameMetrics.Empty();
            }

            double averageFrameTimeMs = validFrameTimes.Average();
            double worstFrameTimeMs = validFrameTimes.Max();
            double fps = 1000.0 / averageFrameTimeMs;

            double onePercentLowFps = CalculateLowFps(validFrameTimes, 0.01);
            double pointOnePercentLowFps = CalculateLowFps(validFrameTimes, 0.001);

            int stutterCount = CountStutters(validFrameTimes, averageFrameTimeMs);

            return new FrameMetrics(
                fps,
                averageFrameTimeMs,
                worstFrameTimeMs,
                onePercentLowFps,
                pointOnePercentLowFps,
                stutterCount
            );
        }

        private static double CalculateLowFps(List<double> frameTimesMs, double percentile)
        {
            List<double> sortedDescending = frameTimesMs
                .OrderByDescending(frameTime => frameTime)
                .ToList();

            int sampleCount = Math.Max(1, (int)Math.Ceiling(sortedDescending.Count * percentile));

            double averageSlowFrameTimeMs = sortedDescending
                .Take(sampleCount)
                .Average();

            return 1000.0 / averageSlowFrameTimeMs;
        }

        private static int CountStutters(List<double> frameTimesMs, double averageFrameTimeMs)
        {
            double stutterThresholdMs = Math.Max(50.0, averageFrameTimeMs * 2.5);

            return frameTimesMs.Count(frameTime => frameTime >= stutterThresholdMs);
        }
    }
}