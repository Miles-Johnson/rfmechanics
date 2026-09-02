using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Sole source of "what race is this player" -- no float, no thresholds, no census. Attached
    /// to every player entity on both sides (seraph-elfidentity.json) -- BranchyLeavesPassthroughPatch's
    /// per-substep read needs the client-side instance too; a server-only attach was the exact bug
    /// fixed in elf-leaf-passthrough-troubleshooting-handover.md. Registered under the historical
    /// "rfelfidentity" key (originally elf-only, now race-general) -- kept as-is since it's an
    /// entity-behavior JSON attachment key, not user-facing.
    /// </summary>
    public class PlayerRaceBehavior : EntityBehavior
    {
        private const string HungerDrainStatSource = "rf-elf-attunement";

        private float accum;

        /// <summary>Cached race result. Nothing outside this behavior may walk
        /// CharacterSystem.HasTrait on a hot path -- every consumer reads this field directly.</summary>
        public PlayerRace Race { get; private set; }

        /// <summary>Derived from Race rather than stored separately, so there's exactly one cache.</summary>
        public bool IsElf => Race == PlayerRace.Elf;

        public PlayerRaceBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfidentity";

        /// <summary>Runs once per entity (re)creation, before the first OnGameTick -- refreshes
        /// Race immediately so consumers (leaf filter, zoom, tree proximity, step height, race
        /// ability hotkey) never read a false negative for the first tick interval after chunk
        /// load/reconnect.</summary>
        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            RefreshRaceCache();
        }

        public override void OnGameTick(float deltaTime)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            accum += deltaTime;
            if (accum < (float)cfg.ElfIdentityTickInterval) return;
            accum = 0f;

            RefreshRaceCache();

            if (entity.World.Side == EnumAppSide.Server) ApplyOrClearHungerDrain(cfg);
        }

        /// <summary>Re-derives the hunger-drain stat every identity tick rather than only on a
        /// change. ElfAttunementBehavior never cleared this key on despawn (only flushed its own
        /// float), so a save carrying a stale entry from before this rename self-heals within one
        /// tick interval instead of needing a one-time migration pass.</summary>
        private void ApplyOrClearHungerDrain(RFMechanicsConfig cfg)
        {
            if (!IsElf || !cfg.EnableElfHungerDrainReduction)
            {
                entity.Stats.Remove("hungerrate", HungerDrainStatSource);
                return;
            }

            entity.Stats.Set("hungerrate", HungerDrainStatSource, (float)cfg.ElfHungerRateMult - 1f);
        }

        /// <summary>Races are mutually exclusive per player (per commit 11d2e0e's own stated
        /// assumption), so the first trait match wins -- order among the four doesn't matter in
        /// practice, only that all four get checked.</summary>
        private void RefreshRaceCache()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) { Race = PlayerRace.None; return; }
            if (entity is not EntityPlayer player) { Race = PlayerRace.None; return; }

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);

            if (RaceTraits.HasTrait(iplayer, cfg.ElfTraitCode)) Race = PlayerRace.Elf;
            else if (RaceTraits.HasTrait(iplayer, cfg.DwarfTraitCode)) Race = PlayerRace.Dwarf;
            else if (RaceTraits.HasTrait(iplayer, cfg.OrcTraitCode)) Race = PlayerRace.Orc;
            else if (RaceTraits.HasTrait(iplayer, cfg.GoblinTraitCode)) Race = PlayerRace.Goblin;
            else Race = PlayerRace.None;
        }
    }
}
