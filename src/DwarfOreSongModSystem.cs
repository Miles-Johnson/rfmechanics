using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    public class OreSongEntry
    {
        public string AssetPath;
        public float GradeGain;
    }

    /// <summary>
    /// Client-only lookup table plus trigger for the Dwarf ore-song mechanic: maps every
    /// registered Ore-material block id to (asset path, grade-derived gain), and fires a scan
    /// centered on the player when RaceAbilityHotkeyModSystem dispatches to TryTrigger --
    /// Variant["type"]/["grade"]/["potential"] reads are already O(1) per-block, so the lookup
    /// just memoizes an already-cheap read (see notes/diagnostics/ore-song-discovery.md Q1).
    ///
    /// Client-only (ShouldLoad), mirroring GoblinDarkvisionModSystem's own pattern (Q3) --
    /// exactly one instance ever exists, so unlike RFMechanicsModSystem.Api this has no
    /// last-writer-wins race.
    /// </summary>
    public class DwarfOreSongModSystem : ModSystem
    {
        private const string LastTriggerKey = "rfmechanics:oreSongLastMs";

        public Dictionary<int, OreSongEntry> Lookup { get; } = new Dictionary<int, OreSongEntry>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            BuildLookup(api);
        }

        /// <summary>Moved here from the old right-click-on-rock BlockBehavior -- same cooldown
        /// key, just centered on the player's feet instead of a clicked block position, since a
        /// keypress has no block selection to read.
        ///
        /// Called from RaceAbilityHotkeyModSystem's dispatch table once it has already confirmed
        /// the presser is cached as Dwarf -- no race check here, that decision belongs to the
        /// dispatcher alone.</summary>
        internal bool TryTrigger(ICoreClientAPI api)
        {
            var cfg = RFMechanicsModSystem.Config;
            IPlayer player = api.World.Player;
            if (cfg == null || player?.Entity == null) return false;

            if (!cfg.DwarfOreSongEnabled) return true;

            EntityPlayer entityPlayer = player.Entity;
            long nowMs = api.World.ElapsedMilliseconds;
            long lastMs = entityPlayer.Attributes.GetLong(LastTriggerKey, 0);
            if (nowMs - lastMs < cfg.OreSongCooldownMs) return true;

            entityPlayer.Attributes.SetLong(LastTriggerKey, nowMs);
            ScanAndPlay(api.World, api.World, this, cfg, entityPlayer.Pos.AsBlockPos);
            return true;
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

        private void BuildLookup(ICoreClientAPI api)
        {
            var loggedUnmapped = new HashSet<string>();

            foreach (Block block in api.World.Blocks)
            {
                if (block == null || block.BlockMaterial != EnumBlockMaterial.Ore)
                    continue;

                // Read Variant["type"] directly off Block, never cast to BlockOre -- gems are
                // plain Block and a cast throws/returns null, silently dropping every
                // diamond/emerald/olivine (discovery report Q1).
                string type = block.Variant["type"];
                if (string.IsNullOrEmpty(type))
                    continue;

                string material = ResolveJointMaterial(type);
                string assetPath = MaterialToAsset(material);
                if (assetPath == null)
                {
                    assetPath = "oresong-nativecopper";
                    if (loggedUnmapped.Add(material))
                    {
                        api.Logger.Debug("[rfmechanics] DwarfOreSong: unmapped ore type '{0}' (from '{1}') -- routed to neutral default (oresong-nativecopper).", material, type);
                    }
                }

                Lookup[block.Id] = new OreSongEntry
                {
                    AssetPath = assetPath,
                    GradeGain = ComputeGradeGain(block)
                };
            }

            api.Logger.Notification("[rfmechanics] DwarfOreSong lookup built: {0} ore/gem blocks mapped.", Lookup.Count);
        }

        /// <summary>
        /// Joint type values (e.g. "galena_nativesilver") need a single substance picked out of
        /// two. Verified against the live install's actual ore-graded.json/ore-gem.json rather
        /// than assumed: the only real joint values that exist are galena_nativesilver,
        /// quartz_nativegold, quartz_nativesilver (host-mineral + precious-metal pairs -- the
        /// game's own ItemOre.cs:29-31/168 always resolves these to the metal half via
        /// Split('_')[1] / prefix-strip, e.g. for smashing ore into nuggets) and
        /// olivine_peridot (NOT a two-substance pairing -- "peridot" is just the gem-quality
        /// name for olivine, not a separate material, and has no entry of its own in the
        /// material map). Preferring the second segment when it resolves, falling back to the
        /// first otherwise, satisfies all four real cases without hardcoding them individually.
        /// </summary>
        private static string ResolveJointMaterial(string type)
        {
            int underscoreIdx = type.IndexOf('_');
            if (underscoreIdx < 0)
                return type;

            string first = type.Substring(0, underscoreIdx);
            string second = type.Substring(underscoreIdx + 1);

            return MaterialToAsset(second) != null ? second : first;
        }

        private static string MaterialToAsset(string material)
        {
            switch (material)
            {
                case "galena": return "oresong-galena";
                case "lignite":
                case "bituminouscoal":
                case "anthracite": return "oresong-coal";
                case "nativegold": return "oresong-nativegold";
                case "nativesilver": return "oresong-nativesilver";
                case "nativecopper":
                case "malachite": return "oresong-nativecopper";
                case "sphalerite":
                case "bismuthinite": return "oresong-sphalerite";
                case "cassiterite": return "oresong-cassiterite";
                case "chromite":
                case "ilmenite": return "oresong-chromite";
                case "limonite":
                case "hematite":
                case "magnetite": return "oresong-iron";
                case "quartz":
                case "diamond":
                case "emerald":
                case "olivine": return "oresong-quartzgem";
                default: return null;
            }
        }

        private static float ComputeGradeGain(Block block)
        {
            switch (block.Variant["grade"])
            {
                case "poor": return 0.55f;
                case "medium": return 0.75f;
                case "rich": return 0.9f;
                case "bountiful": return 1.0f;
            }

            switch (block.Variant["potential"])
            {
                case "low": return 0.6f;
                case "medium": return 0.8f;
                case "high": return 1.0f;
            }

            return 0.75f;
        }
    }
}
