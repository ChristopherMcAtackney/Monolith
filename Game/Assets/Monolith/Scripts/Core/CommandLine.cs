using System;
using System.Globalization;

namespace Monolith.Core
{
    /// <summary>
    /// "-mnFlag value" style arguments, shared by the player and the editor batch methods. Every Monolith flag starts
    /// with <see cref="Prefix"/> so it can never collide with Unity's own flags. Names are matched case-insensitively.
    /// Known flags: -mnCapture plan.json, -mnOut dir, -mnOnly a,b, -mnKind stills|perf, -mnLogEvents, -mnDev (build).
    /// </summary>
    public static class CommandLine
    {
        public const string Prefix = "-mn";

        static string[] _args;
        static string[] Args => _args ??= Environment.GetCommandLineArgs();

        /// <summary>Replace the argument list (tests, editor play mode).</summary>
        public static void SetArgs(string[] args) => _args = args;

        public static bool Has(string flag)
        {
            Check(flag);
            foreach (var a in Args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Get(string flag, string fallback = null)
        {
            Check(flag);
            var a = Args;
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return fallback;
        }

        public static int GetInt(string flag, int fallback)
        {
            var s = Get(flag);
            return s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public static float GetFloat(string flag, float fallback)
        {
            var s = Get(flag);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        /// <summary>A comma-separated value as a list (empty when the flag is absent).</summary>
        public static string[] GetList(string flag)
        {
            var s = Get(flag);
            return string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        static void Check(string flag)
        {
            if (flag == null || !flag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Monolith flags start with '{Prefix}', got '{flag}'");
        }
    }
}
