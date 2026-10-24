#!/usr/bin/env python
"""A short audio reel of radio lines to listen to in one go: the calm read from before the intensity rework (git HEAD at the commit given) against the tense and frantic reads now.

  python tools/audio/make_voice_reel.py [--before 56fb0fd] [--out gauntlet/captures/audio/r1/clips/frantic_reel.mp3]

Each group is: the old calm clip, a pause, the new tense clip (if the line has one), a pause, the new frantic clip, a longer pause. The groups are the ones named in GROUPS below
(the old file, then the new ones, under Game/Assets/StreamingAssets/Audio/). Needs ffmpeg and git on PATH. Not a test: a way to hear the difference.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
VO = 'Game/Assets/StreamingAssets/Audio/vo/'
GROUPS = [
    # (old calm, new tense or None, new frantic)
    ('mercy/dogfight_bombers', None, 'mercy/dogfight_bombers_frantic'),
    ('sable2/dogfight_sable2_lost', None, 'sable2/dogfight_sable2_lost_frantic'),
    ('sable2/ack_attack_03', 'sable2/ack_attack_03_tense', 'sable2/ack_attack_03_frantic'),
    ('mercy/dogfight_capital_hurt', None, 'mercy/dogfight_capital_hurt_frantic'),
    ('sable3/ack_cover_02', 'sable3/ack_cover_02_tense', 'sable3/ack_cover_02_frantic'),
    ('tamsin/escort_tamsin_hurt', None, 'tamsin/escort_tamsin_hurt_frantic'),
]


def mission_reel(mission, out):
    """Every line of one mission in the order the mission file lists its messages: a quick listen to a whole mission's voices (as the game plays them, radio and all)."""
    import re
    ff = shutil.which('ffmpeg')
    txt = re.sub(r'(?m)^\s*//.*$', '', open(os.path.join(ROOT, 'Game', 'Assets', 'Monolith', 'Resources', 'Missions', mission + '.mission.json'), encoding='utf-8').read())
    order = [m['id'] for m in json.loads(txt).get('messages', [])]
    lines = json.load(open(os.path.join(ROOT, 'audio_src', 'voice', 'voice_lines.json'), encoding='utf-8'))['lines']
    tmp = tempfile.mkdtemp(prefix='mn_reel_')
    try:
        gap = os.path.join(tmp, 'gap.wav')
        subprocess.check_call([ff, '-y', '-v', 'error', '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=mono', '-t', '0.6', '-c:a', 'pcm_s16le', gap])
        parts = []
        for mid in order:
            for l in lines:
                if f'{mission}:{mid}' in l['sources'] and l['voice'] != 'sable3' and l['voice'] != 'sable1':
                    w = os.path.join(tmp, f'{len(parts)}.wav')
                    subprocess.check_call([ff, '-y', '-v', 'error', '-i', os.path.join(ROOT, VO + l['file'].replace('.wav', '.ogg')), '-ar', '48000', '-ac', '1', '-c:a', 'pcm_s16le', w])
                    parts += [w, gap]
                    print(f"{mid:16s} {l['voice']:12s} {l['intensity']:8s} {l['text']}")
                    break
        lst = os.path.join(tmp, 'list.txt')
        open(lst, 'w').write(''.join("file '" + p.replace(chr(92), '/') + "'" + chr(10) for p in parts))
        os.makedirs(os.path.dirname(out), exist_ok=True)
        subprocess.check_call([ff, '-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c:a', 'libmp3lame', '-q:a', '3', out])
        print('wrote', out)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mission', help='make a reel of every line of this mission instead (in message order)')
    ap.add_argument('--before', default='56fb0fd')
    ap.add_argument('--out', default=os.path.join(ROOT, 'gauntlet', 'captures', 'audio', 'r1', 'clips', 'frantic_reel.mp3'))
    a = ap.parse_args()
    if a.mission:
        return mission_reel(a.mission, a.out if a.out != ap.get_default('out') else os.path.join(ROOT, 'gauntlet', 'captures', 'audio', 'r2', 'clips', a.mission + '_voices.mp3'))
    ff = shutil.which('ffmpeg')
    if not ff or not shutil.which('git'):
        sys.exit('needs ffmpeg and git on PATH')
    tmp = tempfile.mkdtemp(prefix='mn_reel_')
    try:
        def silence(name, secs):
            p = os.path.join(tmp, name)
            subprocess.check_call([ff, '-y', '-v', 'error', '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=mono', '-t', str(secs), '-c:a', 'pcm_s16le', p])
            return p

        def wav(src_ogg, name):
            p = os.path.join(tmp, name + '.wav')
            subprocess.check_call([ff, '-y', '-v', 'error', '-i', src_ogg, '-ar', '48000', '-ac', '1', '-c:a', 'pcm_s16le', p])
            return p

        short, long_ = silence('s.wav', 0.7), silence('l.wav', 1.6)
        parts = []
        for i, (old, tense, frantic) in enumerate(GROUPS):
            blob = subprocess.run(['git', '-C', ROOT, 'show', f'{a.before}:{VO}{old}.ogg'], capture_output=True)
            if blob.returncode != 0:
                print('skip (no old clip):', old)
                continue
            oldp = os.path.join(tmp, f'old{i}.ogg')
            open(oldp, 'wb').write(blob.stdout)
            parts += [wav(oldp, f'old{i}'), short]
            if tense:
                parts += [wav(os.path.join(ROOT, VO + tense + '.ogg'), f'tense{i}'), short]
            parts += [wav(os.path.join(ROOT, VO + frantic + '.ogg'), f'frantic{i}'), long_]
        lst = os.path.join(tmp, 'list.txt')
        open(lst, 'w').write(''.join("file '" + p.replace('\\', '/') + "'\n" for p in parts))
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        subprocess.check_call([ff, '-y', '-v', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c:a', 'libmp3lame', '-q:a', '3', a.out])
        print('wrote', a.out)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == '__main__':
    main()
