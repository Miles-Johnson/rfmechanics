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

    /// <summary>Per-resolution breakdown of check 2 (E1.12): whether the result came from the
    /// TTL-fresh persisted record or the entity's own positional cache, plus enough of the
    /// generation bookkeeping to tell "census wrong" apart from "census stale" apart from "cache
    /// didn't invalidate."</summary>
    public readonly struct ForestCensusDiagnostics
    {
        public int LogCount { get; }
        public long LastCheckedTimeMs { get; }
        public bool FromCache { get; }
        public int CachedGeneration { get; }
        public int CurrentGeneration { get; }

        public ForestCensusDiagnostics(int logCount, long lastCheckedTimeMs, bool fromCache, int cachedGeneration, int currentGeneration)
        {
            LogCount = logCount;
            LastCheckedTimeMs = lastCheckedTimeMs;
            FromCache = fromCache;
            CachedGeneration = cachedGeneration;
            CurrentGeneration = currentGeneration;
        }

        public static ForestCensusDiagnostics Unevaluated { get; } = new ForestCensusDiagnostics(0, -1, false, 0, 0);
    }

    /// <summary>Per-check breakdown for /rfattune (E1.6) -- see ElfAttunementContext.GetDiagnostics.</summary>
    public readonly struct AttunementDiagnostics
    {
        public bool ForestNaturalGround { get; }
        public bool ForestPresence { get; }
        public int? GroveTier { get; }
        public AttunementContext Context { get; }
        public ForestCensusDiagnostics ForestCensus { get; }

        public AttunementDiagnostics(bool forestNaturalGround, bool forestPresence, int? groveTier, AttunementContext context, ForestCensusDiagnostics forestCensus)
        {
            ForestNaturalGround = forestNaturalGround;
            ForestPresence = forestPresence;
            GroveTier = groveTier;
            Context = context;
            ForestCensus = forestCensus;
        }

        /// <summary>Placeholder for "the three checks were not run this tick" -- e.g. a
        /// non-elf, which ElfAttunementBehavior skips straight to AttunementContext.None
        /// without spending a GetDiagnostics call at all (the checks themselves are
        /// race-independent -- a non-elf standing on forest-natural ground would pass check 1
        /// same as an elf -- skipping is purely to avoid wasted work on every non-elf player's
        /// tick, not because the checks would fail for them). Not the same as "all three
        /// checks ran and failed" -- ForestNaturalGround/ForestPresence read false here as a
        /// default, not a real evaluation result.</summary>
        public static AttunementDiagnostics Unevaluated { get; } = new AttunementDiagnostics(false, false, null, AttunementContext.None, ForestCensusDiagnostics.Unevaluated);
    }

    /// <summary>Positional cache owned per-entity by ElfAttunementBehavior (E1.10), structured
    /// generically so Phase 2 can add a CachedGroveTier slot to the same struct. CachedLogCount
    /// is not part of the original per-column census record's staleness contract (that's
    /// ForestCensusData's job) -- it exists purely so /rfattune can always show a real log count
    /// without the fast path paying a moddata deserialize just to print one.</summary>
    public readonly struct AttunementPositionalCache
    {
        public bool HasValue { get; }
        public int ChunkX { get; }
        public int ChunkZ { get; }
        public bool CachedForestPresence { get; }
        public int CachedLogCount { get; }
        public int CachedGeneration { get; }
        public long LastCheckedTimeMs { get; }

        private AttunementPositionalCache(bool hasValue, int chunkX, int chunkZ, bool cachedForestPresence, int cachedLogCount, int cachedGeneration, long lastCheckedTimeMs)
        {
            HasValue = hasValue;
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            CachedForestPresence = cachedForestPresence;
            CachedLogCount = cachedLogCount;
            CachedGeneration = cachedGeneration;
            LastCheckedTimeMs = lastCheckedTimeMs;
        }

        public static AttunementPositionalCache Empty { get; } = new AttunementPositionalCache(false, 0, 0, false, 0, 0, -1);

        public static AttunementPositionalCache From(int chunkX, int chunkZ, bool forestPresence, int logCount, int generation, long checkedAtMs)
            => new AttunementPositionalCache(true, chunkX, chunkZ, forestPresence, logCount, generation, checkedAtMs);
    }

    /// <summary>
    /// E1.2's context predicate. GetDiagnostics is the actual source of truth: it evaluates all
    /// three checks independently (no short-circuiting) so /rfattune can report which check
    /// failed, not just the combined result -- see AttunementDiagnostics. GetAttunementContext
    /// is a thin convenience wrapper over it for callers who only want the combined
    /// None/WildForest/Grove(tier) result.
    ///
    /// ElfAttunementBehavior calls GetDiagnostics exactly once per tick and caches the result
    /// (LastDiagnostics) for /rfattune to read instead of re-evaluating -- confirmed still true
    /// as of Phase 1b (E1.11): StepAttunement/EvaluateThresholds take the already-resolved
    /// AttunementContext as a parameter and never call back into this class, so check 2's real
    /// census cost (Phase 1b, formerly a free stub) is paid at most once per tick, not per
    /// tick-times-callers. GetAttunementContext stays a live, non-duplicated convenience API
    /// for any future caller that only needs the enum.
    /// </summary>
    public static class ElfAttunementContext
    {
        public static AttunementContext GetAttunementContext(Entity entity) => GetDiagnostics(entity, AttunementPositionalCache.Empty, out _).Context;

        /// <summary>
        /// E1.6 (and the tick's own source of truth too): evaluates all three checks
        /// independently, without short-circuiting, so a caller can see every check's real
        /// result rather than just the combined context. cache/updatedCache thread an entity's
        /// AttunementPositionalCache (E1.10) through check 2 so a stationary elf in an unchanged
        /// column costs a coordinate+generation compare, not a real census consult -- callers
        /// with no cache of their own (GetAttunementContext above) pass
        /// AttunementPositionalCache.Empty and discard the result, which is equivalent to always
        /// paying check 2 fresh.
        /// </summary>
        public static AttunementDiagnostics GetDiagnostics(Entity entity, AttunementPositionalCache cache, out AttunementPositionalCache updatedCache)
        {
            bool ground = IsOnForestNaturalGround(entity);
            bool presence = ResolveForestPresence(entity, cache, out updatedCache, out ForestCensusDiagnostics censusDiag);
            int? groveTier = ResolveGroveMembership_StubPhase1b(entity);

            AttunementContext context = (!ground || !presence)
                ? AttunementContext.None
                : (groveTier.HasValue ? AttunementContext.Grove(groveTier.Value) : AttunementContext.WildForest);

            return new AttunementDiagnostics(ground, presence, groveTier, context, censusDiag);
        }

        /// <summary>
        /// Check 1 (real, not stubbed): the block underfoot (one below the entity's feet
        /// position) is on the config-backed forest-natural whitelist, resolved once at world
        /// load into a HashSet&lt;int&gt; by ElfAttunementBlockWhitelist -- never a Code.Path
        /// scan per call. Evaluates fresh every call by design -- a player changes underfoot
        /// block far more often than they cross a chunk boundary, so unlike check 2 this can't
        /// be chunk-gated without a correctness regression.
        /// </summary>
        private static bool IsOnForestNaturalGround(Entity entity)
        {
            BlockPos underfoot = entity.Pos.AsBlockPos.Down();
            Block block = entity.World.BlockAccessor.GetBlock(underfoot);
            return block != null && ElfAttunementBlockWhitelist.IsForestNatural(block.Id);
        }

        /// <summary>
        /// Check 2 (Phase 1b, real): resolves via the entity's positional cache first --
        /// PeekGeneration is a dictionary read, never a moddata deserialize, so a stationary elf
        /// in an unchanged column costs only a coordinate+generation+age compare. The fast path
        /// carries its own staleness bound (reuses AttunementCensusTtlMs) so a column that's never
        /// felled still gets re-consulted eventually. On a coordinate, generation, or age
        /// mismatch, falls through to ElfForestCensus.GetForestPresence and refreshes the cache,
        /// stamping LastCheckedTimeMs at `now` -- not at the moddata record's own Timestamp, which
        /// on a moddata cache-hit can already be up to Ttl old. The two layers' windows stack
        /// rather than share an origin: worst-case observed staleness is just under 2x
        /// AttunementCensusTtlMs, not 1x. Self-heals every cycle (once real elapsed time since the
        /// actual scan exceeds Ttl, the moddata check forces a rescan), so this doesn't drift
        /// indefinitely -- acceptable at attunement's timescale, just not free of the 2x bound. No
        /// maturity/tree-age gate: design docs reference one, but no such check exists anywhere in
        /// this codebase and nothing in the log-grown block data encodes age or size -- deliberate
        /// deferral, not an oversight, unchanged from the Phase 1a stub's own note.
        /// </summary>
        private static bool ResolveForestPresence(Entity entity, AttunementPositionalCache cache, out AttunementPositionalCache updatedCache, out ForestCensusDiagnostics diag)
        {
            BlockPos pos = entity.Pos.AsBlockPos;
            int cx = ElfForestCensus.ToChunkCoord(pos.X);
            int cz = ElfForestCensus.ToChunkCoord(pos.Z);
            int currentGeneration = ElfForestCensus.PeekGeneration(cx, cz);
            var cfg = RFMechanicsModSystem.Config;
            long now = entity.World.ElapsedMilliseconds;

            bool cacheFresh = cache.HasValue && cache.ChunkX == cx && cache.ChunkZ == cz
                && cache.CachedGeneration == currentGeneration
                && (now - cache.LastCheckedTimeMs) < cfg.AttunementCensusTtlMs;

            if (cacheFresh)
            {
                updatedCache = cache;
                diag = new ForestCensusDiagnostics(cache.CachedLogCount, cache.LastCheckedTimeMs, true, cache.CachedGeneration, currentGeneration);
                return cache.CachedForestPresence;
            }

            ForestCensusResult result = ElfForestCensus.GetForestPresence(entity.World.BlockAccessor, pos, now, cfg, RFMechanicsModSystem.Api?.Logger);

            updatedCache = AttunementPositionalCache.From(cx, cz, result.IsForest, result.LogCount, result.Generation, now);
            diag = new ForestCensusDiagnostics(result.LogCount, result.Timestamp, result.FromCache, currentGeneration, result.Generation);
            return result.IsForest;
        }

        /// <summary>
        /// Check 3: STUBBED per the Phase 1a brief. Groves don't exist yet -- always
        /// "not in a grove" until grove membership tracking is built (Phase 2).
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
