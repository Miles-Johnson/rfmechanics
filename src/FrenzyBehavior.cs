using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Phase 2 (T4): Frenzy -- as health drops, an orc with Thew remaining gets faster and hits
    /// harder, paid for out of the same Thew pool Burn (T3) spends from. Same shape family and
    /// the same threshold-free trigger as Burn: bonus/cost all scale as
    /// (1-healthFrac)^FrenzyCurveExponent, entered/exited via BurnActivationHealthFracGap (shared
    /// with Burn -- both key off "how far below full health", not a separate design threshold of
    /// their own; see BurnActivationHealthFracGap's doc comment on RFMechanicsConfig).
    ///
    /// Composition with Burn (per the brief's own framing -- "both spend the same resource under
    /// the same trigger condition"): no shared budget, no priority ordering. Both read/write the
    /// same clamped ThewBehavior.Thew property independently, the same no-coordination precedent
    /// Burn already established on its own (thew-audit.md Q7) -- each behavior's own fast tick
    /// checks its own floor (BurnThewFloor / FrenzyThewFloor) every tick and stops itself the
    /// moment its own spend is no longer affordable. If Thew runs out mid-fight with both active,
    /// each stops independently and at its own moment, not necessarily simultaneously (their
    /// floors default to the same value but are configured separately).
    ///
    /// Deliberately does NOT special-case Band demotion. Frenzy spends Thew through the same
    /// ThewBehavior.Thew setter every other writer uses; BandBehavior reacts on its own 6s tick
    /// (or resolves multi-step now, see T5's EvaluateBand fix) with no coordination needed here.
    /// A Bulky orc who frenzies and shrinks mid-fight, with Burn's healing shifting as the band
    /// drops, is the intended self-sequencing behaviour and the primary visual tell -- no guard
    /// against it is added, per the brief.
    ///
    /// "Drops the INSTANT health recovers above the trigger band. No duration timer": speed/
    /// damage bonuses are computed fresh every fast tick from the CURRENT healthFrac and
    /// instantly cleared (Stats.Remove, not a fade/lerp) the moment health recovers back above
    /// the trigger gap or Thew runs out -- same instant-clear pattern as
    /// BandBehavior.ClearBandStats.
    ///
    /// Structurally mirrors BurnBehavior throughout (temporary fast-tick listener registered only
    /// while active, entry/exit evaluated on the shared 6s slow tick and immediately on damage,
    /// same JSON-attach + internal IsOrc() gate pattern) -- see BurnBehavior's own header for the
    /// rationale, not repeated here.
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

        /// <summary>Enter/exit check -- mirrors BurnBehavior.Evaluate exactly, including reusing
        /// BurnActivationHealthFracGap as the shared, performance-only entry/exit gate (see class
        /// banner comment on why Burn and Frenzy share this one trigger rather than each having
        /// their own).</summary>
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

            // Instant clear, no fade -- "drops the INSTANT health recovers", per the brief.
            ClearStats();
        }

        /// <summary>Deducts Thew and recomputes the speed/damage bonus every fast tick, both
        /// driven by the SAME curveMult from the CURRENT healthFrac -- so as a frenzied orc takes
        /// further damage mid-session, both the bonus and the cost escalate together, not just at
        /// entry. Unlike BurnBehavior.FastTick, Thew spend here is never partial-tick-clamped
        /// down to "exactly what's affordable" -- it simply stops the moment nothing is left
        /// above the floor, since (unlike Burn's per-hp cost) there is no discrete unit ("one
        /// more hp") to fractionally afford.</summary>
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

        /// <summary>Write-threshold-gated -- Frenzy recomputes continuously (curveMult tracks
        /// live healthFrac every fast tick), so
        /// an unconditional Stats.Set every tick would spam WatchedAttributes dirty/sync.</summary>
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

        /// <summary>Guard chain matching every other rfmechanics race gate -- see
        /// ThewBehavior.IsOrc's own comment for why the null charClass check is load-bearing.</summary>
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
