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

        /// <summary>Written by ThewEatPulsePatch on every qualifying eat, read here to gate the hourly tick gain -- public so both sides share one attribute key.</summary>
        public const string LastFoodCategoryKey = "rf-orc-last-food-category";

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
            if (accum < (float)cfg.ThewTickInterval) return;
            accum = 0f;

            bool isOrc = IsOrc();
            ApplyStomachMultiplier(cfg, isOrc);

            if (!isOrc) return;

            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null || hunger.MaxSaturation <= 0f) return;

            float satFrac = hunger.Saturation / hunger.MaxSaturation;
            float rampMult = RampMultiplier(satFrac, cfg);
            bool proteinGated = IsProteinGated(hunger, cfg);
            bool foodTypeBlocksGain = cfg.EnableThewFoodTypeGate && LastFoodBlocksGain();

            float hourFraction = (float)cfg.ThewTickInterval / 3600f;
            var band = entity.GetBehavior<BandBehavior>()?.CurrentBand ?? BandBehavior.Band.Lean;

            if (proteinGated && rampMult > 0f && !foodTypeBlocksGain)
            {
                float bandMult = (float)BandBehavior.Pick(cfg.ThewGainBandMult, band);
                Thew += (float)cfg.ThewGainPerHour * bandMult * rampMult * hourFraction * GetSeasonalMultiplier(cfg);
            }
            else
            {
                // No neutral parking zone: gain not firing always means decay firing. satFrac ==
                // ThewRampFloor exactly lands here (not the gain branch), taking the sated-tier rate.
                float decayPerHour = satFrac < (float)cfg.ThewRampFloor
                    ? DecayTierPerHour(hunger, satFrac, cfg)
                    : (float)cfg.ThewDecaySatedNonProteinPerHour;
                Thew -= decayPerHour * hourFraction;
            }

            // Stacks with the gain/decay above -- the lever that stops Bulky being sustainable purely by not starving.
            if (band == BandBehavior.Band.Bulky)
            {
                Thew -= (float)cfg.BulkyHoldDecayPerHour * hourFraction;
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

        /// <summary>Entity.Die does not wipe entity.Attributes, so this fires once per death and the reduced Thew value persists through respawn.</summary>
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableThew || !cfg.EnableThewDeathPenalty) return;
            if (!IsOrc()) return;

            Thew -= (float)cfg.ThewDeathPenalty;
        }

        // charClass null-check is load-bearing: HasTrait returns true for a null class by default, so classless entities must be explicitly excluded.
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

        /// <summary>Public static so ThewEatPulsePatch uses the exact same classification as the tick gain.</summary>
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

        /// <summary>Public static so ThewEatPulsePatch's bite gate uses the exact same curve as the tick gain.</summary>
        public static float RampMultiplier(float satFrac, RFMechanicsConfig cfg)
        {
            float floor = (float)cfg.ThewRampFloor;
            float ceiling = (float)cfg.ThewRampCeiling;
            if (ceiling <= floor) return satFrac >= ceiling ? 1f : 0f;
            return GameMath.Clamp((satFrac - floor) / (ceiling - floor), 0f, 1f);
        }

        /// <summary>Starving is keyed off Saturation itself, not satFrac, to match vanilla's own `Saturation &lt;= 0f` starvation-damage trigger exactly.</summary>
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
