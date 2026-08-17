using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>threshold: which of RFMechanicsConfig.AttunementThresholds was crossed. active:
    /// true if this crossing was upward (value now at/above threshold), false if downward. A
    /// subscriber holds a bool per threshold rather than ever polling Attunement -- see
    /// ElfAttunementBehavior.EvaluateThresholds.</summary>
    public delegate void AttunementThresholdHandler(Entity entity, int threshold, bool active, float value);

    /// <summary>
    /// Phase 1a of the Elf attunement system. Owns a single 0-100 "attunement" float in
    /// WatchedAttributes -- this behavior is the ONLY writer of that key. Reasserts ownership
    /// every slow tick: the true value lives in liveAttunement (in-memory) and is stepped every
    /// tick toward the ceiling/floor of LastDiagnostics.Context (ElfAttunementContext.
    /// GetDiagnostics, cached once per tick -- see LastDiagnostics' own doc comment for why),
    /// flushed to WatchedAttributes (clamped via the Attunement property setter) only once the
    /// drift since the last flush exceeds AttunementWriteThreshold -- same write-avoidance shape
    /// as RFTreeProximityBehavior.TrySet, adapted for an accumulator (see AttunementWriteThreshold's
    /// doc comment for why a from-scratch recompute like TrySet's wouldn't work here). Unflushed
    /// drift is also force-flushed on despawn (see OnEntityDespawn) so a disconnect never loses
    /// progress, only mid-session ticks can lag behind by up to the threshold.
    ///
    /// Attached to every player entity via a JSON patch (seraph-elfattunement.json), same
    /// convention as RFTreeProximityBehavior/ThewBehavior -- the elf-race gate lives inside
    /// RefreshElfCache(), not in listener registration/lifecycle. Non-elves (and elves who lose
    /// the trait mid-session) are stepped toward AttunementContext.None (decay to 0) exactly
    /// like a real None context, so a race swap away from Elf self-heals the value back to 0
    /// over time instead of leaving it stuck.
    /// </summary>
    public class ElfAttunementBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-elf-attunement";
        private const string HungerDrainStatSource = "rf-elf-attunement";

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

        /// <summary>E3.4 gate: true once liveAttunement is at/above
        /// RFMechanicsConfig.LeafStandingAttunementThreshold, false below it. Set inline inside
        /// EvaluateThresholds -- the same Schmitt-trigger crossing detection that raises
        /// ThresholdCrossed for this threshold, so this is crossing-driven exactly like the
        /// event, never a per-tick re-check of liveAttunement against the threshold.
        /// BranchyLeavesPassthroughPatch reads this field directly on its physics-substep hot
        /// path, alongside IsElfCached -- never Attunement, never the trait system.</summary>
        public bool LeafStandingActive { get; private set; }

        /// <summary>E3.5 gate: true once liveAttunement is at/above
        /// RFMechanicsConfig.TreeProximityAttunementThreshold, false below it. Set inline inside
        /// EvaluateThresholds, same shape as LeafStandingActive. RFTreeProximityBehavior reads
        /// this before running its own block sweep, not just before writing the stat -- below
        /// threshold there's nothing to compute, so the sweep itself is skipped.</summary>
        public bool TreeProximityActive { get; private set; }

        /// <summary>E3.6 gate: true once liveAttunement is at/above
        /// RFMechanicsConfig.HungerDrainAttunementThreshold, false below it. Set inline inside
        /// EvaluateThresholds. Unlike LeafStandingActive/TreeProximityActive (read by another
        /// behavior), this behavior applies/clears the hungerrate stat itself on the crossing --
        /// there's no per-tick recompute needed, just a flat multiplier while active.</summary>
        public bool HungerDrainActive { get; private set; }

        /// <summary>The full per-check breakdown from this entity's last tick, computed once
        /// per tick via ElfAttunementContext.GetDiagnostics and reused for both the tick's own
        /// stepping (context = LastDiagnostics.Context) and /rfattune's dump -- avoids
        /// evaluating the three checks twice (once for the tick, once for the command). Phase 1b
        /// (E1.11) confirmed the once-per-tick shape holds even with check 2 now a real census:
        /// the tick still calls GetDiagnostics exactly once, now threading ForestCache through
        /// it so a stationary elf's check 2 costs a coordinate+generation compare, not a real
        /// census consult -- see ForestCache and ElfAttunementContext.ResolveForestPresence.
        /// /rfattune's fallback path (used only when not currently an elf, or the system is
        /// disabled) pays a fresh call but warms/reads this same cache rather than a second,
        /// disconnected one.</summary>
        public AttunementDiagnostics LastDiagnostics { get; private set; } = AttunementDiagnostics.Unevaluated;

        /// <summary>E1.10: this entity's cached column coordinate/generation/forest-presence
        /// result, threaded through ElfAttunementContext.GetDiagnostics every tick. `internal
        /// set` (not private) so RFMechanicsModSystem's /rfattune fallback branch -- same
        /// assembly -- can write the refreshed cache back after its own off-tick
        /// GetDiagnostics call, keeping both call sites on one cache instead of a second,
        /// disconnected one.</summary>
        public AttunementPositionalCache ForestCache { get; internal set; } = AttunementPositionalCache.Empty;

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

            if (entity.World.Side != EnumAppSide.Server)
            {
                // BehaviorControlledPhysics.OnGameTick's own comment: "Player physics is called
                // only client side" -- BranchyLeavesPassthroughPatch's collision postfix runs on
                // this side's CachingCollisionTester, so IsElfCached/LeafStandingActive must stay
                // fresh here too or passthrough never engages regardless of attunement, no matter
                // what the server-side value says. Cheap and side-effect-free: no gain/decay
                // stepping, no forest census, no WatchedAttributes write -- Attunement is already
                // synced from the server, so the threshold compares against it directly.
                RefreshElfCache();
                LeafStandingActive = IsElfCached && Attunement >= (float)cfg.LeafStandingAttunementThreshold;
                return;
            }

            if (!liveInitialized)
            {
                liveAttunement = Attunement;
                lastFlushedAttunement = liveAttunement;
                liveInitialized = true;
            }

            RefreshElfCache();

            if (IsElfCached)
            {
                LastDiagnostics = ElfAttunementContext.GetDiagnostics(entity, ForestCache, out var updatedCache);
                ForestCache = updatedCache;
            }
            else
            {
                LastDiagnostics = AttunementDiagnostics.Unevaluated;
            }

            StepAttunement(LastDiagnostics.Context, cfg);
        }

        /// <summary>Testing-only override for /rfattuneset. Writes liveAttunement directly, not
        /// just the flushed Attunement property -- StepAttunement steps liveAttunement toward
        /// its context target every tick and would otherwise silently overwrite a value set
        /// only in WatchedAttributes on the very next tick. Runs EvaluateThresholds immediately
        /// so LeafStandingActive and the other threshold bools reflect the forced value without
        /// waiting for the next slow tick.</summary>
        public float DebugSetAttunement(float value)
        {
            liveAttunement = GameMath.Clamp(value, 0f, 100f);
            liveInitialized = true;
            Attunement = liveAttunement;
            lastFlushedAttunement = Attunement;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg != null) EvaluateThresholds(cfg);

            return liveAttunement;
        }

        /// <summary>Flushes any unwritten liveAttunement drift to WatchedAttributes immediately
        /// on despawn (covers disconnect) -- without this, up to AttunementWriteThreshold of
        /// progress sits only in behavior memory and is lost the moment the entity unloads.
        /// That's a bigger problem at the low end than the headline number suggests: the 0-10
        /// band gates real thresholds (living harvest, climbing per the design doc), so a
        /// player logging in and out repeatedly could otherwise get shaved back below a
        /// threshold they'd just crossed, tick by tick, forever.</summary>
        public override void OnEntityDespawn(EntityDespawnData despawnData)
        {
            if (entity.World.Side == EnumAppSide.Server && liveInitialized
                && Math.Abs(liveAttunement - lastFlushedAttunement) > 0f)
            {
                Attunement = liveAttunement;
                lastFlushedAttunement = Attunement;
            }

            base.OnEntityDespawn(despawnData);
        }

        /// <summary>
        /// Single step-toward-target rule that covers both AttunementContext rules at once:
        /// gain toward the ceiling while below it, decay toward that same ceiling while above it
        /// (Forest's "any value ABOVE AttunementCeiling decays toward AttunementCeiling"), and
        /// decay toward 0 in None (target 0 is always &lt;= current since Attunement is clamped
        /// to [0,100], so this always takes the decay branch for None).
        /// </summary>
        private void StepAttunement(AttunementContext context, RFMechanicsConfig cfg)
        {
            float target;
            float gainRate;
            switch (context.Kind)
            {
                case AttunementContextKind.Forest:
                    target = (float)cfg.AttunementCeiling;
                    gainRate = (float)cfg.AttunementGainRate;
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
                if (t == cfg.LeafStandingAttunementThreshold) LeafStandingActive = nowActive;
                if (t == cfg.TreeProximityAttunementThreshold) TreeProximityActive = nowActive;
                if (t == cfg.HungerDrainAttunementThreshold)
                {
                    HungerDrainActive = nowActive;
                    ApplyOrClearHungerDrain(nowActive, cfg);
                }
                ThresholdCrossed?.Invoke(entity, t, nowActive, liveAttunement);
            }
        }

        /// <summary>E3.6: applies the reduced-hunger-drain multiplier as a Stats.Set delta on
        /// crossing up, removes the stat entry entirely on crossing down -- mirrors
        /// BandBehavior.ClearBandStats' Remove (not a zero-delta Set), since there's no other
        /// consumer relying on the key's continued presence.</summary>
        private void ApplyOrClearHungerDrain(bool active, RFMechanicsConfig cfg)
        {
            if (!cfg.EnableElfHungerDrainReduction || !active)
            {
                entity.Stats.Remove("hungerrate", HungerDrainStatSource);
                return;
            }

            entity.Stats.Set("hungerrate", HungerDrainStatSource, (float)cfg.ElfHungerRateMult - 1f);
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
