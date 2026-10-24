using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Scratch checks (MnAudio debug): the gain of the reverbs on steady noise, so a return at 0 dB means as loud as the dry signal.</summary>
    public static class DebugCmd
    {
        public static int Run(string[] args)
        {
            const int R = 48000;
            if (args.Length > 1 && args[1] == "perf") return Perf();
            var rng = new Rng(5);
            int n = R * 12;
            var x = new float[n];
            var lp = 0f;
            for (int i = 0; i < n; i++) { lp += (rng.Signed() - lp) * 0.2f; x[i] = lp * 0.5f; }
            double rin = Rms(x, R * 4, n);
            var l = new float[n]; var r = new float[n];
            foreach (var rt in new[] { 1.5f, 4.5f, 7f })
            {
                Fx.Hall(x, x, l, r, R, rt, 1.25f, 0.45f, 20f);
                Console.WriteLine($"hall rt60 {rt}: in rms {rin:0.0000}, wet L {Rms(l, R * 4, n):0.0000} ({Db.FromLin((float)(Rms(l, R * 4, n) / rin)):0.0} dB)");
            }
            Fx.Plate(x, x, l, r, R, 0.84f, 0.35f, 8f);
            Console.WriteLine($"plate: wet L {Rms(l, R * 4, n):0.0000} ({Db.FromLin((float)(Rms(l, R * 4, n) / rin)):0.0} dB)");
            Fx.PingPong(x, x, l, r, R, 0.4, 0.42f);
            Console.WriteLine($"delay: wet L {Rms(l, R * 4, n):0.0000} ({Db.FromLin((float)(Rms(l, R * 4, n) / rin)):0.0} dB)");
            return 0;
        }

        static int Perf()
        {
            const int R = 48000;
            var mx = new Mixer(R);
            var rng = new Rng(9);
            var music = new PcmBuffer(R, 2, R * 70);
            for (int i = 0; i < music.Frames; i++) { music.Ch[0][i] = 0.2f * MathF.Sin(i * 0.01f); music.Ch[1][i] = 0.2f * MathF.Sin(i * 0.011f); }
            var mc = Clip.FromPcm(music, "m");
            mx.MusicPlay(mc, Quant.Now, 0f, 0f);
            var b = new PcmBuffer(R, 1, R * 2);
            for (int i = 0; i < b.Frames; i++) b.Ch[0][i] = 0.3f * MathF.Sin(i * 0.05f) * MathF.Exp(-i / 20000f);
            var sc = Clip.FromPcm(b, "s");
            foreach (var nv in new[] { 0, 16, 40 })
            {
                var m2 = new Mixer(R);
                m2.MusicPlay(mc, Quant.Now, 0f, 0f);
                for (int i = 0; i < nv; i++) m2.Play(sc, new PlayParams { Gain = 0.2f, Pan = rng.Signed(), Pitch = 1f + rng.Signed() * 0.05f, CutoffHz = 3000f, Loop = true, Bus = Bus.Sfx, Priority = 1f });
                var l = new float[R * 10]; var r = new float[R * 10];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                m2.RenderPlanar(l, r, 0, R * 10);
                Console.WriteLine($"{nv} looping voices + music: {sw.ElapsedMilliseconds / 10.0:0.0} ms per second of sound ({sw.ElapsedMilliseconds / 10.0 / 10.0:0.0}% of one core)");
            }
            return 0;
        }

        static double Rms(float[] x, int a, int b) { double s = 0; for (int i = a; i < b; i++) s += x[i] * x[i]; return Math.Sqrt(s / (b - a)); }
    }
}
