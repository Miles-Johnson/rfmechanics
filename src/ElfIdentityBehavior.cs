using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Replaces ElfAttunementBehavior (archived) as the sole source of "is this player an elf" --
    /// no float, no thresholds, no census. Attached to every player entity on both sides
    /// (seraph-elfidentity.json) -- BranchyLeavesPassthroughPatch's per-substep read needs the
    /// client-side instance too; a server-only attach was the exact bug fixed in
    /// elf-leaf-passthrough-troubleshooting-handover.md.
    /// </summary>
    public class ElfIdentityBehavior : EntityBehavior
    {
        private const string HungerDrainStatSource = "rf-elf-attunement";

        private float accum;

        /// <summary>Cached elf-race result. Nothing outside this behavior may walk
        /// CharacterSystem.HasTrait on a hot path -- every consumer reads this field directly.</summary>
        public bool IsElf { get; private set; }

        public ElfIdentityBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfidentity";

        /// <summary>Runs once per entity (re)creation, before the first OnGameTick -- refreshes
        /// IsElf immediately so consumers (leaf filter, zoom, tree proximity, step height) never
        /// read a false negative for the first tick interval after chunk load/reconnect.</summary>
        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            RefreshElfCache();
        }

        public override void OnGameTick(float deltaTime)
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            accum += deltaTime;
            if (accum < (float)cfg.ElfIdentityTickInterval) return;
            accum = 0f;

            RefreshElfCache();

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

        /// <summary>
        /// Guard chain matching every other rfmechanics elf gate (copied from
        /// ElfAttunementBehavior.RefreshElfCache): EntityPlayer check, then characterClass null
        /// check (load-bearing -- HasTrait returns true for a null class by default), then the
        /// trait check itself.
        /// </summary>
        private void RefreshElfCache()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) { IsElf = false; return; }
            if (entity is not EntityPlayer player) { IsElf = false; return; }

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) { IsElf = false; return; }

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) { IsElf = false; return; }

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) { IsElf = false; return; }

            IsElf = charSys.HasTrait(iplayer, cfg.ElfTraitCode);
        }
    }
}
