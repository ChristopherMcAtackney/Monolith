"""A rough check of the raw voices without ears: median pitch, duration, speech rate and loudness per voice and per intensity (autocorrelation pitch).
  wsl: ~/breeze/.venv/bin/python audio_src/voice/check_voices.py [--raw DIR]
Not a listening test. It catches a voice that came out the wrong register, a runaway take or a silent one, and it shows whether the tense and
frantic reads really are quicker, higher and louder than the calm ones (the point of the intensity tags).
"""
import json
import os
import sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, 'raw')
if '--raw' in sys.argv:
    RAW = sys.argv[sys.argv.index('--raw') + 1]


def f0_track(x, sr, fmin=60, fmax=450):
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


man = json.load(open(os.path.join(RAW, 'manifest.json')))['files']
rows = {}
for key, m in man.items():
    f = os.path.join(RAW, key)
    if not os.path.exists(f):
        continue
    x, sr = sf.read(f, dtype='float32')
    if x.ndim > 1:
        x = x.mean(axis=1)
    t = f0_track(x, sr)
    inten = m.get('intensity', 'calm')
    words = len(m.get('say', '').split()) or 1
    r = rows.setdefault((m['voice'], inten), {'f0': [], 'dur': [], 'wps': [], 'rms': [], 'p95': [], 'cen': [], 'sd': [], 'n': 0})
    r['n'] += 1
    if len(t):
        r['f0'].append(np.median(t))
        r['p95'].append(np.percentile(t, 95))
    r['dur'].append(len(x) / sr)
    if len(t) > 5:
        r['sd'].append(float(np.std(12 * np.log2(t / np.median(t)))))
    # spectral centroid of the loud frames (shouting is brighter)
    n = 1024
    cs = []
    for i in range(0, len(x) - n, n // 2):
        seg = x[i:i + n] * np.hanning(n)
        if np.sqrt((seg ** 2).mean()) < 0.03:
            continue
        mag = np.abs(np.fft.rfft(seg))
        fr = np.fft.rfftfreq(n, 1 / sr)
        cs.append(float((mag * fr).sum() / (mag.sum() + 1e-9)))
    if cs:
        r['cen'].append(np.median(cs))
    r['wps'].append(words / max(0.1, m.get('speech_seconds', len(x) / sr)))
    r['rms'].append(20 * np.log10(np.sqrt((x ** 2).mean()) + 1e-9))
order = {'calm': 0, 'tense': 1, 'frantic': 2}
print(f"{'voice':14s} {'intensity':8s} {'clips':>5s} {'F0 med':>7s} {'F0 p95':>7s} {'words/s':>8s} {'avg s':>6s} {'rms dB':>7s} {'centroid':>8s} {'F0 sd st':>8s}")
for (voice, inten) in sorted(rows, key=lambda k: (k[0], order[k[1]])):
    r = rows[(voice, inten)]
    f0 = np.median(r['f0']) if r['f0'] else float('nan')
    p95 = np.median(r['p95']) if r['p95'] else float('nan')
    print(f"{voice:14s} {inten:8s} {r['n']:5d} {f0:7.0f} {p95:7.0f} {np.median(r['wps']):8.2f} {np.mean(r['dur']):6.1f} {np.mean(r['rms']):7.1f} {np.median(r['cen']) if r['cen'] else float('nan'):8.0f} {np.median(r['sd']) if r['sd'] else float('nan'):8.1f}")
