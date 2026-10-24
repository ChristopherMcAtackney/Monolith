using System;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// The cabin's sound (the pilot standing, walking, going dark, repairing, loading the rack). The cabin is a system of its own (Monolith.Cabin) that raises events and publishes a
    /// <see cref="CabinBoard"/>; the audio reads the events by their documented ids and the board's state, so it does not depend on the cabin's assembly. Everything here is close and
    /// physical (no positions: this is the room you are standing in) and nothing is a hand: the first-person view has no hands, so there are no hand sounds, only the room, the ship and the body.
    ///
    ///   cabin_step         a boot on the deck (plate, grating or a rung), louder with the pilot's speed
    ///   cabin_stand        the harness opening (standing up) and closing (sitting down)
    ///   cabin_power        the breaker's clunk, then the ship powering down (a falling whine) or booting (a climbing one with the displays coming up in turn)
    ///   cabin_lights       the lighting state: the emergency hum swells in emergency, alert and blackout and the breath comes up in the dark
    ///   cabin_scan_contact a scan finding the ship (a bong through the hull) or passing over it (a moan that falls away)
    ///   cabin_creak        the hull groaning
    ///   cabin_breath       the pilot's breath, louder in the dark and louder again while a scan is crossing
    ///   cabin_repair       the panel's hatch, then living tech knitting into the hull until the hands let go
    ///   cabin_loadout      the rack seating a module (its pull from the press is heard when the action starts)
    ///   board.Action       the breaker's lever, the rack's pull, the repair's knitting loop
    ///   board.ScanLight    the scan's beam crossing the hull, heard through it: level and pan follow the light
    ///   (heartbeat)        in the dark, faster when a scan is crossing
    /// </summary>
    public sealed partial class AudioSystem
    {
        string _cabinState = "normal";
        bool _cabinOwnsCreaks;
        LoopHandle _repair, _hum, _hullScan;
        float _humLevel, _scanLevel, _scanPan, _repairLevel, _heartT;
        string _lastAction;
        bool _scanWas;
        float _cabinNow;

        /// <summary>How dark the cabin is for the ear: breath and heartbeat come up the darker it gets.</summary>
        float DarkMul()
        {
            switch (_cabinState)
            {
                case "blackout": return 1.3f;
                case "dark": return 1.0f;
                case "alert": case "emergency": return 0.8f;
                default: return 0.55f;
            }
        }

        void SubscribeCabin()
        {
            Sub("cabin_lights", e => { _cabinState = e.Subject ?? "normal"; });
            Sub("cabin_step", e => PlayLocal(e.Subject == "tread" ? "step_grating" : e.Subject == "riser" ? "step_rung" : "step_plate", 0.5f + 0.5f * Mathf.Clamp01(e.Value)));
            Sub("cabin_stand", e => { if (e.Subject == "stand" && e.Value < 0.5f) PlayLocal("seat_open"); else if (e.Subject == "sit" && e.Value >= 0.5f) PlayLocal("seat_close"); });
            Sub("cabin_power", e =>
            {
                bool off = e.Subject == "off";
                PlayLocal(off ? "breaker_off" : "breaker_on");
                PlayLocal(off ? "power_down" : "power_up", 1f);
                Log("cabin: breaker " + (off ? "off" : "on"));
            });
            Sub("cabin_scan_contact", e => PlayLocal(e.Subject == "hit" ? "scan_hit" : "scan_miss"));
            Sub("cabin_creak", e => { _cabinOwnsCreaks = true; float v = Mathf.Clamp01(e.Value); PlayLocal("creak_" + (char)('a' + (v < 0.3f ? 0 : v < 0.6f ? 1 : v < 0.85f ? 2 : 3)), 0.45f + 0.55f * v); });
            Sub("cabin_breath", e =>
            {
                // the breath comes up in the dark and again while a scan crosses the hull: the cabin's own value says how deep, the lighting how close
                float scan = World != null && World.Cabin != null && World.Cabin.ScanLight > 0.05f ? 1.25f : 1f;
                PlayLocal(e.Subject == "out" ? "breath_out" : "breath_in", Mathf.Min(1.8f, (0.4f + 0.6f * Mathf.Clamp01(e.Value)) * DarkMul() * scan * 1.15f));
            });
            Sub("cabin_repair", OnRepair);
            Sub("cabin_loadout", e => PlayLocal("rack_seat"));
            Sub("cabin_table", e => PlayLocal(e.Subject == "map" ? "ui_select" : "ui_click"));
            Sub("cabin_jolt", e => PlayLocal("cabin_rattle", 0.3f + 0.7f * Mathf.Clamp01(e.Value)));
        }

        void OnRepair(GameEvent e)
        {
            switch (e.Subject)
            {
                case "start":
                    PlayLocal("panel_open");
                    _repair?.Stop(0.05f);
                    _repair = StartLoop("repair_loop", Vector3.zero, 0f, false, false);      // fades in under the hatch (UpdateCabin sets its level)
                    _repairLevel = 0f;
                    break;
                case "done": _repair?.Stop(0.15f); _repair = null; PlayLocal("repair_done"); break;
                case "slip": _repair?.Stop(0.05f); _repair = null; PlayLocal("repair_slip"); break;
            }
        }

        void SetLoopCtl(LoopHandle h, float gain, float pan, float pitch, float cutoff = 0f)
        {
            if (h == null || h.Stopped || h.Voice == 0 || h.Cue == null) return;
            Mixer.Control(h.Voice, new VoiceCtl { Gain = Db.ToLin(h.Cue.LevelDb) * gain * _juiceMaster, Pan = pan, Pitch = pitch, CutoffHz = cutoff <= 0f ? 20000f : cutoff });
        }

        /// <summary>Once a frame: what the cabin's state asks of the sound (the emergency hum, the scan through the hull, the repair's knitting, the heartbeat, the actions' mechanisms).</summary>
        void UpdateCabin(float frameDt)
        {
            // game time, not frame time: in a capture the audio renders in step with the sim, a frame is one tick, and the heartbeat and the fades must keep game time
            float dt = Mathf.Clamp(_now - _cabinNow, 0f, 0.1f);
            _cabinNow = _now;
            if (dt <= 0f) dt = Options.Offline ? 0f : frameDt;
            var cab = World != null ? World.Cabin : null;
            bool paused = World != null && World.Sim.TimeScale <= 0.001f && !Options.Offline;

            // the emergency lighting's hum
            float humTarget = 0f;
            if (cab != null && !paused)
            {
                switch (_cabinState)
                {
                    case "alert": humTarget = 1.0f; break;
                    case "emergency": humTarget = 0.9f; break;
                    case "blackout": humTarget = 0.7f; break;
                    case "dark": humTarget = 0.4f; break;
                }
            }
            _humLevel = Mathf.MoveTowards(_humLevel, humTarget, dt * (humTarget > _humLevel ? 0.8f : 1.5f));
            if (_humLevel > 0.01f && _hum == null) _hum = StartLoop("emergency_hum", Vector3.zero, 0f, false, false);
            if (_hum != null)
            {
                SetLoopCtl(_hum, _humLevel, 0f, 1f);
                if (_humLevel <= 0.01f && humTarget <= 0.01f) { _hum.Stop(0.3f); _hum = null; }
            }

            // the scan crossing the hull: level follows the light, pan follows where the beam is
            float light = cab != null && !paused ? Mathf.Clamp01(cab.ScanLight) : 0f;
            _scanLevel = Mathf.MoveTowards(_scanLevel, light, dt * (light > _scanLevel ? 2.5f : 1.0f));
            float panTarget = cab != null ? Mathf.Clamp(cab.ScanDirShip.x * 1.3f, -1f, 1f) : 0f;
            _scanPan = Mathf.Lerp(_scanPan, panTarget, Mathf.Clamp01(dt * 5f));
            if (_scanLevel > 0.02f && _hullScan == null) _hullScan = StartLoop("hull_scan", Vector3.zero, 0f, false, false);
            if (_hullScan != null)
            {
                SetLoopCtl(_hullScan, _scanLevel, _scanPan, 0.92f + 0.16f * _scanLevel, 900f + 5000f * _scanLevel);
                if (_scanLevel <= 0.02f && light <= 0.02f) { _hullScan.Stop(0.4f); _hullScan = null; }
            }
            bool scanning = light > 0.05f;
            if (scanning != _scanWas) { _scanWas = scanning; if (scanning) Log("cabin: a scan is crossing the hull"); }

            // what the pilot is doing with the ship: the breaker's lever and the rack's pull are heard from the press, the repair knits until the pilot lets go
            string action = cab != null ? cab.Action : null;
            if (action != _lastAction)
            {
                if (action == "breaker") PlayLocal("breaker_lever");
                else if (action == "rack") PlayLocal("rack_pull");
                _lastAction = action;
            }
            if (_repair != null)
            {
                bool repairing = action == "repair";
                _repairLevel = Mathf.MoveTowards(_repairLevel, repairing ? 1f : 0f, dt * (repairing ? 3.5f : 6f));
                SetLoopCtl(_repair, _repairLevel, 0f, 0.9f + 0.45f * Mathf.Clamp01(cab != null ? cab.ActionProgress : 0f));
                if (!repairing && _repairLevel <= 0.01f) { _repair.Stop(0.1f); _repair = null; }
            }

            // the heart: in the dark, faster while a scan is crossing the hull
            bool lonely = cab != null && !paused && (_cabinState == "dark" || _cabinState == "blackout");
            if (lonely)
            {
                float bpm = 54f + 56f * _scanLevel;
                _heartT += dt * bpm / 60f;
                if (_heartT >= 1f)
                {
                    _heartT -= 1f;
                    PlayLocal("heartbeat", (_cabinState == "blackout" ? 0.55f : 0.35f) + 0.6f * _scanLevel);
                }
            }
            else _heartT = 0f;
        }
    }
}
