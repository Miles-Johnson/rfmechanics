# rfmechanics — handover (as of 2026-09-06)

**Orc Band/Frenzy jump bonus zero-out, Frenzy debt to per-game-hour, Puff Burn/loss states,
Goblin final-item grant fix, trait guards (2026-09-06), built and deployed to the live install
(`tools/deploy-all.ps1`, commit `92b9e19`), not yet confirmed in-game.** `JumpHeightMulDelta` (`OrcBandTriple`) and `FrenzyMaxJumpBonus`
zeroed in `RFMechanicsConfig.cs` — both the coded default and the hand-edited live
`ModConfig/rfmechanics.json`, since a successfully-parsed config always keeps its stored value
over a new code default (same precedent as the `SmellParticlesFar`/`GoblinSpitFliesRadius`
self-heal notes elsewhere in this file). Jump bonuses are zero, not deleted — the config surface
and the `PModuleOnGround` math it feeds stay reversible without a patch.

`FrenzyThewPerSecond` is now dormant: `FrenzyBehavior` no longer reads it (kept in the config
class only so an already-loaded install doesn't drop the key on self-heal). Debt accrual now
reads `FrenzyDebtPerGameHour` (0.60, matching the old 0.005/real-second peak at 120 real
seconds per game hour) sampled from `entity.World.Calendar.ElapsedHours` each tick instead of
`deltaTime` — in-game-hour anchored like Thew's own gain/decay zones, immune to real-time
pausing or tick lag. Guarded against non-finite/backward calendar reads and inactive gaps: the
first sample after activation or a calendar rewind establishes a fresh baseline instead of
billing the gap.

`ThewBehavior.StateAttributeKey`/`OrcPuffModSystem` gained two new puff-cue states: 4 (actively
Burning, i.e. `BurnBehavior.Burning`) and 5 (net Thew loss this tick with no debt), read
alongside the existing 0-3. The old hardcoded particle template is gone — steam/smoke/burn
colors, opacity, rise speed, spread, gravity, and wind-affectedness are all config fields now
(`Puff*` block, ~24 new keys in `RFMechanicsConfig.cs`), also present in the live
`ModConfig/rfmechanics.json` post-deploy.

`GoblinSpitChargeGrantPatch` (rot → spit charges) and dietsetup's `RotIntakeAccrualPatch`
(standalone/meal/pie eating) were both rewritten from a single postfix reading `slot.Itemstack`
to a prefix that captures evidence (the pre-eat stack, its collectible, its count) and a postfix
that confirms the stack actually lost exactly one item before crediting anything — fixes a bug
where the *final* item in a stack (slot goes empty, or gets replaced by an eaten-stack result)
could be missed entirely.

Four `hasClass` guards added in `RFMechanicsModSystem.cs`'s diagnostic dump, gating the
dwarf/elf trait checks on a non-empty `characterClass` so an unassigned character doesn't read
as having a negative/positive trait it was never given.

Also fixed, different repo: the Elf stockpiling paragraph in
`notes/dietsetup-race-diet-design.md` claimed Elf "cannot stockpile" any preserved/spoiled food;
corrected against the actual rule priorities — fish/seed/nut rules at priority 30 retain full
satiety/nutrition and override the generic priority-25 preserved/spoiled penalty, so there is no
blanket stockpiling block.

Build: `dotnet build -c Release`, rfmechanics 0 errors/35 warnings, dietsetup 0/0 — same
warning baseline, no new warnings from this change. Validated first in a standalone harness (90
assertions: 62 compiled-code/API checks, 28 clock/state source-snippet checks) before touching
the live install. Deployed via `tools/deploy-all.ps1`, live DLL confirmed rebuilt (byte size and
timestamp both changed) and the hand-edited `ModConfig/rfmechanics.json` values (jump bonuses
zero, `FrenzyDebtPerGameHour` 0.60, the `Puff*` block) confirmed still present post-deploy.
**Not yet confirmed in-game** — particle appearance (colors/states 4-5) and the zeroed jump feel
are playtest-only checks.

---

**Race ability hotkeys consolidated from four to one (2026-09-02), built, not yet deployed
or confirmed in-game.** `orcsmellfocus`, `rfelfzoom`, `rfdwarforesong`, and `rfgoblinspit`
all defaulted to the same key (`GlKeys.V`) on the theory that races are mutually exclusive
per player, so a shared default wasn't a real conflict -- true internally, but Vintage
Story dispatches a keypress to every handler bound to a key regardless of mod, so a
third-party mod also bound to V (`slowwalkmod`) threw inside its own handler every time any
race pressed their ability key (`notes/slowwalkmod-orcsmell-hotkey-crash.md`). Replaced all
four with one hotkey, `rfraceability` (default `GlKeys.C`), registered and dispatched by
the new `RaceAbilityHotkeyModSystem`. **The four old code strings are retired permanently
and must never be reused** -- a removed hotkey code's `clientsettings.json` rebind entry is
orphaned forever, not cleaned up, so re-registering one of those strings would silently
resurrect a player's old rebind under new semantics. Moving off V avoids the slowwalkmod
collision as a side effect of the new default key; the collision itself is not fixed and
was not attempted.

Dwarf (ore song) and goblin (spit repair) stay discrete presses, now dispatched through a
`Dictionary<PlayerRace, Func<ICoreClientAPI, bool>>` in `RaceAbilityHotkeyModSystem`
keyed by cached race -- one ability per race is assumed, noted in a comment at the
dictionary. Orc (smell focus) and elf (zoom) stay held ramps, unchanged in mechanism
(`OrcSmellFocusModSystem.OnRenderFrame`, `RFElfZoomBehavior.OnGameTick` still poll raw key
state directly each frame/tick), just reading `"rfraceability"` instead of their own
dedicated code and gating on cached race instead of a fresh `RaceTraits.HasTrait` call.
`DwarfOreSongModSystem.TryTrigger` and a new `RFMechanicsModSystem.TryTriggerGoblinSpit`
(extracted from the old `RegisterGoblinSpitHotkey` closure) dropped their own race checks
entirely -- the dispatcher already gates on cached race before calling either, so a second
fresh check would have contradicted the point of routing everything through the cache.
Server-side `RegisterGoblinSpitCommand` keeps its own fresh `RaceTraits.HasTrait` check
unchanged, since that's a trust boundary on a client-triggered command, not a UX cost.

The race cache itself (`ElfIdentityBehavior`, renamed `PlayerRaceBehavior`) generalized
from a single `IsElf` bool to a `PlayerRace Race` property (checked against all four trait
codes on its existing tick-refresh cadence; `IsElf` kept as `Race == PlayerRace.Elf` so the
three unrelated consumers -- `BranchyLeavesPassthroughPatch`, `ElfStepHeightBehavior`,
`RFTreeProximityBehavior` -- needed no logic changes, only the generic type argument at
their `GetBehavior<T>()` call). Registration key stays `"rfelfidentity"` (a JSON
entity-behavior attachment key, not user-facing) so `seraph-elfidentity.json` needed no
change. Elf step-height toggle (Ctrl+H) is untouched -- different key, different mechanism,
out of scope for this consolidation.

Version bumped to `0.1.2-test.1`. `dotnet build -c Release`: 0 errors, 35 warnings, all
pre-existing (verified by location -- none fall on a line this change touched or in either
new file). **Not verifiable without playing the game:** whether holding C as an orc/elf
actually ramps/zooms, whether pressing C as a dwarf/goblin actually triggers its ability
(and respects its existing cooldown/debounce), and whether pressing C as a human (or any
race with no ability) correctly falls through to `false` without eating the keypress. A
clean build proves the dispatch table compiles and loads; it proves nothing about whether
the race→ability mapping fires the right ability at runtime.

**Orc Band entitySize mesh-rebuild spam, fixed same day as the Thew/Band rework below
(2026-08-24), built and deployed, awaiting in-game confirmation.** The Thew/Band rework's
continuous Thew-derived `entitySize` glide (`BandBehavior.ComputeTargetSize`, rate-capped by
`SizeChangeRatePerSecond`) wrote `WatchedAttributes.SetFloat("entitySize", ...)` on almost
every server tick, since the target moves continuously now instead of only on rare band
crossings. Each write fired PlayerModelLib's `entitySize`-watched-attribute listener
(`PlayerSkinBehavior.OnModelSizeAttrChanged` → `ReplaceEntityShape()` →
`entity.MarkShapeModified()`), causing a full client mesh dispose/re-tesselate/re-upload on the
next render frame, plus an eye-height/collision-box recompute via
`RFMechanicsModSystem.TryUpdatePmlEntityProperties`'s reflective `UpdateEntityProperties()`
call. At ~30 ticks/sec this read as near-constant model twitching -- not a literal animation
replay, no `StartAnimation`/`AnimManager` call exists anywhere in this mod.

Fixed by quantizing the *written* value to a grid (new `EntitySizeWriteThreshold` config field,
default 0.01) instead of gating the write on delta-from-last-written -- a delta-gated approach
was tried and rejected: under ordinary drift `SizeChangeRatePerSecond`'s per-tick rate cap
already exceeds the target's own per-tick movement (confirmed: `ThewDriftPerHour`/
`ThewGainPerHour` 0.0025/in-game-hour, `ComputeTargetSize`'s Lean-Standard slope 0.629), so the
internally-stepped size snaps to target almost every tick regardless of any delta threshold,
and that gate never actually engages. `StepSizeTowardTarget` now tracks the continuous glide in
a new private `pendingSize` field (decoupled from `lastKnownSize`, which now means "last value
actually written") and only calls `SetFloat`/`TryUpdatePmlEntityProperties` when
`Math.Round(pendingSize / q) * q` crosses a new grid line. `SelfHealEntitySize` quantizes the
same way before writing, to avoid a self-heal immediately triggering a second unquantized-vs-
quantized write on the very next tick.

**Known caveat, not fixed, note for future sessions:** `StepSizeTowardTarget` runs
unconditionally every tick, while `SelfHealEntitySize` only runs inside the 6-second
`BandTickInterval`-gated block later in the same `OnGameTick`. If something external changes
`entitySize` between self-heal checks, the stepper's own next-tick write will already overwrite
it and resync `lastKnownSize` before self-heal ever sees the mismatch. Harmless for
correctness (the stepper writes the right value regardless), but the self-heal warning log is
no longer a reliable signal that nothing external touched `entitySize` -- silence doesn't prove
it. **Also still open, unrelated to this fix:** `ComputeTargetSize`'s low-end extrapolation
gives `entitySize` ~0.680 at Thew 0, smaller than a vanilla human -- an unresolved design
question, not addressed here. `dotnet build -c Release`: 0 errors, 36 warnings (same baseline).
Redeployed to the live install (`Mods/rfmechanics/rfmechanics.dll`, confirmed byte-identical
post-copy), game closed at deploy time. **Not yet confirmed in-game** -- verification plan
(steady-drift test expecting zero writes over ~2 minutes since one grid crossing takes ~12.7
real minutes at the default quantum, plus a catch-up test via `/rfthew set`) is in
`C:\Users\Kjol\.claude\plans\fix-orc-entitysize-mesh-rebuild-spam.md`.

**Orc Smell — flat far-range particle floor (2026-08-24, same-day follow-up to the smell-size
pass above): built and deployed, not yet confirmed in-game.** User wanted a hard guarantee that
an animal at the edge of detection range reads as 1-2 particles, growing to the existing dense
look on approach — previously `SmellParticlesFar` was one term in a multiplicative chain
(`countBase * spreadRatio * countSizeFactor`), so a large animal or one whose radius sat inside
the spread-widen curve could show 3-5+ particles at its own max range instead of a flat floor.
`OrcSmellModSystem.EmitJet` now computes `density = SmellParticlesFar + growth`, where `growth`
alone carries the size/spread/falloff scaling — the floor is additive, not blended, so every
source shows exactly `SmellParticlesFar` at zero scent strength regardless of size. Also fixed a
second bug this surfaced: `OnGameTick`'s shared `SmellMaxParticles` budget is handed out
closest-source-first (sources are strength-sorted), so nearby jets could exhaust the budget
before reaching the farthest source, starving its floor to zero — worst case with defaults
(6 sources x 90/source cap = 540) exceeds the 400 budget, so this was reachable, not
theoretical. Fixed by reserving each not-yet-processed source's floor before handing out the
rest of the budget each iteration (safe as long as `SmellMaxParticles >= SmellParticlesFar *
SmellMaxSources`, true by a wide margin at current defaults). `SmellParticlesFar` default
lowered 3 -> 2 in `RFMechanicsConfig.cs`; **live `ModConfig/rfmechanics.json` also hand-edited**
(3 -> 2) since a successfully-parsed config always keeps its stored value over the new code
default — the code-only edit would have been a no-op in-game, same precedent as the
`GoblinSpitFliesRadius`/`RangedAccDelta` self-heal notes elsewhere in this file. `dotnet build -c
Release`: 0 errors, 36 warnings (down from the long-standing 37-warning baseline — not
investigated, no warnings touch this file). Redeployed to the live install (DLL confirmed
byte-identical post-copy), game closed at deploy time.

**Orc Thew/Band/Burn/Frenzy full rework (2026-08-24): all 7 phases built and deployed, not yet
confirmed in-game.** Full as-built detail, including the day-length-invariance fix and the
`jumpHeightMul`-additivity confirmation, in
`notes/race-mechanics/orc-thew-rework-2026-08-24-as-built.md` — start there, not here. Summary:
deleted the eat-pulse patch, the starvation shield, the Bulky hold-decay, the sated-non-protein
decay, and Frenzy's melee-damage bonus; replaced Thew's ramp/tier gain-decay model with three
flat satiety zones anchored to `world.Calendar.ElapsedHours` (in-game hours, not real hours --
a real fix, the old code was genuinely real-hour-anchored despite its own docs); replaced
death's flat Thew penalty with a pull-down-only reset cap; added a Thew-debt system so Burn/
Frenzy no longer spend Thew directly (new `ThewDebtRepayPatch.cs`, `BurnDebt`/`FrenzyDebt` on
`ThewBehavior`); rebuilt Frenzy as a passive satiety-driven ramp (no more health-fraction
activation) granting `jumpHeightMul` as well as walkspeed; replaced Bands' entitySize lerp with
a continuous Thew-derived size under a real-second rate cap (`BandBehavior.ComputeTargetSize`);
added a client-side "puff" particle cue driven by a new `rf-orc-state` watched byte
(`OrcPuffModSystem.cs`); and gave `OrcSmellModSystem` a hunger-scaled range bonus plus a
128-block hard cap matching the server's own entity-tracking cutoff. This entry supersedes the
Thew ramp/tier and Bands rows below and in `notes/race-mechanics/README.md` -- none of the
superseded rows' "build-verified only" caveats carry forward as separately-tracked open items.

**Orc Band jump height (2026-08-23): built and deployed, not yet confirmed in-game.** New
`JumpHeightMulDelta` `OrcBandTriple` in `RFMechanicsConfig.cs` (Lean +2.0, Standard +1.0,
Bulky 0.0), wired into `BandBehavior.ApplyBandStats`/`ClearBandStats` under the existing
`"rf-orc-band"` source. Sets vanilla's `jumpHeightMul` stat, which turns out to scale jump
height *linearly* with the blended value itself (`PModuleOnGround.cs`: velocity is
multiplied by `sqrt(blended)`, so height ∝ velocity² ∝ blended) — so blended 3.0/2.0 give
Lean/Standard 3x/2x base jump height respectively. This is an *increase*, which passes
straight through the stat's `MathF.Max(1f, blended)` floor with no patch needed, unlike the
long-standing `BulkyJumpHeightReduction_UNWIRED` field (still correctly left unwired — a
reduction below 1.0 needs a Harmony patch on `PModuleOnGround.DoApply` that hasn't been
built). `dotnet build -c Release`: 0 errors, 37 warnings (same baseline). Redeployed to the
live install, game closed at deploy time.

**Tuning pass — Goblin flies, Orc seasons/floor/smell (2026-08-23): built and deployed, not
yet confirmed in-game.** Four config-driven fixes from user feedback on live play:
`GoblinRotFliesHalfLifeHours` 4.0 → 48.0 and `GoblinRotFliesCap` 1.0 → 5.0 (matches the visible
fly signal's decay to `dietsetup:rotIntake`'s 48h aura, raises the cap so a heavy- vs. light-rot
eater stay visually distinct); `SeasonalGainEnabled` false → true with `Fall` 1.4 / `Winter` 0.6
(Spring/Summer untouched) so orcs bulk before winter and lean out through it; `SmellRangeBase`
120.0 → 60.0 so `OrcSmellModSystem.DetectSources`'s range reads as size-driven
(`SmellRangePerSize`, unchanged at 40.0) rather than a flat tracker. One code change:
`ThewBehavior.cs` gained a `rf-orc-thew-initialized` sentinel (`entity.Attributes`, set once
ever) and a new `ThewCreationFloor` config field (0.4) applied the first tick an entity is ever
detected as orc, so a brand-new orc starts at Standard instead of spending ~3.5 in-game hours in
Lean at `ThewGainPerHour` 0.1. `dotnet build -c Release`: 0 errors, 37 warnings (same baseline).
Redeployed to the live install, game closed at deploy time.

**Orc Smell — particle size now scales with prey size (2026-08-23), same-session follow-up to
the tuning pass above, not yet confirmed in-game.** `OrcSmellModSystem.EmitJet` now sets the
particle quad's `MinSize`/`MaxSize` per source instead of the old fixed `0.12f` template
constant, using the same `size = e.Properties.CollisionBoxSize.X` value `SmellRangePerSize`/
`SmellThicknessDegPerSize` already consumed. Four new config fields: `SmellParticleSizeBase`
(0.10), `SmellParticleSizePerSize` (0.05), `SmellParticleSizeMin` (0.08, floor so small prey
doesn't shrink to an illegible speck), `SmellParticleSizeMax` (0.28, ceiling so large prey
doesn't blow up into a blob). `dotnet build -c Release`: 0 errors, 37 warnings (same baseline).
Redeployed to the live install, game closed at deploy time. Deploy verified byte-identical
against the build output post-copy. Live `ModConfig/rfmechanics.json` has since gone through a
load/self-heal cycle (game launched) with all 4 new `SmellParticleSize*` keys present and
values intact — confirms the schema change loads cleanly, **not** evidence of the visual effect
itself being smoke-tested yet.

**Orc Wild-Animal Resist — built and deployed (2026-08-23), not yet confirmed in-game.**
Standalone Harmony prefix (`OrcWildAnimalResistPatch.cs`) on `EntityBehaviorHealth.
OnEntityReceiveDamage`: a low-health, unarmored orc takes reduced damage from wild-animal
attackers, computed from health alone via a curve that's continuous at its activation
threshold (`OrcWildResistActivationHealthFracGap`, default 0.5). Any of the 3 vanilla armor
slots zeroes it. Wild-animal classification is a deliberate duplicate of
`OrcSmellClassifier.IsSmellableFauna` (same `EntityBehaviorHarvestable`+`creatureDiet` check),
not a shared call. An earlier plan to also decouple Frenzy's speed/melee bonuses from Thew was
reversed mid-review — `FrenzyBehavior.cs`/`ThewBehavior.cs` are untouched by this pass; the
resist is intentionally standalone specifically because Frenzy requires Thew to activate, and
this exists for the Thew-starved orc Frenzy can't help. New config block in
`RFMechanicsConfig.cs` (5 keys), `/rfthew dump` gained a fourth diagnostic block. Full detail:
`notes/race-mechanics/orc-wild-animal-resist-as-built.md`.

**Orc Smell v1 — built (2026-08-23), not yet confirmed in-game.** Client-only particle effect:
an orc gets a vague direction/rough-distance sense of nearby fauna via a drifting band on a shell
around the *player* (never drawn at the source). New files `OrcSmellModSystem.cs`/
`OrcSmellClassifier.cs`, new `// ── Orc Smell (v1) ──` config block (17 keys) in
`RFMechanicsConfig.cs`. Built from scratch, not a port — a prior brief claimed a "Phase-2 smell
implementation" existed to replace; exhaustive search confirmed it never did. Six real bugs found
and fixed in review before any in-game test (particle density inverted against spread, vertical
scan range 4x too generous, exception handling that logged "disabling" without disabling anything,
tick-cadence read from a possibly-null config at registration time, plus two comment/behavior
claims independently verified correct against decompiled 1.22 source). Full detail, all six fixes,
and what to preserve if this gets substantially reworked: `notes/race-mechanics/orc-smell-v1-handover.md`.
Start there, not here.

**Goblin Phase G4 (rot flies) — built (2026-08-22), supersedes the design-only entry directly
below.** Two fly populations. `GoblinSpitChargeGrantPatch.cs` gained a second write beside the
existing charge grant: `rfmechanics:rotFlies`/`rotFliesUpdatedHours` (decay-then-add, 4h
half-life, `+0.34`/rot capped at 1.0 — a sibling signal, never reads `dietsetup:rotIntake`,
which sits near steady-state 0.5 for any imperfectly-fresh food and can't express "ate no rot").
`GoblinRotFliesShared.cs` holds the live-decay reader and the client-side goblin scan both
populations share.

Aura flies (`GoblinAuraFliesModSystem.cs`): vanilla `EnumParticleModel.Quad` particles via
`RegisterAsyncParticleSpawner`, no custom renderer. Count from `rotFlies` (10-150), radius from
`dietsetup:rotIntake` through `GoblinRotAuraBehavior.ComputeShape` (exact match to the invisible
aura's own footprint) — rejection-sampled into the cylinder, density-weighted by a falloff shaped
like `GoblinRotAuraRegistry.SpatialFalloff` but with the horizontal edge pushed 15% out so the
boundary isn't a hard line. Per-goblin centroid lag (2.0s time constant, live-tunable via
`/rfflieslag`) and a breathing radius (10%, 20s period, phase offset from `entityId`).

Spit flies (`GoblinSpitFliesModSystem.cs`): exact count (0-6, == `spitCharges`, no floor/scaling)
needs precision particles can't guarantee, so this is a real `IRenderer` — one `QuadMeshUtil`
quad uploaded once, per-instance matrix, camera-facing via `RiftRenderer`'s technique (translate
into camera-relative space, `ReverseMul` the camera matrix, zero the rotation columns), own
minimal shader (`assets/rfmechanics/shaders/rfspitflies.vsh/.fsh` — texture sample + tint +
opacity, none of Rift's framebuffer-sampling distortion), registered at `EnumRenderStage.AfterBlit`
matching `GoblinDarkvisionModSystem`'s convention. Depth test left at its default (on) — never
toggled. Mesh is uploaded once in the constructor and never rebuilt; only the per-instance
model-view matrix and the `opacity` uniform change per frame, and shader `Use()`/texture
bind/blend-toggle happen once per frame outside the per-fly loop, not per-instance. Texture is a
new hand-drawn 16x16 `assets/rfmechanics/textures/entity/rotflies/fly.png`, loaded standalone via
`GetOrLoadTexture` (confirmed via the decompiled `ClientMain.GetOrLoadCachedTexture` that this
path passes `generateMipmaps=false` — atlas insertion would have forced mipmaps and faded this
texture at distance, per the earlier rendering-findings pass). Polls `spitCharges` every frame
(not event-driven) so charges present at login/relog show immediately; per-fly fade in/out over
0.4s on join/leave.

**Distance re-tuned same day (third pass)**: `GoblinSpitFliesRadius` 0.6 -> 4.5,
`GoblinSpitFliesVerticalExtent` 0.9 -> 0.45, matching More Bugs' own `RotPlayerFlyRoamRadiusBlocks`/
`RotPlayerFlyVerticalRangeBlocks` defaults (4.5 / 0.45, both unmodified in the live
`ModConfig/morebugs.json`) — decompiled `RotFlySource.cs`/`RotFlyAgent.cs` via `ilspycmd` to
confirm those two fields are literally the polar roam radius and vertical jitter band around
`home` for the player-carried ("holding rot in inventory") fly population, not just plausibly-named
fields. Reference only, per the original build prompt's scope — no code dependency on More Bugs.
Live `ModConfig/rfmechanics.json` updated to match (its stored values silently override the C#
defaults on load, same as the `BluntCrushResistDelta`/`EnableChunkScarTracker` precedent
elsewhere in this doc, so the code-default edit alone would have been a no-op in-game).

**Frame cost**: not measured against a live client — no second client was available this session
(see the verification note below). Reasoned estimate: at max charges with two goblins in view,
that's at most 12 draw calls/frame, each a 4-vertex/6-index quad with one matrix + one float
uniform update against an already-bound texture and an already-`Use()`'d minimal shader; this
should be well under the noise floor of a frame budget on any hardware capable of running the
game at all. Treat this as a reasoned estimate pending an actual in-game check, not a
measurement.

**Step 1 (cross-client `WatchedAttributes` verification) was not empirically completed** — no
second client was available. A temporary client-side diagnostic command,
`TempFlyCheckDiag.cs` (`/rfflycheck <playername>`), was built and deployed specifically to make
that check trivial once a second client exists; it reads a named player's `characterClass`/
`dietsetup:rotIntake`/`rfmechanics:spitCharges` off *this* client's own synced state, no server
round-trip. Proceeding past this gate was a deliberate, user-approved call based on strong
indirect evidence instead: `WatchedAttributes` is a generic `SyncedTreeAttribute`, broadcast via
the same dirty-path-diff mechanism (traced through the decompiled `ServerPackets`/
`PhysicsManager`) that already carries `onHurt`/`entityDead`/nametag data to every client tracking
an entity — visible every time you've ever seen another player get hurt or die in vanilla
multiplayer. `dietsetup:rotIntake` and `rfmechanics:spitCharges` ride the identical mechanism, no
namespace-based branching exists in the sync path. **Run `/rfflycheck` for real before trusting
this in a shared session** — if it comes back `<null/default>` for any of the three keys, this
whole feature needs a broadcast packet instead, per the original build prompt's own stop
condition. Delete `TempFlyCheckDiag.cs` once confirmed either way.

New config: `GoblinRotFlies*`/`GoblinSpitFlies*` block in `RFMechanicsConfig.cs`, reusing the rot
aura's own `GoblinRotAuraRadiusMin/Max`/`VerticalHalfExtent` rather than duplicating them. Master
toggles `EnableGoblinRotFlies`/`EnableGoblinSpitFlies`, both default true. New diagnostic commands
below.

Build: `dotnet build -c Release` after deleting `bin\Release\Mods\`, clean (0 errors). Deployed to
the live install.

**Goblin Phase G4 (rot flies) — design/investigation handover only, no code written
(2026-08-22).** Player wants goblins to grow a visible fly swarm scaled by how much rot
they've eaten. Full handover, including the already-existing rot-intake pipeline to reuse
(`dietsetup:rotIntake` → `GoblinRotAuraBehavior.ReadLiveRotIntake`), a More Bugs mod
decompile writeup (inspiration only, not a dependency — its relevant classes are `internal`
and local-player-only), an unverified cross-client `WatchedAttributes` risk that must be
checked before writing any render code, and three design options (none chosen yet — needs a
user decision on effort/fidelity and self-view vs. all-observers visibility) at
`notes/race-mechanics/goblin-phase-g4-rotflies-handover.md`. Start there, not here.

**Chunk scar tracker: elf-buff wiring planned, approved, then cancelled before implementation
(2026-08-22).** A design to scale forage yield (`forageDropRate`), wild-crop yield
(`wildCropDropRate`), and tree-proximity walkspeed by a continuous multiplier derived from the
log-break scar count was fully planned (own-map-chunk-only read, a compensating second Stats
source to shrink forage/wild-crop's trait-file bonus without touching the trait file itself, a
`CharacterSystem.TraitsByCode` read to get the trait's flat delta as a known constant, linear
curve to a floor) and approved, then cancelled on review: this scope belongs in a future
standalone mod, not rfmechanics. **No `ElfChunkScarBuffBehavior` was written, no new Stats
source exists, `RFTreeProximityBehavior` is untouched.** The full cancelled design is preserved
in `notes/race-mechanics/chunk-scar-archived.md` for whoever eventually builds that standalone
mod.

The tracker itself (`ChunkScarTracker.cs`/`ChunkScarBreakPatch.cs`) stays registered and active
as a passive data collector with no consumer by design — same relocation precedent as the
goblin dig-speed/spit-packed-earth extraction (`src/BugRace/`, Phase G3): defuse references,
don't delete code. `ChunkScarTracker.cs`'s header now carries an explicit "no gameplay consumer
by design" boundary marker so a future session doesn't mistake a live tracker for an unfinished
feature and wire it up without reading the archived doc first.

One config field renamed: `EnableChunkScarTracker` → `ChunkScarTrackingEnabled` (same default
`true`, same semantics — gates `ChunkScarBreakPatch`'s write path only, never `/rfscar`'s reads).
**Self-healed, confirmed 2026-08-23**: the live `ModConfig/rfmechanics.json` now has
`ChunkScarTrackingEnabled: true` and no `EnableChunkScarTracker` key — the same load/store
self-heal the `RangedAccDelta`/`BluntCrushResistDelta` rename already went through (see
`notes/race-mechanics/README.md`'s footnote 1). No risk was ever realized here: the live value
was already `true`, matching the new default.

Build: `dotnet build -c Release` after deleting `bin\Release\Mods\` — 0 errors, 37 warnings,
same baseline as every prior entry in this file. `python tools/docs-check.py` run clean against
this change (same 35 pre-existing findings as before this pass, all unrelated to chunk scar —
see the tool's own caveat about bulk-touched mtimes from the 2026-07-29 reorg).

**Chunk scar tracker hardening (2026-08-19, still diagnostic only, no gameplay effect).**
Closes gaps found while re-reading the 2026-08-18 build against the decompiled 1.22 source
before trusting it for a play session. No race gate, no buff scaling, no HUD added — none of
this changes what the tracker is, only whether its numbers can be trusted.

Confirmed by reading `reference/decompiled/1.22` source (file:line cited in-code):
- `IBlockAccessor.GetMapChunk` returns `null` (not a sentinel, no throw) for an unloaded chunk —
  `ServerWorldMap.cs:235-239`.
- `IMapChunk.SetModdata`/`RemoveModdata` already call `MarkDirty()` internally —
  `ServerMapChunk.cs:205-212`, `:238-242`. The tracker's own explicit `MarkDirty()` calls are
  redundant, kept as belt-and-suspenders rather than relying on an implementation detail of a
  type this mod doesn't own.
- `Block.OnBlockBroken` fires from both the client (`ClientMain.OnPlayerTryDestroyBlock`, hit on
  every ordinary survival break, not just Creative) and the server
  (`ServerSystemBlockSimulation.cs:606`), with no engine-side side guard — confirming
  `ChunkScarBreakPatch.Prefix`'s existing `world.Side != EnumAppSide.Server` check is
  load-bearing, not defensive boilerplate; removing it would double-count every break in
  singleplayer the same way the `PatchAll` bug did.
- Grown/placed gate coverage: `log-*-grown-*` (`log.json`) is caught, confirmed directly at
  `BlockLog.cs:18`. `bamboo-grown-*` (`BlockBamboo.cs:48-51`) is **missed** — its code doesn't
  start with `"log-"`. Fern trees (`BlockFernTree`) have no grown/placed variant in code at all,
  so no string-match gate can catch them regardless of prefix list. **Documented, not fixed** —
  widening what counts as "trunk" is a design call for when a buff curve is actually being
  built, not now. Open question, unresolved either way: whether a `log-resin-*` block (the
  `type` variant also has a `"resin"` value, `ForestFloorSystem.cs:279`) is a naturally-grown
  log that lost its `"grown"` type and would silently fall out of the gate.

Confirmed by building (`dotnet build`, 0 errors, 37-warning baseline unchanged — new code added
zero new warnings):
- `ChunkScarBreakPatch.Postfix` now re-checks the block at `pos` against the pre-break `__state`
  before recording — Postfix runs even if some other system (a block behavior, a protection mod)
  cancelled the break inside `OnBlockBroken`, and without this check that would still count a
  tree that's still standing.
- `ChunkScarTracker`'s reads are now tri-state (`ChunkScarCellStatus`: `Unloaded`/`AbsentKey`/
  `Value`) instead of collapsing "chunk not loaded" and "key never written" into the same
  zero-count instance. `/rfscar here`/`around` print `U`/`.`/`never recorded` instead of ever
  showing a `0` for either case — a real decayed-to-zero value still prints `0`.
- `/rfscar bench` now runs an untimed warmup pass before the timed 1000x loop, and reports how
  many of the sampled cells were `Unloaded` (a cheap read that would make the timing look
  artificially fast) — counted once after the timed loop, not per-iteration, so the counting
  logic itself doesn't pollute the number being measured.
- `/rfscar rate` (new): raw count, first/most-recent break hour, elapsed hours between, reported
  separately for scar and leaf. Needed a schema addition — `ChunkScarData.FirstWriteHours`
  (`[ProtoMember(3)]`), set only on the raw stored `Count`'s 0→1 transition in `RecordBreak`.
  Rule: this is the **raw stored count, not the decayed display value** — `ComputeDecayed` is
  read-only and never writes back, so raw `Count` is 0 only right after `/rfscar reset` or
  before any write has ever happened; a fully-decayed chunk keeps its true `FirstWriteHours`.
- `/rfscar selftest` (new): writes a sentinel to a dedicated key
  (`ChunkScarTracker.SelfTestKey`), reads it back via the same `SetModdata<T>`/`GetModdata<T>`
  path the tracker uses, reports pass/fail plus the raw payload's byte length (via `IMapChunk`'s
  `byte[] GetModdata(string)` overload), then cleans up its own key. Also reports
  `ChunkScarBreakPatch.CountOwnPatches` — this mod's own prefix/postfix count on
  `Block.OnBlockBroken` via `Harmony.GetPatchInfo`, owner-filtered so other mods' patches on the
  same method don't skew it. Expected `1/1`; if it ever prints `2/2`, the `PatchAll` double-patch
  bug is back and every number collected that session is suspect — catchable before collecting
  data, not after.

**Not verified at all this pass**: whether `BlockLogSection` (`log-section-*-grown-*`) is
actually caught by the `"log-"` prefix gate — inferred from a third-party patch's path only, no
literal code string was found to confirm its block-code convention matches `log.json`'s.

**Still not confirmed in-game** (unchanged from 2026-08-18): unload/reload persistence,
server-restart persistence, `/rfscar bench`'s actual numbers, and now also the new `rate`/
`selftest` commands and the tri-state display. `/rfscar selftest`'s patch-count check exists
specifically to de-risk the very first thing to check when the in-game session starts.

---

**Chunk scar tracker (2026-08-18, diagnostic only, no gameplay effect).** `ChunkScarTracker.cs`
(storage/decay), `ChunkScarBreakPatch.cs` (Harmony prefix/postfix on `Block.OnBlockBroken`),
`/rfscar` (`RFMechanicsModSystem.cs`, `here`/`around`/`bench`/`rate`/`selftest`/`reset`). Built
to measure four assumptions
before any real mechanic depends on them: does `IMapChunk` moddata survive unload/reload and a
full server restart, what a 3x3 neighbour read costs, and whether VS tracks player-placed state
for logs. Storage is `IMapChunk.SetModdata<T>`/`GetModdata<T>` (confirmed present on the 1.22.6
API via `reference/decompiled/1.22`, not upstream master), key `"rfmechanics:scar"` for logs and
a second independent key `"rfmechanics:scarleaf"` for leaves (never merged). `ChunkScarData` is a
class (changed from an initial struct pass, 2026-08-18, before any in-game test) -- same shape
as the archived `ForestCensusData`, which already round-tripped through this exact
`SetModdata<T>`/`GetModdata<T>` path; a struct would have made the protobuf-net serializer an
untested variable in a diagnostic whose whole job is testing moddata persistence, risking a
false read on assumptions 1/2 that looks exactly like "moddata does not survive unload." Player-
placed detection for logs is real but code-level, not per-instance: `log.json`'s `"type"`
variantgroup is `["grown","placed"]`, so gating on `"-grown-"` (mirrors `ElfLeafDropPatch`'s leaf
convention) excludes a log wall (200 `-placed-` logs stacked and broken) from the scar count.
It deliberately does not distinguish worldgen origin from a player-planted sapling that grew
naturally -- the scar measures standing wood removed from the column, not who planted the seed,
so a grown tree felled counts either way; this is intended behavior, not a gap. **Build-verified
only as of this entry** (`dotnet build`, 0 errors, 37-warning baseline unchanged) -- unload/
reload persistence, server-restart persistence, and `/rfscar bench`'s actual numbers are all
**not yet confirmed in-game.**

**Mod-wide bug surfaced by this tracker, fixed same day (2026-08-18): every Harmony patch in
rfmechanics was double-applied in singleplayer.** `RFMechanicsModSystem.Start(ICoreAPI)` runs
once per side, each on its own `RFMechanicsModSystem` instance (`ModLoader.RunModPhase` creates
a separate instance per `ModLoader`, one per side) -- in singleplayer both sides share one
process, and Harmony patches the shared CLR `MethodBase`, so two unguarded `PatchAll()` calls
(one from each side's `Start()`) registered every prefix/postfix in this assembly twice. First
caught by the scar tracker: a single hand-broken leaf incremented the leaf counter by 2, not 1,
100% reproducible. Root-caused via a background source-trace against
`reference/decompiled/1.22` confirming the engine calls `Block.OnBlockBroken` exactly once per
break (`ServerSystemBlockSimulation.cs:606`) -- the doubling was entirely on rfmechanics' side.
Fixed by guarding `PatchAll` behind `Harmony.HasAnyPatches(HarmonyId)` (process-wide check, not
per-instance) in `RFMechanicsModSystem.cs`. **This affected every registered Harmony patch in
singleplayer, not just the scar tracker** -- any prior "confirmed working in-game" note for a
mechanic whose patch *accumulates* rather than *idempotently sets* a value (drain-per-tick,
flat bonuses added twice, etc.) was tested under this bug and may be worth a second look;
mechanics that recompute an absolute value from scratch each call (e.g. `ClimbSpeedPatch`) were
unaffected by the doubling regardless. Not audited mechanic-by-mechanic here -- flagging so a
future session doesn't have to rediscover the mechanism if a stacking-looking bug turns up
elsewhere. Rebuilt and redeployed 2026-08-18; not yet re-verified in-game.

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
| `GoblinClimbingPatch.cs` | `EntityBehaviorControlledPhysics.MotionAndCollision` + `.ApplyTests` (both postfix) | `GoblinTraitCode` | `EnableGoblinRockClimbing`, `EnableGoblinTreeClimbing`, `GoblinRockClimbCodePrefixes` | **New 2026-08-06 (Phase G2).** Parallel to `TreeClimbingPatch` (Elf), not an extension of it — raw rock is never vanilla `Climbable`-flagged, so the dwarf-style `ClimbSpeedPatch`/`ClimbCollideAssistPatch` shape (which extends vanilla's own ladder detection) would never fire for it; only `TreeClimbingPatch`'s self-contained-scan shape generalizes. Two independent match groups, each its own toggle: `"log-grown"` (tree, same as Elf) and a config-driven whitelist covering raw rock plus rough worked stone/brick masonry and ore veins (18 prefixes, expanded 2026-08-22 from the original 4 raw-rock-only prefixes to include cobblestone/stonebrick/claybrick/drystone/mudbrick/peatbrick/refractorybrick/ore families — see `GoblinRockClimbCodePrefixes` in `RFMechanicsConfig.cs`; polished stone, quartz, tile, glass, and loose material stay excluded). **Chiseled logs/rock climbable too** (2026-08-22) — `IsClimbableGoblinBlock` falls back to `BlockEntityMicroBlock.BlockIds` when the block's own `Code.Path` reads as the generic `"chiseledblock"`, same mechanism as `TreeClimbingPatch.IsClimbableLog` uses for Elves. **No saturation cost** — researched and confirmed vanilla's own `EntityBehaviorHunger.SlowTick` has no `IsClimbing`-specific term at all, so this doesn't introduce an asymmetry against vanilla's free ladder climbing. **Confirmed working in-game (2026-08-14), part of the Goblin G2 pass.** |
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
| `ThewBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json`; also overrides `OnEntityDeath` | `OrcTraitCode` (checked in `IsOrc()`, inline, not a Harmony patch) | `EnableThew`, `OrcTraitCode`, `ThewGainPerHour`, `ThewGainBandMult`, `ThewGainSatietyGate`, `ThewDriftPerHour`, `ThewDecayLowSatietyThreshold`, `ThewDecayLowSatietyPerHour`, `ThewDecayStarvingPerHour`, `ProteinGateLevel`, `SeasonalGainEnabled`, `SeasonalGainMultipliers`, `OrcStomachMultiplier`, `StomachStackingMode`, `ThewDeathResetCap`, `DebtDrainPerHour`, `EnablePuff`, `HeavyDebtThreshold` | **Reworked 2026-08-24, see `notes/race-mechanics/orc-thew-rework-2026-08-24-as-built.md`.** Hidden per-player Thew float (0-1) for orcs, plus `BurnDebt`/`FrenzyDebt` counters and the `rf-orc-state` puff-cue byte. Gain/drift/decay are three flat satiety zones (no ramp), anchored to `world.Calendar.ElapsedHours` (in-game hours). **Build-verified only, NOT yet confirmed in-game** — see Testing status. |
| `BandBehavior.cs` | `EntityBehavior` (`OnGameTick`), attached via `patches/seraph-thew.json` (same file as `rfthew`), registered as `"rfband"` (`RFMechanicsModSystem.cs:38`) | `OrcTraitCode` (own `IsOrc()`, duplicated not shared) | `EnableBands`, `BandUpThresholds`, `BandDownThresholds`, `BandSizes`, `SizeChangeRatePerSecond`, `HungerRateMult`, `WalkSpeedDelta`, `MaxHpExtraPoints`, `AnimalSeekingRangeDelta`, `BulkyMeleeDamageBonus`, `RangedAccDelta`, `BulkyArmorWalkSpeedAffectednessDelta`, `JumpHeightMulDelta`, plus two `_UNWIRED` reserved fields (see below) | **Reworked 2026-08-24.** Lean/Standard/Bulky hysteresis state machine driven off `ThewBehavior`'s Thew value (up 0.35/0.70, down 0.30/0.64), for stats only now — `entitySize` is a separate continuous function of Thew (`ComputeTargetSize`), rate-capped at `SizeChangeRatePerSecond` (replaces the old lerp-on-cross). Applies/clears `Stats.Set` under source key `"rf-orc-band"`: `hungerrate`, `walkspeed`, `animalSeekingRange`, `meleeWeaponsDamage`, `armorWalkSpeedAffectedness`, `maxhealthExtraPoints`, `rangedWeaponsAcc`, `jumpHeightMul`. **Build-verified only, NOT yet confirmed in-game.** |
| `ThewDebtRepayPatch.cs` | `EntityBehaviorHunger.OnEntityReceiveSaturation` (postfix) | `OrcTraitCode` | `DebtRepaidPerSaturationPoint` | **New 2026-08-24, replaces the deleted `ThewEatPulsePatch.cs`/`ThewShieldPatch.cs` (a fresh class on the same hook, not a repurposed one).** Repays `BurnDebt` then `FrenzyDebt` on every eat, funded by saturation rather than Thew; also restamps `LastFoodCategoryKey`. **Not yet verified in-game.** |
| `BurnBehavior.cs` | `EntityBehavior` (`OnGameTick`, `OnEntityReceiveDamage`), attached via `patches/seraph-thew.json`, registered as `"rfburn"` (`RFMechanicsModSystem.cs:39`) | `OrcTraitCode` (`IsOrc()`, inline) | `EnableBurn`, `BurnActivationHealthFracGap`, `BurnMaxHealPerSecond`, `BurnCurveExponent`, `BurnThewPerHp`, `BurnThewFloor`, `BurnFastTickMs` | **Debt-routed 2026-08-24** (was still the superseded flat/threshold model as of 2026-08-13 — see Testing status). Below `BurnActivationHealthFracGap` of MaxHealth, an orc with Thew above `BurnThewFloor` heals via the locked cubic curve, incurring `BurnDebt` (`BurnThewPerHp` per HP) instead of spending Thew directly. **Build-verified only, NOT yet confirmed in-game.** |
| `FrenzyBehavior.cs` | `EntityBehavior`, one `RegisterGameTickListener` registered unconditionally in `Initialize` (no start/stop) | `OrcTraitCode` (`IsOrc()`, inline) | `EnableFrenzy`, `FrenzySatietyGate`, `FrenzyCurveExponent`, `FrenzyMaxSpeedBonus`, `FrenzyMaxJumpBonus`, `FrenzyDebtSatietyThreshold`, `FrenzyThewPerSecond`, `FrenzyFastTickMs`, `FrenzyStatWriteThreshold` | **Not previously in this table — added 2026-08-24 alongside its satiety rework.** Passive: every fast tick, ramps `walkspeed`/`jumpHeightMul` from the orc's current satFrac (zero at `FrenzySatietyGate` 0.50, full at 0), free above `FrenzyDebtSatietyThreshold` and debt-funded below it. Stops only when Thew is 0 with debt outstanding. **Not yet verified in-game.** |
| `PreservedProteinPatch.cs` | *(retired)* | — | — | **Retired 2026-08-25** (dietsetup tag-engine migration step 9) — this was a second, independent diet-multiply system outside dietsetup entirely. Superseded by dietsetup's own `dietsetup:preservedMult` tag fold (`FoodTagRegistry.TagNutritionMultiplier`), which now applies to both satiety and nutrient-bar gain for any `preserved`-tagged food, not just the two hardcoded item codes this patch checked. |
| `GoblinRotAuraBehavior.cs` | `EntityBehavior` (`OnGameTick`, `OnEntityDespawn`), attached via `seraph-goblinrotaura.json`, registered as `"rfgoblinrotaura"` (`RFMechanicsModSystem.cs:41`) | `GoblinTraitCode` (`IsGoblin()`, inline) | `EnableGoblinRotAura`, `EnableGoblinRotAuraCarriedInventory`, `GoblinRotAuraTickInterval`, `GoblinRotAuraRadiusMin/Max`, `GoblinRotAuraVerticalHalfExtent`, `GoblinRotAuraIntensityAtMinRadius`, `GoblinRotAuraRateMultiplier`, `GoblinRotAuraHoldFraction`, `GoblinRotAuraWriteThresholdHours`, `GoblinRotAuraHoldCreepFactor`, `GoblinRotAuraHoldCreepFloorHours`, `GoblinRotAuraIntakeHalfLifeHours` | **New 2026-08-12 (Phase G3) — not previously in this table.** ~2s server-side sweep around the goblin: accelerates spoilage on food in nearby placed containers and (2026-08-12 extension) carried hotbar/worn-bag inventories, held just short of fully spoiled ("larder hold") rather than tipping over. Publishes an `AuraSource` via the static `GoblinRotAuraRegistry` for other consumers (crop stunting, below) to read. Intake-driven shape: reads dietsetup's rot-intake accumulator directly off `WatchedAttributes` (no assembly reference). Deployed to the live install; **not yet smoke-tested in-game.** |
| `GoblinCropStuntBehavior.cs` | `CropBehavior.TryGrowCrop`, registered as `"RfGoblinCropStunt"` (`RFMechanicsModSystem.cs:42`, `RegisterCropBehavior`) | Gated on aura presence, not a direct trait check | `EnableGoblinRotAura`, `CropStuntMinStrength` | **New 2026-08-12 (Phase G3) — not previously in this table.** Crops under a goblin's rot aura stop advancing growth stage (recoverable, never destroyed) — gated on `GoblinRotAuraRegistry`'s spatial falloff only, never on Intensity. Deployed; **not yet smoke-tested in-game.** |
| `GoblinRotEdiblePatch.cs` | `CollectibleObject.GetNutritionProperties` (postfix) | `GoblinTraitCode` | `EnableGoblinRotEdible`, `GoblinRotEdibleSatiety` | **New 2026-08-12 (Phase G3) — not previously in this table.** Grants `game:rot` a minimal `FoodNutritionProperties` for goblins only, so vanilla's eat pipeline (which gates solely on a non-null result) lets them eat it. Never overrides an existing non-null result. Deployed; **not yet smoke-tested in-game.** |
| `GoblinSpitChargeGrantPatch.cs` | `CollectibleObject.tryEatStop` (postfix) | `GoblinTraitCode` | `EnableGoblinSpitCharges`, `SpitChargesPerRot`, `SpitChargeCap`, `EnableGoblinRotFlies`, `GoblinRotFliesHalfLifeHours`, `GoblinRotFliesPerRot`, `GoblinRotFliesCap` | **New 2026-08-12 (Phase G3).** Grants spit charges (capped) when a goblin finishes eating `game:rot`, gated on the same completion threshold (`secondsUsed >= 0.95f`) vanilla uses. **Confirmed working in-game (2026-08-14).** **Extended 2026-08-22 (Phase G4)**: same postfix, independently-toggled `GrantRotFlies` write of `rfmechanics:rotFlies`/`rotFliesUpdatedHours`, decay-then-add, 4h half-life. |
| `RfGoblinSpitRepairBehavior.cs` | `BlockBehavior.OnBlockInteractStart`, registered as `"RfGoblinSpitRepair"` (`RFMechanicsModSystem.cs:43`, `RegisterBlockBehaviorClass`) | `GoblinTraitCode` | `EnableGoblinSpitCharges`, `SpitRepairGain` | **New 2026-08-12 (Phase G3).** Lets a goblin spend a spit charge via empty-hand interact to repair a reparable block, mirroring vanilla's own `BehaviorReparable` repair-application math. **Confirmed working in-game (2026-08-14).** |
| `GoblinRotFliesShared.cs` | Not a Harmony patch — plain static helper class, no registration | `GoblinTraitCode` (inline, in `GetNearbyGoblins`) | — | **New 2026-08-22 (Phase G4).** Shared between the aura and spit fly systems: `ReadLiveRotFlies` (sibling to `GoblinRotAuraBehavior.ReadLiveRotIntake`, same decay shape against the new keys) and `GetNearbyGoblins` (client-side scan, `GoblinRotFliesRange`). Deployed; **not yet confirmed in-game** (see the Phase G4 entry's verification note above). |
| `GoblinAuraFliesModSystem.cs` | Not a Harmony patch — client-only `ModSystem`, auto-discovered, spawns via `RegisterAsyncParticleSpawner` | `GoblinTraitCode` (via `GoblinRotFliesShared`) | `EnableGoblinRotFlies`, `GoblinRotFliesCountMin/Max`, `GoblinRotFliesFloor`, `GoblinRotFliesSize`, `GoblinRotFliesBreathPeriod/Amplitude`, `GoblinRotFliesLagSeconds`, `GoblinRotFliesLifeSeconds`, `GoblinRotFliesRange`, plus reused `GoblinRotAuraRadiusMin/Max`/`VerticalHalfExtent` | **New 2026-08-22 (Phase G4).** Vanilla-particle ambient fly cloud around each nearby goblin. Deployed; **not yet confirmed in-game.** |
| `GoblinSpitFliesModSystem.cs` | Not a Harmony patch — client-only `ModSystem`/`IRenderer`, auto-discovered, `OnRenderFrame` on `EnumRenderStage.AfterBlit` | `GoblinTraitCode` (via `GoblinRotFliesShared`) | `EnableGoblinSpitFlies`, `GoblinSpitFliesSize`, `GoblinSpitFliesRadius`, `GoblinSpitFliesVerticalExtent`, `GoblinSpitFliesRetargetSeconds`, `GoblinSpitFliesLagSeconds`, `GoblinSpitFliesFadeSeconds`, `SpitChargeCap` (shared) | **New 2026-08-22 (Phase G4), tuned same day.** Exact-count (== `spitCharges`) custom-renderer fly cloud, own shader/texture (`assets/rfmechanics/shaders/rfspitflies.*`, `assets/rfmechanics/textures/entity/rotflies/fly.png`). **Tuning pass (same day, second commit)**: crossed double-quad mesh (was a single quad — read as a flat cutout regardless of billboard correctness), fully camera-roll-independent billboard (was already resetting the same two rotation-column sets RiftRenderer resets, `Values[0,1,2]`/`[8,9,10]`, leaving column 1/`Values[4,5,6]` inherited from the camera matrix same as Rift itself — now also reset, since that inheritance only produced a full billboard by relying on the camera never rolling), size halved to 0.075, envelope changed from an 0.8-radius sphere centred on the chest to a body-midpoint cylinder (`GoblinSpitFliesRadius` 0.6 horizontal, `GoblinSpitFliesVerticalExtent` 0.9 vertical half-extent, renamed from `GoblinSpitFliesCloudRadius`), retarget slowed 0.3s -> 2.0s to match the aura population's pace. `/rfflieslag` restructured into four subcommands (`aura`/`size`/`radius`/`vext`). **Third pass, same day**: `GoblinSpitFliesRadius` 0.6 -> 4.5, `GoblinSpitFliesVerticalExtent` 0.9 -> 0.45, matched to More Bugs' player-carried-rot fly roam distance (see prose above). Deployed; **not yet confirmed in-game.** |
| `OrcSmellModSystem.cs` | Not a Harmony patch — client-only `ModSystem`, auto-discovered, one `RegisterGameTickListener` doing detection + synchronous `SpawnParticles` emission | `OrcTraitCode` (own inline check, local player only) | `SmellEnabled`, `SmellTickIntervalMs`, `SmellRangeBase`, `SmellRangePerSize`, `SmellRangeHungerBonus`, `SmellRangeHardCap`, `SmellMaxSources`, `SmellVerticalRange` (config-key list stale beyond this pre-2026-08-24; not otherwise re-audited this pass) | **New 2026-08-23.** Player-anchored drift-particle band toward nearby fauna, direction + rough distance only, no marker ever placed on the source. See `notes/race-mechanics/orc-smell-v1-handover.md` for the full design rationale and six bugs fixed in review before any in-game test. **Extended 2026-08-24**: detection radius now gets a hunger-scaled bonus (`SmellRangeHungerBonus`, same ramp shape as `FrenzyBehavior`) and every computed radius plus `maxScan` are hard-clamped at `SmellRangeHardCap` (128 blocks, the server's own tracking cutoff — also fixes `maxScan` previously sitting 12 blocks past it). **Further extended same day**: far-range particle count (`SmellParticlesFar`) is now an additive floor guaranteed per-source against the shared `SmellMaxParticles` budget — see the dedicated entry above. Deployed; **not yet confirmed in-game.** |
| `OrcPuffModSystem.cs` | Not a Harmony patch — client-only `ModSystem`, auto-discovered, one `RegisterGameTickListener` reading `ThewBehavior.StateAttributeKey` off nearby players | — (reads the raw `rf-orc-state` byte for any `EntityPlayer`, no trait check of its own — the byte is only ever nonzero for an orc) | `EnablePuff`, `PuffIntervalGaining`, `PuffIntervalLightDebt`, `PuffIntervalHeavyDebt`, `PuffRenderRange`, `PuffParticleCount` | **New 2026-08-24.** Short fixed-size particle burst on an interval that scales with orc Thew state (idle/gaining/light debt/heavy debt) rather than a continuous emitter. State 1 (gaining) is local-player-only; states 2/3 (debt) render for every nearby player within `PuffRenderRange`. **Not yet verified in-game.** |
| `OrcSmellClassifier.cs` | Not a Harmony patch — plain static helper, no registration | — | — | **New 2026-08-23.** `IsSmellableFauna(Entity)`: `EntityBehaviorHarvestable` present AND `creatureDiet` attribute present, both required (excludes drifters/shivers, which are harvestable but have no diet). |
| `PlayerRaceBehavior.cs` | `EntityBehavior` (`Initialize`, `OnGameTick`), attached via `patches/seraph-elfidentity.json` **both sides**, registered as `"rfelfidentity"` (historical key, kept — a JSON attachment key, not user-facing) | Checks all four of `ElfTraitCode`/`DwarfTraitCode`/`OrcTraitCode`/`GoblinTraitCode` (own `RefreshRaceCache`) | `ElfIdentityTickInterval`, `EnableElfHungerDrainReduction`, `ElfHungerRateMult` | **New 2026-08-17 as `ElfIdentityBehavior` (elf-only), generalized 2026-09-02 to `PlayerRaceBehavior`.** Sole per-player race cache: one `PlayerRace Race` property, refreshed immediately in `Initialize()` and every `ElfIdentityTickInterval` thereafter. `IsElf` kept as `Race == PlayerRace.Elf` so its three pre-existing consumers (`RFElfZoomBehavior`, `BranchyLeavesPassthroughPatch`, `RFTreeProximityBehavior`) needed no logic changes. `RaceAbilityHotkeyModSystem` (below) and `OrcSmellFocusModSystem` read `Race` directly. Also applies/clears the reduced-hunger-drain stat (`"hungerrate"`, source `"rf-elf-attunement"`, key name unchanged) unconditionally off `IsElf`, re-derived every identity tick rather than edge-triggered. |
| `ElfStepHeightBehavior.cs` | `EntityBehavior` (`Initialize`, `OnGameTick`), attached via `patches/seraph-elfstepheight.json` **both sides**, registered as `"rfelfstepheight"` | Reads `PlayerRaceBehavior.IsElf`, no direct trait check | `EnableElfStepHeight`, `ElfStepHeightValue` (1.0), `ElfStepHeightDefaultEnabled` | **New 2026-08-17.** Sets `EntityBehaviorControlledPhysics.StepHeight` to `ElfStepHeightValue` for elves, a plain field write (public field, no Harmony patch needed). Captures the entity's actual pre-existing `StepHeight` once in `Initialize()` as the restore value (not a hardcoded vanilla 0.6f) and only writes when the current value disagrees with the target. Per-player toggle in `WatchedAttributes["rf-elf-stepheight-enabled"]`, flipped via `/rfelfstepheight toggle` (bound to a client hotkey, default Ctrl+H, 200ms debounce) — **unaffected by the 2026-09-02 race-ability hotkey consolidation**, different key, different mechanism. 1.0 is exactly `FindSteppableCollisionBox`'s threshold, so elves auto-climb fences/stair edges — this is why the toggle exists. |
| `RaceAbilityHotkeyModSystem.cs` | Not a Harmony patch — client-only `ModSystem`, auto-discovered, registers hotkey `"rfraceability"` (default `GlKeys.C`) | Dispatches by `PlayerRaceBehavior.Race`, no direct trait check | — | **New 2026-09-02.** Replaces the four old per-race V-bound hotkeys (`orcsmellfocus`, `rfelfzoom`, `rfdwarforesong`, `rfgoblinspit` — all retired permanently). `SetHotKeyHandler` dispatches Dwarf/Goblin presses through a `Dictionary<PlayerRace, Func<ICoreClientAPI, bool>>` to `DwarfOreSongModSystem.TryTrigger`/`RFMechanicsModSystem.TryTriggerGoblinSpit`; returns `false` for any other race (including Human) so other mods bound to C still see the press. Orc/Elf held ramps are unaffected in mechanism — `OrcSmellFocusModSystem`/`RFElfZoomBehavior` still poll raw key state themselves, just against this shared code now. **Built, not yet confirmed in-game.** |
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

## Diagnostic commands (server-side unless noted, use `/`, not `.`)

- `/rfflies` — Phase G4. Dumps `rotFlies` (raw + live-decayed), live-decayed `rotIntake`, spit
  charges, and the resulting aura fly count/radius + spit fly count, for the calling player.
- `/rfflieslag <aura|size|radius|vext> [value]` — Phase G4, **client-side**, not persisted. Four
  subcommands (added in the same-day tuning pass) get/set `GoblinRotFliesLagSeconds`,
  `GoblinSpitFliesSize`, `GoblinSpitFliesRadius`, `GoblinSpitFliesVerticalExtent` respectively,
  live, for in-game feel tuning. Deliberately a separate command, not a `/rfflies` subcommand: a
  client-registered command shadows a server-registered command of the same name (the client
  resolves locally first), and these values only feed client-only renderers — putting them under
  `/rfflies` would either never fire or would swallow `/rfflies`'s own server-side dump.
- `/rfflycheck <playername>` — Phase G4, **client-side, temporary**. See the Phase G4 entry above
  for what this is for; delete once the cross-client verification it exists to support has
  actually been run.
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

- **Orc Thew/Band/Burn/Frenzy full rework (2026-08-24): build-verified only, NOT confirmed
  in-game.** All 7 phases (deletions, three-zone gain/decay, Thew debt, Frenzy-on-satiety,
  continuous rate-capped size, puff cue, hunger-scaled smell) built and deployed to the live
  install one phase at a time, each building clean before the next started. Full detail in
  `notes/race-mechanics/orc-thew-rework-2026-08-24-as-built.md`. Smoke-test plan (none of it
  run yet): (1) size drift is visible and slow, no band crossing under 5 real minutes; (2) debt
  puffs appear after a fight and clear on eating; (3) a starving orc is fast and jumps high, and
  stops being both when Thew hits 0; (4) nothing crashes with 130 mods loaded. This entry
  supersedes every earlier Thew/Band/Burn/Frenzy testing note below for the fields this rework
  touched — the earlier notes remain accurate for the mechanic shapes they describe (e.g. the
  Phase 4 flat/threshold-vs-cubic-curve finding), just not for the current config surface.
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
- `PreservedProteinPatch`: **retired 2026-08-25** — see the table entry above.
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
