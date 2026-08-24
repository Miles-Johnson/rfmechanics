using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Repays orc Thew debt (Burn/Frenzy) on eat, funded by saturation rather than Thew itself --
    /// pays BurnDebt first, then FrenzyDebt (see ThewBehavior.PayDebt). Same Harmony hook the
    /// deleted eat-pulse patch used (EntityBehaviorHunger.OnEntityReceiveSaturation), but this is
    /// a new class, not a repurposed one -- the two patches share nothing but the hook target.
    /// Also stamps LastFoodCategoryKey: this is the only remaining hook on the eat event, and
    /// ThewBehavior's gain-zone food-type gate still reads that key every tick.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
    public static class ThewDebtRepayPatch
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

                // Recorded unconditionally -- ThewBehavior's gain-zone gate reads this regardless of whether this bite repays any debt.
                entity.Attributes.SetInt(ThewBehavior.LastFoodCategoryKey, (int)foodCat);

                if (saturation <= 0f) return;

                var thewBhv = entity.GetBehavior<ThewBehavior>();
                if (thewBhv == null) return;

                thewBhv.PayDebt(saturation * (float)cfg.DebtRepaidPerSaturationPoint);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in ThewDebtRepayPatch: {0}", ex);
                }
            }
        }
    }
}
