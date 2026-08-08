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
    /// natural rock (code-prefix whitelist below) as if they were ladders, at plain vanilla
    /// ladder speed -- no saturation cost (settled at Part B review: vanilla's own baseline
    /// hunger drain, EntityBehaviorHunger.SlowTick, has no IsClimbing-specific term at all --
    /// climbing already costs exactly the same as any other non-idle Controls state in
    /// vanilla, so shipping Goblin climbing hunger-free doesn't introduce an asymmetry against
    /// vanilla's own ladder climbing, which is also free. Only Dwarf's ClimbSaturationPatch is
    /// a deliberately ADDED cost specific to dwarves).
    ///
    /// A parallel class to TreeClimbingPatch (Elf), not an extension of it -- see
    /// notes/goblin-phase-g2-partA-report.md A2 for why: raw rock is never vanilla
    /// Climbable-flagged (unlike ladders), so the dwarf-style ClimbSpeedPatch/
    /// ClimbCollideAssistPatch extension-of-vanilla-ladder-detection shape would never fire
    /// for it -- only TreeClimbingPatch's self-contained-scan shape (own block scan, own
    /// MotionAndCollision postfix, ApplyTests postfix only for cosmetic state when vanilla's
    /// own scan found nothing) generalizes to non-ladder-flagged block types.
    ///
    /// Two independent match groups, each behind its own toggle (tree climbing and rock
    /// climbing can be enabled/disabled independently): "log-grown" (EnableGoblinTreeClimbing)
    /// and the raw-rock whitelist (EnableGoblinRockClimbing, GoblinRockClimbCodePrefixes --
    /// four prefixes by default: rock-, crackedrock-, meteorite-, stalagsection-, NOT a single
    /// "rock-" StartsWith check -- see Part A report A2 for why cracked rock/meteorite/
    /// stalagmite are natural but don't share the "rock-" prefix, and why worked stone
    /// (cobblestone/polished/stonebricks/quartz/etc.) must stay excluded).
    ///
    /// Same two-postfix shape as TreeClimbingPatch (see that file's doc comment for the full
    /// reasoning on why MotionAndCollision must be postfixed, not just ApplyTests).
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
                    // Hover in place: cancel out whatever gravity/other modules already applied
                    // to Motion.Y this tick, matching TreeClimbingPatch's own reasoning (we run
                    // after those modules, not instead of them).
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

        /// <summary>
        /// Guard chain matching every other rfmechanics race gate: EntityPlayer check, vanilla's
        /// own species-level CanClimb gate, characterClass null check (load-bearing -- HasTrait
        /// returns true for a null class by default), then the trait check itself. Does NOT
        /// gate on either EnableGoblinTreeClimbing/EnableGoblinRockClimbing here -- those are
        /// per-match-group toggles checked inside TryFindGoblinClimb, since a goblin with one
        /// toggle off and the other on should still get partial climbing.
        /// </summary>
        private static bool TryGetGoblin(EntityBehaviorControlledPhysics behavior, out Entity? entity)
        {
            entity = null;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (!cfg.EnableGoblinTreeClimbing && !cfg.EnableGoblinRockClimbing) return false;

            Entity candidate = behavior.entity;
            if (candidate is not EntityPlayer player) return false;
            if (candidate.Properties.CanClimb != true) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            if (!charSys.HasTrait(iplayer, cfg.GoblinTraitCode)) return false;

            entity = candidate;
            return true;
        }

        /// <summary>
        /// Same horizontal-neighbor scan shape as TreeClimbingPatch.TryFindTreeClimb, but
        /// checking against two independently-toggleable prefix groups instead of one fixed
        /// prefix. "log-grown" (tree, same convention as Elf) and the raw-rock whitelist
        /// (config-driven list, not hardcoded -- see GoblinRockClimbCodePrefixes) are checked
        /// per block, first match wins.
        /// </summary>
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
                    string path = inBlock?.Code?.Path;
                    if (path == null) continue;

                    bool matches = (checkTrees && path.StartsWith("log-grown")) || (checkRock && MatchesAnyPrefix(path, rockPrefixes));
                    if (!matches) continue;

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
