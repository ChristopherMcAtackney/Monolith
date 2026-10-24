#!/usr/bin/env python
"""Set the track levels of audio_src/music.json from measured stem loudness, so the mix is made by numbers.

  dotnet tools/audio/bin/Release/net9.0/MnAudio.dll stems <cues...> > audio_src/render/stems.log     (each track alone, with its LUFS)
  python audio_src/compose/mixfit.py audio_src/render/stems.log [--apply]

Each track has a role (lead, drums, bass, pad...) that says how far under the lead it should sit, in LU of K-weighted stem loudness; the
bass and the drone sit lower than their loudness would say because the K-weighting forgives low frequencies and a mix that follows it
alone is all sub. The script prints the new "db" for each track, and with --apply rewrites those numbers in music.json in place (comments
and layout are kept). Loudness of the whole cue is then set by its "lufs".
"""
import re
import sys

# role offsets (LU re the cue's lead) per track name
ROLE = {
    'brass_cs80': 0.0, 'brass_cs80:low': -5.0, 'brass_dark': -7.0, 'brass_stab': -1.0, 'lead_prophet': -3.0,
    'choir_ahh': -3.0, 'pad_juno': -3.0, 'pad_warm': -5.0, 'strings_ens': -6.0,
    'bass_pulse': -1.5, 'bass_moog': -5.0, 'bass_sub': -4.0, 'drone_dark': -12.0,
    'arp_seq': -1.5, 'arp_seq:hi': -5.0, 'arp_soft': -5.0, 'bell_fm': -7.0, 'sweep': -7.0, 'pulse_thin': -4.0,
    'drums': -1.0,
}
# per-cue lead level (LUFS of the lead stem) and overrides
CUE = {
    'ambient': dict(lead=-35.0, over={'drums': -14.0, 'choir_ahh': -2.0, 'drone_dark': -10.0, 'bass_sub': -8.0, 'brass_cs80': 1.0}),
    'battle1': dict(lead=-34.0, over={'drums': -4.0, 'choir_ahh': -4.0, 'drone_dark': -12.0}),
    'battle2': dict(lead=-33.0, over={'drums': -2.0}),
    'battle3': dict(lead=-32.0, over={'drums': -1.5}),
    'friendly_arrival': dict(lead=-33.0, over={'drums': -6.0, 'choir_ahh': -1.0, 'strings_ens': -4.0}),
    'friendly_arrival_battle': dict(lead=-33.0, over={'drums': -3.0, 'choir_ahh': -1.0, 'strings_ens': -4.0}),
    'enemy_arrival': dict(lead=-31.0, over={'brass_dark': 0.0, 'drums': -3.0, 'bass_sub': -3.0, 'bell_fm': -6.0}),
    'enemy_arrival_battle': dict(lead=-31.0, over={'brass_dark': 0.0, 'drums': -2.0, 'bass_sub': -3.0, 'bell_fm': -6.0}),
    'goal_complete': dict(lead=-33.0, over={'drums': -6.0, 'choir_ahh': -2.0, 'bell_fm': -4.0}),
    'goal_failed': dict(lead=-34.0, over={'bell_fm': 0.0, 'brass_dark': -2.0, 'drums': -6.0}),
    'victory': dict(lead=-33.0, over={'drums': -3.0, 'choir_ahh': -2.0, 'bell_fm': -5.0, 'arp_seq': -5.0}),
    'failure': dict(lead=-36.0, over={'brass_cs80': 0.0, 'brass_dark': -2.0, 'choir_ahh': -3.0, 'drums': -6.0, 'bell_fm': -5.0, 'pad_juno': -2.0}),
    'death': dict(lead=-33.0, over={'brass_dark': 0.0, 'drums': -3.0, 'pad_juno': -3.0, 'bell_fm': -4.0, 'bass_sub': -4.0, 'choir_ahh': -6.0}),
}


def parse(path):
    cue = None
    res = {}
    for line in open(path, encoding='utf-8'):
        if line.startswith('== '):
            cue = line[3:].strip()
            res[cue] = {}
            continue
        m = re.match(r'^(\S+)\s+(-?\d+\.\d+)\s+(-?\d+\.\d+)\s+(-?\d+\.\d+)', line)
        if cue and m:
            res[cue][m.group(1)] = (float(m.group(2)), float(m.group(3)))
    return res


def main():
    stems = parse(sys.argv[1])
    new = {}
    for cue, tracks in stems.items():
        spec = CUE.get(cue)
        if not spec:
            continue
        new[cue] = {}
        for tr, (db, lufs) in tracks.items():
            off = spec['over'].get(tr, ROLE.get(tr, -4.0))
            target = spec['lead'] + off
            new[cue][tr] = round(db + (target - lufs), 1)
        print(f'== {cue}: ' + ', '.join(f'{t} {v:+.1f}' for t, v in new[cue].items()))
    if '--apply' in sys.argv:
        path = 'audio_src/music.json'
        text = open(path, encoding='utf-8').read()
        for cue, tr in new.items():
            a = text.index(f'"{cue}": {{')
            b = text.find('\n    "', a + 10)
            b = len(text) if b < 0 else b
            block = text[a:b]
            for t, db in tr.items():
                block = re.sub(r'("%s": \{ "db": )(-?\d+(?:\.\d+)?)' % re.escape(t), lambda m: m.group(1) + f'{db}', block)
            text = text[:a] + block + text[b:]
        open(path, 'w', encoding='utf-8').write(text)
        print('music.json updated')


main()
