using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    /// <summary>
    /// Server-authoritative Rested float (0..1) stored in WatchedAttributes, feeding
    /// hungerrate/miningSpeedMul/forageDropRate/wildCropDropRate via Stats.Set (source
    /// "rested") and, optionally, tool durability. Drain (block break, tool use) is
    /// applied externally via Harmony patches calling Drain(); gain (idle, eating) is
    /// applied here in OnGameTick and OnEntityReceiveSaturation.
    /// </summary>
    public class RestedBehavior : EntityBehavior
    {
        private const string WatchedKey = "rfRested";
        private const float TickInterval = 1.0f;

        private float accum;

        // 3b: tracks the cumulative secondsUsed reported by OnHeldInteractStep so tool-use
        // drain can be computed as a per-second rate rather than a per-call flat amount
        // (the underlying method fires roughly every 20ms while a tool is held in use).
        private float toolUseLastSecondsUsed;

        // 3d write-cache: last value actually pushed via Stats.Set, so ties aren't rewritten.
        private float lastMiningSpeedMul = 1f;
        private float lastForageDropRate = 1f;
        private float lastWildCropDropRate = 1f;
        private float lastHungerRate = 1f;

        public RestedBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfrested";

        public float Rested
        {
            get => entity.WatchedAttributes.GetFloat(WatchedKey, 1f);
            set => entity.WatchedAttributes.SetFloat(WatchedKey, GameMath.Clamp(value, 0f, 1f));
        }

        /// <summary>Called by external drain sources (block break, tool use). No-op for non-positive amounts.</summary>
        public void Drain(float amount)
        {
            if (amount <= 0f) return;
            Rested -= amount;
        }

        /// <summary>Called by external/internal gain sources (idle fill, eating). No-op for non-positive amounts.</summary>
        public void Gain(float amount)
        {
            if (amount <= 0f) return;
            Rested += amount;
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableRested) return;

            accum += deltaTime;
            if (accum < TickInterval) return;
            accum = 0f;

            Gain((float)cfg.RestedIdleGainPerSecond * TickInterval);

            ApplyStatOutputs(cfg);
        }

        /// <summary>
        /// Called from RestedToolUsePatch with the cumulative secondsUsed reported by
        /// OnHeldInteractStep. secondsUsed resets to ~0 when a new use session starts
        /// (release then re-hold), detected here as a decrease from the last-seen value.
        /// </summary>
        public void TrackToolUseSeconds(float secondsUsed, float drainPerSecond)
        {
            float delta = secondsUsed < toolUseLastSecondsUsed ? secondsUsed : secondsUsed - toolUseLastSecondsUsed;
            toolUseLastSecondsUsed = secondsUsed;

            if (delta > 0f) Drain(delta * drainPerSecond);
        }

        public override void OnEntityReceiveSaturation(float saturation, EnumFoodCategory foodCat = EnumFoodCategory.Unknown, float saturationLossDelay = 10f, float nutritionGainMultiplier = 1f)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableRested) return;

            Gain((float)cfg.RestedEatGainFlat);
        }

        /// <summary>
        /// Writes the four blended stat channels from the current Rested value, source
        /// "rested", only when the computed value has moved past RestedStatWriteThreshold
        /// since the last write. Stats.Set marks WatchedAttributes "stats" dirty on every
        /// call, so unconditional per-tick writes would cause sync stutter (confirmed via
        /// EntityStats.cs) — this threshold gate is load-bearing, not a micro-optimization.
        /// </summary>
        private void ApplyStatOutputs(RFMechanicsConfig cfg)
        {
            float rested = Rested;
            float threshold = (float)cfg.RestedStatWriteThreshold;

            float miningSpeedMul = 1f + (rested - 0.5f) * 2f * (float)cfg.RestedMiningSpeedBonus;
            float forageDropRate = 1f + (rested - 0.5f) * 2f * (float)cfg.RestedForageDropBonus;
            float wildCropDropRate = 1f + (rested - 0.5f) * 2f * (float)cfg.RestedWildCropDropBonus;
            float hungerRate = 1f - (rested - 0.5f) * 2f * (float)cfg.RestedHungerRateBonus;

            TrySet("miningSpeedMul", miningSpeedMul, threshold, ref lastMiningSpeedMul);
            TrySet("forageDropRate", forageDropRate, threshold, ref lastForageDropRate);
            TrySet("wildCropDropRate", wildCropDropRate, threshold, ref lastWildCropDropRate);
            TrySet("hungerrate", hungerRate, threshold, ref lastHungerRate);
        }

        private void TrySet(string statCode, float newValue, float threshold, ref float lastWritten)
        {
            if (System.Math.Abs(newValue - lastWritten) <= threshold) return;

            entity.Stats.Set(statCode, "rested", newValue - 1f);
            lastWritten = newValue;
        }
    }
}
