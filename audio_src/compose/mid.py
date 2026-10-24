"""A tiny Standard MIDI File writer, so the score can be composed in Python and opened in any DAW.

  song = Song(bpm=112, title='Monolith - battle 2')
  t = song.track('pad_juno', program=89)              # the track name is the synth patch (see tools/audio/Synth/Patches.cs)
  t.note(beat, dur_beats, 'D3', vel=80)               # beats from the start of the piece; pitch as a name or a number
  t.cc(beat, 11, 90)                                  # a controller (0-127)
  t.bend(beat, 0.5)                                   # pitch bend -1..1
  d = song.drums()                                    # General MIDI drum map on channel 10
  d.hit(beat, 'kick', 100)
  song.write('audio_src/midi/battle2.mid')

Everything is deterministic: humanising uses a seeded random generator, so the same script writes the same file.
"""
import random
import struct

PPQ = 480

NAMES = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}

DRUM = {
    'boom': 33, 'taiko': 34, 'kick': 36, 'rim': 37, 'snare': 38, 'clap': 39, 'snare2': 40,
    'tom1': 41, 'tom2': 43, 'tom3': 45, 'tom4': 47, 'tom5': 48, 'tom6': 50,
    'hat': 42, 'hatp': 44, 'ohat': 46, 'crash': 49, 'ride': 51, 'splash': 55, 'crash2': 57, 'cow': 56,
}


def midi(p):
    """'D3' -> 50, 'Bb2' -> 46, 'F#4' -> 66, or an int passed through. C4 = 60."""
    if isinstance(p, int):
        return p
    s = p.strip()
    n = NAMES[s[0].upper()]
    i = 1
    while i < len(s) and s[i] in '#b':
        n += 1 if s[i] == '#' else -1
        i += 1
    octave = int(s[i:])
    return 12 * (octave + 1) + n


def vlq(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


class Track:
    def __init__(self, song, name, channel, program, drums=False):
        self.song, self.name, self.channel, self.program, self.drums = song, name, channel, program, drums
        self.events = []     # (tick, order, bytes)

    def _tick(self, beat):
        return max(0, int(round(beat * PPQ)))

    def note(self, beat, dur, pitch, vel=90, human=0.0):
        """human: seconds-ish of timing wobble in beats (0 for a sequencer)."""
        p = midi(pitch)
        b = beat
        if human > 0:
            b = max(0.0, beat + self.song.rng.uniform(-human, human))
            vel = int(max(1, min(127, vel + self.song.rng.randint(-4, 4))))
        on = self._tick(b)
        off = max(on + 1, self._tick(b + dur) - 1)
        vel = int(max(1, min(127, vel)))
        self.events.append((on, 1, bytes([0x90 | self.channel, p, vel])))
        self.events.append((off, 0, bytes([0x80 | self.channel, p, 0])))

    def chord(self, beat, dur, pitches, vel=80, human=0.0, spread=0.0):
        for i, p in enumerate(pitches):
            self.note(beat + i * spread, dur - i * spread, p, vel - (i % 2) * 3, human)

    def hit(self, beat, drum, vel=100, dur=0.1):
        self.note(beat, dur, DRUM[drum] if isinstance(drum, str) else drum, vel)

    def cc(self, beat, num, value):
        self.events.append((self._tick(beat), 2, bytes([0xB0 | self.channel, num, int(max(0, min(127, value)))])))

    def ramp(self, beat0, beat1, num, v0, v1, steps=None):
        """A controller ramp from v0 to v1 (0-127) as a series of points."""
        steps = steps or max(2, int((beat1 - beat0) * 4))
        for i in range(steps + 1):
            f = i / steps
            self.cc(beat0 + (beat1 - beat0) * f, num, v0 + (v1 - v0) * f)

    def bend(self, beat, value):
        v = int(max(0, min(16383, round(8192 + value * 8191))))
        self.events.append((self._tick(beat), 2, bytes([0xE0 | self.channel, v & 0x7F, v >> 7])))

    def data(self):
        out = bytearray()
        out += b'\x00\xFF\x03' + vlq(len(self.name)) + self.name.encode('ascii')
        if not self.drums:
            out += bytes([0x00, 0xC0 | self.channel, self.program])
        # a reset of the usual controllers so a DAW starts clean
        out += bytes([0x00, 0xB0 | self.channel, 7, 100, 0x00, 0xB0 | self.channel, 11, 127])
        last = 0
        for tick, order, b in sorted(self.events, key=lambda e: (e[0], e[1])):
            out += vlq(tick - last) + b
            last = tick
        end = max(last, int(self.song.beats * PPQ))
        out += vlq(end - last) + b'\xFF\x2F\x00'
        return bytes(out)


class Song:
    def __init__(self, bpm=112, title='', beats=None, seed=1):
        self.bpm, self.title, self.beats = bpm, title, beats or 0
        self.rng = random.Random(seed)
        self.tracks = []
        self._ch = 0

    def track(self, name, program=0, channel=None):
        if channel is None:
            channel = self._ch
            self._ch += 1
            if self._ch == 9:
                self._ch = 10
            if self._ch > 15:
                self._ch = 0
        t = Track(self, name, channel, program)
        self.tracks.append(t)
        return t

    def drums(self, name='drums'):
        t = Track(self, name, 9, 0, drums=True)
        self.tracks.append(t)
        return t

    def write(self, path):
        head = b'MThd' + struct.pack('>IHHH', 6, 1, len(self.tracks) + 1, PPQ)
        us = int(round(60e6 / self.bpm))
        cond = bytearray()
        title = (self.title or 'Monolith').encode('ascii', 'replace')
        cond += b'\x00\xFF\x03' + vlq(len(title)) + title
        cond += b'\x00\xFF\x58\x04\x04\x02\x18\x08'                    # 4/4
        cond += b'\x00\xFF\x51\x03' + struct.pack('>I', us)[1:]
        cond += b'\x00\xFF\x59\x02\xFF\x01'                            # D minor: one flat, minor
        end = int(self.beats * PPQ)
        cond += vlq(end) + b'\xFF\x2F\x00'
        body = b'MTrk' + struct.pack('>I', len(cond)) + bytes(cond)
        for t in self.tracks:
            d = t.data()
            body += b'MTrk' + struct.pack('>I', len(d)) + d
        with open(path, 'wb') as f:
            f.write(head + body)
