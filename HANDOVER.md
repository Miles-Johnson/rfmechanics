# rfmechanics — handover (as of 2026-08-06)

Current-state reference for a fresh context picking up this mod. For dated bug-fix history see
`notes/rfmechanics-2026-07-30-session-notes.md`, `notes/rfmechanics-2026-08-04-session-notes.md`,
and `notes/orc-thew-phase1-session-2026-08-05.md` (outside this folder, in the workspace `notes/`
repo) — those files explain *why* certain code shapes exist (e.g. why `ClimbSaturationPatch`'s
flush timer always persists, why `ClimbSpeedPatch` only scales one field, why fall damage
reduction had to become a Harmony prefix instead of a plain `EntityBehavior`, why Thew's race
gate is a tick-check rather than listener lifecycle). This file is the current-state map; those
are the incident log. Keep all of them — don't collapse one into another. Also see
`notes/orc-diagnostic-findings.md` (read-only pre-implementation research) and
`notes/orc-phase0-results.md` (in-game verification tests + Thew smoke test) for the Orc/Thew
work specifically.

**Goblin Phase G2 (2026-08-06) is the most recent work in this mod** — body mechanics (dig
speed, climbing, tunnel speed, spit-packed earth) plus an Elf leaf-gathering mechanic that
closes a G1 debt. Start from `notes/goblin-phase-g2-partA-report.md` (investigation + numbers
model) and `notes/goblin-phase-g2-partB-as-built.md` (what shipped) — **build-verified only,
not yet deployed to the live install or smoke-tested in-game**.

**Goblin Phase G1 (2026-08-06)** — start from
`notes/goblin-phase-g1-handover.md` (current state + what's left), which points at
`notes/goblin-diagnostic-findings.md` (pre-implementation research), `notes/goblin-phase-g1-as-built.md`
(what shipped, including the collision-decoupling math and the sneak-era traversal
correction), and `notes/goblin-phase-g1-smoke-test-checklist.md` (in-game verification —
8/9 passed, one open bug: the branchy-leaves recipe not appearing for Elf at all).

**Orc Phase 3 (Bands), one day earlier, is the next-most-recent and least-verified work in
this mod** —
read these three in order before touching `ThewBehavior.cs`/`BandBehavior.cs`/any
`Thew*Patch.cs` file: `notes/orc-phase3-partA-hunger-numbers.md` (the numbers model + the
locked config ruling, including a mid-flight correction once a later brief's Bulky
Thew-gain-rate multiplier was known), `notes/orc-phase3-partB-bands.md` (Part B
implementation, **plus two same-day addenda** — the ramp/eat-pulse/stomach-stacking rework
and the decay-tier/starvation-shield rework, both done *after* Part B's initial "done" report
in response to live testing), and `notes/orc-phase3-smoke-test-checklist.md` (the test plan,
now stale in its specific command-output examples but the checklist structure still holds).
**Almost none of Phase 3 has been confirmed working in-game yet** — see Testing status below,
this is not a "ship it" state.

## What this mod is

A Harmony-patch C# mod (`modid: rfmechanics`, HarmonyId `"rfmechanics"`, universal side)
providing race-specific movement/mining/climbing mechanics that can't be expressed as plain
JSON trait stats. It is **soft-coupled** to `raceframework`: trait codes it gates on are
config strings (`DwarfTraitCode`, `ElfTraitCode`), not a hard project reference — rfmechanics
never touches `raceframework`'s source or assets directly, it only calls
`CharacterSystem.HasTrait(iplayer, traitCode)` at runtime.

## Architecture conventions (every patch follows these — deviate deliberately, not by accident)

- **Guard chain order**: null/type guard first (outside `try` where the entity type itself is
  in question, e.g. `is not EntityPlayer`) → config null/master-toggle → material/context gate
  → `characterClass` null check (**load-bearing**: `CharacterSystem.HasTrait` returns `true`
  for a null/no class by default, so every patch explicitly treats "no class" as "not this
  race" to avoid charging classless entities) → `HasTrait(iplayer, cfg.XTraitCode)`.
- **Exception handling**: everything after the type guard runs inside `try`, catches `Exception`,
  logs once via a `static bool loggedException` flag (never spams the log), and leaves the
  patched value/state untouched on failure — patches fail closed, never corrupt state.
- **Config**: one shared `RFMechanicsConfig` (`rfmechanics.json` in `ModConfig`), loaded in
  `RFMechanicsModSystem.Start()` via `LoadConfig` — missing file → defaults + write; malformed
  JSON → defaults in memory only, file left alone for manual fixing; successful parse → used,
  then written back. Because `StoreModConfig` serializes the strongly-typed config object (not
  raw JSON), renamed/removed keys silently vanish and newly-added keys silently appear on the
  next store — **no manual config migration needed when adding a field**, it self-heals on
  next mod start.
- **`PhysicsBehaviorBase.collisionTester` is `[ThreadStatic]` and shared across every entity
  ticked on that thread** — not per-entity. `AssignToEntity` rebinds it immediately before
  each entity's own collision test, synchronously, with no cross-entity reentrancy within one
  thread's tick. This is the load-bearing fact behind `BranchyLeavesPassthroughPatch`'s design
  — see that file's doc comment before touching it or adding another per-entity collision
  behavior.

## Feature inventory

| File | Patches | Trait gate | Config keys | What it does |
| --- | --- | --- | --- | --- |
| `MiningSpeedPatch.cs` | `CollectibleObject.GetMiningSpeed` (postfix) | `DwarfTraitCode`, `GoblinTraitCode` | `EnableMiningCurve`, `MiningDepthWeight`, `MiningAltitudeWeight`, `MiningBonusCap`, `EnableGoblinStonePenalty`, `GoblinStoneMiningFactor` | Depth/altitude mining-speed bonus for dwarves, gated to Ore/Stone material (mirrors vanilla's own gate at `CollectibleObject.cs:621-624`). **Extended 2026-08-06 (Phase G2)**: flat stone-mining penalty for goblins (`GoblinStoneMiningFactor`, default 0.4) in the same postfix, sequential trait checks, same coexistence shape as `FallDamagePatch`'s multi-race handling. |
| `GoblinDigModifierBehavior.cs` | `BlockBehavior.GetMiningSpeedModifier` (own extension point, **not** a Harmony patch) | `GoblinTraitCode` | `EnableGoblinDigBonus`, `GoblinBareHandDigRate` | **New 2026-08-06 (Phase G2), gate corrected 2026-08-06 (Fourth resolution) and reconfirmed at G2.1 review.** Goblin dig bonus on diggable-earth blocks — returns `GoblinBareHandDigRate` (default 8.0, beats a steel shovel on every material) for goblins, `1.0` otherwise. **Not bare-hand-only** — the original doc comment's "bare-hand-only by construction" claim was an unverified assumption, not an actual guard, and was wrong: `Block.OnGettingBroken` folds the boosted `dt` straight into whatever tool is held (`Block.cs:1040`, "This will also affect tool mining speed... and that's OK"). The real, intentional gate (confirmed standing design at G2.1 review, see `notes/goblin-dig-materials-handover.md`'s "Fourth resolution") is **shovel-only**: the bonus applies bare-handed or holding anything except a shovel; only `Collectible.Tool == EnumTool.Shovel` drops back to `1.0`. Attached via JSON patch (`patches/goblin-dig-blockbehavior.json`) to 15 vanilla diggable-earth blocktype families (soil, sand, gravel, packeddirt, aridpackeddirt, bony, bony-layered, cob, forestfloor, muddygravel, sludgygravel, sand-layered, sand-wavy, gravel-layered, gravel-dirty), and directly in all 10 mod-owned spit-packed blocktypes. Registered via `RegisterBlockBehaviorClass`, not `PatchAll`. **Build-verified only.** |
| `GoblinClimbingPatch.cs` | `EntityBehaviorControlledPhysics.MotionAndCollision` + `.ApplyTests` (both postfix) | `GoblinTraitCode` | `EnableGoblinRockClimbing`, `EnableGoblinTreeClimbing`, `GoblinRockClimbCodePrefixes` | **New 2026-08-06 (Phase G2).** Parallel to `TreeClimbingPatch` (Elf), not an extension of it — raw rock is never vanilla `Climbable`-flagged, so the dwarf-style `ClimbSpeedPatch`/`ClimbCollideAssistPatch` shape (which extends vanilla's own ladder detection) would never fire for it; only `TreeClimbingPatch`'s self-contained-scan shape generalizes. Two independent match groups, each its own toggle: `"log-grown"` (tree, same as Elf) and a config-driven raw-rock whitelist (`rock-`, `crackedrock-`, `meteorite-`, `stalagsection-` — four prefixes, not one, see Part A report A2). **No saturation cost** — researched and confirmed vanilla's own `EntityBehaviorHunger.SlowTick` has no `IsClimbing`-specific term at all, so this doesn't introduce an asymmetry against vanilla's free ladder climbing. **Build-verified only.** |
| `RFGoblinTunnelBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-goblintunnel.json` | `GoblinTraitCode` (checked in `IsGoblin()`, inline, not a Harmony patch) | `EnableGoblinTunnelSpeed`, `GoblinTunnelSpeedBonus` (0.15, bumped from 0.12 at G2.1 review), `GoblinTunnelStatWriteThreshold` | **New 2026-08-06 (Phase G2), earth-check unified 2026-08-06 (G2.1).** Walkspeed bonus for goblins tunneling under diggable earth — `RFTreeProximityBehavior`'s exact pattern (3s tick, not Thew/Band's 6s), but a narrow 2-block directional column check (`headY+1`/`headY+2`) instead of a radius `WalkBlocks` scan. Hysteresis: entry needs only the near sample, exit needs both to fail. Writes `Stats.Set("walkspeed", "tunneling", value)`. Condition now delegates to `GoblinSpitPackingPatch.IsGoblinEarth` instead of maintaining its own `Code.Path` prefix list — the prior standalone list (`RFMechanicsConfig.GoblinDiggableEarthCodePrefixes`, removed G2.1) had drifted out of sync with the spit-packing resolver (5 of 10 mod-owned families were missing from it, so goblins got no tunnel bonus under those ceilings); see `notes/goblin-dig-materials-handover.md`. **Build-verified only.** |
| `GoblinSpitPackingPatch.cs` | `Block.OnBlockBroken` (postfix) | `GoblinTraitCode` | `EnableGoblinSpitPacking` | **New 2026-08-06 (Phase G2), materials/gate expanded across several same-day sessions — see `notes/goblin-dig-materials-handover.md` for the full resolution history.** Converts all six face-adjacent diggable-earth neighbors of a goblin's break to their spit-packed variant, unconditionally rather than replicating `BlockBehaviorUnstableFalling`'s fall-decision logic — **no directional exclusion of the block being dug toward** (considered at the G2.1 diagnostic, explicitly ruled against at G2.1 review: claws always spit-pack all six neighbors, shovel is the harvest path for original material). Now covers 10 mod-owned families (`spitpacked{soil,sand,gravel,sandwavy,dirtygravel,bonysoil,cob,forestfloor,muddygravel,sludgygravel}`), not just soil→`packeddirt`+2 sand/gravel blocktypes as originally shipped. **Gate (confirmed standing design at G2.1 review): shovel-only** — `Collectible.Tool == EnumTool.Shovel` suppresses conversion; bare hands and every other held item still trigger it. This was deliberately revised same-day from an initial empty-hand-only gate (inventory-juggling — picking up an unrelated item mid-dig silently flipping conversion off read as a bug, not a choice). Exposes `IsGoblinEarth` (internal static) as the shared "is this diggable earth" predicate also used by `RFGoblinTunnelBehavior` (G2.1). Relies on winning a race against `TryFalling`'s deferred `EnqueueMainThreadTask` closure by converting synchronously before it drains — reasoned from source, **not yet verified in-game**; the documented fallback (switch to a prefix) was not built speculatively. Ships ungated by primer/rot state (G3 scope). Design intent going forward: spit-packed sand/gravel are goblin-exclusive to *produce*, but a lossy wash-back-to-vanilla barrel recipe (available to any race) is planned — investigated at G2.1 (Part A only, not built, see `notes/goblin-diagnostic-findings-g2.1.md`). **Build-verified only, race-window behavior specifically unconfirmed.** |
| `ElfLeafDropPatch.cs` | `Block.GetDrops` (postfix, appends to `__result`) | `ElfTraitCode` | `EnableElfLeafGathering` | **New 2026-08-06 (Phase G2), duplication bug fixed 2026-08-06 (G2.1).** Appends a self-drop (`leaves-placed-{wood}`/`leavesbranchy-placed-{wood}`, grown-stage-to-placed conversion) to vanilla's existing `treeseed`/`stick` drops for elves breaking `leaves-`/`leavesbranchy-` blocks — appends, does not replace. **Naturally-generated leaves only**: gated on `!path.Contains("-placed-")`, since the original version also re-triggered on breaking an already-placed leaf block, letting elves compound leaves indefinitely by planting and re-harvesting. Verified (G2.1, previously outstanding since G1) that every wood species has a matching `leaves-placed-{wood}`/`leavesbranchy-placed-{wood}` variant — `normal.json`/`branchy.json` share an identical `{type: [grown..grown7, placed]} x {wood}` variant cross-product, 13 species, no `skipVariants`, so the gate can't silently fail to protect any species. Coexists with `OreYieldPatch` on the same `Block.GetDrops` hook (different material gate). **Closes G1's open branchy-leaves ingredient-sourcing gap** (leaves never dropped themselves before this — see `goblin-phase-g1-as-built.md`). **Build-verified only.** |
| `OreYieldPatch.cs` | `Block.GetDrops` (prefix) | `DwarfTraitCode` | `EnableOreCurve`, `OreThreshold`, `OreCeiling` | Depth-only ore yield bonus for dwarves, Ore-material only (server-side only — drops only spawn server-side). Stacks multiplicatively with any `oreDropRate` trait stat, doesn't currently share a value with one. |
| `ClimbSpeedPatch.cs` | `EntityBehaviorControlledPhysics.SetProperties` (postfix) | `DwarfTraitCode` | `EnableClimbSpeed`, `ClimbSpeedFactor` | Scales `climbDownSpeed` (the **Jump/ascend** field — vanilla's field names are inverted from their function) for dwarves. Ascent-only; `climbUpSpeed` (Sneak/descend) is always reset to base. Idempotent — recomputes from JSON base values rather than multiplying current state, so retry/listener/rejoin can all safely re-call it. Has a construction-time entity-link race workaround (`ScheduleRetry` + `RegisterClassListener`, both `ConditionalWeakTable`-guarded one-shot). |
| `ClimbCollideAssistPatch.cs` | `Block.OnEntityCollide` (postfix) | `DwarfTraitCode` | `EnableClimbSpeed` (shared toggle) | Covers the *other* ladder-ascent path — walking into a climbable block without Jump, which hard-sets `Motion.Y = 0.04` via a completely separate vanilla mechanism `ClimbSpeedPatch` doesn't touch. Ascent-only (only rescales when `Motion.Y > 0`). |
| `ClimbSaturationPatch.cs` | `EntityBehaviorHunger.OnGameTick` (prefix) | `DwarfTraitCode` | `EnableClimbSaturation`, `ClimbSaturationPerSecond` | Flat satiety cost per second of climbing (ascent only: `IsClimbing && Jump`), banked in `entity.Attributes["rf-climbseconds"]` and flushed every 10 real seconds (`entity.Attributes["rf-climbflush"]`) as a direct `Saturation` write — skips nutrient drain/max-health erosion deliberately, climbing costs hunger, not health. `ClimbSaturationPerSecond` default `2.4` = vanilla sprint-surcharge parity at 30 TPS. **Known loose end** (from the 2026-07-30 session notes): was temporarily set to `200.0` for testing visibility and needs confirming it's back to `2.4` in the live `rfmechanics.json` before this is "done." |
| `BranchyLeavesPassthroughPatch.cs` | `CachingCollisionTester.AssignToEntity` + `CachingCollisionTester.GenerateCollisionBoxList` (both postfix) | `ElfTraitCode` | `EnableBranchyLeavesPassthrough` | **New 2026-08-03.** Elves walk through branchy leaves (`block.Code.Path.Contains("branchy")`, mirroring vanilla's own `ItemAxe.cs` convention) instead of colliding with their solid sides. Filters `CollisionBoxList` in place, post-query/pre-push-out, via a `ConditionalWeakTable<CachingCollisionTester, Entity>` binding (see architecture note above for why this is safe). No signature changes to `Block.GetCollisionBoxes` or any block subclass; movement integration (`ApplyTerrainCollision`'s push-out logic) is untouched. **Confirmed working in-game (2026-08-04)**; temporary checkpoint-logging added during debugging has been removed, back to the standard single `loggedException` convention. |
| `RFTreeProximityBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-treeproximity.json` | `ElfTraitCode` (checked in `IsElf()`, inline, not a Harmony patch) | `EnableTreeProximitySpeed`, `TreeProximityRadius`, `TreeProximityMaxBonus`, `TreeProximityStatWriteThreshold` | **New 2026-08-04.** Walkspeed bonus for Elves near living trees (`log-grown`-prefixed blocks, vanilla's own convention for a standing tree vs. a cut/placed log — see `BlockLog.cs`). Ticks every ~3s, server-side only, same proximity-scan shape as vanilla's `EntityBehaviorBodyTemperature.getNearHeatSourceStrength` (inverse-distance falloff over a `WalkBlocks` radius). Writes `Stats.Set("walkspeed", "treeproximity", value)` — a source distinct from `"trait"` (the flat `rf-elf-positive` walkspeed 0.08 bonus) and `"rested"`, so it stacks additively instead of overwriting either. Attached to every player (like `RestedBehavior`/`rfrested`); the elf gate lives inside the behavior, not in the JSON patch. Build-verified only — **not yet tested in-game.** |
| `TreeClimbingPatch.cs` | `EntityBehaviorControlledPhysics.MotionAndCollision` + `.ApplyTests` (both postfix) | `ElfTraitCode` | `EnableTreeClimbing` | **New 2026-08-04.** Lets Elves climb standing tree trunks (`log-grown`-prefixed blocks) as if they were ladders, at plain vanilla ladder speed (`climbUpSpeed`/`climbDownSpeed`, no separate cost/curve unlike the dwarf climb mechanics). `MotionAndCollisionPostfix` does the actual `Motion.Y` work (must run before `ApplyTests`/`ApplyTerrainCollision` consume `pos.Motion` the same tick — see Gotcha in session notes); `ApplyTestsPostfix` only sets cosmetic climbing state (`IsClimbing`/`ClimbingOnFace`) and only when vanilla's own ladder scan found nothing, so a real ladder built onto a tree still takes priority. **Confirmed working in-game.** |
| `FallDamagePatch.cs` | `EntityBehaviorHealth.OnEntityReceiveDamage` (prefix) | `ElfTraitCode`, `GoblinTraitCode` | `EnableFallDamageReduction`, `FallDamageReductionFactor`, `EnableGoblinFallDamageReduction`, `GoblinFallDamageReductionFactor` | **New 2026-08-04 (Elf), extended 2026-08-06 (Goblin).** Reduces fall damage for Elves by `FallDamageReductionFactor` (default 60%) and for Goblins by `GoblinFallDamageReductionFactor` (default 50%), both gated on `damageSource.Source == EnumDamageSource.Fall`, checked sequentially in one prefix (a player is only ever one race, so no double-application risk; deliberately kept as one patch on the method rather than a second Harmony patch on the same target). Replaces an earlier plain-`EntityBehavior` approach that never had any effect due to entity-behaviors-array ordering — see Testing status below and the 2026-08-04 session notes for the full root cause. **Elf path confirmed working in-game (2026-08-04); Goblin path smoke-tested 2026-08-06 (checklist item 7, passed) but not yet independently re-verified after the Goblin extension.** |
| `GoblinDarkvisionModSystem.cs` | Not a Harmony patch — client-only `ModSystem`/`IRenderer`, `OnRenderFrame` on `EnumRenderStage.Before` | `GoblinTraitCode` | `EnableGoblinDarkvision`, `GoblinDarkvisionStrength` | **New 2026-08-06.** Constant-strength (0.8, matching vanilla night-vision goggles' own strength ceiling) darkvision for Goblins. `ShouldLoad` is client-only — no server component, `characterClass`/`extraTraits` sync via `WatchedAttributes` so `CharacterSystem.HasTrait` works client-side without a round-trip. Writes `capi.Render.ShaderUniforms.NightVisionStrength` — the same uniform vanilla's own `ModSystemNightVision` (night-vision goggles) writes unconditionally every frame, so this composes via `Math.Max` rather than overwriting, and sets `RenderOrder => 0.1` (vanilla's is `0.0`) to guarantee it runs after vanilla's own write within the same frame — `IRenderer.RenderOrder`'s own doc comment confirms "0 = drawn first, 1 = drawn last", so this ordering is verified, not assumed. **Smoke-tested 2026-08-06 (checklist item 6, all three goggle-compose cases passed).** |
| `ThewBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json`; also overrides `OnEntityDeath` | `OrcTraitCode` (checked in `IsOrc()`, inline, not a Harmony patch) | `EnableThew`, `OrcTraitCode`, `ThewGainPerHour`, `ThewGainBandMult`, `ThewRampFloor`, `ThewRampCeiling`, `ThewPerBite`, `BiteCooldownSec`, `BulkyHoldDecayPerHour`, `ThewDecayUnderfedPerHour`, `ThewDecayHungryPerHour`, `ThewDecayStarvingPerHour`, `ThewHungryThreshold`, `StarvationShieldWhileThew`, `ProteinGateLevel`, `SeasonalGainEnabled`, `SeasonalGainMultipliers`, `OrcStomachMultiplier`, `StomachStackingMode`, `EnableThewDeathPenalty`, `ThewDeathPenalty` | **New 2026-08-05, reworked twice same day (see the two addenda in `notes/orc-phase3-partB-bands.md`).** Hidden per-player Thew float (0–1) in `entity.Attributes` (non-synced, no HUD) for orcs. Ticks every ~6s. Gain: graded ramp (0 at `ThewRampFloor`, full at `ThewRampCeiling`, linear between) × `ThewGainBandMult[currentBand]`, AND protein-gated (`ProteinLevel` > `ProteinGateLevel`) — reads `BandBehavior.CurrentBand` off the sibling behavior. Decay below the ramp floor: three tiers by name (Underfed/Hungry/Starving, see `ThewBehavior.DecayTierName`), no neutral zone. Bulky band adds an unconditional flat bleed (`BulkyHoldDecayPerHour`) on top of whichever tier/gain is active. `ApplyStomachMultiplier` reworked from a one-time idempotent-flag multiply to a continuous per-tick recompute against a hardcoded vanilla baseline (1500) and `Stats.GetBlended("maxSaturationFactor")`, combined with `OrcStomachMultiplier` per `StomachStackingMode` (`Max` default: larger of the two candidates, never compounds; `Multiply`: old always-compounding behavior). Flat Thew death penalty on `OnEntityDeath`. Same tick-gated-behavior-attached-to-everyone convention as `RestedBehavior`/`RFTreeProximityBehavior`. **Original binary-gate version confirmed working in-game 2026-08-05; the reworked ramp/tier version is NOT yet re-verified** — see Testing status. |
| `BandBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json` (same file as `rfthew`) | `OrcTraitCode` (own `IsOrc()`, duplicated not shared — matches this mod's established per-behavior-gate convention) | `EnableBands`, `BandUpThresholds`, `BandDownThresholds`, `BandSizes`, `BandSizeLerpSeconds`, `HungerRateMult`, `WalkSpeedDelta`, `MaxHpExtraPoints`, `AnimalSeekingRangeDelta`, `BulkyMeleeDamageBonus`, `BluntCrushResistDelta`, `BulkyArmorWalkSpeedAffectednessDelta`, plus two `_UNWIRED` reserved fields (see below) | **New 2026-08-05, Phase 3 Part B.** Lean/Standard/Bulky hysteresis state machine driven off `ThewBehavior`'s Thew value (up 0.35/0.70, down 0.30/0.64). Owns `entitySize` (lerps ~10s on cross via `PlayerModelLib`'s reflection-based `UpdateEntityProperties`, self-heals every slow tick if it drifts — covers the T2 race-swap-resets-entitySize finding). Applies/clears a fixed set of `Stats.Set` categories once per band cross (never per-tick) under source key `"rf-orc-band"`: `walkspeed`, `hungerrate`, `animalSeekingRange`, `meleeWeaponsDamage`, `bluntDamageFactor`, `crushingDamageFactor`, `armorWalkSpeedAffectedness`, `maxhealthExtraPoints` (the last needs an explicit `EntityBehaviorHealth.UpdateMaxHealth()` call, nothing else retriggers it off a bare `Stats.Set`). **Two stats from the locked design table are NOT wired**: jump height (`jumpHeightMul`'s only consumer clamps values below 1.0 to have zero effect — can't express a reduction without a Harmony patch) and knockback, both halves (`KnockbackResistance` lives on the shared per-entity-*type* `EntityProperties`, not a per-player `Stats` category — writing it would affect every player of that type, not just the one orc). Both flagged, not built speculatively — see `notes/orc-phase3-partB-bands.md` Deviations. `/rfthew setband <lean\|standard\|bulky>` forces a band for testing; **had a real bug** (typing `lean` reported "forced to bulky") whose exact root cause was never confirmed by reading alone — fixed defensively (explicit validated match + echoes the raw parsed input on both success and failure) rather than assumed-fixed. **Not yet re-verified in-game after that fix.** |
| `ThewEatPulsePatch.cs` | `EntityBehaviorHunger.OnEntityReceiveSaturation` (postfix) | `OrcTraitCode` | `ThewPerBite`, `BiteCooldownSec` | **New 2026-08-05 (Addendum 1).** Small flat Thew grant per qualifying eat event, cooldown-gated per player so it can't be farmed by nibble-spam — the cooldown also incidentally absorbs multi-ingredient meals for free, since `BlockMeal`/`BlockCookedContainer` fire this hook once per ingredient. Shares `ThewBehavior.RampMultiplier` (public static) with the tick gain so both use the identical curve. **Not yet verified in-game.** |
| `ThewShieldPatch.cs` | `EntityBehaviorHealth.OnEntityReceiveDamage` (prefix) | `OrcTraitCode` | `StarvationShieldWhileThew` | **New 2026-08-05 (Addendum 2).** While an orc's Thew > 0, zeroes incoming damage where `damageSource.Type == EnumDamageType.Hunger` (vanilla's own starvation-damage type, dealt from `EntityBehaviorHunger.SlowTick`) — at Thew == 0 the shield drops and vanilla starvation/death applies untouched. Patched at the damage-receive choke point rather than `SlowTick` itself because `SlowTick` also does unrelated cold-resistance stat work in the same method body that a prefix can't selectively skip around. **Not yet verified in-game.** |
| `PreservedProteinPatch.cs` | `CollectibleObject.tryEatStop` (prefix) + `EntityBehaviorHunger.OnEntityReceiveSaturation` (prefix) | `OrcTraitCode` | `PreservedProteinItemCodes`, `PreservedProteinMultiplier` | **New 2026-08-05. Dormant.** Two-step stash pattern: the `tryEatStop` prefix flags the entity when an orc eats an item on the `PreservedProteinItemCodes` list; the `OnEntityReceiveSaturation` prefix consumes that flag (always clearing it, even if the multiplier doesn't apply) and scales `nutritionGainMultiplier` by `PreservedProteinMultiplier`. Dormant because the default list (`survival:redmeat-cured`/`survival:bushmeat-cured`) has no obtainable production path in this install (confirmed via asset-JSON search, see `notes/orc-phase0-results.md` A5) — this is a config surface for modded preserved foods, not active vanilla content. **Note the item domain is `survival:`, not `game:`** — there is no `assets/game/itemtypes/` folder in this install at all. |
| `RFMechanicsConfig.cs` | — | — | (all of the above, plus `DwarfTraitCode` default `"rf-dwarf-positive"`, `ElfTraitCode` default `"rf-elf-positive"`, `GoblinTraitCode` default `"rf-goblin-positive"`, and two `OrcBandTriple`/`OrcBandUpDown`/`OrcStomachStackingMode` helper types) | Shared config POCO. `_UNWIRED`-suffixed fields (`BulkyJumpHeightReduction_UNWIRED`, `StandardKnockbackTakenReduction_UNWIRED`) are reserved config surface only, not consumed by any code — see `BandBehavior.cs`'s row above. |
| `RFMechanicsModSystem.cs` | — | — | — | `Start()`: load config, register `rfrested`/`rftreeproximity`/`rfthew`/`rfband` behavior classes, `harmony.PatchAll(Assembly.GetExecutingAssembly())`. `StartServerSide()`: registers `/dwarfdepth`, `/rfdiag`, `/rfstatsfix`, `/rfphase0` (throwaway Phase-0 diagnostic, marked for removal before release), `/rfthew` (all server-side only — the `.` chat shortcut won't find them, use the full `/name`; registering them client-side too previously broke `rfdiag`'s hunger-behavior reads, since those are server-authoritative). The PlayerModelLib `entitySize` reflection helpers (`GetPmlSkinBehavior`/`TryUpdatePmlEntityProperties`) were relocated from `private` (inside the throwaway PHASE0-DIAG region) to `internal` 2026-08-05 because `BandBehavior` now depends on them for real band-size writes — **do not delete them in a PHASE0-DIAG cleanup pass**, they're load-bearing production code now, just physically sitting above that region with their own doc comment explaining why. |

## Diagnostic commands (server-side only, use `/`, not `.`)

- `/dwarfdepth` — depth/altitude curve debug for the calling player.
- `/rfdiag` — dumps `extraTraits`, blended `walkspeed`/`hungerrate`, explicit dwarf-trait
  `HasTrait` checks, banked climb time, saturation, and now (2026-08-04) the `ElfTraitCode`
  `HasTrait` check and a per-source `walkspeed` breakdown (`trait`/`treeproximity`/`rested`).
  Still doesn't report branchy-leaves state specifically.
- `/rfstatsfix` — forces a full trait/stat recompute (works around a race/model-swap bug that
  isn't rfmechanics' own — see session notes Bug 2 — where a live race swap leaves stale
  `walkspeed`/`hungerrate` stuck from the previous race).
- `/rfphase0` — throwaway Phase-0 verification diagnostics (marker/entitysize/dump), root
  privilege, marked `PHASE0-DIAG — remove before release` in the source. Keep until Bands
  (Phase 3) is done being tuned against it.
- `/rfthew` — root privilege, three subcommands:
  - `dump` — Thew value + full condition readout: `orc`, `charClass`/`extraTraits`,
    `satFrac`, `rampMult` (+floor/ceiling), `protein`/`proteinGated`, `gaining`/`decaying`,
    `decayTier` (Underfed/Hungry/Starving/none), `biteCooldownRemaining`, `shieldActive`,
    and (if `BandBehavior` is attached, which it always is) a `band=...` section with
    current/target `entitySize`, `midLerp`, and every per-band stat value currently applied.
    **Gotcha, confirmed 2026-08-05**: this band section prints for *every* player, orc or
    not, since `BandBehavior` is attached to everyone the same way `ThewBehavior` is — for a
    non-orc it shows what Lean's numbers *would be*, not whether anything is actually
    applied. Check `/rfdiag`'s per-stat breakdown for an `rf-orc-band` source entry to
    confirm real application.
  - `set <value>` — force-set Thew directly (testing only).
  - `setband <lean|standard|bulky>` — force a band directly, bypassing hysteresis (testing
    only). Echoes the raw parsed input in its response as of the 2026-08-05 bugfix (see
    `BandBehavior.cs`'s row above) — if it ever again reports a band that doesn't match what
    was typed, that response text is the first thing to check.

## Cross-mod context (not in this folder, but load-bearing for what's here)

- **`mods/raceframework`** owns the actual trait definitions this mod gates on:
  `rf-dwarf-positive`/`rf-dwarf-negative` (pre-existing) and `rf-elf-positive`/
  `rf-elf-negative` (added 2026-08-03, `assets/raceframework/config/traits.json`, wired to
  the Elf race via `assets/raceframework/patches/racialequality/elf-char.json`). rfmechanics
  only reads these by trait-code string via `CharacterSystem.HasTrait` — never edit
  `raceframework`'s JSON from this mod's code, and never assume a specific trait's attribute
  values from here (config strings are the only coupling).
- **`mods/lrracialtweaks`** also has its own `elf-positive`/`elf-negative` traits wired to the
  Elf race, independently of `raceframework`'s. Deliberately left untouched/ignored per the
  Elf design work — not a bug if you see both trait pairs on an Elf character.
- **Orc (2026-08-05):** `raceframework`'s `rf-orc-positive`/`rf-orc-negative` traits
  (`animalHarvestingTime: -0.3`, `animalLootDropRate: 0.1`, and an empty reserved negative) are
  wired to the orc race the same way Elf's are — a patch onto
  `racialequality:config/customplayermodels/ork-char.json`'s `ExtraTraits` array
  (`mods/raceframework/assets/raceframework/patches/racialequality/ork-char.json`, which also
  now carries `MinCollisionBox`/`MaxCollisionBox` for Phase 3 Bands and a pre-existing
  `AvailableClasses` patch that predates this session). **Important spelling note**: the model
  itself is "ork" in `racialequality`/`PlayerModelLib` — "orc" is only this mod's own naming
  (trait codes, config keys, docs). `lrracialtweaks` also independently patches the same
  `ork-char.json` file with its own unrelated `ork-positive`/`ork-negative`/`blackguardcrafting`
  traits — coexists fine via `addmerge`, same pattern as the Elf trait-pair situation above.
- **Satiety stacking — resolved 2026-08-05, same day as the flag was raised.**
  `ThewBehavior`'s `OrcStomachMultiplier` (2.5x) used to multiply whatever `MaxSaturation`
  already was, compounding with `racialability`'s selectable `bottomless-stomach-*` ability
  traits (which grant a `maxSaturationFactor` stat consumed by a PlayerModelLib Harmony patch,
  `reference/decompiled/PlayerModelLib/PlayerModelLib/StatsPatches.cs:387-402`) — a character
  with both active saw `MaxSaturation` reach 11250 (1500 × 3 ability × 2.5 orc). Now
  config-driven via `StomachStackingMode` (default `Max`): takes the larger of the two
  candidates (2.5x vs 3x → 3x → 4500), never compounds. `Multiply` mode preserves the old
  7.5x-compounding behavior as an opt-in. Required reworking `ApplyStomachMultiplier` from a
  one-time flag-guarded multiply to a continuous per-tick recompute (see `ThewBehavior.cs`'s
  feature-inventory row above for why a simple relative divide-back-out wouldn't have worked
  against PlayerModelLib's own reactive rescale-on-read behavior). **Not yet re-verified
  in-game** — see Testing status.
- **Goblin (2026-08-06):** `raceframework`'s `rf-goblin-positive` (`wildCropDropRate: 0.6`,
  `forageDropRate: 1.35`) / `rf-goblin-negative` (empty, reserved) traits are wired to the
  goblin race the same way Elf/Orc are — a patch onto
  `racialequality:config/customplayermodels/goblin-char.json`'s `ExtraTraits` array
  (`mods/raceframework/assets/raceframework/patches/racialequality/goblin-char.json`), which
  also now carries the static size fields (`ModelSizeFactor`/`GuiModelScale: 0.49`,
  `SizeRange: [0.95, 1.05]`) and a **decoupled** collision box
  (`ScaleColliderWithSize{Horizontally,Vertically}: false`, `CollisionBox: [0.6, 0.9]`,
  `EyeHeight: 0.765` pinned via matching `Min/MaxEyeHeight`). Goblin deliberately does **not**
  use Orc's `entitySize` WatchedAttribute/`BandBehavior` mechanism at all — its size is
  100% static JSON, no rfmechanics runtime code touches it. See
  `notes/goblin-phase-g1-as-built.md` for why the decoupling was necessary (PlayerModelLib
  couples `ModelSizeFactor` to collision by default) and the correction to the old
  lrracialtweaks-era "1-block traversal tested and working" claim (the math shows that era's
  standing collision height was still >1.0 — if real, it was sneak-tested, not standing).
  A live-install deployment gotcha surfaced during G1 smoke testing, worth remembering for
  any future phase: `VintagestoryData/Mods/raceframework` and `VintagestoryData/Mods/rfmechanics`
  are **plain copies**, not symlinks to the dev tree — a fresh `dotnet build` or JSON edit does
  **not** reach the live game until both are manually re-copied (game must be fully closed
  first for the `rfmechanics.dll` half, it's locked while running).
  **G2.1 design ruling on spit-packed material**: goblin-exclusive to *produce* (only a
  goblin's claws convert earth to spit-packed variants), but intended to be washable back to
  vanilla sand/gravel by **any** race via a lossy water-barrel recipe (e.g. 4 in, 3 out) —
  non-goblins should have a reason to want goblin-made material, up to razing a warren for
  it. **Not built** — investigated read-only at G2.1 (Part A only, hard-stopped per that
  phase's brief); see `notes/goblin-diagnostic-findings-g2.1.md`'s wash-recipe section for
  the barrel-mechanics findings this will need to build against.
  **Known debt (G2.1)**: `patches/goblin-dig-blockbehavior.json`'s attachment list (which
  vanilla blocktypes get `GoblinDigModifierBehavior`) is a fourth place that must stay in
  sync with the earth-family set, alongside `GoblinSpitPackingPatch.ResolveConversionTarget`
  and (as of G2.1) `IsGoblinEarth`. Unlike the tunnel-ceiling check, this one **could not**
  be unified onto the same runtime query — block behaviors are attached via JSON patch at
  asset-load time, before any runtime resolver exists to consult, so there's no hook point
  for "ask `GoblinSpitPackingPatch` whether this block should get the behavior" the way
  `RFGoblinTunnelBehavior` can ask at tick time. Left as its own hand-maintained list
  (currently 15 entries); if it drifts the same way `GoblinDiggableEarthCodePrefixes` did,
  the symptom is a diggable-earth family that spit-packs and tunnel-bonuses correctly but
  never gets the goblin dig-speed bonus itself.
- **`mods/dietsetup`** — unrelated to rfmechanics directly, except that `raceframework`'s
  `rf-elf-negative` trait sets `dietsetup:preservedMult` (an entity-stat hook dietsetup reads,
  not something rfmechanics touches).
- Design/planning history for the Elf work lives in `C:\Users\Kjol\.claude\plans\` (outside
  this git repo, not tracked): `elf-task1-json-trait-diet-bundle.md` (the JSON-only trait +
  diet task, completed) and `take-this-plan-with-validated-blossom.md` (the original
  fact-check research pass this and future Elf work are scoped against). **Still deferred/not
  started**: a `CanClimbAnywhere` per-race Harmony patch, a per-instance `MaxSaturation`
  `ModSystem` call, and "Living harvest" tree-tapping. Elf fall damage reduction (originally
  scoped there as a vanilla-`FallDamageMultiplier`-based approach) is **now implemented** — see
  `FallDamagePatch.cs` above.
- **`RestedBehavior.cs`/`RestedBlockBreakPatch.cs`/`RestedToolUsePatch.cs`/
  `RestedDurabilityPatch.cs` — implemented but currently race-agnostic, and this is a known
  mismatch with intent, not the final design.** As shipped: applies equally to every player
  regardless of race, no trait gate anywhere in any of the four files (confirmed — no
  `ElfTraitCode`/`DwarfTraitCode`/`HasTrait` reference exists in any of them), gain is flat
  idle-over-time + a flat per-eat bonus, drain is flat per-block-break/per-second-tool-use; no
  tree or nature tie-in of any kind. **Decided direction (not yet implemented): Rested is meant
  to be Elf-only, and tied to trees/nature in some way** — exact mechanism still to be designed
  (candidates: gate the whole behavior on `ElfTraitCode` the way every other Elf mechanic does;
  boost gain rate near `log-grown` trees, mirroring `RFTreeProximityBehavior`'s scan; or some
  other nature-proximity hook). Not yet in the feature-inventory table either — a documentation
  gap on top of the design gap. Whoever picks this up next: add the elf gate first (cheap, same
  guard-chain shape as everything else in this mod), then design the tree/nature tie-in
  separately — don't conflate the two into one change.
- `rf-elf-positive`'s `traits.json` attributes also carry three cosmetic-only entries
  (`rfFallDamageReduction`, `rfTreeClimbing`, `rfBranchyLeavesPassthrough`) added 2026-08-04
  purely so these three C#-only mechanics show up in the character-creation Traits tab
  alongside the real stat bonuses — `Trait` has no free-text description field
  (`reference/decompiled/VSSurvivalMod/Vintagestory.GameContent/Trait.cs`), so this was the
  only way to surface them there. They have matching `charattribute-*` lang keys but are not
  read by any code — `CharacterSystem.applyTraitAttributes` still calls `Stats.Set` on them
  unconditionally (harmless, just a few unused synced bytes on the player entity).

## Testing status

- **Goblin Phase G2 (2026-08-06): six new mechanics across dig speed, climbing, tunnel
  speed, spit-packed earth conversion, and Elf leaf gathering** — see
  `notes/goblin-phase-g2-partB-as-built.md` for the original smoke-test handoff checklist
  (now stale in a few specifics, see `notes/goblin-dig-materials-handover.md` and G2.1
  below). The spit-packed conversion's race-beating mechanism (`GoblinSpitPackingPatch.cs`)
  is the item most likely to need a follow-up fix (documented prefix-based fallback if the
  postfix version loses the race against the falling-block spawn) — flagged, not built
  speculatively, **still not independently confirmed in-game as of G2.1**.
- **Goblin Phase G2.1 (2026-08-06 + this session): build-verified (`dotnet build`, 0
  errors, same 19-warning baseline as before) and redeployed to the live install**
  (`rfmechanics.dll`/`.pdb`/`.deps.json` + `assets/rfmechanics/` re-copied, game was closed
  at deploy time — not yet smoke-tested against this deploy). Changes: unified the
  tunnel-ceiling diggable-earth check onto `GoblinSpitPackingPatch.IsGoblinEarth` (removed
  `GoblinDiggableEarthCodePrefixes`); `GoblinTunnelSpeedBonus` 0.12→0.15; Elf leaf
  duplication fix (`-placed-` blocks no longer grant the bonus drop); doc corrections
  above. Directional harvest exclusion (G2.1 diagnostic's Q2) was investigated and
  explicitly ruled against — not built, not a live TODO going forward. See
  `notes/goblin-diagnostic-findings-g2.1.md` for the diagnostic this phase was grounded on.
- **Goblin Phase G1 (2026-08-06): 9/9 smoke-test items passed after redeploying both mods
  to the live install (see the deployment-gotcha note above — the first test pass was run
  against stale pre-G1 builds and gave false negatives on everything).** Confirmed working:
  character creation, trait wiring (`rf-goblin-positive`/`negative` contributing to
  `forageDropRate`/`wildCropDropRate` via `/rfdiag`), the decoupled collision box landing at
  runtime exactly as `[0.6, 0.9]` (`/rfphase0 dump`), standing (not sneaking) 1-block
  traversal, eye height/camera feel, the suffocation edge case behaving as expected, the
  three-way darkvision/night-vision-goggle compose matrix, Goblin fall damage reduction
  (Elf's path confirmed unaffected by sharing the guard chain), config regeneration, and
  (after two post-smoke-test bugfixes, below) the Elf-only branchy-leaves recipe.
  **Two bugs found, fixed, and confirmed working 2026-08-06**: the Elf-only
  branchy-leaves recipe
  (`mods/raceframework/assets/raceframework/recipes/grid/branchyleaves-elf.json`) did not
  appear anywhere in the handbook/recipe browser on an Elf character. Two independent
  causes: (1) every ingredient/output code was missing its `game:` domain prefix, so
  `GridRecipeLoader` resolved them against the recipe file's own `raceframework` domain
  instead of `game` — zero valid variants, silent failure. (2) once that was fixed,
  `server-main.log` (recipe loading is server-side only — checking `client-main.log`,
  as the original investigation did, could never have found this) showed
  `ingredientPattern` used `/` as a row separator instead of the `,` the engine actually
  strips, so the 3×3 grid's pattern string was the wrong length and every wood-species
  clone was silently dropped. Both fixed, redeployed, and Miles confirmed the recipe now
  works in-game same day; see `notes/goblin-phase-g1-handover.md`'s writeup for detail.
- `ThewBehavior`/`/rfthew`: **the original binary-gate version confirmed working in-game
  (2026-08-05, morning)** — orc detection via `extraTraits` re-derives correctly on a live
  race swap (no relog/fresh-character needed), `MaxSaturation` stomach multiplier applied
  exactly once (non-compounding, under the *old* single-multiplier design), Thew gain/decay
  ticked as designed, death penalty fired and clamped at 0, no Thew movement on non-orc. Full
  T1–T5 Phase 0 protocol also executed and gate-passed — see `notes/orc-phase0-results.md`.
  **Everything since then (same day) is unverified** — the ramp/eat-pulse/stomach-stacking
  rework and the decay-tier/starvation-shield rework both happened *after* this confirmation,
  in response to exactly this kind of live testing surfacing the old design's gaps (a dead
  zone with no gain or decay, `setband` misfiring, the compounding stomach multiplier). See
  the two addenda in `notes/orc-phase3-partB-bands.md` for what changed and why.
- **Phase 3 Bands (`BandBehavior.cs`) as a whole: build-verified only, effectively
  untested in-game.** One specific piece *is* confirmed: T2 self-heal (entitySize
  snapping back after a live race swap) was observed working correctly by Miles during this
  session. Nothing else on the B5 smoke-test checklist has been run yet — band stat
  application per band, the entitySize lerp visually, the fed/idle Thew net-rate signs, the
  Bulky↔Standard boundary oscillation, race-swap-away Stats cleanup, or the `setband` fix.
  A live saturation-fraction confusion during testing (Thew appearing "stuck" at a forced
  value) turned out to be the old ramp-floor dead zone working as designed, not a bug — see
  `notes/orc-phase3-partB-bands.md` Addendum 2 for the fix that closed that gap. **Next
  session should start from `notes/orc-phase3-smoke-test-checklist.md`**, treating its
  specific command-output examples as stale (written before both addenda) but its checklist
  structure as still the right shape.
- **Phase 4 (burn-to-survive): design-only, zero code.** Locked numbers recorded in
  `notes/orc-phase4-burn-to-survive-design.md` (cubic heal curve, per-HP Thew cost, no
  activation threshold) for whenever this phase actually starts — do not start writing this
  without re-reading that doc, it has open questions (heal application mechanism, band
  variation, interaction with the starvation shield) that need resolving first.
- `PreservedProteinPatch`: build-verified only, **dormant by design** (no reachable preserved
  item in this install to test against) — not expected to be exercised until preserved-food
  content exists.
- Dwarf features (mining/ore/climb-speed/climb-collide/climb-saturation): previously
  confirmed working in-game per the 2026-07-30 session notes, **except** the
  `ClimbSaturationPerSecond` live-value loose end noted above.
- `BranchyLeavesPassthroughPatch`: **confirmed working in-game (2026-08-04)** — an Elf
  character passes through branchy leaves as intended.
- `TreeClimbingPatch`: **confirmed working in-game (2026-08-04)** — Elf tree climbing
  behaves as intended, no cleanup needed.
- `FallDamagePatch` (Harmony prefix on `EntityBehaviorHealth.OnEntityReceiveDamage`, replacing
  the earlier `RFFallDamageBehavior` `EntityBehavior` approach, 2026-08-04): the original
  approach never had any effect — `rffalldamage` was appended to the *end* of `player.json`'s
  server `behaviors` array via `seraph-falldamage.json`, but vanilla's `health` behavior sits
  earlier in that array and applies `Health -= damage` inside its own
  `OnEntityReceiveDamage` call, so by the time `rffalldamage`'s reduction ran, health had
  already been decremented by the full, unreduced amount. A Harmony prefix on
  `EntityBehaviorHealth.OnEntityReceiveDamage` runs before that line unconditionally,
  regardless of behaviors-array order, fixing it for good. `RFFallDamageBehavior.cs` and
  `seraph-falldamage.json` have been deleted (both source and deployed copies) — fall damage
  reduction is now Harmony-only, no `EntityBehavior`/JSON-patch registration involved.
  **Confirmed working in-game (2026-08-04)** — Elf took visibly reduced fall damage from a
  fall; a human control character took much more damage from the same height.
- `RFTreeProximityBehavior`: compiles clean (0 errors), **not yet verified in-game**. Needs:
  an Elf character standing near a `log-grown` block vs. away from any tree, checked via
  `/rfdiag`'s new `walkspeed` breakdown line for a `treeproximity` entry that scales with
  distance, plus a non-Elf player near the same tree confirmed to show no `treeproximity`
  entry at all. Also fixed in the same session: `raceframework`'s Elf trait/attribute
  display was broken (missing lang keys entirely for `rf-elf-positive`/`rf-elf-negative`,
  plus a separate PlayerModelLib format-string exception affecting Dwarf's own attribute
  lines too) — see `mods/raceframework/assets/raceframework/lang/en.json`, now using
  value-suffixed `charattribute-*` keys throughout instead of format strings. Not yet
  confirmed rendering correctly in-game either.
