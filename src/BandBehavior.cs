using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Orc Band state machine (Phase 3) -- Lean/Standard/Bulky, driven off ThewBehavior's hidden
    /// Thew value via hysteresis thresholds. Kept as a separate behavior from ThewBehavior
    /// (rather than folded in) because the concerns are genuinely different: Thew is hidden
    /// resource math, Bands are a visible state machine that owns entitySize and a wide stat
    /// surface. They share the same 6s tick cadence and race-gate pattern by convention, not by
    /// sharing a class -- ThewBehavior reads this behavior's CurrentBand for its own per-band
    /// gain multiplier and Bulky hold-decay (see ThewBehavior.OnGameTick).
    ///
    /// Same EntityBehavior-with-internal-orc-check pattern as ThewBehavior/RFTreeProximityBehavior:
    /// attached to every player via a JSON patch, gated by IsOrc()
    /// inside the tick, not by listener lifecycle. This means live race-swap needs no special
    /// handling for entry (next tick just starts passing the gate) -- but band-derived Stats.Set
    /// entries DO need explicit cleanup on swap-away, unlike Thew's own hidden float, since a
    /// stale walkspeed/hungerrate/etc. entry left under the "rf-orc-band" source key would
    /// otherwise silently keep applying to a non-orc character (the same staleness class as the
    /// documented Bug 2, see notes/rfmechanics-2026-07-30-session-notes.md).
    /// </summary>
    public class BandBehavior : EntityBehavior
    {
        public enum Band { Lean = 0, Standard = 1, Bulky = 2 }

        private const string BandAttributeKey = "rf-orc-band";
        private const string ActiveKey = "rf-orc-band-active";
        private const string StatSource = "rf-orc-band";
        private const float TickInterval = 6.0f; // matches ThewBehavior's cadence

        private float accum;
        private bool midLerp;
        private float lerpElapsed;
        private float lerpFromSize;
        private float lerpToSize;

        public BandBehavior(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // One-time migration cleanup: bluntDamageFactor/crushingDamageFactor used to be
            // written per-band under StatSource but are no longer applied. Initialize() runs
            // once per behavior construction (every load and fresh spawn, before OnGameTick),
            // so this clears any value stamped by the old code onto existing characters without
            // a per-tick cost or a new persisted flag.
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

            // Fast path: step any in-progress entitySize lerp every tick, not gated by the slow
            // accumulator below. Deviation from the brief's literal "temporary fast listener,
            // registered/unregistered" wording: OnGameTick already fires every tick regardless
            // (matches the established codebase convention of reusing OnGameTick + an internal
            // flag instead of RegisterGameTickListener lifecycle management, e.g. Part D's own
            // documented deviation for the Thew race-gate). "Unregistered at completion" here
            // means the midLerp flag clears and this branch becomes a no-op, not a literal
            // listener removal.
            if (midLerp) StepLerp(deltaTime);

            accum += deltaTime;
            if (accum < TickInterval) return;
            accum = 0f;

            bool isOrc = IsOrc();
            bool active = entity.Attributes.GetBool(ActiveKey);

            if (isOrc && !active)
            {
                // Fresh entry -- either the very first tick ever, or resuming after a
                // race-swap-away. Classify directly from the current (unaffected-by-swap) Thew
                // value rather than assuming Lean, so a returning orc resumes wherever their
                // Thew actually puts them.
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
                    // entitySize itself is deliberately left alone here -- PlayerModelLib
                    // already resets it to 1.0 on this same race swap (T2 finding,
                    // notes/orc-phase0-results.md), and reasserting it would fight that reset.
                }
                return;
            }

            // isOrc && active: normal hysteresis evaluation on the slow tick.
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
                SelfHealEntitySize(cfg); // T2 self-heal + character-creation-UI guard
            }
        }

        /// <summary>Root-only testing hook for /rfthew setband -- forces a band directly,
        /// applying its stats and starting the entitySize lerp, bypassing hysteresis.</summary>
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

        /// <summary>Phase 2 (T5): resolves to the correct band in one evaluation regardless of how
        /// far Thew moved this tick. The old version only checked the current band's immediately
        /// adjacent transition -- safe only while Thew moved a few thousandths per 6s tick (the
        /// slow gain/decay/burn rates), but Frenzy (T4) can now spend Thew fast enough to cross
        /// more than one band boundary within a single 6s sampling window (thew-audit.md Q7,
        /// finding #1). Steps one adjacent-transition-check at a time, same single-step logic as
        /// before, but loops until a tick produces no further transition -- at most 2 steps since
        /// there are only 3 bands, so this is not unbounded.</summary>
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

        /// <summary>Single-step check: does the current band transition to an immediately
        /// adjacent one, given `thew`? Returns `current` unchanged if not. Factored out of
        /// EvaluateBand so the multi-step loop above can repeat it without duplicating the
        /// per-band threshold logic.</summary>
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

        /// <summary>All Stats.Set writes for a band cross, once per cross -- never per-tick.
        /// Every category is written every time (0 where a band gets no bonus) rather than
        /// conditionally added/removed, so a fresh Set() under the same source key cleanly
        /// replaces the previous band's value with no separate remove step needed.</summary>
        private void ApplyBandStats(Band b, RFMechanicsConfig cfg)
        {
            entity.Stats.Set("hungerrate", StatSource, (float)Pick(cfg.HungerRateMult, b) - 1f);
            entity.Stats.Set("walkspeed", StatSource, (float)Pick(cfg.WalkSpeedDelta, b));
            entity.Stats.Set("animalSeekingRange", StatSource, (float)Pick(cfg.AnimalSeekingRangeDelta, b));
            entity.Stats.Set("meleeWeaponsDamage", StatSource, b == Band.Bulky ? (float)cfg.BulkyMeleeDamageBonus : 0f);
            entity.Stats.Set("armorWalkSpeedAffectedness", StatSource, b == Band.Bulky ? (float)cfg.BulkyArmorWalkSpeedAffectednessDelta : 0f);
            entity.Stats.Set("maxhealthExtraPoints", StatSource, (float)Pick(cfg.MaxHpExtraPoints, b));
            entity.Stats.Set("rangedWeaponsAcc", StatSource, (float)Pick(cfg.RangedAccDelta, b));

            // Stats.Set alone only marks WatchedAttributes dirty -- nothing re-triggers
            // EntityBehaviorHealth.UpdateMaxHealth() off a bare stats change (confirmed by
            // reading: its only callers are SetMaxHealthModifiers and two damage-path spots),
            // so it must be called explicitly for the HP change to take effect immediately.
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

        /// <summary>T2 self-heal: every slow tick (while not mid-lerp), if entitySize has
        /// drifted from the current band's target -- e.g. a live race swap resetting it to 1.0
        /// (T2, orc-phase0-results.md) or the character-creation UI overwriting it -- snap it
        /// back directly, no lerp. Only reached while isOrc, so this never fights PML's own
        /// swap-away reset for a non-orc character.</summary>
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
