# oresong

Procedural synthesizer for the dwarf ore-song mechanic's sound assets. Renders
one mono `.ogg` per ore/gem material from a Python data table, with real
vanilla-game sound textures blended in as an extra layer per material.
These assets are used by RF Mechanics. Requires Python, NumPy, SciPy and ffmpeg.

## Run it

```
python render_oresong.py
```

Writes 16-bit WAVs to `wav/` (gitignored, intermediate) and the final mono
`.ogg` files to
`assets/rfmechanics/sounds/oresong/oresong-<material>.ogg`.

To retune a single material and re-encode only that file:

```
python render_oresong.py --only iron
python render_oresong.py --only iron,quartzgem
```

`--only` restricts which files get **written**, but every material is still
synthesized internally each run (cheap — pure numpy, no filesystem/ffmpeg
cost for the synthesis itself) so the group loudness normalization (see
below) always reflects the same 10-material reference, even on a
single-file retune. The vanilla-sample blend layer *is* decoded via ffmpeg
on every run for every material regardless of `--only`, since it feeds the
same normalization pass — still well under a second total.

## The MATERIALS table

`RAW_MATERIALS` in `render_oresong.py` is the editable surface — one
18-value tuple per material, columns defined in `COLUMNS`:

```
f0   inharm modes mallet formant  fq  dur  strike sustain jitter scatter ampvar beat grit grind grate noise trem
```

`build_material_spec()` turns each row into a `MaterialSpec` (partials,
envelope timing, layer amounts). Edit a tuple value, re-run, listen — no
need to touch the synthesis code for routine retuning.

| Column | Drives | Notes |
|---|---|---|
| `f0` | Fundamental (Hz) | |
| `inharm` | Partial stretching | Piano-string-style coefficient: `ratio(n) = n·√(1 + B·n²)`, `B = inharm/20000`. Low = harmonic/musical (metals), high = glassy/detuned (quartzgem) |
| `modes` | Partial count | 1–16 partials generated per material |
| `mallet` | Transient brightness + spectral tilt | Harder mallet (higher value) → shorter/brighter impact click (`transient_ms`, `transient_band`) and flatter partial amplitude falloff (brighter sustain) |
| `formant` | Resonance peak (Hz) | A narrow bandpass boost applied to the full mix — an approximate "body resonance" emphasis, also glues the sample layer's color into the synthesized tone |
| `fq` | Shimmer/drift + spectral tilt | Drives slow frequency drift on partials n≥2 (generalizes the old quartzgem-only chirp) and contributes to brightness alongside `mallet` |
| `dur` | Total file length (s) | Includes the 1s tail fade |
| `strike` | `strike_decay_ms` | Length of the fast post-transient decay |
| `sustain` | `sustain_level` (%) | Where strike-decay bottoms out before the slower sustain-decay takes over |
| `jitter` | Per-partial detune spread | Static per-partial mistuning (chorus-like richness), amount = `jitter/15` Hz, seeded per material |
| `scatter` | Transient grain count/spread | Splits the impact into 1–4 randomly-offset noise grains instead of one clean click — rougher, less percussive attack at high values |
| `ampvar` | `tremolo_depth` | Slow amplitude wobble depth (the "hard to pinpoint" cue) |
| `beat` | Detuned fundamental pair | Adds a second partial at ratio 1.0 detuned by `beat/12` Hz — audible beating, generalizes the old sphalerite-only mechanism to every material |
| `grit` | Low rumble noise (80–450Hz) | `noise_body_amp` |
| `grind` | Mid-high grinding noise (800–2500Hz) | New layer; roughened by `grate` |
| `grate` | Grind-layer roughness | Fast amplitude modulation on the grind layer only, rate `20 + grate·8` Hz — distinct from the slow `trem` wobble on the whole mix |
| `noise` | High shimmer/hiss (2.5–6kHz) | `hf_amp` |
| `trem` | `tremolo_hz` | Rate of the slow whole-mix wobble |

Partials pushed above 12kHz by a high `inharm`/`modes` combination (mostly
quartzgem) are soft-rolled off rather than left at full amplitude, so
"stretched" doesn't become "painful."

## Real-sample blending (new this pass)

`SAMPLE_LAYERS` maps each material to a vanilla `assets/survival/sounds/`
file, resampled (naive pitch-shift-via-resample, same mechanism as the
quern-at-pitch-0.5 idea from the original brief) and mixed in alongside the
synthesized layers, then run through the same `formant` resonance boost so
it picks up the material's tonal color instead of sitting on top of it.
Two blend modes:

- **`"transient"`** — the sample is pitched, given a short fade in/out, and
  placed at t=0 as an extra, more "real" impact layer on top of the
  synthesized click/grain transient.
- **`"bed"`** — the sample is pitched, tiled to fill the sustain window, and
  shaped by the same envelope as the tonal body — used where the source
  clip itself has a long, ring-y decay worth keeping (the quern grind, the
  cathedral-bell decay).

| Material | Source | Pitch | Mode | Why |
|---|---|---|---|---|
| galena | `block/quern.ogg` | 0.55 | bed | Original brief's own pick for galena; slowed further into a grinding-rock bed under the synth noise body |
| coal | `block/charcoal2.ogg` | 0.80 | transient | Crumbly break hit, deepened slightly |
| nativegold | `effect/deepbell.ogg` | 1.00 | bed | 7.2s stereo→mono resonant bell, almost exactly gold's 7s target — used near-native |
| nativesilver | `effect/deepbell.ogg` | 1.35 | bed | Same bell, pitched up for a brighter, silvery ring |
| nativecopper | `block/heavymetal-hit.ogg` | 0.90 | transient | Warm metal clang |
| sphalerite | `block/glass.ogg` | 1.10 | transient | Crystal shimmer to sit under the synthesized detuned-pair beating |
| cassiterite | `block/rock-hit-pickaxe.ogg` | 1.00 | transient | Dense mineral clunk |
| chromite | `block/rock-break-pickaxe.ogg` | 0.95 | transient | Denser, brittle-break character |
| iron | `block/anvil2.ogg` | 0.80 | transient | Literal anvil ring, deepened for weight |
| quartzgem | `walk/glass2.ogg` | 1.40 | transient | Bright glass tap on top of the stretched-partial shimmer |

These are a first-pass creative guess, explicitly meant to be experimented
with by ear — swap the `path`, `pitch`, `gain`, or `mode` for any material
and re-render with `--only <name>`. Other candidates worth trying from the
same asset tree: `block/anvil1.ogg`/`anvil3.ogg` (shorter/sharper anvil
hits), `block/heavymetal-hit2.ogg` (brighter metal), `block/loosestone1-4.ogg`
(looser rock clatter), `effect/stonecrush.ogg`, `walk/glass1.ogg`/`glass3.ogg`.

If a sample source moves or the game reinstalls elsewhere, this script
reads `VINTAGE_STORY` from the environment (same var documented in the
workspace's `CLAUDE.md`) and falls back to `C:\Games\Vintagestory`.

## Loudness hierarchy (group normalization, not per-file)

Files are **not** individually peak-normalized — that would erase the
loudness spread that falls naturally out of the table (galena/coal's high
`grit`/`grind`/`noise` values read as quiet-and-hard-to-place; low-noise
metals and the bright, high-`mallet` quartzgem read as present). Instead:
all 10 materials are rendered raw, the single loudest sample across all 10
is found, and one scale factor is computed so that peak lands at -3 dBFS.
Nothing else attenuates further — the hierarchy is entirely a product of
the table values now, not a separate manual dial.

## Playback design note

These files are pre-rendered and deterministic. Two simultaneous instances
of the same material (e.g. two copper veins firing at once) will be
phase-identical and comb-filter into sounding like a single source. The
ore-song design already plans random `SetPitch` jitter per played instance
for other reasons — but that jitter is now **load-bearing for
anti-comb-filtering**, not just cosmetic variation, and should be sized
(~±10%) with that in mind when it's integrated.
