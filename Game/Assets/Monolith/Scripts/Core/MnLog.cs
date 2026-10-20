using UnityEngine;

namespace Monolith.Core
{
    /// <summary>Log lines prefixed [MN] are what tools\unity.ps1 and tools\capture.ps1 echo in their summaries.</summary>
    public static class MnLog
    {
        public static void Info(string msg) => Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[MN] {0}", msg);
        public static void Warn(string msg) => Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "[MN][WARN] {0}", msg);
        public static void Error(string msg) => Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null, "[MN][ERROR] {0}", msg);
    }
}
