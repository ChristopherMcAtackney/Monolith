using System;
using Monolith.Core;
using Unity.Collections;
using UnityEngine;

namespace Monolith.Sky
{
    /// <summary>
    /// The star table (Resources/MonolithSky/stars.bytes, built by tools/sky/build_stars.py) on the GPU: one structured buffer
    /// of float4 (equatorial unit vector, V magnitude) and one of packed colours, brightest first, so drawing "every star
    /// brighter than magnitude m" is just a shorter draw. Shared by every sky renderer and reference counted.
    /// </summary>
    public sealed class StarCatalog
    {
        public const string ResourcePath = "MonolithSky/stars";
        static StarCatalog _shared;
        static int _refs;

        public int Count { get; private set; }
        public ComputeBuffer PosMag { get; private set; }
        public ComputeBuffer Color { get; private set; }
        float[] _mags;

        public static StarCatalog Acquire()
        {
            if (_shared == null) _shared = Load();
            _refs++;
            return _shared;
        }

        public void Release()
        {
            if (--_refs > 0) return;
            _refs = 0;
            PosMag?.Release(); Color?.Release();
            PosMag = Color = null;
            if (_shared == this) _shared = null;
        }

        /// <summary>How many of the stars (the brightest ones) are at or brighter than the magnitude.</summary>
        public int CountBrighterThan(float magnitude)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_mags[mid] <= magnitude) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        static StarCatalog Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath)
                        ?? throw new InvalidOperationException($"Resources/{ResourcePath}.bytes is missing (run python -I tools/sky/build_stars.py)");
            var raw = asset.GetData<byte>();
            if (raw.Length < 16 || raw[0] != 'M' || raw[1] != 'N' || raw[2] != 'S' || raw[3] != 'K' || raw[4] != 'Y' || raw[7] != '1')
                throw new InvalidOperationException("stars.bytes has an unexpected header");
            int n = (int)((uint)raw[8] | (uint)raw[9] << 8 | (uint)raw[10] << 16 | (uint)raw[11] << 24);
            if (raw.Length != 16 + n * 20) throw new InvalidOperationException($"stars.bytes size {raw.Length} does not match {n} stars");

            var pos = raw.GetSubArray(16, n * 16).Reinterpret<Vector4>(1);
            var col = raw.GetSubArray(16 + n * 16, n * 4).Reinterpret<uint>(1);
            var mags = new float[n];
            for (int i = 0; i < n; i++) mags[i] = pos[i].w;

            var cat = new StarCatalog { Count = n, _mags = mags };
            cat.PosMag = new ComputeBuffer(n, 16) { name = "MN_StarPosMag" };
            cat.Color = new ComputeBuffer(n, 4) { name = "MN_StarColor" };
            cat.PosMag.SetData(pos);
            cat.Color.SetData(col);
            MnLog.Info($"SKY star catalogue: {n} stars, brightest V {mags[0]:0.00}, faintest V {mags[n - 1]:0.00}");
            return cat;
        }
    }
}
