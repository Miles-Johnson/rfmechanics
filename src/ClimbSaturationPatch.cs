using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on EntityBehaviorHunger.OnGameTick.
    /// Accumulates climb time while a dwarf ascends (IsClimbing && Jump)
    /// and drains it as flat satiety in a 10-second batch, writing Saturation
    /// directly.
    /// 
    /// Direct write skips the nutrient drain and UpdateNutrientHealthBoost
    /// that ConsumeSaturation/ReduceSaturation incur — climbing costs hunger,
    /// not max health. Vanilla owns starvation.
    /// 
    /// No reflection, no AccessTools, no FieldRefAccess.
    /// State stored in entity.Attributes (non-synced, per-entity tree).
    /// 
    /// Only the EntityPlayer guard sits outside the try block. Every other
    /// statement lives inside the try — this patch runs on the tick path
    /// where the player-entity link is always established, but the guard
    /// section is treated as hostile for consistency with ClimbSpeedPatch.
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
            // ── Guard 1: EntityPlayer only (literal first statement, outside try) ──
            if (__instance.entity is not EntityPlayer player)
                return;

            try
            {
                // 2. Server side only
                if (__instance.entity.World.Side != EnumAppSide.Server)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                // 3. Master toggle
                if (!cfg.EnableClimbSaturation)
                    return;

                // 4. Game mode: not Creative or Spectator
                //    (vanilla returns early in these modes before the batch runs,
                //     so accumulating would bank a large drain on mode change)
                IPlayer iplayer = __instance.entity.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null)
                    return;

                EnumGameMode mode = iplayer.WorldData.CurrentGameMode;
                if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator)
                    return;

                // ── Accumulate climb time (dwarf-only) ──
                bool ascending = player.Controls.IsClimbing && player.Controls.Jump;
                if (ascending)
                {
                    // Re-check IPlayer.Entity (may be null during construction)
                    if (iplayer.Entity == null)
                        return;

                    // Load-bearing: characterClass null check prevents HasTrait's
                    // null-class-returns-true default from charging classless
                    // players as dwarves. Same pattern as ClimbSpeedPatch.
                    string charClass = iplayer.Entity.WatchedAttributes.GetString("characterClass");
                    if (string.IsNullOrEmpty(charClass))
                        return;

                    var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                    if (charSys == null)
                        return;

                    if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                        return;

                    // Dwarf confirmed — accumulate
                    float seconds = __instance.entity.Attributes.GetFloat(KeyClimbSeconds);
                    seconds += deltaTime;
                    __instance.entity.Attributes.SetFloat(KeyClimbSeconds, seconds);
                }

                // ── Flush timer (accumulates every tick regardless) ──
                float flush = __instance.entity.Attributes.GetFloat(KeyFlushTimer) + deltaTime;
                __instance.entity.Attributes.SetFloat(KeyFlushTimer, flush);

                if (flush > 10f)
                {
                    __instance.entity.Attributes.SetFloat(KeyFlushTimer, 0f);

                    float climbSeconds = __instance.entity.Attributes.GetFloat(KeyClimbSeconds);
                    if (climbSeconds <= 0f)
                        return;

                    __instance.entity.Attributes.SetFloat(KeyClimbSeconds, 0f);

                    // Calendar scale: SpeedOfTime * CalendarSpeedMul / 30f
                    float cal = __instance.entity.Api.World.Calendar.SpeedOfTime
                              * __instance.entity.Api.World.Calendar.CalendarSpeedMul / 30f;

                    float drain = climbSeconds
                                * (float)cfg.ClimbSaturationPerSecond
                                * __instance.entity.Stats.GetBlended("hungerrate")
                                * cal;

                    // Direct write skips nutrient drain and UpdateNutrientHealthBoost —
                    // climbing costs hunger, not max health.
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
                // Saturation unchanged on exception
            }
        }
    }
}