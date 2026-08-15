using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics.BugRace
{
    /// <summary>
    /// RE-HOMED, NOT DELETED (goblin extraction) -- earmarked for a future bug race.
    /// Registration is disabled: the [HarmonyPatch]/[HarmonyPostfix] attributes below are
    /// commented out (Harmony's PatchAll discovers classes purely via those attributes, no
    /// separate registration call exists to disable instead). Logic is untouched, ready to
    /// reactivate or lift into the bug-race mod.
    /// Harmony postfix on Block.OnBlockBroken: when a goblin breaks a block, converts every
    /// face-adjacent Soil/Sand/Gravel neighbor to its spit-packed variant, rather than
    /// replicating BlockBehaviorUnstableFalling's own would-it-fall decision tree. Shovel-gated
    /// (only a held shovel suppresses conversion, bare hands and any other held item still
    /// trigger it) rather than "any held item," since picking up something unrelated mid-dig
    /// silently flipping the goblin's fall behavior read as a bug, not a deliberate choice.
    /// UNVERIFIED race-window assumption: running as a postfix means this executes after
    /// vanilla's SetBlock(0, pos)/OnNeighbourBlockChange chain but before
    /// BlockBehaviorUnstableFalling.TryFalling's deferred EnqueueMainThreadTask spawn, which
    /// re-checks the block instance at that position and no-ops if it changed -- converting the
    /// neighbor here before that closure drains should suppress the fall, but this rests on an
    /// unconfirmed assumption about engine tick-processing order. If smoke testing shows
    /// sand/gravel still falls, the documented fallback is switching this to a PREFIX instead,
    /// landing the conversion before vanilla's SetBlock fires neighbor updates at all.
    /// </summary>
    // [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]   // DISABLED -- see banner comment above
    public static class GoblinSpitPackingPatch
    {
        private static bool loggedException = false;

        // [HarmonyPostfix]   // DISABLED -- see banner comment above
        public static void Postfix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinSpitPacking)
                    return;

                if (world.Side != EnumAppSide.Server)
                    return;

                // No class = not a goblin; overrides HasTrait's null-class-returns-true default.
                string charClass = byPlayer.Entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (!charSys.HasTrait(byPlayer, cfg.GoblinTraitCode))
                    return;

                EnumTool? heldTool = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Tool;
                if (heldTool == EnumTool.Shovel)
                    return;

                IBlockAccessor blockAccessor = world.BlockAccessor;
                BlockFacing[] faces = BlockFacing.ALLFACES;
                for (int i = 0; i < faces.Length; i++)
                {
                    BlockPos neighborPos = pos.AddCopy(faces[i]);
                    Block neighbor = blockAccessor.GetBlock(neighborPos);
                    if (neighbor?.Code?.Path == null)
                        continue;

                    if (neighbor.RequiredMiningTier != 0)
                        continue;

                    Block converted = ResolveConversionTarget(world, neighbor);
                    if (converted == null)
                        continue;

                    blockAccessor.SetBlock(converted.BlockId, neighborPos);
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in GoblinSpitPackingPatch: {0}", ex);
                }
            }
        }

        /// <summary>
        /// Path-prefix gate (not just material) so already-converted spit-packed blocks and
        /// unrelated Soil-material blocks (farmland, mudbrick, peat, rawclay, etc.) are never
        /// re-converted or mis-converted. Every qualifying family converts to its own dedicated
        /// spitpacked{family} blocktype, sourced from the vanilla stonepath tile set and
        /// recolored per sub-variant. See notes/goblin-dig-materials-handover.md for the
        /// canonical family/bucket table this method, RFGoblinTunnelBehavior, and the dig-bonus
        /// attachment list must all stay in sync with.
        /// dirtygravel and sandwavy each get their own dedicated blocktype rather than reusing
        /// plain gravel's/sand's, so their moisture-type/rock suffix carries through instead of
        /// approximating with a fixed rock pick.
        /// </summary>
        /// <summary>Shared with RFGoblinTunnelBehavior's tunnel-ceiling check as the single
        /// source of truth for "is this diggable earth", instead of a second hand-maintained
        /// Code.Path prefix list.</summary>
        internal static bool IsGoblinEarth(IWorldAccessor world, Block block)
        {
            if (block?.Code?.Path == null) return false;
            if (block.RequiredMiningTier != 0) return false;
            // "domain == rfmechanics" stands in for "already spit-packed" since the only blocks
            // this mod's domain owns today are the spitpacked{family} blocktypes -- silently
            // wrong if rfmechanics ever registers a non-spit-packed block; replace with an
            // explicit spit-packed prefix check if that happens.
            if (block.Code.Domain == "rfmechanics") return true;
            return ResolveConversionTarget(world, block) != null;
        }

        private static Block ResolveConversionTarget(IWorldAccessor world, Block neighbor)
        {
            string path = neighbor.Code.Path;

            if (neighbor.BlockMaterial == EnumBlockMaterial.Soil)
            {
                if (path.StartsWith("soil-"))
                {
                    string[] parts = path.Split('-');
                    if (parts.Length < 2) return null;
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedsoil-" + parts[1]));
                }
                if (path.StartsWith("bonysoil"))
                {
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedbonysoil"));
                }
                if (path.StartsWith("cob-"))
                {
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedcob"));
                }
                if (path.StartsWith("forestfloor-"))
                {
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedforestfloor"));
                }
                if (path.StartsWith("muddygravel"))
                {
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedmuddygravel"));
                }
                if (path.StartsWith("sludgygravel"))
                {
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedsludgygravel"));
                }
                return null;
            }

            if (neighbor.BlockMaterial == EnumBlockMaterial.Sand)
            {
                if (path.StartsWith("sand-"))
                {
                    string rock = path.Substring("sand-".Length);
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedsand-" + rock));
                }
                if (path.StartsWith("sandwavy-"))
                {
                    string rock = path.Substring("sandwavy-".Length);
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedsandwavy-" + rock));
                }
                return null;
            }

            if (neighbor.BlockMaterial == EnumBlockMaterial.Gravel)
            {
                if (path.StartsWith("gravel-"))
                {
                    string rock = path.Substring("gravel-".Length);
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackedgravel-" + rock));
                }
                if (path.StartsWith("dirtygravel-"))
                {
                    string moistureType = path.Substring("dirtygravel-".Length);
                    return world.GetBlock(new AssetLocation("rfmechanics", "spitpackeddirtygravel-" + moistureType));
                }
                return null;
            }

            return null;
        }
    }
}
