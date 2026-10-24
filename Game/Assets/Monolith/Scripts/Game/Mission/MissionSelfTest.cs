using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Monolith.AI;
using Monolith.Core;
using Monolith.Mission;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the mission system on the real sim: <c>Monolith.exe -batchmode -nographics -mnMissionSelfTest</c>. Each line logs
    /// <c>MISSION PASS</c> or <c>MISSION FAIL</c>. The file format (every built-in mission loads; a broken one is refused with every problem
    /// named), every condition and action on a scripted world (Resources/MissionTests/selftest.mission.json, where the test kills ships and
    /// parts at set times and checks when each event fires), each built-in mission running headless for a minute, and the dogfight's port:
    /// the hand-written dogfight and dogfight.mission.json compared to the bit for four minutes of play.
    /// </summary>
    public static class MissionSelfTest
    {
        const string Flag = "-mnMissionSelfTest";
        static int _checks, _fails;
        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            MnLog.Info($"MISSION {(ok ? "PASS" : "FAIL")}: {what}");
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static bool Run()
        {
            _checks = 0; _fails = 0;
            var tests = new (string, Action)[] { ("format", Format), ("scripted", Scripted), ("queue and beam-fire", QueueAndBeamFire), ("missions", BuiltIn), ("voices", Voices), ("port", Port) };
            foreach (var (name, t) in tests)
            {
                try { t(); }
                catch (Exception e) { _checks++; _fails++; MnLog.Error($"MISSION FAIL: {name} threw {e}"); }
            }
            MnLog.Info($"MISSION SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass");
            return _fails == 0;
        }

        static string TestMission(string name)
        {
            foreach (var t in Resources.LoadAll<TextAsset>("MissionTests")) if (MissionLibrary.NameOf(t.name) == name) return t.text;
            throw new FileNotFoundException($"Resources/MissionTests/{name}.mission.json is missing");
        }

        // ------------------------------------------------------------------------------------------------ the format

        static void Format()
        {
            var names = MissionLibrary.BuiltIn();
            Check(names.Contains("dogfight"), $"the build carries its missions ({string.Join(", ", names)})");
            foreach (var n in names)
            {
                try { var m = MissionLibrary.Load(n); Check(true, $"'{n}' loads: {m.Ships.Count} ships, {m.Wings.Count} wings, {m.Events.Count} events, {m.Objectives.Count} objectives, {m.Messages.Count} messages"); }
                catch (Exception e) { Check(false, $"'{n}' loads: {e.Message}"); }
            }
            var self = MissionFile.Load(TestMission("selftest"), "selftest");
            Check(self.Events.Count > 30 && self.Briefing.Questions.Count == 1 && self.ArrivalCap != null, "the self-test mission loads (events, briefing questions, arrival cap)");

            const string broken = @"{ ""format"": ""monolith-mission/1"", ""id"": ""broken"", ""name"": ""Broken"", ""player"": ""Nobody"",
              ""ships"": [ { ""name"": ""A"", ""class"": ""vanguard_fighter"", ""team"": ""diaspora"", ""hulscale"": 2, ""arrival"": { ""at"": [0, 0, 0] } } ],
              ""events"": [ { ""name"": ""e1"", ""when"": { ""destroyed"": ""Ghost"" }, ""do"": [ { ""do"": ""explode"" }, { ""do"": ""order"", ""ship"": ""A"", ""order"": ""dance"" } ] },
                           { ""name"": ""e2"", ""when"": { ""blah"": 1 } }, { ""name"": ""e1"", ""after"": ""nope"" } ] }";
            string msg = null;
            try { MissionFile.Load(broken, "broken"); }
            catch (InvalidDataException e) { msg = e.Message; }
            bool all = msg != null && msg.Contains("player 'Nobody'") && msg.Contains("unknown field 'hulscale'") && msg.Contains("'Ghost'") && msg.Contains("unknown action 'explode'")
                       && msg.Contains("'dance' is not an order") && msg.Contains("no condition operator") && msg.Contains("a second event called 'e1'") && msg.Contains("no event called 'nope'");
            Check(all, "a broken mission is refused with every problem named: " + (msg ?? "(it loaded)").Replace("\n", " "));

            // the theme action (the music's second layer): a good one loads, a bad one is named
            const string themed = @"{ ""format"": ""monolith-mission/1"", ""id"": ""themed"", ""name"": ""Themed"", ""player"": ""A"",
              ""ships"": [ { ""name"": ""A"", ""class"": ""vanguard_fighter"", ""team"": ""diaspora"", ""arrival"": { ""at"": [0, 0, 0] } } ],
              ""events"": [ { ""name"": ""on"", ""when"": { ""time"": 1 }, ""do"": [ { ""do"": ""theme"", ""name"": ""lector"", ""fadeIn"": 3, ""priority"": 12, ""mode"": ""replace"" } ] },
                           { ""name"": ""off"", ""when"": { ""time"": 9 }, ""do"": [ { ""do"": ""theme"", ""name"": ""lector"", ""on"": false, ""fadeOut"": 5 } ] } ] }";
            bool themeOk = true; string themeMsg = null;
            try { MissionFile.Load(themed, "themed"); } catch (InvalidDataException e) { themeOk = false; themeMsg = e.Message; }
            Check(themeOk, "a mission with theme actions loads" + (themeOk ? "" : ": " + themeMsg));
            const string badTheme = @"{ ""format"": ""monolith-mission/1"", ""id"": ""badtheme"", ""name"": ""Bad"", ""player"": ""A"",
              ""ships"": [ { ""name"": ""A"", ""class"": ""vanguard_fighter"", ""team"": ""diaspora"", ""arrival"": { ""at"": [0, 0, 0] } } ],
              ""events"": [ { ""name"": ""e1"", ""when"": { ""time"": 1 }, ""do"": [ { ""do"": ""theme"" }, { ""do"": ""theme"", ""name"": ""has space"" }, { ""do"": ""theme"", ""name"": ""x"", ""mode"": ""loud"" }, { ""do"": ""theme"", ""name"": ""y"", ""fadeIn"": -1 } ] } ] }";
            string badMsg = null;
            try { MissionFile.Load(badTheme, "badtheme"); } catch (InvalidDataException e) { badMsg = e.Message; }
            Check(badMsg != null && badMsg.Contains("name") && badMsg.Contains("has space") && badMsg.Contains("'loud'") && badMsg.Contains("not negative"), "bad theme actions are named: " + (badMsg ?? "(it loaded)").Replace("\n", " "));
            var round = ThemeEvent.Parse(new ThemeEvent { Mission = "oort", Name = "destroyer", FadeIn = 3f, Priority = 20f, Mode = "replace" }.Format(), 1f);
            Check(round != null && round.Key == "oort/destroyer" && round.On && round.FadeIn == 3f && float.IsNaN(round.FadeOut) && round.Priority == 20f && round.Mode == "replace", "a theme's settings reach the audio intact");
        }

        // ------------------------------------------------------------------------------------------------ every condition and action

        static void Scripted()
        {
            var file = MissionFile.Load(TestMission("selftest"), "selftest");
            var radio = new List<string>();
            int jumpIns = 0, jumpOuts = 0, ends = 0, voices = 0, objectiveEvents = 0, firedEvents = 0;
            var music = new List<string>();
            var cabin = new List<string>();
            using (var lab = new AiLab(2))
            {
                var subs = new List<IDisposable>
                {
                    GameEvents.Subscribe(CombatEvents.RadioLine, e => radio.Add(e.Subject)),
                    GameEvents.Subscribe(MissionEvents.JumpIn, e => jumpIns++),
                    GameEvents.Subscribe(MissionEvents.JumpOut, e => jumpOuts++),
                    GameEvents.Subscribe(MissionEvents.MissionEnd, e => ends++),
                    GameEvents.Subscribe(MissionEvents.RadioVoice, e => voices++),
                    GameEvents.Subscribe(MissionEvents.ObjectiveChanged, e => objectiveEvents++),
                    GameEvents.Subscribe(MissionEvents.EventFired, e => firedEvents++),
                    GameEvents.Subscribe(MissionEvents.MusicState, e => music.Add(e.Subject)),
                    GameEvents.Subscribe(MissionEvents.CabinScan, e => cabin.Add("scan")),
                    GameEvents.Subscribe(MissionEvents.CabinKnockOut, e => cabin.Add("knock " + e.Subject)),
                    GameEvents.Subscribe(MissionEvents.CabinLights, e => cabin.Add("lights " + e.Subject)),
                    GameEvents.Subscribe(MissionEvents.CabinCallOut, e => cabin.Add("call " + e.Subject)),
                };
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai);
                var stub = new CabinStub { AutoRepairSeconds = 3f };
                var rt = new MissionRuntime(lab.W, file, host, stub);
                lab.W.Sim.Register(rt);
                rt.Start();

                int Id(string n) => rt.IdOf(n);
                void Kill(string n)
                {
                    var c = lab.W.Combat.Get(Id(n));
                    if (c == null) return;
                    c.Invulnerable = false;
                    lab.W.Damage.Add(new DamageRequest { Ship = c.Id, Attacker = GameWorld.NoShip, Weapon = "test", Amount = c.HullMax * 10f + 1e6f, HullMult = 1f, BypassShield = true, Kind = DamageKind.Debug, Part = -2, Point = c.Ship.Position });
                }
                float At(string ev) { var e = rt.EventNamed(ev); return e != null && e.FirstFiredAt >= 0f ? e.FirstFiredAt - rt.StartTime : -1f; }
                bool Near(float a, float b) => Mathf.Abs(a - b) < 0.05f;

                // At the start.
                var p = lab.W.Ships.Get(Id("Sable 1"));
                var s2 = lab.W.Ships.Get(Id("Sable 2"));
                Check(p != null && lab.W.LocalPlayerShip == p.Id && s2 != null && Vector3.Distance(s2.Position, p.Position + p.Rotation * new Vector3(-30f, 5f, -30f)) < 0.01f,
                      "ships present at the start spawn in place: the player, and a wingman near it at an offset in its frame");
                var wing = lab.Ai.PilotOf(s2.Id);
                Check(wing != null && wing.Goal.Kind == GoalKind.Engage && wing.DefaultGoal.Kind == GoalKind.Guard && wing.DefaultGoal.Target == p.Id && System.Linq.Enumerable.Contains(lab.Ai.PlayerWing.Members, wing),
                      "a ship's goal and fallback from the file: Sable 2 engages, goes back to guarding the player, and takes the quick menu's orders");
                Check(lab.W.Mission == rt.Board && rt.Board.Objectives.Count == 3 && rt.Board.Objectives[2].Hidden, "the board is published with the objectives (the bonus hidden)");
                Check(rt.Board.Music == "ambient", "the music starts in its start state");

                int Tick(float t) => Mathf.RoundToInt(t * Sim.Hz);
                var actions = new SortedDictionary<int, Action>
                {
                    [Tick(6f)] = () =>
                    {
                        Check(rt.RecOf("Choir 1").Present && rt.RecOf("Choir 2").Present && !rt.RecOf("Choir 3").Present && rt.QueuedCount == 1,
                              $"the arrival cap (2 on Medium) holds the third of Choir back ({rt.QueuedCount} waiting)");
                        var d = rt.Board.Directives;
                        Check(d.Count == 1 && d[0].Text == "Destroy Choir" && d[0].State == MissionBoard.State.Active, "the directive shows once its wing is in (\"Destroy Choir\", active)");
                    },
                    [Tick(8f)] = () => Kill("Choir 1"),
                    [Tick(8f) + 1] = () => Check(rt.RecOf("Choir 3").Present && Near(rt.RecOf("Choir 3").ArrivedAt, 8f), $"a death makes room: Choir 3 arrives on the same tick (t={F(rt.RecOf("Choir 3").ArrivedAt)})"),
                    [Tick(10f)] = () => Kill("Choir 2"),
                    [Tick(11f)] = () =>
                    {
                        var c = lab.W.Combat.Get(Id("Mercy"));
                        lab.W.Damage.Add(new DamageRequest { Ship = c.Id, Attacker = GameWorld.NoShip, Weapon = "test", Amount = c.HullMax * 0.6f + 1f, HullMult = 1f, BypassShield = true, Kind = DamageKind.Debug, Part = -2, Point = c.Ship.Position });
                    },
                    [Tick(12f)] = () => { var c = lab.W.Combat.Get(Id("Warden")); host.DestroyPart(c.Id, c.FindPart("SUB_aaf_F01").Index); host.DestroyPart(c.Id, c.FindPart("SUB_aaf_F03").Index); },
                    [Tick(13f)] = () => { var c = lab.W.Combat.Get(Id("Warden")); foreach (var q in c.Parts) if (q.Kind == "engine") host.DestroyPart(c.Id, q.Index); },
                    [Tick(15f)] = () => Kill("Choir 3"),
                    [Tick(15.5f)] = () =>
                    {
                        var d = rt.Board.Directives;
                        Check(d.Count == 1 && d[0].State == MissionBoard.State.Done, "the directive shows as done once its event fires");
                    },
                    [Tick(17.5f)] = () =>
                    {
                        var pc = lab.W.Combat.Get(Id("Sable 1"));
                        Check(pc.FindPart("engine_port").Destroyed && stub.NeedsRepair(pc.Id, null), "knockOut destroys the player's engine and it needs a hand repair (cabin stub)");
                        Check(rt.Board.Prompt == "Leave the seat" && stub.Lights == "emergency", "callOut sets the prompt on the board, cabinLights the lighting (cabin stub)");
                    },
                    [Tick(20.5f)] = () =>
                    {
                        var pc = lab.W.Combat.Get(Id("Sable 1"));
                        Check(!pc.FindPart("engine_port").Destroyed && !stub.NeedsRepair(pc.Id, null), "the stub's hand repair gives the part back");
                        var r1 = lab.W.Ships.Get(Id("Relay 1")); var wd = lab.W.Ships.Get(Id("Warden"));
                        Check(r1 != null && Mathf.Abs(Vector3.Distance(r1.Position, wd.Position) - 900f) < 120f, $"a wing arrives near a ship at a distance (Relay 1 {F(Vector3.Distance(r1.Position, wd.Position))} m from the Warden; asked 900)");
                    },
                    [Tick(21.5f)] = () =>
                    {
                        var r1 = lab.Ai.PilotOf(Id("Relay 1")); var r2 = lab.Ai.PilotOf(Id("Relay 2"));
                        bool guard = r1 != null && r2 != null && r1.Goal.Kind == GoalKind.Guard && r1.Goal.Target == Id("Warden") && r1.DefaultGoal.Kind == GoalKind.Guard && r2.Goal.Kind == GoalKind.Guard;
                        Check(guard, "an order to a wing reaches every ship in it, standing (goal and fallback)");
                        var w2 = lab.Ai.PilotOf(Id("Sable 2"));
                        Check(w2.Goal.Kind == GoalKind.Form && w2.Goal.Target == Id("Sable 1") && w2.DefaultGoal.Kind == GoalKind.Guard, "an order that is not standing leaves the fallback alone (Sable 2 forms up, still falls back to guarding)");
                    },
                    [Tick(22f)] = () => { Kill("Relay 1"); Kill("Relay 2"); },
                    [Tick(26f)] = () =>
                    {
                        var mc = lab.W.Combat.Get(Id("Mercy"));
                        Check(mc.Invulnerable, "invulnerable turns a ship's protection on");
                        bool free = false; foreach (var cp in lab.Ai.CapitalPilots) if (cp.Ship == mc.Id) free = !cp.Orders.HoldBeams;
                        Check(free, "beams frees the Mercy's beams (FS2's beam-free)");
                    },
                    [Tick(28f)] = () => GameEvents.RaiseAt(CombatEvents.BeamCharge, lab.W.Ships.Get(Id("Warden")).Position, Vector3.forward, "beam_slash", 3f, Id("Warden"), Id("Mercy")),
                    [Tick(1f)] = () =>
                    {
                        bool held = false; foreach (var cp in lab.Ai.CapitalPilots) if (cp.Ship == Id("Warden")) held = cp.Orders.HoldTurrets;
                        Check(held, "a capital can start with its turrets held (holdTurrets)");
                        Check(file.Scenery.Rocks.Count == 1 && file.Scenery.Fields.Count == 1 && file.Scenery.Fields[0].Clear.Count == 1, "scenery is read: single rocks and fields (drawn only, by the scene)");
                    },
                    [Tick(30.5f)] = () =>
                    {
                        bool held = true; foreach (var cp in lab.Ai.CapitalPilots) if (cp.Ship == Id("Warden")) held = cp.Orders.HoldTurrets;
                        Check(!held, "turrets: the Warden's guns wake up on the mission's word");
                    },
                    [Tick(31.5f)] = () =>
                    {
                        var ar = lab.W.Ships.Get(Id("Archon")); var ac = lab.W.Combat.Get(Id("Archon"));
                        var hg = ac.FirstPart("hangar"); var l1 = lab.W.Ships.Get(Id("Launch 1"));
                        float d = l1 != null ? Vector3.Distance(l1.Position, hg.WorldPosition(ar)) : -1f;
                        Check(l1 != null && rt.RecOf("Launch 2").Present && d > 0f && d < 120f, $"a bay arrival comes out of the ship's hangar part (Launch 1 {F(d)} m from the Archon's hangar)");
                        host.DestroyPart(ac.Id, hg.Index);
                    },
                    [Tick(33.5f)] = () =>
                    {
                        var w2 = lab.Ai.PilotOf(Id("Sable 2")); var wc = lab.W.Combat.Get(Id("Warden"));
                        Check(w2.Goal.Kind == GoalKind.Attack && w2.Goal.Target == wc.Id && w2.Goal.Part == wc.FirstPart("flak").Index, $"an order by part kind attacks the target's first living part of that kind (part {w2.Goal.Part}, {wc.Parts[w2.Goal.Part].Name})");
                    },
                };

                int steps = Tick(50f);
                for (int i = 0; i < steps; i++)
                {
                    if (actions.TryGetValue(i, out var a)) a();
                    lab.W.Sim.StepOnce();
                }

                // When each event fired.
                Check(Near(At("t5"), 5f), $"time: fires at 5 s ({F(At("t5"))})");
                Check(Near(At("chained"), 6f), $"chained after another event with a delay: 6 s ({F(At("chained"))})");
                var ticker = rt.EventNamed("ticker");
                Check(ticker.Fired == 3 && Near(ticker.FirstFiredAt, 0f) && Near(ticker.LastFiredAt, 4f) && ticker.State == MissionRuntime.EventState.Done, $"repeat 3, interval 2: fired {ticker.Fired} times, 0 to {F(ticker.LastFiredAt)} s");
                Check(Near(At("logic"), 2f), $"and, or, not: 2 s ({F(At("logic"))})");
                var failMe = rt.EventNamed("fail me");
                Check(failMe.State == MissionRuntime.EventState.Failed && Near(failMe.FailedAt, 3f) && failMe.Fired == 0, "failWhen: the event fails at 3 s and never fires");
                Check(Near(At("event delay"), 7f), $"another event true with a delay: 7 s ({F(At("event delay"))})");
                Check(Near(At("in range"), 0f) && At("far") < 0f, "range: the player within 3 km of the Mercy at once; never 100 km from the start");
                Check(Near(rt.RecOf("Late").ArrivedAt, 9f), $"an arrival cue with a delay: Late arrives 1 s after Choir 1 dies ({F(rt.RecOf("Late").ArrivedAt)})");
                Check(Near(At("two thirds"), 12f), $"wing percent destroyed (66%) plus an event delay of 2 s: 12 s ({F(At("two thirds"))})");
                Check(Near(At("mercy hurt"), 11f), $"hull below 50%: 11 s ({F(At("mercy hurt"))})");
                Check(Near(At("beams down"), 12f), $"subsystems destroyed (the Warden's two beam turrets): 12 s ({F(At("beams down"))})");
                Check(Near(At("warden disabled"), 14f), $"disabled (every engine gone) with a delay of 1 s: 14 s ({F(At("warden disabled"))})");
                Check(Near(At("objective done"), 15f) && rt.ObjectiveState("choir") == MissionBoard.State.Done, $"an objective completes on its condition and another event reads it: 15 s ({F(At("objective done"))})");
                Check(Near(At("kill choir"), 15f) && Near(At("choir 3 dead"), 18f), "wing destroyed at 15 s; a wing member destroyed with a delay of 3 s at 18 s");
                Check(Near(At("spotted"), 18f) && rt.ObjectiveState("spotted") == MissionBoard.State.Done && !rt.Board.Objectives[2].Hidden, "a scan sweep detects the powered player when it ends (cabin stub); the hidden bonus shows once met");
                var darkStub = new CabinStub { GoesDark = true };
                darkStub.StartScan(1, 5f, 1000f, 0f);
                Check(!darkStub.Powered, "a stub that goes dark (auto-play's -mnGoDark) has its power off while a sweep runs");
                Check(Near(At("needs repair"), 17f) && Near(At("repaired"), 20f), $"needs a hand repair at 17 s, repaired at 20 s ({F(At("needs repair"))}, {F(At("repaired"))})");
                Check(Near(At("cabin flags"), 0f), "seated and powered (the stub's defaults)");
                bool right = rt.Log.Entries.Exists(e => e.Text == "right branch"), wrong = rt.Log.Entries.Exists(e => e.Text == "wrong branch" || e.Text == "else branch");
                Check(right && !wrong, "cond runs the first case that holds");
                Check(Near(At("relay 2 in"), 24f) && rt.RecOf("Relay 4").Present, $"wing waves: the second wave comes 2 s after the first is gone, as Relay 3 and 4 ({F(At("relay 2 in"))})");
                Check(Near(At("late gone"), 27f) && lab.W.Ships.Get(Id("Late")) == null, $"depart: flies out, jumps after 2 s, leaves the world ({F(At("late gone"))})");
                Check(Near(At("reserve gone"), 36f) && Id("Reserve 1") == GameWorld.NoShip && Id("Reserve 2") == GameWorld.NoShip && rt.Log.Entries.Exists(e => e.Kind == "stood down") && !rt.HasArrived("Reserve 1") && At("reserve here") < 0f,
                      $"a wing ordered out before it arrives never comes: it reads as departed, never as arrived ({F(At("reserve gone"))})");
                Check(Near(At("launch2 gone"), 34f) && Id("Launch2 1") == GameWorld.NoShip && rt.Log.Entries.Exists(e => e.Kind == "scrubbed"), $"a ship whose hangar is destroyed launches nothing: the wing is scrubbed and counts as destroyed ({F(At("launch2 gone"))})");
                Check(At("departed hull") < 0f, "a ship that jumped out reads as neither hurt nor destroyed (hull and subsystem conditions stay false)");
                Check(At("runner gone") > 25f && lab.W.Ships.Get(Id("Runner")) == null, $"waypoints then depart: the Runner flies its leg and jumps ({F(At("runner gone"))} s)");
                Check(Near(At("earth count"), 5f), $"teamAlive: Earth reaches 4 ships (the Warden, the Archon, two of Choir) when Choir comes in ({F(At("earth count"))})");
                Check(Near(At("hostiles near"), 0f) && Near(At("easy or harder"), 0f) && Near(At("no autopilot"), 0f), "hostilesNear, difficultyAtLeast and autopilot read the world, the level and the pilot");
                Check(Near(At("warden beaming"), 28f), $"beaming: the Warden charging a beam ({F(At("warden beaming"))})");
                Check(rt.Ended && rt.Result == "victory" && rt.ResultText == "TEST OVER" && Near(rt.EndedAt, 45f) && ends == 1 && At("after the end") < 0f, "end: victory at 45 s, mission_end raised once, nothing fires after it");
                Check(jumpIns == 8 && jumpOuts == 2, $"arrival effects: {jumpIns} jump_in (Choir 3, Late, Relay 4 and the start ships none), {jumpOuts} jump_out");
                int b = radio.FindIndex(l => l.Contains("HIGH ONE")), a2 = radio.FindIndex(l => l.Contains("NORMAL ONE"));
                Check(b >= 0 && a2 > b && !radio.Exists(l => l.Contains("LOW ONE")) && voices == 1, $"radio: high priority first, low dropped on a busy channel, the voice slot raised ({string.Join(" / ", radio)})");
                Check(radio.Exists(l => l == "EARTH: FROM A DEAD SHIP."), "radio: a dead ship's line goes out from its fallback speaker");
                Check(music.Count >= 3 && music[0] == "ambient" && music[1] == "battle2" && music.Contains("victory"), $"music: the start state, the forced battle2 and the victory at the end go out ({string.Join(", ", music)})");
                Check(cabin.Contains("scan") && cabin.Contains("knock engine_port") && cabin.Contains("lights emergency") && cabin.Contains("call Leave the seat"), $"cabin actions raise their events ({string.Join(", ", cabin)})");
                Check(objectiveEvents == 2 && firedEvents > 30, $"objective_changed ({objectiveEvents}) and mission_event ({firedEvents}) raised");
                var deb = rt.Debrief();
                Check(deb.Exists(l => l == "Choir went down.") && !deb.Exists(l => l == "Never shown.") && deb.Exists(l => l.StartsWith("KILLS")), "the debrief lists the objectives, kills and losses, and the stages whose conditions hold");
                Check(rt.Log.Entries.Count > 40 && rt.Log.EventsCsv().Split('\n').Length > 40, $"the timeline logs arrivals, deaths, events, objectives, radio and music ({rt.Log.Entries.Count} entries)");
                Check(lab.Errors == 0, $"no errors logged ({lab.Errors})");
                foreach (var s in subs) s.Dispose();
                rt.Dispose(); host.Dispose();
            }
        }

        // ------------------------------------------------------------------------------------------------ the cap's queue, beam-fire, per-level expectations (oort round 2)

        static void QueueAndBeamFire()
        {
            // A cap of one Earth fighter: wing A (two, at the start) fills it and queues its second; B (oldest first) queues at 1 s; C, with a higher
            // capPriority, queues at 2 s and goes ahead of both. The Mercy's beams are freed at 1 s with a target: the far corvette, not the near one.
            const string json = @"{ ""format"": ""monolith-mission/1"", ""id"": ""queue"", ""name"": ""Queue"", ""player"": ""P"", ""teams"": [""diaspora"", ""earth""],
              ""arrivalCap"": { ""team"": ""earth"", ""value"": 1 },
              ""ships"": [ { ""name"": ""P"", ""class"": ""vanguard_fighter"", ""team"": ""diaspora"", ""invulnerable"": true, ""arrival"": { ""at"": [0, 0, -3000] } },
                           { ""name"": ""Mercy"", ""class"": ""diaspora_cruiser"", ""team"": ""diaspora"", ""invulnerable"": true, ""capital"": { ""holdBeams"": true, ""stationary"": true }, ""arrival"": { ""at"": [0, 0, 0] } },
                           { ""name"": ""Near"", ""class"": ""earth_corvette"", ""team"": ""earth"", ""invulnerable"": true, ""capital"": { ""stationary"": true, ""holdTurrets"": true }, ""arrival"": { ""at"": [1800, 0, 0], ""rot"": [0, 0, 0] } },
                           { ""name"": ""Far"", ""class"": ""earth_corvette"", ""team"": ""earth"", ""invulnerable"": true, ""capital"": { ""stationary"": true, ""holdTurrets"": true }, ""arrival"": { ""at"": [-3000, 0, 0], ""rot"": [0, 0, 0] } } ],
              ""wings"": [ { ""name"": ""A"", ""team"": ""earth"", ""capped"": true, ""ships"": [ { ""class"": ""earth_interceptor"", ""count"": 2 } ], ""arrival"": { ""at"": [0, 0, 6000] } },
                           { ""name"": ""B"", ""team"": ""earth"", ""capped"": true, ""ships"": [ { ""class"": ""earth_interceptor"", ""count"": 1 } ], ""arrival"": { ""cue"": { ""time"": 1 }, ""at"": [600, 0, 6000] } },
                           { ""name"": ""C"", ""team"": ""earth"", ""capped"": true, ""capPriority"": 1, ""ships"": [ { ""class"": ""earth_interceptor"", ""count"": 1 } ], ""arrival"": { ""cue"": { ""time"": 2 }, ""at"": [-600, 0, 6000] } } ],
              ""events"": [ { ""name"": ""free"", ""when"": { ""time"": 1 }, ""do"": [ { ""do"": ""beams"", ""ship"": ""Mercy"", ""free"": true, ""target"": ""Far"" } ] } ],
              ""autoplay"": { ""expect"": { ""maxEngagingMax"": [2, 3, 4, 5, 99] } } }";
            var file = MissionFile.Load(json, "queue");
            Check(file.Autoplay.MaxEngagingAt(0) == 2 && file.Autoplay.MaxEngagingAt(3) == 5 && file.Autoplay.MaxEngagingAt(9) == 99, "an auto-play expectation can be one number per difficulty level (maxEngagingMax)");
            string bad = null;
            try { MissionFile.Load(json.Replace(@"""name"": ""C"", ""team"": ""earth"", ""capped"": true,", @"""name"": ""C"", ""team"": ""earth"", ""capped"": false,"), "queue_bad"); }
            catch (InvalidDataException e) { bad = e.Message; }
            Check(bad != null && bad.Contains("capPriority"), "capPriority on a wing that is not capped is refused: " + (bad ?? "(it loaded)").Replace("\n", " "));
            using (var lab = new AiLab(2))
            {
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai);
                var rt = new MissionRuntime(lab.W, file, host, new CabinStub());
                lab.W.Sim.Register(rt);
                rt.Start();
                lab.Run(2.5f);
                Check(rt.RecOf("A 1").Present && !rt.RecOf("A 2").Present && !rt.RecOf("B 1").Present && !rt.RecOf("C 1").Present && rt.QueuedCount == 3, $"three wait behind the cap of one ({rt.QueuedCount} queued)");
                var a1 = lab.W.Combat.Get(rt.IdOf("A 1"));
                lab.W.Damage.Add(new DamageRequest { Ship = a1.Id, Attacker = GameWorld.NoShip, Weapon = "test", Amount = 1e7f, HullMult = 1f, BypassShield = true, Kind = DamageKind.Debug, Part = -2, Point = a1.Ship.Position });
                lab.Run(0.2f);
                Check(rt.RecOf("C 1").Present && !rt.RecOf("A 2").Present && !rt.RecOf("B 1").Present, "a capped wing with a higher capPriority takes the free place ahead of older arrivals (C 1 in; A 2 and B 1 still waiting)");
                int mercy = rt.IdOf("Mercy"), far = rt.IdOf("Far"), near = rt.IdOf("Near");
                int onFar = 0, onNear = 0;
                for (int i = 0; i < 12 * Sim.Hz; i++)
                {
                    lab.W.Sim.StepOnce();
                    foreach (var b in lab.Rig.Weapons.Beams) if (b.Ship == mercy) { if (b.Target == far) onFar++; else if (b.Target == near) onNear++; }
                }
                Check(onFar > 0 && onNear == 0, $"beams with a target (FS2's beam-fire): the Mercy's beams go for the far corvette, not the nearer one ({F(onFar / (float)Sim.Hz)} s of beam on Far, {F(onNear / (float)Sim.Hz)} s on Near)");
                rt.Dispose(); host.Dispose();
            }
        }

        // ------------------------------------------------------------------------------------------------ the built-in missions, briefly

        static void BuiltIn()
        {
            foreach (var n in MissionLibrary.BuiltIn())
            {
                var r = MissionAutoPlay.Run(MissionLibrary.Load(n), 60f, 2, true, null);
                Check(r.Runtime.Started && r.Json["checks"] != null && !r.Report.Contains("FAIL  no errors") && !r.Report.Contains("FAIL  no bad numbers"),
                      $"'{n}' runs a minute headless with the AI flying the player: no errors, no bad numbers ({r.Runtime.Log.Entries.Count} timeline entries, {r.Runtime.Radio.Said.Count} radio lines)");
            }
        }

        // ------------------------------------------------------------------------------------------------ every radio line is voiced

        static void Voices()
        {
            // A line with no recording plays as a squelch: the playtester hears clicks and no voice. A mission written without voicing its lines fails here.
            var gaps = VoiceCoverage.Unvoiced(out int lines);
            int pending = VoiceCoverage.Pending;
            const string control = "{ \"linesPending\": false, \"messages\": [ { \"id\": \"p\", \"from\": \"Sable 2\", \"text\": \"A draft nobody has approved.\", \"pending\": true }, { \"id\": \"q\", \"from\": \"Sable 2\", \"text\": \"An approved line nobody has voiced.\" } ] }";
            var withControl = VoiceCoverage.Unvoiced(out _, ("pending_control", control));
            Check(withControl.Count == gaps.Count + 1 && VoiceCoverage.Pending == pending + 1 && withControl.Exists(g => g.Contains("nobody has voiced")),
                  "a line marked pending is counted, not failed; an approved line with no recording still fails");
            VoiceCoverage.Unvoiced(out _);
            Check(gaps.Count == 0, $"every radio line of every built-in mission has a recording ({lines} speaker and line pairs; {VoiceCoverage.Pending} pending the user's approval, not voiced on purpose)" + (gaps.Count > 0 ? ": " + string.Join(" | ", gaps.GetRange(0, Math.Min(6, gaps.Count))) + " -> " + VoiceCoverage.Fix : ""));
        }

        // ------------------------------------------------------------------------------------------------ the dogfight's port

        static void Port()
        {
            var r = DogfightPortCheck.Run(240f, 2, null);
            Check(r.Same, $"the dogfight's port plays the same as the hand-written dogfight: every ship's state identical at all {r.Seconds} one-second samples of 240 s" + (r.Same ? "" : $" (first difference at {r.FirstDiff} s)"));
        }
    }
}
