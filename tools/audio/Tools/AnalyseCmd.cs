using System;
using System.IO;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>MnAudio analyse file.wav [out.png]: loudness, peaks, and a spectrogram to look at.</summary>
    public static class AnalyseCmd
    {
        public static string Describe(PcmBuffer b)
        {
            var m = LoudnessMeter.Measure(b);
            float tp = LoudnessMeter.TruePeakDb(b);
            return $"{b.Frames / (double)b.SampleRate:0.00} s, {b.SampleRate} Hz x{b.Channels}: integrated {m.Integrated:0.0} LUFS, momentary max {m.MaxMomentary:0.0}, LRA {m.Range:0.0} LU, peak {m.PeakDb:0.0} dBFS, true peak {tp:0.0} dBTP";
        }

        public static int Run(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("usage: analyse in.wav [out.png]"); return 64; }
            var b = Wav.Read(args[1]);
            Console.WriteLine(Describe(b));
            Console.WriteLine(Spectro.BandsLine(b));
            string png = args.Length > 2 ? args[2] : Path.ChangeExtension(args[1], ".png");
            Spectro.Write(png, b);
            Console.WriteLine("wrote " + png);
            return 0;
        }
    }
}
