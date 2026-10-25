using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Sky
{
    /// <summary>
    /// What is known about a body that can be a backdrop: its size, its map, its atmosphere and its motion, as data
    /// (<c>Resources/MonolithSky/Bodies/&lt;id&gt;.body.json</c>, with the source of every number in the file's own "...Why" fields).
    /// </summary>
    public sealed class BodyDef
    {
        public const string Folder = "MonolithSky/Bodies/";
        public const string FormatName = "monolith-body/1";

        [JsonProperty("format")] public string Format;
        public string Id, Name;
        public float RadiusKm = 71492f;
        /// <summary>The planet's north pole in the sky's equatorial frame (J2000 right ascension and declination, degrees).</summary>
        public float PoleRaDeg = 268.0566f, PoleDecDeg = 64.4953f;
        /// <summary>The albedo map (a Resources path), the factor from the map's linear value to reflectance, and where the Great Red Spot is on it.</summary>
        public string Map = "MonolithSky/Bodies/jupiter_map";
        public float AlbedoScale = 1f;
        public GrsDef Grs = new GrsDef();
        /// <summary>Default slow motion (a mission may change each): spin deg/s, how many times the real winds, the Spot's turning deg/s.</summary>
        public float SpinDegPerSec = 0.06f, WindScale = 120f, GrsSpinDegPerSec = 0.5f;
        /// <summary>The zonal winds: [latitude degrees, metres per second east] control points, smoothed into a profile.</summary>
        public List<float[]> Winds = new List<float[]>();
        public AtmosphereDef Atmosphere = new AtmosphereDef();
        public DetailDef Detail = new DetailDef();
        public MoonDef Moon;

        public sealed class GrsDef
        {
            /// <summary>Where the Spot's centre is on the map (u, v in 0..1 of the picture, v from the bottom), its half-size in great-circle degrees, and where the oval ends and the shear collar around it ends, in oval radii.</summary>
            public float U = 0.364f, V = 0.389f, HalfWidthDeg = 9.5f, HalfHeightDeg = 5f, OvalEnd = 0.75f, CollarEnd = 1.5f;
        }

        public sealed class AtmosphereDef
        {
            /// <summary>0 is a Lambert ball (the limb goes dark at once); 1 is Lommel-Seeliger (no limb darkening at full phase). Jupiter's visible limb is in between.</summary>
            public float LommelSeeliger = 0.45f;
            /// <summary>Width of the twilight in cosine units (the softplus width): how far past the geometric terminator the light reaches.</summary>
            public float TwilightWidth = 0.09f;
            public float TwilightReddening = 1.1f;
            /// <summary>The haze's optical depth looking edge-on at the cloud tops, its scale height in km, and its brightness.</summary>
            public float HazeTauLimb = 3f, HazeHeightKm = 160f, HazeBrightness = 1f;
        }

        public sealed class DetailDef
        {
            /// <summary>Detail finer than the map: noise cycles per radian of longitude at the equator, how strong, and how much longer along the bands than across.</summary>
            public float Frequency = 2400f, Contrast = 0.22f, Stretch = 5f;
        }

        /// <summary>A moon rather than a planet: its orbit radius (km) and period (days).</summary>
        public sealed class MoonDef
        {
            public float OrbitKm = 421700f, PeriodDays = 1.769f;
        }

        public static BodyDef Load(string id)
        {
            var text = Resources.Load<TextAsset>(Folder + id + ".body");
            if (text == null) throw new InvalidOperationException($"no body '{id}' (Resources/{Folder}{id}.body.json)");
            return Parse(text.text, id);
        }

        public static BodyDef Parse(string json, string source)
        {
            var d = JsonConvert.DeserializeObject<BodyDef>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore });
            if (d == null) throw new InvalidOperationException("body file is empty: " + source);
            if (d.Format != FormatName) throw new InvalidOperationException($"body '{source}': format must be \"{FormatName}\"");
            if (d.Moon == null && d.Winds.Count < 2) throw new InvalidOperationException($"body '{source}': winds needs at least two [latitude, m/s] points");
            return d;
        }

        /// <summary>The zonal wind profile smoothed (a cubic through the control points) and sampled at <paramref name="count"/> latitudes from -90 to +90 degrees.</summary>
        public float[] WindSamples(int count = 68)
        {
            var pts = new List<Vector2>();
            foreach (var p in Winds) pts.Add(new Vector2(p[0], p[1]));
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            var s = new float[count];
            for (int i = 0; i < count; i++)
            {
                float lat = -90f + 180f * i / (count - 1);
                int k = 0;
                while (k + 1 < pts.Count - 1 && pts[k + 1].x < lat) k++;
                Vector2 p1 = pts[k], p2 = pts[Mathf.Min(k + 1, pts.Count - 1)];
                Vector2 p0 = pts[Mathf.Max(k - 1, 0)], p3 = pts[Mathf.Min(k + 2, pts.Count - 1)];
                float span = Mathf.Max(p2.x - p1.x, 1e-3f);
                float t = Mathf.Clamp01((lat - p1.x) / span);
                float m1 = 0.5f * (p2.y - p0.y) * (span / Mathf.Max(p2.x - p0.x, 1e-3f)), m2 = 0.5f * (p3.y - p1.y) * (span / Mathf.Max(p3.x - p1.x, 1e-3f));
                float t2 = t * t, t3 = t2 * t;
                s[i] = (2 * t3 - 3 * t2 + 1) * p1.y + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2.y + (t3 - t2) * m2;
            }
            return s;
        }
    }
}
