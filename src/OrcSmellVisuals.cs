using System;

namespace rfmechanics;

internal static class OrcSmellVisuals
{
    // Equivalent body dimension uses height as well as width: a fox and a tall moose
    // have surprisingly similar widths, but very different volumes.
    internal static double BodyScale(double width, double height, double exponent) =>
        Math.Pow(Math.Cbrt(Math.Max(0.001, width * width * height)), Math.Clamp(exponent, 0.5, 3));

    internal static float ParticleSize(double scale, RFMechanicsConfig cfg) => (float)Math.Clamp(
        cfg.SmellParticleSizeBase + cfg.SmellParticleSizePerSize * scale,
        cfg.SmellParticleSizeMin, cfg.SmellParticleSizeMax);

    // MeshData writes the low byte first. ColorUtil.ToRgba packs blue there, despite its name.
    internal static int MeshColor(int r, int g, int b, float alpha) =>
        Math.Clamp(r, 0, 255) | (Math.Clamp(g, 0, 255) << 8) | (Math.Clamp(b, 0, 255) << 16)
        | ((int)(Math.Clamp(alpha, 0, 1) * 255) << 24);

    internal static double RangeScale(float quality, double movingScale) =>
        Math.Clamp(movingScale, 0.1, 1) + (1 - Math.Clamp(movingScale, 0.1, 1)) * Math.Clamp(quality, 0, 1);
}
