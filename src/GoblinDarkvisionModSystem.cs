using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Constant, no-drawback darkvision for goblins. Client-side only, no server component --
    /// characterClass/extraTraits are WatchedAttributes, synced to the owning client, so
    /// CharacterSystem.HasTrait works from here without any round-trip.
    ///
    /// Composes with vanilla's own ModSystemNightVision (night-vision goggles) instead of
    /// racing it. Both write ICoreClientAPI.Render.ShaderUniforms.NightVisionStrength every
    /// frame -- confirmed the only other writer in the installed 1.21.5 binary
    /// (VSSurvivalMod/ModSystemNightVision.cs:74-91) -- and the field itself is a plain public
    /// float with no clamp/blend/accumulation logic downstream
    /// (VintagestoryAPI/DefaultShaderUniforms.cs:30). Two unconditional writers would be a
    /// last-writer-wins race (a non-goblin's goggles could get zeroed by our own "not a goblin"
    /// branch, or vice versa), so this renderer never writes 0f itself -- it only raises the
    /// uniform via Math.Max when the trait check passes, and leaves it alone otherwise. RenderOrder
    /// 0.1 (vanilla's ModSystemNightVision is 0.0, and IRenderer.RenderOrder's own doc confirms
    /// "0 = drawn first, 1 = drawn last") guarantees this renderer's OnRenderFrame runs after
    /// vanilla's within the "Before" stage, so the max-compose is the authoritative final write
    /// for the frame regardless of goggle state.
    /// </summary>
    public class GoblinDarkvisionModSystem : ModSystem, IRenderer
    {
        private ICoreClientAPI? capi;
        private static bool loggedException = false;

        public double RenderOrder => 0.1;
        public int RenderRange => 1;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "rfgoblindarkvision");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi == null)
                return;

            try
            {
                var cfg = RFMechanicsModSystem.Config;
                if (cfg == null || !cfg.EnableGoblinDarkvision)
                    return;

                IPlayer? player = capi.World.Player;
                if (player?.Entity == null)
                    return;

                // Class guard: no class = not a goblin (overrides HasTrait's
                // null-class-returns-true default). Same guard shape as every other
                // rfmechanics race gate (FallDamagePatch, TreeClimbingPatch).
                string charClass = player.Entity.WatchedAttributes.GetString("characterClass");
                if (string.IsNullOrEmpty(charClass))
                    return;

                var charSys = capi.ModLoader.GetModSystem<CharacterSystem>();
                if (charSys == null)
                    return;

                if (!charSys.HasTrait(player, cfg.GoblinTraitCode))
                    return;

                capi.Render.ShaderUniforms.NightVisionStrength =
                    Math.Max(capi.Render.ShaderUniforms.NightVisionStrength, (float)cfg.GoblinDarkvisionStrength);
            }
            catch (Exception ex)
            {
                if (!loggedException)
                {
                    loggedException = true;
                    capi.Logger?.Warning("[rfmechanics] Exception in GoblinDarkvisionModSystem: {0}", ex);
                }
                // Leave the uniform unchanged on exception.
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
