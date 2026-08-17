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
    ///
    /// Crouch-to-descend: for every elf, boxes whose top is strictly above foot Y are stripped
    /// UNLESS the entity is also sneaking, in which case every branchy box is stripped (full
    /// passthrough). entity.Controls.Sneak is read rather than ServerControls -- Controls is the
    /// locally-driven, zero-latency copy on the client and is aliased to the same object as
    /// ServerControls on the server (EntityAgent.Initialize), so one read is correct on both
    /// sides without a side branch.
    ///
    /// Canopy standing: unconditional for elves (no attunement gate -- see
    /// elf-attunement-removal-report.md). A scaffolding-mod reference (third-party, decompiled --
    /// see notes/race-mechanics/elf-scaffolding-diagnostic-findings.md) was evaluated and
    /// rejected as a template: it injects whole-block boxes gated on vanilla's ladder-climbing
    /// control state, which doesn't guarantee horizontal passthrough. What's implemented here
    /// instead is a same-postfix, removal-only extension: only boxes whose top is strictly above
    /// the entity's foot Y are stripped, unless the entity is sneaking, which forces a full strip.
    /// This is an exclusion, not a selection -- push-out physics always rests the entity exactly
    /// on top of whichever box currently supports it (that box's Y2 == footY, never &gt; footY),
    /// so it's never the one a jump or a step strips.
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

                // IsElf already applies the exact EntityPlayer/characterClass-null/HasTrait guard
                // chain RefreshElfCache runs on its own slow tick -- reading it here (a field on
                // the entity's own attached behavior) replaces walking the trait system on this
                // per-substep hot path.
                var identity = entity.GetBehavior<ElfIdentityBehavior>();
                if (identity == null || !identity.IsElf) return;

                // Matches how CollisionTester.ApplyTerrainCollision itself derives the entity's
                // world-space box (entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z))
                // -- CollisionBox.Y1 is 0 for players via Entity.SetCollisionBox, but add it
                // rather than assume, since that's what "foot level" means to the engine itself.
                double footY = entity.Pos.Y + entity.CollisionBox.Y1;

                // Canopy standing is unconditional for elves (identity already gated above) --
                // only sneak forces a full strip.
                bool sneaking = entity is EntityAgent agent && agent.Controls.Sneak;
                bool retainFootSupport = !sneaking;

                FilterBranchyLeaves(__instance.CollisionBoxList, retainFootSupport, footY, cfg.LogLeafStandingBoxCounts);
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
        ///
        /// retainFootSupport false: unchanged E3.3 behaviour, every branchy box stripped.
        /// retainFootSupport true (E3.4): a branchy box is stripped only if its world-space top
        /// (cuboids[read].Y2) is strictly above footY -- boxes at or below foot level are always
        /// kept, so horizontal passthrough at body height never regresses and the box a resting
        /// or landing elf is standing on (Y2 == footY exactly, once push-out has resolved) is
        /// never the one removed.
        /// </summary>
        private static void FilterBranchyLeaves(CachedCuboidListFaster list, bool retainFootSupport, double footY, bool logCounts)
        {
            int write = 0;
            int stripped = 0;
            int kept = 0;
            for (int read = 0; read < list.Count; read++)
            {
                Block block = list.blocks[read];
                bool isBranchy = block?.Code?.Path != null && block.Code.Path.Contains("branchy");
                if (isBranchy)
                {
                    bool strip = !retainFootSupport || list.cuboids[read].Y2 > footY;
                    if (strip) { stripped++; continue; }
                    kept++;
                }

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

            if (logCounts && (stripped > 0 || kept > 0))
            {
                RFMechanicsModSystem.Api?.Logger?.Notification(
                    "[rfmechanics] leaf standing: stripped={0} kept={1} footY={2:F3}", stripped, kept, footY);
            }
        }
    }
}
