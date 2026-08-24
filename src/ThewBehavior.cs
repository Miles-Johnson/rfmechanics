using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative, hidden Thew float (0..1) for orc players, stored in
    /// entity.Attributes (non-synced) so it never reaches the client HUD. Climbs while well-fed
    /// and protein-gated, falls while starving or (unconditionally) while Bulky, otherwise holds.
    /// Orc-race gate lives inside OnGameTick (IsOrc()), not listener lifecycle, so a live race
    /// swap needs no special-casing -- swapping out just stops updating Thew; the stored value
    /// sits dormant since nothing else reads it.
    /// </summary>
    public class ThewBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-orc-thew";

        /// <summary>Set once, ever, the first tick an entity is detected as orc -- distinguishes
        /// "never initialized" (apply ThewCreationFloor) from "genuinely decayed to zero" (leave alone).</summary>
        private const string InitializedKey = "rf-orc-thew-initialized";

        /// <summary>Written by the eat-hook patch on every qualifying eat, read here to gate the hourly tick gain -- public so both sides share one attribute key.</summary>
        public const string LastFoodCategoryKey = "rf-orc-last-food-category";

        /// <summary>Client-visible (WatchedAttributes) puff-cue state: 0 idle, 1 gaining, 2 light
        /// debt, 3 heavy debt. Public so OrcPuffModSystem reads the same key without duplicating
        /// the string. Written only on change -- see OnGameTick's puff-state step.</summary>
        public const string StateAttributeKey = "rf-orc-state";

        private float accum;

        /// <summary>NaN means "no prior sample yet" -- distinct from a genuine 0-hour delta, so the first tick after (re)load applies no gain/decay instead of a spurious jump.</summary>
        private double lastElapsedHours = double.NaN;

        public ThewBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfthew";

        public float Thew
        {
            get => entity.Attributes.GetFloat(AttributeKey, 0f);
            set => entity.Attributes.SetFloat(AttributeKey, GameMath.Clamp(value, 0f, 1f));
        }

        private const string BurnDebtKey = "rf-orc-burn-debt";
        private const string FrenzyDebtKey = "rf-orc-frenzy-debt";

        /// <summary>Thew debt incurred by Burn healing, repaid by eating and by ThewBehavior's own
        /// tick-drain -- never subtracted from Thew directly by BurnBehavior. Persists through
        /// death (entity.Attributes survives OnEntityDeath).</summary>
        public float BurnDebt
        {
            get => entity.Attributes.GetFloat(BurnDebtKey, 0f);
            set => entity.Attributes.SetFloat(BurnDebtKey, Math.Max(0f, value));
        }

        /// <summary>See BurnDebt -- same contract, incurred by Frenzy instead.</summary>
        public float FrenzyDebt
        {
            get => entity.Attributes.GetFloat(FrenzyDebtKey, 0f);
            set => entity.Attributes.SetFloat(FrenzyDebtKey, Math.Max(0f, value));
        }

        /// <summary>Reduces outstanding debt by `amount`, burn debt first then frenzy -- shared by
        /// the Thew-funded tick drain and eating's saturation-funded repayment, so both pay in the
        /// same order.</summary>
        public void PayDebt(float amount)
        {
            if (amount <= 0f) return;

            float burn = BurnDebt;
            float payBurn = Math.Min(burn, amount);
            BurnDebt = burn - payBurn;

            float remaining = amount - payBurn;
            if (remaining > 0f) FrenzyDebt -= remaining;
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableThew) return;

            accum += deltaTime;
            if (accum < (float)cfg.ThewTickInterval) return;
            accum = 0f;

            // In-game hours, not real hours: samples the calendar's own elapsed-hours clock so
            // ThewGainPerHour/ThewDriftPerHour/etc. run at the same rate regardless of server day
            // length, matching the hunger drain they're balanced against (see facts doc Section A).
            double nowElapsedHours = entity.World.Calendar.ElapsedHours;
            float hourFraction = double.IsNaN(lastElapsedHours) ? 0f : (float)(nowElapsedHours - lastElapsedHours);
            lastElapsedHours = nowElapsedHours;

            bool isOrc = IsOrc();
            if (isOrc && !entity.Attributes.GetBool(InitializedKey))
            {
                entity.Attributes.SetBool(InitializedKey, true);
                Thew = (float)cfg.ThewCreationFloor;
            }
            ApplyStomachMultiplier(cfg, isOrc);

            if (!isOrc)
            {
                // Clears a leftover puff-cue state after a race-swap away -- OrcPuffModSystem
                // reads this key for any player, with no trait check of its own.
                if (entity.WatchedAttributes.GetInt(StateAttributeKey, 0) != 0)
                {
                    entity.WatchedAttributes.SetInt(StateAttributeKey, 0);
                }
                return;
            }

            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null || hunger.MaxSaturation <= 0f) return;

            float satFrac = hunger.Saturation / hunger.MaxSaturation;
            var band = entity.GetBehavior<BandBehavior>()?.CurrentBand ?? BandBehavior.Band.Lean;

            if (hunger.Saturation <= 0f)
            {
                Thew -= (float)cfg.ThewDecayStarvingPerHour * hourFraction;
            }
            else if (satFrac > (float)cfg.ThewGainSatietyGate)
            {
                bool proteinGated = IsProteinGated(hunger, cfg);
                bool foodTypeBlocksGain = cfg.EnableThewFoodTypeGate && LastFoodBlocksGain();
                // Gate failing in the gain zone means no change this tick, not a fall-through to
                // drift/decay -- those rates are only defined for their own satFrac zones.
                if (proteinGated && !foodTypeBlocksGain)
                {
                    float bandMult = (float)BandBehavior.Pick(cfg.ThewGainBandMult, band);
                    Thew += (float)cfg.ThewGainPerHour * bandMult * hourFraction * GetSeasonalMultiplier(cfg);
                }
            }
            else if (satFrac < (float)cfg.ThewDecayLowSatietyThreshold)
            {
                Thew -= (float)cfg.ThewDecayLowSatietyPerHour * hourFraction;
            }
            else
            {
                Thew -= (float)cfg.ThewDriftPerHour * hourFraction;
            }

            // Debt drain is independent of the satiety zone above -- runs every tick regardless
            // of gain/drift/decay. Capped by both the configured rate and by Thew actually on
            // hand, so it stalls (not overdraws) once Thew reaches 0, per the locked design.
            float totalDebt = BurnDebt + FrenzyDebt;
            if (totalDebt > 0f && Thew > 0f)
            {
                float drain = Math.Min((float)cfg.DebtDrainPerHour * hourFraction, Math.Min(totalDebt, Thew));
                if (drain > 0f)
                {
                    Thew -= drain;
                    PayDebt(drain);
                }
            }

            if (cfg.EnablePuff)
            {
                float debtAfterDrain = BurnDebt + FrenzyDebt;
                int state;
                if (debtAfterDrain > (float)cfg.HeavyDebtThreshold) state = 3;
                else if (debtAfterDrain > 0f) state = 2;
                else if (satFrac > (float)cfg.ThewGainSatietyGate) state = 1;
                else state = 0;

                if (entity.WatchedAttributes.GetInt(StateAttributeKey, 0) != state)
                {
                    entity.WatchedAttributes.SetInt(StateAttributeKey, state);
                }
            }
        }

        /// <summary>Vanilla MaxSaturation before any multiplier (player.json); both stacking-mode
        /// candidates are computed from this baseline rather than reverse-engineered out of a
        /// live MaxSaturation value that may already reflect racialability's own contribution.</summary>
        private const float VanillaBaseMaxSaturation = 1500f;

        /// <summary>
        /// Recomputes and re-asserts MaxSaturation every tick rather than a one-time multiply/
        /// divide, because "max" stacking mode needs to compare two fresh candidates each tick --
        /// multiplying/dividing the current value would still compound with PlayerModelLib's own
        /// reactive rescale-on-change postfix. Reads entity.Stats.GetBlended("maxSaturationFactor")
        /// directly (the actual source stat racialability writes) rather than an already-modified
        /// MaxSaturation. Runs regardless of isOrc so a non-orc's racialability contribution still applies.
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

        /// <summary>Entity.Die does not wipe entity.Attributes, so this fires once per death and the
        /// reset value persists through respawn. Pulls down only -- a Thew already at or below the
        /// cap is left alone, never raised.</summary>
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableThew) return;
            if (!IsOrc()) return;

            if (Thew > (float)cfg.ThewDeathResetCap) Thew = (float)cfg.ThewDeathResetCap;
        }

        // charClass null-check is load-bearing: HasTrait returns true for a null class by default, so classless entities must be explicitly excluded.
        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            return RaceTraits.HasTrait(iplayer, cfg.OrcTraitCode);
        }

        /// <summary>Public static so the eat-hook patch uses the exact same classification as the tick gain.</summary>
        public static bool IsNonProteinPlantCategory(EnumFoodCategory foodCat) =>
            foodCat == EnumFoodCategory.Fruit || foodCat == EnumFoodCategory.Vegetable || foodCat == EnumFoodCategory.Grain;

        /// <summary>Checks Protein OR Dairy, not just Protein -- vanilla's cheese.json tags
        /// "Dairy", never "Protein", so a Protein-only gate silently excluded cheese-only diets
        /// (egg.json/insect.json already tag "Protein" and don't need this). Reuses
        /// ProteinGateLevel as the threshold for both rather than adding a second config number;
        /// untested whether Dairy's per-bite rate matches meat's closely enough for that to feel right.</summary>
        public static bool IsProteinGated(EntityBehaviorHunger hunger, RFMechanicsConfig cfg)
        {
            float threshold = (float)cfg.ProteinGateLevel;
            return hunger.ProteinLevel > threshold || hunger.DairyLevel > threshold;
        }

        /// <summary>Defaults to NoNutrition, which is never a blocking category, so a fresh spawn isn't gated by a value it never wrote.</summary>
        private bool LastFoodBlocksGain()
        {
            int raw = entity.Attributes.GetInt(LastFoodCategoryKey, (int)EnumFoodCategory.NoNutrition);
            return IsNonProteinPlantCategory((EnumFoodCategory)raw);
        }

        /// <summary>Public static so /rfthew dump reports the exact zone OnGameTick is using.
        /// Starving is keyed off Saturation itself, not satFrac, to match vanilla's own
        /// `Saturation &lt;= 0f` starvation-damage trigger exactly.</summary>
        public static string SatietyZoneName(EntityBehaviorHunger hunger, float satFrac, RFMechanicsConfig cfg)
        {
            if (hunger.Saturation <= 0f) return "Starving";
            if (satFrac > (float)cfg.ThewGainSatietyGate) return "Gain";
            if (satFrac < (float)cfg.ThewDecayLowSatietyThreshold) return "LowSatietyDecay";
            return "Drift";
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
