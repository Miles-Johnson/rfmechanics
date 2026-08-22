using System;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>Persisted per-map-chunk scar record, IMapChunk moddata, ProtoBuf-serialized via
    /// SetModdata&lt;T&gt;/GetModdata&lt;T&gt; (confirmed present on the 1.22.6 IMapChunk surface --
    /// see reference/decompiled/1.22/VintagestoryAPI/Vintagestory.API.Common/IMapChunk.cs). Class,
    /// not struct -- deliberately, same shape as the archived ElfForestCensus.ForestCensusData,
    /// which already round-tripped through this exact SetModdata&lt;T&gt;/GetModdata&lt;T&gt; path.
    /// This diagnostic exists to test IMapChunk moddata persistence; an untested serializer
    /// interaction (protobuf-net on a struct has no in-repo precedent) would confound that
    /// result with a second unknown, so the proven shape wins over the literal spec. A
    /// never-written map chunk reads back as GetModdata returning null -- callers must not
    /// collapse that to a fresh zero-count instance themselves; see ChunkScarCellStatus.</summary>
    [ProtoContract]
    public class ChunkScarData
    {
        [ProtoMember(1)] public int Count { get; set; }
        [ProtoMember(2)] public double LastWriteHours { get; set; }

        /// <summary>Set only on the raw Count's 0-&gt;1 transition in RecordBreak -- this is the
        /// STORED count, never the decayed display value (ComputeDecayed is read-only, never
        /// written back), so raw Count is 0 only immediately after a Reset (RemoveModdata deletes
        /// the whole record) or before any write has ever happened. A fully-decayed-to-zero chunk
        /// still has its true raw Count on disk, so this field is never silently reset by decay.</summary>
        [ProtoMember(3)] public double FirstWriteHours { get; set; }
    }

    /// <summary>Unloaded: the map chunk itself isn't resident, data is never forced to load and
    /// is null. AbsentKey: the chunk is resident but this key was never written, data is null.
    /// Value: data is non-null, including a legitimately decayed-to-zero Count. Exists so a null
    /// read is never displayed as a 0 -- 0 means "no scar here", not "we don't know".</summary>
    public enum ChunkScarCellStatus { Unloaded, AbsentKey, Value }

    /// <summary>Status plus decayed count for one /rfscar around or /rfscar bench grid cell.
    /// DecayedCount is meaningful only when Status == Value.</summary>
    public readonly struct ChunkScarSample
    {
        public readonly ChunkScarCellStatus Status;
        public readonly int DecayedCount;

        public ChunkScarSample(ChunkScarCellStatus status, int decayedCount)
        {
            Status = status;
            DecayedCount = decayedCount;
        }
    }

    /// <summary>Chunk scar counter (RFMechanicsConfig.ChunkScarTrackingEnabled gates the write
    /// path; /rfscar reads it). Originally built to measure four assumptions before any design
    /// work depended on them: does IMapChunk moddata survive unload/restart, what does a 3x3
    /// neighbour read cost, and does VS track player-placed state for logs. Decay is a pure
    /// function of the stored struct and the current calendar hour, computed at read time in
    /// ComputeDecayed -- there is no tick loop and no in-memory cache, so a map chunk that has
    /// been unloaded for a week decays identically to one read every tick.
    ///
    /// ARCHIVED BOUNDARY (2026-08-22, see notes/race-mechanics/chunk-scar-archived.md):
    /// - This system has no gameplay consumer by design. It only counts and decays.
    /// - Persistence across chunk unload and server restart is UNVERIFIED. Any future consumer
    ///   must confirm it (see the archived doc's smoke-test checklist) before depending on it.
    /// - The intended consumer is a future standalone mod, not rfmechanics. An elf-buff wiring
    ///   design against this tracker was planned and approved, then cancelled before
    ///   implementation -- the full cancelled design is preserved in the archived doc. Do not
    ///   read a live tracker with no consumer as an unfinished feature and wire it up here.</summary>
    public static class ChunkScarTracker
    {
        public const string ScarKey = "rfmechanics:scar";

        /// <summary>Leaf breaks are recorded under a second, independent moddata entry using the
        /// same ChunkScarData shape -- never merged into ScarKey's count. See
        /// RFMechanicsConfig.ChunkScarLeafBlockCodePrefixes.</summary>
        public const string LeafScarKey = "rfmechanics:scarleaf";

        /// <summary>Floors toward negative infinity, unlike plain integer division -- matches
        /// the archived ElfForestCensus.ToChunkCoord, duplicated here rather than shared since
        /// that file is excluded from the build (see rfmechanics.csproj).</summary>
        public static int ToChunkCoord(int blockCoord)
        {
            int size = GlobalConstants.ChunkSize;
            return blockCoord >= 0 ? blockCoord / size : (blockCoord - size + 1) / size;
        }

        /// <summary>Write path. No-op if the map chunk containing pos isn't currently resident
        /// (a block a player just broke is always in a loaded column, so this should never
        /// actually miss in practice -- guarded anyway since GetMapChunkAtBlockPos can return
        /// null). SetModdata already calls MarkDirty internally (confirmed:
        /// ServerMapChunk.cs:205-212) -- the explicit MarkDirty below is redundant, kept as
        /// belt-and-suspenders rather than relying on an implementation detail of a type this
        /// mod doesn't own.</summary>
        public static void RecordBreak(IWorldAccessor world, BlockPos pos, string key)
        {
            IMapChunk mapChunk = world.BlockAccessor.GetMapChunkAtBlockPos(pos);
            if (mapChunk == null) return;

            ChunkScarData data = mapChunk.GetModdata<ChunkScarData>(key) ?? new ChunkScarData();
            if (data.Count == 0) data.FirstWriteHours = world.Calendar.TotalHours;
            data.Count += 1;
            data.LastWriteHours = world.Calendar.TotalHours;
            mapChunk.SetModdata(key, data);
            mapChunk.MarkDirty();
        }

        /// <summary>Read path. Never forces a load to find out if a chunk isn't resident -- see
        /// ChunkScarCellStatus for what each status means. data is non-null only when the
        /// returned status is Value.</summary>
        public static ChunkScarCellStatus TryGetRaw(IBlockAccessor blockAccessor, int chunkX, int chunkZ, string key, out ChunkScarData? data)
        {
            IMapChunk mapChunk = blockAccessor.GetMapChunk(chunkX, chunkZ);
            if (mapChunk == null)
            {
                data = null;
                return ChunkScarCellStatus.Unloaded;
            }

            data = mapChunk.GetModdata<ChunkScarData>(key);
            return data == null ? ChunkScarCellStatus.AbsentKey : ChunkScarCellStatus.Value;
        }

        /// <summary>decayHoursPerPoint &lt;= 0 is treated as "no decay configured" rather than
        /// dividing by zero -- a misconfigured value should freeze the counter, not crash the
        /// command that reads it.</summary>
        public static int ComputeDecayed(ChunkScarData data, double nowHours, double decayHoursPerPoint)
        {
            if (data.Count <= 0) return 0;
            if (decayHoursPerPoint <= 0) return data.Count;

            double elapsedHours = Math.Max(0.0, nowHours - data.LastWriteHours);
            int pointsDecayed = (int)(elapsedHours / decayHoursPerPoint);
            return Math.Max(0, data.Count - pointsDecayed);
        }

        /// <summary>Shared by /rfscar around (printed once) and /rfscar bench (run 1000x) so the
        /// benchmark measures the exact same read path the diagnostic command uses, not a
        /// synthetic stand-in. Grid is [dx+radius, dz+radius].</summary>
        public static ChunkScarSample[,] SampleGrid(IBlockAccessor blockAccessor, int centerChunkX, int centerChunkZ, int radius, double nowHours, double decayHoursPerPoint, string key)
        {
            int size = radius * 2 + 1;
            var grid = new ChunkScarSample[size, size];

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    ChunkScarCellStatus status = TryGetRaw(blockAccessor, centerChunkX + dx, centerChunkZ + dz, key, out ChunkScarData? data);
                    int decayed = status == ChunkScarCellStatus.Value ? ComputeDecayed(data!, nowHours, decayHoursPerPoint) : 0;
                    grid[dx + radius, dz + radius] = new ChunkScarSample(status, decayed);
                }
            }

            return grid;
        }

        public static void Reset(IBlockAccessor blockAccessor, int chunkX, int chunkZ, string key)
        {
            IMapChunk mapChunk = blockAccessor.GetMapChunk(chunkX, chunkZ);
            mapChunk?.RemoveModdata(key);
            mapChunk?.MarkDirty();
        }

        public const string SelfTestKey = "rfmechanics:scarselftest";

        /// <summary>Isolates a serializer failure from a persistence failure: writes a sentinel
        /// to a dedicated key (never ScarKey/LeafScarKey) on the current map chunk, reads it back
        /// immediately via the same SetModdata&lt;T&gt;/GetModdata&lt;T&gt; path RecordBreak/TryGetRaw
        /// use, and reports the raw serialized byte length via IMapChunk's byte[] GetModdata
        /// overload. Cleans up its own key afterward so it never pollutes a chunk under real
        /// test. Returns false (byteLength 0) if the map chunk isn't resident.</summary>
        public static bool SelfTest(IWorldAccessor world, BlockPos pos, out int byteLength)
        {
            byteLength = 0;
            IMapChunk mapChunk = world.BlockAccessor.GetMapChunkAtBlockPos(pos);
            if (mapChunk == null) return false;

            var sentinel = new ChunkScarData { Count = 12345, LastWriteHours = 6789.5, FirstWriteHours = 1234.5 };
            mapChunk.SetModdata(SelfTestKey, sentinel);

            ChunkScarData? readBack = mapChunk.GetModdata<ChunkScarData>(SelfTestKey);
            byte[] raw = mapChunk.GetModdata(SelfTestKey);
            byteLength = raw?.Length ?? 0;

            mapChunk.RemoveModdata(SelfTestKey);

            return readBack != null
                && readBack.Count == sentinel.Count
                && readBack.LastWriteHours == sentinel.LastWriteHours
                && readBack.FirstWriteHours == sentinel.FirstWriteHours;
        }
    }
}
