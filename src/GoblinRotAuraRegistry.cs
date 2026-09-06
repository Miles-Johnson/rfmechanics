using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// One published aura per goblin, keyed by entity ID. Dictionary, not ConditionalWeakTable:
    /// GetStrengthAt needs to enumerate every live source, and this is cross-behavior broadcast
    /// state, not per-instance bookkeeping.
    /// Intensity is NEVER read by GetStrengthAt/SpatialFalloff -- it shrinks as Radius grows
    /// (rot-fed goblins), which would silently zero out any gating check that multiplied it in
    /// before comparing to a threshold. Consumers gating on "under an aura at all" must use this
    /// class's pure spatial falloff; consumers needing an effect magnitude apply Intensity themselves.
    /// </summary>
    public readonly struct AuraSource
    {
        public BlockPos Pos { get; init; }
        public Vec3d Center { get; init; }
        public double Radius { get; init; }
        public int VerticalHalfExtent { get; init; }
        public float Intensity { get; init; }
        public float Fade { get; init; }
        public long UpdatedMs { get; init; }
    }

    public static class GoblinRotAuraRegistry
    {
        private static readonly Dictionary<long, AuraSource> sources = new();

        public static void SetSource(long entityId, AuraSource source)
        {
            sources[entityId] = source;
        }

        public static void ClearSource(long entityId)
        {
            sources.Remove(entityId);
        }

        internal static void ClearAll() => sources.Clear();

        /// <summary>For the /rfrotaura debug command -- lets an admin confirm an entry disappears on logout rather than inferring it from side effects.</summary>
        public static IReadOnlyDictionary<long, AuraSource> AllSources => sources;

        /// <summary>Max pure spatial falloff (0..1) across every non-stale source, never
        /// multiplied by Intensity. staleAfterMs is the backstop for a missed despawn clear --
        /// without it a stale AuraSource would sit parked forever (a static dictionary has no TTL of its own).</summary>
        public static float GetStrengthAt(BlockPos pos, long nowMs, long staleAfterMs)
        {
            float best = 0f;
            foreach (KeyValuePair<long, AuraSource> kv in sources)
            {
                if (nowMs - kv.Value.UpdatedMs > staleAfterMs) continue;
                float falloff = SpatialFalloff(kv.Value, pos) * kv.Value.Fade;
                if (falloff > best) best = falloff;
            }
            return best;
        }

        /// <summary>Cylinder falloff: horizontal and vertical distance handled independently, not a true cube-applies-uniformly effect.</summary>
        public static float SpatialFalloff(AuraSource src, BlockPos pos)
        {
            return SpatialFalloff(src, new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5), pos.dimension);
        }

        public static float SpatialFalloff(AuraSource src, Vec3d pos, int dimension)
        {
            if (src.Radius <= 0 || src.Center == null || src.Pos.dimension != dimension) return 0;
            double dx = pos.X - src.Center.X, dz = pos.Z - src.Center.Z;
            double dh = System.Math.Sqrt(dx * dx + dz * dz);
            double dv = System.Math.Abs(pos.Y - src.Center.Y);
            return (float)GameMath.Clamp(1.0 - dh / src.Radius, 0.0, 1.0)
                 * (float)GameMath.Clamp(1.0 - dv / (src.VerticalHalfExtent + 0.5), 0.0, 1.0);
        }
    }
}
