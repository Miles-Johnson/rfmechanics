using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative, hidden Thew float (0..1) for orc players, stored in
    /// entity.Attributes (non-synced -- see notes/orc-diagnostic-findings.md §5) so it never
    /// reaches the client HUD. A slow shadow of saturation: climbs at a rate graded by how full
    /// (ThewRampFloor..ThewRampCeiling) the player is, while protein-gated; falls while
    /// saturation is low (starvation) or, unconditionally, while the current band is Bulky
    /// (war-form upkeep); otherwise holds. A separate eat-pulse patch (ThewEatPulsePatch.cs)
    /// adds a small flat grant on qualifying bites, cooldown-gated.
    ///
    /// Attached to every player entity via a JSON patch (seraph-thew.json), same convention as
    /// RestedBehavior/RFTreeProximityBehavior -- the orc-race gate lives inside OnGameTick
    /// (IsOrc()), not in listener registration/lifecycle. This means a live race swap needs no
    /// special-casing: swapping into orc starts passing the gate on the next tick, swapping out
    /// simply stops updating Thew (the stored value sits dormant, unread by anything else),
    /// for free -- matching the T2 phase0 finding that no other rfmechanics stat-output survives
    /// a race swap without an explicit recompute, but Thew has no output to leave stale.
    /// </summary>
    public class ThewBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-orc-thew";
        private const float TickInterval = 6.0f;

        private float accum;

        public ThewBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfthew";

        public float Thew
        {
            get => entity.Attributes.GetFloat(AttributeKey, 0f);
            set => entity.Attributes.SetFloat(AttributeKey, GameMath.Clamp(value, 0f, 1f));
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableThew) return;

            accum += deltaTime;
            if (accum < TickInterval) return;
            accum = 0f;

            bool isOrc = IsOrc();
            ApplyStomachMultiplier(cfg, isOrc);

            if (!isOrc) return;

            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null || hunger.MaxSaturation <= 0f) return;

            float satFrac = hunger.Saturation / hunger.MaxSaturation;
            float rampMult = RampMultiplier(satFrac, cfg);
            bool proteinGated = hunger.ProteinLevel > (float)cfg.ProteinGateLevel;

            float hourFraction = TickInterval / 3600f;
            var band = entity.GetBehavior<BandBehavior>()?.CurrentBand ?? BandBehavior.Band.Lean;

            if (proteinGated && rampMult > 0f)
            {
                float bandMult = (float)BandBehavior.Pick(cfg.ThewGainBandMult, band);
                Thew += (float)cfg.ThewGainPerHour * bandMult * rampMult * hourFraction * GetSeasonalMultiplier(cfg);
            }
            else if (satFrac < (float)cfg.ThewRampFloor)
            {
                // Three-tier decay, no neutral parking zone below the ramp floor -- see
                // RFMechanicsConfig.ThewDecayUnderfedPerHour's doc comment.
                float decayPerHour = DecayTierPerHour(hunger, satFrac, cfg);
                Thew -= decayPerHour * hourFraction;
            }

            // Bulky-only flat bleed, independent of gorge/starvation state above -- stacks with
            // either. This is the lever that stops Bulky being sustainable purely by not
            // starving; see RFMechanicsConfig.BulkyHoldDecayPerHour's doc comment.
            if (band == BandBehavior.Band.Bulky)
            {
                Thew -= (float)cfg.BulkyHoldDecayPerHour * hourFraction;
            }
        }

        /// <summary>
        /// Vanilla MaxSaturation before any multiplier -- player.json:4018, confirmed T1
        /// (orc-phase0-results.md). Used as the common baseline both stacking-mode candidates
        /// are computed from, rather than trying to reverse-engineer it out of a live
        /// MaxSaturation value that may already reflect racialability's own contribution.
        /// </summary>
        private const float VanillaBaseMaxSaturation = 1500f;

        /// <summary>
        /// Continuously-reasserted MaxSaturation target for orc's bigger stomach, combined with
        /// racialability's own "maxSaturationFactor" blended stat (e.g. the bottomless-stomach
        /// ability) per StomachStackingMode. Reworked from a one-time idempotent-flag multiply/
        /// divide (the original design) to a per-tick recompute-and-set, because "max" stacking
        /// needs to compare two candidates freshly every tick, not multiply/divide relative to
        /// whatever the value currently is -- that would still compound with PlayerModelLib's own
        /// reactive rescale-on-change postfix (StatsPatches.ApplyMaxSaturationStats), which fires
        /// on every MaxSaturation read and rescales relative to its own last-seen factor marker.
        /// Reading entity.Stats.GetBlended("maxSaturationFactor") directly sidesteps that
        /// entirely -- it's the actual source stat racialability writes to, not a value derived
        /// from an already-modified MaxSaturation. Runs every tick regardless of isOrc so a
        /// non-orc player's racialability-only contribution (if any) is still asserted correctly.
        /// </summary>
        private void ApplyStomachMultiplier(RFMechanicsConfig cfg, bool isOrc)
        {
            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null) return;

            float racialabilityFactor = entity.Stats.GetBlended("maxSaturationFactor");
            float racialabilityCandidate = VanillaBaseMaxSaturation * racialabilityFactor;

            float target;
            if (isOrc)
            {
                float orcCandidate = VanillaBaseMaxSaturation * (float)cfg.OrcStomachMultiplier;
                target = cfg.StomachStackingMode == OrcStomachStackingMode.Max
                    ? Math.Max(orcCandidate, racialabilityCandidate)
                    : orcCandidate * racialabilityFactor;
            }
            else
            {
                target = racialabilityCandidate;
            }

            if (Math.Abs(hunger.MaxSaturation - target) > 0.5f)
            {
                hunger.MaxSaturation = target;
            }
        }

        /// <summary>
        /// Thew death penalty ("the body burned everything to heal"). Entity.Die does not wipe
        /// entity.Attributes (notes/orc-diagnostic-findings.md §4, confirmed in-game by T1), so
        /// this fires exactly once per death and the reduced Thew value persists through respawn
        /// normally via the same mechanism.
        /// </summary>
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableThew || !cfg.EnableThewDeathPenalty) return;
            if (!IsOrc()) return;

            Thew -= (float)cfg.ThewDeathPenalty;
        }

        /// <summary>
        /// Guard chain matching every other rfmechanics race gate (see
        /// RFTreeProximityBehavior.IsElf): EntityPlayer check, then characterClass null check
        /// (load-bearing -- HasTrait returns true for a null class by default, so classless
        /// entities must be explicitly excluded), then the trait check itself.
        /// </summary>
        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            return charSys.HasTrait(iplayer, cfg.OrcTraitCode);
        }

        /// <summary>Linear ramp: 0 at/below ThewRampFloor, 1 at/above ThewRampCeiling. Public
        /// static so ThewEatPulsePatch's bite gate uses the exact same curve as the tick gain,
        /// rather than a separately-maintained copy.</summary>
        public static float RampMultiplier(float satFrac, RFMechanicsConfig cfg)
        {
            float floor = (float)cfg.ThewRampFloor;
            float ceiling = (float)cfg.ThewRampCeiling;
            if (ceiling <= floor) return satFrac >= ceiling ? 1f : 0f;
            return GameMath.Clamp((satFrac - floor) / (ceiling - floor), 0f, 1f);
        }

        /// <summary>Which of the three decay tiers applies below ThewRampFloor. Starving is keyed
        /// off Saturation itself (not satFrac) to match vanilla's own `Saturation &lt;= 0f`
        /// starvation-damage trigger exactly (EntityBehaviorHunger.SlowTick) rather than a
        /// fraction that could theoretically read as zero from rounding at a nonzero
        /// Saturation.</summary>
        public static string DecayTierName(EntityBehaviorHunger hunger, float satFrac, RFMechanicsConfig cfg)
        {
            if (hunger.Saturation <= 0f) return "Starving";
            if (satFrac < (float)cfg.ThewHungryThreshold) return "Hungry";
            return "Underfed";
        }

        private static float DecayTierPerHour(EntityBehaviorHunger hunger, float satFrac, RFMechanicsConfig cfg)
        {
            return DecayTierName(hunger, satFrac, cfg) switch
            {
                "Starving" => (float)cfg.ThewDecayStarvingPerHour,
                "Hungry" => (float)cfg.ThewDecayHungryPerHour,
                _ => (float)cfg.ThewDecayUnderfedPerHour
            };
        }

        private float GetSeasonalMultiplier(RFMechanicsConfig cfg)
        {
            if (!cfg.SeasonalGainEnabled) return 1f;

            EnumSeason season = entity.World.Calendar.GetSeason(entity.Pos.AsBlockPos);
            ThewSeasonalMultipliers mul = cfg.SeasonalGainMultipliers;
            return season switch
            {
                EnumSeason.Spring => (float)mul.Spring,
                EnumSeason.Summer => (float)mul.Summer,
                EnumSeason.Fall => (float)mul.Fall,
                EnumSeason.Winter => (float)mul.Winter,
                _ => 1f
            };
        }
    }
}
