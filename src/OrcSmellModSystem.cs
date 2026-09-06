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
        private OrcSmellRenderer? renderer;
        private long tickListener;
        private List<SmellSource> sources = new();
        private readonly Dictionary<long, double> emissionCredit = new();
        private bool scannedSources;
        private int trailGeneration;
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
            renderer = new OrcSmellRenderer(api);
            // Fixed base cadence; the real SmellTickIntervalMs is read live from config inside
            // the tick, not at registration time, since Config may not be loaded yet here.
            tickListener = capi.Event.RegisterGameTickListener(OnGameTick, 50);
        }

        private void OnGameTick(float dt)
        {
            if (disabled || capi == null || renderer == null) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.SmellEnabled) return;

            if (!OrcSmellShared.FocusActive || capi.IsGamePaused)
            {
                sources.Clear(); emissionCredit.Clear(); accumMs = 0; scannedSources = false;
                return;
            }
            if (trailGeneration != OrcSmellShared.TrailGeneration)
            {
                trailGeneration = OrcSmellShared.TrailGeneration;
                scannedSources = false;
                emissionCredit.Clear();
            }
            accumMs += Math.Min(dt, 0.1f) * 1000.0;

            // Fade-in is keyed off continuous hold duration, not FocusWeight -- the world
            // darkens over the full engage ramp, but the smell sense itself lags behind on its
            // own slower schedule (nothing until SmellParticleFadeInStartMs, full opacity at
            // SmellParticleFadeInFullMs).
            float fadeT = GameMath.Clamp(
                (OrcSmellShared.HeldMs - cfg.SmellParticleFadeInStartMs) /
                    Math.Max(1f, cfg.SmellParticleFadeInFullMs - cfg.SmellParticleFadeInStartMs),
                0f, 1f);
            if (fadeT <= 0f) return;

            try
            {
                long tickStartMs = capi.World.ElapsedMilliseconds;

                EntityPlayer? self = capi.World.Player?.Entity;
                if (self == null) return;

                if (!RaceTraits.HasTrait(capi.World.Player, cfg.OrcTraitCode)) return;

                Vec3d eye = self.Pos.XYZ;
                eye.Y += self.LocalEyePos.Y;

                int scannedCount = 0;
                if (!scannedSources || accumMs >= Math.Max(100, cfg.SmellTickIntervalMs))
                {
                    sources = DetectSources(cfg, self, eye, out scannedCount);
                    scannedSources = true;
                    accumMs = 0;
                    var ids = new HashSet<long>();
                    foreach (var source in sources) ids.Add(source.Entity.EntityId);
                    foreach (long id in new List<long>(emissionCredit.Keys))
                        if (!ids.Contains(id)) emissionCredit.Remove(id);
                }

                int totalSpawned = 0;
                double observedMaxDist = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    SmellSource src = sources[i];
                    if (totalSpawned >= cfg.SmellMaxParticles) break;
                    observedMaxDist = Math.Max(observedMaxDist, src.HorDist);

                    // Sources are strength-sorted (closest first), so a naive shared budget
                    // starves the farthest source's floor once nearby jets fill it. Reserve each
                    // not-yet-processed source's floor before handing out the rest -- safe as long
                    // as SmellMaxParticles >= SmellParticlesFar * SmellMaxSources.
                    int reserveForRest = (sources.Count - i - 1) * cfg.SmellParticlesFar;
                    int budgetForThis = Math.Max(0, cfg.SmellMaxParticles - totalSpawned - reserveForRest);
                    totalSpawned += EmitJet(cfg, eye, src, budgetForThis, fadeT, Math.Min(dt, 0.1f));
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
                        "[rfmechanics] OrcSmell 60s peaks: budgetPerScan={0} entitiesScanned={1} observedMaxDist={2:F1} tickDurationMs={3} liveNow={4}",
                        peakParticles, peakEntityCount, peakObservedDist, peakTickDurationMs, renderer.Count);
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
            double rangeScale = OrcSmellVisuals.RangeScale(OrcSmellShared.Quality, cfg.SmellWalkingRangeScale);
            maxScan *= (float)rangeScale;
            float vertRange = (float)(cfg.SmellVerticalRange * rangeScale);

            Entity[] nearby = capi!.World.GetEntitiesAround(eye, maxScan, vertRange,
                e => OrcSmellClassifier.IsSmellableFauna(e));
            scannedCount = nearby.Length;

            foreach (Entity e in nearby)
            {
                if (e.EntityId == self.EntityId) continue;
                if (!e.Alive) continue;

                double size = e.Properties.CollisionBoxSize.X;
                double radius = Math.Min(hardCap, cfg.SmellRangeBase + cfg.SmellRangePerSize * size + hungerBonus) * rangeScale;

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

        private int EmitJet(RFMechanicsConfig cfg, Vec3d eye, SmellSource src, int remainingBudget, float fadeT, float emissionDt)
        {
            if (remainingBudget <= 0 || !src.Entity.Alive) return 0;

            int[] rgb = src.Category switch
            {
                ScentCategory.Predator => cfg.SmellColorPredator,
                ScentCategory.Herbivore => cfg.SmellColorHerbivore,
                ScentCategory.Omnivore => cfg.SmellColorOmnivore,
                _ => cfg.SmellColorUnknown,
            };


            double visualScale = OrcSmellVisuals.BodyScale(src.Entity.CollisionBox.XSize,
                src.Entity.CollisionBox.YSize, cfg.SmellVisualSizeExponent);
            float particleSize = OrcSmellVisuals.ParticleSize(visualScale, cfg);

            double dx = src.Entity.Pos.X - eye.X;
            double dz = src.Entity.Pos.Z - eye.Z;
            double bearing = Math.Atan2(dz, dx);
            double thickDeg = cfg.SmellThicknessDegPerSize * visualScale;

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
            // Same base+per-size clamp shape as the particle-size scaling above, but applied to
            // count instead -- so a chicken's jet reads sparser than a bear's at every distance,
            // not just up close.
            double countSizeFactor = GameMath.Clamp(
                cfg.SmellParticleCountFactorBase + cfg.SmellParticleCountFactorPerSize * visualScale,
                cfg.SmellParticleCountFactorMin, cfg.SmellParticleCountFactorMax);
            // SmellParticlesFar is added on top of growth, not blended into it, so every source
            // reads as exactly that many particles at its own max range regardless of size or
            // spread width -- size/spread/distance shaping only applies to growth above the floor.
            double growth = (cfg.SmellParticlesNear - cfg.SmellParticlesFar) * falloff
                * (spreadDeg / cfg.SmellSpreadFarDeg) * countSizeFactor;
            double density = cfg.SmellParticlesFar + growth;
            // Even in the near blowout, a small animal must not saturate the same cap as a large one.
            double sizeCap = cfg.SmellMaxParticlesPerSource * Math.Clamp(countSizeFactor / 1.25, 0.25, 1);
            int count = (int)Math.Round(Math.Min(density, sizeCap));
            // Partial focus loses density and bearing precision, as well as detection range.
            count = Math.Max(cfg.SmellParticlesFar, (int)Math.Round(count * (0.35 + 0.65 * OrcSmellShared.Quality)));
            spreadRad *= 1 + 0.8 * (1 - OrcSmellShared.Quality);
            count = Math.Min(count, remainingBudget);
            // Counts remain the budget per detection interval. Fractional credits preserve even
            // the far-source floor when spreading that budget over 50 ms emissions.
            emissionCredit.TryGetValue(src.Entity.EntityId, out double credit);
            credit += count * emissionDt / Math.Max(0.1, cfg.SmellTickIntervalMs / 1000.0);
            int emitCount = (int)credit;
            emissionCredit[src.Entity.EntityId] = credit - emitCount;

            Random rand = capi!.World.Rand;

            for (int i = 0; i < emitCount; i++)
            {
                bool nearApproach = rand.NextDouble() < 0.35;
                double inner = Math.Max(2.0, cfg.SmellJetInnerRadius);
                double outer = Math.Max(inner + 0.1, nearApproach ? 4.0 : jetLen);
                double t = inner + rand.NextDouble() * (outer - inner);
                double angle = bearing + (rand.NextDouble() - 0.5) * spreadRad
                    + (rand.NextDouble() - 0.5) * thickDeg * GameMath.DEG2RAD_DOUBLE;
                double vOff = (rand.NextDouble() - 0.5) * thickDeg * GameMath.DEG2RAD_DOUBLE * t;

                double px = eye.X + Math.Cos(angle) * t;
                double pz = eye.Z + Math.Sin(angle) * t;
                double py = eye.Y + vOff;

                double speed = Math.Clamp(cfg.SmellDriftSpeed, 0.1, 4) * (0.85 + rand.NextDouble()*0.3);
                double life = Math.Clamp(cfg.SmellParticleLifeSec, 0.5, 8) * (0.85 + rand.NextDouble()*0.3);
                renderer!.Add(src.Entity.EntityId, sources.Count, new Vec3d(px, py, pz), angle, speed, life, particleSize,
                    (90f / 255f) * fadeT * (0.45f + 0.55f * OrcSmellShared.Quality), rgb);
            }

            return count;
        }
        public override void Dispose()
        {
            capi?.Event.UnregisterGameTickListener(tickListener);
            renderer?.Dispose();
            sources.Clear(); emissionCredit.Clear();
            base.Dispose();
        }
    }
}
