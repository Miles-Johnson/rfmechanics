using System;

namespace rfmechanics;

// Pure interaction state, independent of rendering and the calendar clock.
internal sealed class OrcSmellFocusState
{
    internal float HeldMs { get; private set; }
    internal float StillMs { get; private set; }
    internal bool Active { get; private set; }
    internal bool NeedsRelease { get; private set; }
    private float settledMs;

    internal void Update(float dt, bool held, bool eligible, bool stationary, bool hurt, bool interrupted = false)
    {
        dt = Math.Clamp(dt, 0, 0.1f);
        if (!held) NeedsRelease = false;
        if (!eligible || interrupted || hurt || !held || NeedsRelease)
        {
            if (held && (Active || hurt || !eligible || interrupted)) NeedsRelease = true;
            HeldMs = StillMs = settledMs = 0;
            Active = false;
            return;
        }
        settledMs += dt * 1000;
        if (settledMs < 250) return;
        Active = true;
        HeldMs += dt * 1000;
        StillMs = stationary ? StillMs + dt * 1000 : 0;
    }

    internal float Quality(float fullMs) => Math.Clamp(StillMs / Math.Max(1, fullMs), 0, 1);

    internal float Darkness(float engageMs, float movingWeight)
    {
        if (!Active) return 0;
        float warmup = Math.Clamp(HeldMs / Math.Max(1, engageMs), 0, 1);
        return Math.Clamp(movingWeight, 0, 0.5f) * warmup
            + (1 - Math.Clamp(movingWeight, 0, 0.5f)) * Math.Clamp(StillMs / Math.Max(1, engageMs), 0, 1);
    }
}
