using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Dormant preserved-protein multiplier for orc (Part 2c of the Thew brief). Two-step stash
    /// pattern across two Harmony prefixes, since CollectibleObject.tryEatStop's call into
    /// ReceiveSaturation can't be intercepted directly with a prefix alone (no transpiler here):
    ///
    /// 1. PreservedProteinFlagPatch (prefix on CollectibleObject.tryEatStop) -- if the eater is
    ///    orc and the eaten item's code matches PreservedProteinItemCodes, stashes a one-shot
    ///    flag on the entity.
    /// 2. PreservedProteinMultiplierPatch (prefix on EntityBehaviorHunger.OnEntityReceiveSaturation)
    ///    -- reads and unconditionally clears that flag on every call (never let it survive to
    ///    affect an unrelated future saturation event), scaling nutritionGainMultiplier by
    ///    PreservedProteinMultiplier only when it was set.
    ///
    /// Both calls happen synchronously within the same tryEatStop invocation (findings §1: the
    /// saturation grant happens before the stack is decremented, nothing else can interleave),
    /// so the flag's lifetime never crosses a tick boundary.
    ///
    /// Dormant per the settled design: the default PreservedProteinItemCodes list (cured
    /// redmeat/bushmeat) has no obtainable production path in survival (A5 finding) -- this
    /// exists as a server-config surface for modded preserved foods, not active vanilla content.
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
            // ── Guard 1: EntityPlayer only (literal first statement, outside try) ──
            if (byEntity is not EntityPlayer player)
                return;

            try
            {
                if (byEntity.World.Side != EnumAppSide.Server)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew)
                    return;

                // Load-bearing: characterClass null check prevents HasTrait's null-class-returns-
                // true default from charging classless players as orcs (same pattern as every
                // other rfmechanics race gate -- see RFTreeProximityBehavior.IsElf).
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

                // Always clear on read: this is a same-tick, single-use signal from
                // PreservedProteinFlagPatch, never allowed to survive to an unrelated future
                // saturation event regardless of what happens below.
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
