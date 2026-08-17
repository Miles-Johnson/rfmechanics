# rfmechanics — handover (as of 2026-08-13)

Current-state reference for a fresh context picking up this mod. For dated bug-fix history see
`notes/race-mechanics/rfmechanics-2026-07-30-session-notes.md`, `notes/race-mechanics/rfmechanics-2026-08-04-session-notes.md`,
and `notes/race-mechanics/orc-thew-phase1-session-2026-08-05.md` (outside this folder, in the workspace `notes/`
repo) — those files explain *why* certain code shapes exist (e.g. why `ClimbSaturationPatch`'s
flush timer always persists, why `ClimbSpeedPatch` only scales one field, why fall damage
reduction had to become a Harmony prefix instead of a plain `EntityBehavior`, why Thew's race
gate is a tick-check rather than listener lifecycle). This file is the current-state map; those
are the incident log. Keep all of them — don't collapse one into another. Also see
`notes/race-mechanics/orc-diagnostic-findings.md` (read-only pre-implementation research) and
`notes/race-mechanics/orc-phase0-results.md` (in-game verification tests + Thew smoke test) for the Orc/Thew
work specifically.

**Goblin Phase G2 (2026-08-06)** — body mechanics (dig speed, climbing, tunnel speed,
spit-packed earth) plus an Elf leaf-gathering mechanic that closes a G1 debt. Start from
`notes/race-mechanics/goblin-phase-g2-partA-report.md` (investigation + numbers model) and
`notes/race-mechanics/goblin-phase-g2-partB-as-built.md` (what shipped). **Confirmed working
in-game as of 2026-08-14**, along with the Phase G3 spit-charge system (grant-on-eating-rot +
empty-hand repair) — see Testing status below.

**Goblin Phase G1 (2026-08-06)** — start from
`notes/race-mechanics/goblin-phase-g1-handover.md` (current state + what's left), which points at
`notes/race-mechanics/goblin-diagnostic-findings.md` (pre-implementation research), `notes/race-mechanics/goblin-phase-g1-as-built.md`
(what shipped, including the collision-decoupling math and the sneak-era traversal
correction), and `notes/archive/goblin-phase-g1-smoke-test-checklist.md` (in-game verification —
8/9 passed, one open bug: the branchy-leaves recipe not appearing for Elf at all).

**Orc Phase 3 (Bands), one day earlier, is the next-most-recent and least-verified work in
this mod** —
read these three in order before touching `ThewBehavior.cs`/`BandBehavior.cs`/any
`Thew*Patch.cs` file: `notes/race-mechanics/orc-phase3-partA-hunger-numbers.md` (the numbers model + the
locked config ruling, including a mid-flight correction once a later brief's Bulky
Thew-gain-rate multiplier was known), `notes/race-mechanics/orc-phase3-partB-bands.md` (Part B
implementation, **plus two same-day addenda** — the ramp/eat-pulse/stomach-stacking rework
and the decay-tier/starvation-shield rework, both done *after* Part B's initial "done" report
in response to live testing), and `notes/archive/orc-phase3-smoke-test-checklist.md` (the test plan,
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

Split 2026-08-13 into "currently registered" and "disabled/superseded" after an audit
(`notes/diagnostics/workspace-structure.md`) found this table describing two disabled
classes as live and omitting several registered ones. Every row below was re-verified
against `RFMechanicsModSystem.cs`'s actual registration calls and each file's own
`[HarmonyPatch]` attribute state (Harmony's `PatchAll` discovers patch classes purely via
that attribute — a commented-out attribute is the exact equivalent of not registering a
behavior class).

### Currently registered / active

| File | Patches | Trait gate | Config keys | What it does |
| --- | --- | --- | --- | --- |
| `MiningSpeedPatch.cs` | `CollectibleObject.GetMiningSpeed` (postfix) | `DwarfTraitCode`, `GoblinTraitCode` | `EnableMiningCurve`, `MiningDepthWeight`, `MiningAltitudeWeight`, `MiningBonusCap`, `EnableGoblinStonePenalty`, `GoblinStoneMiningFactor` | Depth/altitude mining-speed bonus for dwarves, gated to Ore/Stone material (mirrors vanilla's own gate at `CollectibleObject.cs:621-624`). **Extended 2026-08-06 (Phase G2)**: flat stone-mining penalty for goblins (`GoblinStoneMiningFactor`, default 0.4) in the same postfix, sequential trait checks, same coexistence shape as `FallDamagePatch`'s multi-race handling. |
| `GoblinClimbingPatch.cs` | `EntityBehaviorControlledPhysics.MotionAndCollision` + `.ApplyTests` (both postfix) | `GoblinTraitCode` | `EnableGoblinRockClimbing`, `EnableGoblinTreeClimbing`, `GoblinRockClimbCodePrefixes` | **New 2026-08-06 (Phase G2).** Parallel to `TreeClimbingPatch` (Elf), not an extension of it — raw rock is never vanilla `Climbable`-flagged, so the dwarf-style `ClimbSpeedPatch`/`ClimbCollideAssistPatch` shape (which extends vanilla's own ladder detection) would never fire for it; only `TreeClimbingPatch`'s self-contained-scan shape generalizes. Two independent match groups, each its own toggle: `"log-grown"` (tree, same as Elf) and a config-driven raw-rock whitelist (`rock-`, `crackedrock-`, `meteorite-`, `stalagsection-` — four prefixes, not one, see Part A report A2). **No saturation cost** — researched and confirmed vanilla's own `EntityBehaviorHunger.SlowTick` has no `IsClimbing`-specific term at all, so this doesn't introduce an asymmetry against vanilla's free ladder climbing. **Confirmed working in-game (2026-08-14), part of the Goblin G2 pass.** |
| `RFGoblinTunnelBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-goblintunnel.json`, registered as `"rfgoblintunnel"` (`RFMechanicsModSystem.cs:40`) | `GoblinTraitCode` (checked in `IsGoblin()`, inline, not a Harmony patch) | `EnableGoblinTunnelSpeed`, `GoblinTunnelSpeedBonus` (0.15, bumped from 0.12 at G2.1 review), `GoblinTunnelStatWriteThreshold` | **New 2026-08-06 (Phase G2), earth-check unified 2026-08-06 (G2.1).** Walkspeed bonus for goblins tunneling under diggable earth — `RFTreeProximityBehavior`'s exact pattern (3s tick, not Thew/Band's 6s), but a narrow 2-block directional column check (`headY+1`/`headY+2`) instead of a radius `WalkBlocks` scan. Hysteresis: entry needs only the near sample, exit needs both to fail. Writes `Stats.Set("walkspeed", "tunneling", value)`. Condition delegates to the disabled `GoblinSpitPackingPatch.IsGoblinEarth` (see Disabled/superseded below — that class is unregistered but its static predicate is still called directly, not via Harmony) instead of maintaining its own `Code.Path` prefix list. **Confirmed working in-game (2026-08-14), part of the Goblin G2 pass.** |
| `ElfLeafDropPatch.cs` | `Block.GetDrops` (postfix, appends to `__result`) | `ElfTraitCode` | `EnableElfLeafGathering` | **New 2026-08-06 (Phase G2), duplication bug fixed 2026-08-06 (G2.1).** Appends a self-drop (`leaves-placed-{wood}`/`leavesbranchy-placed-{wood}`, grown-stage-to-placed conversion) to vanilla's existing `treeseed`/`stick` drops for elves breaking `leaves-`/`leavesbranchy-` blocks — appends, does not replace. **Naturally-generated leaves only**: gated on `!path.Contains("-placed-")`, since the original version also re-triggered on breaking an already-placed leaf block, letting elves compound leaves indefinitely by planting and re-harvesting. **Closes G1's open branchy-leaves ingredient-sourcing gap.** **Confirmed working in-game (2026-08-14), part of the Goblin G2 pass.** |
| `OreYieldPatch.cs` | `Block.GetDrops` (prefix) | `DwarfTraitCode` | `EnableOreCurve`, `OreThreshold`, `OreCeiling` | Depth-only ore yield bonus for dwarves, Ore-material only (server-side only — drops only spawn server-side). Stacks multiplicatively with any `oreDropRate` trait stat, doesn't currently share a value with one. |
| `ClimbSpeedPatch.cs` | `EntityBehaviorControlledPhysics.SetProperties` (postfix) | `DwarfTraitCode` | `EnableClimbSpeed`, `ClimbSpeedFactor` | Scales `climbDownSpeed` (the **Jump/ascend** field — vanilla's field names are inverted from their function) for dwarves. Ascent-only; `climbUpSpeed` (Sneak/descend) is always reset to base. Idempotent — recomputes from JSON base values rather than multiplying current state, so retry/listener/rejoin can all safely re-call it. |
| `ClimbCollideAssistPatch.cs` | `Block.OnEntityCollide` (postfix) | `DwarfTraitCode` | `EnableClimbSpeed` (shared toggle) | Covers the *other* ladder-ascent path — walking into a climbable block without Jump, which hard-sets `Motion.Y = 0.04` via a completely separate vanilla mechanism `ClimbSpeedPatch` doesn't touch. Ascent-only (only rescales when `Motion.Y > 0`). |
| `ClimbSaturationPatch.cs` | `EntityBehaviorHunger.OnGameTick` (prefix) | `DwarfTraitCode` | `EnableClimbSaturation`, `ClimbSaturationPerSecond` | Flat satiety cost per second of climbing (ascent only: `IsClimbing && Jump`), banked in `entity.Attributes["rf-climbseconds"]` and flushed every 10 real seconds. `ClimbSaturationPerSecond` default `2.4` — confirmed 2026-08-13 to match the live `rfmechanics.json` value, the earlier "was temporarily set to 200.0 for testing" loose end is closed. |
| `BranchyLeavesPassthroughPatch.cs` | `CachingCollisionTester.AssignToEntity` + `CachingCollisionTester.GenerateCollisionBoxList` (both postfix) | `ElfTraitCode` | `EnableBranchyLeavesPassthrough` | **New 2026-08-03.** Elves walk through branchy leaves instead of colliding with their solid sides. **Confirmed working in-game (2026-08-04).** |
| `RFTreeProximityBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-treeproximity.json`, registered as `"rftreeproximity"` (`RFMechanicsModSystem.cs:36`) | `ElfTraitCode` (checked in `IsElf()`, inline, not a Harmony patch) | `EnableTreeProximitySpeed`, `TreeProximityRadius`, `TreeProximityMaxBonus`, `TreeProximityStatWriteThreshold` | **New 2026-08-04.** Walkspeed bonus for Elves near living trees (`log-grown`-prefixed blocks). Writes `Stats.Set("walkspeed", "treeproximity", value)` — stacks additively with the `"trait"` source. Build-verified only — **not yet tested in-game.** |
| `TreeClimbingPatch.cs` | `EntityBehaviorControlledPhysics.MotionAndCollision` + `.ApplyTests` (both postfix) | `ElfTraitCode` | `EnableTreeClimbing` | **New 2026-08-04.** Lets Elves climb standing tree trunks as if they were ladders. **Confirmed working in-game.** |
| `FallDamagePatch.cs` | `EntityBehaviorHealth.OnEntityReceiveDamage` (prefix) | `ElfTraitCode`, `GoblinTraitCode` | `EnableFallDamageReduction`, `FallDamageReductionFactor`, `EnableGoblinFallDamageReduction`, `GoblinFallDamageReductionFactor` | **New 2026-08-04 (Elf), extended 2026-08-06 (Goblin).** Reduces fall damage for Elves by `FallDamageReductionFactor` (default 60%) and for Goblins by `GoblinFallDamageReductionFactor` (default 50%). **Elf path confirmed working in-game (2026-08-04); Goblin path smoke-tested 2026-08-06, not independently re-verified after the Goblin extension.** |
| `GoblinDarkvisionModSystem.cs` | Not a Harmony patch — client-only `ModSystem`/`IRenderer`, auto-discovered by the engine (not called from `RFMechanicsModSystem.Start()`), `OnRenderFrame` on `EnumRenderStage.Before` | `GoblinTraitCode` | `EnableGoblinDarkvision`, `GoblinDarkvisionStrength` | **New 2026-08-06.** Constant-strength (0.8) darkvision for Goblins, composes with vanilla night-vision goggles via `Math.Max`. **Smoke-tested 2026-08-06 (three goggle-compose cases passed).** |
| `ThewBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json`; also overrides `OnEntityDeath` | `OrcTraitCode` (checked in `IsOrc()`, inline, not a Harmony patch) | `EnableThew`, `OrcTraitCode`, `ThewGainPerHour`, `ThewGainBandMult`, `ThewRampFloor`, `ThewRampCeiling`, `ThewPerBite`, `BiteCooldownSec`, `BulkyHoldDecayPerHour`, `ThewDecayUnderfedPerHour`, `ThewDecayHungryPerHour`, `ThewDecayStarvingPerHour`, `ThewHungryThreshold`, `StarvationShieldWhileThew`, `ProteinGateLevel`, `SeasonalGainEnabled`, `SeasonalGainMultipliers`, `OrcStomachMultiplier`, `StomachStackingMode`, `EnableThewDeathPenalty`, `ThewDeathPenalty` | **New 2026-08-05, reworked twice same day.** Hidden per-player Thew float (0–1) for orcs. Gain: graded ramp × `ThewGainBandMult[currentBand]`, protein-gated. Decay below the ramp floor: three named tiers. **Original binary-gate version confirmed working in-game 2026-08-05; the reworked ramp/tier version is NOT yet re-verified** — see Testing status. |
| `BandBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json` (same file as `rfthew`), registered as `"rfband"` (`RFMechanicsModSystem.cs:38`) | `OrcTraitCode` (own `IsOrc()`, duplicated not shared) | `EnableBands`, `BandUpThresholds`, `BandDownThresholds`, `BandSizes`, `BandSizeLerpSeconds`, `HungerRateMult`, `WalkSpeedDelta`, `MaxHpExtraPoints`, `AnimalSeekingRangeDelta`, `BulkyMeleeDamageBonus`, `RangedAccDelta`, `BulkyArmorWalkSpeedAffectednessDelta`, plus two `_UNWIRED` reserved fields (see below) | **New 2026-08-05, Phase 3 Part B.** Lean/Standard/Bulky hysteresis state machine driven off `ThewBehavior`'s Thew value (up 0.35/0.70, down 0.30/0.64). Owns `entitySize` (lerps ~10s on cross, self-heals every slow tick if it drifts). Applies/clears, once per band cross, `Stats.Set` under source key `"rf-orc-band"`: `hungerrate`, `walkspeed`, `animalSeekingRange`, `meleeWeaponsDamage`, `armorWalkSpeedAffectedness`, `maxhealthExtraPoints`, `rangedWeaponsAcc`. **Corrected 2026-08-13**: this table previously listed a `BluntCrushResistDelta` config key and `bluntDamageFactor`/`crushingDamageFactor` stat writes — verified against current source (`RFMechanicsConfig.cs:325`, `BandBehavior.cs:185-191`) that the field is now `RangedAccDelta` and the stat written is `rangedWeaponsAcc`; `BandBehavior.Initialize()` (`:44-55`) explicitly removes the two old stats as one-time migration cleanup. The live deployed `ModConfig/rfmechanics.json` still has the old `BluntCrushResistDelta` key as of this pass — see `notes/diagnostics/config-versioning-phase-a-survey.md`. Two stats from the locked design table are still **NOT wired**: jump height and knockback — see `notes/race-mechanics/orc-phase3-partB-bands.md` Deviations. |
| `ThewEatPulsePatch.cs` | `EntityBehaviorHunger.OnEntityReceiveSaturation` (postfix) | `OrcTraitCode` | `ThewPerBite`, `BiteCooldownSec` | **New 2026-08-05 (Addendum 1).** Small flat Thew grant per qualifying eat event, cooldown-gated per player. **Not yet verified in-game.** |
| `ThewShieldPatch.cs` | `EntityBehaviorHealth.OnEntityReceiveDamage` (prefix) | `OrcTraitCode` | `StarvationShieldWhileThew` | **New 2026-08-05 (Addendum 2).** While an orc's Thew > 0, zeroes incoming `EnumDamageType.Hunger` damage. **Not yet verified in-game.** |
| `BurnBehavior.cs` | `EntityBehavior` (`OnGameTick`, `OnEntityReceiveDamage`), attached via `patches/seraph-thew.json`, registered as `"rfburn"` (`RFMechanicsModSystem.cs:39`) | `OrcTraitCode` (`IsOrc()`, inline) | `EnableBurn`, `BurnHealthFraction`, `BurnHealPerSecond`, `BurnThewPerHp`, `BurnThewFloor`, `BurnFastTickMs` | **Not previously in this table despite being live since Phase 4 — added 2026-08-13.** Below `BurnHealthFraction` of MaxHealth, an orc with Thew above `BurnThewFloor` burns Thew to heal rapidly via a temporary fast tick listener (`BurnFastTickMs`, default 500ms). **Shipped, but implements the superseded flat/threshold model, not the locked cubic-curve spec** — see Testing status below and `notes/race-mechanics/orc-phase4-burn-to-survive-design.md`. Needs a real code fix, not a doc fix. |
| `PreservedProteinPatch.cs` | `CollectibleObject.tryEatStop` (prefix) + `EntityBehaviorHunger.OnEntityReceiveSaturation` (prefix) | `OrcTraitCode` | `PreservedProteinItemCodes`, `PreservedProteinMultiplier` | **New 2026-08-05. Dormant** — the default item list has no obtainable production path in this install (config surface for modded preserved foods). |
| `GoblinRotAuraBehavior.cs` | `EntityBehavior` (`OnGameTick`, `OnEntityDespawn`), attached via `seraph-goblinrotaura.json`, registered as `"rfgoblinrotaura"` (`RFMechanicsModSystem.cs:41`) | `GoblinTraitCode` (`IsGoblin()`, inline) | `EnableGoblinRotAura`, `EnableGoblinRotAuraCarriedInventory`, `GoblinRotAuraTickInterval`, `GoblinRotAuraRadiusMin/Max`, `GoblinRotAuraVerticalHalfExtent`, `GoblinRotAuraIntensityAtMinRadius`, `GoblinRotAuraRateMultiplier`, `GoblinRotAuraHoldFraction`, `GoblinRotAuraWriteThresholdHours`, `GoblinRotAuraHoldCreepFactor`, `GoblinRotAuraHoldCreepFloorHours`, `GoblinRotAuraIntakeHalfLifeHours` | **New 2026-08-12 (Phase G3) — not previously in this table.** ~2s server-side sweep around the goblin: accelerates spoilage on food in nearby placed containers and (2026-08-12 extension) carried hotbar/worn-bag inventories, held just short of fully spoiled ("larder hold") rather than tipping over. Publishes an `AuraSource` via the static `GoblinRotAuraRegistry` for other consumers (crop stunting, below) to read. Intake-driven shape: reads dietsetup's rot-intake accumulator directly off `WatchedAttributes` (no assembly reference). Deployed to the live install; **not yet smoke-tested in-game.** |
| `GoblinCropStuntBehavior.cs` | `CropBehavior.TryGrowCrop`, registered as `"RfGoblinCropStunt"` (`RFMechanicsModSystem.cs:42`, `RegisterCropBehavior`) | Gated on aura presence, not a direct trait check | `EnableGoblinRotAura`, `CropStuntMinStrength` | **New 2026-08-12 (Phase G3) — not previously in this table.** Crops under a goblin's rot aura stop advancing growth stage (recoverable, never destroyed) — gated on `GoblinRotAuraRegistry`'s spatial falloff only, never on Intensity. Deployed; **not yet smoke-tested in-game.** |
| `GoblinRotEdiblePatch.cs` | `CollectibleObject.GetNutritionProperties` (postfix) | `GoblinTraitCode` | `EnableGoblinRotEdible`, `GoblinRotEdibleSatiety` | **New 2026-08-12 (Phase G3) — not previously in this table.** Grants `game:rot` a minimal `FoodNutritionProperties` for goblins only, so vanilla's eat pipeline (which gates solely on a non-null result) lets them eat it. Never overrides an existing non-null result. Deployed; **not yet smoke-tested in-game.** |
| `GoblinSpitChargeGrantPatch.cs` | `CollectibleObject.tryEatStop` (postfix) | `GoblinTraitCode` | `EnableGoblinSpitCharges`, `SpitChargesPerRot`, `SpitChargeCap` | **New 2026-08-12 (Phase G3).** Grants spit charges (capped) when a goblin finishes eating `game:rot`, gated on the same completion threshold (`secondsUsed >= 0.95f`) vanilla uses. **Confirmed working in-game (2026-08-14).** |
| `RfGoblinSpitRepairBehavior.cs` | `BlockBehavior.OnBlockInteractStart`, registered as `"RfGoblinSpitRepair"` (`RFMechanicsModSystem.cs:43`, `RegisterBlockBehaviorClass`) | `GoblinTraitCode` | `EnableGoblinSpitCharges`, `SpitRepairGain` | **New 2026-08-12 (Phase G3).** Lets a goblin spend a spit charge via empty-hand interact to repair a reparable block, mirroring vanilla's own `BehaviorReparable` repair-application math. **Confirmed working in-game (2026-08-14).** |
| `ElfIdentityBehavior.cs` | `EntityBehavior` (`Initialize`, `OnGameTick`), attached via `patches/seraph-elfidentity.json` **both sides**, registered as `"rfelfidentity"` | `ElfTraitCode` (own `RefreshElfCache`) | `ElfIdentityTickInterval`, `EnableElfHungerDrainReduction`, `ElfHungerRateMult` | **New 2026-08-17.** Replaces `ElfAttunementBehavior` (archived same day — see `archive/elf-attunement/ARCHIVED.md`) as the sole "is this player an elf" source: one cached `IsElf` bool, refreshed immediately in `Initialize()` and every `ElfIdentityTickInterval` thereafter. No float, no thresholds, no census. Also applies/clears the reduced-hunger-drain stat (`"hungerrate"`, source `"rf-elf-attunement"`, key name unchanged) unconditionally off `IsElf`, re-derived every identity tick rather than edge-triggered — self-heals a stat left behind by the old code without a migration pass. `RFElfZoomBehavior`, `BranchyLeavesPassthroughPatch`, and `RFTreeProximityBehavior` all read `IsElf` directly. |
| `ElfStepHeightBehavior.cs` | `EntityBehavior` (`Initialize`, `OnGameTick`), attached via `patches/seraph-elfstepheight.json` **both sides**, registered as `"rfelfstepheight"` | Reads `ElfIdentityBehavior.IsElf`, no direct trait check | `EnableElfStepHeight`, `ElfStepHeightValue` (1.0), `ElfStepHeightDefaultEnabled` | **New 2026-08-17.** Sets `EntityBehaviorControlledPhysics.StepHeight` to `ElfStepHeightValue` for elves, a plain field write (public field, no Harmony patch needed). Captures the entity's actual pre-existing `StepHeight` once in `Initialize()` as the restore value (not a hardcoded vanilla 0.6f) and only writes when the current value disagrees with the target. Per-player toggle in `WatchedAttributes["rf-elf-stepheight-enabled"]`, flipped via `/rfelfstepheight toggle` (bound to a client hotkey, default Ctrl+H, 200ms debounce). 1.0 is exactly `FindSteppableCollisionBox`'s threshold, so elves auto-climb fences/stair edges — this is why the toggle exists. |
| `RFMechanicsConfig.cs` | — | — | (all of the above, plus `DwarfTraitCode` default `"rf-dwarf-positive"`, `ElfTraitCode` default `"rf-elf-positive"`, `GoblinTraitCode` default `"rf-goblin-positive"`, `OrcTraitCode` default `"rf-orc-positive"`, and the `OrcBandTriple`/`OrcBandUpDown`/`OrcStomachStackingMode` helper types) | Shared config POCO. `_UNWIRED`-suffixed fields (`BulkyJumpHeightReduction_UNWIRED`, `StandardKnockbackTakenReduction_UNWIRED`) are reserved config surface only, not consumed by any code — see `BandBehavior.cs`'s row above. Confirmed unchanged 2026-08-13. |
| `RFMechanicsModSystem.cs` | — | — | — | **Corrected 2026-08-13, `rfrested` registration removed 2026-08-14 (Phase 0)** — the registration list below was previously incomplete. `Start()`: load config, register entity/crop/block behavior classes `rftreeproximity`, `rfthew`, `rfband`, `rfburn`, `rfgoblintunnel`, `rfgoblinrotaura`, `RfGoblinCropStunt`, `RfGoblinSpitRepair` (eight calls, `RFMechanicsModSystem.cs:35-43`), then `harmony.PatchAll(Assembly.GetExecutingAssembly())`. `StartServerSide()`: registers `/dwarfdepth`, `/rfdiag`, `/rfstatsfix`, `/rfphase0`, `/rfthew`, and (2026-08-12, previously undocumented here) `/rfrotdiag`/`/rfrotaura` — all server-side only, use the full `/name`, not the `.` shortcut. |

### Disabled / superseded

Logic intact on disk, not currently reachable at runtime — moved to `src/BugRace/` (namespace
`rfmechanics.BugRace`) during Phase G3's goblin extraction (2026-08-12), each carrying its own
"RE-HOMED, NOT DELETED" banner comment.

| File | Disabled since | Reason | What it would do if reactivated |
| --- | --- | --- | --- |
| `src/BugRace/GoblinDigModifierBehavior.cs` | 2026-08-12 (Phase G3) | `RegisterBlockBehaviorClass` call commented out at `RFMechanicsModSystem.cs:47` (own comment: "Phase G3: GoblinDigModifierBehavior re-homed to src/BugRace/ (future bug race), no longer registered for goblins"). Earmarked for a future bug race, not deleted. | Goblin dig bonus on diggable-earth blocks — bare-handed or holding anything but a shovel digs at `GoblinBareHandDigRate` (default 8.0), a held shovel drops back to 1.0. `notes/race-mechanics/goblin-dig-materials-handover.md` remains the authoritative record of the material-family/gate resolution history if this is reactivated. |
| `src/BugRace/GoblinSpitPackingPatch.cs` | 2026-08-12 (Phase G3) | `[HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]` commented out (`GoblinSpitPackingPatch.cs:52`) — `PatchAll` discovers patch classes purely via that attribute, so this is the exact equivalent of an unregistered behavior class. Its static `IsGoblinEarth` predicate is still called directly (not via Harmony) by `RFGoblinTunnelBehavior`, so the class isn't fully inert. | Converts every face-adjacent diggable-earth neighbor of a goblin's break to its spit-packed variant (10 mod-owned families), shovel-gated. **Downstream consequence, not independently verified this pass**: `goblin-phase-g2.2-as-built.md`'s wash-back barrel recipes (spit-packed → vanilla) have no fresh input to consume while this stays disabled — flagged in `notes/race-mechanics/README.md`. |

## Diagnostic commands (server-side only, use `/`, not `.`)

- `/dwarfdepth` — depth/altitude curve debug for the calling player.
- `/rfdiag` — dumps `extraTraits`, blended `walkspeed`/`hungerrate`, explicit dwarf-trait
  `HasTrait` checks, banked climb time, saturation, and now (2026-08-04) the `ElfTraitCode`
  `HasTrait` check and a per-source `walkspeed` breakdown (`trait`/`treeproximity`). The
  restedStr field printed here until 2026-08-14 (Phase 0) is gone — Rested was deleted, not
  a walkspeed source anyway (it only ever fed `miningSpeedMul`/`forageDropRate`/
  `wildCropDropRate`/`hungerrate`, source `"rested"` — this line was already wrong about
  which stat channel it fed).
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
  `notes/race-mechanics/goblin-phase-g1-as-built.md` for why the decoupling was necessary (PlayerModelLib
  couples `ModelSizeFactor` to collision by default) and the correction to the old
  lrracialtweaks-era "1-block traversal tested and working" claim (the math shows that era's
  standing collision height was still >1.0 — if real, it was sneak-tested, not standing).
  A live-install deployment gotcha surfaced during G1 smoke testing, worth remembering for
  any future phase: `VintagestoryData/Mods/raceframework` and `VintagestoryData/Mods/rfmechanics`
  are **plain copies**, not symlinks to the dev tree — a fresh `dotnet build` or JSON edit does
  **not** reach the live game until both are manually re-copied (game must be fully closed
  first for the `rfmechanics.dll` half, it's locked while running).
  **G2.1 design ruling on spit-packed material**: goblin-exclusive to *produce* (only a
  goblin's claws convert earth to spit-packed variants), but washable back to vanilla
  material by **any** race via a lossy water-barrel recipe — non-goblins have a reason to
  want goblin-made material, up to razing a warren for it. **Built 2026-08-08 (Phase G2.2)**:
  4 spit-packed in → 3 vanilla out per family, `waterportion` at `consumeLitres: 2`, no
  `SealHours` (instant). See `notes/race-mechanics/goblin-phase-g2.2-as-built.md` for the per-family
  vanilla-parent mapping (two families — `spitpackedcob`/`spitpackedforestfloor` — lost
  their grass-coverage axis at goblin-dig time and wash to a fixed grassless/soil output,
  mirroring vanilla's own `BlockSoil`/`BlockForestFloor` break-drop behavior, not a guess).
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
- **Rested (`RestedBehavior.cs`/`RestedBlockBreakPatch.cs`/`RestedToolUsePatch.cs`/
  `RestedDurabilityPatch.cs`) — deleted outright in Phase 0 of the Elf attunement work
  (2026-08-14), not gated to Elf-only.** It shipped race-agnostic (no trait gate anywhere in
  any of the four files) and was never race-specific, so the open design question below
  (gate to Elf, tie to tree/nature proximity) is now moot — attunement replaces it entirely
  rather than resolving it. Removing it also drops its four `Stats.Set` outputs
  (`miningSpeedMul`/`forageDropRate`/`wildCropDropRate`/`hungerrate`, source `"rested"`) for
  every race, not just Elf — see `notes/race-mechanics/phase0-playtest.md` for the
  before/after check this motivates for Dwarf/Orc/Goblin baselines.
  **2026-08-17 update: attunement itself is now also gone** (see below) — the premise
  Rested's deletion rested on ("attunement replaces it entirely") no longer holds for
  Dwarf/Orc/Goblin. Not restored, not re-raised — flagging only so the gap isn't rediscovered
  as if it were new.
- **Elf attunement — built, confirmed working in-game, then removed the same day
  (2026-08-17).** The 0-100 float, forest-presence gain/decay, and three threshold-gated
  effects (canopy standing, tree proximity, hunger drain) were scrapped in favour of
  always-on body traits. `ElfAttunementBehavior`/`ElfAttunementContext`/`ElfForestCensus`/
  `ElfForestCensusInvalidationPatch` archived to `archive/elf-attunement/` (excluded from the
  build via `<Compile Remove>` in `rfmechanics.csproj` — see `ARCHIVED.md` there). Replaced by
  `ElfIdentityBehavior` (identity cache) and a new `ElfStepHeightBehavior` (step height 1.0
  for elves, unrelated to attunement) — see their rows above. `/rfattune` and `/rfattuneset`
  are gone, not repurposed. Full investigation: `notes/race-mechanics/elf-attunement-removal-report.md`.
- `rf-elf-positive`'s `traits.json` attributes also carry three cosmetic-only entries
  (`rfFallDamageReduction`, `rfTreeClimbing`, `rfBranchyLeavesPassthrough`) added 2026-08-04
  purely so these three C#-only mechanics show up in the character-creation Traits tab
  alongside the real stat bonuses — `Trait` has no free-text description field
  (`reference/decompiled/VSSurvivalMod/Vintagestory.GameContent/Trait.cs`), so this was the
  only way to surface them there. They have matching `charattribute-*` lang keys but are not
  read by any code — `CharacterSystem.applyTraitAttributes` still calls `Stats.Set` on them
  unconditionally (harmless, just a few unused synced bytes on the player entity).

## Testing status

- **Goblin Phase G2 (2026-08-06) and Phase G3 spit charges (2026-08-12): confirmed working
  in-game (2026-08-14).** Covers `GoblinClimbingPatch.cs` (raw-rock + tree climbing),
  `RFGoblinTunnelBehavior.cs` (tunnel walkspeed bonus), `ElfLeafDropPatch.cs` (leaf
  self-drop), and the spit-charge pair `GoblinSpitChargeGrantPatch.cs` (grant on eating
  `game:rot`) + `RfGoblinSpitRepairBehavior.cs` (empty-hand repair spend). Supersedes the
  "build-verified only" / "not yet smoke-tested" status these carried since their original
  ship dates. Goblin dig speed and spit-packed earth conversion (`GoblinDigModifierBehavior`/
  `GoblinSpitPackingPatch`) are **not** covered by this confirmation — both are disabled/
  re-homed to `src/BugRace/` as of Phase G3 (see Disabled/superseded below) and were not
  part of what was tested.
- **Dwarf ore-song v1 (2026-08-14): built and compiled, not yet smoke-tested in-game.**
  `DwarfOreSongModSystem.cs` (client-only lookup table) + `RfDwarfOreSongBehavior.cs`
  (empty-hand knock on `rock.json` → scan + cluster + positioned playback), patched via
  `patches/dwarf-ore-song-behavior.json`. Not yet in the feature-inventory table above — add
  it there once in-game tested. See `notes/diagnostics/ore-song-discovery.md` and the
  implementation brief for design/verification detail, including a deviation from the
  brief's literal joint-ore-type instruction (verified against `ItemOre.cs` and the live
  install's actual ore JSON) documented in `DwarfOreSongModSystem.ResolveJointMaterial`'s
  doc comment.
- **Goblin Phase G3 rot aura, carried-inventory extension (2026-08-12): sweeps nearby
  players' hotbar and worn-backpack contents, not just placed `BlockEntityContainer`s.**
  `GoblinRotAuraBehavior.cs`: extracted the per-slot larder-hold math into a shared
  `AccelerateSlots` helper (also fixed the hold-ceiling write-back to be threshold-gated
  instead of unconditional — it was firing every tick forever for any stack parked at the
  ceiling, which is the steady state this mechanic is designed to produce), added
  `SweepCarriedInventories`, new `EnableGoblinRotAuraCarriedInventory` config toggle
  (default true), `GoblinRotAuraHoldFraction` default 0.95 → 0.85. **Known gap: a crock (or
  any other nested container item) carried inside a worn bag is not reached by this sweep**
  — its contents live in that crock's own `Attributes["contents"]` tree, not as inventory
  slots this walk visits (the same reason the placed-container sweep already can't see
  nested container contents either — `GetContainingTransitionModifierContained` was
  investigated and found to be the wrong hook for a bare carried stack; see the diagnostic
  reasoning in `AccelerateSlots`'/`SweepCarriedInventories`'s doc comments). Net effect:
  carrying preserves in a sealed carried vessel is currently *safer* than leaving that
  vessel in a placed container within aura range — both are unreached today, but a player
  could reasonably expect the two to behave the same. Accepted as a v1 gap; closing it
  requires recursive nested-container traversal, not attempted here. `dotnet build`: 0
  errors, same warning baseline. Deployed to the live install (game closed); not yet
  smoke-tested in-game.
- **Goblin Phase G2.2 (2026-08-08): wash-back barrel recipes, deployed, smoke test
  in progress.** 10 recipe entries in `washspitpacked.json`, one per spit-packed family
  (4 in → 3 out, 2L water consumed, instant craft). See `notes/race-mechanics/goblin-phase-g2.2-as-built.md`
  for the full per-family output mapping and the reasoning behind the two non-obvious
  outputs (`spitpackedcob` → `cob-none`, `spitpackedforestfloor` → `soil-low-none`, the
  latter matching vanilla `forestfloor`'s own break-drop code rather than any
  `forestfloor-*` variant). `dotnet build`: 0 errors, same 19-warning baseline.
  **First live test (2026-08-08) found `assets/rfmechanics/recipes/` and `lang/` had
  never been deployed to the live install at all** — both now copied over; the world
  needs a fresh load (recipes are asset-loaded once at world/save load, not hot-reloaded)
  before re-testing. Also surfaced a **pre-existing, now-fixed gap**: none of the 10
  spit-packed blocktypes ever had lang entries (since G1/G2), so they rendered as their
  raw untranslated key (`rfmechanics:block-spitpackedsand-peridotite`) in tooltips —
  fixed via `assets/rfmechanics/lang/en.json`, one wildcard entry per family matching
  vanilla's own `block-bonysoil-*`/`block-forestfloor-*` convention. See
  `notes/race-mechanics/goblin-phase-g2.2-smoke-test-checklist.md` for the full re-test plan.
- **Goblin Phase G2 (2026-08-06): six new mechanics across dig speed, climbing, tunnel
  speed, spit-packed earth conversion, and Elf leaf gathering** — see
  `notes/race-mechanics/goblin-phase-g2-partB-as-built.md` for the original smoke-test handoff checklist
  (now stale in a few specifics, see `notes/race-mechanics/goblin-dig-materials-handover.md` and G2.1
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
  `notes/race-mechanics/goblin-diagnostic-findings-g2.1.md` for the diagnostic this phase was grounded on.
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
  works in-game same day; see `notes/race-mechanics/goblin-phase-g1-handover.md`'s writeup for detail.
- `ThewBehavior`/`/rfthew`: **the original binary-gate version confirmed working in-game
  (2026-08-05, morning)** — orc detection via `extraTraits` re-derives correctly on a live
  race swap (no relog/fresh-character needed), `MaxSaturation` stomach multiplier applied
  exactly once (non-compounding, under the *old* single-multiplier design), Thew gain/decay
  ticked as designed, death penalty fired and clamped at 0, no Thew movement on non-orc. Full
  T1–T5 Phase 0 protocol also executed and gate-passed — see `notes/race-mechanics/orc-phase0-results.md`.
  **Everything since then (same day) is unverified** — the ramp/eat-pulse/stomach-stacking
  rework and the decay-tier/starvation-shield rework both happened *after* this confirmation,
  in response to exactly this kind of live testing surfacing the old design's gaps (a dead
  zone with no gain or decay, `setband` misfiring, the compounding stomach multiplier). See
  the two addenda in `notes/race-mechanics/orc-phase3-partB-bands.md` for what changed and why.
- **Phase 3 Bands (`BandBehavior.cs`) as a whole: build-verified only, effectively
  untested in-game.** One specific piece *is* confirmed: T2 self-heal (entitySize
  snapping back after a live race swap) was observed working correctly by Miles during this
  session. Nothing else on the B5 smoke-test checklist has been run yet — band stat
  application per band, the entitySize lerp visually, the fed/idle Thew net-rate signs, the
  Bulky↔Standard boundary oscillation, race-swap-away Stats cleanup, or the `setband` fix.
  A live saturation-fraction confusion during testing (Thew appearing "stuck" at a forced
  value) turned out to be the old ramp-floor dead zone working as designed, not a bug — see
  `notes/race-mechanics/orc-phase3-partB-bands.md` Addendum 2 for the fix that closed that gap. **Next
  session should start from `notes/archive/orc-phase3-smoke-test-checklist.md`**, treating its
  specific command-output examples as stale (written before both addenda) but its checklist
  structure as still the right shape.
- **Phase 4 (burn-to-survive): shipped, but implements the wrong design.**
  `BurnBehavior.cs` is live (build-verified, deployed) and matches the *superseded*
  flat/threshold model (`BurnHealthFraction=0.25` gate, `BurnHealPerSecond=0.75` flat,
  `BurnThewPerHp=0.012`), not the locked cubic-curve spec in
  `notes/race-mechanics/orc-phase4-burn-to-survive-design.md` (`BurnMaxHealPerSecond=1.5`,
  `BurnCurveExponent=3`, `BurnThewPerHp=0.03`, no activation threshold). See
  `notes/race-mechanics/orc-phase4-burn-to-survive.md` for the as-built record and the flagged mismatch —
  needs a real code fix in `BurnBehavior.cs`/`RFMechanicsConfig.cs`, not just a doc
  correction, before this can be considered done.
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
