using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace rfmechanics
{
    public class OrcSmellFocusModSystem : ModSystem, IRenderer
    {
        private ICoreClientAPI? capi;
        private AmbientModifier? ambientMod;
        private static bool loggedException = false;
        private OrcSmellFocusState focus = new();
        private long playerId;
        private int hurtCounter;
        private Vec3d? previousPos;
        private double speedDistance, speedTime, measuredSpeed;

        public double RenderOrder => 0.05;
        public int RenderRange => 1;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            ambientMod = new AmbientModifier
            {
                FogColor = new WeightedFloatArray(new float[] { 0f, 0f, 0f }, 0f),
                AmbientColor = new WeightedFloatArray(new float[] { 0f, 0f, 0f }, 0f),
                FogDensity = new WeightedFloat(0f, 0f),
            }.EnsurePopulated();
            api.Ambient.CurrentModifiers["orcsmellfocus"] = ambientMod;

            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "rforcsmellfocus");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi == null || ambientMod == null) return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.SmellEnabled)
                {
                    OrcSmellShared.FocusWeight = 0f;
                    OrcSmellShared.FocusActive = false;
                    OrcSmellShared.HeldMs = OrcSmellShared.Quality = 0f;
                    focus = new();
                    previousPos = null;
                    ApplyWeight();
                    return;
                }

                bool eligible = false;
                if (!capi.IsGamePaused)
                {
                    IPlayer? player = capi.World.Player;
                    if (player?.Entity?.Alive == true && player.Entity.GetBehavior<PlayerRaceBehavior>()?.Race == PlayerRace.Orc)
                    {
                        eligible = true;
                    }
                }

                bool held = false;
                if (eligible && capi.Input.HotKeys.TryGetValue("rfraceability", out HotKey hotkey))
                {
                    held = capi.Input.KeyboardKeyStateRaw[(int)hotkey.CurrentMapping.KeyCode];
                }

                var self = capi.World.Player?.Entity;
                bool stationary = false, hurt = false, interrupted = false;
                if (self != null)
                {
                    int counter = self.WatchedAttributes.GetInt("onHurtCounter");
                    if (playerId != self.EntityId)
                    {
                        playerId = self.EntityId;
                        hurtCounter = counter;
                        focus = new();
                        previousPos = null;
                        speedDistance = speedTime = measuredSpeed = 0;
                    }
                    hurt = counter != hurtCounter;
                    hurtCounter = counter;
                    var controls = self.Controls;
                    double dx = previousPos == null ? 0 : self.Pos.X - previousPos.X;
                    double dz = previousPos == null ? 0 : self.Pos.Z - previousPos.Z;
                    double tolerance = 0.12 * Math.Max(0.001, deltaTime);
                    double moved = Math.Sqrt(dx*dx + dz*dz);
                    speedDistance += moved;
                    speedTime += Math.Max(0, deltaTime);
                    if (speedTime >= 0.2)
                    {
                        measuredSpeed = speedDistance / speedTime;
                        speedDistance = speedTime = 0;
                    }
                    // Average rendered displacement so physics tick steps do not look like sprints.
                    double actualSpeed = Math.Max(measuredSpeed,
                        60 * Math.Sqrt(self.Pos.Motion.X*self.Pos.Motion.X + self.Pos.Motion.Z*self.Pos.Motion.Z));
                    interrupted = !self.OnGround || self.Swimming || controls.IsClimbing || controls.IsFlying
                        || controls.Jump || controls.Sprint || moved > 0.75 || actualSpeed > cfg.SmellWalkingMaxSpeed;
                    stationary = !interrupted && !controls.TriesToMove && dx*dx + dz*dz <= tolerance*tolerance
                        && self.Pos.Motion.X*self.Pos.Motion.X + self.Pos.Motion.Z*self.Pos.Motion.Z <= 0.000004;
                    previousPos = self.Pos.XYZ;
                }
                float oldQuality = OrcSmellShared.Quality;
                focus.Update(deltaTime, held, eligible, stationary, hurt, interrupted);
                OrcSmellShared.Quality = focus.Quality(cfg.SmellParticleFadeInFullMs);
                if (focus.Active && oldQuality > 0 && OrcSmellShared.Quality == 0) OrcSmellShared.TrailGeneration++;
                OrcSmellShared.HeldMs = focus.HeldMs;

                float darkness = focus.Darkness(cfg.SmellFocusEngageMs, (float)cfg.SmellWalkingVisionWeight);
                // The rising target already follows the long acquisition clock. Restore useful
                // vision quickly when walking, releasing, or breaking concentration.
                if (darkness >= OrcSmellShared.FocusWeight) OrcSmellShared.FocusWeight = darkness;
                else RampToward(darkness, cfg.SmellFocusReleaseMs, deltaTime);

                OrcSmellShared.FocusActive = focus.Active;

                ambientMod.FogDensity.Value = (float)cfg.SmellFocusFogDensity * OrcSmellShared.FocusWeight * OrcSmellShared.FocusWeight;
                ApplyWeight();
            }
            catch (Exception ex)
            {
                OrcSmellShared.FocusActive = false;
                OrcSmellShared.FocusWeight = OrcSmellShared.HeldMs = OrcSmellShared.Quality = 0;
                ApplyWeight();
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in OrcSmellFocusModSystem: {0}", ex);
                }
            }
        }

        private static void RampToward(float target, float rampMs, float deltaTime)
        {
            float step = rampMs > 0 ? deltaTime * 1000f / rampMs : 1f;
            float delta = GameMath.Clamp(target - OrcSmellShared.FocusWeight, -step, step);
            OrcSmellShared.FocusWeight = GameMath.Clamp(OrcSmellShared.FocusWeight + delta, 0f, 1f);
        }

        private void ApplyWeight()
        {
            float w = OrcSmellShared.FocusWeight;
            ambientMod!.FogColor.Weight = w;
            ambientMod.AmbientColor.Weight = w;
            ambientMod.FogDensity.Weight = w;
        }

        public override void Dispose()
        {
            OrcSmellShared.FocusActive = false;
            OrcSmellShared.FocusWeight = OrcSmellShared.HeldMs = OrcSmellShared.Quality = 0;
            capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            capi?.Ambient.CurrentModifiers.Remove("orcsmellfocus");
            base.Dispose();
        }
    }
}
