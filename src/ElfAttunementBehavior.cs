using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Phase 1a of the Elf attunement system. Owns a single 0-100 "attunement" float in
    /// WatchedAttributes -- this behavior is the ONLY writer of that key. Reasserts ownership
    /// every slow tick: the true value lives in liveAttunement (in-memory) and is stepped every
    /// tick toward GetAttunementContext's ceiling/floor, flushed to WatchedAttributes (clamped
    /// via the Attunement property setter) only once the drift since the last flush exceeds
    /// AttunementWriteThreshold -- same write-avoidance shape as
    /// RFTreeProximityBehavior.TrySet, adapted for an accumulator (see AttunementWriteThreshold's
    /// doc comment for why a from-scratch recompute like TrySet's wouldn't work here).
    ///
    /// Attached to every player entity via a JSON patch (seraph-elfattunement.json), same
    /// convention as RFTreeProximityBehavior/ThewBehavior -- the elf-race gate lives inside
    /// RefreshElfCache(), not in listener registration/lifecycle. Non-elves (and elves who lose
    /// the trait mid-session) are stepped toward AttunementContext.None (decay to 0) exactly
    /// like a real None context, so a race swap away from Elf self-heals the value back to 0
    /// over time instead of leaving it stuck.
    /// </summary>
    /// <summary>threshold: which of RFMechanicsConfig.AttunementThresholds was crossed. active:
    /// true if this crossing was upward (value now at/above threshold), false if downward. A
    /// subscriber holds a bool per threshold rather than ever polling Attunement -- see
    /// ElfAttunementBehavior.EvaluateThresholds.</summary>
    public delegate void AttunementThresholdHandler(Entity entity, int threshold, bool active, float value);

    public class ElfAttunementBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-elf-attunement";

        /// <summary>Fires once per threshold per crossing direction -- never a storm, see
        /// EvaluateThresholds' Schmitt-trigger hysteresis. Static: effects (Phase 2+) subscribe
        /// once at mod start rather than per-entity.</summary>
        public static event AttunementThresholdHandler ThresholdCrossed;

        private float accum;
        private bool staggerApplied;

        /// <summary>Per-threshold active/inactive state (parallel to
        /// RFMechanicsConfig.AttunementThresholds), used only to detect crossings -- see
        /// EvaluateThresholds.</summary>
        private bool[] activeThresholds;

        /// <summary>True live value, stepped every tick. Lazily initialized from the persisted
        /// WatchedAttributes value on the first qualifying tick after (re)load, so a relogged
        /// entity resumes from its last-flushed value, not from 0.</summary>
        private float liveAttunement;
        private bool liveInitialized;
        private float lastFlushedAttunement;

        /// <summary>True live value (see the class doc comment) -- /rfattune (E1.6) reads this
        /// rather than the flushed Attunement property so the dump reflects reality even
        /// between flushes.</summary>
        public float LiveAttunement => liveAttunement;

        /// <summary>Snapshot of which thresholds (parallel to
        /// RFMechanicsConfig.AttunementThresholds) are currently active. Empty until the first
        /// qualifying tick has run.</summary>
        public bool[] ActiveThresholdsSnapshot => activeThresholds ?? System.Array.Empty<bool>();

        /// <summary>Cached elf-race result, refreshed every slow tick (and therefore within one
        /// tick interval of any characterClass change -- there is no separate change listener,
        /// polling on the slow tick is the refresh mechanism). Nothing outside this behavior may
        /// walk CharacterSystem.HasTrait on a hot path -- Phase 3 reads this field directly
        /// instead of re-deriving it.</summary>
        public bool IsElfCached { get; private set; }

        public ElfAttunementBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfattunement";

        /// <summary>The owned float, clamped to [0,100] on every write. Server-authoritative,
        /// synced via WatchedAttributes (not entity.Attributes) so client-side effects
        /// (Phase 2+) can read it directly without a round trip.</summary>
        public float Attunement
        {
            get => entity.WatchedAttributes.GetFloat(AttributeKey, 0f);
            set => entity.WatchedAttributes.SetFloat(AttributeKey, GameMath.Clamp(value, 0f, 100f));
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableElfAttunement) return;

            if (!staggerApplied)
            {
                // Stagger by entity hash so every elf doesn't evaluate on the same tick --
                // trivial cost now (this task's tick body is cheap), but load-bearing once
                // Phase 1b's forest census makes this scan per-chunk-costly and a synchronized
                // spike across every elf on the server would actually be felt.
                staggerApplied = true;
                accum = ComputeStaggerOffset(entity.EntityId, (float)cfg.AttunementTickInterval);
            }

            accum += deltaTime;
            if (accum < (float)cfg.AttunementTickInterval) return;
            accum = 0f;

            if (!liveInitialized)
            {
                liveAttunement = Attunement;
                lastFlushedAttunement = liveAttunement;
                liveInitialized = true;
            }

            RefreshElfCache();

            AttunementContext context = IsElfCached
                ? ElfAttunementContext.GetAttunementContext(entity)
                : AttunementContext.None;

            StepAttunement(context, cfg);
        }

        /// <summary>
        /// Single step-toward-target rule that covers all three AttunementContext rules at
        /// once: gain toward a ceiling while below it, decay toward that same ceiling while
        /// above it (WildForest's "any value ABOVE WildCeiling decays toward WildCeiling"),
        /// and decay toward 0 in None (target 0 is always <= current since Attunement is
        /// clamped to [0,100], so this always takes the decay branch for None). Grove mirrors
        /// WildForest's shape in anticipation of Phase 1b/2, even though it can't fire yet
        /// (E1.2's grove check is stubbed).
        /// </summary>
        private void StepAttunement(AttunementContext context, RFMechanicsConfig cfg)
        {
            float target;
            float gainRate;
            switch (context.Kind)
            {
                case AttunementContextKind.Grove:
                    target = ResolveGroveCeiling(context.GroveTier, cfg);
                    gainRate = (float)cfg.AttunementGainRateGrove;
                    break;
                case AttunementContextKind.WildForest:
                    target = (float)cfg.AttunementWildCeiling;
                    gainRate = (float)cfg.AttunementGainRateWild;
                    break;
                default:
                    target = 0f;
                    gainRate = 0f; // unreachable (target 0 is never above liveAttunement), kept for symmetry
                    break;
            }

            float rate = liveAttunement < target ? gainRate : (float)cfg.AttunementDecayRate;
            liveAttunement = StepToward(liveAttunement, target, rate, (float)cfg.AttunementTickInterval);

            EvaluateThresholds(cfg);

            if (Math.Abs(liveAttunement - lastFlushedAttunement) > (float)cfg.AttunementWriteThreshold)
            {
                Attunement = liveAttunement;
                lastFlushedAttunement = Attunement; // read back post-clamp, in case liveAttunement ever drifted outside [0,100]
            }
        }

        /// <summary>
        /// Schmitt trigger per threshold: once ACTIVE, only deactivates below (threshold -
        /// half); once inactive, only activates at/above (threshold + half). Evaluated against
        /// liveAttunement (the true value) every tick, independent of whether this tick also
        /// flushed to WatchedAttributes -- thresholds must not miss a crossing just because the
        /// write-gate held it back.
        ///
        /// Why this can't storm: AttunementThresholdHysteresis is sized above the largest
        /// possible single-tick delta (see its own doc comment), so a value cannot cross both
        /// trip points in one tick from rest -- reactivating after a deactivation needs at
        /// least one more full tick of sustained movement in the same direction, not noise.
        /// StepToward's linear clamp-at-target behavior (see its own doc comment) additionally
        /// means a value that reaches a ceiling holds there exactly, with no floating-point
        /// wobble to trigger chatter in the first place -- the hysteresis band mainly guards
        /// the case of a genuinely flickering context (e.g. pacing in and out of forest cover)
        /// landing a value close to a threshold.
        /// </summary>
        private void EvaluateThresholds(RFMechanicsConfig cfg)
        {
            int[] thresholds = cfg.AttunementThresholds;
            if (activeThresholds == null || activeThresholds.Length != thresholds.Length)
                activeThresholds = new bool[thresholds.Length];

            float half = (float)cfg.AttunementThresholdHysteresis / 2f;
            for (int i = 0; i < thresholds.Length; i++)
            {
                int t = thresholds[i];
                bool wasActive = activeThresholds[i];
                bool nowActive = wasActive ? liveAttunement > t - half : liveAttunement >= t + half;

                if (nowActive == wasActive) continue;

                activeThresholds[i] = nowActive;
                ThresholdCrossed?.Invoke(entity, t, nowActive, liveAttunement);
            }
        }

        /// <summary>Moves current toward target at ratePerSecond, clamped so it can never
        /// overshoot -- a plain linear ramp, not exponential easing, so a value that reaches
        /// its ceiling holds there exactly (no floating-point wobble around the target that
        /// could otherwise cause threshold-crossing chatter, see E1.4's hysteresis).</summary>
        private static float StepToward(float current, float target, float ratePerSecond, float dtSeconds)
        {
            float delta = ratePerSecond * dtSeconds;
            if (current < target) return Math.Min(target, current + delta);
            if (current > target) return Math.Max(target, current - delta);
            return current;
        }

        /// <summary>Groves don't exist yet (Phase 1b/2) -- E1.2's grove check never returns a
        /// tier, so this is unreachable in Phase 1a. Falls back to WildCeiling so the branch is
        /// still well-defined rather than throwing if it's ever hit early.</summary>
        private static float ResolveGroveCeiling(int tier, RFMechanicsConfig cfg) => (float)cfg.AttunementWildCeiling;

        /// <summary>Deterministic per-entity fraction of one tick interval, used once to seed
        /// accum so every elf's slow tick lands on a different real-time offset instead of all
        /// firing on the same frame. EntityId increments monotonically and is already
        /// well-distributed modulo a modulus not aligned to any power of two -- a full hash
        /// function would add nothing here. Static/pure so it's trivially reasoned about (and
        /// testable) independent of entity/tick state.</summary>
        private static float ComputeStaggerOffset(long entityId, float interval)
        {
            if (interval <= 0f) return 0f;
            long m = ((entityId % 997) + 997) % 997; // defensive against a hypothetical negative EntityId
            return (m / 997f) * interval;
        }

        /// <summary>
        /// Guard chain matching every other rfmechanics elf gate (see
        /// RFTreeProximityBehavior.IsElf / ThewBehavior.IsOrc): EntityPlayer check, then
        /// characterClass null check (load-bearing -- HasTrait returns true for a null class
        /// by default, so classless entities must be explicitly excluded), then the trait
        /// check itself. Copied verbatim, only the trait code differs.
        /// </summary>
        private void RefreshElfCache()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) { IsElfCached = false; return; }
            if (entity is not EntityPlayer player) { IsElfCached = false; return; }

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) { IsElfCached = false; return; }

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) { IsElfCached = false; return; }

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) { IsElfCached = false; return; }

            IsElfCached = charSys.HasTrait(iplayer, cfg.ElfTraitCode);
        }
    }
}
