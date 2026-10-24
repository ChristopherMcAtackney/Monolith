using System;
using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    public enum AiRole { Fighter, Bomber }

    public enum GoalKind
    {
        /// <summary>Free engage: pick the best hostile and fight it (FS2's default for wings).</summary>
        Engage,
        /// <summary>Attack one ship (and, if given, one of its parts) until it dies.</summary>
        Attack,
        /// <summary>Destroy one ship's engines and leave it drifting.</summary>
        Disable,
        /// <summary>Protect a ship: fight whatever attacks it, else stay near it (cover me, escorts).</summary>
        Guard,
        /// <summary>Fly in formation on a leader.</summary>
        Form,
        /// <summary>Torpedo runs on a capital ship (bombers).</summary>
        Bomb,
    }

    public struct AiGoal
    {
        public GoalKind Kind;
        public int Target;
        /// <summary>A part of the target to attack (its index), or -1.</summary>
        public int Part;
    }

    /// <summary>
    /// The brain of one AI fighter or bomber (FS2's ai_chase, ai_big_strafe, guard and formation, our own code), flying its ship only through
    /// its <see cref="ControlInput"/>. Each tick it perceives (hits, missiles, damage), picks or keeps a target for its goal, chooses a mode and
    /// flies it, then keeps clear of capital hulls and other ships:
    /// <list type="bullet">
    /// <item><b>Fight</b> a small ship: get on its tail with lead (FS2's aim error from accuracy, growing when the target has only just come into
    /// range), fire the guns when the lead is in the firing cone (narrowed and held longer by patience), the heavy gun once shields are down,
    /// lock-on and heat-seeking missiles when they can hit, and match speed when close behind. Break off when it goes nowhere (FS2's get-away),
    /// pass a head-on target and come round again.</item>
    /// <item><b>Evade</b> when hit (a chance from evasion, higher when hurt): jink hard with the afterburner, then re-engage. <b>Missile evasion</b>:
    /// a hard turn across the missile's path and decoys (FS2's countermeasure chance per level, scaled by evasion).</item>
    /// <item><b>Peel off</b> when hull falls below what courage allows (never for a fearless drone): run home under afterburner, mend, come back.</item>
    /// <item><b>Strafe</b> a capital ship (FS2's attack run and retreat), <b>bomb</b> it (line up, lock, launch the torpedo, peel away, repeat),
    /// <b>guard</b> a ship, <b>form</b> on a leader.</item>
    /// </list>
    /// </summary>
    public sealed class AiPilot : IPilotSource
    {
        enum Mode { Fight, Evade, Extend, MissileEvade, Peel, StrafeRun, StrafeOut, BombApproach, BombLine, BombPeel, Form, Escort, Recover, Idle }

        readonly AiDirector _ai;
        public readonly int Ship;
        public readonly string ProfileId;
        public AiRole Role;
        public Skill Skill;
        /// <summary>The goal it goes back to when an order is done (wingmen: cover the player; drones: engage).</summary>
        public AiGoal DefaultGoal;
        public AiGoal Goal { get; private set; }
        /// <summary>Its place in the player's wing (formation slot), or -1.</summary>
        public int WingSlot = -1;
        /// <summary>A skill forced for the arena's knob sweeps (null: from the profile and difficulty).</summary>
        public Func<Skill, Skill> SkillOverride;

        Mode _mode = Mode.Fight;
        float _now, _modeAt, _modeFor;
        int _target = GameWorld.NoShip, _targetPart = -1;
        float _retargetAt, _targetAt, _lastMyHit = -999f, _inRangeSince = -1f, _holdSince = -1f;
        float _lastDamageSeen = -999f, _lastEvadeRoll = -999f, _decoyRollAt, _missileWarnAt = -999f;
        bool _missileEvadeWanted;
        Vector3 _errVec, _aimErr; float _errUntil;
        float _nextGunAt, _nextHeavyAt, _nextMissileAt;
        int _missileSlot = -1, _missileFor = GameWorld.NoShip;
        Vector3 _point, _dir;
        float _extendDist;
        float _avoidUntil; Vector3 _avoidDir;
        bool _abHeld;
        // The pilot's picture of its target, as old as the level makes it (FS2's Skill_level_delay): what it aims from.
        readonly ShipState _view = new ShipState();
        float _viewAt = -999f; int _viewOf = GameWorld.NoShip;
        // How fast the aim direction is turning (world, rad/s), fed forward to the stick so the nose keeps up with a manoeuvring target.
        Vector3 _leadPrev, _leadRate; int _leadFor = GameWorld.NoShip; bool _leadValid, _leadUsed;
        int _torpsAtLine;
        float _peelAgainAt = -999f, _hullAtRejoin;
        float _jinkA, _jinkB, _jinkF1, _jinkF2, _jinkNext, _jinkFlip, _breakSide = 1f, _evadeAgainAt;
        Vector3 _jinkDir;
        int _evadeFrom = GameWorld.NoShip;
        float _tailedCheckAt;
        uint _rng;

        public int Target => _target;
        public string ModeName => _mode.ToString();
        public bool Peeling => _mode == Mode.Peel;

        public AiPilot(AiDirector ai, int ship, string profile, AiRole role, uint seed)
        {
            _ai = ai; Ship = ship; ProfileId = profile; Role = role;
            _rng = seed == 0 ? 1u : seed;
            Reskill();
            DefaultGoal = new AiGoal { Kind = role == AiRole.Bomber ? GoalKind.Bomb : GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
            Goal = DefaultGoal;
            _jinkA = Rand() * 6.3f; _jinkB = Rand() * 6.3f; _jinkF1 = 1.1f + Rand() * 1.3f; _jinkF2 = 0.8f + Rand() * 1.1f;
        }

        public void Reskill()
        {
            var s = _ai.World.Ships.Get(Ship);
            bool hostile = s != null && _ai.IsHostileSide(s.Team);
            Skill = _ai.Data.Resolve(ProfileId, _ai.Level, hostile);
            if (SkillOverride != null) Skill = SkillOverride(Skill);
        }

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }
        Vector3 RandUnit() { var v = new Vector3(Rand() * 2f - 1f, Rand() * 2f - 1f, Rand() * 2f - 1f); return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up; }

        /// <summary>Give the pilot a goal (wingman orders, mission orders, the scene). Its default goal is kept for when this one is done.</summary>
        public void SetGoal(AiGoal goal)
        {
            Goal = goal;
            _retargetAt = 0f;
            if (goal.Kind == GoalKind.Form || goal.Kind == GoalKind.Guard) { if (_mode != Mode.Peel) SetMode(Mode.Fight); }
            else if (_mode != Mode.Peel && _mode != Mode.MissileEvade) SetMode(Mode.Fight);
            if (goal.Kind == GoalKind.Attack || goal.Kind == GoalKind.Disable || goal.Kind == GoalKind.Bomb) SwitchTarget(goal.Target, goal.Part);
        }

        void SetMode(Mode m, float seconds = 0f)
        {
            if (m != _mode) _modeAt = _now;
            _mode = m;
            _modeFor = seconds;
        }

        float InMode => _now - _modeAt;

        void SwitchTarget(int id, int part)
        {
            if (id != _target) { _ai.NoteTarget(_target, id); _targetAt = _now; _inRangeSince = -1f; _holdSince = -1f; _lastMyHit = -999f; _missileSlot = -1; }
            _target = id; _targetPart = part;
        }

        // ------------------------------------------------------------------------------------------------ what it hears

        /// <summary>One of this pilot's shots or missiles hurt <paramref name="victim"/>.</summary>
        public void OnMyHit(int victim, float now) { if (victim == _target) _lastMyHit = now; }

        public void OnMissileWarning(float now)
        {
            if (now - _missileWarnAt < 0.5f) return;
            _missileWarnAt = now;
            // FS2 does not always dodge: the better the pilot's evasion, the likelier it reacts to this one.
            if (Rand() < 0.15f + 0.85f * Skill.Evasion) _missileEvadeWanted = true;
        }

        public void OnCollision(int other, Vector3 point, Vector3 normal, float now)
        {
            var oc = _ai.World.Combat.Get(other);
            var me = _ai.World.Ships.Get(Ship);
            if (oc == null || me == null) return;
            Vector3 away = me.Position - point;
            if (away.sqrMagnitude < 1f) away = me.Position - oc.Ship.Position;
            _dir = away.normalized;
            if (_mode != Mode.Peel) SetMode(Mode.Recover, AiDirector.IsBig(oc) ? 1.6f : 0.8f);
        }

        // ------------------------------------------------------------------------------------------------ the tick

        public ControlInput Sample(in SimFrame frame)
        {
            var input = default(ControlInput);
            var me = _ai.World.Ships.Get(Ship);
            var mc = _ai.World.Combat.Get(Ship);
            if (me == null || mc == null || !mc.Alive) { _abHeld = false; return input; }
            _now = frame.Time;

            Perceive(me, mc);
            ChooseTarget(me, mc);

            Vector3 goal = me.Forward;
            float throttle = 0.8f;
            bool ab = false;
            Vector3? up = null;
            float bank = 0.6f;
            _leadUsed = false;
            Act(me, mc, ref input, ref goal, ref throttle, ref ab, ref up, ref bank);
            Vector3 g0 = goal;
            Avoid(me, ref goal, ref throttle, ref ab);
            if (!_leadUsed || goal != g0) { _leadValid = false; _leadRate = Vector3.zero; }

            AiSteer.TurnTo(me, goal, Skill.TurnScale, ref input, up, bank, _leadUsed && goal == g0 ? _leadRate : Vector3.zero);
            input.Throttle = Mathf.Clamp01(throttle);
            Afterburner(me, ab, ref input);
            if (_target != GameWorld.NoShip && _ai.World.Ships.Get(_target) != null)
            {
                input.TargetShip = _target;
                if (_targetPart >= 0) input.TargetPartPlusOne = _targetPart + 1;
            }
            return input;
        }

        void Afterburner(ShipState me, bool want, ref ControlInput input)
        {
            bool press = false;
            if (want && me.FuelMax > 0f)
            {
                // The flight model lights it on a press: holding a dead burner needs a release first.
                if (me.AfterburnerOn) press = me.Fuel > me.FuelMax * 0.04f;
                else if (me.Fuel > me.FuelMax * 0.3f) press = !_abHeld;
            }
            if (press) input.Buttons |= ControlButtons.Afterburner;
            _abHeld = press;
        }

        // ------------------------------------------------------------------------------------------------ perceiving

        float PeelHull => Mathf.Lerp(_ai.Data.Tuning.PeelHullNoCourage, _ai.Data.Tuning.PeelHullFullCourage, Skill.Courage);
        bool InPlayerWing => WingSlot >= 0;

        void Perceive(ShipState me, ShipCombat mc)
        {
            // Hurt badly enough to pull out (courage); a fearless drone never does.
            if (!Skill.Fearless && !mc.Invulnerable && _mode != Mode.Peel && mc.Hull01 < PeelHull && (_now >= _peelAgainAt || mc.Hull01 < _hullAtRejoin - 0.1f))
            {
                SetMode(Mode.Peel);
                _ai.Stats.Peeled(Ship, mc.Hull01);
                if (InPlayerWing) _ai.Radio.Say("peel", Ship);
                return;
            }
            if (mc.LastDamagedTime > _lastDamageSeen)
            {
                _lastDamageSeen = mc.LastDamagedTime;
                int attacker = mc.LastAttacker;
                var ac = _ai.World.Combat.Get(attacker);
                bool smallAttacker = ac != null && ac.Alive && !AiDirector.IsBig(ac);
                // FS2's dynamic chase: an enemy that hurts you and is not your target becomes it (not when on an order, a run or out of the fight).
                bool playerFull = attacker == _ai.World.LocalPlayerShip && _ai.IsHostileSide(me.Team) && _ai.AttackersOn(attacker) >= Skill.MaxPlayerAttackers;
                playerFull |= Committed();
                if (smallAttacker && attacker != _target && !playerFull && (Goal.Kind == GoalKind.Engage || Goal.Kind == GoalKind.Guard) && _mode != Mode.Peel)
                {
                    var t = _ai.World.Ships.Get(_target);
                    float dT = t != null ? Vector3.Distance(t.Position, me.Position) : float.MaxValue;
                    float dA = Vector3.Distance(ac.Ship.Position, me.Position);
                    var tc = _ai.World.Combat.Get(_target);
                    if (t == null || (tc != null && AiDirector.IsBig(tc)) || dA < dT * 1.5f) SwitchTarget(attacker, -1);
                }
                // Jink away (FS2 evades when hit unless near full strength; ours is a chance that grows with evasion and with damage).
                // A pilot whose shield still holds keeps attacking (FS2: near full strength it fights on); a quiet spell after each evasion
                // gives it the chance to.
                bool canEvade = (_mode == Mode.Fight || _mode == Mode.StrafeRun || _mode == Mode.Escort) && Goal.Kind != GoalKind.Form && _now >= _evadeAgainAt;
                if (smallAttacker && canEvade && _now - _lastEvadeRoll > 1.0f && (mc.Shield01 < 0.5f || mc.ShieldMax <= 0f))
                {
                    _lastEvadeRoll = _now;
                    float chance = 0.1f + 0.6f * Skill.Evasion;
                    if (Role == AiRole.Bomber) chance *= 0.5f;
                    if (Rand() < chance)
                    {
                        _point = ac.Ship.Position; _evadeFrom = attacker; _breakSide = Rand() < 0.5f ? -1f : 1f;
                        _jinkNext = 0f;
                        SetMode(Mode.Evade, 0.8f + 1.5f * Skill.Evasion + 0.5f * Rand());
                        _ai.Stats.Evaded(Ship);
                    }
                }
            }
            // Someone on my tail and pointing at me: an evasive pilot does not wait to be hit (checked a few times a second).
            if (_now >= _tailedCheckAt && (_mode == Mode.Fight || _mode == Mode.Escort) && Role != AiRole.Bomber && _now >= _evadeAgainAt && mc.Shield01 < 0.7f)
            {
                _tailedCheckAt = _now + 0.25f;
                var list = _ai.Small;
                for (int i = 0; i < list.Count; i++)
                {
                    var o = list[i];
                    if (!o.Alive || o.Ship.Target != Ship || !Teams.Hostile(me.Team, o.Ship.Team)) continue;
                    if (!AiStats.OnTail(o.Ship, me, 450f, 0.96f, 0.6f)) continue;
                    if (Rand() < Skill.Evasion * 0.12f)
                    {
                        _point = o.Ship.Position; _evadeFrom = o.Id; _breakSide = Rand() < 0.5f ? -1f : 1f;
                        _jinkNext = 0f;
                        SetMode(Mode.Evade, 0.8f + 1.5f * Skill.Evasion + 0.5f * Rand());
                        _ai.Stats.Evaded(Ship);
                    }
                    break;
                }
            }
            // Missiles: a hard turn across the path once it is close (if this pilot reacts to this one), decoys by chance.
            if (mc.IncomingMissiles > 0)
            {
                if (_missileEvadeWanted && mc.IncomingEta < 3f && _mode != Mode.MissileEvade && _mode != Mode.Recover && _mode != Mode.BombLine)
                {
                    _missileEvadeWanted = false;
                    SetMode(Mode.MissileEvade, 3.5f);
                    _ai.Stats.Evaded(Ship);
                }
            }
            else _missileEvadeWanted = false;
        }

        // ------------------------------------------------------------------------------------------------ targets

        bool Alive(int id, out ShipState s, out ShipCombat c)
        {
            s = _ai.World.Ships.Get(id); c = _ai.World.Combat.Get(id);
            return s != null && c != null && c.Alive;
        }

        void ChooseTarget(ShipState me, ShipCombat mc)
        {
            // A bomber with its torpedoes spent goes home to its own capital ship and guards it (FS2's bombers leave after their runs; a
            // bomber strafing a capital ship with its guns for the rest of the fight would do more than its torpedoes did).
            if (Goal.Kind == GoalKind.Bomb && Role == AiRole.Bomber && !HasTorpedoes(mc))
            {
                ShipCombat home = null; float bd = float.MaxValue;
                foreach (var b in _ai.Big)
                    if (b.Ship.Team == me.Team) { float d = (b.Ship.Position - me.Position).sqrMagnitude; if (d < bd) { bd = d; home = b; } }
                DefaultGoal = home != null ? new AiGoal { Kind = GoalKind.Guard, Target = home.Id, Part = -1 } : new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
                Goal = DefaultGoal;
                _retargetAt = 0f;
                SwitchTarget(GameWorld.NoShip, -1);
                if (_mode != Mode.Peel) SetMode(Mode.Fight);
            }
            switch (Goal.Kind)
            {
                case GoalKind.Attack:
                case GoalKind.Disable:
                case GoalKind.Bomb:
                    if (!Alive(Goal.Target, out var gs, out var gc) || !Teams.Hostile(me.Team, gs.Team)) { DoneWithGoal(); return; }
                    if (Goal.Kind == GoalKind.Disable && (gc.Flags & CombatFlags.EnginesDown) != 0)
                    {
                        if (InPlayerWing) _ai.Radio.Say("disabled", Ship, force: true);
                        DoneWithGoal();
                        return;
                    }
                    int part = Goal.Kind == GoalKind.Disable ? NearestEngine(gc, me) : (Goal.Part >= 0 && Goal.Part < gc.Parts.Count && !gc.Parts[Goal.Part].Destroyed ? Goal.Part : -1);
                    SwitchTarget(Goal.Target, part);
                    return;
                case GoalKind.Form:
                    SwitchTarget(GameWorld.NoShip, -1);
                    return;
                case GoalKind.Guard:
                    if (!Alive(Goal.Target, out var guarded, out _)) { DoneWithGoal(); return; }
                    if (_now >= _retargetAt || !Alive(_target, out _, out _))
                    {
                        _retargetAt = _now + 1.0f + Rand();
                        SwitchTarget(ThreatTo(guarded, me, 1800f), -1);
                    }
                    return;
                default:
                    if (_now >= _retargetAt || !Alive(_target, out _, out _))
                    {
                        _retargetAt = _now + Skill.RetargetSeconds * (0.5f + 0.5f * Rand());
                        SwitchTarget(BestTarget(me, mc), -1);
                    }
                    return;
            }
        }

        void DoneWithGoal()
        {
            var g = DefaultGoal;
            if (g.Kind == GoalKind.Guard || g.Kind == GoalKind.Form)
            {
                if (!Alive(g.Target, out _, out _)) g = new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
            }
            else if (g.Kind == GoalKind.Bomb && !Alive(g.Target, out _, out _)) g = new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
            Goal = g;
            _retargetAt = 0f;
            SwitchTarget(GameWorld.NoShip, -1);
            if (_mode != Mode.Peel) SetMode(Mode.Fight);
        }

        static int NearestEngine(ShipCombat c, ShipState from)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < c.Parts.Count; i++)
            {
                var p = c.Parts[i];
                if (p.Destroyed || p.Kind != "engine") continue;
                float d = (p.WorldPosition(c.Ship) - from.Position).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>FS2's find_enemy, roughly: nearest hostile, preferring whoever is attacking us or our capital, spreading pilots across targets.</summary>
        int BestTarget(ShipState me, ShipCombat mc)
        {
            int best = GameWorld.NoShip; float bestScore = float.MaxValue;
            int capped = GameWorld.NoShip;
            float range = _ai.Data.Tuning.SensorRange;
            int player = _ai.World.LocalPlayerShip;
            Consider(_ai.Small);
            Consider(_ai.Big);
            return best != GameWorld.NoShip ? best : capped;

            void Consider(System.Collections.Generic.IReadOnlyList<ShipCombat> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c.Id == Ship || !c.Alive || !Teams.Hostile(me.Team, c.Ship.Team)) continue;
                    float d = Vector3.Distance(c.Ship.Position, me.Position);
                    if (d > range) continue;
                    float score = d;
                    if (AiDirector.IsBig(c))
                    {
                        score = Role == AiRole.Bomber ? d * 0.4f : d * 2.5f + 1500f;
                    }
                    else
                    {
                        if (c.Ship.Target == Ship || (mc.LastAttacker == c.Id && _now - mc.LastAttackerTime < 6f)) score *= 0.5f;
                        var tt = _ai.World.Combat.Get(c.Ship.Target);
                        if (c.SizeClass == CombatEvents.SizeBomber && tt != null && AiDirector.IsBig(tt)) score *= 0.45f;     // bombers on a run come first
                        int others = _ai.AttackersOn(c.Id) - (c.Id == _target ? 1 : 0);
                        if (c.Id == player && _ai.IsHostileSide(me.Team) && others >= Skill.MaxPlayerAttackers) { capped = c.Id; continue; }   // FS2 caps attackers on the player per level
                        else score *= (others + 2) * 0.5f;
                        if (Role == AiRole.Bomber) score *= 3f;
                    }
                    if (c.Id == _target) score *= 0.75f;
                    // A pilot who fears for itself would rather not fight under an enemy capital ship's guns.
                    if (!Skill.Fearless && !AiDirector.IsBig(c) && InEnvelope(me.Team, c.Ship.Position, 1f, out _, out _)) score *= 2.5f;
                    if (score < bestScore) { bestScore = score; best = c.Id; }
                }
            }
        }

        /// <summary>The nearest hostile small ship that is attacking <paramref name="guarded"/> (its target, or the last to hurt it), within reach.</summary>
        int ThreatTo(ShipState guarded, ShipState me, float reach)
        {
            var gc = _ai.World.Combat.Get(guarded.Id);
            int best = GameWorld.NoShip; float bd = float.MaxValue;
            var list = _ai.Small;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.Alive || !Teams.Hostile(me.Team, c.Ship.Team)) continue;
                float d = Vector3.Distance(c.Ship.Position, guarded.Position);
                bool threat = c.Ship.Target == guarded.Id || (gc != null && gc.LastAttacker == c.Id && _now - gc.LastAttackerTime < 8f) || d < 600f;
                if (!threat || d > reach) continue;
                if (c.Id == _ai.World.LocalPlayerShip && c.Id != _target && _ai.IsHostileSide(me.Team) && _ai.AttackersOn(c.Id) >= Skill.MaxPlayerAttackers) continue;
                if (c.Ship.Target == guarded.Id) d *= 0.6f;
                if (d < bd) { bd = d; best = c.Id; }
            }
            return best;
        }

        // ------------------------------------------------------------------------------------------------ acting

        void Act(ShipState me, ShipCombat mc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab, ref Vector3? up, ref float bank)
        {
            // Modes that override whatever the goal wants, while they last.
            switch (_mode)
            {
                case Mode.Recover:
                    goal = _dir; throttle = 0.6f;
                    if (InMode > _modeFor) SetMode(Mode.Fight);
                    return;
                case Mode.MissileEvade:
                    MissileEvade(me, mc, ref input, ref goal, ref throttle, ref ab);
                    return;
                case Mode.Peel:
                    Peel(me, mc, ref input, ref goal, ref throttle, ref ab);
                    return;
                case Mode.Evade:
                    Evade(me, ref goal, ref throttle, ref ab);
                    MaybeDecoy(mc, ref input);
                    // Still a fighter: a target that crosses the nose is shot at.
                    if (Alive(_target, out var et, out var etc) && !AiDirector.IsBig(etc)) Guns(me, mc, et, etc, et.Position, Vector3.Distance(et.Position, me.Position), ref input, true);
                    if (InMode > _modeFor) EndEvade();
                    return;
            }
            MaybeDecoy(mc, ref input);

            if (Goal.Kind == GoalKind.Form) { Form(me, ref goal, ref throttle, ref ab, ref up, ref bank); return; }
            if (Goal.Kind == GoalKind.Bomb && Role == AiRole.Bomber && HasTorpedoes(mc)) { Bomb(me, mc, ref input, ref goal, ref throttle, ref ab); return; }

            if (!Alive(_target, out var t, out var tc))
            {
                if (Goal.Kind == GoalKind.Guard && Alive(Goal.Target, out var g, out _)) { Escort(me, g, ref goal, ref throttle, ref ab, ref up, ref bank); return; }
                Idle(me, ref goal, ref throttle);
                return;
            }
            // A guard keeps to its charge: it lets a threat go if it is dragged too far away.
            if (Goal.Kind == GoalKind.Guard && Alive(Goal.Target, out var gs, out _) && Vector3.Distance(gs.Position, me.Position) > 2500f) { SwitchTarget(GameWorld.NoShip, -1); Escort(me, gs, ref goal, ref throttle, ref ab, ref up, ref bank); return; }

            if (AiDirector.IsBig(tc)) Strafe(me, mc, t, tc, ref input, ref goal, ref throttle, ref ab);
            else Fight(me, mc, t, tc, ref input, ref goal, ref throttle, ref ab);
        }

        void Idle(ShipState me, ref Vector3 goal, ref float throttle)
        {
            // Nothing to do: drift towards the nearest friendly capital at a gentle pace, or fly on.
            ShipCombat home = null; float bd = float.MaxValue;
            foreach (var b in _ai.Big)
                if (b.Ship.Team == me.Team) { float d = (b.Ship.Position - me.Position).sqrMagnitude; if (d < bd) { bd = d; home = b; } }
            if (home != null && bd > 900f * 900f) goal = AiSteer.Dir(me, home.Ship.Position + home.Ship.Rotation * new Vector3(0f, 250f, 0f));
            throttle = 0.6f;
        }

        // ------------------------------------------------------------------------------------------------ dogfighting

        void Fight(ShipState me, ShipCombat mc, ShipState t, ShipCombat tc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            Vector3 aimAt = _targetPart >= 0 && _targetPart < tc.Parts.Count && !tc.Parts[_targetPart].Destroyed ? tc.Parts[_targetPart].WorldPosition(t) : t.Position;
            Vector3 to = aimAt - me.Position;
            float dist = to.magnitude;
            Vector3 u = dist > 0.1f ? to / dist : me.Forward;
            float dotTo = Vector3.Dot(me.Forward, u);
            float dotFrom = Vector3.Dot(t.Forward, -u);

            if (_mode == Mode.Extend)
            {
                goal = _dir; throttle = 1f; ab = true;
                if (dist > _extendDist || InMode > 7f) SetMode(Mode.Fight);
                Guns(me, mc, t, tc, aimAt, dist, ref input, false);
                return;
            }
            SetMode(Mode.Fight);

            var seen = View(t);
            // Aim: with lead once the target has been ours a moment, is roughly ahead and in gun range (FS2 aims at the raw position otherwise:
            // leading a turning target from far off overshoots its turn and hands it your tail).
            Vector3 aimDir = u;
            _aimErr = Vector3.zero;
            float speed = GunSpeed(mc);
            var gunDef = WeaponCatalog.Get(mc.PrimaryOnline ? mc.PrimaryGun : mc.HeavyGun);
            float reach = (gunDef != null ? gunDef.Range : 800f) * Mathf.Lerp(1.15f, 0.85f, Skill.Patience);   // an impatient pilot leads from further out
            if (speed > 1f && dotTo > 0.25f && _now - _targetAt > 0.5f && dist < reach)
            {
                Vector3 err = AimError(dist, t, tc);
                _aimErr = err;
                Vector3 seenAt = aimAt + (seen.Position - t.Position);
                if (AiAim.Lead(me.Position, me.Velocity, seen, seenAt + err, speed, Skill.Accuracy, out var d, out _, out _)) aimDir = d;
                // The rate the lead turns at (without the error, which jumps), smoothed: the nose is flown ahead of it, not after it.
                if (AiAim.Lead(me.Position, me.Velocity, seen, seenAt, speed, Skill.Accuracy, out var trueLead, out _, out _)) TrackLead(t.Id, trueLead);
            }
            goal = aimDir;

            // Speed: run in fast, match speed close behind, slow a little in a tight turning fight.
            if (dist > 1100f) { throttle = 1f; ab = dist > 1500f; }
            else if (dotTo > 0.8f && Vector3.Dot(t.Forward, u) > 0.3f && dist < 400f)
            {
                float want = Mathf.Lerp(150f, 260f, Skill.Patience);
                throttle = Mathf.Max(0.3f, AiSteer.ThrottleFor(me, t.ForwardSpeed + (dist - want) * 0.6f));    // a fighter never sits still behind a slow target
            }
            else if (dotTo < 0.5f && dist < 700f) throttle = 0.55f;          // slower turns tighter (the turn rate is the same at any speed)
            else throttle = 1f;
            // Closing on a target from behind too fast overshoots it; a patient pilot closes slower.
            if (dist < 700f && dotTo > 0.6f && Vector3.Dot(t.Forward, u) > 0f)
            {
                float closingNow = Vector3.Dot(me.Velocity - t.Velocity, u);
                float maxClose = Mathf.Lerp(45f, 15f, Skill.Patience) + Mathf.Max(0f, dist - 250f) * 0.12f;
                if (closingNow > maxClose) throttle = Mathf.Min(throttle, Mathf.Max(0.3f, AiSteer.ThrottleFor(me, Mathf.Max(0f, t.ForwardSpeed) + maxClose * 0.6f)));
            }

            // Head-on and about to meet: keep shooting but pass to one side, then turn after it (FS2 avoids the collision and fights on).
            float closing = Vector3.Dot(me.Velocity - t.Velocity, u);
            if (dotFrom > 0.6f && dotTo > 0.5f && dist < 80f + closing * 2.5f)
            {
                Vector3 side = Vector3.Cross(u, me.Rotation * Vector3.up);
                if (side.sqrMagnitude < 1e-3f) side = me.Rotation * Vector3.right;
                if ((Ship & 1) == 1) side = -side;
                goal = (aimDir + side.normalized * Mathf.Clamp01(1f - dist / (80f + closing * 2.5f)) * 0.6f).normalized;
            }
            // Going nowhere: no hits for a while, close and not lined up (FS2's get-away), or a long turning fight.
            if (_now - _lastMyHit > 6f && _now - _targetAt > 6f && dist < 500f && dotTo < 0.2f && Rand() < 0.02f * (1f + Skill.Level) * (1f - 0.7f * Skill.Patience))
            {
                StartExtend(me, -u, 450f + 300f * Rand());
                return;
            }

            // Do not follow it into the flak (unless fearless or ordered): break away from the capital ship and pick another.
            if (!Skill.Fearless && (Goal.Kind == GoalKind.Engage || Goal.Kind == GoalKind.Guard)
                && InEnvelope(me.Team, me.Position, 0.8f, out var cap, out _) && InEnvelope(me.Team, t.Position, 1f, out _, out _))
            {
                SwitchTarget(GameWorld.NoShip, -1);
                _retargetAt = _now + 1.5f;
                StartExtend(me, (cap.Ship.Position - me.Position).normalized, 700f);
                _dir = (me.Position - cap.Ship.Position).normalized;
                return;
            }

            Guns(me, mc, t, tc, aimAt, dist, ref input, true);
            Missiles(me, mc, t, tc, dist, dotTo, ref input);
        }

        /// <summary>A point within an enemy capital ship's gun envelope (its turrets not all silenced), <paramref name="scale"/> x the envelope from its hull.</summary>
        bool InEnvelope(int myTeam, Vector3 p, float scale, out ShipCombat cap, out float hull)
        {
            cap = null; hull = float.MaxValue;
            float env = _ai.Data.Tuning.FlakEnvelope * scale;
            foreach (var b in _ai.Big)
            {
                if (!Teams.Hostile(myTeam, b.Ship.Team) || (b.Flags & CombatFlags.TurretsSilenced) != 0) continue;
                if ((b.Ship.Position - p).sqrMagnitude > (b.Shape.BoundRadius + env) * (b.Shape.BoundRadius + env)) continue;
                float d = ShipHits.DistanceToHull(b.Ship, b, p, out _);
                if (d < env && d < hull) { hull = d; cap = b; }
            }
            return cap != null;
        }

        /// <summary>
        /// The target as this pilot sees it: on the harder levels the truth, on the easier ones a picture refreshed only every
        /// <see cref="Skill.PredictDelay"/> seconds and carried forward in a straight line (FS2's ai_set_positions), so a weaving target is
        /// where it was going, not where it went.
        /// </summary>
        ShipState View(ShipState t)
        {
            if (Skill.PredictDelay <= 0.01f) return t;
            if (t.Id != _viewOf || _now - _viewAt >= Skill.PredictDelay * (0.8f + 0.4f * Rand()))
            {
                _viewOf = t.Id; _viewAt = _now;
                _view.Id = t.Id; _view.Rotation = t.Rotation; _view.Velocity = t.Velocity; _view.AngularVelocity = t.AngularVelocity;
                _view.Position = t.Position; _view.ForwardSpeed = t.ForwardSpeed; _view.Radius = t.Radius;
                _viewBase = t.Position;
            }
            _view.Position = _viewBase + _view.Velocity * (_now - _viewAt);
            return _view;
        }
        Vector3 _viewBase;

        void TrackLead(int target, Vector3 dir)
        {
            float dt = Sim.Dt;
            if (_leadValid && _leadFor == target)
            {
                Vector3 w = Vector3.Cross(_leadPrev, dir) / dt;           // small-angle rotation rate of the direction
                if (w.magnitude > 6f) w = w.normalized * 6f;
                _leadRate = Vector3.Lerp(_leadRate, w, 0.25f);
            }
            else _leadRate = Vector3.zero;
            _leadPrev = dir; _leadFor = target; _leadValid = true; _leadUsed = true;
        }

        void StartExtend(ShipState me, Vector3 awayFrom, float dist)
        {
            // Out past the target and away, angled off so the turn back is not a reversal into its guns.
            Vector3 side = Vector3.Cross(awayFrom, me.Rotation * Vector3.up);
            if (side.sqrMagnitude < 1e-3f) side = me.Rotation * Vector3.right;
            _dir = (me.Forward + side.normalized * (Rand() < 0.5f ? 0.6f : -0.6f) + RandUnit() * 0.2f).normalized;
            _extendDist = dist;
            SetMode(Mode.Extend);
        }

        float GunSpeed(ShipCombat mc) => mc.PrimaryOnline ? mc.PrimarySpeed : mc.HeavyOnline ? mc.HeavySpeed : 0f;

        /// <summary>
        /// FS2's aim error: a random offset that changes about once a second, (1 - accuracy) x the level's aiming error, up to five times
        /// bigger while the target has only just come into range.
        /// </summary>
        Vector3 AimError(float dist, ShipState t, ShipCombat tc)
        {
            if (_now >= _errUntil) { _errVec = RandUnit() * Mathf.Sqrt(Rand()); _errUntil = _now + 0.7f + 0.6f * Rand(); }
            if (_inRangeSince < 0f) _inRangeSince = _now;
            float settle = Mathf.Max(0.25f, 2f + Skill.InRangeTime);
            float fresh = 1f + 4f * Mathf.Clamp01(1f - (_now - _inRangeSince) / settle);
            float metres = (1f - Skill.Accuracy) * _ai.Data.Tuning.AimErrorMetres * Skill.AimingError * fresh * Mathf.Clamp(dist / 400f, 0.4f, 2.5f);
            return _errVec * metres;
        }

        void Guns(ShipState me, ShipCombat mc, ShipState t, ShipCombat tc, Vector3 aimAt, float dist, ref ControlInput input, bool allowed)
        {
            if (!allowed || !Teams.Hostile(me.Team, t.Team)) { _holdSince = -1f; return; }
            float speed = GunSpeed(mc);
            if (speed <= 1f) return;
            var gun = WeaponCatalog.Get(mc.PrimaryOnline ? mc.PrimaryGun : mc.HeavyGun);
            if (gun == null) return;
            // The pilot fires on its own solution, error and all, from its own (perhaps stale) picture of the target (FS2's AI fires at its
            // predicted position).
            bool big = AiDirector.IsBig(tc);
            Vector3 err = big ? Vector3.zero : _aimErr;
            var seen = big ? t : View(t);
            if (!AiAim.Lead(me.Position, me.Velocity, seen, aimAt + (seen.Position - t.Position) + err, speed, Skill.Accuracy, out var dir, out float tof, out _)) { _holdSince = -1f; return; }
            bool inRange = tof < gun.Life * _ai.Data.Tuning.GunRangeFraction;
            if (!inRange) _inRangeSince = -1f;
            // FS2's firing cone (0.90 + accuracy/10 - radius/distance), narrowed towards the target's own size by patience.
            float radius = AiDirector.IsBig(tc) ? 25f : Mathf.Max(3f, tc.Shape.HasBubble ? tc.Shape.ShieldRadii.magnitude * 0.5f : t.Radius * 0.6f);
            float fs2 = Mathf.Max(0.5f, 0.90f + Skill.Accuracy * 0.1f - radius / Mathf.Max(1f, dist));
            float tight = Mathf.Cos(Mathf.Atan2(radius + 1.5f, Mathf.Max(1f, dist)));
            float cone = Mathf.Lerp(fs2, Mathf.Max(fs2, tight), Skill.Patience * _ai.Data.Tuning.PatienceConeBlend);
            bool lined = Vector3.Dot(me.Forward, dir) >= cone;
            if (lined && inRange) { if (_holdSince < 0f) _holdSince = _now; }
            else { _holdSince = -1f; return; }
            if (_now - _holdSince < Skill.Patience * _ai.Data.Tuning.PatienceHoldSeconds) return;

            // Fire at the gun's rate times the level's fire delay (FS2 slows AI guns on easy levels), never more often than the gun allows.
            if (mc.PrimaryOnline && _now >= _nextGunAt && _now >= mc.PrimaryReadyAt - 1e-3f)
            {
                var pg = WeaponCatalog.Get(mc.PrimaryGun);
                if (pg != null && mc.Energy >= pg.Energy)
                {
                    input.Buttons |= ControlButtons.PrimaryFire;
                    _nextGunAt = _now + pg.FireWait * Skill.FireDelay;
                }
            }
            bool heavyNow = tc.Shield01 < 0.25f || AiDirector.IsBig(tc) || Goal.Kind == GoalKind.Disable || _targetPart >= 0;
            if (mc.HeavyOnline && heavyNow && _now >= _nextHeavyAt && _now >= mc.HeavyReadyAt - 1e-3f)
            {
                var hg = WeaponCatalog.Get(mc.HeavyGun);
                if (hg != null && mc.Energy >= hg.Energy && mc.Energy > mc.EnergyMax * 0.2f)
                {
                    input.Buttons |= ControlButtons.SecondaryFire;
                    _nextHeavyAt = _now + hg.FireWait * Skill.FireDelay;
                }
            }
        }

        int SlotOf(ShipCombat mc, WeaponKind kind)
        {
            for (int i = 0; i < mc.Missiles.Count; i++)
            {
                var w = WeaponCatalog.Get(mc.Missiles[i].Weapon);
                if (w != null && w.Kind == kind && mc.Missiles[i].Ammo > 0) return i;
            }
            return -1;
        }

        bool HasTorpedoes(ShipCombat mc) => SlotOf(mc, WeaponKind.Torpedo) >= 0 && (mc.Flags & CombatFlags.LaunchersOffline) == 0;

        void Missiles(ShipState me, ShipCombat mc, ShipState t, ShipCombat tc, float dist, float dotTo, ref ControlInput input)
        {
            if ((mc.Flags & CombatFlags.LaunchersOffline) != 0 || mc.Missiles.Count == 0) return;
            if (_missileFor != t.Id || _missileSlot < 0 || _missileSlot >= mc.Missiles.Count || mc.Missiles[_missileSlot].Ammo <= 0)
            {
                _missileFor = t.Id;
                _missileSlot = SlotOf(mc, WeaponKind.LockMissile);
                if (_missileSlot < 0) _missileSlot = SlotOf(mc, WeaponKind.HeatMissile);
            }
            if (_missileSlot < 0) return;
            input.SelectMissilePlusOne = _missileSlot + 1;
            if (_now < _nextMissileAt) return;
            var slot = mc.Missiles[_missileSlot];
            var def = WeaponCatalog.Get(slot.Weapon);
            if (def == null || _now < slot.ReadyAt) return;
            // Do not swamp the player (FS2 caps the homers on the player per level).
            if (t.Id == _ai.World.LocalPlayerShip && _ai.IsHostileSide(me.Team) && _ai.HomersOn(t.Id) >= Skill.MaxPlayerHomers) return;
            bool fire;
            if (def.NeedsLock) fire = mc.Lock == LockPhase.Locked && mc.LockTarget == t.Id && dist < def.Range * 0.75f && dist > 150f;
            else fire = mc.Lock == LockPhase.Locked && mc.LockTarget == t.Id && dist < 900f && dist > 120f && dotTo > Mathf.Lerp(0.8f, 0.95f, Skill.Patience);
            if (!fire) return;
            input.Buttons |= ControlButtons.MissileFire;
            // FS2's secondary reload: the weapon's wait scaled by the level, varied, and short waits often doubled.
            float wait = def.FireWait * Skill.FireDelay * (0.8f + 0.4f * Rand());
            if (wait < 5f && Rand() < 0.5f) wait = wait * 2f + 2f;
            _nextMissileAt = _now + wait;
        }

        void MaybeDecoy(ShipCombat mc, ref ControlInput input)
        {
            if (mc.IncomingMissiles == 0 || mc.Decoys <= 0 || mc.IncomingEta > 2f || _now < _decoyRollAt) return;
            _decoyRollAt = _now + 1f;
            if (Rand() < Skill.CmeasureChance * (0.3f + 0.7f * Skill.Evasion)) input.Buttons |= ControlButtons.Countermeasure;
        }

        // ------------------------------------------------------------------------------------------------ evading

        void Evade(ShipState me, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            // FS2's evade: a hard break turn towards the attacker's side (it must turn harder to stay with you, or overshoot), slowed to tighten
            // it, with jinks up and down that spoil its lead (sooner and harder for a better evader). Then the pilot turns on the attacker.
            var attacker = _ai.World.Ships.Get(_evadeFrom);
            Vector3 at = attacker != null ? attacker.Position : _point;
            Vector3 lateral = Vector3.ProjectOnPlane(at - me.Position, me.Forward);
            if (lateral.sqrMagnitude < 25f) lateral = me.Rotation * Vector3.right * _breakSide;
            if (_now >= _jinkNext)
            {
                _jinkNext = _now + Mathf.Lerp(0.8f, 0.35f, Skill.Evasion) * (0.7f + 0.6f * Rand());
                _jinkFlip = (Rand() < 0.5f ? -1f : 1f) * (0.3f + 0.7f * Skill.Evasion);
            }
            Vector3 lift = Vector3.Cross(me.Forward, lateral.normalized);
            goal = (me.Forward * 0.2f + lateral.normalized + lift * _jinkFlip * 0.7f).normalized;
            throttle = 0.5f; ab = false;
        }

        /// <summary>The pilot is in the middle of killing its target (its shield is broken, or the pilot has been hitting it): it does not turn away to a new one.</summary>
        bool Committed()
        {
            var cur = _ai.World.Combat.Get(_target);
            return cur != null && cur.Alive && !AiDirector.IsBig(cur) && (cur.Shield01 < 0.5f || _now - _lastMyHit < 3f);
        }

        void EndEvade()
        {
            _evadeAgainAt = _now + 3f + 3f * (1f - Skill.Evasion) + 2f * Rand();
            // A target already being worked on is finished first.
            if (Committed()) { SetMode(Mode.Fight); return; }
            // Turn on whoever forced the break (FS2's dynamic chase), unless on an order.
            bool playerFull = _evadeFrom == _ai.World.LocalPlayerShip && _ai.IsHostileSide(_ai.World.Ships.Get(Ship)?.Team ?? 0) && _ai.AttackersOn(_evadeFrom) >= Skill.MaxPlayerAttackers;
            if (!playerFull && (Goal.Kind == GoalKind.Engage || Goal.Kind == GoalKind.Guard) && Alive(_evadeFrom, out var a, out var ac) && !AiDirector.IsBig(ac) && _ai.Hostile(Ship, a.Id))
                SwitchTarget(a.Id, -1);
            SetMode(Mode.Fight);
        }

        void MissileEvade(ShipState me, ShipCombat mc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            MaybeDecoy(mc, ref input);
            if (mc.IncomingMissiles == 0 || InMode > _modeFor) { SetMode(Mode.Fight); goal = me.Forward; return; }
            // Turn hard across the missile's path, the way that is already nearest, and burn (a heat-seeker loses you outside its cone).
            Vector3 m = mc.IncomingDirection.sqrMagnitude > 0.5f ? mc.IncomingDirection : -me.Forward;
            Vector3 across = Vector3.ProjectOnPlane(me.Forward, m);
            if (across.sqrMagnitude < 0.05f) across = Vector3.ProjectOnPlane(me.Rotation * Vector3.up, m);
            goal = (across.normalized - m * 0.25f).normalized;
            throttle = 1f; ab = true;
        }

        void Peel(ShipState me, ShipCombat mc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            MaybeDecoy(mc, ref input);
            var tuning = _ai.Data.Tuning;
            bool mended = mc.Hull01 >= Mathf.Min(0.9f, PeelHull + 0.2f) && mc.Shield01 >= 0.5f;
            if (mended || InMode > tuning.PeelMaxSeconds)
            {
                if (InPlayerWing) _ai.Radio.Say("rejoin", Ship);
                _peelAgainAt = _now + 20f; _hullAtRejoin = mc.Hull01;
                SetMode(Mode.Fight);
                _retargetAt = 0f;
                return;
            }
            // Home: our nearest capital ship, above it; else straight away from the nearest threat.
            ShipCombat home = null; float bd = float.MaxValue;
            foreach (var b in _ai.Big)
                if (b.Ship.Team == me.Team) { float d = (b.Ship.Position - me.Position).sqrMagnitude; if (d < bd) { bd = d; home = b; } }
            Vector3 dest;
            if (home != null) dest = home.Ship.Position + home.Ship.Rotation * new Vector3(0f, home.Shape.BoundRadius * 0.6f + 200f, 0f);
            else
            {
                var a = _ai.World.Ships.Get(mc.LastAttacker);
                dest = a != null ? me.Position + (me.Position - a.Position).normalized * 2000f : me.Position + me.Forward * 2000f;
            }
            float dd = Vector3.Distance(dest, me.Position);
            goal = AiSteer.Dir(me, dest);
            throttle = dd > 400f ? 1f : 0.45f;
            ab = dd > 800f;
            // Still being shot at or chased: jink all the way home (a straight run home is the easiest shot there is).
            bool chased = false;
            foreach (var o in _ai.Small)
                if (o.Alive && o.Ship.Target == Ship && Teams.Hostile(me.Team, o.Ship.Team) && AiStats.OnTail(o.Ship, me, 800f, 0.9f, 0.5f)) { chased = true; _point = o.Ship.Position; break; }
            if (chased || _now - mc.LastDamagedTime < 3f)
            {
                if (_now >= _jinkNext)
                {
                    _jinkNext = _now + Mathf.Lerp(0.8f, 0.35f, Skill.Evasion) * (0.7f + 0.6f * Rand());
                    Vector3 axis = me.Rotation * new Vector3(Rand() * 2f - 1f, Rand() * 2f - 1f, 0f);
                    if (axis.sqrMagnitude < 0.01f) axis = me.Rotation * Vector3.right;
                    _jinkDir = Quaternion.AngleAxis(35f + 35f * Rand(), axis.normalized) * goal;
                }
                goal = (goal * 0.35f + _jinkDir * 0.65f).normalized;
                ab = me.FuelMax > 0f;
            }
            if (dd < 400f) goal = (goal + me.Forward * 2f).normalized;   // loiter round home
        }

        // ------------------------------------------------------------------------------------------------ capital ships

        void Strafe(ShipState me, ShipCombat mc, ShipState t, ShipCombat tc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            // FS2's attack run on a big ship: in fast at a part (or the nearest hull), guns at range, break off before the hull, out, round again.
            Vector3 aimAt;
            if (_targetPart >= 0 && _targetPart < tc.Parts.Count && !tc.Parts[_targetPart].Destroyed) aimAt = tc.Parts[_targetPart].WorldPosition(t);
            else
            {
                ShipHits.DistanceToHull(t, tc, me.Position, out var near);
                aimAt = near + (t.Position - near).normalized * 4f;
            }
            float hull = ShipHits.DistanceToHull(t, tc, me.Position, out var nearest);
            if (_mode != Mode.StrafeRun && _mode != Mode.StrafeOut) SetMode(Mode.StrafeRun);
            if (_mode == Mode.StrafeOut)
            {
                goal = AiSteer.Dir(me, _point); throttle = 1f; ab = true;
                if (Vector3.Distance(me.Position, _point) < 200f || hull > 900f || InMode > 8f) SetMode(Mode.StrafeRun);
                return;
            }
            Vector3 u = AiSteer.Dir(me, aimAt);
            goal = u;
            throttle = 1f; ab = hull > 1600f;
            // A part can only be hit from its own side of the hull: come round to it first (out along its face), then dive at it.
            if (_targetPart >= 0 && _targetPart < tc.Parts.Count && !tc.Parts[_targetPart].Destroyed)
            {
                var part = tc.Parts[_targetPart];
                Vector3 normal = t.Rotation * TurretGunnery.OutwardNormal(tc, part.LocalPosition);
                Vector3 fromPart = me.Position - aimAt;
                if (Vector3.Dot(fromPart.normalized, normal) < 0.55f)
                {
                    Vector3 setup = aimAt + normal * 800f + Vector3.ProjectOnPlane(fromPart, normal).normalized * 300f + t.Velocity * 4f;
                    goal = AiSteer.Dir(me, setup);
                    ab = Vector3.Distance(me.Position, setup) > 900f;
                    return;
                }
            }
            float breakAt = 200f + Mathf.Max(0f, me.ForwardSpeed) * 1.6f;
            if (hull < breakAt)
            {
                Vector3 outward = (me.Position - nearest).sqrMagnitude > 1f ? (me.Position - nearest).normalized : (me.Position - t.Position).normalized;
                Vector3 lateral = Vector3.Cross(outward, t.Forward);
                if (lateral.sqrMagnitude < 1e-3f) lateral = Vector3.Cross(outward, Vector3.up);
                _point = nearest + outward * 750f + lateral.normalized * (Rand() < 0.5f ? 300f : -300f) + t.Velocity * 6f;
                SetMode(Mode.StrafeOut);
                return;
            }
            if (hull < 1000f) Guns(me, mc, t, tc, aimAt, Vector3.Distance(me.Position, aimAt), ref input, true);
        }

        void Bomb(ShipState me, ShipCombat mc, ref ControlInput input, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            if (!Alive(Goal.Target, out var cap, out var cc)) { DoneWithGoal(); return; }
            int slot = SlotOf(mc, WeaponKind.Torpedo);
            if (slot >= 0) input.SelectMissilePlusOne = slot + 1;
            SwitchTarget(cap.Id, Goal.Part >= 0 && Goal.Part < cc.Parts.Count && !cc.Parts[Goal.Part].Destroyed ? Goal.Part : -1);
            float hull = ShipHits.DistanceToHull(cap, cc, me.Position, out _);
            float centre = Vector3.Distance(cap.Position, me.Position);
            if (_mode != Mode.BombApproach && _mode != Mode.BombLine && _mode != Mode.BombPeel)
            {
                Vector3 side = Vector3.ProjectOnPlane(me.Position - cap.Position, cap.Rotation * Vector3.up);
                if (side.sqrMagnitude < 1f) side = cap.Rotation * Vector3.right;
                _point = cap.Position + side.normalized * 1500f + cap.Rotation * new Vector3(0f, 150f, 0f);
                SetMode(Mode.BombApproach);
            }
            var def = slot >= 0 ? WeaponCatalog.Get(mc.Missiles[slot].Weapon) : null;
            switch (_mode)
            {
                case Mode.BombApproach:
                    goal = AiSteer.Dir(me, _point + cap.Velocity * 5f);
                    throttle = 1f; ab = hull > 2500f;
                    if (hull < 1600f || Vector3.Distance(me.Position, _point) < 300f)
                    {
                        _torpsAtLine = mc.Missiles[slot].Ammo;
                        SetMode(Mode.BombLine);
                    }
                    return;
                case Mode.BombLine:
                {
                    Vector3 aimAt = _targetPart >= 0 ? cc.Parts[_targetPart].WorldPosition(cap) : cap.Position;
                    goal = AiSteer.Dir(me, aimAt + cap.Velocity * 3f);
                    throttle = 0.7f;
                    bool fired = mc.Missiles[slot].Ammo < _torpsAtLine;
                    if (!fired && def != null && mc.Lock == LockPhase.Locked && mc.LockTarget == cap.Id && centre < def.Range * 0.72f && _now >= mc.Missiles[slot].ReadyAt && _now >= _nextMissileAt)
                    {
                        input.Buttons |= ControlButtons.MissileFire;
                        _nextMissileAt = _now + def.FireWait * Skill.FireDelay * (0.9f + 0.2f * Rand());
                    }
                    if (fired || hull < 450f || InMode > 20f)
                    {
                        Vector3 away = Vector3.Cross(AiSteer.Dir(me, cap.Position), cap.Rotation * Vector3.up);
                        if (away.sqrMagnitude < 1e-3f) away = me.Rotation * Vector3.right;
                        _point = me.Position + (away.normalized * (Rand() < 0.5f ? 1f : -1f) + (me.Position - cap.Position).normalized * 0.6f + cap.Rotation * Vector3.up * 0.3f).normalized * 2200f;
                        SetMode(Mode.BombPeel);
                    }
                    return;
                }
                case Mode.BombPeel:
                    goal = AiSteer.Dir(me, _point); throttle = 1f; ab = true;
                    if (centre > 2300f || InMode > 14f)
                    {
                        Vector3 side = Vector3.ProjectOnPlane(me.Position - cap.Position, cap.Rotation * Vector3.up);
                        if (side.sqrMagnitude < 1f) side = cap.Rotation * Vector3.right;
                        _point = cap.Position + side.normalized * 1500f + cap.Rotation * new Vector3(0f, 150f * (Rand() < 0.5f ? 1f : -1f), 0f);
                        SetMode(Mode.BombApproach);
                    }
                    return;
            }
        }

        // ------------------------------------------------------------------------------------------------ formation and escort

        static readonly Vector3[] Slots =
        {
            new Vector3(-32f, 5f, -30f), new Vector3(32f, -5f, -30f), new Vector3(-64f, 10f, -60f), new Vector3(64f, -10f, -60f),
            new Vector3(0f, 18f, -60f), new Vector3(0f, -18f, -90f),
        };

        void Form(ShipState me, ref Vector3 goal, ref float throttle, ref bool ab, ref Vector3? up, ref float bank)
        {
            if (!Alive(Goal.Target, out var leader, out _)) { DoneWithGoal(); return; }
            SetMode(Mode.Form);
            int slot = Mathf.Clamp(WingSlot, 0, Slots.Length - 1);
            float spread = 1f + Mathf.Max(0f, leader.ForwardSpeed) / 150f;
            Vector3 place = leader.Position + leader.Rotation * (Slots[slot] * spread);
            Vector3 toSlot = place - me.Position;
            float dist = toSlot.magnitude;
            if (dist > 400f)
            {
                float lead = dist / Mathf.Max(20f, me.TopSpeed);
                goal = AiSteer.Dir(me, place + leader.Velocity * Mathf.Min(lead, 8f));
                throttle = 1f; ab = dist > 900f;
                return;
            }
            // Close in: chase a point ahead of the slot along the leader's heading, and hold the gap with the throttle.
            Vector3 carrot = place + leader.Forward * (60f + Mathf.Max(0f, leader.ForwardSpeed) * 0.8f);
            goal = AiSteer.Dir(me, carrot);
            float along = Vector3.Dot(toSlot, leader.Forward);
            throttle = AiSteer.ThrottleFor(me, Mathf.Max(0f, leader.ForwardSpeed) + Mathf.Clamp(along * 0.6f, -25f, 40f));
            if (dist < 150f) { up = leader.Rotation * Vector3.up; bank = 0.2f; }
        }

        void Escort(ShipState me, ShipState charge, ref Vector3 goal, ref float throttle, ref bool ab, ref Vector3? up, ref float bank)
        {
            // Nothing to fight: hold a loose place by the ship being guarded (formation spacing, wider for a capital).
            SetMode(Mode.Escort);
            var cc = _ai.World.Combat.Get(charge.Id);
            bool big = cc != null && AiDirector.IsBig(cc);
            int slot = Mathf.Clamp(WingSlot >= 0 ? WingSlot : (Ship % Slots.Length), 0, Slots.Length - 1);
            Vector3 offset = big ? new Vector3((slot % 2 == 0 ? -1f : 1f) * (cc.Shape.BoundRadius * 0.6f + 150f), cc.Shape.BoundRadius * 0.5f + 80f, 0f) : Slots[slot] * 2.2f;
            Vector3 place = charge.Position + charge.Rotation * offset;
            float dist = Vector3.Distance(place, me.Position);
            if (dist > 300f)
            {
                goal = AiSteer.Dir(me, place + charge.Velocity * 2f);
                throttle = 1f; ab = dist > 1200f;
                return;
            }
            Vector3 carrot = place + charge.Forward * (80f + Mathf.Max(0f, charge.ForwardSpeed));
            goal = AiSteer.Dir(me, carrot);
            float along = Vector3.Dot(place - me.Position, charge.Forward);
            throttle = Mathf.Max(0.3f, AiSteer.ThrottleFor(me, Mathf.Max(big ? 25f : 0f, charge.ForwardSpeed) + Mathf.Clamp(along * 0.5f, -20f, 35f)));
        }

        // ------------------------------------------------------------------------------------------------ keeping clear

        /// <summary>
        /// Keep off capital hulls (FS2 predicts collisions a few seconds ahead): the path for the next seconds against every big ship's boxes,
        /// grown by a margin; on a hit the pilot turns along the hull face and out from it, slowing if it is pointed at it. Then other ships:
        /// one passing too close in the next second and a half pushes the goal aside.
        /// </summary>
        void Avoid(ShipState me, ref Vector3 goal, ref float throttle, ref bool ab)
        {
            var tuning = _ai.Data.Tuning;
            float speed = Mathf.Max(me.Velocity.magnitude, 5f);
            float look = Mathf.Clamp(tuning.AvoidLookAheadMin + speed / 50f, tuning.AvoidLookAheadMin, tuning.AvoidLookAheadMax);
            float margin = me.Radius + tuning.AvoidMargin;
            float bestUrg = 0f; Vector3 bestN = Vector3.zero;
            var big = _ai.Big;
            for (int i = 0; i < big.Count; i++)
            {
                var c = big[i];
                if (c.Id == Ship) continue;
                var s = c.Ship;
                float reach = Mathf.Max(c.Shape.BoundRadius, s.Radius) + speed * look + margin + 50f;
                if ((s.Position - me.Position).sqrMagnitude > reach * reach) continue;
                var inv = Quaternion.Inverse(s.Rotation);
                Vector3 a = inv * (me.Position - s.Position);
                Vector3 relVel = inv * (me.Velocity - s.Velocity);
                Vector3 relGoal = inv * (goal.normalized * speed - s.Velocity);
                foreach (var box in c.Shape.Boxes)
                {
                    Vector3 half = box.Half + Vector3.one * margin;
                    float urg; Vector3 n;
                    if (Inside(a, box.Centre, half, out n)) urg = 1f;
                    else
                    {
                        // The path it is on, and the path it wants: steer if either runs into the hull.
                        urg = 0f; n = Vector3.zero;
                        if (Geo.SegmentBox(a, a + relVel * look, box.Centre, half, out float t1, out var n1)) { urg = 1f - t1; n = n1; }
                        if (Geo.SegmentBox(a, a + relGoal * look, box.Centre, half, out float t2, out var n2) && 1f - t2 > urg) { urg = (1f - t2) * 0.9f; n = n2; }
                    }
                    if (urg > bestUrg) { bestUrg = urg; bestN = s.Rotation * n; }
                }
            }
            if (bestUrg > 0f)
            {
                Vector3 tangent = Vector3.ProjectOnPlane(goal, bestN);
                if (tangent.sqrMagnitude < 0.04f) tangent = Vector3.ProjectOnPlane(me.Forward, bestN);
                if (tangent.sqrMagnitude < 0.04f) tangent = Vector3.Cross(bestN, me.Rotation * Vector3.right);
                Vector3 dir = (tangent.normalized * (1.1f - bestUrg) + bestN * (0.5f + 1.6f * bestUrg)).normalized;
                goal = dir;
                if (bestUrg > 0.45f && Vector3.Dot(me.Forward, -bestN) > 0.3f) { throttle = Mathf.Min(throttle, 0.35f); ab = false; }
                _avoidDir = dir; _avoidUntil = _now + 0.4f;
            }
            else if (_now < _avoidUntil) goal = (goal.normalized + _avoidDir * 1.5f).normalized;

            // Other small ships: one about to pass too close pushes the goal aside (not the formation leader's slot, which is kept by spacing).
            var small = _ai.Small;
            for (int i = 0; i < small.Count; i++)
            {
                var o = small[i];
                if (o.Id == Ship) continue;
                Vector3 r = o.Ship.Position - me.Position;
                if (r.sqrMagnitude > 250f * 250f) continue;
                Vector3 v = o.Ship.Velocity - me.Velocity;
                float vv = v.sqrMagnitude;
                if (vv < 1f) continue;
                float tc = -Vector3.Dot(r, v) / vv;
                if (tc < 0f || tc > 1.5f) continue;
                Vector3 miss = r + v * tc;
                float need = me.Radius + o.Ship.Radius + 10f;
                if (miss.magnitude > need) continue;
                Vector3 away = miss.sqrMagnitude > 0.01f ? -miss.normalized : Vector3.Cross(r.normalized, me.Rotation * Vector3.up).normalized;
                goal = (goal.normalized * 0.3f + away * 0.7f * (1f - tc / 1.5f) + me.Forward * 0.2f).normalized;
                break;
            }
        }

        static bool Inside(Vector3 p, Vector3 centre, Vector3 half, out Vector3 normal)
        {
            Vector3 q = p - centre;
            normal = Vector3.zero;
            if (Mathf.Abs(q.x) > half.x || Mathf.Abs(q.y) > half.y || Mathf.Abs(q.z) > half.z) return false;
            float dx = half.x - Mathf.Abs(q.x), dy = half.y - Mathf.Abs(q.y), dz = half.z - Mathf.Abs(q.z);
            if (dx <= dy && dx <= dz) normal = new Vector3(Mathf.Sign(q.x), 0f, 0f);
            else if (dy <= dz) normal = new Vector3(0f, Mathf.Sign(q.y), 0f);
            else normal = new Vector3(0f, 0f, Mathf.Sign(q.z));
            return true;
        }
    }
}
