using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Phase 4 (rewritten Phase 2 T3): Burn-to-Survive. An orc with Thew remaining above
    /// BurnThewFloor burns Thew to heal, at a rate that follows a cubic curve with NO activation
    /// threshold: heal/s = BurnMaxHealPerSecond * (1-healthFrac)^BurnCurveExponent. Near full
    /// health the curve is imperceptibly small; it escalates as health drops, fastest right at
    /// death's door. This supersedes the original flat/threshold model (heal only below
    /// BurnHealthFraction, at a constant rate) -- see BurnHealthFraction/BurnHealPerSecond's own
    /// doc comments for what changed and why. Thew-gated, not band-gated: any band burns if Thew
    /// remains. Stacks with vanilla's own saturation-throttled regen (untouched) and with the
    /// starvation shield (ThewShieldPatch) -- a starving orc near death burns from both
    /// simultaneously, which is intended and will be fast.
    ///
    /// Kept as its own EntityBehavior (same rationale as BandBehavior's own separation from
    /// ThewBehavior, see BandBehavior's header) because it owns something neither of the other
    /// two do: a *temporary* fast (BurnFastTickMs, default 500ms) game-tick listener, registered
    /// only while burn conditions hold. Entry/exit is evaluated on the shared 6s slow tick (same
    /// cadence as ThewBehavior/BandBehavior, by convention) and immediately on damage received,
    /// rather than gating a listener that runs unconditionally every tick like the other two
    /// behaviors -- the "no activation threshold" design property does NOT mean this listener
    /// runs permanently for every orc regardless of health; see BurnActivationHealthFracGap's doc
    /// comment for why a listener still needs a (purely performance-motivated) entry/exit gate.
    /// The fast tick itself only ever touches Health and Thew -- never Stats, never entitySize;
    /// Bands react to Thew dropping through their own hysteresis on their own tick with no
    /// special-casing needed here (per the brief: if it doesn't, that's a Phase 3 bug).
    ///
    /// Same JSON-attach-to-every-player + internal IsOrc() gate pattern as ThewBehavior/
    /// BandBehavior -- attached via seraph-thew.json, appended after the vanilla
    /// "health" behavior in the server behaviors list, so by the time this behavior's own
    /// OnEntityReceiveDamage override runs (Entity.ReceiveDamage loops every behavior in list
    /// order, findings doc §4/§the ReceiveDamage source), EntityBehaviorHealth has already
    /// applied the hit and Health reflects it.
    /// </summary>
    public class BurnBehavior : EntityBehavior
    {
        /// <summary>Reference "bar" for the /rfthew dump projection -- vanilla base player
        /// MaxHealth (player.json), not this orc's actual (band-boosted) MaxHealth. Matches the
        /// brief's own "full vanilla 15-hp bar" framing for BurnThewPerHp's doc comment.</summary>
        public const float ReferenceBarHp = 15f;

        private const float SlowTickInterval = 6.0f; // matches ThewBehavior/BandBehavior cadence

        private float accum;
        private long fastListenerId = -1;
        private bool burning;
        private float thewSpentThisBurn;

        public BurnBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfburn";

        public bool Burning => burning;
        public float ThewSpentThisBurn => thewSpentThisBurn;

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            accum += deltaTime;
            if (accum < SlowTickInterval) return;
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

        /// <summary>Enter/exit check -- cheap, safe to call from both the slow tick and on
        /// damage-received. Idempotent: re-entering while already burning, or exiting while
        /// already stopped, are both no-ops.</summary>
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

            // T3: no game-design activation threshold anymore -- BurnActivationHealthFracGap is a
            // performance-only cutoff (skips registering the fast tick when the cubic curve's
            // effect would be imperceptible), not the old BurnHealthFraction design gate.
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
            thewSpentThisBurn = 0f;
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

        /// <summary>Deducts Thew and grants HP atomically per fast tick. When the remaining
        /// Thew above BurnThewFloor can't cover the full tick's heal, heals proportionally to
        /// what's affordable, spends exactly that much Thew, then stops.</summary>
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

            float thewAvailable = thewBhv.Thew - (float)cfg.BurnThewFloor;
            if (thewAvailable <= 0f)
            {
                StopBurn();
                return;
            }

            // T3: cubic curve, recomputed fresh every fast tick from the CURRENT healthFrac (not
            // cached from StartBurn) -- heal/s = BurnMaxHealPerSecond * (1-healthFrac)^
            // BurnCurveExponent, so the rate itself escalates smoothly as health continues to
            // drop within the same burn session, not just at entry.
            float healthFrac = healthBhv.Health / healthBhv.MaxHealth;
            float curveMult = (float)Math.Pow(Math.Max(0.0, 1.0 - healthFrac), cfg.BurnCurveExponent);
            float hpWanted = (float)cfg.BurnMaxHealPerSecond * curveMult * deltaTime;
            float thewPerHp = (float)cfg.BurnThewPerHp;
            float thewNeeded = hpWanted * thewPerHp;

            float hpToApply;
            float thewToSpend;
            if (thewPerHp <= 0f || thewNeeded <= thewAvailable)
            {
                hpToApply = hpWanted;
                thewToSpend = thewNeeded;
            }
            else
            {
                // Partial final tick -- only enough Thew above the floor remains.
                thewToSpend = thewAvailable;
                hpToApply = thewAvailable / thewPerHp;
            }

            thewBhv.Thew -= thewToSpend;
            thewSpentThisBurn += thewToSpend;
            healthBhv.Health = Math.Min(healthBhv.Health + hpToApply, healthBhv.MaxHealth);

            float newFrac = healthBhv.Health / healthBhv.MaxHealth;
            if ((1f - newFrac) <= (float)cfg.BurnActivationHealthFracGap || thewBhv.Thew <= (float)cfg.BurnThewFloor)
            {
                StopBurn();
            }
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
