using System;
using System.IO;

namespace Monolith.Audio
{
    /// <summary>Decoded audio: planar float channels at a sample rate.</summary>
    public sealed class PcmBuffer
    {
        public int SampleRate;
        public float[][] Ch;
        public int Frames => Ch.Length == 0 ? 0 : Ch[0].Length;
        public int Channels => Ch.Length;

        public PcmBuffer(int sampleRate, int channels, int frames)
        {
            SampleRate = sampleRate;
            Ch = new float[channels][];
            for (int c = 0; c < channels; c++) Ch[c] = new float[frames];
        }

        public static PcmBuffer FromMono(int sampleRate, float[] mono) => new PcmBuffer(sampleRate, 0, 0) { Ch = new[] { mono } };

        public static PcmBuffer FromStereo(int sampleRate, float[] l, float[] r) => new PcmBuffer(sampleRate, 0, 0) { Ch = new[] { l, r } };

        public PcmBuffer Clone()
        {
            var b = new PcmBuffer(SampleRate, Channels, Frames);
            for (int c = 0; c < Channels; c++) Array.Copy(Ch[c], b.Ch[c], Frames);
            return b;
        }

        public float Peak()
        {
            float p = 0f;
            foreach (var c in Ch) for (int i = 0; i < c.Length; i++) { float a = MathF.Abs(c[i]); if (a > p) p = a; }
            return p;
        }

        public void Scale(float g) { foreach (var c in Ch) for (int i = 0; i < c.Length; i++) c[i] *= g; }

        /// <summary>A mono mix (average of the channels).</summary>
        public float[] Mono()
        {
            if (Channels == 1) return Ch[0];
            var m = new float[Frames];
            for (int c = 0; c < Channels; c++) for (int i = 0; i < m.Length; i++) m[i] += Ch[c][i];
            float k = 1f / Channels;
            for (int i = 0; i < m.Length; i++) m[i] *= k;
            return m;
        }

        public void Resize(int frames)
        {
            for (int c = 0; c < Channels; c++) Array.Resize(ref Ch[c], frames);
        }
    }

    /// <summary>RIFF/WAVE reading (PCM 8/16/24/32 and 32/64-bit float, mono or more) and writing (16/24-bit PCM, 32-bit float).</summary>
    public static class Wav
    {
        public static PcmBuffer Read(string path) => Read(File.ReadAllBytes(path));

        public static PcmBuffer Read(byte[] data)
        {
            if (data.Length < 44 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') throw new InvalidDataException("not a RIFF file");
            int pos = 12, fmtTag = 0, channels = 0, rate = 0, bits = 0;
            int dataPos = -1, dataLen = 0;
            while (pos + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                int len = BitConverter.ToInt32(data, pos + 4);
                if (len < 0 || pos + 8 + len > data.Length) len = data.Length - pos - 8;
                if (id == "fmt ")
                {
                    fmtTag = BitConverter.ToUInt16(data, pos + 8);
                    channels = BitConverter.ToUInt16(data, pos + 10);
                    rate = BitConverter.ToInt32(data, pos + 12);
                    bits = BitConverter.ToUInt16(data, pos + 22);
                    if (fmtTag == 0xFFFE && len >= 26) fmtTag = BitConverter.ToUInt16(data, pos + 32);   // WAVE_FORMAT_EXTENSIBLE: the sub-format's tag
                }
                else if (id == "data") { dataPos = pos + 8; dataLen = len; break; }
                pos += 8 + len + (len & 1);
            }
            if (dataPos < 0 || channels < 1 || rate < 1) throw new InvalidDataException("no fmt/data chunk");
            int bytes = bits / 8;
            int frames = dataLen / (bytes * channels);
            var b = new PcmBuffer(rate, channels, frames);
            for (int i = 0; i < frames; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int o = dataPos + (i * channels + c) * bytes;
                    float v;
                    if (fmtTag == 3) v = bits == 64 ? (float)BitConverter.ToDouble(data, o) : BitConverter.ToSingle(data, o);
                    else if (bits == 16) v = BitConverter.ToInt16(data, o) / 32768f;
                    else if (bits == 24) v = ((data[o] << 8 | data[o + 1] << 16 | data[o + 2] << 24) >> 8) / 8388608f;
                    else if (bits == 32) v = BitConverter.ToInt32(data, o) / 2147483648f;
                    else if (bits == 8) v = (data[o] - 128) / 128f;
                    else throw new InvalidDataException($"unsupported WAV: {bits} bits, tag {fmtTag}");
                    b.Ch[c][i] = v;
                }
            }
            return b;
        }

        public static void Write16(string path, PcmBuffer b, bool dither = true) => Write(path, b, 16, dither);

        public static void Write(string path, PcmBuffer b, int bits = 16, bool dither = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, ToBytes(b, bits, dither));
        }

        public static byte[] ToBytes(PcmBuffer b, int bits = 16, bool dither = true)
        {
            int ch = b.Channels, frames = b.Frames, bytes = bits / 8;
            int dataLen = frames * ch * bytes;
            var ms = new MemoryStream(44 + dataLen);
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)(bits == 32 ? 3 : 1)); w.Write((short)ch); w.Write(b.SampleRate); w.Write(b.SampleRate * ch * bytes);
            w.Write((short)(ch * bytes)); w.Write((short)bits);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            var rng = new Rng(1234567);
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < ch; c++)
                {
                    float v = b.Ch[c][i];
                    if (bits == 32) { w.Write(v); continue; }
                    if (bits == 16)
                    {
                        float d = dither ? (rng.Float() - rng.Float()) : 0f;    // TPDF, +-1 LSB
                        int q = (int)MathF.Round(v * 32767f + d);
                        w.Write((short)(q < -32768 ? -32768 : q > 32767 ? 32767 : q));
                    }
                    else
                    {
                        int q = (int)MathF.Round(v * 8388607f);
                        q = q < -8388608 ? -8388608 : q > 8388607 ? 8388607 : q;
                        w.Write((byte)q); w.Write((byte)(q >> 8)); w.Write((byte)(q >> 16));
                    }
                }
            w.Flush();
            return ms.ToArray();
        }
    }
}
