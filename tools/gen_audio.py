"""Synthesize placeholder game sounds (deterministic, re-runnable).

  python tools/gen_audio.py

Outputs 16-bit mono WAVs to game/assets/audio/:
  click.wav   - ball-ball impact (bright phenolic click)
  cushion.wav - rubber cushion thump
  pocket.wav  - ball dropping into the pocket
Real CC0 recordings can replace these later; the AudioManager only cares
about the filenames.
"""

from __future__ import annotations

import math
import os
import random
import struct
import wave

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "game", "assets", "audio")


def write_wav(name: str, samples: list[float]) -> None:
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    peak = max(1e-9, max(abs(s) for s in samples))
    scale = 0.92 / peak
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767)) for s in samples))
    print("wrote", os.path.normpath(path))


def click() -> list[float]:
    """Phenolic ball click: very short bright noise + high resonant partials."""
    rng = random.Random(11)
    n = int(SR * 0.055)
    out = []
    for i in range(n):
        t = i / SR
        env = math.exp(-t * 220.0)
        noise = (rng.random() * 2 - 1) * math.exp(-t * 900.0)
        tone = (
            0.7 * math.sin(2 * math.pi * 3400 * t)
            + 0.5 * math.sin(2 * math.pi * 5200 * t)
            + 0.3 * math.sin(2 * math.pi * 7600 * t)
        )
        out.append(env * (0.55 * noise + 0.45 * tone))
    return out


def cushion() -> list[float]:
    """Rubber cushion: soft low thump with a quick decay."""
    rng = random.Random(23)
    n = int(SR * 0.10)
    out = []
    for i in range(n):
        t = i / SR
        env = math.exp(-t * 70.0)
        tone = math.sin(2 * math.pi * 130 * t) + 0.4 * math.sin(2 * math.pi * 210 * t)
        noise = (rng.random() * 2 - 1) * math.exp(-t * 500.0) * 0.3
        out.append(env * (tone * 0.8 + noise))
    return out


def pocket() -> list[float]:
    """Pocket drop: knock into the leather + short rattle tail."""
    rng = random.Random(37)
    n = int(SR * 0.28)
    out = []
    for i in range(n):
        t = i / SR
        knock = math.exp(-t * 55.0) * (
            math.sin(2 * math.pi * 95 * t) + 0.5 * math.sin(2 * math.pi * 160 * t))
        rattle = 0.0
        for k, delay in enumerate((0.08, 0.14, 0.19)):
            if t > delay:
                td = t - delay
                rattle += math.exp(-td * 260.0) * math.sin(2 * math.pi * (2600 - 300 * k) * td) * (0.25 - 0.06 * k)
        noise = (rng.random() * 2 - 1) * math.exp(-t * 220.0) * 0.15
        out.append(knock * 0.9 + rattle + noise)
    return out


if __name__ == "__main__":
    write_wav("click.wav", click())
    write_wav("cushion.wav", cushion())
    write_wav("pocket.wav", pocket())
