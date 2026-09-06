using System;
using Vintagestory.API.MathTools;

namespace rfmechanics;

internal static class OrcSmellGeometry
{
    // Inclusive slab intersection catches both endpoints and a complete crossing between frames.
    internal static bool CrossesBox(Vec3d a, Vec3d b, Cuboidf box, double margin)
    {
        double lo = 0, hi = 1;
        return Slab(a.X, b.X - a.X, box.X1 - margin, box.X2 + margin, ref lo, ref hi)
            && Slab(a.Y, b.Y - a.Y, box.Y1 - margin, box.Y2 + margin, ref lo, ref hi)
            && Slab(a.Z, b.Z - a.Z, box.Z1 - margin, box.Z2 + margin, ref lo, ref hi);
    }

    private static bool Slab(double start, double delta, double min, double max, ref double lo, ref double hi)
    {
        if (Math.Abs(delta) < 1e-10) return start >= min && start <= max;
        double t1 = (min - start) / delta, t2 = (max - start) / delta;
        lo = Math.Max(lo, Math.Min(t1, t2));
        hi = Math.Min(hi, Math.Max(t1, t2));
        return lo <= hi;
    }

    internal static bool CrossesSphere(Vec3d a, Vec3d b, double radius)
    {
        double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
        double lengthSq = x*x + y*y + z*z;
        double t = lengthSq < 1e-12 ? 0 : Math.Clamp(-(a.X*x + a.Y*y + a.Z*z) / lengthSq, 0, 1);
        x = a.X + x*t; y = a.Y + y*t; z = a.Z + z*t;
        return x*x + y*y + z*z <= radius*radius;
    }

    internal static double DistanceToBox(Vec3d p, Cuboidf box)
    {
        double x = Math.Max(Math.Max(box.X1 - p.X, p.X - box.X2), 0);
        double y = Math.Max(Math.Max(box.Y1 - p.Y, p.Y - box.Y2), 0);
        double z = Math.Max(Math.Max(box.Z1 - p.Z, p.Z - box.Z2), 0);
        return Math.Sqrt(x*x + y*y + z*z);
    }

    internal static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t*t*(3-2*t); }
}
