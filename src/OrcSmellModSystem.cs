using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    public class OrcSmellModSystem : ModSystem
    {
        private ICoreClientAPI? capi;
        private SimpleParticleProperties? smellParticle;
        private static bool loggedException = false;
        private bool disabled = false;
        private double accumMs = 0;

        private long lastPeakLogMs = 0;
        private int peakParticles = 0;
        private int peakEntityCount = 0;
        private double peakObservedDist = 0;
        private long peakTickDurationMs = 0;

        private readonly struct SmellSource
        {
            public readonly Entity Entity;
            public readonly double HorDist;
            public readonly double Size;
            public readonly double Radius;
            public readonly double Strength;
            public readonly ScentCategory Category;

            public SmellSource(Entity entity, double horDist, double size, double radius, double strength, ScentCategory category)
            {
                Entity = entity; HorDist = horDist; Size = size; Radius = radius; Strength = strength; Category = category;
            }
        }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            BuildParticleTemplate();
            // Fixed base cadence; the real SmellTickIntervalMs is read live from config inside
            // the tick, not at registration time, since Config may not be loaded yet here.
            capi.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private void BuildParticleTemplate()
        {
            smellParticle = new SimpleParticleProperties
            {
                ParticleModel = EnumParticleModel.Quad,
                Color = ColorUtil.ToRgba(90, 190, 225, 130),
                GravityEffect = 0f,
                // Self-lit so the jet stays visible once the focus fog darkens the ambient
                // light it would otherwise be shaded by (same technique as vanilla
                // LightningFlash, which has the same problem in a dark storm).
                VertexFlags = 255,
                LightEmission = int.MaxValue,
                ShouldDieInLiquid = true,
                WithTerrainCollision = false,
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = new Vec3d(),
            };
            smellParticle.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -1f);
        }

        private void OnGameTick(float dt)
        {
            if (disabled || capi == null || smellParticle == null) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.SmellEnabled) return;

            accumMs += dt * 1000.0;
            if (accumMs < cfg.SmellTickIntervalMs) return;
            accumMs = 0;

            if (!cfg.SmellPassiveEnabled && !OrcSmellShared.FocusActive) return;

            // Fade-in is keyed off continuous hold duration, not FocusWeight -- the world
            // darkens over the full engage ramp, but the smell sense itself lags behind on its
            // own slower schedule (nothing until SmellParticleFadeInStartMs, full opacity at
            // SmellParticleFadeInFullMs).
            float fadeT = GameMath.Clamp(
                (OrcSmellShared.HeldMs - cfg.SmellParticleFadeInStartMs) /
                    (float)(cfg.SmellParticleFadeInFullMs - cfg.SmellParticleFadeInStartMs),
                0f, 1f);
            if (fadeT <= 0f) return;

            try
            {
                long tickStartMs = capi.World.ElapsedMilliseconds;

                EntityPlayer? self = capi.World.Player?.Entity;
                if (self == null) return;

                // Load-bearing: HasTrait returns true for a null class (verified against
                // CharacterSystem.HasTrait, decompiled 1.22).
                string charClass = self.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass)) return;

                var charSys = capi.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null) return;
                if (!charSys.HasTrait(capi.World.Player, cfg.OrcTraitCode)) return;

                Vec3d eye = self.Pos.XYZ;
                eye.Y += self.LocalEyePos.Y;

                List<SmellSource> sources = DetectSources(cfg, self, eye, out int scannedCount);

                int totalSpawned = 0;
                double observedMaxDist = 0;
                foreach (SmellSource src in sources)
                {
                    if (totalSpawned >= cfg.SmellMaxParticles) break;
                    observedMaxDist = Math.Max(observedMaxDist, src.HorDist);
                    totalSpawned += EmitJet(cfg, eye, src, cfg.SmellMaxParticles - totalSpawned, fadeT, self.Pos.Motion);
                }

                long nowMs = capi.World.ElapsedMilliseconds;
                peakParticles = Math.Max(peakParticles, totalSpawned);
                peakEntityCount = Math.Max(peakEntityCount, scannedCount);
                peakObservedDist = Math.Max(peakObservedDist, observedMaxDist);
                peakTickDurationMs = Math.Max(peakTickDurationMs, nowMs - tickStartMs);

                // Observed range/perf ceiling, not just the configured one -- the server's
                // entity-sync range to the client is unknown and may cap detection below the
                // configured scan radius regardless of what SmellRangeBase says.
                if (nowMs - lastPeakLogMs > 60000)
                {
                    capi.Logger?.Debug(
                        "[rfmechanics] OrcSmell 60s peaks: particles={0} entitiesScanned={1} observedMaxDist={2:F1} tickDurationMs={3}",
                        peakParticles, peakEntityCount, peakObservedDist, peakTickDurationMs);
                    lastPeakLogMs = nowMs;
                    peakParticles = 0;
                    peakEntityCount = 0;
                    peakObservedDist = 0;
                    peakTickDurationMs = 0;
                }
            }
            catch (Exception ex)
            {
                disabled = true;
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in OrcSmellModSystem, disabling: {0}", ex);
                }
            }
        }

        /// <summary>Ramps 0..SmellRangeHungerBonus as the local orc's own satFrac drops from 0.50
        /// to 0.0 -- same shape (gate + curve) as FrenzyBehavior's ramp, reused rather than
        /// duplicated since both express "how starved is this orc right now."</summary>
        private static float ComputeHungerRangeBonus(RFMechanicsConfig cfg, EntityPlayer self)
        {
            var hunger = self.GetBehavior<EntityBehaviorHunger>();
            if (hunger == null || hunger.MaxSaturation <= 0f) return 0f;

            float satFrac = hunger.Saturation / hunger.MaxSaturation;
            return (float)cfg.SmellRangeHungerBonus * FrenzyBehavior.ComputeCurveMult(satFrac, cfg);
        }

        private List<SmellSource> DetectSources(RFMechanicsConfig cfg, EntityPlayer self, Vec3d eye, out int scannedCount)
        {
            var result = new List<SmellSource>();
            float hardCap = (float)cfg.SmellRangeHardCap;
            float hungerBonus = ComputeHungerRangeBonus(cfg, self);
            float maxScan = Math.Min(hardCap, (float)(cfg.SmellRangeBase + cfg.SmellRangePerSize * 2.0) + hungerBonus);
            float vertRange = (float)cfg.SmellVerticalRange;

            Entity[] nearby = capi!.World.GetEntitiesAround(eye, maxScan, vertRange,
                e => OrcSmellClassifier.IsSmellableFauna(e));
            scannedCount = nearby.Length;

            foreach (Entity e in nearby)
            {
                if (e.EntityId == self.EntityId) continue;
                if (!e.Alive) continue;

                double size = e.Properties.CollisionBoxSize.X;
                double radius = Math.Min(hardCap, cfg.SmellRangeBase + cfg.SmellRangePerSize * size + hungerBonus);

                double dx = e.Pos.X - eye.X;
                double dz = e.Pos.Z - eye.Z;
                double horDist = Math.Sqrt(dx * dx + dz * dz);

                if (horDist > radius) continue;
                if (Math.Abs(e.Pos.Y - eye.Y) > vertRange) continue;

                double strength = GameMath.Clamp(1.0 - horDist / radius, 0.0, 1.0);
                result.Add(new SmellSource(e, horDist, size, radius, strength, ScentCategory.Unknown));
            }

            result.Sort((a, b) => b.Strength.CompareTo(a.Strength));

            if (result.Count > cfg.SmellMaxSources)
                result.RemoveRange(cfg.SmellMaxSources, result.Count - cfg.SmellMaxSources);

            // Classified only after trimming to SmellMaxSources -- CreatureDiet lookup is wasted
            // work for candidates that never get a jet.
            for (int i = 0; i < result.Count; i++)
            {
                SmellSource s = result[i];
                result[i] = new SmellSource(s.Entity, s.HorDist, s.Size, s.Radius, s.Strength, OrcSmellClassifier.Classify(s.Entity, cfg));
            }

            return result;
        }

        private int EmitJet(RFMechanicsConfig cfg, Vec3d eye, SmellSource src, int remainingBudget, float fadeT, Vec3d selfMotion)
        {
            if (remainingBudget <= 0) return 0;

            int[] rgb = src.Category switch
            {
                ScentCategory.Predator => cfg.SmellColorPredator,
                ScentCategory.Herbivore => cfg.SmellColorHerbivore,
                ScentCategory.Omnivore => cfg.SmellColorOmnivore,
                _ => cfg.SmellColorUnknown,
            };
            smellParticle!.Color = ColorUtil.ToRgba((int)(90 * fadeT), rgb[0], rgb[1], rgb[2]);

            float particleSize = (float)GameMath.Clamp(
                cfg.SmellParticleSizeBase + cfg.SmellParticleSizePerSize * src.Size,
                cfg.SmellParticleSizeMin, cfg.SmellParticleSizeMax);
            smellParticle.MinSize = particleSize;
            smellParticle.MaxSize = particleSize;

            double dx = src.Entity.Pos.X - eye.X;
            double dz = src.Entity.Pos.Z - eye.Z;
            double bearing = Math.Atan2(dz, dx);
            double thickDeg = cfg.SmellThicknessDegPerSize * src.Size;

            // Two-stage spread: a slow widen across the long approach, then a fast smoothstep
            // collapse inside the blowout band. Both curves are anchored to SmellBlowoutStart
            // so they meet without a step in jet width at that distance.
            double f = GameMath.Clamp(
                (cfg.SmellSpreadFarDist - src.HorDist) / Math.Max(0.01, cfg.SmellSpreadFarDist - cfg.SmellBlowoutStart),
                0.0, 1.0);
            double farSpread = GameMath.Lerp(cfg.SmellSpreadFarDeg, cfg.SmellSpreadMidDeg, f);

            double b = GameMath.Clamp(
                (cfg.SmellBlowoutStart - src.HorDist) / Math.Max(0.01, cfg.SmellBlowoutStart - cfg.SmellBlowoutEnd),
                0.0, 1.0);
            double blow = b * b * (3.0 - 2.0 * b);

            double spreadDeg = GameMath.Lerp(farSpread, cfg.SmellSpreadNearDeg, blow);
            double spreadRad = spreadDeg * GameMath.DEG2RAD_DOUBLE;

            // Jet length depends only on the blowout, never on distance to the source -- a jet
            // reaching toward the animal would be a rangefinder, the same class of mistake as
            // spawning a particle at the animal's position.
            double jetLen = GameMath.Lerp(cfg.SmellJetLengthFar, cfg.SmellJetLengthNear, blow);

            // spreadDeg reads absolute distance (placement precision is a fact about geometry);
            // strength reads distance relative to the source's own radius (scent intensity is a
            // fact about the animal) -- a bear and a chicken at 30 blocks share an arc width but
            // not a density.
            double falloff = Math.Pow(src.Strength, cfg.SmellFalloffExponent);
            // SmellParticlesFar is a floor, not a target -- deliberately sparse at range, the
            // opposite of the v1 finding that distance shouldn't cost signal strength. That
            // finding was about a wide arc smearing a low count into noise; a narrow jet stays
            // legible even this sparse.
            double countBase = GameMath.Lerp(cfg.SmellParticlesFar, cfg.SmellParticlesNear, falloff);
            // Same base+per-size clamp shape as the particle-size scaling above, but applied to
            // count instead -- so a chicken's jet reads sparser than a bear's at every distance,
            // not just up close.
            double countSizeFactor = GameMath.Clamp(
                cfg.SmellParticleCountFactorBase + cfg.SmellParticleCountFactorPerSize * src.Size,
                cfg.SmellParticleCountFactorMin, cfg.SmellParticleCountFactorMax);
            double density = countBase * (spreadDeg / cfg.SmellSpreadFarDeg) * countSizeFactor;
            int count = (int)Math.Round(Math.Min(density, cfg.SmellMaxParticlesPerSource));
            count = Math.Min(count, remainingBudget);

            Random rand = capi!.World.Rand;

            for (int i = 0; i < count; i++)
            {
                double t = cfg.SmellJetInnerRadius + rand.NextDouble() * (jetLen - cfg.SmellJetInnerRadius);
                double angle = bearing + (rand.NextDouble() - 0.5) * spreadRad;
                double vOff = (rand.NextDouble() - 0.5) * thickDeg * GameMath.DEG2RAD_DOUBLE * t;

                double px = eye.X + Math.Cos(angle) * t;
                double pz = eye.Z + Math.Sin(angle) * t;
                double py = eye.Y + vOff;

                // Carrying the player's own motion means the particle's spawn-time trajectory
                // stays valid relative to the player as they move -- without this, a player
                // walking toward an incoming jet closes the gap the lifeLength math below
                // assumes is fixed, and particles visibly overshoot into the player. 60x
                // converts Pos.Motion (blocks per 60Hz physics tick) to blocks/sec, the same
                // convention EntityBehaviorPlayerPhysics uses for its own position deltas.
                float vx = (float)(-Math.Cos(angle) * cfg.SmellDriftSpeed + selfMotion.X * 60.0);
                float vz = (float)(-Math.Sin(angle) * cfg.SmellDriftSpeed + selfMotion.Z * 60.0);

                // Cap lifetime to the time-to-inner-radius so the particle fades out at the
                // dead zone instead of drifting through the player (no terrain collision here).
                // Valid in the player's own reference frame since selfMotion above cancels out
                // of the relative approach speed, leaving driftSpeed as the only closing rate.
                double distanceToInner = t - cfg.SmellJetInnerRadius;
                float lifeLength = (float)Math.Min(cfg.SmellParticleLifeSec, distanceToInner / Math.Max(0.01, cfg.SmellDriftSpeed));

                smellParticle!.MinPos.Set(px, py, pz);
                smellParticle.MinVelocity.Set(vx, 0f, vz);
                smellParticle.LifeLength = lifeLength;

                capi.World.SpawnParticles(smellParticle);
            }

            return count;
        }
    }
}
