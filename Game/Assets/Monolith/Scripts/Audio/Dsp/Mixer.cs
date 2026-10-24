using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Monolith.Audio
{
    /// <summary>The mixer's buses. Music, effects and voice have their own volume; voice ducks music and effects; a big moment ducks everything but itself.</summary>
    public enum Bus { Sfx = 0, SfxBig = 1, Music = 2, Voice = 3, Ui = 4 }

    /// <summary>When a music change takes effect: at once, on the next beat, or on the next bar (measured on the music clock).</summary>
    public enum Quant { Now, Beat, Bar }

    /// <summary>A decoded sound held in memory as 16-bit PCM (mono or stereo, interleaved).</summary>
    public sealed class Clip
    {
        public string Name;
        public int SampleRate, Channels;
        public short[] Pcm;
        /// <summary>The loop body in frames; LoopEnd 0 means the end of the clip.</summary>
        public int LoopStart, LoopEnd;
        public float TrimDb;
        /// <summary>Where the first pass starts (frames), skipping an intro; the loop body is still LoopStart to LoopEnd.</summary>
        public int StartFrame;
        public int Frames => Pcm.Length / Channels;
        public float Seconds => (float)Frames / SampleRate;

        /// <summary>
        /// Make the loop seamless by cross-fading: the last <paramref name="fadeSeconds"/> of the loop body (before LoopEnd) are blended into the material that leads up to LoopStart, so
        /// when the mixer jumps from LoopEnd back to LoopStart the signal carries on from where it was. Needs that much audio before LoopStart; returns false (and changes nothing) when there is not.
        /// Done once, on the clip, when it is loaded.
        /// </summary>
        public bool BakeLoopCrossfade(float fadeSeconds)
        {
            int le = LoopEnd > 0 ? Math.Min(LoopEnd, Frames) : Frames;
            int xf = (int)(fadeSeconds * SampleRate);
            if (xf < 16 || LoopStart < xf || le - LoopStart < xf * 2) return false;
            for (int j = 0; j < xf; j++)
            {
                float t = (j + 0.5f) / xf;
                float w = t * t * (3f - 2f * t);                                  // smoothstep: the seam region is mostly one or the other, never a bump
                for (int k = 0; k < Channels; k++)
                {
                    int dst = (le - xf + j) * Channels + k, src = (LoopStart - xf + j) * Channels + k;
                    float v = Pcm[dst] * (1f - w) + Pcm[src] * w;
                    Pcm[dst] = (short)(v > 32767f ? 32767 : v < -32768f ? -32768 : (int)MathF.Round(v));
                }
            }
            return true;
        }

        /// <summary>A sample in -1..1 as 16 bits, rounded to the nearest and clipped. (Written for the load: tens of millions of these, so no calls and no nested loops.)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Quantize(float x)
        {
            float v = x * 32767f;
            int s = v >= 0f ? (int)(v + 0.5f) : (int)(v - 0.5f);
            return (short)(s > 32767 ? 32767 : s < -32768 ? -32768 : s);
        }

        /// <summary>
        /// Turn <paramref name="frames"/> frames of interleaved float samples (any number of channels; the first two are kept) into 16 bits at frame <paramref name="dstFrame"/> of
        /// <paramref name="dst"/> (interleaved, <c>Math.Min(channels, 2)</c> channels). Touches nothing else, so several threads can do different stretches of one clip.
        /// </summary>
        public static void Quantize(float[] src, int channels, short[] dst, int dstFrame, int frames)
        {
            int ch = Math.Min(channels, 2), o = dstFrame * ch;
            if (channels == ch)
            {
                int n = frames * ch;
                for (int i = 0; i < n; i++) dst[o + i] = Quantize(src[i]);
            }
            else
                for (int i = 0, b = 0; i < frames; i++, b += channels)
                    for (int k = 0; k < ch; k++) dst[o++] = Quantize(src[b + k]);
        }

        public static Clip FromPcm(PcmBuffer b, string name)
        {
            int ch = Math.Min(b.Channels, 2);
            var pcm = new short[b.Frames * ch];
            for (int k = 0; k < ch; k++)
            {
                var src = b.Ch[k];
                int frames = b.Frames;
                for (int i = 0, o = k; i < frames; i++, o += ch) pcm[o] = Quantize(src[i]);
            }
            return new Clip { Name = name, SampleRate = b.SampleRate, Channels = ch, Pcm = pcm };
        }

        public static Clip FromInterleaved(float[] data, int channels, int sampleRate, string name)
        {
            int ch = Math.Min(channels, 2);
            int frames = data.Length / channels;
            var pcm = new short[frames * ch];
            Quantize(data, channels, pcm, 0, frames);
            return new Clip { Name = name, SampleRate = sampleRate, Channels = ch, Pcm = pcm };
        }
    }

    /// <summary>How to start a sound.</summary>
    public struct PlayParams
    {
        public float Gain;           // linear
        public float Pan;            // -1 left .. 1 right
        public float Pitch;          // 1 = as recorded
        public float CutoffHz;       // 0 = no low-pass
        public Bus Bus;
        public bool Loop;
        public float DelaySeconds;   // wait before it starts (sound arriving from far away)
        public float FadeInSeconds;
        public float Priority;       // higher survives voice stealing
        public bool PitchFollowsTime;   // slow motion drops its pitch

        public static PlayParams Default => new PlayParams { Gain = 1f, Pan = 0f, Pitch = 1f, CutoffHz = 0f, Bus = Bus.Sfx, Priority = 1f };
    }

    /// <summary>Live changes to a playing sound (NaN = leave as is).</summary>
    public struct VoiceCtl
    {
        public float Gain, Pan, Pitch, CutoffHz;
        public static VoiceCtl None => new VoiceCtl { Gain = float.NaN, Pan = float.NaN, Pitch = float.NaN, CutoffHz = float.NaN };
    }

    /// <summary>
    /// The game's own mixer: sampled voices (3D-placed by the caller as gain, pan, low-pass and pitch), the music deck, a few tone generators,
    /// five buses with their own volumes, ducking (voice under music and effects, big moments over everything), and a lookahead limiter on
    /// the master. It runs on any thread that calls <see cref="Render"/>: Unity's audio callback in the game, a plain loop in the tools and the
    /// self-test, so the same sound can be rendered offline, measured and recorded. Control calls are thread-safe (a command queue drained at
    /// the start of each block); nothing in here allocates while rendering.
    /// </summary>
    public sealed partial class Mixer
    {
        public const int Block = 64;
        public const int NumBuses = 5;

        readonly int _rate;
        public int SampleRate => _rate;
        readonly ConcurrentQueue<Cmd> _cmds = new ConcurrentQueue<Cmd>();
        int _nextId = 1;

        // voices (audio thread only)
        readonly List<Voice> _voices = new List<Voice>(80);
        readonly Stack<Voice> _pool = new Stack<Voice>();
        readonly Dictionary<int, Voice> _byId = new Dictionary<int, Voice>();
        public int MaxVoices = 64;

        // buses
        readonly float[][] _busL = new float[NumBuses][], _busR = new float[NumBuses][];
        readonly float[] _busGainCur = new float[NumBuses], _busGainTarget = new float[NumBuses];
        readonly float[] _mixL = new float[Block], _mixR = new float[Block];
        readonly float[][] _out = new float[2][];
        readonly float[][] _single = new float[2][];

        // master
        float _masterCur = 1f, _masterTarget = 1f;
        readonly Limiter _limiter;
        readonly LoudnessMeter _loud;
        public readonly float[] BusPeak = new float[NumBuses];       // since the last ResetMeters
        public float MasterPeak, PostLimiterPeak;
        public int ClippedSamples;                                    // after the limiter: must stay 0
        public float MinLimiterGain => _limiter.MinGainSeen;
        /// <summary>K-weighted loudness metering of the master (the checks and -mnAudioLog; costs a little).</summary>
        public bool MeterLoudness = true;

        // ducking
        float _voiceDuckGain = 0f, _bigDuckGain = 1f, _bigDuckTarget = 1f, _stingDuck = 1f;
        float _bigHold, _bigRelease;
        float _voiceFollow;
        public float VoiceDuckMusicDb = -9f, VoiceDuckSfxDb = -4.5f, VoiceThresholdDb = -50f;
        float _duckAttack, _duckRelease, _bigAttack;

        float _timePitch = 1f, _timePitchTarget = 1f;
        /// <summary>How far the music and the effects are ducked right now, dB (0 = not at all): for the checks and the debug line.</summary>
        public float LastMusicDuckDb, LastSfxDuckDb;
        long _framesRendered;
        public long FramesRendered => _framesRendered;
        public int ActiveVoices => _voices.Count;
        public int StolenVoices;

        public Mixer(int sampleRate)
        {
            _rate = sampleRate;
            for (int b = 0; b < NumBuses; b++) { _busL[b] = new float[Block]; _busR[b] = new float[Block]; _busGainCur[b] = _busGainTarget[b] = 1f; }
            _out[0] = new float[Block]; _out[1] = new float[Block];
            _single[0] = new float[Block]; _single[1] = new float[Block];
            _limiter = new Limiter(sampleRate, 2, -1.0f, 2f, 120f);
            _loud = new LoudnessMeter(sampleRate, 2);
            _duckAttack = Dsp.TauToCoef(0.025f, sampleRate / (float)Block * Block);
            _duckRelease = Dsp.TauToCoef(0.55f, sampleRate);
            _bigAttack = Dsp.TauToCoef(0.02f, sampleRate);
            InitMusic();
            InitTones();
        }

        // ------------------------------------------------------------------------------------------------ control (any thread)

        enum CmdType { Play, Ctl, Stop, StopAll, BusGain, MasterGain, BigDuck, TimePitch, MusicPlay, MusicStop, Sting, Tone, ToneOff, MusicTempo, ThemePlay, ThemeStop, ThemeHold, ThemeLevel }

        sealed class Cmd
        {
            public CmdType Type;
            public int Id, Slot;
            public Clip Clip;
            public PlayParams P;
            public VoiceCtl C;
            public float A, B, C2, D, E;
            public Bus Bus;
            public Quant Quant;
            public bool Flag, Flag2;
        }

        public int Play(Clip clip, PlayParams p)
        {
            if (clip == null || clip.Pcm == null || clip.Pcm.Length == 0) return 0;
            int id = System.Threading.Interlocked.Increment(ref _nextId);
            _cmds.Enqueue(new Cmd { Type = CmdType.Play, Id = id, Clip = clip, P = p });
            return id;
        }

        public void Control(int id, VoiceCtl c) { if (id != 0) _cmds.Enqueue(new Cmd { Type = CmdType.Ctl, Id = id, C = c }); }
        public void Stop(int id, float fadeSeconds = 0.03f) { if (id != 0) _cmds.Enqueue(new Cmd { Type = CmdType.Stop, Id = id, A = fadeSeconds }); }
        public void StopAll(float fadeSeconds = 0.05f) => _cmds.Enqueue(new Cmd { Type = CmdType.StopAll, A = fadeSeconds });
        public void SetBusGain(Bus bus, float linear) => _cmds.Enqueue(new Cmd { Type = CmdType.BusGain, Bus = bus, A = linear });
        public void SetMasterGain(float linear) => _cmds.Enqueue(new Cmd { Type = CmdType.MasterGain, A = linear });

        /// <summary>The biggest moments lower everything but themselves to <c>1 - amount</c> for <paramref name="seconds"/> (held, then eased back).</summary>
        public void BigDuck(float amount, float seconds) => _cmds.Enqueue(new Cmd { Type = CmdType.BigDuck, A = Dsp.Clamp01(amount), B = seconds });

        /// <summary>Slow motion: effects drop in pitch (1 = normal).</summary>
        public void SetTimePitch(float pitch) => _cmds.Enqueue(new Cmd { Type = CmdType.TimePitch, A = pitch });

        // ------------------------------------------------------------------------------------------------ voices

        sealed class Voice
        {
            public int Id;
            public Clip Clip;
            public double Pos;
            public double SrcRatio;                  // clip rate / output rate
            public float Gain, GainT, Pan, PanT, Pitch, PitchT, Cut, CutT;      // Cut: one-pole coefficient (1 = open)
            public float LpL, LpR;
            public Bus Bus;
            public bool Loop, Dying, FollowsTime;
            public int Delay;                        // frames to wait
            public float FadeGain = 1f, FadeStep;    // multiplies Gain: fade in (rising) or out (falling to 0)
            public float Priority;
            public long Started;
            public bool Done;
            public float Trim = 1f;
        }

        Voice Rent()
        {
            var v = _pool.Count > 0 ? _pool.Pop() : new Voice();
            v.Done = false; v.Dying = false; v.LpL = v.LpR = 0f; v.FadeGain = 1f; v.FadeStep = 0f; v.Delay = 0;
            return v;
        }

        float CutCoef(float hz) => hz <= 0f || hz >= _rate * 0.45f ? 1f : Dsp.OnePoleCoef(hz, _rate);

        void StartVoice(Cmd c)
        {
            if (_voices.Count >= MaxVoices)
            {
                // steal the least important: low priority, quiet, old
                Voice worst = null; float ws = float.MaxValue;
                foreach (var v in _voices)
                {
                    if (v.Dying || v.Loop) continue;
                    float score = v.Priority * 4f + v.Gain - (float)(_framesRendered - v.Started) / _rate * 0.05f;
                    if (score < ws) { ws = score; worst = v; }
                }
                if (worst == null || ws > c.P.Priority * 4f + c.P.Gain) return;
                worst.Dying = true; worst.FadeStep = 1f / (0.004f * _rate);
                StolenVoices++;
            }
            var nv = Rent();
            nv.Id = c.Id; nv.Clip = c.Clip;
            nv.SrcRatio = (double)c.Clip.SampleRate / _rate;
            nv.Trim = c.Clip.TrimDb == 0f ? 1f : Db.ToLin(c.Clip.TrimDb);
            nv.Pos = 0; nv.Bus = c.P.Bus; nv.Loop = c.P.Loop; nv.FollowsTime = c.P.PitchFollowsTime;
            nv.Gain = nv.GainT = c.P.Gain; nv.Pan = nv.PanT = c.P.Pan; nv.Pitch = nv.PitchT = c.P.Pitch <= 0f ? 1f : c.P.Pitch;
            nv.Cut = nv.CutT = CutCoef(c.P.CutoffHz);
            nv.Delay = (int)(c.P.DelaySeconds * _rate);
            nv.Priority = c.P.Priority; nv.Started = _framesRendered;
            if (c.P.FadeInSeconds > 0.001f) { nv.FadeGain = 0f; nv.FadeStep = 1f / (c.P.FadeInSeconds * _rate); }
            _voices.Add(nv);
            _byId[nv.Id] = nv;
        }

        void Apply(Cmd c)
        {
            switch (c.Type)
            {
                case CmdType.Play: StartVoice(c); break;
                case CmdType.Ctl:
                    if (_byId.TryGetValue(c.Id, out var v))
                    {
                        if (!float.IsNaN(c.C.Gain)) v.GainT = c.C.Gain;
                        if (!float.IsNaN(c.C.Pan)) v.PanT = c.C.Pan;
                        if (!float.IsNaN(c.C.Pitch)) v.PitchT = MathF.Max(c.C.Pitch, 0.05f);
                        if (!float.IsNaN(c.C.CutoffHz)) v.CutT = CutCoef(c.C.CutoffHz);
                    }
                    break;
                case CmdType.Stop:
                    if (_byId.TryGetValue(c.Id, out var s)) { s.Dying = true; s.Loop = false; s.FadeStep = 1f / (MathF.Max(c.A, 0.002f) * _rate); if (s.FadeGain <= 0f) s.Done = true; }
                    break;
                case CmdType.StopAll:
                    foreach (var vv in _voices) { vv.Dying = true; vv.FadeStep = 1f / (MathF.Max(c.A, 0.002f) * _rate); }
                    break;
                case CmdType.BusGain: _busGainTarget[(int)c.Bus] = c.A; break;
                case CmdType.MasterGain: _masterTarget = c.A; break;
                case CmdType.BigDuck:
                    {
                        float target = 1f - c.A;
                        if (target < _bigDuckTarget || _bigHold <= 0f) { _bigDuckTarget = Math.Min(_bigDuckTarget, target); }
                        _bigHold = Math.Max(_bigHold, c.B * 0.45f * _rate);
                        _bigRelease = Math.Max(_bigRelease, c.B * 0.55f);
                        break;
                    }
                case CmdType.TimePitch: _timePitchTarget = c.A; break;
                case CmdType.Tone: ApplyTone(c); break;
                default: ApplyMusic(c); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ rendering

        /// <summary>Render <paramref name="frames"/> frames into an interleaved buffer (stereo goes in the first two channels; extra channels are silent).</summary>
        public void Render(float[] data, int frames, int channels)
        {
            int done = 0;
            while (done < frames)
            {
                int n = Math.Min(Block, frames - done);
                RenderBlock(n);
                if (channels == 1)
                    for (int i = 0; i < n; i++) data[done + i] = 0.5f * (_out[0][i] + _out[1][i]);
                else
                    for (int i = 0; i < n; i++)
                    {
                        int o = (done + i) * channels;
                        data[o] = _out[0][i]; data[o + 1] = _out[1][i];
                        for (int c = 2; c < channels; c++) data[o + c] = 0f;
                    }
                done += n;
            }
        }

        /// <summary>Render into two planar arrays starting at <paramref name="offset"/> (the tools and the tests).</summary>
        public void RenderPlanar(float[] l, float[] r, int offset, int frames)
        {
            int done = 0;
            while (done < frames)
            {
                int n = Math.Min(Block, frames - done);
                RenderBlock(n);
                Array.Copy(_out[0], 0, l, offset + done, n);
                Array.Copy(_out[1], 0, r, offset + done, n);
                done += n;
            }
        }

        void RenderBlock(int n)
        {
            while (_cmds.TryDequeue(out var cmd)) Apply(cmd);

            for (int b = 0; b < NumBuses; b++) { Array.Clear(_busL[b], 0, n); Array.Clear(_busR[b], 0, n); }

            // time-pitch smoothing (slow motion)
            _timePitch += (_timePitchTarget - _timePitch) * 0.01f;

            for (int vi = _voices.Count - 1; vi >= 0; vi--)
            {
                var v = _voices[vi];
                MixVoice(v, n);
                if (v.Done)
                {
                    _voices.RemoveAt(vi); _byId.Remove(v.Id); v.Clip = null; _pool.Push(v);
                }
            }
            MixMusic(n);
            MixTones(n);

            // The voice bus drives the ducking of the rest: how loud is speech right now.
            float vp = 0f;
            for (int i = 0; i < n; i++) { float a = MathF.Max(MathF.Abs(_busL[(int)Bus.Voice][i]), MathF.Abs(_busR[(int)Bus.Voice][i])); if (a > vp) vp = a; }
            _voiceFollow = vp > _voiceFollow ? vp : _voiceFollow + (vp - _voiceFollow) * 0.2f;
            bool speaking = _voiceFollow > Db.ToLin(VoiceThresholdDb);
            float vdTarget = speaking ? 1f : 0f;
            _voiceDuckGain += (vdTarget - _voiceDuckGain) * (speaking ? 1f - MathF.Exp(-n / (0.03f * _rate)) : 1f - MathF.Exp(-n / (0.6f * _rate)));

            // big-moment duck: attack, hold, release
            if (_bigHold > 0f) { _bigHold -= n; _bigDuckGain += (_bigDuckTarget - _bigDuckGain) * (1f - MathF.Exp(-n / (0.02f * _rate))); }
            else
            {
                float rel = _bigRelease > 0.05f ? _bigRelease : 0.3f;
                _bigDuckGain += (1f - _bigDuckGain) * (1f - MathF.Exp(-n / (rel * 0.35f * _rate)));
                if (_bigDuckGain > 0.999f) { _bigDuckGain = 1f; _bigDuckTarget = 1f; _bigRelease = 0f; }
            }

            float vdMusic = Dsp.Lerp(1f, Db.ToLin(VoiceDuckMusicDb), _voiceDuckGain);
            LastMusicDuckDb = Db.FromLin(vdMusic * _bigDuckGain * _stingDuck);
            LastSfxDuckDb = Db.FromLin(Dsp.Lerp(1f, Db.ToLin(VoiceDuckSfxDb), _voiceDuckGain) * _bigDuckGain);
            float vdSfx = Dsp.Lerp(1f, Db.ToLin(VoiceDuckSfxDb), _voiceDuckGain);

            for (int i = 0; i < n; i++) { _mixL[i] = 0f; _mixR[i] = 0f; }
            for (int b = 0; b < NumBuses; b++)
            {
                float duck = 1f;
                switch ((Bus)b)
                {
                    case Bus.Music: duck = vdMusic * _bigDuckGain * _stingDuck; break;
                    case Bus.Sfx: duck = vdSfx * _bigDuckGain; break;
                    case Bus.SfxBig: duck = vdSfx; break;
                    case Bus.Ui: duck = vdSfx; break;
                    case Bus.Voice: duck = 1f; break;
                }
                float g0 = _busGainCur[b];
                _busGainCur[b] += (_busGainTarget[b] - g0) * 0.05f;
                float g1 = _busGainCur[b];
                float pk = BusPeak[b];
                var bl = _busL[b]; var br = _busR[b];
                for (int i = 0; i < n; i++)
                {
                    float g = (g0 + (g1 - g0) * (i + 1) / n) * duck;
                    float l = bl[i] * g, r = br[i] * g;
                    float a = MathF.Max(MathF.Abs(l), MathF.Abs(r));
                    if (a > pk) pk = a;
                    _mixL[i] += l; _mixR[i] += r;
                }
                BusPeak[b] = pk;
            }

            float m0 = _masterCur;
            _masterCur += (_masterTarget - m0) * 0.05f;
            float m1 = _masterCur;
            float mp = MasterPeak;
            for (int i = 0; i < n; i++)
            {
                float g = m0 + (m1 - m0) * (i + 1) / n;
                _out[0][i] = _mixL[i] * g; _out[1][i] = _mixR[i] * g;
                float a = MathF.Max(MathF.Abs(_out[0][i]), MathF.Abs(_out[1][i]));
                if (a > mp) mp = a;
            }
            MasterPeak = mp;
            _limiter.Process(_out, 0, n);
            float pp = PostLimiterPeak;
            for (int i = 0; i < n; i++)
            {
                float a = MathF.Max(MathF.Abs(_out[0][i]), MathF.Abs(_out[1][i]));
                if (a > pp) pp = a;
                if (a >= 1f || float.IsNaN(a)) ClippedSamples++;
            }
            PostLimiterPeak = pp;
            if (MeterLoudness) _loud.Add(_out, 0, n);
            _framesRendered += n;
        }

        void MixVoice(Voice v, int n)
        {
            var clip = v.Clip;
            var pcm = clip.Pcm;
            int ch = clip.Channels, frames = clip.Frames;
            int ls = clip.LoopStart, le = clip.LoopEnd > 0 ? Math.Min(clip.LoopEnd, frames) : frames;
            int bus = (int)v.Bus;
            var bl = _busL[bus]; var br = _busR[bus];

            // per-block parameter ramps
            float g0 = v.Gain, g1 = v.GainT; v.Gain = g1 = g0 + (g1 - g0) * 0.35f;
            float p0 = v.Pan, p1 = v.PanT; v.Pan = p1 = p0 + (p1 - p0) * 0.35f;
            float k0 = v.Cut, k1 = v.CutT; v.Cut = k1 = k0 + (k1 - k0) * 0.35f;
            float s0 = v.Pitch; v.Pitch += (v.PitchT - v.Pitch) * 0.3f; float s1 = v.Pitch;
            float tp = v.FollowsTime ? _timePitch : 1f;
            float trim = v.Trim;
            const float inv = 1f / 32768f;
            float panAngle0 = (p0 + 1f) * 0.25f * Dsp.Pi, panAngle1 = (p1 + 1f) * 0.25f * Dsp.Pi;
            float gl0 = MathF.Cos(panAngle0), gr0 = MathF.Sin(panAngle0), gl1 = MathF.Cos(panAngle1), gr1 = MathF.Sin(panAngle1);
            float invN = 1f / n;
            float busTp = v.Bus == Bus.Music || v.Bus == Bus.Voice ? 1f : tp;
            double ratioBase = v.SrcRatio * busTp;
            bool slowFilter = k0 < 0.999f || k1 < 0.999f;

            // too quiet to hear and not fading in: only keep its place in the clip
            if (v.FadeStep == 0f && v.Delay == 0 && MathF.Max(g0, g1) * trim * v.FadeGain < 0.00003f)
            {
                v.Pos += ratioBase * (s0 + s1) * 0.5 * n;
                if (v.Loop) { while (v.Pos >= le) v.Pos -= (le - ls); } else if (v.Pos >= frames) v.Done = true;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                if (v.Delay > 0) { v.Delay--; continue; }
                float f = (i + 1) * invN;
                float g = (g0 + (g1 - g0) * f) * trim;
                if (v.FadeStep != 0f)
                {
                    v.FadeGain += v.Dying ? -MathF.Abs(v.FadeStep) : MathF.Abs(v.FadeStep);
                    if (v.FadeGain >= 1f) { v.FadeGain = 1f; v.FadeStep = 0f; }
                    else if (v.FadeGain <= 0f) { v.FadeGain = 0f; v.Done = true; break; }
                }
                g *= v.FadeGain;
                float gl = gl0 + (gl1 - gl0) * f, gr = gr0 + (gr1 - gr0) * f;
                float coef = k0 + (k1 - k0) * f;

                // read
                int i1 = (int)v.Pos;
                float t = (float)(v.Pos - i1);
                float sl, sr;
                if (t < 1e-4f)
                {
                    if (v.Loop) { int len0 = le - ls; if (len0 <= 0) { v.Done = true; break; } i1 = Wrap(i1, ls, len0); }
                    else if (i1 >= frames) { v.Done = true; break; }
                    sl = pcm[i1 * ch] * inv; sr = ch == 2 ? pcm[i1 * ch + 1] * inv : sl;
                }
                else
                {
                    int a, b2, c2, d;
                    if (v.Loop)
                    {
                        int len = le - ls;
                        if (len <= 0) { v.Done = true; break; }
                        a = Wrap(i1 - 1, ls, len); b2 = Wrap(i1, ls, len); c2 = Wrap(i1 + 1, ls, len); d = Wrap(i1 + 2, ls, len);
                    }
                    else
                    {
                        if (i1 >= frames) { v.Done = true; break; }
                        a = i1 - 1 < 0 ? 0 : i1 - 1; b2 = i1; c2 = i1 + 1 >= frames ? frames - 1 : i1 + 1; d = i1 + 2 >= frames ? frames - 1 : i1 + 2;
                    }
                    if (ch == 1)
                    {
                        sl = Dsp.Hermite(pcm[a] * inv, pcm[b2] * inv, pcm[c2] * inv, pcm[d] * inv, t); sr = sl;
                    }
                    else
                    {
                        sl = Dsp.Hermite(pcm[a * 2] * inv, pcm[b2 * 2] * inv, pcm[c2 * 2] * inv, pcm[d * 2] * inv, t);
                        sr = Dsp.Hermite(pcm[a * 2 + 1] * inv, pcm[b2 * 2 + 1] * inv, pcm[c2 * 2 + 1] * inv, pcm[d * 2 + 1] * inv, t);
                    }
                }
                if (slowFilter && coef < 0.999f) { v.LpL += (sl - v.LpL) * coef; v.LpR += (sr - v.LpR) * coef; sl = v.LpL; sr = v.LpR; }
                if (ch == 1) { bl[i] += sl * g * gl; br[i] += sr * g * gr; }
                else { bl[i] += sl * g * MathF.Min(1f, 1.41f * gl); br[i] += sr * g * MathF.Min(1f, 1.41f * gr); }

                v.Pos += ratioBase * (s0 + (s1 - s0) * f);
                if (v.Loop) { if (v.Pos >= le) v.Pos -= (le - ls); }
            }
            if (!v.Loop && v.Pos >= frames) v.Done = true;
        }

        static int Wrap(int i, int start, int len)
        {
            if (i >= start + len) i -= len;
            else if (i < start) i += len;
            return i;
        }

        // ------------------------------------------------------------------------------------------------ meters

        public void ResetMeters()
        {
            for (int i = 0; i < NumBuses; i++) BusPeak[i] = 0f;
            MasterPeak = 0f; PostLimiterPeak = 0f; ClippedSamples = 0; _limiter.MinGainSeen = 1f;
        }

        public float LoudnessMomentary => _loud.Momentary;
        public float LoudnessShortTerm => _loud.ShortTerm;
        public float LoudnessIntegrated => _loud.Integrated;
        public LoudnessMeter Loudness => _loud;
    }
}
