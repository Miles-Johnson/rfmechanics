using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Vanilla-particle aura fly population (Phase G4 Step 3): a density-weighted cloud around
    /// each nearby goblin. Count is driven by rfmechanics:rotFlies; radius/shape is read live from
    /// dietsetup:rotIntake through GoblinRotAuraBehavior.ComputeShape, so the visible cloud always
    /// matches the invisible spoilage-acceleration field's own footprint.
    /// Follows ModSystemAmbientParticles: one SimpleParticleProperties template, spawned through
    /// the async particle manager so this never costs main-render-thread time.
    /// </summary>
    public class GoblinAuraFliesModSystem : ModSystem
    {
        private ICoreClientAPI? capi;
        private SimpleParticleProperties? flyParticle;
        private readonly Dictionary<long, Vec3d> centroidLag = new();
        private static bool loggedException = false;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            flyParticle = new SimpleParticleProperties
            {
                ParticleModel = EnumParticleModel.Quad,
                Color = ColorUtil.ToRgba(220, 45, 32, 20),
                GravityEffect = 0f,
                ShouldDieInLiquid = true,
                WithTerrainCollision = true,
                RandomVelocityChange = true,
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = new Vec3d(),
            };
            flyParticle.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.CLAMPEDPOSITIVESINUS, GameMath.PI);

            capi.Event.RegisterAsyncParticleSpawner(AsyncParticleSpawnTick);
        }

        private bool AsyncParticleSpawnTick(float dt, IAsyncParticleManager manager)
        {
            if (capi == null || flyParticle == null) return true;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinRotFlies) return true;

                List<EntityPlayer> goblins = GoblinRotFliesShared.GetNearbyGoblins(capi, cfg);
                double nowTotalSeconds = capi.World.ElapsedMilliseconds / 1000.0;
                Random rand = capi.World.Rand;

                foreach (EntityPlayer goblin in goblins)
                {
                    float f = GameMath.Clamp(GoblinRotFliesShared.ReadLiveRotFlies(goblin, cfg), 0f, (float)cfg.GoblinRotFliesCap);
                    if (f < cfg.GoblinRotFliesFloor)
                    {
                        centroidLag.Remove(goblin.EntityId);
                        continue;
                    }

                    int count = (int)Math.Round(GameMath.Lerp(cfg.GoblinRotFliesCountMin, cfg.GoblinRotFliesCountMax, f / (float)cfg.GoblinRotFliesCap));

                    float t = GameMath.Clamp(GoblinRotAuraBehavior.ReadLiveRotIntake(goblin, cfg), 0f, 1f);
                    (int radius, _) = GoblinRotAuraBehavior.ComputeShape(cfg, t);
                    int vHalfExtent = cfg.GoblinRotAuraVerticalHalfExtent;

                    // Per-goblin phase (from entityId) so two goblins' clouds don't breathe in lockstep.
                    double phase = (goblin.EntityId % 360) * (Math.PI / 180.0);
                    double breathScale = 1.0 + cfg.GoblinRotFliesBreathAmplitude * Math.Sin(2.0 * Math.PI * nowTotalSeconds / cfg.GoblinRotFliesBreathPeriod + phase);
                    float spawnRadius = (float)(radius * breathScale);

                    Vec3d target = goblin.Pos.XYZ;
                    Vec3d centroid;
                    if (!centroidLag.TryGetValue(goblin.EntityId, out centroid!))
                    {
                        centroid = target;
                    }
                    else
                    {
                        double lagFrac = cfg.GoblinRotFliesLagSeconds <= 0 ? 1.0 : GameMath.Clamp(dt / cfg.GoblinRotFliesLagSeconds, 0.0, 1.0);
                        centroid = centroid + (target - centroid) * lagFrac;
                    }
                    centroidLag[goblin.EntityId] = centroid;

                    // Continuous spawn rate that balances against LifeLength to hold ~count particles alive at steady state.
                    float spawnThisTick = count * dt / (float)cfg.GoblinRotFliesLifeSeconds;
                    int spawnCount = GameMath.RoundRandom(rand, spawnThisTick);

                    for (int i = 0; i < spawnCount; i++)
                    {
                        SampleWeightedCylinderOffset(rand, spawnRadius, vHalfExtent, out double ox, out double oy, out double oz);

                        flyParticle.MinPos!.Set(centroid.X + ox, centroid.Y + oy, centroid.Z + oz);
                        flyParticle.MinSize = (float)cfg.GoblinRotFliesSize;
                        flyParticle.MaxSize = (float)cfg.GoblinRotFliesSize;
                        flyParticle.LifeLength = (float)cfg.GoblinRotFliesLifeSeconds;
                        flyParticle.addLifeLength = (float)cfg.GoblinRotFliesLifeSeconds * 0.25f;

                        const float jitter = 0.05f;
                        flyParticle.MinVelocity.Set(
                            (float)(rand.NextDouble() * jitter * 2 - jitter),
                            (float)(rand.NextDouble() * jitter * 2 - jitter),
                            (float)(rand.NextDouble() * jitter * 2 - jitter));
                        flyParticle.AddVelocity.Set(0, 0, 0);

                        manager.Spawn(flyParticle);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in GoblinAuraFliesModSystem, disabling: {0}", ex);
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Rejection-sampled offset within a cylinder, density-weighted by a linear falloff that
        /// matches GoblinRotAuraRegistry.SpatialFalloff's shape but with the horizontal edge
        /// pushed 15% past the nominal radius, so the cloud thins toward the boundary instead of
        /// stopping at a visibly hard edge. Vertical already carries its own +1-block tail via the
        /// same (vHalfExtent+1) denominator the aura's own falloff uses.
        /// </summary>
        private static void SampleWeightedCylinderOffset(Random rand, float radius, int vHalfExtent, out double ox, out double oy, out double oz)
        {
            float extRadius = Math.Max(0.1f, radius * 1.15f);
            float vExtent = vHalfExtent + 1;

            double horizDist, weight;
            int tries = 0;
            do
            {
                horizDist = extRadius * Math.Sqrt(rand.NextDouble());
                weight = GameMath.Clamp(1.0 - horizDist / extRadius, 0.0, 1.0);
            } while (rand.NextDouble() > weight && ++tries < 6);

            double angle = rand.NextDouble() * 2.0 * Math.PI;

            double vy;
            tries = 0;
            do
            {
                vy = (rand.NextDouble() * 2.0 - 1.0) * vExtent;
                weight = GameMath.Clamp(1.0 - Math.Abs(vy) / vExtent, 0.0, 1.0);
            } while (rand.NextDouble() > weight && ++tries < 6);

            ox = Math.Cos(angle) * horizDist;
            oz = Math.Sin(angle) * horizDist;
            oy = vy;
        }
    }
}
