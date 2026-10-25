#!/usr/bin/env python
"""Collect every radio line in the game into audio_src/voice/voice_lines.json (one entry per voice, text and intensity).

  python audio_src/voice/collect_lines.py            writes voice_lines.json and prints a summary
  python audio_src/voice/collect_lines.py --check    exits 1 if voice_lines.json is out of date (CI-style)

Sources (all data, no code): Game/Assets/Monolith/Resources/Missions/*.mission.json (the messages, and any inline 'radio' action
or 'callOut'), Resources/AI/radio.json (wingman acknowledgements, situation lines) and every 'voice' slot. Who says what comes
from cast.json. A line a wingman may say ("<any wingman>", the acknowledgements) is recorded in each wingman voice (and the player's, since the
player says it when no wingman is left), because the mission does not know which wingman is alive; a line that Earth may say through a
fallback is recorded in the sender's voice and the fallback's. The index maps (voice, text, intensity) to the clip; at run time a line with no
clip for its speaker falls back to the same text in any voice, then to a logged squelch.

INTENSITY (the user's direction, 2026-10-24: under fire the crew must sound frantic, not calm). Every line is tagged calm, tense or frantic from
its mission context: the event that fires it and the music state at that moment (the mission timelines of the auto-play, events.csv: ambient is
calm, battle 1 tense, battle 2 and 3 frantic), then adjusted by what it says (bad news cracks the voice whatever the music is doing; the quiet
opening, the going-dark whisper and the relief at the end stay low). Earth never panics: its lines are always calm and cold.
The wingmen's acknowledgements and situation calls are made in two intensities (tense and frantic) and chosen at run time by the music state,
so in a big fight they are mostly the frantic ones. Everything is in INTENSITY below with the reason beside it.

The user's own sheet (design/dialogue, read back by tools/dialogue/import_sheet.py) adds two small files here:
  spoken.json     Line ID -> what the voice says, where it differs from the on-screen text (the on-screen text is the mission's; the clip is found by it)
  intensity.json  Line ID -> calm, tense or frantic, over the table below (a Notes entry in the sheet)
A mission's lines that the user has not approved yet are marked in the mission file ("linesPending": true, or "pending": true on a line): they are left out of voice_lines.json, so no voice is made for them.
A Line ID is mission:message_id for a message, radio:category:n for radio.json (the sheet's first column).
"""
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
RES = os.path.join(ROOT, 'Game', 'Assets', 'Monolith', 'Resources')
sys.path.insert(0, os.path.join(ROOT, 'tools', 'dialogue'))
from line_ids import inline_lines  # noqa: E402  (a call-out's Line ID, made from its event's name: the same ids as the dialogue sheet)
OUT = os.path.join(os.path.dirname(__file__), 'voice_lines.json')
HERE = os.path.dirname(os.path.abspath(__file__))
CAST = json.load(open(os.path.join(HERE, 'cast.json'), encoding='utf-8'))


def _side(name):
    p = os.path.join(HERE, name)
    if not os.path.exists(p):
        return {}
    d = json.load(open(p, encoding='utf-8'))
    d.pop('_about', None)
    return d


SPOKEN = _side('spoken.json')                      # Line ID -> what is said, where it is not the on-screen text
INTENSITY_OVERRIDE = _side('intensity.json')       # Line ID -> calm, tense or frantic (the user's Notes)

NUMWORD = {'1': 'one', '2': 'two', '3': 'three', '4': 'four', '5': 'five', '6': 'six', '7': 'seven', '8': 'eight', '9': 'nine'}

# message id (mission:message) -> intensity, with the music state when it fires in the auto-play timeline and why
INTENSITY = {
    # the dogfight: battle 2 from the first second
    'dogfight:opening': 'tense',          # t=6, battle2: the Mercy's officer gives the order, urgent but in control
    'dogfight:beams_threat': 'frantic',   # t=0, battle2: the beams are charging at the Mercy
    'dogfight:bombers': 'frantic',        # bombers inbound on the Mercy
    'dogfight:capital_hurt': 'frantic',   # "Hull's going"
    'dogfight:beams_down': 'tense',       # relief, still under fire
    'dogfight:sable2_lost': 'frantic',    # bad news in the fight: the voice cracks
    'dogfight:sable3_lost': 'frantic',
    'dogfight:lost_capital': 'frantic',   # the Mercy is gone
    'dogfight:won': 'calm',               # t=end, the fight is over: exhausted, quiet
    # the Jupiter Skirmish (test): the Dogfight's fight and lines, word for word, so the same tags
    'jupiter:opening': 'tense',          # t=6, battle2: the Mercy's officer gives the order, urgent but in control
    'jupiter:beams_threat': 'frantic',   # t=0, battle2: the beams are charging at the Mercy
    'jupiter:bombers': 'frantic',        # bombers inbound on the Mercy
    'jupiter:capital_hurt': 'frantic',   # "Hull's going"
    'jupiter:beams_down': 'tense',       # relief, still under fire
    'jupiter:sable2_lost': 'frantic',    # bad news in the fight: the voice cracks
    'jupiter:sable3_lost': 'frantic',
    'jupiter:lost_capital': 'frantic',   # the Mercy is gone
    'jupiter:won': 'calm',               # t=end, the fight is over: exhausted, quiet
    # the crossing: quiet until 58 s, then battle 1 and 2, battle 3 at 113 s
    'escort:open_tamsin': 'calm',         # t=2, ambient: the briefing over the radio
    'escort:open_sable2': 'calm',         # t=11, ambient: the quiet opening
    'escort:lector_sweep': 'tense',       # t=118, battle3: the going-dark moment: low and urgent, not shouting
    'escort:thurible_ours': 'frantic',    # t=311, battle3: bombers on the scope
    'escort:tamsin_hurt': 'frantic',      # t=188, battle2: venting from three places
    'escort:lantern': 'tense',            # t=184, battle2: the relief arriving into a fight
    'escort:wick': 'tense',
    'escort:sable2_lost': 'tense',        # t=279: sober news, said fast
    'escort:lector_down': 'tense',
    'escort:hold': 'frantic',             # the Tamsin at the jump point with its drive cold, battle 3, Earth's last wing coming in
    'escort:terce2': 'calm',              # Earth's last throw: Earth never panics
    'escort:spool': 'frantic',            # twenty seconds to hold, battle 3
    'escort:away': 'calm',                # they are through
    'escort:lost': 'frantic',             # Tamsin is gone
    # Dead Water (the cabin mission): ambient and silent running, then the Lector's sweep, then drones
    'cabin:quiet': 'calm',                # t=6, ambient: the quiet opening
    'cabin:go_dark': 'tense',             # t=24, ambient: an urgent order to go dark, said low and fast, not shouted
    'cabin:missed': 'calm',               # the sweep passed over them: a held breath let out
    'cabin:spotted_reply': 'frantic',     # battle 1: found, drones inbound
    'cabin:hit': 'frantic',               # the port drive is gone and the ship is venting
    'cabin:power_up': 'frantic',          # the fight is on and the pilot must be in the cabin
    'cabin:choir_in': 'frantic',          # three drones on the port side
    'cabin:engine_back': 'tense',         # relief, still in a fight
    'cabin:clear': 'calm',                # the last drone is down: the fight is over
    # (Earth's Lector lines, cabin:lector_here, lector_sweep, lector_blind and spotted, are always calm.)
}
# radio.json situation -> the intensities it is made in (a list: made once for each; chosen by the music state at run time)
SITUATION = {
    'ack_attack': ['tense', 'frantic'], 'ack_disable': ['tense', 'frantic'], 'ack_cover': ['tense', 'frantic'], 'ack_form': ['tense', 'frantic'],
    'no_target': ['tense', 'frantic'], 'disabled': ['tense'], 'peel': ['frantic'], 'rejoin': ['tense'], 'wingman_lost': ['frantic'],
    'bombers': ['frantic'], 'capital_hurt': ['frantic'], 'beams_threat': ['frantic'], 'beams_down': ['tense'],
    'earth_wave': ['calm'], 'earth_bombers': ['calm'], 'won': ['calm'], 'lost_capital': ['frantic'],
}
DEFAULT_MESSAGE = 'calm'      # Earth's lines and anything untagged: composed
UNTAGGED = []                 # mission:message ids that had no entry in INTENSITY (printed at the end: add them, with the reason)
PENDING = []                  # lines that are drafts pending the user's approval ("pending": true, or the mission's "linesPending"): never voiced


def spoken(text, intensity):
    """What the TTS is asked to say: digits after a callsign become words ('Sable 2' -> 'Sable two'); a tense or frantic line gets the punctuation of someone
    under fire (the words are unchanged, so the line still matches its text)."""
    t = re.sub(r'\b(Sable|Lantern|Wick|Censer|Vigil|Choir|Psalm)\s+(\d)\b', lambda m: m.group(1) + ' ' + NUMWORD[m.group(2)], text)
    if intensity == 'frantic':
        t = re.sub(r'\.(\s|$)', r'!\1', t)
        t = re.sub(r', ', ' - ', t)
    elif intensity == 'tense':
        t = re.sub(r'\.\s*$', '!', t.strip())
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
    entries = {}   # (voice, norm, intensity) -> entry

    def add(voice, text, source, name, intensities, line_id=None):
        for inten in intensities:
            if line_id in INTENSITY_OVERRIDE and INTENSITY_OVERRIDE[line_id] in ('calm', 'tense', 'frantic'):
                inten = INTENSITY_OVERRIDE[line_id]
            if CAST['voices'][voice].get('intensity') == 'calm':
                inten = 'calm'              # Earth never panics
            k = (voice, norm(text), inten)
            if k in entries:
                entries[k]['sources'].append(source)
                if line_id and line_id not in entries[k]['lineIds']:
                    entries[k]['lineIds'].append(line_id)
                continue
            suffix = '' if inten == 'calm' else '_' + inten
            said = SPOKEN.get(line_id, text) if line_id else text
            entries[k] = {'voice': voice, 'text': text, 'intensity': inten, 'say': spoken(said, inten), 'file': f'{voice}/{name}{suffix}.wav', 'sources': [source], 'lineIds': [line_id] if line_id else []}

    mdir = os.path.join(RES, 'Missions')
    for fn in sorted(os.listdir(mdir)):
        if not fn.endswith('.mission.json'):
            continue
        d = json.loads(strip_comments(open(os.path.join(mdir, fn), encoding='utf-8').read()))
        mid = d.get('id', fn.split('.')[0])
        file_pending = d.get('linesPending', False)
        for m in d.get('messages', []):
            if m.get('pending', file_pending):
                PENDING.append(f'{mid}:{m["id"]}')
                continue
            voices = voice_of(m.get('from'))
            if m.get('fallbackFrom'):
                voices += [v for v in voice_of(m['fallbackFrom']) if v not in voices]
            inten = m.get('intensity') or INTENSITY.get(f'{mid}:{m["id"]}')       # the line's own tag first
            if inten is None:
                # a mission written after this table: guess (a high-priority message is tense, anything else calm) and say so; Earth's voices ignore it anyway
                inten = 'tense' if m.get('priority') == 'high' else DEFAULT_MESSAGE
                if any(CAST['voices'][v].get('intensity') != 'calm' for v in voices):       # Earth's voices are always calm: no need to tag them
                    UNTAGGED.append(f'{mid}:{m["id"]} -> {inten}')
            for v in voices:
                add(v, m['text'], f'{mid}:{m["id"]}', f'{mid}_{slug(m["id"])}', [inten], line_id=f'{mid}:{m["id"]}')

        inline_ids = {id(act): lid for lid, _, act in inline_lines(d)}

        def walk(o):
            if isinstance(o, dict):
                if o.get('do') in ('radio', 'callOut') and 'text' in o and o.get('pending', file_pending):
                    PENDING.append(f'{mid}:{o["do"]}:{o["text"][:40]}')
                elif o.get('do') == 'radio' and 'text' in o:
                    for v in voice_of(o.get('from')):
                        add(v, o['text'], f'{mid}:inline', f'{mid}_inline_{slug(o["text"])[:24]}', [o.get('intensity') or DEFAULT_MESSAGE], line_id=inline_ids.get(id(o)))
                elif o.get('do') == 'callOut' and 'text' in o:
                    for v in (voice_of(o['from']) if o.get('from') else CAST['defaults']['wingmanVoices']):
                        add(v, o['text'], f'{mid}:callOut', f'{mid}_callout_{slug(o["text"])[:24]}', [o.get('intensity') or 'tense'], line_id=inline_ids.get(id(o)))
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
           'earth_wave': earth, 'earth_bombers': earth, 'lost_capital': wing + [CAST['defaults']['playerVoice']], 'won': wing + [CAST['defaults']['playerVoice']]}
    for sit, lines in radio.items():
        voices = who.get(sit, wing)
        intens = SITUATION.get(sit, ['tense'])
        for i, t in enumerate(lines, 1):
            texts = [t]
            if '{0}' in t:
                texts = [t.replace('{0}', f) for f in CAST['fills']['{0}']]
            for j, tt in enumerate(texts, 1):
                for v in voices:
                    add(v, tt, f'radio.json:{sit}', f'{sit}_{i:02d}' + (f'_{slug(CAST["fills"]["{0}"][j - 1])}' if len(texts) > 1 else ''), intens, line_id=f'radio:{sit}:{i}')

    out = {'format': 'monolith-voice-lines/3', 'lines': sorted(entries.values(), key=lambda e: (e['voice'], e['file']))}
    text = json.dumps(out, indent=1, ensure_ascii=False) + '\n'
    if PENDING:
        print("  " + str(len(PENDING)) + " line(s) pending the user's approval: not voiced (once approved, set \"pending\": false on the line or drop the mission's \"linesPending\")")
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
    byi = {}
    for e in entries.values():
        byi[e['intensity']] = byi.get(e['intensity'], 0) + 1
    words = sum(len(e['say'].split()) for e in entries.values())
    if UNTAGGED:
        print('  note: no intensity tag for ' + str(len(UNTAGGED)) + ' message(s), guessed: ' + '; '.join(UNTAGGED) + '  (add them to INTENSITY in collect_lines.py with the reason)')
    print(f'{len(entries)} clips, {words} words: ' + ', '.join(f'{k} {v}' for k, v in sorted(by.items())) + ' | ' + ', '.join(f'{k} {v}' for k, v in sorted(byi.items())))
    return 0


if __name__ == '__main__':
    sys.exit(main())
