using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// One mission, as read from its file (<c>*.mission.json</c>, format "monolith-mission/1"; the format and every condition and action are in
    /// Scripts/Mission/README.md). <see cref="Load"/> reads and checks it: misspelt fields, unknown names, conditions and actions are errors,
    /// all reported together. Fields ending in "Why", and "about" and "note", are comments and are kept out of the way.
    /// </summary>
    public sealed class MissionFile
    {
        public const string Format = "monolith-mission/1";
        public const string Player = "<player>";
        public const string AnyWingman = "<any wingman>";

        public string Id, Name, About, Difficulty, PlayerName, Source;
        public List<string> Teams = new List<string> { "diaspora", "earth" };
        public bool PlayerDeathEnds = true;
        public BriefingDef Briefing = new BriefingDef();
        public List<DebriefDef> Debriefing = new List<DebriefDef>();
        public MusicDef Music = new MusicDef();
        public PacingDef Pacing = new PacingDef();
        public CapDef ArrivalCap;
        public List<ShipDef> Ships = new List<ShipDef>();
        public List<WingDef> Wings = new List<WingDef>();
        public List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public List<MessageDef> Messages = new List<MessageDef>();
        public List<EventDef> Events = new List<EventDef>();
        public AutoplayDef Autoplay = new AutoplayDef();

        // ------------------------------------------------------------------------------------------------ the parts of a file

        public sealed class BriefingDef
        {
            public string Title;
            public List<string> Stages = new List<string>();
            public List<KeyValuePair<string, string>> Questions = new List<KeyValuePair<string, string>>();
        }

        public sealed class DebriefDef { public Cond When; public string Text; }

        public sealed class MusicDef
        {
            public string Start = "ambient";
            public bool Auto = true;
            public Dictionary<string, string> Tracks = new Dictionary<string, string>();
        }

        public sealed class PacingDef
        {
            public List<string> Hulls = new List<string>();
            public float QuietRange = 2500f;
        }

        public sealed class CapDef { public int Team; public float[] PerLevel; public float At(int level) => PerLevel[Mathf.Clamp(level, 0, PerLevel.Length - 1)]; }

        /// <summary>Where and how a ship or a wing comes in (FS2's arrival cue, delay, location and anchor).</summary>
        public sealed class ArrivalDef
        {
            /// <summary>Null: present when the mission starts. Otherwise it arrives when this holds (plus <see cref="Delay"/>); <see cref="Cond.Never"/>: only an "arrive" action brings it.</summary>
            public Cond Cue;
            public float Delay;
            public Vector3? At, Offset, Rot, FacingDir;
            public string Near, Bay, Facing;
            public float Distance;
            public string Formation = "v";
            public Vector3 Spacing = new Vector3(45f, 8f, 40f);
            public List<Vector3> Offsets;
            public float Speed = 0.5f, Throttle = -1f;
            /// <summary>"jump" (a jump-in flash and sound through the juice) or "none". Ships present at the start never flash.</summary>
            public string Effect = "jump";
            public bool AtStart => Cue == null;
        }

        public sealed class DepartureDef
        {
            public Cond Cue;
            public float Delay;
            public Vector3? To;
            public string Bay;
            /// <summary>Seconds of flying out before the jump (FS2's ships run up their drive for a few seconds).</summary>
            public float JumpAfter = 3f;
        }

        /// <summary>An AI order (FS2's ai-goals): attack, disable, disarm, guard, form, engage, bomb, waypoints, depart.</summary>
        public sealed class OrderDef
        {
            public OrderKind Kind;
            public string Target;
            public List<string> Parts;
            public List<Vector3> Path;
            public float Speed = -1f;
            /// <summary>After the last waypoint: "depart", "stop" or "loop".</summary>
            public string Then = "stop";
            public Vector3? To;
            public string Where;
            public override string ToString() => Kind.ToString().ToLowerInvariant() + (Target != null ? " " + Target : "") + (Parts != null ? " [" + string.Join(", ", Parts) + "]" : "");
        }

        /// <summary>What a ship carries into the fight and how it is flown: the same fields for a single ship and for a wing's group.</summary>
        public class Loadout
        {
            public string Class, Ai, Role;
            public Dictionary<string, int> Ammo;
            public float HullScale = 1f, Hull = 100f;
            public bool? SelfHeal;
            public bool Invulnerable, Wingman;
            public OrderDef Goal, Fallback;
            public CapitalDef Capital;
            public LookDef Look;
        }

        public sealed class ShipDef : Loadout
        {
            public string Name;
            public int Team;
            public ArrivalDef Arrival = new ArrivalDef();
            public DepartureDef Departure;
            public string Where;
        }

        public sealed class GroupDef : Loadout { public int Count = 1; }

        public sealed class WingDef
        {
            public string Name, Where;
            public int Team;
            public List<GroupDef> Groups = new List<GroupDef>();
            public int Waves = 1, WaveThreshold;
            public float WaveDelay = 5f;
            /// <summary>Its fighters count against, and wait for, the mission's arrival cap.</summary>
            public bool Capped;
            public bool Wingmen;
            public OrderDef Goal, Fallback;
            public ArrivalDef Arrival = new ArrivalDef();
            public DepartureDef Departure;
            public int PerWave { get { int n = 0; foreach (var g in Groups) n += g.Count; return n; } }
        }

        /// <summary>A capital ship's helm and gunners (the AI's CapitalOrders, as data).</summary>
        public sealed class CapitalDef
        {
            public float Standoff = 2600f, Band = 400f, Bearing = 70f, Throttle = 0.3f, TurretWaitScale = 1f, BeamWaitScale = 1f;
            public string TurretProfile = "earth_turret";
            public bool HoldBeams, Stationary;
            public Dictionary<string, string> Weapons = new Dictionary<string, string>();
        }

        public sealed class LookDef { public float Length = 400f, Width = 100f, Height = 80f; public int Seed = 1; public bool HasHull; }

        public sealed class ObjectiveDef
        {
            public string Id, Kind = "primary", Text, Where;
            public bool Hidden;
            public Cond Complete, Fail;
        }

        public sealed class MessageDef
        {
            public string Id, From, FallbackFrom, Text, Priority = "normal", Voice, Where;
            public float Seconds;
        }

        public sealed class EventDef
        {
            public string Name, Where, After, DirectiveText;
            public Cond When, FailWhen, DirectiveShow;
            public float Delay, AfterDelay, Interval = 1f;
            /// <summary>How many times it may fire; -1 for ever (FS2's repeat count).</summary>
            public int Repeat = 1;
            /// <summary>False keeps its firing out of the timeline (a check that repeats every few seconds).</summary>
            public bool Log = true;
            public List<Act> Do = new List<Act>();
        }

        public sealed class AutoplayDef
        {
            public float Seconds = 1200f;
            public string Difficulty;
            public bool Invulnerable = true;
            public List<string> Result = new List<string>();
            public List<string> Fired = new List<string>(), NotFired = new List<string>(), Order = new List<string>();
            public float LongestQuietMax = -1f, ThreatsAvgMin = -1f;
            public int MaxEngagingMax = -1, MaxThreatsMax = -1;
        }

        // ------------------------------------------------------------------------------------------------ loading

        /// <summary>Read and check a mission. Throws <see cref="InvalidDataException"/> listing every problem found.</summary>
        public static MissionFile Load(string json, string source)
        {
            var errors = new Errors(source);
            JObject root;
            try { root = JObject.Parse(json, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
            catch (Exception e) { throw new InvalidDataException($"{source}: not valid JSON: {e.Message}"); }
            var m = new MissionFile { Source = source };
            var r = new Reader(root, "mission", errors);
            if (r.Str("format") != Format) errors.Add("mission: format must be \"" + Format + "\"");
            m.Id = r.Str("id", required: true);
            m.Name = r.Str("name", required: true);
            m.About = r.Str("about");
            m.Difficulty = r.Str("difficulty", "Medium");
            m.PlayerName = r.Str("player", required: true);
            m.PlayerDeathEnds = r.Bool("playerDeathEnds", true);
            var teams = r.Strings("teams");
            if (teams != null) m.Teams = teams;
            var ctx = new ParseContext(m, errors);

            // Names first (conditions and actions refer to anything, wherever it is in the file), then everything else.
            foreach (var s in r.Objects("ships")) ctx.DeclareShip(s.Str("name", required: true), s.Where);
            foreach (var w in r.Objects("wings")) ctx.DeclareWing(w.Str("name", required: true), w.Where, w);
            foreach (var o in r.Objects("objectives")) ctx.Declare(ctx.Objectives, o.Str("id", required: true), "objective", o.Where);
            foreach (var o in r.Objects("messages")) ctx.Declare(ctx.Messages, o.Str("id", required: true), "message", o.Where);
            foreach (var o in r.Objects("events")) ctx.Declare(ctx.EventNames, o.Str("name", required: true), "event", o.Where);
            if (m.PlayerName != null && !ctx.Ships.ContainsKey(m.PlayerName)) errors.Add($"mission: player '{m.PlayerName}' is not one of the ships");

            foreach (var s in r.Objects("ships")) m.Ships.Add(ReadShip(s, ctx));
            foreach (var w in r.Objects("wings")) m.Wings.Add(ReadWing(w, ctx));
            foreach (var o in r.Objects("objectives")) m.Objectives.Add(ReadObjective(o, ctx));
            foreach (var o in r.Objects("messages")) m.Messages.Add(ReadMessage(o, ctx));
            foreach (var o in r.Objects("events")) m.Events.Add(ReadEvent(o, ctx));
            var b = r.Obj("briefing");
            if (b != null)
            {
                m.Briefing.Title = b.Str("title");
                m.Briefing.Stages = b.Strings("stages") ?? new List<string>();
                foreach (var q in b.Objects("questions")) { m.Briefing.Questions.Add(new KeyValuePair<string, string>(q.Str("q", required: true), q.Str("a", required: true))); q.Done(); }
                b.Done();
            }
            foreach (var d in r.Objects("debriefing")) { m.Debriefing.Add(new DebriefDef { When = Cond.Parse(d.Raw("when") ?? true, d.Where + ".when", ctx), Text = d.Str("text", required: true) }); d.Done(); }
            var mu = r.Obj("music");
            if (mu != null)
            {
                m.Music.Start = mu.Str("start", "ambient");
                if (!MissionMusic.IsState(m.Music.Start)) errors.Add($"{mu.Where}.start: '{m.Music.Start}' is not a music state ({string.Join(", ", MissionMusic.States)})");
                m.Music.Auto = mu.Bool("auto", true);
                var tr = mu.Obj("tracks");
                if (tr != null) { foreach (var p in tr.Keys()) { if (!MissionMusic.IsState(p)) errors.Add($"{tr.Where}: '{p}' is not a music state"); m.Music.Tracks[p] = tr.Str(p); } tr.Done(); }
                mu.Done();
            }
            var pa = r.Obj("pacing");
            if (pa != null)
            {
                m.Pacing.Hulls = pa.Strings("hulls") ?? new List<string>();
                foreach (var h in m.Pacing.Hulls) ctx.CheckShip(h, pa.Where + ".hulls", false);
                m.Pacing.QuietRange = pa.Float("quietRange", 2500f);
                pa.Done();
            }
            var cap = r.Obj("arrivalCap");
            if (cap != null)
            {
                m.ArrivalCap = new CapDef { Team = ctx.Team(cap.Str("team", required: true), cap.Where), PerLevel = cap.PerLevel("value") };
                cap.Done();
            }
            var ap = r.Obj("autoplay");
            if (ap != null)
            {
                var a = m.Autoplay;
                a.Seconds = ap.Float("seconds", 1200f);
                a.Difficulty = ap.Str("difficulty");
                a.Invulnerable = ap.Bool("invulnerable", true);
                var ex = ap.Obj("expect");
                if (ex != null)
                {
                    a.Result = ex.Strings("result") ?? new List<string>();
                    foreach (var res in a.Result) if (res != "victory" && res != "defeat" && res != "neutral" && res != "none") errors.Add($"{ex.Where}.result: '{res}' (victory, defeat, neutral or none)");
                    a.Fired = ex.Strings("fired") ?? new List<string>();
                    a.NotFired = ex.Strings("notFired") ?? new List<string>();
                    a.Order = ex.Strings("order") ?? new List<string>();
                    foreach (var n in a.Fired) ctx.CheckEvent(n, ex.Where + ".fired");
                    foreach (var n in a.NotFired) ctx.CheckEvent(n, ex.Where + ".notFired");
                    foreach (var n in a.Order) ctx.CheckEvent(n, ex.Where + ".order");
                    a.LongestQuietMax = ex.Float("longestQuietMax", -1f);
                    a.ThreatsAvgMin = ex.Float("threatsAvgMin", -1f);
                    a.MaxEngagingMax = ex.Int("maxEngagingMax", -1);
                    a.MaxThreatsMax = ex.Int("maxThreatsMax", -1);
                    ex.Done();
                }
                ap.Done();
            }
            r.Done();
            errors.ThrowIfAny();
            return m;
        }

        static ShipDef ReadShip(Reader s, ParseContext ctx)
        {
            var d = new ShipDef { Name = s.Str("name"), Where = s.Where };
            d.Team = ctx.Team(s.Str("team", required: true), s.Where);
            ReadLoadout(s, d, ctx);
            d.Arrival = ReadArrival(s.Obj("arrival"), s.Where + ".arrival", ctx, single: true);
            d.Departure = ReadDeparture(s.Obj("departure"), s.Where + ".departure", ctx);
            s.Done();
            return d;
        }

        static WingDef ReadWing(Reader w, ParseContext ctx)
        {
            var d = new WingDef { Name = w.Str("name"), Where = w.Where };
            d.Team = ctx.Team(w.Str("team", required: true), w.Where);
            foreach (var g in w.Objects("ships"))
            {
                var gd = new GroupDef { Count = g.Int("count", 1) };
                if (gd.Count < 1) ctx.Errors.Add($"{g.Where}.count must be at least 1");
                ReadLoadout(g, gd, ctx);
                g.Done();
                d.Groups.Add(gd);
            }
            if (d.Groups.Count == 0) ctx.Errors.Add($"{w.Where}: a wing needs ships");
            d.Waves = w.Int("waves", 1);
            d.WaveThreshold = w.Int("waveThreshold", 0);
            d.WaveDelay = w.Float("waveDelay", 5f);
            d.Capped = w.Bool("capped", false);
            d.Wingmen = w.Bool("wingmen", false);
            d.Goal = ReadOrder(w.Obj("goal"), w.Where + ".goal", ctx);
            d.Fallback = ReadOrder(w.Obj("fallback"), w.Where + ".fallback", ctx);
            d.Arrival = ReadArrival(w.Obj("arrival"), w.Where + ".arrival", ctx, single: false);
            d.Departure = ReadDeparture(w.Obj("departure"), w.Where + ".departure", ctx);
            w.Done();
            return d;
        }

        static void ReadLoadout(Reader r, Loadout d, ParseContext ctx)
        {
            d.Class = r.Str("class", required: true);
            d.Ai = r.Str("ai");
            d.Role = r.Str("role");
            if (d.Role != null && d.Role != "fighter" && d.Role != "bomber") ctx.Errors.Add($"{r.Where}.role: '{d.Role}' (fighter or bomber)");
            var ammo = r.Obj("ammo");
            if (ammo != null) { d.Ammo = new Dictionary<string, int>(); foreach (var k in ammo.Keys()) d.Ammo[k] = ammo.Int(k, 0); ammo.Done(); }
            d.HullScale = r.Float("hullScale", 1f);
            d.Hull = r.Float("hull", 100f);
            if (d.Hull <= 0f || d.Hull > 100f) ctx.Errors.Add($"{r.Where}.hull: a percentage from 0 (exclusive) to 100");
            d.SelfHeal = r.Has("selfHeal") ? r.Bool("selfHeal", false) : (bool?)null;
            d.Invulnerable = r.Bool("invulnerable", false);
            d.Wingman = r.Bool("wingman", false);
            d.Goal = ReadOrder(r.Obj("goal"), r.Where + ".goal", ctx);
            d.Fallback = ReadOrder(r.Obj("fallback"), r.Where + ".fallback", ctx);
            var cap = r.Obj("capital");
            if (cap != null)
            {
                var c = new CapitalDef();
                c.Standoff = cap.Float("standoff", c.Standoff); c.Band = cap.Float("band", c.Band); c.Bearing = cap.Float("bearing", c.Bearing);
                c.Throttle = cap.Float("throttle", c.Throttle); c.TurretWaitScale = cap.Float("turretWaitScale", 1f); c.BeamWaitScale = cap.Float("beamWaitScale", 1f);
                c.TurretProfile = cap.Str("turretProfile", c.TurretProfile); c.HoldBeams = cap.Bool("holdBeams", false); c.Stationary = cap.Bool("stationary", false);
                var wp = cap.Obj("weapons");
                if (wp != null) { foreach (var k in wp.Keys()) c.Weapons[k] = wp.Str(k); wp.Done(); }
                cap.Done();
                d.Capital = c;
            }
            var look = r.Obj("look");
            if (look != null)
            {
                var h = look.Obj("hull");
                if (h != null)
                {
                    d.Look = new LookDef { HasHull = true, Length = h.Float("length", 400f), Width = h.Float("width", 100f), Height = h.Float("height", 80f), Seed = h.Int("seed", 1) };
                    h.Done();
                }
                look.Done();
            }
        }

        static ArrivalDef ReadArrival(Reader a, string where, ParseContext ctx, bool single)
        {
            var d = new ArrivalDef();
            if (a == null) return d;
            var cue = a.Raw("cue");
            if (cue != null && !(cue.Type == JTokenType.Boolean && (bool)cue)) d.Cue = Cond.Parse(cue, where + ".cue", ctx);
            d.Delay = a.Float("delay", 0f);
            d.At = a.Vec("at");
            d.Offset = a.Vec("offset");
            d.Rot = a.Vec("rot");
            d.Near = a.Str("near");
            if (d.Near != null) ctx.CheckShip(d.Near, where + ".near", false);
            d.Bay = a.Str("bay");
            if (d.Bay != null) ctx.CheckShip(d.Bay, where + ".bay", false);
            d.Distance = a.Float("distance", 0f);
            var facing = a.Raw("facing");
            if (facing != null)
            {
                if (facing.Type == JTokenType.String) { d.Facing = (string)facing; ctx.CheckShip(d.Facing, where + ".facing", false); }
                else d.FacingDir = a.Vec("facing");
            }
            d.Formation = a.Str("formation", "v");
            if (d.Formation != "v" && d.Formation != "line" && d.Formation != "column" && d.Formation != "none") ctx.Errors.Add($"{where}.formation: '{d.Formation}' (v, line, column or none)");
            d.Spacing = a.Vec("spacing") ?? d.Spacing;
            var offs = a.Raw("offsets");
            if (offs != null)
            {
                d.Offsets = new List<Vector3>();
                if (offs is JArray arr) foreach (var t in arr) { var v = Reader.ToVec(t); if (v.HasValue) d.Offsets.Add(v.Value); else ctx.Errors.Add($"{where}.offsets: each is [x, y, z]"); }
                else ctx.Errors.Add($"{where}.offsets: a list of [x, y, z]");
            }
            d.Speed = a.Float("speed", 0.5f);
            d.Throttle = a.Float("throttle", -1f);
            d.Effect = a.Str("effect", "jump");
            if (d.Effect != "jump" && d.Effect != "none") ctx.Errors.Add($"{where}.effect: '{d.Effect}' (jump or none)");
            int anchors = (d.At.HasValue ? 1 : 0) + (d.Near != null ? 1 : 0) + (d.Bay != null ? 1 : 0);
            if (anchors > 1) ctx.Errors.Add($"{where}: give one of at, near or bay");
            if (anchors == 0) ctx.Errors.Add($"{where}: needs a place (at, near or bay)");
            if (d.Offset.HasValue && d.Near == null && d.Bay == null) ctx.Errors.Add($"{where}.offset goes with near or bay");
            a.Done();
            return d;
        }

        static DepartureDef ReadDeparture(Reader a, string where, ParseContext ctx)
        {
            if (a == null) return null;
            var d = new DepartureDef();
            var cue = a.Raw("cue");
            d.Cue = cue != null ? Cond.Parse(cue, where + ".cue", ctx) : Cond.Never;
            d.Delay = a.Float("delay", 0f);
            d.To = a.Vec("to");
            d.Bay = a.Str("bay");
            if (d.Bay != null) ctx.CheckShip(d.Bay, where + ".bay", false);
            d.JumpAfter = a.Float("jumpAfter", 3f);
            a.Done();
            return d;
        }

        internal static OrderDef ReadOrder(Reader o, string where, ParseContext ctx)
        {
            if (o == null) return null;
            var d = new OrderDef { Where = where };
            string kind = o.Str("order", required: true);
            if (string.IsNullOrEmpty(kind) || Array.IndexOf(OrderNames, kind) < 0 || !Enum.TryParse(kind, true, out d.Kind))
            {
                ctx.Errors.Add($"{where}.order: '{kind}' is not an order ({string.Join(", ", OrderNames)})");
                o.Done();
                return null;
            }
            d.Target = o.Str("target");
            bool needsTarget = d.Kind == OrderKind.Attack || d.Kind == OrderKind.Disable || d.Kind == OrderKind.Disarm || d.Kind == OrderKind.Guard || d.Kind == OrderKind.Bomb || d.Kind == OrderKind.Form;
            if (needsTarget && d.Target == null) ctx.Errors.Add($"{where}: '{kind}' needs a target");
            if (d.Target != null) ctx.CheckShip(d.Target, where + ".target", true);
            var part = o.Raw("part");
            if (part != null)
            {
                if (part.Type == JTokenType.String) d.Parts = new List<string> { (string)part };
                else if (part is JArray pa) { d.Parts = new List<string>(); foreach (var t in pa) d.Parts.Add((string)t); }
                else ctx.Errors.Add($"{where}.part: a part name or a list of them (the first still standing is attacked)");
            }
            var path = o.Raw("path");
            if (path != null)
            {
                d.Path = new List<Vector3>();
                if (path is JArray arr) foreach (var t in arr) { var v = Reader.ToVec(t); if (v.HasValue) d.Path.Add(v.Value); else ctx.Errors.Add($"{where}.path: each point is [x, y, z]"); }
            }
            if (d.Kind == OrderKind.Waypoints && (d.Path == null || d.Path.Count == 0)) ctx.Errors.Add($"{where}: waypoints need a path");
            d.Speed = o.Float("speed", -1f);
            d.Then = o.Str("then", "stop");
            if (d.Then != "stop" && d.Then != "depart" && d.Then != "loop") ctx.Errors.Add($"{where}.then: '{d.Then}' (stop, depart or loop)");
            d.To = o.Vec("to");
            o.Done();
            return d;
        }

        public static readonly string[] OrderNames = { "attack", "disable", "disarm", "guard", "form", "engage", "bomb", "waypoints", "depart" };

        static ObjectiveDef ReadObjective(Reader o, ParseContext ctx)
        {
            var d = new ObjectiveDef { Id = o.Str("id"), Where = o.Where, Text = o.Str("text", required: true), Kind = o.Str("kind", "primary"), Hidden = o.Bool("hidden", false) };
            if (d.Kind != "primary" && d.Kind != "secondary" && d.Kind != "bonus") ctx.Errors.Add($"{o.Where}.kind: '{d.Kind}' (primary, secondary or bonus)");
            if (o.Has("complete")) d.Complete = Cond.Parse(o.Raw("complete"), o.Where + ".complete", ctx);
            if (o.Has("fail")) d.Fail = Cond.Parse(o.Raw("fail"), o.Where + ".fail", ctx);
            o.Done();
            return d;
        }

        static MessageDef ReadMessage(Reader o, ParseContext ctx)
        {
            var d = new MessageDef
            {
                Id = o.Str("id"), Where = o.Where, From = o.Str("from", required: true), FallbackFrom = o.Str("fallbackFrom"), Text = o.Str("text", required: true),
                Priority = o.Str("priority", "normal"), Voice = o.Str("voice"), Seconds = o.Float("seconds", 0f),
            };
            ctx.CheckSpeaker(d.From, o.Where + ".from");
            if (d.FallbackFrom != null) ctx.CheckSpeaker(d.FallbackFrom, o.Where + ".fallbackFrom");
            if (d.Priority != "high" && d.Priority != "normal" && d.Priority != "low") ctx.Errors.Add($"{o.Where}.priority: '{d.Priority}' (high, normal or low)");
            o.Done();
            return d;
        }

        static EventDef ReadEvent(Reader o, ParseContext ctx)
        {
            var d = new EventDef { Name = o.Str("name"), Where = o.Where };
            d.When = Cond.Parse(o.Raw("when") ?? true, o.Where + ".when", ctx);
            d.Delay = o.Float("delay", 0f);
            d.Repeat = o.Int("repeat", 1);
            if (d.Repeat == 0 || d.Repeat < -1) ctx.Errors.Add($"{o.Where}.repeat: a count of 1 or more, or -1 for ever");
            d.Interval = o.Float("interval", 1f);
            d.After = o.Str("after");
            if (d.After != null) ctx.CheckEvent(d.After, o.Where + ".after");
            d.AfterDelay = o.Float("afterDelay", 0f);
            d.Log = o.Bool("log", true);
            if (o.Has("failWhen")) d.FailWhen = Cond.Parse(o.Raw("failWhen"), o.Where + ".failWhen", ctx);
            var dir = o.Raw("directive");
            if (dir != null)
            {
                if (dir.Type == JTokenType.String) d.DirectiveText = (string)dir;
                else
                {
                    var dr = new Reader(dir, o.Where + ".directive", ctx.Errors);
                    d.DirectiveText = dr.Str("text", required: true);
                    if (dr.Has("show")) d.DirectiveShow = Cond.Parse(dr.Raw("show"), dr.Where + ".show", ctx);
                    dr.Done();
                }
            }
            var acts = o.Raw("do");
            if (acts is JArray arr) { int i = 0; foreach (var t in arr) { var a = Act.Parse(t, $"{o.Where}.do[{i++}]", ctx); if (a != null) d.Do.Add(a); } }
            else if (acts != null) ctx.Errors.Add($"{o.Where}.do: a list of actions");
            o.Done();
            return d;
        }

        // ------------------------------------------------------------------------------------------------ the reader

        internal sealed class Errors
        {
            readonly string _source;
            public readonly List<string> List = new List<string>();
            public Errors(string source) { _source = source; }
            public void Add(string e) { if (List.Count < 200) List.Add(e); }
            public void ThrowIfAny()
            {
                if (List.Count == 0) return;
                throw new InvalidDataException($"{_source}: {List.Count} problem(s):\n  " + string.Join("\n  ", List));
            }
        }

        /// <summary>Reads one JSON object, remembering which fields were read so that <see cref="Done"/> can report the misspelt ones.</summary>
        internal sealed class Reader
        {
            readonly JObject _o;
            readonly HashSet<string> _used = new HashSet<string>();
            readonly Errors _errors;
            public readonly string Where;

            public Reader(JToken t, string where, Errors errors)
            {
                Where = where; _errors = errors;
                _o = t as JObject;
                if (_o == null) { errors.Add($"{where}: must be an object"); _o = new JObject(); }
            }

            public bool Has(string k) => _o[k] != null;
            public JToken Raw(string k) { _used.Add(k); return _o[k]; }
            public IEnumerable<string> Keys() { foreach (var p in _o.Properties()) { _used.Add(p.Name); yield return p.Name; } }

            public string Str(string k, string def = null, bool required = false)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null || t.Type == JTokenType.Null) { if (required) _errors.Add($"{Where}: '{k}' is missing"); return def; }
                if (t.Type != JTokenType.String) { _errors.Add($"{Where}.{k}: must be text"); return def; }
                return (string)t;
            }

            public float Float(string k, float def)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) return def;
                if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) { _errors.Add($"{Where}.{k}: must be a number"); return def; }
                return t.ToObject<float>();
            }

            public int Int(string k, int def)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) return def;
                if (t.Type != JTokenType.Integer) { _errors.Add($"{Where}.{k}: must be a whole number"); return def; }
                return t.ToObject<int>();
            }

            public bool Bool(string k, bool def)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) return def;
                if (t.Type != JTokenType.Boolean) { _errors.Add($"{Where}.{k}: must be true or false"); return def; }
                return (bool)t;
            }

            public Vector3? Vec(string k)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) return null;
                var v = ToVec(t);
                if (!v.HasValue) _errors.Add($"{Where}.{k}: must be [x, y, z]");
                return v;
            }

            public static Vector3? ToVec(JToken t)
            {
                if (!(t is JArray a) || a.Count != 3) return null;
                foreach (var e in a) if (e.Type != JTokenType.Float && e.Type != JTokenType.Integer) return null;
                return new Vector3(a[0].ToObject<float>(), a[1].ToObject<float>(), a[2].ToObject<float>());
            }

            /// <summary>A number, or one per difficulty level (Very easy to Insane).</summary>
            public float[] PerLevel(string k)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) { _errors.Add($"{Where}: '{k}' is missing"); return new[] { 0f }; }
                if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return new[] { t.ToObject<float>() };
                if (t is JArray a && a.Count > 0) { var r = new float[a.Count]; for (int i = 0; i < a.Count; i++) r[i] = a[i].ToObject<float>(); return r; }
                _errors.Add($"{Where}.{k}: a number or a list of one per difficulty level");
                return new[] { 0f };
            }

            public List<string> Strings(string k)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) return null;
                var list = new List<string>();
                if (t is JArray a) { foreach (var e in a) { if (e.Type == JTokenType.String) list.Add((string)e); else _errors.Add($"{Where}.{k}: a list of text"); } }
                else _errors.Add($"{Where}.{k}: a list of text");
                return list;
            }

            public Reader Obj(string k)
            {
                _used.Add(k);
                var t = _o[k];
                return t == null ? null : new Reader(t, Where + "." + k, _errors);
            }

            public IEnumerable<Reader> Objects(string k)
            {
                _used.Add(k);
                var t = _o[k];
                if (t == null) yield break;
                if (!(t is JArray a)) { _errors.Add($"{Where}.{k}: must be a list"); yield break; }
                for (int i = 0; i < a.Count; i++)
                {
                    string label = (string)a[i]["name"] ?? (string)a[i]["id"];
                    yield return new Reader(a[i], $"{k}[{i}]" + (label != null ? $" '{label}'" : ""), _errors);
                }
            }

            /// <summary>Report every field nobody read (a misspelling), except comments: "about", "note" and anything ending in "Why".</summary>
            public void Done()
            {
                foreach (var p in _o.Properties())
                    if (!_used.Contains(p.Name) && p.Name != "about" && p.Name != "note" && !p.Name.EndsWith("Why", StringComparison.Ordinal) && !p.Name.StartsWith("_", StringComparison.Ordinal))
                        _errors.Add($"{Where}: unknown field '{p.Name}'");
            }
        }

        /// <summary>What the parser knows while it reads: every name in the file, for checking references.</summary>
        internal sealed class ParseContext
        {
            public readonly MissionFile File;
            public readonly Errors Errors;
            public readonly Dictionary<string, string> Ships = new Dictionary<string, string>();
            /// <summary>Wing name to the number of ships it will ever have (all waves), so "Choir 2" can be checked.</summary>
            public readonly Dictionary<string, int> Wings = new Dictionary<string, int>();
            public readonly Dictionary<string, string> Objectives = new Dictionary<string, string>(), Messages = new Dictionary<string, string>(), EventNames = new Dictionary<string, string>();

            public ParseContext(MissionFile file, Errors errors) { File = file; Errors = errors; }

            public void Declare(Dictionary<string, string> set, string name, string what, string where)
            {
                if (name == null) return;
                if (set.ContainsKey(name)) Errors.Add($"{where}: a second {what} called '{name}'");
                set[name] = where;
            }

            public void DeclareShip(string name, string where)
            {
                if (name == null) return;
                if (Ships.ContainsKey(name) || Wings.ContainsKey(name)) Errors.Add($"{where}: the name '{name}' is used twice");
                if (name.StartsWith("<", StringComparison.Ordinal) || name.StartsWith("#", StringComparison.Ordinal)) Errors.Add($"{where}: names may not start with < or #");
                Ships[name] = where;
            }

            public void DeclareWing(string name, string where, Reader w)
            {
                if (name == null) return;
                if (Ships.ContainsKey(name) || Wings.ContainsKey(name)) Errors.Add($"{where}: the name '{name}' is used twice");
                int perWave = 0;
                var ships = w.Raw("ships") as JArray;
                if (ships != null) foreach (var g in ships) perWave += g["count"] != null && g["count"].Type == JTokenType.Integer ? (int)g["count"] : 1;
                int waves = w.Raw("waves") is JToken wt && wt.Type == JTokenType.Integer ? (int)wt : 1;
                Wings[name] = Mathf.Max(1, perWave) * Mathf.Max(1, waves);
            }

            public int Team(string name, string where)
            {
                if (name == null) return 0;
                int i = File.Teams.IndexOf(name);
                if (i < 0) { Errors.Add($"{where}.team: '{name}' is not one of the teams ({string.Join(", ", File.Teams)})"); return 0; }
                return i;
            }

            public bool IsWing(string name) => name != null && Wings.ContainsKey(name);

            /// <summary>A ship by name, "&lt;player&gt;", a wing (when allowed) or a wing's member ("Choir 2").</summary>
            public void CheckShip(string name, string where, bool wingOk)
            {
                if (name == null) { Errors.Add($"{where}: a ship name is missing"); return; }
                if (name == Player || Ships.ContainsKey(name)) return;
                if (Wings.ContainsKey(name)) { if (!wingOk) Errors.Add($"{where}: '{name}' is a wing; a single ship is needed here"); return; }
                if (IsWingMember(name)) return;
                Errors.Add($"{where}: no ship or wing called '{name}'");
            }

            public bool IsWingMember(string name)
            {
                int sp = name.LastIndexOf(' ');
                if (sp <= 0) return false;
                return Wings.TryGetValue(name.Substring(0, sp), out int total)
                       && int.TryParse(name.Substring(sp + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) && k >= 1 && k <= total;
            }

            public void CheckSpeaker(string name, string where)
            {
                if (name == null) return;
                if (name.StartsWith("#", StringComparison.Ordinal) || name == AnyWingman) return;
                CheckShip(name, where, false);
            }

            public void CheckEvent(string name, string where) { if (name == null || !EventNames.ContainsKey(name)) Errors.Add($"{where}: no event called '{name}'"); }
            public void CheckObjective(string id, string where) { if (id == null || !Objectives.ContainsKey(id)) Errors.Add($"{where}: no objective '{id}'"); }
            public void CheckMessage(string id, string where) { if (id == null || !Messages.ContainsKey(id)) Errors.Add($"{where}: no message '{id}'"); }
        }
    }

    public enum OrderKind { Attack, Disable, Disarm, Guard, Form, Engage, Bomb, Waypoints, Depart }

    /// <summary>
    /// Finds mission files: a path to a file; else <c>Missions/&lt;name&gt;.mission.json</c> next to the game (the playtest folder's
    /// editable copies); else the build's own <c>Resources/Missions/&lt;name&gt;.mission.json</c>.
    /// </summary>
    public static class MissionLibrary
    {
        public const string ResourceFolder = "Missions";
        public const string Extension = ".mission.json";

        public static string NameOf(string nameOrPath)
        {
            string n = Path.GetFileName(nameOrPath ?? "");
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
            // A TextAsset keeps the inner extension in its name: "dogfight.mission".
            if (n.EndsWith(".mission", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 8);
            return n;
        }

        /// <summary>The text of a mission and where it came from.</summary>
        public static string Read(string nameOrPath, out string source)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath)) throw new FileNotFoundException("no mission named");
            if (File.Exists(nameOrPath)) { source = Path.GetFullPath(nameOrPath); return File.ReadAllText(nameOrPath); }
            string name = NameOf(nameOrPath);
            try
            {
                string beside = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", ResourceFolder, name + Extension);
                if (File.Exists(beside)) { source = beside; return File.ReadAllText(beside); }
            }
            catch (Exception) { /* no folder beside the game */ }
            foreach (var t in Resources.LoadAll<TextAsset>(ResourceFolder))
                if (string.Equals(NameOf(t.name), name, StringComparison.OrdinalIgnoreCase)) { source = $"Resources/{ResourceFolder}/{t.name}.json"; return t.text; }
            throw new FileNotFoundException($"no mission '{nameOrPath}' (a file, Missions/{name}{Extension} beside the game, or Resources/{ResourceFolder})");
        }

        public static MissionFile Load(string nameOrPath)
        {
            string text = Read(nameOrPath, out string source);
            return MissionFile.Load(text, source);
        }

        /// <summary>Every mission the build carries (names).</summary>
        public static List<string> BuiltIn()
        {
            var list = new List<string>();
            foreach (var t in Resources.LoadAll<TextAsset>(ResourceFolder)) list.Add(NameOf(t.name));
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
