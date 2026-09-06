using System;

namespace rfmechanics;

internal readonly record struct GoblinAuraShape(double Radius, int VerticalHalfExtent,
    float Intensity, float Fade, double Level, double DaysRemaining)
{
    public bool Active => Radius > 0 && Fade > 0;
}

/// <summary>Calendar-day recovery, independent of DietSetup and of inventory contents.</summary>
internal static class GoblinAuraMath
{
    internal static GoblinAuraShape Evaluate(bool hasMeal, double fedLevel, double elapsedDays, RFMechanicsConfig cfg)
    {
        if (!hasMeal || !double.IsFinite(fedLevel) || !double.IsFinite(elapsedDays)) return default;
        double age = Math.Max(0, elapsedDays);
        double narrowDays = FiniteClamp(cfg.GoblinRotAuraNarrowAfterDays, 2, 0.1, 30);
        double clearDays = FiniteClamp(cfg.GoblinRotAuraClearAfterDays, 3, narrowDays + 0.1, 60);
        if (age >= clearDays) return default;
        double level = Math.Max(0, Math.Clamp(fedLevel, 0, 1) - age / narrowDays);
        double minRadius = Math.Clamp(cfg.GoblinRotAuraRadiusMin, 1, 15);
        double maxRadius = Math.Clamp(cfg.GoblinRotAuraRadiusMax, minRadius, 15);
        double radius = minRadius + (maxRadius - minRadius) * level;
        float fade = (float)Math.Clamp((clearDays - age) / (clearDays - narrowDays), 0, 1);
        // Preserve wide-aura output, cap concentration at the old narrow-aura potency.
        double intensity = Math.Min(1, 16 / (radius * radius))
            * FiniteClamp(cfg.GoblinRotAuraIntensityAtMinRadius, 1, 0, 10) * fade;
        int vertical = Math.Clamp((int)Math.Ceiling(radius), 1, Math.Clamp(cfg.GoblinRotAuraVerticalHalfExtent, 1, 8));
        return new(radius, vertical, (float)intensity, fade, level, clearDays - age);
    }

    internal static double AfterBite(GoblinAuraShape current, RFMechanicsConfig cfg) =>
        Math.Clamp(current.Level + FiniteClamp(cfg.GoblinRotAuraPerRot, 0.15, 0.01, 1), 0, 1);

    internal static double FiniteClamp(double value, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
}
