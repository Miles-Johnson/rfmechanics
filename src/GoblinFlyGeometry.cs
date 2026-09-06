using System;
using Vintagestory.API.MathTools;

namespace rfmechanics;

internal static class GoblinFlyGeometry
{
    internal static Vec3d Camera(Vec3d origin, float[] view) => origin.AddCopy(
        -(view[0]*view[12] + view[1]*view[13] + view[2]*view[14]),
        -(view[4]*view[12] + view[5]*view[13] + view[6]*view[14]),
        -(view[8]*view[12] + view[9]*view[13] + view[10]*view[14]));

    // Relative endpoints cover both a moving insect and the camera walking through its trail.
    internal static bool CrossesCamera(Vec3d from, Vec3d to, double clearance)
    {
        Vec3d delta = to - from;
        double lenSq = delta.LengthSq();
        double t = lenSq < 1e-12 ? 0 : Math.Clamp(-from.Dot(delta) / lenSq, 0, 1);
        return (from + delta * t).LengthSq() <= clearance * clearance;
    }

    internal static float SmoothFade(double value)
    {
        double t = Math.Clamp(value, 0, 1);
        return (float)(t * t * (3 - 2 * t));
    }

    internal static float Opacity(GoblinAuraShape shape, RFMechanicsConfig cfg)
    {
        double min = GoblinAuraMath.FiniteClamp(cfg.GoblinRotFliesOpacityMin, 0.2, 0, 1);
        double max = GoblinAuraMath.FiniteClamp(cfg.GoblinRotFliesOpacityMax, 0.65, min, 1);
        return (float)(min + (max - min) * shape.Level) * shape.Fade;
    }

    internal static int AuraCount(GoblinAuraShape shape, RFMechanicsConfig cfg)
    {
        if (!shape.Active || !cfg.EnableGoblinRotFlies) return 0;
        int min = Math.Clamp(cfg.GoblinRotFliesCountMin, 1, 128);
        int max = Math.Clamp(cfg.GoblinRotFliesCountMax, min, 256);
        return Math.Max(1, (int)Math.Round((min + (max - min) * shape.Level) * shape.Fade));
    }
}
