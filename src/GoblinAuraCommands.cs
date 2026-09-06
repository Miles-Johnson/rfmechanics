using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace rfmechanics;

internal static class GoblinAuraCommands
{
    internal static string Describe(IPlayer player, RFMechanicsConfig cfg)
    {
        if (player?.Entity == null) return "No player context.";
        var entity = player.Entity;
        GoblinRotAuraState.Migrate(entity, cfg);
        bool eligible = cfg.EnableGoblinRotAura && entity.Alive && RaceTraits.HasTrait(player, cfg.GoblinTraitCode);
        GoblinAuraShape shape = eligible ? GoblinRotAuraState.Read(entity, cfg) : default;
        int charges = entity.WatchedAttributes.GetInt("rfmechanics:spitCharges");
        double age = entity.WatchedAttributes.HasAttribute(GoblinRotAuraState.MealDayKey)
            ? Math.Max(0, entity.World.Calendar.TotalDays - entity.WatchedAttributes.GetDouble(GoblinRotAuraState.MealDayKey)) : 0;
        return $"Rot aura: {(shape.Active ? "active" : "off")}; radius={shape.Radius:F2} blocks; potency={shape.Intensity:F3}; "
            + $"daysSinceRot={age:F2}; daysUntilClear={shape.DaysRemaining:F2}; "
            + $"auraFliesTarget={GoblinFlyGeometry.AuraCount(shape, cfg)}; storedSpitCharges={charges}; "
            + "only eating game:rot resets recovery. Fly counts are targets before camera/distance culling.";
    }

    internal static void Register(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("rfrotaura")
            .WithDescription("Set your rot aura radius, clear it, or simulate days since eating rot. Does not grant spit charges.")
            .RequiresPrivilege(Privilege.root)
            .HandleWith(args => Status(args))
            .BeginSubCommand("status").WithDescription("Show your current aura and recovery.")
                .HandleWith(args => Status(args)).EndSubCommand()
            .BeginSubCommand("set").WithDescription("Set your aura to a radius in blocks (1-15 by default); 0 clears it. Starts normal recovery.")
                .WithArgs(parsers.Float("radius"))
                .HandleWith(args => Change(args, "set")).EndSubCommand()
            .BeginSubCommand("off").WithDescription("Remove your aura and ambient flies; keep spit charges and their persistent winged flies.")
                .HandleWith(args => Change(args, "off")).EndSubCommand()
            .BeginSubCommand("age").WithDescription("Set days since your last rot meal, without advancing world time. Use set first for repeatable tests.")
                .WithArgs(parsers.Float("days"))
                .HandleWith(args => Change(args, "age")).EndSubCommand()
            .BeginSubCommand("registry").WithDescription("Show every active server aura.")
                .HandleWith(args =>
                {
                    var lines = new List<string>();
                    foreach (var pair in GoblinRotAuraRegistry.AllSources)
                    {
                        var source = pair.Value;
                        lines.Add($"entityId={pair.Key} pos={source.Center} radius={source.Radius:F2} vertical={source.VerticalHalfExtent} "
                            + $"potency={source.Intensity:F3} ageMs={api.World.ElapsedMilliseconds - source.UpdatedMs}");
                    }
                    return TextCommandResult.Success(lines.Count == 0 ? "No active AuraSource entries." : string.Join("\n", lines));
                }).EndSubCommand()
            .BeginSubCommand("timescale").WithDescription("Legacy world calendar speed dial; changes time for the whole server. Prefer age for aura tests.")
                .WithArgs(parsers.OptionalFloat("multiplier"))
                .HandleWith(args =>
                {
                    if (args.Parsers[0].IsMissing) return TextCommandResult.Success($"CalendarSpeedMul={api.World.Calendar.CalendarSpeedMul:F2}");
                    float value = (float)args[0];
                    if (!float.IsFinite(value) || value <= 0 || value > 100) return TextCommandResult.Error("Use a finite multiplier above 0 and at most 100.");
                    float old = api.World.Calendar.CalendarSpeedMul;
                    api.World.Calendar.CalendarSpeedMul = value;
                    return TextCommandResult.Success($"World CalendarSpeedMul={value:F2}; previous={old:F2}. Restore the previous value after testing.");
                }).EndSubCommand();
    }

    private static TextCommandResult Status(TextCommandCallingArgs args) => RFMechanicsModSystem.Config is { } cfg
        ? TextCommandResult.Success(Describe(args.Caller.Player, cfg)) : TextCommandResult.Error("Config not loaded.");

    private static TextCommandResult Change(TextCommandCallingArgs args, string action)
    {
        var cfg = RFMechanicsModSystem.Config;
        IPlayer player = args.Caller.Player;
        if (cfg == null || player?.Entity == null) return TextCommandResult.Error("Join the world as a player first.");
        EntityPlayer entity = player.Entity;
        if (!RaceTraits.HasTrait(player, cfg.GoblinTraitCode)) return TextCommandResult.Error("This command requires a goblin character.");
        if (action != "off" && (!cfg.EnableGoblinRotAura || !entity.Alive)) return TextCommandResult.Error("Aura is disabled or your character is not alive.");
        var behavior = entity.GetBehavior<GoblinRotAuraBehavior>();
        if (behavior == null) return TextCommandResult.Error("Goblin aura behavior is missing from this player.");
        double value = action == "off" ? 0 : (float)args[0];
        if (!double.IsFinite(value)) return TextCommandResult.Error("Use a finite number.");
        GoblinRotAuraState.Migrate(entity, cfg);
        if (action == "off" || action == "set" && value == 0) GoblinRotAuraState.Clear(entity);
        else if (action == "age")
        {
            if (value < 0 || value > 365) return TextCommandResult.Error("Days must be between 0 and 365.");
            if (!entity.WatchedAttributes.HasAttribute(GoblinRotAuraState.MealDayKey))
                return TextCommandResult.Error("No rot meal to age. Use /rfrotaura set 15 first.");
            entity.WatchedAttributes.SetDouble(GoblinRotAuraState.MealDayKey, entity.World.Calendar.TotalDays - value);
        }
        else
        {
            double min = Math.Clamp(cfg.GoblinRotAuraRadiusMin, 1, 15);
            double max = Math.Clamp(cfg.GoblinRotAuraRadiusMax, min, 15);
            if (value < min || value > max) return TextCommandResult.Error($"Radius must be 0 (off) or {min:F1}-{max:F1} blocks.");
            GoblinRotAuraState.Set(entity, max == min ? 0 : (value - min) / (max - min), entity.World.Calendar.TotalDays);
        }
        behavior.Refresh();
        return TextCommandResult.Success(Describe(player, cfg));
    }
}
