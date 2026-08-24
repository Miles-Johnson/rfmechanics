using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Burn-to-Survive: an orc above BurnThewFloor heals via a cubic curve with no activation
    /// threshold (see FastTick), Thew-gated not band-gated. Healing incurs BurnDebt rather than
    /// spending Thew directly (see ThewBehavior.BurnDebt) -- Thew only gates whether burn can
    /// run at all. Deliberately stacks with vanilla's saturation-throttled regen -- a starving
    /// orc near death still burns.
    /// Owns a *temporary* fast tick listener (BurnFastTickMs), registered only while burn
    /// conditions hold rather than running unconditionally every tick like ThewBehavior/
    /// BandBehavior; see BurnActivationHealthFracGap's doc comment for the entry/exit gate.
    /// Attached via seraph-thew.json after the vanilla "health" behavior, so
    /// EntityBehaviorHealth has already applied the hit by the time this behavior's own
    /// OnEntityReceiveDamage override runs.
    /// </summary>
    public class BurnBehavior : EntityBehavior
    {
        /// <summary>Reference bar for /rfthew dump: vanilla base player MaxHealth (player.json), not this orc's actual band-boosted MaxHealth.</summary>
        public const float ReferenceBarHp = 15f;


        private float accum;
        private long fastListenerId = -1;
        private bool burning;
        private float debtIncurredThisBurn;

        public BurnBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfburn";

        public bool Burning => burning;
        public float DebtIncurredThisBurn => debtIncurredThisBurn;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;

            accum += deltaTime;
            if (accum < (float)(cfg?.BurnSlowTickInterval ?? 6.0)) return;
            accum = 0f;

            Evaluate();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);
            if (entity.World.Side != EnumAppSide.Server) return;

            Evaluate();
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            StopBurn();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            StopBurn();
        }

        /// <summary>Idempotent -- re-entering while already burning, or exiting while already stopped, are both no-ops.</summary>
        private void Evaluate()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableBurn || !cfg.EnableThew || !entity.Alive || !IsOrc())
            {
                if (burning) StopBurn();
                return;
            }

            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
            var thewBhv = entity.GetBehavior<ThewBehavior>();
            if (healthBhv == null || thewBhv == null || healthBhv.MaxHealth <= 0f)
            {
                if (burning) StopBurn();
                return;
            }

            // BurnActivationHealthFracGap is a performance-only cutoff (skips the fast tick when
            // the cubic curve's effect would be imperceptible) -- not a game-design threshold.
            float frac = healthBhv.Health / healthBhv.MaxHealth;
            bool shouldBurn = (1f - frac) > (float)cfg.BurnActivationHealthFracGap && thewBhv.Thew > (float)cfg.BurnThewFloor;

            if (shouldBurn && !burning) StartBurn();
            else if (!shouldBurn && burning) StopBurn();
        }

        private void StartBurn()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            burning = true;
            debtIncurredThisBurn = 0f;
            fastListenerId = entity.World.RegisterGameTickListener(FastTick, cfg.BurnFastTickMs, 0);
        }

        private void StopBurn()
        {
            if (fastListenerId >= 0)
            {
                entity.World.UnregisterGameTickListener(fastListenerId);
                fastListenerId = -1;
            }
            burning = false;
        }

        private void FastTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) { StopBurn(); return; }

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableBurn || !cfg.EnableThew || !entity.Alive)
            {
                StopBurn();
                return;
            }

            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
            var thewBhv = entity.GetBehavior<ThewBehavior>();
            if (healthBhv == null || thewBhv == null || healthBhv.MaxHealth <= 0f)
            {
                StopBurn();
                return;
            }

            if (thewBhv.Thew <= (float)cfg.BurnThewFloor)
            {
                StopBurn();
                return;
            }

            // Recomputed fresh from the CURRENT healthFrac each tick (not cached from StartBurn)
            // so the rate escalates smoothly as health drops within the same burn session. No
            // longer capped by available Thew -- healing incurs debt (BurnDebt) instead of
            // spending Thew directly, so there's no per-tick budget to run out of.
            float healthFrac = healthBhv.Health / healthBhv.MaxHealth;
            float curveMult = (float)Math.Pow(Math.Max(0.0, 1.0 - healthFrac), cfg.BurnCurveExponent);
            float hpToApply = (float)cfg.BurnMaxHealPerSecond * curveMult * deltaTime;
            float debtIncurred = hpToApply * (float)cfg.BurnThewPerHp;

            thewBhv.BurnDebt += debtIncurred;
            debtIncurredThisBurn += debtIncurred;
            healthBhv.Health = Math.Min(healthBhv.Health + hpToApply, healthBhv.MaxHealth);

            float newFrac = healthBhv.Health / healthBhv.MaxHealth;
            if ((1f - newFrac) <= (float)cfg.BurnActivationHealthFracGap)
            {
                StopBurn();
            }
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
