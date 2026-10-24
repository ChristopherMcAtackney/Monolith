using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The retro brake (Juice/Fx/BrakeFx.cs plays these through IJuiceAudio when the player clicks R3 at speed): the thump of the retro valves slamming open and
    /// the ship taking the load, and the hiss of the jets firing forward. Both are noise and low body, nothing tonal: a steady pitch in a sound this long gets
    /// heard as a whine (the lesson of the death sequence's first hull sound).
    /// </summary>
    public static class SfxBrake
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>The valves slam open and the fighter takes it in the chest: a low thud, a body of dull noise, a clank of metal. 0.9 s.</summary>
        public static Sd Thump()
        {
            const float L = 0.9f;
            var s = new Sd(L);
            s.Sine(t => Ex(t, 86f, 34f, 0.09f), t => 1.4f * Dsp.Smooth(t / 0.004f) * MathF.Exp(-t * 4.5f));                       // the thud
            s.Sine(t => 52f, t => 0.6f * Dsp.Smooth(t / 0.012f) * MathF.Exp(-t * 6.5f));
            s.Mix(Noise(L, 811u, t => Hit(t, 45f, 0.0006f)).BandPass(900f, 0.25f, 2), -3f);                                        // the valves
            s.Mix(Noise(L, 812u, t => Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 3.5f)).LowPass(260f, 0.2f, 2), 3f);                   // the body of the blow
            s.Mix(Noise(L, 813u, t => Hit(t, 70f, 0.0004f)).HighPass(2500f), -9f);                                                 // a tick of metal
            s.Drive(1.5f);
            s.Reverb(0.35f, 0.06f, 0.35f, 0.7f, 3f, 0.12f);
            return s.Edges(0.3f, 100f).Normalize(-1.5f);
        }

        /// <summary>The retro jets firing: a gas hiss that opens at once and falls away as the pressure drops, with a soft roar under it. 1.8 s.</summary>
        public static Sd Hiss()
        {
            const float L = 1.8f;
            var s = new Sd(L);
            Func<float, float> env = t => Dsp.Smooth(t / 0.05f) * MathF.Exp(-t * 0.5f) * (t < 0.9f ? 1f : MathF.Exp(-(t - 0.9f) * 3.2f));
            s.Mix(Noise(L, 821u, env).BandPass(t => 3400f - 1600f * Dsp.Clamp01(t / 1.4f), 0.2f, 2), 2f);                         // the jets, the pressure falling
            s.Mix(Noise(L, 822u, env).HighPass(1800f).LowPass(6500f, 0.2f, 2), -8f);
            s.Mix(Noise(L, 823u, t => Dsp.Smooth(t / 0.08f) * MathF.Exp(-t * 1.6f)).LowPass(380f, 0.2f, 2), 0f);                  // the roar under it
            s.Drive(1.2f);
            s.Reverb(0.4f, 0.06f, 0.4f, 0.7f, 3f, 0.1f);
            return s.Edges(0.2f, 300f).Normalize(-3f);
        }
    }
}
