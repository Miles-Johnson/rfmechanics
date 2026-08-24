using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Rot aura: inverts the vanilla food-preservation loop for goblins. On a throttled server
    /// tick, sweeps a bounding box and (a) accelerates spoilage on nearby food, holding it just
    /// short of fully spoiled rather than letting it tip over (larder hold), and (b) publishes
    /// an AuraSource other consumers (crop stunting) read from.
    /// Recomputed fresh from current positions each sweep, no accumulation/per-block/per-stack
    /// memory. Sealed/airtight containers are dampened via the polymorphic
    /// BlockContainer.GetContainingTransitionModifierPlaced hook, not a hardcoded class list.
    /// </summary>
    public class GoblinRotAuraBehavior : EntityBehavior
    {
        private float accum;

        public GoblinRotAuraBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfgoblinrotaura";

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableGoblinRotAura) return;

            accum += deltaTime;
            if (accum < cfg.GoblinRotAuraTickInterval) return;
            accum = 0f;

            if (!IsGoblin())
            {
                GoblinRotAuraRegistry.ClearSource(entity.EntityId);
                return;
            }

            float t = GameMath.Clamp(ReadLiveRotIntake(entity, cfg), 0f, 1f);
            (int radius, float intensity) = ComputeShape(cfg, t);

            var source = new AuraSource
            {
                Pos = entity.Pos.AsBlockPos,
                Radius = radius,
                VerticalHalfExtent = cfg.GoblinRotAuraVerticalHalfExtent,
                Intensity = intensity,
                UpdatedMs = entity.World.ElapsedMilliseconds
            };
            GoblinRotAuraRegistry.SetSource(entity.EntityId, source);

            // Logs every sweep (not a spam risk -- already throttled per-goblin) so real
            // wall-clock cost under real load, with multiple goblins online, can be verified.
            var sw = Stopwatch.StartNew();
            SweepContainers(source, cfg);
            SweepCarriedInventories(source, cfg);
            sw.Stop();
            RFMechanicsModSystem.Api?.Logger?.VerboseDebug(
                "[rfmechanics] rot aura sweep: entityId={0} radius={1} verticalHalfExtent={2} tookMs={3:F2}",
                entity.EntityId, source.Radius, source.VerticalHalfExtent, sw.Elapsed.TotalMilliseconds);
        }

        // Primary clear path -- IsGoblin() going false only clears on the *next* tick, which
        // never fires once the entity is gone. GetStrengthAt's staleness check is the backstop for anything this misses.
        public override void OnEntityDespawn(EntityDespawnData despawnData)
        {
            GoblinRotAuraRegistry.ClearSource(entity.EntityId);
            base.OnEntityDespawn(despawnData);
        }

        /// <summary>
        /// Cross-mod contract: reads dietsetup's "dietsetup:rotIntake" /
        /// "dietsetup:rotIntakeUpdatedHours" WatchedAttributes keys directly, no assembly
        /// reference to dietsetup. Decays live using the same exponential half-life formula
        /// dietsetup uses on write -- GoblinRotAuraIntakeHalfLifeHours must be kept in sync with
        /// dietsetup's own RotIntakeHalfLifeHours.
        /// </summary>
        internal static float ReadLiveRotIntake(Entity entity, RFMechanicsConfig cfg)
        {
            var wa = entity.WatchedAttributes;
            double nowHours = entity.World.Calendar.TotalHours;
            double lastHours = wa.GetDouble("dietsetup:rotIntakeUpdatedHours", nowHours);
            double raw = wa.GetDouble("dietsetup:rotIntake", 0.0);
            double elapsed = Math.Max(0.0, nowHours - lastHours);
            return (float)(raw * Math.Pow(0.5, elapsed / cfg.GoblinRotAuraIntakeHalfLifeHours));
        }

        /// <summary>intensity = totalOutputConstant / radius^2, so "total spoilage output stays
        /// roughly constant" as radius grows is structural, not tuned (rounding of radius is the only source of "roughly").</summary>
        internal static (int radius, float intensity) ComputeShape(RFMechanicsConfig cfg, float t)
        {
            int radius = (int)Math.Round(GameMath.Lerp(cfg.GoblinRotAuraRadiusMin, cfg.GoblinRotAuraRadiusMax, t));
            double totalOutputConstant = cfg.GoblinRotAuraRadiusMin * cfg.GoblinRotAuraRadiusMin * cfg.GoblinRotAuraIntensityAtMinRadius;
            float intensity = (float)(totalOutputConstant / Math.Max(1, radius * radius));
            return (radius, intensity);
        }

        private bool IsGoblin()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            return RaceTraits.HasTrait(iplayer, cfg.GoblinTraitCode);
        }

        /// <summary>Falloff logic lives in GoblinRotAuraRegistry.SpatialFalloff so this sweep's geometry and the crop-stunt gate can never drift apart.</summary>
        private void SweepContainers(AuraSource src, RFMechanicsConfig cfg)
        {
            BlockPos min = src.Pos.AddCopy(-src.Radius, -src.VerticalHalfExtent, -src.Radius);
            BlockPos max = src.Pos.AddCopy(src.Radius, src.VerticalHalfExtent, src.Radius);

            entity.World.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
            {
                var pos = new BlockPos(x, y, z, src.Pos.dimension);
                if (entity.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityContainer beContainer) return;   // non-container: skip, not "sealed"

                float spatialFalloff = GoblinRotAuraRegistry.SpatialFalloff(src, pos);
                if (spatialFalloff <= 0f) return;
                AccelerateContents(beContainer, spatialFalloff, src.Intensity, cfg);
            });
        }

        /// <summary>Delegates the per-slot math to AccelerateSlots so it can never drift from the carried-inventory sweep's own math.</summary>
        private void AccelerateContents(BlockEntityContainer beContainer, float spatialFalloff, float intensity, RFMechanicsConfig cfg)
        {
            IWorldAccessor world = entity.World;

            float sealedMod = 1f;
            if (world.BlockAccessor.GetBlock(beContainer.Pos) is BlockContainer bc)
                sealedMod = bc.GetContainingTransitionModifierPlaced(world, beContainer.Pos, EnumTransitionType.Perish);

            AccelerateSlots(beContainer.Inventory, spatialFalloff, intensity, sealedMod, cfg, _ => beContainer.MarkDirty(true));
        }

        /// <summary>
        /// Shared math for both sweep paths so they can't drift apart.
        /// deltaHours is (RateMultiplier - 1) times calendar-hours-per-sweep, not RateMultiplier:
        /// vanilla's own passive aging already advances TransitionedHours by exactly 1x per
        /// elapsed hour regardless of transitionHours, so adding a full Nx on top would make the
        /// effective total (N+1)x -- this collapses to a transitionHours-independent flat delta.
        /// Calendar rate is SpeedOfTime * CalendarSpeedMul, not SpeedOfTime alone -- the two are
        /// independent multipliers despite IGameCalendar.SpeedOfTime's doc comment reading
        /// otherwise (ClimbSaturationPatch uses the same product for the same reason).
        /// Hold-creep's write-threshold gate checks (TransitionedHours - holdCeilingHours), which
        /// is calendar-driven only, not intensity-scaled, so it reliably clears every few sweeps
        /// regardless of aura strength -- gating on the (tiny, intensity-scaled) creep delta
        /// itself instead would risk a stall at low intensity.
        /// </summary>
        private void AccelerateSlots(IEnumerable<ItemSlot> slots, float spatialFalloff, float intensity,
            float sealedMod, RFMechanicsConfig cfg, Action<ItemSlot> onChanged)
        {
            IWorldAccessor world = entity.World;

            float inGameHoursPerRealHour = world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul;
            float vanillaHoursPerSweep = (float)(cfg.GoblinRotAuraTickInterval / 3600.0) * inGameHoursPerRealHour;
            float auraExtraPerSweep = (float)Math.Max(0.0, cfg.GoblinRotAuraRateMultiplier - 1.0) * vanillaHoursPerSweep;

            foreach (ItemSlot slot in slots)
            {
                if (slot.Empty) continue;
                CollectibleObject coll = slot.Itemstack.Collectible;
                TransitionState state = coll.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
                if (state == null) continue;   // no Perish transitionableProps on this item

                float holdCeilingHours = state.FreshHours + state.TransitionHours * (float)cfg.GoblinRotAuraHoldFraction;
                float deltaHours = auraExtraPerSweep * spatialFalloff * intensity * sealedMod;

                float overflow = state.TransitionedHours - holdCeilingHours;
                if (overflow > (float)cfg.GoblinRotAuraWriteThresholdHours)
                {
                    // Floor exists so a low-intensity (wide-slow) goblin's tiny creepDelta can't shrink toward zero and reproduce the old hard clamp.
                    float creepDelta = Math.Max(deltaHours * (float)cfg.GoblinRotAuraHoldCreepFactor, (float)cfg.GoblinRotAuraHoldCreepFloorHours);
                    float retained = Math.Min(overflow, creepDelta);
                    coll.SetTransitionState(slot.Itemstack, EnumTransitionType.Perish, holdCeilingHours + retained);
                    onChanged(slot);
                    continue;
                }

                if (deltaHours <= 0f) continue;

                float candidate = Math.Min(state.TransitionedHours + deltaHours, holdCeilingHours);
                if (candidate - state.TransitionedHours < (float)cfg.GoblinRotAuraWriteThresholdHours) continue;

                coll.SetTransitionState(slot.Itemstack, EnumTransitionType.Perish, candidate);
                onChanged(slot);
            }
        }

        /// <summary>
        /// No exemptions -- includes the goblin's own carried food and every other nearby
        /// player's equally, by design.
        /// sealedMod is hardcoded 1f, not read from GetContainingTransitionModifierContained:
        /// worn bags implement CollectibleBehaviorHeldBag, not CollectibleBehaviorContainer, so
        /// there is no polymorphic sealing hook to consult the way the placed-container sweep
        /// has -- vanilla has no "sealed backpack" concept, so 1f is accurate, not an exemption.
        /// </summary>
        private void SweepCarriedInventories(AuraSource src, RFMechanicsConfig cfg)
        {
            if (!cfg.EnableGoblinRotAuraCarriedInventory) return;

            BlockPos min = src.Pos.AddCopy(-src.Radius, -src.VerticalHalfExtent, -src.Radius);
            BlockPos max = src.Pos.AddCopy(src.Radius, src.VerticalHalfExtent, src.Radius);

            // GetEntitiesInsideCuboid's matches delegate isn't documented as call-once, so no side effects belong inside it.
            Entity[] nearbyPlayers = entity.World.GetEntitiesInsideCuboid(min, max, e => e is EntityPlayer);

            foreach (Entity e in nearbyPlayers)
            {
                var targetPlayer = (EntityPlayer)e;

                float spatialFalloff = GoblinRotAuraRegistry.SpatialFalloff(src, targetPlayer.Pos.AsBlockPos);
                if (spatialFalloff <= 0f) continue;

                IPlayer iplayer = targetPlayer.Player;
                if (iplayer == null) continue;

                const float sealedMod = 1f;

                IInventory hotbar = iplayer.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
                if (hotbar != null)
                {
                    AccelerateSlots(hotbar, spatialFalloff, src.Intensity, sealedMod, cfg, slot => slot.MarkDirty());
                }

                IInventory backpackEquip = iplayer.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                if (backpackEquip == null) continue;

                ItemSlot[] bagSlots = backpackEquip.ToArray();
                // ReloadBagInventory is a full deserialize of every worn bag; skip it when nothing's equipped (the common case).
                if (bagSlots.All(s => s.Empty)) continue;

                var bagInv = new BagInventory(entity.Api, bagSlots);
                var dummyParent = new InventoryGeneric(entity.Api);
                bagInv.ReloadBagInventory(dummyParent, bagSlots);

                AccelerateSlots(bagInv, spatialFalloff, src.Intensity, sealedMod, cfg, slot =>
                {
                    var bagContentSlot = (ItemSlotBagContent)slot;
                    bagInv.SaveSlotIntoBag(bagContentSlot);        // persists into the bag stack's own tree
                    bagSlots[bagContentSlot.BagIndex].MarkDirty(); // dirties the real equip slot for save/sync
                });
            }
        }
    }
}
