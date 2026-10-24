#!/usr/bin/env python
"""Compose the score: writes audio_src/midi/*.mid. Run with plain Python (no packages):  python audio_src/compose/build_music.py [cue ...]

The loops share one 32-bar form and one harmony (see score.py), so the game can cross-fade between them on a beat. Each layer function below is
one instrument line; a cue is a choice of layers and how hard they play. The synth and mix are in tools/audio (the track name is the patch).
"""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from mid import Song, midi  # noqa: E402
import score as S  # noqa: E402
from score import FORM, CH, beat, BARS, LOOP_BEATS, bars_of  # noqa: E402

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'midi'))

PROG = {
    'pad_juno': 89, 'pad_warm': 90, 'strings_ens': 50, 'brass_cs80': 62, 'brass_dark': 63, 'brass_stab': 62, 'lead_prophet': 81,
    'bass_moog': 38, 'bass_pulse': 39, 'bass_sub': 38, 'arp_seq': 80, 'arp_soft': 87, 'bell_fm': 14, 'epiano_fm': 4,
    'choir_ahh': 52, 'drone_dark': 95, 'sweep': 100, 'pulse_thin': 80,
}


def trk(song, name):
    return song.track(name, PROG.get(name.split(':')[0], 0))


def new(title, bpm=S.BPM, beats=LOOP_BEATS, seed=1):
    return Song(bpm=bpm, title=title, beats=beats, seed=seed)


# ====================================================================================================================================
# layers of the 32-bar loops
# ====================================================================================================================================

def runs_of(bars):
    runs = []
    for bar in bars:
        if runs and FORM[runs[-1][0]] == FORM[bar] and runs[-1][-1] == bar - 1:
            runs[-1].append(bar)
        else:
            runs.append([bar])
    return runs


def L_drone(s, vel=70, name='drone_dark'):
    t = trk(s, name)
    # D stays under the E flat of the dark turn: the pedal that makes it a semitone clash
    chords = list(FORM)
    for b in range(16, 20):
        chords[b] = 'Dm'
    bar = 0
    while bar < BARS:
        j = bar
        while j + 1 < BARS and chords[j + 1] == chords[bar]:
            j += 1
        t.note(beat(bar), (j - bar + 1) * 4 + 0.6, midi(CH[chords[bar]]['root']), vel, 0.01)
        bar = j + 1
    t.ramp(0, LOOP_BEATS, 11, 90, 90, steps=2)
    return t


def L_sub(s, vel=70, bars=None, name='bass_sub'):
    t = trk(s, name)
    for run in runs_of(bars if bars is not None else range(BARS)):
        b0, b1 = beat(run[0]), beat(run[-1] + 1)
        t.note(b0, (b1 - b0) - 0.15, midi(CH[FORM[run[0]]]['sub']), vel)
    return t


def L_pad(s, vel=60, bars=None, name='pad_juno', key='pad', **kw):
    t = trk(s, name)
    S.pad(t, bars if bars is not None else range(BARS), vel=vel, key=key, **kw)
    return t


def L_strings(s, vel=50, bars=None):
    t = trk(s, 'strings_ens')
    S.pad(t, bars if bars is not None else range(BARS), vel=vel, key='hi', swell=True, cc_lo=50, cc_hi=120)
    return t


def L_choir_theme(s, sections, vel=60, shift=0):
    t = trk(s, 'choir_ahh')
    for sec in sections:
        S.melody_notes(t, sec, vel=vel, shift=shift, legato=0.98)
        t.ramp(beat(sec * 8), beat(sec * 8 + 8), 20, 0, 90)
        t.ramp(beat(sec * 8 + 8) - 0.01, beat(sec * 8 + 8), 20, 90, 0, steps=2)
    return t


def L_choir_pad(s, bars, vel=50, key='hi', octave=0):
    t = trk(s, 'choir_ahh')
    for run in runs_of(bars):
        b0, b1 = beat(run[0]), beat(run[-1] + 1)
        for p in CH[FORM[run[0]]][key]:
            t.note(b0, (b1 - b0) + 0.2, midi(p) + 12 * octave, vel, 0.015)
        t.ramp(b0, b0 + (b1 - b0) * 0.6, 11, 50, 115)
        t.ramp(b0 + (b1 - b0) * 0.6, b1, 11, 115, 50)
    return t


def L_choir_cluster(s, bars, vel=48):
    """The dark turn: D and E flat held together, the vowel turning from ah to oh."""
    t = trk(s, 'choir_ahh')
    b0, b1 = beat(bars[0]), beat(bars[-1] + 1)
    for p in ('D3', 'Eb3', 'A3', 'D4'):
        t.note(b0, b1 - b0 + 0.3, p, vel, 0.02)
    t.ramp(b0, b1, 20, 0, 127)
    t.ramp(b0, b0 + 8, 11, 30, 110)
    t.ramp(b0 + 8, b1, 11, 110, 60)
    return t


def L_bells(s, density, vel=52, seed=7, name='bell_fm'):
    """density: per-section notes per bar. Deterministic scatter over the chord's upper tones."""
    rng = random.Random(seed)
    t = trk(s, name)
    for bar in range(BARS):
        n = density[S.SECTION(bar)]
        k = int(n) + (1 if rng.random() < n - int(n) else 0)
        tones = [midi(x) + 12 for x in CH[FORM[bar]]['hi']] + [midi(CH[FORM[bar]]['hi'][0]) + 24]
        slots = rng.sample([0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5], 8)
        for i in range(k):
            t.note(beat(bar, slots[i]), 1.5, rng.choice(tones), vel + rng.randint(-8, 6), 0.0)
    return t


def L_arp(s, bars, pattern, vel=70, name='arp_seq', octave=0, sweep=(0.30, 0.80), bar_patterns=None, accent=14, gate=0.85, human=0.0, tones_key='tones'):
    t = trk(s, name)
    S.arp(t, bars, pattern, vel=vel, octave=octave, bar_patterns=bar_patterns, accent=accent, gate=gate, human=human, tones_key=tones_key)
    # a resonant filter sweep over every section
    for sec in sorted(set(b // 8 for b in bars)):
        t.ramp(beat(sec * 8), beat(sec * 8 + 7), 74, int(sweep[0] * 127), int(sweep[1] * 127))
        t.ramp(beat(sec * 8 + 7), beat(sec * 8 + 8), 74, int(sweep[1] * 127), int(sweep[0] * 127))
    return t


def L_bass(s, bars, pattern, vel=95, name='bass_moog', key='root', octave=0, accent=12):
    t = trk(s, name)
    S.bassline(t, bars, pattern, vel=vel, key=key, octave=octave, accent=accent)
    return t


def L_brass_theme(s, sections, vel=88, shift=0, name='brass_cs80', legato=0.97):
    t = trk(s, name)
    for sec in sections:
        S.melody_notes(t, sec, vel=vel, shift=shift, legato=legato, human=0.01)
        # lean on the keys: the expression pedal swells on every long note
        for (b, d, p) in S.melody(sec, shift):
            if d >= 2:
                t.ramp(b, b + d * 0.7, 11, 75, 127, steps=4)
    return t


def L_brass_pad(s, bars, vel=60, name='brass_dark', key='pad', octave=-1):
    t = trk(s, name)
    for run in runs_of(bars):
        b0, b1 = beat(run[0]), beat(run[-1] + 1)
        for p in CH[FORM[run[0]]][key][:3]:
            t.note(b0, (b1 - b0) + 0.2, midi(p) + 12 * octave, vel, 0.02)
        t.ramp(b0, b0 + (b1 - b0) * 0.7, 11, 55, 127)
        t.ramp(b0 + (b1 - b0) * 0.7, b1, 11, 127, 70)
    return t


def L_stabs(s, bars, steps, vel=100, name='brass_stab', octave=0):
    t = trk(s, name)
    for bar in bars:
        for st in steps:
            for p in CH[FORM[bar]]['stab']:
                t.note(beat(bar, st * 0.25), 0.2, midi(p) + 12 * octave, vel, 0.0)
    return t


def L_lead(s, sections, vel=92, shift=12, name='lead_prophet'):
    t = trk(s, name)
    for sec in sections:
        for (b, d, p) in S.melody(sec, shift):
            t.note(b, d * 0.95, p, vel, 0.008)
        t.ramp(beat(sec * 8), beat(sec * 8 + 8), 1, 0, 40)
    return t


def L_sweeps(s, ends, length=4, name='sweep'):
    """A riser into each of the given bars: filter opening, volume growing."""
    t = trk(s, name)
    for end in ends:
        b1 = beat(end)
        b0 = b1 - length * 4 if b1 - length * 4 >= 0 else 0
        root = midi(CH[FORM[(end - 1) % BARS]]['pad'][1])
        t.note(b0, b1 - b0, root, 80)
        t.note(b0, b1 - b0, root + 12, 70)
        t.ramp(b0, b1, 74, 18, 115, steps=16)
        t.ramp(b0, b1, 11, 15, 120, steps=16)
        t.ramp(b1 - 0.02, b1, 11, 120, 0, steps=2)
    return t


# --- drums ------------------------------------------------------------------------------------------------------------------------

def drums_loop(s, level):
    """level 1: a soft gated beat; 2: driving; 3: full. Returns the drum track."""
    d = s.drums()
    secs = range(4)
    for bar in range(BARS):
        sec = S.SECTION(bar)
        last = (bar % 8) == 7
        first = (bar % 8) == 0
        if level == 1:
            if bar >= 4 or sec > 0:
                S.drum_grid(d, [bar], {'kick': 'x.......o.......', 'snare': '....x.......x...'}, {'kick': 0.85, 'snare': 0.78})
            if sec >= 1:
                S.drum_grid(d, [bar], {'hat': 'o.o.o.o.o.o.o.o.'}, {'hat': 0.7})
            if first and sec > 0:
                d.hit(beat(bar), 'taiko', 70)
            if last:
                S.roll(d, beat(bar, 3), beat(bar + 1), 'tom3', 55, 85, 0.5)
        elif level == 2:
            kick = 'x.....x.x.....x.' if sec != 2 else 'x.......x.....x.'
            S.drum_grid(d, [bar], {'kick': kick, 'snare': '....x.......x...', 'hat': 'x.o.x.o.x.o.x.o.'}, {'hat': 0.62, 'kick': 0.95})
            if sec >= 1:
                S.drum_grid(d, [bar], {'clap': '....o.......o...'}, {'clap': 0.8})
            if bar % 2 == 1:
                S.drum_grid(d, [bar], {'ohat': '..............o.'}, {'ohat': 0.55})
            if first:
                d.hit(beat(bar), 'crash', 88)
                d.hit(beat(bar), 'taiko', 92)
            if last:
                S.roll(d, beat(bar, 2), beat(bar + 1), 'tom4', 60, 110, 0.25)
                d.hit(beat(bar, 3.5), 'tom1', 118)
        else:
            kick = 'x...x...x..xx.x.' if sec != 2 else 'x..xx...x..xx.x.'
            S.drum_grid(d, [bar], {'kick': kick, 'snare': '....X.......X...', 'hat': 'x.xxx.xxx.xxx.xx'}, {'hat': 0.66, 'kick': 1.0})
            S.drum_grid(d, [bar], {'clap': '....x.......x...'}, {'clap': 0.85})
            if bar % 2 == 1:
                S.drum_grid(d, [bar], {'ohat': '..............X.'}, {'ohat': 0.6})
            if bar % 4 == 0:
                d.hit(beat(bar), 'crash', 100)
                d.hit(beat(bar), 'taiko', 108)
            if bar % 4 == 3 or last:
                S.roll(d, beat(bar, 2), beat(bar + 1), 'tom6', 70, 118, 0.25 if not last else 0.125)
                d.hit(beat(bar, 3.75), 'snare', 120)
            if last:
                d.hit(beat(bar, 3.75), 'boom', 110)
    return d


# ====================================================================================================================================
# the cues
# ====================================================================================================================================

def ambient():
    s = new('Monolith - ambient')
    L_drone(s, 72)
    L_sub(s, 62)
    L_pad(s, 52, bars=bars_of(0, 1, 2, 3), name='pad_juno', cc_lo=30, cc_hi=105)
    L_pad(s, 44, bars=bars_of(1, 3), name='pad_warm', key='hi')
    L_strings(s, 40, bars=bars_of(1, 3))
    # the call, quietly, once in each major section, and a bar of lament at the dark turn
    L_choir_theme(s, [1, 3], vel=44)
    L_choir_cluster(s, [16, 17, 18, 19], 40)
    t = trk(s, 'choir_ahh')
    t.note(beat(3), 4, 'A4', 38); t.note(beat(7), 4, 'A4', 36)
    L_bells(s, density=[0.5, 1.0, 0.75, 1.25], vel=48)
    L_arp(s, bars_of(1, 2, 3), S.ARP_SLOW, vel=40, name='arp_soft', octave=0, sweep=(0.35, 0.65))
    # the CS-80 far away: a long call in the lull
    t = trk(s, 'brass_cs80')
    t.note(beat(12), 8, 'D4', 46); t.ramp(beat(12), beat(14), 11, 40, 120)
    t.note(beat(20), 8, 'F4', 44); t.ramp(beat(20), beat(22), 11, 40, 120)
    d = s.drums()
    for bar in range(16, 24):                         # a slow heartbeat under the dark turn
        d.hit(beat(bar), 'boom', 52 + (bar - 16) * 3)
        d.hit(beat(bar, 0.75), 'boom', 36 + (bar - 16) * 2)
    d.hit(beat(16), 'taiko', 66); d.hit(beat(24), 'taiko', 62)
    return s


def battle1():
    s = new('Monolith - battle 1', seed=2)
    L_drone(s, 64)
    L_sub(s, 70)
    L_pad(s, 56, bars=range(BARS), name='pad_juno', cc_lo=40, cc_hi=110)
    L_pad(s, 48, bars=bars_of(1, 3), name='pad_warm', key='hi')
    L_bass(s, range(BARS), S.BASS_8TH, vel=72, name='bass_pulse', accent=8)
    L_arp(s, bars_of(0), S.ARP_8TH, vel=64, sweep=(0.25, 0.55))
    L_arp(s, bars_of(1, 2, 3), S.ARP_UP, vel=64, sweep=(0.25, 0.65), bar_patterns=[S.ARP_UP, S.ARP_TD])
    L_brass_theme(s, [0, 1], vel=66)
    L_brass_pad(s, bars_of(2), vel=54)
    L_choir_theme(s, [1, 3], vel=52)
    L_choir_cluster(s, [16, 17, 18, 19], 44)
    L_bells(s, density=[0.5, 0.75, 0.5, 1.0], vel=46, seed=11)
    drums_loop(s, 1)
    L_sweeps(s, [16, 24], length=2)
    return s


def battle2():
    s = new('Monolith - battle 2', seed=3)
    L_drone(s, 60)
    L_sub(s, 76)
    L_pad(s, 56, name='pad_juno', cc_lo=50, cc_hi=112)
    L_pad(s, 52, bars=bars_of(1, 2, 3), name='pad_warm', key='hi')
    L_strings(s, 44, bars=bars_of(1, 2, 3))
    L_bass(s, range(BARS), S.BASS_GALLOP, vel=78, name='bass_pulse', accent=10)
    L_bass(s, range(BARS), S.BASS_8TH, vel=70, name='bass_moog', octave=1, accent=8)
    L_arp(s, range(BARS), S.ARP_UP, vel=70, bar_patterns=[S.ARP_UP, S.ARP_TD, S.ARP_UP, S.ARP_RUN], sweep=(0.30, 0.75))
    L_brass_theme(s, [0, 1, 3], vel=84)
    L_brass_pad(s, bars_of(2), vel=64)
    L_stabs(s, bars_of(1, 2, 3), [10, 14], vel=88)
    L_choir_theme(s, [1, 3], vel=62)
    L_choir_cluster(s, [16, 17, 18, 19], 52)
    L_lead(s, [3], vel=72, shift=0)
    L_bells(s, density=[0.5, 1.0, 0.75, 1.0], vel=44, seed=13)
    drums_loop(s, 2)
    L_sweeps(s, [8, 16, 24, 32], length=2)
    return s


def battle3():
    s = new('Monolith - battle 3', seed=4)
    L_drone(s, 60)
    L_sub(s, 82)
    L_pad(s, 54, name='pad_juno', cc_lo=60, cc_hi=118)
    L_pad(s, 56, name='pad_warm', key='hi')
    L_strings(s, 50)
    L_bass(s, range(BARS), S.BASS_GALLOP, vel=88, name='bass_pulse', accent=10)
    L_bass(s, range(BARS), S.BASS_SYNC, vel=84, name='bass_moog', octave=1, accent=8)
    L_arp(s, range(BARS), S.ARP_UP, vel=76, bar_patterns=[S.ARP_TD, S.ARP_UP, S.ARP_TD, S.ARP_RUN], sweep=(0.35, 0.85), human=0.0)
    L_arp(s, range(BARS), S.ARP_TD, vel=56, name='arp_seq:hi', octave=1, sweep=(0.25, 0.7), bar_patterns=[S.ARP_UP, S.ARP_RUN])
    L_brass_theme(s, [0, 1, 2, 3], vel=100)
    L_brass_theme(s, [1, 3], vel=84, shift=-12, name='brass_cs80:low')
    L_brass_pad(s, range(BARS), vel=58)
    L_stabs(s, bars_of(0, 1, 2, 3), [3, 6, 10, 14], vel=96)
    L_choir_theme(s, [0, 1, 3], vel=76)
    L_choir_cluster(s, [16, 17, 18, 19], 56)
    L_lead(s, [1, 3], vel=86, shift=12)
    L_bells(s, density=[1.0, 1.5, 1.0, 1.5], vel=50, seed=17)
    drums_loop(s, 3)
    L_sweeps(s, [8, 16, 24, 32], length=2)
    return s


# ====================================================================================================================================
# stings and endings
# ====================================================================================================================================

def one_shot(title, bpm, bars, seed=1):
    return Song(bpm=bpm, title=title, beats=bars * 4, seed=seed)


def friendly(battle=False):
    s = one_shot('Monolith - friendly arrival' + (' (battle)' if battle else ''), 112, 2, seed=21)
    t = trk(s, 'brass_cs80')
    t.note(0, 2, 'A3', 84, 0.0); t.note(2, 6, 'D4', 96); t.note(2, 6, 'A4', 80)
    t.ramp(0, 6, 11, 70, 127)
    c = trk(s, 'choir_ahh')
    for p in ('A3', 'E4', 'A4', 'D5'):
        c.note(2, 6, p, 58, 0.02)
    c.ramp(2, 8, 20, 0, 120); c.ramp(2, 6, 11, 40, 120)
    st = trk(s, 'strings_ens')
    for p in ('D3', 'A3', 'E4', 'A4'):
        st.note(0, 8, p, 56)
    st.ramp(0, 5, 11, 40, 125)
    b = trk(s, 'bell_fm')
    b.note(4, 3, 'A5', 70); b.note(5, 3, 'E6', 64); b.note(6, 3, 'D6', 60)
    sb = trk(s, 'bass_sub'); sb.note(0, 8, 'D1', 86)
    d = s.drums()
    d.hit(0, 'taiko', 90)
    if battle:
        S.roll(d, 3, 4, 'snare', 50, 105, 0.125)
        d.hit(4, 'crash', 100); d.hit(4, 'taiko', 110)
        sk = trk(s, 'brass_stab')
        for p in ('D3', 'A3', 'D4', 'F4', 'A4'):
            sk.note(4, 0.5, p, 104)
    return s


def enemy(battle=False):
    s = one_shot('Monolith - enemy arrival' + (' (battle)' if battle else ''), 112, 2, seed=22)
    t = trk(s, 'brass_dark')
    for p in ('D2', 'Eb2', 'A2'):
        t.note(0, 7, p, 100, 0.0)
    t.ramp(0, 3, 11, 30, 127); t.ramp(3, 8, 11, 127, 60)
    t.ramp(0, 3, 74, 40, 90)
    sb = trk(s, 'bass_sub'); sb.note(0, 8, 'D1', 100)
    sw = trk(s, 'sweep'); sw.note(0, 3.6, 'Eb3', 90); sw.note(0, 3.6, 'A3', 80); sw.ramp(0, 3.6, 74, 15, 120, steps=12); sw.ramp(0, 3.6, 11, 10, 127, steps=12)
    p = trk(s, 'pulse_thin')
    p.note(0, 0.5, 'A4', 100); p.note(0.75, 0.5, 'Eb4', 96); p.note(1.5, 0.5, 'A3', 90); p.note(2.25, 0.8, 'Eb3', 90)
    b = trk(s, 'bell_fm'); b.note(0, 4, 'D4', 90); b.note(4, 4, 'Eb3', 70)
    d = s.drums()
    d.hit(0, 'boom', 120); d.hit(0, 'crash2', 90); d.hit(3.5, 'taiko', 100)
    if battle:
        S.roll(d, 2, 4, 'tom4', 60, 118, 0.25)
        d.hit(4, 'boom', 127); d.hit(4, 'taiko', 118); d.hit(4, 'crash', 108)
    return s


def goal_complete():
    s = one_shot('Monolith - goal complete', 112, 2, seed=23)
    t = trk(s, 'brass_cs80')
    t.note(0, 1.5, 'D4', 92); t.note(1.5, 5, 'A4', 98); t.ramp(0, 4, 11, 80, 127)
    b = trk(s, 'bell_fm'); b.note(1.5, 3, 'D6', 70); b.note(2.25, 3, 'A5', 60)
    c = trk(s, 'choir_ahh'); c.note(1.5, 5, 'D5', 54, 0.02); c.note(1.5, 5, 'A4', 50, 0.02); c.ramp(1.5, 6, 11, 40, 110)
    sb = trk(s, 'bass_sub'); sb.note(0, 6, 'D1', 80)
    d = s.drums(); d.hit(0, 'taiko', 82)
    return s


def goal_failed():
    s = one_shot('Monolith - goal failed', 96, 2, seed=24)
    b = trk(s, 'bell_fm'); b.note(0, 3, 'Eb5', 90); b.note(1.5, 4, 'D5', 84); b.note(3.5, 4, 'A4', 70)
    t = trk(s, 'brass_dark')
    for p in ('D2', 'A2', 'F3'):
        t.note(0, 6, p, 84, 0.02)
    t.ramp(0, 2, 11, 40, 110); t.ramp(2, 6, 11, 110, 40)
    sb = trk(s, 'bass_sub'); sb.note(0, 6, 'D1', 90)
    d = s.drums(); d.hit(0, 'boom', 100); d.hit(1.5, 'boom', 70)
    return s


def victory():
    # 10 bars: the build in minor, the arrival in major, the long fall into silence
    s = Song(bpm=104, title='Monolith - victory', beats=10 * 4, seed=25)
    prog = ['Bb', 'Bb', 'Gm', 'A', 'D', 'D', 'G', 'A', 'D', 'D']
    chordsets = {'Bb': ['Bb2', 'F3', 'Bb3', 'D4'], 'Gm': ['G2', 'D3', 'G3', 'Bb3'], 'A': ['A2', 'E3', 'A3', 'C#4'],
                 'D': ['D3', 'A3', 'D4', 'F#4'], 'G': ['G2', 'D3', 'G3', 'B3']}
    roots = {'Bb': 'Bb1', 'Gm': 'G1', 'A': 'A1', 'D': 'D1', 'G': 'G1'}
    pj = trk(s, 'pad_juno'); pw = trk(s, 'pad_warm'); sb = trk(s, 'bass_sub'); st = trk(s, 'strings_ens'); ch = trk(s, 'choir_ahh')
    for i, c in enumerate(prog):
        b0 = i * 4
        for p in chordsets[c]:
            pj.note(b0, 4.2, p, 62, 0.015)
        for p in chordsets[c][1:]:
            pw.note(b0, 4.2, midi(p) + 12, 50, 0.015)
        sb.note(b0, 3.9, roots[c], 84)
        if i >= 4:
            for p in chordsets[c][1:]:
                st.note(b0, 4.2, midi(p) + 12, 56, 0.015)
    pj.ramp(0, 16, 11, 40, 120); st.ramp(16, 40, 11, 90, 127)
    # the build: timpani roll and a snare march
    d = s.drums()
    S.roll(d, 8, 16, 'taiko', 40, 110, 0.5)
    S.roll(d, 12, 16, 'snare', 40, 115, 0.25)
    d.hit(16, 'crash', 104); d.hit(16, 'taiko', 118); d.hit(16, 'boom', 100)
    for bar in range(4, 9):
        for st_ in (0, 2):
            d.hit(bar * 4 + st_, 'kick', 90)
        d.hit(bar * 4 + 1, 'snare', 82); d.hit(bar * 4 + 3, 'snare', 90)
    d.hit(32, 'taiko', 100); d.hit(32, 'crash2', 90)
    # the call, resolved into the major
    br = trk(s, 'brass_cs80')
    mel = [(0, 2, 'F4'), (2, 2, 'A4'), (4, 2, 'Bb4'), (6, 2, 'D5'),            # the climb out of the minor
           (8, 2, 'G4'), (10, 1, 'Bb4'), (11, 1, 'D5'), (12, 4, 'E5'),
           (16, 2, 'A4'), (18, 1, 'D5'), (19, 1, 'F#5'), (20, 3, 'A5'), (23, 1, 'G5'),
           (24, 2, 'F#5'), (26, 1, 'E5'), (27, 1, 'D5'), (28, 2, 'E5'), (30, 1, 'C#5'), (31, 1, 'E5'),
           (32, 6, 'D5')]
    for (b, dur, p) in mel:
        br.note(b, dur * 0.97, p, 92 if b < 16 else 104, 0.01)
        if dur >= 2:
            br.ramp(b, b + dur * 0.7, 11, 80, 127, steps=4)
    ch.note(16, 12, 'D5', 64, 0.02); ch.note(16, 12, 'A4', 60, 0.02); ch.note(16, 12, 'F#4', 56, 0.02); ch.ramp(16, 28, 11, 60, 125)
    ch.note(28, 12, 'D5', 62, 0.02); ch.note(28, 12, 'A4', 58, 0.02); ch.note(28, 12, 'F#4', 54, 0.02); ch.ramp(28, 40, 11, 125, 40)
    b = trk(s, 'bell_fm')
    for k, p in enumerate(('D6', 'A5', 'F#6', 'D6', 'A5', 'E6', 'D6', 'F#6')):
        b.note(32 + k * 0.5, 4, p, 64 - k * 2)
    ar = trk(s, 'arp_seq')
    for bar in range(4, 8):
        for i in range(16):
            tones = chordsets[prog[bar]]
            ar.note(bar * 4 + i * 0.25, 0.2, midi(tones[[0, 1, 2, 3, 2, 1, 2, 3][i % 8]]) + 12, 64 + (12 if i % 4 == 0 else 0))
    return s


def failure():
    s = Song(bpm=72, title='Monolith - mission failed', beats=6 * 4, seed=26)
    prog = ['Dm', 'Bb', 'Gm', 'A', 'Dm', 'Dm']
    pads = {'Dm': ['D2', 'A2', 'D3', 'F3'], 'Bb': ['Bb1', 'F2', 'Bb2', 'D3'], 'Gm': ['G1', 'D2', 'G2', 'Bb2'], 'A': ['A1', 'E2', 'A2', 'C#3']}
    pj = trk(s, 'pad_juno'); bd = trk(s, 'brass_dark'); sb = trk(s, 'bass_sub'); ch = trk(s, 'choir_ahh')
    for i, c in enumerate(prog):
        b0 = i * 4
        for p in pads[c][:3 if i == 5 else 4]:
            pj.note(b0, 4.4, midi(p) + 12, 58, 0.015)
            bd.note(b0, 4.4, midi(p) + 12, 52, 0.02)
        sb.note(b0, 4.0, pads[c][0], 80)
    pj.ramp(0, 20, 11, 60, 110); pj.ramp(20, 24, 11, 110, 20)
    bd.ramp(0, 24, 11, 50, 100); bd.ramp(20, 24, 11, 100, 15)
    br = trk(s, 'brass_cs80')
    lament = [(0, 2, 'A4'), (2, 1, 'G4'), (3, 1, 'F4'), (4, 3, 'E4'), (7, 1, 'D4'), (8, 2, 'D4'), (10, 1, 'C4'), (11, 1, 'Bb3'),
              (12, 4, 'A3'), (16, 3, 'F4'), (19, 1, 'E4'), (20, 4, 'D4')]
    for (b, dur, p) in lament:
        br.note(b, dur * 0.98, p, 78 - int(b), 0.01)
        br.ramp(b, b + dur * 0.8, 11, 60, 118, steps=3)
    br.ramp(20, 24, 11, 110, 10)
    ch.note(8, 16, 'D4', 46, 0.02); ch.note(8, 16, 'F4', 42, 0.02); ch.ramp(8, 24, 20, 120, 127); ch.ramp(8, 24, 11, 40, 100)
    b = trk(s, 'bell_fm'); b.note(0, 4, 'D4', 66); b.note(12, 6, 'A3', 52); b.note(20, 6, 'D3', 50)
    d = s.drums(); d.hit(0, 'boom', 100); d.hit(8, 'taiko', 60); d.hit(16, 'taiko', 50); d.hit(20, 'boom', 70)
    return s


def death():
    s = Song(bpm=72, title='Monolith - player lost', beats=6 * 4, seed=27)
    d = s.drums()
    d.hit(0, 'boom', 127); d.hit(0, 'taiko', 120); d.hit(0, 'crash2', 108); d.hit(0.05, 'kick', 120)
    pj = trk(s, 'pad_juno')
    for p in ('D3', 'Eb3', 'A3', 'D4'):
        pj.note(0, 12, p, 86, 0.0)
    pj.ramp(0, 12, 11, 127, 20)
    bd = trk(s, 'brass_dark')
    for p in ('D2', 'Eb2', 'A2'):
        bd.note(0, 10, p, 104)
    bd.ramp(0, 10, 11, 127, 10)
    sb = trk(s, 'bass_sub'); sb.note(0, 12, 'D1', 100); sb.ramp(0, 12, 11, 127, 10)
    b = trk(s, 'bell_fm'); b.note(4, 6, 'D4', 78); b.note(10, 6, 'A3', 60); b.note(16, 8, 'D3', 48)
    ch = trk(s, 'choir_ahh')
    for p in ('A3', 'D4', 'F4'):
        ch.note(4, 14, p, 40, 0.03)
    ch.ramp(4, 18, 11, 10, 90); ch.ramp(4, 18, 20, 127, 127)
    d.hit(12, 'boom', 70); d.hit(13.5, 'boom', 46)               # a slow heart, and the second beat does not come
    d.hit(18, 'boom', 30)
    return s


CUES = {
    'ambient': ambient, 'battle1': battle1, 'battle2': battle2, 'battle3': battle3,
    'friendly_arrival': lambda: friendly(False), 'friendly_arrival_battle': lambda: friendly(True),
    'enemy_arrival': lambda: enemy(False), 'enemy_arrival_battle': lambda: enemy(True),
    'goal_complete': goal_complete, 'goal_failed': goal_failed, 'victory': victory, 'failure': failure, 'death': death,
}


from themes import THEMES  # noqa: E402  (the theme tracks: themes.py)
CUES.update(THEMES)


def main(argv):
    os.makedirs(OUT, exist_ok=True)
    names = argv or list(CUES)
    for n in names:
        song = CUES[n]()
        path = os.path.join(OUT, n + '.mid')
        song.write(path)
        notes = sum(len(t.events) for t in song.tracks) // 2
        print(f'{n}: {len(song.tracks)} tracks, {notes} events -> {os.path.relpath(path)}')


if __name__ == '__main__':
    main(sys.argv[1:])
