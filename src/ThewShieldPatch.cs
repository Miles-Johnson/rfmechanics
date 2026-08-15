using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// StarvationShieldWhileThew: while an orc's Thew &gt; 0, suppresses vanilla's own
    /// starvation damage (EntityBehaviorHunger.SlowTick raises it as DamageSource { Source =
    /// Internal, Type = Hunger }).
    /// Patched at EntityBehaviorHealth.OnEntityReceiveDamage instead of SlowTick itself, since
    /// SlowTick also does unrelated cold-resistance work in the same method body that must not
    /// be skipped -- a Harmony prefix can only skip a method's entire body, not part of it,
    /// without a transpiler. Zeroing `damage` here lets the original run harmlessly (Health -= 0)
    /// rather than skipping whatever else OnEntityReceiveDamage does (death check, events).
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorHealth), nameof(EntityBehaviorHealth.OnEntityReceiveDamage))]
    public static class ThewShieldPatch
    {
        private static bool loggedException = false;

        [HarmonyPrefix]
        public static void Prefix(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            if (damageSource == null || damageSource.Type != EnumDamageType.Hunger)
                return;

            Entity? entity = __instance?.entity;
            if (entity == null) return;

            try
            {
                if (entity.World.Side != EnumAppSide.Server) return;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableThew || !cfg.StarvationShieldWhileThew) return;

                if (entity is not EntityPlayer player) return;

                // Load-bearing: HasTrait returns true for a null class by default.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null || !charSys.HasTrait(iplayer, cfg.OrcTraitCode)) return;

                var thewBhv = entity.GetBehavior<ThewBehavior>();
                if (thewBhv == null || thewBhv.Thew <= 0f) return; // Thew == 0: shield off, vanilla resumes untouched

                damage = 0f;
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Error(
                        "[rfmechanics] Exception in ThewShieldPatch: {0}", ex);
                }
            }
        }
    }
}
