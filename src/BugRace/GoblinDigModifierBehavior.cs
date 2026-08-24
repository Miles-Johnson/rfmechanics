using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics.BugRace
{
    /// <summary>
    /// RE-HOMED, NOT DELETED (goblin extraction) -- earmarked for a future bug race.
    /// Registration is disabled: the RegisterBlockBehaviorClass call in
    /// RFMechanicsModSystem.Start() is commented out. Logic is untouched, ready to reactivate
    /// or lift into the bug-race mod.
    /// LANDMINE: Block.OnGettingBroken multiplies dt by every attached behavior's modifier
    /// BEFORE checking whether a tool is held, and the boosted dt folds into the held tool's
    /// own GetMiningSpeed result too -- so the bonus is gated on "not holding a shovel", not
    /// "empty hands", or a goblin digging with a shovel would stack the bonus on top of normal
    /// shovel speed instead of matching it.
    /// Attached via JSON to vanilla diggable-earth blocktypes plus the mod-owned spit-packed
    /// ones; any material addition/removal must be checked against the canonical family table
    /// in notes/goblin-dig-materials-handover.md across all three goblin-dig consumers (this
    /// attachment list, RFGoblinTunnelBehavior, GoblinSpitPackingPatch.ResolveConversionTarget).
    /// </summary>
    public class GoblinDigModifierBehavior : BlockBehavior
    {
        private static bool loggedException = false;

        public GoblinDigModifierBehavior(Block block) : base(block) { }

        public override float GetMiningSpeedModifier(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            try
            {
                if (byPlayer?.Entity == null)
                    return 1f;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return 1f;

                if (!cfg.EnableGoblinDigBonus)
                    return 1f;

                EnumTool? heldTool = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Tool;
                if (heldTool == EnumTool.Shovel)
                    return 1f;

                if (!RaceTraits.HasTrait(byPlayer, cfg.GoblinTraitCode))
                    return 1f;

                return (float)cfg.GoblinBareHandDigRate;
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    RFMechanicsModSystem.Api?.Logger?.Warning(
                        "[rfmechanics] Exception in GoblinDigModifierBehavior: {0}", ex);
                }
                return 1f;
            }
        }
    }
}
