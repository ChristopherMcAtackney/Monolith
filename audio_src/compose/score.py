"""The harmony, the themes and the pattern helpers shared by every cue of the score.

All the looping cues (the ambient bed and the three battle intensities) share one form: 32 bars of 4/4 at 112 BPM (68.6 s), four sections of eight
bars. They also share one harmonic skeleton, so the game can cross-fade from any of them to any other on a beat or a bar and the chords still
line up. They differ in what plays over it: more instruments, faster notes, bigger drums.

  A  bars  0-7   D minor: the lament (i, VI, iv, V)
  B  bars  8-15  the relative major lifts (III, VII, VI, iv, V)
  C  bars 16-23  the dark turn: D minor against E flat (the Phrygian second, the Blade Runner chord)
  D  bars 24-31  the climb back: VI, III, iv, i ... ending on V so the loop falls into D minor again

Two themes. The CALL (bar 0: A, D, F) is the diaspora's; the LAMENT (a falling line, A G F E D) is what the dead hear. Earth has no theme: it has a
semitone (D against E flat) and a low horn.
"""
from mid import midi

BPM = 112
BEATS_PER_BAR = 4
BARS = 32
LOOP_BEATS = BARS * BEATS_PER_BAR

#             bar: 0   1   2    3    4    5    6   7 | 8  9  10 11  12   13   14   15 | 16   17   18   19   20   21  22  23 | 24   25  26   27   28   29  30  31
FORM = ['Dm', 'Dm', 'Bb', 'Bb', 'Gm', 'Gm', 'A', 'A', 'F', 'F', 'C', 'C', 'Bb', 'Bb', 'Gm', 'A',
        'Dm', 'Eb', 'Dm', 'Eb', 'Bb', 'C', 'A', 'A', 'Bb', 'F', 'Gm', 'Dm', 'Bb', 'F', 'A', 'A']

SECTION = lambda bar: bar // 8      # 0 A, 1 B, 2 C, 3 D

CH = {
    'Dm': dict(root='D2', sub='D1', pad=['D3', 'A3', 'D4', 'F4'], hi=['A4', 'D5', 'F5'], tones=['D3', 'F3', 'A3', 'C4', 'D4', 'E4', 'F4', 'A4'], stab=['D4', 'F4', 'A4', 'D5']),
    'Bb': dict(root='Bb1', sub='Bb1', pad=['Bb2', 'F3', 'Bb3', 'D4'], hi=['F4', 'Bb4', 'D5'], tones=['Bb3', 'D4', 'F4', 'A4', 'Bb4', 'C5', 'D5', 'F5'], stab=['Bb3', 'D4', 'F4', 'Bb4']),
    'Gm': dict(root='G1', sub='G1', pad=['G2', 'D3', 'G3', 'Bb3'], hi=['D4', 'G4', 'Bb4'], tones=['G3', 'Bb3', 'D4', 'F4', 'G4', 'A4', 'Bb4', 'D5'], stab=['G3', 'Bb3', 'D4', 'G4']),
    'A':  dict(root='A1', sub='A1', pad=['A2', 'E3', 'A3', 'C#4'], hi=['E4', 'A4', 'C#5'], tones=['A3', 'C#4', 'E4', 'G4', 'A4', 'C#5', 'E5', 'G5'], stab=['A3', 'C#4', 'E4', 'A4']),
    'F':  dict(root='F2', sub='F1', pad=['F2', 'C3', 'F3', 'A3'], hi=['C4', 'F4', 'A4'], tones=['F3', 'A3', 'C4', 'E4', 'F4', 'G4', 'A4', 'C5'], stab=['F3', 'A3', 'C4', 'F4']),
    'C':  dict(root='C2', sub='C2', pad=['C3', 'G3', 'C4', 'E4'], hi=['G4', 'C5', 'E5'], tones=['C4', 'E4', 'G4', 'B4', 'C5', 'D5', 'E5', 'G5'], stab=['C4', 'E4', 'G4', 'C5']),
    'Eb': dict(root='Eb2', sub='Eb1', pad=['Eb3', 'Bb3', 'D4', 'G4'], hi=['Bb4', 'D5', 'G5'], tones=['Eb3', 'G3', 'Bb3', 'D4', 'Eb4', 'F4', 'G4', 'Bb4'], stab=['Eb4', 'G4', 'Bb4', 'D5']),
}

# the call, section by section: (bar, beat in bar, beats, pitch). Written over the chords above.
THEME = {
    0: [(0, 0, 2, 'A4'), (0, 2, 1, 'D5'), (0, 3, 1, 'F5'), (1, 0, 3, 'E5'), (1, 3, 1, 'D5'),
        (2, 0, 2, 'D5'), (2, 2, 1, 'C5'), (2, 3, 1, 'Bb4'), (3, 0, 4, 'A4'),
        (4, 0, 2, 'Bb4'), (4, 2, 1, 'D5'), (4, 3, 1, 'G5'), (5, 0, 3, 'F5'), (5, 3, 1, 'D5'),
        (6, 0, 2, 'E5'), (6, 2, 1, 'C#5'), (6, 3, 1, 'E5'), (7, 0, 3, 'A4')],
    1: [(8, 0, 1, 'A4'), (8, 1, 1, 'C5'), (8, 2, 2, 'F5'), (9, 0, 2, 'E5'), (9, 2, 1, 'D5'), (9, 3, 1, 'C5'),
        (10, 0, 1, 'C5'), (10, 1, 1, 'E5'), (10, 2, 2, 'G5'), (11, 0, 2, 'F5'), (11, 2, 2, 'E5'),
        (12, 0, 1, 'D5'), (12, 1, 1, 'F5'), (12, 2, 2, 'Bb5'), (13, 0, 2, 'A5'), (13, 2, 2, 'F5'),
        (14, 0, 2, 'G5'), (14, 2, 1, 'D5'), (14, 3, 1, 'Bb4'), (15, 0, 2, 'C#5'), (15, 2, 1, 'E5'), (15, 3, 1, 'A5')],
    2: [(16, 0, 4, 'D5'), (17, 0, 3, 'Eb5'), (17, 3, 1, 'D5'), (18, 0, 2, 'F5'), (18, 2, 2, 'E5'), (19, 0, 2, 'Eb5'), (19, 2, 2, 'D5'),
        (20, 0, 2, 'D5'), (20, 2, 2, 'F5'), (21, 0, 2, 'E5'), (21, 2, 2, 'G5'), (22, 0, 2, 'C#5'), (22, 2, 2, 'E5'), (23, 0, 4, 'A4')],
    3: [(24, 0, 2, 'F5'), (24, 2, 2, 'Bb5'), (25, 0, 2, 'A5'), (25, 2, 2, 'C6'), (26, 0, 2, 'D6'), (26, 2, 1, 'Bb5'), (26, 3, 1, 'G5'),
        (27, 0, 3, 'A5'), (27, 3, 1, 'F5'), (28, 0, 2, 'Bb5'), (28, 2, 2, 'D6'), (29, 0, 2, 'C6'), (29, 2, 2, 'A5'),
        (30, 0, 2, 'E6'), (30, 2, 1, 'C#6'), (30, 3, 1, 'A5'), (31, 0, 4, 'A5')],
}


def beat(bar, b=0.0):
    return bar * BEATS_PER_BAR + b


def melody(section, shift=0):
    return [(beat(bar, b), d, midi(p) + shift) for (bar, b, d, p) in THEME[section]]


def bars_of(*sections):
    out = []
    for s in sections:
        out += list(range(s * 8, s * 8 + 8))
    return out


# --- patterns ------------------------------------------------------------------------------------------------------------------------

# arpeggio shapes: 16 sixteenths a bar, each an index into the chord's eight tones (None = rest)
ARP_UP = [0, 2, 4, 2, 5, 4, 2, 4, 0, 2, 4, 6, 7, 6, 4, 2]
ARP_TD = [0, 4, 2, 4, 0, 5, 2, 5, 0, 4, 2, 6, 0, 7, 4, 2]
ARP_RUN = [0, 1, 2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 3, 4, 2]
ARP_8TH = [0, None, 2, None, 4, None, 2, None, 5, None, 4, None, 2, None, 4, None]
ARP_SLOW = [0, None, None, None, 4, None, None, None, 2, None, None, None, 5, None, None, None]

# bass shapes: 16 sixteenths, an interval above the root (None = rest)
BASS_8TH = [0, None, 0, None, 0, None, 12, None, 0, None, 0, None, 12, None, 7, None]
BASS_PULSE = [0, None, 0, None, 0, None, 0, None, 0, None, 0, None, 0, None, 0, None]
BASS_GALLOP = [0, 0, 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 0, 7, 12, 7]
BASS_SYNC = [0, None, None, 0, None, None, 0, None, 12, None, None, 0, None, 0, None, 7]


def arp(track, bars, pattern, vel=70, accent=14, octave=0, gate=0.85, step=0.25, bar_patterns=None, human=0.0, tones_key='tones'):
    for i, bar in enumerate(bars):
        tones = [midi(t) for t in CH[FORM[bar]][tones_key]]
        pat = bar_patterns[i % len(bar_patterns)] if bar_patterns else pattern
        for s, idx in enumerate(pat):
            if idx is None:
                continue
            p = tones[idx % len(tones)] + 12 * (idx // len(tones)) + 12 * octave
            v = vel + (accent if s % 4 == 0 else (accent // 3 if s % 2 == 0 else 0))
            track.note(beat(bar, s * step), step * gate, p, v, human)


def bassline(track, bars, pattern, vel=95, accent=12, octave=0, gate=0.8, step=0.25, key='root'):
    for bar in bars:
        root = midi(CH[FORM[bar]][key]) + 12 * octave
        for s, iv in enumerate(pattern):
            if iv is None:
                continue
            track.note(beat(bar, s * step), step * gate, root + iv, vel + (accent if s % 4 == 0 else 0))


def pad(track, bars, vel=60, key='pad', swell=True, human=0.015, merge=True, cc_lo=40, cc_hi=112):
    """Hold each chord for its run of bars, with an expression swell."""
    runs = []
    for bar in bars:
        if merge and runs and FORM[runs[-1][0]] == FORM[bar] and runs[-1][-1] == bar - 1:
            runs[-1].append(bar)
        else:
            runs.append([bar])
    for run in runs:
        b0 = beat(run[0]); b1 = beat(run[-1] + 1)
        for p in CH[FORM[run[0]]][key]:
            track.note(b0, (b1 - b0) + 0.3, p, vel, human)
        if swell:
            track.ramp(b0, b0 + (b1 - b0) * 0.55, 11, cc_lo, cc_hi)
            track.ramp(b0 + (b1 - b0) * 0.55, b1, 11, cc_hi, cc_lo + 20)


def melody_notes(track, section, vel=80, shift=0, human=0.012, legato=1.0):
    for (b, d, p) in melody(section, shift):
        track.note(b, d * legato, p, vel, human)


def drum_grid(d, bars, hits, vel_map=None, human=0.0):
    """hits: {'kick': 'x...x...', ...} one char per sixteenth ('x' normal, 'X' accent, 'o' ghost, '.' rest); repeats within the bar."""
    for bar in bars:
        for name, grid in hits.items():
            for s, c in enumerate(grid):
                if c == '.':
                    continue
                base = {'x': 100, 'X': 118, 'o': 58}[c]
                if vel_map and name in vel_map:
                    base = int(base * vel_map[name])
                d.hit(beat(bar, s * 0.25), name, max(1, min(127, base)))


def roll(d, b0, b1, drum, v0, v1, step=0.25):
    n = int(round((b1 - b0) / step))
    for i in range(n):
        f = i / max(1, n - 1)
        d.hit(b0 + i * step, drum, int(v0 + (v1 - v0) * f))
