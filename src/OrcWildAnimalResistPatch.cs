using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on EntityBehaviorHealth.OnEntityReceiveDamage, same target/pattern as
    /// FallDamagePatch (behaviors-array ordering would otherwise let vanilla's "health" behavior
    /// apply full damage before a plain EntityBehavior override ran). FallDamagePatch also
    /// prefixes this method; the two never overlap in practice since fall damage's DamageSource
    /// has no cause entity, so guard 8 below always excludes it -- no HarmonyPriority needed.
    /// Deliberately standalone: does not read ThewBehavior or FrenzyBehavior at all, since Frenzy
    /// requires Thew to activate and this resist exists specifically for a Thew-starved orc.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage))]
    public static class OrcWildAnimalResistPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            if (__instance.entity is not EntityPlayer player)
                return;

            try
            {
                if (player.World.Side != EnumAppSide.Server)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableOrcWildAnimalResist)
                    return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (!RaceTraits.HasTrait(iplayer, cfg.OrcTraitCode))
                    return;

                float resist = ComputeResist(__instance, cfg);
                if (resist <= 0f)
                    return;

                if (cfg.OrcWildResistRequiresNoArmor && IsWearingArmor(player))
                    return;

                Entity? attacker = damageSource?.GetCauseEntity();
                if (attacker == null || !IsWildAnimal(attacker))
                    return;

                damage *= (1f - resist);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in OrcWildAnimalResistPatch: {0}", ex);
                }
            }
        }

        /// <summary>Continuous at the activation threshold: resist is exactly 0 at gap ==
        /// ActivationHealthFracGap, not a step, so there's no discontinuity for the player to
        /// notice right at the boundary.</summary>
        internal static float ComputeResist(EntityBehaviorHealth healthBhv, RFMechanicsConfig cfg)
        {
            if (healthBhv.MaxHealth <= 0f) return 0f;

            float gap = 1f - (healthBhv.Health / healthBhv.MaxHealth);
            float activationGap = (float)cfg.OrcWildResistActivationHealthFracGap;
            if (gap <= activationGap) return 0f;

            float t = (gap - activationGap) / (1f - activationGap);
            return (float)cfg.OrcWildResistMaxBonus * (float)Math.Pow(t, cfg.OrcWildResistCurveExponent);
        }

        /// <summary>Binary by slot, not scaled by protection value -- any one of the three
        /// vanilla armor slots disables the resist entirely, so the player only has to learn
        /// "wearing armor turns this off," not a curve they can't see. Checked by slot rather
        /// than protection value so modded clothing with incidental protection in a non-armor
        /// slot doesn't unexpectedly disable it -- accepted tradeoff.</summary>
        internal static bool IsWearingArmor(EntityPlayer player)
        {
            IInventory? inv = player.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return false;

            foreach (ItemSlot slot in inv)
            {
                if (slot.Empty) continue;
                if (slot is not ItemSlotCharacter charSlot) continue;

                if (charSlot.Type == EnumCharacterDressType.ArmorHead
                    || charSlot.Type == EnumCharacterDressType.ArmorBody
                    || charSlot.Type == EnumCharacterDressType.ArmorLegs)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Deliberately duplicated from OrcSmellClassifier.IsSmellableFauna rather than
        /// calling it -- damage-resistance classification and smell classification answer
        /// different questions today and may diverge; a shared call would silently couple them.</summary>
        private static bool IsWildAnimal(Entity entity)
        {
            if (entity.GetBehavior<EntityBehaviorHarvestable>() == null) return false;
            return entity.Properties.Attributes?["creatureDiet"]?.Exists ?? false;
        }
    }
}
