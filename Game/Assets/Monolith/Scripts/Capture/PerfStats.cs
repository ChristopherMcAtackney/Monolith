using System;
using System.Collections.Generic;

namespace Monolith.Capture
{
    /// <summary>Frame statistics from a list of frame times in milliseconds.</summary>
    public sealed class PerfStats
    {
        public int frames;
        public double seconds;
        public double avgFps;
        /// <summary>1000 / the mean of the slowest 1% of frame times.</summary>
        public double low1PctFps;
        public double worstFrameMs;
        public double p50Ms, p95Ms, p99Ms;

        public static PerfStats From(List<double> frameMs)
        {
            if (frameMs == null || frameMs.Count == 0) throw new ArgumentException("no frames were measured");
            var sorted = new List<double>(frameMs);
            sorted.Sort();
            int n = sorted.Count;
            double total = 0;
            foreach (var v in frameMs) total += v;
            int worstN = Math.Max(1, n / 100);
            double worstSum = 0;
            for (int i = n - worstN; i < n; i++) worstSum += sorted[i];
            double P(double q) => sorted[Math.Min(n - 1, (int)Math.Ceiling(q * n) - 1)];
            return new PerfStats
            {
                frames = n,
                seconds = Math.Round(total / 1000.0, 3),
                avgFps = Math.Round(n / (total / 1000.0), 1),
                low1PctFps = Math.Round(1000.0 / (worstSum / worstN), 1),
                worstFrameMs = Math.Round(sorted[n - 1], 3),
                p50Ms = Math.Round(P(0.50), 3),
                p95Ms = Math.Round(P(0.95), 3),
                p99Ms = Math.Round(P(0.99), 3),
            };
        }
    }
}
