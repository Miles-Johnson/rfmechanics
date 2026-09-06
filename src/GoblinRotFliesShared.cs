using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Discovers server-published auras for the combined renderer. The client need not reconstruct
    /// another player's race or have access to their inventory/CharacterSystem state.
    /// </summary>
    internal static class GoblinRotFliesShared
    {
        /// <summary>
        /// Every nearby player with an active server aura, plus self even if the client spatial
        /// index excludes its own player. More Bugs' inventory scanning is deliberately unrelated.
        /// </summary>
        internal static List<EntityPlayer> GetNearbyGoblins(ICoreClientAPI capi, RFMechanicsConfig cfg, bool chargesOnly = false)
        {
            var result = new List<EntityPlayer>();

            EntityPlayer? self = capi.World.Player?.Entity;
            if (self == null) return result;

            float range = (float)GoblinAuraMath.FiniteClamp(cfg.GoblinRotFliesRange, 32, 16, 48);
            BlockPos min = self.Pos.AsBlockPos.AddCopy(-range, -range, -range);
            BlockPos max = self.Pos.AsBlockPos.AddCopy(range, range, range);

            Entity[] nearby = capi.World.GetEntitiesInsideCuboid(min, max, e => e is EntityPlayer);
            foreach (Entity e in nearby)
            {
                var player = (EntityPlayer)e;

                if (player.Pos.Dimension == self.Pos.Dimension && HasVisual(player, cfg, chargesOnly))
                    result.Add(player);
            }

            if (HasVisual(self, cfg, chargesOnly) && !result.Exists(p => p.EntityId == self.EntityId)) result.Add(self);

            return result;
        }

        private static bool HasVisual(EntityPlayer player, RFMechanicsConfig cfg, bool chargesOnly) => chargesOnly
            ? player.Alive && cfg.EnableGoblinSpitFlies && player.WatchedAttributes.GetInt("rfmechanics:spitCharges") > 0
                && RaceTraits.HasTrait(player.Player, cfg.GoblinTraitCode)
            : GoblinRotAuraState.ReadVisual(player).Active;
    }
}
