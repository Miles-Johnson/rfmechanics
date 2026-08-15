using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on CollectibleObject.GetMiningSpeed. Applies the depth/altitude mining
    /// speed curve for dwarf players and a flat stone-mining slowdown for goblins in one
    /// postfix, sequential trait checks (a player is only ever one race, no double-application risk).
    /// LANDMINE: GetMiningSpeed is virtual -- a modded tool overriding it without calling
    /// base.GetMiningSpeed silently bypasses this patch (no vanilla subclass does).
    /// Vanilla's body already reads Stats.GetBlended("miningSpeedMul") before this postfix
    /// runs, so any other writer of that stat composes multiplicatively -- no conflict.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMiningSpeed))]
    public static class MiningSpeedPatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(
            ref float __result,
            IItemStack itemstack,
            BlockSelection blockSel,
            Block block,
            IPlayer forPlayer)
        {
            if (forPlayer?.Entity == null)
                return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null)
                return;

            // Mirrors vanilla's own Ore/Stone check -- only apply either race's modifier to those materials.
            if (blockSel?.Position == null || block == null)
                return;

            EnumBlockMaterial material = block.GetBlockMaterial(
                RFMechanicsModSystem.Api.World.BlockAccessor, blockSel.Position);
            if (material != EnumBlockMaterial.Ore && material != EnumBlockMaterial.Stone)
                return;

            // No class = not any race; overrides HasTrait's null-class-returns-true default.
            string charClass = forPlayer.Entity.WatchedAttributes.GetString("characterClass");
            if (charClass == null)
                return;

            var charSys = RFMechanicsModSystem.Api.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null)
                return;

            if (cfg.EnableMiningCurve && charSys.HasTrait(forPlayer, cfg.DwarfTraitCode))
            {
                try
                {
                    int y = blockSel.Position.Y;
                    int seaLevel = RFMechanicsModSystem.Api.World.SeaLevel;
                    double bonus = RFMechanicsModSystem.ComputeBonus(y, seaLevel);
                    __result *= (float)(1.0 + bonus);
                }
                catch (Exception ex)
                {
                    LogExceptionOnce(ex);
                }
                return;
            }

            if (cfg.EnableGoblinStonePenalty && charSys.HasTrait(forPlayer, cfg.GoblinTraitCode))
            {
                try
                {
                    __result *= (float)cfg.GoblinStoneMiningFactor;
                }
                catch (Exception ex)
                {
                    LogExceptionOnce(ex);
                }
            }
        }

        private static void LogExceptionOnce(Exception ex)
        {
            if (!loggedException)
            {
                loggedException = true;
                RFMechanicsModSystem.Api?.Logger?.Error(
                    "[rfmechanics] Exception in MiningSpeedPatch: {0}", ex);
            }
        }
    }
}