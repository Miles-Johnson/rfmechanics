using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on Block.OnEntityCollide -- covers the second vanilla ladder-ascent
    /// method (walking into a climbable block horizontally, no Jump needed), which hard-sets
    /// Motion.Y to a fixed constant every tick and bypasses climbUpSpeed/climbDownSpeed
    /// entirely (see ClimbSpeedPatch for the Jump-held path), so ClimbSpeedFactor previously
    /// had no effect here. Re-checks vanilla's own conditions since a postfix can't otherwise
    /// tell whether this call hit that branch; rescales only when Motion.Y is positive
    /// (ascent-only). Vanilla resets Motion.Y to the same constant every tick it fires, so
    /// rescaling after each call does not compound.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnEntityCollide))]
    public static class ClimbCollideAssistPatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(Block __instance, Entity entity, BlockPos pos, BlockFacing facing)
        {
            if (entity is not EntityPlayer player)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableClimbSpeed)
                    return;

                // Mirrors vanilla's own gate so this only touches Motion.Y on the branch vanilla set it.
                if (!player.Properties.CanClimb)
                    return;

                if (!(__instance.IsClimbable(pos) || player.Properties.CanClimbAnywhere))
                    return;

                if (!facing.IsHorizontal)
                    return;

                if (player.Controls.Sneak)
                    return;

                // No class = not a dwarf; overrides HasTrait's null-class-returns-true default.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null)
                    return;

                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (!charSys.HasTrait(iplayer, cfg.DwarfTraitCode))
                    return;

                if (player.SidedPos.Motion.Y > 0)
                {
                    float factor = (float)(1.0 + cfg.ClimbSpeedFactor);
                    player.SidedPos.Motion.Y *= factor;
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in ClimbCollideAssistPatch: {0}", ex);
                }
            }
        }
    }
}
