using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin's lights: dim instrument glow (the displays and their lamps), emergency lighting (red, from strips in the ceiling and the floor), the Sun through the
    /// canopy (a spot of the cabin's own, shadowed by the frame: the Sun itself does not reach the cabin, which sits on its own rendering layer) and a scanner's sweep
    /// crawling through the glass. Every light is on the cabin's layer, so nothing else in the scene is lit by it and nothing else in the scene (explosions, the Sun) lights the
    /// cabin: hits and blasts make the lights flicker instead.
    /// </summary>
    public sealed class CabinLights
    {
        public const LightLayerEnum Layer = LightLayerEnum.LightLayer1;

        sealed class Item
        {
            public CabinLayout.LightDef Def;
            public Light Light;
            public HDAdditionalLightData Hd;
            public float Level, Phase;
        }

        static readonly bool NoShadows = CommandLine.Has("-mnCabinNoShadows");
        readonly List<Item> _items = new List<Item>();
        readonly CabinConfig _cfg;
        readonly Transform _root;
        readonly Light _sun, _scan, _knit;
        readonly HDAdditionalLightData _sunHd, _scanHd, _knitHd;
        readonly System.Random _rng = new System.Random(77);
        float _flicker;

        public CabinLights(Transform cabinRoot, CabinLayout layout, CabinConfig cfg)
        {
            _cfg = cfg; _root = cabinRoot;
            var holder = new GameObject("CabinLights");
            holder.transform.SetParent(cabinRoot, false);
            foreach (var d in layout.Lights)
            {
                var go = new GameObject("LIGHT_" + d.Name);
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = d.Pos;
                var hd = go.AddHDLight(d.Kind == "spot" ? HDLightTypeAndShape.ConeSpot : HDLightTypeAndShape.Point);
                var light = go.GetComponent<Light>();
                light.color = d.Colour;
                light.range = d.Range;
                hd.shapeRadius = 0.02f;
                hd.affectsVolumetric = false;
                hd.lightlayersMask = Layer;
                hd.EnableShadows(d.Shadow && !NoShadows);
                if (d.Shadow) SetShadow(hd, 512);
                if (d.Kind == "spot")
                {
                    go.transform.localRotation = Quaternion.LookRotation(d.SpotDir.sqrMagnitude > 0f ? d.SpotDir : Vector3.down);
                    hd.SetSpotAngle(d.SpotAngle, 60f);
                }
                hd.SetIntensity(0.001f, LightUnit.Candela);
                _items.Add(new Item { Def = d, Light = light, Hd = hd, Phase = (float)_rng.NextDouble() * 10f });
            }
            _sun = MakeSpot("SunThroughCanopy", out _sunHd, 74f, 2048);
            _scan = MakeSpot("ScanSweep", out _scanHd, 62f, 1024);
            var kg = new GameObject("KnitGlow");
            kg.transform.SetParent(cabinRoot, false);
            _knitHd = kg.AddHDLight(HDLightTypeAndShape.Point);
            _knit = kg.GetComponent<Light>();
            _knit.range = 1.8f;
            _knit.color = new Color(0.35f, 0.95f, 0.82f);
            _knitHd.shapeRadius = 0.03f;
            _knitHd.affectsVolumetric = false;
            _knitHd.lightlayersMask = Layer;
            _knitHd.EnableShadows(false);
            _knitHd.SetIntensity(0.001f, LightUnit.Candela);
            _knit.enabled = false;
        }

        Light MakeSpot(string name, out HDAdditionalLightData hd, float angle, int res)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            hd = go.AddHDLight(HDLightTypeAndShape.ConeSpot);
            var l = go.GetComponent<Light>();
            l.range = 14f;
            l.color = Color.white;
            hd.SetSpotAngle(angle, 80f);
            hd.shapeRadius = 0.02f;
            hd.affectsVolumetric = false;
            hd.lightlayersMask = Layer;
            hd.EnableShadows(!NoShadows);
            SetShadow(hd, res);
            hd.SetIntensity(0.001f, LightUnit.Candela);
            l.enabled = false;
            return l;
        }

        static void SetShadow(HDAdditionalLightData hd, int res)
        {
            hd.shadowResolution.@override = res;
            hd.shadowResolution.useOverride = true;
        }

        /// <summary>The Sun through the glass: <paramref name="sunDirCabin"/> points from the cabin toward the Sun (cabin axes); <paramref name="strength"/> 0..1.</summary>
        /// <summary>Candela that lights a mid-grey (0.3) surface at <paramref name="metres"/> to <paramref name="units"/> on screen at the eye's current exposure.</summary>
        public static float Candela(float units, float metres, float nitsPerUnit) => units * nitsPerUnit * Mathf.PI / 0.3f * metres * metres;

        public void SetSun(Vector3 sunDirCabin, float strength, float nitsPerUnit)
        {
            bool on = strength > 0.01f && sunDirCabin.sqrMagnitude > 0.5f;
            _sun.enabled = on;
            if (!on) return;
            const float dist = 4.5f;
            Vector3 centre = new Vector3(0f, 1.25f, -0.2f);
            _sun.transform.localPosition = centre + sunDirCabin.normalized * dist;
            _sun.transform.localRotation = Quaternion.LookRotation(-sunDirCabin.normalized, Mathf.Abs(sunDirCabin.y) > 0.95f ? Vector3.forward : Vector3.up);
            _sunHd.SetIntensity(Candela(_cfg.light.canopySunUnits, dist, nitsPerUnit) * strength, LightUnit.Candela);
            _sun.color = new Color(1f, 0.95f, 0.86f);
        }

        /// <summary>The glow of a module knitting back into its socket: a small light in front of the panel (cabin axes), lighting the wall, strength 0..1.</summary>
        public void SetKnit(Vector3 localPos, float strength, float nitsPerUnit)
        {
            bool on = strength > 0.01f;
            _knit.enabled = on;
            if (!on) return;
            _knit.transform.localPosition = localPos;
            _knitHd.SetIntensity(Candela(_cfg.light.knitUnits, 0.55f, nitsPerUnit) * strength, LightUnit.Candela);
        }

        /// <summary>A scanner's beam coming through the canopy from a direction (cabin axes), at a strength 0..1.</summary>
        public void SetScan(Vector3 dirCabin, float strength, float nitsPerUnit)
        {
            bool on = strength > 0.01f && dirCabin.sqrMagnitude > 0.5f;
            _scan.enabled = on;
            if (!on) return;
            const float dist = 4.5f;
            Vector3 centre = new Vector3(0f, 1.25f, -0.3f);
            _scan.transform.localPosition = centre + dirCabin.normalized * dist;
            _scan.transform.localRotation = Quaternion.LookRotation(-dirCabin.normalized, Mathf.Abs(dirCabin.y) > 0.95f ? Vector3.forward : Vector3.up);
            _scanHd.SetIntensity(Candela(_cfg.scan.units, dist, nitsPerUnit) * strength, LightUnit.Candela);
            _scan.color = new Color(0.82f, 0.92f, 1f);
        }

        /// <summary>Set each group's target level from the cabin's lighting state and ease toward it; <paramref name="jolt"/> (0..1) flickers everything.</summary>
        public void Update(float dt, string state, float jolt, bool visible, float now, float nitsPerUnit)
        {
            foreach (var it in _items)
            {
                float want = Target(it.Def.Group, state, now + it.Phase);
                it.Level = Mathf.MoveTowards(it.Level, want, dt * 6f);
                float k = it.Level;
                if (jolt > 0.05f && it.Def.Group != "emergency") k *= 1f - jolt * (float)_rng.NextDouble() * 0.9f;
                if (state == "dark" && it.Def.Group == "instrument") k *= 0.8f + 0.2f * Mathf.Sin(now * 31f + it.Phase);
                bool on = visible && k > 0.002f;
                if (it.Light.enabled != on) it.Light.enabled = on;
                if (on) it.Hd.SetIntensity(Candela(it.Def.Glow, 1f, nitsPerUnit) * k * Scale(it.Def.Group), LightUnit.Candela);
            }
        }

        float Scale(string group) => group == "instrument" ? _cfg.light.instrumentScale : group == "emergency" ? _cfg.light.emergencyScale : _cfg.light.workScale;

        static float Target(string group, string state, float t)
        {
            switch (group)
            {
                case "instrument":
                    return state == "normal" ? 1f : state == "dark" ? 0.25f : state == "emergency" ? 0.45f : state == "alert" ? 0.45f : 0f;
                case "emergency":
                    if (state == "normal") return 0f;
                    if (state == "dark") return 0.10f;
                    if (state == "alert") return 0.25f + 0.75f * Mathf.Clamp01(0.5f + 0.7f * Mathf.Sin(t * 6f));
                    if (state == "emergency") return 1f;
                    return 0.45f;           // blackout: the emergency glow, dim
                default:
                    return 0f;              // the work light stays off: the cabin is lit by its instruments and what the windows let in
            }
        }
    }
}
