using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Preserved-protein multiplier for orc. Two-step stash pattern across two Harmony prefixes,
    /// since tryEatStop's call into ReceiveSaturation can't be intercepted directly with a
    /// prefix alone (no transpiler here): PreservedProteinFlagPatch (prefix on tryEatStop)
    /// stashes a one-shot flag when an orc eats a matching item; PreservedProteinMultiplierPatch
    /// (prefix on OnEntityReceiveSaturation) reads and unconditionally clears that flag every
    /// call, scaling nutritionGainMultiplier only when it was set. Both calls happen
    /// synchronously within the same tryEatStop invocation, so the flag never crosses a tick
    /// boundary. Dormant by default: the default PreservedProteinItemCodes have no obtainable
    /// production path in survival -- this is a server-config surface for modded preserved foods.
    /// </summary>
    internal static class PreservedProteinFlag
    {
        public const string PendingKey = "rf-orc-preserved-eat-pending";
    }

    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop", new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) })]
    public static class PreservedProteinFlagPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(ItemSlot slot, EntityAgent byEntity)
        {
            if (byEntity is not EntityPlayer player)
                return;

            try
            {
                if (byEntity.World.Side != EnumAppSide.Server)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew)
                    return;

                // Load-bearing: prevents HasTrait's null-class-returns-true default from charging classless players as orcs.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null)
                    return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null || !charSys.HasTrait(iplayer, cfg.OrcTraitCode))
                    return;

                string? itemCode = slot?.Itemstack?.Collectible?.Code?.ToString();
                if (itemCode == null)
                    return;

                bool isPreserved = false;
                foreach (string code in cfg.PreservedProteinItemCodes)
                {
                    if (code == itemCode) { isPreserved = true; break; }
                }
                if (!isPreserved)
                    return;

                byEntity.Attributes.SetBool(PreservedProteinFlag.PendingKey, true);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in PreservedProteinFlagPatch: {0}", ex);
                }
            }
        }
    }

    [HarmonyPatch(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation))]
    public static class PreservedProteinMultiplierPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorHunger __instance, ref float nutritionGainMultiplier)
        {
            Entity? entity = __instance?.entity;
            if (entity == null)
                return;

            try
            {
                bool pending = entity.Attributes.GetBool(PreservedProteinFlag.PendingKey);
                if (!pending)
                    return;

                // Always clear on read: a same-tick, single-use signal, never allowed to survive to an unrelated future saturation event.
                entity.Attributes.RemoveAttribute(PreservedProteinFlag.PendingKey);

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew)
                    return;

                nutritionGainMultiplier *= (float)cfg.PreservedProteinMultiplier;
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in PreservedProteinMultiplierPatch: {0}", ex);
                }
            }
        }
    }
}
