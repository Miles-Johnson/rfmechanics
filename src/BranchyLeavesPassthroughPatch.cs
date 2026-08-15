using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfixes on CachingCollisionTester so branchy leaves ("leavesbranchy" -- solid-
    /// sided unlike regular leaves) don't block Elf movement, without touching
    /// Block.GetCollisionBoxes, any block subclass, or movement integration directly.
    /// ConditionalWeakTable binds each [ThreadStatic] tester instance to the entity it's
    /// currently testing (Harmony can't add a field to an existing type); AssignToEntity runs
    /// synchronously right before that entity's own collision test, so there's no cross-entity
    /// reentrancy within a thread's tick. Filter runs as a postfix on
    /// GenerateCollisionBoxList, strictly after CollisionBoxList is populated and before
    /// ApplyTerrainCollision's push-out.
    /// </summary>
    [HarmonyPatch]
    public static class BranchyLeavesPassthroughPatch
    {
        private static readonly ConditionalWeakTable<CachingCollisionTester, Entity> testerEntity = new();
        private static bool loggedException = false;

        // protected internal static, not accessible from this assembly directly.
        private static readonly System.Reflection.FieldInfo CollisionTesterField =
            AccessTools.Field(typeof(PhysicsBehaviorBase), "collisionTester");

        [HarmonyPatch(typeof(CachingCollisionTester), nameof(CachingCollisionTester.AssignToEntity))]
        [HarmonyPostfix]
        public static void AssignToEntityPostfix(CachingCollisionTester __instance, PhysicsBehaviorBase entityPhysics)
        {
            if (entityPhysics?.entity == null) return;
            testerEntity.AddOrUpdate(__instance, entityPhysics.entity);
        }

        /// <summary>
        /// EntityBehaviorPlayerPhysics overrides OnPhysicsTick and routes through its own
        /// SimPhysics instead of calling base.OnPhysicsTick() (the method that calls
        /// AssignToEntity), so for players AssignToEntity never fires and the testerEntity
        /// binding is never populated; this prefix restores that missing call for players.
        /// </summary>
        [HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), nameof(EntityBehaviorPlayerPhysics.SimPhysics))]
        [HarmonyPrefix]
        public static void SimPhysicsPrefix(EntityBehaviorPlayerPhysics __instance, EntityPos pos)
        {
            try
            {
                (CollisionTesterField.GetValue(null) as CachingCollisionTester)?.AssignToEntity(__instance, pos.Dimension);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in BranchyLeavesPassthroughPatch.SimPhysicsPrefix: {0}", ex);
                }
            }
        }

        [HarmonyPatch(typeof(CachingCollisionTester), "GenerateCollisionBoxList", new[]
        {
            typeof(IBlockAccessor), typeof(double), typeof(double), typeof(double), typeof(float), typeof(float), typeof(int)
        })]
        [HarmonyPostfix]
        public static void GenerateCollisionBoxListPostfix(CachingCollisionTester __instance)
        {
            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableBranchyLeavesPassthrough) return;

                if (!testerEntity.TryGetValue(__instance, out Entity? entity)) return;

                // No class = not an elf; overrides HasTrait's null-class-returns-true default.
                if (entity is not EntityPlayer player) return;

                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null) return;

                if (!charSys.HasTrait(iplayer, cfg.ElfTraitCode)) return;

                FilterBranchyLeaves(__instance.CollisionBoxList);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in BranchyLeavesPassthroughPatch: {0}", ex);
                }
            }
        }

        /// <summary>
        /// cuboids[] holds Cuboidd as a mutable *reference* type reused across ticks in place
        /// (CachedCuboidListFaster's populatedSize optimization), so compacting must copy values
        /// through the existing object at the destination slot -- copying the reference itself
        /// would alias two slots and a later in-place mutation would silently corrupt both
        /// (observed in-game as falling through solid ground after passing through branchy leaves).
        /// </summary>
        private static void FilterBranchyLeaves(CachedCuboidListFaster list)
        {
            int write = 0;
            for (int read = 0; read < list.Count; read++)
            {
                Block block = list.blocks[read];
                bool isBranchy = block?.Code?.Path != null && block.Code.Path.Contains("branchy");
                if (isBranchy) continue;

                if (write != read)
                {
                    Cuboidd src = list.cuboids[read];
                    list.cuboids[write].Set(src.X1, src.Y1, src.Z1, src.X2, src.Y2, src.Z2);
                    list.positions[write] = list.positions[read];
                    list.blocks[write] = list.blocks[read];
                }
                write++;
            }
            list.Count = write;
        }
    }
}
