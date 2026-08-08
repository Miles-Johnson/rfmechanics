using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on Block.GetDrops.
    /// Scales the dropQuantityMultiplier by (1 + oreBonus) for dwarf players
    /// mining ore blocks at depth, so dropped stacks are multiplied by the
    /// ore yield curve (takes effect via vanilla RoundRandom in BlockDropItemStack).
    /// 
    /// Vanilla applies oreDropRate in BlockOre.OnBlockBroken BEFORE GetDrops is
    /// called. This prefix scales dropQuantityMultiplier further, so the two are
    /// multiplicative: final = base * oreDropRate * (1 + oreBonus).
    /// Dwarves currently have no oreDropRate trait value. If one is ever added
    /// to a dwarf trait, both multipliers stack.
    /// 
    /// Filtered on BlockMaterial.Ore rather than `is BlockOre` because gem ores
    /// are plain Block with material Ore. Explosion drops pass byPlayer null and
    /// are excluded by the null guard, matching vanilla oreDropRate behavior.
    /// 
    /// NOTE: Phase 1's MiningSpeedPatch gates on Ore AND Stone (mirroring vanilla).
    /// Phase 2 is Ore-only because stone blocks produce no ore drops to scale.
    /// This is intentional, not an inconsistency.
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
            // ── Material gate (first, by request): only apply to Ore blocks ──
            if (__instance.BlockMaterial != EnumBlockMaterial.Ore)
                return;

            // ── Early exits (ordered by cost) ──

            // 1. Null player guard
            if (byPlayer?.Entity == null)
                return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null)
                return;

            // 2. Master toggle
            if (!cfg.EnableOreCurve)
                return;

            // 3. Server-side only — drops are only spawned server-side
            if (world.Side != EnumAppSide.Server)
                return;

            // 4. Class guard: no class = not a dwarf (overrides HasTrait's null-class-returns-true default)
            string charClass = byPlayer.Entity.WatchedAttributes.GetString("characterClass");
            if (charClass == null)
                return;

            // 5. Trait check
            var charSys = RFMechanicsModSystem.Api.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null)
                return;

            if (!charSys.HasTrait(byPlayer, cfg.DwarfTraitCode))
                return;

            // ── Compute and apply ore bonus ──
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
                // Leave dropQuantityMultiplier untouched on exception
            }
        }
    }
}