using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on EntityBehaviorControlledPhysics.SetProperties.
    /// Scales only climbDownSpeed (the Jump/ascend field) by (1 + ClimbSpeedFactor)
    /// for dwarf players. climbUpSpeed (the Sneak/descend field) is left at its
    /// base JSON value — ascent-only, matching the saturation-drain mechanic.
    /// Field names are inverted from their function: Sneak (descend) reads
    /// climbUpSpeed, Jump (ascend) reads climbDownSpeed. See ClimbCollideAssistPatch
    /// for the second ascent method (walking into a ladder without Jump), which
    /// bypasses these fields entirely via Block.OnEntityCollide.
    ///
    /// Does NOT re-invoke SetProperties from our code — SetProperties calls
    /// SetModules which appends physics modules without clearing, causing
    /// duplicate PModuleGravity and PModuleMotionDrag that break movement
    /// for all players. Instead, ApplyClimbScale writes the scaled fields
    /// directly from JSON base values, which is idempotent.
    /// 
    /// EntityPlayer guard is the literal first statement in the method body
    /// because EntityBehaviorControlledPhysics exists on all mobs, not just
    /// players. Every other statement lives inside the try block — this patch
    /// runs during entity construction (SpawnEntity_internal) where the
    /// player-entity link may not yet exist.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorControlledPhysics), nameof(EntityBehaviorControlledPhysics.SetProperties))]
    public static class ClimbSpeedPatch
    {
        private static bool loggedException = false;

        // Per-entity-instance guards. Entries die with the entity (no
        // persistence, no cleanup). Not stored in entity.Attributes or
        // WatchedAttributes — those persist across sessions and would break
        // on rejoin.
        private static readonly ConditionalWeakTable<Entity, object> retryScheduled = new();
        private static readonly ConditionalWeakTable<Entity, object> listenerRegistered = new();
        private static readonly object sentinel = new();

        [HarmonyPostfix]
        public static void Postfix(
            EntityBehaviorControlledPhysics __instance,
            EntityProperties properties,
            JsonObject attributes)
        {
            // ── Guard 1: EntityPlayer only (literal first statement, outside try) ──
            if (__instance.entity is not EntityPlayer playerEntity)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                // 2. Master toggle
                if (!cfg.EnableClimbSpeed)
                    return;

                Entity entity = __instance.entity;

                // 3. Player lookup — link may not exist during construction
                IPlayer iplayer = entity.World.PlayerByUid(playerEntity.PlayerUID);
                if (iplayer?.Entity == null)
                {
                    // Entity-link race: schedule one-shot retry and register
                    // a characterClass listener. Both may fire; ApplyClimbScale
                    // is idempotent (recomputes from JSON base values).
                    float factor = (float)(1.0 + cfg.ClimbSpeedFactor);
                    ScheduleRetry(__instance, attributes, factor);
                    RegisterClassListener(__instance, attributes, factor);
                    return;
                }

                // 4. Class guard: no class = not a dwarf (overrides HasTrait's
                //    null-class-returns-true default). Register listener so we
                //    re-apply when the class is assigned during character creation.
                string charClass = entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                {
                    float factor = (float)(1.0 + cfg.ClimbSpeedFactor);
                    RegisterClassListener(__instance, attributes, factor);
                    return;
                }

                // 5. Trait check
                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                    return;

                // ── Apply speed scaling (ascent-only: climbDownSpeed) ──
                float factor2 = (float)(1.0 + cfg.ClimbSpeedFactor);
                ApplyClimbScale(__instance, attributes, factor2);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in ClimbSpeedPatch: {0}", ex);
                }
                // Leave climb speeds unchanged on exception
            }
        }

        /// <summary>
        /// Apply climb speed scaling from JSON base values.
        /// Idempotent: recomputes from base rather than multiplying the current
        /// value, so it is safe to call repeatedly (retry, listener, rejoin).
        /// Ascent-only: climbUpSpeed (Sneak/descend) is reset to its unscaled
        /// base value, never multiplied by factor.
        /// </summary>
        private static void ApplyClimbScale(
            EntityBehaviorControlledPhysics behavior,
            JsonObject attributes,
            float factor)
        {
            float baseUp = attributes["climbUpSpeed"].AsFloat(0.07f);
            float baseDown = attributes["climbDownSpeed"].AsFloat(0.035f);
            behavior.climbUpSpeed = baseUp;
            behavior.climbDownSpeed = baseDown * factor;
        }

        /// <summary>
        /// One-shot retry for the entity-link race (player.Entity null during
        /// construction). Guarded by a ConditionalWeakTable — genuinely one-shot
        /// per entity instance, no persistence across sessions.
        /// Re-runs the guard chain before applying scale.
        /// </summary>
        private static void ScheduleRetry(
            EntityBehaviorControlledPhysics behavior,
            JsonObject attributes,
            float factor)
        {
            Entity entity = behavior.entity;

            if (!retryScheduled.TryAdd(entity, sentinel))
                return;

            entity.World.RegisterCallback(dt =>
            {
                try
                {
                    // Verify the entity is still valid
                    if (entity.World == null || entity.State == EnumEntityState.Despawned)
                        return;

                    // Re-run guard chain
                    if (entity is not EntityPlayer retryPlayer)
                        return;

                    var cfg = RFMechanicsModSystem.Config;
                    if (cfg == null)
                        return;

                    if (!cfg.EnableClimbSpeed)
                        return;

                    IPlayer iplayer = entity.World.PlayerByUid(retryPlayer.PlayerUID);
                    if (iplayer?.Entity == null)
                        return;

                    string charClass = entity.WatchedAttributes.GetString("characterClass");
                    if (string.IsNullOrEmpty(charClass))
                        return;

                    var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                    if (charSys == null)
                        return;

                    if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                        return;

                    ApplyClimbScale(behavior, attributes, factor);
                }
                catch (Exception ex)
                {
                    if (!loggedException)
                    {
                        loggedException = true;
                        RFMechanicsModSystem.Api?.Logger?.Warning(
                            "[rfmechanics] Exception in ClimbSpeedPatch retry: {0}", ex);
                    }
                }
            }, 2000);
        }

        /// <summary>
        /// Register a modified listener on the entity's characterClass attribute.
        /// When the class is assigned (or changed), re-apply scaling through the
        /// guard chain + ApplyClimbScale path.
        /// Guarded by a ConditionalWeakTable — one registration per entity
        /// instance, no persistence across sessions.
        /// </summary>
        private static void RegisterClassListener(
            EntityBehaviorControlledPhysics behavior,
            JsonObject attributes,
            float factor)
        {
            Entity entity = behavior.entity;

            if (!listenerRegistered.TryAdd(entity, sentinel))
                return;

            entity.WatchedAttributes.RegisterModifiedListener("characterClass", () =>
            {
                try
                {
                    // Re-run guard chain
                    if (entity is not EntityPlayer listenerPlayer)
                        return;

                    var cfg = RFMechanicsModSystem.Config;
                    if (cfg == null)
                        return;

                    if (!cfg.EnableClimbSpeed)
                        return;

                    IPlayer iplayer = entity.World.PlayerByUid(listenerPlayer.PlayerUID);
                    if (iplayer?.Entity == null)
                        return;

                    string charClass = entity.WatchedAttributes.GetString("characterClass");
                    if (string.IsNullOrEmpty(charClass))
                        return;

                    var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                    if (charSys == null)
                        return;

                    if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                        return;

                    ApplyClimbScale(behavior, attributes, factor);
                }
                catch (Exception ex)
                {
                    if (!loggedException)
                    {
                        loggedException = true;
                        RFMechanicsModSystem.Api?.Logger?.Warning(
                            "[rfmechanics] Exception in ClimbSpeedPatch listener: {0}", ex);
                    }
                }
            });
        }
    }
}