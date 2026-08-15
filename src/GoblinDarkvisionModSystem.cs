using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Constant, no-drawback darkvision for goblins. Client-side only, no server component --
    /// characterClass/extraTraits are WatchedAttributes synced to the owning client, so
    /// CharacterSystem.HasTrait works here without a round-trip.
    /// Composes with vanilla's ModSystemNightVision by Math.Max on
    /// ShaderUniforms.NightVisionStrength rather than racing it -- this renderer never writes
    /// 0f itself, only raises the uniform when the trait check passes, since two unconditional
    /// writers would be a last-writer-wins race (goggles zeroed by our "not a goblin" branch or
    /// vice versa). RenderOrder 0.1 (vanilla is 0.0, lower draws first) guarantees this runs
    /// after vanilla within the "Before" stage, so the max-compose is the frame's final write.
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

                // No class = not a goblin; overrides HasTrait's null-class-returns-true default.
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
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
