using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace rfmechanics
{
    /// <summary>
    /// Telescopic vision: holding right-click with an empty hand eases the FOV down to
    /// ElfZoomFovMult; releasing eases it back to 1.0. No attunement gate -- every elf has this
    /// at all times, gated only by ElfIdentityBehavior.IsElf.
    ///
    /// Recomputes the zoom target every tick from live conditions rather than latching a
    /// press/release flag, so it structurally cannot get stuck -- release, item pickup, opening
    /// a GUI, and death all fall out of the same recompute for free (see EvaluateWantsZoom).
    /// Teleport is deliberately not special-cased: a teleport while right-click is held just
    /// leaves the view zoomed, and release clears it next tick like any other release.
    ///
    /// Attached to /client/behaviors/- ONLY (deviates from seraph-elfidentity.json's dual-side
    /// attach) -- nothing server-side ever calls GetBehavior&lt;RFElfZoomBehavior&gt;(), FOV/camera
    /// state has no server-authoritative counterpart, so a server-side instance would be dead code.
    ///
    /// LOCAL PLAYER ONLY: attached to every humanoid player entity, including remote players'
    /// client-side representations -- EntityControls is synced per-entity so their held-item
    /// animations render correctly. Without the clientWorld.Player.Entity==entity guard, a
    /// nearby player's right-click would drive this client's own static FOV state.
    /// </summary>
    public class RFElfZoomBehavior : EntityBehavior
    {
        private static float currentFovMult = 1f;

        /// <summary>Read by RFElfZoomFovPatch.AdjustFov from static/Harmony-patch context.</summary>
        public static float CurrentFovMult => currentFovMult;

        private float targetFovMult = 1f;
        private float rightMouseHeldMs;

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

        /// <summary>Reused guard idiom from RfDwarfOreSongBehavior/RfGoblinSpitRepairBehavior
        /// (empty-hand check), composed with the vanilla flags that actually decide a click is
        /// already claimed: HandUse != None is set the instant any block's OnBlockInteractStart
        /// returns true (containers included, generically -- SystemMouseInWorldInteractions.cs
        /// TryBeginUseBlock -- not just the block types those two examples touch), and
        /// CurrentEntitySelection != null covers mounting, which bypasses HandUse entirely
        /// (EntityBehaviorSeatable.OnInteract, dispatched directly from
        /// HandleMouseInteractionsNoBlockSelected whenever nothing is block-selected).
        /// ElfZoomEngageDelayMs additionally absorbs the RightMouseDown/HandUse tick-vs-render
        /// scheduling race: RightMouseDown updates on a fixed 20ms tick while HandUse is set from
        /// render-stage interaction dispatch, so a container click can transiently read as
        /// RightMouseDown=true, HandUse=None for a single tick.
        ///
        /// Does not read Controls while mounted -- SystemPlayerControl routes mouse state into
        /// MountedOn.Controls in that case, not the player's own Controls, so zoom simply never
        /// engages on horseback rather than reading stale state. Not addressed here: out of scope.</summary>
        private bool EvaluateWantsZoom(RFMechanicsConfig cfg, float deltaTime)
        {
            if (!entity.Alive) { rightMouseHeldMs = 0f; return false; }

            EntityPlayer player = (EntityPlayer)entity;
            if (!player.Controls.RightMouseDown)
            {
                rightMouseHeldMs = 0f;
                return false;
            }
            rightMouseHeldMs += deltaTime * 1000f;

            IPlayer iplayer = entity.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) return false;
            if (!iplayer.InventoryManager.ActiveHotbarSlot.Empty) return false;
            if (player.Controls.HandUse != EnumHandInteract.None) return false;
            if (iplayer.CurrentEntitySelection != null) return false;

            var identity = entity.GetBehavior<ElfIdentityBehavior>();
            if (identity == null || !identity.IsElf) return false;

            if (entity.Api is ICoreClientAPI capi && !capi.Input.MouseGrabbed) return false;

            return rightMouseHeldMs >= cfg.ElfZoomEngageDelayMs;
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
            rightMouseHeldMs = 0f;
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
