using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Thew grant on qualifying eat events ("bites"), on top of ThewBehavior's own per-hour tick
    /// gain. Postfix on EntityBehaviorHunger.OnEntityReceiveSaturation so it reads POST-eat
    /// Saturation/ProteinLevel.
    /// The grant scales with this bite's raw saturation gain (capped per event by
    /// ThewPerBiteCap) rather than a flat amount, so no cooldown gate is needed to resist
    /// nibble-spam farming -- reward tracks total saturation eaten, not event count. This also
    /// means every ingredient of a multi-ingredient meal contributes its own proportional share,
    /// rather than only the first ingredient getting credit under a flat-plus-cooldown design.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
    public static class ThewEatPulsePatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(EntityBehaviorHunger __instance, float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown)
        {
            Entity? entity = __instance?.entity;
            if (entity == null) return;

            try
            {
                if (entity.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew) return;

                if (entity is not EntityPlayer player) return;

                // Load-bearing: HasTrait returns true for a null class by default.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null || !charSys.HasTrait(iplayer, cfg.OrcTraitCode)) return;

                // Recorded unconditionally -- ThewBehavior's tick gain reads this regardless of whether this bite's own pulse-gate below passes.
                entity.Attributes.SetInt(ThewBehavior.LastFoodCategoryKey, (int)foodCat);

                if (cfg.EnableThewFoodTypeGate && ThewBehavior.IsNonProteinPlantCategory(foodCat)) return;

                if (__instance.MaxSaturation <= 0f) return;
                float satFrac = __instance.Saturation / __instance.MaxSaturation;
                bool proteinGated = ThewBehavior.IsProteinGated(__instance, cfg);
                if (!proteinGated || ThewBehavior.RampMultiplier(satFrac, cfg) <= 0f) return;
                if (saturation <= 0f) return;

                var thewBhv = entity.GetBehavior<ThewBehavior>();
                if (thewBhv == null) return;

                float grant = Math.Min((float)cfg.ThewGainPerSaturationPoint * saturation, (float)cfg.ThewPerBiteCap);
                thewBhv.Thew += grant;
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
