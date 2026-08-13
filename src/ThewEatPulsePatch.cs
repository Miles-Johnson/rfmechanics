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
    /// Saturation/ProteinLevel -- same hook point and __instance.entity access pattern as
    /// PreservedProteinPatch.cs's own postfix/prefix pair on this method.
    ///
    /// Phase 2 (T1/T6) rework of the original flat-grant/cooldown design:
    /// - T1: records the eaten item's EnumFoodCategory unconditionally (entity.Attributes,
    ///   ThewBehavior.LastFoodCategoryKey) for ThewBehavior's tick gain to read, and independently
    ///   blocks THIS bite's own pulse when the category is Fruit/Vegetable/Grain (see
    ///   ThewBehavior.IsNonProteinPlantCategory), regardless of ProteinLevel/satFrac.
    /// - T6: the grant itself now scales with `saturation` (this bite's raw, pre-
    ///   nutritionGainMultiplier saturation gain -- see EntityBehaviorHunger.cs:239,245) instead
    ///   of a flat amount, capped per event by ThewPerBiteCap. BiteCooldownSec's old cooldown gate
    ///   is gone: a flat-per-event grant needed cooldown-gating to resist nibble-spam farming, but
    ///   a size-proportional grant doesn't -- reward now tracks total saturation eaten, not event
    ///   count. This also fixes the old cooldown's side effect of only crediting the first
    ///   ingredient of a multi-ingredient meal (BlockMeal/BlockCookedContainer call
    ///   ReceiveSaturation once per ingredient, orc-diagnostic-findings.md §1) -- every ingredient
    ///   now contributes its own proportional share.
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

                // Load-bearing null check -- see ThewBehavior.IsOrc's own comment.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null || !charSys.HasTrait(iplayer, cfg.OrcTraitCode)) return;

                // T1: record what was eaten unconditionally -- ThewBehavior's tick gain reads
                // this regardless of whether this specific bite's own pulse-gate below passes.
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
