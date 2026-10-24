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
            var tests = new (string, Action)[] { ("format", Format), ("scripted", Scripted), ("missions", BuiltIn), ("port", Port) };
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
                Check(Near(At("needs repair"), 17f) && Near(At("repaired"), 20f), $"needs a hand repair at 17 s, repaired at 20 s ({F(At("needs repair"))}, {F(At("repaired"))})");
                Check(Near(At("cabin flags"), 0f), "seated and powered (the stub's defaults)");
                bool right = rt.Log.Entries.Exists(e => e.Text == "right branch"), wrong = rt.Log.Entries.Exists(e => e.Text == "wrong branch" || e.Text == "else branch");
                Check(right && !wrong, "cond runs the first case that holds");
                Check(Near(At("relay 2 in"), 24f) && rt.RecOf("Relay 4").Present, $"wing waves: the second wave comes 2 s after the first is gone, as Relay 3 and 4 ({F(At("relay 2 in"))})");
                Check(Near(At("late gone"), 27f) && lab.W.Ships.Get(Id("Late")) == null, $"depart: flies out, jumps after 2 s, leaves the world ({F(At("late gone"))})");
                Check(At("runner gone") > 25f && lab.W.Ships.Get(Id("Runner")) == null, $"waypoints then depart: the Runner flies its leg and jumps ({F(At("runner gone"))} s)");
                Check(Near(At("earth count"), 5f), $"teamAlive: Earth reaches 3 ships when Choir comes in ({F(At("earth count"))})");
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

        // ------------------------------------------------------------------------------------------------ the dogfight's port

        static void Port()
        {
            var r = DogfightPortCheck.Run(240f, 2, null);
            Check(r.Same, $"the dogfight's port plays the same as the hand-written dogfight: every ship's state identical at all {r.Seconds} one-second samples of 240 s" + (r.Same ? "" : $" (first difference at {r.FirstDiff} s)"));
        }
    }
}
