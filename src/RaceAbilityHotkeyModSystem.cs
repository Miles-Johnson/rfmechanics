using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace rfmechanics
{
    /// <summary>
    /// Single hotkey ("rfraceability", default C) replacing the four old per-race V-bound keys
    /// (orcsmellfocus, rfelfzoom, rfdwarforesong, rfgoblinspit) -- those four all defaulted to the
    /// same key on the assumption that races are mutually exclusive per player, which never
    /// actually isolated them from third-party mods also bound to V (see
    /// notes/slowwalkmod-orcsmell-hotkey-crash.md). Those four code strings must never be
    /// reused: a removed hotkey code's clientsettings.json rebind entry is orphaned forever, not
    /// cleaned up, so re-registering one would silently resurrect a player's old rebind under new
    /// semantics.
    ///
    /// Only handles the two discrete-press abilities (dwarf, goblin). Orc smell focus and elf zoom
    /// are held ramps driven by their own render/tick pollers (OrcSmellFocusModSystem,
    /// RFElfZoomBehavior), which read this same "rfraceability" code's raw key state directly and
    /// gate on the same cached PlayerRaceBehavior.Race -- they never go through SetHotKeyHandler.
    /// </summary>
    public class RaceAbilityHotkeyModSystem : ModSystem
    {
        // One ability per race is assumed, for both the press table below and the held abilities
        // handled elsewhere -- a race needing a second ability needs a redesign here, not a second
        // dictionary entry or a second hotkey.
        private static readonly Dictionary<PlayerRace, System.Func<ICoreClientAPI, bool>> PressAbilities = new()
        {
            [PlayerRace.Dwarf] = api => api.ModLoader.GetModSystem<DwarfOreSongModSystem>().TryTrigger(api),
            [PlayerRace.Goblin] = api => api.ModLoader.GetModSystem<RFMechanicsModSystem>().TryTriggerGoblinSpit(api),
        };

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            api.Input.RegisterHotKey("rfraceability", "Race Ability", GlKeys.C, HotkeyType.CharacterControls);
            api.Input.SetHotKeyHandler("rfraceability", _ => Dispatch(api));
        }

        /// <summary>Race lookup uses the cached path (PlayerRaceBehavior.Race), never a fresh
        /// RaceTraits.HasTrait call -- a race with no ability, including Human, falls through to
        /// the dictionary miss below and returns false so other mods bound to C still see the
        /// press.</summary>
        private static bool Dispatch(ICoreClientAPI api)
        {
            IPlayer? player = api.World.Player;
            PlayerRace race = player?.Entity?.GetBehavior<PlayerRaceBehavior>()?.Race ?? PlayerRace.None;

            return PressAbilities.TryGetValue(race, out var ability) && ability(api);
        }
    }
}
