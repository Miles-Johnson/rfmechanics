using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Lets Goblins climb standing tree trunks ("log-grown"-prefixed, same as Elf) and raw
    /// natural rock (whitelist below) as if they were ladders, at plain vanilla ladder speed --
    /// no saturation cost, since vanilla's own baseline hunger drain has no IsClimbing-specific
    /// term at all (only Dwarf's ClimbSaturationPatch is a deliberately ADDED cost).
    /// Parallel to TreeClimbingPatch (Elf), not an extension of it: raw rock is never vanilla
    /// Climbable-flagged, so the dwarf-style extension-of-vanilla-ladder-detection shape never
    /// fires for it -- only TreeClimbingPatch's self-contained-scan shape generalizes.
    /// Tree and rock climbing are independently toggleable; the rock whitelist
    /// (GoblinRockClimbCodePrefixes) covers raw rock plus rough worked-stone/masonry families,
    /// see the config's doc comment. Chiseled logs/rock (BlockEntityMicroBlock) are climbable
    /// via the same fallback TreeClimbingPatch.IsClimbableLog uses for Elves -- see
    /// IsClimbableGoblinBlock.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorControlledPhysics))]
    public static class GoblinClimbingPatch
    {
        private static bool loggedException = false;

        [HarmonyPatch(nameof(EntityBehaviorControlledPhysics.MotionAndCollision))]
        [HarmonyPostfix]
        public static void MotionAndCollisionPostfix(EntityBehaviorControlledPhysics __instance, EntityPos pos, EntityControls controls, float dt)
        {
            try
            {
                if (!TryGetGoblin(__instance, out Entity? entity)) return;
                if (!TryFindGoblinClimb(entity!, pos, out BlockFacing? _, out Cuboidf? _)) return;

                if (controls.Jump)
                {
                    pos.Motion.Y = __instance.climbDownSpeed * dt * 60f; // vanilla naming is inverted: Jump = ascend
                }
                else if (controls.Sneak)
                {
                    pos.Motion.Y = Math.Max(-__instance.climbUpSpeed, pos.Motion.Y - __instance.climbUpSpeed); // Sneak = descend
                }
                else
                {
                    // Cancels whatever gravity/other modules already applied to Motion.Y this tick -- runs after those modules, not instead of them.
                    pos.Motion.Y = 0;
                }
            }
            catch (Exception ex)
            {
                LogExceptionOnce(ex);
            }
        }

        [HarmonyPatch(nameof(EntityBehaviorControlledPhysics.ApplyTests))]
        [HarmonyPostfix]
        public static void ApplyTestsPostfix(EntityBehaviorControlledPhysics __instance, EntityPos pos, EntityControls controls)
        {
            if (controls.IsClimbing) return; // vanilla (or TreeClimbingPatch) already found something to climb

            try
            {
                if (!TryGetGoblin(__instance, out Entity? entity)) return;
                if (!TryFindGoblinClimb(entity!, pos, out BlockFacing? face, out Cuboidf? collBox)) return;

                controls.IsClimbing = true;
                entity!.ClimbingOnFace = face;
                entity.ClimbingOnCollBox = collBox;
            }
            catch (Exception ex)
            {
                LogExceptionOnce(ex);
            }
        }

        /// <summary>charClass null check is load-bearing (HasTrait returns true for a null class
        /// by default). Does not gate on EnableGoblinTreeClimbing/EnableGoblinRockClimbing here --
        /// those are per-match-group toggles inside TryFindGoblinClimb, so one toggle off and the
        /// other on still gets partial climbing.</summary>
        private static bool TryGetGoblin(EntityBehaviorControlledPhysics behavior, out Entity? entity)
        {
            entity = null;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (!cfg.EnableGoblinTreeClimbing && !cfg.EnableGoblinRockClimbing) return false;

            Entity candidate = behavior.entity;
            if (candidate is not EntityPlayer player) return false;
            if (candidate.Properties.CanClimb != true) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (!RaceTraits.HasTrait(iplayer, cfg.GoblinTraitCode)) return false;

            entity = candidate;
            return true;
        }

        /// <summary>Same horizontal-neighbor scan shape as TreeClimbingPatch.TryFindTreeClimb, checking two independently-toggleable prefix groups instead of one fixed prefix.</summary>
        private static bool TryFindGoblinClimb(Entity entity, EntityPos pos, out BlockFacing? face, out Cuboidf? collBox)
        {
            face = null;
            collBox = null;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;

            bool checkTrees = cfg.EnableGoblinTreeClimbing;
            bool checkRock = cfg.EnableGoblinRockClimbing;
            if (!checkTrees && !checkRock) return false;

            string[] rockPrefixes = cfg.GoblinRockClimbCodePrefixes ?? Array.Empty<string>();

            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            float touchDistance = entity.Properties.ClimbTouchDistance;
            int height = (int)Math.Ceiling(entity.CollisionBox.Y2);
            Cuboidd entityBox = new Cuboidd().SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
            BlockPos tmpPos = new BlockPos(pos.Dimension);
            int baseY = (int)pos.Y;

            tmpPos.Set((int)pos.X, baseY, (int)pos.Z);
            for (int i = 0; i < 4; i++)
            {
                tmpPos.IterateHorizontalOffsets(i);
                for (int dy = 0; dy < height; dy++)
                {
                    tmpPos.Y = baseY + dy;
                    Block inBlock = blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Solid);
                    if (!IsClimbableGoblinBlock(entity.World, inBlock, tmpPos, checkTrees, checkRock, rockPrefixes)) continue;

                    Cuboidf[] collisionBoxes = inBlock.GetCollisionBoxes(blockAccessor, tmpPos);
                    if (collisionBoxes == null) continue;

                    for (int j = 0; j < collisionBoxes.Length; j++)
                    {
                        double distance = entityBox.ShortestDistanceFrom(collisionBoxes[j], tmpPos);
                        if (distance < touchDistance)
                        {
                            face = BlockFacing.HORIZONTALS[i];
                            collBox = collisionBoxes[j];
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Chiseling replaces a block's own Code.Path with the generic "chiseledblock", so
        /// a carved log or rock face no longer matches its prefix directly -- same
        /// BlockEntityMicroBlock.BlockIds fallback as TreeClimbingPatch.IsClimbableLog, generalized
        /// to cover both the tree and rock match groups here.</summary>
        private static bool IsClimbableGoblinBlock(IWorldAccessor world, Block block, BlockPos pos, bool checkTrees, bool checkRock, string[] rockPrefixes)
        {
            if (MatchesPath(block?.Code?.Path, checkTrees, checkRock, rockPrefixes)) return true;

            BlockEntity blockEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity is BlockEntityMicroBlock micro && micro.BlockIds != null)
            {
                foreach (int id in micro.BlockIds)
                {
                    if (MatchesPath(world.GetBlock(id)?.Code?.Path, checkTrees, checkRock, rockPrefixes)) return true;
                }
            }

            return false;
        }

        private static bool MatchesPath(string? path, bool checkTrees, bool checkRock, string[] rockPrefixes)
        {
            if (path == null) return false;
            return (checkTrees && path.StartsWith("log-grown")) || (checkRock && MatchesAnyPrefix(path, rockPrefixes));
        }

        private static bool MatchesAnyPrefix(string path, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!string.IsNullOrEmpty(prefixes[i]) && path.StartsWith(prefixes[i])) return true;
            }
            return false;
        }

        private static void LogExceptionOnce(Exception ex)
        {
            if (!loggedException)
            {
                loggedException = true;
                RFMechanicsModSystem.Api?.Logger?.Warning("[rfmechanics] Exception in GoblinClimbingPatch: {0}", ex);
            }
        }
    }
}
