using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Juice
{
    /// <summary>
    /// A handful of HDRP point lights for the light that blasts, hits and beams throw on the hulls around them. Flashes fade by
    /// themselves; a held light (a beam's impact point) is kept alive by calling <see cref="Hold"/> each frame. Intensity is in
    /// candela: a surface d metres away gets I / d squared lux, and the sun is 100,000 lux, so a blast that should light a hull
    /// like the sun at 300 m needs about 9e9.
    /// </summary>
    public sealed class LightPool
    {
        sealed class Entry
        {
            public Light Light;
            public HDAdditionalLightData Hd;
            public float Age, Life, Peak, FadeExp;
            public Color Start, End;
            public bool Ramp;
            public int Key;
            public float HeldFor;
            public bool Used;
        }

        readonly List<Entry> _all = new List<Entry>();
        readonly Dictionary<int, Entry> _held = new Dictionary<int, Entry>();
        readonly Transform _root;
        readonly int _max;
        public bool Enabled = true;

        public int Active { get { int n = 0; foreach (var e in _all) if (e.Used) n++; return n; } }

        public LightPool(Transform parent, int max = 14)
        {
            _max = max;
            var go = new GameObject("JuiceLights");
            go.transform.SetParent(parent, false);
            _root = go.transform;
        }

        Entry Rent()
        {
            foreach (var e in _all) if (!e.Used) return e;
            if (_all.Count < _max)
            {
                var go = new GameObject("JuiceLight");
                go.transform.SetParent(_root, false);
                var hd = go.AddHDLight(HDLightTypeAndShape.Point);
                hd.EnableShadows(false);
                hd.affectsVolumetric = false;
                var e = new Entry { Light = go.GetComponent<Light>(), Hd = hd };
                go.SetActive(false);
                _all.Add(e);
                return e;
            }
            // Full: steal the one with the least left to give.
            Entry best = null; float least = float.MaxValue;
            foreach (var e in _all)
            {
                if (e.HeldFor > 0f) continue;
                float left = e.Peak * Mathf.Pow(Mathf.Clamp01(1f - e.Age / e.Life), e.FadeExp);
                if (left < least) { least = left; best = e; }
            }
            return best;
        }

        /// <summary>A flash: starts at <paramref name="candela"/> and fades over <paramref name="life"/> seconds.</summary>
        public void Flash(Vector3 position, Color colour, float candela, float range, float life, float fadeExp = 2f, Color? endColour = null, float shapeRadius = 0f)
        {
            if (!Enabled || candela <= 0f || life <= 0f) return;
            var e = Rent();
            if (e == null) return;
            Set(e, position, colour, candela, range);
            // A light with a size gives a hull a broad, bright highlight (a point gives a pinprick a rough plate hides); a colour ramp lets a blast start white and go amber.
            e.Hd.shapeRadius = Mathf.Max(0.025f, shapeRadius);
            e.Start = colour; e.End = endColour ?? colour; e.Ramp = endColour.HasValue;
            e.Age = 0f; e.Life = life; e.Peak = candela; e.FadeExp = fadeExp; e.Key = 0; e.HeldFor = 0f; e.Used = true;
            e.Light.gameObject.SetActive(true);
        }

        /// <summary>A light that follows something: call every frame with the same key; it goes out a moment after the last call.</summary>
        public void Hold(int key, Vector3 position, Color colour, float candela, float range)
        {
            if (!Enabled) return;
            if (!_held.TryGetValue(key, out var e) || !e.Used || e.Key != key)
            {
                e = Rent();
                if (e == null) return;
                _held[key] = e;
                e.Key = key;
                e.Used = true;
                e.Light.gameObject.SetActive(true);
            }
            Set(e, position, colour, candela, range);
            e.Age = 0f; e.Life = 1e9f; e.Peak = candela; e.FadeExp = 0f; e.HeldFor = 0.12f;
        }

        void Set(Entry e, Vector3 position, Color colour, float candela, float range)
        {
            e.Light.transform.position = position;
            e.Hd.SetColor(colour, -1f);
            e.Hd.SetIntensity(candela, LightUnit.Candela);
            e.Hd.SetRange(range);
        }

        public void Update(float dt)
        {
            foreach (var e in _all)
            {
                if (!e.Used) continue;
                if (e.HeldFor > 0f)
                {
                    e.HeldFor -= Mathf.Max(dt, 1f / 120f);
                    if (e.HeldFor <= 0f) { Kill(e); }
                    continue;
                }
                e.Age += dt;
                if (e.Age >= e.Life) { Kill(e); continue; }
                float k = Mathf.Pow(1f - e.Age / e.Life, e.FadeExp);
                e.Hd.SetIntensity(e.Peak * k, LightUnit.Candela);
                if (e.Ramp) e.Hd.SetColor(Color.Lerp(e.Start, e.End, Mathf.Clamp01(e.Age / (e.Life * 0.45f))), -1f);
            }
        }

        void Kill(Entry e)
        {
            e.Used = false;
            e.Key = 0;
            e.Light.gameObject.SetActive(false);
        }

        public void Clear() { foreach (var e in _all) if (e.Used) Kill(e); }
    }
}
