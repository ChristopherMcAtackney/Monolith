using System;
using System.Diagnostics;
using Monolith.Core;
using UnityEditor;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Wrap every -executeMethod entry point in Batch.Run: it logs "[MN] name OK", saves assets, and on any exception
    /// logs "[MN][ERROR] ..." and exits Unity with code 1 so tools\unity.ps1 reports the failure.
    /// Read extra arguments with Monolith.Core.CommandLine ("-mnSomething value").
    /// </summary>
    public static class Batch
    {
        public static void Run(string name, Action body)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                MnLog.Info($"{name} start");
                body();
                AssetDatabase.SaveAssets();
                MnLog.Info($"{name} OK in {sw.Elapsed.TotalSeconds:0.0}s");
            }
            catch (Exception e)
            {
                MnLog.Error($"{name} FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>Several steps in one Unity session (saves the editor start-up of a separate run per step).</summary>
        public static void Chain(string name, params Action[] steps) => Run(name, () => { foreach (var s in steps) s(); });
    }
}
