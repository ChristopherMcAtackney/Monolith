using System;
using System.Collections.Generic;

namespace Monolith.Audio
{
    public sealed partial class Mixer
    {
        // ------------------------------------------------------------------------------------------------ the music deck
        //
        // Every music loop of the score is the same length (32 bars at the same tempo) and the same harmony, so "the music changes" is a
        // cross-fade between aligned loops: the new one starts at the position the old one has reached, on the next beat or bar of the music
        // clock. A loop from the player's own MP3 that is not that length starts from its beginning instead, on the grid. Stings lay over
        // the top and dip the music a little while they sound. Victory, failure and death play once.

        sealed class MusicLayer
        {
            public Clip Clip;
            public double Pos, SrcRatio;
            public float Gain, GainTarget, GainStep, Level;
            public bool Loop, Sting, Pending, Dying, Done, Aligned;
            /// <summary>-1 for the base score; the theme's slot (the conductor numbers them) for a theme laid over it.</summary>
            public int Theme = -1;
            /// <summary>Paused: the gain is faded to nothing and the position stops where it is, until the layer is released (a fight theme under an event theme, or in a lull).</summary>
            public bool Held;
            /// <summary>For a theme: the factor the base is lowered to while it plays in full (0 silences the base, 0.3 is about -10 dB).</summary>
            public float DuckLin = 1f;
            public long StartClock;
            public int FadeInFrames, FadeOutFrames;
            public float StingDuckDb;
        }

        readonly List<MusicLayer> _layers = new List<MusicLayer>(8);
        long _clock;
        double _fpBeat;
        int _beatsPerBar = 4;
        long _loopFrames;
        float _stingDuckTarget = 1f;

        void InitMusic() { SetTempo(100f, 4); }

        void SetTempo(float bpm, int beatsPerBar)
        {
            _fpBeat = 60.0 / Math.Max(bpm, 20f) * _rate;
            _beatsPerBar = Math.Max(1, beatsPerBar);
        }

        /// <summary>The grid the music changes on (tempo and beats per bar) and the length of the score's loops. Set once from the score's table.</summary>
        public void SetMusicTempo(float bpm, int beatsPerBar = 4, float loopSeconds = 0f) => _cmds.Enqueue(new Cmd { Type = CmdType.MusicTempo, A = bpm, Slot = beatsPerBar, B = loopSeconds });

        /// <summary>Change the music to <paramref name="clip"/>: cross-fade at the next beat or bar. A looping clip with the same length as the others is joined in step.</summary>
        public void MusicPlay(Clip clip, Quant quant, float fadeInSeconds, float fadeOutSeconds, bool loop = true, float levelDb = 0f)
        {
            if (clip == null) return;
            _cmds.Enqueue(new Cmd { Type = CmdType.MusicPlay, Clip = clip, Quant = quant, A = fadeInSeconds, B = fadeOutSeconds, Flag = loop, C2 = levelDb });
        }

        /// <summary>Fade the music out (and stop it).</summary>
        public void MusicStop(float fadeSeconds) => _cmds.Enqueue(new Cmd { Type = CmdType.MusicStop, A = fadeSeconds });

        /// <summary>A short cue over the music; the music dips by <paramref name="duckDb"/> (negative) while it sounds. Starts on the next beat when one is within a second.</summary>
        public void MusicSting(Clip clip, Quant quant, float levelDb = 0f, float duckDb = -3f)
        {
            if (clip == null) return;
            _cmds.Enqueue(new Cmd { Type = CmdType.Sting, Clip = clip, Quant = quant, C2 = levelDb, D = duckDb });
        }

        public bool MusicActive => _layers.Count > 0;
        public long MusicClock => _clock;
        /// <summary>Music clock position within the current bar, 0..1 (for tests).</summary>
        public double BarPhase => _fpBeat <= 0 ? 0 : (_clock % (long)(_fpBeat * _beatsPerBar)) / (_fpBeat * _beatsPerBar);

        long NextBoundary(Quant q)
        {
            if (q == Quant.Now || _layers.Count == 0) return _clock;
            double grid = q == Quant.Beat ? _fpBeat : _fpBeat * _beatsPerBar;
            double lead = 0.008 * _rate;
            return (long)(Math.Ceiling((_clock + lead) / grid) * grid);
        }

        void ApplyMusic(Cmd c)
        {
            switch (c.Type)
            {
                case CmdType.MusicTempo: SetTempo(c.A, c.Slot); if (c.B > 0f) _loopFrames = (long)(c.B * _rate); break;
                case CmdType.MusicStop:
                    foreach (var l in _layers) if (!l.Sting) { l.Pending = false; BeginFade(l, c.A); }
                    break;
                case CmdType.ThemePlay:
                    {
                        // a theme goes over the base: same slot again replaces it (the old one fades out as the new one comes in)
                        _layers.RemoveAll(l => l.Pending && l.Theme == c.Slot);
                        foreach (var l in _layers) if (l.Theme == c.Slot && !l.Dying) BeginFade(l, c.B);
                        bool nobase = true;
                        foreach (var l in _layers) if (l.Theme < 0 && !l.Sting && !l.Dying) nobase = false;
                        if (_layers.Count == 0) _clock = 0;
                        _layers.Add(new MusicLayer
                        {
                            Clip = c.Clip, SrcRatio = (double)c.Clip.SampleRate / _rate, Loop = c.Flag, Level = Db.ToLin(c.C2), Theme = c.Slot, DuckLin = c.D,
                            Pending = true, StartClock = nobase ? _clock : NextBoundary(c.Quant),
                            FadeInFrames = (int)(c.A * _rate), FadeOutFrames = (int)(c.B * _rate),
                        });
                        break;
                    }
                case CmdType.ThemeHold:
                    foreach (var l in _layers)
                    {
                        if (l.Theme != c.Slot || l.Dying || l.Done) continue;
                        int fr = Math.Max(1, (int)(c.A * _rate));
                        if (c.Flag)
                        {
                            l.Held = true;
                            if (!l.Pending) { l.GainTarget = 0f; l.GainStep = l.Gain > 0f ? -l.Gain / fr : 0f; }
                        }
                        else
                        {
                            if (c.Flag2 && l.Held && l.Gain <= 0.0001f) l.Pos = l.Clip.StartFrame;          // after a long lull it begins again
                            l.Held = false;
                            if (!l.Pending) { l.GainTarget = l.Level; l.GainStep = (l.Level - l.Gain) / fr; }
                        }
                    }
                    break;
                case CmdType.ThemeLevel:
                    foreach (var l in _layers)
                    {
                        if (l.Theme != c.Slot || l.Dying || l.Done) continue;
                        l.Level = Db.ToLin(c.C2);
                        if (l.Held || l.Pending) continue;
                        int fr = Math.Max(1, (int)(c.A * _rate));
                        l.GainTarget = l.Level; l.GainStep = (l.Level - l.Gain) / fr;
                    }
                    break;
                case CmdType.ThemeStop:
                    // slot -1 stops every theme
                    foreach (var l in _layers)
                        if (l.Theme >= 0 && (c.Slot < 0 || l.Theme == c.Slot) && !l.Dying) { if (l.Pending) { l.Done = true; } else BeginFade(l, c.A); }
                    break;
                case CmdType.MusicPlay:
                    {
                        _layers.RemoveAll(l => l.Pending && !l.Sting && l.Theme < 0);
                        bool idle = true;
                        foreach (var l in _layers) if (!l.Sting && !l.Dying && l.Theme < 0) idle = false;
                        if (_layers.Count == 0) _clock = 0;
                        var layer = new MusicLayer
                        {
                            Clip = c.Clip, SrcRatio = (double)c.Clip.SampleRate / _rate, Loop = c.Flag, Level = Db.ToLin(c.C2),
                            Pending = true, StartClock = idle ? _clock : NextBoundary(c.Quant),
                            FadeInFrames = (int)(c.A * _rate), FadeOutFrames = (int)(c.B * _rate),
                        };
                        _layers.Add(layer);
                        break;
                    }
                case CmdType.Sting:
                    {
                        _layers.Add(new MusicLayer
                        {
                            Clip = c.Clip, SrcRatio = (double)c.Clip.SampleRate / _rate, Loop = false, Sting = true, Level = Db.ToLin(c.C2),
                            Pending = true, StartClock = _layers.Count == 0 ? _clock : NextBoundary(c.Quant == Quant.Bar ? Quant.Beat : c.Quant),
                            StingDuckDb = c.D, Gain = Db.ToLin(c.C2), GainTarget = Db.ToLin(c.C2),
                        });
                        break;
                    }
            }
        }

        void BeginFade(MusicLayer l, float seconds)
        {
            l.Dying = true; l.GainTarget = 0f;
            int frames = Math.Max(1, (int)(seconds * _rate));
            l.GainStep = -l.Gain / frames;
            if (l.Gain <= 0f) l.Done = true;
        }

        // ------------------------------------------------------------------------------------------------ themes
        //
        // A theme is a music layer on top of the base score. The base keeps playing under it (so it is in step when the theme ends), lowered by the theme's duck factor
        // in proportion to how far the theme has faded in: the base gives way as the theme comes up and returns as it goes. Themes are looped like the base (a 32-bar loop
        // the same length as the score's joins in step on the bar). Stings dip the whole music bus as before.

        /// <summary>A theme over the base: <paramref name="slot"/> is the conductor's number for it; <paramref name="duckLin"/> the factor the base is lowered to under it (0 silences the base).</summary>
        public void ThemePlay(int slot, Clip clip, Quant quant, float fadeInSeconds, float fadeOutSeconds, float levelDb, float duckLin, bool loop = true)
        {
            if (clip == null) return;
            _cmds.Enqueue(new Cmd { Type = CmdType.ThemePlay, Slot = slot, Clip = clip, Quant = quant, A = fadeInSeconds, B = fadeOutSeconds, C2 = levelDb, D = duckLin, Flag = loop });
        }

        /// <summary>Pause a theme (it fades out over <paramref name="fadeSeconds"/> and its position stops) or release it (it fades back in from where it was; <paramref name="restart"/> begins it again from its start).</summary>
        public void ThemeHold(int slot, bool hold, float fadeSeconds, bool restart = false) => _cmds.Enqueue(new Cmd { Type = CmdType.ThemeHold, Slot = slot, Flag = hold, Flag2 = restart, A = fadeSeconds });

        /// <summary>Move a playing theme to a new level (dB) over <paramref name="fadeSeconds"/>.</summary>
        public void ThemeLevel(int slot, float levelDb, float fadeSeconds) => _cmds.Enqueue(new Cmd { Type = CmdType.ThemeLevel, Slot = slot, C2 = levelDb, A = fadeSeconds });

        /// <summary>Fade a theme out (slot -1: every theme); the base comes back as it goes.</summary>
        public void ThemeStop(int slot, float fadeSeconds) => _cmds.Enqueue(new Cmd { Type = CmdType.ThemeStop, Slot = slot, A = fadeSeconds });

        /// <summary>How many themes have begun playing (for the checks), and the factor the base is lowered to just now (1 untouched).</summary>
        public int ThemeStarts;
        public long LastThemeStartClock;
        public float BaseDuck => _themeDuck;
        float _themeDuck = 1f;

        /// <summary>Gain (0..1 of its level) of the loudest running theme layer, or -1 when no theme is on (for the checks; read on the thread that mixes).</summary>
        public float ThemeFade { get { float m = -1f; foreach (var l in _layers) if (l.Theme >= 0 && !l.Pending && !l.Done && l.Level > 0f) m = Math.Max(m, l.Gain / l.Level); return m; } }
        public int ThemeLayerCount { get { int k = 0; foreach (var l in _layers) if (l.Theme >= 0 && !l.Done) k++; return k; } }

        void MixMusic(int n)
        {
            if (_layers.Count == 0) { _stingDuck += (1f - _stingDuck) * 0.01f; _themeDuck += (1f - _themeDuck) * 0.01f; return; }
            // how far the themes have lowered the base: each theme by its own fade, the deepest wins
            float duckTarget = 1f;
            foreach (var t in _layers)
                if (t.Theme >= 0 && !t.Pending && !t.Done && t.Level > 0f)
                {
                    float k = Math.Min(1f, Math.Max(0f, t.Gain / t.Level));
                    duckTarget = Math.Min(duckTarget, 1f - (1f - t.DuckLin) * k);
                }
            _themeDuck += (duckTarget - _themeDuck) * 0.12f;
            var bl = _busL[(int)Bus.Music]; var br = _busR[(int)Bus.Music];
            bool anyStingPlaying = false;
            for (int li = 0; li < _layers.Count; li++)
            {
                var l = _layers[li];
                if (l.Held && !l.Pending && l.Gain <= 0f && l.GainStep == 0f) continue;                 // paused: nothing plays and the position stays
                var clip = l.Clip; var pcm = clip.Pcm; int ch = clip.Channels, frames = clip.Frames;
                int ls = clip.LoopStart, le = clip.LoopEnd > 0 ? Math.Min(clip.LoopEnd, frames) : frames;
                float trim = clip.TrimDb == 0f ? 1f : Db.ToLin(clip.TrimDb);
                const float inv = 1f / 32768f;
                for (int i = 0; i < n; i++)
                {
                    long now = _clock + i;
                    if (l.Pending)
                    {
                        if (now < l.StartClock) continue;
                        StartLayer(l, now);
                    }
                    if (l.Done) break;
                    // gain ramp
                    if (l.GainStep != 0f)
                    {
                        l.Gain += l.GainStep;
                        if (l.GainStep > 0f && l.Gain >= l.GainTarget) { l.Gain = l.GainTarget; l.GainStep = 0f; }
                        else if (l.GainStep < 0f && l.Gain <= l.GainTarget) { l.Gain = l.GainTarget; l.GainStep = 0f; if (l.Dying && l.GainTarget <= 0f) { l.Done = true; break; } }
                    }
                    int i1 = (int)l.Pos; float t = (float)(l.Pos - i1);
                    float sl, sr;
                    if (t < 1e-4f && l.Loop)
                    {
                        int ii = Wrap(i1, ls, le - ls);
                        sl = pcm[ii * ch] * inv; sr = ch == 2 ? pcm[ii * 2 + 1] * inv : sl;
                    }
                    else if (l.Loop)
                    {
                        int len = le - ls;
                        int a = Wrap(i1 - 1, ls, len), b = Wrap(i1, ls, len), c2 = Wrap(i1 + 1, ls, len), d = Wrap(i1 + 2, ls, len);
                        if (ch == 1) { sl = sr = Dsp.Hermite(pcm[a] * inv, pcm[b] * inv, pcm[c2] * inv, pcm[d] * inv, t); }
                        else
                        {
                            sl = Dsp.Hermite(pcm[a * 2] * inv, pcm[b * 2] * inv, pcm[c2 * 2] * inv, pcm[d * 2] * inv, t);
                            sr = Dsp.Hermite(pcm[a * 2 + 1] * inv, pcm[b * 2 + 1] * inv, pcm[c2 * 2 + 1] * inv, pcm[d * 2 + 1] * inv, t);
                        }
                    }
                    else
                    {
                        if (i1 >= frames) { l.Done = true; break; }
                        int a = i1 - 1 < 0 ? 0 : i1 - 1, b = i1, c2 = i1 + 1 >= frames ? frames - 1 : i1 + 1, d = i1 + 2 >= frames ? frames - 1 : i1 + 2;
                        if (ch == 1) { sl = sr = Dsp.Hermite(pcm[a] * inv, pcm[b] * inv, pcm[c2] * inv, pcm[d] * inv, t); }
                        else
                        {
                            sl = Dsp.Hermite(pcm[a * 2] * inv, pcm[b * 2] * inv, pcm[c2 * 2] * inv, pcm[d * 2] * inv, t);
                            sr = Dsp.Hermite(pcm[a * 2 + 1] * inv, pcm[b * 2 + 1] * inv, pcm[c2 * 2 + 1] * inv, pcm[d * 2 + 1] * inv, t);
                        }
                    }
                    float g = l.Gain * trim * (l.Theme < 0 && !l.Sting ? _themeDuck : 1f);
                    bl[i] += sl * g; br[i] += sr * g;
                    l.Pos += l.SrcRatio;
                    if (l.Loop && l.Pos >= le) l.Pos -= (le - ls);
                }
                if (l.Sting && !l.Done) anyStingPlaying = true;
                if (l.Sting && l.Pending == false && !l.Done) _stingDuckTarget = Math.Min(_stingDuckTarget, Db.ToLin(l.StingDuckDb));
            }
            _clock += n;
            for (int li = _layers.Count - 1; li >= 0; li--) if (_layers[li].Done) _layers.RemoveAt(li);
            if (!anyStingPlaying) _stingDuckTarget = 1f;
            _stingDuck += (_stingDuckTarget - _stingDuck) * (_stingDuckTarget < _stingDuck ? 0.02f : 0.002f);
            _stingDuckTarget = 1f;
            // a deck with nothing left rests its clock
            bool any = false;
            foreach (var l in _layers) if (!l.Sting) any = true;
            if (_layers.Count == 0) _clock = 0;
            if (!any && _layers.Count > 0) { /* only stings left: the clock keeps running for their quantising */ }
        }

        /// <summary>The music clock reading at which the latest music layer began (for the checks: it must be on a beat or a bar), and how many have begun.</summary>
        public long LastMusicStartClock;
        public int MusicStarts;
        public double FramesPerBeat => _fpBeat;
        public int BeatsPerBar => _beatsPerBar;

        void StartLayer(MusicLayer l, long now)
        {
            l.Pending = false;
            if (!l.Sting && l.Theme < 0) { LastMusicStartClock = now; MusicStarts++; }
            if (l.Theme >= 0) { ThemeStarts++; LastThemeStartClock = now; }
            long ref0 = now;
            if (!l.Sting)
            {
                // start the new loop where the music clock says, if its length matches the loop length of the score
                long frames = (long)Math.Round(l.Clip.Frames / l.SrcRatio);
                if (l.Loop && _loopFrames == 0 && IsAlone(l)) _loopFrames = frames;
                if (l.Loop && _loopFrames > 0 && Math.Abs(frames - _loopFrames) <= 4)
                {
                    l.Aligned = true;
                    l.Pos = ((double)(ref0 % _loopFrames)) * l.SrcRatio;
                    if (l.Pos >= l.Clip.Frames) l.Pos = 0;
                }
                else l.Pos = l.Clip.StartFrame;
                l.Gain = l.FadeInFrames > 0 ? 0f : l.Level;
                l.GainTarget = l.Level;
                l.GainStep = l.FadeInFrames > 0 ? l.Level / l.FadeInFrames : 0f;
                if (l.Held) { l.Gain = 0f; l.GainTarget = 0f; l.GainStep = 0f; }
                int outFrames = l.FadeOutFrames > 0 ? l.FadeOutFrames : Math.Max(l.FadeInFrames, (int)(0.05f * _rate));
                if (l.Theme < 0)
                foreach (var o in _layers)
                {
                    if (o == l || o.Sting || o.Pending || o.Dying || o.Theme >= 0) continue;
                    o.Dying = true; o.GainTarget = 0f;
                    o.GainStep = -o.Gain / Math.Max(1, outFrames);
                    if (o.Gain <= 0f) o.Done = true;
                }
            }
            else
            {
                l.Pos = 0; l.Gain = l.Level; l.GainTarget = l.Level; l.GainStep = 0f;
            }
        }

        bool IsAlone(MusicLayer l)
        {
            foreach (var o in _layers) if (o != l && !o.Sting && !o.Done && o.Theme < 0) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ tones (lock tone, warnings)

        sealed class Tone
        {
            public bool On;
            public float Freq, FreqT, Gain, GainT, GateHz, Duty = 0.5f, Timbre, Pan;
            public float Phase, GatePhase, Env;
        }

        const int ToneSlots = 4;
        readonly Tone[] _tones = new Tone[ToneSlots];

        void InitTones() { for (int i = 0; i < ToneSlots; i++) _tones[i] = new Tone(); }

        /// <summary>Set a tone generator: a pitched beep, gated (<paramref name="gateHz"/> pulses a second with the given duty; 0 = steady). Gain 0 silences it.</summary>
        public void SetTone(int slot, float freq, float gain, float gateHz, float duty = 0.5f, float timbre = 0.4f, float pan = 0f)
        {
            _cmds.Enqueue(new Cmd { Type = CmdType.Tone, Slot = slot, A = freq, B = gain, C2 = gateHz, D = duty, E = timbre, Flag = true, C = new VoiceCtl { Pan = pan } });
        }

        void ApplyTone(Cmd c)
        {
            if (c.Slot < 0 || c.Slot >= ToneSlots) return;
            var t = _tones[c.Slot];
            t.FreqT = c.A; t.GainT = c.B; t.GateHz = c.C2; t.Duty = c.D; t.Timbre = c.E; t.Pan = c.C.Pan;
            if (!t.On) { t.Freq = c.A; t.Gain = 0f; t.On = true; }
        }

        void MixTones(int n)
        {
            var bl = _busL[(int)Bus.Ui]; var br = _busR[(int)Bus.Ui];
            for (int s = 0; s < ToneSlots; s++)
            {
                var t = _tones[s];
                if (!t.On) continue;
                if (t.Gain < 1e-5f && t.GainT < 1e-5f && t.Env < 1e-5f) { t.On = false; continue; }
                float pa = (t.Pan + 1f) * 0.25f * Dsp.Pi, gl = MathF.Cos(pa), gr = MathF.Sin(pa);
                float gateInc = t.GateHz / _rate;
                float envA = 1f - MathF.Exp(-1f / (0.0022f * _rate)), envR = 1f - MathF.Exp(-1f / (0.012f * _rate));
                float gC = 1f - MathF.Exp(-1f / (0.02f * _rate)), fC = 1f - MathF.Exp(-1f / (0.03f * _rate));
                for (int i = 0; i < n; i++)
                {
                    t.Gain += (t.GainT - t.Gain) * gC;
                    t.Freq += (t.FreqT - t.Freq) * fC;
                    bool open = true;
                    if (t.GateHz > 0.01f)
                    {
                        t.GatePhase += gateInc; if (t.GatePhase >= 1f) t.GatePhase -= 1f;
                        open = t.GatePhase < t.Duty;
                    }
                    float target = open ? 1f : 0f;
                    t.Env += (target - t.Env) * (target > t.Env ? envA : envR);
                    t.Phase += t.Freq / _rate; if (t.Phase >= 1f) t.Phase -= 1f;
                    float ph = t.Phase * Dsp.TwoPi;
                    float w = MathF.Sin(ph) + t.Timbre * (0.5f * MathF.Sin(2f * ph) + 0.25f * MathF.Sin(3f * ph));
                    float y = w * 0.6f * t.Gain * t.Env;
                    bl[i] += y * gl * 1.41f; br[i] += y * gr * 1.41f;
                }
            }
        }
    }
}
