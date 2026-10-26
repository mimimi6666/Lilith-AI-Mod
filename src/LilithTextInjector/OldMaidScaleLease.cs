using System;

namespace LilithTextInjector;

// A temporary size lease, not a player preference. The native adapter captures
// the verified slider values only after the snack transition completes, applies
// Value on the Unity thread, and restores before discarding the lease.
internal sealed class OldMaidScaleLease
{
    internal const double TransitionSeconds = 0.65;
    private double _from;
    private double _elapsed;

    internal bool IsActive { get; private set; }
    internal bool IsRestoring { get; private set; }
    internal bool IsAnimating { get; private set; }
    internal double OriginalScale { get; private set; } = 1;
    internal double Value { get; private set; } = 1;
    internal double Target { get; private set; } = 1;

    internal bool TryBegin(double originalScale, double maximumScale)
    {
        // Replays / reentry retain the very first snapshot. Restoration cannot
        // be interrupted by a stale or double-clicked start command.
        if (IsActive) return !IsRestoring;
        if (!IsValidScale(originalScale) || !IsValidScale(maximumScale) || originalScale > maximumScale)
            return false;

        IsActive = true;
        IsRestoring = false;
        OriginalScale = originalScale;
        Value = originalScale;
        BeginTween(maximumScale);
        return true;
    }

    internal double Advance(double deltaSeconds)
    {
        if (!IsActive || !IsAnimating || double.IsNaN(deltaSeconds) ||
            double.IsInfinity(deltaSeconds) || deltaSeconds <= 0)
            return Value;

        _elapsed = Math.Min(TransitionSeconds, _elapsed + deltaSeconds);
        var t = _elapsed / TransitionSeconds;
        var smooth = t * t * (3 - 2 * t);
        Value = _from + (Target - _from) * smooth;
        if (_elapsed >= TransitionSeconds)
        {
            Value = Target;
            IsAnimating = false;
            if (IsRestoring)
            {
                IsRestoring = false;
                IsActive = false;
            }
        }
        return Value;
    }

    internal bool BeginRestore()
    {
        if (!IsActive) return false;
        if (IsRestoring) return true;
        IsRestoring = true;
        BeginTween(OriginalScale);
        if (!IsAnimating)
        {
            IsActive = false;
            IsRestoring = false;
        }
        return true;
    }

    // Exception / teardown cleanup: return the captured native restore target
    // before forgetting it. The caller must apply it if the character survives.
    internal double? Reset()
    {
        double? restore = IsActive ? OriginalScale : null;
        IsActive = false;
        IsRestoring = false;
        IsAnimating = false;
        _elapsed = 0;
        _from = Value = Target = OriginalScale = 1;
        return restore;
    }

    private void BeginTween(double target)
    {
        _from = Value;
        Target = target;
        _elapsed = 0;
        IsAnimating = Value != target;
    }

    private static bool IsValidScale(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
