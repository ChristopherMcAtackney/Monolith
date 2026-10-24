"""The theme tracks: music tied to a moment of a mission (the Lector arriving, a capital ship, an outpost), laid over the base score.

A theme is a 32-bar loop at 112 BPM in D minor, the same length as the base loops, so the game starts it in step with whatever the base is playing and it
sits on the bar. The base keeps playing under it (ducked, or silent while the theme replaces it), so the harmony cannot be allowed to fight: the themes
stand on D and A (the root and the fifth, both in nearly every chord of the form) and use the semitone E flat against D as their colour, which is Earth's
mark in the whole score. The diaspora's CALL (A D F E) comes back once in the outpost's.

  theme_lector      Earth's picket corvette hunting in the dark: a sonar ping, a bell that tolls and does not answer, a drone with the semitone creeping in
  theme_destroyer   Earth's capital ship: a low horn on D and A with the E flat arriving, a chant, the taiko, a march that builds
  theme_outpost     an isolated station at the edge of everything: open fifths in a slow electric piano, a pad that breathes, a faint far call

Written by build_music.py (run it with the cue names, or none for everything). Deterministic.
"""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from mid import Song, midi  # noqa: E402
import score as S  # noqa: E402
from score import LOOP_BEATS, BARS, beat  # noqa: E402

PROG = {
    'pad_juno': 89, 'pad_warm': 90, 'strings_ens': 50, 'brass_cs80': 62, 'brass_dark': 63, 'brass_stab': 62, 'lead_prophet': 81,
    'bass_moog': 38, 'bass_pulse': 39, 'bass_sub': 38, 'arp_seq': 80, 'arp_soft': 87, 'bell_fm': 14, 'epiano_fm': 4,
    'choir_ahh': 52, 'drone_dark': 95, 'sweep': 100, 'pulse_thin': 80,
}


def trk(song, name):
    return song.track(name, PROG.get(name.split(':')[0], 0))


def new(title, seed):
    return Song(bpm=S.BPM, title=title, beats=LOOP_BEATS, seed=seed)


def theme_lector():
    s = new('Monolith - theme: the Lector', 31)
    rng = random.Random(31)
    # the drone: D under everything for the whole loop; the E flat creeps in under the second and fourth eights
    d = trk(s, 'drone_dark')
    d.note(0, LOOP_BEATS - 0.5, 'D2', 66, 0.01)
    d.ramp(0, LOOP_BEATS, 11, 70, 90, steps=2)
    for sec in (1, 3):
        d.note(beat(sec * 8), 32, 'Eb2', 50, 0.02)
    sb = trk(s, 'bass_sub')
    for bar in range(0, BARS, 2):
        sb.note(beat(bar), 7.2, 'D1', 64 + (8 if bar >= 16 else 0))
    # the sonar: a thin pulse ping every two bars, a tritone pair (A and E flat) every fourth, and its echo a beat and a half later
    p = trk(s, 'pulse_thin')
    for bar in range(2, BARS, 2):
        v = 52 + 3 * (bar // 4)
        p.note(beat(bar, 0), 0.25, 'A5', v)
        p.note(beat(bar, 1.5), 0.25, 'A5', v - 22)
        if bar % 4 == 2:
            p.note(beat(bar, 2), 0.25, 'Eb5', v - 6)
            p.note(beat(bar, 3.5), 0.25, 'Eb5', v - 26)
    # the bell: tolls on D and answers nothing
    b = trk(s, 'bell_fm')
    for bar in range(0, BARS, 4):
        b.note(beat(bar), 3.5, 'D4', 58)
        if bar % 8 == 4:
            b.note(beat(bar, 2.0), 3.5, 'A3', 44)
    for bar in (14, 30):
        b.note(beat(bar, 2), 3.5, 'Eb4', 50)
    # the sequencer: a slow cold pulse on D and A in eighths, entering at bar 4, widening its filter over each eight
    a = trk(s, 'arp_seq')
    for bar in range(4, BARS - 2):
        for st in range(8):
            if bar < 8 and st % 2:
                continue
            pit = ('D3', 'A3', 'D3', 'A3', 'D4', 'A3', 'D3', 'A3')[st]
            a.note(beat(bar, st * 0.5), 0.4, pit, 46 + (8 if st % 4 == 0 else 0) + (6 if bar >= 16 else 0))
    for sec in range(4):
        a.ramp(beat(sec * 8), beat(sec * 8 + 7), 74, 24, 75)
        a.ramp(beat(sec * 8 + 7), beat(sec * 8 + 8), 74, 75, 24)
    # the choir and the horn: the D against E flat cluster twice, the dark turn
    c = trk(s, 'choir_ahh')
    for (b0, b1) in ((8, 16), (24, 32)):
        for pit in ('D4', 'Eb4', 'A4'):
            c.note(beat(b0), (b1 - b0) * 4 - 1.5, pit, 40, 0.03)                  # released before the loop point: the vowel and the level are back at rest at the seam
        c.ramp(beat(b0), beat(b1 - 1), 20, 0, 127)
        c.ramp(beat(b1 - 1), beat(b1) - 0.5, 20, 127, 0, steps=4)
        c.ramp(beat(b0), beat(b0 + 4), 11, 20, 105)
        c.ramp(beat(b0 + 4), beat(b1) - 2.5, 11, 105, 40)
        c.ramp(beat(b1) - 2.5, beat(b1) - 1.0, 11, 40, 0, steps=3)
    h = trk(s, 'brass_dark')
    for pit in ('D2', 'Eb2', 'A2'):
        h.note(beat(16), 32, pit, 72, 0.02)
    h.ramp(beat(16), beat(20), 11, 30, 118)
    h.ramp(beat(20), beat(24), 11, 118, 40)
    # a heartbeat of drums: a boom each four bars, a rim tick under the sonar from the dark turn
    dr = s.drums()
    for bar in range(0, BARS, 4):
        dr.hit(beat(bar), 'boom', 60 + 4 * (bar // 4))
    for bar in range(16, BARS):
        dr.hit(beat(bar, 2), 'rim', 38)
    sw = trk(s, 'sweep')
    for bar in (15, 31):
        b0 = beat(bar - 1)
        sw.note(b0, 8.0, 'A3', 70)
        sw.ramp(b0, b0 + 8, 74, 18, 100, steps=12)
        sw.ramp(b0, b0 + 8, 11, 12, 100, steps=12)
        sw.ramp(b0 + 7.98, b0 + 8, 11, 100, 0, steps=2)
    return s


def theme_destroyer():
    s = new('Monolith - theme: the destroyer', 32)
    # the horn: D and A, the E flat arriving over the third bar of every four, and the fall back
    h = trk(s, 'brass_dark')
    for k in range(8):
        b0 = beat(k * 4)
        for pit in ('D2', 'A2', 'D3'):
            h.note(b0, 8.0 if k % 2 == 0 else 6.0, pit, 82, 0.02)
        if k % 2 == 0:
            for pit in ('Eb2', 'Bb2'):
                h.note(b0 + 8.0, 8.0, pit, 78, 0.02)
        h.ramp(b0, b0 + 3, 11, 40, 127)
        h.ramp(b0 + 3, b0 + 7.8, 11, 127, 50)
    sb = trk(s, 'bass_sub')
    for bar in range(0, BARS, 4):
        sb.note(beat(bar), 11.5, 'D1', 90)
        sb.note(beat(bar + 3), 4.2, 'Eb1' if (bar // 4) % 2 == 0 else 'D1', 78)
    # the chant: low voices on D and A all through; F and the fifth above in the last eight (the climb back toward D minor)
    c = trk(s, 'choir_ahh')
    for (b0, b1) in ((0, 32),):
        for pit in ('D3', 'A3', 'D4'):
            c.note(beat(b0), (b1 - b0) * 4 + 0.2, pit, 44, 0.03)
    c.ramp(0, LOOP_BEATS, 11, 40, 90, steps=2)
    for bar in range(8, BARS, 8):
        c.ramp(beat(bar - 2), beat(bar + 2), 11, 50, 112)
        c.ramp(beat(bar + 2), beat(bar + 6), 11, 112, 55)
    c.ramp(0, LOOP_BEATS, 20, 0, 127, steps=8)
    for pit in ('F4', 'A4'):
        c.note(beat(24), 32, pit, 36, 0.03)
    # the strings: the fifth, held, so the horn has a floor
    st = trk(s, 'strings_ens')
    for pit in ('D3', 'A3', 'E4'):
        st.note(beat(8), 96, pit, 46)
        st.note(beat(24), 31, pit, 46)
    st.ramp(beat(8), beat(16), 11, 40, 112)
    st.ramp(beat(24), beat(32), 11, 40, 112)
    # the weight: a taiko every two bars, a boom every four, the march coming in at the second eight and the snare at the third
    d = s.drums()
    for bar in range(0, BARS, 2):
        d.hit(beat(bar), 'taiko', 96 + (8 if bar % 4 == 0 else 0))
    for bar in range(0, BARS, 4):
        d.hit(beat(bar), 'boom', 110)
    for bar in range(8, BARS):
        d.hit(beat(bar, 1), 'kick', 74 if bar < 16 else 88)
        d.hit(beat(bar, 3), 'kick', 70 if bar < 16 else 84)
    for bar in range(16, BARS):
        if bar % 2 == 1:
            d.hit(beat(bar, 2), 'snare2', 84)
    for bar in (7, 15, 23, 31):
        S.roll(d, beat(bar, 2), beat(bar + 1), 'tom6', 50, 112, 0.25)
    for bar in (8, 16, 24):
        d.hit(beat(bar), 'crash2', 92)
    b = trk(s, 'bell_fm')
    for bar in range(0, BARS, 8):
        b.note(beat(bar), 7, 'D3', 70)
        b.note(beat(bar + 4), 7, 'Eb3', 58)
    sw = trk(s, 'sweep')
    for bar in (8, 16, 24, 32):
        b0 = beat(bar - 2)
        sw.note(b0, 8.0, 'D3', 78); sw.note(b0, 8.0, 'A3', 70)
        sw.ramp(b0, b0 + 8, 74, 15, 118, steps=16)
        sw.ramp(b0, b0 + 8, 11, 12, 118, steps=16)
        sw.ramp(b0 + 7.98, b0 + 8, 11, 118, 0, steps=2)
    # the lament, bent: the line the dead hear, in the dark horn, once, over the last eight
    lam = [(0, 4, 'A4'), (4, 4, 'G4'), (8, 4, 'F4'), (12, 6, 'E4'), (18, 12, 'D4')]
    ln = trk(s, 'brass_cs80')
    for (b0, dur, pit) in lam:
        ln.note(beat(24) + b0, dur * 0.97, pit, 70, 0.01)
        ln.ramp(beat(24) + b0, beat(24) + b0 + dur * 0.8, 11, 50, 118, steps=3)
    return s


def theme_outpost():
    s = new('Monolith - theme: the outpost', 33)
    rng = random.Random(33)
    # the slow electric piano: open fifths and the ninth, D A E A, a little different each four bars, a note short of sentimental
    e = trk(s, 'epiano_fm')
    cells = [('D4', 'A4', 'E5', 'A4'), ('D4', 'A4', 'E5', 'D5'), ('A3', 'D4', 'E4', 'A4'), ('D4', 'A4', 'D5', 'A4')]
    for bar in range(BARS):
        cell = cells[(bar // 4) % 4]
        if bar < 2:
            continue
        for st in range(8):
            if bar < 6 and st % 2:
                continue
            pit = cell[(st // 2) % 4] if st % 2 == 0 else cell[(st // 2 + 1) % 4]
            e.note(beat(bar, st * 0.5), 0.9, pit, 42 + (10 if st % 4 == 0 else 0), 0.01)
    pj = trk(s, 'pad_juno')
    for (b0, b1) in ((4, 12), (12, 20), (20, 28), (28, 32)):
        for pit in ('D3', 'A3', 'E4'):
            pj.note(beat(b0), (b1 - b0) * 4 + 0.3, pit, 44, 0.02)
        pj.ramp(beat(b0), beat(b0 + (b1 - b0) * 0.55), 11, 30, 110)
        pj.ramp(beat(b0 + (b1 - b0) * 0.55), beat(b1), 11, 110, 40)
    pw = trk(s, 'pad_warm')
    for (b0, b1) in ((8, 16), (24, 32)):
        for pit in ('A4', 'D5'):
            pw.note(beat(b0), (b1 - b0) * 4 + 0.3, pit, 40, 0.02)
        pw.ramp(beat(b0), beat(b1), 11, 30, 105)
    sb = trk(s, 'bass_sub')
    for bar in range(0, BARS, 8):
        sb.note(beat(bar), 31.0, 'D1', 62)
    # the bells: sparse, high, on D A E, scattered
    b = trk(s, 'bell_fm')
    tones = ['D6', 'A5', 'E6', 'A6', 'D5']
    for bar in range(2, BARS, 2):
        if rng.random() < 0.7:
            b.note(beat(bar, rng.choice([0.0, 1.0, 2.0, 2.5, 3.0])), 3.0, rng.choice(tones), 40 + rng.randint(-4, 6))
    # a call from very far away: the diaspora's (A D F E), one note a bar, in the soft brass, once in the dark turn
    br = trk(s, 'brass_cs80')
    for k, (pit, dur) in enumerate((('A4', 4), ('D5', 4), ('F5', 4), ('E5', 8))):
        br.note(beat(16) + k * 4 + (4 if k == 3 else 0) * 0, dur - 0.2, pit, 42, 0.02)
    br.ramp(beat(16), beat(20), 11, 30, 95)
    br.ramp(beat(20), beat(24), 11, 95, 30)
    ch = trk(s, 'choir_ahh')
    for (b0, b1) in ((12, 16), (28, 32)):
        for pit in ('A4', 'D5'):
            ch.note(beat(b0), (b1 - b0) * 4 + 0.3, pit, 36, 0.03)
        ch.ramp(beat(b0), beat(b1), 11, 20, 90)
        ch.ramp(beat(b0), beat(b1), 20, 0, 90)
    d = s.drums()
    for bar in (16, 24):
        d.hit(beat(bar), 'boom', 52)
        d.hit(beat(bar, 1.25), 'boom', 36)
    return s


THEMES = {'theme_lector': theme_lector, 'theme_destroyer': theme_destroyer, 'theme_outpost': theme_outpost}
