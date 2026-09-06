using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace rfmechanics;

/// <summary>Owns the fading ambient aura flies. Spit-charge flies have an independent renderer.</summary>
public class GoblinAuraFliesModSystem : ModSystem
{
    private GoblinFlyRenderer? renderer;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api) => renderer = new GoblinFlyRenderer(api);
    public override void Dispose() { renderer?.Dispose(); base.Dispose(); }
}
