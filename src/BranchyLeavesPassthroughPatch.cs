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
    /// Two Harmony postfixes on CachingCollisionTester so branchy leaves (game code prefix
    /// "leavesbranchy" — solid-sided unlike regular leaves, see BlockLeaves.cs) don't block
    /// Elf movement, without any signature change to Block.GetCollisionBoxes, any block
    /// subclass, or movement integration itself.
    ///
    /// PhysicsBehaviorBase.collisionTester is a single [ThreadStatic] CachingCollisionTester
    /// reused across every entity ticked on that thread. AssignToEntity(entityPhysics, dim)
    /// is called synchronously immediately before that entity's own collision test
    /// (EntityBehaviorControlledPhysics.SetState / EntityBehaviorPassivePhysics), so "which
    /// entity does this tester instance currently belong to" is well-defined at the moment
    /// GenerateCollisionBoxList runs afterward in the same call chain — there is no
    /// reentrancy across entities within a single thread's tick. A ConditionalWeakTable
    /// keyed on the tester instance records that binding, since Harmony cannot add a real
    /// field to an existing type; this also naturally handles one binding per physics
    /// thread (client + server) without us managing thread-locals ourselves.
    ///
    /// The filter runs as a postfix on GenerateCollisionBoxList — after CollisionBoxList is
    /// populated from the block query, before ApplyTerrainCollision's pushOutX/Y/Z calls —
    /// so this is strictly post-query, pre-push-out.
    /// </summary>
    [HarmonyPatch]
    public static class BranchyLeavesPassthroughPatch
    {
        private static readonly ConditionalWeakTable<CachingCollisionTester, Entity> testerEntity = new();
        private static bool loggedException = false;

        // PhysicsBehaviorBase.collisionTester is protected internal static, not directly
        // accessible from this class/assembly - read reflectively via AccessTools.
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
        /// EntityBehaviorPlayerPhysics (the behavior that actually drives real players,
        /// client and server both) overrides OnPhysicsTick and routes through its own
        /// SimPhysics instead of calling base.OnPhysicsTick() - the one method that calls
        /// AssignToEntity. So for player entities specifically, AssignToEntity never fires,
        /// the testerEntity binding above is never populated, and
        /// GenerateCollisionBoxListPostfix bails at the "no binding" guard on every call,
        /// regardless of trait or block. This prefix restores the missing call so
        /// AssignToEntityPostfix runs for players the same as it already does for every
        /// other controlled/passive-physics entity.
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

                // Class guard: no class = not an elf (overrides HasTrait's
                // null-class-returns-true default). Same guard chain shape as
                // ClimbSpeedPatch/ClimbCollideAssistPatch.
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
                // Leave CollisionBoxList unfiltered on exception
            }
        }

        /// <summary>
        /// In-place removal of branchy-leaf cuboids from an already-populated
        /// CachedCuboidListFaster. Identification mirrors vanilla's own convention
        /// (ItemAxe.cs: block.Code.Path.Contains("branchy")) rather than inventing a new
        /// one. CachedCuboidListFaster exposes no RemoveAt, so this compacts the three
        /// parallel arrays (cuboids/positions/blocks) in place and shrinks Count — the same
        /// shape as a manual List&lt;T&gt;.RemoveAll. Safe to call on an unchanged list
        /// (e.g. when GenerateCollisionBoxList's own early-return skipped a rebuild this
        /// call) since branchy entries removed on a prior call are simply already gone.
        ///
        /// cuboids[] holds Cuboidd instances, which are a mutable *reference* type -
        /// CachedCuboidListFaster.Add reuses slots across ticks by mutating them via
        /// .Set(...) in place (its populatedSize optimization avoids reallocating when
        /// Count shrinks then regrows). Compacting by copying the element reference
        /// (list.cuboids[write] = list.cuboids[read]) would alias two slots onto the same
        /// object; a later in-place mutation of one slot (e.g. a subsequent rebuild
        /// overwriting an unrelated block's cuboid) would then silently corrupt the other
        /// slot's data too - observed in-game as the player intermittently falling through
        /// solid ground shortly after passing through branchy leaves. Copying values
        /// through the existing object at the destination slot avoids the aliasing.
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
