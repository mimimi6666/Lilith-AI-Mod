using System;

namespace LilithTextInjector;

internal enum MotionDirectorMode
{
    Off,
    Observe,
    LocalRules
}

internal enum MotionDirectorDisposition
{
    Silence,
    Observe,
    Dispatch
}

internal enum MotionDirectorReason
{
    None,
    Disabled,
    UnsafeState,
    ClothingUnsupported,
    SeekWindow,
    ResumeFromSit,
    ExploreLeft,
    ExploreRight,
    React,
    Rest,
    Peek,
    Depart
}

internal readonly struct MotionDirectorSnapshot
{
    internal bool Enabled { get; }
    internal bool ObserveOnly { get; }
    internal bool CharacterAvailable { get; }
    internal bool ConversationBusy { get; }
    internal bool IsDragging { get; }
    internal bool IsStateMachineLocked { get; }
    internal bool IsGrounded { get; }
    internal bool IsNativeIdle { get; }
    internal bool IsPajamas { get; }
    internal bool HasWindowPlatform { get; }
    internal bool IsOnDesktopGround { get; }
    internal bool CanWalkLeft { get; }
    internal bool CanWalkRight { get; }
    internal bool HasPendingOperation { get; }
    internal bool IsOverlayBehindWindow { get; }
    internal bool HasWalkContinuation { get; }
    internal bool IsActionAnimationPlaying { get; }
    internal bool IsSitting { get; }
    internal bool SitCooldownActive { get; }
    internal bool DepartureAllowed { get; }
    internal int Step { get; }
    internal MotionIntentKind LastIntent { get; }

    internal MotionDirectorSnapshot(
        bool enabled,
        bool observeOnly,
        bool characterAvailable,
        bool conversationBusy,
        bool isDragging,
        bool isStateMachineLocked,
        bool isGrounded,
        bool isNativeIdle,
        bool isPajamas,
        bool hasWindowPlatform,
        bool isOnDesktopGround,
        bool canWalkLeft,
        bool canWalkRight,
        bool hasPendingOperation,
        bool isOverlayBehindWindow,
        bool hasWalkContinuation,
        bool isActionAnimationPlaying,
        bool isSitting,
        bool sitCooldownActive,
        bool departureAllowed,
        int step,
        MotionIntentKind lastIntent)
    {
        Enabled = enabled;
        ObserveOnly = observeOnly;
        CharacterAvailable = characterAvailable;
        ConversationBusy = conversationBusy;
        IsDragging = isDragging;
        IsStateMachineLocked = isStateMachineLocked;
        IsGrounded = isGrounded;
        IsNativeIdle = isNativeIdle;
        IsPajamas = isPajamas;
        HasWindowPlatform = hasWindowPlatform;
        IsOnDesktopGround = isOnDesktopGround;
        CanWalkLeft = canWalkLeft;
        CanWalkRight = canWalkRight;
        HasPendingOperation = hasPendingOperation;
        IsOverlayBehindWindow = isOverlayBehindWindow;
        HasWalkContinuation = hasWalkContinuation;
        IsActionAnimationPlaying = isActionAnimationPlaying;
        IsSitting = isSitting;
        SitCooldownActive = sitCooldownActive;
        DepartureAllowed = departureAllowed;
        Step = step;
        LastIntent = lastIntent;
    }
}

internal sealed class MotionDirectorProposal
{
    internal MotionDirectorDisposition Disposition { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentSource Source { get; }
    internal MotionDirectorReason Reason { get; }
    internal string ProviderId { get; }

    private MotionDirectorProposal(
        MotionDirectorDisposition disposition,
        MotionIntentKind kind,
        MotionIntentSource source,
        MotionDirectorReason reason,
        string providerId)
    {
        Disposition = disposition;
        Kind = kind;
        Source = source;
        Reason = reason;
        ProviderId = providerId;
    }

    internal static MotionDirectorProposal Silence(MotionDirectorReason reason, string providerId)
        => new(
            MotionDirectorDisposition.Silence,
            MotionIntentKind.None,
            MotionIntentSource.Initiative,
            reason,
            providerId);

    internal static MotionDirectorProposal Propose(
        MotionIntentKind kind,
        MotionDirectorReason reason,
        string providerId,
        bool observeOnly)
        => new(
            observeOnly ? MotionDirectorDisposition.Observe : MotionDirectorDisposition.Dispatch,
            kind,
            MotionIntentSource.Initiative,
            reason,
            providerId);
}

internal interface IMotionDecisionProvider
{
    string Id { get; }

    MotionDirectorProposal Propose(
        MotionDirectorSnapshot snapshot,
        int primaryRoll,
        int secondaryRoll);
}

internal sealed class LocalRuleMotionDecisionProvider : IMotionDecisionProvider
{
    internal const string ProviderName = "builtin.local-rules.v1";

    public string Id => ProviderName;

    public MotionDirectorProposal Propose(
        MotionDirectorSnapshot snapshot,
        int primaryRoll,
        int secondaryRoll)
    {
        if (!snapshot.Enabled)
            return MotionDirectorProposal.Silence(MotionDirectorReason.Disabled, Id);

        if (!snapshot.CharacterAvailable
            || snapshot.ConversationBusy
            || snapshot.IsDragging
            || snapshot.IsStateMachineLocked
            || !snapshot.IsGrounded
            || (!snapshot.IsNativeIdle && !snapshot.IsSitting)
            || snapshot.HasPendingOperation
            || snapshot.IsOverlayBehindWindow
            || snapshot.HasWalkContinuation
            || snapshot.IsActionAnimationPlaying)
        {
            return MotionDirectorProposal.Silence(MotionDirectorReason.UnsafeState, Id);
        }

        if (snapshot.IsPajamas)
        {
            return MotionDirectorProposal.Silence(
                MotionDirectorReason.ClothingUnsupported,
                Id);
        }

        if (snapshot.IsSitting)
        {
            return MotionDirectorProposal.Propose(
                MotionIntentKind.Stand,
                MotionDirectorReason.ResumeFromSit,
                Id,
                snapshot.ObserveOnly);
        }

        if (!snapshot.HasWindowPlatform)
        {
            if (snapshot.IsOnDesktopGround)
            {
                primaryRoll = Math.Clamp(primaryRoll, 0, 99);
                secondaryRoll = Math.Clamp(secondaryRoll, 0, 99);
                var groundKind = ChooseGroundIntent(snapshot, primaryRoll, secondaryRoll);
                if (groundKind == MotionIntentKind.Sit && snapshot.SitCooldownActive)
                    groundKind = ChooseAvailableWalkOrReaction(snapshot, secondaryRoll);

                return MotionDirectorProposal.Propose(
                    groundKind,
                    ReasonFor(groundKind),
                    Id,
                    snapshot.ObserveOnly);
            }

            return MotionDirectorProposal.Propose(
                MotionIntentKind.ArriveAtWindow,
                MotionDirectorReason.SeekWindow,
                Id,
                snapshot.ObserveOnly);
        }

        primaryRoll = Math.Clamp(primaryRoll, 0, 99);
        secondaryRoll = Math.Clamp(secondaryRoll, 0, 99);
        var kind = ChoosePlatformIntent(snapshot, primaryRoll, secondaryRoll);
        if (kind == MotionIntentKind.Sit && snapshot.SitCooldownActive)
        {
            kind = secondaryRoll < 55
                ? ChooseAvailableWalkOrReaction(snapshot, secondaryRoll)
                : MotionIntentKind.PlayOneShot;
        }

        return MotionDirectorProposal.Propose(
            kind,
            ReasonFor(kind),
            Id,
            snapshot.ObserveOnly);
    }

    private static MotionIntentKind ChooseGroundIntent(
        MotionDirectorSnapshot snapshot,
        int roll,
        int directionRoll)
    {
        if (roll < 62)
            return ChooseAvailableWalkOrReaction(snapshot, directionRoll);
        if (roll < 90)
            return MotionIntentKind.PlayOneShot;
        return MotionIntentKind.ArriveAtWindow;
    }

    private static MotionIntentKind ChoosePlatformIntent(
        MotionDirectorSnapshot snapshot,
        int roll,
        int directionRoll)
    {
        return snapshot.LastIntent switch
        {
            MotionIntentKind.ArriveAtWindow => roll < 48
                ? ChooseAvailableWalkOrReaction(snapshot, directionRoll)
                : roll < 78 ? MotionIntentKind.PlayOneShot : MotionIntentKind.Sit,
            MotionIntentKind.WalkLeft => roll < 38
                ? MotionIntentKind.PlayOneShot
                : roll < 63 ? MotionIntentKind.Sit
                : roll < 88 ? ChoosePreferredWalkOrReaction(
                    snapshot,
                    MotionIntentKind.WalkRight,
                    directionRoll)
                : snapshot.DepartureAllowed ? MotionIntentKind.DropFromWindow : MotionIntentKind.PlayOneShot,
            MotionIntentKind.WalkRight => roll < 38
                ? MotionIntentKind.PlayOneShot
                : roll < 63 ? MotionIntentKind.Sit
                : roll < 88 ? ChoosePreferredWalkOrReaction(
                    snapshot,
                    MotionIntentKind.WalkLeft,
                    directionRoll)
                : snapshot.DepartureAllowed ? MotionIntentKind.DropFromWindow : MotionIntentKind.PlayOneShot,
            MotionIntentKind.PlayOneShot => roll < 45
                ? ChooseAvailableWalkOrReaction(snapshot, directionRoll)
                : roll < 70 ? MotionIntentKind.Sit
                : roll < 88 && snapshot.DepartureAllowed ? MotionIntentKind.PeekFromWindow
                : snapshot.DepartureAllowed
                    ? MotionIntentKind.DropFromWindow
                    : ChooseAvailableWalkOrReaction(snapshot, directionRoll),
            MotionIntentKind.Stand => roll < 58
                ? ChooseAvailableWalkOrReaction(snapshot, directionRoll)
                : roll < 88 ? MotionIntentKind.PlayOneShot
                : snapshot.DepartureAllowed ? MotionIntentKind.DropFromWindow : MotionIntentKind.PlayOneShot,
            MotionIntentKind.PeekFromWindow => roll < 55
                ? ChooseAvailableWalkOrReaction(snapshot, directionRoll)
                : roll < 85 ? MotionIntentKind.PlayOneShot : MotionIntentKind.Sit,
            _ => roll < 50
                ? ChooseAvailableWalkOrReaction(snapshot, directionRoll)
                : roll < 78 ? MotionIntentKind.PlayOneShot : MotionIntentKind.Sit
        };
    }

    private static MotionIntentKind ChooseWalkDirection(
        MotionIntentKind lastIntent,
        int directionRoll)
    {
        if (lastIntent == MotionIntentKind.WalkLeft)
            return MotionIntentKind.WalkRight;
        if (lastIntent == MotionIntentKind.WalkRight)
            return MotionIntentKind.WalkLeft;
        return directionRoll < 50 ? MotionIntentKind.WalkLeft : MotionIntentKind.WalkRight;
    }

    private static MotionIntentKind ChooseAvailableWalkOrReaction(
        MotionDirectorSnapshot snapshot,
        int directionRoll)
    {
        if (snapshot.CanWalkLeft && !snapshot.CanWalkRight)
            return MotionIntentKind.WalkLeft;
        if (snapshot.CanWalkRight && !snapshot.CanWalkLeft)
            return MotionIntentKind.WalkRight;
        if (!snapshot.CanWalkLeft && !snapshot.CanWalkRight)
            return MotionIntentKind.PlayOneShot;

        return ChooseWalkDirection(snapshot.LastIntent, directionRoll);
    }

    private static MotionIntentKind ChoosePreferredWalkOrReaction(
        MotionDirectorSnapshot snapshot,
        MotionIntentKind preferred,
        int directionRoll)
    {
        if (preferred == MotionIntentKind.WalkLeft && snapshot.CanWalkLeft)
            return MotionIntentKind.WalkLeft;
        if (preferred == MotionIntentKind.WalkRight && snapshot.CanWalkRight)
            return MotionIntentKind.WalkRight;
        return ChooseAvailableWalkOrReaction(snapshot, directionRoll);
    }

    private static MotionDirectorReason ReasonFor(MotionIntentKind kind) => kind switch
    {
        MotionIntentKind.ArriveAtWindow => MotionDirectorReason.SeekWindow,
        MotionIntentKind.WalkLeft => MotionDirectorReason.ExploreLeft,
        MotionIntentKind.WalkRight => MotionDirectorReason.ExploreRight,
        MotionIntentKind.PlayOneShot => MotionDirectorReason.React,
        MotionIntentKind.Sit => MotionDirectorReason.Rest,
        MotionIntentKind.Stand => MotionDirectorReason.ResumeFromSit,
        MotionIntentKind.PeekFromWindow => MotionDirectorReason.Peek,
        MotionIntentKind.DropFromWindow => MotionDirectorReason.Depart,
        _ => MotionDirectorReason.None
    };
}
