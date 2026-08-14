using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using rfmechanics.BugRace;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative walkspeed bonus for Goblins tunneling under diggable earth --
    /// same pattern as RFTreeProximityBehavior (Elf), 3s tick (matching that behavior's own
    /// cadence, not ThewBehavior/BandBehavior's 6s -- see notes/goblin-phase-g2-partA-report.md
    /// A3 for why tunnel-speed is mechanically the same shape as tree-proximity, not Thew/Band's).
    ///
    /// Condition is a narrow directional check, not RFTreeProximityBehavior's radius WalkBlocks
    /// scan: two block reads straight up from the player's head (+1, +2), checking for
    /// diggable earth via GoblinSpitPackingPatch.IsGoblinEarth. Was originally a
    /// BlockMaterial-only check (Soil/Sand/Gravel, tier 0), then a standalone Code.Path
    /// prefix list (RFMechanicsConfig.GoblinDiggableEarthCodePrefixes) because the material
    /// check both missed several raw-terrain families and over-matched non-terrain
    /// Soil-material blocks that merely share the material (food/egg, food/cheese,
    /// charcoalpile, coalpile, saltpeter). **G2.1**: the standalone prefix list was itself a
    /// second list drifting out of sync with GoblinSpitPackingPatch.ResolveConversionTarget's
    /// own family set (5 of 10 spit-packed families were missing from it) -- removed in favor
    /// of querying IsGoblinEarth, which is backed directly by ResolveConversionTarget plus an
    /// rfmechanics-domain check for already-converted blocks. See
    /// notes/goblin-dig-materials-handover.md for the drift history.
    ///
    /// Hysteresis: entering the bonus only needs the near (+1) sample to qualify; clearing it
    /// requires BOTH the near and far (+2) samples to fail, so a player standing right at a
    /// tunnel mouth doesn't flicker the bonus on/off every tick.
    ///
    /// Writes entity.Stats.Set("walkspeed", "tunneling", value) -- a source distinct from
    /// "trait"/"treeproximity", so it stacks additively via the WeightedSum blend
    /// instead of overwriting either.
    ///
    /// Attached to the player entity type via a JSON patch (seraph-goblintunnel.json), same
    /// as RFTreeProximityBehavior -- it ticks for every player, and the goblin
    /// gate lives inside IsGoblin(), matching the guard-chain shape every other rfmechanics
    /// race-gated behavior uses.
    /// </summary>
    public class RFGoblinTunnelBehavior : EntityBehavior
    {
        private const float TickInterval = 3.0f;

        private float accum;
        private float lastWalkspeed = 1f;
        private bool underEarth;

        public RFGoblinTunnelBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfgoblintunnel";

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableGoblinTunnelSpeed) return;

            accum += deltaTime;
            if (accum < TickInterval) return;
            accum = 0f;

            if (!IsGoblin())
            {
                // Not a goblin (or no class yet): clear any previously applied bonus rather
                // than leaving it stuck from before a race/class change, and reset the
                // hysteresis state so a later race-swap-back starts clean.
                underEarth = false;
                TrySet(1f, cfg);
                return;
            }

            UpdateHysteresis();

            float walkspeed = 1f + (underEarth ? (float)cfg.GoblinTunnelSpeedBonus : 0f);
            TrySet(walkspeed, cfg);
        }

        /// <summary>
        /// Guard chain matching every other rfmechanics race gate (see
        /// RFTreeProximityBehavior.IsElf): EntityPlayer check, then characterClass null check
        /// (load-bearing -- HasTrait returns true for a null class by default), then the trait
        /// check itself.
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
        /// Two-sample directional check straight up from the player's head. Entry needs only
        /// the near sample; exit needs both samples to fail -- see class doc comment.
        /// "Diggable earth" is now delegated to GoblinSpitPackingPatch.IsGoblinEarth (G2.1) --
        /// this behavior no longer maintains its own Code.Path prefix list. See
        /// notes/goblin-dig-materials-handover.md for the drift that caused (5 of 10
        /// spit-packed families missing from the old GoblinDiggableEarthCodePrefixes list).
        /// </summary>
        private void UpdateHysteresis()
        {
            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            int headY = (int)System.Math.Ceiling(entity.Pos.Y + entity.CollisionBox.Y2);
            BlockPos samplePos = new BlockPos((int)entity.Pos.X, headY + 1, (int)entity.Pos.Z, entity.Pos.Dimension);

            bool nearQualifies = GoblinSpitPackingPatch.IsGoblinEarth(entity.World, blockAccessor.GetBlock(samplePos));

            samplePos.Y = headY + 2;
            bool farQualifies = GoblinSpitPackingPatch.IsGoblinEarth(entity.World, blockAccessor.GetBlock(samplePos));

            if (nearQualifies)
            {
                underEarth = true;
            }
            else if (!farQualifies)
            {
                underEarth = false;
            }
            // else: near failed but far still qualifies -- hold the previous state (hysteresis buffer).
        }

        /// <summary>
        /// Write-threshold gate before Stats.Set, mirroring RFTreeProximityBehavior.TrySet --
        /// Stats.Set marks WatchedAttributes dirty on every
        /// call, so unconditional per-tick writes would cause sync stutter.
        /// </summary>
        private void TrySet(float newValue, RFMechanicsConfig cfg)
        {
            float threshold = (float)cfg.GoblinTunnelStatWriteThreshold;
            if (System.Math.Abs(newValue - lastWalkspeed) <= threshold) return;

            entity.Stats.Set("walkspeed", "tunneling", newValue - 1f);
            lastWalkspeed = newValue;
        }
    }
}
