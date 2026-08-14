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
    public class ElfAttunementBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-elf-attunement";

        private float accum;

        /// <summary>True live value, stepped every tick. Lazily initialized from the persisted
        /// WatchedAttributes value on the first qualifying tick after (re)load, so a relogged
        /// entity resumes from its last-flushed value, not from 0.</summary>
        private float liveAttunement;
        private bool liveInitialized;
        private float lastFlushedAttunement;

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

            if (Math.Abs(liveAttunement - lastFlushedAttunement) > (float)cfg.AttunementWriteThreshold)
            {
                Attunement = liveAttunement;
                lastFlushedAttunement = Attunement; // read back post-clamp, in case liveAttunement ever drifted outside [0,100]
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
