using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum MotionIntentKind
{
    None,
    ArriveAtWindow,
    WalkLeft,
    WalkRight,
    PlayOneShot,
    Sit,
    Stand,
    PeekFromWindow,
    DropFromWindow
}

internal enum MotionIntentSource
{
    Showcase,
    Initiative,
    Model,
    Remote
}

internal enum MotionIntentStatus
{
    Accepted,
    Started,
    Completed,
    Rejected,
    Interrupted,
    Failed
}

internal enum MotionIntentReason
{
    None,
    InvalidIntent,
    CharacterUnavailable,
    ConversationBusy,
    Dragging,
    StateMachineLocked,
    Airborne,
    TransitionPending,
    OverlayTransitionActive,
    WalkContinuationActive,
    ActionAnimationPlaying,
    Sleeping,
    ClothingUnsupported,
    WalkableSurfaceRequired,
    NoWalkableDistance,
    DesktopGroundRequired,
    WindowPlatformRequired,
    NoEligibleWindow,
    AlreadyInRequestedState,
    AnimationUnavailable,
    PlayerInterrupted,
    ExecutionTimedOut,
    ExecutionFailed
}

internal sealed class MotionIntentRequest
{
    internal Guid Id { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentSource Source { get; }
    internal DateTimeOffset RequestedAt { get; }

    private MotionIntentRequest(Guid id, MotionIntentKind kind, MotionIntentSource source, DateTimeOffset requestedAt)
    {
        Id = id;
        Kind = kind;
        Source = source;
        RequestedAt = requestedAt;
    }

    internal static MotionIntentRequest Create(MotionIntentKind kind, MotionIntentSource source)
        => new(Guid.NewGuid(), kind, source, DateTimeOffset.UtcNow);

    internal static MotionIntentRequest Create(
        Guid id,
        MotionIntentKind kind,
        MotionIntentSource source,
        DateTimeOffset requestedAt)
        => new(id, kind, source, requestedAt);
}

internal readonly struct MotionPolicySnapshot
{
    internal bool CharacterAvailable { get; }
    internal bool ConversationBusy { get; }
    internal bool IsDragging { get; }
    internal bool IsStateMachineLocked { get; }
    internal bool IsGrounded { get; }
    internal bool IsPajamas { get; }
    internal bool HasWindowPlatform { get; }
    internal bool IsOnDesktopGround { get; }
    internal bool HasPendingOperation { get; }
    internal bool IsOverlayBehindWindow { get; }
    internal bool HasWalkContinuation { get; }
    internal bool IsActionAnimationPlaying { get; }
    internal bool IsSitting { get; }
    internal bool IsSleeping { get; }
    internal bool CanWalkLeft { get; }
    internal bool CanWalkRight { get; }

    internal MotionPolicySnapshot(
        bool characterAvailable,
        bool conversationBusy,
        bool isDragging,
        bool isStateMachineLocked,
        bool isGrounded,
        bool isPajamas,
        bool hasWindowPlatform,
        bool isOnDesktopGround,
        bool hasPendingOperation,
        bool isOverlayBehindWindow,
        bool hasWalkContinuation,
        bool isActionAnimationPlaying,
        bool isSitting,
        bool isSleeping = false,
        bool canWalkLeft = true,
        bool canWalkRight = true)
    {
        CharacterAvailable = characterAvailable;
        ConversationBusy = conversationBusy;
        IsDragging = isDragging;
        IsStateMachineLocked = isStateMachineLocked;
        IsGrounded = isGrounded;
        IsPajamas = isPajamas;
        HasWindowPlatform = hasWindowPlatform;
        IsOnDesktopGround = isOnDesktopGround;
        HasPendingOperation = hasPendingOperation;
        IsOverlayBehindWindow = isOverlayBehindWindow;
        HasWalkContinuation = hasWalkContinuation;
        IsActionAnimationPlaying = isActionAnimationPlaying;
        IsSitting = isSitting;
        IsSleeping = isSleeping;
        CanWalkLeft = canWalkLeft;
        CanWalkRight = canWalkRight;
    }
}

internal readonly struct MotionPolicyDecision
{
    internal bool Allowed { get; }
    internal MotionIntentReason Reason { get; }
    internal string Detail { get; }

    private MotionPolicyDecision(bool allowed, MotionIntentReason reason, string detail)
    {
        Allowed = allowed;
        Reason = reason;
        Detail = detail;
    }

    internal static MotionPolicyDecision Allow()
        => new(true, MotionIntentReason.None, string.Empty);

    internal static MotionPolicyDecision Reject(MotionIntentReason reason, string detail)
        => new(false, reason, detail);
}

internal static class MotionIntentPolicy
{
    internal static MotionPolicyDecision Evaluate(MotionIntentRequest request, MotionPolicySnapshot snapshot)
    {
        if (request.Kind == MotionIntentKind.None)
            return MotionPolicyDecision.Reject(MotionIntentReason.InvalidIntent, "No motion intent was supplied.");
        if (!snapshot.CharacterAvailable)
            return MotionPolicyDecision.Reject(MotionIntentReason.CharacterUnavailable, "Lilith's motion controllers are unavailable.");
        if (snapshot.IsDragging)
            return MotionPolicyDecision.Reject(MotionIntentReason.Dragging, "The player is dragging Lilith.");
        if (snapshot.HasPendingOperation)
            return MotionPolicyDecision.Reject(MotionIntentReason.TransitionPending, "A motion transition is already pending.");
        if (snapshot.IsOverlayBehindWindow)
            return MotionPolicyDecision.Reject(MotionIntentReason.OverlayTransitionActive, "A behind-window transition is active.");
        if (snapshot.HasWalkContinuation)
            return MotionPolicyDecision.Reject(MotionIntentReason.WalkContinuationActive, "A directed walk is still in progress.");
        if (snapshot.IsStateMachineLocked)
            return MotionPolicyDecision.Reject(MotionIntentReason.StateMachineLocked, "The native state machine is locked.");
        var isSupportedWindowSitStand = request.Kind == MotionIntentKind.Stand
            && snapshot.IsSitting
            && snapshot.HasWindowPlatform;
        if (!snapshot.IsGrounded && !isSupportedWindowSitStand)
            return MotionPolicyDecision.Reject(MotionIntentReason.Airborne, "Lilith is not standing on a stable surface.");
        if (snapshot.IsActionAnimationPlaying)
            return MotionPolicyDecision.Reject(MotionIntentReason.ActionAnimationPlaying, "A one-shot animation is still playing.");
        if (snapshot.IsSleeping)
            return MotionPolicyDecision.Reject(MotionIntentReason.Sleeping, "Lilith is sleeping; model and proactive motion must not wake or reposition her.");
        if (request.Kind == MotionIntentKind.Sit && snapshot.IsSitting)
            return MotionPolicyDecision.Reject(MotionIntentReason.AlreadyInRequestedState, "Lilith is already sitting.");

        if (snapshot.ConversationBusy && request.Source != MotionIntentSource.Showcase)
            return MotionPolicyDecision.Reject(MotionIntentReason.ConversationBusy, "Conversation or voice input has priority.");

        if (snapshot.IsPajamas
            && request.Kind != MotionIntentKind.Stand)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.ClothingUnsupported,
                "Pajamas use a separate static-only action set. Until typed lie-down, sleep, and stretch intents are available, only Stand is supported.");
        }

        if (request.Kind == MotionIntentKind.ArriveAtWindow
            && snapshot.HasWindowPlatform)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.AlreadyInRequestedState,
                "Lilith is already standing on a supported window platform.");
        }

        if (request.Kind == MotionIntentKind.ArriveAtWindow
            && !snapshot.IsOnDesktopGround)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.DesktopGroundRequired,
                "Untargeted window arrival is only allowed from stable desktop ground.");
        }

        if (request.Kind is MotionIntentKind.WalkLeft or MotionIntentKind.WalkRight
            && !snapshot.HasWindowPlatform
            && !snapshot.IsOnDesktopGround)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.WalkableSurfaceRequired,
                "Directional walking requires a supported window platform or the desktop ground.");
        }

        if ((request.Kind == MotionIntentKind.WalkLeft && !snapshot.CanWalkLeft)
            || (request.Kind == MotionIntentKind.WalkRight && !snapshot.CanWalkRight))
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.NoWalkableDistance,
                "Lilith is already at the safe edge in the requested direction.");
        }

        if (request.Kind == MotionIntentKind.Sit
            && !snapshot.HasWindowPlatform
            && !snapshot.IsOnDesktopGround)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.WindowPlatformRequired,
                "Sitting requires either a supported window-top seat or stable desktop ground from which the dispatcher can transfer to a verified seat.");
        }

        if (request.Kind is MotionIntentKind.PeekFromWindow or MotionIntentKind.DropFromWindow
            && !snapshot.HasWindowPlatform)
        {
            return MotionPolicyDecision.Reject(
                MotionIntentReason.WindowPlatformRequired,
                "This motion requires a currently supported window platform.");
        }

        return MotionPolicyDecision.Allow();
    }
}

internal sealed class MotionIntentResult
{
    internal Guid RequestId { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentStatus Status { get; }
    internal MotionIntentReason Reason { get; }
    internal string Detail { get; }
    internal DateTimeOffset RecordedAt { get; }

    private MotionIntentResult(
        Guid requestId,
        MotionIntentKind kind,
        MotionIntentStatus status,
        MotionIntentReason reason,
        string detail)
    {
        RequestId = requestId;
        Kind = kind;
        Status = status;
        Reason = reason;
        Detail = detail;
        RecordedAt = DateTimeOffset.UtcNow;
    }

    internal static MotionIntentResult Started(MotionIntentRequest request, string detail = "")
        => new(request.Id, request.Kind, MotionIntentStatus.Started, MotionIntentReason.None, detail);

    internal static MotionIntentResult Completed(MotionIntentRequest request, string detail = "")
        => new(request.Id, request.Kind, MotionIntentStatus.Completed, MotionIntentReason.None, detail);

    internal static MotionIntentResult Interrupted(MotionIntentRequest request, MotionIntentReason reason, string detail)
        => new(request.Id, request.Kind, MotionIntentStatus.Interrupted, reason, detail);

    internal static MotionIntentResult Rejected(MotionIntentRequest request, MotionIntentReason reason, string detail)
        => new(request.Id, request.Kind, MotionIntentStatus.Rejected, reason, detail);

    internal static MotionIntentResult Failed(MotionIntentRequest request, string detail)
        => new(request.Id, request.Kind, MotionIntentStatus.Failed, MotionIntentReason.ExecutionFailed, detail);

    internal static MotionIntentResult Failed(MotionIntentRequest request, MotionIntentReason reason, string detail)
        => new(request.Id, request.Kind, MotionIntentStatus.Failed, reason, detail);
}

internal static class MotionIntentEvents
{
    private const int MaximumBufferedResults = 256;
    private static readonly object Gate = new();
    private static readonly Queue<MotionIntentResult> Results = new();

    internal static void Publish(MotionIntentResult result)
    {
        lock (Gate)
        {
            while (Results.Count >= MaximumBufferedResults)
                Results.Dequeue();
            Results.Enqueue(result);
        }
    }

    internal static bool TryDequeue(out MotionIntentResult? result)
    {
        lock (Gate)
        {
            if (Results.Count == 0)
            {
                result = null;
                return false;
            }

            result = Results.Dequeue();
            return true;
        }
    }

    internal static int BufferedCount
    {
        get
        {
            lock (Gate)
                return Results.Count;
        }
    }
}
