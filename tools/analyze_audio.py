"""Objective checks on the generated audio (stdlib only).

  python tools/analyze_audio.py

Nobody on this side of the screen can hear the result, so the claims the
synthesis makes are asserted numerically instead: harder impacts must actually
be brighter, the cue must not sound like a ball, cushions must not have a click
attack, and the rolling loop must have no tonal peak that would drone.
"""

from __future__ import annotations

import cmath
import math
import os
import sys
import wave

AUDIO = os.path.join(os.path.dirname(__file__), "..", "game", "assets", "audio")
BANDS = [63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]


def read(path: str) -> tuple[list[float], int]:
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
    return [int.from_bytes(raw[i:i + 2], "little", signed=True) / 32768.0
            for i in range(0, len(raw), 2)], sr


def goertzel(sig: list[float], freq: float, sr: int) -> float:
    """Energy at one frequency — cheaper than a full FFT for a coarse spectrum."""
    w = 2.0 * math.pi * freq / sr
    coef = 2.0 * math.cos(w)
    s1 = s2 = 0.0
    for x in sig:
        s0 = x + coef * s1 - s2
        s2, s1 = s1, s0
    return abs(complex(s1 - s2 * math.cos(w), s2 * math.sin(w))) / len(sig)


def spectrum(sig: list[float], sr: int, n: int = 44) -> list[tuple[float, float]]:
    lo, hi = 60.0, min(18000.0, sr * 0.45)
    out = []
    for i in range(n):
        f = lo * (hi / lo) ** (i / (n - 1))
        out.append((f, goertzel(sig, f, sr)))
    return out


def centroid(spec: list[tuple[float, float]]) -> float:
    num = sum(f * a for f, a in spec)
    den = sum(a for _, a in spec)
    return num / den if den > 0 else 0.0


def flatness(spec: list[tuple[float, float]]) -> float:
    mags = [a for _, a in spec if a > 1e-12]
    if not mags:
        return 0.0
    geo = math.exp(sum(math.log(m) for m in mags) / len(mags))
    ari = sum(mags) / len(mags)
    return geo / ari if ari > 0 else 0.0


def peak_over_median(spec: list[tuple[float, float]]) -> float:
    mags = sorted(a for _, a in spec)
    med = mags[len(mags) // 2]
    return 20.0 * math.log10(max(mags) / med) if med > 0 else 99.0


def rise_ms(sig: list[float], sr: int) -> float:
    """10%->90% rise of the envelope: a click has a near-zero rise, a soft
    cushion impact must not."""
    peak = max(abs(x) for x in sig)
    if peak <= 0:
        return 0.0
    lo = hi = None
    for i, x in enumerate(sig):
        a = abs(x)
        if lo is None and a >= 0.1 * peak:
            lo = i
        if a >= 0.9 * peak:
            hi = i
            break
    if lo is None or hi is None or hi < lo:
        return 0.0
    return (hi - lo) * 1000.0 / sr


def analyse(name: str, window_ms: float | None = None) -> dict:
    sig, sr = read(os.path.join(AUDIO, name))
    full = sig
    if window_ms is not None:
        sig = sig[:int(sr * window_ms / 1000.0)]
    spec = spectrum(sig, sr)
    return {
        "centroid": centroid(spec),
        "flatness": flatness(spec),
        "peak_over_median_db": peak_over_median(spec),
        "rise_ms": rise_ms(sig, sr),
        "peak": max(abs(x) for x in full),
        "dc": sum(full) / len(full),
        "seconds": len(full) / sr,
    }


def main() -> int:
    failures: list[str] = []

    def check(cond: bool, msg: str) -> None:
        print(("  ok   " if cond else "  FAIL ") + msg)
        if not cond:
            failures.append(msg)

    print("click tiers — a harder impact must be genuinely brighter, not just louder")
    cents = []
    for tier in range(3):
        a = analyse(f"click_{tier}_0.wav", window_ms=2.0)
        cents.append(a["centroid"])
        print(f"  tier {tier}: centroid {a['centroid']:7.0f} Hz  rise {a['rise_ms']:.3f} ms  peak {a['peak']:.2f}")
    check(cents[0] < cents[1] < cents[2], "click centroid rises monotonically with impact speed")

    print("cue strike — must sit far below the ball click (it is leather, not phenolic)")
    cue = analyse("cue_1_0.wav", window_ms=2.0)
    print(f"  centroid {cue['centroid']:7.0f} Hz  rise {cue['rise_ms']:.3f} ms")
    check(cue["centroid"] < cents[1] * 0.75, "cue is darker than a ball-ball click")

    print("cushion — rubber contact lasts milliseconds, so it must not attack like a click")
    for kind in ("rail", "jaw"):
        a = analyse(f"cushion_{kind}_1_0.wav")
        print(f"  {kind}: centroid {a['centroid']:7.0f} Hz  rise {a['rise_ms']:.2f} ms")
        check(a["rise_ms"] > 0.8, f"{kind} cushion has a soft attack, not a click")

    print("pocket — the catch is a soft leather thud")
    pc = analyse("pocket_catch_0.wav")
    print(f"  centroid {pc['centroid']:7.0f} Hz  rise {pc['rise_ms']:.2f} ms  {pc['seconds']:.2f} s")
    check(pc["rise_ms"] > 2.0, "pocket catch has a soft attack")

    print("loops — a tonal peak here would become a maddening drone")
    for name in ("roll_loop.wav", "slide_loop.wav"):
        a = analyse(name)
        print(f"  {name}: flatness {a['flatness']:.3f}  peak/median {a['peak_over_median_db']:.1f} dB")
        check(a["peak_over_median_db"] < 18.0, f"{name} has no dominant tonal peak")

    print("levels")
    for name in ("click_2_0.wav", "cue_0_0.wav", "ambience.wav"):
        a = analyse(name)
        print(f"  {name}: peak {a['peak']:.3f}  dc {a['dc']:+.5f}")
        check(a["peak"] <= 0.98, f"{name} does not clip")
        check(abs(a["dc"]) < 0.01, f"{name} has no DC offset")

    print()
    print("FAILURES:" if failures else "all audio checks passed")
    for f in failures:
        print("  -", f)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
