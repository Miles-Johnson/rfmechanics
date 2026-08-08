using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Lets Elves climb standing tree trunks ("log-grown"-prefixed block code — vanilla's own
    /// convention for a living tree vs. a cut/placed log, see BlockLog.cs and
    /// RFTreeProximityBehavior.cs, which already uses the same prefix check) as if they were
    /// ladders, at plain vanilla ladder speed (climbUpSpeed/climbDownSpeed, no separate cost or
    /// curve unlike ClimbSpeedPatch/ClimbSaturationPatch for dwarves).
    ///
    /// Two postfixes, both on EntityBehaviorControlledPhysics (confirmed via reflection against
    /// the installed VSEssentials.dll that EntityBehaviorPlayerPhysics does not override either
    /// method, so patching the base class covers players — unlike OnPhysicsTick, which it does
    /// override; see BranchyLeavesPassthroughPatch.SimPhysicsPrefix for that lesson):
    ///
    /// - MotionAndCollisionPostfix does the actual work: it's the one that makes movement
    ///   happen. Per-tick call order (see EntityBehaviorPlayerPhysics.SimPhysics) is
    ///   MotionAndCollision (applies gravity/other PModules to pos.Motion) then ApplyTests
    ///   (vanilla's own ladder-climb detection + its climb-speed motion.Y correction + finally
    ///   ApplyTerrainCollision, which is what actually consumes pos.Motion to move the entity).
    ///   A first version of this patch only postfixed ApplyTests, imitating vanilla's own
    ///   controls.IsClimbing flag — that ran too late in the same tick to affect that tick's
    ///   ApplyTerrainCollision call, AND because PModuleGravity.Applicable() skips gravity
    ///   whenever controls.IsClimbing is true, the stale flag left over from one tick's postfix
    ///   suppressed gravity at the START of the *next* tick's MotionAndCollision (before vanilla
    ///   ever got a chance to re-derive it) without ever running the actual climb-speed
    ///   correction — observed in-game as sticking to the tree with no gravity, but no vertical
    ///   movement either. Postfixing MotionAndCollision instead runs before
    ///   ApplyTerrainCollision consumes pos.Motion, so the correction takes effect the same
    ///   tick, and it sets pos.Motion.Y to an absolute value rather than relying on
    ///   controls.IsClimbing to have suppressed gravity in advance, so it doesn't matter whether
    ///   gravity already ran this tick or not.
    /// - ApplyTestsPostfix is cosmetic only now (controls.IsClimbing / entity.ClimbingOnFace /
    ///   ClimbingOnCollBox, for animation state) and only fires when vanilla's own scan found
    ///   nothing, so a real ladder — including one built onto a tree — still takes priority and
    ///   behaves exactly as vanilla intends.
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
                    // Hover in place: cancel out whatever gravity/other modules already applied
                    // to Motion.Y this tick (unlike vanilla, we run after those modules, not
                    // instead of them, so we can't rely on PModuleGravity.Applicable() having
                    // skipped gravity for us).
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

        /// <summary>
        /// Guard chain matching every other rfmechanics elf gate (see
        /// BranchyLeavesPassthroughPatch.GenerateCollisionBoxListPostfix,
        /// RFTreeProximityBehavior.IsElf): config toggle, EntityPlayer, vanilla's own
        /// species-level CanClimb gate, characterClass null check (load-bearing — HasTrait
        /// returns true for a null class by default), then the trait check itself.
        /// </summary>
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

        /// <summary>
        /// Simplified re-implementation of BehaviorControlledPhysics.ApplyTests's own
        /// horizontal-neighbor climb scan, substituting "log-grown" for Block.IsClimbable.
        /// tmpPos.IterateHorizontalOffsets(i) is cumulative (each call offsets from tmpPos's
        /// current value, not from a fixed origin) — it must be seeded at floor(pos) once
        /// before the loop and never reset inside it, matching vanilla's own usage exactly.
        /// </summary>
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
