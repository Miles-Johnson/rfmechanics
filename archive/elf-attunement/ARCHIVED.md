# Archived: Elf attunement (Phase 1a/1b)

**Why**: Scrapped in favour of always-on elf body traits. The 0-100 attunement float, its
forest-presence gain/decay, and the three threshold-gated effects (canopy standing, tree
proximity walkspeed, reduced hunger drain) were replaced by unconditional versions of the
same three effects, gated only on elf identity (`ElfIdentityBehavior`). See
`notes/race-mechanics/elf-attunement-removal-report.md` for the full investigation behind
the decision.

**When**: 2026-08-17 — the same day attunement's client-side attach bug was fully resolved
and the system was confirmed working in-game. This was a same-day pivot, not a
half-finished feature being cut.

**Superseded by**: `src/ElfIdentityBehavior.cs` (identity cache) and
`src/ElfStepHeightBehavior.cs` (new, unrelated to attunement — a body-trait addition made in
the same pass). The three former attunement consumers (`RFElfZoomBehavior`,
`BranchyLeavesPassthroughPatch`, `RFTreeProximityBehavior`) now read
`ElfIdentityBehavior.IsElf` directly; the reduced-hunger-drain effect moved into
`ElfIdentityBehavior` itself, unconditional on identity, still gated by its own
`EnableElfHungerDrainReduction`/`ElfHungerRateMult` config pair.

**Last commit where it was live**: `0432317bbe8ddf4bb0f104e8e2ea63babb500a01` (2026-08-17,
"T6: extend /rfattune with every Phase 3 threshold effect's active gate") — `d5d23bf`
onward through that commit is the full Phase 1a/1b build, including the grove-tier removal
earlier the same day.

**Files here**: `ElfAttunementBehavior.cs`, `ElfAttunementContext.cs` (includes the nested
`ElfAttunementBlockWhitelist`), `ElfForestCensus.cs`, `ElfForestCensusInvalidationPatch.cs`.
Excluded from the build via `<Compile Remove="archive/**/*.cs" />` in `rfmechanics.csproj`
(the SDK's implicit glob would otherwise compile them). Historical record only — do not
re-register or re-attach.
