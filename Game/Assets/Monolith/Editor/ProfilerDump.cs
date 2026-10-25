using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Monolith.Core;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Reads a Unity profiler capture (<c>.raw</c>, written by a development player run with <c>-profiler-enable -profiler-log-file x.raw</c>) and writes a text report of
    /// its slowest frames: for each, every thread's samples above a threshold, nested by depth. Nobody opens the editor, so this is how a hitch is looked into.
    /// <code>tools\unity.ps1 Monolith.EditorTools.ProfilerDump.Dump -- -mnProfile x.raw -mnOut report.txt [-mnMinFrameMs 100] [-mnMinSampleMs 3] [-mnMaxFrames 6]</code>
    /// Build the development player first: <c>tools\unity.ps1 Monolith.EditorTools.Build.Player -- -mnDev</c>.
    /// </summary>
    public static class ProfilerDump
    {
        /// <summary>The profiler's "frames to keep" (an internal setting, so by reflection).</summary>
        static void SetHistory(int frames)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var n in new[] { "UnityEditor.ProfilerUserSettings", "UnityEditor.Profiling.ProfilerUserSettings", "UnityEditorInternal.ProfilerUserSettings" })
                {
                    var t = a.GetType(n);
                    var p = t?.GetProperty("frameCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p == null) continue;
                    p.SetValue(null, frames);
                    MnLog.Info($"ProfilerDump: history set to {frames} frames through {n}");
                    return;
                }
            MnLog.Warn("ProfilerDump: could not set the profiler's history length; only the last 300 frames are read");
        }

        public static void Dump() => Batch.Run("ProfilerDump", () =>
        {
            string file = Path.GetFullPath(CommandLine.Get("-mnProfile"));
            string outPath = Path.GetFullPath(CommandLine.Get("-mnOut", file + ".txt"));
            float minFrame = CommandLine.GetFloat("-mnMinFrameMs", 100f), minSample = CommandLine.GetFloat("-mnMinSampleMs", 3f);
            int maxFrames = CommandLine.GetInt("-mnMaxFrames", 6);
            if (!File.Exists(file)) throw new FileNotFoundException("no profiler capture", file);
            SetHistory(2000);                                                // the history the editor keeps (300 by default: the hitch is long before the end of a run)
            if (!ProfilerDriver.LoadProfile(file, false)) throw new Exception("could not load " + file);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var sb = new StringBuilder();
            sb.AppendLine($"{file}: frames {first}..{last}");

            var frames = new List<(int frame, float ms)>();
            for (int f = first; f <= last; f++)
                using (var d = ProfilerDriver.GetRawFrameDataView(f, 0))
                    if (d.valid) frames.Add((f, d.frameTimeMs));
            sb.AppendLine($"{frames.Count} frames read");
            var sorted = new List<(int frame, float ms)>(frames);
            sorted.Sort((a, b) => b.ms.CompareTo(a.ms));
            sb.AppendLine("slowest frames (index: ms):");
            for (int i = 0; i < Math.Min(20, sorted.Count); i++) sb.AppendLine($"  {sorted[i].frame}: {sorted[i].ms:0.0} ms");

            int dumped = 0;
            foreach (var (frame, ms) in sorted)
            {
                if (ms < minFrame || dumped >= maxFrames) break;
                dumped++;
                sb.AppendLine();
                sb.AppendLine($"==== frame {frame}: {ms:0.0} ms ====");
                for (int ti = 0; ti < 64; ti++)
                {
                    using (var td = ProfilerDriver.GetRawFrameDataView(frame, ti))
                    {
                        if (!td.valid) break;
                        // The thread's own samples above the threshold, nested by depth.
                        var lines = new List<string>();
                        var ends = new Stack<int>();                        // samples come depth first; a sample's descendants are the next GetSampleChildrenCount ones
                        for (int i = 0; i < td.sampleCount; i++)
                        {
                            while (ends.Count > 0 && ends.Peek() < i) ends.Pop();
                            int depth = ends.Count;
                            ends.Push(i + td.GetSampleChildrenCount(i));
                            float sms = td.GetSampleTimeMs(i);
                            if (sms < minSample) continue;
                            lines.Add($"{new string(' ', Math.Min(depth, 24) * 2)}{td.GetSampleName(i)}  {sms:0.0} ms");
                        }
                        if (lines.Count == 0) continue;
                        sb.AppendLine($"-- thread {ti}: {td.threadName} ({td.threadGroupName}), frame time {td.frameTimeMs:0.0} ms");
                        foreach (var l in lines) sb.AppendLine("   " + l);
                    }
                }
            }
            File.WriteAllText(outPath, sb.ToString());
            MnLog.Info($"ProfilerDump: {frames.Count} frames, {dumped} dumped -> {outPath}");
        });
    }
}
