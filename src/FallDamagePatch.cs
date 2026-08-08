using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony prefix on EntityBehaviorHealth.OnEntityReceiveDamage.
    ///
    /// Replaces the earlier RFFallDamageBehavior (plain EntityBehavior) approach: that relied
    /// on Entity.ReceiveDamage's foreach over SidedProperties.Behaviors mutating a single
    /// shared `ref float damage` in array order (Entity.cs:962-1017), but "rffalldamage" is
    /// appended to the END of player.json's server/behaviors array (seraph-falldamage.json,
    /// `/-` path) while vanilla's "health" behavior sits at index 4 — so EntityBehaviorHealth's
    /// own OnEntityReceiveDamage (VSEssentials, EntityBehaviorHealth.cs:226-267) always ran
    /// first and applied `Health -= damage` (line 252) with the full, unreduced damage before
    /// this behavior's reduction ever executed. A Harmony prefix runs before that method body
    /// unconditionally, regardless of behaviors-array order, so it sidesteps the ordering bug
    /// entirely.
    ///
    /// Gated on damageSource.Source == EnumDamageSource.Fall specifically, not
    /// damageSource.Type -- EntityBehaviorHealth.OnFallToGround is the sole place that raises
    /// fall damage, and it always sets Source to Fall.
    ///
    /// Also covers Goblin (Phase G1), with its own config toggle/factor
    /// (EnableGoblinFallDamageReduction/GoblinFallDamageReductionFactor) -- kept as one patch on
    /// one method rather than a second Harmony patch on the same target, since a player is only
    /// ever one race and stacking two independent patches on the same method risks
    /// patch-ordering ambiguity for no benefit.
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage))]
    public static class FallDamagePatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            // ── Guard 1: literal first statement, outside try ──
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

                // Class guard: no class = not this race (overrides HasTrait's
                // null-class-returns-true default). Same guard chain shape as every other
                // rfmechanics race gate (BranchyLeavesPassthroughPatch, TreeClimbingPatch).
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
                // Leave damage unchanged on exception
            }
        }
    }
}
