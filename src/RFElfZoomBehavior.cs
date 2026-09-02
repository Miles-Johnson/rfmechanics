using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace rfmechanics
{
    /// <summary>
    /// Telescopic vision: holding the shared "rfraceability" hotkey (default C) while cached as an
    /// elf eases the FOV down to ElfZoomFovMult; releasing eases it back to 1.0. No attunement gate
    /// -- every elf has this at all times, gated only by PlayerRaceBehavior.IsElf.
    ///
    /// Recomputes the zoom target every tick from live conditions rather than latching a
    /// press/release flag, so it structurally cannot get stuck -- release, opening a GUI, and
    /// death all fall out of the same recompute for free (see EvaluateWantsZoom). Teleport is
    /// deliberately not special-cased: a teleport while the key is held just leaves the view
    /// zoomed, and release clears it next tick like any other release.
    ///
    /// Attached to /client/behaviors/- ONLY (deviates from seraph-elfidentity.json's dual-side
    /// attach) -- nothing server-side ever calls GetBehavior&lt;RFElfZoomBehavior&gt;(), FOV/camera
    /// state has no server-authoritative counterpart, so a server-side instance would be dead code.
    ///
    /// LOCAL PLAYER ONLY: attached to every humanoid player entity, including remote players'
    /// client-side representations -- EntityControls is synced per-entity so their held-item
    /// animations render correctly. Without the clientWorld.Player.Entity==entity guard, a
    /// nearby player's own key state would drive this client's own static FOV state (raw
    /// keyboard state is read from this client's Input, not per-entity, so the guard is what
    /// keeps a remote player's copy of this behavior from also reacting to it).
    /// </summary>
    public class RFElfZoomBehavior : EntityBehavior
    {
        private static float currentFovMult = 1f;

        /// <summary>Read by RFElfZoomFovPatch.AdjustFov from static/Harmony-patch context.</summary>
        public static float CurrentFovMult => currentFovMult;

        private float targetFovMult = 1f;
        private float zoomKeyHeldMs;

        public RFElfZoomBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfzoom";

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Client) return;
            if (entity.World is not IClientWorldAccessor clientWorld || clientWorld.Player?.Entity != entity) return;

            var cfg = RFMechanicsModSystem.Config;
            double transitionMs = cfg?.ElfZoomTransitionMs ?? 400.0;

            targetFovMult = (cfg != null && cfg.EnableElfZoom && EvaluateWantsZoom(cfg, deltaTime))
                ? (float)cfg.ElfZoomFovMult
                : 1f;

            StepTowardTarget(deltaTime, transitionMs);
        }

        /// <summary>Held-key check, same polling shape as OrcSmellFocusModSystem.OnRenderFrame:
        /// reads the resolved key's raw hardware state each tick rather than latching a
        /// press/release event, so it respects live rebinding for free. ElfZoomEngageDelayMs is
        /// kept as a short hold-to-engage delay (no longer absorbing a tick/render scheduling
        /// race -- that only applied to the old RightMouseDown/HandUse trigger -- just a
        /// deliberate small debounce against a stray tap).
        ///
        /// Does not read Controls while mounted, unlike the old RightMouseDown check would have
        /// -- Input.KeyboardKeyStateRaw is independent of SystemPlayerControl's mount routing, so
        /// zoom now works on horseback too.</summary>
        private bool EvaluateWantsZoom(RFMechanicsConfig cfg, float deltaTime)
        {
            if (!entity.Alive) { zoomKeyHeldMs = 0f; return false; }
            if (entity.Api is not ICoreClientAPI capi) return false;

            bool held = capi.Input.HotKeys.TryGetValue("rfraceability", out HotKey hotkey)
                && capi.Input.KeyboardKeyStateRaw[(int)hotkey.CurrentMapping.KeyCode];

            if (!held)
            {
                zoomKeyHeldMs = 0f;
                return false;
            }
            zoomKeyHeldMs += deltaTime * 1000f;

            var identity = entity.GetBehavior<PlayerRaceBehavior>();
            if (identity == null || !identity.IsElf) return false;

            if (!capi.Input.MouseGrabbed) return false;

            return zoomKeyHeldMs >= cfg.ElfZoomEngageDelayMs;
        }

        /// <summary>Linear step-toward, same shape as ElfAttunementBehavior.StepToward -- rate
        /// derived from ElfZoomTransitionMs so a full 1.0-to-target traversal takes that long
        /// regardless of direction.</summary>
        private void StepTowardTarget(float deltaTime, double transitionMs)
        {
            float rate = transitionMs > 0 ? 1f / (float)(transitionMs / 1000.0) : float.MaxValue;
            float delta = rate * deltaTime;

            if (currentFovMult < targetFovMult) currentFovMult = Math.Min(targetFovMult, currentFovMult + delta);
            else if (currentFovMult > targetFovMult) currentFovMult = Math.Max(targetFovMult, currentFovMult - delta);
        }

        /// <summary>Snaps to 1.0 immediately rather than coasting the eased tween down over a
        /// corpse -- the recompute above already zeroes the target via entity.Alive, but without
        /// this override the visual transition still plays out over ElfZoomTransitionMs.</summary>
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            targetFovMult = 1f;
            currentFovMult = 1f;
            zoomKeyHeldMs = 0f;
        }

        /// <summary>Called from RFMechanicsModSystem.Dispose() on client teardown.
        /// EnumDespawnReason.Disconnect is the wrong hook for this: it means "the last player
        /// left the server" (a world-unload signal), not "this client disconnected."</summary>
        public static void ResetStaticState()
        {
            currentFovMult = 1f;
        }
    }
}
