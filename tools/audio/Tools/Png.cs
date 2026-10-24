using System;
using System.IO;
using System.IO.Compression;

namespace Monolith.AudioTools
{
    /// <summary>A minimal PNG writer (8-bit RGB), enough for spectrograms and the audio sheet. No dependencies.</summary>
    public static class Png
    {
        static uint[] _crc;

        static uint Crc(byte[] d, int off, int len, uint crc = 0xFFFFFFFFu)
        {
            if (_crc == null)
            {
                _crc = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crc[n] = c; }
            }
            for (int i = 0; i < len; i++) crc = _crc[(crc ^ d[off + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; len[0] = (byte)(data.Length >> 24); len[1] = (byte)(data.Length >> 16); len[2] = (byte)(data.Length >> 8); len[3] = (byte)data.Length;
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
            Array.Copy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            uint c = Crc(td, 0, td.Length) ^ 0xFFFFFFFFu;
            s.Write(new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c }, 0, 4);
        }

        /// <summary>rgb: width*height*3 bytes, row-major.</summary>
        public static void Write(string path, int w, int h, byte[] rgb)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var fs = File.Create(path))
            {
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
                ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
                ihdr[8] = 8; ihdr[9] = 2;
                Chunk(fs, "IHDR", ihdr);
                var raw = new byte[h * (w * 3 + 1)];
                for (int y = 0; y < h; y++)
                {
                    raw[y * (w * 3 + 1)] = 0;
                    Buffer.BlockCopy(rgb, y * w * 3, raw, y * (w * 3 + 1) + 1, w * 3);
                }
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                    Chunk(fs, "IDAT", ms.ToArray());
                }
                Chunk(fs, "IEND", new byte[0]);
            }
        }
    }
}
