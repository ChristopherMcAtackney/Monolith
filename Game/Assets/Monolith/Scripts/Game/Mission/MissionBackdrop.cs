using Monolith.Mission;
using Monolith.Sky;

namespace Monolith.Game
{
    /// <summary>A mission's <c>backdrop</c> block (the format's <see cref="MissionFile.BackdropDef"/>) as the sky's options: the planet, where the Sun is, and the sky's cold fill turned down to make room for the planet's light.</summary>
    public static class MissionBackdrop
    {
        public static SkyOptions Options(MissionFile file, bool enabled = true)
        {
            var o = new SkyOptions();
            if (!enabled || file?.Backdrop == null) return o;
            var d = file.Backdrop;
            var b = o.Backdrop;
            b.Body = d.Body;
            b.DistanceKm = d.DistanceKm; b.Azimuth = d.Azimuth; b.Elevation = d.Elevation;
            b.HasSun = d.HasSun; b.SunAzimuth = d.SunAzimuth; b.SunElevation = d.SunElevation; b.SunDistanceAu = d.SunDistanceAu;
            b.GrsLongitude = d.GrsLongitude; b.StartSeconds = d.StartSeconds;
            b.SpinDegPerSec = d.Spin; b.WindScale = d.WindScale; b.GrsSpinDegPerSec = d.GrsSpin; b.MoonSpeed = d.MoonSpeed;
            b.Brightness = d.Brightness; b.Planetshine = d.Planetshine; b.AmbientScale = d.AmbientScale; b.Detail = d.Detail; b.Haze = d.Haze;
            foreach (var m in d.Moons) b.Moons.Add(new MoonOptions { Body = m.Body, OrbitDeg = m.OrbitDeg });
            return o;
        }
    }
}
