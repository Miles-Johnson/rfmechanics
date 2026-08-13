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
    /// Phase G3 rot aura: inverts the vanilla food-preservation loop for goblins. On a ~2s
    /// server-side throttle, sweeps a bounding box around the goblin and (a) accelerates
    /// spoilage on food in nearby BlockEntityContainer inventories, holding it just short of
    /// fully spoiled rather than letting it tip over (larder hold, Task 2), and (b) publishes
    /// an AuraSource other consumers (crop stunting, Task 3) read from.
    ///
    /// No accumulation, no per-block memory, no per-stack memory -- everything is recomputed
    /// fresh from current positions each sweep. Applies to all food in range regardless of
    /// owner (no exemptions). Sealed/airtight containers are dampened via the polymorphic
    /// BlockContainer.GetContainingTransitionModifierPlaced hook, not a hardcoded class list.
    ///
    /// Attached to the player entity type via seraph-goblinrotaura.json, same shape as every
    /// other rfmechanics player behavior -- the goblin gate lives inside IsGoblin(), not in
    /// attach/detach lifecycle.
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

            // Intake-driven shape (Task 4).
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

            // Sweep-cost measurement: the plan approved ~6,700 positions/sweep as an estimate,
            // not a measured number, with the explicit condition that actual wall-clock cost
            // (with multiple goblins online simultaneously, since cost is per-goblin and
            // multiplies) be measured before treating it as settled. This logs every sweep --
            // sweeps are already throttled to once per GoblinRotAuraTickInterval per goblin, so
            // this is not a spam risk -- so a server admin can grep client/server-main.log for
            // "[rfmechanics] rot aura sweep" and see real timings under real load.
            var sw = Stopwatch.StartNew();
            SweepContainers(source, cfg);
            SweepCarriedInventories(source, cfg);
            sw.Stop();
            RFMechanicsModSystem.Api?.Logger?.VerboseDebug(
                "[rfmechanics] rot aura sweep: entityId={0} radius={1} verticalHalfExtent={2} tookMs={3:F2}",
                entity.EntityId, source.Radius, source.VerticalHalfExtent, sw.Elapsed.TotalMilliseconds);
        }

        // Despawn (logout, in practice, for a player entity) is the primary clear path --
        // IsGoblin() going false only clears on the *next* tick, which never fires once the
        // entity is gone. GoblinRotAuraRegistry.GetStrengthAt's staleness check is the backstop
        // for anything this misses (death without despawn, an exception before this runs, etc.).
        public override void OnEntityDespawn(EntityDespawnData despawnData)
        {
            GoblinRotAuraRegistry.ClearSource(entity.EntityId);
            base.OnEntityDespawn(despawnData);
        }

        /// <summary>
        /// Reads dietsetup's rot-intake accumulator via a plain WatchedAttributes.GetDouble on
        /// two documented synced keys -- no assembly reference to dietsetup, same pattern as
        /// reading characterClass today. Decays it live using the same exponential half-life
        /// formula dietsetup itself uses to decay on write (RotIntakeAccrualPatch.AccrueRotIntake)
        /// -- GoblinRotAuraIntakeHalfLifeHours must be kept in sync with dietsetup's own
        /// RotIntakeHalfLifeHours, a documented cross-reference, same category of duplication
        /// this codebase already accepts (see GoblinTraitCode, independently declared per-mod).
        /// Internal static (not tied to a live behavior instance) so /rfrotdiag can call it
        /// directly for any online player, not just report on itself.
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

        /// <summary>
        /// Radius grows / intensity shrinks together (not independently configured), so "total
        /// spoilage output stays roughly constant" is structural, not tuned -- intensity =
        /// totalOutputConstant / radius^2 guarantees it by construction, with only
        /// integer-rounding of radius introducing the "roughly" instead of "exactly". Rot-fed
        /// (t-&gt;1) = wide and slow; rot-starved (t-&gt;0) = narrow and intense.
        /// </summary>
        internal static (int radius, float intensity) ComputeShape(RFMechanicsConfig cfg, float t)
        {
            int radius = (int)Math.Round(GameMath.Lerp(cfg.GoblinRotAuraRadiusMin, cfg.GoblinRotAuraRadiusMax, t));
            double totalOutputConstant = cfg.GoblinRotAuraRadiusMin * cfg.GoblinRotAuraRadiusMin * cfg.GoblinRotAuraIntensityAtMinRadius;
            float intensity = (float)(totalOutputConstant / Math.Max(1, radius * radius));
            return (radius, intensity);
        }

        /// <summary>
        /// Guard chain copied verbatim from every other rfmechanics goblin behavior
        /// (RFGoblinTunnelBehavior.IsGoblin, GoblinSpitPackingPatch's inline equivalent): the
        /// characterClass null check is load-bearing -- HasTrait returns true for a null class
        /// by default, so classless entities (mobs, un-created characters) must be explicitly
        /// excluded, not left to HasTrait's own default.
        /// </summary>
        private bool IsGoblin()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            return charSys.HasTrait(iplayer, cfg.GoblinTraitCode);
        }

        /// <summary>
        /// WalkBlocks idiom from RFTreeProximityBehavior.GetNearTreeStrength; cylinder falloff
        /// (horizontal/vertical distance handled independently, not a true cube-applies-
        /// uniformly effect), factored into GoblinRotAuraRegistry.SpatialFalloff so this sweep's
        /// geometry and the crop-stunt gate (Task 3) can never drift apart. Spatial falloff and
        /// Intensity are threaded through separately -- AccelerateContents is the only place
        /// they're multiplied together.
        /// </summary>
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

        /// <summary>
        /// Larder hold (Task 2), placed-container path. Thin wrapper around AccelerateSlots --
        /// computes this container's own sealedMod via GetContainingTransitionModifierPlaced,
        /// then delegates the per-slot math to the shared helper so it can never drift from the
        /// carried-inventory sweep's own math.
        /// </summary>
        private void AccelerateContents(BlockEntityContainer beContainer, float spatialFalloff, float intensity, RFMechanicsConfig cfg)
        {
            IWorldAccessor world = entity.World;

            float sealedMod = 1f;
            if (world.BlockAccessor.GetBlock(beContainer.Pos) is BlockContainer bc)
                sealedMod = bc.GetContainingTransitionModifierPlaced(world, beContainer.Pos, EnumTransitionType.Perish);

            AccelerateSlots(beContainer.Inventory, spatialFalloff, intensity, sealedMod, cfg, _ => beContainer.MarkDirty(true));
        }

        /// <summary>
        /// Larder hold (Task 2) shared math, used by both the placed-container sweep
        /// (AccelerateContents) and the carried-inventory sweep (SweepCarriedInventories) so the
        /// per-slot math cannot drift between the two paths.
        ///
        /// Rate model (Task 1): deltaHours is no longer an absolute per-sweep constant (the old
        /// GoblinRotAuraBaseDeltaHoursPerSweep bug -- same hours added regardless of an item's own
        /// transitionHours, which is what produced the ~150x spread between short- and long-lived
        /// foods). Instead it's derived from the calendar: vanilla's own passive aging already
        /// advances TransitionedHours by exactly 1 in-game-hour per in-game-hour elapsed,
        /// independent of transitionHours (confirmed against
        /// reference/upstream/vsapi/Common/Collectible/Collectible.cs:3037-3040 --
        /// UpdateAndGetTransitionStatesNative does `transitionedHours[i] += hoursPassed *
        /// transitionRateMul`, no transitionHours term). So "the aura runs spoilage at Nx normal"
        /// is achieved by adding (N-1) more calendar-hours-per-sweep on top of that already-uniform
        /// 1x baseline -- (N-1), not N, because vanilla's own aging supplies the first 1x for free
        /// (it's baked into state.TransitionedHours by UpdateAndGetTransitionState before this
        /// method ever runs); adding a full Nx on top would make the effective total (N+1)x.
        /// Algebraically this collapses to a transitionHours-independent flat delta -- multiplying
        /// a per-item rate (1/transitionHours) by transitionHours is 1 regardless of the item, so
        /// there is no formulation of "Nx of an item's own rate, expressed as absolute hours" that
        /// keeps a live transitionHours term. Validated against the brief's own sanity check:
        /// redmeat (freshHours 36, transitionHours 24, holdFraction 0.85) at RateMultiplier 3.0
        /// works out to ~37.6 real minutes to the hold ceiling, matching the expected ~40.
        ///
        /// Calendar rate is read live via world.Calendar.SpeedOfTime * world.Calendar.
        /// CalendarSpeedMul -- NOT SpeedOfTime alone. Verified against
        /// reference/upstream/vsessentialsmod/Entity/Behavior/BehaviorHunger.cs:254-255 (vanilla's
        /// own hunger drain scaling): "60 * 0.5 = 30 (SpeedOfTime * CalendarSpeedMul) is the
        /// default" -- the two are independent multipliers, contrary to IGameCalendar.SpeedOfTime's
        /// own doc comment which reads as if CalendarSpeedMul were already folded in. This mod's
        /// own ClimbSaturationPatch.cs already uses the same product for the same reason.
        ///
        /// Two independent effects per slot, both gated on GoblinRotAuraWriteThresholdHours:
        /// (a) hold creep (Task 2) if the stack is already at/over the hold ceiling, and (b) the
        /// acceleration itself below the ceiling. (a)'s gate gets a threshold check on
        /// (TransitionedHours - holdCeilingHours), i.e. how far vanilla's own unconditional aging
        /// has pushed the stack past the ceiling since the last correction -- that quantity is
        /// calendar-driven only (not scaled by intensity or transitionHours), so it reliably clears
        /// the threshold every few sweeps regardless of how weak the goblin's aura is here,
        /// avoiding the write-threshold stall the proportional model would otherwise risk if the
        /// gate were instead on the (small, intensity-scaled) creep delta itself -- the creep
        /// amount is never discarded, just applied less often when it would otherwise be
        /// negligible. See AccelerateSlots' hold-creep block below for the floor that keeps that
        /// amount from decaying to an effective re-clamp at low intensity.
        /// </summary>
        private void AccelerateSlots(IEnumerable<ItemSlot> slots, float spatialFalloff, float intensity,
            float sealedMod, RFMechanicsConfig cfg, Action<ItemSlot> onChanged)
        {
            IWorldAccessor world = entity.World;

            float inGameHoursPerRealHour = world.Calendar.SpeedOfTime * world.Calendar.CalendarSpeedMul;
            float vanillaHoursPerSweep = (float)(cfg.GoblinRotAuraTickInterval / 3600.0) * inGameHoursPerRealHour;
            // (RateMultiplier - 1): vanilla's own aging already supplies the first 1x -- see the
            // class doc comment above for why this isn't a bare RateMultiplier factor.
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
                    // (a) Hold creep (Task 2): past the ceiling, claw back most of vanilla's own
                    // natural drift but retain a small, floored slice of it each correction so the
                    // stack keeps inching toward fully spoiled instead of parking forever. The
                    // floor (GoblinRotAuraHoldCreepFloorHours) exists specifically so a low-
                    // intensity (rot-fed, wide-slow) goblin's tiny creepDelta can't shrink toward
                    // zero and reproduce the old hard clamp.
                    float creepDelta = Math.Max(deltaHours * (float)cfg.GoblinRotAuraHoldCreepFactor, (float)cfg.GoblinRotAuraHoldCreepFloorHours);
                    float retained = Math.Min(overflow, creepDelta);
                    coll.SetTransitionState(slot.Itemstack, EnumTransitionType.Perish, holdCeilingHours + retained);
                    onChanged(slot);
                    continue;
                }

                // (b) Acceleration -- gated by spatialFalloff only (never zero here; callers
                // already filter spatialFalloff <= 0), scaled by intensity for magnitude.
                if (deltaHours <= 0f) continue;

                float candidate = Math.Min(state.TransitionedHours + deltaHours, holdCeilingHours);
                if (candidate - state.TransitionedHours < (float)cfg.GoblinRotAuraWriteThresholdHours) continue;

                coll.SetTransitionState(slot.Itemstack, EnumTransitionType.Perish, candidate);
                onChanged(slot);
            }
        }

        /// <summary>
        /// Carried-inventory path (Task 1 extension): sweeps nearby players' hotbar and worn
        /// backpack contents the same way SweepContainers sweeps BlockEntityContainers. No
        /// exemptions -- includes the goblin's own carried food and every other nearby player's
        /// equally, per the design brief.
        ///
        /// sealedMod is hardcoded 1f, not read from GetContainingTransitionModifierContained --
        /// verified against BlockCrock's own override that the hook answers "how sealed is the
        /// vessel sitting in this slot" (for that vessel's own nested contents), not "how
        /// protective is the inventory this bare stack sits in". Worn bags implement
        /// CollectibleBehaviorHeldBag, not CollectibleBehaviorContainer, so there is no
        /// polymorphic sealing hook to consult here the way GetContainingTransitionModifierPlaced
        /// serves the placed-container sweep -- vanilla has no "sealed backpack" concept, so 1f
        /// is accurate, not an exemption.
        /// </summary>
        private void SweepCarriedInventories(AuraSource src, RFMechanicsConfig cfg)
        {
            if (!cfg.EnableGoblinRotAuraCarriedInventory) return;

            BlockPos min = src.Pos.AddCopy(-src.Radius, -src.VerticalHalfExtent, -src.Radius);
            BlockPos max = src.Pos.AddCopy(src.Radius, src.VerticalHalfExtent, src.Radius);

            // Pure type-check predicate only -- GetEntitiesInsideCuboid's matches delegate isn't
            // documented as call-once, so no side effects belong inside it. Collect first, sweep after.
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
                // Lazy materialization: ReloadBagInventory decodes every stored stack in every
                // worn bag from tree attributes -- a full deserialize, not just an allocation.
                // Skip it entirely when there's no bag equipped at all (the common case).
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
