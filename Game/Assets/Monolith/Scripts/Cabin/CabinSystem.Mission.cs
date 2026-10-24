using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Mission;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>The cabin as the mission sees it (<see cref="ICabin"/>), and the scans, lights and jolts that feed the view.</summary>
    public sealed partial class CabinSystem
    {
        MissionRuntime _rt;
        bool _callOutOpen;
        readonly HashSet<int> _detectedBy = new HashSet<int>();

        sealed class Sweep
        {
            public int From;
            public float Start, Seconds, Range, ContactAt;
            public bool Resolved, Hit, Warned;
        }
        readonly List<Sweep> _sweeps = new List<Sweep>();
        public int ActiveSweeps => _sweeps.Count;

        // ------------------------------------------------------------------------------------------------ ICabin

        bool ICabin.Seated => Board.State == CabinBoard.Phase.Seated;
        bool ICabin.Powered => Powered;
        string ICabin.Lights => _missionLights;

        public bool DetectedBy(int ship) => ship == GameWorld.NoShip ? _detectedBy.Count > 0 : _detectedBy.Contains(ship);

        public bool NeedsRepair(int ship, string part)
        {
            var c = World.Combat.Get(ship);
            if (c == null) return false;
            if (ship != World.LocalPlayerShip) return false;
            foreach (var p in c.Parts) if (p.Destroyed && (part == null || p.Name == part)) return true;
            return false;
        }

        public void StartScan(int fromShip, float seconds, float range, float now)
        {
            _sweeps.Add(new Sweep { From = fromShip, Start = now, Seconds = seconds, Range = range, ContactAt = now + seconds * Cfg.scan.contactAt });
        }

        public void CallOut(string prompt)
        {
            _callOutOpen = true;
        }

        void ClearCallOut()
        {
            if (_rt != null && _rt.Board.Prompt != null) { _rt.Board.Prompt = null; _rt.Board.Version++; }
            _callOutOpen = false;
        }

        public void KnockOut(int ship, string part, float now)
        {
            // The part is destroyed by the action (through the host); it needs a hand repair for as long as it stays destroyed (NeedsRepair reads the combat state).
            _rt?.Note("cabin", $"{part} knocked out: it needs a hand repair at the {PanelOf(World.Combat.Get(ship)?.FindPart(part)?.Kind)} panel");
        }

        public void SetLights(string state)
        {
            if (state != "normal" && state != "dark" && state != "emergency" && state != "alert") state = "normal";
            _missionLights = state;
            Board.Version++;
        }

        void ICabin.Step(MissionRuntime rt)
        {
            _rt = rt;
            if (Board.State != CabinBoard.Phase.Seated && rt.Board.Prompt != null && Board.State == CabinBoard.Phase.Rising) ClearCallOut();
            if (_callOutOpen && rt.Board.Prompt == null) _callOutOpen = false;
        }

        // ------------------------------------------------------------------------------------------------ scans

        /// <summary>Is the ship warm enough to be found: powered, or only just dark.</summary>
        public bool Detectable => Powered || _now - _poweredOffAt < Cfg.timing.coolSeconds;

        void StepScans(float dt)
        {
            float light = 0f;
            Vector3 dir = Vector3.forward;
            var me = World.Ships.Get(World.LocalPlayerShip);
            for (int i = _sweeps.Count - 1; i >= 0; i--)
            {
                var s = _sweeps[i];
                var from = World.Ships.Get(s.From);
                if (from == null || me == null) { _sweeps.RemoveAt(i); continue; }
                float dist = Vector3.Distance(from.Position, me.Position);
                if (!s.Warned && _now >= s.ContactAt - Cfg.scan.creakLeadSeconds && dist < s.Range)
                {
                    s.Warned = true;
                    GameEvents.Raise(CabinEvents.Creak, "hull", 0.35f);
                }
                if (!s.Resolved && _now >= s.ContactAt)
                {
                    s.Resolved = true;
                    s.Hit = dist < s.Range && Detectable;
                    if (s.Hit && _detectedBy.Add(s.From)) _rt?.Note("cabin", $"scan from {from.Callsign} detected the player ({(Powered ? "powered" : "still warm")})");
                    else if (!s.Hit) _rt?.Note("cabin", dist >= s.Range ? $"scan from {from.Callsign} out of range" : $"scan from {from.Callsign} swept past a dark ship");
                    GameEvents.Raise(CabinEvents.ScanContact, s.Hit ? "hit" : "miss", s.Seconds, s.From);
                    GameEvents.Raise(CabinEvents.Creak, "hull", s.Hit ? 0.9f : 0.6f);
                }
                // the beam: a swing across the cabin, a bell of light round the moment of contact, fading with range
                float u = Mathf.Clamp01((_now - s.Start) / Mathf.Max(0.1f, s.Seconds));
                float swing = Mathf.Lerp(-Cfg.scan.swingDegrees * 0.5f, Cfg.scan.swingDegrees * 0.5f, u);
                float sigma = Cfg.scan.passSeconds / 2.3548f;
                float bell = Mathf.Exp(-0.5f * Mathf.Pow((_now - s.ContactAt) / sigma, 2f));
                float fx = Mathf.Clamp01((dist / Mathf.Max(1f, s.Range) - 0.55f) / 0.45f);
                float falloff = 1f - fx * fx * (3f - 2f * fx);
                float l = bell * falloff;
                if (l > light)
                {
                    light = l;
                    Vector3 world = (from.Position - me.Position).normalized;
                    Vector3 local = Quaternion.Inverse(me.Rotation) * world;
                    dir = Quaternion.AngleAxis(swing, Vector3.up) * local;
                }
                if (_now >= s.Start + s.Seconds + 0.5f) _sweeps.RemoveAt(i);
            }
            Board.ScanLight = light;
            if (light > 0.001f) Board.ScanDirShip = dir;
        }

        // ------------------------------------------------------------------------------------------------ lights and exposure

        string _effective = "normal";
        float _ev = 14f;

        void StepLights(float dt)
        {
            if (Powered && _relightT < Cfg.timing.relightSeconds) _relightT += dt;
            string eff = !Powered ? "blackout" : _relightT < Cfg.timing.relightSeconds ? "dark" : _missionLights;
            if (eff != _effective)
            {
                _effective = eff;
                Board.Lights = eff;
                GameEvents.Raise(CabinEvents.Lights, eff);
                Board.Version++;
            }
            // The HUD: gone with the view's rise and when the ship is dark. The battle map is the HUD's own screen now (it draws it while MapOpen is on and keeps the warnings and the radio up).
            float collapse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Board.Stand01 / 0.5f));
            float hudWant = Mathf.Max(collapse, (!Powered || _relightT < Cfg.timing.relightSeconds) ? 1f : 0f);
            if (Paused) hudWant = 0f;
            Board.HudCollapse = Mathf.MoveTowards(Board.HudCollapse, hudWant, dt * (Powered ? 3.0f : 5.0f));
            // The eye's exposure: the flight view's when seated, the cabin's own light when up (it opens slowly into the dark and closes fast into the light).
            var e = Cfg.exposure;
            float standEv = _effective == "blackout" ? e.blackout : _effective == "dark" ? e.dark : _effective == "emergency" ? e.emergency : _effective == "alert" ? e.alert : e.lit;
            float want = Mathf.Lerp(e.seated, standEv, Ease(Mathf.Clamp01(Board.Stand01)));
            float rate = want < _ev ? 1f / Mathf.Max(0.05f, e.openSeconds) : 1f / Mathf.Max(0.05f, e.closeSeconds);
            _ev = Mathf.MoveTowards(_ev, want, Mathf.Abs(want - _ev) * Mathf.Min(1f, rate * dt * 2.2f) + 0.002f);
            Board.ExposureShift = _ev - e.seated;
            // The sky's fill is absolute light: an eye that has opened into the dark (a lower EV) would see it brighten (2 EV: four times), the cabin go grey, not black.
            // Hold it where it was in screen terms for the lit cabin, and take most of it away in a blackout.
            float eyeOpen = Mathf.Pow(2f, Mathf.Min(0f, _ev - e.lit));
            float dark01 = Mathf.Clamp01((_ev - e.blackout) / Mathf.Max(0.01f, e.lit - e.blackout));
            float fill = Cfg.light.ambientScale * eyeOpen * Mathf.Lerp(Cfg.light.ambientBlackout, 1f, dark01);
            Board.AmbientScale = Mathf.Lerp(1f, fill, Ease(Mathf.Clamp01(Board.Stand01)));
            Board.NitsPerUnit = 1.2f * Mathf.Pow(2f, e.seated + Board.ExposureShift);
            StepAmbient(dt);
        }

        // ------------------------------------------------------------------------------------------------ breathing, creaks

        float _breathT, _creakT = 7f;
        bool _breathIn;

        void StepAmbient(float dt)
        {
            if (Board.State == CabinBoard.Phase.Seated && Powered) return;
            bool scanned = Board.ScanLight > 0.05f;
            float period = Powered ? 3.8f : (scanned ? 2.4f : 4.6f);
            _breathT += dt;
            if (_breathT >= period * 0.5f)
            {
                _breathT = 0f; _breathIn = !_breathIn;
                GameEvents.Raise(CabinEvents.Breath, _breathIn ? "in" : "out", Powered ? 0.4f : (scanned ? 0.25f : 0.55f));
            }
            _creakT -= dt;
            if (_creakT <= 0f)
            {
                _creakT = 6f + (float)_rng.NextDouble() * 10f;
                GameEvents.Raise(CabinEvents.Creak, "hull", 0.2f + (float)_rng.NextDouble() * 0.3f);
            }
        }

        // ------------------------------------------------------------------------------------------------ jolts

        void OnShove(GameEvent e, float strength, float perDamage)
        {
            if (e.Entity != World.LocalPlayerShip) return;
            Jolt(Mathf.Clamp01(strength + e.Value * perDamage), e.Subject);
        }

        void OnExplosion(GameEvent e)
        {
            var me = World.Ships.Get(World.LocalPlayerShip);
            if (me == null || !e.HasPosition) return;
            float d = Vector3.Distance(e.Position, me.Position);
            float reach = Mathf.Max(60f, e.Value * 4f);
            if (d < reach) Jolt(0.5f * (1f - d / reach), "explosion");
        }

        void Jolt(float amount, string what)
        {
            if (amount <= Board.Jolt) return;
            Board.Jolt = Mathf.Min(1f, amount);
            GameEvents.Raise(CabinEvents.Jolt, what, amount);
            if (amount > 0.4f) GameEvents.Raise(CabinEvents.Creak, "hull", Mathf.Clamp01(amount));
        }

        void StepJolt(float dt) { Board.Jolt = Mathf.Max(0f, Board.Jolt - dt * 2.2f); }

        // ------------------------------------------------------------------------------------------------ names

        /// <summary>"engine_port" becomes "PORT ENGINE" and "SUB_flak_P02" "FLAK P02"; the panel's prompt and the displays use it.</summary>
        public static string PartLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return "PART";
            string n = name.StartsWith("SUB_", StringComparison.Ordinal) ? name.Substring(4) : name;
            var bits = n.Split('_');
            if (bits.Length == 2 && (bits[1] == "port" || bits[1] == "starboard" || bits[1] == "left" || bits[1] == "right" || bits[1] == "center" || bits[1] == "centre"))
                return (bits[1] + " " + bits[0]).ToUpperInvariant();
            return n.Replace('_', ' ').ToUpperInvariant();
        }
    }
}
