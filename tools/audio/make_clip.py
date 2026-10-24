#!/usr/bin/env python
"""A short clip of the game with its sound: stills from a capture run plus the mix the audio system rendered in step with that run.

  Monolith.exe -mnCapture plan.json -mnOut dir -mnAudioCapture dir\\audio.wav          (the audio system renders one tick of sound per tick of the game)
  python tools/audio/make_clip.py --dir dir --prefix cap --audio dir/audio.wav --fps 30 --out clip.mp4 [--fade 0.4]

The stills are the plan's frames (named <prefix>_000.png, ...). Each was taken at a sim tick that the run's capture_manifest.json records, so the audio is cut
from exactly the first still's tick to just past the last, and joined to the frames at the given frame rate: what you hear is what the game made at that moment.
Needs ffmpeg on PATH. Writes the .mp4 (H.264 + AAC) and a .wav of the same audio.
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import wave


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dir', required=True)
    ap.add_argument('--prefix', required=True)
    ap.add_argument('--audio', required=True)
    ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--out', required=True)
    ap.add_argument('--fade', type=float, default=0.3)
    ap.add_argument('--tick-hz', type=int, default=60)
    a = ap.parse_args()
    ff = shutil.which('ffmpeg')
    if not ff:
        raise SystemExit('ffmpeg not found on PATH')
    man = json.load(open(os.path.join(a.dir, 'capture_manifest.json'), encoding='utf-8'))
    shots = man.get('results') or []
    ticks = {}
    for s in shots:
        name = s.get('name')
        t = s.get('simTick')
        if name and t is not None:
            ticks[name] = t
    files = sorted(glob.glob(os.path.join(a.dir, a.prefix + '_[0-9][0-9][0-9].png')))
    if not files:
        raise SystemExit(f'no {a.prefix}_NNN.png frames in {a.dir}')
    names = [os.path.splitext(os.path.basename(f))[0] for f in files]
    t0 = ticks.get(names[0])
    t1 = ticks.get(names[-1])
    if t0 is None or t1 is None:
        raise SystemExit('the manifest has no simTick for the first or last frame')
    start = t0 / a.tick_hz
    dur = len(files) / a.fps
    wav = os.path.splitext(a.out)[0] + '.wav'
    af = f'atrim=start={start:.4f}:duration={dur:.4f},asetpts=PTS-STARTPTS,afade=t=in:d=0.02,afade=t=out:st={max(0.0, dur - a.fade):.3f}:d={a.fade}'
    r = subprocess.run([ff, '-y', '-v', 'error', '-i', a.audio, '-af', af, '-c:a', 'pcm_s16le', wav])
    if r.returncode:
        sys.exit(r.returncode)
    r = subprocess.run([ff, '-y', '-v', 'error', '-framerate', str(a.fps), '-i', os.path.join(a.dir, a.prefix + '_%03d.png'), '-i', wav,
                        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '17', '-c:a', 'aac', '-b:a', '192k', '-shortest', a.out])
    if r.returncode:
        sys.exit(r.returncode)
    print(f'{a.out}: {len(files)} frames at {a.fps} fps = {dur:.1f} s, audio from game tick {t0} ({start:.2f} s) to {t1}')


if __name__ == '__main__':
    main()
