using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// Phase G3 rot aura, Task 3: crops in the aura stop advancing growth stage while a
    /// goblin lingers -- recoverable, never destroyed. Uses the real, already-used
    /// RegisterCropBehavior/CropBehavior extension point (vanilla's own PumpkinCropBehavior is
    /// the precedent), not a Harmony patch on BlockEntityFarmland.TryGrowCrop -- no method-body
    /// patch needed, and this composes safely with other crop behaviors via EnumHandling.
    ///
    /// BlockEntityFarmland.TryGrowCrop threads a single `handled` EnumHandling value through
    /// every behavior in the list (not OR'd) -- a later behavior can silently override an
    /// earlier one's PreventDefault back to PassThrough. This is why the attach patch
    /// (goblin-crop-stunt.json) appends this behavior LAST for every crop, including
    /// motherplant.json's existing Pumpkin behavior -- load-bearing ordering, not incidental.
    ///
    /// Gates on pure spatial falloff (GoblinRotAuraRegistry.GetStrengthAt), never on Intensity
    /// -- Intensity legitimately shrinks as a rot-fed goblin's aura widens (Task 4), but the
    /// aura's spatial reach (whether a crop is "under" it at all) does not, so crop stunting
    /// stays correct across the whole radius/intensity range instead of silently stopping as
    /// goblins become more rot-fed.
    /// </summary>
    public class GoblinCropStuntBehavior : CropBehavior
    {
        public GoblinCropStuntBehavior(Block block) : base(block) { }

        public override bool TryGrowCrop(ICoreAPI api, IFarmlandBlockEntity farmland,
            double currentTotalHours, int newGrowthStage, ref EnumHandling handling)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableGoblinRotAura)
            {
                handling = EnumHandling.PassThrough;
                return false;
            }

            // No accumulation, no per-block memory: recomputed fresh each call from the live
            // registry (populated at most GoblinRotAuraTickInterval seconds ago; entries older
            // than staleAfterMs are ignored by GetStrengthAt as a despawn-miss backstop).
            long nowMs = api.World.ElapsedMilliseconds;
            long staleAfterMs = (long)(3 * cfg.GoblinRotAuraTickInterval * 1000);
            float strength = GoblinRotAuraRegistry.GetStrengthAt(farmland.UpPos, nowMs, staleAfterMs);

            if (strength >= (float)cfg.CropStuntMinStrength)
            {
                handling = EnumHandling.PreventDefault;   // skip stage advance + nutrient consumption
                return false;
            }

            handling = EnumHandling.PassThrough;
            return false;
        }
    }
}
