using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Monolith.Audio;
using Monolith.Core;
using Monolith.Mission;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The audio checks (<c>Monolith.exe -batchmode -nographics -mnAudioSelfTest [-mnOut dir]</c>), run on the real audio system rendered offline and driven by the
    /// real event vocabulary, with the real decoded files: every file loads; every loop is the same length and closes on itself; every state and
    /// sting plays, and a change from any state to any other lands on a beat or a bar without a click or a drop-out; every radio line in
    /// the built-in missions and the wingmen's acknowledgements has a clip, and a line with none falls back to a squelch and is logged; the player's
    /// own files override the built-in ones and give them back when deleted; every effect plays; and a scripted battle (music, a volley of
    /// guns, explosions, a radio line) never clips the master bus and stays inside its loudness bounds. Writes audio_selftest.txt and the mixes it rendered.
    /// </summary>
    public static class AudioSelfTest
    {
        public static bool Requested => CommandLine.Has("-mnAudioSelfTest");

        public static void Start(GameRoot root)
        {
            var go = new GameObject("AudioSelfTest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        sealed class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                var t = new Test(this);
                yield return t.Run();
                Application.Quit(t.Failures == 0 ? 0 : 1);
            }
        }

        sealed class Test
        {
            readonly MonoBehaviour _host;
            readonly StringBuilder _report = new StringBuilder();
            readonly string _out;
            public int Failures, Checks;
            AudioLibrary _lib;
            const int R = 48000;

            public Test(MonoBehaviour host)
            {
                _host = host;
                _out = CommandLine.Get("-mnOut", "audio_selftest");
                Directory.CreateDirectory(_out);
            }

            void Say(string s) { _report.AppendLine(s); MnLog.Info("AUDIOTEST " + s); }

            void Check(bool ok, string what)
            {
                Checks++;
                if (!ok) Failures++;
                Say((ok ? "  ok   " : "  FAIL ") + what);
            }

            public IEnumerator Run()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Application.logMessageReceived += OnLog;
                yield return Load();
                if (_lib.Ready)
                {
                    yield return Guard("Files", Files());
                    yield return Guard("Music", Music());
                    yield return Guard("Transitions", Transitions());
                    yield return Guard("Stings", Stings());
                    yield return Guard("Endings", Endings());
                    yield return Guard("Radio", Radio());
                    yield return Guard("Overrides", Overrides());
                    yield return Guard("Effects", Effects());
                    yield return Guard("Cabin", Cabin());
                    yield return Guard("Mix", Mix());
                    Prefs();
                }
                Application.logMessageReceived -= OnLog;
                Check(_errors == 0, $"no errors in the log ({_errors} seen)");
                Say(Failures == 0 ? $"AUDIO SELF-TEST PASS ({Checks} checks, {sw.Elapsed.TotalSeconds:0} s)" : $"AUDIO SELF-TEST FAILED: {Failures} of {Checks} checks ({sw.Elapsed.TotalSeconds:0} s)");
                File.WriteAllText(Path.Combine(_out, "audio_selftest.txt"), _report.ToString());
            }

            /// <summary>Runs one stage; an exception in it is a failed check, not a hung run (a coroutine that throws just stops, and the player would wait forever).</summary>
            IEnumerator Guard(string stage, IEnumerator inner)
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!inner.MoveNext()) yield break;
                        current = inner.Current;
                    }
                    catch (Exception e)
                    {
                        Check(false, $"{stage} threw {e.GetType().Name}: {e.Message}");
                        yield break;
                    }
                    yield return current;
                }
            }

            int _errors;
            void OnLog(string msg, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) _errors++; }

            // ------------------------------------------------------------------------------------------------ a system to test against

            sealed class Rig
            {
                public GameWorld World; public AudioSystem Sys; public int Tick;
                public void Step(float seconds) { int n = Mathf.RoundToInt(seconds * Sim.Hz); for (int i = 0; i < n; i++) { World.Sim.StepOnce(); Tick++; } }
                public float Now => Tick / (float)Sim.Hz;
            }

            Rig NewRig(bool full = true, AudioLibrary library = null)
            {
                var old = AudioSystem.Active;
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                GameEvents.Reset();
                var world = new GameWorld(new Sim());
                GameWorld.SetCurrent(world);
                var sys = AudioSystem.Create(world, null, new AudioOptions { Offline = true, Record = true, SavePrefs = false, Library = library ?? _lib });
                if (full) { sys.Prefs.master = 1f; sys.Prefs.music = 1f; sys.Prefs.effects = 1f; sys.Prefs.voice = 1f; sys.ApplyPrefs(); }
                return new Rig { World = world, Sys = sys };
            }

            static float Rms(float[] x, int a, int n)
            {
                a = Mathf.Clamp(a, 0, x.Length); n = Mathf.Clamp(n, 0, x.Length - a);
                if (n <= 0) return 0f;
                double s = 0; for (int i = a; i < a + n; i++) s += x[i] * x[i];
                return (float)Math.Sqrt(s / n);
            }

            static float Db(float lin) => 20f * Mathf.Log10(Mathf.Max(lin, 1e-9f));

            /// <summary>The biggest second difference in a window against the 99.9th percentile of the whole signal: a click shows as a ratio far above 1.</summary>
            static float ClickRatio(float[] x, int from, int to, int step = 3)
            {
                from = Mathf.Clamp(from, 2, x.Length - 3); to = Mathf.Clamp(to, from + 1, x.Length - 2);
                float worst = 0f;
                for (int i = from; i < to; i++) worst = Mathf.Max(worst, Mathf.Abs(x[i + 1] - 2f * x[i] + x[i - 1]));
                var d = new List<float>();
                for (int i = 2; i < x.Length - 2; i += step * 7) d.Add(Mathf.Abs(x[i + 1] - 2f * x[i] + x[i - 1]));
                d.Sort();
                float p = d.Count > 0 ? d[(int)(d.Count * 0.999f)] : 1e-6f;
                return worst / Mathf.Max(p, 1e-6f);
            }

            // ------------------------------------------------------------------------------------------------ the checks

            IEnumerator Load()
            {
                Say("== loading every file (decoded by Unity, as in the game)");
                _lib = new AudioLibrary();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                yield return _lib.Load(_host);
                Check(_lib.Ready, $"the library loaded in {sw.ElapsedMilliseconds} ms");
                Check(_lib.FilesFailed == 0, $"{_lib.FilesTotal} files, {_lib.FilesFailed} failed");
                foreach (var p in _lib.Problems) Say("   problem: " + p);
            }

            IEnumerator Files()
            {
                Say("== the catalogs");
                string[] tracks = { "ambient", "battle1", "battle2", "battle3", "victory", "failure", "death", "sting_friendly", "sting_friendly_battle", "sting_enemy", "sting_enemy_battle", "sting_goal", "sting_fail" };
                foreach (var t in tracks) Check(_lib.Music.TryGetValue(t, out var m) && m.Clip != null && m.Clip.Frames > R, $"music track '{t}' is there" + (_lib.Music.TryGetValue(t, out var mm) && mm.Clip != null ? $" ({mm.Clip.Seconds:0.0} s)" : ""));
                string[] cues = { "laser_light", "laser_enemy", "laser_heavy", "laser_turret", "flak_shell", "flak_burst", "missile_launch", "torpedo_launch", "missile_hiss", "decoy_launch", "decoy_success", "near_miss",
                    "beam_charge", "beam_fire", "beam_loop", "beam_impact_loop", "beam_end", "hit_shield", "hit_shield_player", "hit_hull", "hit_hull_player", "hit_hull_player_heavy", "shield_down", "subsystem_blast",
                    "collision", "explosion_small", "explosion_medium", "explosion_large", "breakup", "shockwave", "creak_a", "creak_b", "creak_c", "creak_d", "engine_loop", "afterburner_loop", "afterburner_on", "afterburner_off",
                    "cabin_air", "alarm_hull", "alarm_beep", "system_fail", "jump_in", "jump_in_big", "jump_out", "jump_out_big", "earth_horn", "earth_horn_small", "scan_sweep", "ui_click", "ui_select", "target_blip",
                    "lock_acquired", "lock_lost", "power_blip", "squelch_open", "squelch_close",
                    "step_plate", "step_grating", "step_rung", "seat_open", "seat_close", "breaker_off", "breaker_on", "scan_hit", "breath_in", "breath_out", "repair_loop", "repair_done", "repair_slip", "rack_seat", "cabin_rattle" };
                var missing = cues.Where(c => !_lib.Has(c)).ToList();
                Check(missing.Count == 0, $"{cues.Length} effects are there" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
                // loops are all one length, and each closes on itself
                var loops = new[] { "ambient", "battle1", "battle2", "battle3" };
                int f0 = _lib.Music["ambient"].Clip.Frames;
                Check(loops.All(l => Mathf.Abs(_lib.Music[l].Clip.Frames - f0) <= 2), $"the four loops are the same length ({string.Join(", ", loops.Select(l => _lib.Music[l].Clip.Frames.ToString()))} frames)");
                foreach (var l in loops)
                {
                    var c = _lib.Music[l].Clip;
                    var x = new float[c.Frames];
                    for (int i = 0; i < x.Length; i++) x[i] = c.Pcm[i * c.Channels] / 32768f;
                    // the seam: last sample to first, as a second difference
                    float seam = Mathf.Max(Mathf.Abs(x[0] - 2f * x[x.Length - 1] + x[x.Length - 2]), Mathf.Abs(x[1] - 2f * x[0] + x[x.Length - 1]));
                    var d = new List<float>();
                    for (int i = 2; i < x.Length - 2; i += 7) d.Add(Mathf.Abs(x[i + 1] - 2f * x[i] + x[i - 1]));
                    d.Sort();
                    float p = d[(int)(d.Count * 0.999f)];
                    Check(seam <= p * 1.5f, $"loop '{l}' closes on itself (seam {seam:0.0000} against {p:0.0000} elsewhere)");
                }
                yield return null;
            }

            IEnumerator Music()
            {
                Say("== each state plays and loops cleanly");
                foreach (var state in new[] { "ambient", "battle1", "battle2", "battle3" })
                {
                    var rig = NewRig();
                    GameEvents.Raise(MissionEvents.MusicState, state);
                    float len = _lib.Music[state].Clip.Seconds;
                    rig.Step(len * 1.15f + 1f);
                    var rec = rig.Sys.RecordedAudio();
                    var m = LoudnessMeter.Measure(Skip(rec, 3f));
                    float want = _lib.Music[state].Lufs;
                    Check(rig.Sys.Music.State == state, $"{state}: the conductor is in '{state}'");
                    Check(rig.Sys.Mixer.ClippedSamples == 0, $"{state}: no clipped samples on the master bus");
                    Check(m.PeakDb <= -0.9f, $"{state}: peak {m.PeakDb:0.0} dBFS under the limiter's ceiling");
                    Check(Mathf.Abs(m.Integrated - want) <= 2.0f, $"{state}: loudness {m.Integrated:0.0} LUFS (the file says {want:0.0}, music at full volume)");
                    int seamAt = Mathf.RoundToInt(len * R) + Mathf.RoundToInt(R * 0.0f);
                    // the clip began (after its fade-in start) at t = 0 of the clock, so the seam is len seconds in
                    float ratio = ClickRatio(rec.Ch[0], seamAt - 400, seamAt + 400);
                    Check(ratio <= 3.0f, $"{state}: no click where the loop comes round (ratio {ratio:0.0})");
                    Say($"   {state}: {m.Integrated:0.0} LUFS, momentary max {m.MaxMomentary:0.0}, peak {m.PeakDb:0.0} dBFS");
                    if (state == "battle3") WriteWav("music_battle3_loop.wav", rec);
                    yield return null;
                }
            }

            static PcmBuffer Skip(PcmBuffer b, float seconds)
            {
                int s = Mathf.Min(b.Frames, (int)(seconds * b.SampleRate));
                var o = new PcmBuffer(b.SampleRate, b.Channels, b.Frames - s);
                for (int c = 0; c < b.Channels; c++) Array.Copy(b.Ch[c], s, o.Ch[c], 0, o.Frames);
                return o;
            }

            void WriteWav(string name, PcmBuffer b) { try { Wav.Write(Path.Combine(_out, name), b, 16, true); } catch (Exception e) { Say("could not write " + name + ": " + e.Message); } }

            IEnumerator Transitions()
            {
                Say("== every state to every other, off the grid");
                var states = new[] { "ambient", "battle1", "battle2", "battle3" };
                int bad = 0, total = 0;
                foreach (var a in states)
                    foreach (var b in states)
                    {
                        if (a == b) continue;
                        total++;
                        var rig = NewRig();
                        GameEvents.Raise(MissionEvents.MusicState, a);
                        rig.Step(6.37f);                                       // not on a beat
                        int startsBefore = rig.Sys.Mixer.MusicStarts;
                        long clockAtRequest = rig.Sys.Mixer.MusicClock;
                        GameEvents.Raise(MissionEvents.MusicState, b);
                        rig.Step(9f);
                        var mx = rig.Sys.Mixer;
                        bool rising = Array.IndexOf(states, b) > Array.IndexOf(states, a);
                        double grid = rising ? mx.FramesPerBeat : mx.FramesPerBeat * mx.BeatsPerBar;
                        double rem = mx.LastMusicStartClock % grid; if (rem > grid / 2) rem = grid - rem;
                        bool onGrid = mx.MusicStarts == startsBefore + 1 && rem <= 1.5 && mx.LastMusicStartClock >= clockAtRequest;
                        var rec = rig.Sys.RecordedAudio();
                        int at = (int)(mx.LastMusicStartClock * (double)R / mx.SampleRate);
                        float click = ClickRatio(rec.Ch[0], at - 2000, at + R * 2);
                        float quiet = float.MaxValue;
                        for (int i = R * 2; i + 4800 < rec.Frames; i += 4800) quiet = Mathf.Min(quiet, Rms(rec.Ch[0], i, 4800));
                        bool ok = onGrid && click <= 4.0f && Db(quiet) > -60f && mx.ClippedSamples == 0;
                        if (!ok) { bad++; Say($"   {a} -> {b}: starts {mx.MusicStarts - startsBefore}, {(rising ? "beat" : "bar")} remainder {rem:0.0} frames, click {click:0.0}, quietest {Db(quiet):0.0} dB, clipped {mx.ClippedSamples}"); }
                        yield return null;
                    }
                Check(bad == 0, $"{total} transitions: each on its beat or bar, none clicks, none drops out ({bad} bad)");
            }

            IEnumerator Stings()
            {
                Say("== the stings");
                var seen = new HashSet<string>();
                foreach (var state in new[] { "ambient", "battle2" })
                {
                    var rig = NewRig();
                    GameEvents.Raise(MissionEvents.MusicState, state);
                    rig.Step(5f);
                    foreach (var s in new[] { "enemy_arrival", "friendly_arrival", "goal_complete", "goal_failed" })
                    {
                        float before = Rms(rig.Sys.RecordedAudio().Ch[0], (int)((rig.Now - 0.5f) * R), R / 2);
                        GameEvents.Raise(MissionEvents.MusicSting, s);
                        rig.Step(9f);
                    }
                    foreach (var l in rig.Sys.Music.Log) if (l.StartsWith("sting ")) seen.Add(l.Substring(l.IndexOf('(') + 1).TrimEnd(')'));
                    Check(rig.Sys.Music.Stings == 4, $"{state}: four stings played");
                    Check(rig.Sys.Mixer.ClippedSamples == 0, $"{state}: no clipping with stings on top");
                    yield return null;
                }
                var all = new[] { "sting_enemy", "sting_enemy_battle", "sting_friendly", "sting_friendly_battle", "sting_goal", "sting_fail" };
                Check(all.All(seen.Contains), $"all six sting files played ({string.Join(", ", seen.OrderBy(x => x))})");
                // each sting sounds: its own loudness through the mixer
                foreach (var name in all)
                {
                    var rig = NewRig();
                    rig.Sys.Mixer.MusicSting(_lib.Music[name].Clip, Quant.Now);
                    rig.Step(_lib.Music[name].Clip.Seconds + 0.5f);
                    var rec = rig.Sys.RecordedAudio();
                    var m = LoudnessMeter.Measure(rec);
                    float tail = Rms(rec.Ch[0], rec.Frames - R / 4, R / 4);
                    Check(m.PeakDb > -30f && Db(tail) < -50f, $"{name}: peak {m.PeakDb:0.0} dBFS, loud {m.Integrated:0.0} LUFS, ends silent ({Db(tail):0.0} dB)");
                    yield return null;
                }
            }

            IEnumerator Endings()
            {
                Say("== victory, failure and death");
                foreach (var state in new[] { "victory", "failure", "death" })
                {
                    var rig = NewRig();
                    GameEvents.Raise(MissionEvents.MusicState, "battle3");
                    rig.Step(7f);
                    GameEvents.Raise(MissionEvents.MusicState, state);
                    float len = _lib.Music[state].Clip.Seconds;
                    rig.Step(len + 3f);
                    var rec = rig.Sys.RecordedAudio();
                    float tail = Rms(rec.Ch[0], rec.Frames - R / 2, R / 2);
                    Check(rig.Sys.Music.State == state && !rig.Sys.Mixer.MusicActive, $"{state}: plays once and stops");
                    Check(Db(tail) < -60f && rig.Sys.Mixer.ClippedSamples == 0, $"{state}: ends in silence ({Db(tail):0.0} dB), no clipping");
                    yield return null;
                }
            }

            // ------------------------------------------------------------------------------------------------ the radio

            sealed class Line { public string Display, Source; }

            List<Line> AllLines()
            {
                var lines = new List<Line>();
                void Add(string speaker, string text, string src) => lines.Add(new Line { Display = (speaker.Length > 0 ? speaker.ToUpperInvariant() + ": " : "") + text, Source = src });
                foreach (var name in MissionLibrary.BuiltIn())
                {
                    var f = MissionLibrary.Load(name);
                    foreach (var m in f.Messages)
                    {
                        if (m.Pending) continue;                // a draft pending the user's approval: not voiced on purpose (VoiceCoverage counts it)
                        var speakers = new List<string>();
                        string from = m.From ?? "";
                        if (from == MissionFile.AnyWingman) { speakers.Add("Sable 2"); speakers.Add("Sable 3"); speakers.Add("Sable 1"); }
                        else if (from.StartsWith("#")) speakers.Add(from.Substring(1));
                        else speakers.Add(from);
                        if (!string.IsNullOrEmpty(m.FallbackFrom)) speakers.Add(m.FallbackFrom.TrimStart('#'));
                        foreach (var sp in speakers) Add(sp, m.Text, $"{name}:{m.Id}");
                    }
                }
                var radio = JObject.Parse(Resources.Load<TextAsset>("AI/radio").text);
                var wingmen = new[] { "Sable 2", "Sable 3" };
                var capital = new HashSet<string> { "bombers", "capital_hurt", "beams_threat", "beams_down" };
                foreach (var p in ((JObject)radio["lines"]).Properties())
                    foreach (var t in p.Value.ToObject<string[]>())
                    {
                        var texts = t.Contains("{0}") ? wingmen.Select(w => t.Replace("{0}", w)).ToArray() : new[] { t };
                        foreach (var tt in texts)
                        {
                            if (p.Name == "earth_wave" || p.Name == "earth_bombers") { Add("Warden", tt, "radio.json:" + p.Name); Add("Lector", tt, "radio.json:" + p.Name); }
                            else if (capital.Contains(p.Name)) Add("Small Mercy", tt, "radio.json:" + p.Name);
                            else foreach (var w in wingmen) Add(w, tt, "radio.json:" + p.Name);
                        }
                    }
                return lines;
            }

            IEnumerator Radio()
            {
                Say("== the radio: every line has a clip, or a logged squelch");
                var rig = NewRig();
                var lines = AllLines();
                var missing = new List<string>(); int other = 0;
                foreach (var l in lines)
                {
                    var vc = rig.Sys.Radio.Resolve(l.Display, out var voice, out bool otherVoice);
                    if (vc == null || vc.Clip == null) missing.Add($"{l.Display} ({l.Source})");
                    else if (otherVoice) other++;
                }
                Check(lines.Count >= 100, $"{lines.Count} lines gathered from the built-in missions and the wingmen's acknowledgements");
                Check(missing.Count == 0, $"every line has a clip ({lines.Count - missing.Count} of {lines.Count}; {other} use another voice's recording of the same words)" + (missing.Count > 0 ? ": " + string.Join(" | ", missing.Take(6)) + " -> " + VoiceCoverage.Fix : ""));
                // and by the speaker's own voice: a line voiced only by somebody else's recording is a line nobody has voiced
                var gaps = VoiceCoverage.Unvoiced(out int looked);
                Check(gaps.Count == 0 && other == 0, $"every radio line of every mission in the build ({string.Join(", ", MissionLibrary.BuiltIn())}) is voiced by its own speaker ({looked} speaker and line pairs, {other} on another voice; {VoiceCoverage.Pending} pending the user's approval, not voiced on purpose)" + (gaps.Count > 0 ? ": " + string.Join(" | ", gaps.Take(6)) + " -> " + VoiceCoverage.Fix : ""));
                // the check itself works: a mission with a line nobody voiced, and a speaker nobody is cast as, is caught
                const string unvoiced = "{ \"messages\": [ { \"id\": \"a\", \"from\": \"Sable 2\", \"text\": \"This line has never been recorded by anybody.\" }, { \"id\": \"b\", \"from\": \"Zed\", \"text\": \"Copy.\" } ], \"events\": [ { \"do\": [ { \"do\": \"callOut\", \"text\": \"Nor has this one.\" } ] } ] }";
                var caught = VoiceCoverage.Unvoiced(out _, ("negative_control", unvoiced));
                Check(caught.Count == gaps.Count + 5 && caught.Any(g => g.Contains("never been recorded")) && caught.Any(g => g.Contains("nobody is cast as 'Zed'")) && caught.Any(g => g.Contains("Nor has this one")), $"an unvoiced line, an uncast speaker and an unvoiced callOut are each caught ({caught.Count - gaps.Count} found in the negative control)");
                // intensity: every line is tagged calm, tense or frantic from its mission context; a line made at two intensities plays the one the music state asks for
                var crew = rig.Sys.Lib.Voices.Where(v => !v.Voice.StartsWith("earth")).ToList();
                int nCalm = crew.Count(v => v.Intensity == 0), nTense = crew.Count(v => v.Intensity == 1), nFrantic = crew.Count(v => v.Intensity == 2);
                Say($"   crew clips by intensity: calm {nCalm}, tense {nTense}, frantic {nFrantic}");
                Check(nCalm > 0 && nTense > 0 && nFrantic > nCalm, $"the crew's lines are tagged by intensity ({nCalm} calm, {nTense} tense, {nFrantic} frantic): the fight is mostly frantic");
                var rd = rig.Sys.Radio;
                var ack0 = rd.Resolve("SABLE 2: COPY. GOING IN.", out _, out _, 0); var ack1 = rd.Resolve("SABLE 2: COPY. GOING IN.", out _, out _, 1); var ack2 = rd.Resolve("SABLE 2: COPY. GOING IN.", out _, out _, 2);
                Check(ack0 != null && ack1 != null && ack2 != null && ack0.Intensity == 1 && ack1.Intensity == 1 && ack2.Intensity == 2, $"an acknowledgement is tense in the quiet and first contact and frantic in the fight ({ack0?.File} / {ack1?.File} / {ack2?.File})");
                var bad = rd.Resolve("SMALL MERCY: MERCY HERE. HULL'S GOING. ANY TIME NOW.", out _, out _, 0);
                Check(bad != null && bad.Intensity == 2, $"bad news in the fight is frantic whatever the music is doing ({bad?.File})");
                var cold = rd.Resolve("WARDEN: BIOLOGICS. YOU WERE US, ONCE.", out _, out _, 2);
                Check(cold != null && cold.Intensity == 0, $"Earth never panics, even in the worst of it ({cold?.File})");
                int franticAcks = 0, acks = 0;
                foreach (var l in lines.Where(x => x.Source.StartsWith("radio.json:ack_")))
                {
                    var c = rd.Resolve(l.Display, out _, out _, 2);
                    if (c == null) continue;
                    acks++; if (c.Intensity == 2) franticAcks++;
                }
                Check(acks > 0 && franticAcks * 2 >= acks, $"in a big fight most acknowledgements are the frantic ones ({franticAcks} of {acks})");
                float Mean(IEnumerable<float> xs) { var a = xs.ToList(); return a.Count == 0 ? 0f : a.Average(); }
                float lufsCalm = Mean(crew.Where(v => v.Intensity == 0).Select(v => v.Lufs)), lufsFr = Mean(crew.Where(v => v.Intensity == 2).Select(v => v.Lufs));
                Check(lufsFr > lufsCalm + 2f, $"shouting is louder than talking ({lufsFr:0.0} against {lufsCalm:0.0} LUFS)");
                float WordsPerSec(int inten) => Mean(crew.Where(v => v.Intensity == inten && v.Seconds > 0.3f).Select(v => System.Text.RegularExpressions.Regex.Matches(v.Text, "[A-Za-z']+").Count / v.Seconds));
                Check(WordsPerSec(2) > WordsPerSec(0) * 1.15f, $"and quicker ({WordsPerSec(2):0.0} against {WordsPerSec(0):0.0} words a second, squelch and all)");
                // speak a few through the whole path
                string[] sample = { "SABLE 2: COPY. GOING IN.", "SMALL MERCY: MERCY HERE. HULL'S GOING. ANY TIME NOW.", "WARDEN: BIOLOGICS. YOU WERE US, ONCE.", "TAMSIN: BOMBERS ON THE SCOPE. THEY'RE COMING FOR US. PLEASE DON'T LET THEM.", "WICK 1: WICK FLIGHT. LAST ONES IN THE BARREL. MAKE IT COUNT." };
                foreach (var s in sample)
                {
                    GameEvents.Raise(CombatEvents.RadioLine, s, 0f, GameWorld.NoShip);
                    rig.Step(0.1f);
                    yield return null;
                    float guard = 0f;
                    while (rig.Sys.Radio.Busy(rig.Sys.Time01) && guard < 12f) { rig.Step(0.5f); guard += 0.5f; }
                }
                Check(rig.Sys.Radio.Voiced == sample.Length && rig.Sys.Radio.Fallbacks == 0, $"{sample.Length} sample lines played with their clips ({rig.Sys.Radio.Voiced} voiced, {rig.Sys.Radio.Fallbacks} squelch)");
                var rec = rig.Sys.RecordedAudio();
                var m = LoudnessMeter.Measure(rec);
                Check(m.PeakDb > -25f && m.PeakDb <= -0.9f && rig.Sys.Mixer.ClippedSamples == 0, $"the radio is heard and never clips (peak {m.PeakDb:0.0} dBFS, {m.Integrated:0.0} LUFS)");
                WriteWav("radio_sample.wav", rec);
                // a line with no clip
                var rig2 = NewRig();
                GameEvents.Raise(CombatEvents.RadioLine, "NOBODY: THIS LINE HAS NO RECORDING AT ALL.", 0f, GameWorld.NoShip);
                rig2.Step(0.2f); yield return null; rig2.Step(5f);
                var rec2 = rig2.Sys.RecordedAudio();
                Check(rig2.Sys.Radio.Fallbacks == 1 && rig2.Sys.Radio.Missing.Count == 1, "a line with no clip falls back to a squelch and is logged");
                Check(Db(Rms(rec2.Ch[0], 0, rec2.Frames)) > -70f, "the squelch is heard");
                rig2.Sys.Radio.Log.ForEach(l => Say("   " + l));
            }

            // ------------------------------------------------------------------------------------------------ the player's own files

            IEnumerator Overrides()
            {
                Say("== the player's own files");
                string prev = AudioPaths.User;
                string scratch = Path.Combine(Application.temporaryCachePath, "audio_override_test_" + DateTime.Now.Ticks);
                try
                {
                    Directory.CreateDirectory(Path.Combine(scratch, "music"));
                    Directory.CreateDirectory(Path.Combine(scratch, "vo", "sable2"));
                    var tone = new PcmBuffer(R, 2, R * 2);
                    for (int i = 0; i < tone.Frames; i++) { float v = 0.25f * Mathf.Sin(2f * Mathf.PI * 440f * i / R); tone.Ch[0][i] = v; tone.Ch[1][i] = v; }
                    Wav.Write(Path.Combine(scratch, "music", "battle2.wav"), tone, 16, false);
                    File.WriteAllText(Path.Combine(scratch, "music", "battle2.json"), "{ \"loopStart\": 0.5, \"loopEnd\": 1.5, \"volume\": -3 }");
                    var voice = new PcmBuffer(24000, 1, 24000);
                    var rng = new Rng(3);
                    for (int i = 0; i < voice.Frames; i++) voice.Ch[0][i] = 0.3f * Mathf.Sin(2f * Mathf.PI * 180f * i / 24000f) * (0.6f + 0.4f * Mathf.Sin(i * 0.002f)) + 0.02f * rng.Signed();
                    Wav.Write(Path.Combine(scratch, "vo", "sable2", "ack_attack_01_frantic.wav"), voice, 16, false);
                    // an MP3 and an OGG as well: whatever the player's DAW exports
                    string tests = Path.Combine(Application.streamingAssetsPath, "AudioTest");
                    File.Copy(Path.Combine(tests, "tone_440.mp3"), Path.Combine(scratch, "music", "battle1.mp3"));
                    File.Copy(Path.Combine(tests, "tone_330.ogg"), Path.Combine(scratch, "music", "ambient.ogg"));
                    AudioPaths.OverrideUser(scratch);
                    var lib2 = new AudioLibrary();
                    yield return lib2.Load(_host);
                    var bt = lib2.Music["battle2"];
                    Check(lib2.Overrides == 4, $"four files overridden: a WAV, an MP3, an OGG and a recording ({lib2.Overrides})");
                    var mp3 = lib2.Music["battle1"].Clip; var ogg = lib2.Music["ambient"].Clip;
                    Check(mp3 != null && mp3.SampleRate == 44100 && mp3.Seconds > 1.9f && mp3.Seconds < 2.2f && mp3.Channels == 2, $"an MP3 plays instead of battle1 ({(mp3 != null ? mp3.Seconds.ToString("0.000") : "-")} s at {(mp3 != null ? mp3.SampleRate : 0)} Hz; encoder padding included)");
                    Check(ogg != null && ogg.SampleRate == 44100 && Mathf.Abs(ogg.Seconds - 2f) < 0.05f, $"an OGG plays instead of ambient ({(ogg != null ? ogg.Seconds.ToString("0.000") : "-")} s)");
                    // a 44.1 kHz file through the 48 kHz mixer: the pitch must come out right
                    var rig3 = NewRig();
                    rig3.Sys.Mixer.MusicPlay(ogg, Quant.Now, 0f, 0f, false);
                    rig3.Step(1.5f);
                    var rec3 = rig3.Sys.RecordedAudio();
                    int zc = 0; int a3 = R / 4, b3 = R;
                    for (int i = a3; i < b3; i++) if (rec3.Ch[0][i] <= 0f && rec3.Ch[0][i + 1] > 0f) zc++;
                    float hz = zc / ((b3 - a3) / (float)R);
                    Check(Mathf.Abs(hz - 330f) < 8f, $"a 44.1 kHz file keeps its pitch through the 48 kHz mixer ({hz:0} Hz, 330 expected)");
                    Check(bt.Source != "built in" && bt.Clip.Frames == R * 2 && bt.Clip.LoopStart == R / 2 && bt.Clip.LoopEnd == R * 3 / 2 && Mathf.Abs(bt.Clip.TrimDb + 3f) < 0.01f, $"your battle2.wav replaces the built-in track, with its loop points and trim from the sidecar ({bt.Source})");
                    var vc = lib2.Voices.First(v => v.File == "vo/sable2/ack_attack_01_frantic.ogg");
                    var vm = LoudnessMeter.Measure(PcmFrom(vc.Clip));
                    Check(vc.Source != "built in" && vc.Clip.Seconds > 1.0f && vm.Integrated > -30f && vm.Integrated < -17f, $"your recording replaces the clip and goes through the radio ({vc.Clip.Seconds:0.00} s, {vm.Integrated:0.0} LUFS)");
                    // a track the mission names: the player's file plays, a name with no file falls back to the built-in state track at once and says so
                    var rigM = NewRig(true, lib2);
                    rigM.World.Mission = new MissionBoard { Music = "battle2", MusicTrack = "crossing_battle_2" };
                    File.Copy(Path.Combine(scratch, "music", "battle2.wav"), Path.Combine(scratch, "music", "crossing_battle_2.wav"));
                    GameEvents.Raise(MissionEvents.MusicState, "battle2");
                    for (int i = 0; i < 40 && rigM.Sys.Music.State == null; i++) { rigM.Step(0.05f); yield return null; }
                    Check(rigM.Sys.Music.Track == "crossing_battle_2", $"the mission's own track name finds the player's file (playing '{rigM.Sys.Music.Track}')");
                    var rigN = NewRig(true, lib2);
                    rigN.World.Mission = new MissionBoard { Music = "battle3", MusicTrack = "no_such_track" };
                    GameEvents.Raise(MissionEvents.MusicState, "battle3");
                    rigN.Step(0.5f);
                    Check(rigN.Sys.Music.Track == "battle3" && rigN.Sys.Music.Log.Any(l => l.Contains("no_such_track")), "a mission track with no file falls back at once to the built-in one, and the log says why");
                    // delete them: the built-in sound comes back
                    File.Delete(Path.Combine(scratch, "music", "battle2.wav"));
                    float t = 0f;
                    while (t < 6f && bt.Source != "built in") { lib2.Poll(_host, 1000f + t * 3f); t += 0.5f; yield return new WaitForSeconds(0.25f); }
                    Check(bt.Source == "built in" && bt.Clip.Frames > R * 30, "delete your file and the built-in track comes back");
                }
                finally
                {
                    AudioPaths.OverrideUser(prev);
                    try { Directory.Delete(scratch, true); } catch (Exception) { }
                }
            }

            static PcmBuffer PcmFrom(Clip c)
            {
                var b = new PcmBuffer(c.SampleRate, c.Channels, c.Frames);
                for (int i = 0; i < c.Frames; i++) for (int k = 0; k < c.Channels; k++) b.Ch[k][i] = c.Pcm[i * c.Channels + k] / 32768f;
                return b;
            }

            // ------------------------------------------------------------------------------------------------ effects

            IEnumerator Effects()
            {
                Say("== every effect plays");
                var rig = NewRig();
                var csv = new StringBuilder("cue,seconds,peakDb,lufsMomentary\n");
                int silent = 0, n = 0;
                foreach (var cue in _lib.Sfx.Values.OrderBy(c => c.Id))
                {
                    int start = rig.Sys.Mixer.ClippedSamples;
                    int tickStart = rig.Tick;
                    if (cue.Spatial) rig.Sys.PlayCue(cue.Id, new Vector3(25f, 4f, 90f), 1f, 100f); else rig.Sys.PlayCue(cue.Id, Vector3.zero, 1f, 0f);
                    float len = Mathf.Min(cue.Near[0].Seconds, 4f) + 0.3f;
                    rig.Step(len);
                    var rec = rig.Sys.RecordedAudio();
                    int a = tickStart * R / Sim.Hz, cnt = Mathf.Min(rec.Frames - a, (int)(len * R));
                    float peak = 0f; for (int i = a; i < a + cnt; i++) peak = Mathf.Max(peak, Mathf.Abs(rec.Ch[0][i]), Mathf.Abs(rec.Ch[1][i]));
                    var seg = new PcmBuffer(R, 2, cnt); Array.Copy(rec.Ch[0], a, seg.Ch[0], 0, cnt); Array.Copy(rec.Ch[1], a, seg.Ch[1], 0, cnt);
                    var m = LoudnessMeter.Measure(seg);
                    csv.AppendLine($"{cue.Id},{cue.Near[0].Seconds:0.00},{Db(peak):0.0},{m.MaxMomentary:0.0}");
                    n++;
                    rig.Sys.Mixer.StopAll(0.02f); rig.Step(0.15f);           // loops and tails end before the next one is measured
                    if (Db(peak) < -55f) { silent++; Say($"   {cue.Id} is silent through the mixer (peak {Db(peak):0.0} dB)"); }
                    if (n % 8 == 0) yield return null;
                }
                File.WriteAllText(Path.Combine(_out, "effects_levels.csv"), csv.ToString());
                Check(silent == 0, $"{n} effects each make sound through the mixer ({silent} silent)");
                Check(rig.Sys.Mixer.ClippedSamples == 0, "none clipped the master bus");
                int before = rig.Sys.CuesMissing;
                int id = rig.Sys.PlayCue("no_such_cue", Vector3.zero, 1f, 10f);
                Check(id == 0 && rig.Sys.CuesMissing == before + 1, "an unknown cue is refused and logged, not thrown");
            }

            /// <summary>Magnitude of one frequency (a single DFT bin) in a window of a channel: the 100 Hz hum, say, whatever else is playing.</summary>
            static float Tone(float[] x, int from, int n, float hz)
            {
                from = Mathf.Clamp(from, 0, x.Length); n = Mathf.Clamp(n, 0, x.Length - from);
                double re = 0, im = 0, w = 2.0 * Math.PI * hz / R;
                for (int i = 0; i < n; i++) { re += x[from + i] * Math.Cos(w * i); im -= x[from + i] * Math.Sin(w * i); }
                return n > 0 ? (float)(2.0 * Math.Sqrt(re * re + im * im) / n) : 0f;
            }

            IEnumerator Cabin()
            {
                Say("== the cabin: its events and its board");
                const int Tk = R / Sim.Hz;                          // samples a tick
                // 1. every event the cabin documents makes its sound (the real vocabulary, with a board in the world as there is in the game)
                var rig = NewRig();
                var sys = rig.Sys;
                var board = new CabinBoard();
                rig.World.Cabin = board;
                void Ev(string id, string subject, float value) => GameEvents.Raise(id, subject, value);
                Ev("cabin_stand", "stand", 0f); Ev("cabin_step", "floor", 0.8f); Ev("cabin_step", "tread", 0.5f); Ev("cabin_step", "riser", 1f);
                Ev("cabin_power", "off", 0f); Ev("cabin_scan_contact", "hit", 6f); Ev("cabin_scan_contact", "miss", 6f); Ev("cabin_creak", "hull", 0.7f); Ev("cabin_breath", "in", 0.8f); Ev("cabin_breath", "out", 0.6f);
                Ev("cabin_loadout", "needle", 1f); Ev("cabin_table", "page", 2f); Ev("cabin_jolt", "hit", 0.6f); Ev("cabin_power", "on", 1f); Ev("cabin_stand", "sit", 1f);
                Ev("cabin_repair", "start", 0f); rig.Step(0.5f); yield return null; Ev("cabin_repair", "progress", 0.5f); rig.Step(0.5f); Ev("cabin_repair", "done", 1f);
                board.Action = "breaker"; rig.Step(0.2f); yield return null; board.Action = "rack"; rig.Step(0.2f); yield return null; board.Action = null;
                rig.Step(3f);
                string[] want = { "seat_open", "step_plate", "step_grating", "step_rung", "breaker_off", "breaker_on", "power_down", "power_up", "scan_hit", "scan_miss", "creak_c", "breath_in", "breath_out",
                                  "rack_seat", "ui_click", "cabin_rattle", "seat_close", "panel_open", "repair_done", "breaker_lever", "rack_pull" };
                var missing = want.Where(w => !sys.CueCounts.ContainsKey(w)).ToList();
                Check(missing.Count == 0, "every cabin event and action makes its sound" + (missing.Count > 0 ? ": none for " + string.Join(", ", missing) : ""));
                var rec = sys.RecordedAudio();
                var m = LoudnessMeter.Measure(rec);
                Check(m.PeakDb > -40f && sys.Mixer.ClippedSamples == 0, $"the cabin is heard and does not clip (peak {m.PeakDb:0.0} dBFS)");
                WriteWav("cabin_events.wav", rec);

                // 2. the emergency hum swells with the lights going out (the 100 Hz of the transformer, measured whatever else plays)
                rig = NewRig(); sys = rig.Sys; board = new CabinBoard(); rig.World.Cabin = board;
                Ev("cabin_lights", "normal", 0f); board.Lights = "normal";
                for (int i = 0; i < 40; i++) { rig.Step(0.1f); yield return null; }
                float humLit = Tone(sys.RecordedAudio().Ch[0], rig.Tick * Tk - R, R, 100f);
                Ev("cabin_lights", "blackout", 0f); board.Lights = "blackout"; board.Powered = false;
                for (int i = 0; i < 60; i++) { rig.Step(0.1f); yield return null; }
                float humDark = Tone(sys.RecordedAudio().Ch[0], rig.Tick * Tk - R, R, 100f);
                Check(humDark > humLit * 3f && humDark > 0.0005f, $"the emergency hum comes up when the breaker goes out ({Db(humLit):0.0} dB at 100 Hz lit, {Db(humDark):0.0} dB in the blackout)");

                // 3. the breath is louder in the dark, and in a scan (the energy a breath adds over what was playing before it)
                float BreathEnergy(string light, float scanLight)
                {
                    var r = NewRig(); var sy = r.Sys; var bd = new CabinBoard(); r.World.Cabin = bd;
                    Ev("cabin_lights", light, 0f); bd.Lights = light; bd.ScanLight = scanLight;
                    for (int i = 0; i < 80; i++) { r.Step(0.1f); }
                    int t0 = r.Tick * Tk;
                    Ev("cabin_breath", "in", 0.7f);
                    r.Step(1.6f);
                    int t1 = r.Tick * Tk;
                    var x = sy.RecordedAudio().Ch[0];
                    float after = Rms(x, t0, t1 - t0), before = Rms(x, t0 - (t1 - t0), t1 - t0);
                    float excess = Mathf.Max(after * after - before * before, 1e-12f);
                    return 10f * Mathf.Log10(excess);
                }
                float bLit = BreathEnergy("normal", 0f), bDark = BreathEnergy("dark", 0f), bBlack = BreathEnergy("blackout", 0f), bScan = BreathEnergy("blackout", 1f);
                Say($"   breath energy over the room: lit {bLit:0.0} dB, dark {bDark:0.0}, blackout {bBlack:0.0}, blackout in a scan {bScan:0.0}");
                Check(bDark > bLit + 2f && bBlack > bDark + 1f && bScan > bBlack + 0.5f, "your own breathing gets louder as the cabin gets darker, and louder again while a scan crosses");
                yield return null;

                // 4. a scan crossing the hull is heard through it, on the side the beam is
                rig = NewRig(); sys = rig.Sys; board = new CabinBoard(); rig.World.Cabin = board;
                Ev("cabin_lights", "blackout", 0f); board.Lights = "blackout"; board.Powered = false;
                board.ScanLight = 1f; board.ScanDirShip = new Vector3(1f, 0f, 0.3f);
                for (int i = 0; i < 40; i++) { rig.Step(0.1f); yield return null; }
                var rc = sys.RecordedAudio();
                int w1 = rig.Tick * Tk - R;
                float lRight = Tone(rc.Ch[0], w1, R, 149.8f), rRight = Tone(rc.Ch[1], w1, R, 149.8f);
                board.ScanDirShip = new Vector3(-1f, 0f, 0.3f);
                for (int i = 0; i < 40; i++) { rig.Step(0.1f); yield return null; }
                rc = sys.RecordedAudio();
                int w2 = rig.Tick * Tk - R;
                float lLeft = Tone(rc.Ch[0], w2, R, 149.8f), rLeft = Tone(rc.Ch[1], w2, R, 149.8f);
                Check(rRight > lRight * 1.4f && lLeft > rLeft * 1.4f, $"the scan's moan through the hull follows the beam (beam to starboard: {Db(lRight):0.0} dB left, {Db(rRight):0.0} dB right; to port: {Db(lLeft):0.0} left, {Db(rLeft):0.0} right)");
                board.ScanLight = 0f;
                for (int i = 0; i < 60; i++) { rig.Step(0.1f); yield return null; }
                float gone = Tone(sys.RecordedAudio().Ch[0], rig.Tick * Tk - R, R, 149.8f);
                Check(gone < Mathf.Max(lLeft, rRight) * 0.3f, $"and it dies away with the light ({Db(gone):0.0} dB)");

                // 5. the heart, in the dark, faster while a scan crosses
                int calm = 0, scared = 0;
                {
                    var r1 = NewRig(); var b1 = new CabinBoard(); r1.World.Cabin = b1; Ev("cabin_lights", "blackout", 0f); b1.Lights = "blackout";
                    for (int i = 0; i < 80; i++) { r1.Step(0.1f); yield return null; }
                    calm = r1.Sys.CueCounts.TryGetValue("heartbeat", out int c) ? c : 0;
                    var r2 = NewRig(); var b2 = new CabinBoard(); r2.World.Cabin = b2; Ev("cabin_lights", "blackout", 0f); b2.Lights = "blackout"; b2.ScanLight = 1f;
                    for (int i = 0; i < 80; i++) { r2.Step(0.1f); yield return null; }
                    scared = r2.Sys.CueCounts.TryGetValue("heartbeat", out int d) ? d : 0;
                }
                Check(calm >= 5 && scared > calm + 2, $"a heartbeat in the dark, quicker while a scan crosses ({calm} beats in 8 s, {scared} in a scan)");
                yield return null;
            }

            // ------------------------------------------------------------------------------------------------ the mix

            IEnumerator Mix()
            {
                Say("== a scripted battle: battle3, a volley, explosions, a radio line");
                var rig = NewRig(false);                 // the player's default volumes
                var sys = rig.Sys;
                GameEvents.Raise(MissionEvents.MusicState, "battle3");
                rig.Step(4f);
                var rnd = new System.Random(5);
                var cpu = System.Diagnostics.Stopwatch.StartNew();
                float duckMid = 0f, duckAfter = 0f, duckBig = 0f, sfxDuckMid = 0f;
                string[] guns = { "laser_light", "laser_enemy", "laser_heavy", "laser_turret", "flak_burst", "hit_shield", "hit_hull", "near_miss" };
                float tVolley0 = rig.Now;
                for (int i = 0; i < 12 * 60; i++)
                {
                    // thirty or so sounds a second, at random places and distances
                    if (i % 2 == 0) { var g = guns[rnd.Next(guns.Length)]; var p = new Vector3(rnd.Next(-400, 400), rnd.Next(-80, 80), rnd.Next(-200, 900)); sys.PlayCue(g, p, 0.4f + 0.6f * (float)rnd.NextDouble(), -1f); }
                    if (i == 3 * 60) { sys.PlayCue("explosion_medium", new Vector3(120, 0, 600), 0.9f, 620f); }
                    if (i == 6 * 60) GameEvents.Raise(CombatEvents.RadioLine, "SMALL MERCY: BOMBERS INBOUND ON THE MERCY. SOMEBODY DEAL WITH THEM.", 0f, GameWorld.NoShip);
                    if (i == 9 * 60) { sys.PlayCue("breakup", new Vector3(-200, 20, 1200), 1f, 1220f); sys.PlayCue("explosion_large", new Vector3(-200, 20, 1200), 1f, 1220f); }
                    if (i == 9 * 60 + 40) sys.PlayCue("shockwave", new Vector3(-200, 20, 1200), 1f, 1220f);
                    rig.Step(1f / 60f);
                    if (i == 7 * 60) { duckMid = sys.Mixer.LastMusicDuckDb; sfxDuckMid = sys.Mixer.LastSfxDuckDb; }
                    if (i == 9 * 60 + 20) duckBig = sys.Mixer.LastMusicDuckDb;
                    if (i % 120 == 0) yield return null;
                }
                rig.Step(6f);
                duckAfter = sys.Mixer.LastMusicDuckDb;
                cpu.Stop();
                var rec = sys.RecordedAudio();
                WriteWav("mix_battle.wav", rec);
                var mt = LoudnessMeter.Measure(rec);
                var mx = sys.Mixer;
                Check(mx.ClippedSamples == 0, $"no clipped samples on the master bus (limiter reduced by up to {Db(mx.MinLimiterGain):0.0} dB)");
                Check(mt.PeakDb <= -0.9f, $"master peak {mt.PeakDb:0.0} dBFS under the ceiling");
                Check(mt.Integrated > -27f && mt.Integrated < -14f, $"the battle mix is {mt.Integrated:0.0} LUFS integrated (bounds -27 to -14)");
                Check(mt.MaxMomentary < -9f, $"loudest 400 ms {mt.MaxMomentary:0.0} LUFS (under -9)");
                // the voice sits above the music: loudness of the music alone over the window before, the whole mix during
                int w0 = Mathf.RoundToInt((tVolley0 + 6f) * R), w1 = w0 + R * 3;
                var seg = new PcmBuffer(R, 2, w1 - w0); Array.Copy(rec.Ch[0], w0, seg.Ch[0], 0, w1 - w0); Array.Copy(rec.Ch[1], w0, seg.Ch[1], 0, w1 - w0);
                Say($"   during the radio line: {LoudnessMeter.Measure(seg).Integrated:0.0} LUFS");
                // the ducking: with the voice quiet (a voice-only run) compare music-bus level on its own
                Say($"   bus peaks (dBFS): sfx {Db(mx.BusPeak[(int)Bus.Sfx]):0.0}, big {Db(mx.BusPeak[(int)Bus.SfxBig]):0.0}, music {Db(mx.BusPeak[(int)Bus.Music]):0.0}, voice {Db(mx.BusPeak[(int)Bus.Voice]):0.0}");
                Check(sys.Radio.Voiced == 1, "the radio line in the middle of the battle was voiced");
                Check(duckMid <= -7.5f && duckMid >= -13f, $"while the voice speaks the music is {duckMid:0.0} dB down and the effects {sfxDuckMid:0.0} dB (music -9 and effects -4.5 asked, plus the explosion's own duck)");
                Check(duckBig <= -5f, $"a capital ship's death ducks the music by {duckBig:0.0} dB");
                Check(duckAfter > -1.0f, $"the music is back to {duckAfter:0.0} dB down some seconds after the last big sound");
                float secs = 12f + 4f + 6f;
                Say($"   the mixer and the checks' own work: {cpu.ElapsedMilliseconds} ms for {secs:0} s of battle ({cpu.ElapsedMilliseconds / secs:0.0} ms per second of sound, sim steps included)");
                Check(cpu.ElapsedMilliseconds / secs < 120f, "mixing, the sim steps and the recording together stay under 12% of a core (the audio thread alone is measured in the player: -mnAudioLog)");
            }

            void Prefs()
            {
                Say("== settings");
                string path = Path.Combine(Application.temporaryCachePath, "audio_prefs_test.json");
                var p = new AudioPrefs { master = 0.55f, music = 0.35f, effects = 0.9f, voice = 0.75f, mute = true };
                p.Save(path);
                var q = AudioPrefs.Load(path);
                Check(Mathf.Approximately(q.master, 0.55f) && Mathf.Approximately(q.music, 0.35f) && Mathf.Approximately(q.effects, 0.9f) && Mathf.Approximately(q.voice, 0.75f) && q.mute, "volume settings save and load");
                try { File.Delete(path); } catch (Exception) { }
            }
        }
    }
}
