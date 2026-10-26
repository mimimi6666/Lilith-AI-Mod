using System;

namespace LilithTextInjector;

internal enum MotionExecutionPhase
{
    AwaitingEvidence,
    Settling,
    Completed,
    Interrupted,
    Failed
}

internal readonly struct MotionExecutionObservation
{
    internal bool CharacterAvailable { get; }
    internal bool IsDragging { get; }
    internal bool IsGrounded { get; }
    internal bool HasWindowPlatform { get; }
    internal bool IsOnDesktopGround { get; }
    internal bool IsSitting { get; }
    internal bool IsLyingDown { get; }
    internal bool IsSleeping { get; }
    internal bool IsWalkingLeft { get; }
    internal bool IsWalkingRight { get; }
    internal bool IsActionAnimationPlaying { get; }
    internal bool HasPendingOperation { get; }
    internal bool IsOverlayBehindWindow { get; }
    internal bool HasWalkContinuation { get; }
    internal string AnimationName { get; }
    internal float PositionX { get; }
    internal float PositionY { get; }
    internal float VelocityX { get; }
    internal float VelocityY { get; }

    internal bool HasSupportedSurface => HasWindowPlatform || IsOnDesktopGround;
    internal bool IsWalking => IsWalkingLeft || IsWalkingRight || HasWalkContinuation;
    internal bool IsVisuallySitting => IsSitting
        && AnimationName.StartsWith("sit_", StringComparison.OrdinalIgnoreCase);
    internal bool IsStable => CharacterAvailable
        && !IsDragging
        && IsGrounded
        && !HasPendingOperation
        && !IsOverlayBehindWindow
        && !HasWalkContinuation
        && !IsWalkingLeft
        && !IsWalkingRight
        && Math.Abs(VelocityX) <= MotionExecutionTracker.MaximumStableHorizontalSpeed
        && Math.Abs(VelocityY) <= MotionExecutionTracker.MaximumStableVerticalSpeed;
    internal bool IsStableWindowSit => CharacterAvailable
        && !IsDragging
        && HasWindowPlatform
        && !HasPendingOperation
        && !IsOverlayBehindWindow
        && !HasWalkContinuation
        && !IsWalkingLeft
        && !IsWalkingRight
        && Math.Abs(VelocityX) <= MotionExecutionTracker.MaximumStableHorizontalSpeed
        && Math.Abs(VelocityY) <= MotionExecutionTracker.MaximumStableVerticalSpeed;

    internal MotionExecutionObservation(
        bool characterAvailable,
        bool isDragging,
        bool isGrounded,
        bool hasWindowPlatform,
        bool isOnDesktopGround,
        bool isSitting,
        bool isLyingDown,
        bool isSleeping,
        bool isWalkingLeft,
        bool isWalkingRight,
        bool isActionAnimationPlaying,
        bool hasPendingOperation,
        bool isOverlayBehindWindow,
        bool hasWalkContinuation,
        string? animationName,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY)
    {
        CharacterAvailable = characterAvailable;
        IsDragging = isDragging;
        IsGrounded = isGrounded;
        HasWindowPlatform = hasWindowPlatform;
        IsOnDesktopGround = isOnDesktopGround;
        IsSitting = isSitting;
        IsLyingDown = isLyingDown;
        IsSleeping = isSleeping;
        IsWalkingLeft = isWalkingLeft;
        IsWalkingRight = isWalkingRight;
        IsActionAnimationPlaying = isActionAnimationPlaying;
        HasPendingOperation = hasPendingOperation;
        IsOverlayBehindWindow = isOverlayBehindWindow;
        HasWalkContinuation = hasWalkContinuation;
        AnimationName = animationName ?? string.Empty;
        PositionX = positionX;
        PositionY = positionY;
        VelocityX = velocityX;
        VelocityY = velocityY;
    }
}

internal readonly struct MotionExecutionUpdate
{
    internal MotionExecutionPhase Phase { get; }
    internal MotionIntentStatus Status { get; }
    internal MotionIntentReason Reason { get; }
    internal string Detail { get; }
    internal bool IsTerminal => Phase is MotionExecutionPhase.Completed
        or MotionExecutionPhase.Interrupted
        or MotionExecutionPhase.Failed;

    private MotionExecutionUpdate(
        MotionExecutionPhase phase,
        MotionIntentStatus status,
        MotionIntentReason reason,
        string detail)
    {
        Phase = phase;
        Status = status;
        Reason = reason;
        Detail = detail;
    }

    internal static MotionExecutionUpdate Running(MotionExecutionPhase phase, string detail)
        => new(phase, MotionIntentStatus.Started, MotionIntentReason.None, detail);

    internal static MotionExecutionUpdate Complete(string detail)
        => new(MotionExecutionPhase.Completed, MotionIntentStatus.Completed, MotionIntentReason.None, detail);

    internal static MotionExecutionUpdate Interrupt(MotionIntentReason reason, string detail)
        => new(MotionExecutionPhase.Interrupted, MotionIntentStatus.Interrupted, reason, detail);

    internal static MotionExecutionUpdate Fail(MotionIntentReason reason, string detail)
        => new(MotionExecutionPhase.Failed, MotionIntentStatus.Failed, reason, detail);
}

internal sealed class MotionExecutionTracker
{
    internal const float MaximumStableHorizontalSpeed = 0.12f;
    internal const float MaximumStableVerticalSpeed = 0.12f;
    internal const double RequiredStableSeconds = 0.20d;
    internal const double RequiredVisibleOneShotSeconds = 0.12d;
    private const float MinimumPositionEvidence = 0.06f;

    private readonly MotionIntentKind _kind;
    private readonly MotionExecutionObservation _initial;
    private bool _evidenceObserved;
    private double _oneShotEvidenceSince = -1d;
    private double _stableSince = -1d;
    private MotionExecutionUpdate? _terminal;

    internal MotionExecutionTracker(
        MotionIntentKind kind,
        MotionExecutionObservation initial)
    {
        if (kind == MotionIntentKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        _kind = kind;
        _initial = initial;
    }

    internal bool EvidenceObserved => _evidenceObserved;

    internal MotionExecutionUpdate Observe(
        MotionExecutionObservation current,
        double elapsedSeconds)
    {
        if (_terminal.HasValue)
            return _terminal.Value;
        if (elapsedSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!current.CharacterAvailable)
            return StoreTerminal(MotionExecutionUpdate.Interrupt(
                MotionIntentReason.CharacterUnavailable,
                "Lilith's runtime disappeared before the motion reached a terminal state."));
        if (current.IsDragging)
            return StoreTerminal(MotionExecutionUpdate.Interrupt(
                MotionIntentReason.PlayerInterrupted,
                "The player took direct control before the motion reached a terminal state."));

        if (_kind == MotionIntentKind.PlayOneShot)
            ObserveVisibleOneShotEvidence(current, elapsedSeconds);
        else
            _evidenceObserved |= HasExpectedEvidence(current);
        if (_evidenceObserved && IsValidFinalState(current))
        {
            if (_stableSince < 0d)
                _stableSince = elapsedSeconds;
            if (elapsedSeconds - _stableSince >= RequiredStableSeconds)
            {
                return StoreTerminal(MotionExecutionUpdate.Complete(
                    $"Observed {_kind} and a valid stable terminal state."));
            }
            return MotionExecutionUpdate.Running(
                MotionExecutionPhase.Settling,
                "Expected motion was observed; validating that the final state remains stable.");
        }

        _stableSince = -1d;
        if (elapsedSeconds >= GetTimeoutSeconds(_kind, _initial))
        {
            var missing = _evidenceObserved
                ? "The expected motion occurred, but no valid stable final state was observed."
                : "No matching visual or physical motion evidence was observed.";
            return StoreTerminal(MotionExecutionUpdate.Fail(
                MotionIntentReason.ExecutionTimedOut,
                missing));
        }
        return MotionExecutionUpdate.Running(
            MotionExecutionPhase.AwaitingEvidence,
            _evidenceObserved
                ? "Motion evidence was observed; waiting for a valid terminal state."
                : "Waiting for matching visual or physical motion evidence.");
    }

    private MotionExecutionUpdate StoreTerminal(MotionExecutionUpdate update)
    {
        _terminal = update;
        return update;
    }

    private void ObserveVisibleOneShotEvidence(
        MotionExecutionObservation current,
        double elapsedSeconds)
    {
        if (current.IsActionAnimationPlaying && current.HasSupportedSurface)
        {
            if (_oneShotEvidenceSince < 0d)
                _oneShotEvidenceSince = elapsedSeconds;
            if (elapsedSeconds - _oneShotEvidenceSince >= RequiredVisibleOneShotSeconds)
                _evidenceObserved = true;
            return;
        }

        if (!_evidenceObserved)
            _oneShotEvidenceSince = -1d;
    }

    private bool HasExpectedEvidence(MotionExecutionObservation current)
    {
        var movedX = Math.Abs(current.PositionX - _initial.PositionX) >= MinimumPositionEvidence;
        var movedY = Math.Abs(current.PositionY - _initial.PositionY) >= MinimumPositionEvidence;
        return _kind switch
        {
            MotionIntentKind.ArriveAtWindow => current.HasPendingOperation
                || current.IsActionAnimationPlaying
                || movedX
                || movedY,
            MotionIntentKind.WalkLeft => current.IsWalkingLeft
                || current.PositionX <= _initial.PositionX - MinimumPositionEvidence,
            MotionIntentKind.WalkRight => current.IsWalkingRight
                || current.PositionX >= _initial.PositionX + MinimumPositionEvidence,
            MotionIntentKind.PlayOneShot => current.IsActionAnimationPlaying,
            MotionIntentKind.Sit => current.IsVisuallySitting,
            MotionIntentKind.Stand => (_initial.IsSitting || _initial.IsLyingDown || _initial.IsSleeping)
                && !current.IsSitting
                && !current.IsLyingDown
                && !current.IsSleeping,
            MotionIntentKind.PeekFromWindow => current.IsOverlayBehindWindow
                || current.IsActionAnimationPlaying
                || movedX
                || movedY,
            MotionIntentKind.DropFromWindow => !current.IsGrounded
                || current.VelocityY < -MaximumStableVerticalSpeed
                || movedY,
            _ => false
        };
    }

    private bool IsValidFinalState(MotionExecutionObservation current)
    {
        // Native seated poses can clear IsGround shortly after entering the
        // animation even though the verified window anchor remains attached.
        // Keep every other stability guard, and require both a real window
        // platform and the visible sit animation so an air-chair cannot pass.
        if (_kind == MotionIntentKind.Sit)
        {
            return current.IsStableWindowSit
                && current.IsVisuallySitting
                && !current.IsActionAnimationPlaying;
        }

        if (!current.IsStable)
            return false;
        return _kind switch
        {
            MotionIntentKind.ArriveAtWindow => current.HasWindowPlatform
                && !current.IsActionAnimationPlaying,
            MotionIntentKind.WalkLeft or MotionIntentKind.WalkRight => current.HasSupportedSurface
                && !current.IsWalking
                && !current.IsActionAnimationPlaying,
            MotionIntentKind.PlayOneShot => current.HasSupportedSurface
                && !current.IsActionAnimationPlaying,
            MotionIntentKind.Stand => current.HasSupportedSurface
                && !current.IsSitting
                && !current.IsLyingDown
                && !current.IsSleeping
                && !current.IsActionAnimationPlaying,
            MotionIntentKind.PeekFromWindow => current.HasWindowPlatform
                && !current.IsActionAnimationPlaying,
            MotionIntentKind.DropFromWindow => current.HasSupportedSurface
                && !current.IsActionAnimationPlaying,
            _ => false
        };
    }

    private static double GetTimeoutSeconds(
        MotionIntentKind kind,
        MotionExecutionObservation initial) => kind switch
    {
        MotionIntentKind.Stand => 2.5d,
        // Ground sit is a safe composite transition: Snap (up to about 3.7 s),
        // hidden window placement, native sit entry, then terminal settling.
        MotionIntentKind.Sit when initial.IsOnDesktopGround => 6d,
        MotionIntentKind.Sit => 3.5d,
        MotionIntentKind.PlayOneShot => 8d,
        MotionIntentKind.WalkLeft or MotionIntentKind.WalkRight => 7d,
        MotionIntentKind.ArriveAtWindow => 8d,
        MotionIntentKind.PeekFromWindow => 10d,
        MotionIntentKind.DropFromWindow => 10d,
        _ => 5d
    };
}
