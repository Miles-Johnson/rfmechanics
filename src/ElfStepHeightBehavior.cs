using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace rfmechanics
{
    /// <summary>
    /// Sets StepHeight to ElfStepHeightValue for elves, restoring whatever value the entity had
    /// before this behavior touched it otherwise. A plain field write, not a Harmony patch --
    /// StepHeight is public on EntityBehaviorControlledPhysics (BehaviorControlledPhysics.cs:67)
    /// and MotionAndCollision (the FindSteppableCollisionBox call site) runs for players on both
    /// sides, same reason TreeClimbingPatch patches the base class instead of
    /// EntityBehaviorPlayerPhysics -- so this behavior is attached dual-side too
    /// (seraph-elfstepheight.json), same lesson as ElfIdentityBehavior.
    ///
    /// restoreValue is captured once in Initialize(), not hardcoded to vanilla's 0.6f default --
    /// a pinned literal would go stale against a future vanilla change or another mod's own
    /// SetProperties override, and the per-tick correction would then overwrite that value with a
    /// wrong number instead of restoring it. EntityProperties.loadBehaviors constructs and
    /// Initializes behaviors in array order, and this behavior's JSON patch appends to the end of
    /// the array, so the physics behavior (registered earlier, core) has already run its own
    /// Initialize/SetProperties by the time this reads it.
    /// </summary>
    public class ElfStepHeightBehavior : EntityBehavior
    {
        private EntityBehaviorControlledPhysics? physics;
        private float restoreValue = 0.6f;
        private bool captured;

        public ElfStepHeightBehavior(Entity entity) : base(entity) { }

        public override string PropertyName() => "rfelfstepheight";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            physics = entity.GetBehavior<EntityBehaviorControlledPhysics>();
            if (physics != null)
            {
                restoreValue = physics.StepHeight;
                captured = true;
            }
        }

        /// <summary>Only writes when the current value disagrees with the target. The reassert
        /// only needs to win against SetProperties, which runs once per Initialize() (entity
        /// (re)creation), not every tick -- a plain per-tick compare is enough to catch that
        /// without forcing an unconditional write every tick.</summary>
        public override void OnGameTick(float deltaTime)
        {
            if (!captured || physics == null) return;

            var cfg = RFMechanicsModSystem.Config;
            if (cfg == null) return;

            bool isElf = entity.GetBehavior<ElfIdentityBehavior>()?.IsElf ?? false;
            bool toggledOn = entity.WatchedAttributes.GetBool("rf-elf-stepheight-enabled", cfg.ElfStepHeightDefaultEnabled);

            float target = (cfg.EnableElfStepHeight && isElf && toggledOn)
                ? (float)cfg.ElfStepHeightValue
                : restoreValue;

            if (physics.StepHeight != target) physics.StepHeight = target;
        }
    }
}
