using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace rfmechanics
{
    /// <summary>
    /// TEMPORARY -- G4 fly system Step 1 verification only. Delete this file once the
    /// cross-client WatchedAttributes read is confirmed (or the phase concludes).
    /// Registers a client-side-only command so the read happens against this client's own
    /// synced entity state, with no server round-trip -- that's the actual thing being tested.
    /// </summary>
    public class TempFlyCheckDiag : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.ChatCommands.Create("rfflycheck")
                .WithDescription("TEMP: read characterClass/rotIntake/spitCharges off a named player's WatchedAttributes, as seen by this client.")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(api.ChatCommands.Parsers.Word("playername"))
                .HandleWith(args =>
                {
                    string targetName = (string)args[0];
                    IPlayer target = null;
                    foreach (var p in api.World.AllOnlinePlayers)
                    {
                        if (p.PlayerName == targetName) { target = p; break; }
                    }
                    if (target == null)
                        return TextCommandResult.Success($"No online player named '{targetName}' found in this client's player list.");

                    EntityPlayer entity = target.Entity;
                    if (entity == null)
                        return TextCommandResult.Success($"Found player '{targetName}' but this client has no Entity for them (not loaded/tracked?).");

                    string charClass = entity.WatchedAttributes.GetString("characterClass", null);
                    bool hasRotIntake = entity.WatchedAttributes.HasAttribute("dietsetup:rotIntake");
                    double rotIntake = entity.WatchedAttributes.GetDouble("dietsetup:rotIntake", -1);
                    bool hasSpitCharges = entity.WatchedAttributes.HasAttribute("rfmechanics:spitCharges");
                    int spitCharges = entity.WatchedAttributes.GetInt("rfmechanics:spitCharges", -1);

                    return TextCommandResult.Success(
                        $"characterClass={(charClass ?? "<null/default>")} | " +
                        $"dietsetup:rotIntake present={hasRotIntake} value={rotIntake} | " +
                        $"rfmechanics:spitCharges present={hasSpitCharges} value={spitCharges}");
                });
        }
    }
}
