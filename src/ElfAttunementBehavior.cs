using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Phase 1a of the Elf attunement system. Owns a single 0-100 "attunement" float in
    /// WatchedAttributes -- this behavior is the ONLY writer of that key. Reasserts ownership
    /// every slow tick (re-clamped on every write via the Attunement property setter, same
    /// self-heal spirit as BandBehavior's entitySize drift-correction).
    ///
    /// This task (E1.1) only establishes ownership of the float, the race gate, and the
    /// cached-bool contract Phase 3 will read from a physics-rate context -- the actual
    /// gain/decay-toward-ceiling step (E1.3) and threshold-crossing events (E1.4) land in
    /// later Phase 1a tasks on top of this skeleton.
    ///
    /// Attached to every player entity via a JSON patch (seraph-elfattunement.json), same
    /// convention as RFTreeProximityBehavior/ThewBehavior -- the elf-race gate lives inside
    /// RefreshElfCache(), not in listener registration/lifecycle.
    /// </summary>
    public class ElfAttunementBehavior : EntityBehavior
    {
        private const string AttributeKey = "rf-elf-attunement";

        private float accum;

        /// <summary>Cached elf-race result, refreshed every slow tick (and therefore within one
        /// tick interval of any characterClass change -- there is no separate change listener,
        /// polling on the slow tick is the refresh mechanism). Nothing outside this behavior may
        /// walk CharacterSystem.HasTrait on a hot path -- Phase 3 reads this field directly
        /// instead of re-deriving it.</summary>
        public bool IsElfCached { get; private set; }

        public ElfAttunementBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfattunement";

        /// <summary>The owned float, clamped to [0,100] on every write. Server-authoritative,
        /// synced via WatchedAttributes (not entity.Attributes) so client-side effects
        /// (Phase 2+) can read it directly without a round trip.</summary>
        public float Attunement
        {
            get => entity.WatchedAttributes.GetFloat(AttributeKey, 0f);
            set => entity.WatchedAttributes.SetFloat(AttributeKey, GameMath.Clamp(value, 0f, 100f));
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side != EnumAppSide.Server) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null || !cfg.EnableElfAttunement) return;

            accum += deltaTime;
            if (accum < (float)cfg.AttunementTickInterval) return;
            accum = 0f;

            RefreshElfCache();
        }

        /// <summary>
        /// Guard chain matching every other rfmechanics elf gate (see
        /// RFTreeProximityBehavior.IsElf / ThewBehavior.IsOrc): EntityPlayer check, then
        /// characterClass null check (load-bearing -- HasTrait returns true for a null class
        /// by default, so classless entities must be explicitly excluded), then the trait
        /// check itself. Copied verbatim, only the trait code differs.
        /// </summary>
        private void RefreshElfCache()
        {
            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) { IsElfCached = false; return; }
            if (entity is not EntityPlayer player) { IsElfCached = false; return; }

            string charClass = player.WatchedAttributes.GetString("characterClass");
            if (string.IsNullOrEmpty(charClass)) { IsElfCached = false; return; }

            IPlayer iplayer = player.World.PlayerByUid(player.PlayerUID);
            if (iplayer == null) { IsElfCached = false; return; }

            var charSys = RFMechanicsModSystem.Api?.ModLoader.GetModSystem<CharacterSystem>();
            if (charSys == null) { IsElfCached = false; return; }

            IsElfCached = charSys.HasTrait(iplayer, cfg.ElfTraitCode);
        }
    }
}
