using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>E1.9: eager census invalidation on felling. Block.OnBlockBroken is postfix-only
    /// elsewhere in this codebase, and by postfix time the block at pos is already air -- a
    /// Prefix capturing the pre-break block ID into __state is the only way to know what was
    /// removed. Harmony auto-threads a same-named/same-typed __state parameter from Prefix to
    /// Postfix, no extra plumbing needed.
    ///
    /// The null-byPlayer guard living in Prefix, before the block lookup, is the entire bypass
    /// filter for fire/WorldEdit/sapling-growth/worldgen (confirmed: only axe-felling routes
    /// through Block.OnBlockBroken with a non-null player, per the traced call chain
    /// ItemAxe.OnBlockBrokenWith -> BlockAccessorBase.BreakBlock -> Block.OnBlockBroken).
    /// Explosions are excluded by a stronger, separately-confirmed fact, not this guard:
    /// ServerMain.CreateExplosion calls Block.OnBlockExploded (a different method entirely),
    /// never Block.OnBlockBroken -- confirmed against reference/decompiled/1.22/VintagestoryLib/
    /// Vintagestory.Server/ServerMain.cs:3217. This patch's target method is simply never
    /// reached by an explosion, independent of what byPlayer would have been.
    ///
    /// No ChunkColumnUnloaded subscription: the persisted census already survives unload/reload
    /// via IMapChunk.SetModdata + MarkDirty, and TTL/generation are the only two staleness
    /// sources this design has -- both already lifecycle-independent. An unload handler here
    /// would have no invalidation work to do.</summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
    public static class ElfForestCensusInvalidationPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, out int __state)
        {
            __state = -1;
            if (byPlayer?.Entity == null) return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableElfAttunement) return;
                if (world.Side != EnumAppSide.Server) return;

                Block block = world.BlockAccessor.GetBlock(pos);
                if (block != null) __state = block.Id;
            }
            catch (Exception ex)
            {
                LogOnce(ex, "Prefix");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, BlockPos pos, int __state)
        {
            if (__state < 0) return;

            try
            {
                if (!ElfAttunementBlockWhitelist.IsLogGrown(__state)) return;
                ElfForestCensus.InvalidateColumn(world, pos);
            }
            catch (Exception ex)
            {
                LogOnce(ex, "Postfix");
            }
        }

        private static void LogOnce(Exception ex, string where)
        {
            if (!loggedException)
            {
                loggedException = true;
                RFMechanicsModSystem.Api?.Logger?.Warning("[rfmechanics] Exception in ElfForestCensusInvalidationPatch.{0}: {1}", where, ex);
            }
        }
    }
}
