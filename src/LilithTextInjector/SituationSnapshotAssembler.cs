using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class SituationProjection
{
    internal string SessionId { get; }
    internal DateTimeOffset AsOf { get; }
    internal long LastEventSequence { get; }
    internal SituationEvent? LastEvent { get; }
    internal SituationConflictLevel ConflictLevel { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool IsInitiativeCooldownActive { get; }
    internal DecisionEvidence AvailableEvidence { get; }
    internal AttentionState Attention { get; }
    internal IReadOnlyList<GoalState> Goals { get; }

    private SituationProjection(
        string sessionId,
        DateTimeOffset asOf,
        long lastEventSequence,
        SituationEvent? lastEvent,
        SituationConflictLevel conflictLevel,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool isInitiativeCooldownActive,
        DecisionEvidence availableEvidence,
        AttentionState attention,
        IReadOnlyList<GoalState> goals)
    {
        SessionId = sessionId;
        AsOf = asOf;
        LastEventSequence = lastEventSequence;
        LastEvent = lastEvent;
        ConflictLevel = conflictLevel;
        IsAvatarUnderDirectPlayerControl = isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        IsInitiativeCooldownActive = isInitiativeCooldownActive;
        AvailableEvidence = availableEvidence;
        Attention = attention;
        Goals = goals;
    }

    internal static SituationProjection Create(
        string sessionId,
        DateTimeOffset asOf)
        => new(
            DecisionCoreContractGuard.Identifier(sessionId, 128, nameof(sessionId)),
            DecisionCoreContractGuard.Timestamp(asOf, nameof(asOf)),
            lastEventSequence: 0,
            lastEvent: null,
            SituationConflictLevel.None,
            isAvatarUnderDirectPlayerControl: false,
            isConversationBusy: false,
            hasPendingExecution: false,
            isQuietPeriod: false,
            isInitiativeCooldownActive: false,
            DecisionEvidence.None,
            AttentionState.None,
            Array.Empty<GoalState>());

    internal static SituationProjection FromReducedState(
        SituationProjection previous,
        SituationEvent situationEvent,
        SituationConflictLevel conflictLevel,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool isInitiativeCooldownActive,
        DecisionEvidence availableEvidence,
        AttentionState attention,
        IReadOnlyList<GoalState> goals)
    {
        if (previous == null)
            throw new ArgumentNullException(nameof(previous));
        if (situationEvent == null)
            throw new ArgumentNullException(nameof(situationEvent));
        if (attention == null)
            throw new ArgumentNullException(nameof(attention));
        if (goals == null)
            throw new ArgumentNullException(nameof(goals));

        var copy = new GoalState[goals.Count];
        for (var index = 0; index < goals.Count; index++)
            copy[index] = goals[index];

        return new SituationProjection(
            previous.SessionId,
            situationEvent.OccurredAt,
            situationEvent.Sequence,
            situationEvent,
            conflictLevel,
            isAvatarUnderDirectPlayerControl,
            isConversationBusy,
            hasPendingExecution,
            isQuietPeriod,
            isInitiativeCooldownActive,
            availableEvidence,
            attention,
            Array.AsReadOnly(copy));
    }
}

internal static class SituationEventReducer
{
    internal static SituationProjection Apply(
        SituationProjection projection,
        SituationEvent situationEvent)
    {
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        if (situationEvent == null)
            throw new ArgumentNullException(nameof(situationEvent));
        if (!string.Equals(
                projection.SessionId,
                situationEvent.SessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Situation event belongs to another session.",
                nameof(situationEvent));
        }
        if (situationEvent.Sequence != projection.LastEventSequence + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(situationEvent),
                "Situation event sequence must be consecutive.");
        }
        if (situationEvent.OccurredAt < projection.AsOf)
        {
            throw new ArgumentOutOfRangeException(
                nameof(situationEvent),
                "Situation events must be reduced in timestamp order.");
        }

        var conflictLevel = projection.ConflictLevel;
        var directControl = projection.IsAvatarUnderDirectPlayerControl;
        var conversationBusy = projection.IsConversationBusy;
        var pendingExecution = projection.HasPendingExecution;
        var quietPeriod = projection.IsQuietPeriod;
        var initiativeCooldown = projection.IsInitiativeCooldownActive;
        var availableEvidence = projection.AvailableEvidence;
        var attention = projection.Attention;
        var goals = CopyGoals(projection.Goals);

        switch (situationEvent)
        {
            case GoalRegisteredSituationEvent registered:
                RegisterGoal(goals, registered.Goal);
                break;
            case GoalStatusChangedSituationEvent statusChanged:
                ChangeGoalStatus(goals, statusChanged.GoalId, statusChanged.Status);
                if (string.Equals(
                        attention.FocusedGoalId,
                        statusChanged.GoalId,
                        StringComparison.Ordinal)
                    && statusChanged.Status is not GoalStatus.Active
                        and not GoalStatus.Candidate)
                {
                    attention = AttentionState.None;
                }
                break;
            case AttentionChangedSituationEvent attentionChanged:
                ValidateAttention(goals, attentionChanged.Attention);
                attention = attentionChanged.Attention;
                break;
            case InteractionStateChangedSituationEvent interaction:
                directControl = interaction.IsAvatarUnderDirectPlayerControl;
                conversationBusy = interaction.IsConversationBusy;
                pendingExecution = interaction.HasPendingExecution;
                break;
            case InitiativeGateChangedSituationEvent initiative:
                quietPeriod = initiative.IsQuietPeriod;
                initiativeCooldown = initiative.IsInitiativeCooldownActive;
                break;
            case ConflictChangedSituationEvent conflict:
                conflictLevel = conflict.ConflictLevel;
                break;
            case EvidenceChangedSituationEvent evidence:
                availableEvidence = evidence.AvailableEvidence;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(situationEvent),
                    "Unknown situation event type.");
        }

        return SituationProjection.FromReducedState(
            projection,
            situationEvent,
            conflictLevel,
            directControl,
            conversationBusy,
            pendingExecution,
            quietPeriod,
            initiativeCooldown,
            availableEvidence,
            attention,
            goals);
    }

    private static List<GoalState> CopyGoals(IReadOnlyList<GoalState> source)
    {
        var copy = new List<GoalState>(source.Count);
        foreach (var goal in source)
            copy.Add(goal);
        return copy;
    }

    private static void RegisterGoal(List<GoalState> goals, GoalState goal)
    {
        foreach (var existing in goals)
        {
            if (string.Equals(existing.Id, goal.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Goal is already registered.");
        }
        goals.Add(goal);
        goals.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
    }

    private static void ChangeGoalStatus(
        List<GoalState> goals,
        string goalId,
        GoalStatus nextStatus)
    {
        for (var index = 0; index < goals.Count; index++)
        {
            var current = goals[index];
            if (!string.Equals(current.Id, goalId, StringComparison.Ordinal))
                continue;
            if (!IsAllowedTransition(current.Status, nextStatus))
            {
                throw new InvalidOperationException(
                    "Goal status transition is not allowed.");
            }
            goals[index] = CopyGoalWithStatus(current, nextStatus);
            return;
        }
        throw new InvalidOperationException("Goal is not registered.");
    }

    private static bool IsAllowedTransition(GoalStatus current, GoalStatus next)
        => current switch
        {
            GoalStatus.Candidate =>
                next is GoalStatus.Active
                    or GoalStatus.Suspended
                    or GoalStatus.Cancelled
                    or GoalStatus.Expired,
            GoalStatus.Active =>
                next is GoalStatus.Suspended
                    or GoalStatus.Completed
                    or GoalStatus.Cancelled
                    or GoalStatus.Expired,
            GoalStatus.Suspended =>
                next is GoalStatus.Active
                    or GoalStatus.Completed
                    or GoalStatus.Cancelled
                    or GoalStatus.Expired,
            _ => false
        };

    private static GoalState CopyGoalWithStatus(GoalState goal, GoalStatus status)
        => new(
            goal.Id,
            goal.SessionId,
            goal.Source,
            goal.PriorityClass,
            status,
            goal.PersistenceScope,
            goal.DesiredDisposition,
            goal.RequiredEvidence,
            goal.Urgency,
            goal.InterruptionCost,
            goal.RequiresConsent,
            goal.CreatedAt,
            goal.Deadline,
            goal.ExpiresAt,
            goal.EvidenceCandidateId,
            attentionCost: goal.AttentionCost,
            requiresAttentionBudget: goal.RequiresAttentionBudget);

    private static void ValidateAttention(
        IReadOnlyList<GoalState> goals,
        AttentionState attention)
    {
        if (attention.FocusedGoalId == null)
            return;
        foreach (var goal in goals)
        {
            if (!string.Equals(
                    goal.Id,
                    attention.FocusedGoalId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (goal.Status is GoalStatus.Active or GoalStatus.Candidate)
                return;
            throw new InvalidOperationException(
                "Attention cannot focus a non-eligible goal.");
        }
        throw new InvalidOperationException(
            "Attention cannot focus an unregistered goal.");
    }
}

internal static class SituationSnapshotAssembler
{
    internal static readonly TimeSpan MaximumValidity = TimeSpan.FromMinutes(5);

    internal static SituationSnapshot Assemble(
        SituationProjection projection,
        Guid snapshotId,
        long snapshotSequence,
        TimeSpan validity,
        DecisionBudgets? budgets = null,
        IReadOnlyList<EvidenceTerminalState>? evidenceTerminalStates = null)
    {
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        if (projection.LastEvent == null)
        {
            throw new InvalidOperationException(
                "A snapshot requires at least one reduced situation event.");
        }
        if (validity <= TimeSpan.Zero || validity > MaximumValidity)
            throw new ArgumentOutOfRangeException(nameof(validity));

        return new SituationSnapshot(
            snapshotId,
            snapshotSequence,
            projection.SessionId,
            projection.AsOf,
            projection.AsOf.Add(validity),
            new SituationTrigger(
                projection.LastEvent.Id,
                projection.LastEvent.TriggerKind,
                projection.LastEvent.OccurredAt),
            projection.ConflictLevel,
            projection.IsAvatarUnderDirectPlayerControl,
            projection.IsConversationBusy,
            projection.HasPendingExecution,
            projection.IsQuietPeriod,
            projection.IsInitiativeCooldownActive,
            projection.AvailableEvidence,
            budgets,
            projection.Attention,
            projection.Goals,
            evidenceTerminalStates);
    }
}
