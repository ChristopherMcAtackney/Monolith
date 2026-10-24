using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Monolith.AudioTools
{
    public sealed class MidiNote
    {
        public double Start, Dur;       // seconds
        public int Key, Vel, Channel, Id;
        public double StartBeat, DurBeat;
    }

    /// <summary>A controller curve (CC value 0..1 or pitch bend -1..1) over time, linearly interpolated, held after the last point.</summary>
    public sealed class Curve
    {
        public readonly List<double> T = new List<double>();
        public readonly List<float> V = new List<float>();
        int _hint;
        public float Default;

        public void Add(double t, float v) { T.Add(t); V.Add(v); }
        public bool Empty => T.Count == 0;

        public float Eval(double t)
        {
            if (T.Count == 0) return Default;
            if (t <= T[0]) return V[0];
            if (t >= T[T.Count - 1]) return V[V.Count - 1];
            int i = Math.Min(_hint, T.Count - 2);
            while (i > 0 && T[i] > t) i--;
            while (i < T.Count - 2 && T[i + 1] <= t) i++;
            _hint = i;
            double a = T[i], b = T[i + 1];
            if (b - a < 1e-9) return V[i + 1];
            double f = (t - a) / (b - a);
            return (float)(V[i] + (V[i + 1] - V[i]) * f);
        }

        public Curve Shifted(double dt)
        {
            var c = new Curve { Default = Default };
            for (int i = 0; i < T.Count; i++) c.Add(T[i] + dt, V[i]);
            return c;
        }

        public void Append(Curve o, double dt) { for (int i = 0; i < o.T.Count; i++) Add(o.T[i] + dt, o.V[i]); }
    }

    public sealed class MidiTrack
    {
        public string Name = "";
        public int Program = -1;
        public List<MidiNote> Notes = new List<MidiNote>();
        public Dictionary<int, Curve> Cc = new Dictionary<int, Curve>();     // 0..1
        public Curve Bend = new Curve();                                       // -1..1 (times the bend range)
        public bool IsDrums;

        public Curve CcCurve(int cc, float def)
        {
            if (!Cc.TryGetValue(cc, out var c)) { c = new Curve { Default = def }; Cc[cc] = c; }
            return c;
        }
    }

    public sealed class MidiSong
    {
        public int Ppq = 480;
        public List<(long tick, double usPerQuarter)> Tempos = new List<(long, double)>();
        public List<MidiTrack> Tracks = new List<MidiTrack>();
        public long LengthTicks;
        public double LengthBeats => (double)LengthTicks / Ppq;
        public double Bpm => Tempos.Count == 0 ? 120.0 : 60e6 / Tempos[0].usPerQuarter;

        public double TickToSeconds(long tick)
        {
            double sec = 0; long last = 0; double us = 500000;
            foreach (var (t, u) in Tempos)
            {
                if (t >= tick) break;
                sec += (t - last) * us / Ppq / 1e6; last = t; us = u;
            }
            return sec + (tick - last) * us / Ppq / 1e6;
        }

        public double Seconds => TickToSeconds(LengthTicks);

        public static MidiSong Load(string path) => Parse(File.ReadAllBytes(path));

        public static MidiSong Parse(byte[] d)
        {
            int p = 0;
            string Id() { var s = Encoding.ASCII.GetString(d, p, 4); p += 4; return s; }
            int I32() { int v = d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]; p += 4; return v; }
            int I16() { int v = d[p] << 8 | d[p + 1]; p += 2; return v; }
            if (Id() != "MThd") throw new InvalidDataException("not a MIDI file");
            int hlen = I32(); int fmt = I16(); int ntr = I16(); int div = I16();
            p = 8 + hlen;
            var song = new MidiSong { Ppq = div & 0x7FFF };
            for (int t = 0; t < ntr; t++)
            {
                if (p + 8 > d.Length) break;
                string id = Id(); int len = I32();
                int end = p + len;
                if (id != "MTrk") { p = end; continue; }
                var track = new MidiTrack();
                long tick = 0; int running = 0;
                var open = new Dictionary<int, (long tick, int vel)>();
                int idc = 0;
                while (p < end)
                {
                    long delta = 0; int b;
                    do { b = d[p++]; delta = (delta << 7) | (uint)(b & 0x7F); } while ((b & 0x80) != 0);
                    tick += delta;
                    int status = d[p];
                    if (status < 0x80) status = running; else { p++; if (status < 0xF0) running = status; }
                    if (status == 0xFF)
                    {
                        int type = d[p++]; int ml = 0; do { b = d[p++]; ml = (ml << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                        if (type == 0x03) track.Name = Encoding.ASCII.GetString(d, p, ml);
                        else if (type == 0x51 && ml == 3) song.Tempos.Add((tick, d[p] << 16 | d[p + 1] << 8 | d[p + 2]));
                        p += ml;
                        if (type == 0x2F) break;
                    }
                    else if (status == 0xF0 || status == 0xF7)
                    {
                        int ml = 0; do { b = d[p++]; ml = (ml << 7) | (b & 0x7F); } while ((b & 0x80) != 0); p += ml;
                    }
                    else
                    {
                        int hi = status & 0xF0, ch = status & 0x0F;
                        int a = d[p++]; int c = (hi == 0xC0 || hi == 0xD0) ? 0 : d[p++];
                        switch (hi)
                        {
                            case 0x90 when c > 0:
                                if (open.TryGetValue(ch * 128 + a, out var o0)) CloseNote(song, track, o0, ch, a, tick, ref idc);
                                open[ch * 128 + a] = (tick, c); break;
                            case 0x80:
                            case 0x90:
                                if (open.TryGetValue(ch * 128 + a, out var o)) { CloseNote(song, track, o, ch, a, tick, ref idc); open.Remove(ch * 128 + a); }
                                break;
                            case 0xB0:
                                track.CcCurve(a, 0f).Add(tick, c / 127f);       // ticks for now; converted below
                                break;
                            case 0xE0: track.Bend.Add(tick, ((c << 7 | a) - 8192) / 8192f); break;
                            case 0xC0: track.Program = a; break;
                        }
                        if (ch == 9) track.IsDrums = true;
                    }
                }
                foreach (var kv in open) CloseNote(song, track, kv.Value, kv.Key / 128, kv.Key % 128, tick, ref idc);
                song.LengthTicks = Math.Max(song.LengthTicks, tick);
                song.Tracks.Add(track);
                p = end;
            }
            if (song.Tempos.Count == 0) song.Tempos.Add((0, 500000));
            song.Tempos.Sort((x, y) => x.tick.CompareTo(y.tick));
            // convert curve times from ticks to seconds
            foreach (var tr in song.Tracks)
            {
                foreach (var cv in tr.Cc.Values) for (int i = 0; i < cv.T.Count; i++) cv.T[i] = song.TickToSeconds((long)cv.T[i]);
                for (int i = 0; i < tr.Bend.T.Count; i++) tr.Bend.T[i] = song.TickToSeconds((long)tr.Bend.T[i]);
                foreach (var n in tr.Notes) { n.Start = song.TickToSeconds((long)(n.StartBeat * song.Ppq)); n.Dur = song.TickToSeconds((long)((n.StartBeat + n.DurBeat) * song.Ppq)) - n.Start; }
                tr.Notes.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : x.Key.CompareTo(y.Key));
                for (int i = 0; i < tr.Notes.Count; i++) tr.Notes[i].Id = i;
            }
            return song;
        }

        static void CloseNote(MidiSong s, MidiTrack tr, (long tick, int vel) o, int ch, int key, long end, ref int id)
        {
            if (end <= o.tick) return;
            tr.Notes.Add(new MidiNote { Key = key, Vel = o.vel, Channel = ch, Id = id++, StartBeat = (double)o.tick / s.Ppq, DurBeat = (double)(end - o.tick) / s.Ppq });
        }
    }
}
