using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Orc Band state machine (Lean/Standard/Bulky) driven by ThewBehavior's Thew via hysteresis.
    /// Kept as its own behavior rather than folded into ThewBehavior: Thew is hidden resource
    /// math, Bands are a visible state machine owning entitySize/stats; ThewBehavior reads
    /// CurrentBand back for its own gain multiplier (see ThewBehavior.OnGameTick).
    /// </summary>
    public class BandBehavior : EntityBehavior
    {
        public enum Band { Lean = 0, Standard = 1, Bulky = 2 }

        private const string BandAttributeKey = "rf-orc-band";
        private const string ActiveKey = "rf-orc-band-active";
        private const string StatSource = "rf-orc-band";

        private float accum;
        private bool midLerp;
        private float lerpElapsed;
        private float lerpFromSize;
        private float lerpToSize;

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

        public bool MidLerp => midLerp;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableBands) return;

            // Steps the entitySize lerp via OnGameTick + midLerp flag rather than a separate
            // temporary listener, matching this codebase's convention of reusing OnGameTick over
            // RegisterGameTickListener lifecycle management (e.g. ThewBehavior's race-gate).
            if (midLerp) StepLerp(deltaTime);

            accum += deltaTime;
            if (accum < (float)cfg.BandTickInterval) return;
            accum = 0f;

            bool isOrc = IsOrc();
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
                StartLerp(cfg, initial);
                return;
            }

            if (!isOrc)
            {
                if (active)
                {
                    ClearBandStats();
                    entity.Attributes.SetBool(ActiveKey, false);
                    // entitySize is left alone here: PlayerModelLib already resets it to 1.0 on
                    // this same race swap, and reasserting it would fight that reset.
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
                StartLerp(cfg, target);
            }

            if (!midLerp)
            {
                SelfHealEntitySize(cfg);
            }
        }

        /// <summary>Testing hook for /rfthew setband -- forces a band directly, bypassing hysteresis.</summary>
        public void ForceBand(Band band)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            CurrentBand = band;
            entity.Attributes.SetBool(ActiveKey, true);
            ApplyBandStats(band, cfg);
            StartLerp(cfg, band);
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

            entity.GetBehavior<EntityBehaviorHealth>()?.UpdateMaxHealth();
        }

        private void StartLerp(RFMechanicsConfig cfg, Band target)
        {
            lerpFromSize = entity.WatchedAttributes.GetFloat("entitySize", 1f);
            lerpToSize = (float)Pick(cfg.BandSizes, target);
            lerpElapsed = 0f;
            midLerp = true;
        }

        private void StepLerp(float deltaTime)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) { midLerp = false; return; }

            lerpElapsed += deltaTime;
            float duration = Math.Max(0.01f, (float)cfg.BandSizeLerpSeconds);
            float t = Math.Min(1f, lerpElapsed / duration);
            float size = lerpFromSize + (lerpToSize - lerpFromSize) * t;

            entity.WatchedAttributes.SetFloat("entitySize", size);
            RFMechanicsModSystem.TryUpdatePmlEntityProperties(entity, out _);

            if (t >= 1f) midLerp = false;
        }

        /// <summary>Snaps entitySize back to the current band's target (no lerp) if it drifts --
        /// e.g. a live race swap or the character-creation UI resetting/overwriting it.</summary>
        private void SelfHealEntitySize(RFMechanicsConfig cfg)
        {
            float target = (float)Pick(cfg.BandSizes, CurrentBand);
            float actual = entity.WatchedAttributes.GetFloat("entitySize", 1f);
            if (Math.Abs(actual - target) <= 0.001f) return;

            entity.WatchedAttributes.SetFloat("entitySize", target);
            RFMechanicsModSystem.TryUpdatePmlEntityProperties(entity, out string message);
            entity.World.Api.Logger.Warning(
                "[rfmechanics] BandBehavior self-healed entitySize for entity {0}: {1:F3} -> {2:F3} ({3})",
                entity.EntityId, actual, target, message);
        }

        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            return charSys.HasTrait(iplayer, cfg.OrcTraitCode);
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
