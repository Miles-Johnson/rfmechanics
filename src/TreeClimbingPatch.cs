using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Lets Elves climb standing tree trunks ("log-grown"-prefixed, vanilla's convention for a
    /// living tree vs. a cut/placed log) as if they were ladders, at plain vanilla ladder speed.
    /// Two postfixes on EntityBehaviorControlledPhysics, not EntityBehaviorPlayerPhysics: that
    /// subclass overrides OnPhysicsTick but not these two methods, so patching the base class
    /// covers players (see BranchyLeavesPassthroughPatch.SimPhysicsPrefix for the OnPhysicsTick case).
    /// MotionAndCollisionPostfix does the real work, not ApplyTestsPostfix: an earlier version
    /// postfixed only ApplyTests (mirroring vanilla's IsClimbing flag), but that ran too late to
    /// affect that tick's ApplyTerrainCollision, and PModuleGravity.Applicable() skipping gravity
    /// off the stale flag from the previous tick's postfix suppressed gravity without ever
    /// running the climb-speed correction -- observed in-game as sticking to the tree with no
    /// gravity and no vertical movement. Postfixing MotionAndCollision runs before
    /// ApplyTerrainCollision consumes pos.Motion and sets an absolute value, so it's independent
    /// of whether gravity already ran that tick.
    /// ApplyTestsPostfix is cosmetic only now (animation state), firing only when vanilla's own
    /// scan found nothing, so a real ladder (even one built onto a tree) still takes priority.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorControlledPhysics))]
    public static class TreeClimbingPatch
    {
        private static bool loggedException = false;

        [HarmonyPatch(nameof(EntityBehaviorControlledPhysics.MotionAndCollision))]
        [HarmonyPostfix]
        public static void MotionAndCollisionPostfix(EntityBehaviorControlledPhysics __instance, EntityPos pos, EntityControls controls, float dt)
        {
            try
            {
                if (!TryGetElf(__instance, out Entity? entity)) return;
                if (!TryFindTreeClimb(entity!, pos, out BlockFacing? _, out Cuboidf? _)) return;

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
                    // Cancels whatever gravity/other modules already applied to Motion.Y this tick -- we run after those modules, not instead of them.
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
            if (controls.IsClimbing) return; // vanilla already found something to climb

            try
            {
                if (!TryGetElf(__instance, out Entity? entity)) return;
                if (!TryFindTreeClimb(entity!, pos, out BlockFacing? face, out Cuboidf? collBox)) return;

                controls.IsClimbing = true;
                entity!.ClimbingOnFace = face;
                entity.ClimbingOnCollBox = collBox;
            }
            catch (Exception ex)
            {
                LogExceptionOnce(ex);
            }
        }

        // charClass null-check is load-bearing: HasTrait returns true for a null class by default.
        private static bool TryGetElf(EntityBehaviorControlledPhysics behavior, out Entity? entity)
        {
            entity = null;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableTreeClimbing) return false;

            Entity candidate = behavior.entity;
            if (candidate is not EntityPlayer player) return false;
            if (candidate.Properties.CanClimb != true) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            if (!charSys.HasTrait(iplayer, cfg.ElfTraitCode)) return false;

            entity = candidate;
            return true;
        }

        /// <summary>LANDMINE: tmpPos.IterateHorizontalOffsets(i) is cumulative (each call offsets
        /// from tmpPos's current value, not a fixed origin) -- it must be seeded at floor(pos)
        /// once before the loop and never reset inside it.</summary>
        private static bool TryFindTreeClimb(Entity entity, EntityPos pos, out BlockFacing? face, out Cuboidf? collBox)
        {
            face = null;
            collBox = null;

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
                    if (inBlock?.Code?.Path == null || !inBlock.Code.Path.StartsWith("log-grown")) continue;

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

        private static void LogExceptionOnce(Exception ex)
        {
            if (!loggedException)
            {
                loggedException = true;
                RFMechanicsModSystem.Api?.Logger?.Warning("[rfmechanics] Exception in TreeClimbingPatch: {0}", ex);
            }
        }
    }
}
