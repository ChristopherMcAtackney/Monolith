using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Juice
{
    /// <summary>
    /// The juice system: listens for combat events (<see cref="CombatEvents"/>) and turns them into light, fire, sparks, debris,
    /// camera shake, pad rumble and sound, without anything that raises an event knowing how. Build it once for a scene with
    /// <see cref="Create"/>; ships that should break up on screen register their visual with <see cref="RegisterShip"/>.
    /// Everything it draws goes through one HDRP custom pass in the HDR buffer before post-processing, so the camera's bloom does the glow.
    /// Settings (intensity, switches) come from <see cref="JuiceSettings"/>.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class JuiceDirector : MonoBehaviour
    {
        /// <summary>The exposure the effect colours are written for (the sky's fixed exposure): 1 display unit is 1.2 x 2^EV nits.</summary>
        public const float ReferenceEv100 = 14f;

        public JuiceContext Context { get; private set; }
        public JuiceSettings Settings => Context.Settings;
        public Trauma Trauma => Context.Trauma;
        public RumbleOut Rumble => Context.Rumble;
        public IJuiceAudio Audio => Context.Audio;

        /// <summary>
        /// The game speed the host wants (1 normally; the sandbox's slow-motion and pause). When <see cref="OwnsTimeScale"/> is on the
        /// director sets the sim's time scale to this times the hit-stop factor every frame.
        /// </summary>
        public float BaseTimeScale = 1f;
        public bool OwnsTimeScale;

        Projectiles _projectiles;
        HitEffects _hits;
        Explosions _explosions;
        Beams _beams;
        Debris _debris;
        Shockwaves _waves;
        Emitters _fx;
        BoostFx _boost;
        BrakeFx _brake;
        DeathFx _death;
        Feel _feel;
        FxFrame _frame;
        CustomPassVolume _volume;
        CameraShake _shake;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        float _fxTimeReal;
        bool _built;

        /// <summary>One-line state for the panel and the log.</summary>
        public string Status => $"particles {Context.Particles.Count}, quads {_frame.Add.Count + _frame.Alpha.Count}, bolts {_projectiles.Count}, lights {Context.Lights.Active}, debris {_debris.Count}";
        public int ParticleCount => Context.Particles.Count;
        public int QuadCount => _frame.Add.Count + _frame.Alpha.Count;
        public int DebrisCount => _debris.Count;
        public int ActiveBeams => _beams.Count;
        public int ActiveDeaths => _explosions.ActiveDeaths;
        /// <summary>The boost (afterburner) juice: its state for the panel and the tests.</summary>
        public BoostFx Boost => _boost;
        /// <summary>The retro-brake juice (R3): its state for the panel and the tests.</summary>
        public BrakeFx Brake => _brake;
        /// <summary>The player's death sequence (a button in the sandbox, <c>-mnDeathTest</c>).</summary>
        public DeathFx Death => _death;
        public int DroppedQuads => _frame.Add.Dropped + _frame.Alpha.Dropped;

        public static JuiceDirector Create(GameWorld world, Camera camera, Transform parent, JuiceSettings settings = null, IJuiceAudio audio = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var go = new GameObject("MonolithJuice");
            if (parent != null) go.transform.SetParent(parent, false);
            var d = go.AddComponent<JuiceDirector>();
            d.Build(world, camera, settings ?? JuiceSettings.Load(), audio);
            return d;
        }

        void Build(GameWorld world, Camera camera, JuiceSettings settings, IJuiceAudio audio)
        {
            var c = new JuiceContext { World = world, Camera = camera, Settings = settings, Root = transform };
            Context = c;
            c.Rng = new FxRng(20261023);
            c.Particles = new FxParticles(16000);
            _frame = new FxFrame { Add = new FxBatch("MN_FxAdd", 15000), Alpha = new FxBatch("MN_FxAlpha", 2500) };
            _frame.NitsPerUnit = 1.2f * Mathf.Pow(2f, ReferenceEv100);
            c.Frame = _frame;
            c.Lights = new LightPool(transform);
            c.Trauma = new Trauma();
            c.Rumble = new RumbleOut(settings);
            c.HitStop = new HitStop();
            c.Audio = audio ?? CreateAudio(settings);

            _fx = new Emitters(c); c.Fx = _fx;
            _feel = new Feel(c); c.Feel = _feel;
            _debris = new Debris(c, _fx); c.Debris = _debris;
            _waves = new Shockwaves(c, _fx); c.Shockwaves = _waves;
            _projectiles = new Projectiles(c);
            _hits = new HitEffects(c, _fx, _projectiles, _feel); c.Hits = _hits;
            _explosions = new Explosions(c, _fx, _feel, _debris, _waves); c.Explosions = _explosions;
            _beams = new Beams(c, _fx, _feel);
            _boost = new BoostFx(c);
            _brake = new BrakeFx(c);
            _death = new DeathFx(c); c.Death = _death;

            // The one custom pass that draws it all, for this camera only.
            var volGo = new GameObject("JuiceFxPass");
            volGo.transform.SetParent(transform, false);
            _volume = volGo.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            _volume.targetCamera = camera;
            _volume.priority = 5f;
            _volume.customPasses.Add(new FxPass { name = "MN Juice", Frame = _frame, Camera = camera });

            _shake = CameraShake.Create(c);
            if (camera.GetComponent<AudioListener>() == null && FindObjectOfType<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();

            Sub(CombatEvents.WeaponFired, e => _projectiles.OnFired(e));
            Sub(CombatEvents.MissileTrack, e => _projectiles.OnTrack(e));
            Sub(CombatEvents.HitShield, e => _hits.OnShieldHit(e));
            Sub(CombatEvents.HitHull, e => _hits.OnHullHit(e));
            Sub(CombatEvents.NearMiss, e => _hits.OnNearMiss(e));
            Sub(CombatEvents.FlakBurst, e => _hits.OnFlakBurst(e));
            Sub(CombatEvents.ShipDestroyed, e => _explosions.OnShipDestroyed(e));
            Sub(CombatEvents.SubsystemDestroyed, e => _explosions.OnSubsystemDestroyed(e));
            Sub(CombatEvents.Explosion, e => _explosions.OnExplosion(e));
            Sub(CombatEvents.BeamCharge, e => _beams.OnCharge(e));
            Sub(CombatEvents.BeamFire, e => _beams.OnFire(e));
            Sub(CombatEvents.BeamAim, e => _beams.OnAim(e));
            Sub(CombatEvents.BeamEnd, e => _beams.OnEnd(e));
            _debris.WarmUp();
            FlipLibrary.Load();                                  // the simulated explosions: loaded now, not at the first blast
            MnLog.Info("JUICE " + FlipLibrary.Report);
            _built = true;
            MnLog.Info($"JUICE on: preset {settings.preset}, settings from {settings.Source}, audio {c.Audio.GetType().Name}, rumble: {RumbleOut.Describe()}");
        }

        void Sub(string id, Action<GameEvent> handler)
        {
            _subs.Add(GameEvents.Subscribe(id, e =>
            {
                try { handler(e); }
                catch (Exception ex) { MnLog.Error($"JUICE handler for '{id}' threw: {ex}"); }
            }));
        }

        static IJuiceAudio CreateAudio(JuiceSettings settings)
        {
            if (JuiceAudioHooks.Factory != null)
            {
                try { var real = JuiceAudioHooks.Factory(settings); if (real != null) return real; }
                catch (Exception e) { MnLog.Warn("JUICE the audio system could not start: " + e.Message); }
            }
            // Captures are silent unless -mnAudio is given (nobody wants a battle playing through the speakers during a render run).
            bool dump = CommandLine.Has("-mnDumpSounds");
            if (!dump && !CommandLine.Has("-mnAudioMute") && (CommandLine.Has("-mnNoAudio") || Application.isBatchMode || (CommandLine.Has("-mnCapture") && !CommandLine.Has("-mnAudio")))) return new NullAudio();
            try
            {
                if (CommandLine.Has("-mnAudioMute")) AudioListener.volume = 0f;      // run every sound path, hear none of it (tests)
                var a = new ProceduralAudio(settings);
                if (dump) a.Dump(CommandLine.Get("-mnDumpSounds"));
                return a;
            }
            catch (Exception e) { MnLog.Warn("JUICE audio unavailable: " + e.Message); return new NullAudio(); }
        }

        // ------------------------------------------------------------------------------------------------ ships

        /// <summary>
        /// Tell the juice what a ship looks like: its visual (so it can break into lit pieces when destroyed and so beams and hits
        /// know its hull) and, for fighters and bombers, a shield bubble. Capital ships get no shield (design appendix: shield 0).
        /// </summary>
        public ShipLook RegisterShip(int shipId, Transform visual, Vector3? shieldRadii = null, IJuiceShipDamage damage = null)
        {
            var st = Context.World.Ships.Get(shipId);
            var look = new ShipLook { Id = shipId, Visual = visual, Radius = st != null ? st.Radius : 10f, Damage = damage };
            if (damage != null) HullSections.Warm(damage.HullRenderers);
            look.HullMat = Debris.HullMaterial(visual);
            if (visual != null)
            {
                var b = Debris.LocalBounds(visual);
                look.LocalCentre = b.center; look.HalfExtents = b.extents;
                foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("FX_", StringComparison.Ordinal)) look.FxSites.Add(t);
                if (st != null) look.Radius = Mathf.Max(look.Radius, 0.5f * b.size.magnitude * 0.7f);
            }
            look.SizeClass = JuiceContext.SizeClassFromRadius(look.Radius);
            if (shieldRadii.HasValue) look.ShieldRadii = shieldRadii.Value;
            else if (look.SizeClass == CombatEvents.SizeFighter || look.SizeClass == CombatEvents.SizeBomber)
            {
                var e = look.HalfExtents;
                look.ShieldRadii = new Vector3(Mathf.Max(e.x * 1.55f, 3f), Mathf.Max(e.y * 1.9f, e.x * 0.9f, 2.6f), Mathf.Max(e.z * 1.4f, 4f));
            }
            Context.Looks[shipId] = look;
            _hits.AttachShield(look);
            return look;
        }

        public void ForgetShip(int shipId) => Context.Looks.Remove(shipId);

        // ------------------------------------------------------------------------------------------------ the frame

        void Update()
        {
            if (!_built) return;
            var c = Context;
            float real = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            c.HitStop.Update(real);
            if (OwnsTimeScale && !c.World.Sim.OneTickPerFrame) c.World.Sim.TimeScale = BaseTimeScale * c.HitStop.Factor;
            c.Audio.Enabled = c.Settings.effects.sound;
            c.Audio.MasterVolume = c.Settings.levels.volume;
            c.Lights.Enabled = c.Settings.effects.lights;
            _death.Update(c.World.Sim.OneTickPerFrame ? Sim.Dt : real);                  // before the pad is written: it adds pulses and shake
            c.Rumble.Update(real);
            c.Audio.SetTimeScale(BaseTimeScale);
            c.Audio.Update(real);
        }

        static readonly bool HitchLog = CommandLine.Has("-mnJuiceHitchLog");
        int _gcBefore;
        float _lastRealTime;

        void LateUpdate()
        {
            if (!_built) return;
            var c = Context;
            if (HitchLog)
            {
                float now = Time.realtimeSinceStartup, frameMs = (now - _lastRealTime) * 1000f;
                int gc = System.GC.CollectionCount(0);
                if (_lastRealTime > 0f && frameMs > 10f)
                    MnLog.Info($"HITCH {frameMs:0.0} ms at t={c.Time:0.00}s: particles {c.Particles.Count}, quads {_frame.Add.Count}, lights {c.Lights.Active}, debris {_debris.Count}, gen0 GCs this frame {gc - _gcBefore}");
                _gcBefore = gc; _lastRealTime = now;
            }
            var sim = c.World.Sim;
            float real = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float dt = sim.OneTickPerFrame ? Sim.Dt : real * sim.TimeScale;
            c.Dt = dt;
            c.Time += dt;
            _frame.Time = c.Time;
            UpdateSun();

            _frame.Add.Begin();
            _frame.Alpha.Begin();
            c.Trauma.Tick(dt);
            _boost.Update(dt);                   // the boost: the view, the shake and rumble, the lens, the dust (after the trauma tick: it sustains)
            _brake.Update(dt);                   // the brake: the view narrowing (after the boost wrote the kick), the lurch, the rumble

            _fx.Update(dt);
            _projectiles.Update(dt);
            _beams.Update(dt);
            _explosions.Update(dt);
            _waves.Update(dt);
            _debris.Update(dt);
            _hits.Update(dt);
            c.Lights.Update(dt);

            _projectiles.Draw();
            _hits.Draw();
            _boost.Draw();
            _brake.Draw();
            c.Particles.Update(dt);
            c.Particles.Write(_frame.Add, _frame.Alpha, c.Time, c.Bright, c.CameraPosition);
            _frame.Add.Upload();
            _frame.Alpha.Upload();
            _frame.Enabled = true;
        }

        Light _sun;
        int _sunSearch;

        /// <summary>The Sun for the lit smoke of the simulated explosions: the scene's brightest directional light, found now and then.</summary>
        void UpdateSun()
        {
            if (_sun == null && _sunSearch-- <= 0)
            {
                _sunSearch = 120;
                float best = 0f;
                foreach (var l in FindObjectsOfType<Light>())
                {
                    if (l.type != LightType.Directional || !l.enabled) continue;
                    float k = l.intensity;
                    if (k > best) { best = k; _sun = l; }
                }
            }
            if (_sun == null) { _frame.SunDir = new Vector4(0f, 1f, 0f, 0f); return; }
            Vector3 toSun = -_sun.transform.forward;
            var hd = _sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            float lux = hd != null ? hd.intensity : _sun.intensity;
            if (lux < 100f) lux = 100000f;
            float units = lux / Mathf.PI / _frame.NitsPerUnit;                       // radiance of a white diffuse surface facing the Sun, in display units
            Color col = _sun.color;
            _frame.SunDir = new Vector4(toSun.x, toSun.y, toSun.z, 1f);
            _frame.SunCol = new Vector4(col.r * units, col.g * units, col.b * units, 0f);
        }

        /// <summary>Clear every effect in flight (a scene reset).</summary>
        public void ClearAll()
        {
            var c = Context;
            c.Particles.Clear(); _fx.Clear(); _debris.Clear(); _explosions.Clear(); _beams.Clear(); _waves.Clear(); c.Lights.Clear();
            c.Trauma.Clear(); c.Rumble.Stop(); c.HitStop.Clear(); _boost.Clear(); _brake.Clear(); _death.Reset();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _built) Context.Rumble.Stop();
        }

        void OnDestroy()
        {
            foreach (var s in _subs) s.Dispose();
            _boost?.Dispose();
            _death?.Dispose();
            _subs.Clear();
            if (_built) Context.Rumble.Stop();
        }
    }
}
