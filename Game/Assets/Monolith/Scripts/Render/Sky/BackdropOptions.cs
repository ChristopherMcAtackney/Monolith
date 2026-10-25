using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Sky
{
    /// <summary>
    /// A planet in the sky: what a mission's <c>"backdrop"</c> block says, and the fields of <see cref="SkyOptions.Backdrop"/>. The planet is drawn in the sky pass
    /// (see MonolithPlanet.hlsl), seen from one fixed point, so it has no parallax, no depth to lose precision in and nothing to clip against. Numbers are in the
    /// world's frame: azimuth is degrees from +Z toward +X, elevation degrees above the XZ plane (the ecliptic; world +Y is ecliptic north).
    /// A body that is empty ("") is no backdrop at all and the sky is as it always was.
    /// </summary>
    [Serializable]
    public sealed class BackdropOptions
    {
        /// <summary>The body's id: a <c>Resources/MonolithSky/Bodies/&lt;id&gt;.body.json</c> ("jupiter"). Empty: no backdrop.</summary>
        public string Body = "";
        /// <summary>Distance from the viewer to the planet's centre, km. 200,000 km (2.8 radii) makes Jupiter 42 degrees across.</summary>
        public float DistanceKm = 200000f;
        /// <summary>Where the planet's centre lies, as seen from the fight.</summary>
        public float Azimuth = 20f, Elevation = -12f;
        /// <summary>Where the Sun lies (only used when <see cref="HasSun"/>; otherwise the sky's own Sun, <see cref="SkyOptions.SunEclipticLongitude"/>, is used).</summary>
        public bool HasSun;
        public float SunAzimuth = -70f, SunElevation = 28f;
        /// <summary>How far the planet is from the Sun, AU. Sunlight there is 1 / AU^2 of Earth's (5.2 AU: 1/27); the camera's exposure is opened by the same factor, so the picture is the one the game's reference exposure shows (see <see cref="BackdropState.PhysicalSunLux"/>).</summary>
        public float SunDistanceAu = 5.2f;
        /// <summary>The Great Red Spot's longitude seen from the centre of the disc at the start, degrees east of the central meridian (positive: right of centre).</summary>
        public float GrsLongitude = 28f;
        /// <summary>The planet's own clock when the mission begins, seconds (a different place in its drift and the Spot's turning).</summary>
        public float StartSeconds = 0f;
        /// <summary>Slow motion. Spin: degrees of longitude per second the whole planet turns (real: 0.01 deg/s, sped up to read); WindScale: how many times the real zonal winds the bands slide at; GrsSpin: degrees per second the Spot turns. Negative: the body's own default.</summary>
        public float SpinDegPerSec = -1f, WindScale = -1f, GrsSpinDegPerSec = -1f;
        /// <summary>How many times faster than real the moons go round (100: Io once in 25 minutes, Europa once in 51). A moon passing in front of the Sun's side of the planet casts its shadow on the clouds; one going behind the planet is hidden by it.</summary>
        public float MoonSpeed = 100f;
        /// <summary>Taste knobs. Brightness scales the planet as seen; Planetshine scales the light it throws on the ships (1 = the physical amount); AmbientScale scales the sky's cold fill (a big warm planet should be the main fill on its side); Detail scales the detail added beyond the map; Haze scales the limb haze.</summary>
        public float Brightness = 1f, Planetshine = 1f, AmbientScale = 0.4f, Detail = 1f, Haze = 1f;
        /// <summary>The moons in front of or behind the planet (Io, Europa), each by id and its place in its orbit.</summary>
        public List<MoonOptions> Moons = new List<MoonOptions>();

        public bool Enabled => !string.IsNullOrEmpty(Body);
    }

    /// <summary>One moon: the body's id (<c>io</c>, <c>europa</c>) and where it is on its orbit (degrees from the direction to the Sun's far side, counter-clockwise seen from the planet's north).</summary>
    [Serializable]
    public sealed class MoonOptions
    {
        public string Body = "io";
        public float OrbitDeg = 40f;
    }
}
