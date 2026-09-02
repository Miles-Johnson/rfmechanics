using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Orc Band state machine (Lean/Standard/Bulky) driven by ThewBehavior's Thew via hysteresis,
    /// for stats only. entitySize is a separate, continuous function of Thew (see
    /// ComputeTargetSize) rate-capped by SizeChangeRatePerSecond -- it no longer snaps or lerps
    /// on a band cross, and BandSizes now serves only as the anchor points for that map.
    /// Kept as its own behavior rather than folded into ThewBehavior: Thew is hidden resource
    /// math, Bands are a visible state machine owning stats; ThewBehavior reads CurrentBand back
    /// for its own gain multiplier (see ThewBehavior.OnGameTick).
    /// </summary>
    public class BandBehavior : EntityBehavior
    {
        public enum Band { Lean = 0, Standard = 1, Bulky = 2 }

        private const string BandAttributeKey = "rf-orc-band";
        private const string ActiveKey = "rf-orc-band-active";
        private const string StatSource = "rf-orc-band";

        private float accum;

        /// <summary>The entitySize value StepSizeTowardTarget itself last wrote (NaN if not yet
        /// active this load) -- SelfHealEntitySize compares the live attribute against this, not
        /// against the final Thew target, since during an ordinary glide the two legitimately
        /// differ for minutes at a time.</summary>
        private float lastKnownSize = float.NaN;

        /// <summary>Internally-tracked continuous size, stepped every tick regardless of whether
        /// it's been flushed to WatchedAttributes yet -- decoupled from lastKnownSize (the last
        /// value actually written) so the rate cap keeps real-time accuracy while writes stay
        /// quantized to EntitySizeWriteThreshold.</summary>
        private float pendingSize = float.NaN;

        public BandBehavior(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // One-time migration: clears bluntDamageFactor/crushingDamageFactor stamped by old
            // code (no longer applied) onto existing characters; Initialize() runs once per load/spawn.
            entity.Stats.Remove("bluntDamageFactor", StatSource);
            entity.Stats.Remove("crushingDamageFactor", StatSource);
        }

        public override string PropertyName() => "rfband";

        public Band CurrentBand
        {
            get => (Band)entity.Attributes.GetInt(BandAttributeKey, (int)Band.Lean);
            private set => entity.Attributes.SetInt(BandAttributeKey, (int)value);
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableBands) return;

            bool isOrc = IsOrc();

            // Size tracks Thew continuously every tick, independent of the slower band-hysteresis
            // cadence below -- it has nothing to do with band crossings anymore.
            if (isOrc) StepSizeTowardTarget(cfg, deltaTime);

            accum += deltaTime;
            if (accum < (float)cfg.BandTickInterval) return;
            accum = 0f;

            bool active = entity.Attributes.GetBool(ActiveKey);

            if (isOrc && !active)
            {
                // Classifies from current Thew rather than defaulting to Lean, so a returning
                // orc (post race-swap) resumes at the band their Thew actually puts them in.
                var thewBhv = entity.GetBehavior<ThewBehavior>();
                float thew = thewBhv?.Thew ?? 0f;
                Band initial = ClassifyFresh(thew, cfg);

                CurrentBand = initial;
                entity.Attributes.SetBool(ActiveKey, true);
                ApplyBandStats(initial, cfg);
                return;
            }

            if (!isOrc)
            {
                if (active)
                {
                    ClearBandStats();
                    entity.Attributes.SetBool(ActiveKey, false);
                    // entitySize is left alone here: PlayerModelLib already resets it to 1.0 on
                    // this same race swap, and reasserting it would fight that reset. Drop our own
                    // tracking so a later swap back doesn't self-heal against a stale expectation.
                    lastKnownSize = float.NaN;
                    pendingSize = float.NaN;
                }
                return;
            }

            var thewBhv2 = entity.GetBehavior<ThewBehavior>();
            if (thewBhv2 == null) return;

            Band target = EvaluateBand(thewBhv2.Thew, CurrentBand, cfg);
            if (target != CurrentBand)
            {
                CurrentBand = target;
                ApplyBandStats(target, cfg);
            }

            SelfHealEntitySize(cfg);
        }

        /// <summary>Testing hook for /rfthew setband -- forces a band directly, bypassing
        /// hysteresis. Does not touch entitySize: size tracks Thew, not band, so forcing a band
        /// has no direct size effect (see ComputeTargetSize).</summary>
        public void ForceBand(Band band)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            CurrentBand = band;
            entity.Attributes.SetBool(ActiveKey, true);
            ApplyBandStats(band, cfg);
        }

        private static Band ClassifyFresh(float thew, RFMechanicsConfig cfg)
        {
            if (thew >= (float)cfg.BandUpThresholds.StandardToBulky) return Band.Bulky;
            if (thew >= (float)cfg.BandUpThresholds.LeanToStandard) return Band.Standard;
            return Band.Lean;
        }

        /// <summary>Loops the single-step check up to 2 times (bounded: only 3 bands) instead of
        /// checking one adjacent transition -- Frenzy can spend Thew fast enough to cross more
        /// than one band boundary within a single 6s tick.</summary>
        private static Band EvaluateBand(float thew, Band current, RFMechanicsConfig cfg)
        {
            for (int i = 0; i < 2; i++)
            {
                Band next = EvaluateAdjacent(thew, current, cfg);
                if (next == current) return current;
                current = next;
            }
            return current;
        }

        private static Band EvaluateAdjacent(float thew, Band current, RFMechanicsConfig cfg)
        {
            switch (current)
            {
                case Band.Lean:
                    return thew >= (float)cfg.BandUpThresholds.LeanToStandard ? Band.Standard : Band.Lean;
                case Band.Standard:
                    if (thew >= (float)cfg.BandUpThresholds.StandardToBulky) return Band.Bulky;
                    if (thew < (float)cfg.BandDownThresholds.LeanToStandard) return Band.Lean;
                    return Band.Standard;
                case Band.Bulky:
                    return thew < (float)cfg.BandDownThresholds.StandardToBulky ? Band.Standard : Band.Bulky;
                default:
                    return Band.Lean;
            }
        }

        /// <summary>Every stat category is written every call (0 = no bonus) rather than
        /// conditionally added/removed, so a fresh Set() cleanly replaces the prior band's value.</summary>
        private void ApplyBandStats(Band b, RFMechanicsConfig cfg)
        {
            entity.Stats.Set("hungerrate", StatSource, (float)Pick(cfg.HungerRateMult, b) - 1f);
            entity.Stats.Set("walkspeed", StatSource, (float)Pick(cfg.WalkSpeedDelta, b));
            entity.Stats.Set("animalSeekingRange", StatSource, (float)Pick(cfg.AnimalSeekingRangeDelta, b));
            entity.Stats.Set("meleeWeaponsDamage", StatSource, b == Band.Bulky ? (float)cfg.BulkyMeleeDamageBonus : 0f);
            entity.Stats.Set("armorWalkSpeedAffectedness", StatSource, b == Band.Bulky ? (float)cfg.BulkyArmorWalkSpeedAffectednessDelta : 0f);
            entity.Stats.Set("maxhealthExtraPoints", StatSource, (float)Pick(cfg.MaxHpExtraPoints, b));
            entity.Stats.Set("rangedWeaponsAcc", StatSource, (float)Pick(cfg.RangedAccDelta, b));
            entity.Stats.Set("jumpHeightMul", StatSource, (float)Pick(cfg.JumpHeightMulDelta, b));

            // Stats.Set alone doesn't retrigger EntityBehaviorHealth.UpdateMaxHealth(); must call
            // it explicitly for the HP change to take effect immediately.
            entity.GetBehavior<EntityBehaviorHealth>()?.UpdateMaxHealth();
        }

        private void ClearBandStats()
        {
            entity.Stats.Remove("hungerrate", StatSource);
            entity.Stats.Remove("walkspeed", StatSource);
            entity.Stats.Remove("animalSeekingRange", StatSource);
            entity.Stats.Remove("meleeWeaponsDamage", StatSource);
            entity.Stats.Remove("armorWalkSpeedAffectedness", StatSource);
            entity.Stats.Remove("maxhealthExtraPoints", StatSource);
            entity.Stats.Remove("rangedWeaponsAcc", StatSource);
            entity.Stats.Remove("jumpHeightMul", StatSource);

            entity.GetBehavior<EntityBehaviorHealth>()?.UpdateMaxHealth();
        }

        /// <summary>Piecewise-linear map from Thew to entitySize, anchored at BandUpThresholds'
        /// own Lean/Standard boundary values (0.35/0.70) and BandSizes' three values, plus a fixed
        /// Thew==1.0 anchor (Thew's own ceiling, not separately configurable). Below the
        /// Lean-Standard anchor the same segment's slope keeps extending down toward Thew 0 --
        /// no separate low-end anchor exists.</summary>
        public static float ComputeTargetSize(float thew, RFMechanicsConfig cfg)
        {
            float leanThew = (float)cfg.BandUpThresholds.LeanToStandard;
            float standardThew = (float)cfg.BandUpThresholds.StandardToBulky;
            const float bulkyThew = 1f;

            float leanSize = (float)cfg.BandSizes.Lean;
            float standardSize = (float)cfg.BandSizes.Standard;
            float bulkySize = (float)cfg.BandSizes.Bulky;

            if (thew >= standardThew)
            {
                float t = (thew - standardThew) / (bulkyThew - standardThew);
                return standardSize + (bulkySize - standardSize) * t;
            }

            float t2 = (thew - leanThew) / (standardThew - leanThew);
            return leanSize + (standardSize - leanSize) * t2;
        }

        /// <summary>Moves entitySize toward its Thew-derived target, rate-capped by
        /// SizeChangeRatePerSecond. pendingSize steps every tick but only flushes to
        /// WatchedAttributes -- each flush costs a client mesh rebuild via PlayerModelLib -- once
        /// it crosses an EntitySizeWriteThreshold grid line; gating on delta-from-written instead
        /// was rejected because under ordinary drift the rate cap already outpaces the target's
        /// own per-tick movement, so that gate never engages.</summary>
        private void StepSizeTowardTarget(RFMechanicsConfig cfg, float deltaTime)
        {
            float thew = entity.GetBehavior<ThewBehavior>()?.Thew ?? 0f;
            float target = ComputeTargetSize(thew, cfg);
            float written = entity.WatchedAttributes.GetFloat("entitySize", 1f);

            if (float.IsNaN(pendingSize)) pendingSize = written;

            float maxDelta = (float)cfg.SizeChangeRatePerSecond * deltaTime;
            float diff = target - pendingSize;
            pendingSize = Math.Abs(diff) <= maxDelta ? target : pendingSize + Math.Sign(diff) * maxDelta;

            float q = (float)cfg.EntitySizeWriteThreshold;
            float quantized = (float)(Math.Round(pendingSize / q) * q);
            if (quantized != written)
            {
                entity.WatchedAttributes.SetFloat("entitySize", quantized);
                RFMechanicsModSystem.TryUpdatePmlEntityProperties(entity, out _);
                lastKnownSize = quantized;
            }
        }

        /// <summary>Snaps entitySize instantly to its current Thew-derived target if it drifts
        /// from what StepSizeTowardTarget itself last wrote -- e.g. a live race swap or the
        /// character-creation UI resetting/overwriting it. Comparing against lastKnownSize (not
        /// the target directly) is load-bearing: during an ordinary Thew-driven glide, actual size
        /// legitimately sits far from the final target for minutes at a time, and that is not drift.</summary>
        private void SelfHealEntitySize(RFMechanicsConfig cfg)
        {
            if (float.IsNaN(lastKnownSize)) return;

            float actual = entity.WatchedAttributes.GetFloat("entitySize", 1f);
            if (Math.Abs(actual - lastKnownSize) <= 0.001f) return;

            float thew = entity.GetBehavior<ThewBehavior>()?.Thew ?? 0f;
            float target = ComputeTargetSize(thew, cfg);
            // Quantize here too -- an unquantized write lands off-grid, so the next tick's
            // StepSizeTowardTarget would see it as a fresh grid crossing and write again immediately.
            float q = (float)cfg.EntitySizeWriteThreshold;
            float healed = (float)(Math.Round(target / q) * q);

            entity.WatchedAttributes.SetFloat("entitySize", healed);
            lastKnownSize = healed;
            pendingSize = healed;
            RFMechanicsModSystem.TryUpdatePmlEntityProperties(entity, out string message);
            entity.World.Api.Logger.Warning(
                "[rfmechanics] BandBehavior self-healed entitySize for entity {0}: {1:F3} -> {2:F3} ({3})",
                entity.EntityId, actual, healed, message);
        }

        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
            return RaceTraits.HasTrait(iplayer, cfg.OrcTraitCode);
        }

        public static double Pick(OrcBandTriple t, Band b) => b switch
        {
            Band.Lean => t.Lean,
            Band.Standard => t.Standard,
            Band.Bulky => t.Bulky,
            _ => t.Lean
        };
    }
}
