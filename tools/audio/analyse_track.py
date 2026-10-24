"""Analyse a piece of music for use as a looping fight theme: its shape, its tempo, its beat grid, and the best place to loop it.

  wsl: ~/breeze/.venv/bin/python tools/audio/analyse_track.py track.wav [--min-loop 90] [--xfade 2.0] [--json out.json]

track.wav: mono, any rate (make it with ffmpeg -ac 1 -ar 22050). Prints a loudness map (RMS in dB every 4 s), the tempo and the beat offset, and the loop
candidates: pairs (loopStart, loopEnd) on bars of the beat grid where the two seconds leading into the loop start sound most like the two seconds leading into
the loop end (the material that is cross-faded across the seam), best first. Not a listening test: it picks the places where a cross-fade has the least to hide.
"""
import argparse
import json
import sys

import numpy as np
import soundfile as sf

ap = argparse.ArgumentParser()
ap.add_argument('wav')
ap.add_argument('--min-loop', type=float, default=90.0)
ap.add_argument('--xfade', type=float, default=2.0)
ap.add_argument('--json')
a = ap.parse_args()

x, sr = sf.read(a.wav, dtype='float32')
if x.ndim > 1:
    x = x.mean(axis=1)
dur = len(x) / sr
print(f'{a.wav}: {dur:.1f} s at {sr} Hz')

# loudness map
step = 4.0
print('loudness every 4 s (dB RMS):')
line = ''
for t in np.arange(0, dur, step):
    seg = x[int(t * sr):int((t + step) * sr)]
    db = 20 * np.log10(np.sqrt((seg ** 2).mean()) + 1e-9)
    line += f'{int(t // 60)}:{int(t % 60):02d} {db:6.1f}   '
    if len(line) > 110:
        print('  ' + line)
        line = ''
if line:
    print('  ' + line)

# short-time spectra
n_fft, hop = 2048, 512
win = np.hanning(n_fft)
frames = 1 + (len(x) - n_fft) // hop
S = np.empty((frames, n_fft // 2 + 1), dtype=np.float32)
for i in range(frames):
    S[i] = np.abs(np.fft.rfft(x[i * hop:i * hop + n_fft] * win))
fps = sr / hop

# onset strength: positive spectral flux on a log-compressed spectrum
L = np.log1p(S * 8)
flux = np.maximum(0, np.diff(L, axis=0)).sum(axis=1)
flux = flux - np.convolve(flux, np.ones(int(fps)) / int(fps), mode='same')
flux = np.maximum(flux, 0)

# tempo from the autocorrelation of the onset strength, 70 to 190 BPM
ac = np.correlate(flux, flux, mode='full')[len(flux) - 1:]
ac /= ac[0]
lo, hi = int(fps * 60 / 190), int(fps * 60 / 70)
lags = np.arange(lo, hi)
# favour musical tempi: weight by a broad prior around 120
bpm_of = 60 * fps / lags
prior = np.exp(-0.5 * (np.log2(bpm_of / 120.0) / 0.6) ** 2)
score = ac[lags] * prior
best = lags[np.argmax(score)]
# refine with a parabola
k = int(np.argmax(score))
if 0 < k < len(lags) - 1:
    y0, y1, y2 = score[k - 1], score[k], score[k + 1]
    off = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2 + 1e-12)
else:
    off = 0.0
period = (best + off)
bpm = 60 * fps / period
print(f'tempo: {bpm:.2f} BPM (beat {60 / bpm:.4f} s); runner-ups: ' + ', '.join(f'{b:.1f}' for b in sorted(set(round(60 * fps / l, 1) for l in lags[np.argsort(-score)[:12]]))[:6]))

# the beat grid: the offset (in beats) at which a comb of beats collects the most onset energy; refine the tempo by trying a few nearby
def comb(bpm_try):
    p = fps * 60 / bpm_try
    best_s, best_o = -1, 0
    for o in np.arange(0, p, 0.5):
        idx = np.round(np.arange(o, len(flux) - 1, p)).astype(int)
        s = flux[idx].sum()
        if s > best_s:
            best_s, best_o = s, o
    return best_s, best_o
cands = [(comb(b)[0], b) for b in np.arange(bpm - 0.6, bpm + 0.61, 0.05)]
score_b, bpm = max(cands)
_, o = comb(bpm)
beat = 60 / bpm
first = o / fps
print(f'refined tempo {bpm:.2f} BPM, first beat at {first:.3f} s; bar (4 beats) {4 * beat:.3f} s')

# bar-level features: a 12-bin chroma and a coarse spectral shape, per beat
freqs = np.fft.rfftfreq(n_fft, 1 / sr)
chroma_map = np.zeros(len(freqs), dtype=int) - 1
for i, f in enumerate(freqs):
    if 55 < f < 2000:
        chroma_map[i] = int(round(12 * np.log2(f / 440.0))) % 12
def feat(t0, t1):
    i0, i1 = int(t0 * fps), max(int(t0 * fps) + 1, int(t1 * fps))
    seg = S[i0:i1]
    if len(seg) == 0:
        return None
    sp = seg.mean(axis=0)
    ch = np.zeros(12)
    for c in range(12):
        ch[c] = sp[chroma_map == c].sum()
    ch /= (np.linalg.norm(ch) + 1e-9)
    bands = np.array([sp[(freqs >= lo_) & (freqs < hi_)].sum() for lo_, hi_ in ((55, 200), (200, 600), (600, 1500), (1500, 4000), (4000, 10000))])
    bands = np.log1p(bands)
    bands /= (np.linalg.norm(bands) + 1e-9)
    return np.concatenate([ch * 1.0, bands * 0.8])

bars = []
t = first
while t + 4 * beat < dur:
    bars.append(t)
    t += 4 * beat
bars = np.array(bars)
xf = a.xfade
F = {}
def fb(t):
    k = round(t, 3)
    if k not in F:
        F[k] = feat(t - xf, t)
    return F[k]
cands = []
for i, ls in enumerate(bars):
    if ls < xf + 8:                     # leave the intro and room for the cross-fade's lead-in
        continue
    fl = fb(ls)
    if fl is None:
        continue
    for j in range(i + 1, len(bars)):
        le = bars[j]
        if le - ls < a.min_loop or le > dur - 4:
            continue
        fe = fb(le)
        if fe is None:
            continue
        sim = float(np.dot(fl, fe))
        # the first bars after the loop start should also not be a sudden quiet (the intro) - prefer places where the level before and after the seam agree
        l0 = np.sqrt((x[int((ls - xf) * sr):int(ls * sr)] ** 2).mean()); l1 = np.sqrt((x[int((le - xf) * sr):int(le * sr)] ** 2).mean())
        lvl = 1.0 - min(1.0, abs(20 * np.log10((l0 + 1e-6) / (l1 + 1e-6))) / 6.0)
        cands.append((sim * 0.7 + lvl * 0.3, sim, lvl, ls, le))
cands.sort(reverse=True)
print(f'loop candidates (cross-fade {xf:.1f} s, at least {a.min_loop:.0f} s long), best first:')
out = []
for c in cands[:12]:
    score_, sim, lvl, ls, le = c
    print(f'  start {ls:7.3f} s  end {le:7.3f} s  length {le - ls:6.1f} s ({round((le - ls) / (4 * beat))} bars)  similarity {sim:.3f}  level match {lvl:.2f}  score {score_:.3f}')
    out.append(dict(loopStart=round(float(ls), 3), loopEnd=round(float(le), 3), score=round(float(score_), 4)))
if a.json:
    json.dump(dict(bpm=round(float(bpm), 3), firstBeat=round(float(first), 3), seconds=round(float(dur), 3), candidates=out), open(a.json, 'w'), indent=1)
    print('wrote', a.json)
