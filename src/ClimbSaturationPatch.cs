using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on EntityBehaviorHunger.OnGameTick. Accumulates climb time while a dwarf
    /// ascends (IsClimbing &amp;&amp; Jump) and drains it as flat satiety in a batch, writing
    /// Saturation directly -- this skips the nutrient drain and UpdateNutrientHealthBoost that
    /// ConsumeSaturation/ReduceSaturation incur, since climbing costs hunger, not max health
    /// (vanilla still owns starvation). State lives in entity.Attributes (non-synced, per-entity).
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnGameTick))]
    public static class ClimbSaturationPatch
    {
        private static bool loggedException = false;

        private const string KeyClimbSeconds = "rf-climbseconds";
        private const string KeyFlushTimer = "rf-climbflush";

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorHunger __instance, float deltaTime)
        {
            if (__instance.entity is not EntityPlayer player)
                return;

            try
            {
                if (__instance.entity.World.Side != EnumAppSide.Server)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableClimbSaturation)
                    return;

                // Vanilla returns early in Creative/Spectator before the batch runs, so
                // accumulating here would bank a large drain on mode change.
                IPlayer iplayer = __instance.entity.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null)
                    return;

                EnumGameMode mode = iplayer.WorldData.CurrentGameMode;
                if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator)
                    return;

                bool ascending = player.Controls.IsClimbing && player.Controls.Jump;
                if (ascending)
                {
                    // iplayer.Entity may still be null during construction.
                    if (iplayer.Entity == null)
                        return;

                    // Load-bearing: null check prevents HasTrait's null-class-returns-true
                    // default from charging classless players as dwarves.
                    string charClass = iplayer.Entity.WatchedAttributes.GetString("characterClass");
                    if (string.IsNullOrEmpty(charClass))
                        return;

                    var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                    if (charSys == null)
                        return;

                    if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                        return;

                    float seconds = __instance.entity.Attributes.GetFloat(KeyClimbSeconds);
                    seconds += deltaTime;
                    __instance.entity.Attributes.SetFloat(KeyClimbSeconds, seconds);
                }

                // Flush timer accumulates every tick regardless of whether the player is ascending.
                float flush = __instance.entity.Attributes.GetFloat(KeyFlushTimer) + deltaTime;
                __instance.entity.Attributes.SetFloat(KeyFlushTimer, flush);

                if (flush > (float)cfg.ClimbSaturationFlushIntervalSeconds)
                {
                    __instance.entity.Attributes.SetFloat(KeyFlushTimer, 0f);

                    float climbSeconds = __instance.entity.Attributes.GetFloat(KeyClimbSeconds);
                    if (climbSeconds <= 0f)
                        return;

                    __instance.entity.Attributes.SetFloat(KeyClimbSeconds, 0f);

                    float cal = __instance.entity.Api.World.Calendar.SpeedOfTime
                              * __instance.entity.Api.World.Calendar.CalendarSpeedMul / 30f;

                    float drain = climbSeconds
                                * (float)cfg.ClimbSaturationPerSecond
                                * __instance.entity.Stats.GetBlended("hungerrate")
                                * cal;

                    __instance.Saturation = Math.Max(0f, __instance.Saturation - drain);
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in ClimbSaturationPatch: {0}", ex);
                }
            }
        }
    }
}