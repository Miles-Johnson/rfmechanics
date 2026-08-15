using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using rfmechanics.BugRace;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative walkspeed bonus for Goblins tunneling under diggable earth.
    /// Condition is a narrow directional check, not RFTreeProximityBehavior's radius WalkBlocks
    /// scan: two block reads straight up from the player's head (+1, +2), checking for diggable
    /// earth via GoblinSpitPackingPatch.IsGoblinEarth -- delegated there rather than maintaining
    /// a separate prefix list, so this can never drift out of sync with
    /// GoblinSpitPackingPatch.ResolveConversionTarget's own family set.
    /// Hysteresis: entering the bonus only needs the near sample to qualify; clearing it
    /// requires BOTH samples to fail, so standing at a tunnel mouth doesn't flicker the bonus.
    /// Writes Stats.Set("walkspeed", "tunneling", value) -- a source distinct from
    /// "trait"/"treeproximity", so it stacks additively via the WeightedSum blend.
    /// </summary>
    public class RFGoblinTunnelBehavior : EntityBehavior
    {

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
            if (accum < (float)cfg.GoblinTunnelTickInterval) return;
            accum = 0f;

            if (!IsGoblin())
            {
                // Clears any previously applied bonus and resets hysteresis so a later race-swap-back starts clean.
                underEarth = false;
                TrySet(1f, cfg);
                return;
            }

            UpdateHysteresis();

            float walkspeed = 1f + (underEarth ? (float)cfg.GoblinTunnelSpeedBonus : 0f);
            TrySet(walkspeed, cfg);
        }

        // charClass null-check is load-bearing: HasTrait returns true for a null class by default.
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

        /// <summary>Write-threshold gate before Stats.Set, which marks WatchedAttributes dirty on every call -- unconditional per-tick writes would cause sync stutter.</summary>
        private void TrySet(float newValue, RFMechanicsConfig cfg)
        {
            float threshold = (float)cfg.GoblinTunnelStatWriteThreshold;
            if (System.Math.Abs(newValue - lastWalkspeed) <= threshold) return;

            entity.Stats.Set("walkspeed", "tunneling", newValue - 1f);
            lastWalkspeed = newValue;
        }
    }
}
