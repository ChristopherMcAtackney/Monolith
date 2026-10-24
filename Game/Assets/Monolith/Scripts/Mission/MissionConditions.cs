using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>What a fact about a ship or a wing is (the "with delay" conditions).</summary>
    public enum Fact { Arrived, Destroyed, Departed, Disabled, Disarmed }

    /// <summary>
    /// A condition tree (FS2's SEXP conditions, as JSON): <c>true</c>, <c>false</c>, or an object with one operator key and its modifiers,
    /// e.g. <c>{ "destroyed": "Warden", "delay": 6 }</c> or <c>{ "and": [ ... ] }</c>. Evaluated every tick by the runtime; nothing here changes
    /// the world. Every operator is listed in <see cref="Ops"/> and documented in Scripts/Mission/README.md.
    /// </summary>
    public abstract class Cond
    {
        public static readonly Cond Always = new Literal(true), Never = new Literal(false);

        public static readonly string[] Ops =
        {
            "and", "or", "not", "time", "arrived", "destroyed", "departed", "disabled", "disarmed", "destroyedPercent", "hull", "subsystem",
            "range", "hostilesNear", "teamAlive", "event", "objective", "beaming", "autopilot", "difficultyAtLeast",
            "seated", "powered", "detected", "needsRepair",
        };

        public abstract bool Eval(MissionRuntime rt);
        public abstract string Describe();
        public override string ToString() => Describe();

        // ------------------------------------------------------------------------------------------------ parsing

        internal static Cond Parse(JToken t, string where, MissionFile.ParseContext ctx)
        {
            if (t == null) { ctx.Errors.Add($"{where}: a condition is missing"); return Never; }
            if (t.Type == JTokenType.Boolean) return (bool)t ? Always : Never;
            if (!(t is JObject o)) { ctx.Errors.Add($"{where}: a condition is true, false or an object like {{ \"destroyed\": \"Warden\" }}"); return Never; }
            // The operator is the first key that names one; the rest are its modifiers (so "destroyed": true can modify "subsystem"). A second
            // operator in the same object is not read by the first and is reported as an unknown field.
            string op = null;
            foreach (var p in o.Properties())
                if (Array.IndexOf(Ops, p.Name) >= 0) { op = p.Name; break; }
            if (op == null) { ctx.Errors.Add($"{where}: no condition operator among {string.Join(", ", NamesOf(o))} (known: {string.Join(", ", Ops)})"); return Never; }
            var r = new MissionFile.Reader(o, where, ctx.Errors);
            Cond c = Build(op, r, where, ctx);
            r.Done();
            return c ?? Never;
        }

        static IEnumerable<string> NamesOf(JObject o) { foreach (var p in o.Properties()) yield return "'" + p.Name + "'"; }

        static Cond Build(string op, MissionFile.Reader r, string where, MissionFile.ParseContext ctx)
        {
            switch (op)
            {
                case "and":
                case "or":
                {
                    var list = new List<Cond>();
                    if (r.Raw(op) is JArray a) { int i = 0; foreach (var e in a) list.Add(Parse(e, $"{where}.{op}[{i++}]", ctx)); }
                    else ctx.Errors.Add($"{where}.{op}: a list of conditions");
                    return op == "and" ? (Cond)new All(list) : new Any(list);
                }
                case "not": return new Not(Parse(r.Raw("not"), where + ".not", ctx));
                case "time": return new Time(r.Float("time", 0f));
                case "arrived": return new ShipFact(Fact.Arrived, Names(r, op, where, ctx, true), r.Float("delay", 0f), r.Bool("any", false));
                case "destroyed": return new ShipFact(Fact.Destroyed, Names(r, op, where, ctx, true), r.Float("delay", 0f), false);
                case "departed": return new ShipFact(Fact.Departed, Names(r, op, where, ctx, true), r.Float("delay", 0f), false);
                case "disabled": return new ShipFact(Fact.Disabled, Names(r, op, where, ctx, true), r.Float("delay", 0f), false);
                case "disarmed": return new ShipFact(Fact.Disarmed, Names(r, op, where, ctx, true), r.Float("delay", 0f), false);
                case "destroyedPercent":
                {
                    var names = Names(r, op, where, ctx, true);
                    if (!r.Has("atLeast")) ctx.Errors.Add($"{where}: destroyedPercent needs atLeast (a percentage)");
                    return new PercentDestroyed(names, r.Float("atLeast", 100f));
                }
                case "hull":
                {
                    string ship = One(r, op, where, ctx);
                    Bounds(r, where, ctx, out float below, out float above);
                    return new Hull(ship, below, above);
                }
                case "subsystem":
                {
                    string ship = One(r, op, where, ctx);
                    var parts = new List<string>();
                    string part = r.Str("part"); if (part != null) parts.Add(part);
                    var many = r.Strings("parts"); if (many != null) parts.AddRange(many);
                    string kind = r.Str("kind");
                    if ((parts.Count == 0) == (kind == null)) ctx.Errors.Add($"{where}: subsystem needs part, parts or kind (one of them)");
                    bool destroyed = r.Bool("destroyed", false);
                    float below = r.Float("below", -1f), above = r.Float("above", -1f);
                    if (!destroyed && below < 0f && above < 0f) ctx.Errors.Add($"{where}: subsystem needs below, above or \"destroyed\": true");
                    return new Subsystem(ship, parts, kind, destroyed, below, above);
                }
                case "range":
                {
                    string a = One(r, op, where, ctx);
                    var to = r.Raw("to");
                    string toShip = null; Vector3? toPoint = null;
                    if (to != null && to.Type == JTokenType.String) { toShip = (string)to; ctx.CheckShip(toShip, where + ".to", false); }
                    else { toPoint = r.Vec("to"); if (!toPoint.HasValue) ctx.Errors.Add($"{where}: range needs 'to' (a ship or [x, y, z])"); }
                    Bounds(r, where, ctx, out float below, out float above);
                    return new Range(a, toShip, toPoint, below, above);
                }
                case "hostilesNear":
                {
                    string a = One(r, op, where, ctx);
                    return new HostilesNear(a, r.Float("within", 1500f), r.Int("atLeast", 1), r.Bool("small", true));
                }
                case "teamAlive":
                {
                    int team = ctx.Team(r.Str("teamAlive"), where);
                    int atMost = r.Int("atMost", -1), atLeast = r.Int("atLeast", -1);
                    if (atMost < 0 && atLeast < 0) ctx.Errors.Add($"{where}: teamAlive needs atMost or atLeast");
                    return new TeamAlive(team, r.Str("teamAlive"), atMost, atLeast, r.Bool("small", false));
                }
                case "event":
                {
                    string name = r.Str("event"); ctx.CheckEvent(name, where + ".event");
                    string state = r.Str("state", "fired");
                    if (state != "fired" && state != "failed" && state != "pending") ctx.Errors.Add($"{where}.state: '{state}' (fired, failed or pending)");
                    return new EventIs(name, state, r.Float("delay", 0f));
                }
                case "objective":
                {
                    string id = r.Str("objective"); ctx.CheckObjective(id, where + ".objective");
                    string state = r.Str("is", "complete");
                    if (state != "complete" && state != "failed" && state != "pending") ctx.Errors.Add($"{where}.is: '{state}' (complete, failed or pending)");
                    return new ObjectiveIs(id, state);
                }
                case "beaming": return new Beaming(One(r, op, where, ctx));
                case "autopilot": return new Autopilot(r.Bool("autopilot", true));
                case "difficultyAtLeast": return new DifficultyAtLeast(r.Str("difficultyAtLeast"));
                case "seated": return new CabinFlag("seated", r.Bool("seated", true));
                case "powered": return new CabinFlag("powered", r.Bool("powered", true));
                case "detected":
                {
                    string by = r.Str("detected");
                    if (by != null && by != "<any>") ctx.CheckShip(by, where + ".detected", false);
                    return new Detected(by == "<any>" ? null : by);
                }
                case "needsRepair":
                {
                    string ship = One(r, op, where, ctx);
                    return new NeedsRepair(ship, r.Str("part"));
                }
            }
            ctx.Errors.Add($"{where}: unknown condition '{op}'");
            return null;
        }

        static List<string> Names(MissionFile.Reader r, string op, string where, MissionFile.ParseContext ctx, bool wingOk)
        {
            var t = r.Raw(op);
            var list = new List<string>();
            if (t != null && t.Type == JTokenType.String) list.Add((string)t);
            else if (t is JArray a) foreach (var e in a) list.Add((string)e);
            if (list.Count == 0) ctx.Errors.Add($"{where}.{op}: a ship or wing name, or a list of them");
            foreach (var n in list) ctx.CheckShip(n, where + "." + op, wingOk);
            return list;
        }

        static string One(MissionFile.Reader r, string op, string where, MissionFile.ParseContext ctx)
        {
            string n = r.Str(op);
            ctx.CheckShip(n, where + "." + op, false);
            return n;
        }

        static void Bounds(MissionFile.Reader r, string where, MissionFile.ParseContext ctx, out float below, out float above)
        {
            below = r.Float("below", -1f); above = r.Float("above", -1f);
            if (below < 0f && above < 0f) ctx.Errors.Add($"{where}: needs below or above");
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string Delay(float d) => d > 0f ? $" +{F(d)}s" : "";
        static string List(List<string> n) => n.Count == 1 ? n[0] : "[" + string.Join(", ", n) + "]";

        // ------------------------------------------------------------------------------------------------ the operators

        sealed class Literal : Cond
        {
            readonly bool _v;
            public Literal(bool v) { _v = v; }
            public override bool Eval(MissionRuntime rt) => _v;
            public override string Describe() => _v ? "true" : "false";
        }

        sealed class All : Cond
        {
            readonly List<Cond> _c;
            public All(List<Cond> c) { _c = c; }
            public override bool Eval(MissionRuntime rt) { foreach (var c in _c) if (!c.Eval(rt)) return false; return true; }
            public override string Describe() => "and(" + string.Join(", ", _c) + ")";
        }

        sealed class Any : Cond
        {
            readonly List<Cond> _c;
            public Any(List<Cond> c) { _c = c; }
            public override bool Eval(MissionRuntime rt) { foreach (var c in _c) if (c.Eval(rt)) return true; return false; }
            public override string Describe() => "or(" + string.Join(", ", _c) + ")";
        }

        sealed class Not : Cond
        {
            readonly Cond _c;
            public Not(Cond c) { _c = c; }
            public override bool Eval(MissionRuntime rt) => !_c.Eval(rt);
            public override string Describe() => "not(" + _c + ")";
        }

        /// <summary>FS2's has-time-elapsed: seconds since the mission started.</summary>
        sealed class Time : Cond
        {
            readonly float _s;
            public Time(float s) { _s = s; }
            public override bool Eval(MissionRuntime rt) => rt.Now >= rt.StartTime + _s;
            public override string Describe() => $"time {F(_s)}s";
        }

        /// <summary>
        /// FS2's has-arrived-delay, is-destroyed-delay, has-departed-delay, is-disabled-delay, is-disarmed-delay: true <c>delay</c> seconds after
        /// every named ship (or every ship of a named wing) did it. For arrivals, a wing counts once its first wave is in, or with <c>any</c> once
        /// one of its ships is.
        /// </summary>
        sealed class ShipFact : Cond
        {
            readonly Fact _fact; readonly List<string> _names; readonly float _delay; readonly bool _any;
            public ShipFact(Fact fact, List<string> names, float delay, bool any) { _fact = fact; _names = names; _delay = delay; _any = any; }
            public override bool Eval(MissionRuntime rt)
            {
                float since = -1f;
                foreach (var n in _names)
                {
                    float s = rt.Since(n, _fact, _any);
                    if (s < 0f) return false;
                    since = Mathf.Max(since, s);
                }
                return rt.Now >= since + _delay;
            }
            public override string Describe() => $"{_fact.ToString().ToLowerInvariant()}{(_any ? " any" : "")} {List(_names)}{Delay(_delay)}";
        }

        /// <summary>FS2's percent-ships-destroyed: of every ship the named wing (all its waves) or the listed ships will ever have.</summary>
        sealed class PercentDestroyed : Cond
        {
            readonly List<string> _names; readonly float _pct;
            public PercentDestroyed(List<string> names, float pct) { _names = names; _pct = pct; }
            public override bool Eval(MissionRuntime rt)
            {
                int dead = 0, total = 0;
                foreach (var n in _names) { rt.CountDestroyed(n, out int d, out int t); dead += d; total += t; }
                return total > 0 && dead * 100f >= _pct * total - 1e-3f;
            }
            public override string Describe() => $"destroyedPercent {List(_names)} >= {F(_pct)}%";
        }

        /// <summary>FS2's hits-left: hull percentage of a ship that has arrived (a destroyed ship has 0).</summary>
        sealed class Hull : Cond
        {
            readonly string _ship; readonly float _below, _above;
            public Hull(string ship, float below, float above) { _ship = ship; _below = below; _above = above; }
            public override bool Eval(MissionRuntime rt)
            {
                if (!rt.HasArrived(_ship)) return false;
                var c = rt.CombatOf(_ship);
                float pct = c != null && c.Alive ? c.Hull01 * 100f : 0f;
                return (_below < 0f || pct < _below) && (_above < 0f || pct > _above);
            }
            public override string Describe() => $"hull {_ship}" + (_below >= 0f ? $" < {F(_below)}%" : "") + (_above >= 0f ? $" > {F(_above)}%" : "");
        }

        /// <summary>
        /// FS2's hits-left-subsystem: the named part's health (several parts: their mean; a kind: every part of that kind), or with
        /// <c>"destroyed": true</c> every named part gone. A destroyed or vanished ship's parts count as destroyed.
        /// </summary>
        sealed class Subsystem : Cond
        {
            readonly string _ship, _kind; readonly List<string> _parts; readonly bool _destroyed; readonly float _below, _above;
            public Subsystem(string ship, List<string> parts, string kind, bool destroyed, float below, float above) { _ship = ship; _parts = parts; _kind = kind; _destroyed = destroyed; _below = below; _above = above; }
            public override bool Eval(MissionRuntime rt)
            {
                if (!rt.HasArrived(_ship)) return false;
                var c = rt.CombatOf(_ship);
                bool gone = c == null || !c.Alive;
                if (_destroyed)
                {
                    if (gone) return true;
                    foreach (var p in c.Parts)
                        if ((_kind != null ? p.Kind == _kind : _parts.Contains(p.Name)) && !p.Destroyed) return false;
                    return true;
                }
                float sum = 0f; int n = 0;
                if (!gone)
                    foreach (var p in c.Parts)
                        if (_kind != null ? p.Kind == _kind : _parts.Contains(p.Name)) { sum += p.Health01; n++; }
                float pct = n > 0 ? sum / n * 100f : 0f;
                return (_below < 0f || pct < _below) && (_above < 0f || pct > _above);
            }
            public override string Describe() => $"subsystem {_ship} {(_kind != null ? "kind " + _kind : List(_parts))}" + (_destroyed ? " destroyed" : "") + (_below >= 0f ? $" < {F(_below)}%" : "") + (_above >= 0f ? $" > {F(_above)}%" : "");
        }

        /// <summary>A ship within (below) or beyond (above) a distance of another ship or a point; false while either is not in the world.</summary>
        sealed class Range : Cond
        {
            readonly string _a, _toShip; readonly Vector3? _toPoint; readonly float _below, _above;
            public Range(string a, string toShip, Vector3? toPoint, float below, float above) { _a = a; _toShip = toShip; _toPoint = toPoint; _below = below; _above = above; }
            public override bool Eval(MissionRuntime rt)
            {
                var a = rt.ShipOf(_a);
                if (a == null) return false;
                Vector3 to;
                if (_toShip != null) { var b = rt.ShipOf(_toShip); if (b == null) return false; to = b.Position; }
                else to = _toPoint.Value;
                float d = Vector3.Distance(a.Position, to);
                return (_below < 0f || d < _below) && (_above < 0f || d > _above);
            }
            public override string Describe() => $"range {_a} to {(_toShip ?? _toPoint.ToString())}" + (_below >= 0f ? $" < {F(_below)} m" : "") + (_above >= 0f ? $" > {F(_above)} m" : "");
        }

        /// <summary>At least N ships hostile to the named one within a distance of it (small: fighters and bombers only).</summary>
        sealed class HostilesNear : Cond
        {
            readonly string _ship; readonly float _within; readonly int _atLeast; readonly bool _small;
            public HostilesNear(string ship, float within, int atLeast, bool small) { _ship = ship; _within = within; _atLeast = atLeast; _small = small; }
            public override bool Eval(MissionRuntime rt)
            {
                var me = rt.ShipOf(_ship);
                if (me == null) return false;
                int n = 0;
                foreach (var c in rt.World.Combat.All)
                {
                    if (!c.Alive || c.Ship == null || !Teams.Hostile(me.Team, c.Ship.Team) || (_small && MissionRuntime.IsBig(c))) continue;
                    if (Vector3.Distance(c.Ship.Position, me.Position) < _within && ++n >= _atLeast) return true;
                }
                return false;
            }
            public override string Describe() => $"hostilesNear {_ship} within {F(_within)} m >= {_atLeast}";
        }

        /// <summary>How many ships of a team are alive in the world (small: fighters and bombers only).</summary>
        sealed class TeamAlive : Cond
        {
            readonly int _team, _atMost, _atLeast; readonly string _name; readonly bool _small;
            public TeamAlive(int team, string name, int atMost, int atLeast, bool small) { _team = team; _name = name; _atMost = atMost; _atLeast = atLeast; _small = small; }
            public override bool Eval(MissionRuntime rt)
            {
                int n = 0;
                foreach (var c in rt.World.Combat.All) if (c.Alive && c.Ship != null && c.Ship.Team == _team && (!_small || !MissionRuntime.IsBig(c))) n++;
                return (_atMost < 0 || n <= _atMost) && (_atLeast < 0 || n >= _atLeast);
            }
            public override string Describe() => $"teamAlive {_name}" + (_atMost >= 0 ? $" <= {_atMost}" : "") + (_atLeast >= 0 ? $" >= {_atLeast}" : "");
        }

        /// <summary>FS2's is-event-true-delay (and is-event-false): another event has fired (first time), failed, or neither yet.</summary>
        sealed class EventIs : Cond
        {
            readonly string _name, _state; readonly float _delay;
            public EventIs(string name, string state, float delay) { _name = name; _state = state; _delay = delay; }
            public override bool Eval(MissionRuntime rt)
            {
                var e = rt.EventNamed(_name);
                if (e == null) return false;
                switch (_state)
                {
                    case "fired": return e.FirstFiredAt >= 0f && rt.Now >= e.FirstFiredAt + _delay;
                    case "failed": return e.FailedAt >= 0f && rt.Now >= e.FailedAt + _delay;
                    default: return e.FirstFiredAt < 0f && e.FailedAt < 0f;
                }
            }
            public override string Describe() => $"event '{_name}' {_state}{Delay(_delay)}";
        }

        sealed class ObjectiveIs : Cond
        {
            readonly string _id, _state;
            public ObjectiveIs(string id, string state) { _id = id; _state = state; }
            public override bool Eval(MissionRuntime rt)
            {
                var s = rt.ObjectiveState(_id);
                return _state == "complete" ? s == MissionBoard.State.Done : _state == "failed" ? s == MissionBoard.State.Failed : s == MissionBoard.State.Active;
            }
            public override string Describe() => $"objective {_id} {_state}";
        }

        /// <summary>A ship is charging or firing a heavy beam (from the beam_charge and beam_end events).</summary>
        sealed class Beaming : Cond
        {
            readonly string _ship;
            public Beaming(string ship) { _ship = ship; }
            public override bool Eval(MissionRuntime rt) { var s = rt.ShipOf(_ship); return s != null && rt.IsBeaming(s.Id); }
            public override string Describe() => $"beaming {_ship}";
        }

        /// <summary>The AI flies the player's ship (soaks, captures, the auto-play check): lets a mission tell its autopilot what to go after.</summary>
        sealed class Autopilot : Cond
        {
            readonly bool _v;
            public Autopilot(bool v) { _v = v; }
            public override bool Eval(MissionRuntime rt) => rt.Host.PlayerIsAi == _v;
            public override string Describe() => $"autopilot {_v}";
        }

        sealed class DifficultyAtLeast : Cond
        {
            readonly string _level;
            public DifficultyAtLeast(string level) { _level = level; }
            public override bool Eval(MissionRuntime rt) => rt.Host.Level >= rt.Host.LevelOf(_level);
            public override string Describe() => $"difficulty >= {_level}";
        }

        // The cabin (design section 6). The cabin is not built yet: these read the ICabin hook, which a stub answers for now.

        sealed class CabinFlag : Cond
        {
            readonly string _what; readonly bool _v;
            public CabinFlag(string what, bool v) { _what = what; _v = v; }
            public override bool Eval(MissionRuntime rt) => (_what == "seated" ? rt.Cabin.Seated : rt.Cabin.Powered) == _v;
            public override string Describe() => $"{_what} {_v} (cabin)";
        }

        sealed class Detected : Cond
        {
            readonly string _by;
            public Detected(string by) { _by = by; }
            public override bool Eval(MissionRuntime rt) => rt.Cabin.DetectedBy(_by == null ? GameWorld.NoShip : rt.IdOf(_by));
            public override string Describe() => $"detected by {_by ?? "any"} (cabin)";
        }

        sealed class NeedsRepair : Cond
        {
            readonly string _ship, _part;
            public NeedsRepair(string ship, string part) { _ship = ship; _part = part; }
            public override bool Eval(MissionRuntime rt) => rt.Cabin.NeedsRepair(rt.IdOf(_ship), _part);
            public override string Describe() => $"needsRepair {_ship}{(_part != null ? " " + _part : "")} (cabin)";
        }
    }
}
