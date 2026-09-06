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
            if (cfg == null) return;
            if (!cfg.EnableGoblinRotAura)
            {
                GoblinRotAuraRegistry.ClearSource(entity.EntityId);
                GoblinRotAuraState.Publish(entity, default);
                return;
            }

            accum += deltaTime;
            if (accum < cfg.GoblinRotAuraTickInterval) return;
            accum = 0f;

            Refresh(sweep: true);
        }

        internal void Refresh(bool sweep = false)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (entity.World.Side != EnumAppSide.Server || cfg == null) return;
            if (!cfg.EnableGoblinRotAura || !entity.Alive || !IsGoblin())
            {
                GoblinRotAuraRegistry.ClearSource(entity.EntityId);
                GoblinRotAuraState.Publish(entity, default);
                return;
            }

            GoblinRotAuraState.Migrate(entity, cfg);
            GoblinAuraShape shape = GoblinRotAuraState.Read(entity, cfg);
            GoblinRotAuraState.Publish(entity, shape);
            if (!shape.Active)
            {
                GoblinRotAuraRegistry.ClearSource(entity.EntityId);
                return;
            }

            var source = new AuraSource
            {
                Pos = entity.Pos.AsBlockPos,
                Center = entity.Pos.XYZ,
                Radius = shape.Radius,
                VerticalHalfExtent = shape.VerticalHalfExtent,
                Intensity = shape.Intensity,
                Fade = shape.Fade,
                UpdatedMs = entity.World.ElapsedMilliseconds
            };
            GoblinRotAuraRegistry.SetSource(entity.EntityId, source);
            if (!sweep) return;

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
            int reach = (int)Math.Ceiling(src.Radius);
            BlockPos min = src.Pos.AddCopy(-reach, -src.VerticalHalfExtent, -reach);
            BlockPos max = src.Pos.AddCopy(reach, src.VerticalHalfExtent, reach);

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

            int reach = (int)Math.Ceiling(src.Radius);
            BlockPos min = src.Pos.AddCopy(-reach, -src.VerticalHalfExtent, -reach);
            BlockPos max = src.Pos.AddCopy(reach + 1, src.VerticalHalfExtent + 1, reach + 1);

            // GetEntitiesInsideCuboid's matches delegate isn't documented as call-once, so no side effects belong inside it.
            Entity[] nearbyPlayers = entity.World.GetEntitiesInsideCuboid(min, max, e => e is EntityPlayer);

            foreach (Entity e in nearbyPlayers)
            {
                var targetPlayer = (EntityPlayer)e;

                float spatialFalloff = GoblinRotAuraRegistry.SpatialFalloff(src, targetPlayer.Pos.XYZ, targetPlayer.Pos.Dimension);
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

                // The player's backpack inventory already exposes its live content slots.
                // Marking a content slot saves it into its bag and syncs that slot. Marking
                // the worn bag instead reloads the inventory, broadcasts player data and
                // rebuilds the player mesh every sweep (visible as an animation twitch).
                AccelerateSlots(backpackEquip.Where(slot => slot is ItemSlotBagContent),
                    spatialFalloff, src.Intensity, sealedMod, cfg, slot => slot.MarkDirty());
            }
        }
    }
}
