using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace rfmechanics;

internal readonly record struct OreSongChunkKey(int X, int Y, int Z);

/// <summary>
/// On-demand, bounded, memory-only summaries of loaded server chunks. All world reads are on
/// the server tick thread. A quantum reads at most 256 blocks; the owner supplies ONE shared
/// stopwatch budget for all listeners. No worldgen, disk reads, worker threads or chunk pinning.
/// </summary>
internal sealed class DwarfOreSongIndex
{
    private const int Size = GlobalConstants.ChunkSize;
    private const int ChunkVolume = Size * Size * Size;
    private const int MaxCells = 256;
    private readonly ICoreServerAPI api;
    private readonly Dictionary<int, OreSongMaterial> lookup;
    private readonly ConcurrentDictionary<OreSongChunkKey, Entry> cache = new();
    private readonly Queue<(OreSongChunkKey Key, Entry Value)> insertionOrder = new();
    private readonly List<int> palette = new();
    private readonly int capacity;
    internal long BlocksRead, PaletteSkips, CacheHits;
    internal int CacheCount => cache.Count;

    internal DwarfOreSongIndex(ICoreServerAPI api, int capacity)
    {
        this.api = api;
        this.capacity = Math.Clamp(capacity, 64, 1024);
        lookup = OreSongRules.BuildLookup(api.World);
        api.Logger.Notification("[rfmechanics] Ore-Song server lookup: {0} ore/gem blocks.", lookup.Count);
    }

    // ChunkDirty may originate outside the main thread. Only invalidate here; never read blocks.
    internal void Invalidate(Vec3i position, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        var key = new OreSongChunkKey(position.X, position.Y, position.Z);
        if (cache.TryRemove(key, out var entry)) entry.Invalid = true;
    }

    internal void Clear()
    {
        cache.Clear();
        insertionOrder.Clear();
    }

    internal Query CreateQuery(BlockPos center, int radius) => new(center, radius, api.World.BlockAccessor.MapSizeY);

    // Returns true when this quantum prepared/unpacked a new chunk. The owner also caps that
    // count, since an individual engine decompression cannot be preempted by our stopwatch.
    internal bool Step(Query query, long now)
    {
        if (query.Done) return false;
        var key = query.Keys[query.Cursor];
        var chunk = api.WorldManager.GetChunk(key.X, key.Y, key.Z);
        if (chunk == null)
        {
            query.Incomplete = true;
            query.Cursor++;
            return false;
        }

        bool prepared = false;
        if (!cache.TryGetValue(key, out var entry) || entry.Invalid || now - entry.CreatedMs > 30000
            || !entry.Source.TryGetTarget(out var original) || !ReferenceEquals(original, chunk))
        {
            entry = new Entry(chunk, now);
            while (insertionOrder.Count >= capacity)
            {
                var old = insertionOrder.Dequeue();
                if (cache.TryGetValue(old.Key, out var current) && ReferenceEquals(current, old.Value))
                    cache.TryRemove(old.Key, out _);
            }
            cache[key] = entry;
            insertionOrder.Enqueue((key, entry));
            prepared = true;
            chunk.Unpack_ReadOnly(); // Return value means "was packed", not success.
            if (chunk.Disposed)
            {
                entry.Invalid = true;
                query.Incomplete = true;
                query.Cursor++;
                return prepared;
            }
            palette.Clear();
            chunk.Data.TakeBulkReadLock();
            try { chunk.Data.FuzzyListBlockIds(palette); }
            finally { chunk.Data.ReleaseBulkReadLock(); }
            bool hasOre = false;
            foreach (int id in palette) if (lookup.ContainsKey(id)) { hasOre = true; break; }
            if (!hasOre)
            {
                entry.Complete = true;
                PaletteSkips++;
            }
        }
        else if (entry.Complete) CacheHits++;

        if (!entry.Complete)
        {
            chunk.Unpack_ReadOnly();
            if (chunk.Disposed)
            {
                entry.Invalid = true;
                query.Incomplete = true;
                query.Cursor++;
                return prepared;
            }
            IChunkBlocks data = chunk.Data;
            int end = Math.Min(entry.NextBlock + 256, ChunkVolume);
            // Never keep a lock between work quanta/ticks. The palette can change between them;
            // dirty invalidation discards the unfinished summary in that case.
            data.TakeBulkReadLock();
            try
            {
                for (; entry.NextBlock < end; entry.NextBlock++)
                {
                    int index = entry.NextBlock;
                    int blockId = data.GetBlockIdUnsafe(index);
                    BlocksRead++;
                    if (!lookup.TryGetValue(blockId, out var material)) continue;
                    int x = index % Size, z = index / Size % Size, y = index / (Size * Size);
                    // Eight-block cells prevent a long vein being reduced to one misleading
                    // midpoint, without storing each ore block. Material identity is never an asset ID.
                    int cell = ((y / 8) * 4 + z / 8) * 4 + x / 8;
                    var cellKey = (material.Id, cell);
                    if (!entry.Cells.TryGetValue(cellKey, out var ore))
                    {
                        if (entry.Cells.Count >= MaxCells) { entry.Truncated = true; continue; }
                        ore = new Cell { Material = material };
                        entry.Cells[cellKey] = ore;
                    }
                    ore.Count++;
                    ore.X += key.X * Size + x + 0.5;
                    ore.Y += (key.Y * Size) % BlockPos.DimensionBoundary + y + 0.5;
                    ore.Z += key.Z * Size + z + 0.5;
                    ore.Grade += material.Grade;
                }
            }
            finally { data.ReleaseBulkReadLock(); }
            entry.Complete = entry.NextBlock == ChunkVolume;
        }

        if (entry.Complete && !entry.Invalid)
        {
            foreach (Cell cell in entry.Cells.Values) query.Accept(cell);
            query.Incomplete |= entry.Truncated;
            query.Cursor++;
        }
        return prepared;
    }

    private sealed class Entry
    {
        internal readonly WeakReference<IWorldChunk> Source;
        internal readonly long CreatedMs;
        internal readonly Dictionary<(int Material, int Cell), Cell> Cells = new();
        internal int NextBlock;
        internal bool Complete, Truncated;
        internal volatile bool Invalid;
        internal Entry(IWorldChunk chunk, long now) { Source = new(chunk); CreatedMs = now; }
    }

    internal sealed class Cell
    {
        internal OreSongMaterial Material;
        internal int Count;
        internal double X, Y, Z, Grade;
    }

    internal sealed class Query
    {
        internal readonly List<OreSongChunkKey> Keys = new();
        internal int Cursor;
        internal bool Incomplete;
        internal bool Done => Cursor >= Keys.Count;
        private readonly BlockPos center;
        private readonly int radius;
        private readonly Dictionary<(int Material, int Bearing, int Elevation, int Band), Impression> impressions = new();

        internal Query(BlockPos center, int radius, int mapHeight)
        {
            this.center = center.Copy();
            this.radius = radius;
            int minY = Math.Max(0, center.Y - radius), maxY = Math.Min(mapHeight - 1, center.Y + radius);
            int dimOffset = center.dimension * (BlockPos.DimensionBoundary / Size);
            for (int y = minY >> 5; y <= maxY >> 5; y++)
            for (int z = (center.Z - radius) >> 5; z <= (center.Z + radius) >> 5; z++)
            for (int x = (center.X - radius) >> 5; x <= (center.X + radius) >> 5; x++)
            {
                double dx = center.X + 0.5 - Math.Clamp(center.X + 0.5, x * Size, (x + 1) * Size);
                double dy = center.Y + 0.5 - Math.Clamp(center.Y + 0.5, y * Size, (y + 1) * Size);
                double dz = center.Z + 0.5 - Math.Clamp(center.Z + 0.5, z * Size, (z + 1) * Size);
                if (dx * dx + dy * dy + dz * dz <= radius * radius) Keys.Add(new(x, y + dimOffset, z));
            }
            // Stable ordering: incomplete results favour the ground closest to the listener.
            Keys.Sort((a, b) => ChunkDistance(a).CompareTo(ChunkDistance(b)));
        }

        private double ChunkDistance(OreSongChunkKey key)
        {
            double dx = key.X * Size + 16 - center.X;
            double dy = key.Y * Size + 16 - center.InternalY;
            double dz = key.Z * Size + 16 - center.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        internal void Accept(Cell cell)
        {
            double dx = cell.X / cell.Count - center.X - 0.5;
            double dy = cell.Y / cell.Count - center.Y - 0.5;
            double dz = cell.Z / cell.Count - center.Z - 0.5;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > radius) return;
            int band = distance < 12 ? 0 : distance < 32 ? 1 : distance < 64 ? 2 : 3;
            int bearing = band == 0 ? 0 : ((int)Math.Round(Math.Atan2(dz, dx) * 6 / Math.PI) + 12) % 12;
            double horizontal = Math.Sqrt(dx * dx + dz * dz);
            int elevation = band == 0 || Math.Abs(dy) < Math.Max(8, horizontal * 0.35) ? 0 : Math.Sign(dy);
            var key = (cell.Material.Id, bearing, elevation, band);
            if (!impressions.TryGetValue(key, out var impression))
            {
                if (impressions.Count >= 512) { Incomplete = true; return; }
                impression = new Impression { Material = cell.Material, Bearing = bearing, Elevation = elevation, Band = band };
                impressions[key] = impression;
            }
            impression.Count += cell.Count;
            impression.Grade += cell.Grade;
        }

        internal OreSongVoice[] Voices()
        {
            // One dominant bearing per actual mineral. Do not average opposite veins into an
            // imaginary source, and do not let several quartz clusters steal every voice.
            var selected = impressions.Values.GroupBy(i => i.Material.Name)
                .Select(g => g.OrderByDescending(i => i.Score).ThenBy(i => i.Bearing).First())
                .OrderByDescending(i => i.Score).ThenBy(i => i.Material.Name, StringComparer.Ordinal)
                .ToArray();
            Incomplete |= selected.Length > OreSongRules.MaxReplyVoices;
            return selected.Take(OreSongRules.MaxReplyVoices)
                .Select(i => new OreSongVoice
                {
                    Material = i.Material.Name, Asset = i.Material.Asset, Bearing = i.Bearing,
                    Elevation = i.Elevation, DistanceBand = i.Band,
                    Fullness = Math.Clamp((float)Math.Log2(1 + i.Count) / 5, 0.2f, 1f),
                    Grade = (float)(i.Grade / i.Count)
                }).ToArray();
        }

        private sealed class Impression
        {
            internal OreSongMaterial Material;
            internal int Bearing, Elevation, Band, Count;
            internal double Grade;
            internal double Score => (1 + Math.Min(Count, 32) / 32.0) / (1 + Band * 0.45);
        }
    }
}
