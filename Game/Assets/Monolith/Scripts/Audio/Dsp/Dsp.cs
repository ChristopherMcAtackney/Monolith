using System;

namespace Monolith.Audio
{
    // The audio DSP core. Plain C# with no Unity types, so the same source runs in the game (the mixer, the radio filter, the
    // loudness meter) and in tools/audio (the offline synth that renders the music and the effects). C# 9, .NET Standard 2.1.

    public static class Db
    {
        public static float ToLin(float db) => MathF.Pow(10f, db * 0.05f);
        public static float FromLin(float lin) => 20f * MathF.Log10(MathF.Max(lin, 1e-9f));
    }

    public static class Dsp
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;

        public static float Clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;
        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        public static float InvLerp(float a, float b, float x) => a == b ? 0f : Clamp01((x - a) / (b - a));
        /// <summary>A hash of a string that is the same on every machine and every run (string.GetHashCode is not): seeds must not depend on the process.</summary>
        public static uint StableHash(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        public static float MidiToHz(float note) => 440f * MathF.Pow(2f, (note - 69f) / 12f);
        public static float HzToMidi(float hz) => 69f + 12f * MathF.Log(hz / 440f) / MathF.Log(2f);

        /// <summary>A smooth saturator: x for small x, approaching +-1. (tanh through a rational approximation.)</summary>
        public static float Tanh(float x)
        {
            if (x < -3f) return -1f;
            if (x > 3f) return 1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>Soft clip with a drive: a slightly asymmetric tube-like curve (even harmonics) when <paramref name="bias"/> is not 0.</summary>
        public static float Saturate(float x, float drive, float bias = 0f)
        {
            float y = Tanh(x * drive + bias) - Tanh(bias);
            return y / MathF.Max(Tanh(drive + bias) - Tanh(bias), 0.05f);
        }

        /// <summary>Four-point Hermite interpolation between y1 and y2 (frac 0..1) with neighbours y0 and y3.</summary>
        public static float Hermite(float y0, float y1, float y2, float y3, float t)
        {
            float c0 = y1;
            float c1 = 0.5f * (y2 - y0);
            float c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
            float c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
            return ((c3 * t + c2) * t + c1) * t + c0;
        }

        /// <summary>Time constant in seconds to a one-pole coefficient per sample: y += (x - y) * coef.</summary>
        public static float TauToCoef(float tauSeconds, float sampleRate) => tauSeconds <= 0f ? 1f : 1f - MathF.Exp(-1f / (tauSeconds * sampleRate));

        public static float OnePoleCoef(float cutoffHz, float sampleRate) => 1f - MathF.Exp(-TwoPi * Clamp(cutoffHz, 1f, sampleRate * 0.49f) / sampleRate);
    }

    /// <summary>A small deterministic random source (xorshift): the same seed gives the same sound on every machine.</summary>
    public sealed class Rng
    {
        uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; Next(); Next(); }
        public uint Next() { uint x = _s; x ^= x << 13; x ^= x >> 17; x ^= x << 5; _s = x; return x; }
        /// <summary>0..1</summary>
        public float Float() => (Next() >> 8) * (1f / 16777216f);
        /// <summary>-1..1</summary>
        public float Signed() => Float() * 2f - 1f;
        public float Range(float a, float b) => a + (b - a) * Float();
        public int Int(int n) => (int)(Next() % (uint)Math.Max(n, 1));
        /// <summary>Roughly gaussian, sd 1.</summary>
        public float Gauss() { float s = 0; for (int i = 0; i < 6; i++) s += Float(); return (s - 3f) * 1.4142f; }
    }

    /// <summary>One-pole low-pass.</summary>
    public struct OnePole
    {
        public float Y;
        public float Process(float x, float coef) { Y += (x - Y) * coef; return Y; }
    }

    /// <summary>Removes DC and sub-sonic drift (a one-pole high-pass at about 8 Hz).</summary>
    public struct DcBlock
    {
        float _x1, _y1;
        public float Process(float x) { float y = x - _x1 + 0.9995f * _y1; _x1 = x; _y1 = y; return y; }
    }

    public enum BiquadType { LowPass, HighPass, BandPass, Notch, Peak, LowShelf, HighShelf, AllPass }

    /// <summary>RBJ cookbook biquad, transposed direct form II.</summary>
    public struct Biquad
    {
        public float B0, B1, B2, A1, A2;
        float _z1, _z2;

        public static Biquad Make(BiquadType t, float sampleRate, float freq, float q = 0.7071f, float gainDb = 0f)
        {
            var b = new Biquad();
            b.Set(t, sampleRate, freq, q, gainDb);
            return b;
        }

        public void Set(BiquadType t, float sampleRate, float freq, float q = 0.7071f, float gainDb = 0f)
        {
            freq = Dsp.Clamp(freq, 5f, sampleRate * 0.499f);
            q = MathF.Max(q, 0.05f);
            float w0 = Dsp.TwoPi * freq / sampleRate, cw = MathF.Cos(w0), sw = MathF.Sin(w0);
            float alpha = sw / (2f * q);
            float A = MathF.Pow(10f, gainDb / 40f);
            float b0, b1, b2, a0, a1, a2;
            switch (t)
            {
                case BiquadType.LowPass: b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = (1 - cw) / 2; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                case BiquadType.HighPass: b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = (1 + cw) / 2; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                case BiquadType.BandPass: b0 = alpha; b1 = 0; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                case BiquadType.Notch: b0 = 1; b1 = -2 * cw; b2 = 1; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
                case BiquadType.Peak: b0 = 1 + alpha * A; b1 = -2 * cw; b2 = 1 - alpha * A; a0 = 1 + alpha / A; a1 = -2 * cw; a2 = 1 - alpha / A; break;
                case BiquadType.LowShelf:
                {
                    float sq = 2f * MathF.Sqrt(A) * alpha;
                    b0 = A * ((A + 1) - (A - 1) * cw + sq); b1 = 2 * A * ((A - 1) - (A + 1) * cw); b2 = A * ((A + 1) - (A - 1) * cw - sq);
                    a0 = (A + 1) + (A - 1) * cw + sq; a1 = -2 * ((A - 1) + (A + 1) * cw); a2 = (A + 1) + (A - 1) * cw - sq; break;
                }
                case BiquadType.HighShelf:
                {
                    float sq = 2f * MathF.Sqrt(A) * alpha;
                    b0 = A * ((A + 1) + (A - 1) * cw + sq); b1 = -2 * A * ((A - 1) + (A + 1) * cw); b2 = A * ((A + 1) + (A - 1) * cw - sq);
                    a0 = (A + 1) - (A - 1) * cw + sq; a1 = 2 * ((A - 1) - (A + 1) * cw); a2 = (A + 1) - (A - 1) * cw - sq; break;
                }
                default: b0 = 1 - alpha; b1 = -2 * cw; b2 = 1 + alpha; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
            }
            float inv = 1f / a0;
            B0 = b0 * inv; B1 = b1 * inv; B2 = b2 * inv; A1 = a1 * inv; A2 = a2 * inv;
        }

        /// <summary>Change the response without clearing the state (for sweeps).</summary>
        public float Process(float x)
        {
            float y = B0 * x + _z1;
            _z1 = B1 * x - A1 * y + _z2;
            _z2 = B2 * x - A2 * y;
            return y;
        }

        public void Reset() { _z1 = _z2 = 0f; }
    }

    /// <summary>Zero-delay-feedback state-variable filter (Zavalishin): low, band and high pass from one structure, stable under fast modulation.</summary>
    public struct Svf
    {
        float _ic1, _ic2, _g, _k, _a1, _a2, _a3;
        public float Low, Band, High;

        public void Set(float cutoffHz, float resonance01, float sampleRate)
        {
            cutoffHz = Dsp.Clamp(cutoffHz, 8f, sampleRate * 0.45f);
            _g = MathF.Tan(Dsp.Pi * cutoffHz / sampleRate);
            _k = 2f - 2f * Dsp.Clamp(resonance01, 0f, 0.995f);
            _a1 = 1f / (1f + _g * (_g + _k));
            _a2 = _g * _a1;
            _a3 = _g * _a2;
        }

        public void Process(float x)
        {
            float v3 = x - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            Low = v2; Band = v1; High = x - _k * v1 - v2;
        }

        public void Reset() { _ic1 = _ic2 = 0f; }
    }

    /// <summary>
    /// A four-pole resonant low-pass in the Moog manner (zero-delay feedback, one tanh in the loop): the bass of the 80s. Resonance 0..1
    /// (1 is near self-oscillation); drive pushes the input into the saturation.
    /// </summary>
    public struct Ladder
    {
        float _s1, _s2, _s3, _s4, _g, _gComp, _k;

        public void Set(float cutoffHz, float resonance01, float sampleRate)
        {
            cutoffHz = Dsp.Clamp(cutoffHz, 10f, sampleRate * 0.45f);
            float wc = Dsp.Pi * cutoffHz / sampleRate;
            _g = MathF.Tan(wc);
            _g = _g / (1f + _g);
            _k = 4f * Dsp.Clamp(resonance01, 0f, 1.05f);
            _gComp = 1f;
        }

        public float Process(float x, float drive = 1f)
        {
            // Solve the loop for the linear case, then saturate the input of each stage lightly.
            float g = _g;
            float G = g * g * g * g;
            float S = g * g * g * _s1 * (1f - g) + g * g * _s2 * (1f - g) + g * _s3 * (1f - g) + _s4 * (1f - g);
            // Feedback estimate (one-sample delayed in the nonlinear part).
            float u = (Dsp.Tanh(x * drive) - _k * S) / (1f + _k * G);
            u = Dsp.Tanh(u);
            float v1 = (u - _s1) * g; float y1 = v1 + _s1; _s1 = y1 + v1;
            float v2 = (y1 - _s2) * g; float y2 = v2 + _s2; _s2 = y2 + v2;
            float v3 = (y2 - _s3) * g; float y3 = v3 + _s3; _s3 = y3 + v3;
            float v4 = (y3 - _s4) * g; float y4 = v4 + _s4; _s4 = y4 + v4;
            return y4;
        }

        public void Reset() { _s1 = _s2 = _s3 = _s4 = 0f; }
    }

    /// <summary>A circular delay with fractional reads (Hermite), for echoes, chorus and reverb.</summary>
    public sealed class DelayLine
    {
        readonly float[] _buf;
        readonly int _mask;
        int _w;

        public DelayLine(int maxSamples)
        {
            int n = 16;
            while (n < maxSamples + 8) n <<= 1;
            _buf = new float[n];
            _mask = n - 1;
        }

        public int Capacity => _buf.Length - 8;

        public void Write(float x) { _buf[_w] = x; _w = (_w + 1) & _mask; }

        /// <summary>Read <paramref name="delay"/> samples back (0 = the last written, as long as Write came first).</summary>
        public float Read(float delay)
        {
            float pos = _w - 1 - delay;
            int i = (int)MathF.Floor(pos);
            float f = pos - i;
            return Dsp.Hermite(_buf[(i - 1) & _mask], _buf[i & _mask], _buf[(i + 1) & _mask], _buf[(i + 2) & _mask], f);
        }

        public float ReadInt(int delay) => _buf[(_w - 1 - delay) & _mask];

        public void Clear() { Array.Clear(_buf, 0, _buf.Length); }
    }

    /// <summary>An analog-style ADSR: exponential segments, like an RC envelope generator.</summary>
    public struct Adsr
    {
        public enum Stage { Idle, Attack, Decay, Sustain, Release }
        public Stage State;
        public float Level;
        float _aCoef, _dCoef, _rCoef, _sus;

        public void Set(float attack, float decay, float sustain, float release, float sampleRate)
        {
            _aCoef = attack <= 0.0005f ? 1f : 1f - MathF.Exp(-4.6f / (attack * sampleRate));        // reaches ~99% in the attack time
            _dCoef = decay <= 0.0005f ? 1f : 1f - MathF.Exp(-4.6f / (decay * sampleRate));
            _rCoef = release <= 0.0005f ? 1f : 1f - MathF.Exp(-4.6f / (release * sampleRate));
            _sus = sustain;
        }

        public void Gate(bool on)
        {
            if (on) State = Stage.Attack;          // from wherever the level is, as an analog envelope does
            else if (State != Stage.Idle) State = Stage.Release;
        }

        public bool Active => State != Stage.Idle;

        public float Next()
        {
            switch (State)
            {
                case Stage.Attack:
                    Level += (1.08f - Level) * _aCoef;
                    if (Level >= 1f) { Level = 1f; State = Stage.Decay; }
                    break;
                case Stage.Decay:
                    Level += (_sus - Level) * _dCoef;
                    if (Level - _sus < 0.0005f) { Level = _sus; State = Stage.Sustain; }
                    break;
                case Stage.Sustain: Level = _sus; break;
                case Stage.Release:
                    Level += (0f - Level) * _rCoef;
                    if (Level < 0.00008f) { Level = 0f; State = Stage.Idle; }
                    break;
            }
            return Level;
        }
    }

    /// <summary>Soft-knee-free helper: peak/RMS follower for meters and sidechains.</summary>
    public struct Follower
    {
        public float Value;
        public float Process(float x, float attackCoef, float releaseCoef)
        {
            float a = MathF.Abs(x);
            Value += (a - Value) * (a > Value ? attackCoef : releaseCoef);
            return Value;
        }
    }
}
