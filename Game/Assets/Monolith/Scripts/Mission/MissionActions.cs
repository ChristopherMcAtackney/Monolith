using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// One thing an event does (FS2's SEXP actions, as JSON): <c>{ "do": "arrive", "ship": "Psalm" }</c>. Every action is listed in <see cref="Ops"/>
    /// and documented in Scripts/Mission/README.md. Actions run on the sim tick, in order, through the runtime.
    /// </summary>
    public abstract class Act
    {
        public static readonly string[] Ops =
        {
            "arrive", "depart", "radio", "objective", "order", "beams", "turrets", "music", "end", "cond", "invulnerable", "log",
            "scan", "callOut", "knockOut", "cabinLights", "theme",
        };

        public abstract void Run(MissionRuntime rt);
        public abstract string Describe();
        public override string ToString() => Describe();

        internal static Act Parse(JToken t, string where, MissionFile.ParseContext ctx)
        {
            if (!(t is JObject o)) { ctx.Errors.Add($"{where}: an action is an object like {{ \"do\": \"arrive\", \"ship\": \"Choir\" }}"); return null; }
            var r = new MissionFile.Reader(o, where, ctx.Errors);
            string op = r.Str("do", required: true);
            Act a = op == null ? null : Build(op, r, where, ctx);
            if (op != null && a == null && System.Array.IndexOf(Ops, op) < 0) ctx.Errors.Add($"{where}: unknown action '{op}' (known: {string.Join(", ", Ops)})");
            r.Done();
            return a;
        }

        static Act Build(string op, MissionFile.Reader r, string where, MissionFile.ParseContext ctx)
        {
            switch (op)
            {
                case "arrive":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    return new Arrive(ship);
                }
                case "depart":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    string bay = r.Str("bay"); if (bay != null) ctx.CheckShip(bay, where + ".bay", false);
                    return new Depart(ship, r.Vec("to"), bay, r.Float("jumpAfter", 3f));
                }
                case "radio":
                {
                    string id = r.Str("message");
                    if (id != null) { ctx.CheckMessage(id, where + ".message"); return new Radio(id, null); }
                    var m = new MissionFile.MessageDef
                    {
                        Id = "(inline)", Where = where, From = r.Str("from", required: true), FallbackFrom = r.Str("fallbackFrom"), Text = r.Str("text", required: true),
                        Priority = r.Str("priority", "normal"), Voice = r.Str("voice"), Seconds = r.Float("seconds", 0f),
                    };
                    MissionFile.ReadLineMeta(r, m, ctx);
                    ctx.CheckSpeaker(m.From, where + ".from");
                    if (m.FallbackFrom != null) ctx.CheckSpeaker(m.FallbackFrom, where + ".fallbackFrom");
                    if (m.Priority != "high" && m.Priority != "normal" && m.Priority != "low") ctx.Errors.Add($"{where}.priority: '{m.Priority}' (high, normal or low)");
                    return new Radio(null, m);
                }
                case "objective":
                {
                    string id = r.Str("id", required: true); ctx.CheckObjective(id, where + ".id");
                    string set = r.Str("set", required: true);
                    if (set != "complete" && set != "failed" && set != "pending" && set != "show" && set != "hide") ctx.Errors.Add($"{where}.set: '{set}' (complete, failed, pending, show or hide)");
                    return new Objective(id, set);
                }
                case "order":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    bool standing = r.Bool("standing", false);
                    // The order itself is the rest of this object (order, target, part, path, speed, then, to).
                    var order = new JObject();
                    foreach (var k in new[] { "order", "target", "part", "partKind", "path", "speed", "then", "to" }) { var v = r.Raw(k); if (v != null) order[k] = v; }
                    var od = MissionFile.ReadOrder(new MissionFile.Reader(order, where, ctx.Errors), where, ctx);
                    return od == null ? null : new Order(ship, od, standing);
                }
                case "beams":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    if (!r.Has("free")) ctx.Errors.Add($"{where}: beams needs \"free\": true or false");
                    string target = r.Str("target", null);
                    if (target != null) ctx.CheckShip(target, where + ".target", false);
                    return new Beams(ship, r.Bool("free", true), target);
                }
                case "turrets":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    if (!r.Has("fire")) ctx.Errors.Add($"{where}: turrets needs \"fire\": true or false");
                    return new Turrets(ship, r.Bool("fire", true));
                }
                case "music":
                {
                    string state = r.Str("state"), sting = r.Str("sting");
                    bool hasAuto = r.Has("auto"); bool auto = r.Bool("auto", false);
                    float hold = r.Float("for", 0f);
                    if (state != null && !MissionMusic.IsState(state)) ctx.Errors.Add($"{where}.state: '{state}' is not a music state ({string.Join(", ", MissionMusic.States)})");
                    if (sting != null && !MissionMusic.IsSting(sting)) ctx.Errors.Add($"{where}.sting: '{sting}' ({string.Join(", ", MissionMusic.Stings)})");
                    if (state == null && sting == null && !hasAuto) ctx.Errors.Add($"{where}: music needs state, sting or auto");
                    return new Music(state, sting, hasAuto ? auto : (bool?)null, hold);
                }
                case "theme":
                {
                    // a theme track over the base score: { "do": "theme", "name": "lector" } fades it in, { "do": "theme", "name": "lector", "on": false } fades it out
                    string name = r.Str("name", required: true);
                    bool on = r.Bool("on", true);
                    float fadeIn = r.Float("fadeIn", float.NaN), fadeOut = r.Float("fadeOut", float.NaN), prio = r.Float("priority", float.NaN), vol = r.Float("volume", float.NaN), duck = r.Float("duck", float.NaN);
                    string mode = r.Str("mode");
                    if (name != null && !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]+$")) ctx.Errors.Add($"{where}.name: '{name}' is letters, digits and underscores (it names a theme in music.json: \"<mission>/<name>\")");
                    if (mode != null && mode != "duck" && mode != "replace") ctx.Errors.Add($"{where}.mode: '{mode}' (duck or replace)");
                    if (!float.IsNaN(fadeIn) && fadeIn < 0f || !float.IsNaN(fadeOut) && fadeOut < 0f) ctx.Errors.Add($"{where}: fadeIn and fadeOut are seconds, not negative");
                    return name == null ? null : new Theme(name, on, fadeIn, fadeOut, prio, vol, mode, duck);
                }
                case "end":
                {
                    string result = r.Str("result", "victory");
                    if (result != "victory" && result != "defeat" && result != "neutral") ctx.Errors.Add($"{where}.result: '{result}' (victory, defeat or neutral)");
                    return new End(result, r.Str("text"));
                }
                case "cond":
                {
                    var cases = new List<(Cond, List<Act>)>();
                    if (r.Raw("cases") is JArray arr)
                    {
                        int i = 0;
                        foreach (var c in arr)
                        {
                            string cw = $"{where}.cases[{i++}]";
                            var cr = new MissionFile.Reader(c, cw, ctx.Errors);
                            Cond test = cr.Has("else") ? Cond.Always : Cond.Parse(cr.Raw("if"), cw + ".if", ctx);
                            var acts = new List<Act>();
                            var then = cr.Has("else") ? cr.Raw("else") : cr.Raw("then");
                            if (then is JArray ta) { int k = 0; foreach (var t in ta) { var a = Parse(t, $"{cw}.then[{k++}]", ctx); if (a != null) acts.Add(a); } }
                            else ctx.Errors.Add($"{cw}: a case is {{ \"if\": condition, \"then\": [actions] }} or {{ \"else\": [actions] }}");
                            cr.Done();
                            cases.Add((test, acts));
                        }
                    }
                    else ctx.Errors.Add($"{where}.cases: a list of {{ \"if\", \"then\" }} (the first that holds runs)");
                    return new CondAct(cases);
                }
                case "invulnerable":
                {
                    string ship = r.Str("ship", required: true); ctx.CheckShip(ship, where + ".ship", true);
                    return new Invulnerable(ship, r.Bool("on", true));
                }
                case "log": return new Log(r.Str("text", ""));
                case "scan":
                {
                    string from = r.Str("from", required: true); ctx.CheckShip(from, where + ".from", false);
                    return new Scan(from, r.Float("seconds", 10f), r.Float("range", 5000f));
                }
                case "callOut":
                {
                    MissionFile.ReadLineMeta(r, new MissionFile.MessageDef(), ctx);       // pending and intensity: for the voice tools, nothing changes in play
                    return new CallOut(r.Str("text"), r.Str("prompt", "LEAVE THE SEAT"), r.Str("from", "#Crew"));
                }
                case "knockOut":
                {
                    string ship = r.Str("ship", MissionFile.Player); ctx.CheckShip(ship, where + ".ship", false);
                    string part = r.Str("part", required: true);
                    return new KnockOut(ship, part);
                }
                case "cabinLights":
                {
                    string state = r.Str("state", required: true);
                    if (state != "normal" && state != "dark" && state != "emergency" && state != "alert") ctx.Errors.Add($"{where}.state: '{state}' (normal, dark, emergency or alert)");
                    return new CabinLights(state);
                }
            }
            return null;
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------------------------------------ the actions

        /// <summary>Bring a ship, or a wing's next wave, into the fight now (with its arrival effect).</summary>
        sealed class Arrive : Act
        {
            readonly string _ship;
            public Arrive(string ship) { _ship = ship; }
            public override void Run(MissionRuntime rt) => rt.Arrive(_ship);
            public override string Describe() => $"arrive {_ship}";
        }

        /// <summary>A ship or wing leaves: flies out (towards a point or a ship's bay) and jumps.</summary>
        sealed class Depart : Act
        {
            readonly string _ship, _bay; readonly Vector3? _to; readonly float _after;
            public Depart(string ship, Vector3? to, string bay, float after) { _ship = ship; _to = to; _bay = bay; _after = after; }
            public override void Run(MissionRuntime rt) => rt.Depart(_ship, _to, _bay, _after);
            public override string Describe() => $"depart {_ship}";
        }

        sealed class Radio : Act
        {
            readonly string _id; readonly MissionFile.MessageDef _inline;
            public Radio(string id, MissionFile.MessageDef inline) { _id = id; _inline = inline; }
            public override void Run(MissionRuntime rt) => rt.Radio.Send(_inline ?? rt.MessageNamed(_id));
            public override string Describe() => _id != null ? $"radio {_id}" : $"radio \"{_inline.Text}\"";
        }

        sealed class Objective : Act
        {
            readonly string _id, _set;
            public Objective(string id, string set) { _id = id; _set = set; }
            public override void Run(MissionRuntime rt) => rt.SetObjective(_id, _set);
            public override string Describe() => $"objective {_id} {_set}";
        }

        /// <summary>An AI order to a ship or every ship of a wing (FS2's add-goal): attack, disable, disarm, guard, form, engage, bomb, waypoints, depart.</summary>
        sealed class Order : Act
        {
            readonly string _ship; readonly MissionFile.OrderDef _order; readonly bool _standing;
            public Order(string ship, MissionFile.OrderDef order, bool standing) { _ship = ship; _order = order; _standing = standing; }
            public override void Run(MissionRuntime rt) => rt.OrderShips(_ship, _order, _standing);
            public override string Describe() => $"order {_ship} {_order}" + (_standing ? " (standing)" : "");
        }

        /// <summary>FS2's beam-free-all and beam-lock-all; with a target, FS2's beam-fire: the ship's beams (and its helm) go for that ship
        /// first while it is alive and in reach, and for the nearest enemy capital otherwise.</summary>
        sealed class Beams : Act
        {
            readonly string _ship, _target; readonly bool _free;
            public Beams(string ship, bool free, string target) { _ship = ship; _free = free; _target = target; }
            public override void Run(MissionRuntime rt)
            {
                int target = _target != null ? rt.IdOf(_target) : GameWorld.NoShip;
                foreach (var id in rt.IdsOf(_ship)) { rt.Host.SetBeams(id, _free); if (_target != null) rt.Host.SetBeamTarget(id, target); }
                rt.Note("beams", $"{_ship} {(_free ? "free" : "held")}" + (_target != null ? $" on {_target}" : ""));
            }
            public override string Describe() => $"beams {_ship} {(_free ? "free" : "held")}" + (_target != null ? $" on {_target}" : "");
        }

        /// <summary>A capital ship's or station's gun and flak turrets hold their fire, or wake up (its beams follow "beams").</summary>
        sealed class Turrets : Act
        {
            readonly string _ship; readonly bool _fire;
            public Turrets(string ship, bool fire) { _ship = ship; _fire = fire; }
            public override void Run(MissionRuntime rt) { foreach (var id in rt.IdsOf(_ship)) rt.Host.SetTurrets(id, _fire); rt.Note("turrets", $"{_ship} {(_fire ? "firing" : "holding")}"); }
            public override string Describe() => $"turrets {_ship} {(_fire ? "fire" : "hold")}";
        }

        sealed class Music : Act
        {
            readonly string _state, _sting; readonly bool? _auto; readonly float _hold;
            public Music(string state, string sting, bool? auto, float hold) { _state = state; _sting = sting; _auto = auto; _hold = hold; }
            public override void Run(MissionRuntime rt)
            {
                if (_sting != null) rt.Music.Sting(_sting);
                if (_state != null) rt.Music.Force(_state, _hold);
                if (_auto.HasValue) rt.Music.SetAuto(_auto.Value);
            }
            public override string Describe() => "music" + (_state != null ? " " + _state : "") + (_sting != null ? " sting " + _sting : "") + (_auto.HasValue ? " auto " + _auto.Value : "") + (_hold > 0f ? $" for {F(_hold)}s" : "");
        }

        /// <summary>A theme track on or off over the base score (see MissionMusic.Theme).</summary>
        sealed class Theme : Act
        {
            readonly string _name, _mode; readonly bool _on; readonly float _in, _out, _prio, _vol, _duck;
            public Theme(string name, bool on, float fadeIn, float fadeOut, float priority, float volume, string mode, float duck) { _name = name; _on = on; _in = fadeIn; _out = fadeOut; _prio = priority; _vol = volume; _mode = mode; _duck = duck; }
            public override void Run(MissionRuntime rt) => rt.Music.Theme(new ThemeEvent { Name = _name, On = _on, FadeIn = _in, FadeOut = _out, Priority = _prio, VolumeDb = _vol, Mode = _mode, DuckDb = _duck });
            public override string Describe() => $"theme {_name} {(_on ? "on" : "off")}" + (float.IsNaN(_in) ? "" : $" fadeIn {F(_in)}s") + (float.IsNaN(_out) ? "" : $" fadeOut {F(_out)}s");
        }

        sealed class End : Act
        {
            readonly string _result, _text;
            public End(string result, string text) { _result = result; _text = text; }
            public override void Run(MissionRuntime rt) => rt.End(_result, _text);
            public override string Describe() => $"end {_result} \"{_text}\"";
        }

        /// <summary>FS2's cond: the first case whose condition holds runs its actions.</summary>
        sealed class CondAct : Act
        {
            readonly List<(Cond test, List<Act> acts)> _cases;
            public CondAct(List<(Cond, List<Act>)> cases) { _cases = cases; }
            public override void Run(MissionRuntime rt)
            {
                foreach (var (test, acts) in _cases)
                    if (test.Eval(rt)) { foreach (var a in acts) rt.RunAction(a); return; }
            }
            public override string Describe() => "cond(" + string.Join("; ", _cases.ConvertAll(c => c.test + " -> " + string.Join(", ", c.acts))) + ")";
        }

        /// <summary>FS2's protect-ship / ship-invulnerable: damage is taken and shown but never kills.</summary>
        sealed class Invulnerable : Act
        {
            readonly string _ship; readonly bool _on;
            public Invulnerable(string ship, bool on) { _ship = ship; _on = on; }
            public override void Run(MissionRuntime rt) { foreach (var id in rt.IdsOf(_ship)) { var c = rt.World.Combat.Get(id); if (c != null) c.Invulnerable = _on; } }
            public override string Describe() => $"invulnerable {_ship} {_on}";
        }

        sealed class Log : Act
        {
            readonly string _text;
            public Log(string text) { _text = text; }
            public override void Run(MissionRuntime rt) => rt.Note("log", _text);
            public override string Describe() => $"log \"{_text}\"";
        }

        // The cabin's actions (design section 6). The cabin is not built yet: these go to the ICabin hook, whose stub logs them and does the
        // part of each that already has a meaning in the fight (a knocked-out part is destroyed; a scan decides detection by power state).

        sealed class Scan : Act
        {
            readonly string _from; readonly float _seconds, _range;
            public Scan(string from, float seconds, float range) { _from = from; _seconds = seconds; _range = range; }
            public override void Run(MissionRuntime rt)
            {
                var s = rt.ShipOf(_from);
                if (s == null) return;
                rt.Cabin.StartScan(s.Id, _seconds, _range, rt.Now);
                GameEvents.RaiseAt(MissionEvents.CabinScan, s.Position, s.Forward, "start", _seconds, s.Id);
                rt.Note("cabin", $"scan sweep from {_from} for {F(_seconds)} s, range {F(_range)} m");
            }
            public override string Describe() => $"scan from {_from} {F(_seconds)}s";
        }

        sealed class CallOut : Act
        {
            readonly string _text, _prompt, _from;
            public CallOut(string text, string prompt, string from) { _text = text; _prompt = prompt; _from = from; }
            public override void Run(MissionRuntime rt)
            {
                if (_text != null) rt.Radio.Send(new MissionFile.MessageDef { Id = "(call out)", From = _from, Text = _text, Priority = "high" });
                rt.Cabin.CallOut(_prompt);
                rt.Board.Prompt = _prompt; rt.Board.Version++;
                GameEvents.Raise(MissionEvents.CabinCallOut, _prompt);
                rt.Note("cabin", $"call out of the seat: {_prompt}");
            }
            public override string Describe() => $"callOut \"{_prompt}\"";
        }

        sealed class KnockOut : Act
        {
            readonly string _ship, _part;
            public KnockOut(string ship, string part) { _ship = ship; _part = part; }
            public override void Run(MissionRuntime rt)
            {
                int id = rt.IdOf(_ship);
                var c = rt.World.Combat.Get(id);
                if (c == null) return;
                var p = c.FindPart(_part) ?? c.FirstPart(_part);
                if (p == null) { rt.Note("cabin", $"knockOut: {_ship} has no part '{_part}'"); return; }
                rt.Cabin.KnockOut(id, p.Name, rt.Now);
                rt.Host.DestroyPart(id, p.Index);
                GameEvents.Raise(MissionEvents.CabinKnockOut, p.Name, p.Index + 1, id);
                rt.Note("cabin", $"{_ship}'s {p.Name} knocked out: it needs a hand repair");
            }
            public override string Describe() => $"knockOut {_ship} {_part}";
        }

        sealed class CabinLights : Act
        {
            readonly string _state;
            public CabinLights(string state) { _state = state; }
            public override void Run(MissionRuntime rt)
            {
                rt.Cabin.SetLights(_state);
                GameEvents.Raise(MissionEvents.CabinLights, _state);
                rt.Note("cabin", $"lights {_state}");
            }
            public override string Describe() => $"cabinLights {_state}";
        }
    }
}
