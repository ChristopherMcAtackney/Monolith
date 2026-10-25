using System;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Sky
{
    /// <summary>
    /// A backdrop planet as the sky renderer needs it: the frame (pole, central meridian, Sun, distance), the motion and the look, worked out from a
    /// <see cref="BackdropOptions"/> and a <see cref="BodyDef"/> by <see cref="Configure"/>, and pushed into a property block by <see cref="Push"/>.
    /// The sky's volume holds it by reference (<see cref="BackdropParameter"/>), so the rig can retune it without rebuilding the sky.
    /// </summary>
    [Serializable]
    public sealed class BackdropState
    {
        public BodyDef Body;
        public BackdropOptions Options;
        public Texture2D Map;
        public bool Active => Body != null && Map != null && Options != null && Options.Enabled;

        // The frame, in the world.
        public Vector3 ToCentre, Pole, E1, E2, SunDir;
        public float DistanceRadii, AngularRadius, AngularRadiusWithHaze;
        /// <summary>Radiance in nits of a reflectance-1 surface facing the Sun at the reference exposure, with the Brightness knob.</summary>
        public float SunNits;
        /// <summary>What the Sun would deliver at the planet in real life (lux), and the stops the camera opens to show it as the reference exposure does.</summary>
        public float PhysicalSunLux, ExposureOpenStops;
        public int BakeHash;

        sealed class Moon
        {
            public BodyDef Def; public Texture2D Map; public MoonOptions Options;
            public float A, Rm, Omega, Theta0;                // orbit radius and moon radius in planet radii, rad per second, the angle from the sunward point at time zero
            public Vector4 Pos, Cen, F, G, Look;
        }
        readonly System.Collections.Generic.List<Moon> _moons = new System.Collections.Generic.List<Moon>();
        readonly Vector4[] _moonPos = new Vector4[2], _moonCen = new Vector4[2], _moonF = new Vector4[2], _moonG = new Vector4[2], _moonLook = new Vector4[2];
        Vector3 _sunEq, _east;
        /// <summary>The moons' directions and sizes as seen from the viewer: id, angular radius in degrees, where (azimuth, elevation), lit share (0 in the planet's shadow). For the self-test and the docs.</summary>
        public readonly System.Collections.Generic.List<string> MoonReport = new System.Collections.Generic.List<string>();

        float _spin, _windScale, _grsSpin;
        float _mapLon0, _grsLon, _grsLat;
        float[] _winds;
        readonly Vector4[] _windArray = new Vector4[17];
        Vector4 _pole, _e1, _e2, _sun, _motion, _map0, _grs, _look, _look2, _px, _detail;

        static readonly int IdDir = Shader.PropertyToID("_PlanetDir"), IdPole = Shader.PropertyToID("_PlanetPole"), IdE1 = Shader.PropertyToID("_PlanetE1"), IdE2 = Shader.PropertyToID("_PlanetE2"),
            IdSun = Shader.PropertyToID("_PlanetSun"), IdMotion = Shader.PropertyToID("_PlanetMotion"), IdMap0 = Shader.PropertyToID("_PlanetMap0"), IdGrs = Shader.PropertyToID("_PlanetGrs"),
            IdLook = Shader.PropertyToID("_PlanetLook"), IdLook2 = Shader.PropertyToID("_PlanetLook2"), IdPx = Shader.PropertyToID("_PlanetPx"), IdDetail = Shader.PropertyToID("_PlanetDetail"),
            IdWinds = Shader.PropertyToID("_WindProfile"), IdMap = Shader.PropertyToID("_PlanetMap"),
            IdMoonCount = Shader.PropertyToID("_MoonCount"), IdMoonPos = Shader.PropertyToID("_MoonPos"), IdMoonF = Shader.PropertyToID("_MoonF"), IdMoonG = Shader.PropertyToID("_MoonG"),
            IdMoonLook = Shader.PropertyToID("_MoonLook"), IdMoonCen = Shader.PropertyToID("_MoonCen"), IdMoonMap0 = Shader.PropertyToID("_MoonMap0"), IdMoonMap1 = Shader.PropertyToID("_MoonMap1");

        public static Vector3 AzEl(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad, el = elevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        /// <summary>Make a state ready to draw: loads the body's data and map (throws if the body is not there).</summary>
        public static BackdropState Create(BackdropOptions options)
        {
            var s = new BackdropState();
            s.Body = BodyDef.Load(options.Body);
            s.Map = Resources.Load<Texture2D>(s.Body.Map) ?? throw new InvalidOperationException($"Resources/{s.Body.Map} is missing (python -I tools/jupiter/build_map.py)");
            foreach (var mo in options.Moons)
            {
                try
                {
                    var def = BodyDef.Load(mo.Body);
                    if (def.Moon == null) throw new InvalidOperationException($"body '{mo.Body}' has no moon block");
                    var map = Resources.Load<Texture2D>(def.Map) ?? throw new InvalidOperationException($"Resources/{def.Map} is missing");
                    if (s._moons.Count < 2) s._moons.Add(new Moon { Def = def, Map = map, Options = mo });
                }
                catch (Exception e) { MnLog.Error($"SKY moon '{mo.Body}' left out: {e.Message}"); }
            }
            s._winds = s.Body.WindSamples(68);
            for (int i = 0; i < 17; i++) s._windArray[i] = new Vector4(s._winds[i * 4], s._winds[i * 4 + 1], s._winds[i * 4 + 2], s._winds[i * 4 + 3]);
            return s;
        }

        /// <summary>Work the frame, the motion and the look out from the options. <paramref name="sunDirection"/> is where the scene's Sun is (the rig's), <paramref name="sunLux"/> its illuminance at the reference exposure, <paramref name="sunLuxScale"/> 1.</summary>
        public void Configure(BackdropOptions o, Vector3 sunDirection, float sunLux, Vector3 skyRotationEuler)
        {
            Options = o;
            var b = Body;
            ToCentre = AzEl(o.Azimuth, o.Elevation);
            SunDir = sunDirection.normalized;
            DistanceRadii = Mathf.Max(o.DistanceKm / b.RadiusKm, 1.02f);
            AngularRadius = Mathf.Asin(1f / DistanceRadii);

            // The pole: the body's real north pole (J2000 equatorial), put into the sky's world frame.
            Pole = SkyMath.EquatorialToWorld(b.PoleRaDeg / 15f, b.PoleDecDeg, skyRotationEuler);
            Vector3 fromCentre = -ToCentre;
            E1 = fromCentre - Vector3.Dot(fromCentre, Pole) * Pole;
            if (E1.sqrMagnitude < 1e-6f) E1 = Vector3.Cross(Pole, Vector3.right);
            E1.Normalize();
            E2 = Vector3.Cross(E1, Pole).normalized;

            float au = Mathf.Max(o.SunDistanceAu, 0.1f);
            PhysicalSunLux = sunLux / (au * au);
            ExposureOpenStops = 2f * Mathf.Log(au, 2f);
            SunNits = sunLux / Mathf.PI * o.Brightness;           // the reference exposure shows the planet as the same Sun shows a hull at the same iris (the open stops cancel the 1 / AU^2)

            _spin = (o.SpinDegPerSec >= 0f ? o.SpinDegPerSec : b.SpinDegPerSec) * Mathf.Deg2Rad;
            _windScale = o.WindScale >= 0f ? o.WindScale : b.WindScale;
            _grsSpin = (o.GrsSpinDegPerSec >= 0f ? o.GrsSpinDegPerSec : b.GrsSpinDegPerSec) * Mathf.Deg2Rad;

            _grsLon = (b.Grs.U - 0.5f) * 2f * Mathf.PI;
            _grsLat = (b.Grs.V - 0.5f) * Mathf.PI;
            _mapLon0 = _grsLon - o.GrsLongitude * Mathf.Deg2Rad;              // the map longitude at the central meridian

            float haze = Mathf.Max(o.Haze, 0f);
            float hazeH = b.Atmosphere.HazeHeightKm / b.RadiusKm;
            float tau = Mathf.Max(b.Atmosphere.HazeTauLimb, 0.01f);
            float zMax = hazeH * Mathf.Log(tau * 800f);                     // where the haze's column has fallen to a thousandth
            AngularRadiusWithHaze = Mathf.Asin(Mathf.Min((1f + Mathf.Max(zMax, 0f)) / DistanceRadii, 0.999f)) + 0.002f;

            _pole = new Vector4(Pole.x, Pole.y, Pole.z, Mathf.Cos(AngularRadiusWithHaze));
            _e1 = new Vector4(E1.x, E1.y, E1.z, 0f);
            _e2 = new Vector4(E2.x, E2.y, E2.z, 0f);
            _sun = new Vector4(SunDir.x, SunDir.y, SunDir.z, SunNits);
            _map0 = new Vector4(_mapLon0, _grsLon, _grsLat, 0f);
            _grs = new Vector4(b.Grs.HalfWidthDeg * Mathf.Deg2Rad, b.Grs.HalfHeightDeg * Mathf.Deg2Rad, b.Grs.OvalEnd, b.Grs.CollarEnd);
            _look = new Vector4(b.Atmosphere.LommelSeeliger, b.Atmosphere.TwilightWidth, tau * haze, hazeH);
            _look2 = new Vector4(b.Atmosphere.HazeBrightness * haze, b.Atmosphere.TwilightReddening, b.AlbedoScale, o.Detail);
            _detail = new Vector4(b.Detail.Frequency, b.Detail.Contrast, b.Detail.Stretch, 0f);

            // The moons: on their orbits in the planet's equator plane, OrbitDeg from the sunward point (90: toward the planet's east), going round at MoonSpeed times the real rate.
            _sunEq = SunDir - Vector3.Dot(SunDir, Pole) * Pole;
            if (_sunEq.sqrMagnitude < 1e-6f) _sunEq = E1;
            _sunEq.Normalize();
            _east = Vector3.Cross(_sunEq, Pole).normalized;
            foreach (var m in _moons)
            {
                m.A = m.Def.Moon.OrbitKm / b.RadiusKm; m.Rm = m.Def.RadiusKm / b.RadiusKm;
                m.Theta0 = m.Options.OrbitDeg * Mathf.Deg2Rad;
                m.Omega = 2f * Mathf.PI / (m.Def.Moon.PeriodDays * 86400f) * Mathf.Max(o.MoonSpeed, 0f);
            }
            PlaceMoons(0f, true);

            unchecked
            {
                int h = 17;
                foreach (var f in new[] { ToCentre.x, ToCentre.y, ToCentre.z, SunDir.x, SunDir.y, SunDir.z, DistanceRadii, SunNits, o.Planetshine, Pole.x, Pole.y, Pole.z, _mapLon0, _look.z, _look.w, _look.x, _look.y, _look2.x, _look2.z })
                    h = h * 31 + f.GetHashCode();
                BakeHash = h;
            }
        }

        /// <summary>Put the moons where they are at planet-clock time <paramref name="t"/> (seconds): their places, their frames for the maps, whether the planet's shadow hides the Sun from them.</summary>
        void PlaceMoons(float t, bool report)
        {
            if (report) MoonReport.Clear();
            for (int i = 0; i < 2; i++)
            {
                if (i >= _moons.Count) { _moonPos[i] = _moonCen[i] = _moonF[i] = _moonG[i] = _moonLook[i] = Vector4.zero; continue; }
                var m = _moons[i];
                float th = m.Theta0 + m.Omega * t;
                Vector3 c = m.A * (Mathf.Cos(th) * _sunEq + Mathf.Sin(th) * _east);               // from the planet's centre
                Vector3 rel = c + DistanceRadii * ToCentre;                                         // from the viewer (who is DistanceRadii * -ToCentre from the centre)
                Vector3 f = -(c - Vector3.Dot(c, Pole) * Pole);
                f.Normalize();
                Vector3 g = Vector3.Cross(f, Pole).normalized;
                float h = Vector3.Dot(c, SunDir), q = (c - h * SunDir).magnitude;
                float lit = h > 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - 0.012f, 1f + 0.012f, q));
                m.Pos = new Vector4(rel.x, rel.y, rel.z, m.Rm);
                m.Cen = new Vector4(c.x, c.y, c.z, m.Rm);
                m.F = new Vector4(f.x, f.y, f.z, 0f); m.G = new Vector4(g.x, g.y, g.z, 0f);
                m.Look = new Vector4(m.Def.AlbedoScale, lit, m.Map.width, 0f);
                _moonPos[i] = m.Pos; _moonCen[i] = m.Cen; _moonF[i] = m.F; _moonG[i] = m.G; _moonLook[i] = m.Look;
                if (report)
                {
                    var dirm = rel.normalized;
                    float az = Mathf.Atan2(dirm.x, dirm.z) * Mathf.Rad2Deg, el = Mathf.Asin(dirm.y) * Mathf.Rad2Deg;
                    MoonReport.Add($"{m.Def.Id}: {Mathf.Asin(m.Rm / rel.magnitude) * Mathf.Rad2Deg * 2f:0.00} degrees across at azimuth {az:0.0}, elevation {el:0.0}, {(lit >= 0.99f ? "lit" : lit <= 0.01f ? "in the planet's shadow" : "in the planet's shadow edge")}, goes round in {2f * Mathf.PI / Mathf.Max(m.Omega, 1e-9f) / 60f:0} minutes");
                }
            }
        }

        /// <summary>Fill a property block for one draw. <paramref name="pixelAngle"/>: the angle of one pixel, rad. <paramref name="bake"/>: for the lighting cubemap (the planet's clock held at zero).</summary>
        public void Push(MaterialPropertyBlock block, float pixelAngle, float seconds, bool bake)
        {
            float t = bake ? 0f : seconds + Options.StartSeconds;
            float pxPerRc = 1f / Mathf.Max(pixelAngle * Mathf.Sqrt(DistanceRadii * DistanceRadii - 1f), 1e-9f);
            block.SetVector(IdDir, new Vector4(ToCentre.x, ToCentre.y, ToCentre.z, DistanceRadii));
            block.SetVector(IdPole, _pole);
            block.SetVector(IdE1, _e1);
            block.SetVector(IdE2, _e2);
            block.SetVector(IdSun, _sun);
            block.SetVector(IdMotion, new Vector4(_spin, _windScale, _grsSpin, t));
            block.SetVector(IdMap0, _map0);
            block.SetVector(IdGrs, _grs);
            block.SetVector(IdLook, _look);
            block.SetVector(IdLook2, _look2);
            block.SetVector(IdPx, new Vector4(pixelAngle, 2f * Mathf.PI / Mathf.Max(Map.width, 1), pxPerRc, Options.Planetshine));
            block.SetVector(IdDetail, _detail);
            block.SetVectorArray(IdWinds, _windArray);
            block.SetTexture(IdMap, Map);
            block.SetVector(IdMoonCount, new Vector4(bake ? 0f : _moons.Count, 0f, 0f, 0f));
            if (!bake && _moons.Count > 0) PlaceMoons(t, false);
            block.SetVectorArray(IdMoonPos, _moonPos); block.SetVectorArray(IdMoonCen, _moonCen); block.SetVectorArray(IdMoonF, _moonF); block.SetVectorArray(IdMoonG, _moonG); block.SetVectorArray(IdMoonLook, _moonLook);
            block.SetTexture(IdMoonMap0, _moons.Count > 0 ? _moons[0].Map : Texture2D.whiteTexture);
            block.SetTexture(IdMoonMap1, _moons.Count > 1 ? _moons[1].Map : Texture2D.whiteTexture);
        }

        /// <summary>Tell a block there is no planet (the shader's guards read the distance).</summary>
        public static void PushNone(MaterialPropertyBlock block) { block.SetVector(IdDir, Vector4.zero); block.SetVector(IdMoonCount, Vector4.zero); }

        /// <summary>The fraction of the sky's width the planet's disc covers across a view of <paramref name="fovDeg"/> degrees: for the self-test and the docs.</summary>
        public float AngularDiameterDeg => 2f * AngularRadius * Mathf.Rad2Deg;
    }

    /// <summary>The volume parameter that carries a <see cref="BackdropState"/> to the sky renderer.</summary>
    [Serializable]
    public sealed class BackdropParameter : VolumeParameter<BackdropState>
    {
        public BackdropParameter(BackdropState value, bool overrideState = false) : base(value, overrideState) { }
    }
}
