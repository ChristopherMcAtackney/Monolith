using System.Collections.Generic;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    /// <summary>
    /// Everything the HUD draws in one frame, as plain data: the whole interface between the game and the HUD. <see cref="HudReader"/> fills it from <see cref="GameWorld"/> (and the
    /// camera, for where things are on screen); <see cref="HudRenderer"/> draws it and reads nothing else. So the HUD can be checked with made-up frames, drawn at any size, and
    /// restyled without touching the game. Screen positions are pixels from the top-left of the screen the frame was read for.
    /// </summary>
    public sealed class HudFrame
    {
        public sealed class MissileLine { public string Name; public int Ammo; public bool Selected, Offline; public float Reload01 = 1f; }
        public sealed class SystemLine { public string Text; public bool Bad; }
        public sealed class DirectiveLine { public string Text; public MissionBoard.State State; public float Age; }
        public sealed class FeedLine { public string Speaker, Text; public float Alpha = 1f; public bool Alert; }

        // ---- the screen
        public float Width = 1920f, Height = 1080f;
        /// <summary>Real seconds, for blinks and the radar's sweep.</summary>
        public float Time;

        // ---- who
        public bool HasShip, Alive = true;
        public string ClassName;
        /// <summary>False for a made-up frame (a check) that has no real sensors.</summary>
        public bool UsePadGlyphs = true;

        // ---- flight
        public float Speed, TopSpeed = 70f, BoostSpeed = 235f, Throttle, Fuel01 = 1f;
        public bool Matching, Burning, EnginesDown, EnginesCrippled;
        public float SpeedScale = 1f;

        // ---- defence and energy
        public float Hull01 = 1f, Shield01 = 1f, Energy01 = 1f;
        public bool HasShield = true, HasEnergy = true;
        public PowerMode Power = PowerMode.Balanced;
        public bool SelfHeals, Mending;

        // ---- arms
        public string GunName, HeavyName;
        public bool GunOnline = true, HeavyOnline = true;
        public readonly List<MissileLine> Missiles = new List<MissileLine>();
        public bool LaunchersOffline;
        public int Decoys, DecoysMax;

        // ---- the target
        public bool HasTarget, TargetHostile = true, TargetOnScreen;
        public string TargetName, TargetClass, TargetSize;
        public float TargetHull01 = 1f, TargetShield01, TargetDist, TargetSpeed;
        public bool TargetHasShield;
        public Vector2 TargetScreen;
        public float TargetRadiusPx = 24f;
        /// <summary>When off screen: the unit direction from the reticle toward it (y down).</summary>
        public Vector2 TargetDir = Vector2.down;
        public bool HasPart;
        public string PartName;
        public float PartHealth01 = 1f;
        public Vector2 PartScreen;
        public float PartRadiusPx = 12f;
        public bool PartOnScreen;
        public bool TargetHasParts;
        public bool HasLead;
        public Vector2 LeadScreen;

        // ---- lock
        public LockPhase Lock;
        public float LockProgress;
        public bool WeaponNeedsLock, WeaponSeeks;
        public string LockWeaponName;

        // ---- threats
        public int IncomingMissiles;
        public float IncomingNearest, IncomingEta = 99f;
        /// <summary>Unit direction from the reticle toward the nearest incoming missile (y down); only meaningful when <see cref="IncomingOffAxis"/>.</summary>
        public Vector2 IncomingDir;
        public bool IncomingOffAxis;
        public bool LockedOnMe;
        public int BeingLocked;

        // ---- radar and map
        public readonly List<Contact> Contacts = new List<Contact>(64);
        public float RadarRange = 3000f, MapRange = 3000f;
        /// <summary>0 clear .. 1 the sensors are gone.</summary>
        public float SensorStatic;

        // ---- systems you feel
        public readonly List<SystemLine> Systems = new List<SystemLine>();

        // ---- mission, radio, notices
        public readonly List<DirectiveLine> Directives = new List<DirectiveLine>();
        public string Prompt;
        public bool MissionRunning;
        public readonly List<FeedLine> Radio = new List<FeedLine>();
        public readonly List<FeedLine> Notices = new List<FeedLine>();

        // ---- modes and overlays
        public bool MapOpen, QuickMenu;
        public string LastOrder;
        /// <summary>1 right after a hit on the hull, fading: the frame of the screen flashes (set by the HUD component).</summary>
        public float DamageFlash;
        public string[] Info;
        public string Banner, BannerSub;
        public string[] Overlay;
        public string Footer;

        /// <summary>Forget what the world told us last frame. The HUD component's own fields (the feeds, the overlay, the banner, the info lines, the footer, the damage flash, the glyph choice) are left alone.</summary>
        readonly Stack<MissileLine> _missilePool = new Stack<MissileLine>();
        readonly Stack<SystemLine> _systemPool = new Stack<SystemLine>();
        readonly Stack<DirectiveLine> _directivePool = new Stack<DirectiveLine>();

        /// <summary>Lines are reused from frame to frame, so reading the world costs no garbage.</summary>
        public MissileLine AddMissile() { var m = _missilePool.Count > 0 ? _missilePool.Pop() : new MissileLine(); m.Selected = false; m.Offline = false; m.Reload01 = 1f; Missiles.Add(m); return m; }
        public SystemLine AddSystem(string text, bool bad) { var l = _systemPool.Count > 0 ? _systemPool.Pop() : new SystemLine(); l.Text = text; l.Bad = bad; Systems.Add(l); return l; }
        public DirectiveLine AddDirective(string text, MissionBoard.State state, float age) { var d = _directivePool.Count > 0 ? _directivePool.Pop() : new DirectiveLine(); d.Text = text; d.State = state; d.Age = age; Directives.Add(d); return d; }

        public void ResetWorld()
        {
            foreach (var m in Missiles) _missilePool.Push(m);
            foreach (var l in Systems) _systemPool.Push(l);
            foreach (var d in Directives) _directivePool.Push(d);
            Missiles.Clear(); Contacts.Clear(); Systems.Clear(); Directives.Clear();
            HasShip = false; Alive = true; HasTarget = false; HasPart = false; HasLead = false; TargetHasParts = false; TargetOnScreen = false; PartOnScreen = false;
            Lock = LockPhase.None; LockProgress = 0f; WeaponNeedsLock = false; WeaponSeeks = false; LockWeaponName = null;
            IncomingMissiles = 0; IncomingOffAxis = false; LockedOnMe = false; BeingLocked = 0;
            SensorStatic = 0f; Prompt = null; MissionRunning = false; MapOpen = false; QuickMenu = false; LastOrder = null;
            Matching = false; Burning = false; EnginesDown = false; EnginesCrippled = false; LaunchersOffline = false; Mending = false;
        }
    }
}
