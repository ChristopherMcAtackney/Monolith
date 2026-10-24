using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    public sealed class TrackSpec
    {
        public string Patch;
        public float Db, Pan;
        public float? Hall, Plate, Delay, Gate, Sat, Width;
        public string Chorus;
        public bool Mute;
        public float? HpHz, LpHz;
    }

    public sealed class MasterSpec
    {
        public float HallRt60 = 4.5f, HallSize = 1.25f, HallDamp = 0.45f, HallPre = 28f, HallDb = -3f;
        public float PlateDecay = 0.84f, PlateDamp = 0.35f, PlateDb = -6f;
        public float DelayBeats = 0.75f, DelayFeedback = 0.42f, DelayDb = -8f;
        public float GateRt60 = 1.5f, GateHold = 0.19f, GateDb = -2f;
        public float CompThresh = -16f, CompRatio = 2.2f, CompAttack = 28f, CompRelease = 260f, CompMakeup = 1.5f;
        public float Sat = 0.12f, Width = 1.05f;
        public float LowShelfDb = 0f, HighShelfDb = -1f;
    }

    public sealed class CueSpec
    {
        public string Name, Midi;
        public bool Loop;
        public float Tail = 0f;               // seconds of reverb tail after a one-shot's last event
        public double Lufs = -22.0;           // integrated loudness target for the finished cue
        public int Bars;                      // loops: the length in bars (4/4)
        public int Seed = 1;
        public string Track;                  // the state or sting this cue is for (the runtime catalog)
        public Dictionary<string, TrackSpec> Tracks = new Dictionary<string, TrackSpec>();
        public MasterSpec Master = new MasterSpec();
        public float FadeOutSeconds = 0.25f;
        [System.Text.Json.Serialization.JsonIgnore] public string[] SoloTracks, MuteTracks;
    }

    public sealed class CueReport
    {
        public string Name;
        public double Seconds, Bpm, Lufs, TruePeakDb, SamplePeakDb, LoopSeamStep, LoopSeamBaseline;
        public int Bars, Notes;
        public bool Loop;
    }

    public static class MusicRender
    {
        public const int Rate = 48000;

        public static Dictionary<string, CueSpec> LoadSpecs(string path)
        {
            var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            var res = new Dictionary<string, CueSpec>();
            foreach (var e in doc.RootElement.GetProperty("cues").EnumerateObject())
            {
                var spec = e.Value.Deserialize<CueSpec>(opt);
                spec.Name = e.Name;
                res[e.Name] = spec;
            }
            return res;
        }

        static float PanL(float pan) => MathF.Cos((pan + 1f) * 0.25f * Dsp.Pi) * 1.41421356f;
        static float PanR(float pan) => MathF.Sin((pan + 1f) * 0.25f * Dsp.Pi) * 1.41421356f;

        /// <summary>Render one cue to a stereo buffer: for a loop, exactly one pass of the loop (the second of two, so tails wrap round); for a one-shot, the piece and its tail.</summary>
        public static PcmBuffer Render(CueSpec spec, string midiDir, out CueReport report, bool verbose = true)
        {
            Patches.Calibrate(Rate);
            var song = MidiSong.Load(Path.Combine(midiDir, spec.Midi));
            double bpm = song.Bpm;
            double beatSec = 60.0 / bpm;
            double songSec;
            if (spec.Loop)
            {
                int bars = spec.Bars > 0 ? spec.Bars : (int)Math.Round(song.LengthBeats / 4.0);
                spec.Bars = bars;
                songSec = bars * 4 * beatSec;
            }
            else songSec = song.Seconds;
            int passes = spec.Loop ? 2 : 1;
            int lenSamples = (int)Math.Round(songSec * Rate);
            int total = lenSamples * passes + (spec.Loop ? 0 : (int)(spec.Tail * Rate));
            var m = spec.Master;
            if (verbose) Console.WriteLine($"  {spec.Name}: {song.Tracks.Count} tracks, {bpm:0.#} BPM, {songSec:0.00} s{(spec.Loop ? " x2 passes (loop)" : $" + {spec.Tail:0.0} s tail")}");

            // buses
            float[] Z() => new float[total];
            float[] dryL = Z(), dryR = Z(), hallL = Z(), hallR = Z(), plateL = Z(), plateR = Z(), delL = Z(), delR = Z(), gateL = Z(), gateR = Z();
            var gateEnv = new float[total];
            int noteCount = 0;

            foreach (var tr in song.Tracks)
            {
                if (tr.Notes.Count == 0) continue;
                string tname = tr.Name;
                string patchName = tname.Contains(":") ? tname.Substring(0, tname.IndexOf(':')) : tname;
                bool isDrums = tr.IsDrums || patchName == "drums";
                spec.Tracks.TryGetValue(tname, out var ts);
                ts ??= new TrackSpec();
                if (ts.Mute) continue;
                if (spec.SoloTracks != null && Array.IndexOf(spec.SoloTracks, tname) < 0) continue;
                if (spec.MuteTracks != null && Array.IndexOf(spec.MuteTracks, tname) >= 0) continue;

                // notes and automation, repeated for each pass
                var notes = new List<MidiNote>();
                var au = new TrackAuto();
                for (int ps = 0; ps < passes; ps++)
                {
                    double off = ps * songSec;
                    foreach (var n in tr.Notes)
                        notes.Add(new MidiNote { Start = n.Start + off, Dur = n.Dur, Key = n.Key, Vel = n.Vel, Id = n.Id, Channel = n.Channel });
                    AppendCurve(au.Expression, tr, 11, 1f, off);
                    AppendCurve(au.Mod, tr, 1, 0f, off);
                    AppendCurve(au.Cutoff, tr, 74, 0.5f, off);
                    AppendCurve(au.Pan, tr, 10, 0.5f, off);
                    AppendCurve(au.Vowel, tr, 20, 0f, off);
                    for (int i = 0; i < tr.Bend.T.Count; i++) au.Bend.Add(tr.Bend.T[i] + off, tr.Bend.V[i]);
                }
                noteCount += tr.Notes.Count;

                float trackGain = Db.ToLin(ts.Db);
                float pan = ts.Pan;
                if (isDrums)
                {
                    foreach (var n in notes)
                    {
                        int at = (int)Math.Round(n.Start * Rate);
                        if (at >= total) continue;
                        var hit = Drums.Hit(n.Key, n.Vel, Rate, (uint)(spec.Seed * 7919 + n.Id));
                        var (sg, sh, sp) = Drums.Sends(n.Key);
                        float hv = (n.Vel / 127f);
                        float dpan = DrumPan(n.Key) + pan;
                        float gl = PanL(Dsp.Clamp(dpan, -1f, 1f)) * trackGain, gr = PanR(Dsp.Clamp(dpan, -1f, 1f)) * trackGain;
                        if (ts.Gate.HasValue) sg *= ts.Gate.Value; if (ts.Hall.HasValue) sh *= ts.Hall.Value; if (ts.Plate.HasValue) sp *= ts.Plate.Value;
                        for (int i = 0; i < hit.Length && at + i < total; i++)
                        {
                            float x = hit[i];
                            dryL[at + i] += x * gl; dryR[at + i] += x * gr;
                            if (sg > 0f) { gateL[at + i] += x * gl * sg; gateR[at + i] += x * gr * sg; }
                            if (sh > 0f) { hallL[at + i] += x * gl * sh; hallR[at + i] += x * gr * sh; }
                            if (sp > 0f) { plateL[at + i] += x * gl * sp; plateR[at + i] += x * gr * sp; }
                        }
                        if (sg > 0f && hv > 0.3f)
                        {
                            // the gate: open for the hold time at the hit, then closing fast (the Collins gated-reverb snare)
                            int hold = (int)(m.GateHold * Rate), rel = (int)(0.035f * Rate);
                            for (int i = 0; i < hold + rel && at + i < total; i++)
                            {
                                float e = i < hold ? 1f : 1f - (i - hold) / (float)rel;
                                if (e > gateEnv[at + i]) gateEnv[at + i] = e;
                            }
                        }
                    }
                    continue;
                }

                var patch = Patches.Get(patchName);
                var tl = new float[total]; var tr2 = new float[total];
                // sort by start; monophonic patches cut each note at the next one and glide
                var order = notes.OrderBy(n => n.Start).ThenBy(n => n.Key).ToList();
                double lastKey = -1, lastEnd = -1e9;
                for (int ni = 0; ni < order.Count; ni++)
                {
                    var n = order[ni];
                    double endCut = 0;
                    if (patch.Mono)
                    {
                        for (int nj = ni + 1; nj < order.Count; nj++)
                            if (order[nj].Start > n.Start + 1e-6) { if (order[nj].Start < n.Start + n.Dur) endCut = order[nj].Start + 0.004; break; }
                    }
                    double gap = n.Start - lastEnd;
                    uint seed = Osc.Hash((uint)spec.Seed, (uint)n.Id, Dsp.StableHash(patchName) & 0xFFFF);
                    SynthVoice.Render(patch, n, lastKey, gap, au, tl, tr2, Rate, seed, endCut);
                    lastKey = n.Key; lastEnd = endCut > 0 ? endCut : n.Start + n.Dur;
                }

                // inserts
                float sat = ts.Sat ?? patch.Sat;
                string chorus = ts.Chorus ?? patch.Chorus;
                Fx.Saturate(tl, tr2, sat);
                Fx.Chorus(tl, tr2, Rate, chorus, spec.Loop ? songSec : 0.0);
                if (ts.HpHz.HasValue) Fx.Eq(tl, tr2, Rate, new Fx.Band(BiquadType.HighPass, ts.HpHz.Value, 0.7f));
                if (ts.LpHz.HasValue) Fx.Eq(tl, tr2, Rate, new Fx.Band(BiquadType.LowPass, ts.LpHz.Value, 0.7f));
                if (ts.Width.HasValue) Fx.Widen(tl, tr2, ts.Width.Value);

                float sh2 = ts.Hall ?? patch.HallSend, sp2 = ts.Plate ?? patch.PlateSend, sd2 = ts.Delay ?? patch.DelaySend;
                float l = PanL(Dsp.Clamp(pan, -1f, 1f)) * trackGain, r = PanR(Dsp.Clamp(pan, -1f, 1f)) * trackGain;
                // pan a stereo signal as a balance
                float bl = pan <= 0f ? 1f : 1f - pan, br = pan >= 0f ? 1f : 1f + pan;
                for (int i = 0; i < total; i++)
                {
                    float a = tl[i] * trackGain * bl, b = tr2[i] * trackGain * br;
                    dryL[i] += a; dryR[i] += b;
                    if (sh2 > 0f) { hallL[i] += a * sh2; hallR[i] += b * sh2; }
                    if (sp2 > 0f) { plateL[i] += a * sp2; plateR[i] += b * sp2; }
                    if (sd2 > 0f) { delL[i] += a * sd2; delR[i] += b * sd2; }
                }
            }

            // returns
            var oL = new float[total]; var oR = new float[total];
            Array.Copy(dryL, oL, total); Array.Copy(dryR, oR, total);
            var wl = new float[total]; var wr = new float[total];
            Fx.Hall(hallL, hallR, wl, wr, Rate, m.HallRt60, m.HallSize, m.HallDamp, m.HallPre);
            Fx.Add(oL, oR, wl, wr, Db.ToLin(m.HallDb));
            Fx.Plate(plateL, plateR, wl, wr, Rate, m.PlateDecay, m.PlateDamp, 8f);
            Fx.Add(oL, oR, wl, wr, Db.ToLin(m.PlateDb));
            Fx.PingPong(delL, delR, wl, wr, Rate, m.DelayBeats * beatSec, m.DelayFeedback);
            Fx.Add(oL, oR, wl, wr, Db.ToLin(m.DelayDb));
            // the gated reverb: a bright dense hall, gated
            Fx.Hall(gateL, gateR, wl, wr, Rate, m.GateRt60, 0.55f, 0.12f, 0f, 200f);
            for (int i = 0; i < total; i++) { wl[i] *= gateEnv[i]; wr[i] *= gateEnv[i]; }
            Fx.Add(oL, oR, wl, wr, Db.ToLin(m.GateDb));

            // master
            Fx.Eq(oL, oR, Rate, new Fx.Band(BiquadType.HighPass, 26f, 0.7f), new Fx.Band(BiquadType.HighShelf, 7500f, 0.7f, m.HighShelfDb), new Fx.Band(BiquadType.LowShelf, 90f, 0.7f, m.LowShelfDb));
            Fx.Compress(oL, oR, Rate, m.CompThresh, m.CompRatio, m.CompAttack, m.CompRelease, m.CompMakeup);
            Fx.Saturate(oL, oR, m.Sat, 0.02f);
            if (Math.Abs(m.Width - 1f) > 0.001f) Fx.Widen(oL, oR, m.Width);

            // extract
            int start = spec.Loop ? lenSamples : 0;
            int outLen = spec.Loop ? lenSamples : total;
            var outBuf = new PcmBuffer(Rate, 2, outLen);
            Array.Copy(oL, start, outBuf.Ch[0], 0, outLen);
            Array.Copy(oR, start, outBuf.Ch[1], 0, outLen);

            // loudness and peak
            var meter = LoudnessMeter.Measure(outBuf);
            float gainDb = spec.Lufs < -100 ? 0f : (float)spec.Lufs - meter.Integrated;
            outBuf.Scale(Db.ToLin(gainDb));
            var lim = new Limiter(Rate, 2, -1.2f, 3f, 180f);
            if (spec.Loop)
            {
                // limit with the loop wrapped round: prime on the end of the loop, and let the start follow so the seam is not squeezed
                int prime = Math.Min(Rate, outLen), post = Math.Min(lim.Latency + 16, outLen);
                var tmp = new PcmBuffer(Rate, 2, prime + outLen + post);
                for (int c = 0; c < 2; c++)
                {
                    Array.Copy(outBuf.Ch[c], outLen - prime, tmp.Ch[c], 0, prime);
                    Array.Copy(outBuf.Ch[c], 0, tmp.Ch[c], prime, outLen);
                    Array.Copy(outBuf.Ch[c], 0, tmp.Ch[c], prime + outLen, post);
                }
                lim.Process(tmp.Ch, 0, tmp.Frames);
                for (int c = 0; c < 2; c++) Array.Copy(tmp.Ch[c], prime + lim.Latency, outBuf.Ch[c], 0, outLen);
            }
            else
            {
                var tmp = new PcmBuffer(Rate, 2, outLen + lim.Latency);
                for (int c = 0; c < 2; c++) Array.Copy(outBuf.Ch[c], tmp.Ch[c], outLen);
                lim.Process(tmp.Ch, 0, tmp.Frames);
                for (int c = 0; c < 2; c++) Array.Copy(tmp.Ch[c], lim.Latency, outBuf.Ch[c], 0, outLen);
                // clean start and end
                int fi = (int)(0.003 * Rate), fo = (int)(spec.FadeOutSeconds * Rate);
                for (int c = 0; c < 2; c++)
                {
                    for (int i = 0; i < fi && i < outLen; i++) outBuf.Ch[c][i] *= i / (float)fi;
                    for (int i = 0; i < fo && i < outLen; i++) { float g = i / (float)fo; outBuf.Ch[c][outLen - 1 - i] *= g * g; }
                }
            }

            var fm = LoudnessMeter.Measure(outBuf);
            report = new CueReport
            {
                Name = spec.Name, Seconds = outLen / (double)Rate, Bpm = bpm, Lufs = fm.Integrated, SamplePeakDb = fm.PeakDb,
                TruePeakDb = LoudnessMeter.TruePeakDb(outBuf), Bars = spec.Bars, Notes = noteCount, Loop = spec.Loop,
            };
            if (spec.Loop) SeamCheck(outBuf, out report.LoopSeamStep, out report.LoopSeamBaseline);
            return outBuf;
        }

        static float DrumPan(int key)
        {
            switch (key)
            {
                case 41: return -0.45f; case 43: return -0.25f; case 45: return -0.05f; case 47: return 0.15f; case 48: return 0.3f; case 50: return 0.5f;
                case 42: case 44: return 0.3f; case 46: return 0.35f; case 49: return -0.3f; case 57: return 0.3f; case 51: return 0.4f; case 56: return -0.4f;
                default: return 0f;
            }
        }

        static void AppendCurve(Curve dst, MidiTrack tr, int cc, float def, double off)
        {
            dst.Default = def;
            if (tr.Cc.TryGetValue(cc, out var c)) dst.Append(c, off);
        }

        /// <summary>How big is the step from the last sample to the first, against the biggest step seen elsewhere (a click shows as a ratio well above 1)?</summary>
        public static void SeamCheck(PcmBuffer b, out double seamStep, out double baseline)
        {
            seamStep = 0; baseline = 0;
            int n = b.Frames;
            for (int c = 0; c < b.Channels; c++)
            {
                var x = b.Ch[c];
                // second difference of the seam against the p99.9 of second differences elsewhere
                double seam = Math.Abs(x[0] - 2 * x[n - 1] + x[n - 2]);
                seam = Math.Max(seam, Math.Abs(x[1] - 2 * x[0] + x[n - 1]));
                var d = new List<float>(n / 16);
                for (int i = 2; i < n - 2; i += 7) d.Add(MathF.Abs(x[i + 1] - 2 * x[i] + x[i - 1]));
                d.Sort();
                double p = d[(int)(d.Count * 0.999)];
                seamStep = Math.Max(seamStep, seam); baseline = Math.Max(baseline, p);
            }
        }
    }
}
