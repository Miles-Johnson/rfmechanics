"""Render seated Ore-Song: original synthesis with no imported game samples."""
from __future__ import annotations
import argparse
from dataclasses import dataclass
from pathlib import Path
import subprocess
import numpy as np
from scipy.signal import butter, sosfilt

RATE = 44100
DURATION = 4.2
OUTPUT = Path(__file__).resolve().parents[2] / "assets/rfmechanics/sounds/oresong"


@dataclass(frozen=True)
class Voice:
    fundamental: float
    ratios: tuple[float, ...]
    strikes: tuple[float, ...]
    decay: float
    grit: float
    pulse: float


# Identity: attack rhythm, partial spacing, decay and texture, not pitch alone.
VOICES = {
    "iron": Voice(110, (1, 2, 3.01, 4.2, 6.3), (0, .78, 1.56), 1.15, .16, .8),
    "nativecopper": Voice(146.83, (1, 2, 3, 4.05), (0, .18, 1.65, 1.83), .9, .07, .4),
    "cassiterite": Voice(220, (1, 2.7, 4.15, 6.8), (0, .14, .42, 1.6, 1.74, 2.02), .4, .14, 0),
    "nativegold": Voice(164.81, (1, 2, 3, 5), (0,), 1.9, .025, .35),
    "nativesilver": Voice(329.63, (1, 2, 4, 6.02), (0, .6, 1.2), 1.1, .025, 2.2),
    "galena": Voice(98, (1, 1.48, 2.18, 3.3), (0, 1.25), .7, .65, 1.1),
    "coal": Voice(80, (1, 2.4), (0, .31, 1.13, 1.85, 2.4), .55, 1.4, 1.7),
    "sphalerite": Voice(196, (1, 1.025, 2.65, 4.1), (0, .42, 1.4, 1.82), .8, .18, 3.1),
    "bismuthinite": Voice(174.61, (1, 1.5, 2.25, 3.4), (0, .24, .72, 1.8), .65, .24, .6),
    "chromite": Voice(130.81, (1, 2.15, 3.8, 5.9), (0, .95), .7, .4, 0),
    "ilmenite": Voice(123.47, (1, 1.8, 3.3, 4.7), (0, .28, 1.5, 1.78), .6, .32, .9),
    "quartz": Voice(440, (1, 1.414, 2.31, 3.77, 5.2), (0, .13, .47, 1.09, 1.8, 2.16), .36, .06, 0),
    "diamond": Voice(523.25, (1, 2, 4, 6), (0, .55, 1.1), 1.1, .008, .5),
    "emerald": Voice(392, (1, 1.5, 2, 3), (0, .28, 1.25, 1.53), 1.0, .02, .7),
    "olivine": Voice(349.23, (1, 2, 2.5, 4), (0, .65, .88), .95, .04, 1.2),
    "unknown": Voice(155, (1, 1.37, 2.63), (0, .39, 1.67), .55, .5, .45),
}


def band_noise(rng, count, low, high):
    noise = sosfilt(butter(2, (low, high), btype="bandpass", fs=RATE, output="sos"), rng.normal(size=count))
    return noise / max(float(np.std(noise)), 1e-6)


def normalize(signal, rms=.095):
    signal = signal - np.mean(signal)
    signal *= rms / max(float(np.sqrt(np.mean(signal * signal))), 1e-9)
    signal = .72 * np.tanh(signal / .72)
    assert np.all(np.isfinite(signal)) and np.max(np.abs(signal)) <= .72
    return signal.astype("<f4")


def render_voice(voice, variation, clear, seed):
    rng = np.random.default_rng(seed + variation * 101)
    time = np.arange(round(DURATION * RATE)) / RATE
    signal = np.zeros_like(time)
    frequency = voice.fundamental * (1 + (variation - 1) * .0025)
    # Rough/clear share phase and timing so crossfading preserves the identifying motif.
    phases = rng.uniform(-.08, .08, len(voice.ratios))
    texture = band_noise(rng, len(time), 150 if voice.grit > 1 else 230, 650 if voice.grit > 1 else 2100)
    for number, strike in enumerate(voice.strikes):
        local = np.maximum(0, time - strike)
        active = time >= strike
        attack = 1 - np.exp(-local / (.045 if voice.decay > 1.5 else .009))
        envelope = attack * np.exp(-local / voice.decay) * active
        level = 1 if number == 0 else .68
        for harmonic, ratio in enumerate(voice.ratios):
            weight = 1 / (harmonic + 1) ** (1.1 if clear else 1.7)
            decay = np.exp(-local * harmonic * (.22 if clear else .5))
            signal += level * weight * envelope * decay * np.sin(2 * np.pi * frequency * ratio * local + phases[harmonic])
        grit = voice.grit + (0 if clear else .18)
        signal += level * grit * texture * envelope * (.11 if clear else .20)
        signal += texture * active * (1 - np.exp(-local / .002)) * np.exp(-local / .025) * .07 * level
    if voice.pulse:
        signal *= .92 + .08 * np.cos(2 * np.pi * voice.pulse * time)
    signal *= np.clip((DURATION - time) / .65, 0, 1)
    signal[:220] *= np.linspace(0, 1, 220)
    return normalize(signal)


def render_contact(knock):
    duration = .45 if knock else 2.0
    time = np.arange(round(duration * RATE)) / RATE
    rng = np.random.default_rng(902 if knock else 903)
    texture = band_noise(rng, len(time), 170, 1700 if knock else 650)
    signal = texture * .35 + np.sin(2 * np.pi * 135 * time) + .45 * np.sin(2 * np.pi * 310 * time)
    signal *= (1 - np.exp(-time / (.002 if knock else .08))) * np.exp(-time / (.055 if knock else .55))
    signal *= np.clip((duration - time) / .15, 0, 1)
    return normalize(signal, .11 if knock else .065)


def write(name, signal):
    OUTPUT.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "f32le", "-ar", str(RATE),
                    "-ac", "1", "-i", "pipe:0", "-c:a", "libvorbis", "-q:a", "4", str(OUTPUT / (name + ".ogg"))],
                   input=signal.tobytes(), check=True)


def check(names):
    peaks, levels = [], []
    for name in names:
        path = OUTPUT / (name + ".ogg")
        metadata = subprocess.check_output(["ffprobe", "-v", "error", "-show_entries", "stream=channels,sample_rate,duration",
                                            "-of", "csv=p=0", str(path)], text=True).strip().split(",")
        assert metadata[0:2] == [str(RATE), "1"], (name, metadata)
        duration = .45 if name == "knock" else 2 if name == "stone" else DURATION
        assert abs(float(metadata[2]) - duration) < .01, (name, metadata)
        decoded = subprocess.check_output(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "pipe:1"])
        signal = np.frombuffer(decoded, dtype="<f4")
        peak = float(np.max(np.abs(signal)))
        rms = float(np.sqrt(np.mean(signal * signal)))
        assert np.all(np.isfinite(signal)) and .01 < peak < .95, (name, peak)
        if name not in ("knock", "stone"):
            peaks.append(peak)
            levels.append(rms)
    print(f"Verified {len(names)} mono 44.1kHz assets; maximum voice peak {max(peaks, default=0):.3f}; "
          f"voice RMS range {min(levels, default=0):.3f}..{max(levels, default=0):.3f}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", help="Comma-separated material names")
    parser.add_argument("--check", action="store_true", help="Verify existing assets without writing")
    args = parser.parse_args()
    selected = set(args.only.split(",")) if args.only else set(VOICES)
    if selected - VOICES.keys():
        parser.error("Unknown materials: " + ", ".join(sorted(selected - VOICES.keys())))
    names = []
    for number, (name, voice) in enumerate(VOICES.items()):
        if name not in selected:
            continue
        for variation in range(3):
            for clear in (False, True):
                stem = f"{name}-{variation}-{'clear' if clear else 'rough'}"
                names.append(stem)
                if not args.check:
                    write(stem, render_voice(voice, variation, clear, 1700 + number * 1000))
    if not args.only:
        for name in ("knock", "stone"):
            names.append(name)
            if not args.check:
                write(name, render_contact(name == "knock"))
    check(names)


if __name__ == "__main__":
    main()
