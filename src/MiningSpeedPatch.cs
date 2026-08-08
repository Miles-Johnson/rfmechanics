using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on CollectibleObject.GetMiningSpeed.
    /// Applies the depth/altitude mining speed curve for dwarf players, and (2026-08-06,
    /// Phase G2) a flat stone-mining slowdown for goblin players -- both live in one postfix,
    /// sequential trait checks, same coexistence shape as FallDamagePatch's Elf/Goblin fall
    /// damage reduction ("a player is only ever one race, so no double-application risk").
    ///
    /// NOTE: GetMiningSpeed is virtual. Any modded tool that overrides it
    /// without calling base.GetMiningSpeed will silently bypass this patch.
    /// No vanilla subclass overrides this method (verified against 1.21.5 decompile).
    ///
    /// NOTE: GetMiningSpeed's vanilla body already reads Stats.GetBlended("miningSpeedMul")
    /// before this postfix runs, so RestedBehavior's Stats.Set("miningSpeedMul", "rested", ...)
    /// composes multiplicatively with this patch's dwarf bonus — both apply, no conflict.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMiningSpeed))]
    public static class MiningSpeedPatch
    {
        private static bool loggedException = false;

        // HarmonyPostfix: __result is the return value from the original method.
        // We multiply it by (1 + bonus) for dwarf players mining Ore or Stone, and/or by
        // GoblinStoneMiningFactor for goblin players (same material gate, mutually exclusive
        // since a player is only ever one race).
        [HarmonyPostfix]
        public static void Postfix(
            ref float __result,
            IItemStack itemstack,
            BlockSelection blockSel,
            Block block,
            IPlayer forPlayer)
        {
            // ── Early exits (ordered by cost) ──

            // 1. Null player guard
            if (forPlayer?.Entity == null)
                return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null)
                return;

            // 3. Material gate: mirror vanilla's Ore/Stone check at CollectibleObject.cs:621-624
            //    Only apply either race's stone-mining modifier to Ore and Stone blocks.
            if (blockSel?.Position == null || block == null)
                return;

            EnumBlockMaterial material = block.GetBlockMaterial(
                RFMechanicsModSystem.Api.World.BlockAccessor, blockSel.Position);
            if (material != EnumBlockMaterial.Ore && material != EnumBlockMaterial.Stone)
                return;

            // 4. Class guard: no class = not any race (overrides HasTrait's null-class-returns-true default)
            string charClass = forPlayer.Entity.WatchedAttributes.GetString("characterClass");
            if (charClass == null)
                return;

            var charSys = RFMechanicsModSystem.Api.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null)
                return;

            // ── Dwarf: depth/altitude bonus ──
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
                    // Leave __result untouched on exception
                }
                return;
            }

            // ── Goblin: flat stone-mining penalty ──
            if (cfg.EnableGoblinStonePenalty && charSys.HasTrait(forPlayer, cfg.GoblinTraitCode))
            {
                try
                {
                    __result *= (float)cfg.GoblinStoneMiningFactor;
                }
                catch (Exception ex)
                {
                    LogExceptionOnce(ex);
                    // Leave __result untouched on exception
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