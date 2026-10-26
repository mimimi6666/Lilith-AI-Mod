using System;

namespace LilithTextInjector;

internal enum SituationEventKind
{
    GoalRegistered = 0,
    GoalStatusChanged = 1,
    AttentionChanged = 2,
    InteractionStateChanged = 3,
    InitiativeGateChanged = 4,
    ConflictChanged = 5,
    EvidenceChanged = 6
}

internal abstract class SituationEvent
{
    internal string Id { get; }
    internal long Sequence { get; }
    internal string SessionId { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal SituationEventKind Kind { get; }
    internal SituationTriggerKind TriggerKind { get; }

    protected SituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationEventKind kind,
        SituationTriggerKind triggerKind)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        SessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (!Enum.IsDefined(typeof(SituationEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(SituationTriggerKind), triggerKind)
            || triggerKind == SituationTriggerKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(triggerKind));
        }

        Sequence = sequence;
        Kind = kind;
        TriggerKind = triggerKind;
    }
}

internal sealed class GoalRegisteredSituationEvent : SituationEvent
{
    internal GoalState Goal { get; }

    internal GoalRegisteredSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        GoalState goal)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.GoalRegistered,
            triggerKind)
    {
        Goal = goal ?? throw new ArgumentNullException(nameof(goal));
        if (!string.Equals(goal.SessionId, SessionId, StringComparison.Ordinal))
            throw new ArgumentException("Goal and event must use the same session.", nameof(goal));
        if (goal.CreatedAt > OccurredAt)
            throw new ArgumentException("Goal cannot be created after its registration event.", nameof(goal));
        if (goal.Status is not GoalStatus.Candidate and not GoalStatus.Active)
        {
            throw new ArgumentException(
                "A newly registered goal must be Candidate or Active.",
                nameof(goal));
        }
        if (goal.Source is GoalSource.ExplicitPlayerRequest
            or GoalSource.ExplicitCommitment)
        {
            if (triggerKind != SituationTriggerKind.UserMessage)
            {
                throw new ArgumentException(
                    "Player requests and commitments require a user-message trigger.",
                    nameof(triggerKind));
            }
        }
        else if (goal.Source == GoalSource.Initiative
            && triggerKind == SituationTriggerKind.UserMessage)
        {
            throw new ArgumentException(
                "An initiative goal cannot claim a direct user-message trigger.",
                nameof(triggerKind));
        }
    }
}

internal sealed class GoalStatusChangedSituationEvent : SituationEvent
{
    internal string GoalId { get; }
    internal GoalStatus Status { get; }

    internal GoalStatusChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        string goalId,
        GoalStatus status)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.GoalStatusChanged,
            triggerKind)
    {
        GoalId = DecisionCoreContractGuard.Identifier(
            goalId,
            128,
            nameof(goalId));
        if (!Enum.IsDefined(typeof(GoalStatus), status)
            || status == GoalStatus.Candidate)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        Status = status;
    }
}

internal sealed class AttentionChangedSituationEvent : SituationEvent
{
    internal AttentionState Attention { get; }

    internal AttentionChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        AttentionState attention)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.AttentionChanged,
            triggerKind)
    {
        Attention = attention ?? throw new ArgumentNullException(nameof(attention));
        if (attention.FocusedSince > OccurredAt)
            throw new ArgumentException("Attention cannot begin after its event.", nameof(attention));
    }
}

internal sealed class InteractionStateChangedSituationEvent : SituationEvent
{
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }

    internal InteractionStateChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.InteractionStateChanged,
            triggerKind)
    {
        IsAvatarUnderDirectPlayerControl = isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
    }
}

internal sealed class InitiativeGateChangedSituationEvent : SituationEvent
{
    internal bool IsQuietPeriod { get; }
    internal bool IsInitiativeCooldownActive { get; }

    internal InitiativeGateChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        bool isQuietPeriod,
        bool isInitiativeCooldownActive)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.InitiativeGateChanged,
            triggerKind)
    {
        IsQuietPeriod = isQuietPeriod;
        IsInitiativeCooldownActive = isInitiativeCooldownActive;
    }
}

internal sealed class ConflictChangedSituationEvent : SituationEvent
{
    internal SituationConflictLevel ConflictLevel { get; }

    internal ConflictChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        SituationConflictLevel conflictLevel)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.ConflictChanged,
            triggerKind)
    {
        if (!Enum.IsDefined(typeof(SituationConflictLevel), conflictLevel))
            throw new ArgumentOutOfRangeException(nameof(conflictLevel));
        ConflictLevel = conflictLevel;
    }
}

internal sealed class EvidenceChangedSituationEvent : SituationEvent
{
    internal DecisionEvidence AvailableEvidence { get; }

    internal EvidenceChangedSituationEvent(
        string id,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        DecisionEvidence availableEvidence)
        : base(
            id,
            sequence,
            sessionId,
            occurredAt,
            SituationEventKind.EvidenceChanged,
            triggerKind)
    {
        if ((availableEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(availableEvidence));
        AvailableEvidence = availableEvidence;
    }
}
