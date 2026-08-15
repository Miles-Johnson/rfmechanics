using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on Block.GetDrops. Scales dropQuantityMultiplier by (1 + oreBonus) for
    /// dwarf players mining ore at depth.
    /// Vanilla applies oreDropRate in BlockOre.OnBlockBroken BEFORE GetDrops runs, so this is
    /// multiplicative on top of it: final = base * oreDropRate * (1 + oreBonus).
    /// Filtered on BlockMaterial.Ore rather than `is BlockOre` because gem ores are plain Block
    /// with material Ore. Explosion drops pass byPlayer null and are excluded by the null guard.
    /// Unlike MiningSpeedPatch (Ore and Stone), this is Ore-only -- stone blocks produce no ore
    /// drops to scale; intentional, not an inconsistency.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
    public static class OreYieldPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer byPlayer,
            ref float dropQuantityMultiplier)
        {
            if (__instance.BlockMaterial != EnumBlockMaterial.Ore)
                return;

            if (byPlayer?.Entity == null)
                return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null)
                return;

            if (!cfg.EnableOreCurve)
                return;

            if (world.Side != EnumAppSide.Server)
                return;

            // No class = not a dwarf; overrides HasTrait's null-class-returns-true default.
            string charClass = byPlayer.Entity.WatchedAttributes.GetString("characterClass");
            if (charClass == null)
                return;

            var charSys = RFMechanicsModSystem.Api.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null)
                return;

            if (!charSys.HasTrait(byPlayer, cfg.DwarfTraitCode))
                return;

            try
            {
                int y = pos.Y;
                int seaLevel = world.SeaLevel;
                double oreBonus = RFMechanicsModSystem.ComputeOreBonus(y, seaLevel);
                dropQuantityMultiplier *= (float)(1.0 + oreBonus);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in OreYieldPatch: {0}", ex);
                }
            }
        }
    }
}