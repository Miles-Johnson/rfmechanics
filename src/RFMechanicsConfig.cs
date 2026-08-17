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

    /// <summary>Attunement threshold (must match an entry in AttunementThresholds) at and above
    /// which BranchyLeavesPassthroughPatch retains the branchy-leaf box supporting an Elf's
    /// feet instead of stripping every branchy box outright -- E3.4. Below this threshold,
    /// behavior is unchanged from E3.3: every branchy box is stripped, full passthrough, no
    /// standing.</summary>
    public int LeafStandingAttunementThreshold { get; set; } = 25;

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

    // ── Elf attunement (Phase 1a) ──

    /// <summary>Master toggle for the Elf attunement system (ElfAttunementBehavior's owned
    /// 0-100 WatchedAttributes float). Phase 1a only -- gain/decay and threshold effects land
    /// in later Phase 1a tasks; this toggle already gates the behavior's tick from task one.</summary>
    public bool EnableElfAttunement { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for ElfAttunementBehavior's slow tick (race-cache
    /// refresh now; gain/decay evaluation from Phase 1a task E1.3 onward). Matches
    /// GoblinRotAuraTickInterval's 2.0s precedent.</summary>
    public double AttunementTickInterval { get; set; } = 2.0;

    /// <summary>Block Code.Path prefixes (StartsWith, not wildcard) counting as "forest-natural
    /// ground" -- verified against the live install's actual assets, not guessed. "leaves-grown"
    /// matches every "-grown1-".."-grown7-" variant via the shared prefix while excluding
    /// "-placed-"; "log-grown-"/"log-placed-" deliberately excludes planks and processed log
    /// shapes. Plain soil, stone, and sand are deliberately absent.</summary>
    public string[] AttunementForestBlockCodePrefixes { get; set; } = new[]
    {
        "forestfloor-",
        "mossycobblestone-", "mossyrockpolished-", "mossystonebricks-",
        "leaves-grown", "leavesbranchy-grown",
        "log-grown-", "log-placed-"
    };

    /// <summary>Per-second gain rate toward AttunementCeiling. TUNING (2026-08-17 grove-removal
    /// re-spec): see notes/race-mechanics/elf-attunement-breakdown-v2.md's rate-repricing pass for
    /// the derivation -- ~5/hour, chosen so threshold 10 arrives within a couple hours of forest
    /// presence and the full 0-100 climb takes ~20 hours, matching the design doc's "short session
    /// vs. long-term goal" split. Renamed from AttunementGainRateWild when the wild/grove split
    /// was removed -- there is only one gain context now (Forest).</summary>
    public double AttunementGainRate { get; set; } = 0.0013889;

    /// <summary>Per-second decay rate toward the context's floor -- toward 0 in
    /// AttunementContextKind.None, or back down toward AttunementCeiling if a value somehow sits
    /// above it. TUNING (2026-08-17 grove-removal re-spec): ~3.33x AttunementGainRate, the same
    /// gain:decay ratio the old wild-forest numbers used (0.3 vs 1.0) -- preserves "loses ground
    /// faster than it's gained" at the new scale. At this rate a full climb from 0 drains back to
    /// 0 in ~6 hours of leaving forest entirely, against ~20 hours to build it.</summary>
    public double AttunementDecayRate { get; set; } = 0.0046296;

    /// <summary>Gain ceiling in AttunementContextKind.Forest. Values above this decay back
    /// toward it (AttunementDecayRate) rather than being hard-clamped -- see
    /// ElfAttunementBehavior's tick step. Renamed from AttunementWildCeiling (25) on 2026-08-17:
    /// with no grove, there's no higher ceiling to sit below, so Forest now climbs to the same
    /// 100 that the Attunement property itself clamps to -- this field exists as a separately
    /// tunable server-config surface, not because the value is expected to differ from 100.</summary>
    public double AttunementCeiling { get; set; } = 100.0;

    /// <summary>Minimum change in the live (in-memory) attunement value before it flushes to
    /// WatchedAttributes. Unlike a fully-recomputed field, attunement is an accumulator, so the
    /// true value is tracked in memory between flushes rather than re-derived from the
    /// last-written value -- otherwise a sub-threshold delta would be lost every tick instead of
    /// accumulating. OnEntityDespawn force-flushes on unload/disconnect, so only an ungraceful stop (crash) can lose the residual gap.
    /// TUNING (2026-08-17 rate re-spec): tightened 0.5 -> 0.05. At 0.5, AttunementGainRate's
    /// ~5/hour meant up to ~6 minutes between flushes -- too much to risk on a crash against a
    /// ~20-hour climb. At 0.05 the window is ~36 seconds; the write itself is one
    /// WatchedAttributes.SetFloat on an already-paid slow tick, so the extra write frequency
    /// costs effectively nothing.</summary>
    public double AttunementWriteThreshold { get; set; } = 0.05;

    /// <summary>Attunement thresholds, in ascending order, that fire
    /// ElfAttunementBehavior.ThresholdCrossed on crossing in either direction. Effects subscribe
    /// and hold a bool per threshold rather than ever polling Attunement directly.</summary>
    public int[] AttunementThresholds { get; set; } = new[] { 10, 25, 45, 100 };

    /// <summary>Full width of the dead band around each threshold (Schmitt trigger) -- prevents
    /// a value hovering near a threshold from firing a crossing event every tick. Must exceed
    /// the largest possible single-tick delta. NOT derived automatically from the rate/interval
    /// fields, so RFMechanicsModSystem.ValidateAttunementConfig checks this bound at load time
    /// and warns if a retune violates it silently.
    /// TUNING (2026-08-17 rate re-spec): tightened 2.5 -> 0.2. The 2.5 value was sized against
    /// the old flat-rate model's worst-case tick delta of 2.0; at the new ~5/hour gain rate the
    /// worst-case delta is ~0.009, so 2.5 was ~270x oversized -- a value hovering near threshold
    /// 25 sat in a 2.5-wide dead zone that took ~30 minutes to cross, well past where the design
    /// doc says the threshold should visibly land. 0.2 still clears the worst-case delta by a
    /// wide, safe margin (~21x) while keeping the dead zone (and the real-time cost of crossing
    /// it, ~2.4 minutes at current rates) small enough that thresholds read as landing where the
    /// numbers say they do.</summary>
    public double AttunementThresholdHysteresis { get; set; } = 0.2;

    // ── Elf attunement forest census (Phase 1b) ──

    /// <summary>Block Code.Path prefixes (StartsWith) counting as a living, still-standing tree
    /// for the per-column forest census (ElfForestCensus). Deliberately separate from
    /// AttunementForestBlockCodePrefixes above: that whitelist includes "log-placed-" (cut/placed
    /// logs, fine as ground cover underfoot) plus leaves/moss/soil, none of which should count as
    /// a living tree for the census. Only naturally-grown, still-standing trunks count here.</summary>
    public string[] AttunementCensusLogCodePrefixes { get; set; } = new[] { "log-grown-" };

    /// <summary>Column scan-band floor, in blocks below min(WorldGenTerrainHeightMap,
    /// RainHeightMap) at each column position. Deliberately uses the min of the two heightmaps,
    /// not WorldGenTerrainHeightMap alone: the latter is frozen pre-vegetation and stable, but
    /// also pre-terrain-modification, and would miss a forested valley floor sitting beside a
    /// worldgen ridge. A band that misses trunks under-counts silently, the worst failure mode
    /// available here, so the wider/safer of the two floors wins.</summary>
    public int AttunementCensusSurfaceBandBelow { get; set; } = 4;

    /// <summary>Column scan-band ceiling, in blocks above RainHeightMap at each column position.
    /// Reaches well above the rain-blocking surface to capture trunk/canopy height, not just the
    /// ground-level footprint.</summary>
    public int AttunementCensusSurfaceBandAbove { get; set; } = 24;

    /// <summary>Log count (strictly above) at which a column's census reads as forest-present.
    /// Roughly more than one trunk's worth, so a lone sapling doesn't qualify. No maturity/age
    /// gate exists -- see ElfAttunementContext's check-2 doc comment for why.</summary>
    public int AttunementCensusLogCountThreshold { get; set; } = 12;

    /// <summary>Per-column census freshness window, in milliseconds, before ElfForestCensus.
    /// GetForestPresence re-scans instead of trusting the persisted LogCount. Eager invalidation
    /// on felling (ElfForestCensusInvalidationPatch) punches through this early by marking the
    /// persisted record as needing a rescan regardless of age -- see ElfForestCensus.
    /// InvalidateColumn.</summary>
    public int AttunementCensusTtlMs { get; set; } = 300000;

    /// <summary>Logs elapsed scan time, resulting log count, and prefilter-hit/total-sections
    /// counts every time ElfForestCensus actually runs a section scan (never on a TTL-fresh or
    /// positional-cache hit, since those skip the scan entirely). Default on for this phase to
    /// validate the palette prefilter's real hit rate against production terrain -- the ratio is
    /// what separates "band too wide, prefilter missing" from "band right, per-cell scan is the
    /// real cost" once real numbers come in.</summary>
    public bool AttunementCensusLogTiming { get; set; } = true;

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

    /// <summary>Master toggle for the Thew death penalty. Default ON per the settled design
    /// ("the body burned everything to heal").</summary>
    public bool EnableThewDeathPenalty { get; set; } = true;

    /// <summary>Flat Thew loss applied on death for orc players, when EnableThewDeathPenalty is
    /// on. 0.35 ~= one band's worth of Thew; clamped to 0 by Thew's own setter.</summary>
    public double ThewDeathPenalty { get; set; } = 0.35;

    /// <summary>Food-type gate on Thew gain: whenever the last item eaten resolved to
    /// Fruit/Vegetable/Grain, BOTH the hourly tick gain and the eat-pulse are blocked regardless
    /// of ProteinLevel/satFrac, so a residually-elevated ProteinLevel from an earlier meat meal
    /// can't be "ridden" by topping off satiety on cheap grain/veg/fruit afterward.</summary>
    public bool EnableThewFoodTypeGate { get; set; } = true;

    /// <summary>Decay rate when Thew's gain condition doesn't fire but satFrac is at/above
    /// ThewRampFloor (well-fed but not building Thew). Gentler than ThewDecayUnderfedPerHour
    /// since this orc isn't starving, just eating the wrong things -- but deliberately nonzero so a bread-only diet still erodes Bulky/Standard over hours. TUNING: not locked.</summary>
    public double ThewDecaySatedNonProteinPerHour { get; set; } = 0.03;

    /// <summary>Base Thew gain per real-world elapsed hour at full ramp (sat &gt;= ThewRampCeiling)
    /// + protein-gated, before the per-band ThewGainBandMult multiplier and ramp scaling. Locked design value.</summary>
    public double ThewGainPerHour { get; set; } = 0.10;

    /// <summary>Per-band multiplier on ThewGainPerHour -- Lean gains fastest (provisioning is
    /// easy to keep up), Bulky slowest (war-form resists being built further while already
    /// deep). Locked: Lean 1.2 / Standard 1.0 / Bulky 0.8.</summary>
    public OrcBandTriple ThewGainBandMult { get; set; } = new OrcBandTriple { Lean = 1.2, Standard = 1.0, Bulky = 0.8 };

    /// <summary>Flat Thew decay per real-world elapsed hour applied continuously while the
    /// player's current band is Bulky, regardless of saturation/gorge/decay-tier state --
    /// stacks additively with whichever decay tier is currently active. This is the lever that
    /// makes Bulky a war posture rather than a lifestyle: at the coded defaults, a
    /// well-provisioned non-fighting orc can just barely sustain Bulky.</summary>
    public double BulkyHoldDecayPerHour { get; set; } = 0.025;

    /// <summary>Three-tier decay below ThewRampFloor: Underfed (ThewHungryThreshold..
    /// ThewRampFloor), Hungry (0..ThewHungryThreshold), Starving (Saturation == 0 exactly,
    /// matching vanilla's own starvation-damage trigger). No neutral parking zone exists below
    /// the ramp floor -- above it you build, below it you erode, always at some rate.</summary>
    public double ThewDecayUnderfedPerHour { get; set; } = 0.05;

    /// <summary>See ThewDecayUnderfedPerHour's doc comment -- the middle tier, satFrac between 0
    /// (exclusive) and ThewHungryThreshold.</summary>
    public double ThewDecayHungryPerHour { get; set; } = 0.15;

    /// <summary>See ThewDecayUnderfedPerHour's doc comment -- the steepest tier, only at
    /// Saturation == 0 exactly (vanilla's own starvation-damage zone).</summary>
    public double ThewDecayStarvingPerHour { get; set; } = 0.60;

    /// <summary>Saturation fraction boundary between the Underfed and Hungry decay tiers.</summary>
    public double ThewHungryThreshold { get; set; } = 0.25;

    /// <summary>While true: an orc with Thew &gt; 0 is immune to vanilla's own starvation damage,
    /// suppressed via a Harmony prefix on EntityBehaviorHealth.OnEntityReceiveDamage. At Thew ==
    /// 0 the shield drops and vanilla starvation damage/death applies untouched. See ThewShieldPatch.cs.</summary>
    public bool StarvationShieldWhileThew { get; set; } = true;

    /// <summary>Saturation fraction below which the graded gain ramp is zero; gain scales
    /// linearly from 0 here to full rate at ThewRampCeiling. Also doubles as the upper boundary of the Underfed decay tier.</summary>
    public double ThewRampFloor { get; set; } = 0.50;

    /// <summary>Saturation fraction at which the graded gain ramp reaches its full (1.0)
    /// multiplier. Linear between ThewRampFloor and this value.</summary>
    public double ThewRampCeiling { get; set; } = 1.00;

    /// <summary>DORMANT: superseded by ThewGainPerSaturationPoint -- a flat grant per bite made
    /// nibbling the dominant, farmable Thew income path. No longer read anywhere; left in place so existing rfmechanics.json installs don't drop the key.</summary>
    public double ThewPerBite { get; set; } = 0.001;

    /// <summary>DORMANT: superseded -- proportional-to-saturation grants no longer need a
    /// farming-prevention cooldown, and a cooldown actively worked against multi-ingredient
    /// meals (which fire OnEntityReceiveSaturation once per ingredient). No longer read anywhere; left in place for install compatibility.</summary>
    public double BiteCooldownSec { get; set; } = 60.0;

    /// <summary>Thew granted per point of raw saturation on a qualifying eat event, replacing
    /// ThewPerBite's flat-per-event grant -- rewards eating well (one real meal) over nibbling,
    /// since total reward tracks total saturation eaten rather than event count. Applied to the
    /// pre-nutritionGainMultiplier raw `saturation` parameter, unlike the *Level fields. TUNING:
    /// deliberately small so the eat-pulse stays a bounded top-up, not a rival to the tick gain.</summary>
    public double ThewGainPerSaturationPoint { get; set; } = 0.0000133;

    /// <summary>Ceiling on a single eat event's ThewGainPerSaturationPoint grant, so one unusually high-saturation item can't produce an outsized single jump.</summary>
    public double ThewPerBiteCap { get; set; } = 0.005;

    /// <summary>Threshold above which the protein gain condition is met -- checked against BOTH
    /// ProteinLevel and DairyLevel (see ThewBehavior.IsProteinGated). Tuned against ProteinLevel:
    /// one fresh cooked meat delivers +112 protein from near-zero, so 150 requires sustained
    /// meat-eating, not one snack. Whether 150 is well-calibrated for DairyLevel too is untested.</summary>
    public double ProteinGateLevel { get; set; } = 150.0;

    /// <summary>Master toggle for the seasonal Thew gain multiplier. Off by default per settled design.</summary>
    public bool SeasonalGainEnabled { get; set; } = false;

    /// <summary>Per-season Thew gain multipliers, applied only when SeasonalGainEnabled is true.</summary>
    public ThewSeasonalMultipliers SeasonalGainMultipliers { get; set; } = new ThewSeasonalMultipliers();

    /// <summary>Full item codes that count as "preserved protein" for orc, filling ProteinLevel
    /// at PreservedProteinMultiplier instead of the full rate. Default (cured redmeat/bushmeat)
    /// is unreachable in normal survival play -- this is a server-config surface for modded preserved foods, not vestigial.</summary>
    public string[] PreservedProteinItemCodes { get; set; } = new[] { "survival:redmeat-cured", "survival:bushmeat-cured" };

    /// <summary>Multiplier applied to ProteinLevel gain (via nutritionGainMultiplier) for
    /// preserved-protein items. 0.5 = half the protein fill rate of an equivalent fresh item.</summary>
    public double PreservedProteinMultiplier { get; set; } = 0.5;

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

    /// <summary>Locked design value: Thew cost per HP healed while burning. A full vanilla
    /// 15-hp bar costs BurnThewPerHp * 15 =~ 0.45 Thew.</summary>
    public double BurnThewPerHp { get; set; } = 0.03;

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

    // ── Frenzy (Orc, Phase 2 T4) ──

    /// <summary>Master toggle for Frenzy. Independent of EnableBurn -- both key off the same
    /// health-fraction trigger and spend from the same Thew pool (see FrenzyCurveExponent's doc
    /// comment for the composition rationale) but are separately disableable.</summary>
    public bool EnableFrenzy { get; set; } = true;

    /// <summary>Tick cadence, in seconds, for FrenzyBehavior's slow evaluation (entry/exit
    /// check). Matches Thew/Band/Burn's cadence by convention, independently tunable.</summary>
    public double FrenzySlowTickInterval { get; set; } = 6.0;

    /// <summary>Exponent on the Frenzy curve -- same shape family as Burn, same threshold-free
    /// trigger (gated for performance only, not a game-design cutoff): speed/damage bonus and
    /// Thew cost per second all scale as (1-healthFrac)^FrenzyCurveExponent.</summary>
    public double FrenzyCurveExponent { get; set; } = 3.0;

    /// <summary>Walkspeed delta (Stats.Set-delta units, matching WalkSpeedDelta's convention) at
    /// the curve's peak (healthFrac -&gt; 0), scaled by (1-healthFrac)^FrenzyCurveExponent at every
    /// point below. TUNING: new mechanic, no locked number -- chosen, flagged for review.</summary>
    public double FrenzyMaxSpeedBonus { get; set; } = 0.25;

    /// <summary>Melee damage delta (Stats.Set-delta units, matching BulkyMeleeDamageBonus's
    /// convention) at the curve's peak, same scaling as FrenzyMaxSpeedBonus. TUNING: new
    /// mechanic, no locked number -- chosen, flagged for review.</summary>
    public double FrenzyMaxDamageBonus { get; set; } = 0.35;

    /// <summary>Thew spend rate (per second) at the curve's peak. Combined with Burn's own
    /// worst-case spend, a prolonged fight at critical health can burn through Thew fast --
    /// intentional: "the correct response to a bloodied orc is to leave." TUNING: not locked.</summary>
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

    /// <summary>Code.Path prefixes treated as climbable raw rock for goblins. Four prefixes, not
    /// one: "crackedrock-" is a natural UnstableRock collapse product that doesn't start with
    /// "rock-". Worked stone (cobblestone/polished/stonebricks/quartz/etc.) is excluded by not
    /// matching any of these. Config-driven so a modded rock-alike block can be added without a code change.</summary>
    public string[] GoblinRockClimbCodePrefixes { get; set; } = new[] { "rock-", "crackedrock-", "meteorite-", "stalagsection-" };

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
