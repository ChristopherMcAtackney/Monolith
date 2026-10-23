using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.Input;
using Monolith.Juice;
using Monolith.Sky;
using Monolith.View;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The juice sandbox (-mnScene juice): the player's fighter (the real flight model and pad) with targets at range and two
    /// capital hulls, and the juice system listening. A panel (Start) fires every event, switches every effect, picks the intensity
    /// preset, slows time, and saves what you tune to juice.json next to the game. Pad shortcuts while flying: hold LB and press A, B, X, Y
    /// for fighter kill, bomber kill, capital death, beam strike; hold RB for shield hit, hull hit, near miss, flak; hold both for hits on
    /// you. RT fires. A capture plan may add "sceneOptions": { "script": [...] } to drive it (see <see cref="SandboxScript"/>).
    /// </summary>
    public sealed class JuiceSandbox : MonoBehaviour
    {
        public const string SceneName = "juice";

        GameWorld _world;
        FlightSystem _flight;
        ShipViews _views;
        PlaceholderShips.Materials _mats;
        Transform _root;
        public JuiceDirector Juice { get; private set; }
        public SandboxCombat Combat { get; private set; }
        JuicePanel _panel;
        ShipState _player;
        SandboxPilot _pilot;
        FlightHud _hud;
        bool _slowMo, _scripted;
        float _dirtyAt = -1f, _savedAt = -10f;
        string _lastAction = "ready";
        SandboxShip _cruiser, _destroyer;
        int _kbEvent;

        static readonly Vector3 PlayerStart = new Vector3(0f, 90f, -250f);

        // ------------------------------------------------------------------------------------------------ building

        public static JuiceSandbox Build(GameWorld world, FlightSystem flight, Transform parent, string sceneOptions = null)
        {
            var root = new GameObject("JuiceSandbox");
            root.transform.SetParent(parent, false);
            var self = root.AddComponent<JuiceSandbox>();
            self._world = world; self._flight = flight; self._root = root.transform;

            JObject opts = null;
            if (!string.IsNullOrEmpty(sceneOptions)) opts = JObject.Parse(sceneOptions);

            var view = CockpitView.Create(world, root.transform);
            SkyRig.Enable(view.Camera, root.transform);
            MakeShadowVolume(root.transform);
            self._views = ShipViews.Create(world, root.transform);
            self._mats = PlaceholderShips.MakeMaterials();

            // The player.
            var cls = ShipCatalog.Load(CommandLine.Get("-mnShip", "vanguard_fighter"));
            self._player = flight.Spawn(cls, PlayerStart, Quaternion.identity, 0.25f * cls.TopSpeed, 0.25f, 0, "Player");
            world.LocalPlayerShip = self._player.Id;
            self._hud = FlightHud.Create(world, view.Camera, root.transform);

            // The juice, then everything that raises events for it.
            var settings = JuiceSettings.Load();
            if (opts != null && opts["preset"] != null) settings.ApplyPreset((string)opts["preset"]);
            self.Juice = JuiceDirector.Create(world, view.Camera, root.transform, settings);
            self.Juice.OwnsTimeScale = true;
            self.Combat = new SandboxCombat(world, flight, self.Juice, self._player.Id);
            world.Sim.Register(self.Combat);
            self.SpawnWorld();

            // The pilot: the pad (or a script) through a wrapper that goes quiet while the panel is open.
            bool scripted = opts != null && opts["script"] != null;
            self._scripted = scripted;
            if (scripted)
            {
                var script = new SandboxScript(world, flight, self, (JArray)opts["script"]);
                world.Sim.Register(script);
                world.Pilots.Attach(self._player.Id, script);
                if (opts["battle"] != null) self.Combat.Battle = (bool)opts["battle"];
                MnLog.Info($"JUICE sandbox: scripted, {((JArray)opts["script"]).Count} steps");
            }
            else
            {
                var controls = ControlsConfig.Load(out string source);
                var inner = new PlayerPilot(world, self._player.Id, controls);
                self._pilot = new SandboxPilot(inner);
                world.Pilots.Attach(self._player.Id, self._pilot);
                MnLog.Info($"JUICE sandbox: controls from {source}; {PlayerPilot.Devices()}");
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                self.BuildPanel();
                SandboxChecks.Attach(self, self._panel, root.transform);
            }
            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
            return self;
        }

        static void MakeShadowVolume(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MN_JuiceShadows";
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(6000f);
            shadows.cascadeShadowSplitCount.Override(4);
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.6f);
            contact.maxDistance.Override(400f);
            var go = new GameObject("JuiceShadowVolume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        // ------------------------------------------------------------------------------------------------ the scene

        struct Slot
        {
            public string Name, Class, Kind; public int Team;
            public Vector3 Pos, Rot; public float Yaw, Speed;
            public float Length, Width, Height; public int Seed;
        }

        static readonly Slot[] Slots =
        {
            new Slot { Name = "Reaper 1", Class = "gtf_ulysses", Kind = "fighter", Team = 1, Pos = new Vector3(45, 105, 260), Rot = new Vector3(0, 200, 0), Yaw = 0.16f, Speed = 0.3f },
            new Slot { Name = "Reaper 2", Class = "gtf_myrmidon", Kind = "fighter", Team = 1, Pos = new Vector3(-130, 78, 400), Rot = new Vector3(0, 160, 0), Yaw = -0.14f, Speed = 0.3f },
            new Slot { Name = "Reaper 3", Class = "gtf_ulysses", Kind = "fighter", Team = 1, Pos = new Vector3(190, 150, 540), Rot = new Vector3(0, 245, 0), Yaw = 0.12f, Speed = 0.3f },
            new Slot { Name = "Hammer 1", Class = "gtb_artemis", Kind = "bomber", Team = 1, Pos = new Vector3(-30, 172, 730), Rot = new Vector3(0, 190, 0), Yaw = 0.09f, Speed = 0.3f },
            new Slot { Name = "Vanguard 2", Class = "gtf_myrmidon", Kind = "wingman", Team = 0, Pos = new Vector3(-75, 82, -160), Rot = new Vector3(0, 20, 0), Yaw = 0.2f, Speed = 0.3f },
            new Slot { Name = "Vanguard 3", Class = "gtf_ulysses", Kind = "wingman", Team = 0, Pos = new Vector3(100, 100, -200), Rot = new Vector3(0, -30, 0), Yaw = -0.18f, Speed = 0.3f },
            new Slot { Name = "Resolute", Class = "juice_cruiser", Kind = "cruiser", Team = 1, Pos = new Vector3(430, 40, 1020), Rot = new Vector3(0, 112f, 0), Length = 1100, Width = 180, Height = 140, Seed = 3 },
            new Slot { Name = "Ravager", Class = "juice_destroyer", Kind = "destroyer", Team = 0, Pos = new Vector3(-1250, 520, 2700), Rot = new Vector3(0, 42f, 5f), Length = 2200, Width = 300, Height = 220, Seed = 2 },
        };

        void SpawnWorld()
        {
            foreach (var s in Slots) Spawn(s);
            var cruiser = Combat.Find(x => x.Kind == "cruiser"); _cruiser = cruiser;
            _destroyer = Combat.Find(x => x.Kind == "destroyer");
        }

        SandboxShip Spawn(Slot s)
        {
            var cls = ShipCatalog.Load(s.Class);
            var rot = Quaternion.Euler(s.Rot);
            bool capital = s.Kind == "cruiser" || s.Kind == "destroyer";
            var st = _flight.Spawn(cls, s.Pos, rot, capital ? 0f : s.Speed * cls.TopSpeed, capital ? 0f : s.Speed, s.Team, s.Name);
            Transform visual;
            if (capital)
            {
                st.Radius = 0.5f * new Vector3(s.Width, s.Height, s.Length).magnitude;
                visual = PlaceholderShips.Hull(s.Name, s.Length, s.Width, s.Height, s.Seed, _mats, _root).transform;
                _world.Pilots.Attach(st.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, default(ControlInput)) }));
            }
            else
            {
                visual = PlaceholderShips.Fighter(s.Name, cls.Size, _mats, _root).transform;
                var keys = new List<ScriptedPilot.Key>
                {
                    new ScriptedPilot.Key(0f, new ControlInput { Yaw = s.Yaw, Throttle = s.Speed, Pitch = 0.02f }),
                };
                _world.Pilots.Attach(st.Id, new ScriptedPilot(keys));
            }
            _views.Register(st.Id, visual);
            Juice.RegisterShip(st.Id, visual);
            var rec = new SandboxShip { Id = st.Id, Name = s.Name, Team = s.Team, Kind = s.Kind };
            switch (s.Kind)
            {
                case "bomber": rec.ShieldMax = 220f; rec.HullMax = 300f; break;
                case "cruiser": case "destroyer": rec.ShieldMax = 0f; rec.HullMax = 400000f; break;
                default: rec.ShieldMax = 120f; rec.HullMax = 100f; break;
            }
            rec.Shield = rec.ShieldMax; rec.Hull = rec.HullMax;
            var slot = s;
            rec.Respawn = () =>
            {
                var fresh = Spawn(slot);
                if (slot.Kind == "cruiser") _cruiser = fresh; else if (slot.Kind == "destroyer") _destroyer = fresh;
                return fresh;
            };
            Combat.Add(rec);
            return rec;
        }

        // ------------------------------------------------------------------------------------------------ the panel

        void BuildPanel()
        {
            _panel = JuicePanel.Create(_root);
            var s = Juice.Settings;
            _panel.OnVisibleChanged = v => { Combat.Blocked = v; _pilot.Blocked = v; };
            _panel.OnChanged = () => { s.preset = s.DetectPreset(); _dirtyAt = Time.unscaledTime + 0.8f; };
            _panel.Footer = () =>
            {
                string saved = Time.unscaledTime - _savedAt < 3f ? "  saved" : (_dirtyAt > 0f ? "  ..." : "");
                return $"{s.preset.ToUpperInvariant()}  {(_slowMo ? "SLOW-MO " : "")}{(Combat.Battle ? "BATTLE " : "")} {1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime):0} fps  fx {Juice.ParticleCount}p {Juice.QuadCount}q{saved}";
            };

            var events = new PanelPage("EVENTS");
            events.Add(PanelItem.Info(() => "Fire an event. RT fires your guns. Hold LB or RB with A B X Y to fire them in flight."));
            events.Add(PanelItem.Button("Fighter kill", () => Trigger("fighter_kill"), "LB+A"), PanelItem.Button("Bomber kill", () => Trigger("bomber_kill"), "LB+B"));
            events.Add(PanelItem.Button("Capital death", () => Trigger("capital_death"), "LB+X"), PanelItem.Button("Beam strike", () => Trigger("beam_strike"), "LB+Y"));
            events.Add(PanelItem.Button("Shield hit", () => Trigger("shield_hit"), "RB+A"), PanelItem.Button("Hull hit", () => Trigger("hull_hit"), "RB+B"));
            events.Add(PanelItem.Button("Near miss", () => Trigger("near_miss"), "RB+X"), PanelItem.Button("Flak", () => Trigger("flak"), "RB+Y"));
            events.Add(PanelItem.Button("Missile", () => Trigger("missile")), PanelItem.Button("Laser volley", () => Trigger("volley")));
            events.Add(PanelItem.Button("Hit on you: shield", () => Trigger("you_shield"), "LB+RB+A"), PanelItem.Button("Hit on you: hull", () => Trigger("you_hull"), "LB+RB+B"));
            events.Add(PanelItem.Toggle("Ambient battle", () => Combat.Battle, v => Combat.Battle = v), PanelItem.Toggle("Slow motion", () => _slowMo, SetSlowMo));
            events.Add(PanelItem.Info(() => "last: " + _lastAction));
            _panel.Pages.Add(events);

            var fx = new PanelPage("EFFECTS");
            var e = s.effects;
            fx.Add(PanelItem.Toggle("Laser bolts", () => e.bolts, v => e.bolts = v), PanelItem.Toggle("Muzzle flash", () => e.muzzleFlash, v => e.muzzleFlash = v));
            fx.Add(PanelItem.Toggle("Heavy beams", () => e.beams, v => e.beams = v), PanelItem.Toggle("Flak", () => e.flak, v => e.flak = v));
            fx.Add(PanelItem.Toggle("Missile trails", () => e.missileTrails, v => e.missileTrails = v), PanelItem.Toggle("Shield ripples", () => e.shieldRipples, v => e.shieldRipples = v));
            fx.Add(PanelItem.Toggle("Hull hits", () => e.hullHits, v => e.hullHits = v), PanelItem.Toggle("Explosions", () => e.explosions, v => e.explosions = v));
            fx.Add(PanelItem.Toggle("Debris", () => e.debris, v => e.debris = v), PanelItem.Toggle("Shockwave", () => e.shockwave, v => e.shockwave = v));
            fx.Add(PanelItem.Toggle("Lights", () => e.lights, v => e.lights = v), PanelItem.Toggle("Screen-edge flash", () => e.screenFlash, v => e.screenFlash = v));
            fx.Add(PanelItem.Toggle("Camera shake", () => e.cameraShake, v => e.cameraShake = v), PanelItem.Toggle("Pad rumble", () => e.rumble, v => e.rumble = v));
            fx.Add(PanelItem.Toggle("Sound", () => e.sound, v => e.sound = v), PanelItem.Toggle("Hit-stop on kills", () => s.hitStop.enabled, v => s.hitStop.enabled = v));
            fx.Add(PanelItem.Info(() => "Shake and flashes also have levels (TUNING). Off = off."));
            _panel.Pages.Add(fx);

            var tune = new PanelPage("TUNING");
            tune.Add(PanelItem.Button("Low", () => Preset("Low"), null, () => s.preset == "Low"), PanelItem.Button("Medium", () => Preset("Medium"), null, () => s.preset == "Medium"));
            tune.Add(PanelItem.Button("High", () => Preset("High"), null, () => s.preset == "High"), PanelItem.Button("Extreme", () => Preset("Extreme"), null, () => s.preset == "Extreme"));
            var l = s.levels;
            tune.Add(PanelItem.Slider("Shake", () => l.shake, v => l.shake = v, 0f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Rumble", () => l.rumble, v => l.rumble = v, 0f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Flash / brightness", () => l.flash, v => l.flash = v, 0f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Explosion size", () => l.size, v => l.size = v, 0.3f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Particle density", () => l.density, v => l.density = v, 0.2f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Shockwave shove", () => l.shove, v => l.shove = v, 0f, 3f, 0.05f));
            tune.Add(PanelItem.Slider("Bolt thickness", () => l.boltWidth, v => l.boltWidth = v, 0.3f, 4f, 0.05f));
            tune.Add(PanelItem.Slider("Beam thickness", () => l.beamWidth, v => l.beamWidth = v, 0.3f, 4f, 0.05f));
            tune.Add(PanelItem.Slider("Volume", () => l.volume, v => l.volume = v, 0f, 1f, 0.05f));
            tune.Add(PanelItem.Slider("Hit-stop (ms)", () => s.hitStop.ms, v => s.hitStop.ms = v, 20f, 250f, 5f, "0"));
            tune.Add(PanelItem.Slider("Slow-motion speed", () => s.slowMo, v => { s.slowMo = v; SetSlowMo(_slowMo); }, 0.05f, 1f, 0.05f));
            tune.Add(PanelItem.Button("Save now", SaveNow), PanelItem.Button("Reset to defaults", () => { s.ResetToDefaults(); SaveNow(); }));
            _panel.Pages.Add(tune);
        }

        void Preset(string name)
        {
            Juice.Settings.ApplyPreset(name);
            _dirtyAt = Time.unscaledTime + 0.8f;
        }

        void SetSlowMo(bool on)
        {
            _slowMo = on;
            Juice.BaseTimeScale = on ? Juice.Settings.slowMo : 1f;
            _dirtyAt = Time.unscaledTime + 0.8f;
        }

        void SaveNow()
        {
            if (Juice.Settings.Save()) { _savedAt = Time.unscaledTime; _dirtyAt = -1f; }
        }

        // ------------------------------------------------------------------------------------------------ per frame

        void Update()
        {
            if (_dirtyAt > 0f && Time.unscaledTime >= _dirtyAt) SaveNow();
            if (_scripted) return;
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            if (pad != null && !_panel.Visible)
            {
                bool lb = pad.leftShoulder.isPressed, rb = pad.rightShoulder.isPressed;
                if (lb || rb)
                {
                    string ev = null;
                    if (lb && rb)
                    {
                        if (pad.buttonSouth.wasPressedThisFrame) ev = "you_shield";
                        else if (pad.buttonEast.wasPressedThisFrame) ev = "you_hull";
                    }
                    else if (lb)
                    {
                        if (pad.buttonSouth.wasPressedThisFrame) ev = "fighter_kill";
                        else if (pad.buttonEast.wasPressedThisFrame) ev = "bomber_kill";
                        else if (pad.buttonWest.wasPressedThisFrame) ev = "capital_death";
                        else if (pad.buttonNorth.wasPressedThisFrame) ev = "beam_strike";
                    }
                    else
                    {
                        if (pad.buttonSouth.wasPressedThisFrame) ev = "shield_hit";
                        else if (pad.buttonEast.wasPressedThisFrame) ev = "hull_hit";
                        else if (pad.buttonWest.wasPressedThisFrame) ev = "near_miss";
                        else if (pad.buttonNorth.wasPressedThisFrame) ev = "flak";
                    }
                    if (ev != null) Trigger(ev);
                }
                if (pad.selectButton.wasPressedThisFrame) ResetPlayer();
                if (pad.dpad.up.wasPressedThisFrame && lb) SetSlowMo(!_slowMo);
            }
            if (kb != null)
            {
                string[] keyEvents = { "fighter_kill", "bomber_kill", "capital_death", "beam_strike", "shield_hit", "hull_hit", "near_miss", "flak" };
                Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8 };
                for (int i = 0; i < keys.Length; i++) if (kb[keys[i]].wasPressedThisFrame) Trigger(keyEvents[i]);
                if (kb.f10Key.wasPressedThisFrame) Application.Quit(0);
                if (kb.backspaceKey.wasPressedThisFrame) ResetPlayer();
            }
        }

        void ResetPlayer()
        {
            var cls = ShipCatalog.Load(_player.ClassId);
            _flight.Place(_player.Id, PlayerStart, Quaternion.identity, 0.25f * cls.TopSpeed, 0.25f);
            _lastAction = "player reset";
        }

        // ------------------------------------------------------------------------------------------------ the events

        /// <summary>Fire one of the sandbox's named events: raises the same <see cref="CombatEvents"/> a real weapons system would.</summary>
        public void Trigger(string name)
        {
            var me = _world.Ships.Get(_player.Id);
            if (me == null) return;
            switch (name)
            {
                case "fighter_kill": Kill(s => s.Kind == "fighter", "fighter"); break;
                case "bomber_kill": Kill(s => s.Kind == "bomber", "bomber"); break;
                case "capital_death": Kill(s => s.Kind == "cruiser", "capital"); break;
                case "shield_hit": HitTarget(false); break;
                case "hull_hit": HitTarget(true); break;
                case "near_miss": NearMiss(); break;
                case "beam_strike": BeamStrike(); break;
                case "flak": Flak(); break;
                case "missile": Missile(); break;
                case "volley": Volley(); break;
                case "you_shield": HitYou(false); break;
                case "you_hull": HitYou(true); break;
                default: MnLog.Warn($"JUICE sandbox: unknown event '{name}'"); return;
            }
            MnLog.Info($"JUICE sandbox event: {name}");
        }

        void Kill(Func<SandboxShip, bool> fits, string what)
        {
            var t = Combat.Pick(fits);
            if (t == null) { _lastAction = $"no {what} to kill right now (it respawns)"; return; }
            Combat.Destroy(t, _player.Id);
            _lastAction = $"{what} kill: {t.Name}";
        }

        void HitTarget(bool hull)
        {
            var t = Combat.Pick(s => s.Alive && s.Team != 0 && (hull || !s.Capital));
            if (t == null) { _lastAction = "no target"; return; }
            var me = _world.Ships.Get(_player.Id);
            Combat.SurfaceFacing(t, me.Position, out var point, out var normal);
            if (hull || t.Capital)
            {
                GameEvents.RaiseAt(CombatEvents.HitHull, point, normal, "laser_player", 40f, t.Id, _player.Id);
                _lastAction = $"hull hit on {t.Name}";
            }
            else
            {
                GameEvents.RaiseAt(CombatEvents.HitShield, point, (point - me.Position).normalized, "laser_player", 40f, t.Id, _player.Id);
                _lastAction = $"shield hit on {t.Name}";
            }
        }

        void HitYou(bool hull)
        {
            var me = _world.Ships.Get(_player.Id);
            Vector3 dir = me.Rotation * new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.2f, 0.4f), 1f);
            Vector3 point = me.Position + me.Rotation * new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-1f, 2f), 5f);
            if (hull) GameEvents.RaiseAt(CombatEvents.HitHull, point, -dir.normalized, "laser_enemy", 40f, _player.Id, GameWorld.NoShip);
            else GameEvents.RaiseAt(CombatEvents.HitShield, point, -dir.normalized, "laser_enemy", 40f, _player.Id, GameWorld.NoShip);
            _lastAction = hull ? "hull hit on you" : "shield hit on you";
        }

        void NearMiss()
        {
            var me = _world.Ships.Get(_player.Id);
            // A bolt from far ahead and a little to one side that passes close beside you.
            Vector3 side = me.Rotation * new Vector3(UnityEngine.Random.value < 0.5f ? -1f : 1f, UnityEngine.Random.Range(-0.3f, 0.5f), 0f).normalized;
            Vector3 from = me.Position + me.Rotation * Vector3.forward * 520f + side * 40f;
            Vector3 to = me.Position + side * 11f;
            Vector3 dir = (to - from).normalized;
            Combat.FireScripted(GameWorld.NoShip, 1, "laser_enemy", from, dir, 520f, 12f, 3.2f);
            _lastAction = "near miss";
        }

        void Volley()
        {
            var me = _world.Ships.Get(_player.Id);
            Combat.Schedule(0f, () => { });
            for (int i = 0; i < 10; i++)
            {
                int k = i;
                Combat.Schedule(i * 0.07f, () =>
                {
                    var m = _world.Ships.Get(_player.Id); if (m == null) return;
                    Vector3 mz = m.Position + m.Rotation * new Vector3((k & 1) == 0 ? -2.9f : 2.9f, -0.35f, 3.4f);
                    Vector3 aim = m.Position + m.Rotation * new Vector3((k & 1) == 0 ? 2f : -2f, 0.4f, 650f);
                    Combat.FireScripted(m.Id, 0, "laser_player", mz, (aim - mz).normalized, 620f, 14f, 2.6f);
                });
            }
            _lastAction = "laser volley";
        }

        void Missile()
        {
            var me = _world.Ships.Get(_player.Id);
            var t = Combat.Pick(s => s.Alive && s.Team != 0 && !s.Capital) ?? Combat.Pick(s => s.Alive && s.Team != 0);
            if (t == null) { _lastAction = "no target"; return; }
            var tst = _world.Ships.Get(t.Id);
            Vector3 mz = me.Position + me.Rotation * new Vector3(0f, -1.2f, 3f);
            Vector3 dir = me.Rotation * Vector3.forward;
            GameEvents.RaiseAt(CombatEvents.WeaponFired, mz, dir, "missile_heat", 190f, me.Id, t.Id);
            float dist = Vector3.Distance(me.Position, tst.Position);
            float tof = 0.4f + dist / 175f;
            Combat.Schedule(tof, () =>
            {
                var st = _world.Ships.Get(t.Id);
                if (st == null || !t.Alive) return;
                Vector3 p = st.Position - (st.Position - me.Position).normalized * Mathf.Max(3f, st.Radius * 0.5f);
                t.Shield = 0f;
                t.Hull -= 120f;
                GameEvents.RaiseAt(CombatEvents.HitHull, p, (p - st.Position).normalized, "missile_heat", 120f, t.Id, me.Id);
                if (t.Hull <= 0f && !t.Capital) Combat.Destroy(t, me.Id);
            });
            _lastAction = "missile away";
        }

        void BeamStrike()
        {
            var src = _destroyer; var dst = _cruiser;
            if (src == null || !src.Alive || dst == null || !dst.Alive) { _lastAction = "capital ships are respawning"; return; }
            var sst = _world.Ships.Get(src.Id); var dstSt = _world.Ships.Get(dst.Id);
            if (sst == null || dstSt == null) return;
            // The muzzle: on the destroyer's flank facing the cruiser, ahead of midships.
            Vector3 toTarget = (dstSt.Position - sst.Position);
            Vector3 localT = Quaternion.Inverse(sst.Rotation) * toTarget;
            float side = localT.x >= 0f ? 1f : -1f;
            var look = Juice.Context.LookOf(src.Id);
            Vector3 half = look != null ? look.HalfExtents : new Vector3(150, 110, 1100);
            Vector3 muzzle = sst.Position + sst.Rotation * new Vector3(side * (half.x + 4f), half.y * 0.25f, half.z * 0.35f);
            Vector3 aim = (dstSt.Position - muzzle).normalized;
            float charge = 2.3f, fire = 4.8f;
            GameEvents.RaiseAt(CombatEvents.BeamCharge, muzzle, aim, "beam_green", charge, src.Id, dst.Id);
            Combat.Schedule(charge, () =>
            {
                var s2 = _world.Ships.Get(src.Id); if (s2 == null) return;
                GameEvents.RaiseAt(CombatEvents.BeamFire, muzzle, aim, "beam_green", fire, src.Id, dst.Id);
            });
            Combat.Schedule(charge + fire, () => GameEvents.RaiseAt(CombatEvents.BeamEnd, muzzle, aim, "beam_green", 0f, src.Id, dst.Id));
            _lastAction = "beam strike: charging";
        }

        void Flak()
        {
            var me = _world.Ships.Get(_player.Id);
            // A battery on the cruiser throws shells that burst in a ring ahead of you.
            var cruiser = _cruiser;
            Vector3 origin = cruiser != null && _world.Ships.Get(cruiser.Id) is ShipState cs ? cs.Position + cs.Rotation * new Vector3(0f, 90f, -200f) : me.Position + me.Rotation * new Vector3(0, 0, 900f);
            Vector3 centre = me.Position + me.Rotation * new Vector3(UnityEngine.Random.Range(-40f, 40f), UnityEngine.Random.Range(-20f, 30f), 230f);
            for (int i = 0; i < 9; i++)
            {
                float ang = i / 9f * Mathf.PI * 2f + UnityEngine.Random.value;
                Vector3 burst = centre + me.Rotation * new Vector3(Mathf.Cos(ang) * UnityEngine.Random.Range(40f, 85f), Mathf.Sin(ang) * UnityEngine.Random.Range(25f, 55f), UnityEngine.Random.Range(-45f, 60f));
                Vector3 dir = (burst - origin).normalized;
                float tof = Vector3.Distance(origin, burst) / 720f;
                float delay = i * 0.09f;
                Combat.Schedule(delay, () =>
                {
                    GameEvents.RaiseAt(CombatEvents.WeaponFired, origin, dir, "flak_shell", 720f, cruiser != null ? cruiser.Id : GameWorld.NoShip, GameWorld.NoShip);
                });
                Combat.Schedule(delay + tof, () => GameEvents.RaiseAt(CombatEvents.FlakBurst, burst, Vector3.zero, "flak_shell", UnityEngine.Random.Range(22f, 32f), cruiser != null ? cruiser.Id : GameWorld.NoShip));
            }
            _lastAction = "flak barrage";
        }

        // ------------------------------------------------------------------------------------------------ hooks for the script

        public GameWorld World => _world;
        public ShipState Player => _player;
        public void Say(string s) => _lastAction = s;
        public void SetBattle(bool on) { Combat.Battle = on; }
        public void PlacePlayer(Vector3 pos, Vector3 eulerRot, float speedFraction, float throttle)
        {
            var cls = ShipCatalog.Load(_player.ClassId);
            _flight.Place(_player.Id, pos, Quaternion.Euler(eulerRot), speedFraction * cls.TopSpeed, throttle);
        }

        // ------------------------------------------------------------------------------------------------ the pad, quiet when the panel is up

        /// <summary>Wraps the player's pad pilot: while the panel is open the ship holds its heading and the guns are quiet; while LB or RB is held the face buttons are shortcuts and do nothing else.</summary>
        sealed class SandboxPilot : IPilotSource
        {
            readonly PlayerPilot _inner;
            public bool Blocked;
            float _throttle;

            public SandboxPilot(PlayerPilot inner) { _inner = inner; }

            public ControlInput Sample(in SimFrame frame)
            {
                if (Blocked) return new ControlInput { Throttle = _throttle };
                var c = _inner.Sample(frame);
                _throttle = c.Throttle;
                var pad = Gamepad.current;
                if (pad != null && (pad.leftShoulder.isPressed || pad.rightShoulder.isPressed))
                    c.Buttons &= ~(ControlButtons.TargetNext | ControlButtons.MatchSpeed | ControlButtons.Countermeasure | ControlButtons.Orders | ControlButtons.SecondaryFire | ControlButtons.MissileFire);
                return c;
            }
        }
    }
}
