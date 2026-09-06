"""Procedural synthesis of dwarf ore-song sound assets. See README.md."""
from __future__ import annotations

import argparse
import math
import os
import subprocess
import wave
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np
from scipy.signal import butter, sosfilt

SAMPLE_RATE = 44100
TAIL_FADE_S = 1.0
TARGET_DBFS = -3.0
HF_BAND = (2500.0, 6000.0)
GRIND_BAND = (800.0, 2500.0)
NOISE_BODY_BAND = (80.0, 450.0)

SCRIPT_DIR = Path(__file__).resolve().parent
WAV_DIR = SCRIPT_DIR / "wav"
OGG_DIR = (
    SCRIPT_DIR.parent.parent
    / "assets" / "rfmechanics" / "sounds" / "oresong"
)
VANILLA_SOUNDS = Path(os.environ.get("VINTAGE_STORY", r"C:\Games\Vintagestory")) / "assets" / "survival" / "sounds"


@dataclass
class Partial:
    ratio: float
    amp: float
    detune_hz: float = 0.0
    drift_hz: float = 0.0
    drift_ms: float = 0.0


@dataclass
class SampleLayer:
    """A real vanilla-game sound blended alongside the synthesized layers."""
    path: Path
    pitch: float = 1.0   # resample ratio: >1 = higher & shorter, <1 = lower & longer
    gain: float = 0.4
    mode: str = "transient"  # "transient" = short hit at t=0; "bed" = tiled under the sustain


@dataclass
class MaterialSpec:
    name: str
    f0: float
    partials: List[Partial]
    duration_s: float
    tremolo_hz: float
    tremolo_depth: float
    hf_amp: float
    sustain_level: float = 0.35
    transient_ms: float = 3.0
    strike_decay_ms: float = 250.0
    decay_multiplier: float = 1.0
    noise_body_amp: float = 0.0
    noise_body_band: Tuple[float, float] = NOISE_BODY_BAND
    output_gain: float = 1.0
    seed: int = 0
    grind_amp: float = 0.0
    formant_hz: float = 0.0
    transient_band: Tuple[float, float] = (800.0, 4000.0)
    scatter: float = 0.0
    grate_hz: float = 0.0
    grate_depth: float = 0.0
    sample: Optional[SampleLayer] = None


def stage_boundaries(spec: MaterialSpec) -> dict:
    n1 = int(spec.transient_ms / 1000.0 * SAMPLE_RATE)
    n2 = n1 + int(spec.strike_decay_ms / 1000.0 * SAMPLE_RATE)
    ntotal = int(spec.duration_s * SAMPLE_RATE)
    n3 = ntotal - int(TAIL_FADE_S * SAMPLE_RATE)
    assert n3 > n2, (
        f"{spec.name}: sustain window is non-positive (n3={n3} <= n2={n2}); "
        f"duration_s too short for transient_ms + strike_decay_ms + tail fade"
    )
    return {"n1": n1, "n2": n2, "n3": n3, "ntotal": ntotal}


def per_partial_envelope(t: np.ndarray, ratio: float, spec: MaterialSpec, b: dict) -> np.ndarray:
    t1 = b["n1"] / SAMPLE_RATE
    t2 = b["n2"] / SAMPLE_RATE
    env = np.empty_like(t)

    attack_mask = t < t1
    if t1 > 0:
        env[attack_mask] = 0.5 * (1.0 - np.cos(np.pi * t[attack_mask] / t1))
    else:
        env[attack_mask] = 1.0

    base_tau = max(spec.strike_decay_ms / 1000.0, 1e-4) / 3.0
    tau_i = max(base_tau / max(ratio, 0.05) * spec.decay_multiplier, 1e-4)
    strike_mask = (t >= t1) & (t < t2)
    env[strike_mask] = spec.sustain_level + (1.0 - spec.sustain_level) * np.exp(
        -(t[strike_mask] - t1) / tau_i
    )

    level_at_t2 = spec.sustain_level + (1.0 - spec.sustain_level) * np.exp(-(t2 - t1) / tau_i)
    sustain_span = max(b["n3"] - b["n2"], 1) / SAMPLE_RATE
    sustain_tau = max(sustain_span / 3.0 * spec.decay_multiplier, 1e-4)
    sustain_mask = t >= t2
    env[sustain_mask] = level_at_t2 * np.exp(-(t[sustain_mask] - t2) / sustain_tau)

    return env


def _bandpass_noise(n_samples: int, band: Tuple[float, float], rng: np.random.Generator) -> np.ndarray:
    noise = rng.standard_normal(n_samples)
    lo, hi = band
    sos = butter(4, [lo, hi], btype="bandpass", fs=SAMPLE_RATE, output="sos")
    return sosfilt(sos, noise)


def amplitude_modulate(signal: np.ndarray, t: np.ndarray, rate_hz: float, depth: float) -> np.ndarray:
    if depth <= 0.0 or rate_hz <= 0.0:
        return signal
    mult = 1.0 - depth * (0.5 + 0.5 * np.sin(2 * np.pi * rate_hz * t))
    return signal * mult


def synthesize_transient(spec: MaterialSpec, t: np.ndarray, rng: np.random.Generator, b: dict) -> np.ndarray:
    """Scatter (spec.scatter) splits the impact into several offset noise grains."""
    t1 = b["n1"] / SAMPLE_RATE
    pad_s = max(t1 * 4.0, 0.02)
    n_pad = int(pad_s * SAMPLE_RATE)

    local_t = np.arange(n_pad) / SAMPLE_RATE
    if t1 > 0:
        attack_env = np.where(
            local_t < t1, 0.5 * (1.0 - np.cos(np.pi * np.clip(local_t, 0, t1) / t1)), 1.0
        )
    else:
        attack_env = np.ones_like(local_t)
    decay_tau = max(t1 * 1.5, 0.002)
    decay_env = np.exp(-np.maximum(local_t - t1, 0.0) / decay_tau)
    local_env = np.minimum(attack_env, 1.0) * decay_env

    n_grains = 1 + int(spec.scatter / 25.0)
    spread_s = (spec.scatter / 100.0) * 0.04

    out = np.zeros_like(t)
    for _ in range(n_grains):
        offset_n = int(rng.uniform(0.0, spread_s) * SAMPLE_RATE) if spread_s > 0 else 0
        filtered = _bandpass_noise(n_pad, spec.transient_band, rng)
        shaped = filtered * local_env / math.sqrt(n_grains)
        start = offset_n
        end = min(start + len(shaped), len(out))
        seg = end - start
        if seg > 0:
            out[start:end] += shaped[:seg]
    return out


def synthesize_tonal_body(spec: MaterialSpec, t: np.ndarray, rng: np.random.Generator, b: dict) -> np.ndarray:
    out = np.zeros_like(t)
    for p in spec.partials:
        base_freq = spec.f0 * p.ratio + p.detune_hz
        if p.drift_hz != 0.0 and p.drift_ms > 0.0:
            drift_period_s = p.drift_ms / 1000.0
            inst_freq = base_freq + p.drift_hz * np.sin(2 * np.pi * t / drift_period_s)
            phase = 2 * np.pi * np.cumsum(inst_freq) / SAMPLE_RATE
            osc = np.sin(phase)
        else:
            osc = np.sin(2 * np.pi * base_freq * t)
        env = per_partial_envelope(t, p.ratio, spec, b)
        out += p.amp * env * osc
    return out


def synthesize_noise_body(spec: MaterialSpec, t: np.ndarray, rng: np.random.Generator, b: dict) -> np.ndarray:
    """Low-band rumble, amount driven by the 'grit' column."""
    if spec.noise_body_amp <= 0.0:
        return np.zeros_like(t)
    filtered = _bandpass_noise(len(t), spec.noise_body_band, rng)
    env = per_partial_envelope(t, 1.0, spec, b)
    return spec.noise_body_amp * env * filtered


def synthesize_grind_layer(spec: MaterialSpec, t: np.ndarray, rng: np.random.Generator, b: dict) -> np.ndarray:
    """Mid-high grinding noise, amount driven by 'grind'; roughened by 'grate'."""
    if spec.grind_amp <= 0.0:
        return np.zeros_like(t)
    filtered = _bandpass_noise(len(t), GRIND_BAND, rng)
    env = per_partial_envelope(t, 1.0, spec, b)
    layer = spec.grind_amp * env * filtered
    return amplitude_modulate(layer, t, spec.grate_hz, spec.grate_depth)


def synthesize_hf_layer(spec: MaterialSpec, t: np.ndarray, rng: np.random.Generator, b: dict) -> np.ndarray:
    """High shimmer/hiss, amount driven by 'noise'."""
    if spec.hf_amp <= 0.0:
        return np.zeros_like(t)
    filtered = _bandpass_noise(len(t), HF_BAND, rng)
    env = per_partial_envelope(t, 1.0, spec, b)
    return spec.hf_amp * env * filtered


def load_sample_mono(path: Path) -> np.ndarray:
    proc = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "f32le", "-"],
        check=True,
        capture_output=True,
    )
    return np.frombuffer(proc.stdout, dtype="<f4").astype(np.float64)


def resample_pitch(signal: np.ndarray, ratio: float) -> np.ndarray:
    """Naive resample pitch-shift: ratio>1 raises pitch and shortens, ratio<1 lowers and lengthens."""
    if ratio == 1.0 or len(signal) < 2:
        return signal
    n_out = max(int(len(signal) / ratio), 1)
    x_old = np.linspace(0.0, 1.0, len(signal), endpoint=False)
    x_new = np.linspace(0.0, 1.0, n_out, endpoint=False)
    return np.interp(x_new, x_old, signal)


def synthesize_sample_layer(spec: MaterialSpec, t: np.ndarray, b: dict) -> np.ndarray:
    if spec.sample is None:
        return np.zeros_like(t)
    raw = load_sample_mono(spec.sample.path)
    shifted = resample_pitch(raw, spec.sample.pitch)
    out = np.zeros_like(t)

    if spec.sample.mode == "bed":
        if len(shifted) == 0:
            return out
        reps = int(np.ceil(len(t) / len(shifted)))
        tiled = np.tile(shifted, reps)[: len(t)]
        env = per_partial_envelope(t, 1.0, spec, b)
        out = tiled * env
    else:  # "transient"
        n = min(len(shifted), len(out))
        fade = min(int(0.005 * SAMPLE_RATE), max(n // 4, 1))
        w = np.ones(n)
        if fade > 0:
            w[:fade] = np.linspace(0.0, 1.0, fade)
            w[-fade:] *= np.linspace(1.0, 0.0, fade)
        out[:n] = shifted[:n] * w

    return out * spec.sample.gain


def apply_formant(signal: np.ndarray, spec: MaterialSpec) -> np.ndarray:
    """Narrow resonance boost near formant_hz, blending the sample layer's color into the mix."""
    if spec.formant_hz <= 0.0:
        return signal
    lo = max(spec.formant_hz * 0.85, 40.0)
    hi = min(spec.formant_hz * 1.15, SAMPLE_RATE / 2.0 - 100.0)
    if hi <= lo:
        return signal
    sos = butter(2, [lo, hi], btype="bandpass", fs=SAMPLE_RATE, output="sos")
    resonance = sosfilt(sos, signal)
    return signal + 0.3 * resonance


def apply_tremolo(signal: np.ndarray, t: np.ndarray, spec: MaterialSpec) -> np.ndarray:
    return amplitude_modulate(signal, t, spec.tremolo_hz, spec.tremolo_depth)


def apply_tail_fade(signal: np.ndarray, b: dict) -> np.ndarray:
    out = signal.copy()
    n3, ntotal = b["n3"], b["ntotal"]
    fade_len = max(ntotal - n3, 1)
    fade = np.linspace(1.0, 0.0, fade_len)
    end = min(ntotal, len(out))
    out[n3:end] *= fade[: end - n3]
    if ntotal < len(out):
        out[ntotal:] = 0.0
    return out


def render_material_raw(spec: MaterialSpec) -> np.ndarray:
    b = stage_boundaries(spec)
    n = b["ntotal"]
    t = np.arange(n) / SAMPLE_RATE
    rng = np.random.default_rng(spec.seed)

    mix = (
        synthesize_transient(spec, t, rng, b)
        + synthesize_tonal_body(spec, t, rng, b)
        + synthesize_noise_body(spec, t, rng, b)
        + synthesize_grind_layer(spec, t, rng, b)
        + synthesize_hf_layer(spec, t, rng, b)
        + synthesize_sample_layer(spec, t, b)
    )
    mix = apply_formant(mix, spec)
    mix = apply_tremolo(mix, t, spec)
    mix = apply_tail_fade(mix, b)
    return mix


def write_wav(path: Path, signal: np.ndarray, sr: int = SAMPLE_RATE) -> None:
    pcm16 = (np.clip(signal, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm16.tobytes())


def encode_ogg(wav_path: Path, ogg_path: Path) -> None:
    subprocess.run(
        ["ffmpeg", "-y", "-i", str(wav_path), "-ac", "1", "-q:a", "5", str(ogg_path)],
        check=True,
        capture_output=True,
    )


# ---------------------------------------------------------------------------
# Material table (as given). Column order below must match each tuple.
# ---------------------------------------------------------------------------

COLUMNS = [
    "f0", "inharm", "modes", "mallet", "formant", "fq", "dur", "strike", "sustain",
    "jitter", "scatter", "ampvar", "beat", "grit", "grind", "grate", "noise", "trem",
]

#                     f0   inharm modes mallet formant  fq  dur  strike sustain jitter scatter ampvar beat grit grind grate noise trem
RAW_MATERIALS: Dict[str, Tuple[float, ...]] = {
    "galena":       (65,  80,   2,    8,    220,    15,  6.0,  180,   20,     40,    55,     70,    20,  30,  82,   1.1,  88,   2.0),
    "coal":         (80,  95,   1,    5,    300,     8,  6.0,  140,   16,     55,    70,     80,    10,  45,  90,   1.6,  95,   2.5),
    "nativegold":   (110,  8,   4,   18,    480,    35,  7.0,  300,   30,      8,    10,     20,    15,   5,  14,   0.8,  12,   3.0),
    "nativesilver": (145, 12,   5,   30,    700,    42,  7.0,  320,   33,     12,    14,     25,    22,   8,  12,   1.0,  10,   3.0),
    "nativecopper": (180, 38,   6,   45,    900,    55,  8.0,  380,   36,     22,    22,     38,    30,  16,  22,   1.4,  14,   3.5),
    "sphalerite":   (200, 55,   7,   38,   1100,    30,  8.0,  340,   34,     65,    40,     60,    85,  55,  38,   2.2,  30,   4.0),
    "cassiterite":  (290, 45,   6,   72,   2200,    60,  7.0,  200,   26,     30,     8,     55,    25,  22,  18,   2.6,  20,   4.5),
    "chromite":     (340, 62,   6,   85,   1600,    70,  7.0,  170,   28,     18,     6,     30,    18,  38,  26,   1.9,  16,   4.0),
    "iron":         (420, 48,   9,   65,   2600,    65,  9.0,  450,   45,     14,    16,     28,    40,  12,  10,   1.2,   8,   5.0),
    "quartzgem":    (620, 72,  16,   95,   3600,    80, 10.0,  500,   52,     48,    30,     65,    70,   6,   4,   3.4,   5,   6.0),
}

# Real vanilla-game textures blended in alongside the synthesized layers, chosen
# per material character. Experimental starting points — see README.md.
SAMPLE_LAYERS: Dict[str, SampleLayer] = {
    "galena":       SampleLayer(VANILLA_SOUNDS / "block/quern.ogg", pitch=0.55, gain=0.50, mode="bed"),
    "coal":         SampleLayer(VANILLA_SOUNDS / "block/charcoal2.ogg", pitch=0.80, gain=0.60, mode="transient"),
    "nativegold":   SampleLayer(VANILLA_SOUNDS / "effect/deepbell.ogg", pitch=1.00, gain=0.35, mode="bed"),
    "nativesilver": SampleLayer(VANILLA_SOUNDS / "effect/deepbell.ogg", pitch=1.35, gain=0.30, mode="bed"),
    "nativecopper": SampleLayer(VANILLA_SOUNDS / "block/heavymetal-hit.ogg", pitch=0.90, gain=0.40, mode="transient"),
    "sphalerite":   SampleLayer(VANILLA_SOUNDS / "block/glass.ogg", pitch=1.10, gain=0.35, mode="transient"),
    "cassiterite":  SampleLayer(VANILLA_SOUNDS / "block/rock-hit-pickaxe.ogg", pitch=1.00, gain=0.40, mode="transient"),
    "chromite":     SampleLayer(VANILLA_SOUNDS / "block/rock-break-pickaxe.ogg", pitch=0.95, gain=0.45, mode="transient"),
    "iron":         SampleLayer(VANILLA_SOUNDS / "block/anvil2.ogg", pitch=0.80, gain=0.45, mode="transient"),
    "quartzgem":    SampleLayer(VANILLA_SOUNDS / "walk/glass2.ogg", pitch=1.40, gain=0.40, mode="transient"),
}


def build_partials(
    f0: float, inharm: float, modes: int, mallet: float, fq: float,
    jitter: float, beat: float, rng: np.random.Generator,
) -> List[Partial]:
    n_modes = max(int(modes), 1)
    inharm_b = inharm / 20000.0          # piano-string-style inharmonicity coefficient
    tilt_p = min(max(1.5 - (mallet / 100.0) * 0.8 - (fq / 100.0) * 0.4, 0.3), 1.8)

    partials: List[Partial] = []
    for n in range(1, n_modes + 1):
        ratio = n * math.sqrt(1.0 + inharm_b * n * n)
        amp = 0.6 / (n ** tilt_p)
        freq = f0 * ratio
        if freq > 12000.0:  # soft-roll off partials pushed into harsh/ultrasonic range
            amp *= max(0.0, 1.0 - (freq - 12000.0) / 4000.0)
        detune = rng.uniform(-jitter / 15.0, jitter / 15.0) if jitter > 0 else 0.0
        drift_hz = (fq / 100.0) * 3.0 if (n >= 2 and fq > 0) else 0.0
        drift_ms = 600.0 if drift_hz > 0 else 0.0
        partials.append(Partial(ratio=ratio, amp=amp, detune_hz=detune, drift_hz=drift_hz, drift_ms=drift_ms))

    if beat > 0 and partials:
        partials.append(Partial(ratio=1.0, amp=partials[0].amp * 0.8, detune_hz=beat / 12.0))
    return partials


def build_material_spec(name: str, seed: int) -> MaterialSpec:
    row = dict(zip(COLUMNS, RAW_MATERIALS[name]))
    partial_rng = np.random.default_rng(seed)
    partials = build_partials(
        row["f0"], row["inharm"], row["modes"], row["mallet"], row["fq"],
        row["jitter"], row["beat"], partial_rng,
    )

    mallet = row["mallet"]
    transient_ms = 8.0 - (mallet / 100.0) * 6.5
    transient_band = (400.0 + mallet * 4.0, 1500.0 + mallet * 55.0)

    return MaterialSpec(
        name=name, f0=row["f0"], partials=partials, duration_s=row["dur"],
        tremolo_hz=row["trem"], tremolo_depth=min(row["ampvar"] / 100.0, 0.9),
        hf_amp=row["noise"] / 100.0 * 0.35,
        sustain_level=min(max(row["sustain"] / 100.0, 0.02), 0.9),
        transient_ms=transient_ms, strike_decay_ms=row["strike"],
        noise_body_amp=row["grit"] / 100.0 * 0.7, noise_body_band=NOISE_BODY_BAND,
        seed=seed,
        grind_amp=row["grind"] / 100.0 * 0.5, formant_hz=row["formant"],
        transient_band=transient_band, scatter=row["scatter"],
        grate_hz=20.0 + row["grate"] * 8.0, grate_depth=min(row["grate"] / 4.5, 0.7),
        sample=SAMPLE_LAYERS.get(name),
    )


MATERIALS: Dict[str, MaterialSpec] = {
    name: build_material_spec(name, seed) for seed, name in enumerate(RAW_MATERIALS, start=1)
}


def parse_only(arg: str) -> List[str]:
    names = [n.strip() for n in arg.split(",") if n.strip()]
    unknown = [n for n in names if n not in MATERIALS]
    if unknown:
        raise SystemExit(f"Unknown material(s): {', '.join(unknown)}. Valid: {', '.join(MATERIALS)}")
    return names


def main() -> None:
    parser = argparse.ArgumentParser(description="Synthesize dwarf ore-song sound assets.")
    parser.add_argument(
        "--only", type=str, default=None,
        help="Comma-separated material names to write (WAV+ogg). All materials are still "
             "rendered internally so group loudness normalization stays consistent.",
    )
    args = parser.parse_args()

    write_names = parse_only(args.only) if args.only else list(MATERIALS.keys())

    WAV_DIR.mkdir(parents=True, exist_ok=True)
    OGG_DIR.mkdir(parents=True, exist_ok=True)

    raw: Dict[str, np.ndarray] = {name: render_material_raw(spec) for name, spec in MATERIALS.items()}

    global_peak = max(float(np.max(np.abs(sig))) for sig in raw.values())
    if global_peak <= 0.0:
        raise SystemExit("All rendered signals are silent; check MATERIALS table.")
    common_scale = (10.0 ** (TARGET_DBFS / 20.0)) / global_peak

    for name in write_names:
        spec = MATERIALS[name]
        final = np.clip(raw[name] * common_scale * spec.output_gain, -1.0, 1.0)

        wav_path = WAV_DIR / f"oresong-{name}.wav"
        ogg_path = OGG_DIR / f"oresong-{name}.ogg"
        write_wav(wav_path, final)
        try:
            encode_ogg(wav_path, ogg_path)
        except subprocess.CalledProcessError as e:
            stderr = e.stderr.decode(errors="replace") if e.stderr else ""
            raise SystemExit(f"ffmpeg failed for {name}: {stderr}") from e
        print(f"wrote {ogg_path}")


if __name__ == "__main__":
    main()
