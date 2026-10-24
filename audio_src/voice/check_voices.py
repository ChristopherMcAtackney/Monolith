"""A rough check of the raw voices without ears: median pitch, duration, speech rate and loudness per voice (autocorrelation pitch).
  wsl: ~/breeze/.venv/bin/python audio_src/voice/check_voices.py
Not a listening test. It catches a voice that came out the wrong register, a runaway take or a silent one.
"""
import glob
import json
import os
import sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, 'raw')
CAST = json.load(open(os.path.join(HERE, 'cast.json'), encoding='utf-8'))


def f0_track(x, sr, fmin=60, fmax=400):
    hop, win = int(0.01 * sr), int(0.04 * sr)
    out = []
    lo, hi = int(sr / fmax), int(sr / fmin)
    for s in range(0, len(x) - win - hi, hop):
        seg = x[s:s + win + hi]
        a = seg[:win]
        if np.sqrt((a ** 2).mean()) < 0.02:
            continue
        ac = np.correlate(a, seg, mode='full')[len(seg) - 1:len(seg) - 1 + hi + 1]
        ac /= (np.sqrt((a ** 2).sum() * (seg[:hi + win][-win:] ** 2).sum()) + 1e-9)
        k = lo + int(np.argmax(ac[lo:hi]))
        if ac[k] > 0.45:
            out.append(sr / k)
    return np.array(out)


rows = []
for voice in sorted(CAST['voices']):
    files = sorted(glob.glob(os.path.join(RAW, voice, '*.wav')))
    f0s, durs, rates, levels = [], [], [], []
    man = json.load(open(os.path.join(RAW, 'manifest.json')))['files']
    for f in files:
        x, sr = sf.read(f, dtype='float32')
        if x.ndim > 1:
            x = x.mean(axis=1)
        t = f0_track(x, sr)
        if len(t):
            f0s.append(np.median(t))
        durs.append(len(x) / sr)
        key = voice + '/' + os.path.basename(f)
        words = len(man.get(key, {}).get('say', '').split()) or 1
        rates.append(words / max(0.1, man.get(key, {}).get('speech_seconds', len(x) / sr)))
        levels.append(20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-9))
    if files:
        print(f'{voice:14s} {len(files):3d} clips  median F0 {np.median(f0s):5.0f} Hz (range {np.min(f0s):.0f}-{np.max(f0s):.0f})  words/s {np.median(rates):.2f}  avg {np.mean(durs):.1f}s  rms {np.mean(levels):.1f} dB')
