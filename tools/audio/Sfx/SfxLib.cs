using System;
using System.Collections.Generic;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>One sound effect of the game: how to make it and how the game plays it. The runtime reads this as Audio/sfx/sfx.json.</summary>
    public sealed class SfxDef
    {
        public string Id;
        public Func<int, Sd> Build;
        public int Variants = 1;
        public float FarAmount = 0f;             // > 0: also make a distant version (dulled, smeared, longer)
        public bool Loop;
        public string Bus = "sfx";               // sfx, big, ui
        public float LevelDb = -12f;             // the level at gain 1, re the asset's -1 dBFS peak
        public float MinDist = 60f, MaxDist = 9000f;
        public float NearFadeFrom = 250f, NearFadeTo = 1500f;   // metres over which the near version gives way to the far one
        public float PitchVar = 0.03f;
        public float DuckAmount, DuckSeconds;    // a big moment: lower everything else by this, for this long
        public float Priority = 1f;
        public float ArrivalDelay;               // seconds of delay per kilometre of distance (the far sound arrives late)
        public bool Spatial = true;              // false: heard in the cockpit, not placed
        public string Note = "";
    }

    public static class SfxLib
    {
        public static List<SfxDef> All()
        {
            var l = new List<SfxDef>();
            // ---- guns and missiles
            l.Add(new SfxDef { Id = "laser_light", Variants = 3, FarAmount = 0.8f, LevelDb = -10, MinDist = 40, NearFadeFrom = 300, NearFadeTo = 1600, PitchVar = 0.025f, Note = "friendly fast gun" , Build = SfxWeapons.LaserLight });
            l.Add(new SfxDef { Id = "laser_enemy", Variants = 3, FarAmount = 0.8f, LevelDb = -11.5f, MinDist = 40, NearFadeFrom = 300, NearFadeTo = 1600, PitchVar = 0.025f, Note = "Earth's fast gun", Build = SfxWeapons.LaserEnemy });
            l.Add(new SfxDef { Id = "laser_heavy", Variants = 2, FarAmount = 1.0f, LevelDb = -5.5f, MinDist = 120, NearFadeFrom = 500, NearFadeTo = 3000, PitchVar = 0.03f, Priority = 2f, Note = "heavy gun and capital turrets", Build = SfxWeapons.LaserHeavy });
            l.Add(new SfxDef { Id = "laser_turret", Variants = 1, FarAmount = 1.3f, LevelDb = -6, MinDist = 300, NearFadeFrom = 900, NearFadeTo = 4500, PitchVar = 0.04f, Priority = 2f, ArrivalDelay = 0.18f, Note = "a capital turret, from afar", Build = v => SfxWeapons.LaserTurret(v) });
            l.Add(new SfxDef { Id = "flak_shell", FarAmount = 0.8f, LevelDb = -12, MinDist = 60, Build = v => SfxWeapons.FlakShell() });
            l.Add(new SfxDef { Id = "flak_burst", FarAmount = 1.0f, LevelDb = -9.5f, MinDist = 90, NearFadeFrom = 400, NearFadeTo = 2400, Build = v => SfxWeapons.FlakBurst() });
            l.Add(new SfxDef { Id = "missile_launch", FarAmount = 0.9f, LevelDb = -8.5f, MinDist = 80, Priority = 2f, Build = v => SfxWeapons.MissileLaunch(false) });
            l.Add(new SfxDef { Id = "torpedo_launch", FarAmount = 1.2f, LevelDb = -6, MinDist = 150, Priority = 3f, Build = v => SfxWeapons.MissileLaunch(true) });
            l.Add(new SfxDef { Id = "missile_hiss", Loop = true, LevelDb = -22, MinDist = 40, MaxDist = 1500, PitchVar = 0f, Build = v => SfxWeapons.MissileHiss() });
            l.Add(new SfxDef { Id = "decoy_launch", FarAmount = 0.7f, LevelDb = -12, MinDist = 30, Build = v => SfxWeapons.DecoyLaunch() });
            l.Add(new SfxDef { Id = "decoy_success", FarAmount = 0.8f, LevelDb = -11, MinDist = 60, Build = v => SfxWeapons.DecoySuccess() });
            l.Add(new SfxDef { Id = "near_miss", Variants = 2, LevelDb = -9, MinDist = 20, MaxDist = 400, PitchVar = 0.06f, Priority = 2f, Build = SfxWeapons.NearMiss });
            // ---- beams
            l.Add(new SfxDef { Id = "beam_charge", FarAmount = 1.0f, Bus = "big", LevelDb = -9, MinDist = 700, NearFadeFrom = 2500, NearFadeTo = 8000, PitchVar = 0f, Priority = 4f, DuckAmount = 0.18f, DuckSeconds = 2.4f, Build = v => SfxWeapons.BeamCharge() });
            l.Add(new SfxDef { Id = "beam_fire", FarAmount = 1.2f, Bus = "big", LevelDb = -6, MinDist = 700, NearFadeFrom = 2500, NearFadeTo = 8000, PitchVar = 0f, Priority = 4f, DuckAmount = 0.3f, DuckSeconds = 2.0f, Build = v => SfxWeapons.BeamFire() });
            l.Add(new SfxDef { Id = "beam_loop", Loop = true, Bus = "big", LevelDb = -14, MinDist = 500, MaxDist = 14000, PitchVar = 0f, Priority = 4f, Build = v => SfxWeapons.BeamLoop() });
            l.Add(new SfxDef { Id = "beam_impact_loop", Loop = true, LevelDb = -18, MinDist = 300, MaxDist = 9000, PitchVar = 0f, Build = v => SfxWeapons.BeamImpactLoop() });
            l.Add(new SfxDef { Id = "beam_end", FarAmount = 1.0f, Bus = "big", LevelDb = -12, MinDist = 500, PitchVar = 0f, Build = v => SfxWeapons.BeamEnd() });
            // ---- impacts
            l.Add(new SfxDef { Id = "hit_shield", Variants = 3, FarAmount = 0.8f, LevelDb = -10, MinDist = 70, PitchVar = 0.04f, Build = v => SfxImpacts.HitShield(v, false) });
            l.Add(new SfxDef { Id = "hit_shield_player", Variants = 3, LevelDb = -7, MinDist = 30, Spatial = false, PitchVar = 0.04f, Priority = 3f, Build = v => SfxImpacts.HitShield(v, true) });
            l.Add(new SfxDef { Id = "hit_hull", Variants = 3, FarAmount = 0.8f, LevelDb = -9.5f, MinDist = 70, PitchVar = 0.05f, Build = v => SfxImpacts.HitHull(v, false) });
            l.Add(new SfxDef { Id = "hit_hull_player", Variants = 3, LevelDb = -5, MinDist = 30, Spatial = false, PitchVar = 0.04f, Priority = 3f, Build = v => SfxImpacts.HitHull(v, true) });
            l.Add(new SfxDef { Id = "hit_hull_player_heavy", Variants = 2, LevelDb = -3, MinDist = 30, Spatial = false, PitchVar = 0.03f, Priority = 4f, DuckAmount = 0.25f, DuckSeconds = 0.9f, Build = v => SfxImpacts.HitHull(v, true, true) });
            l.Add(new SfxDef { Id = "shield_down", LevelDb = -6, Spatial = false, Priority = 4f, Build = v => SfxImpacts.ShieldDown() });
            l.Add(new SfxDef { Id = "subsystem_blast", FarAmount = 0.9f, LevelDb = -8, MinDist = 150, Priority = 2f, Build = v => SfxImpacts.SubsystemBlast(false) });
            l.Add(new SfxDef { Id = "collision", LevelDb = -5, MinDist = 40, Spatial = false, Priority = 4f, DuckAmount = 0.25f, DuckSeconds = 1.2f, Build = v => SfxImpacts.Collision() });
            // ---- explosions
            l.Add(new SfxDef { Id = "explosion_small", Variants = 2, FarAmount = 1.0f, LevelDb = -9, MinDist = 100, NearFadeFrom = 400, NearFadeTo = 3000, ArrivalDelay = 0.12f, Priority = 2f, Build = v => SfxImpacts.Explosion(0, v) });
            l.Add(new SfxDef { Id = "explosion_medium", Variants = 2, FarAmount = 1.2f, Bus = "big", LevelDb = -6, MinDist = 220, NearFadeFrom = 800, NearFadeTo = 4500, ArrivalDelay = 0.2f, Priority = 3f, DuckAmount = 0.22f, DuckSeconds = 1.2f, Build = v => SfxImpacts.Explosion(1, v) });
            l.Add(new SfxDef { Id = "explosion_large", Variants = 1, FarAmount = 1.4f, Bus = "big", LevelDb = -3, MinDist = 900, NearFadeFrom = 2500, NearFadeTo = 9000, ArrivalDelay = 0.3f, Priority = 5f, DuckAmount = 0.55f, DuckSeconds = 3.5f, PitchVar = 0.01f, Build = v => SfxImpacts.Explosion(2, v) });
            l.Add(new SfxDef { Id = "breakup", FarAmount = 1.3f, Bus = "big", LevelDb = -4, MinDist = 900, NearFadeFrom = 2500, NearFadeTo = 9000, ArrivalDelay = 0.25f, Priority = 5f, DuckAmount = 0.4f, DuckSeconds = 2.4f, PitchVar = 0.01f, Build = v => SfxImpacts.BreakUp() });
            l.Add(new SfxDef { Id = "shockwave", Bus = "big", LevelDb = -5, MinDist = 400, MaxDist = 12000, Priority = 5f, DuckAmount = 0.3f, DuckSeconds = 2.5f, PitchVar = 0.01f, Build = v => SfxImpacts.Shockwave() });
            for (int k = 0; k < 4; k++) { int kk = k; l.Add(new SfxDef { Id = "creak_" + (char)('a' + k), LevelDb = -15 + (k >= 2 ? 1 : 0), Spatial = false, PitchVar = 0.06f, Note = "hull groan", Build = v => SfxImpacts.Creak(kk) }); }
            // ---- ship and cockpit
            l.Add(new SfxDef { Id = "engine_loop", Loop = true, LevelDb = -25, Spatial = false, PitchVar = 0f, Note = "pitch follows speed", Build = v => SfxShip.EngineLoop() });
            l.Add(new SfxDef { Id = "afterburner_loop", Loop = true, LevelDb = -19, Spatial = false, PitchVar = 0f, Build = v => SfxShip.AfterburnerLoop() });
            l.Add(new SfxDef { Id = "afterburner_on", LevelDb = -12, Spatial = false, PitchVar = 0f, Build = v => SfxShip.AfterburnerOn() });
            l.Add(new SfxDef { Id = "afterburner_off", LevelDb = -16, Spatial = false, PitchVar = 0f, Build = v => SfxShip.AfterburnerOff() });
            l.Add(new SfxDef { Id = "afterburner_whoomph", LevelDb = -9, Spatial = false, PitchVar = 0.02f, Priority = 4f, Note = "the boost's ignition punch (juice), over afterburner_on", Build = v => SfxShip.AfterburnerWhoomph() });
            l.Add(new SfxDef { Id = "afterburner_sputter", LevelDb = -12, Spatial = false, PitchVar = 0.02f, Priority = 4f, Note = "the empty tank: four coughs and a flameout", Build = v => SfxShip.AfterburnerSputter() });
            l.Add(new SfxDef { Id = "cabin_air", Loop = true, LevelDb = -26, Spatial = false, PitchVar = 0f, Build = v => SfxShip.CabinAir() });
            l.Add(new SfxDef { Id = "alarm_hull", Loop = true, LevelDb = -19, Spatial = false, PitchVar = 0f, Priority = 4f, Build = v => SfxShip.AlarmHull() });
            l.Add(new SfxDef { Id = "alarm_beep", LevelDb = -17, Spatial = false, PitchVar = 0f, Priority = 4f, Bus = "ui", Build = v => SfxShip.AlarmBeep(1180f) });
            l.Add(new SfxDef { Id = "alarm_beep_low", LevelDb = -17, Spatial = false, PitchVar = 0f, Priority = 4f, Bus = "ui", Build = v => SfxShip.AlarmBeep(780f) });
            l.Add(new SfxDef { Id = "system_fail", LevelDb = -11, Spatial = false, Priority = 4f, Build = v => SfxShip.SystemFail() });
            l.Add(new SfxDef { Id = "jump_in", FarAmount = 0.9f, LevelDb = -8, MinDist = 200, NearFadeFrom = 1500, NearFadeTo = 6000, Priority = 3f, ArrivalDelay = 0.05f, Build = v => SfxShip.JumpIn(false) });
            l.Add(new SfxDef { Id = "jump_in_big", FarAmount = 1.2f, Bus = "big", LevelDb = -5, MinDist = 900, NearFadeFrom = 3000, NearFadeTo = 10000, Priority = 5f, DuckAmount = 0.35f, DuckSeconds = 3.0f, Build = v => SfxShip.JumpIn(true) });
            l.Add(new SfxDef { Id = "jump_out", FarAmount = 0.9f, LevelDb = -9, MinDist = 200, NearFadeFrom = 1500, NearFadeTo = 6000, Priority = 3f, Build = v => SfxShip.JumpOut(false) });
            l.Add(new SfxDef { Id = "jump_out_big", FarAmount = 1.2f, Bus = "big", LevelDb = -6, MinDist = 900, NearFadeFrom = 3000, NearFadeTo = 10000, Priority = 5f, DuckAmount = 0.3f, DuckSeconds = 2.5f, Build = v => SfxShip.JumpOut(true) });
            l.Add(new SfxDef { Id = "scan_sweep", Bus = "sfx", LevelDb = -15, Spatial = false, PitchVar = 0f, Note = "a scan crawling over the hull (the cabin's go-dark moment)", Build = v => SfxShip.ScanSweep() });
            l.Add(new SfxDef { Id = "earth_horn", Bus = "big", LevelDb = -3, Spatial = false, PitchVar = 0f, Priority = 6f, DuckAmount = 0.5f, DuckSeconds = 6.0f, Note = "a deep Reaper-like horn when an Earth capital ship arrives", Build = v => SfxShip.EarthHorn(true) });
            l.Add(new SfxDef { Id = "earth_horn_small", Bus = "big", LevelDb = -5, Spatial = false, PitchVar = 0f, Priority = 6f, DuckAmount = 0.35f, DuckSeconds = 3.5f, Note = "the same, for a corvette", Build = v => SfxShip.EarthHorn(false) });
            // ---- the cabin on foot (the cabin system raises the events; see AudioSystem)
            l.Add(new SfxDef { Id = "step_plate", Variants = 3, Bus = "ui", LevelDb = -17, Spatial = false, PitchVar = 0.05f, Note = "a footfall on the deck plate", Build = v => SfxCabin.Step(0, v) });
            l.Add(new SfxDef { Id = "step_grating", Variants = 3, Bus = "ui", LevelDb = -17, Spatial = false, PitchVar = 0.05f, Note = "a footfall on grating", Build = v => SfxCabin.Step(1, v) });
            l.Add(new SfxDef { Id = "step_rung", Variants = 3, Bus = "ui", LevelDb = -18, Spatial = false, PitchVar = 0.05f, Note = "a foot on a rung or riser", Build = v => SfxCabin.Step(2, v) });
            l.Add(new SfxDef { Id = "seat_open", Bus = "ui", LevelDb = -19, Spatial = false, PitchVar = 0.03f, Note = "the harness opens, the pilot stands", Build = v => SfxCabin.Seat(true) });
            l.Add(new SfxDef { Id = "seat_close", Bus = "ui", LevelDb = -19, Spatial = false, PitchVar = 0.03f, Note = "the pilot sits, the harness closes", Build = v => SfxCabin.Seat(false) });
            l.Add(new SfxDef { Id = "breaker_off", LevelDb = -10, Spatial = false, PitchVar = 0f, Priority = 4f, Note = "the main breaker out: the displays die", Build = v => SfxCabin.Breaker(false) });
            l.Add(new SfxDef { Id = "breaker_on", LevelDb = -10, Spatial = false, PitchVar = 0f, Priority = 4f, Note = "the main breaker in: the displays boot", Build = v => SfxCabin.Breaker(true) });
            l.Add(new SfxDef { Id = "scan_hit", LevelDb = -12, Spatial = false, PitchVar = 0f, Priority = 4f, Note = "a scan found the ship", Build = v => SfxCabin.ScanHit() });
            l.Add(new SfxDef { Id = "breath_in", Variants = 2, Bus = "ui", LevelDb = -26, Spatial = false, PitchVar = 0.04f, Build = v => SfxCabin.Breath(true, v) });
            l.Add(new SfxDef { Id = "breath_out", Variants = 2, Bus = "ui", LevelDb = -26, Spatial = false, PitchVar = 0.04f, Build = v => SfxCabin.Breath(false, v) });
            l.Add(new SfxDef { Id = "repair_loop", Loop = true, Bus = "ui", LevelDb = -22, Spatial = false, PitchVar = 0f, Note = "a hand repair: living tech knitting (pitch rises with progress)", Build = v => SfxCabin.RepairLoop() });
            l.Add(new SfxDef { Id = "repair_done", Bus = "ui", LevelDb = -16, Spatial = false, PitchVar = 0f, Build = v => SfxCabin.RepairDone() });
            l.Add(new SfxDef { Id = "repair_slip", Bus = "ui", LevelDb = -14, Spatial = false, PitchVar = 0.03f, Build = v => SfxCabin.RepairSlip() });
            l.Add(new SfxDef { Id = "rack_seat", Bus = "ui", LevelDb = -14, Spatial = false, PitchVar = 0.03f, Note = "a module seated in the weapon rack", Build = v => SfxCabin.RackSeat() });
            l.Add(new SfxDef { Id = "cabin_rattle", LevelDb = -17, Spatial = false, PitchVar = 0.05f, Note = "loose things shaken by a shove", Build = v => SfxCabin.Rattle() });
            // ---- interface
            l.Add(new SfxDef { Id = "ui_click", Bus = "ui", LevelDb = -16, Spatial = false, PitchVar = 0f, Build = v => SfxShip.UiClick() });
            l.Add(new SfxDef { Id = "ui_select", Bus = "ui", LevelDb = -15, Spatial = false, PitchVar = 0f, Build = v => SfxShip.UiSelect() });
            l.Add(new SfxDef { Id = "target_blip", Bus = "ui", LevelDb = -18, Spatial = false, PitchVar = 0f, Build = v => SfxShip.TargetBlip() });
            l.Add(new SfxDef { Id = "lock_acquired", Bus = "ui", LevelDb = -15, Spatial = false, PitchVar = 0f, Build = v => SfxShip.LockAcquired() });
            l.Add(new SfxDef { Id = "lock_lost", Bus = "ui", LevelDb = -16, Spatial = false, PitchVar = 0f, Build = v => SfxShip.LockLost() });
            l.Add(new SfxDef { Id = "power_blip", Bus = "ui", LevelDb = -18, Spatial = false, PitchVar = 0f, Build = v => SfxShip.PowerBlip() });
            l.Add(new SfxDef { Id = "squelch_open", Bus = "ui", LevelDb = -20, Spatial = false, PitchVar = 0.02f, Build = v => SfxShip.SquelchOpen() });
            l.Add(new SfxDef { Id = "squelch_close", Bus = "ui", LevelDb = -21, Spatial = false, PitchVar = 0.02f, Build = v => SfxShip.SquelchClose() });
            return l;
        }
    }
}
