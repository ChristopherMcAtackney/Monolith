using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Band-limited oscillators (PolyBLEP) and a few free helpers for the analog synth.</summary>
    public static class Osc
    {
        /// <summary>The PolyBLEP residual for a unit step at phase 0 (t in 0..1, dt = phase increment).</summary>
        public static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }

        public static float Saw(float phase, float dt) => 2f * phase - 1f - Blep(phase, dt);

        public static float Pulse(float phase, float dt, float width)
        {
            float v = phase < width ? 1f : -1f;
            v += Blep(phase, dt);
            float p2 = phase - width; if (p2 < 0f) p2 += 1f;
            v -= Blep(p2, dt);
            return v;
        }

        /// <summary>Triangle: the integral of the band-limited square, in effect (a leaky integrator is not needed at these rates; the naive shape aliases very little).</summary>
        public static float Tri(float phase) => 4f * MathF.Abs(phase - 0.5f) - 1f;

        public static float Sine(float phase) => MathF.Sin(phase * Dsp.TwoPi);

        public static float Wave(int wave, float phase, float dt, float width)
        {
            switch (wave)
            {
                case 0: return Saw(phase, dt);
                case 1: return Pulse(phase, dt, width);
                case 2: return Tri(phase);
                default: return Sine(phase);
            }
        }

        public static uint Hash(uint a, uint b = 0, uint c = 0)
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA6Bu ^ (c + 0x1B873593u) * 0xC2B2AE35u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>A slow smoothed random walk, for analog drift and drift in the "tape".</summary>
    public struct Drift
    {
        float _a, _b, _t, _inc, _v;
        Rng _r;
        public Drift(uint seed, float rateHz, float sampleRate)
        {
            _r = new Rng(seed); _a = _r.Signed(); _b = _r.Signed(); _t = 0; _inc = rateHz / sampleRate; _v = _a;
        }
        public float Next()
        {
            _t += _inc;
            if (_t >= 1f) { _t -= 1f; _a = _b; _b = _r.Signed(); }
            float s = _t * _t * (3f - 2f * _t);
            _v = _a + (_b - _a) * s;
            return _v;
        }
    }
}
