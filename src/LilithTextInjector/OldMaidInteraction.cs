using System;
using System.Collections.Generic;

namespace LilithTextInjector;

// Hit geometry never receives the lifted/animated visual pose. Keeping the
// entire fan immutable during a hover removes the enter/exit feedback loop.
internal static class OldMaidCardHitTest
{
    internal static int Find(IReadOnlyList<OldMaidCardPose> basePoses, float x, float y, int previous)
    {
        if (previous >= 0 && previous < basePoses.Count && Contains(basePoses[previous], x, y)) return previous;
        for (var i = basePoses.Count - 1; i >= 0; i--)
            if (Contains(basePoses[i], x, y)) return i;
        return -1;
    }

    internal static bool Contains(OldMaidCardPose pose, float x, float y)
    {
        if (!OldMaidCardGeometry.IsFinite(x) || !OldMaidCardGeometry.IsFinite(y)
            || !OldMaidCardGeometry.IsFinite(pose.X) || !OldMaidCardGeometry.IsFinite(pose.Y)
            || !OldMaidCardGeometry.IsFinite(pose.Angle) || !OldMaidCardGeometry.PositiveFinite(pose.Width)
            || !OldMaidCardGeometry.PositiveFinite(pose.Height)) return false;
        var radians = pose.Angle * Math.PI / 180d;
        var dx = x - pose.X;
        var dy = y - pose.Y;
        var localX = Math.Abs(dx * Math.Cos(radians) + dy * Math.Sin(radians));
        var localY = Math.Abs(-dx * Math.Sin(radians) + dy * Math.Cos(radians));
        var halfW = pose.Width * 0.5;
        var halfH = pose.Height * 0.5;
        if (localX > halfW || localY > halfH) return false;
        var radius = Math.Min(pose.Width * 0.055, halfH);
        var cornerX = Math.Max(0, localX - (halfW - radius));
        var cornerY = Math.Max(0, localY - (halfH - radius));
        return cornerX * cornerX + cornerY * cornerY <= radius * radius;
    }
}

// Background/modal safety is transient, not a user-facing Pause/Continue mode.
// Resume only after a quiet, released-pointer interval so the activating press
// cannot also draw. Reading properties never advances a clock.
internal sealed class OldMaidActivityGate
{
    internal const float ResumeDelaySeconds = 0.25f;
    private float _quiet;
    internal bool IsSuspended { get; private set; }
    internal void Suspend() { IsSuspended = true; _quiet = 0; }
    internal void Clear() { IsSuspended = false; _quiet = 0; }
    internal bool Step(bool available, bool pointerHeld, float delta)
    {
        if (!available) { Suspend(); return false; }
        if (!IsSuspended) return true;
        if (pointerHeld) { _quiet = 0; return false; }
        if (!OldMaidCardGeometry.PositiveFinite(delta)) return false;
        _quiet += Math.Min(delta, 0.05f);
        if (_quiet + 0.000001f < ResumeDelaySeconds) return false;
        Clear();
        return true;
    }
}
