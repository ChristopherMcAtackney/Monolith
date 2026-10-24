using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The placeholder sounds, built in code at start-up (no audio files): weighty and layered (a transient, a body, a low
    /// thump or a tail), each with a near version and a far one (dull, soft, late), mixed by distance. It sits behind
    /// <see cref="IJuiceAudio"/>, which the real audio system will implement instead. A pool of 3D voices plays them; the biggest moments duck the
    /// rest. Judge the weight of these by ear; the build log lists each clip's length, peak and loudness.
    /// </summary>
    public sealed class ProceduralAudio : IJuiceAudio
    {
        sealed class Cue
        {
            public JuiceCue Id;
            public AudioClip Near, Far;
            public float MinDistance = 60f, MaxDistance = 9000f;
            public float Gain = 1f;
            public bool Big;             // does not duck, and ducks the rest
            public bool Loop;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public float BaseVolume, BasePitch = 1f, Started, Priority;
            public bool Ducks, InUse, Looping;
            public float TargetGain, Fade;
        }

        sealed class LoopHandle : IJuiceLoop
        {
            public Voice V; public ProceduralAudio Owner; public float Gain, Master; public bool Stopping; public float StopRate; public Cue C;
            public void Set(Vector3 position, float gain) { if (V == null || !V.InUse) return; V.Source.transform.position = position; Gain = gain; }
            public void Stop(float fadeSeconds) { Stopping = true; StopRate = fadeSeconds > 0.01f ? 1f / fadeSeconds : 100f; }
        }

        readonly Dictionary<JuiceCue, Cue> _cues = new Dictionary<JuiceCue, Cue>();
        readonly List<Voice> _voices = new List<Voice>();
        readonly List<LoopHandle> _loops = new List<LoopHandle>();
        readonly Transform _root;
        readonly JuiceSettings _settings;
        float _duck, _duckLeft, _duckTotal;
        float _time;
        float _pitchScale = 1f;

        public float MasterVolume { get; set; } = 0.8f;
        public bool Enabled { get; set; } = true;
        public int ClipCount => _cues.Count;

        public ProceduralAudio(JuiceSettings settings)
        {
            _settings = settings;
            var go = new GameObject("JuiceAudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _root = go.transform;
            Build();
            for (int i = 0; i < 34; i++)
            {
                var vgo = new GameObject("voice");
                vgo.transform.SetParent(_root, false);
                var src = vgo.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 1f; src.dopplerLevel = 0f; src.rolloffMode = AudioRolloffMode.Logarithmic;
                _voices.Add(new Voice { Source = src });
            }
        }

        // ------------------------------------------------------------------------------------------------ playing

        public void Play(JuiceCue cue, Vector3 position, float gain = 1f, float distance = 0f)
        {
            if (!Enabled || gain <= 0.001f || !_cues.TryGetValue(cue, out var c)) return;
            // Near and far versions: the near one fades out between 250 and 1400 m, the far one in.
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.MinDistance * 3f, c.MinDistance * 22f, distance));
            float near = 1f - far * 0.92f;
            if (near > 0.02f && c.Near != null) Start(c, c.Near, position, gain * near, c.Big);
            if (far > 0.02f && c.Far != null) Start(c, c.Far, position, gain * Mathf.Lerp(0.55f, 1f, far) * far, c.Big);
        }

        void Start(Cue c, AudioClip clip, Vector3 position, float gain, bool big)
        {
            var v = Rent(gain);
            if (v == null) return;
            var s = v.Source;
            s.transform.position = position;
            s.clip = clip;
            s.loop = false;
            v.BasePitch = 1f + (float)(Hash(_time + gain) * 0.06 - 0.03);
            s.pitch = v.BasePitch * _pitchScale;
            s.minDistance = c.MinDistance; s.maxDistance = c.MaxDistance;
            v.BaseVolume = Mathf.Clamp01(gain * c.Gain); v.Ducks = !big; v.Started = _time; v.Priority = gain * (big ? 4f : 1f); v.InUse = true; v.Looping = false;
            s.volume = v.BaseVolume * MasterVolume * (v.Ducks ? 1f - _duck : 1f);
            s.Play();
        }

        static double Hash(float x) { double s = Math.Sin(x * 127.1) * 43758.5453; return s - Math.Floor(s); }

        Voice Rent(float priority)
        {
            Voice worst = null; float worstScore = float.MaxValue;
            foreach (var v in _voices)
            {
                if (!v.InUse || !v.Source.isPlaying) { v.InUse = false; return v; }
                float age = _time - v.Started;
                float score = v.Priority - age * 0.05f;
                if (!v.Looping && score < worstScore) { worstScore = score; worst = v; }
            }
            if (worst != null && worstScore < priority) { worst.Source.Stop(); worst.InUse = false; return worst; }
            return null;
        }

        public IJuiceLoop StartLoop(JuiceCue cue, Vector3 position, float gain = 1f)
        {
            if (!Enabled || !_cues.TryGetValue(cue, out var c) || c.Near == null) return new LoopHandle();
            var v = Rent(10f);
            if (v == null) return new LoopHandle();
            var s = v.Source;
            s.transform.position = position;
            v.BasePitch = 1f;
            s.clip = c.Near; s.loop = true; s.pitch = _pitchScale; s.minDistance = c.MinDistance; s.maxDistance = c.MaxDistance; s.volume = 0f;
            v.Ducks = false; v.InUse = true; v.Looping = true; v.Started = _time; v.Priority = 99f;
            s.Play();
            var h = new LoopHandle { V = v, Owner = this, Gain = gain, C = c };
            _loops.Add(h);
            return h;
        }

        public void SetTimeScale(float scale)
        {
            // Slow motion deepens the sound, but not all the way (a quarter speed is not a quarter pitch).
            _pitchScale = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(scale));
        }

        public void Duck(float amount, float seconds)
        {
            _duck = Mathf.Max(_duck, Mathf.Clamp01(amount));
            _duckLeft = Mathf.Max(_duckLeft, seconds); _duckTotal = Mathf.Max(_duckLeft, 0.01f);
        }

        public void Update(float dt)
        {
            _time += dt;
            if (_duckLeft > 0f)
            {
                _duckLeft -= dt;
                // Hold, then ease back.
                float k = Mathf.Clamp01(_duckLeft / _duckTotal);
                _duck = Mathf.Min(_duck, Mathf.SmoothStep(0f, _duck, Mathf.Clamp01(k * 3f)));
                if (_duckLeft <= 0f) _duck = 0f;
            }
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                var h = _loops[i];
                if (h.V == null) { _loops.RemoveAt(i); continue; }
                h.Master = Mathf.MoveTowards(h.Master, h.Stopping ? 0f : 1f, dt * (h.Stopping ? h.StopRate : 6f));
                h.V.Source.volume = Mathf.Clamp01(h.Gain * h.C.Gain) * h.Master * MasterVolume;
                if (h.Stopping && h.Master <= 0.001f)
                {
                    h.V.Source.Stop(); h.V.InUse = false; h.V.Looping = false; _loops.RemoveAt(i);
                }
            }
            foreach (var v in _voices)
            {
                if (v.InUse) v.Source.pitch = v.BasePitch * _pitchScale;
                if (!v.InUse || v.Looping) continue;
                v.Source.volume = v.BaseVolume * MasterVolume * (v.Ducks ? 1f - _duck : 1f);
            }
        }

        // ------------------------------------------------------------------------------------------------ building

        AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, Synth.Rate, false);
            c.SetData(samples, 0);
            return c;
        }

        readonly Dictionary<JuiceCue, float[]> _raw = new Dictionary<JuiceCue, float[]>();
        public IReadOnlyDictionary<JuiceCue, float[]> RawSamples => _raw;

        void Add(JuiceCue id, float[] near, float peak, float minDist, float gain, bool big = false, bool makeFar = true, bool loop = false)
        {
            Synth.Edges(near);
            Synth.Normalize(near, peak);
            var c = new Cue { Id = id, MinDistance = minDist, Gain = gain, Big = big, Loop = loop, Near = Clip(id + "_near", near) };
            _raw[id] = near;
            if (makeFar && !loop)
            {
                var far = Synth.Far(near, big ? 1.2f : 0.5f);
                Synth.Normalize(far, peak * 0.9f);
                c.Far = Clip(id + "_far", far);
                _raw[(JuiceCue)(1000 + (int)id)] = far;
            }
            _cues[id] = c;
        }

        void Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Add(JuiceCue.LaserLight, LaserLight(), 0.8f, 50f, 0.55f);
            Add(JuiceCue.LaserEnemy, LaserEnemy(), 0.8f, 50f, 0.55f);
            Add(JuiceCue.LaserHeavy, LaserHeavy(), 0.85f, 120f, 0.8f);
            Add(JuiceCue.FlakShell, FlakShell(), 0.8f, 60f, 0.5f);
            Add(JuiceCue.MissileLaunch, MissileLaunch(), 0.8f, 80f, 0.65f);
            Add(JuiceCue.HitShield, HitShield(false), 0.8f, 70f, 0.7f);
            Add(JuiceCue.HitHull, HitHull(false), 0.85f, 70f, 0.75f);
            Add(JuiceCue.HitPlayerShield, HitShield(true), 0.9f, 30f, 0.85f, makeFar: false);
            Add(JuiceCue.HitPlayerHull, HitHull(true), 0.95f, 30f, 0.95f, makeFar: false);
            Add(JuiceCue.ExplosionSmall, Explosion(1.7f, 0.7f, 70f, 6000f, 11), 0.9f, 100f, 0.85f);
            Add(JuiceCue.ExplosionMedium, Explosion(2.8f, 1.0f, 50f, 4500f, 12), 0.95f, 220f, 0.95f, big: true);
            Add(JuiceCue.ExplosionLarge, ExplosionLarge(), 1.0f, 900f, 1.0f, big: true);
            Add(JuiceCue.Shockwave, Shockwave(), 0.95f, 400f, 1.0f, big: true, makeFar: false);
            Add(JuiceCue.BreakUp, BreakUp(), 1.0f, 900f, 1.0f, big: true);
            Add(JuiceCue.SubsystemBlast, Explosion(2.0f, 0.85f, 62f, 5000f, 13), 0.92f, 150f, 0.9f);
            Add(JuiceCue.FlakBurst, FlakBurst(), 0.85f, 90f, 0.7f);
            Add(JuiceCue.NearMiss, NearMiss(), 0.8f, 25f, 0.8f, makeFar: false);
            Add(JuiceCue.BeamCharge, BeamCharge(), 0.9f, 700f, 0.9f, big: true);
            Add(JuiceCue.BeamLoop, BeamLoop(), 0.5f, 500f, 0.9f, makeFar: false, loop: true);
            Add(JuiceCue.BeamImpactLoop, BeamImpact(), 0.5f, 300f, 0.7f, makeFar: false, loop: true);
            Add(JuiceCue.UiClick, UiClick(), 0.5f, 10f, 0.4f, makeFar: false);
            MnLog.Info($"JUICE audio: {_cues.Count} placeholder cues built in {sw.ElapsedMilliseconds} ms");
        }

        // ---- each sound (see the design: weighty, layered: a crack, a body, a low thump or a tail) -------------------------------

        static float[] LaserLight()
        {
            var b = Synth.Buf(0.30f);
            Synth.AddSine(b, t => 2300f * Mathf.Exp(-t * 17f) + 330f, t => 0.8f * Synth.Hit(t, 11f));
            Synth.AddSaw(b, t => 1150f * Mathf.Exp(-t * 15f) + 165f, t => 0.35f * Synth.Hit(t, 13f), 0.2f);
            Synth.AddNoise(b, t => 0.55f * Synth.Hit(t, 130f, 0.0004f), 3);                       // the crack
            Synth.AddSine(b, t => 140f * Mathf.Exp(-t * 20f) + 52f, t => 0.7f * Synth.Hit(t, 16f, 0.002f)); // the thump
            return b;
        }

        static float[] LaserEnemy()
        {
            var b = Synth.Buf(0.34f);
            Synth.AddSaw(b, t => 1400f * Mathf.Exp(-t * 11f) + 190f, t => 0.7f * Synth.Hit(t, 9f), 0.45f);
            Synth.AddSine(b, t => 85f, t => 0.5f * Synth.Hit(t, 14f));
            Synth.AddNoise(b, t => 0.5f * Synth.Hit(t, 110f, 0.0004f), 5);
            Synth.AddSine(b, t => 110f * Mathf.Exp(-t * 14f) + 44f, t => 0.8f * Synth.Hit(t, 12f, 0.002f));
            Synth.Shape(b, t => 1f - 0.35f * Mathf.Sin(t * 85f * Synth.TwoPi) * Mathf.Sin(t * 85f * Synth.TwoPi));       // grit
            return b;
        }

        static float[] LaserHeavy()
        {
            var b = Synth.Buf(0.95f);
            Synth.AddSaw(b, t => 780f * Mathf.Exp(-t * 4.5f) + 82f, t => 0.55f * Synth.Hit(t, 4.2f, 0.004f), 0.1f);
            Synth.AddSaw(b, t => 790f * Mathf.Exp(-t * 4.5f) + 85f, t => 0.45f * Synth.Hit(t, 4.2f, 0.004f), 0.1f);
            Synth.AddSine(b, t => 90f * Mathf.Exp(-t * 6f) + 38f, t => 1.0f * Synth.Hit(t, 3.4f, 0.004f));
            var n = Synth.Buf(0.95f);
            Synth.AddNoise(n, t => Synth.Hit(t, 22f, 0.001f), 9);
            Synth.LowPass(n, t => 2600f * Mathf.Exp(-t * 6f) + 300f);
            Synth.Mix(b, n, 0.9f);
            Synth.SoftClip(b, 1.3f);
            return b;
        }

        static float[] FlakShell()
        {
            var b = Synth.Buf(0.34f);
            Synth.AddSine(b, t => 120f * Mathf.Exp(-t * 14f) + 58f, t => 0.9f * Synth.Hit(t, 11f, 0.002f));
            var n = Synth.Buf(0.34f);
            Synth.AddNoise(n, t => Synth.Hit(t, 25f, 0.002f), 21);
            Synth.LowPass(n, t => 1500f * Mathf.Exp(-t * 9f) + 200f);
            Synth.Mix(b, n, 0.8f);
            return b;
        }

        static float[] MissileLaunch()
        {
            var b = Synth.Buf(1.5f);
            var n = Synth.Buf(1.5f);
            Synth.AddNoise(n, t => Mathf.Min(1f, t / 0.12f) * Mathf.Exp(-t * 1.8f), 31);
            Synth.BandPass(n, t => 280f + 1900f * Mathf.Clamp01(t / 0.9f), 1.1f);
            Synth.Mix(b, n, 1.6f);
            Synth.AddSine(b, t => 95f * Mathf.Exp(-t * 6f) + 40f, t => 1.0f * Synth.Hit(t, 5f, 0.003f));
            Synth.AddSaw(b, t => 70f + 40f * t, t => 0.18f * Mathf.Min(1f, t / 0.2f) * Mathf.Exp(-t * 1.6f), 0.3f);
            return b;
        }

        static float[] HitShield(bool onPlayer)
        {
            float len = onPlayer ? 0.8f : 0.6f;
            var b = Synth.Buf(len);
            Synth.AddSine(b, t => 2100f * Mathf.Exp(-t * 6.5f) + 520f, t => 0.7f * Synth.Hit(t, onPlayer ? 5.5f : 7f) * (1f + 0.5f * Mathf.Sin(t * 58f * Synth.TwoPi)));
            Synth.AddSine(b, t => 3150f * Mathf.Exp(-t * 7f) + 780f, t => 0.3f * Synth.Hit(t, 8f));
            var n = Synth.Buf(len);
            Synth.AddNoise(n, t => Synth.Hit(t, 9f, 0.001f), 41);
            Synth.HighPass(n, t => 2500f);
            Synth.Mix(b, n, 0.5f);
            Synth.AddSine(b, t => 150f * Mathf.Exp(-t * 18f) + 62f, t => (onPlayer ? 1.0f : 0.5f) * Synth.Hit(t, onPlayer ? 8f : 16f, 0.002f));
            return b;
        }

        static float[] HitHull(bool onPlayer)
        {
            float len = onPlayer ? 0.9f : 0.55f;
            var b = Synth.Buf(len);
            Synth.AddNoise(b, t => 1.0f * Synth.Hit(t, 190f, 0.0003f), 51);                   // the crack
            var ping = Synth.Buf(len);
            float[] modes = { 880f, 1430f, 2240f, 3130f };
            for (int i = 0; i < modes.Length; i++)
            {
                float f = modes[i] * (1f + 0.011f * i); float k = 14f + 9f * i;
                Synth.AddSine(ping, t => f, t => 0.5f / (1f + i) * Synth.Hit(t, k), i);
            }
            Synth.Mix(b, ping, 0.8f);
            Synth.AddSine(b, t => 125f * Mathf.Exp(-t * 20f) + 46f, t => (onPlayer ? 1.4f : 0.9f) * Synth.Hit(t, onPlayer ? 9f : 16f, 0.002f));
            if (onPlayer)
            {
                // The hull rattles round you.
                Synth.AddCrackle(b, t => 90f * Mathf.Exp(-t * 4f), t => 0.5f * Mathf.Exp(-t * 3f), 57, 2.5f);
            }
            return b;
        }

        static float[] Explosion(float len, float weight, float subHz, float topHz, int seed)
        {
            var b = Synth.Buf(len);
            // Body: noise whose brightness collapses (the fireball dimming), shaped to swell then die.
            var body = Synth.Buf(len);
            Synth.AddNoise(body, t => Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * (3.4f / weight)), seed);
            Synth.LowPass(body, t => topHz * Mathf.Exp(-t * 4.5f) + 220f);
            Synth.Mix(b, body, 1.1f);
            // The crack.
            var crack = Synth.Buf(0.12f);
            Synth.AddNoise(crack, t => Synth.Hit(t, 60f, 0.0002f), seed + 100);
            Synth.HighPass(crack, t => 1200f);
            Synth.Mix(b, crack, 0.9f);
            // Sub boom, falling in pitch.
            Synth.AddSine(b, t => subHz * Mathf.Exp(-t * 0.9f) + subHz * 0.45f, t => 1.35f * weight * Synth.Hit(t, 3.2f / weight, 0.004f));
            // Debris crackle.
            Synth.AddCrackle(b, t => 140f * Mathf.Exp(-t * 1.6f), t => 0.35f * Mathf.Exp(-t * 2.2f), seed + 200, 2f);
            Synth.SoftClip(b, 1.35f);
            return b;
        }

        static float[] ExplosionLarge()
        {
            float len = 6.5f;
            var b = Synth.Buf(len);
            var body = Synth.Buf(len);
            Synth.AddNoise(body, t => Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t * 0.85f) * (0.72f + 0.28f * Mathf.Sin(t * 7.3f * Synth.TwoPi)), 61);
            Synth.LowPass(body, t => 3400f * Mathf.Exp(-t * 1.3f) + 110f);
            Synth.Mix(b, body, 1.2f);
            var crack = Synth.Buf(0.2f);
            Synth.AddNoise(crack, t => Synth.Hit(t, 28f, 0.0002f), 62);
            Synth.HighPass(crack, t => 800f);
            Synth.Mix(b, crack, 1.0f);
            Synth.AddSine(b, t => 46f * Mathf.Exp(-t * 0.55f) + 17f, t => 1.9f * Synth.Hit(t, 0.95f, 0.01f));
            Synth.AddSine(b, t => 70f * Mathf.Exp(-t * 0.8f) + 30f, t => 1.1f * Synth.Hit(t, 1.3f, 0.01f));
            // The ship groaning as it goes: a slow saw dragged down.
            Synth.AddSaw(b, t => 62f * Mathf.Exp(-t * 0.35f) + 24f, t => 0.35f * Mathf.Min(1f, t / 0.3f) * Mathf.Exp(-t * 0.7f), 0.2f);
            Synth.AddCrackle(b, t => 220f * Mathf.Exp(-t * 0.8f), t => 0.4f * Mathf.Exp(-t * 1.0f), 63, 3f);
            Synth.SoftClip(b, 1.3f);
            return b;
        }

        static float[] Shockwave()
        {
            var b = Synth.Buf(3.0f);
            Synth.AddSine(b, t => 38f * Mathf.Exp(-t * 0.6f) + 20f, t => 2.0f * Synth.Hit(t, 1.2f, 0.03f));
            var n = Synth.Buf(3.0f);
            Synth.AddNoise(n, t => Mathf.Sin(Mathf.Clamp01(t / 2.2f) * Mathf.PI) , 71);
            Synth.LowPass(n, t => 260f - 80f * t);
            Synth.Mix(b, n, 1.4f);
            Synth.SoftClip(b, 1.2f);
            return b;
        }

        static float[] BreakUp()
        {
            var b = Synth.Buf(5.5f);
            var tear = Synth.Buf(5.5f);
            Synth.AddNoise(tear, t => Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t * 0.9f) * (0.6f + 0.4f * Mathf.Sin(t * 11f * Synth.TwoPi)), 81);
            Synth.BandPass(tear, t => 900f * Mathf.Exp(-t * 0.7f) + 160f, 0.9f);
            Synth.Mix(b, tear, 2.2f);
            var crack = Synth.Buf(0.25f);
            Synth.AddNoise(crack, t => Synth.Hit(t, 22f, 0.0002f), 82);
            Synth.Mix(b, crack, 1.1f);
            Synth.AddSine(b, t => 52f * Mathf.Exp(-t * 0.5f) + 22f, t => 1.9f * Synth.Hit(t, 0.8f, 0.01f));
            for (int i = 0; i < 4; i++)
            {
                float f0 = 95f + 37f * i, start = 0.15f * i;
                Synth.AddSaw(b, t => (f0 * Mathf.Exp(-(t - start) * 0.6f) + 30f), t => t < start ? 0f : 0.22f * Synth.Hit(t - start, 1.1f, 0.05f), 0.4f);
            }
            Synth.AddCrackle(b, t => 260f * Mathf.Exp(-t * 0.7f), t => 0.5f * Mathf.Exp(-t * 0.9f), 83, 3f);
            Synth.SoftClip(b, 1.4f);
            return b;
        }

        static float[] FlakBurst()
        {
            var b = Synth.Buf(0.9f);
            Synth.AddSine(b, t => 115f * Mathf.Exp(-t * 11f) + 52f, t => 1.1f * Synth.Hit(t, 9f, 0.002f));
            var n = Synth.Buf(0.9f);
            Synth.AddNoise(n, t => Synth.Hit(t, 12f, 0.0005f), 91);
            Synth.BandPass(n, t => 2600f * Mathf.Exp(-t * 5f) + 500f, 0.8f);
            Synth.Mix(b, n, 1.1f);
            Synth.AddCrackle(b, t => 300f * Mathf.Exp(-t * 6f), t => 0.5f * Mathf.Exp(-t * 6f), 92, 1.2f);
            return b;
        }

        static float[] NearMiss()
        {
            var b = Synth.Buf(0.75f);
            var n = Synth.Buf(0.75f);
            Synth.AddNoise(n, t => Mathf.Sin(Mathf.Clamp01(t / 0.5f) * Mathf.PI), 101);
            Synth.BandPass(n, t => 4200f * Mathf.Exp(-t * 4.4f) + 380f, 1.6f);
            Synth.Mix(b, n, 1.4f);
            Synth.AddSine(b, t => 640f * Mathf.Exp(-t * 3.5f) + 190f, t => 0.25f * Mathf.Sin(Mathf.Clamp01(t / 0.55f) * Mathf.PI));
            return b;
        }

        static float[] BeamCharge()
        {
            float len = 2.5f;
            var b = Synth.Buf(len);
            // A rising whine and a growing rumble, cut off when the beam fires.
            Synth.AddSine(b, t => 70f * Mathf.Pow(10f, t / len), t => 0.5f * Mathf.Pow(t / len, 2f) + 0.05f);
            Synth.AddSaw(b, t => 55f * Mathf.Pow(8f, t / len), t => 0.35f * Mathf.Pow(t / len, 2.5f), 0.2f);
            var n = Synth.Buf(len);
            Synth.AddNoise(n, t => Mathf.Pow(t / len, 3f), 111);
            Synth.BandPass(n, t => 400f + 3200f * Mathf.Pow(t / len, 2f), 0.9f);
            Synth.Mix(b, n, 1.0f);
            Synth.AddSine(b, t => 36f + 12f * t / len, t => 1.0f * Mathf.Min(1f, t / 0.6f) * (0.4f + 0.6f * t / len));
            Synth.SoftClip(b, 1.2f);
            return b;
        }

        static float[] BeamLoop()
        {
            // Frequencies are multiples of 0.5 Hz so the 2 s loop closes on itself.
            float len = 2.0f;
            var b = Synth.Buf(len);
            float[] f = { 55f, 55.5f, 82.5f, 110f, 165.5f };
            float[] g = { 0.9f, 0.7f, 0.5f, 0.45f, 0.25f };
            for (int i = 0; i < f.Length; i++)
            {
                float fi = f[i], gi = g[i];
                Synth.AddSaw(b, t => fi, t => gi * (1f + 0.22f * Mathf.Sin(t * 8f * Synth.TwoPi + i)), 0.15f);
            }
            var n = Synth.Buf(len);
            Synth.AddNoise(n, t => 1f, 121);
            Synth.BandPass(n, t => 520f + 380f * Mathf.Sin(t * 3f * Synth.TwoPi), 0.7f);
            Synth.Shape(n, t => 0.7f + 0.3f * Mathf.Sin(t * 10f * Synth.TwoPi));
            Synth.Mix(b, n, 1.3f);
            Synth.AddSine(b, t => 36f, t => 0.8f);
            Synth.LowPass(b, t => 1700f);
            var loop = Synth.SeamlessLoop(b, 0.12f);
            Synth.SoftClip(loop, 1.4f);
            return loop;
        }

        static float[] BeamImpact()
        {
            var b = Synth.Buf(1.6f);
            Synth.AddNoise(b, t => 1f, 131);
            Synth.HighPass(b, t => 1800f);
            Synth.Shape(b, t => 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 37f * Synth.TwoPi / 2f)));
            Synth.AddCrackle(b, t => 160f, t => 0.7f, 132, 1.8f);
            Synth.AddSine(b, t => 62f, t => 0.35f);
            return Synth.SeamlessLoop(b, 0.1f);
        }

        static float[] UiClick()
        {
            var b = Synth.Buf(0.07f);
            Synth.AddSine(b, t => 1700f, t => Synth.Hit(t, 70f, 0.0005f));
            Synth.AddNoise(b, t => 0.3f * Synth.Hit(t, 250f, 0.0002f), 141);
            return b;
        }

        // ------------------------------------------------------------------------------------------------ the check

        /// <summary>Write every clip as a 16-bit WAV into <paramref name="dir"/> and log its length, peak and loudness (for checking them without ears).</summary>
        public void Dump(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var kv in _raw)
            {
                string name = kv.Key >= (JuiceCue)1000 ? ((JuiceCue)((int)kv.Key - 1000)) + "_far" : kv.Key + "_near";
                var s = kv.Value;
                WriteWav(Path.Combine(dir, name + ".wav"), s);
                float peak = Synth.Peak(s), rms = Synth.Rms(s);
                MnLog.Info($"SOUND {name}: {s.Length / (float)Synth.Rate:0.00} s, peak {20f * Mathf.Log10(Mathf.Max(peak, 1e-6f)):0.0} dBFS, rms {20f * Mathf.Log10(Mathf.Max(rms, 1e-6f)):0.0} dBFS");
            }
        }

        static void WriteWav(string path, float[] s)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int n = s.Length;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Synth.Rate); w.Write(Synth.Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
                for (int i = 0; i < n; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s[i] * 32767f), -32768, 32767));
            }
        }
    }
}
