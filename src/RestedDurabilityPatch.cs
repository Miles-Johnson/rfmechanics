using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on CollectibleObject.DamageItem. Scales durability loss by the
    /// player's current Rested value: rested = less loss, tired = more loss.
    ///
    /// amount is almost always 1 per call, so a naive `(int)(amount * mul)` truncates to
    /// the same 1 for any bonus under 50% and produces zero observable effect. Instead this
    /// converts to a float, applies the multiplier, and stochastically rounds via
    /// GameMath.RoundRandom (the same mechanism vanilla uses for fractional drop quantities
    /// in BlockDropItemStack, per OreYieldPatch's own comments) — so a sub-1 average loss
    /// still shows up as an occasional skipped point of damage rather than never.
    ///
    /// Off by default (EnableRestedDurability) — this is subtask 3e, lower priority and
    /// may slip per the brief.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem))]
    public static class RestedDurabilityPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(IWorldAccessor world, Entity byEntity, ref int amount)
        {
            // ── Guard 1: EntityPlayer only (outside try, mirrors ClimbSpeedPatch/ClimbSaturationPatch) ──
            if (byEntity is not EntityPlayer)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableRestedDurability)
                    return;

                if (world.Side != EnumAppSide.Server)
                    return;

                var restedBhv = byEntity.GetBehavior<RestedBehavior>();
                if (restedBhv == null)
                    return;

                float rested = restedBhv.Rested;
                float multiplier = 1f - (rested - 0.5f) * 2f * (float)cfg.RestedDurabilityBonus;
                multiplier = GameMath.Clamp(multiplier, 0f, 2f);

                amount = GameMath.RoundRandom(world.Rand, amount * multiplier);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in RestedDurabilityPatch: {0}", ex);
                }
                // amount unchanged on exception
            }
        }
    }
}
