using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>Diagnostic-only write path for RFMechanicsConfig.EnableChunkScarTracker --
    /// increments a moddata counter, no gameplay effect. Prefix/postfix __state shape mirrors
    /// the archived ElfForestCensusInvalidationPatch: the pre-break Block is captured in Prefix
    /// since it's already gone by Postfix time in the normal case; Postfix re-checks the block
    /// at pos against __state before counting, so a break some other system cancelled doesn't
    /// get recorded just because OnBlockBroken ran to completion. The null-byPlayer guard
    /// filters worldgen/fire/sapling-growth (explosions never reach Block.OnBlockBroken at all).
    ///
    /// Player-placed detection for logs: log.json's "type" variantgroup is ["grown","placed"]
    /// -- breaking a grown log always drops the "-placed-" item form, so gating on "-grown-"
    /// (same mechanism ElfLeafDropPatch uses for leaves) is what excludes a log wall (200
    /// placed logs stacked and broken) from the scar count. It does not, and is not meant to,
    /// distinguish worldgen origin from a player-planted sapling that grew naturally -- the
    /// scar measures standing wood removed from the column, not who planted the seed, so a
    /// grown tree felled counts either way.</summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
    public static class ChunkScarBreakPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, out Block? __state)
        {
            __state = null;
            if (byPlayer?.Entity == null) return;
            if (world.Side != EnumAppSide.Server) return; // IMapChunk moddata is server-only (unsynced to client)

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableChunkScarTracker) return;

                __state = world.BlockAccessor.GetBlock(pos);
            }
            catch (Exception ex)
            {
                LogOnce(ex, "Prefix");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, BlockPos pos, Block? __state)
        {
            if (__state?.Code == null) return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableChunkScarTracker) return;

                // Postfix runs even if something else (a block behavior, a protection mod)
                // cancelled the break inside OnBlockBroken -- confirm the block at pos actually
                // changed before counting it, rather than trusting that the method ran to
                // completion means it succeeded.
                if (world.BlockAccessor.GetBlock(pos).Id == __state.Id) return;

                // "survival namespace" == vanilla's "game" domain; blocktypes ship under
                // assets/survival/ but resolve at runtime to AssetLocation domain "game".
                if (__state.Code.Domain != GlobalConstants.DefaultDomain) return;

                string path = __state.Code.Path;

                if (MatchesAny(path, cfg.ChunkScarLogBlockCodePrefixes) && path.Contains("-grown-"))
                {
                    ChunkScarTracker.RecordBreak(world, pos, ChunkScarTracker.ScarKey);
                    return;
                }

                if (MatchesAny(path, cfg.ChunkScarLeafBlockCodePrefixes))
                {
                    ChunkScarTracker.RecordBreak(world, pos, ChunkScarTracker.LeafScarKey);
                }
            }
            catch (Exception ex)
            {
                LogOnce(ex, "Postfix");
            }
        }

        /// <summary>/rfscar selftest's double-patch check: counts this Harmony instance's own
        /// prefixes/postfixes on Block.OnBlockBroken (owner-filtered, so other mods' patches on
        /// the same method don't skew the count). Expected 1/1 -- the PatchAll double-patch bug
        /// this tracker uncovered would show as 2/2, catchable before trusting any number
        /// collected in that session rather than after.</summary>
        public static void CountOwnPatches(string harmonyId, out int prefixCount, out int postfixCount)
        {
            var original = AccessTools.Method(typeof(Block), nameof(Block.OnBlockBroken));
            var patches = Harmony.GetPatchInfo(original);
            prefixCount = patches?.Prefixes.Count(p => p.owner == harmonyId) ?? 0;
            postfixCount = patches?.Postfixes.Count(p => p.owner == harmonyId) ?? 0;
        }

        private static bool MatchesAny(string path, string[] prefixes)
        {
            if (prefixes == null) return false;
            foreach (string prefix in prefixes)
            {
                if (!string.IsNullOrEmpty(prefix) && path.StartsWith(prefix)) return true;
            }
            return false;
        }

        private static void LogOnce(Exception ex, string where)
        {
            if (!loggedException)
            {
                loggedException = true;
                RFMechanicsModSystem.Api?.Logger?.Warning("[rfmechanics] Exception in ChunkScarBreakPatch.{0}: {1}", where, ex);
            }
        }
    }
}
