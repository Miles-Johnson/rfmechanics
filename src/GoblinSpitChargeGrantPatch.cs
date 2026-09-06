using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Postfix on CollectibleObject.tryEatStop -- grants spit charges when a goblin finishes
    /// eating game:rot. Deliberately not folded into GoblinRotEdiblePatch: that class patches
    /// GetNutritionProperties, which only makes rot look edible and carries no eat-completion
    /// signal. Prefix captures the original game:rot stack; postfix confirms its count fell by
    /// one even when the final item left the slot empty or replaced with an eaten-stack result.
    /// tryEatStop is protected, so the attribute below uses the string method name -- nameof(...)
    /// cannot reference an inaccessible member; there is only one overload, so it's unambiguous.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    public static class GoblinSpitChargeGrantPatch
    {
        private const string SpitChargesKey = "rfmechanics:spitCharges";
        private const string RotFliesKey = "rfmechanics:rotFlies";
        private const string RotFliesUpdatedHoursKey = "rfmechanics:rotFliesUpdatedHours";
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(CollectibleObject __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
            out (ItemStack Stack, CollectibleObject Collectible, int InitialCount)? __state)
        {
            __state = null;
            try
            {
                if (byEntity?.World == null) return;
                if (byEntity.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null) return;
                if (!cfg.EnableGoblinSpitCharges && !cfg.EnableGoblinRotFlies && !cfg.EnableGoblinRotAura) return;

                // Mirrors tryEatStop's own completion gate -- a cancelled bite shouldn't grant a charge.
                if (secondsUsed < 0.95f) return;

                var stack = slot?.Itemstack;
                var collectible = stack?.Collectible;
                var code = collectible?.Code;
                if (code == null || code.Domain != "game" || code.Path != "rot") return;
                if (stack == null || stack.StackSize <= 0 || !ReferenceEquals(collectible, __instance)) return;

                if (byEntity is not EntityPlayer player) return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (!RaceTraits.HasTrait(iplayer, cfg.GoblinTraitCode)) return;

                __state = (stack, __instance, stack.StackSize);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(float secondsUsed, EntityAgent byEntity, bool __runOriginal,
            (ItemStack Stack, CollectibleObject Collectible, int InitialCount)? __state)
        {
            try
            {
                if (!__runOriginal || secondsUsed < 0.95f || __state is not { } evidence) return;
                if (!ReferenceEquals(evidence.Stack.Collectible, evidence.Collectible)
                    || evidence.Stack.StackSize != evidence.InitialCount - 1) return;
                if (byEntity is not EntityPlayer player || player.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null) return;

                if (cfg.EnableGoblinRotAura) GoblinRotAuraState.EatRot(player, cfg);

                if (cfg.EnableGoblinSpitCharges)
                {
                    int current = player.WatchedAttributes.GetInt(SpitChargesKey, 0);
                    int granted = Math.Min(current + cfg.SpitChargesPerRot, cfg.SpitChargeCap);
                    player.WatchedAttributes.SetInt(SpitChargesKey, granted);
                }

                if (cfg.EnableGoblinRotFlies)
                {
                    GrantRotFlies(player, cfg);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private static void LogException(Exception ex)
        {
            if (loggedException) return;
            loggedException = true;
            RFMechanicsModSystem.Api?.Logger?.Warning(
                "[rfmechanics] Exception in GoblinSpitChargeGrantPatch: {0}", ex);
        }

        /// <summary>
        /// Decay-then-add, same shape as dietsetup's RotIntakeAccrual but a deliberately separate
        /// signal: rotIntake accrues from any imperfectly fresh food and sits near 0.5 in steady
        /// state, so it can't express "this goblin has eaten no rot." This keys on literal
        /// game:rot only (already enforced by the caller) and never reads dietsetup:intake:rot.
        /// </summary>
        private static void GrantRotFlies(EntityPlayer player, RFMechanicsConfig cfg)
        {
            var wa = player.WatchedAttributes;
            double nowHours = player.World.Calendar.TotalHours;
            double lastHours = wa.GetDouble(RotFliesUpdatedHoursKey, nowHours);
            double raw = wa.GetDouble(RotFliesKey, 0.0);

            double decayed = raw * Math.Pow(0.5, Math.Max(0.0, nowHours - lastHours) / cfg.GoblinRotFliesHalfLifeHours);
            double next = Math.Min(cfg.GoblinRotFliesCap, decayed + cfg.GoblinRotFliesPerRot);

            wa.SetDouble(RotFliesKey, next);
            wa.SetDouble(RotFliesUpdatedHoursKey, nowHours);
        }
    }
}
