using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on EntityBehaviorHealth.OnEntityReceiveDamage. Replaces the earlier
    /// RFFallDamageBehavior approach: "rffalldamage" was appended to the END of player.json's
    /// behaviors array, after vanilla's "health" behavior, so EntityBehaviorHealth always
    /// applied the full unreduced damage before this behavior's reduction ran. A Harmony prefix
    /// runs before the method body unconditionally regardless of behaviors-array order,
    /// sidestepping that ordering bug entirely.
    /// Gated on damageSource.Source == EnumDamageSource.Fall (not .Type) -- OnFallToGround is
    /// the sole source of fall damage and always sets Source to Fall.
    /// Also covers Goblin fall damage reduction on this same patch rather than a second Harmony
    /// patch on the same method, since a player is only ever one race.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage))]
    public static class FallDamagePatch
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

                if (damageSource.Source != EnumDamageSource.Fall)
                    return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                // No class = not this race; overrides HasTrait's null-class-returns-true default.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null)
                    return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (cfg.EnableFallDamageReduction && charSys.HasTrait(iplayer, cfg.ElfTraitCode))
                {
                    damage *= (float)(1.0 - cfg.FallDamageReductionFactor);
                    return;
                }

                if (cfg.EnableGoblinFallDamageReduction && charSys.HasTrait(iplayer, cfg.GoblinTraitCode))
                {
                    damage *= (float)(1.0 - cfg.GoblinFallDamageReductionFactor);
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in FallDamagePatch: {0}", ex);
                }
            }
        }
    }
}
