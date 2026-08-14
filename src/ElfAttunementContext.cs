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
    }

    /// <summary>
    /// E1.2's context predicate. Three checks, ascending cost, short-circuiting -- most callers
    /// (the tick) only ever need GetAttunementContext; GetDiagnostics (added E1.6) evaluates all
    /// three independently, without short-circuiting, purely for the debug command.
    /// </summary>
    public static class ElfAttunementContext
    {
        public static AttunementContext GetAttunementContext(Entity entity)
        {
            if (!IsOnForestNaturalGround(entity)) return AttunementContext.None;
            if (!HasNearbyForestPresence_StubPhase1b(entity)) return AttunementContext.None;

            int? groveTier = ResolveGroveMembership_StubPhase1b(entity);
            return groveTier.HasValue ? AttunementContext.Grove(groveTier.Value) : AttunementContext.WildForest;
        }

        /// <summary>
        /// E1.6: non-short-circuiting sibling of GetAttunementContext -- evaluates all three
        /// checks independently (even ones a real GetAttunementContext call wouldn't reach) so
        /// /rfattune can report which check failed, not just the combined result. Debug-only;
        /// GetAttunementContext itself stays short-circuiting for the tick's sake.
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

        public static void Resolve(ICoreAPI api, RFMechanicsConfig cfg)
        {
            string[] prefixes = cfg.AttunementForestBlockCodePrefixes;
            var ids = new HashSet<int>();
            var matched = new bool[prefixes.Length];

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
            }

            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!matched[i])
                {
                    api.Logger.Warning("[rfmechanics] ElfAttunement: forest block code prefix '{0}' matched no registered block -- typo, or the block was removed/renamed upstream?", prefixes[i]);
                }
            }

            resolvedIds = ids;
            api.Logger.Notification("[rfmechanics] ElfAttunement forest block whitelist resolved: {0} block IDs across {1} configured prefixes.", ids.Count, prefixes.Length);
        }

        public static bool IsForestNatural(int blockId) => resolvedIds != null && resolvedIds.Contains(blockId);
    }
}
