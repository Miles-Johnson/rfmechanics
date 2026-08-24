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
    /// Harmony postfix on EntityBehaviorControlledPhysics.SetProperties. Scales only
    /// climbDownSpeed by (1 + ClimbSpeedFactor) for dwarf players; climbUpSpeed stays at its
    /// base JSON value (ascent-only). LANDMINE: field names are inverted from their function --
    /// Sneak (descend) reads climbUpSpeed, Jump (ascend) reads climbDownSpeed. See
    /// ClimbCollideAssistPatch for the second ascent method (walking into a ladder without
    /// Jump), which bypasses these fields entirely.
    /// Never re-invokes SetProperties from here: it calls SetModules, which appends physics
    /// modules without clearing, causing duplicate PModuleGravity/PModuleMotionDrag that break
    /// movement for all players -- ApplyClimbScale instead writes the scaled fields directly
    /// from JSON base values, which is idempotent.
    /// EntityPlayer guard is the first statement outside the try, since
    /// EntityBehaviorControlledPhysics exists on all mobs; this patch runs during entity
    /// construction, where the player-entity link may not yet exist.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorControlledPhysics), nameof(EntityBehaviorControlledPhysics.SetProperties))]
    public static class ClimbSpeedPatch
    {
        private static bool loggedException = false;

        // Not stored in entity.Attributes/WatchedAttributes -- those persist across sessions and
        // would wrongly suppress the retry/listener on rejoin.
        private static readonly ConditionalWeakTable<Entity, object> retryScheduled = new();
        private static readonly ConditionalWeakTable<Entity, object> listenerRegistered = new();
        private static readonly object sentinel = new();

        [HarmonyPostfix]
        public static void Postfix(
            EntityBehaviorControlledPhysics __instance,
            EntityProperties properties,
            JsonObject attributes)
        {
            if (__instance.entity is not EntityPlayer playerEntity)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableClimbSpeed)
                    return;

                Entity entity = __instance.entity;

                IPlayer iplayer = entity.World.PlayerByUid(playerEntity.PlayerUID);
                if (iplayer?.Entity == null)
                {
                    // Entity-link race during construction: both the retry and the listener may
                    // end up firing, which is fine since ApplyClimbScale is idempotent.
                    float factor = (float)(1.0 + cfg.ClimbSpeedFactor);
                    ScheduleRetry(__instance, attributes, factor);
                    RegisterClassListener(__instance, attributes, factor);
                    return;
                }

                // No class = not a dwarf; overrides HasTrait's null-class-returns-true default.
                string charClass = entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                {
                    float factor = (float)(1.0 + cfg.ClimbSpeedFactor);
                    RegisterClassListener(__instance, attributes, factor);
                    return;
                }

                if (!RaceTraits.HasTrait(iplayer, cfg.DwarfTraitCode))
                    return;

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
            }
        }

        /// <summary>Idempotent: recomputes from JSON base values rather than multiplying the current value, so repeat calls (retry, listener, rejoin) are safe.</summary>
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

        /// <summary>ConditionalWeakTable makes this genuinely one-shot per entity instance, with no persistence across sessions to accidentally suppress.</summary>
        private static void ScheduleRetry(
            EntityBehaviorControlledPhysics behavior,
            JsonObject attributes,
            float factor)
        {
            Entity entity = behavior.entity;

            if (!retryScheduled.TryAdd(entity, sentinel))
                return;

            int retryDelayMs = RFMechanicsModSystem.Config?.ClimbLinkRetryDelayMs ?? 2000;

            entity.World.RegisterCallback(dt =>
            {
                try
                {
                    if (entity.World == null || entity.State == EnumEntityState.Despawned)
                        return;

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

                    if (!RaceTraits.HasTrait(iplayer, cfg.DwarfTraitCode))
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
            }, retryDelayMs);
        }

        /// <summary>ConditionalWeakTable caps this at one registration per entity instance.</summary>
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

                    if (!RaceTraits.HasTrait(iplayer, cfg.DwarfTraitCode))
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