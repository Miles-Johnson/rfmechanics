using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Small flat Thew grant on qualifying eat events ("bites"), on top of ThewBehavior's own
    /// per-hour tick gain. Postfix on EntityBehaviorHunger.OnEntityReceiveSaturation so it reads
    /// POST-eat Saturation/ProteinLevel -- same hook point and __instance.entity access pattern
    /// as PreservedProteinPatch.cs's own postfix/prefix pair on this method.
    ///
    /// Cooldown-gated (BiteCooldownSec, default 60s) so it can't be farmed by rapid nibble-spam,
    /// and incidentally absorbs multi-ingredient meals for free: BlockMeal/BlockCookedContainer
    /// call ReceiveSaturation once per ingredient (orc-diagnostic-findings.md §1), so a 5-
    /// ingredient stew fires this postfix 5 times in the same moment -- only the first is inside
    /// an expired cooldown window, the rest are silently no-ops.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
    public static class ThewEatPulsePatch
    {
        private const string LastBiteKey = "rf-orc-thew-lastbite-ms";
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(EntityBehaviorHunger __instance)
        {
            Entity? entity = __instance?.entity;
            if (entity == null) return;

            try
            {
                if (entity.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew) return;

                if (entity is not EntityPlayer player) return;

                // Load-bearing null check -- see ThewBehavior.IsOrc's own comment.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null || !charSys.HasTrait(iplayer, cfg.OrcTraitCode)) return;

                if (__instance.MaxSaturation <= 0f) return;
                float satFrac = __instance.Saturation / __instance.MaxSaturation;
                bool proteinGated = __instance.ProteinLevel > (float)cfg.ProteinGateLevel;
                if (!proteinGated || ThewBehavior.RampMultiplier(satFrac, cfg) <= 0f) return;

                long now = entity.World.ElapsedMilliseconds;
                long last = entity.Attributes.GetLong(LastBiteKey, 0);
                long cooldownMs = (long)(cfg.BiteCooldownSec * 1000.0);
                if (now - last < cooldownMs) return;

                entity.Attributes.SetLong(LastBiteKey, now);

                var thewBhv = entity.GetBehavior<ThewBehavior>();
                if (thewBhv == null) return;

                thewBhv.Thew += (float)cfg.ThewPerBite;
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in ThewEatPulsePatch: {0}", ex);
                }
            }
        }
    }
}
