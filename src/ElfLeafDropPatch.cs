using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on Block.GetDrops. Appends a self-drop (the placed/obtainable form of
    /// the leaves block just harvested) to vanilla's own drops for Elf players breaking
    /// leaves-*/leavesbranchy-* blocks -- APPENDS, does not replace __result, so elves keep
    /// vanilla's existing treeseed/stick drops too (ruling at Part B review).
    ///
    /// Same hook OreYieldPatch already patches (Block.GetDrops), different gate (Leaves vs.
    /// Ore material) and different shape (postfix appending to __result vs. prefix scaling
    /// dropQuantityMultiplier) -- no conflict, confirmed BlockLeaves does not override
    /// GetDrops so the base-class patch reaches it cleanly. See
    /// notes/goblin-phase-g2-partA-report.md A5 -- this directly closes G1's open
    /// branchy-leaves ingredient-sourcing gap (leaves never dropped themselves before this).
    ///
    /// Grown-to-placed conversion: world leaf blocks are leaves-grown{0-7}-{wood}
    /// (tree-attached); the obtainable/inventory form is leaves-placed-{wood} (same for the
    /// leavesbranchy family). The dropped stack is always constructed as "{family}-placed-
    /// {wood}" regardless of which grown stage was harvested, matching G1's own recipe wood-
    /// species passthrough shape.
    /// </summary>
    [HarmonyPatch]
    public static class ElfLeafDropPatch
    {
        private static bool loggedException = false;

        /// <summary>True for the full duration of a server-side ItemAxe.OnBlockBrokenWith call.
        /// A held-tool check alone misses leaves broken after the axe's own durability runs out
        /// mid-sweep (ItemAxe's flag2 gate on wood only -- leaves keep popping regardless), so
        /// this wraps the whole felling call instead. Client-side calls never touch this: the
        /// GetDrops postfix below already returns before consulting it off the server side, so
        /// there's no cross-thread race between the client-prediction and server-authoritative
        /// calls that both invoke OnBlockBrokenWith.</summary>
        private static bool inServerAxeFelling = false;

        [HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith))]
        [HarmonyPrefix]
        public static void OnBlockBrokenWithPrefix(IWorldAccessor world)
        {
            if (world.Side == EnumAppSide.Server) inServerAxeFelling = true;
        }

        [HarmonyPatch(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith))]
        [HarmonyPostfix]
        public static void OnBlockBrokenWithPostfix(IWorldAccessor world)
        {
            if (world.Side == EnumAppSide.Server) inServerAxeFelling = false;
        }

        [HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
        [HarmonyPostfix]
        public static void Postfix(
            Block __instance,
            ref ItemStack[] __result,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer byPlayer,
            float dropQuantityMultiplier)
        {
            // ── Guard 1: null player ──
            if (byPlayer?.Entity == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableElfLeafGathering)
                    return;

                // Drops only spawn server-side (mirrors OreYieldPatch's own reasoning).
                if (world.Side != EnumAppSide.Server)
                    return;

                // Axe felling breaks every leaf in the canopy along with the trunk; no bonus for
                // any of it. Living harvest (Phase 4) is the intended better-yield path for
                // elves, not axes.
                if (inServerAxeFelling)
                    return;

                // Material/code gate: leaves-* or leavesbranchy-* only.
                string path = __instance?.Code?.Path;
                if (path == null)
                    return;

                bool isBranchy = path.StartsWith("leavesbranchy-");
                bool isPlainLeaves = !isBranchy && path.StartsWith("leaves-");
                if (!isBranchy && !isPlainLeaves)
                    return;

                // Naturally-generated leaves only (G2.1) -- "leaves-placed-{wood}"/
                // "leavesbranchy-placed-{wood}" is the obtainable/replanted form (see the
                // grown-to-placed conversion below); re-breaking a placed leaf block must not
                // grant a second bonus drop, or elves could compound leaves indefinitely by
                // planting and re-harvesting. Every grown-stage variant's Code.Path contains
                // "-grown"/"-grown1".."-grown7", never "-placed-", so this single substring
                // check is sufficient -- confirmed both leaves/normal.json and
                // leavesbranchy/branchy.json share the identical {type: [grown..grown7,
                // placed]} x {wood} variant cross-product (13 species, no skipVariants), so
                // there's no species where this gate could misfire either direction.
                if (path.Contains("-placed-"))
                    return;

                if (!RaceTraits.HasTrait(byPlayer, cfg.ElfTraitCode))
                    return;

                // ── Construct the "{family}-placed-{wood}" self-drop ──
                int lastDash = path.LastIndexOf('-');
                if (lastDash < 0 || lastDash == path.Length - 1)
                    return;

                string wood = path.Substring(lastDash + 1);
                string family = isBranchy ? "leavesbranchy" : "leaves";
                Block placedBlock = world.GetBlock(new AssetLocation(__instance.Code.Domain, family + "-placed-" + wood));
                if (placedBlock == null)
                    return;

                ItemStack selfDrop = new ItemStack(placedBlock, 1);

                if (__result == null)
                {
                    __result = new ItemStack[] { selfDrop };
                }
                else
                {
                    ItemStack[] appended = new ItemStack[__result.Length + 1];
                    Array.Copy(__result, appended, __result.Length);
                    appended[__result.Length] = selfDrop;
                    __result = appended;
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in ElfLeafDropPatch: {0}", ex);
                }
                // Leave __result untouched on exception
            }
        }
    }
}
