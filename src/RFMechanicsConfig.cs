namespace rfmechanics;

/// <summary>
/// Configuration for the RF Mechanics mod.
/// Loaded from rfmechanics.json in the ModConfig folder.
/// </summary>
public class RFMechanicsConfig
{
    /// <summary>Default trait code for the dwarf race. Loaded from config so it is trivially changeable.</summary>
    public string DwarfTraitCode { get; set; } = "rf-dwarf-positive";

    /// <summary>Weight for the depth component of the mining speed bonus.</summary>
    public double MiningDepthWeight { get; set; } = 1.0;

    /// <summary>Weight for the altitude component of the mining speed bonus.</summary>
    public double MiningAltitudeWeight { get; set; } = 0.6;

    /// <summary>Maximum total bonus from the depth/altitude curve. Clamped to this value.</summary>
    public double MiningBonusCap { get; set; } = 1.0;

    /// <summary>Master toggle for the mining speed curve.</summary>
    public bool EnableMiningCurve { get; set; } = true;

    /// <summary>Depth fraction threshold for the ore yield curve. Below this depthFrac, bonus is 0.</summary>
    public double OreThreshold { get; set; } = 0.4;

    /// <summary>Maximum ore bonus multiplier applied at maximum depth fraction.</summary>
    public double OreCeiling { get; set; } = 0.75;

    /// <summary>Master toggle for the ore yield curve.</summary>
    public bool EnableOreCurve { get; set; } = true;

    // ── Phase 3: Climb cost ──

    /// <summary>Scales both climbUpSpeed and climbDownSpeed by (1 + ClimbSpeedFactor).
    /// Negative values slow the dwarf on the ladder (both directions).
    /// Vanilla field names are inverted: Sneak (descend) reads climbUpSpeed (0.07),
    /// Jump (ascend) reads climbDownSpeed (0.035).</summary>
    public double ClimbSpeedFactor { get; set; } = -0.2;

    /// <summary>Flat satiety cost per second of climbing (ascent only).
    /// 2.4 = vanilla sprint surcharge at 30 TPS, i.e. sprint parity.
    /// A plain tuning number, not derived from vanilla at runtime.</summary>
    public double ClimbSaturationPerSecond { get; set; } = 2.4;

    /// <summary>Master toggle for the climb speed curve.</summary>
    public bool EnableClimbSpeed { get; set; } = true;

    /// <summary>Master toggle for the climb saturation drain.</summary>
    public bool EnableClimbSaturation { get; set; } = true;

    // ── Branchy leaves passthrough (Elf) ──

    /// <summary>Trait code granting the branchy-leaves collision passthrough. Loaded from
    /// config so it is trivially changeable, mirroring DwarfTraitCode.</summary>
    public string ElfTraitCode { get; set; } = "rf-elf-positive";

    /// <summary>Master toggle for the branchy-leaves collision passthrough.</summary>
    public bool EnableBranchyLeavesPassthrough { get; set; } = true;

    // ── Rested ──

    /// <summary>Master toggle for the Rested mechanic (behavior, drain, gain, and outputs).</summary>
    public bool EnableRested { get; set; } = true;

    /// <summary>Passive Rested gain per second while EnableRested is on. Applies regardless of activity.</summary>
    public double RestedIdleGainPerSecond { get; set; } = 0.003;

    /// <summary>Flat Rested gain per eat event (OnEntityReceiveSaturation call), not saturation-proportional.</summary>
    public double RestedEatGainFlat { get; set; } = 0.05;

    /// <summary>Rested drain per block broken, any material.</summary>
    public double RestedBlockBreakDrain { get; set; } = 0.02;

    /// <summary>Rested drain per second of sustained tool use (mining/chopping hold), not per interact tick.</summary>
    public double RestedToolUseDrainPerSecond { get; set; } = 0.02;

    /// <summary>Minimum change in a computed output stat value before it is re-written via Stats.Set.
    /// Stats.Set marks WatchedAttributes dirty on every call; this threshold avoids per-tick sync writes.</summary>
    public double RestedStatWriteThreshold { get; set; } = 0.02;

    /// <summary>Max miningSpeedMul swing (+/-) at Rested=1/Rested=0, applied via Stats.Set source "rested".</summary>
    public double RestedMiningSpeedBonus { get; set; } = 0.10;

    /// <summary>Max forageDropRate swing (+/-) at Rested=1/Rested=0, applied via Stats.Set source "rested".</summary>
    public double RestedForageDropBonus { get; set; } = 0.10;

    /// <summary>Max wildCropDropRate swing (+/-) at Rested=1/Rested=0, applied via Stats.Set source "rested".</summary>
    public double RestedWildCropDropBonus { get; set; } = 0.10;

    /// <summary>Max hungerrate swing (+/-) at Rested=1/Rested=0 (inverted: rested = slower hunger).</summary>
    public double RestedHungerRateBonus { get; set; } = 0.10;

    /// <summary>Master toggle for the Rested tool-durability output (3e). Off by default; may slip.</summary>
    public bool EnableRestedDurability { get; set; } = false;

    /// <summary>Max durability-loss reduction (+/-) at Rested=1/Rested=0.</summary>
    public double RestedDurabilityBonus { get; set; } = 0.10;

    // ── Tree proximity speed (Elf) ──

    /// <summary>Master toggle for the near-trees walkspeed bonus.</summary>
    public bool EnableTreeProximitySpeed { get; set; } = true;

    /// <summary>Scan radius in blocks around the entity for "log-grown" tree blocks.</summary>
    public int TreeProximityRadius { get; set; } = 5;

    /// <summary>Max walkspeed swing at full proximity strength (strength 1.0), applied via
    /// Stats.Set source "treeproximity". Separate from rf-elf-positive's own flat 0.08
    /// walkspeed trait bonus -- stacks additively, does not replace it.</summary>
    public double TreeProximityMaxBonus { get; set; } = 0.12;

    /// <summary>Minimum change in the computed walkspeed value before it is re-written via
    /// Stats.Set. Mirrors RestedStatWriteThreshold -- avoids per-tick sync writes.</summary>
    public double TreeProximityStatWriteThreshold { get; set; } = 0.02;

    // ── Tree climbing (Elf) ──

    /// <summary>Master toggle for letting Elves climb standing tree trunks ("log-grown"
    /// blocks) as if they were ladders, at plain vanilla ladder speed (no separate cost or
    /// speed curve, unlike ClimbSpeedFactor/ClimbSaturationPerSecond for dwarves).</summary>
    public bool EnableTreeClimbing { get; set; } = true;

    // ── Fall damage reduction (Elf) ──

    /// <summary>Master toggle for the Elf fall damage reduction.</summary>
    public bool EnableFallDamageReduction { get; set; } = true;

    /// <summary>Fraction of fall damage removed for Elves, e.g. 0.6 = 60% less fall damage.</summary>
    public double FallDamageReductionFactor { get; set; } = 0.6;

    // ── Thew (Orc) ──

    /// <summary>Master toggle for the Thew mechanic (gain/decay tick and preserved-protein multiplier).</summary>
    public bool EnableThew { get; set; } = true;

    /// <summary>Trait code for the orc race. Loaded from config so it is trivially changeable,
    /// mirroring DwarfTraitCode/ElfTraitCode. Wired to the actual character via a raceframework
    /// patch onto racialequality's own ork-char.json ExtraTraits list -- note the model itself is
    /// spelled "ork" in racialequality/PlayerModelLib, "orc" is this mod's own naming.</summary>
    public string OrcTraitCode { get; set; } = "rf-orc-positive";

    /// <summary>Multiplier applied to MaxSaturation for orc players (bigger stomach), relative to
    /// the vanilla base (1500). Combined with racialability's own "maxSaturationFactor" trait
    /// stat (e.g. the bottomless-stomach ability, 3x) per StomachStackingMode.</summary>
    public double OrcStomachMultiplier { get; set; } = 2.5;

    /// <summary>How OrcStomachMultiplier combines with racialability's own "maxSaturationFactor"
    /// blended stat (e.g. the bottomless-stomach ability, 3x) when both are active on the same
    /// player. Max (default): take the larger of the two candidates -- 2.5x vs 3x = 3x, never
    /// compounds. Multiply: the original behavior -- 2.5x * 3x = 7.5x, preserved as an option.
    /// See notes/orc-thew-phase1-session-2026-08-05.md for the satiety-stacking investigation
    /// that surfaced this as a design question.</summary>
    public OrcStomachStackingMode StomachStackingMode { get; set; } = OrcStomachStackingMode.Max;

    /// <summary>Master toggle for the Thew death penalty. Default ON per the settled design
    /// ("the body burned everything to heal").</summary>
    public bool EnableThewDeathPenalty { get; set; } = true;

    /// <summary>Flat Thew loss applied on death for orc players, when EnableThewDeathPenalty is
    /// on. 0.35 ~= one band's worth of Thew; clamped to 0 by Thew's own setter.</summary>
    public double ThewDeathPenalty { get; set; } = 0.35;

    /// <summary>Base Thew gain per real-world elapsed hour at full ramp (sat &gt;= ThewRampCeiling)
    /// + protein-gated, before the per-band ThewGainBandMult multiplier and the ThewRampFloor..
    /// ThewRampCeiling ramp scaling itself. Locked default per the Phase 3 hunger-numbers pass
    /// (notes/orc-phase3-partA-hunger-numbers.md), Option 2 ruling.</summary>
    public double ThewGainPerHour { get; set; } = 0.10;

    /// <summary>Per-band multiplier on ThewGainPerHour -- Lean gains fastest (provisioning is
    /// easy to keep up), Bulky slowest (war-form resists being built further while already
    /// deep). Locked: Lean 1.2 / Standard 1.0 / Bulky 0.8.</summary>
    public OrcBandTriple ThewGainBandMult { get; set; } = new OrcBandTriple { Lean = 1.2, Standard = 1.0, Bulky = 0.8 };

    /// <summary>Flat Thew decay per real-world elapsed hour applied continuously while the
    /// player's current band is Bulky, regardless of saturation/gorge/decay-tier state --
    /// stacks additively with whichever of the three decay tiers below is currently active (or
    /// with the gain ramp, if somehow both are true at once, though the tiers are structured so
    /// they aren't). This is the lever that makes Bulky a war posture rather than a lifestyle:
    /// fed net at Bulky is ThewGainPerHour*0.8 - this value = +0.03/h (still positive, deepening
    /// slowly while fed), idle/unfed net is -0.05/h (negative -- Bulky always trends back toward
    /// Standard without active provisioning). See notes/orc-phase3-partA-hunger-numbers.md
    /// "RULING -- Option 2 locked" for the full derivation and the corrected net-rate
    /// arithmetic.</summary>
    public double BulkyHoldDecayPerHour { get; set; } = 0.05;

    /// <summary>Three-tier decay below ThewRampFloor -- supersedes the old flat
    /// ThewStarvationDecayPerHour/LowSaturationFraction pair, which left a neutral no-gain-no-
    /// decay gap between the old 0.25 starvation threshold and the 0.50 ramp floor. Now every
    /// saturation fraction below the ramp floor decays at one of three rates: Underfed
    /// (ThewHungryThreshold..ThewRampFloor), Hungry (0..ThewHungryThreshold), Starving (Saturation
    /// == 0 exactly, matching vanilla's own starvation-damage trigger condition in
    /// EntityBehaviorHunger.SlowTick). No neutral parking zone exists below the ramp floor --
    /// above it you build, below it you erode, always at some rate.</summary>
    public double ThewDecayUnderfedPerHour { get; set; } = 0.05;

    /// <summary>See ThewDecayUnderfedPerHour's doc comment -- the middle tier, satFrac between 0
    /// (exclusive) and ThewHungryThreshold.</summary>
    public double ThewDecayHungryPerHour { get; set; } = 0.15;

    /// <summary>See ThewDecayUnderfedPerHour's doc comment -- the steepest tier, only at
    /// Saturation == 0 exactly (vanilla's own starvation-damage zone).</summary>
    public double ThewDecayStarvingPerHour { get; set; } = 0.60;

    /// <summary>Saturation fraction boundary between the Underfed and Hungry decay tiers.
    /// Replaces the old LowSaturationFraction's role (same default value, repurposed as a tier
    /// boundary rather than a single binary decay gate).</summary>
    public double ThewHungryThreshold { get; set; } = 0.25;

    /// <summary>While true: an orc with Thew &gt; 0 is immune to vanilla's own starvation damage
    /// (EntityBehaviorHunger.SlowTick's Saturation&lt;=0 check, EnumDamageType.Hunger) -- suppressed
    /// via a Harmony prefix on EntityBehaviorHealth.OnEntityReceiveDamage that zeroes the incoming
    /// damage when its type is Hunger, the entity is orc, and Thew &gt; 0. At Thew == 0 the shield
    /// drops and vanilla starvation damage/death applies untouched. See ThewShieldPatch.cs.</summary>
    public bool StarvationShieldWhileThew { get; set; } = true;

    /// <summary>Saturation fraction below which the graded gain ramp is zero. Replaces the old
    /// binary GorgeSaturationFraction gate -- gain now scales linearly from 0 at this fraction
    /// to full rate at ThewRampCeiling, rather than snapping on/off at a single threshold. Also
    /// doubles as the upper boundary of the Underfed decay tier -- see ThewDecayUnderfedPerHour.</summary>
    public double ThewRampFloor { get; set; } = 0.50;

    /// <summary>Saturation fraction at which the graded gain ramp reaches its full (1.0)
    /// multiplier. Linear between ThewRampFloor and this value.</summary>
    public double ThewRampCeiling { get; set; } = 1.00;

    /// <summary>Flat Thew granted per qualifying eat event ("bite"), on top of the per-hour tick
    /// gain -- gated by BiteCooldownSec so it can't be farmed by rapid nibble-spam, and by the
    /// same protein gate + ThewRampFloor saturation check as the tick gain. Small and mostly
    /// symbolic: even perfect cadence (one pulse every BiteCooldownSec) adds roughly
    /// ThewPerBite * 3600 / BiteCooldownSec per hour = 0.06/h at these defaults, well under the
    /// difference the ramp itself makes between half-full and stuffed.</summary>
    public double ThewPerBite { get; set; } = 0.001;

    /// <summary>Minimum real-world seconds between eat-pulse grants, per player. Also incidentally
    /// absorbs multi-ingredient meals, which fire EntityBehaviorHunger.OnEntityReceiveSaturation
    /// once per ingredient (orc-diagnostic-findings.md §1) -- only the first ingredient within the
    /// cooldown window grants a pulse.</summary>
    public double BiteCooldownSec { get; set; } = 60.0;

    /// <summary>ProteinLevel threshold above which the protein gain condition is met. Tuned against
    /// the T5 baselines in notes/orc-phase0-results.md: one fresh cooked meat delivered +112
    /// protein from a near-zero start, so 150 sits clearly above what a single meal gives --
    /// reaching it requires sustained meat-eating, not one snack.</summary>
    public double ProteinGateLevel { get; set; } = 150.0;

    /// <summary>Master toggle for the seasonal Thew gain multiplier. Off by default per settled design.</summary>
    public bool SeasonalGainEnabled { get; set; } = false;

    /// <summary>Per-season Thew gain multipliers, applied only when SeasonalGainEnabled is true.</summary>
    public ThewSeasonalMultipliers SeasonalGainMultipliers { get; set; } = new ThewSeasonalMultipliers();

    /// <summary>Full item codes (e.g. "survival:redmeat-cured") that count as "preserved protein"
    /// for orc, filling ProteinLevel at PreservedProteinMultiplier instead of the full rate.
    /// Default is the vanilla cured redmeat/bushmeat variants -- unreachable in normal survival
    /// play per the A5 finding, so this is a server-config surface for modded preserved foods,
    /// not vestigial. Domain is "survival" (confirmed against the installed asset tree: all
    /// itemtypes, including redmeat.json/bushmeat.json, live under assets/survival/, not
    /// assets/game/ -- there is no assets/game/itemtypes/ folder at all in this install).</summary>
    public string[] PreservedProteinItemCodes { get; set; } = new[] { "survival:redmeat-cured", "survival:bushmeat-cured" };

    /// <summary>Multiplier applied to ProteinLevel gain (via nutritionGainMultiplier) for
    /// preserved-protein items. 0.5 = half the protein fill rate of an equivalent fresh item.</summary>
    public double PreservedProteinMultiplier { get; set; } = 0.5;

    // ── Bands (Orc, Phase 3) ──

    /// <summary>Master toggle for the Band mechanic (state machine, entitySize, and stat
    /// application). Independent of EnableThew -- Thew must still be on for bands to have
    /// anything to key off, but this lets bands be disabled while keeping Thew itself running.</summary>
    public bool EnableBands { get; set; } = true;

    /// <summary>Thew value at which Lean crosses up into Standard, and Standard up into Bulky.
    /// Hysteresis gap vs BandDownThresholds is deliberate -- see BandDownThresholds.</summary>
    public OrcBandUpDown BandUpThresholds { get; set; } = new OrcBandUpDown { LeanToStandard = 0.35, StandardToBulky = 0.70 };

    /// <summary>Thew value below which Standard falls back to Lean, and Bulky falls back to
    /// Standard. Set below the corresponding up-threshold (0.30 &lt; 0.35, 0.64 &lt; 0.70) so a
    /// player sitting near the boundary doesn't flicker bands every tick.</summary>
    public OrcBandUpDown BandDownThresholds { get; set; } = new OrcBandUpDown { LeanToStandard = 0.30, StandardToBulky = 0.64 };

    /// <summary>Target entitySize (WatchedAttributes, real hitbox via PlayerModelLib) per band.
    /// Orc practical max is entitySize 1.5 (T3, notes/orc-phase0-results.md) -- these sit well
    /// inside it. MinCollisionBox/MaxCollisionBox on ork-char.json (0.86-1.35) never clamp these.</summary>
    public OrcBandTriple BandSizes { get; set; } = new OrcBandTriple { Lean = 0.90, Standard = 1.12, Bulky = 1.28 };

    /// <summary>Real-world seconds to lerp entitySize from the old band's size to the new band's
    /// on a band cross.</summary>
    public double BandSizeLerpSeconds { get; set; } = 10.0;

    /// <summary>Per-band hungerrate multiplier, applied as a Stats.Set delta (target - 1) under
    /// source "rf-orc-band" on the vanilla "hungerrate" category. Lean unmodified (1.0),
    /// Standard 1.3x, Bulky 1.8x.</summary>
    public OrcBandTriple HungerRateMult { get; set; } = new OrcBandTriple { Lean = 1.0, Standard = 1.3, Bulky = 1.8 };

    /// <summary>Per-band walkspeed delta (already in Stats.Set-delta units, not a target
    /// multiplier), applied under source "rf-orc-band" on "walkspeed". Lean unmodified.</summary>
    public OrcBandTriple WalkSpeedDelta { get; set; } = new OrcBandTriple { Lean = 0.0, Standard = -0.05, Bulky = -0.15 };

    /// <summary>Per-band extra max-HP points, applied via Stats.Set on "maxhealthExtraPoints"
    /// (delta units == literal HP points added, per EntityBehaviorHealth.UpdateMaxHealth's
    /// "GetBlended(...) - 1f" formula) followed by an explicit UpdateMaxHealth() call, since
    /// nothing else re-triggers it off a bare Stats.Set.</summary>
    public OrcBandTriple MaxHpExtraPoints { get; set; } = new OrcBandTriple { Lean = -1.0, Standard = 2.0, Bulky = 6.0 };

    /// <summary>Per-band delta on the vanilla "animalSeekingRange" blended stat (consumed live in
    /// AiTaskBaseTargetable.CanSensePlayer -- no Harmony patch needed, confirmed
    /// notes/orc-diagnostic-findings.md §6 / orc-phase0-results.md A2). Lean is the stalker/
    /// provisioner band (animals notice it least), Bulky the loudest.</summary>
    public OrcBandTriple AnimalSeekingRangeDelta { get; set; } = new OrcBandTriple { Lean = -0.15, Standard = 0.15, Bulky = 0.40 };

    /// <summary>Bulky-only melee damage delta, applied under source "rf-orc-band" on the vanilla
    /// "meleeWeaponsDamage" category (registered EntityPlayer.cs:393, consumed EntityAgent.cs:390).
    /// Lean/Standard get 0 (no entry needed, but written as 0 to keep the source key's presence
    /// uniform across bands and simplify removal on race-swap-away).</summary>
    public double BulkyMeleeDamageBonus { get; set; } = 0.12;

    /// <summary>Per-band delta applied to both "bluntDamageFactor" and "crushingDamageFactor" --
    /// these are PlayerModelLib's own Harmony-patched Stats categories (StatsPatches.cs), not
    /// vanilla-registered ones, confirmed already working in this codebase via the dwarf trait
    /// (crushingDamageFactor -0.5, bluntDamageFactor -0.2 in traits.json). Negative = resistance.</summary>
    public OrcBandTriple BluntCrushResistDelta { get; set; } = new OrcBandTriple { Lean = 0.0, Standard = -0.06, Bulky = -0.18 };

    /// <summary>Bulky-only delta on "armorWalkSpeedAffectedness" (real vanilla-registered stat,
    /// EntityPlayer.cs:404; already used by dwarf -0.85 / elf-negative +0.8 in traits.json).
    /// -0.5 == "halved" per the locked table, by the same delta convention as those two existing
    /// uses (delta -1.0 would fully cancel armor's walk-speed penalty at GetBlended()==0).</summary>
    public double BulkyArmorWalkSpeedAffectednessDelta { get; set; } = -0.5;

    /// <summary>NOT WIRED -- reserved config surface only. The locked band table calls for
    /// Bulky jump height -20%, but the only consumer of the vanilla "jumpHeightMul" stat
    /// (PModuleOnGround.cs:86) computes MathF.Sqrt(MathF.Max(1f, blended)) -- clamped so any
    /// value below 1.0 has zero effect. A reduction is not achievable through this stat as
    /// installed; would need a Harmony patch on PModuleOnGround.DoApply. Flagged for review,
    /// not implemented speculatively -- see the Phase 3 Part B final report.</summary>
    public double BulkyJumpHeightReduction_UNWIRED { get; set; } = 0.20;

    /// <summary>NOT WIRED -- reserved config surface only. "KnockbackResistance" lives on the
    /// shared per-entity-TYPE EntityProperties object (EntityProperties.cs:104), not a per-player
    /// Stats category -- there is no Stats.Register for it on EntityPlayer. Setting it directly
    /// would mutate shared state across every player entity of the same type, not just orc.
    /// "Applies on hit" has the same problem in the other direction (no player-outgoing-melee
    /// hook found). Both halves need a Harmony patch design decision, not a speculative build --
    /// flagged for review, see the Phase 3 Part B final report.</summary>
    public double StandardKnockbackTakenReduction_UNWIRED { get; set; } = 0.30;

    // ── Burn-to-survive (Orc, Phase 4) ──

    /// <summary>Master toggle for the Burn-to-Survive mechanic. Independent of EnableThew's own
    /// toggle, same convention as EnableBands -- Thew must still be on for there to be anything
    /// to burn, but this lets burn be disabled while Thew/Bands keep running.</summary>
    public bool EnableBurn { get; set; } = true;

    /// <summary>Health fraction (of current MaxHealth) below which burn mode triggers, for as
    /// long as Thew remains above BurnThewFloor. Thew-gated, not band-gated: any band burns if
    /// Thew remains -- supersedes an earlier "nothing to burn at Lean" framing. At the floor, no
    /// net heal; vanilla death rules apply untouched.</summary>
    public double BurnHealthFraction { get; set; } = 0.25;

    /// <summary>HP healed per real second while burning.</summary>
    public double BurnHealPerSecond { get; set; } = 0.75;

    /// <summary>Thew cost per HP healed while burning. A full vanilla 15-hp bar costs
    /// BurnThewPerHp * 15 =~ 0.18 Thew at the default -- a war-built 1.0 orc carries roughly 4-5
    /// emergency bars, a fresh 0.70 Bulky ~3, a 0.2 Lean one thin partial heal.</summary>
    public double BurnThewPerHp { get; set; } = 0.012;

    /// <summary>Thew floor burn cannot cross. Below this remaining Thew, burn will not
    /// trigger/continue; vanilla death rules apply untouched from that point.</summary>
    public double BurnThewFloor { get; set; } = 0.02;

    /// <summary>Interval, in milliseconds, of the fast game-tick listener registered only while
    /// burn conditions hold (entered/exited on the shared 6s slow tick and immediately on
    /// damage received). Too coarse a listener would make burn feel laggy in combat; this is
    /// deliberately much faster than the 6s Thew/Band cadence, but only runs while burning.</summary>
    public int BurnFastTickMs { get; set; } = 500;

    // ── Darkvision (Goblin) ──

    /// <summary>Master toggle for the Goblin darkvision effect. Client-side only feature (no
    /// server authority), but still gets a toggle for parity with every other mechanic in this
    /// config.</summary>
    public bool EnableGoblinDarkvision { get; set; } = false;

    /// <summary>Trait code for the goblin race. Loaded from config so it is trivially changeable,
    /// mirroring DwarfTraitCode/ElfTraitCode/OrcTraitCode.</summary>
    public string GoblinTraitCode { get; set; } = "rf-goblin-positive";

    /// <summary>Strength written to ShaderUniforms.NightVisionStrength for goblins. 0.8 matches
    /// vanilla's own definition of "full strength" -- ModSystemNightVision clamps night-vision
    /// goggles' fuel-derived strength to a ceiling of 0.8, never 1.0. Composed via Math.Max with
    /// whatever vanilla's own night-vision-goggle system already set, not an unconditional
    /// overwrite -- see GoblinDarkvisionModSystem's doc comment for why.</summary>
    public double GoblinDarkvisionStrength { get; set; } = 0.8;

    // ── Fall damage reduction (Goblin) ──

    /// <summary>Master toggle for the Goblin fall damage reduction. Separate from
    /// EnableFallDamageReduction (Elf) so either race's reduction can be tuned/disabled
    /// independently even though both share the same FallDamagePatch prefix.</summary>
    public bool EnableGoblinFallDamageReduction { get; set; } = true;

    /// <summary>Fraction of fall damage removed for Goblins, e.g. 0.5 = 50% less fall damage.</summary>
    public double GoblinFallDamageReductionFactor { get; set; } = 0.5;

    // ── Goblin dig speed (Phase G2) ──

    /// <summary>Master toggle for the Goblin bare-hand dig bonus on Soil/Sand/Gravel-tier
    /// blocks (soil, sand, gravel, packeddirt, drypackeddirt, and the new spit-packed
    /// variants). Applied via GoblinDigModifierBehavior, vanilla's own GetMiningSpeedModifier
    /// extension point -- bare-hand only by construction, a held tool overwrites the result
    /// (see notes/goblin-phase-g2-partA-report.md A1).</summary>
    public bool EnableGoblinDigBonus { get; set; } = true;

    /// <summary>Goblin bare-hand dig rate on diggable earth (replaces the vanilla implicit
    /// 1.0 flat rate). Locked at Part B review, Option A: 8.0 beats the fastest (steel)
    /// shovel on every material (soil/sand rate 7, gravel rate 4.4), not just a "plausible"
    /// mid-tier shovel.</summary>
    public double GoblinBareHandDigRate { get; set; } = 8.0;

    /// <summary>Master toggle for the Goblin stone-mining penalty. Shares MiningSpeedPatch's
    /// postfix with the Dwarf depth/altitude bonus (sequential trait checks, one player is
    /// only ever one race, same coexistence shape as FallDamagePatch).</summary>
    public bool EnableGoblinStonePenalty { get; set; } = true;

    /// <summary>Multiplier applied to GetMiningSpeed's result for goblins mining Ore/Stone
    /// material, any tool. 0.4 = 60% slower ("editing a squat is tolerable, excavation is
    /// tedious" -- roughly 2.5x slower across every pick tier).</summary>
    public double GoblinStoneMiningFactor { get; set; } = 0.4;

    // ── Goblin climbing (Phase G2) ──

    /// <summary>Master toggle for Goblin raw-rock climbing (GoblinClimbingPatch). Independent
    /// of EnableGoblinTreeClimbing -- either match group can be disabled without the other.</summary>
    public bool EnableGoblinRockClimbing { get; set; } = true;

    /// <summary>Master toggle for Goblin tree climbing (same "log-grown" mechanism as Elf's
    /// TreeClimbingPatch, parallel implementation in GoblinClimbingPatch). Independent of
    /// EnableGoblinRockClimbing.</summary>
    public bool EnableGoblinTreeClimbing { get; set; } = true;

    /// <summary>Code.Path prefixes treated as climbable raw rock for goblins. Four prefixes,
    /// not one: "rock-" (primary strata), "crackedrock-" (a natural UnstableRock collapse
    /// product, does not start with "rock-"), "meteorite-", "stalagsection-" -- all
    /// world-generated, none player-crafted. Worked stone (cobblestone/polished/
    /// stonebricks/quartz/etc.) is excluded by NOT matching any of these prefixes -- see
    /// notes/goblin-phase-g2-partA-report.md A2 for the full worked-stone exclusion survey.
    /// Config-driven (not hardcoded) so a modded rock-alike block can be added without a
    /// code change.</summary>
    public string[] GoblinRockClimbCodePrefixes { get; set; } = new[] { "rock-", "crackedrock-", "meteorite-", "stalagsection-" };

    // ── Goblin tunnel speed (Phase G2) ──

    /// <summary>Master toggle for the goblin tunnel-speed walkspeed bonus.</summary>
    public bool EnableGoblinTunnelSpeed { get; set; } = true;

    /// <summary>Walkspeed bonus applied while a goblin is under diggable earth (Soil/Sand/
    /// Gravel-tier ceiling within 1-2 blocks overhead), Stats.Set source "tunneling".
    /// Originally 0.12 (matched TreeProximityMaxBonus's existing default almost exactly,
    /// Part A report A3); bumped to 0.15 at G2.1 review, Miles still dialing this in.</summary>
    public double GoblinTunnelSpeedBonus { get; set; } = 0.15;

    /// <summary>Minimum change in the computed tunneling walkspeed value before it is
    /// re-written via Stats.Set. Mirrors RestedStatWriteThreshold/
    /// TreeProximityStatWriteThreshold -- avoids per-tick sync writes.</summary>
    public double GoblinTunnelStatWriteThreshold { get; set; } = 0.02;

    // ── Goblin spit-packed earth (Phase G2) ──

    // NOTE (G2.1): GoblinDiggableEarthCodePrefixes was removed here. It was a third,
    // hand-maintained Code.Path prefix list duplicating what
    // GoblinSpitPackingPatch.ResolveConversionTarget already knows -- and it drifted (5 of
    // 10 spit-packed families were missing from it, so goblins got no tunnel walkspeed
    // bonus tunneling under those ceilings). RFGoblinTunnelBehavior now calls
    // GoblinSpitPackingPatch.IsGoblinEarth directly instead of consulting a config array.
    // See notes/goblin-dig-materials-handover.md for the drift history.

    /// <summary>Master toggle for the spit-packed earth conversion (GoblinSpitPackingPatch).
    /// Ships ungated by any gut-primer/rot state -- primer-gating is G3 scope. Soil converts
    /// to vanilla's packeddirt; Sand/Gravel convert to the new spitpackedsand-{rock}/
    /// spitpackedgravel-{rock} blocktypes shipped in this mod's own assets.</summary>
    public bool EnableGoblinSpitPacking { get; set; } = true;

    // ── Elf leaf gathering (Phase G2) ──

    /// <summary>Master toggle for the Elf leaf self-drop (ElfLeafDropPatch). Appends the
    /// harvested leaves-*/leavesbranchy-* block's own placed/obtainable form to vanilla's
    /// existing treeseed/stick drops -- does not replace them. Closes G1's open
    /// branchy-leaves ingredient-sourcing gap (see notes/goblin-phase-g1-as-built.md).</summary>
    public bool EnableElfLeafGathering { get; set; } = true;
}

/// <summary>How OrcStomachMultiplier combines with racialability's own maxSaturationFactor
/// trait stat. See RFMechanicsConfig.StomachStackingMode's doc comment.</summary>
public enum OrcStomachStackingMode
{
    Max,
    Multiply
}

/// <summary>Reusable per-band triple (Lean/Standard/Bulky), used for every Phase 3 band stat
/// that varies across all three bands.</summary>
public class OrcBandTriple
{
    public double Lean { get; set; }
    public double Standard { get; set; }
    public double Bulky { get; set; }
}

/// <summary>Hysteresis threshold pair -- Lean/Standard boundary and Standard/Bulky boundary.
/// Used for both BandUpThresholds and BandDownThresholds.</summary>
public class OrcBandUpDown
{
    public double LeanToStandard { get; set; }
    public double StandardToBulky { get; set; }
}

/// <summary>Per-season Thew gain multipliers. Only consulted when SeasonalGainEnabled is true.</summary>
public class ThewSeasonalMultipliers
{
    public double Spring { get; set; } = 1.0;
    public double Summer { get; set; } = 1.0;
    public double Fall { get; set; } = 1.0;
    public double Winter { get; set; } = 1.0;
}
