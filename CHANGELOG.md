# Changelog

## 1.0.0 - initial baseline, 7 September 2026

- Dwarven depth mechanics and seated Ore-Song with timed listening, coarse directional mineral voices and optional captions.
- Bounded loaded-terrain search and cache; 98 original synthesized Ore-Song sound assets replace the legacy cues.
- Elven climbing and focused vision, Orc Thew/frenzy/scent, and goblin size/scavenging/spit/rot-aura mechanics.
- Includes the fix removing the nonexistent bellpepper crop patch for Vintage Story 1.22.6.
- Includes original-work MIT licensing, third-party notices and source/build identity.

Prepared for client/server deployment before the first ModDB release. Live gameplay and existing-world installation/removal acceptance remain unverified.

## 0.1.3-dev.1 — seated Ore-Song, unpublished

- Replace the instant dwarf scan with seated, empty-hand stone contact, a settling period,
  a knock, ten seconds of listening and three seconds of recovery.
- Search loaded server terrain out to 96 blocks using a shared work budget and bounded
  chunk-summary cache. No terrain loading/generation; incomplete searches are identified.
- Play staggered mineral voices with coarse bearings, nearby enveloping sound, average
  grade harmonics and size-dependent chorus. Add optional sensory captions.
- Replace the ten legacy cues with sixteen original synthesized material voices,
  including separate gem and bismuth voices, three variations and rough/clear layers.
- Local build only; multiplayer performance and in-game audio acceptance pending.

## 0.1.2-rc.2 ? candidate, unpublished

- Remove the crop-stunting patch for the absent vanilla bellpepper asset, fixing its 1.22.6 startup error.

## 0.1.2-rc.1 ? candidate, unpublished

- Snapshot of current development for gameplay acceptance; not an approved stable release.
- Include MIT licensing for original work, credits and applicable third-party notices in packages.
- Add safe build/package commands, full source identity and immutable Release ZIPs.
- Document AI-assisted development, existing-save limitations and the development/stable release workflow.
