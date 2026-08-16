using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    public enum AttunementContextKind
    {
        None,
        WildForest,
        Grove
    }

    /// <summary>
    /// GetAttunementContext's result: None | WildForest | Grove(tier). GroveTier is only
    /// meaningful when Kind == Grove -- construct via the static members/factory, never
    /// directly, so an invalid (Kind, GroveTier) pairing can't be assembled by a caller.
    /// </summary>
    public readonly struct AttunementContext
    {
        public AttunementContextKind Kind { get; }
        public int GroveTier { get; }

        private AttunementContext(AttunementContextKind kind, int groveTier)
        {
            Kind = kind;
            GroveTier = groveTier;
        }

        public static AttunementContext None { get; } = new AttunementContext(AttunementContextKind.None, 0);
        public static AttunementContext WildForest { get; } = new AttunementContext(AttunementContextKind.WildForest, 0);
        public static AttunementContext Grove(int tier) => new AttunementContext(AttunementContextKind.Grove, tier);

        public override string ToString() => Kind == AttunementContextKind.Grove ? $"Grove(tier={GroveTier})" : Kind.ToString();
    }

    /// <summary>Per-check breakdown for /rfattune (E1.6) -- see ElfAttunementContext.GetDiagnostics.</summary>
    public readonly struct AttunementDiagnostics
    {
        public bool ForestNaturalGround { get; }
        public bool ForestPresence { get; }
        public int? GroveTier { get; }
        public AttunementContext Context { get; }

        public AttunementDiagnostics(bool forestNaturalGround, bool forestPresence, int? groveTier, AttunementContext context)
        {
            ForestNaturalGround = forestNaturalGround;
            ForestPresence = forestPresence;
            GroveTier = groveTier;
            Context = context;
        }

        /// <summary>Placeholder for "the three checks were not run this tick" -- e.g. a
        /// non-elf, which ElfAttunementBehavior skips straight to AttunementContext.None
        /// without spending a GetDiagnostics call at all (the checks themselves are
        /// race-independent -- a non-elf standing on forest-natural ground would pass check 1
        /// same as an elf -- skipping is purely to avoid wasted work on every non-elf player's
        /// tick, not because the checks would fail for them). Not the same as "all three
        /// checks ran and failed" -- ForestNaturalGround/ForestPresence read false here as a
        /// default, not a real evaluation result.</summary>
        public static AttunementDiagnostics Unevaluated { get; } = new AttunementDiagnostics(false, false, null, AttunementContext.None);
    }

    /// <summary>
    /// E1.2's context predicate. GetDiagnostics is the actual source of truth: it evaluates all
    /// three checks independently (no short-circuiting) so /rfattune can report which check
    /// failed, not just the combined result -- see AttunementDiagnostics. GetAttunementContext
    /// is a thin convenience wrapper over it for callers who only want the combined
    /// None/WildForest/Grove(tier) result.
    ///
    /// Originally GetAttunementContext short-circuited and GetDiagnostics duplicated its
    /// branching non-short-circuited, evaluated separately by the tick and by /rfattune. That
    /// meant checks 2/3 could run twice per tick once /rfattune was called -- free while they're
    /// O(1) stubs, but a real cost once Phase 1b's census makes check 2 expensive.
    /// ElfAttunementBehavior now calls GetDiagnostics once per tick and caches the result
    /// (LastDiagnostics) for /rfattune to read instead of re-evaluating, which is what makes
    /// GetAttunementContext's own short-circuiting moot today -- it's kept as a live, correct,
    /// non-duplicated API for any future caller that only needs the enum, not deleted, since a
    /// Phase 1b revert to a short-circuited tick would want it back as a genuinely cheap path
    /// again (at which point it should stop delegating to GetDiagnostics and regain its own
    /// short-circuiting body).
    /// </summary>
    public static class ElfAttunementContext
    {
        public static AttunementContext GetAttunementContext(Entity entity) => GetDiagnostics(entity).Context;

        /// <summary>
        /// E1.6 (and, since the caching fix above, the tick's own source of truth too):
        /// evaluates all three checks independently, without short-circuiting, so a caller can
        /// see every check's real result rather than just the combined context.
        /// </summary>
        public static AttunementDiagnostics GetDiagnostics(Entity entity)
        {
            bool ground = IsOnForestNaturalGround(entity);
            bool presence = HasNearbyForestPresence_StubPhase1b(entity);
            int? groveTier = ResolveGroveMembership_StubPhase1b(entity);

            AttunementContext context = (!ground || !presence)
                ? AttunementContext.None
                : (groveTier.HasValue ? AttunementContext.Grove(groveTier.Value) : AttunementContext.WildForest);

            return new AttunementDiagnostics(ground, presence, groveTier, context);
        }

        /// <summary>
        /// Check 1 (real, not stubbed): the block underfoot (one below the entity's feet
        /// position) is on the config-backed forest-natural whitelist, resolved once at world
        /// load into a HashSet&lt;int&gt; by ElfAttunementBlockWhitelist -- never a Code.Path
        /// scan per call.
        /// </summary>
        private static bool IsOnForestNaturalGround(Entity entity)
        {
            BlockPos underfoot = entity.Pos.AsBlockPos.Down();
            Block block = entity.World.BlockAccessor.GetBlock(underfoot);
            return block != null && ElfAttunementBlockWhitelist.IsForestNatural(block.Id);
        }

        /// <summary>
        /// Check 2: STUBBED per the Phase 1a brief. Deliberately hardcoded true, not a
        /// temporary block sweep -- Phase 1b replaces this with the real forest census. Also
        /// where a "mature tree" condition would eventually live: design docs reference a
        /// mature-tree gate and a pre-existing tree-age check, but no such check exists
        /// anywhere in this codebase and nothing in the log-grown block data encodes age or
        /// size. Phase 1 ships with NO maturity condition -- any grown log counts once the
        /// census (Phase 1b) lands. Deliberate deferral, not an oversight.
        /// </summary>
        private static bool HasNearbyForestPresence_StubPhase1b(Entity entity) => true;

        /// <summary>
        /// Check 3: STUBBED per the Phase 1a brief. Groves don't exist yet -- always
        /// "not in a grove" until grove membership tracking is built (Phase 1b/2).
        /// </summary>
        private static int? ResolveGroveMembership_StubPhase1b(Entity entity) => null;
    }

    /// <summary>
    /// Resolves RFMechanicsConfig.AttunementForestBlockCodePrefixes into a HashSet&lt;int&gt;
    /// of block IDs exactly once (call Resolve from RFMechanicsModSystem.StartServerSide,
    /// after blocks are registered -- mirrors DwarfOreSongModSystem's own
    /// iterate-api.World.Blocks-once pattern). IsForestNatural is then an O(1) Contains check,
    /// never a per-call Code.Path string scan.
    /// </summary>
    public static class ElfAttunementBlockWhitelist
    {
        private static HashSet<int> resolvedIds;
        private static HashSet<int> resolvedLogGrownIds;

        /// <summary>Single pass over api.World.Blocks builds both the forest-natural-ground set
        /// (AttunementForestBlockCodePrefixes) and the census log-grown set
        /// (AttunementCensusLogCodePrefixes, Phase 1b) -- deliberately one scan, not two, per
        /// E1.8's brief. The two prefix lists are intentionally different (forest-natural ground
        /// includes leaves/moss/soil/placed logs; the census counts only naturally-grown,
        /// still-standing trunks), so they get independent HashSets from the same loop.</summary>
        public static void Resolve(ICoreAPI api, RFMechanicsConfig cfg)
        {
            string[] prefixes = cfg.AttunementForestBlockCodePrefixes;
            string[] logPrefixes = cfg.AttunementCensusLogCodePrefixes;
            var ids = new HashSet<int>();
            var logIds = new HashSet<int>();
            var matched = new bool[prefixes.Length];
            var logMatched = new bool[logPrefixes.Length];

            foreach (Block block in api.World.Blocks)
            {
                if (block?.Code?.Path == null) continue;

                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (block.Code.Path.StartsWith(prefixes[i]))
                    {
                        ids.Add(block.Id);
                        matched[i] = true;
                    }
                }

                for (int i = 0; i < logPrefixes.Length; i++)
                {
                    if (block.Code.Path.StartsWith(logPrefixes[i]))
                    {
                        logIds.Add(block.Id);
                        logMatched[i] = true;
                    }
                }
            }

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!matched[i])
                {
                    api.Logger.Warning("[rfmechanics] ElfAttunement: forest block code prefix '{0}' matched no registered block -- typo, or the block was removed/renamed upstream?", prefixes[i]);
                }
            }

            for (int i = 0; i < logPrefixes.Length; i++)
            {
                if (!logMatched[i])
                {
                    api.Logger.Warning("[rfmechanics] ElfAttunement: census log code prefix '{0}' matched no registered block -- typo, or the block was removed/renamed upstream?", logPrefixes[i]);
                }
            }

            resolvedIds = ids;
            resolvedLogGrownIds = logIds;
            api.Logger.Notification("[rfmechanics] ElfAttunement forest block whitelist resolved: {0} block IDs across {1} configured prefixes ({2} log-grown IDs across {3} census prefixes).",
                ids.Count, prefixes.Length, logIds.Count, logPrefixes.Length);
        }

        public static bool IsForestNatural(int blockId) => resolvedIds != null && resolvedIds.Contains(blockId);

        public static bool IsLogGrown(int blockId) => resolvedLogGrownIds != null && resolvedLogGrownIds.Contains(blockId);
    }
}
