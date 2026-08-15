using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Frenzy: as health drops, an orc with Thew remaining gets faster and hits harder, paid
    /// from the same Thew pool Burn spends from, via curveMult = (1-healthFrac)^
    /// FrenzyCurveExponent, entered/exited off BurnActivationHealthFracGap (shared with Burn
    /// intentionally -- both key off "how far below full health", not their own threshold).
    /// No shared budget with Burn: each independently reads/writes ThewBehavior.Thew and stops
    /// itself once its own floor is no longer affordable, so with both active they can stop at
    /// different moments.
    /// Deliberately does not special-case Band demotion -- a Bulky orc who frenzies and shrinks
    /// mid-fight is the intended self-sequencing behavior, so no guard against it is added.
    /// Structurally mirrors BurnBehavior (temporary fast-tick listener, same JSON-attach/IsOrc
    /// gate) -- see BurnBehavior's header for the rationale.
    /// </summary>
    public class FrenzyBehavior : EntityBehavior
    {
        private const string StatSource = "rf-orc-frenzy";

        private float accum;
        private long fastListenerId = -1;
        private bool frenzied;
        private float thewSpentThisFrenzy;

        // Write-cache of the last value actually pushed via Stats.Set, so ties aren't rewritten.
        private float lastWalkSpeedDelta;
        private float lastMeleeDamageDelta;

        public FrenzyBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rffrenzy";

        public bool Frenzied => frenzied;
        public float ThewSpentThisFrenzy => thewSpentThisFrenzy;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;

            accum += deltaTime;
            if (accum < (float)(cfg?.FrenzySlowTickInterval ?? 6.0)) return;
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
            StopFrenzy();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            StopFrenzy();
        }

        /// <summary>Mirrors BurnBehavior.Evaluate, including reuse of BurnActivationHealthFracGap as the shared entry/exit gate.</summary>
        private void Evaluate()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableFrenzy || !cfg.EnableThew || !entity.Alive || !IsOrc())
            {
                if (frenzied) StopFrenzy();
                return;
            }

            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
            var thewBhv = entity.GetBehavior<ThewBehavior>();
            if (healthBhv == null || thewBhv == null || healthBhv.MaxHealth <= 0f)
            {
                if (frenzied) StopFrenzy();
                return;
            }

            float frac = healthBhv.Health / healthBhv.MaxHealth;
            bool shouldFrenzy = (1f - frac) > (float)cfg.BurnActivationHealthFracGap && thewBhv.Thew > (float)cfg.FrenzyThewFloor;

            if (shouldFrenzy && !frenzied) StartFrenzy();
            else if (!shouldFrenzy && frenzied) StopFrenzy();
        }

        private void StartFrenzy()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            frenzied = true;
            thewSpentThisFrenzy = 0f;
            fastListenerId = entity.World.RegisterGameTickListener(FastTick, cfg.FrenzyFastTickMs, 0);
        }

        private void StopFrenzy()
        {
            if (fastListenerId >= 0)
            {
                entity.World.UnregisterGameTickListener(fastListenerId);
                fastListenerId = -1;
            }
            frenzied = false;

            // Instant clear, no fade -- bonus drops the instant health recovers above the trigger.
            ClearStats();
        }

        /// <summary>Bonus and Thew cost are both driven by the same curveMult from the current
        /// healthFrac, so they escalate together as health drops further within a session.</summary>
        private void FastTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) { StopFrenzy(); return; }

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableFrenzy || !cfg.EnableThew || !entity.Alive)
            {
                StopFrenzy();
                return;
            }

            var healthBhv = entity.GetBehavior<EntityBehaviorHealth>();
            var thewBhv = entity.GetBehavior<ThewBehavior>();
            if (healthBhv == null || thewBhv == null || healthBhv.MaxHealth <= 0f)
            {
                StopFrenzy();
                return;
            }

            float thewAvailable = thewBhv.Thew - (float)cfg.FrenzyThewFloor;
            if (thewAvailable <= 0f)
            {
                StopFrenzy();
                return;
            }

            float healthFrac = healthBhv.Health / healthBhv.MaxHealth;
            float curveMult = (float)Math.Pow(Math.Max(0.0, 1.0 - healthFrac), cfg.FrenzyCurveExponent);

            float thewWanted = (float)cfg.FrenzyMaxThewPerSecond * curveMult * deltaTime;
            float thewToSpend = Math.Min(thewWanted, thewAvailable);
            if (thewToSpend > 0f)
            {
                thewBhv.Thew -= thewToSpend;
                thewSpentThisFrenzy += thewToSpend;
            }

            ApplyStats(cfg, curveMult);

            if ((1f - healthFrac) <= (float)cfg.BurnActivationHealthFracGap || thewBhv.Thew <= (float)cfg.FrenzyThewFloor)
            {
                StopFrenzy();
            }
        }

        /// <summary>Write-threshold-gated: curveMult recomputes every fast tick, so an unconditional Stats.Set every tick would spam WatchedAttributes dirty/sync.</summary>
        private void ApplyStats(RFMechanicsConfig cfg, float curveMult)
        {
            float threshold = (float)cfg.FrenzyStatWriteThreshold;
            float walkSpeedDelta = (float)cfg.FrenzyMaxSpeedBonus * curveMult;
            float meleeDamageDelta = (float)cfg.FrenzyMaxDamageBonus * curveMult;

            if (Math.Abs(walkSpeedDelta - lastWalkSpeedDelta) > threshold)
            {
                entity.Stats.Set("walkspeed", StatSource, walkSpeedDelta);
                lastWalkSpeedDelta = walkSpeedDelta;
            }

            if (Math.Abs(meleeDamageDelta - lastMeleeDamageDelta) > threshold)
            {
                entity.Stats.Set("meleeWeaponsDamage", StatSource, meleeDamageDelta);
                lastMeleeDamageDelta = meleeDamageDelta;
            }
        }

        private void ClearStats()
        {
            entity.Stats.Remove("walkspeed", StatSource);
            entity.Stats.Remove("meleeWeaponsDamage", StatSource);
            lastWalkSpeedDelta = 0f;
            lastMeleeDamageDelta = 0f;
        }

        // charClass null-check is load-bearing: HasTrait treats an unset class as trait-having, not trait-lacking.
        private bool IsOrc()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return false;
            if (entity is not EntityPlayer player) return false;

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            IPlayer? iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) return false;

            return charSys.HasTrait(iplayer, cfg.OrcTraitCode);
        }
    }
}
