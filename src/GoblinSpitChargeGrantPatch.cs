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
    /// signal. tryEatStop has direct access to slot.Itemstack (to confirm the eaten item is
    /// literally game:rot) and already gates real eat completion the same way this mirrors.
    /// tryEatStop is protected, so the attribute below uses the string method name -- nameof(...)
    /// cannot reference an inaccessible member; there is only one overload, so it's unambiguous.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    public static class GoblinSpitChargeGrantPatch
    {
        private const string SpitChargesKey = "rfmechanics:spitCharges";
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(float secondsUsed, ItemSlot slot, EntityAgent byEntity)
        {
            try
            {
                if (byEntity?.World == null) return;
                if (byEntity.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinSpitCharges) return;

                // Mirrors tryEatStop's own completion gate -- a cancelled bite shouldn't grant a charge.
                if (secondsUsed < 0.95f) return;

                var code = slot?.Itemstack?.Collectible?.Code;
                if (code == null || code.Domain != "game" || code.Path != "rot") return;

                if (byEntity is not EntityPlayer player) return;

                // Load-bearing: HasTrait returns true for a null class, so skipping this makes every unassigned player match.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null) return;

                if (!charSys.HasTrait(iplayer, cfg.GoblinTraitCode)) return;

                int current = player.WatchedAttributes.GetInt(SpitChargesKey, 0);
                int granted = Math.Min(current + cfg.SpitChargesPerRot, cfg.SpitChargeCap);
                player.WatchedAttributes.SetInt(SpitChargesKey, granted);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in GoblinSpitChargeGrantPatch: {0}", ex);
                }
            }
        }
    }
}
