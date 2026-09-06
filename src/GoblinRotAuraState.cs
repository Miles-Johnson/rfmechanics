using System;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace rfmechanics;

internal static class GoblinRotAuraState
{
    internal const string LevelKey = "rfmechanics:rotAuraLevel";
    internal const string MealDayKey = "rfmechanics:rotAuraMealDay";
    internal const string SnapshotKey = "rfmechanics:rotAura";

    internal static GoblinAuraShape Read(Entity entity, RFMechanicsConfig cfg)
    {
        var wa = entity.WatchedAttributes;
        return GoblinAuraMath.Evaluate(wa.HasAttribute(MealDayKey), wa.GetDouble(LevelKey),
            entity.World.Calendar.TotalDays - wa.GetDouble(MealDayKey, entity.World.Calendar.TotalDays), cfg);
    }

    internal static void EatRot(Entity entity, RFMechanicsConfig cfg)
    {
        Migrate(entity, cfg);
        Set(entity, GoblinAuraMath.AfterBite(Read(entity, cfg), cfg), entity.World.Calendar.TotalDays);
        entity.GetBehavior<GoblinRotAuraBehavior>()?.Refresh();
    }

    internal static void Set(Entity entity, double level, double mealDay)
    {
        entity.WatchedAttributes.SetDouble(LevelKey, Math.Clamp(level, 0, 1));
        entity.WatchedAttributes.SetDouble(MealDayKey, mealDay);
    }

    internal static void Clear(Entity entity)
    {
        // Prevent importing the old signal again after an explicit clear.
        entity.WatchedAttributes.SetBool("rfmechanics:rotAuraMigrated", true);
        entity.WatchedAttributes.RemoveAttribute(MealDayKey);
        entity.WatchedAttributes.SetDouble(LevelKey, 0);
        GoblinRotAuraRegistry.ClearSource(entity.EntityId);
        Publish(entity, default);
    }

    internal static void Migrate(Entity entity, RFMechanicsConfig cfg)
    {
        var wa = entity.WatchedAttributes;
        if (wa.GetBool("rfmechanics:rotAuraMigrated")) return;
        wa.SetBool("rfmechanics:rotAuraMigrated", true);
        if (wa.HasAttribute(MealDayKey)) return;
        // Only the old literal-rot signal proves a meal. Never import spoiled-food intake.
        double raw = wa.GetDouble("rfmechanics:rotFlies");
        if (!double.IsFinite(raw) || raw <= 0 || !wa.HasAttribute("rfmechanics:rotFliesUpdatedHours")) return;
        double mealHours = wa.GetDouble("rfmechanics:rotFliesUpdatedHours");
        double nowHours = entity.World.Calendar.TotalHours;
        double hoursPerDay = Math.Max(1, entity.World.Calendar.HoursPerDay);
        if (!double.IsFinite(mealHours) || mealHours > nowHours) return;
        double age = (nowHours - mealHours) / hoursPerDay;
        double level = Math.Clamp(raw / GoblinAuraMath.FiniteClamp(cfg.GoblinRotFliesCap, 5, 0.01, 100), 0, 1);
        if (GoblinAuraMath.Evaluate(true, level, age, cfg).Active)
            Set(entity, level, entity.World.Calendar.TotalDays - age);
    }

    internal static void Publish(Entity entity, GoblinAuraShape shape)
    {
        double radius = Math.Round(shape.Radius, 3);
        float fade = (float)Math.Round(shape.Fade, 3);
        double level = Math.Round(shape.Level, 3);
        var old = entity.WatchedAttributes.GetTreeAttribute(SnapshotKey);
        if (old != null && old.GetDouble("radius") == radius && old.GetInt("vertical") == shape.VerticalHalfExtent
            && old.GetFloat("fade") == fade && old.GetDouble("level") == level) return;
        var tree = new TreeAttribute();
        tree.SetDouble("radius", radius);
        tree.SetInt("vertical", shape.VerticalHalfExtent);
        tree.SetFloat("fade", fade);
        tree.SetDouble("level", level);
        entity.WatchedAttributes.SetAttribute(SnapshotKey, tree);
        entity.WatchedAttributes.MarkPathDirty(SnapshotKey);
    }

    // Local client config cannot invent a different active radius.
    internal static GoblinAuraShape ReadVisual(Entity entity)
    {
        var tree = entity.WatchedAttributes.GetTreeAttribute(SnapshotKey);
        if (tree == null) return default;
        double radius = tree.GetDouble("radius");
        float fade = tree.GetFloat("fade");
        if (!double.IsFinite(radius) || !float.IsFinite(fade) || radius <= 0 || fade <= 0) return default;
        return new(Math.Clamp(radius, 0, 15), Math.Clamp(tree.GetInt("vertical"), 1, 8), 0,
            Math.Clamp(fade, 0, 1), GoblinAuraMath.FiniteClamp(tree.GetDouble("level"), 0, 0, 1), 0);
    }
}
