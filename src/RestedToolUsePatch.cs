using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on CollectibleObject.OnHeldInteractStep. Drains Rested for
    /// sustained tool use (mining/chopping hold, etc.). Deliberately not
    /// OnHeldAttackStep — that's the combat swing path, and the brief excludes combat.
    /// This method fires roughly every 20ms while a tool is held in use, so drain is
    /// computed as a per-second rate via RestedBehavior.TrackToolUseSeconds rather than
    /// a flat amount per call.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldInteractStep))]
    public static class RestedToolUsePatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(float secondsUsed, EntityAgent byEntity)
        {
            // ── Guard 1: null entity (outside try, mirrors ClimbSpeedPatch/ClimbSaturationPatch) ──
            if (byEntity?.World == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableRested)
                    return;

                if (byEntity.World.Side != EnumAppSide.Server)
                    return;

                byEntity.GetBehavior<RestedBehavior>()?.TrackToolUseSeconds(secondsUsed, (float)cfg.RestedToolUseDrainPerSecond);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in RestedToolUsePatch: {0}", ex);
                }
            }
        }
    }
}
