using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative walkspeed bonus for Elves standing near living trees. Scans a
    /// radius for "log-grown"-prefixed blocks (vanilla's convention for a standing tree log vs.
    /// a cut/placed one) with inverse-distance falloff, same scan shape as
    /// EntityBehaviorBodyTemperature.getNearHeatSourceStrength.
    /// Writes Stats.Set("walkspeed", "treeproximity", value) -- a source distinct from "trait"
    /// (rf-elf-positive's own flat bonus), so it stacks additively via the WeightedSum blend.
    /// </summary>
    public class RFTreeProximityBehavior : EntityBehavior
    {

        private float accum;
        private float lastWalkspeed = 1f;

        public RFTreeProximityBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rftreeproximity";

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableTreeProximitySpeed) return;

            accum += deltaTime;
            if (accum < (float)cfg.TreeProximityTickInterval) return;
            accum = 0f;

            if (!IsElf())
            {
                // Clears any previously applied bonus rather than leaving it stuck from before a race/class change.
                TrySet(1f, cfg);
                return;
            }

            float strength = GetNearTreeStrength(cfg.TreeProximityRadius);
            float walkspeed = 1f + strength * (float)cfg.TreeProximityMaxBonus;
            TrySet(walkspeed, cfg);
        }

        // charClass null-check is load-bearing: HasTrait returns true for a null class by default.
        private bool IsElf()
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

            return charSys.HasTrait(iplayer, cfg.ElfTraitCode);
        }

        /// <summary>"log-grown" prefix identifies a standing tree log; cut/placed logs and firewood are excluded on purpose.</summary>
        private float GetNearTreeStrength(int radius)
        {
            BlockPos centerPos = entity.Pos.AsBlockPos;
            BlockPos min = centerPos.AddCopy(-radius, -radius, -radius);
            BlockPos max = centerPos.AddCopy(radius, radius, radius);

            double px = entity.Pos.X;
            double py = entity.Pos.Y + 0.9;
            double pz = entity.Pos.Z;

            float strength = 0f;
            entity.World.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
            {
                if (block?.Code?.Path == null || !block.Code.Path.StartsWith("log-grown")) return;

                double dx = x + 0.5 - px;
                double dy = y + 0.5 - py;
                double dz = z + 0.5 - pz;
                double distSq = dx * dx + dy * dy + dz * dz;

                strength += Math.Min(1f, 9f / (8f + (float)Math.Pow(distSq, 1.25)));
            });

            return GameMath.Clamp(strength, 0f, 1f);
        }

        /// <summary>Write-threshold gate before Stats.Set, which marks WatchedAttributes dirty on every call -- unconditional per-tick writes would cause sync stutter.</summary>
        private void TrySet(float newValue, RFMechanicsConfig cfg)
        {
            float threshold = (float)cfg.TreeProximityStatWriteThreshold;
            if (Math.Abs(newValue - lastWalkspeed) <= threshold) return;

            entity.Stats.Set("walkspeed", "treeproximity", newValue - 1f);
            lastWalkspeed = newValue;
        }
    }
}
