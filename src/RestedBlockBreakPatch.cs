using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on Block.OnBlockBroken. Drains Rested a flat amount per block
    /// broken, any material — deliberately not restricted to Ore/Stone like
    /// MiningSpeedPatch/OreYieldPatch, since Rested is universal (no trait gate) and
    /// "block break" in the brief means any block.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
    public static class RestedBlockBreakPatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, IPlayer byPlayer)
        {
            // ── Guard 1: null player (outside try, mirrors ClimbSpeedPatch/ClimbSaturationPatch) ──
            if (byPlayer?.Entity == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableRested)
                    return;

                if (world.Side != EnumAppSide.Server)
                    return;

                byPlayer.Entity.GetBehavior<RestedBehavior>()?.Drain((float)cfg.RestedBlockBreakDrain);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in RestedBlockBreakPatch: {0}", ex);
                }
            }
        }
    }
}
