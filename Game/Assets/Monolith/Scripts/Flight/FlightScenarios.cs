using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>
    /// The flight-feel scenarios (gauntlet/shots/flight_scenarios.json, format "monolith-flight-scenarios/1"; documented
    /// in that file and tools/fs2ref/README.md). Scenario signs are FS2's: pitch +1 = nose down, roll +1 = roll left.
    /// </summary>
    public sealed class FlightScenario
    {
        public const string Format = "monolith-flight-scenarios/1";

        public struct Inputs
        {
            public float Pitch, Yaw, Roll, Throttle;   // scenario convention
            public bool Afterburner;

            /// <summary>As a <see cref="ControlInput"/> (Core's convention: pitch +1 nose up, roll +1 roll right).</summary>
            public ControlInput ToControl() => new ControlInput
            {
                Pitch = -Pitch,
                Yaw = Yaw,
                Roll = -Roll,
                Throttle = Throttle,
                Buttons = Afterburner ? ControlButtons.Afterburner : ControlButtons.None,
            };
        }

        struct Key
        {
            public float T;
            public float? Pitch, Yaw, Roll, Throttle;
            public bool? Afterburner;
        }

        public string Name, Description;
        public float Duration, SpeedFraction, StartThrottle;
        readonly List<Key> _keys = new List<Key>();

        /// <summary>Inputs in force at time t: every key at or before t applies in order (step hold).</summary>
        public Inputs At(float t)
        {
            var c = new Inputs { Throttle = StartThrottle };
            foreach (var k in _keys)
            {
                if (k.T > t + 1e-5f) break;
                if (k.Pitch.HasValue) c.Pitch = k.Pitch.Value;
                if (k.Yaw.HasValue) c.Yaw = k.Yaw.Value;
                if (k.Roll.HasValue) c.Roll = k.Roll.Value;
                if (k.Throttle.HasValue) c.Throttle = k.Throttle.Value;
                if (k.Afterburner.HasValue) c.Afterburner = k.Afterburner.Value;
            }
            return c;
        }

        public static List<FlightScenario> LoadFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"scenario file not found: {path}");
            var root = JObject.Parse(File.ReadAllText(path));
            if ((string)root["format"] != Format) throw new InvalidDataException($"{path}: format '{root["format"]}', expected '{Format}'");
            var list = new List<FlightScenario>();
            var names = new HashSet<string>();
            foreach (JObject raw in (JArray)root["scenarios"])
            {
                var init = raw["initial"] as JObject ?? new JObject();
                var s = new FlightScenario
                {
                    Name = (string)raw["name"],
                    Description = (string)raw["description"] ?? "",
                    Duration = (float)raw["duration"],
                    SpeedFraction = (float?)init["speed_fraction"] ?? 0f,
                    StartThrottle = (float?)init["throttle"] ?? 0f,
                };
                if (string.IsNullOrWhiteSpace(s.Name) || !names.Add(s.Name)) throw new InvalidDataException($"{path}: missing or duplicate scenario name '{s.Name}'");
                foreach (JObject k in (JArray)raw["keys"] ?? new JArray())
                {
                    foreach (var p in k.Properties())
                        if (Array.IndexOf(new[] { "t", "pitch", "yaw", "roll", "throttle", "afterburner" }, p.Name) < 0)
                            throw new InvalidDataException($"{path}: scenario {s.Name}: unknown key field '{p.Name}'");
                    s._keys.Add(new Key
                    {
                        T = (float)k["t"],
                        Pitch = (float?)k["pitch"],
                        Yaw = (float?)k["yaw"],
                        Roll = (float?)k["roll"],
                        Throttle = (float?)k["throttle"],
                        Afterburner = (bool?)k["afterburner"],
                    });
                }
                s._keys.Sort((a, b) => a.T.CompareTo(b.T));
                list.Add(s);
            }
            return list;
        }
    }

    /// <summary>A pilot that plays one scenario's timeline by sim time.</summary>
    public sealed class ScenarioPilot : IPilotSource
    {
        readonly FlightScenario _scenario;
        public ScenarioPilot(FlightScenario scenario) { _scenario = scenario; }
        public ControlInput Sample(in SimFrame frame) => _scenario.At(frame.Time).ToControl();
    }

    /// <summary>
    /// The built player's flight-trace mode:
    ///   -mnFlightScenarios gauntlet/shots/flight_scenarios.json -mnShip "GTF Ulysses" -mnOut dir
    /// Replays every scenario through the game's own Sim, Pilots and FlightSystem at 60 Hz and writes
    /// dir/&lt;scenario&gt;/trace.csv in the format tools/fs2ref/compare.py reads: one row per tick, the state at the
    /// START of the tick plus the inputs about to be applied.
    /// </summary>
    public static class FlightScenarioRunner
    {
        public const string Flag = "-mnFlightScenarios";

        static readonly string[] Columns =
        {
            "time", "pitch", "yaw", "roll", "throttle", "afterburner", "vx", "vy", "vz", "speed", "wx", "wy", "wz",
            "fwd_x", "fwd_y", "fwd_z", "up_x", "up_y", "up_z", "pos_x", "pos_y", "pos_z", "fuel",
        };

        /// <summary>Exit code: 0 ok, 1 a scenario failed, 2 bad arguments.</summary>
        public static int RunFromCommandLine()
        {
            string file = CommandLine.Get(Flag), ship = CommandLine.Get("-mnShip"), outDir = CommandLine.Get("-mnOut");
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(ship) || string.IsNullOrEmpty(outDir))
            {
                MnLog.Error($"FLIGHT usage: {Flag} scenarios.json -mnShip <class> -mnOut <dir>");
                return 2;
            }
            try { return Run(file, ship, outDir); }
            catch (Exception e)
            {
                MnLog.Error($"FLIGHT scenarios failed: {e}");
                return 2;
            }
        }

        public static int Run(string scenarioFile, string ship, string outDir)
        {
            var cls = ShipCatalog.Load(ship);
            var rules = FlightRules.Default;
            var scenarios = FlightScenario.LoadFile(scenarioFile);
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            MnLog.Info($"FLIGHT {scenarios.Count} scenario(s) for {cls} at {Sim.Hz} Hz -> {outDir}");

            int failed = 0;
            foreach (var sc in scenarios)
            {
                try
                {
                    string path = Path.Combine(outDir, sc.Name, "trace.csv");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    int rows = RunOne(sc, cls, rules, path);
                    MnLog.Info($"FLIGHT {sc.Name}: {rows} rows -> {path}");
                }
                catch (Exception e)
                {
                    failed++;
                    MnLog.Error($"FLIGHT {sc.Name} FAILED: {e}");
                }
            }

            bool selfTest = FlightSelfTest.Run();
            if (!selfTest) failed++;

            var info = new JObject
            {
                ["selfTest"] = selfTest ? "ok" : "failed",
                ["ship"] = cls.Name,
                ["shipId"] = cls.Id,
                ["tickHz"] = Sim.Hz,
                ["scenarioFile"] = Path.GetFullPath(scenarioFile),
                ["scenarios"] = scenarios.Count,
                ["failed"] = failed,
                ["unity"] = Application.unityVersion,
                ["written"] = DateTime.Now.ToString("s"),
                ["model"] = "Game/Assets/Monolith/Scripts/Flight (ShipFlight, FlightSystem) run through Core's Sim and Pilots",
            };
            File.WriteAllText(Path.Combine(outDir, "run.json"), info.ToString());
            MnLog.Info($"FLIGHT DONE {scenarios.Count - (selfTest ? failed : failed - 1)}/{scenarios.Count} ok, selftest {(selfTest ? "ok" : "FAILED")}");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>One scenario through a private Sim + Pilots + FlightSystem, exactly as the game wires them.</summary>
        static int RunOne(FlightScenario sc, ShipClass cls, FlightRules rules, string path)
        {
            var sim = new Sim();
            var world = new GameWorld(sim);
            sim.Register(world.Pilots);
            var flight = new FlightSystem(world, rules);
            sim.Register(flight);
            var state = flight.Spawn(cls, Vector3.zero, Quaternion.identity, sc.SpeedFraction * cls.TopSpeed, sc.StartThrottle);
            world.Pilots.Attach(state.Id, new ScenarioPilot(sc));
            var model = flight.Model(state.Id);

            int n = Mathf.RoundToInt(sc.Duration * Sim.Hz);
            var sb = new StringBuilder(256 * (n + 2));
            sb.Append(string.Join(",", Columns)).Append('\n');
            for (int i = 0; i <= n; i++)
            {
                var input = sc.At(new SimFrame(sim.Tick).Time);     // what the pilot will hand flight on this tick
                Row(sb, i / (double)Sim.Hz, input, model);
                if (i < n) sim.StepOnce();
            }
            File.WriteAllText(path, sb.ToString());
            return n + 1;
        }

        static void Row(StringBuilder sb, double time, FlightScenario.Inputs c, ShipFlight m)
        {
            var v = m.LocalVelocity;
            var f = m.Rotation * Vector3.forward;
            var u = m.Rotation * Vector3.up;
            F(sb, time); F(sb, c.Pitch); F(sb, c.Yaw); F(sb, c.Roll); F(sb, c.Throttle);
            sb.Append(c.Afterburner ? "1" : "0").Append(',');
            F(sb, v.x); F(sb, v.y); F(sb, v.z); F(sb, m.Velocity.magnitude);
            F(sb, m.Spin.x); F(sb, m.Spin.y); F(sb, m.Spin.z);
            F(sb, f.x); F(sb, f.y); F(sb, f.z); F(sb, u.x); F(sb, u.y); F(sb, u.z);
            P(sb, m.Position.x); P(sb, m.Position.y); P(sb, m.Position.z);
            sb.Append(m.Fuel.ToString("F5", CultureInfo.InvariantCulture)).Append('\n');
        }

        static void F(StringBuilder sb, double v) => sb.Append(v.ToString("F5", CultureInfo.InvariantCulture)).Append(',');
        static void P(StringBuilder sb, double v) => sb.Append(v.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
    }
}
