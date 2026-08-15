using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Block behavior for the Dwarf ore-song mechanic: empty-hand right-click on raw rock scans
    /// nearby ore/gem deposits and answers with one positioned, non-looping sound per material
    /// cluster. Client-side only, no network traffic. Mirrors RfGoblinSpitRepairBehavior's guard
    /// structure (own BlockBehavior on vanilla's OnBlockInteractStart dispatch, not a Harmony
    /// patch -- see notes/diagnostics/ore-song-discovery.md Q5), but ALWAYS returns PassThrough,
    /// even on success -- vanilla empty-hand-on-rock does nothing, so there is nothing to claim,
    /// and no other rfmechanics behavior is attached to rock.json to collide with.
    /// </summary>
    public class RfDwarfOreSongBehavior : BlockBehavior
    {
        private const string LastTriggerKey = "rfmechanics:oreSongLastMs";

        public RfDwarfOreSongBehavior(Block block) : base(block)
        {
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.DwarfOreSongEnabled)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            if (world.Side != EnumAppSide.Client)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            // Only act on empty hand -- same convention as RfGoblinSpitRepairBehavior.
            if (!byPlayer.InventoryManager.ActiveHotbarSlot.Empty)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            EntityPlayer player = byPlayer.Entity;

            // Load-bearing null check -- HasTrait returns true for a null class (discovery
            // report Q4). Same pattern as every other rfmechanics race gate.
            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass))
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            // Resolved via world.Api.ModLoader, never the RFMechanicsModSystem.Api static --
            // that static is last-writer-wins between the client/server instances in
            // singleplayer (discovery report Q3) and unsafe for anything side-sensitive.
            var charSys = world.Api.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null || !charSys.HasTrait(byPlayer, cfg.DwarfTraitCode))
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            long nowMs = world.ElapsedMilliseconds;
            long lastMs = player.Attributes.GetLong(LastTriggerKey, 0);
            if (nowMs - lastMs < cfg.OreSongCooldownMs)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            player.Attributes.SetLong(LastTriggerKey, nowMs);

            var clientWorld = world as IClientWorldAccessor;
            var oreSongSys = world.Api.ModLoader.GetModSystem<DwarfOreSongModSystem>();
            if (clientWorld != null && oreSongSys != null)
            {
                ScanAndPlay(world, clientWorld, oreSongSys, cfg, blockSel.Position);
            }

            // Never Handled/PreventSubsequent -- vanilla empty-hand-on-rock does nothing, so
            // there is nothing to claim from other behaviors or vanilla's own dispatch.
            handling = EnumHandling.PassThrough;
            return false;
        }

        private static void ScanAndPlay(IWorldAccessor world, IClientWorldAccessor clientWorld, DwarfOreSongModSystem oreSongSys, RFMechanicsConfig cfg, BlockPos center)
        {
            int radius = GameMath.Clamp(cfg.OreSongRadius, 1, 20);
            BlockPos min = center.AddCopy(-radius, -radius, -radius);
            BlockPos max = center.AddCopy(radius, radius, radius);
            double radiusSq = (double)radius * radius;
            double mergeDistSq = cfg.OreSongClusterMergeDistance * cfg.OreSongClusterMergeDistance;

            var clustersByMaterial = new Dictionary<string, List<Cluster>>();
            var chunkLoadedCache = new Dictionary<long, bool>();

            var sw = Stopwatch.StartNew();

            world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
            {
                if (block == null || block.Id == 0)
                    return;

                double dx = x - center.X, dy = y - center.Y, dz = z - center.Z;
                if (dx * dx + dy * dy + dz * dz > radiusSq)
                    return;

                if (!oreSongSys.Lookup.TryGetValue(block.Id, out OreSongEntry entry))
                    return;

                // Chunk-loaded check, cached per chunk (not re-fetched per block) -- unloaded
                // reads silently return air rather than throwing (discovery report Q6), but a
                // chunk sitting at the render horizon could still return a stale/partial block
                // id, so this mirrors vanilla's own GetChunk+LoadedFromServer client-side check
                // (ChunkMapLayer.cs) before trusting a hit.
                long chunkKey = ChunkKey(x >> 5, y >> 5, z >> 5);
                if (!chunkLoadedCache.TryGetValue(chunkKey, out bool loaded))
                {
                    var chunk = world.BlockAccessor.GetChunk(x >> 5, y >> 5, z >> 5);
                    loaded = chunk != null && (chunk as IClientChunk)?.LoadedFromServer == true;
                    chunkLoadedCache[chunkKey] = loaded;
                }
                if (!loaded)
                    return;

                if (!clustersByMaterial.TryGetValue(entry.AssetPath, out List<Cluster> clusters))
                {
                    clusters = new List<Cluster>();
                    clustersByMaterial[entry.AssetPath] = clusters;
                }

                Cluster target = null;
                foreach (Cluster c in clusters)
                {
                    double cdx = x - c.CentroidX(), cdy = y - c.CentroidY(), cdz = z - c.CentroidZ();
                    if (cdx * cdx + cdy * cdy + cdz * cdz <= mergeDistSq)
                    {
                        target = c;
                        break;
                    }
                }
                if (target == null)
                {
                    target = new Cluster { AssetPath = entry.AssetPath, GradeGain = entry.GradeGain };
                    clusters.Add(target);
                }

                target.SumX += x;
                target.SumY += y;
                target.SumZ += z;
                target.Count++;
                target.GradeGain = Math.Max(target.GradeGain, entry.GradeGain);
            });

            sw.Stop();
            ICoreAPI api = world.Api;
            if (sw.ElapsedMilliseconds > 15)
            {
                api.Logger.Warning("[rfmechanics] DwarfOreSong scan took {0}ms (radius {1}) -- exceeds 15ms budget.", sw.ElapsedMilliseconds, radius);
            }
            else
            {
                api.Logger.Debug("[rfmechanics] DwarfOreSong scan took {0}ms (radius {1}).", sw.ElapsedMilliseconds, radius);
            }

            var allClusters = new List<Cluster>();
            foreach (List<Cluster> clusters in clustersByMaterial.Values)
                allClusters.AddRange(clusters);

            allClusters.Sort((a, b) => a.DistSqTo(center).CompareTo(b.DistSqTo(center)));

            int playCount = Math.Min(Math.Max(0, cfg.OreSongMaxClusters), allClusters.Count);
            for (int i = 0; i < playCount; i++)
            {
                PlayCluster(world, clientWorld, cfg, allClusters[i], center, radius);
            }
        }

        private static void PlayCluster(IWorldAccessor world, IClientWorldAccessor clientWorld, RFMechanicsConfig cfg, Cluster cluster, BlockPos center, int radius)
        {
            double dist = Math.Sqrt(cluster.DistSqTo(center));
            float distanceFalloff = (float)GameMath.Clamp(1.0 - dist / radius, 0.0, 1.0);
            float volume = Math.Max((float)cfg.OreSongVolumeFloor, cluster.GradeGain * distanceFalloff);

            float jitter = (float)((world.Rand.NextDouble() * 2.0 - 1.0) * cfg.OreSongPitchJitter);
            float pitch = 1.0f + jitter;

            var param = new SoundParams()
            {
                Location = new AssetLocation("rfmechanics", "sounds/oresong/" + cluster.AssetPath + ".ogg"),
                Position = new Vec3f((float)cluster.CentroidX() + 0.5f, (float)cluster.CentroidY() + 0.5f, (float)cluster.CentroidZ() + 0.5f),
                RelativePosition = false,
                ShouldLoop = false,
                DisposeOnFinish = true,
                SoundType = EnumSoundType.Ambient,
                Pitch = pitch,
                Volume = volume,
                Range = radius + 8,
            };

            ILoadedSound sound = clientWorld.LoadSound(param);
            sound?.Start();
        }

        private static long ChunkKey(int cx, int cy, int cz)
        {
            return ((long)(cx & 0x1FFFFF) << 42) | ((long)(cy & 0x1FFFFF) << 21) | (uint)(cz & 0x1FFFFF);
        }

        private class Cluster
        {
            public string AssetPath;
            public float GradeGain;
            public double SumX, SumY, SumZ;
            public int Count;

            public double CentroidX() => SumX / Count;
            public double CentroidY() => SumY / Count;
            public double CentroidZ() => SumZ / Count;

            public double DistSqTo(BlockPos pos)
            {
                double dx = CentroidX() - pos.X, dy = CentroidY() - pos.Y, dz = CentroidZ() - pos.Z;
                return dx * dx + dy * dy + dz * dz;
            }
        }
    }
}
