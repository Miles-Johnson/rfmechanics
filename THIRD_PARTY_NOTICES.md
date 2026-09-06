# Third-party notices

Original work is licensed under the root MIT license. You may fork, modify and redistribute that work while retaining its copyright and permission notice. Third-party material remains under its own terms; the MIT grant does not relicense it.

## Spyglass 0.6.1 — Fuami

Affected file: `src/RFElfZoomFovPatch.cs`, FOV argument transpiler adaptation.

Upstream: https://github.com/fuami/Spyglass/tree/6ca24153d16d36a2fe83c77cfbf2dcc15cecc14c (tag 0.6.1). The exact applicable MIT license, copyright (c) 2021 Fuami, is included in [licenses/Spyglass-MIT.txt](licenses/Spyglass-MIT.txt). License blob: `3e94e775cee9c5abebba1f9f32807ef3003767d2`.

## Vintage Story — Anego Studios and credited asset creators

Game/API dependencies are installed separately; no game DLLs are included. Game-derived material remains under Anego's applicable terms, not this mod's MIT grant. [Published source terms](https://github.com/anegostudios/vssurvivalmod/blob/master/license.txt) are reproduced in [licenses/VintageStory-source.txt](licenses/VintageStory-source.txt) for any adapted game-source portions. Those terms permit game-mod adaptations subject to their original-work requirement.

[Anego's official FAQ](https://www.vintagestory.at/faq.html/) permits game assets, including music, for Vintage Story-related content. We rely on that permission for the following game-derived assets; it does not authorize unrestricted MIT reuse outside that scope.

### Spit-packed textures

All files under `assets/rfmechanics/textures/block/spitpacked/` are recolors based on the game's `assets/survival/textures/block/stone/path/normal1.png` through `normal6.png`. Their color palettes are sampled from the corresponding vanilla sand, gravel, soil, dirty gravel, bony soil, cob, forest floor, muddy gravel and sludgy gravel textures.

Reproduction script: `tools/recolor_spitpacked.py` in this repository. It requires Pillow and an installed copy of the game. Existing block definitions refer to these derivatives; disabled terrain-conversion patches are excluded from the ZIP.

### Ore-Song audio

All ten files under `assets/rfmechanics/sounds/oresong/` blend procedural synthesis with the following game samples. Source paths are relative to the game's `assets/survival/sounds/`.

| Output suffix | Sample |
| --- | --- |
| galena | block/quern.ogg |
| coal | block/charcoal2.ogg |
| nativegold | effect/deepbell.ogg |
| nativesilver | effect/deepbell.ogg |
| nativecopper | block/heavymetal-hit.ogg |
| sphalerite | block/glass.ogg |
| cassiterite | block/rock-hit-pickaxe.ogg |
| chromite | block/rock-break-pickaxe.ogg |
| iron | block/anvil2.ogg |
| quartzgem | walk/glass2.ogg |

Reproduction script and tuning documentation: `tools/oresong/`. Requires Python, NumPy, SciPy, ffmpeg and a legitimate game installation; do not redistribute the game input files.

## Research references

See CREDITS.md for separately distributed dependencies and technical inspiration. Those acknowledgements are not additional reuse grants.
