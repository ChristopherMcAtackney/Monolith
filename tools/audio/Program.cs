using System;
using System.Linq;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "help";
            switch (cmd)
            {
                case "mixertest": return MixerTests.Run();
                case "music": return MusicCmd.Run(args);
                case "sheet": return SheetCmd.Run(args);
                case "docs": return DocsCmd.Run(args);
                case "voice": return VoiceCmd.Run(args);
                case "sfx": return SfxCmd.Run(args);
                case "stems": return MusicCmd.Stems(args);
                case "debug": return DebugCmd.Run(args);
                case "analyse": return AnalyseCmd.Run(args);
                default:
                    Console.WriteLine("MnAudio <command>\n  mixertest   checks of the mixer and DSP core against known signals");
                    return cmd == "help" ? 0 : 64;
            }
        }
    }
}
