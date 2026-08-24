using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Shared between the aura fly spawner, the spit fly renderer, and /rfflies: the live-decayed
    /// rotFlies reader and the goblin scan both populations must agree on. Deliberately not a
    /// per-frame cache shared across systems -- the aura spawner (async particle thread) and the
    /// spit renderer (main render thread) run on different cadences, so each calls this fresh;
    /// "walk it once" means one written implementation, not one shared per-tick result.
    /// </summary>
    internal static class GoblinRotFliesShared
    {
        /// <summary>
        /// Sibling to GoblinRotAuraBehavior.ReadLiveRotIntake -- same decay-then-read shape, but
        /// against rfmechanics:rotFlies/rotFliesUpdatedHours, never dietsetup:rotIntake.
        /// </summary>
        internal static float ReadLiveRotFlies(Entity entity, RFMechanicsConfig cfg)
        {
            var wa = entity.WatchedAttributes;
            double nowHours = entity.World.Calendar.TotalHours;
            double lastHours = wa.GetDouble("rfmechanics:rotFliesUpdatedHours", nowHours);
            double raw = wa.GetDouble("rfmechanics:rotFlies", 0.0);
            double elapsed = Math.Max(0.0, nowHours - lastHours);
            return (float)(raw * Math.Pow(0.5, elapsed / cfg.GoblinRotFliesHalfLifeHours));
        }

        /// <summary>
        /// Every loaded EntityPlayer within GoblinRotFliesRange of the local player that passes
        /// the goblin trait check. Box cuboid, not a true sphere -- matches the rot aura sweep's
        /// own precedent (GoblinRotAuraBehavior.SweepCarriedInventories).
        /// </summary>
        internal static List<EntityPlayer> GetNearbyGoblins(ICoreClientAPI capi, RFMechanicsConfig cfg)
        {
            var result = new List<EntityPlayer>();

            EntityPlayer self = capi.World.Player?.Entity;
            if (self == null) return result;

            var charSys = capi.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return result;

            float range = (float)cfg.GoblinRotFliesRange;
            BlockPos min = self.Pos.AsBlockPos.AddCopy(-range, -range, -range);
            BlockPos max = self.Pos.AsBlockPos.AddCopy(range, range, range);

            Entity[] nearby = capi.World.GetEntitiesInsideCuboid(min, max, e => e is EntityPlayer);
            foreach (Entity e in nearby)
            {
                var player = (EntityPlayer)e;

                // Load-bearing: HasTrait returns true for a null class.
                string charClass = player.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) continue;

                IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
                if (iplayer == null) continue;

                if (!charSys.HasTrait(iplayer, cfg.GoblinTraitCode)) continue;

                result.Add(player);
            }

            return result;
        }
    }
}
