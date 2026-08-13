Patches parked here are re-homed, not deleted (Phase G3 goblin extraction). VS only scans
`assets/<domain>/patches/*.json` for patch application, so anything outside that path is
inert regardless of content -- this folder is a deliberate off-path parking spot.

- `goblin-dig-blockbehavior.json` -- attaches the `GoblinDigModifier` block behavior
  (`mods/rfmechanics/src/BugRace/GoblinDigModifierBehavior.cs`) to vanilla soil/sand/gravel
  blocktypes. Move it back into `assets/rfmechanics/patches/` and re-enable
  `RegisterBlockBehaviorClass("GoblinDigModifier", ...)` in `RFMechanicsModSystem.Start()`
  if this mechanic is reactivated (for goblins, or lifted into a future bug-race mod).
