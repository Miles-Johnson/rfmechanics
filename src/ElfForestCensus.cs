using System.Collections.Generic;
using System.Diagnostics;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>Persisted per-column census record (IMapChunk moddata, ProtoBuf-serialized --
    /// same shape convention as ArcaneEquipment.Attunement.AttunementData). Timestamp doubles as
    /// a staleness sentinel: -1 means "needs a rescan regardless of age," set both for a
    /// never-censused column and by ElfForestCensus.InvalidateColumn on felling. Never derive
    /// staleness by subtracting from a -1 timestamp -- see GetForestPresence's explicit check.</summary>
    [ProtoContract]
    public class ForestCensusData
    {
        [ProtoMember(1)] public int LogCount { get; set; }
        [ProtoMember(2)] public long Timestamp { get; set; } = -1;
        [ProtoMember(3)] public int Generation { get; set; }
    }

    /// <summary>Result of a GetForestPresence call. FromCache=true means the persisted
    /// TTL-fresh record was trusted with no section scan this call -- distinct from the
    /// positional cache (AttunementPositionalCache) one layer up, which can skip even the
    /// moddata read this result implies.</summary>
    public readonly struct ForestCensusResult
    {
        public bool IsForest { get; }
        public int LogCount { get; }
        public long Timestamp { get; }
        public int Generation { get; }
        public bool FromCache { get; }

        private ForestCensusResult(bool isForest, int logCount, long timestamp, int generation, bool fromCache)
        {
            IsForest = isForest;
            LogCount = logCount;
            Timestamp = timestamp;
            Generation = generation;
            FromCache = fromCache;
        }

        public static ForestCensusResult From(bool isForest, int logCount, long timestamp, int generation, bool fromCache)
            => new ForestCensusResult(isForest, logCount, timestamp, generation, fromCache);
    }

    /// <summary>
    /// E1.8/E1.9: per-column log census, backed by IMapChunk moddata (server-only, unsynced,
    /// correct granularity for a signal that changes on the order of minutes -- see
    /// elf-attunement-breakdown-v2.md E1.2). Threading stays lazy-synchronous: a resolution runs
    /// inline on whichever tick asks for it, no background worker.
    /// </summary>
    public static class ElfForestCensus
    {
        private const string ModDataKey = "rfElfForestCensus";

        /// <summary>In-memory-only shadow of each touched column's Generation, keyed by packed
        /// chunk coord. This is the entire reason AttunementPositionalCache's fast path can cost
        /// "a coordinate and generation compare, nothing more": PeekGeneration never touches
        /// moddata. Deliberately NOT persisted -- a cold entry (missing key, treated as 0) only
        /// ever matters when the caller's own cache is also cold (see ResolveForestPresence),
        /// at which point the ensuing GetForestPresence call reads the real persisted generation
        /// and reseeds this dictionary, so the two numbering spaces reconverge on first touch
        /// every session, including after a server restart. Not thread-safe -- valid only under
        /// this mod's lazy-synchronous, main-thread-only design.</summary>
        private static readonly Dictionary<long, int> generationCache = new Dictionary<long, int>();

        /// <summary>Columns whose persisted record already reads Timestamp=-1 from a felling this
        /// cycle -- write-amplification guard for InvalidateColumn (E1.2/FIX3): a redwood pops
        /// dozens of log blocks off one BFS sweep, each calling InvalidateColumn, but the moddata
        /// write only needs to happen once (Timestamp=-1 already forces the next GetForestPresence
        /// to rescan; writing it again is redundant). Cleared the moment a real scan completes and
        /// persists a fresh Timestamp, so the next felling writes again. Same lifetime/thread-safety
        /// contract as generationCache.</summary>
        private static readonly HashSet<long> pendingRescan = new HashSet<long>();

        private static long ColumnKey(int chunkX, int chunkZ) => ((long)(uint)chunkX << 32) | (uint)chunkZ;

        private static void SeedGeneration(int chunkX, int chunkZ, int generation)
        {
            generationCache[ColumnKey(chunkX, chunkZ)] = generation;
        }

        /// <summary>Floors toward negative infinity, unlike plain integer division (which
        /// truncates toward zero and misclassifies e.g. blockCoord=-1 into chunk 0 instead of
        /// chunk -1 -- a real bug west/north of the origin where two adjacent chunks would
        /// otherwise share a census). GlobalConstants.ChunkSize stays the one source of truth for
        /// chunk size per E1.8's brief; this only fixes the rounding direction, not the divisor.</summary>
        public static int ToChunkCoord(int blockCoord)
        {
            int size = GlobalConstants.ChunkSize;
            return blockCoord >= 0 ? blockCoord / size : (blockCoord - size + 1) / size;
        }

        /// <summary>Cheap generation read for AttunementPositionalCache's fast path -- dictionary
        /// lookup only, never a moddata deserialize. Missing entry reads as 0, matching a
        /// never-invalidated ForestCensusData's default Generation.</summary>
        public static int PeekGeneration(int chunkX, int chunkZ)
            => generationCache.TryGetValue(ColumnKey(chunkX, chunkZ), out int gen) ? gen : 0;

        /// <summary>TTL-aware entry point. A null IMapChunk (column not loaded) returns
        /// IsForest=false/Timestamp=-1/FromCache=false without forcing a load -- "not censused"
        /// is a valid, cheap answer, never a blocking one.</summary>
        public static ForestCensusResult GetForestPresence(IBlockAccessor blockAccessor, BlockPos pos, long nowMs, RFMechanicsConfig cfg, ILogger logger)
        {
            int cx = ToChunkCoord(pos.X);
            int cz = ToChunkCoord(pos.Z);

            IMapChunk mapChunk = blockAccessor.GetMapChunkAtBlockPos(pos);
            if (mapChunk == null)
            {
                return ForestCensusResult.From(false, 0, -1, 0, false);
            }

            ForestCensusData data = mapChunk.GetModdata<ForestCensusData>(ModDataKey);

            // data.Timestamp < 0 is the sole staleness trigger for an invalidated/never-censused
            // record -- deliberately not folded into an age subtraction. A back-dated timestamp
            // (nowMs - Ttl - 1) wraps negative during a server's first TTL window and would read
            // as falsely fresh; the explicit sentinel check has no such edge.
            if (data != null && data.Timestamp >= 0 && (nowMs - data.Timestamp) < cfg.AttunementCensusTtlMs)
            {
                SeedGeneration(cx, cz, data.Generation);
                bool isForestCached = data.LogCount > cfg.AttunementCensusLogCountThreshold;
                return ForestCensusResult.From(isForestCached, data.LogCount, data.Timestamp, data.Generation, true);
            }

            // Prefer the in-memory shadow over the persisted value: InvalidateColumn bumps it on
            // every felling but may have skipped the moddata write (debounced, see pendingRescan),
            // so moddata's Generation can lag. Falling back to the persisted value only matters on
            // a column's first touch this session, when the shadow is still cold.
            int generation = generationCache.TryGetValue(ColumnKey(cx, cz), out int cachedGen) ? cachedGen : (data?.Generation ?? 0);
            bool logTiming = cfg.AttunementCensusLogTiming;

            Stopwatch? sw = logTiming ? Stopwatch.StartNew() : null;
            int logCount = ScanColumn(blockAccessor, mapChunk, cx, cz, cfg, out int sectionsExamined, out int sectionsPrefilterHit);
            sw?.Stop();

            var newData = new ForestCensusData { LogCount = logCount, Timestamp = nowMs, Generation = generation };
            mapChunk.SetModdata(ModDataKey, newData);
            mapChunk.MarkDirty();
            SeedGeneration(cx, cz, generation);
            pendingRescan.Remove(ColumnKey(cx, cz));

            if (logTiming)
            {
                logger?.Notification(
                    "[rfmechanics] ElfForestCensus scan column ({0},{1}): elapsedMs={2} logCount={3} sectionsPrefilterHit={4}/{5}",
                    cx, cz, sw?.ElapsedMilliseconds ?? -1, logCount, sectionsPrefilterHit, sectionsExamined);
            }

            bool isForest = logCount > cfg.AttunementCensusLogCountThreshold;
            return ForestCensusResult.From(isForest, logCount, nowMs, generation, false);
        }

        /// <summary>Called only from ElfForestCensusInvalidationPatch on a confirmed log-grown
        /// felling. Does NOT re-scan inline -- block breaks are far more frequent than a census
        /// consult should tolerate re-scanning on. The in-memory generation shadow always bumps,
        /// every call, so any entity's positional cache punches through immediately on the same
        /// tick regardless of its own TTL. The moddata write (SetModdata+MarkDirty) is debounced
        /// via pendingRescan: felling a tall tree pops dozens of log blocks off one BFS sweep, each
        /// calling this method, but Timestamp=-1 only needs writing once -- it already forces the
        /// next GetForestPresence to rescan, so repeating the write for every remaining block in
        /// the same sweep bought nothing except moddata churn. pendingRescan clears itself the
        /// moment that rescan actually runs, so the next felling writes again.</summary>
        public static void InvalidateColumn(IWorldAccessor world, BlockPos pos)
        {
            IMapChunk mapChunk = world.BlockAccessor.GetMapChunkAtBlockPos(pos);
            if (mapChunk == null) return; // a block just broken by a player is always in a loaded column

            int cx = ToChunkCoord(pos.X);
            int cz = ToChunkCoord(pos.Z);
            long key = ColumnKey(cx, cz);

            int newGeneration = (generationCache.TryGetValue(key, out int cachedGen) ? cachedGen : 0) + 1;
            generationCache[key] = newGeneration;

            if (!pendingRescan.Add(key)) return; // already queued by an earlier block in this same felling sweep

            ForestCensusData data = mapChunk.GetModdata<ForestCensusData>(ModDataKey) ?? new ForestCensusData();
            data.Generation = newGeneration;
            data.Timestamp = -1;
            mapChunk.SetModdata(ModDataKey, data);
            mapChunk.MarkDirty();
        }

        /// <summary>Palette prefilter before per-cell scan: FuzzyListBlockIds per chunk section
        /// overlapping the Y-band, and only on a hit against the log-grown set does the section
        /// pay for a bulk read lock + GetBlockIdUnsafe walk. IBlockAccessor.GetBlock pays a
        /// chunk-hash lookup plus lock acquire/release per block -- deliberately not used here.
        /// The Y-band is a single envelope aggregated across the whole column (min/max over all
        /// 1024 positions' heightmap entries), not evaluated per-position, since the resulting
        /// census is cached and shared for the whole column, not tied to one caller's position.</summary>
        private static int ScanColumn(IBlockAccessor blockAccessor, IMapChunk mapChunk, int cx, int cz, RFMechanicsConfig cfg, out int sectionsExamined, out int sectionsPrefilterHit)
        {
            sectionsExamined = 0;
            sectionsPrefilterHit = 0;

            ushort[] rain = mapChunk.RainHeightMap;
            ushort[] worldGen = mapChunk.WorldGenTerrainHeightMap;

            int chunksize = GlobalConstants.ChunkSize;
            int minFloor = int.MaxValue;
            int maxCeil = int.MinValue;
            for (int i = 0; i < rain.Length; i++)
            {
                int floor = System.Math.Min(worldGen[i], rain[i]) - cfg.AttunementCensusSurfaceBandBelow;
                int ceil = rain[i] + cfg.AttunementCensusSurfaceBandAbove;
                if (floor < minFloor) minFloor = floor;
                if (ceil > maxCeil) maxCeil = ceil;
            }

            int mapSizeY = blockAccessor.MapSizeY;
            minFloor = GameMath.Clamp(minFloor, 0, mapSizeY - 1);
            maxCeil = GameMath.Clamp(maxCeil, 0, mapSizeY - 1);
            if (maxCeil < minFloor) return 0; // degenerate band (e.g. mapSizeY smaller than expected) -- nothing to scan

            int chunkYMin = minFloor / chunksize;
            int chunkYMax = maxCeil / chunksize;

            int logCount = 0;
            var fuzzyIds = new List<int>();

            for (int cy = chunkYMin; cy <= chunkYMax; cy++)
            {
                sectionsExamined++;
                IWorldChunk chunk = blockAccessor.GetChunk(cx, cy, cz);
                if (chunk == null || chunk.Disposed) continue;

                // Data is a raw field, null whenever the chunk is currently packed (compressed
                // after ~8s untouched -- WorldChunk.TryCommitPackAndFree). Unpack_ReadOnly is the
                // documented way to guarantee Data is populated before touching it; every vanilla
                // block accessor calls it first, this scan is column-direct and bypassed that.
                if (!chunk.Unpack_ReadOnly()) continue;

                IChunkBlocks blocks = chunk.Data;
                fuzzyIds.Clear();
                blocks.FuzzyListBlockIds(fuzzyIds);

                bool hit = false;
                for (int i = 0; i < fuzzyIds.Count; i++)
                {
                    if (ElfAttunementBlockWhitelist.IsLogGrown(fuzzyIds[i])) { hit = true; break; }
                }
                if (!hit) continue;

                sectionsPrefilterHit++;
                int sectionBaseY = cy * chunksize;
                int localYStart = System.Math.Max(0, minFloor - sectionBaseY);
                int localYEnd = System.Math.Min(chunksize - 1, maxCeil - sectionBaseY);

                blocks.TakeBulkReadLock();
                try
                {
                    for (int ly = localYStart; ly <= localYEnd; ly++)
                    {
                        for (int lz = 0; lz < chunksize; lz++)
                        {
                            for (int lx = 0; lx < chunksize; lx++)
                            {
                                int index3d = (chunksize * ly + lz) * chunksize + lx;
                                if (ElfAttunementBlockWhitelist.IsLogGrown(blocks.GetBlockIdUnsafe(index3d)))
                                    logCount++;
                            }
                        }
                    }
                }
                finally
                {
                    blocks.ReleaseBulkReadLock();
                }
            }

            return logCount;
        }
    }
}
