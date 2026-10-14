#!/usr/bin/env python3
"""Synthesises AVAPaperboy's sound effects and tunes into ../src/AVAPaperboy/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPaperboy", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Papers.
    write("Throw.wav", tone(0.14, 900, 2400, "noise", release=0.8), 0.25)
    write("Mailbox.wav", cat(mix(tone(0.05, 600, 300, "noise", release=1.0), tone(0.06, 330, 300, "square", release=0.8, gain=0.5)),
                             tone(0.12, hz("E6"), kind="tri", release=0.6), tone(0.2, hz("A6"), kind="tri", release=0.8)), 0.4)
    write("Door.wav", cat(*[mix(tone(0.05, 220, 140, "square", release=1.0), tone(0.04, 800, 200, "noise", release=1.0, gain=0.5)) + silence(0.07) for _ in range(2)]), 0.4)
    write("Glass.wav", mix(tone(0.4, 5200, 2600, "noise", release=0.9), *[tone(0.35, f, kind="tri", release=1.2, gain=0.3) for f in [2900, 3700, 4400, 5100]]), 0.35)
    write("Thud.wav", mix(tone(0.08, 140, 70, "square", release=1.0), tone(0.06, 500, 150, "noise", release=1.0, gain=0.6)), 0.35)
    # Riding.
    write("Crash.wav", cat(mix(tone(0.15, 2400, 600, "noise", release=0.6), tone(0.12, 300, 100, "square", release=0.8)),
                           mix(tone(0.6, 600, 80, "noise", release=0.5), tone(0.3, 3000, 2000, "tri", release=0.9, gain=0.2))), 0.55)
    write("Bell.wav", cat(*[mix(tone(0.22, 2100, kind="tri", release=0.8), tone(0.22, 2650, kind="tri", release=1.1, gain=0.5)) for _ in range(2)]), 0.4)
    write("Bark.wav", cat(*[mix(tone(0.09, 520, 300, "saw", release=0.6), tone(0.09, 900, 400, "noise", release=0.8, gain=0.4)) + silence(0.1) for _ in range(2)]), 0.45)
    write("Yelp.wav", tone(0.3, 1400, 700, "square", release=0.5, vibrato=0.08, vib_rate=24), 0.35)
    write("Horn.wav", mix(tone(0.45, 350, kind="square", attack=0.01, release=0.15), tone(0.45, 440, kind="square", attack=0.01, release=0.15)), 0.3)
    write("Pickup.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6"]]), 0.35)
    write("Ramp.wav", tone(0.35, 200, 700, "saw", release=0.4, gain=0.6), 0.3)
    write("Land.wav", mix(tone(0.12, 120, 60, "square", release=0.9), tone(0.12, 600, 150, "noise", release=0.9, gain=0.6)), 0.35)
    write("Target.wav", cat(tone(0.05, 300, 200, "noise", release=1.0), tone(0.25, hz("G6"), kind="pulse", release=0.6)), 0.35)
    write("Wind.wav", tone(1.6, 300, 900, "noise", attack=0.4, release=0.5, vibrato=0.3, vib_rate=2), 0.3)
    # The end of the day.
    write("Cancel.wav", cat(tone(0.3, hz("E4"), kind="square", release=0.3), tone(0.6, hz("C4"), kind="square", release=0.6)), 0.35)
    write("Subscribe.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6", "G6", "C7"]]), 0.4)
    write("Finish.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.3) for n in ["C5", "C5", "G5"]], tone(0.5, hz("C6"), kind="pulse", release=0.6)), 0.45)
    write("DayEnd.wav", tune(140, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 2)], [("C3", 2), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)

    # Riding: a bouncy major-key whistle tune over an oom-pah bass, with a light snare (new). Loops.
    bassline = []
    for root, fifth in [("C3", "G2"), ("C3", "G2"), ("F2", "C3"), ("G2", "D3"), ("C3", "G2"), ("A2", "E3"), ("F2", "C3"), ("G2", "D3")] * 2:
        bassline += [(root, 0.5), (fifth, 0.5), (root, 0.5), (fifth, 0.5)]
    lead = [("E5", 0.5), ("G5", 0.5), ("C6", 1), ("B5", 0.5), ("A5", 0.5), ("G5", 1), ("A5", 0.5), ("F5", 0.5), ("D5", 1), ("G5", 1), ("-", 1),
            ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("D6", 0.5), ("C6", 0.5), ("A5", 1), ("F5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 2),
            ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 0.5), ("A5", 1), ("C6", 0.5), ("A5", 0.5), ("G5", 1), ("E5", 1), ("-", 1),
            ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("D5", 0.5), ("B4", 0.5), ("C5", 2), ("-", 2)]
    bass = seq(150, bassline, kind="tri", gain=0.5, release=0.4)
    top = seq(150, lead, kind="pulse", gain=0.25, release=0.5)
    beat = 60.0 / 150
    drums = cat(*[silence(beat) + mix(tone(0.07, 5000, 2500, "noise", release=1.0, gain=0.3), silence(beat)) for _ in range(int(len(bass) / RATE / beat / 2))])
    write("Music.wav", mix(bass, top, drums), 0.42)

    write("TitleTune.wav", tune(132,
                                [("C5", 0.5), ("E5", 0.5), ("G5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("G5", 2),
                                 ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("C6", 0.5), ("B5", 0.5), ("A5", 1), ("G5", 0.5), ("D5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 2)],
                                [("C3", 2), ("A2", 2), ("F2", 2), ("G2", 2), ("C3", 2), ("F2", 2), ("G2", 2), ("C3", 2)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1.5), ("E5", 0.5), ("G5", 2)], [("C3", 2), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(84, [("G5", 1), ("F5", 1), ("E5", 1), ("D5", 1), ("C5", 3)], [("C4", 2), ("A3", 2), ("G3", 1), ("C3", 3)], kind="tri"), 0.45)
    write("Victory.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5), ("G6", 2), ("E6", 0.5), ("C6", 2)],
                              [("C3", 1), ("G2", 1), ("C3", 1), ("E3", 1), ("G3", 2), ("C3", 2)], kind="pulse"), 0.45)


if __name__ == "__main__":
    main()
