using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Harmony postfix on Block.OnEntityCollide.
    ///
    /// Covers the second vanilla ladder-ascent method: walking into a climbable
    /// block horizontally (no Jump needed) hard-sets Motion.Y to a fixed constant
    /// (0.04) every tick the entity remains in contact, unless Sneak is held.
    /// This is a completely separate mechanism from climbUpSpeed/climbDownSpeed
    /// (see ClimbSpeedPatch, which only covers the Jump-held ascent path) — it
    /// bypasses those fields entirely, so ClimbSpeedFactor previously had no
    /// effect on this ascent method at all.
    ///
    /// The postfix re-checks the same conditions vanilla used (CanClimb,
    /// IsClimbable/CanClimbAnywhere, horizontal facing, not Sneak) since a
    /// postfix can't otherwise tell whether this call actually hit that branch.
    /// It only rescales when Motion.Y ends up positive (ascending) — inherently
    /// ascent-only, matching ClimbSpeedPatch's descent-untouched design. Vanilla
    /// resets Motion.Y to the same absolute constant every tick it fires, so
    /// rescaling it immediately after each time does not compound.
    ///
    /// EntityPlayer guard is the literal first statement, outside the try —
    /// OnEntityCollide fires for every entity colliding with every block, not
    /// just players.
    /// </summary>
    [HarmonyPatch(typeof(Block), nameof(Block.OnEntityCollide))]
    public static class ClimbCollideAssistPatch
    {
        private static bool loggedException = false;

        [HarmonyPostfix]
        public static void Postfix(Block __instance, Entity entity, BlockPos pos, BlockFacing facing)
        {
            // ── Guard 1: EntityPlayer only (literal first statement, outside try) ──
            if (entity is not EntityPlayer player)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return;

                if (!cfg.EnableClimbSpeed)
                    return;

                // Mirror vanilla's own gate (Block.OnEntityCollide) so we only
                // touch Motion.Y on the same tick/branch vanilla actually set it.
                if (!player.Properties.CanClimb)
                    return;

                if (!(__instance.IsClimbable(pos) || player.Properties.CanClimbAnywhere))
                    return;

                if (!facing.IsHorizontal)
                    return;

                if (player.Controls.Sneak)
                    return; // vanilla didn't touch Motion.Y this call either

                // Class guard: no class = not a dwarf (overrides HasTrait's
                // null-class-returns-true default).
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
                // Leave Motion.Y unchanged on exception
            }
        }
    }
}
