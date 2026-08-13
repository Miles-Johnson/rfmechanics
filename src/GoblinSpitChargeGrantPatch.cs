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
    /// GetNutritionProperties, which only makes rot look edible to goblins and carries no
    /// eat-completion signal or itemstack-independent trigger. tryEatStop is patched instead
    /// because it has direct access to slot.Itemstack (needed to confirm the eaten item is
    /// literally game:rot, not just "some NoNutrition-category food") and already gates the
    /// real eat completion the same way this patch mirrors: secondsUsed &gt;= 0.95f.
    ///
    /// tryEatStop is protected, so the HarmonyPatch attribute below uses the string method name
    /// rather than nameof(...), which cannot reference an inaccessible member from outside the
    /// class. There is only one tryEatStop overload on CollectibleObject (confirmed by reading
    /// reference/upstream/vsapi/Common/Collectible/Collectible.cs), so the string name is
    /// unambiguous.
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

                // Mirrors tryEatStop's own completion gate (Collectible.cs:1856) -- a cancelled
                // bite (released early) should not grant a charge.
                if (secondsUsed < 0.95f) return;

                var code = slot?.Itemstack?.Collectible?.Code;
                if (code == null || code.Domain != "game" || code.Path != "rot") return;

                if (byEntity is not EntityPlayer player) return;

                // Load-bearing null check -- HasTrait returns true for a null class, so skipping
                // this makes every unassigned player match. Same pattern as GoblinRotEdiblePatch
                // and GoblinSpitPackingPatch.
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
