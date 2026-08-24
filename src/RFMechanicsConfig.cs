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

    /// <summary>Scales climbUpSpeed/climbDownSpeed by (1 + ClimbSpeedFactor); negative slows the
    /// dwarf. LANDMINE: vanilla field names are inverted -- Sneak (descend) reads climbUpSpeed, Jump (ascend) reads climbDownSpeed.</summary>
    public double ClimbSpeedFactor { get; set; } = -0.2;

    /// <summary>Flat satiety cost per second of climbing (ascent only). 2.4 = vanilla sprint surcharge at 30 TPS, i.e. sprint parity.</summary>
    public double ClimbSaturationPerSecond { get; set; } = 2.4;

    /// <summary>Master toggle for the climb speed curve.</summary>
    public bool EnableClimbSpeed { get; set; } = true;

    /// <summary>Master toggle for the climb saturation drain.</summary>
    public bool EnableClimbSaturation { get; set; } = true;

    /// <summary>Batch interval, in seconds, for flushing banked climb time into a satiety
    /// drain. ClimbSaturationPatch accumulates climb seconds every tick but only applies the
    /// drain once this many seconds have passed, to avoid a Saturation write every tick.</summary>
    public double ClimbSaturationFlushIntervalSeconds { get; set; } = 10.0;

    /// <summary>Delay, in milliseconds, before ClimbSpeedPatch retries applying climb scaling
    /// after the entity-link race (player.Entity null during construction).</summary>
    public int ClimbLinkRetryDelayMs { get; set; } = 2000;

    // ── Branchy leaves passthrough (Elf) ──

    /// <summary>Trait code granting the branchy-leaves collision passthrough. Loaded from
    /// config so it is trivially changeable, mirroring DwarfTraitCode.</summary>
    public string ElfTraitCode { get; set; } = "rf-elf-positive";

    /// <summary>Master toggle for the branchy-leaves collision passthrough.</summary>
    public bool EnableBranchyLeavesPassthrough { get; set; } = true;

    /// <summary>Logs branchy-leaf boxes stripped vs. retained per FilterBranchyLeaves call.
    /// Default off -- diagnostic only, for validating the E3.4 foot-level exclusion rule during
    /// the manual test pass, not meant to run in production (this is a per-substep hot path).</summary>
    public bool LogLeafStandingBoxCounts { get; set; } = false;

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

    /// <summary>Tick cadence, in seconds, for RFTreeProximityBehavior's tree scan.</summary>
    public double TreeProximityTickInterval { get; set; } = 3.0;

    // ── Elf reduced hunger drain ──

    /// <summary>Master toggle for the Elf reduced-hunger-drain effect, parity with every other
    /// mechanic in this config. Unconditional for elves now (no attunement threshold) -- applied
    /// by ElfIdentityBehavior directly off IsElf.</summary>
    public bool EnableElfHungerDrainReduction { get; set; } = true;

    /// <summary>Hungerrate multiplier while active, applied as a Stats.Set delta (target - 1)
    /// under source "rf-elf-attunement" on the vanilla "hungerrate" category, mirroring
    /// HungerRateMult's convention. 0.85 = 15% less hunger drain.</summary>
    public double ElfHungerRateMult { get; set; } = 0.85;

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

    // ── Telescopic vision / zoom (Elf) ──

    /// <summary>Master toggle for Elf telescopic vision.</summary>
    public bool EnableElfZoom { get; set; } = true;

    /// <summary>FOV multiplier at full zoom. Milder than Spyglass's 0.08 floor -- there's no
    /// tube prop framing the view here, so a spyglass-strength drop reads as a bug, not a body trait.</summary>
    public double ElfZoomFovMult { get; set; } = 0.5;

    /// <summary>Milliseconds for the FOV multiplier to ease fully between 1.0 and ElfZoomFovMult, either direction.</summary>
    public double ElfZoomTransitionMs { get; set; } = 400.0;

    /// <summary>Milliseconds RightMouseDown must be held continuously before the zoom target
    /// engages. Guards against the tick/render scheduling race between EntityControls.RightMouseDown
    /// (fixed 20ms tick) and Controls.HandUse (set from render-stage interaction dispatch) --
    /// without this, a container/block click can read as a one-tick zoom-then-cancel flicker.</summary>
    public double ElfZoomEngageDelayMs { get; set; } = 120.0;

    // ── Elf identity ──

    /// <summary>Tick cadence, in seconds, for ElfIdentityBehavior's race-cache refresh (after the
    /// immediate Initialize()-time refresh). Matches GoblinRotAuraTickInterval's 2.0s precedent.</summary>
    public double ElfIdentityTickInterval { get; set; } = 2.0;

    // ── Elf step height ──

    /// <summary>Master toggle for the Elf step-height boost.</summary>
    public bool EnableElfStepHeight { get; set; } = true;

    /// <summary>StepHeight value applied to elves (vanilla default is 0.6f). 1.0 is exactly the
    /// threshold FindSteppableCollisionBox checks against, so elves auto-climb single-block-tall
    /// obstacles (fences, stair edges) -- see ElfStepHeightBehavior and the client-side toggle.</summary>
    public double ElfStepHeightValue { get; set; } = 1.0;

    /// <summary>Default state of each player's own step-height toggle (WatchedAttributes
    /// "rf-elf-stepheight-enabled"), flippable via the "/rfelfstepheight toggle" command or its
    /// bound client hotkey (default Ctrl+H).</summary>
    public bool ElfStepHeightDefaultEnabled { get; set; } = true;

    // ── Elf living harvest yield (Phase 4 stub, E3.1) ──

    /// <summary>Yield multiplier at attunement 0. Stub only -- Phase 4 wires this to the actual
    /// harvest tool once D3 (shears vs. knife) is settled; ComputeHarvestYieldMultiplier is not
    /// called from anywhere yet.</summary>
    public double ElfHarvestYieldPoor { get; set; } = 0.25;

    /// <summary>Yield multiplier at attunement 100.</summary>
    public double ElfHarvestYieldFull { get; set; } = 1.0;

    // ── Thew (Orc) ──

    /// <summary>Master toggle for the Thew mechanic (gain/decay tick and preserved-protein multiplier).</summary>
    public bool EnableThew { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for ThewBehavior's gain/decay evaluation.</summary>
    public double ThewTickInterval { get; set; } = 6.0;

    /// <summary>Trait code for the orc race. Note the model itself is spelled "ork" in racialequality/PlayerModelLib -- "orc" is this mod's own naming.</summary>
    public string OrcTraitCode { get; set; } = "rf-orc-positive";

    /// <summary>Multiplier applied to MaxSaturation for orc players (bigger stomach), relative to
    /// the vanilla base (1500). Combined with racialability's own "maxSaturationFactor" trait
    /// stat (e.g. the bottomless-stomach ability, 3x) per StomachStackingMode.</summary>
    public double OrcStomachMultiplier { get; set; } = 2.5;

    /// <summary>How OrcStomachMultiplier combines with racialability's own "maxSaturationFactor"
    /// when both are active. Max (default): take the larger candidate, never compounds.
    /// Multiply: the original behavior (2.5x * 3x = 7.5x), preserved as an option.</summary>
    public OrcStomachStackingMode StomachStackingMode { get; set; } = OrcStomachStackingMode.Max;

    /// <summary>Ceiling Thew is reset down to on death, never up -- a Thew &gt; this value drops to
    /// it; Thew already at or below it is left alone.</summary>
    public double ThewDeathResetCap { get; set; } = 0.4;

    /// <summary>Food-type gate on Thew gain: whenever the last item eaten resolved to
    /// Fruit/Vegetable/Grain, BOTH the hourly tick gain and the eat-pulse are blocked regardless
    /// of ProteinLevel/satFrac, so a residually-elevated ProteinLevel from an earlier meat meal
    /// can't be "ridden" by topping off satiety on cheap grain/veg/fruit afterward.</summary>
    public bool EnableThewFoodTypeGate { get; set; } = true;

    /// <summary>Three satiety zones, no ramp: gain above ThewGainSatietyGate, drift between it
    /// and ThewDecayLowSatietyThreshold, decay below that, all per in-game hour (ThewBehavior
    /// samples world.Calendar.ElapsedHours deltas, not real time, so this rate is invariant to
    /// the server's day length). Applied before the per-band ThewGainBandMult multiplier.</summary>
    public double ThewGainPerHour { get; set; } = 0.0025;

    /// <summary>Per-band multiplier on ThewGainPerHour -- Lean gains fastest (provisioning is
    /// easy to keep up), Bulky slowest (war-form resists being built further while already
    /// deep). Locked: Lean 1.2 / Standard 1.0 / Bulky 0.8.</summary>
    public OrcBandTriple ThewGainBandMult { get; set; } = new OrcBandTriple { Lean = 1.2, Standard = 1.0, Bulky = 0.8 };

    /// <summary>satFrac boundary above which the gain zone applies (below it, drift or decay
    /// applies instead -- see ThewDecayLowSatietyThreshold).</summary>
    public double ThewGainSatietyGate { get; set; } = 0.75;

    /// <summary>Per-in-game-hour Thew loss while satFrac sits in the drift zone (between
    /// ThewDecayLowSatietyThreshold and ThewGainSatietyGate) -- well-fed but not gaining.</summary>
    public double ThewDriftPerHour { get; set; } = 0.0025;

    /// <summary>satFrac boundary below which the steeper low-satiety decay applies instead of drift.</summary>
    public double ThewDecayLowSatietyThreshold { get; set; } = 0.20;

    /// <summary>Per-in-game-hour Thew loss while satFrac is below ThewDecayLowSatietyThreshold
    /// but Saturation hasn't hit exactly 0 (see ThewDecayStarvingPerHour for that case).</summary>
    public double ThewDecayLowSatietyPerHour { get; set; } = 0.05;

    /// <summary>Per-in-game-hour Thew loss while Saturation == 0 exactly, matching vanilla's own
    /// starvation-damage trigger. Steepest tier -- overrides ThewDecayLowSatietyPerHour.</summary>
    public double ThewDecayStarvingPerHour { get; set; } = 0.20;

    /// <summary>DORMANT: the eat-pulse mechanic this fed (a flat grant per bite) was removed. No
    /// longer read anywhere; left in place so existing rfmechanics.json installs don't drop the key.</summary>
    public double ThewPerBite { get; set; } = 0.001;

    /// <summary>DORMANT: superseded -- proportional-to-saturation grants no longer need a
    /// farming-prevention cooldown, and a cooldown actively worked against multi-ingredient
    /// meals (which fire OnEntityReceiveSaturation once per ingredient). No longer read anywhere; left in place for install compatibility.</summary>
    public double BiteCooldownSec { get; set; } = 60.0;

    /// <summary>Threshold above which the protein gain condition is met -- checked against BOTH
    /// ProteinLevel and DairyLevel (see ThewBehavior.IsProteinGated). Tuned against ProteinLevel:
    /// one fresh cooked meat delivers +112 protein from near-zero, so 150 requires sustained
    /// meat-eating, not one snack. Whether 150 is well-calibrated for DairyLevel too is untested.</summary>
    public double ProteinGateLevel { get; set; } = 150.0;

    /// <summary>Master toggle for the seasonal Thew gain multiplier. On by default (2026-08-23) --
    /// settled design is orcs bulk before winter and lean out through it.</summary>
    public bool SeasonalGainEnabled { get; set; } = true;

    /// <summary>Per-season Thew gain multipliers, applied only when SeasonalGainEnabled is true.</summary>
    public ThewSeasonalMultipliers SeasonalGainMultipliers { get; set; } = new ThewSeasonalMultipliers();

    /// <summary>One-time starting Thew value applied the first time an entity is ever detected as
    /// orc (character creation, or the first-ever race-swap into orc) -- distinguishes "never
    /// initialized" from "genuinely decayed to zero" via ThewBehavior's InitializedKey sentinel.
    /// 0.4 sits a player just above LeanToStandard (0.35) so a new orc starts Standard, not
    /// several hours of Lean.</summary>
    public double ThewCreationFloor { get; set; } = 0.4;

    /// <summary>Full item codes that count as "preserved protein" for orc, filling ProteinLevel
    /// at PreservedProteinMultiplier instead of the full rate. Default (cured redmeat/bushmeat)
    /// is unreachable in normal survival play -- this is a server-config surface for modded preserved foods, not vestigial.</summary>
    public string[] PreservedProteinItemCodes { get; set; } = new[] { "survival:redmeat-cured", "survival:bushmeat-cured" };

    /// <summary>Multiplier applied to ProteinLevel gain (via nutritionGainMultiplier) for
    /// preserved-protein items. 0.5 = half the protein fill rate of an equivalent fresh item.</summary>
    public double PreservedProteinMultiplier { get; set; } = 0.5;

    // ── Thew Debt (Orc) ──

    /// <summary>Per-in-game-hour Thew moved from ThewBehavior's own tick into paying down
    /// outstanding Burn/Frenzy debt (see BurnDebt/FrenzyDebt), applied to the sum of both
    /// counters. Stalls at Thew == 0 -- see ThewBehavior.OnGameTick's debt-drain step.</summary>
    public double DebtDrainPerHour { get; set; } = 0.04;

    /// <summary>Debt repaid per point of raw saturation on any eat event, regardless of protein
    /// gate or food type -- eating pays debt before anything else. Burn debt is paid first, then
    /// frenzy debt (see ThewDebtRepayPatch).</summary>
    public double DebtRepaidPerSaturationPoint { get; set; } = 0.0000533;

    // ── Puff Cue (Orc) ──

    /// <summary>Master toggle for the client-side orc state particle cue.</summary>
    public bool EnablePuff { get; set; } = true;

    /// <summary>Total debt (BurnDebt + FrenzyDebt) above which the state byte reads "heavy debt"
    /// (3) instead of "light debt" (2).</summary>
    public double HeavyDebtThreshold { get; set; } = 0.15;

    /// <summary>Real seconds between puff bursts while state == 1 (gaining: satFrac above
    /// ThewGainSatietyGate, no debt). Local-player-only render.</summary>
    public double PuffIntervalGaining { get; set; } = 6.0;

    /// <summary>Real seconds between puff bursts while state == 2 (light debt: total debt above 0,
    /// at or below HeavyDebtThreshold). Renders for every nearby player.</summary>
    public double PuffIntervalLightDebt { get; set; } = 3.0;

    /// <summary>Real seconds between puff bursts while state == 3 (heavy debt: total debt above
    /// HeavyDebtThreshold). Renders for every nearby player.</summary>
    public double PuffIntervalHeavyDebt { get; set; } = 1.5;

    /// <summary>Max distance, in blocks, from the viewing client's own player at which a puff
    /// burst still renders -- beyond this, skipped entirely rather than just faded.</summary>
    public double PuffRenderRange { get; set; } = 24.0;

    /// <summary>Particle count per burst, fixed regardless of state -- only the interval between
    /// bursts scales with state, never the burst size. TUNING: not locked.</summary>
    public double PuffParticleCount { get; set; } = 10.0;

    // ── Bands (Orc, Phase 3) ──

    /// <summary>Master toggle for the Band mechanic (state machine, entitySize, and stat
    /// application). Independent of EnableThew -- Thew must still be on for bands to have
    /// anything to key off, but this lets bands be disabled while keeping Thew itself running.</summary>
    public bool EnableBands { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for BandBehavior's slow evaluation (band
    /// hysteresis checks). Matches ThewTickInterval's cadence by convention, independently
    /// tunable.</summary>
    public double BandTickInterval { get; set; } = 6.0;

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

    /// <summary>Max entitySize change per real second, either direction. entitySize now tracks
    /// Thew continuously (see BandBehavior.ComputeTargetSize), not band membership, so this rate
    /// cap is what makes growth/shrink read as a slow drift rather than an instant snap -- e.g.
    /// the full Lean-to-Standard anchor gap (0.90 to 1.12, 0.22) takes 0.22/0.0007 =~ 314s (5.2
    /// real minutes) to fully close.</summary>
    public double SizeChangeRatePerSecond { get; set; } = 0.0007;

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
    /// AiTaskBaseTargetable.CanSensePlayer, no Harmony patch needed). Lean is the stalker/
    /// provisioner band (animals notice it least), Bulky the loudest.</summary>
    public OrcBandTriple AnimalSeekingRangeDelta { get; set; } = new OrcBandTriple { Lean = -0.15, Standard = 0.15, Bulky = 0.40 };

    /// <summary>Bulky-only melee damage delta, applied under source "rf-orc-band" on the vanilla
    /// "meleeWeaponsDamage" category (registered EntityPlayer.cs:393, consumed EntityAgent.cs:390).
    /// Lean/Standard get 0 (no entry needed, but written as 0 to keep the source key's presence
    /// uniform across bands and simplify removal on race-swap-away).</summary>
    public double BulkyMeleeDamageBonus { get; set; } = 0.12;

    /// <summary>Per-band extra delta on "rangedWeaponsAcc", stacked on top of the race-wide -0.25
    /// baseline (raceframework's rf-orc-negative trait). Only Bulky gets an extra penalty.
    /// Combined worst case (-0.40 off a base of 1.0) stays well clear of where BaseAimingAccuracy's formula degrades as the blended value approaches 0.</summary>
    public OrcBandTriple RangedAccDelta { get; set; } = new OrcBandTriple { Lean = 0.0, Standard = 0.0, Bulky = -0.15 };

    /// <summary>Bulky-only delta on "armorWalkSpeedAffectedness" (real vanilla-registered stat,
    /// EntityPlayer.cs:404; already used by dwarf -0.85 / elf-negative +0.8 in traits.json).
    /// -0.5 == "halved" per the locked table, by the same delta convention as those two existing
    /// uses (delta -1.0 would fully cancel armor's walk-speed penalty at GetBlended()==0).</summary>
    public double BulkyArmorWalkSpeedAffectednessDelta { get; set; } = -0.5;

    /// <summary>NOT WIRED -- reserved config surface only. The only consumer of the vanilla
    /// "jumpHeightMul" stat clamps via MathF.Sqrt(MathF.Max(1f, blended)), so any value below
    /// 1.0 has zero effect -- a reduction is not achievable through this stat without a Harmony patch on PModuleOnGround.DoApply. Not implemented speculatively.</summary>
    public double BulkyJumpHeightReduction_UNWIRED { get; set; } = 0.20;

    /// <summary>Per-band delta on "jumpHeightMul" (delta units, same convention as
    /// WalkSpeedDelta -- base stat entry is already 1.0, so blended == 1 + delta). Unlike the
    /// Bulky reduction above, an increase passes straight through PModuleOnGround's
    /// MathF.Max(1f, blended) floor with no patch needed -- jump height itself is proportional
    /// to blended (velocity is scaled by sqrt(blended), height by velocity^2). Lean 3x base
    /// (delta +2.0), Standard 2x base (delta +1.0), Bulky left at 0 (unchanged, matching the
    /// reduction above staying unimplemented).</summary>
    public OrcBandTriple JumpHeightMulDelta { get; set; } = new OrcBandTriple { Lean = 2.0, Standard = 1.0, Bulky = 0.0 };

    /// <summary>NOT WIRED -- reserved config surface only. "KnockbackResistance" lives on the
    /// shared per-entity-TYPE EntityProperties object, not a per-player Stats category; setting
    /// it directly would mutate shared state across every player entity of that type, not just
    /// orc. "Applies on hit" has the same problem in reverse (no player-outgoing-melee hook found). Both need a Harmony patch design decision, not a speculative build.</summary>
    public double StandardKnockbackTakenReduction_UNWIRED { get; set; } = 0.30;

    // ── Burn-to-survive (Orc, Phase 4) ──

    /// <summary>Master toggle for the Burn-to-Survive mechanic. Independent of EnableThew's own
    /// toggle, same convention as EnableBands -- Thew must still be on for there to be anything
    /// to burn, but this lets burn be disabled while Thew/Bands keep running.</summary>
    public bool EnableBurn { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for BurnBehavior's slow evaluation (entry/exit
    /// check). Matches ThewBehavior/BandBehavior's cadence by convention, independently
    /// tunable.</summary>
    public double BurnSlowTickInterval { get; set; } = 6.0;

    /// <summary>DEPRECATED as an activation gate (Phase 2 T3): the locked cubic burn model has no
    /// activation threshold -- the cubic curve is notionally active at any health below 100%,
    /// near-zero close to full health, escalating as health drops. No longer read by
    /// BurnBehavior's trigger check (see BurnActivationHealthFracGap). Left in place so existing
    /// rfmechanics.json installs don't drop the key, and still shown in /rfthew dump for reference.</summary>
    public double BurnHealthFraction { get; set; } = 0.25;

    /// <summary>DORMANT: superseded by BurnMaxHealPerSecond/BurnCurveExponent's cubic curve --
    /// the old flat-rate model healed this many hp/s unconditionally whenever burn was active. No longer read anywhere. Left in place for install compatibility.</summary>
    public double BurnHealPerSecond { get; set; } = 0.75;

    /// <summary>Locked design value: peak heal rate (hp/s) the cubic curve approaches as
    /// healthFrac -&gt; 0 -- heal/s = BurnMaxHealPerSecond * (1-healthFrac)^BurnCurveExponent.</summary>
    public double BurnMaxHealPerSecond { get; set; } = 1.5;

    /// <summary>Locked design value: exponent on the cubic burn curve. Also the default for FrenzyCurveExponent (same shape family), though independently configurable.</summary>
    public double BurnCurveExponent { get; set; } = 3.0;

    /// <summary>Thew debt incurred per HP healed while burning (added to BurnDebt, not
    /// subtracted from Thew directly -- see DebtDrainPerHour). A full vanilla 15-hp bar adds
    /// BurnThewPerHp * 15 =~ 0.075 Thew of debt.</summary>
    public double BurnThewPerHp { get; set; } = 0.005;

    /// <summary>Thew floor burn cannot cross. Below this remaining Thew, burn will not
    /// trigger/continue; vanilla death rules apply untouched from that point.</summary>
    public double BurnThewFloor { get; set; } = 0.02;

    /// <summary>Purely a performance gate, NOT a game-design threshold. The cubic curve is
    /// notionally active at any healthFrac &lt; 1, but the effect is imperceptible extremely
    /// close to full health -- registering a fast-tick listener for that is pure waste. Burn's
    /// fast tick enters/stops when (1-healthFrac) crosses this gap, not a design-relevant threshold.</summary>
    public double BurnActivationHealthFracGap { get; set; } = 0.02;

    /// <summary>Interval, in milliseconds, of the fast game-tick listener registered only while
    /// burn conditions hold (entered/exited on the shared 6s slow tick and immediately on
    /// damage received). Too coarse a listener would make burn feel laggy in combat; this is
    /// deliberately much faster than the 6s Thew/Band cadence, but only runs while burning.</summary>
    public int BurnFastTickMs { get; set; } = 500;

    // ── Frenzy (Orc) ──

    /// <summary>Master toggle for Frenzy.</summary>
    public bool EnableFrenzy { get; set; } = true;

    /// <summary>satFrac at/above which Frenzy's ramp is zero -- passive, no activation event, just
    /// recomputed from current satFrac every fast tick.</summary>
    public double FrenzySatietyGate { get; set; } = 0.50;

    /// <summary>Exponent on the Frenzy ramp: curveMult = (1 - satFrac/FrenzySatietyGate)^this,
    /// zero at FrenzySatietyGate, full at satFrac 0. Same shape family as Burn's curve.</summary>
    public double FrenzyCurveExponent { get; set; } = 3.0;

    /// <summary>Walkspeed delta (Stats.Set-delta units, matching WalkSpeedDelta's convention) at
    /// the curve's peak (satFrac -&gt; 0), scaled by curveMult at every point below the gate.</summary>
    public double FrenzyMaxSpeedBonus { get; set; } = 0.25;

    /// <summary>jumpHeightMul delta (Stats.Set-delta units, matching JumpHeightMulDelta's
    /// convention -- base stat entry is already 1.0, so blended == 1 + sum of every source's
    /// delta) at the curve's peak, same scaling as FrenzyMaxSpeedBonus.</summary>
    public double FrenzyMaxJumpBonus { get; set; } = 1.0;

    /// <summary>satFrac below which Frenzy's bonus starts incurring FrenzyDebt. Above this
    /// threshold (but still under FrenzySatietyGate) the bonus is free.</summary>
    public double FrenzyDebtSatietyThreshold { get; set; } = 0.25;

    /// <summary>Thew debt incurred per second at the curve's peak, only while satFrac is below
    /// FrenzyDebtSatietyThreshold (added to FrenzyDebt, not subtracted from Thew directly -- see
    /// DebtDrainPerHour). TUNING: not locked.</summary>
    public double FrenzyThewPerSecond { get; set; } = 0.005;

    /// <summary>Interval, in milliseconds, of Frenzy's fast game-tick listener, registered
    /// unconditionally in Initialize (Frenzy is passive, no start/stop). Mirrors BurnFastTickMs
    /// by default, independently tunable.</summary>
    public int FrenzyFastTickMs { get; set; } = 500;

    /// <summary>Minimum change in Frenzy's computed walkspeed/jumpHeightMul stat values before
    /// they're re-written via Stats.Set -- Frenzy recomputes every fast tick (the bonus tracks
    /// current satFrac continuously), so without a write-avoidance threshold this would spam
    /// WatchedAttributes dirty/sync on every tick.</summary>
    public double FrenzyStatWriteThreshold { get; set; } = 0.02;

    // ── Orc Wild-Animal Resist (standalone, no Thew/Frenzy dependency) ──

    /// <summary>Master toggle for orc damage resistance against wild-animal attackers.
    /// Deliberately independent of EnableFrenzy/EnableThew -- this exists specifically for an
    /// orc with no Thew budget, so it must not require either to be on.</summary>
    public bool EnableOrcWildAnimalResist { get; set; } = true;

    /// <summary>Health must drop below this fraction remaining (i.e. (1-healthFrac) must
    /// exceed this gap) before any resist applies. The curve is continuous at this
    /// threshold (resist is exactly 0 here, not a step) -- see OrcWildAnimalResistPatch.
    /// TUNING: not locked.</summary>
    public double OrcWildResistActivationHealthFracGap { get; set; } = 0.5;

    /// <summary>Multiplicative damage-taken reduction against wild-animal attackers at the
    /// curve's peak (health -&gt; 0). TUNING: new mechanic, no locked number -- chosen,
    /// flagged for review.</summary>
    public double OrcWildResistMaxBonus { get; set; } = 0.35;

    /// <summary>Exponent on the resist curve, applied to the normalized post-gate health
    /// fraction. Matches FrenzyCurveExponent's default, independently tunable.</summary>
    public double OrcWildResistCurveExponent { get; set; } = 3.0;

    /// <summary>If true, wearing any of the 3 vanilla armor slots (head/body/legs) disables
    /// the resist entirely -- binary by slot, not scaled by protection value, so the player
    /// only has to learn "armor turns this off."</summary>
    public bool OrcWildResistRequiresNoArmor { get; set; } = true;

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
    /// goggles' fuel-derived strength to a ceiling of 0.8, never 1.0. See GoblinDarkvisionModSystem for the Math.Max composition.</summary>
    public double GoblinDarkvisionStrength { get; set; } = 0.8;

    // ── Fall damage reduction (Goblin) ──

    /// <summary>Master toggle for the Goblin fall damage reduction. Separate from
    /// EnableFallDamageReduction (Elf) so either race's reduction can be tuned/disabled
    /// independently even though both share the same FallDamagePatch prefix.</summary>
    public bool EnableGoblinFallDamageReduction { get; set; } = true;

    /// <summary>Fraction of fall damage removed for Goblins, e.g. 0.5 = 50% less fall damage.</summary>
    public double GoblinFallDamageReductionFactor { get; set; } = 0.5;

    // ── Goblin dig speed (Phase G2) ──

    /// <summary>DORMANT: GoblinDigModifierBehavior is re-homed to src/BugRace/ and no longer
    /// registered, so this flag currently has no effect. Left in place so existing
    /// rfmechanics.json installs don't drop the key. Was: master toggle for the Goblin bare-hand
    /// dig bonus on Soil/Sand/Gravel-tier blocks via vanilla's GetMiningSpeedModifier extension point.</summary>
    public bool EnableGoblinDigBonus { get; set; } = true;

    /// <summary>Goblin bare-hand dig rate on diggable earth (replaces the vanilla implicit 1.0
    /// flat rate). 8.0 beats the fastest (steel) shovel on every material, not just a "plausible" mid-tier shovel.</summary>
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

    /// <summary>Code.Path prefixes treated as climbable rock/masonry for goblins. Split by
    /// texture roughness, not material: raw rock, worked stone/brick masonry, and ore veins
    /// are all rough enough to grip. Polished stone, quartz, tile, glass, and loose material
    /// (gravel/sand) are excluded by not matching any of these, as are non-full-cube shapes
    /// (slabs/stairs/course/grating). Config-driven so a modded rock-alike block can be added
    /// without a code change.</summary>
    public string[] GoblinRockClimbCodePrefixes { get; set; } = new[]
    {
        "rock-", "crackedrock-", "meteorite-", "stalagsection-",
        "cobblestone-", "mossycobblestone-", "lichencobblestone-",
        "stonebricks-", "agedstonebricks-", "crackedstonebricks-", "mossybrick-", "lichenbrick-",
        "claybricks-", "drystone-", "mudbrick-", "peatbrick-", "refractorybrick-", "ore-"
    };

    // ── Goblin tunnel speed (Phase G2) ──

    /// <summary>Master toggle for the goblin tunnel-speed walkspeed bonus.</summary>
    public bool EnableGoblinTunnelSpeed { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for RFGoblinTunnelBehavior's earth-check scan.</summary>
    public double GoblinTunnelTickInterval { get; set; } = 3.0;

    /// <summary>Walkspeed bonus applied while a goblin is under diggable earth (ceiling within 1-2 blocks overhead), Stats.Set source "tunneling". Still being tuned.</summary>
    public double GoblinTunnelSpeedBonus { get; set; } = 0.15;

    /// <summary>Minimum change in the computed tunneling walkspeed value before it is
    /// re-written via Stats.Set. Mirrors TreeProximityStatWriteThreshold -- avoids
    /// per-tick sync writes.</summary>
    public double GoblinTunnelStatWriteThreshold { get; set; } = 0.02;

    // ── Goblin spit-packed earth (Phase G2) ──

    /// <summary>DORMANT: GoblinSpitPackingPatch is re-homed to src/BugRace/ and its Harmony
    /// attributes are commented out, so this flag currently has no effect. Left in place so
    /// existing rfmechanics.json installs don't drop the key. Was: master toggle for the
    /// spit-packed earth conversion (Soil -&gt; packeddirt, Sand/Gravel -&gt; this mod's spitpacked{family} blocktypes).</summary>
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

    /// <summary>DEPRECATED: replaced by GoblinRotAuraRateMultiplier. This was an absolute
    /// in-game-hours-per-sweep constant applied identically regardless of an item's own
    /// transitionHours, producing a ~150x spread between how many aura-multiples short- and
    /// long-lived foods effectively received. No longer read anywhere; left in place so existing
    /// rfmechanics.json installs don't get a stale value silently dropped. See GoblinRotAuraBehavior.AccelerateSlots for the replacement.</summary>
    public double GoblinRotAuraBaseDeltaHoursPerSweep { get; set; } = 0.5;

    /// <summary>The aura advances spoilage at this multiple of the item's own normal (vanilla,
    /// unaided) rate -- e.g. 3.0 reaches the hold ceiling about 3x faster than sitting untouched.
    /// Derived live each sweep from world.Calendar.SpeedOfTime * CalendarSpeedMul, not a
    /// hardcoded 30, so it tracks CalendarSpeedMul if a server changes it. See
    /// GoblinRotAuraBehavior.AccelerateSlots for why the aura only adds (RateMultiplier - 1)
    /// worth of extra calendar-hours per sweep, not the full multiplier.</summary>
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

    /// <summary>Repair applied per spit charge spent, fed into vanilla's own repair formula
    /// unchanged. At reparability 6 (6 of the 7 target blocktypes) this reduces to x1.0, so 8
    /// applications per block against a cap of 6 -- the intended gap. Known accepted exception:
    /// jonaslamp (reparability 4) only needs ~5, under the cap, so it can be fully repaired in a
    /// single load; lowering the global cap to close that gap would cost the other six blocks room instead.</summary>
    public double SpitRepairGain { get; set; } = 0.125;

    // ── Goblin rot flies (Phase G4) ──

    /// <summary>Master toggle for the rotFlies signal write (GoblinSpitChargeGrantPatch) and the
    /// vanilla-particle aura fly population it drives.</summary>
    public bool EnableGoblinRotFlies { get; set; } = true;

    /// <summary>Master toggle for the exact-count spit fly renderer.</summary>
    public bool EnableGoblinSpitFlies { get; set; } = true;

    /// <summary>Decay half-life (in-game calendar hours) for rfmechanics:rotFlies. Matched to
    /// GoblinRotAuraIntakeHalfLifeHours (48h, 2026-08-23) so flies don't decay to near-zero
    /// while the invisible dietsetup:rotIntake aura is still near full -- the signal itself
    /// stays distinct (still driven by GoblinSpitChargeGrantPatch.GrantRotFlies).</summary>
    public double GoblinRotFliesHalfLifeHours { get; set; } = 48.0;

    /// <summary>rfmechanics:rotFlies gained per qualifying game:rot eat, same gate as spit
    /// charges. 3 rots (0.34*3 ~= 1.02) fills both the spit charge cap and this signal --
    /// intended, not coincidental.</summary>
    public double GoblinRotFliesPerRot { get; set; } = 0.34;

    /// <summary>Ceiling on rfmechanics:rotFlies. Raised 1.0 -> 5.0 (2026-08-23) alongside the
    /// half-life match above -- at 48h, historical eating persists far longer, so the old cap
    /// saturated a heavy-eater and a light-eater goblin at the same fly count. TUNING: not locked.</summary>
    public double GoblinRotFliesCap { get; set; } = 5.0;

    /// <summary>Aura fly count at rotFlies == 0 (still nonzero -- a goblin who hasn't eaten rot
    /// recently isn't fly-free, just sparse).</summary>
    public int GoblinRotFliesCountMin { get; set; } = 10;

    /// <summary>Aura fly count at rotFlies == 1 (cap).</summary>
    public int GoblinRotFliesCountMax { get; set; } = 150;

    /// <summary>Below this rotFlies value, the aura fly population stops spawning entirely
    /// rather than trailing off to an unreadable handful.</summary>
    public double GoblinRotFliesFloor { get; set; } = 0.02;

    /// <summary>Aura fly quad size, in blocks.</summary>
    public double GoblinRotFliesSize { get; set; } = 0.08;

    /// <summary>Period, in seconds, of the slow sine that breathes the aura fly spawn radius.</summary>
    public double GoblinRotFliesBreathPeriod { get; set; } = 20.0;

    /// <summary>Amplitude of the breathing sine, as a fraction of the nominal spawn radius.</summary>
    public double GoblinRotFliesBreathAmplitude { get; set; } = 0.10;

    /// <summary>Time constant, in seconds, for the aura fly cloud's centroid to lag a moving
    /// goblin. Exposed as a live-tunable via /rfflies for in-game feel tuning.</summary>
    public double GoblinRotFliesLagSeconds { get; set; } = 2.0;

    /// <summary>Aura fly particle lifetime, in seconds.</summary>
    public double GoblinRotFliesLifeSeconds { get; set; } = 2.0;

    /// <summary>Range, in blocks, for both fly populations' goblin scan (Step 5) -- shared so aura
    /// and spit flies iterate the same goblin set.</summary>
    public double GoblinRotFliesRange { get; set; } = 32.0;

    /// <summary>Spit fly quad size, in blocks. Halved from the original 0.15 (2026-08-22 tuning
    /// pass) -- the original read oversized once the crossed-quad mesh gave the flies real
    /// silhouette instead of a flat cutout.</summary>
    public double GoblinSpitFliesSize { get; set; } = 0.075;

    /// <summary>Spit fly cloud horizontal radius, in blocks, centred on the goblin's body
    /// midpoint. Set to match More Bugs' RotPlayerFlyRoamRadiusBlocks default (2026-08-22) --
    /// the reference point the user asked for is the "carrying rot in inventory" fly cloud
    /// from that mod, not a from-scratch feel.</summary>
    public double GoblinSpitFliesRadius { get; set; } = 4.5;

    /// <summary>Spit fly cloud vertical half-extent, in blocks, around the goblin's body
    /// midpoint. Set to match More Bugs' RotPlayerFlyVerticalRangeBlocks default (2026-08-22),
    /// same rationale as GoblinSpitFliesRadius.</summary>
    public double GoblinSpitFliesVerticalExtent { get; set; } = 0.45;

    /// <summary>Spit fly retarget interval, in seconds. Slowed 0.3 -> 2.0 (2026-08-22 tuning
    /// pass) to match the aura population's pace -- vanilla's own RandomVelocityChange jitter
    /// (traced in the decompiled ParticleGeneric.cs) resets on roughly a 2s cadence, so this
    /// keeps both populations reading as the same kind of insect rather than the spit flies
    /// darting.</summary>
    public double GoblinSpitFliesRetargetSeconds { get; set; } = 2.0;

    /// <summary>Time constant, in seconds, for the spit fly cloud centroid to lag the goblin.
    /// Tight (&lt;=1.0s) by design -- these are a body-relative indicator, not ambient atmosphere.</summary>
    public double GoblinSpitFliesLagSeconds { get; set; } = 1.0;

    /// <summary>Fade-in/fade-out duration, in seconds, when a spit fly spawns or despawns on a
    /// charge count change. Instant appearance reads as a bug at this render distance.</summary>
    public double GoblinSpitFliesFadeSeconds { get; set; } = 0.4;

    // ── Elf leaf gathering (Phase G2) ──

    /// <summary>Master toggle for the Elf leaf self-drop (ElfLeafDropPatch). Appends the
    /// harvested leaves-*/leavesbranchy-* block's own placed/obtainable form to vanilla's
    /// existing treeseed/stick drops -- does not replace them. Closes G1's open
    /// branchy-leaves ingredient-sourcing gap (see notes/goblin-phase-g1-as-built.md).</summary>
    public bool EnableElfLeafGathering { get; set; } = true;

    // ── Dwarf ore-song (v1 wire-up) ──

    /// <summary>Master toggle for the Dwarf ore-song mechanic (empty-hand knock on raw rock,
    /// nearby ore/gem deposits answer with a positioned sound per material). Client-only,
    /// no network traffic.</summary>
    public bool DwarfOreSongEnabled { get; set; } = true;

    /// <summary>Scan radius in blocks around the knocked rock. Capped at 20 by
    /// DwarfOreSongModSystem (see notes/diagnostics/ore-song-discovery.md Q6 -- vanilla itself
    /// routes comparable-or-smaller inline WalkBlocks scans onto a background thread; a v1
    /// inline scan does not go past this cap).</summary>
    public int OreSongRadius { get; set; } = 16;

    /// <summary>Cooldown between ore-song triggers, milliseconds. Must stay &gt;= the longest
    /// ore-song asset (10s) -- this is what prevents overlapping playback instead of any
    /// fade/dispose-tracking logic (see the v1 brief's Phase 4 rationale).</summary>
    public int OreSongCooldownMs { get; set; } = 10000;

    /// <summary>Max number of material clusters played per knock. Clusters beyond the nearest
    /// this many (by distance) are discarded silently.</summary>
    public int OreSongMaxClusters { get; set; } = 3;

    /// <summary>Greedy cluster-merge distance in blocks -- a hit joins an existing cluster of
    /// the same material if within this distance of that cluster's centroid.</summary>
    public double OreSongClusterMergeDistance { get; set; } = 6;

    /// <summary>Floor applied to a cluster's final playback volume (gradeGain x
    /// distanceFalloff), so distant/poor-grade deposits are still faintly audible rather than
    /// silent.</summary>
    public double OreSongVolumeFloor { get; set; } = 0.15;

    /// <summary>Max random pitch jitter (+/-, fraction of 1.0) applied per cluster. Load-bearing,
    /// not cosmetic -- two same-material clusters at identical pitch are phase-identical files
    /// and comb-filter into sounding like one source.</summary>
    public double OreSongPitchJitter { get; set; } = 0.05;

    // ── Chunk scar tracker (passive data collector, no gameplay consumer -- see
    // ChunkScarTracker.cs's header and notes/race-mechanics/chunk-scar-archived.md) ──

    /// <summary>Master toggle for ChunkScarBreakPatch's write path only -- false makes the
    /// Harmony postfix return immediately with no moddata written. Does not gate /rfscar's
    /// read subcommands; they always report whatever is already on disk.</summary>
    public bool ChunkScarTrackingEnabled { get; set; } = true;

    /// <summary>In-game hours per one point of linear scar decay, applied at read time, never
    /// ticked: decayedCount = max(0, storedCount - floor(elapsedHours / this)).</summary>
    public double ChunkScarDecayHoursPerPoint { get; set; } = 24.0;

    /// <summary>Code.Path prefixes (game domain only) counted as a log/trunk break. Vanilla has
    /// no block family distinct from "log" for tree trunks (log.json's own texture is literally
    /// named "treetrunk") -- one prefix by default, config-driven in case a mod adds a separate
    /// trunk block.</summary>
    public string[] ChunkScarLogBlockCodePrefixes { get; set; } = new[] { "log-" };

    /// <summary>Code.Path prefixes (game domain only) counted toward the separate leaf-break
    /// counter -- recorded but never merged into the scar count, to test whether leaf-break
    /// volume (an axe felling one tree pops dozens of leaf blocks) would pollute a log-break
    /// signal.</summary>
    public string[] ChunkScarLeafBlockCodePrefixes { get; set; } = new[] { "leaves-", "leavesbranchy-" };

    /// <summary>Neighbour sample radius, in map chunks, for /rfscar around and /rfscar bench.
    /// Radius 1 = the 3x3 grid centered on the calling player's map chunk.</summary>
    public int ChunkScarNeighborSampleRadius { get; set; } = 1;

    // ── Orc Smell ──

    /// <summary>Master toggle for the Orc Smell mechanic. Client-side only, no server authority.</summary>
    public bool SmellEnabled { get; set; } = true;

    /// <summary>Tick cadence, in milliseconds, for the detection scan and jet emission.</summary>
    public int SmellTickIntervalMs { get; set; } = 500;

    /// <summary>Base detection radius in blocks, at collision_w == 0. Lowered 80 -> 60
    /// (2026-08-23) so total range reads as size-driven (SmellRangePerSize dominant) rather than
    /// a flat tracker with a minor size adjustment.</summary>
    public double SmellRangeBase { get; set; } = 60;

    /// <summary>Additional detection radius in blocks per unit of collision_w.</summary>
    public double SmellRangePerSize { get; set; } = 40;

    /// <summary>Additional detection radius in blocks at the peak of the hunger ramp (satFrac 0
    /// via FrenzySatietyGate/FrenzyCurveExponent's shape, same curve as Frenzy's own ramp) --
    /// zero at satFrac FrenzySatietyGate, full here at satFrac 0. Added on top of the base+size
    /// radius before the hard cap below.</summary>
    public double SmellRangeHungerBonus { get; set; } = 48.0;

    /// <summary>Hard ceiling, in blocks, on every computed smell radius and the scan's maxScan --
    /// the server stops tracking/syncing entities past this distance (see facts doc Section B),
    /// so anything a larger radius would reach is dead range regardless.</summary>
    public double SmellRangeHardCap { get; set; } = 128.0;

    /// <summary>Vertical half-range for the entity scan (GetEntitiesAround's vertRange). Sized
    /// for an 80-160 block horizontal scan -- the old 12-block value was sized for a 30-block
    /// scan and would hide most distant animals behind ordinary terrain relief.</summary>
    public double SmellVerticalRange { get; set; } = 40;

    /// <summary>Jet angular width in degrees at SmellSpreadFarDist (the far pin).</summary>
    public double SmellSpreadFarDeg { get; set; } = 6;

    /// <summary>Jet angular width in degrees at SmellBlowoutStart, where the far curve and the
    /// blowout curve meet. Anchoring both curves to this one value is load-bearing -- anchoring
    /// the far curve to a different distance puts a visible step in jet width at that range.</summary>
    public double SmellSpreadMidDeg { get; set; } = 20;

    /// <summary>Jet angular width in degrees once fully blown out (SmellBlowoutEnd and closer) --
    /// the cloud the jet fans into. 300 leaves only a 60-degree dead arc behind the player;
    /// smaller values leave enough of a gap to still read as a bearing, which defeats the point
    /// of the blowout.</summary>
    public double SmellSpreadNearDeg { get; set; } = 300;

    /// <summary>Distance in blocks at which the jet is at its narrowest (SmellSpreadFarDeg).</summary>
    public double SmellSpreadFarDist { get; set; } = 120;

    /// <summary>Distance in blocks where the fast blowout collapse begins (still SmellSpreadMidDeg
    /// wide here).</summary>
    public double SmellBlowoutStart { get; set; } = 20;

    /// <summary>Distance in blocks where the blowout collapse completes (full SmellSpreadNearDeg
    /// cloud). The 20-to-15 gap is the only distance cue in the mechanic -- crossing it is meant
    /// to read as "the animal is close, use your eyes now."</summary>
    public double SmellBlowoutEnd { get; set; } = 15;

    /// <summary>Jet length in blocks before the blowout. Length depends only on the blowout
    /// curve, never on distance to the source -- a jet that reaches toward the animal is a
    /// rangefinder, not a bearing indicator.</summary>
    public double SmellJetLengthFar { get; set; } = 25;

    /// <summary>Jet length in blocks once fully blown out into a cloud.</summary>
    public double SmellJetLengthNear { get; set; } = 6;

    /// <summary>Dead-zone radius in blocks at the player's face -- both the nearest a particle
    /// can spawn and its designed fade-out target (a particle's lifetime is capped to the
    /// travel time from its spawn point to this radius, so opacity reaches zero right as it
    /// arrives). Raised 1.5 -> 3.0 (2026-08-23) after particles were observed visibly passing
    /// through the player, then 3.0 -> 3.5 (same day) as a belt-and-suspenders margin once the
    /// actual root cause was fixed: EmitJet's particle velocity now includes the player's own
    /// motion (see OrcSmellModSystem.EmitJet), so a moving player no longer invalidates the
    /// spawn-time trajectory this radius assumes.</summary>
    public double SmellJetInnerRadius { get; set; } = 3.5;

    /// <summary>Jet cross-section thickness in degrees per unit of collision_w.</summary>
    public double SmellThicknessDegPerSize { get; set; } = 8;

    /// <summary>Particle quad size in blocks at collision_w == 0 -- a hypothetical extrapolation
    /// point, not a real creature (chicken, the smallest vanilla land fauna, is 0.5). Negative
    /// by design: solved together with SmellParticleSizePerSize so the formula lands exactly on
    /// SmellParticleSizeMin at collision_w 0.5 (chicken) and SmellParticleSizeMax at 1.6 (bear,
    /// polar male, the largest vanilla land fauna) -- chicken is the intended visual floor, not
    /// an arbitrary clamp catching sizes that never occur. TUNING: not locked.</summary>
    public double SmellParticleSizeBase { get; set; } = -0.05;

    /// <summary>Additional particle quad size in blocks per unit of collision_w -- a bear's
    /// scent jet reads as visibly coarser than a chicken's, same idea as
    /// SmellThicknessDegPerSize but for the individual particle instead of the jet's spread.
    /// See SmellParticleSizeBase for how this and the Min/Max clamps were solved together.
    /// TUNING: not locked.</summary>
    public double SmellParticleSizePerSize { get; set; } = 0.22;

    /// <summary>Clamp floor on the size-scaled particle quad -- lands exactly at chicken's
    /// collision_w (0.5), the smallest vanilla land fauna, so chicken is the visual floor by
    /// construction, not an arbitrary illegible-speck guard.</summary>
    public double SmellParticleSizeMin { get; set; } = 0.06;

    /// <summary>Clamp ceiling on the size-scaled particle quad -- lands exactly at bear polar
    /// male's collision_w (1.6), the largest vanilla land fauna, so nothing vanilla ever
    /// actually clamps here; it exists for modded megafauna bigger than any vanilla creature.</summary>
    public double SmellParticleSizeMax { get; set; } = 0.30;

    /// <summary>Particle count multiplier at collision_w == 0, same base+per-size convention as
    /// SmellParticleSizeBase -- applied on top of the distance-based falloff so small prey read
    /// sparser than large prey at every distance, not just up close. TUNING: not locked.</summary>
    public double SmellParticleCountFactorBase { get; set; } = 0.5;

    /// <summary>Additional particle count multiplier per unit of collision_w. TUNING: not
    /// locked.</summary>
    public double SmellParticleCountFactorPerSize { get; set; } = 0.4;

    /// <summary>Clamp floor on the size-scaled particle count multiplier.</summary>
    public double SmellParticleCountFactorMin { get; set; } = 0.3;

    /// <summary>Clamp ceiling on the size-scaled particle count multiplier.</summary>
    public double SmellParticleCountFactorMax { get; set; } = 1.6;

    /// <summary>Particle count basis at scent strength 1 (source near the player relative to its
    /// own detection radius).</summary>
    public int SmellParticlesNear { get; set; } = 16;

    /// <summary>Particle count basis at scent strength 0 (source near its own detection edge). A
    /// floor, not a target -- below this the jet stops reading as a line and starts reading as
    /// noise. Deliberately sparse at range even though the old v1 finding said distance should
    /// never cost signal strength: that finding was about a wide arc reading as noise when
    /// thinly populated, but a narrow jet reads as a line even at this count.</summary>
    public int SmellParticlesFar { get; set; } = 3;

    /// <summary>Exponent shaping how scent strength maps to particle count between
    /// SmellParticlesFar and SmellParticlesNear.</summary>
    public double SmellFalloffExponent { get; set; } = 1.0;

    /// <summary>Hard cap on particles spawned for a single source in one tick, applied after
    /// spread-density scaling.</summary>
    public int SmellMaxParticlesPerSource { get; set; } = 90;

    /// <summary>Hard cap on particles spawned across all sources combined in one tick.</summary>
    public int SmellMaxParticles { get; set; } = 400;

    /// <summary>Max sources emitted per tick. A frame-budget cap, not a legibility choice --
    /// six-plus overlapping jets is intended, not a bug.</summary>
    public int SmellMaxSources { get; set; } = 6;

    /// <summary>Inward drift speed in blocks/sec, toward the player's horizontal position.</summary>
    public double SmellDriftSpeed { get; set; } = 4.0;

    /// <summary>Particle lifetime in seconds.</summary>
    public double SmellParticleLifeSec { get; set; } = 1.2;

    /// <summary>If true, jets emit continuously without holding the focus hotkey.</summary>
    public bool SmellPassiveEnabled { get; set; } = false;

    /// <summary>Milliseconds for the focus fog weight to ramp in on hotkey press. Deliberately
    /// slow (5s) -- the darkening is meant to read as a gradual sensory shift, not an instant
    /// toggle.</summary>
    public int SmellFocusEngageMs { get; set; } = 5000;

    /// <summary>Milliseconds for the focus fog weight to ramp back out on hotkey release.</summary>
    public int SmellFocusReleaseMs { get; set; } = 200;

    /// <summary>Fog density value applied at full focus weight.</summary>
    public double SmellFocusFogDensity { get; set; } = 0.25;

    /// <summary>Milliseconds the hotkey must be continuously held before smell particles start
    /// appearing at all. Keyed off hold duration (OrcSmellShared.HeldMs), independent of the
    /// fog ramp -- the world darkens first, the smell sense kicks in after.</summary>
    public int SmellParticleFadeInStartMs { get; set; } = 3000;

    /// <summary>Milliseconds held at which smell particles reach full opacity.</summary>
    public int SmellParticleFadeInFullMs { get; set; } = 6000;

    /// <summary>Jet RGB for sources classified Predator (CreatureDiet includes Protein, no plant categories).</summary>
    public int[] SmellColorPredator { get; set; } = new[] { 229, 57, 53 };

    /// <summary>Jet RGB for sources classified Herbivore (CreatureDiet has Fruit/Vegetable/Grain, no Protein).</summary>
    public int[] SmellColorHerbivore { get; set; } = new[] { 102, 187, 106 };

    /// <summary>Jet RGB for sources classified Omnivore (CreatureDiet has both Protein and a plant category).</summary>
    public int[] SmellColorOmnivore { get; set; } = new[] { 255, 179, 0 };

    /// <summary>Jet RGB fallback when diet doesn't cleanly classify. Matches the mechanic's original single color.</summary>
    public int[] SmellColorUnknown { get; set; } = new[] { 190, 225, 130 };

    /// <summary>Master toggle for tag-based predator detection. False falls back to
    /// diet-only classification (the pre-existing behavior) if tags misbehave in-game.</summary>
    public bool SmellUseEntityTags { get; set; } = true;

    /// <summary>Entity tags that mark a creature Predator regardless of diet (ANY match --
    /// modders apply predator/ferocious by feel, not a fixed schema, so real creatures have
    /// one without the other). New fauna mods may need new entries here without a rebuild.</summary>
    public string[] SmellPredatorTags { get; set; } = new[] { "predator", "ferocious" };

    /// <summary>Entity codes (domain:path) forced to Predator regardless of tags or diet --
    /// escape hatch for dangerous creatures that are untagged and whose diet reads as
    /// harmless (e.g. an aggressive omnivore boss mob that would otherwise classify
    /// identically to a farm animal).</summary>
    public string[] SmellForcePredatorCodes { get; set; } = new[] { "feverstonewilds:hellboar" };
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
    public double Fall { get; set; } = 1.4;
    public double Winter { get; set; } = 0.6;
}
