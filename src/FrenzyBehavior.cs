using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Frenzy: passive, no activation event. Every fast tick, recomputes a ramp from the orc's
    /// current satFrac (curveMult = (1 - satFrac/FrenzySatietyGate)^FrenzyCurveExponent, zero at
    /// the gate, full at satFrac 0) and applies walkspeed/jumpHeightMul deltas scaled by it. The
    /// bonus is free above FrenzyDebtSatietyThreshold; below it, it also incurs FrenzyDebt (see
    /// ThewBehavior.FrenzyDebt) rather than spending Thew directly. Stops entirely only when Thew
    /// is 0 and some debt (Burn or Frenzy) is still outstanding -- resumes on its own once
    /// ThewBehavior's tick-drain or eating brings Thew back above 0.
    /// Deliberately does not special-case Band demotion -- a Bulky orc who frenzies and shrinks
    /// mid-fight is the intended self-sequencing behavior, so no guard against it is added.
    /// </summary>
    public class FrenzyBehavior : EntityBehavior
    {
        private const string StatSource = "rf-orc-frenzy";

        private long fastListenerId = -1;

        // Write-cache of the last values actually pushed via Stats.Set, so ties aren't rewritten.
        private float lastWalkSpeedDelta;
        private float lastJumpBonusDelta;

        public FrenzyBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rffrenzy";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            var cfg = RFMechanicsModSystem.Config;
            int ms = cfg?.FrenzyFastTickMs ?? 500;
            fastListenerId = entity.World.RegisterGameTickListener(FastTick, ms, 0);
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (fastListenerId >= 0)
            {
                entity.World.UnregisterGameTickListener(fastListenerId);
                fastListenerId = -1;
            }
        }

        private void FastTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableFrenzy || !cfg.EnableThew || !entity.Alive || !IsOrc())
            {
                ClearStats();
                return;
            }

            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            var thewBhv = entity.GetBehavior<ThewBehavior>();
            if (hunger == null || hunger.MaxSaturation <= 0f || thewBhv == null)
            {
                ClearStats();
                return;
            }

            if (thewBhv.Thew <= 0f && (thewBhv.BurnDebt + thewBhv.FrenzyDebt) > 0f)
            {
                ClearStats();
                return;
            }

            float satFrac = hunger.Saturation / hunger.MaxSaturation;
            float gate = (float)cfg.FrenzySatietyGate;
            if (satFrac >= gate)
            {
                ClearStats();
                return;
            }

            float t = GameMath.Clamp(1f - satFrac / gate, 0f, 1f);
            float curveMult = (float)Math.Pow(t, cfg.FrenzyCurveExponent);

            if (satFrac < (float)cfg.FrenzyDebtSatietyThreshold)
            {
                float debtIncurred = (float)cfg.FrenzyThewPerSecond * curveMult * deltaTime;
                if (debtIncurred > 0f) thewBhv.FrenzyDebt += debtIncurred;
            }

            ApplyStats(cfg, curveMult);
        }

        /// <summary>Write-threshold-gated: curveMult recomputes every fast tick, so an unconditional Stats.Set every tick would spam WatchedAttributes dirty/sync.</summary>
        private void ApplyStats(RFMechanicsConfig cfg, float curveMult)
        {
            float threshold = (float)cfg.FrenzyStatWriteThreshold;
            float walkSpeedDelta = (float)cfg.FrenzyMaxSpeedBonus * curveMult;
            float jumpBonusDelta = (float)cfg.FrenzyMaxJumpBonus * curveMult;

            if (Math.Abs(walkSpeedDelta - lastWalkSpeedDelta) > threshold)
            {
                entity.Stats.Set("walkspeed", StatSource, walkSpeedDelta);
                lastWalkSpeedDelta = walkSpeedDelta;
            }

            if (Math.Abs(jumpBonusDelta - lastJumpBonusDelta) > threshold)
            {
                entity.Stats.Set("jumpHeightMul", StatSource, jumpBonusDelta);
                lastJumpBonusDelta = jumpBonusDelta;
            }
        }

        private void ClearStats()
        {
            if (lastWalkSpeedDelta != 0f)
            {
                entity.Stats.Remove("walkspeed", StatSource);
                lastWalkSpeedDelta = 0f;
            }
            if (lastJumpBonusDelta != 0f)
            {
                entity.Stats.Remove("jumpHeightMul", StatSource);
                lastJumpBonusDelta = 0f;
            }
        }

        /// <summary>Public static so /rfthew dump reports the exact ramp value FastTick is using.</summary>
        public static float ComputeCurveMult(float satFrac, RFMechanicsConfig cfg)
        {
            float gate = (float)cfg.FrenzySatietyGate;
            if (satFrac >= gate) return 0f;
            float t = GameMath.Clamp(1f - satFrac / gate, 0f, 1f);
            return (float)Math.Pow(t, cfg.FrenzyCurveExponent);
        }

        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
            return RaceTraits.HasTrait(iplayer, cfg.OrcTraitCode);
        }
    }
}
