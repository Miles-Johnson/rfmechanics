using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// Crops in the aura stop advancing growth stage while a goblin lingers -- recoverable,
    /// never destroyed. Uses the RegisterCropBehavior/CropBehavior extension point (vanilla's
    /// PumpkinCropBehavior is the precedent), not a Harmony patch on TryGrowCrop.
    /// LOAD-BEARING ORDERING: TryGrowCrop threads a single `handled` EnumHandling value through
    /// every behavior in the list (not OR'd), so a later behavior can silently override an
    /// earlier one's PreventDefault back to PassThrough -- the attach patch
    /// (goblin-crop-stunt.json) must append this behavior LAST for every crop, including
    /// motherplant.json's existing Pumpkin behavior.
    /// Gates on pure spatial falloff, never on Intensity: Intensity legitimately shrinks as a
    /// rot-fed goblin's aura widens, but the aura's spatial reach does not.
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

            // Recomputed fresh each call from the live registry; staleAfterMs is a despawn-miss backstop.
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
