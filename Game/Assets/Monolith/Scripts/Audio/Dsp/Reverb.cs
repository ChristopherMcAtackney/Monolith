using System;

namespace Monolith.Audio
{
    /// <summary>A Schroeder all-pass diffuser with an optional slow modulation of its delay (for plates and halls that do not ring).</summary>
    public sealed class AllPass
    {
        readonly float[] _buf;
        int _w;
        readonly int _len;
        public float G;
        float _modPhase, _modInc, _modDepth;

        public AllPass(int length, float g, float modDepthSamples = 0f, float modHz = 0.5f, float sampleRate = 48000f, float phase0 = 0f)
        {
            _len = Math.Max(2, length);
            _buf = new float[_len + 64];
            G = g;
            _modDepth = modDepthSamples;
            _modInc = Dsp.TwoPi * modHz / sampleRate;
            _modPhase = phase0;
        }

        public float Process(float x)
        {
            float d;
            if (_modDepth > 0f)
            {
                _modPhase += _modInc; if (_modPhase > Dsp.TwoPi) _modPhase -= Dsp.TwoPi;
                float delay = _len + _modDepth * MathF.Sin(_modPhase);
                float pos = _w - delay;
                int i = (int)MathF.Floor(pos);
                float f = pos - i;
                int n = _buf.Length;
                d = Dsp.Hermite(_buf[Mod(i - 1, n)], _buf[Mod(i, n)], _buf[Mod(i + 1, n)], _buf[Mod(i + 2, n)], f);
            }
            else d = _buf[Mod(_w - _len, _buf.Length)];
            float v = x - G * d;
            _buf[_w] = v;
            _w++; if (_w >= _buf.Length) _w = 0;
            return d + G * v;
        }

        static int Mod(int a, int n) { a %= n; return a < 0 ? a + n : a; }
    }

    /// <summary>
    /// A feedback-delay-network hall: eight delay lines through a Householder matrix, damped by a one-pole in each line (the highs die first,
    /// as in a real room), modulated so it does not ring, fed through four diffusers. Stereo out from two sign patterns of the lines.
    /// </summary>
    public sealed class FdnReverb
    {
        const int N = 8;
        static readonly int[] BaseLens = { 1117, 1559, 1999, 2413, 2851, 3331, 3779, 4231 };
        readonly DelayLine[] _lines = new DelayLine[N];
        readonly float[] _lens = new float[N], _gains = new float[N], _damp = new float[N], _lp = new float[N], _o = new float[N];
        readonly float[] _modPhase = new float[N], _modInc = new float[N];
        readonly AllPass[] _diff = new AllPass[4];
        readonly DelayLine _pre;
        readonly float _rate;
        readonly float _modDepth;
        float _preDelay;
        float _hpState;            // a high-pass on the input so the tail is not boomy
        public float LowCut = 90f;

        /// <param name="rt60">seconds to decay 60 dB (mid band)</param>
        /// <param name="size">scales the delay lengths (1 = a medium hall)</param>
        /// <param name="damping">0..1: how fast the highs die relative to the mids</param>
        public FdnReverb(float sampleRate, float rt60, float size = 1f, float damping = 0.4f, float preDelayMs = 20f, float modDepth = 1.2f, uint seed = 7)
        {
            _rate = sampleRate;
            float sr = sampleRate / 48000f;
            _pre = new DelayLine((int)(0.5f * sampleRate));
            _preDelay = preDelayMs * 0.001f * sampleRate;
            _modDepth = modDepth;
            var rng = new Rng(seed);
            for (int i = 0; i < N; i++)
            {
                _lens[i] = BaseLens[i] * size * sr;
                _lines[i] = new DelayLine((int)(_lens[i] * 1.2f) + 64);
                _gains[i] = MathF.Pow(10f, -3f * _lens[i] / (sampleRate * MathF.Max(rt60, 0.05f)));
                _damp[i] = Dsp.Clamp(damping, 0f, 0.95f);
                _modPhase[i] = rng.Float() * Dsp.TwoPi;
                _modInc[i] = Dsp.TwoPi * (0.15f + rng.Float() * 0.5f) / sampleRate;
            }
            int[] dl = { 142, 107, 379, 277 };
            for (int i = 0; i < 4; i++) _diff[i] = new AllPass((int)(dl[i] * sr * Math.Max(size, 0.5f)), 0.7f);
        }

        public void SetRt60(float rt60)
        {
            for (int i = 0; i < N; i++) _gains[i] = MathF.Pow(10f, -3f * _lens[i] / (_rate * MathF.Max(rt60, 0.05f)));
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float x = (inL + inR) * 0.5f;
            float hpc = Dsp.OnePoleCoef(LowCut, _rate);
            _hpState += (x - _hpState) * hpc;
            x -= _hpState;
            _pre.Write(x);
            x = _pre.Read(_preDelay);
            for (int i = 0; i < 4; i++) x = _diff[i].Process(x);

            float sum = 0f;
            var o = _o;
            for (int i = 0; i < N; i++)
            {
                _modPhase[i] += _modInc[i]; if (_modPhase[i] > Dsp.TwoPi) _modPhase[i] -= Dsp.TwoPi;
                float v = _lines[i].Read(_lens[i] - 1f + _modDepth * MathF.Sin(_modPhase[i]));
                _lp[i] += (v - _lp[i]) * (1f - _damp[i]);
                o[i] = _lp[i] * _gains[i];
                sum += o[i];
            }
            float k = 2f / N * sum;
            float l = 0f, r = 0f;
            for (int i = 0; i < N; i++)
            {
                float fb = o[i] - k;
                _lines[i].Write(fb + x * 0.35f);
                float s = (i & 1) == 0 ? 1f : -1f;
                l += (i < N / 2 ? 1f : s) * o[i];
                r += (i < N / 2 ? s : 1f) * o[i];
            }
            outL = l * 0.35f; outR = r * 0.35f;
        }
    }

    /// <summary>
    /// A plate reverb after Dattorro's figure-of-eight tank: bright, dense, with no early reflections (the sound of a steel plate; the 80s
    /// on vocals, snares and synth leads).
    /// </summary>
    public sealed class PlateReverb
    {
        readonly float _rate;
        readonly AllPass[] _in = new AllPass[4];
        readonly AllPass _apL1, _apR1, _apL2, _apR2;
        readonly DelayLine _dL1, _dR1, _dL2, _dR2, _pre;
        readonly int _lL1, _lR1, _lL2, _lR2;
        float _bw, _dampL, _dampR, _lpIn;
        public float Decay, Damping;
        float _preDelay;
        float _lastL, _lastR;

        public PlateReverb(float sampleRate, float decay = 0.85f, float damping = 0.35f, float bandwidth = 0.9995f, float preDelayMs = 0f)
        {
            _rate = sampleRate;
            float s = sampleRate / 29761f;
            Decay = decay; Damping = damping; _bw = bandwidth;
            _pre = new DelayLine((int)(0.4f * sampleRate));
            _preDelay = preDelayMs * 0.001f * sampleRate;
            _in[0] = new AllPass((int)(142 * s), 0.75f);
            _in[1] = new AllPass((int)(107 * s), 0.75f);
            _in[2] = new AllPass((int)(379 * s), 0.625f);
            _in[3] = new AllPass((int)(277 * s), 0.625f);
            _apL1 = new AllPass((int)(672 * s), 0.7f, 8f * s, 1.1f, sampleRate);
            _apR1 = new AllPass((int)(908 * s), 0.7f, 8f * s, 0.9f, sampleRate, 1.3f);
            _apL2 = new AllPass((int)(1800 * s), 0.5f);
            _apR2 = new AllPass((int)(2656 * s), 0.5f);
            _lL1 = (int)(4453 * s); _lR1 = (int)(4217 * s); _lL2 = (int)(3720 * s); _lR2 = (int)(3163 * s);
            _dL1 = new DelayLine(_lL1 + 64); _dR1 = new DelayLine(_lR1 + 64); _dL2 = new DelayLine(_lL2 + 64); _dR2 = new DelayLine(_lR2 + 64);
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float x = (inL + inR) * 0.5f;
            _pre.Write(x);
            x = _preDelay > 0.5f ? _pre.Read(_preDelay) : x;
            _lpIn += (x - _lpIn) * _bw;
            x = _lpIn;
            for (int i = 0; i < 4; i++) x = _in[i].Process(x);

            // the tank: each side takes the other's last output
            float a = _apL1.Process(x + _lastR * Decay);
            _dL1.Write(a);
            float d1 = _dL1.ReadInt(_lL1 - 1);
            _dampL += (d1 - _dampL) * (1f - Damping);
            float b = _apL2.Process(_dampL * Decay);
            _dL2.Write(b);
            _lastL = _dL2.ReadInt(_lL2 - 1);

            float c = _apR1.Process(x + _lastL * Decay);
            _dR1.Write(c);
            float e1 = _dR1.ReadInt(_lR1 - 1);
            _dampR += (e1 - _dampR) * (1f - Damping);
            float f = _apR2.Process(_dampR * Decay);
            _dR2.Write(f);
            _lastR = _dR2.ReadInt(_lR2 - 1);

            float s = _rate / 29761f;
            outL = 0.6f * (_dL1.ReadInt((int)(266 * s)) + _dL1.ReadInt((int)(2974 * s)) - _dL2.ReadInt((int)(1913 * s)) + _dR2.ReadInt((int)(1996 * s))
                           - _dR1.ReadInt((int)(1990 * s)) - _dL2.ReadInt((int)(187 * s)) - _dR2.ReadInt((int)(1066 * s)));
            outR = 0.6f * (_dR1.ReadInt((int)(353 * s)) + _dR1.ReadInt((int)(3627 * s)) - _dR2.ReadInt((int)(1228 * s)) + _dL2.ReadInt((int)(2673 * s))
                           - _dL1.ReadInt((int)(2111 * s)) - _dR2.ReadInt((int)(335 * s)) - _dL2.ReadInt((int)(121 * s)));
        }
    }
}
