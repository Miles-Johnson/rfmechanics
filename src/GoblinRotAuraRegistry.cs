using System.Collections.Generic;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// One published aura per goblin, keyed by entity ID. Small static dictionary, not
    /// ConditionalWeakTable -- this needs enumeration (GetStrengthAt walks every live source)
    /// and is cross-behavior broadcast state, not per-instance bookkeeping tied to a single
    /// behavior's own lifetime.
    ///
    /// Intensity is a magnitude-only scalar -- it is NEVER read by GetStrengthAt/SpatialFalloff.
    /// Task 4 drives Intensity down as Radius grows (rot-fed goblins: wide-and-thin), which
    /// would silently zero out any gating check that multiplied strength by Intensity before
    /// comparing against a fixed threshold. Consumers that gate on "is this position under an
    /// aura at all" (e.g. the crop-stunt behavior) must use the pure spatial falloff this class
    /// returns; consumers that need an actual effect magnitude (e.g. spoilage acceleration)
    /// apply Intensity themselves, separately, at the point they compute that magnitude.
    /// </summary>
    public readonly struct AuraSource
    {
        public BlockPos Pos { get; init; }
        public int Radius { get; init; }
        public int VerticalHalfExtent { get; init; }
        public float Intensity { get; init; }
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

        /// <summary>
        /// Read-only snapshot of every currently registered source, keyed by entity ID -- for
        /// the /rfrotaura registry debug command only. Lets an admin directly confirm a
        /// goblin's entry disappears on logout instead of inferring it from side effects
        /// (crops resuming growth, containers no longer accelerating).
        /// </summary>
        public static IReadOnlyDictionary<long, AuraSource> AllSources => sources;

        /// <summary>
        /// Pure spatial falloff (0..1) over every non-stale registered source -- the maximum
        /// across all of them, never multiplied by Intensity. Sources older than staleAfterMs
        /// are skipped: entries are explicitly cleared on despawn, but this is the backstop for
        /// anything that misses that (an exception before the clear runs, death handling that
        /// doesn't despawn the entity, etc.) -- without it a missed clear would leave a stale
        /// AuraSource parked at its last position forever, since a static dictionary has no TTL
        /// of its own.
        /// </summary>
        public static float GetStrengthAt(BlockPos pos, long nowMs, long staleAfterMs)
        {
            float best = 0f;
            foreach (KeyValuePair<long, AuraSource> kv in sources)
            {
                if (nowMs - kv.Value.UpdatedMs > staleAfterMs) continue;
                float falloff = SpatialFalloff(kv.Value, pos);
                if (falloff > best) best = falloff;
            }
            return best;
        }

        /// <summary>
        /// Cylinder falloff: horizontal and vertical distance handled independently, not a true
        /// cube-applies-uniformly effect. Factored out so the sweep (Task 1) and the crop-stunt
        /// gate (Task 3) can never drift apart from each other's geometry.
        /// </summary>
        public static float SpatialFalloff(AuraSource src, BlockPos pos)
        {
            double dh = System.Math.Sqrt(System.Math.Pow(pos.X + 0.5 - src.Pos.X, 2) + System.Math.Pow(pos.Z + 0.5 - src.Pos.Z, 2));
            double dv = System.Math.Abs(pos.Y + 0.5 - src.Pos.Y);
            return (float)GameMath.Clamp(1.0 - dh / src.Radius, 0.0, 1.0)
                 * (float)GameMath.Clamp(1.0 - dv / (src.VerticalHalfExtent + 1), 0.0, 1.0);
        }
    }
}
