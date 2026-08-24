using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    public class OrcSmellFocusModSystem : ModSystem, IRenderer
    {
        private ICoreClientAPI? capi;
        private AmbientModifier? ambientMod;
        private static bool loggedException = false;

        public double RenderOrder => 0.1;
        public int RenderRange => 1;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Input.RegisterHotKey("orcsmellfocus", "Orc Smell Focus", GlKeys.V, HotkeyType.CharacterControls);

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
                    OrcSmellShared.HeldMs = 0f;
                    ApplyWeight();
                    return;
                }

                bool eligible = false;
                if (!capi.IsGamePaused)
                {
                    IPlayer? player = capi.World.Player;
                    if (player?.Entity != null)
                    {
                        // Load-bearing: HasTrait returns true for a null class (verified against
                        // CharacterSystem.HasTrait, decompiled 1.22) -- the explicit class-string
                        // check below overrides that permissive default for the focus gate.
                        string charClass = player.Entity.WatchedAttributes.GetString("characterClass");
                        if (!string.IsNullOrEmpty(charClass))
                        {
                            var charSys = capi.ModLoader.GetModSystem<CharacterSystem>();
                            if (charSys != null && charSys.HasTrait(player, cfg.OrcTraitCode))
                            {
                                eligible = true;
                            }
                        }
                    }
                }

                bool held = false;
                if (eligible && capi.Input.HotKeys.TryGetValue("orcsmellfocus", out HotKey hotkey))
                {
                    held = capi.Input.KeyboardKeyStateRaw[(int)hotkey.CurrentMapping.KeyCode];
                }

                OrcSmellShared.HeldMs = held ? OrcSmellShared.HeldMs + deltaTime * 1000f : 0f;

                float rampMs = held ? cfg.SmellFocusEngageMs : cfg.SmellFocusReleaseMs;
                RampToward(held ? 1f : 0f, rampMs, deltaTime);

                OrcSmellShared.FocusActive = OrcSmellShared.FocusWeight > 0.01f;

                ambientMod.FogDensity.Value = (float)cfg.SmellFocusFogDensity;
                ApplyWeight();
            }
            catch (Exception ex)
            {
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
            capi?.Ambient.CurrentModifiers.Remove("orcsmellfocus");
            base.Dispose();
        }
    }
}
