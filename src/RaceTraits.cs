using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace rfmechanics
{
    // None covers both "no character class yet" and "human" -- there's no human trait code to
    // check against, and every consumer only needs to distinguish "has one of the four abilities"
    // from "doesn't."
    public enum PlayerRace { None, Elf, Dwarf, Orc, Goblin }

    public static class RaceTraits
    {
        /// <summary>CharacterSystem.HasTrait returns true for a null/unset characterClass
        /// (vssurvivalmod Character.cs:568) -- an unresolvable-but-non-null class code already
        /// falls through to false on its own, so only the null case needs an explicit override
        /// here. Resolved via iplayer.Entity.Api (set per-instance at entity spawn) rather than
        /// RFMechanicsModSystem.Api's static, which last-writer-wins between the client/server
        /// instances in singleplayer (notes/diagnostics/ore-song-discovery.md Q3) and is unsafe
        /// for anything side-sensitive.</summary>
        public static bool HasTrait(IPlayer? iplayer, string traitCode)
        {
            if (iplayer?.Entity?.Api == null) return false;

            string charClass = iplayer.Entity.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) return false;

            var charSys = iplayer.Entity.Api.ModLoader.GetModSystem<CharacterSystem>();
            return charSys != null && charSys.HasTrait(iplayer, traitCode);
        }
    }
}
