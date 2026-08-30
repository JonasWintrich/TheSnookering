"""Synthesize the game's sound effects (deterministic, stdlib only).

  python tools/gen_audio.py

Why the old version sounded fake: it modelled a billiard ball as a resonator
with audible partials at 3.4/5.2/7.6 kHz. A phenolic ball's lowest natural mode
is near 19 kHz — inaudible. What you actually hear is the radiated *time
derivative of the Hertzian contact force*, plus the table's response, plus the
room. That is what this generates.

Contact time follows Hertz theory, tau_c proportional to v^(-1/5), so a harder
hit is genuinely brighter rather than merely louder or pitch-shifted. Each
family is baked at three reference speeds ("tiers"); the game crossfades
between them.

Outputs 48 kHz 16-bit mono WAVs to game/assets/audio/.
"""

from __future__ import annotations

import math
import os
import random
import struct
import wave

SR = 48000
OUT = os.path.join(os.path.dirname(__file__), "..", "game", "assets", "audio")


# ----------------------------------------------------------------- primitives
def zeros(n: int) -> list[float]:
    return [0.0] * n


def modal(out: list[float], exciter: list[float], freq: float, q: float, amp: float) -> None:
    """Add a resonator (2-pole recurrence) driven by `exciter` into `out`."""
    if freq >= SR * 0.48:
        return
    r = math.exp(-math.pi * freq / (q * SR))
    coef = 2.0 * r * math.cos(2.0 * math.pi * freq / SR)
    rr = r * r
    y1 = y2 = 0.0
    for i, x in enumerate(exciter):
        y = coef * y1 - rr * y2 + x
        y2, y1 = y1, y
        out[i] += amp * y


def bandpass(sig: list[float], f0: float, q: float) -> list[float]:
    """RBJ constant-skirt bandpass."""
    w0 = 2.0 * math.pi * f0 / SR
    alpha = math.sin(w0) / (2.0 * q)
    b0, b1, b2 = q * alpha, 0.0, -q * alpha
    a0, a1, a2 = 1.0 + alpha, -2.0 * math.cos(w0), 1.0 - alpha
    b0, b1, b2 = b0 / a0, b1 / a0, b2 / a0
    a1, a2 = a1 / a0, a2 / a0
    out = zeros(len(sig))
    x1 = x2 = y1 = y2 = 0.0
    for i, x in enumerate(sig):
        y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        out[i] = y
        x2, x1 = x1, x
        y2, y1 = y1, y
    return out


def lowpass(sig: list[float], f0: float) -> list[float]:
    a = math.exp(-2.0 * math.pi * f0 / SR)
    out = zeros(len(sig))
    y = 0.0
    for i, x in enumerate(sig):
        y = (1.0 - a) * x + a * y
        out[i] = y
    return out


def noise(n: int, rng: random.Random) -> list[float]:
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def velvet(n: int, density: float, rng: random.Random) -> list[float]:
    """Sparse +-1 impulses: reads as texture, where white noise reads as hiss."""
    out = zeros(n)
    step = max(1, int(SR / density))
    for base in range(0, n, step):
        idx = base + rng.randrange(step)
        if idx < n:
            out[idx] = 1.0 if rng.random() < 0.5 else -1.0
    return out


def hertz_pulse(tau: float, n: int) -> list[float]:
    """Radiated shape of a Hertzian impact: dF/dt over the contact window.

    Bipolar, starts at zero with a steep but finite-energy edge, ends exactly at
    zero when the bodies separate. That shape *is* the crispness of a real click.
    """
    out = zeros(n)
    span = min(n, int(tau * SR))
    for i in range(span):
        x = math.pi * (i / (tau * SR))
        s = math.sin(x)
        if s <= 0.0:
            continue
        out[i] = math.sqrt(s) * math.cos(x)
    return out


def attack_window(sig: list[float], ms: float) -> None:
    """Raised-cosine fade-in, so soft impacts have no artificial click."""
    n = int(SR * ms / 1000.0)
    for i in range(min(n, len(sig))):
        sig[i] *= 0.5 - 0.5 * math.cos(math.pi * i / n)


def decay(sig: list[float], rate: float) -> None:
    for i in range(len(sig)):
        sig[i] *= math.exp(-rate * i / SR)


def mix(dst: list[float], src: list[float], gain: float) -> None:
    for i in range(min(len(dst), len(src))):
        dst[i] += gain * src[i]


def norm(sig: list[float]) -> list[float]:
    """Scale to unit peak. Resonator banks accumulate ~Q times their input, so
    without this a nominal -25 dB layer can dominate the mix."""
    hi = max((abs(x) for x in sig), default=0.0)
    if hi <= 1e-12:
        return sig
    return [x / hi for x in sig]


def db(x: float) -> float:
    return 10.0 ** (x / 20.0)


def write_wav(name: str, samples: list[float], peak: float = 0.90,
              rms_db: float | None = None) -> None:
    """Transients are peak-normalised (they need the headroom); sustained beds
    are normalised by RMS, because that is what the ear actually weighs."""
    os.makedirs(OUT, exist_ok=True)
    hi = max(1e-9, max(abs(s) for s in samples))
    scale = peak / hi
    if rms_db is not None:
        rms = math.sqrt(sum(x * x for x in samples) / len(samples)) or 1e-9
        scale = min(scale, (10.0 ** (rms_db / 20.0)) / rms)
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767)) for s in samples))
    print("wrote", os.path.normpath(path))


def jitter(rng: random.Random, value: float, frac: float) -> float:
    return value * (1.0 + rng.uniform(-frac, frac))


# ----------------------------------------------------------------- table body
# Every impact shakes the slate and cabinet a little. Without it the sounds feel
# like they happen in a vacuum; it is the cheapest "this is a real object" cue.
TABLE_MODES = [(118.0, 6.0, 1.0), (176.0, 7.0, 0.72), (243.0, 5.0, 0.55), (385.0, 4.0, 0.30)]


def add_table(out: list[float], exciter: list[float], rng: random.Random, gain: float) -> None:
    body = lowpass(exciter, 500.0)
    layer = zeros(len(out))
    for f, q, a in TABLE_MODES:
        modal(layer, body, jitter(rng, f, 0.018), jitter(rng, q, 0.15), a)
    mix(out, norm(layer), gain)


# ----------------------------------------------------------------- families
CLICK_TIERS = [0.6, 2.2, 6.0]
CUE_TIERS = [1.5, 4.0, 7.5]
CUSHION_TIERS = [0.8, 2.5, 5.5]


def click(v: float, rng: random.Random, ball_mode: float = 19030.0) -> list[float]:
    n = int(SR * 0.26)
    tau = jitter(rng, 327.7e-6 * v ** -0.2, 0.04)
    out = zeros(n)

    pulse = hertz_pulse(tau, n)
    mix(out, pulse, 1.0)

    # Contact scuff: the texture of two surfaces rubbing during the contact.
    scuff = bandpass(noise(n, rng), jitter(rng, 2400.0, 0.12), 1.1)
    for i in range(n):
        x = math.pi * (i / (tau * SR))
        scuff[i] *= (math.sin(x) ** 2) if 0.0 <= x <= math.pi else 0.0
    mix(out, norm(scuff), db(jitter(rng, -17.0, 0.15)))

    if v > 4.0:
        crack = bandpass(noise(n, rng), 7000.0, 0.8)
        for i in range(n):
            x = math.pi * (i / (tau * SR))
            crack[i] *= (math.sin(x) ** 2) if 0.0 <= x <= math.pi else 0.0
        mix(out, norm(crack), db(-24.0) * ((v / 4.0 - 1.0) ** 0.7))

    add_table(out, pulse, rng, db(-20.0))

    # The ball's own mode really is up at ~19 kHz — a faint glassy sheen only.
    ring = zeros(n)
    for detune in (0.992, 1.0, 1.009):
        modal(ring, pulse, ball_mode * detune, 900.0, 1.0)
    mix(out, norm(ring), db(-26.0))
    return out


def cue(v: float, rng: random.Random) -> list[float]:
    """Leather tip on ball: ~6x longer contact than ball-ball, so it is a soft
    wooden pock, not a click. Using the ball click here was the single most
    wrong sound in the game — it plays on every shot."""
    n = int(SR * 0.18)
    tau = jitter(rng, 1.75e-3 * v ** -0.18, 0.05)
    out = zeros(n)

    pulse = hertz_pulse(tau, n)
    mix(out, pulse, 1.0)

    shaft = zeros(n)
    for f, q, a in ((1380.0, 28.0, 1.0), (2760.0, 18.0, 0.42), (4140.0, 12.0, 0.18)):
        modal(shaft, pulse, jitter(rng, f, 0.01), q, a)
    mix(out, norm(shaft), db(-8.0))

    thump = zeros(n)
    for f, q, a in ((188.0, 9.0, 1.0), (262.0, 7.0, 0.6)):
        modal(thump, pulse, f, q, a)
    mix(out, norm(thump), db(-14.0))

    chalk = bandpass(noise(n, rng), 4200.0, 0.9)
    for i in range(n):
        x = math.pi * (i / (tau * SR))
        chalk[i] *= (math.sin(x) ** 1.5) if 0.0 <= x <= math.pi else math.exp(-i / (SR * 0.003))
    mix(out, norm(chalk), db(jitter(rng, -20.0, 0.25)))

    add_table(out, pulse, rng, db(-24.0))
    return out


RAIL_MODES = [(96.0, 14.0, 1.0), (142.0, 12.0, 0.86), (187.0, 11.0, 0.70), (251.0, 9.0, 0.62),
              (310.0, 8.0, 0.44), (415.0, 7.0, 0.33), (620.0, 6.0, 0.20), (880.0, 5.0, 0.12)]


def cushion(v: float, rng: random.Random, jaw: bool) -> list[float]:
    """Rubber is three orders softer than phenolic: contact lasts milliseconds,
    so this is a low thump with cloth scuff — and it must NOT have a click
    attack, which is what the old two-sine version had."""
    n = int(SR * 0.28)
    tau_ms = jitter(rng, 7.0 * v ** -0.14, 0.06)
    out = zeros(n)

    exciter = zeros(n)
    span = int(SR * tau_ms / 1000.0)
    for i in range(span):
        exciter[i] = math.sin(math.pi * i / span)

    body = zeros(n)
    for f, q, a in RAIL_MODES:
        modal(body, exciter, jitter(rng, f, 0.018), jitter(rng, q, 0.15), a)
    mix(out, norm(body), 1.0)

    box = zeros(n)
    for f, q, a in ((108.0, 4.0, 1.0), (163.0, 3.5, 0.7)):
        modal(box, exciter, f, q, a)
    mix(out, norm(box), db(-9.0))

    scuff = bandpass(noise(n, rng), jitter(rng, 2000.0, 0.15), 0.9)
    for i in range(n):
        scuff[i] *= exciter[i] if i < span else 0.0
    mix(out, norm(scuff), db(jitter(rng, -11.0, 0.2)))

    if jaw:
        # Jaws are short stiff stubs bolted at the pocket — they knock.
        knock = zeros(n)
        for f, q, a in ((720.0, 16.0, 1.0), (1180.0, 13.0, 0.6), (1650.0, 10.0, 0.35)):
            modal(knock, exciter, jitter(rng, f, 0.02), q, a)
        mix(out, norm(knock), db(-6.0))

    attack_window(out, tau_ms * 0.8)
    return out


def pocket_catch(rng: random.Random, net: bool) -> list[float]:
    """The ball leaving the table: it falls ~0.14 s onto leather or into a net."""
    n = int(SR * 0.55)
    out = zeros(n)

    exciter = zeros(n)
    span = int(SR * 0.008)
    for i in range(span):
        exciter[i] = math.sin(math.pi * i / span)

    thud = zeros(n)
    for f, q, a in ((74.0, 5.0, 1.0), (118.0, 4.0, 0.7), (165.0, 3.5, 0.45)):
        modal(thud, exciter, jitter(rng, f, 0.03), q, a)
    mix(out, norm(thud), 1.0)

    creak = bandpass(noise(n, rng), 1500.0, 0.7)
    decay(creak, 22.0)
    mix(out, norm(creak), db(-13.0))

    rustle = bandpass(noise(n, rng), 4500.0, 0.6)
    for i in range(n):
        t = i / SR
        swing = sum(math.exp(-((t - d) ** 2) / 0.004) for d in (0.0, 0.19, 0.34))
        rustle[i] *= swing
    mix(out, norm(rustle), db(-19.0 if not net else -14.0))

    attack_window(out, 8.0)
    return out


def pocket_return(rng: random.Random) -> list[float]:
    """The ball rolling down the return channel and clacking home. This tail is
    what makes a recording read as 'pool hall' rather than 'sound effect'."""
    n = int(SR * 2.4)
    out = zeros(n)

    rumble = bandpass(velvet(n, 3000.0, rng), 220.0, 0.5)
    for i in range(n):
        t = i / SR
        env = math.exp(-((t - 0.55) ** 2) / 0.28) * (1.0 + 0.25 * math.sin(2 * math.pi * 6.0 * t))
        rumble[i] *= env
    mix(out, norm(rumble), db(-6.0))

    for t0 in sorted(rng.uniform(0.35, 1.6) for _ in range(4)):
        start = int(t0 * SR)
        hit = click(1.2, rng)
        wood = zeros(len(hit))
        modal(wood, hit, 340.0, 8.0, 1.0)
        for i, x in enumerate(hit):
            if start + i < n:
                out[start + i] += 0.5 * x + 0.35 * wood[i]

    final = click(2.4, rng)
    start = int(rng.uniform(1.5, 2.0) * SR)
    for i, x in enumerate(final):
        if start + i < n:
            out[start + i] += 0.8 * x
    return out


def roll_loop(rng: random.Random, sliding: bool) -> list[float]:
    """Cloth texture at 1 m/s. The nap pitch sets the centroid, so the game can
    resample by v and stay physically honest."""
    length = 2.5
    n = int(SR * (length + 0.5))
    base = velvet(n, 4000.0, rng)
    centre = 4200.0 if sliding else 2000.0
    band = bandpass(base, centre * 0.55, 0.35)
    mix(band, bandpass(base, centre, 0.35), 0.9)
    mix(band, bandpass(base, centre * 1.9, 0.35), 0.7)
    if not sliding:
        bed = lowpass(noise(n, rng), 220.0)
        mix(band, bed, db(-10.0))

    # Seamless: crossfade the tail onto the head.
    keep = int(SR * length)
    fade = int(SR * 0.5)
    for i in range(fade):
        w = i / fade
        band[i] = band[i] * math.sqrt(w) + band[keep + i] * math.sqrt(1.0 - w)
    return band[:keep]


def ambience(rng: random.Random) -> list[float]:
    """Room tone plus indistinct bar murmur — 20 s, loops."""
    n = int(SR * 20.0)
    # A real room tone is weighted low. Mid-band noise reads as tape hiss, which
    # is exactly what "background static" sounds like, so roll it off hard.
    out = lowpass(lowpass(noise(n, rng), 260.0), 260.0)
    for i in range(n):
        out[i] *= 0.5 * (1.0 + 0.12 * math.sin(2 * math.pi * 0.07 * i / SR))

    # Formant-filtered noise bursts read as voices once they sit behind reverb.
    for _ in range(12):
        stream = noise(n, rng)
        f1 = rng.uniform(500, 800)
        f2 = rng.uniform(1100, 1800)
        voice = bandpass(stream, f1, 8.0)
        mix(voice, bandpass(stream, f2, 8.0), 0.6)
        rate = rng.uniform(3.0, 8.0)
        phase = rng.random() * 10.0
        for i in range(n):
            g = math.sin(2 * math.pi * rate * (i / SR + phase))
            voice[i] *= max(0.0, g) ** 2
        mix(out, lowpass(voice, 1400.0), db(rng.uniform(-34.0, -28.0)))
    return out


# ----------------------------------------------------------------- main
LOOPING = ("roll_loop.wav", "slide_loop.wav", "ambience.wav")


def mark_loops_pcm() -> None:
    """Godot imports .wav as lossy QOA by default and does not loop it.

    QOA stores compressed bytes, so any loop point computed from the byte length
    at runtime lands in the middle of compressed data and plays as static - which
    is exactly what happened. Import these three as raw PCM and let Godot loop
    them natively instead.
    """
    for name in LOOPING:
        path = os.path.join(OUT, name + ".import")
        if not os.path.exists(path):
            continue  # created on the next Godot import; re-run this after
        with open(path, encoding="utf-8") as f:
            text = f.read()
        text = text.replace("compress/mode=2", "compress/mode=0")
        text = text.replace("edit/loop_mode=0", "edit/loop_mode=1")
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)
        print("patched", os.path.normpath(path))


def main() -> None:
    for tier, v in enumerate(CLICK_TIERS):
        for variant in range(6):
            rng = random.Random(1000 + tier * 10 + variant)
            write_wav(f"click_{tier}_{variant}.wav", click(v, rng))

    for tier, v in enumerate(CUE_TIERS):
        for variant in range(4):
            rng = random.Random(2000 + tier * 10 + variant)
            write_wav(f"cue_{tier}_{variant}.wav", cue(v, rng))

    for kind, jaw in (("rail", False), ("jaw", True)):
        for tier, v in enumerate(CUSHION_TIERS):
            for variant in range(4):
                rng = random.Random(3000 + (0 if kind == "rail" else 500) + tier * 10 + variant)
                write_wav(f"cushion_{kind}_{tier}_{variant}.wav", cushion(v, rng, jaw), rms_db=-30.0)

    for variant in range(4):
        rng = random.Random(4000 + variant)
        write_wav(f"pocket_catch_{variant}.wav", pocket_catch(rng, net=False), rms_db=-28.0)
    for variant in range(3):
        rng = random.Random(4500 + variant)
        write_wav(f"pocket_net_{variant}.wav", pocket_catch(rng, net=True), rms_db=-30.0)
    for variant in range(3):
        rng = random.Random(5000 + variant)
        write_wav(f"pocket_return_{variant}.wav", pocket_return(rng), rms_db=-34.0)

    write_wav("roll_loop.wav", roll_loop(random.Random(6000), sliding=False), rms_db=-42.0)
    write_wav("slide_loop.wav", roll_loop(random.Random(6100), sliding=True), rms_db=-38.0)
    write_wav("ambience.wav", ambience(random.Random(7000)), rms_db=-54.0)
    mark_loops_pcm()


if __name__ == "__main__":
    main()
