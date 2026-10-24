using System;
using System.Globalization;

namespace Monolith.Core
{
    /// <summary>
    /// What a mission's <c>theme</c> action tells the audio (the event <see cref="MissionEvents.Theme"/>): a theme track fades in over the base score when its moment
    /// begins (a Lector arrives) and out when it ends. The event's Subject carries everything as text so the mission code and the audio need share only this file:
    /// <c>mission/name;fadeIn=4;fadeOut=6;priority=10;volume=-2;mode=duck;duck=-9</c> and its Value is 1 for on, 0 for off. A field left out is the theme's own default
    /// (the built-in theme's, or the player's music.json). <see cref="StopAll"/> (Value 0) ends every theme (a mission starting or ending).
    /// </summary>
    public sealed class ThemeEvent
    {
        public const string StopAll = "*";

        public string Mission, Name;
        public bool On = true;
        /// <summary>Seconds; NaN when the action did not say (the theme's default applies).</summary>
        public float FadeIn = float.NaN, FadeOut = float.NaN;
        /// <summary>NaN when not said; the higher priority wins when two themes are on at once.</summary>
        public float Priority = float.NaN;
        /// <summary>Level of the theme in dB, NaN when not said.</summary>
        public float VolumeDb = float.NaN;
        /// <summary>"duck" (the base score drops by DuckDb under the theme) or "replace" (the base falls silent under it); null when not said.</summary>
        public string Mode;
        public float DuckDb = float.NaN;

        /// <summary>The key a theme is looked up by in the player's music.json: "escort/lector".</summary>
        public string Key => string.IsNullOrEmpty(Mission) ? Name : Mission + "/" + Name;

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public string Format()
        {
            var s = Key;
            if (!float.IsNaN(FadeIn)) s += ";fadeIn=" + F(FadeIn);
            if (!float.IsNaN(FadeOut)) s += ";fadeOut=" + F(FadeOut);
            if (!float.IsNaN(Priority)) s += ";priority=" + F(Priority);
            if (!float.IsNaN(VolumeDb)) s += ";volume=" + F(VolumeDb);
            if (Mode != null) s += ";mode=" + Mode;
            if (!float.IsNaN(DuckDb)) s += ";duck=" + F(DuckDb);
            return s;
        }

        public static ThemeEvent Parse(string subject, float value)
        {
            if (string.IsNullOrEmpty(subject)) return null;
            var parts = subject.Split(';');
            var t = new ThemeEvent { On = value > 0.5f };
            string key = parts[0].Trim();
            int slash = key.IndexOf('/');
            if (slash >= 0) { t.Mission = key.Substring(0, slash); t.Name = key.Substring(slash + 1); }
            else t.Name = key;
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = parts[i].Substring(0, eq), v = parts[i].Substring(eq + 1);
                float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f);
                switch (k)
                {
                    case "fadeIn": t.FadeIn = f; break;
                    case "fadeOut": t.FadeOut = f; break;
                    case "priority": t.Priority = f; break;
                    case "volume": t.VolumeDb = f; break;
                    case "mode": t.Mode = v; break;
                    case "duck": t.DuckDb = f; break;
                }
            }
            return t;
        }
    }
}
