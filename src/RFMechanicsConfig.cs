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
    /// Stats.Set -- avoids per-tick sync writes.</summary>
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

    /// <summary>Phase 2 (T1): food-type gate on Thew gain. Whenever the last item an orc ate
    /// (tracked by ThewEatPulsePatch's postfix, "rf-orc-last-food-category" on entity.Attributes)
    /// resolved to EnumFoodCategory Fruit/Vegetable/Grain, BOTH the hourly tick gain
    /// (ThewBehavior.OnGameTick) and the eat-pulse (ThewEatPulsePatch) are blocked regardless of
    /// ProteinLevel/satFrac -- a residually-elevated ProteinLevel from an earlier meat meal can no
    /// longer be "ridden" by topping off satiety on cheap grain/veg/fruit afterward. Protein and
    /// Dairy categories are unaffected (pass through to the existing ProteinLevel gate
    /// unchanged). Not independently toggleable -- this is a sub-condition of EnableThew, not a
    /// separate feature.</summary>
    public bool EnableThewFoodTypeGate { get; set; } = true;

    /// <summary>Phase 2 (T2): decay rate applied when Thew's gain condition doesn't fire AND
    /// satFrac is at/above ThewRampFloor -- i.e. the orc is well fed (or gorged on grain/veg/
    /// fruit, see EnableThewFoodTypeGate) but not building Thew. Closes the dead zone the audit
    /// found in the old if/else-if structure (notes/diagnostics/thew-audit.md Q4): previously
    /// this state did nothing at all. Set gentler than ThewDecayUnderfedPerHour (0.05/h) because
    /// this orc isn't starving, just eating the wrong things -- but deliberately nonzero so a
    /// bread-only diet still erodes Bulky/Standard over hours, matching the "bread orcs should
    /// shrink" design intent. TUNING: chosen, not locked -- flagged for review alongside the T0
    /// Bulky break-even numbers (see thew-audit.md T0 / the Phase 2 report).</summary>
    public double ThewDecaySatedNonProteinPerHour { get; set; } = 0.03;

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
    /// they aren't). This is the lever that makes Bulky a war posture rather than a lifestyle.
    ///
    /// T0 DECIDED (Phase 2 report, orc-phase2-as-built.md): lowered from 0.05 to 0.025 (midpoint
    /// of the answered 0.02-0.03 range; ThewGainPerHour deliberately left untouched per the same
    /// answer). At the coded-default ThewGainPerHour=0.10, this moves the Bulky sustain
    /// break-even from 0.8125 to 0.65625 (rampMult = 0.025/(0.10*0.8) = 0.3125, satFrac =
    /// 0.5+0.3125*0.5) -- a well-provisioned, non-fighting orc can now actually hold Bulky, per
    /// the design intent T0 was blocked on. NOTE: this is an EXISTING config key -- per
    /// thew-audit.md Q8 finding #3, a coded-default change alone does not update an
    /// already-written live rfmechanics.json (only added/removed keys self-heal on restart); the
    /// live value was hand-patched to 0.025 as part of this same deploy, see the as-built doc's
    /// Deployment section.</summary>
    public double BulkyHoldDecayPerHour { get; set; } = 0.025;

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

    /// <summary>DORMANT (Phase 2 T6): superseded by ThewGainPerSaturationPoint -- a flat grant
    /// per bite made nibbling small bites every BiteCooldownSec the dominant, farmable Thew
    /// income path (up to 0.06/h at the live ThewGainPerHour=0.04, i.e. more than the base tick
    /// rate itself -- see thew-audit.md Q2). No longer read anywhere. Left in place (not deleted)
    /// so existing rfmechanics.json installs don't silently drop the key on the next
    /// StoreModConfig rewrite.</summary>
    public double ThewPerBite { get; set; } = 0.001;

    /// <summary>DORMANT (Phase 2 T6): superseded -- proportional-to-saturation grants (see
    /// ThewGainPerSaturationPoint) no longer need a farming-prevention cooldown, and a cooldown
    /// actively works against the fix: a multi-ingredient meal fires
    /// EntityBehaviorHunger.OnEntityReceiveSaturation once per ingredient
    /// (orc-diagnostic-findings.md §1), and each ingredient's proportional share should count, not
    /// just the first. No longer read anywhere. Left in place for the same install-compatibility
    /// reason as ThewPerBite.</summary>
    public double BiteCooldownSec { get; set; } = 60.0;

    /// <summary>Phase 2 (T6): Thew granted per point of raw saturation on a qualifying eat event
    /// ("bite"), replacing ThewPerBite's flat-per-event grant -- rewards eating well (one real
    /// meal) over nibbling (many tiny bites), since total reward now tracks total saturation
    /// eaten rather than event count. Applied to `saturation`, the pre-nutritionGainMultiplier
    /// raw parameter of OnEntityReceiveSaturation (reference/decompiled/VSEssentials/
    /// Vintagestory.GameContent/EntityBehaviorHunger.cs:239,245 -- confirmed NOT already scaled by
    /// nutritionGainMultiplier, unlike the *Level fields).
    /// RETUNED (post-deploy, same day as the T0/T1 update): lowered from 0.00015 to 0.0000133,
    /// alongside ThewPerBiteCap dropping 0.05 -> 0.005 and ThewGainPerHour rising back to the
    /// coded default 0.10 (live now matches). Together these push the eat-pulse toward "small,
    /// bounded top-up" rather than a source that can rival the tick gain on its own -- still
    /// TUNING, not locked, but this specific pairing was chosen and deployed live.</summary>
    public double ThewGainPerSaturationPoint { get; set; } = 0.0000133;

    /// <summary>Phase 2 (T6): ceiling on a single eat event's ThewGainPerSaturationPoint grant, so
    /// one unusually high-saturation item can't produce an outsized single jump. RETUNED
    /// alongside ThewGainPerSaturationPoint above -- see that field's doc comment.</summary>
    public double ThewPerBiteCap { get; set; } = 0.005;

    /// <summary>Threshold above which the protein gain condition is met -- checked against BOTH
    /// ProteinLevel and DairyLevel (see ThewBehavior.IsProteinGated; widened post-report, since
    /// cheese.json tags "Dairy" not "Protein" in vanilla assets, and a cheese-only diet was
    /// silently failing to gain Thew under the original ProteinLevel-only check). Originally tuned
    /// against ProteinLevel specifically: the T5 baselines in notes/orc-phase0-results.md found one
    /// fresh cooked meat delivers +112 protein from a near-zero start, so 150 sits clearly above
    /// what a single meal gives -- reaching it requires sustained meat-eating, not one snack.
    /// Whether the same 150 threshold is well-calibrated for DairyLevel's own per-bite rate is
    /// untested -- flagged for review.</summary>
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

    /// <summary>Per-band extra delta on the vanilla "rangedWeaponsAcc" blended stat (registered
    /// EntityPlayer.cs, consumed BaseAimingAccuracy.Update in vsessentialsmod), stacked on top of
    /// the race-wide "rangedWeaponsAcc": -0.25 baseline in raceframework's rf-orc-negative trait.
    /// Only Bulky gets an extra penalty -- mass costs something specific on top of every orc's
    /// baseline inaccuracy. Combined worst case (race -0.25 + band -0.15 = -0.40 off a base of
    /// 1.0) stays well clear of the point where BaseAimingAccuracy's 1 - 0.075/rangedAcc formula
    /// degrades as the blended value approaches 0.</summary>
    public OrcBandTriple RangedAccDelta { get; set; } = new OrcBandTriple { Lean = 0.0, Standard = 0.0, Bulky = -0.15 };

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

    /// <summary>DEPRECATED as an activation gate (Phase 2 T3): the locked cubic burn model has no
    /// activation threshold -- heal/s = BurnMaxHealPerSecond * (1-healthFrac)^BurnCurveExponent is
    /// notionally active at any health below 100%, near-zero close to full health, escalating as
    /// health drops. No longer read by BurnBehavior's trigger check (see
    /// BurnActivationHealthFracGap for the new, purely-performance gate). Left in place so
    /// existing rfmechanics.json installs don't silently drop the key, and still shown in
    /// /rfthew dump for reference.</summary>
    public double BurnHealthFraction { get; set; } = 0.25;

    /// <summary>DORMANT (Phase 2 T3): superseded by BurnMaxHealPerSecond/BurnCurveExponent's
    /// cubic curve -- the old flat-rate model healed this many hp/s unconditionally whenever burn
    /// was active, regardless of how far below the (now-removed) BurnHealthFraction threshold
    /// health was. No longer read anywhere. Left in place for install compatibility.</summary>
    public double BurnHealPerSecond { get; set; } = 0.75;

    /// <summary>Phase 2 (T3) locked design value: peak heal rate (hp/s) the cubic curve approaches
    /// as healthFrac -&gt; 0 -- heal/s = BurnMaxHealPerSecond * (1-healthFrac)^BurnCurveExponent.
    /// Given directly by the Phase 2 brief (orc-phase4-burn-to-survive-design.md), not derived.</summary>
    public double BurnMaxHealPerSecond { get; set; } = 1.5;

    /// <summary>Phase 2 (T3) locked design value: exponent on the cubic burn curve. Given directly
    /// by the brief. Also the default for FrenzyCurveExponent (same shape family, see T4), though
    /// the two are independently configurable.</summary>
    public double BurnCurveExponent { get; set; } = 3.0;

    /// <summary>Phase 2 (T3) locked design value, changed from 0.012: Thew cost per HP healed
    /// while burning. A full vanilla 15-hp bar now costs BurnThewPerHp * 15 =~ 0.45 Thew -- burn
    /// is markedly more expensive than the old flat model, consistent with the cubic curve's peak
    /// rate (1.5 hp/s vs the old flat 0.75 hp/s) also being roughly double. Live value was pushed
    /// to match this default (post-deploy, same day as the T0/T1 update) -- no longer drifted.</summary>
    public double BurnThewPerHp { get; set; } = 0.03;

    /// <summary>Thew floor burn cannot cross. Below this remaining Thew, burn will not
    /// trigger/continue; vanilla death rules apply untouched from that point.</summary>
    public double BurnThewFloor { get; set; } = 0.02;

    /// <summary>Phase 2 (T3): purely a performance gate, NOT a game-design threshold (that's what
    /// BurnHealthFraction used to be, before the cubic rewrite removed it). The cubic curve is
    /// notionally active at any healthFrac &lt; 1, but the effect is imperceptible extremely close to
    /// full health (e.g. at healthFrac=0.99, (1-healthFrac)^3 = 1e-6) -- registering a 500ms fast-
    /// tick listener for that is pure waste. Burn's fast tick is entered/stopped when
    /// (1-healthFrac) crosses this gap, not when it crosses a design-relevant threshold. Small by
    /// design -- large enough to skip true noise, small enough that no player-visible healing is
    /// ever skipped.</summary>
    public double BurnActivationHealthFracGap { get; set; } = 0.02;

    /// <summary>Interval, in milliseconds, of the fast game-tick listener registered only while
    /// burn conditions hold (entered/exited on the shared 6s slow tick and immediately on
    /// damage received). Too coarse a listener would make burn feel laggy in combat; this is
    /// deliberately much faster than the 6s Thew/Band cadence, but only runs while burning.</summary>
    public int BurnFastTickMs { get; set; } = 500;

    // ── Frenzy (Orc, Phase 2 T4) ──

    /// <summary>Master toggle for Frenzy. Independent of EnableBurn -- both key off the same
    /// health-fraction trigger and spend from the same Thew pool (see FrenzyCurveExponent's doc
    /// comment for the composition rationale) but are separately disableable.</summary>
    public bool EnableFrenzy { get; set; } = true;

    /// <summary>Exponent on the Frenzy curve -- same shape family as Burn (T3), same threshold-
    /// free trigger (health &lt; 100%, gated for performance only by
    /// BurnActivationHealthFracGap/FrenzyThewFloor, not a game-design cutoff): speed/damage bonus
    /// and Thew cost per second all scale as (1-healthFrac)^FrenzyCurveExponent. Defaults to the
    /// same value as BurnCurveExponent but is independently tunable.</summary>
    public double FrenzyCurveExponent { get; set; } = 3.0;

    /// <summary>Walkspeed delta (Stats.Set-delta units, matching WalkSpeedDelta's convention) at
    /// the curve's peak (healthFrac -&gt; 0), scaled by (1-healthFrac)^FrenzyCurveExponent at every
    /// point below. TUNING: new mechanic, no locked number -- chosen, flagged for review.</summary>
    public double FrenzyMaxSpeedBonus { get; set; } = 0.25;

    /// <summary>Melee damage delta (Stats.Set-delta units, matching BulkyMeleeDamageBonus's
    /// convention) at the curve's peak, same scaling as FrenzyMaxSpeedBonus. TUNING: new
    /// mechanic, no locked number -- chosen, flagged for review.</summary>
    public double FrenzyMaxDamageBonus { get; set; } = 0.35;

    /// <summary>Thew spend rate (per second) at the curve's peak, same scaling as
    /// FrenzyMaxSpeedBonus/FrenzyMaxDamageBonus. At a genuinely dangerous healthFrac=0.2 this
    /// works out to ~0.015 Thew/s (0.03 * 0.8^3) -- roughly a third of a band's worth of Thew
    /// (~0.30-0.40) per 20-25s of sustained near-death combat. Combined with Burn's own worst-case
    /// spend (BurnMaxHealPerSecond * BurnThewPerHp = 0.045 Thew/s), a prolonged fight at critical
    /// health can burn through Thew fast -- intentional, per the brief's "the correct response to
    /// a bloodied orc is to leave." TUNING: new mechanic, no locked number -- chosen, flagged for
    /// review.</summary>
    public double FrenzyMaxThewPerSecond { get; set; } = 0.03;

    /// <summary>Thew floor Frenzy cannot cross, mirroring BurnThewFloor by default but
    /// independently configurable (Burn and Frenzy read/write the same Thew pool with no
    /// reservation between them, same no-coordination precedent as any two Thew spenders -- see
    /// thew-audit.md Q7 -- so each needs its own floor check).</summary>
    public double FrenzyThewFloor { get; set; } = 0.02;

    /// <summary>Interval, in milliseconds, of Frenzy's own fast game-tick listener. Mirrors
    /// BurnFastTickMs by default, independently tunable.</summary>
    public int FrenzyFastTickMs { get; set; } = 500;

    /// <summary>Minimum change in Frenzy's computed walkspeed/meleeWeaponsDamage stat values
    /// before they're re-written via Stats.Set -- Frenzy recomputes every fast tick (the bonus
    /// tracks current healthFrac continuously, not a value fixed at trigger time), so without a
    /// write-avoidance threshold this would spam WatchedAttributes dirty/sync on every tick.</summary>
    public double FrenzyStatWriteThreshold { get; set; } = 0.02;

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

    /// <summary>DORMANT (Phase G3): GoblinDigModifierBehavior is re-homed to src/BugRace/ and
    /// no longer registered, so this flag currently has no effect. Left in place (not
    /// deleted) so existing rfmechanics.json installs don't silently drop the key on the next
    /// StoreModConfig rewrite. Was: master toggle for the Goblin bare-hand dig bonus on
    /// Soil/Sand/Gravel-tier blocks (soil, sand, gravel, packeddirt, drypackeddirt, and the
    /// spit-packed variants), via vanilla's own GetMiningSpeedModifier extension point.</summary>
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
    /// re-written via Stats.Set. Mirrors TreeProximityStatWriteThreshold -- avoids
    /// per-tick sync writes.</summary>
    public double GoblinTunnelStatWriteThreshold { get; set; } = 0.02;

    // ── Goblin spit-packed earth (Phase G2) ──

    // NOTE (G2.1): GoblinDiggableEarthCodePrefixes was removed here. It was a third,
    // hand-maintained Code.Path prefix list duplicating what
    // GoblinSpitPackingPatch.ResolveConversionTarget already knows -- and it drifted (5 of
    // 10 spit-packed families were missing from it, so goblins got no tunnel walkspeed
    // bonus tunneling under those ceilings). RFGoblinTunnelBehavior now calls
    // GoblinSpitPackingPatch.IsGoblinEarth directly instead of consulting a config array.
    // See notes/goblin-dig-materials-handover.md for the drift history.

    /// <summary>DORMANT (Phase G3): GoblinSpitPackingPatch is re-homed to src/BugRace/ and its
    /// Harmony attributes are commented out, so this flag currently has no effect. Left in
    /// place (not deleted) so existing rfmechanics.json installs don't silently drop the key
    /// on the next StoreModConfig rewrite. Was: master toggle for the spit-packed earth
    /// conversion. Soil converted to vanilla's packeddirt; Sand/Gravel converted to the
    /// spitpackedsand-{rock}/spitpackedgravel-{rock} blocktypes shipped in this mod's own
    /// assets.</summary>
    public bool EnableGoblinSpitPacking { get; set; } = true;

    // ── Goblin rot aura (Phase G3) ──

    /// <summary>Master toggle for the rot aura (spoilage acceleration, larder hold, and crop
    /// stunting -- Tasks 1-3 of the Phase G3 rebuild that replaced spit-packed earth).</summary>
    public bool EnableGoblinRotAura { get; set; } = true;

    /// <summary>Master toggle for sweeping nearby players' carried inventory (hotbar and worn
    /// backpack contents), independent of EnableGoblinRotAura's own placed-container sweep so
    /// carried-inventory sweeping can be disabled without disabling the container sweep.</summary>
    public bool EnableGoblinRotAuraCarriedInventory { get; set; } = true;

    /// <summary>Throttle interval (seconds) for the aura sweep, same accum-field pattern as
    /// every other rfmechanics behavior. Matches vanilla's own 1.5-3s precedent for comparable
    /// radius scans (BlockVicinityCondition, EntityBehaviorBodyTemperature).</summary>
    public double GoblinRotAuraTickInterval { get; set; } = 2.0;

    /// <summary>Horizontal radius floor -- the max-intensity end of Task 4's intake-driven
    /// range (rot-starved goblins: narrow and intense).</summary>
    public int GoblinRotAuraRadiusMin { get; set; } = 4;

    /// <summary>Horizontal radius ceiling -- the wide/rot-fed end of Task 4's range, and the
    /// approved sweep-cost cap (~6,700 positions/sweep at the matching VerticalHalfExtent).</summary>
    public int GoblinRotAuraRadiusMax { get; set; } = 15;

    /// <summary>Vertical clamp (+/-V) on the sweep, keeping it a flattened cylinder rather than
    /// a full cube -- most of the horizontal reach without the Y-axis cost.</summary>
    public int GoblinRotAuraVerticalHalfExtent { get; set; } = 3;

    /// <summary>Intensity anchor at GoblinRotAuraRadiusMin (Task 4's narrow/intense end).
    /// Intensity at other radii is derived, not independently configured -- see Task 4's
    /// radius^2*intensity-constant mapping.</summary>
    public double GoblinRotAuraIntensityAtMinRadius { get; set; } = 1.0;

    /// <summary>DEPRECATED (Phase G3 rate-model fix): replaced by GoblinRotAuraRateMultiplier.
    /// This was an absolute in-game-hours-per-sweep constant applied identically regardless of an
    /// item's own transitionHours -- against vanilla's ~30 in-game-hours/real-hour default that
    /// worked out to ~900 in-game-hours/real-hour (~30x too fast) and, because the delta was
    /// absolute rather than proportional to each item's own transitionHours, a ~150x spread
    /// between how many aura-multiples short- and long-lived foods effectively received. No
    /// longer read anywhere -- left in place (not deleted, not renamed) purely so existing
    /// rfmechanics.json installs with this key already written don't get a stale/misleading value
    /// silently dropped on the next StoreModConfig rewrite. See GoblinRotAuraBehavior.
    /// AccelerateSlots for the replacement.</summary>
    public double GoblinRotAuraBaseDeltaHoursPerSweep { get; set; } = 0.5;

    /// <summary>Phase G3 rate-model fix: the aura advances spoilage at this multiple of the item's
    /// own normal (vanilla, unaided) rate -- e.g. 3.0 means a stack held in the aura reaches the
    /// hold ceiling about 3x faster than it would sitting untouched. Derived live each sweep from
    /// world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul (in-game-hours per real-hour,
    /// ~30 at vanilla defaults) and GoblinRotAuraTickInterval, NOT a hardcoded 30 -- tracks
    /// CalendarSpeedMul if a server changes it. The aura only ever adds (RateMultiplier - 1) worth
    /// of extra calendar-hours per sweep, since vanilla's own passive aging already supplies the
    /// first 1x for free (baked into TransitionedHours by UpdateAndGetTransitionState before the
    /// aura's own delta is added) -- adding a full multiplier on top would make the effective total
    /// (RateMultiplier + 1)x instead of RateMultiplier x. See GoblinRotAuraBehavior.AccelerateSlots
    /// for the full derivation, including why this does not (and structurally cannot) vary by the
    /// item's own transitionHours despite fixing the old spread bug.</summary>
    public double GoblinRotAuraRateMultiplier { get; set; } = 3.0;

    /// <summary>Ceiling on TransitionLevel the aura will ever push a stack to -- food degrades
    /// toward "about to spoil" but the aura alone never fully destroys it (larder hold).
    /// Maps linearly and exactly to the tooltip's displayed spoilage percentage (TransitionLevel
    /// == HoldFraction at the hold ceiling) -- 0.85 displays as "85%".</summary>
    public double GoblinRotAuraHoldFraction { get; set; } = 0.85;

    /// <summary>Minimum ACCELERATION delta (hours) before a SetTransitionState+MarkDirty write
    /// happens -- write-avoidance only, never gates the hold-ceiling write-back. Sized against
    /// the worst case: at RadiusMax/min Intensity (RadiusMin^2/RadiusMax^2 = 16/225 = 0.071),
    /// max achievable delta is BaseDeltaHoursPerSweep * 1.0 * 0.071 ~= 0.036 hours -- this must
    /// stay comfortably below that or every wide/rot-fed goblin's acceleration silently zeroes
    /// out.</summary>
    public double GoblinRotAuraWriteThresholdHours { get; set; } = 0.01;

    /// <summary>Phase G3 hold-creep fix: past the hold ceiling, food no longer parks there
    /// forever -- the delta becomes the normal computed per-sweep delta multiplied by this
    /// factor, so it keeps crawling (very slowly) toward fully spoiled instead of hard-clamping.
    /// 0.05 = 5% of the normal accelerated-phase rate. See GoblinRotAuraBehavior.AccelerateSlots'
    /// hold-creep block.</summary>
    public double GoblinRotAuraHoldCreepFactor { get; set; } = 0.05;

    /// <summary>Absolute floor (in-game hours) under GoblinRotAuraHoldCreepFactor's computed
    /// delta. Without it, a low-intensity (rot-fed, wide-slow) goblin's creep delta shrinks
    /// toward zero along with its intensity and effectively reproduces the old hard clamp --
    /// this guarantees a minimum crawl regardless of intensity. 0.001h (~3.6 real seconds'
    /// worth at vanilla calendar defaults) was picked to sit below the accelerated-phase delta
    /// at typical intensities (so it doesn't distort GoblinRotAuraHoldCreepFactor's intended
    /// scaling in the common case) while still bounding worst-case time-to-fully-spoiled to
    /// roughly an hour or two rather than an effectively-unbounded asymptote.</summary>
    public double GoblinRotAuraHoldCreepFloorHours { get; set; } = 0.001;

    /// <summary>Minimum spatial-falloff strength (0..1, one block above the farmland,
    /// independent of Intensity -- see GoblinRotAuraRegistry's doc comment) required to pause
    /// that crop's growth check.</summary>
    public double CropStuntMinStrength { get; set; } = 0.15;

    /// <summary>Decay half-life (in-game calendar hours) used to decay dietsetup's rot-intake
    /// accumulator live on read. MUST match dietsetup's own RotIntakeHalfLifeHours
    /// (DietSetupConfig.cs) -- a documented cross-reference, not independently tunable, since
    /// rfmechanics has no assembly reference to dietsetup to read the value directly.</summary>
    public double GoblinRotAuraIntakeHalfLifeHours { get; set; } = 48.0;

    /// <summary>Master toggle for letting goblins eat game:rot (grants it a minimal
    /// FoodNutritionProperties via GoblinRotEdiblePatch; vanilla and every other player still
    /// see it as inedible).</summary>
    public bool EnableGoblinRotEdible { get; set; } = true;

    /// <summary>Satiety granted when a goblin eats game:rot. Deliberately far below dietsetup's
    /// raw-redmeat grant (30, see dietsetup grants.json) -- this is a survival-floor mechanic,
    /// not a food source.</summary>
    public float GoblinRotEdibleSatiety { get; set; } = 3.0f;

    // ── Goblin spit charges (rot repair) ──

    /// <summary>Master toggle for goblin spit charges (RfGoblinSpitChargeGrantPatch +
    /// RfGoblinSpitRepairBehavior). A goblin's gut renders decay into a binding secretion --
    /// eating game:rot grants charges, empty-hand interact on a reparable block spends one to
    /// apply repair through the same repairState math vanilla glue uses.</summary>
    public bool EnableGoblinSpitCharges { get; set; } = true;

    /// <summary>Spit charges granted per qualifying game:rot eat (gated the same way
    /// GoblinRotEdiblePatch gates edibility itself: goblin trait + secondsUsed &gt;= 0.95f
    /// completion, see RfGoblinSpitChargeGrantPatch).</summary>
    public int SpitChargesPerRot { get; set; } = 2;

    /// <summary>Max spit charges a goblin can hold. Deliberately kept below the number of
    /// applications needed to fully repair a reparability-6 block (8, at SpitRepairGain 0.125)
    /// so a goblin cannot finish a repair without stopping to eat again -- see SpitRepairGain's
    /// doc comment for the derivation and the known jonaslamp (reparability 4) exception.</summary>
    public int SpitChargeCap { get; set; } = 6;

    /// <summary>Repair applied per spit charge spent, fed into vanilla's own
    /// BehaviorReparable.cs:151 formula (repairQuantity * 5 / (reparability - 1)) unchanged. At
    /// reparability 6 (6 of the 7 target blocktypes) this reduces to x1.0, so 0.125 -&gt; 8
    /// applications per block against a cap of 6 -- the intended gap. Known accepted exception:
    /// jonaslamp (reparability 4) reduces to x1.667, so ~5 applications -- under the cap, so a
    /// goblin can fully repair one in a single load. Lowering the global cap to close that gap
    /// would cost the other six blocks room instead (6/8 -&gt; 4/8), a bigger regression than
    /// accepting jonaslamp as an exception; a future per-reparability override could fix both.</summary>
    public double SpitRepairGain { get; set; } = 0.125;

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
