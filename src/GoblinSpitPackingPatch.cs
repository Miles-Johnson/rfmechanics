using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on Block.OnBlockBroken. When a goblin breaks a block, converts every
    /// face-adjacent Soil/Sand/Gravel neighbor to its spit-packed variant (packeddirt for
    /// Soil; the new spitpackedsand-{rock}/spitpackedgravel-{rock} blocktypes for Sand/
    /// Gravel), rather than replicating BlockBehaviorUnstableFalling's own would-it-actually-
    /// fall decision tree -- see notes/goblin-phase-g2-partA-report.md A6. Shovel-gated
    /// (ActiveHotbarSlot's item has EnumTool.Shovel): only a held shovel suppresses
    /// conversion -- bare hands AND any other held item (weapon, torch, unrelated junk in the
    /// hotbar slot) still trigger it. Originally gated on "any held item at all," but that was
    /// janky in practice: picking up something completely unrelated mid-dig would silently
    /// flip the goblin from "nothing falls" to "normal UnstableFalling physics," which read as
    /// a bug rather than a deliberate choice. Shovel-only makes the tradeoff legible -- you
    /// have to deliberately equip the tool that's *for* loose material to get loose material.
    /// See notes/goblin-dig-materials-handover.md for the full design history.
    ///
    /// Race-window rationale (A6): running as a POSTFIX means this necessarily executes after
    /// vanilla's own Block.OnBlockBroken body -- including the SetBlock(0, pos) call and the
    /// synchronous OnNeighbourBlockChange/TryFalling chain it triggers for neighbors -- but
    /// BEFORE the falling-entity spawn, which BlockBehaviorUnstableFalling.TryFalling defers
    /// via ICoreServerAPI.Event.EnqueueMainThreadTask. That deferred closure re-reads the
    /// block at the position when it eventually runs and only spawns EntityBlockFalling if the
    /// position still holds the SAME block instance that triggered the fall check. Converting
    /// the neighbor synchronously here, before that closure drains, makes the re-check fail
    /// (the position now holds a different Block instance) and the entity spawn is skipped.
    /// NOT yet verified in-game -- flagged in Part A as resting on an assumption about engine
    /// tick-processing order that wasn't independently confirmed beyond reading this one call
    /// site. If smoke testing shows sand/gravel still falls near a goblin's break, the
    /// documented fallback is to switch this to a PREFIX on Block.OnBlockBroken instead, so
    /// the conversion lands before vanilla's own SetBlock(0, pos) fires neighbor updates at
    /// all (avoiding the race window entirely rather than trying to win it).
    ///
    /// Ships ungated by any primer/rot state (G3 wires that in later) -- goblin trait check
    /// only, EnableGoblinSpitPacking master toggle.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
    public static class GoblinSpitPackingPatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            // ── Guard 1: null player (outside try, mirrors RestedBlockBreakPatch) ──
            if (byPlayer?.Entity == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinSpitPacking)
                    return;

                if (world.Side != EnumAppSide.Server)
                    return;

                // Class guard: no class = not a goblin (overrides HasTrait's
                // null-class-returns-true default).
                string charClass = byPlayer.Entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (!charSys.HasTrait(byPlayer, cfg.GoblinTraitCode))
                    return;

                // Shovel gate: only a held shovel suppresses spit-packing -- bare hands and
                // any other held item still trigger conversion. See
                // notes/goblin-dig-materials-handover.md.
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
        /// unrelated Soil-material blocks (worked/resource/special-purpose ones like farmland,
        /// mudbrick, peat, rawclay -- see notes/goblin-dig-materials-handover.md's exclusion
        /// list) are never re-converted or mis-converted -- only the plain world-generated
        /// soil-{fertility}-{grasscoverage}/sand-{rock}/gravel-{rock} families, plus
        /// sandwavy-{rock}, dirtygravel-{moisture}-{type}, bonysoil(-{layer}), cob-{grass},
        /// forestfloor-{grass}, muddygravel, and sludgygravel, qualify. Every family converts
        /// to its own dedicated spitpacked{family} blocktype -- all now sourced from the same
        /// vanilla stonepath tile set, recolored per sub-variant (rock/fertility/moisture-type)
        /// to match that sub-variant's own vanilla color. See handover doc ("Third
        /// resolution") for the recolor pipeline and the full canonical family/bucket table
        /// that this method, RFGoblinTunnelBehavior's GoblinDiggableEarthCodePrefixes, and the
        /// dig-bonus attachment list must all stay in sync with.
        ///
        /// dirtygravel and sandwavy each get their own dedicated blocktype
        /// (spitpackeddirtygravel, spitpackedsandwavy) rather than reusing plain
        /// gravel's/sand's -- this retires the previous DirtyGravelFallbackRock fixed-rock
        /// tradeoff (dirtygravel now carries its own moisture-type suffix through instead of
        /// approximating with a permanent "granite" pick).
        /// </summary>
        /// <summary>
        /// Single source of truth for "is this diggable earth" for goblin purposes, shared
        /// with RFGoblinTunnelBehavior's tunnel-ceiling check instead of that behavior
        /// maintaining its own parallel Code.Path prefix list (RFMechanicsConfig's former
        /// GoblinDiggableEarthCodePrefixes -- removed G2.1, see
        /// notes/goblin-dig-materials-handover.md for the drift it caused: 5 of 10
        /// spit-packed families were missing from that list). True if the block is either a
        /// valid conversion source per ResolveConversionTarget, or is itself an
        /// already-converted rfmechanics-owned spit-packed block (which ResolveConversionTarget
        /// itself never matches -- its whole point is recognizing pre-conversion blocks).
        /// RequiredMiningTier == 0 is checked here too (mirrors the Postfix's own guard) so
        /// callers get a complete predicate without re-deriving that guard themselves.
        /// </summary>
        internal static bool IsGoblinEarth(IWorldAccessor world, Block block)
        {
            if (block?.Code?.Path == null) return false;
            if (block.RequiredMiningTier != 0) return false;
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
