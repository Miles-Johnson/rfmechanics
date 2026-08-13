using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics.BugRace
{
    /// <summary>
    /// RE-HOMED, NOT DELETED (Phase G3 goblin extraction). This mechanic no longer runs for
    /// goblins -- it's earmarked for the future bug race. Registration is disabled below (the
    /// RegisterBlockBehaviorClass call in RFMechanicsModSystem.Start() is commented out). The
    /// logic itself is untouched and ready to be reactivated, or lifted wholesale into the
    /// bug-race mod, later. See notes/race-mechanics/ for the G3 rot-aura work this extraction
    /// made room for.
    ///
    /// BlockBehavior on the vanilla GetMiningSpeedModifier extension point (Block.cs:1010) --
    /// Block.OnGettingBroken multiplies dt by every attached behavior's modifier inside the
    /// RequiredMiningTier==0 branch BEFORE checking whether a tool is held, and vanilla's own
    /// comment at that call site says so explicitly: "This will also affect tool mining speed
    /// if stack != null, and that's OK" (Block.cs:1040) -- the boosted dt is passed straight
    /// into ItemSlot's OnBlockBreaking, which folds it into the held tool's own GetMiningSpeed
    /// result. This is exactly what's exploited below: rather than gating on "empty hands"
    /// (an earlier, incorrect version of this comment assumed that was already true "by
    /// construction" -- it wasn't, that was an unverified assumption with no actual guard),
    /// the bonus is gated on "not holding a shovel" -- a goblin digs at full speed bare-handed
    /// OR holding anything else (weapon, torch, unrelated junk), and only a held shovel drops
    /// back to default speed. Mirrors the same shovel-is-the-exception rule already used by
    /// GoblinSpitPackingPatch's conversion gate, for a consistent "shovel = normal digging
    /// tool, no goblin bonuses" story. See notes/goblin-phase-g2-partA-report.md A1 for why
    /// this extension point was chosen over patching OnGettingBroken directly, and
    /// notes/goblin-dig-materials-handover.md for the fix/design history.
    ///
    /// Attached via JSON patch to vanilla diggable-earth blocktypes (see
    /// patches/goblin-dig-blockbehavior.json for the current full attachment list -- deliberately
    /// not re-enumerated here, that list already went stale once) and authored directly into
    /// the mod-owned spit-packed blocktypes -- returns the goblin dig rate (Option A: beats
    /// steel shovel on every material, see Part A report A1) for goblins not holding a shovel,
    /// 1.0 (no-op) for everyone else and for goblins holding a shovel. The canonical family/
    /// bucket table for which blocks belong in this attachment list lives in
    /// notes/goblin-dig-materials-handover.md -- any future material addition/removal must be
    /// checked against that table across all three goblin-dig consumers (this attachment list,
    /// RFGoblinTunnelBehavior's GoblinDiggableEarthCodePrefixes, and
    /// GoblinSpitPackingPatch.ResolveConversionTarget).
    /// </summary>
    public class GoblinDigModifierBehavior : BlockBehavior
    {
        private static bool loggedException = false;

        public GoblinDigModifierBehavior(Block block) : base(block) { }

        public override float GetMiningSpeedModifier(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            try
            {
                // ── Guard 1: null player ──
                if (byPlayer?.Entity == null)
                    return 1f;

                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null)
                    return 1f;

                // 2. Master toggle
                if (!cfg.EnableGoblinDigBonus)
                    return 1f;

                // 3. Shovel guard: full dig-speed bonus applies bare-handed or holding
                //    anything except a shovel -- only a held shovel drops back to default
                //    speed. Without this, vanilla's own OnGettingBroken folds the boosted dt
                //    into whatever tool is held (Block.cs:1040), so a goblin digging with a
                //    shovel would otherwise get the full bonus stacked on top of normal
                //    shovel speed instead of matching it.
                EnumTool? heldTool = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Tool;
                if (heldTool == EnumTool.Shovel)
                    return 1f;

                // 4. Class guard: no class = not a goblin (overrides HasTrait's
                //    null-class-returns-true default), same convention as every other gate.
                string charClass = byPlayer.Entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return 1f;

                // 5. Trait check
                var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return 1f;

                if (!charSys.HasTrait(byPlayer, cfg.GoblinTraitCode))
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
