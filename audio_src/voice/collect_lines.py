#!/usr/bin/env python
"""Collect every radio line in the game into audio_src/voice/voice_lines.json (one entry per voice and text).

  python audio_src/voice/collect_lines.py            writes voice_lines.json and prints a summary
  python audio_src/voice/collect_lines.py --check    exits 1 if voice_lines.json is out of date (CI-style)

Sources (all data, no code): Game/Assets/Monolith/Resources/Missions/*.mission.json (the messages, and any inline 'radio' action
or 'callOut'), Resources/AI/radio.json (wingman acknowledgements, situation lines) and every 'voice' slot. Who says what comes
from cast.json. A line a wingman may say ("<any wingman>", the acknowledgements) is recorded in each wingman voice, because the
mission does not know which wingman is alive; a line that Earth may say through a fallback is recorded in the sender's voice and
the fallback's. The index maps (voice, text) to the clip; at run time a line with no clip for its speaker falls back to the same
text in any voice, then to a logged squelch.
"""
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
RES = os.path.join(ROOT, 'Game', 'Assets', 'Monolith', 'Resources')
OUT = os.path.join(os.path.dirname(__file__), 'voice_lines.json')
CAST = json.load(open(os.path.join(os.path.dirname(__file__), 'cast.json'), encoding='utf-8'))

NUMWORD = {'1': 'one', '2': 'two', '3': 'three', '4': 'four', '5': 'five', '6': 'six', '7': 'seven', '8': 'eight', '9': 'nine'}


def spoken(text):
    """What the TTS is asked to say: digits after a callsign become words ('Sable 2' -> 'Sable two')."""
    t = re.sub(r'\b(Sable|Lantern|Wick|Censer|Vigil|Choir|Psalm)\s+(\d)\b', lambda m: m.group(1) + ' ' + NUMWORD[m.group(2)], text)
    return t


def norm(text):
    return re.sub(r'\s+', ' ', text.strip().upper())


def slug(s):
    return re.sub(r'[^a-z0-9]+', '_', s.lower()).strip('_')


def voice_of(name):
    """voices for a speaker name as it appears in the mission file ('Small Mercy', '#EARTH', '<any wingman>')."""
    if not name:
        return []
    key = name.strip().lower()
    if key == '<any wingman>':
        # a living wingman, else the player: so the player's voice says it too
        return list(CAST['defaults']['wingmanVoices']) + [CAST['defaults']['playerVoice']]
    v = CAST['speakers'].get(key)
    if v:
        return [v]
    m = re.match(r'^(.*?)\s*\d+$', key)
    if m and m.group(1) in CAST['speakers']:
        return [CAST['speakers'][m.group(1)]]
    print(f'  note: no voice for speaker "{name}", using {CAST["defaults"]["unknown"]}', file=sys.stderr)
    return [CAST['defaults']['unknown']]


def strip_comments(txt):
    return re.sub(r'(?m)^\s*//.*$', '', txt)


def main():
    entries = {}   # (voice, norm) -> entry

    def add(voice, text, source, name):
        k = (voice, norm(text))
        if k in entries:
            entries[k]['sources'].append(source)
            return
        entries[k] = {'voice': voice, 'text': text, 'say': spoken(text), 'file': f'{voice}/{name}.wav', 'sources': [source]}

    mdir = os.path.join(RES, 'Missions')
    for fn in sorted(os.listdir(mdir)):
        if not fn.endswith('.mission.json'):
            continue
        d = json.loads(strip_comments(open(os.path.join(mdir, fn), encoding='utf-8').read()))
        mid = d.get('id', fn.split('.')[0])
        for m in d.get('messages', []):
            voices = voice_of(m.get('from'))
            if m.get('fallbackFrom'):
                voices += [v for v in voice_of(m['fallbackFrom']) if v not in voices]
            for v in voices:
                add(v, m['text'], f'{mid}:{m["id"]}', f'{mid}_{slug(m["id"])}')

        def walk(o):
            if isinstance(o, dict):
                if o.get('do') == 'radio' and 'text' in o:
                    for v in voice_of(o.get('from')):
                        add(v, o['text'], f'{mid}:inline', f'{mid}_inline_{slug(o["text"])[:24]}')
                if o.get('do') == 'callOut' and 'text' in o:
                    for v in CAST['defaults']['wingmanVoices']:
                        add(v, o['text'], f'{mid}:callOut', f'{mid}_callout_{slug(o["text"])[:24]}')
                for x in o.values():
                    walk(x)
            elif isinstance(o, list):
                for x in o:
                    walk(x)
        walk(d.get('events', []))

    radio = json.load(open(os.path.join(RES, 'AI', 'radio.json'), encoding='utf-8'))['lines']
    wing = CAST['defaults']['wingmanVoices']
    earth = CAST['defaults']['earthVoices']
    who = {'bombers': ['mercy'], 'capital_hurt': ['mercy'], 'beams_threat': ['mercy'], 'beams_down': ['mercy'],
           'earth_wave': earth, 'earth_bombers': earth, 'lost_capital': wing, 'won': wing}
    for sit, lines in radio.items():
        voices = who.get(sit, wing)
        for i, t in enumerate(lines, 1):
            texts = [t]
            if '{0}' in t:
                texts = [t.replace('{0}', f) for f in CAST['fills']['{0}']]
            for j, tt in enumerate(texts, 1):
                for v in voices:
                    add(v, tt, f'radio.json:{sit}', f'{sit}_{i:02d}' + (f'_{slug(CAST["fills"]["{0}"][j - 1])}' if len(texts) > 1 else ''))

    out = {'format': 'monolith-voice-lines/1', 'lines': sorted(entries.values(), key=lambda e: (e['voice'], e['file']))}
    text = json.dumps(out, indent=1, ensure_ascii=False) + '\n'
    if '--check' in sys.argv:
        cur = open(OUT, encoding='utf-8').read() if os.path.exists(OUT) else ''
        if cur != text:
            print('voice_lines.json is out of date: run python audio_src/voice/collect_lines.py')
            return 1
        print('voice_lines.json is up to date')
        return 0
    open(OUT, 'w', encoding='utf-8', newline='\n').write(text)
    by = {}
    for e in entries.values():
        by[e['voice']] = by.get(e['voice'], 0) + 1
    words = sum(len(e['say'].split()) for e in entries.values())
    print(f'{len(entries)} clips, {words} words: ' + ', '.join(f'{k} {v}' for k, v in sorted(by.items())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
