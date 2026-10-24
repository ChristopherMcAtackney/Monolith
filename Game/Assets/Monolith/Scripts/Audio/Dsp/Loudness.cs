using System;
using System.Collections.Generic;

namespace Monolith.Audio
{
    /// <summary>
    /// Loudness the way the broadcast world measures it (ITU-R BS.1770-4 / EBU R128): K-weighted, gated, in LUFS. The mix is set by these numbers,
    /// not by ear. Also sample peak and a 4x-oversampled true peak.
    /// </summary>
    public sealed class LoudnessMeter
    {
        readonly int _rate;
        readonly Biquad[] _shelf, _hp;
        readonly int _blockLen, _hop;
        readonly double[][] _ring;           // per channel: squared K-weighted samples of the current 400 ms window
        readonly List<double> _blocks = new List<double>();    // mean-square power of each 400 ms block (summed over channels)
        readonly double[] _stSq;             // for short-term (3 s) = last 30 blocks of hop 100 ms
        int _sinceHop;
        long _total;
        double[] _sum;                       // running sum of squares per channel inside the window
        public float SamplePeak;
        public int Clipped;                  // samples at or above full scale

        public LoudnessMeter(int sampleRate, int channels)
        {
            _rate = sampleRate;
            Channels = channels;
            _shelf = new Biquad[channels]; _hp = new Biquad[channels];
            for (int c = 0; c < channels; c++)
            {
                MakeKWeight(sampleRate, out _shelf[c], out _hp[c]);
            }
            _blockLen = (int)(0.4 * sampleRate);
            _hop = (int)(0.1 * sampleRate);
            _ring = new double[channels][];
            for (int c = 0; c < channels; c++) _ring[c] = new double[_blockLen];
            _sum = new double[channels];
            _stSq = new double[30];
        }

        public int Channels { get; }

        /// <summary>The two K-weighting stages (a high shelf, then a 38 Hz high-pass) for any sample rate, from the BS.1770 analog prototypes.</summary>
        public static void MakeKWeight(int fs, out Biquad shelf, out Biquad hp)
        {
            {
                double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
                double K = Math.Tan(Math.PI * f0 / fs), Vh = Math.Pow(10, G / 20), Vb = Math.Pow(Vh, 0.4996667741545416);
                double a0 = 1 + K / Q + K * K;
                shelf = new Biquad
                {
                    B0 = (float)((Vh + Vb * K / Q + K * K) / a0), B1 = (float)(2 * (K * K - Vh) / a0), B2 = (float)((Vh - Vb * K / Q + K * K) / a0),
                    A1 = (float)(2 * (K * K - 1) / a0), A2 = (float)((1 - K / Q + K * K) / a0),
                };
            }
            {
                double f0 = 38.13547087602444, Q = 0.5003270373238773;
                double K = Math.Tan(Math.PI * f0 / fs);
                double a0 = 1 + K / Q + K * K;
                hp = new Biquad { B0 = 1f, B1 = -2f, B2 = 1f, A1 = (float)(2 * (K * K - 1) / a0), A2 = (float)((1 - K / Q + K * K) / a0) };
            }
        }

        /// <summary>Feed planar samples (one array per channel).</summary>
        public void Add(float[][] ch, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int slot = (int)(_total % _blockLen);
                for (int c = 0; c < Channels; c++)
                {
                    float x = ch[c][offset + i];
                    float a = MathF.Abs(x);
                    if (a > SamplePeak) SamplePeak = a;
                    if (a >= 1f) Clipped++;
                    float y = _hp[c].Process(_shelf[c].Process(x));
                    double sq = (double)y * y;
                    _sum[c] += sq - _ring[c][slot];
                    _ring[c][slot] = sq;
                }
                _total++;
                if (++_sinceHop >= _hop)
                {
                    _sinceHop = 0;
                    if (_total >= _blockLen)
                    {
                        double p = 0;
                        for (int c = 0; c < Channels; c++) p += Math.Max(_sum[c], 0) / _blockLen;   // channel weights are 1 for L/R
                        _blocks.Add(p);
                    }
                }
            }
        }

        public void Add(float[] mono) => Add(new[] { mono }, 0, mono.Length);

        static float ToLufs(double power) => power <= 1e-12 ? -120f : (float)(-0.691 + 10 * Math.Log10(power));

        /// <summary>Momentary loudness (the last 400 ms), LUFS.</summary>
        public float Momentary => _blocks.Count == 0 ? -120f : ToLufs(_blocks[_blocks.Count - 1]);

        /// <summary>Short-term loudness (the last 3 s), LUFS.</summary>
        public float ShortTerm
        {
            get
            {
                if (_blocks.Count == 0) return -120f;
                int n = Math.Min(30, _blocks.Count);
                double s = 0;
                for (int i = _blocks.Count - n; i < _blocks.Count; i++) s += _blocks[i];
                return ToLufs(s / n);
            }
        }

        /// <summary>Integrated loudness with the absolute (-70) and relative (-10 LU) gates, LUFS. -120 when nothing is above the gate.</summary>
        public float Integrated
        {
            get
            {
                double s = 0; int n = 0;
                foreach (var b in _blocks) if (ToLufs(b) > -70f) { s += b; n++; }
                if (n == 0) return -120f;
                float rel = ToLufs(s / n) - 10f;
                double s2 = 0; int n2 = 0;
                foreach (var b in _blocks) if (ToLufs(b) > rel && ToLufs(b) > -70f) { s2 += b; n2++; }
                return n2 == 0 ? -120f : ToLufs(s2 / n2);
            }
        }

        /// <summary>The loudest 400 ms window so far, LUFS (for matching short sounds).</summary>
        public float MaxMomentary
        {
            get
            {
                double m = 0; foreach (var b in _blocks) if (b > m) m = b;
                return ToLufs(m);
            }
        }

        /// <summary>Loudness range (LRA) in LU from the short-term distribution, gated at -20 LU relative.</summary>
        public float Range
        {
            get
            {
                var st = new List<float>();
                for (int i = 29; i < _blocks.Count; i += 10)       // 3 s windows, hop 1 s
                {
                    double s = 0; for (int j = i - 29; j <= i; j++) s += _blocks[j];
                    float l = ToLufs(s / 30);
                    if (l > -70f) st.Add(l);
                }
                if (st.Count < 2) return 0f;
                double sumP = 0; foreach (var l in st) sumP += Math.Pow(10, l / 10);
                float rel = (float)(10 * Math.Log10(sumP / st.Count)) - 20f;
                var g = st.FindAll(l => l > rel);
                if (g.Count < 2) return 0f;
                g.Sort();
                return g[(int)(0.95 * (g.Count - 1))] - g[(int)(0.10 * (g.Count - 1))];
            }
        }

        public float PeakDb => Db.FromLin(SamplePeak);

        /// <summary>Convenience: measure a whole buffer.</summary>
        public static LoudnessMeter Measure(PcmBuffer b)
        {
            var m = new LoudnessMeter(b.SampleRate, b.Channels);
            m.Add(b.Ch, 0, b.Frames);
            return m;
        }

        /// <summary>True peak (4x oversampled, windowed-sinc) in dBTP.</summary>
        public static float TruePeakDb(PcmBuffer b)
        {
            float best = 0f;
            const int taps = 12;
            var kern = new float[4][];
            for (int ph = 0; ph < 4; ph++)
            {
                kern[ph] = new float[taps];
                float frac = ph / 4f;
                for (int k = 0; k < taps; k++)
                {
                    float x = (k - taps / 2 + 1) - frac;
                    float sinc = MathF.Abs(x) < 1e-6f ? 1f : MathF.Sin(Dsp.Pi * x) / (Dsp.Pi * x);
                    float w = 0.5f + 0.5f * MathF.Cos(Dsp.Pi * x / (taps / 2f + 0.5f));
                    kern[ph][k] = sinc * w;
                }
            }
            foreach (var c in b.Ch)
            {
                for (int i = taps; i < c.Length - taps; i++)
                    for (int ph = 0; ph < 4; ph++)
                    {
                        float s = 0;
                        for (int k = 0; k < taps; k++) s += c[i + k - taps / 2 + 1] * kern[ph][k];
                        s = MathF.Abs(s);
                        if (s > best) best = s;
                    }
            }
            return Db.FromLin(best);
        }
    }

    /// <summary>
    /// A lookahead brick-wall limiter: the last thing on the master bus, so nothing the mix does can clip. The gain is the sliding minimum of
    /// what each sample needs over the lookahead, released slowly, then smoothed by a boxcar as long as the lookahead (so the gain arrives
    /// as a short ramp, exactly when the peak leaves the delay line).
    /// </summary>
    public sealed class Limiter
    {
        readonly int _look, _channels;
        readonly float[][] _delay;
        readonly float[] _need, _rb;
        readonly int[] _dq;                 // indices of the sliding minimum (a monotonic deque)
        int _dqHead, _dqCount;
        long _n;                            // samples seen
        readonly float[] _nv;               // the need values by absolute position modulo the window
        int _w;
        float _ceiling, _r = 1f;
        double _sum;
        readonly float _relCoef;
        public float MinGainSeen = 1f;

        public Limiter(int sampleRate, int channels, float ceilingDb = -1.0f, float lookaheadMs = 2.0f, float releaseMs = 120f)
        {
            _channels = channels;
            _look = Math.Max(8, (int)(lookaheadMs * 0.001f * sampleRate));
            _delay = new float[channels][];
            for (int c = 0; c < channels; c++) _delay[c] = new float[_look];
            _need = new float[_look]; _rb = new float[_look];
            _dq = new int[_look + 1]; _nv = new float[_look + 1];
            for (int i = 0; i < _look; i++) { _need[i] = 1f; _rb[i] = 1f; }
            _sum = _look;
            _ceiling = Db.ToLin(ceilingDb);
            _relCoef = Dsp.TauToCoef(releaseMs * 0.001f, sampleRate);
        }

        public float Ceiling { get => _ceiling; set => _ceiling = value; }
        public int Latency => _look - 1;

        /// <summary>Process planar channels in place.</summary>
        public void Process(float[][] ch, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float pk = 0f;
                for (int c = 0; c < _channels; c++) { float a = MathF.Abs(ch[c][offset + i]); if (a > pk) pk = a; }
                float nd = pk > _ceiling ? _ceiling / pk : 1f;
                _need[_w] = nd;
                // sliding minimum over the last _look samples in O(1): drop larger values from the back, expired from the front
                int cap = _look + 1;
                _nv[(int)(_n % cap)] = nd;
                while (_dqCount > 0 && _nv[_dq[(_dqHead + _dqCount - 1) % cap] % cap] >= nd) _dqCount--;
                // _dq holds absolute indices modulo cap
                _dq[(_dqHead + _dqCount) % cap] = (int)(_n % cap); _dqCount++;
                // expire: absolute position of the front must be > _n - _look
                while (_dqCount > 1)
                {
                    long frontAbs = _n - ((_n % cap - _dq[_dqHead] + cap) % cap);
                    if (frontAbs <= _n - _look) { _dqHead = (_dqHead + 1) % cap; _dqCount--; } else break;
                }
                float gmin = _nv[_dq[_dqHead]];
                _n++;
                _r = gmin < _r ? gmin : _r + (gmin - _r) * _relCoef;
                _sum += _r - _rb[_w];
                _rb[_w] = _r;
                float gain = (float)(_sum / _look);
                if (gain < MinGainSeen) MinGainSeen = gain;
                int rd = _w + 1 == _look ? 0 : _w + 1;
                for (int c = 0; c < _channels; c++)
                {
                    float x = ch[c][offset + i];
                    float d = _delay[c][rd];
                    _delay[c][_w] = x;
                    float y = d * gain;
                    ch[c][offset + i] = y > _ceiling ? _ceiling : y < -_ceiling ? -_ceiling : y;
                }
                _w = _w + 1 == _look ? 0 : _w + 1;
            }
        }
    }
}
