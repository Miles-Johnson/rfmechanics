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

The current 98 files under `assets/rfmechanics/sounds/oresong/` are original procedural
synthesis, covered by the root MIT license. They contain no imported game samples.
Reproduction: `tools/oresong/render_seated_oresong.py`; requires Python, NumPy, SciPy and ffmpeg.

The historical `tools/oresong/render_oresong.py` mixed game samples into ten v1 cues.
Those cues have been removed from the current package. That historical renderer now
writes outside shipped assets, under `tools/oresong/wav/legacy-v1/`. Its game-derived
outputs still require Anego's applicable asset terms; do not redistribute its game inputs.

## Research references

See CREDITS.md for separately distributed dependencies and technical inspiration. Those acknowledgements are not additional reuse grants.
