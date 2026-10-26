using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum SituationTriggerKind
{
    None = 0,
    UserMessage = 1,
    AvatarStateChanged = 2,
    ExecutionOutcome = 3,
    Timer = 4,
    PresenceChanged = 5,
    InternalStateChanged = 6
}

internal enum SituationConflictLevel
{
    None = 0,
    Partial = 1,
    Critical = 2
}

internal enum GoalSource
{
    Safety = 0,
    ExplicitPlayerRequest = 1,
    ExplicitCommitment = 2,
    ConversationContinuity = 3,
    CharacterContinuity = 4,
    Initiative = 5
}

internal enum GoalPriorityClass
{
    Safety = 0,
    ExplicitPlayerRequest = 1,
    ExplicitCommitment = 2,
    ConversationContinuity = 3,
    CharacterContinuity = 4,
    Initiative = 5
}

internal enum GoalStatus
{
    Candidate = 0,
    Active = 1,
    Suspended = 2,
    Completed = 3,
    Cancelled = 4,
    Expired = 5
}

internal enum GoalPersistenceScope
{
    Session = 0,
    DurableCommitment = 1
}

internal enum AttentionFocusKind
{
    None = 0,
    PlayerRequest = 1,
    PendingExecution = 2,
    Consent = 3,
    OpenLoop = 4,
    Initiative = 5
}

internal enum DecisionDisposition
{
    Silence = 0,
    Observe = 1,
    Defer = 2,
    Respond = 3,
    Act = 4,
    AskConsent = 5
}

internal enum DecisionReason
{
    NoEligibleGoal = 0,
    SnapshotExpired = 1,
    CriticalConflict = 2,
    DirectPlayerControl = 3,
    PendingExecution = 4,
    ConversationBusy = 5,
    QuietPeriod = 6,
    InitiativeCooldown = 7,
    EvidenceInvocationPlanned = 8,
    EvidenceUnavailable = 9,
    ConsentRequired = 10,
    GoalSelected = 11,
    EvidenceInsufficient = 12,
    AttentionBudgetUnavailable = 13,
    AttentionBudgetExhausted = 14
}

internal enum EvidenceTerminalReason
{
    SituationRevisionChanged = 0,
    CandidateExpired = 1,
    CandidateWithdrawn = 2,
    EpochDraining = 3,
    SourceDisabled = 4,
    MasterDisabled = 5,
    SessionClosed = 6,
    InvocationCancelled = 7,
    InvocationTimedOut = 8,
    EvidenceUnavailable = 9,
    EvidenceInsufficient = 10,
    AssessmentCompleted = 11
}

[Flags]
internal enum DecisionEvidence
{
    None = 0,
    Memory = 1 << 0,
    Reasoning = 1 << 1,
    CharacterState = 1 << 2,
    RelationshipState = 1 << 3,
    CapabilityState = 1 << 4
}

internal enum OutcomeStatus
{
    Unknown = 0,
    Rejected = 1,
    ConsentPending = 2,
    Started = 3,
    Completed = 4,
    Interrupted = 5,
    Failed = 6,
    TimedOut = 7
}

internal enum CausalConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal enum AdaptationProposalKind
{
    None = 0,
    ReviewGoalPriority = 1,
    ReviewInterventionTiming = 2,
    ReviewCapabilityChoice = 3
}

internal sealed class SituationTrigger
{
    internal string Id { get; }
    internal SituationTriggerKind Kind { get; }
    internal DateTimeOffset ObservedAt { get; }

    internal SituationTrigger(
        string id,
        SituationTriggerKind kind,
        DateTimeOffset observedAt)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        if (!Enum.IsDefined(typeof(SituationTriggerKind), kind) || kind == SituationTriggerKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        ObservedAt = DecisionCoreContractGuard.Timestamp(observedAt, nameof(observedAt));
    }
}

internal sealed class DecisionBudgets
{
    internal static DecisionBudgets ShadowDefault { get; } = new(
        maximumMemoryItems: 0,
        maximumContextCharacters: 0,
        maximumReasoningCalls: 0);

    internal int MaximumMemoryItems { get; }
    internal int MaximumContextCharacters { get; }
    internal int MaximumReasoningCalls { get; }
    internal double? MaximumAttentionCost { get; }

    internal DecisionBudgets(
        int maximumMemoryItems,
        int maximumContextCharacters,
        int maximumReasoningCalls,
        double? maximumAttentionCost = null)
    {
        if (maximumMemoryItems < 0 || maximumMemoryItems > 64)
            throw new ArgumentOutOfRangeException(nameof(maximumMemoryItems));
        if (maximumContextCharacters < 0 || maximumContextCharacters > 32_000)
            throw new ArgumentOutOfRangeException(nameof(maximumContextCharacters));
        if (maximumReasoningCalls < 0 || maximumReasoningCalls > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumReasoningCalls));
        if (maximumAttentionCost.HasValue
            && (double.IsNaN(maximumAttentionCost.Value)
                || maximumAttentionCost.Value < 0d
                || maximumAttentionCost.Value > 1d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAttentionCost));
        }

        MaximumMemoryItems = maximumMemoryItems;
        MaximumContextCharacters = maximumContextCharacters;
        MaximumReasoningCalls = maximumReasoningCalls;
        MaximumAttentionCost = maximumAttentionCost;
    }
}

internal sealed class EvidenceTerminalState
{
    internal string CandidateId { get; }
    internal string CandidateFingerprint { get; }
    internal string GoalId { get; }
    internal string PairId { get; }
    internal string InquiryId { get; }
    internal bool EvidenceRoundConsumed => true;
    internal EvidenceTerminalReason Reason { get; }
    internal long OriginatingSituationRevision { get; }
    internal long TerminalSituationRevision { get; }

    internal EvidenceTerminalState(
        string candidateId,
        string candidateFingerprint,
        string goalId,
        string pairId,
        string inquiryId,
        EvidenceTerminalReason reason,
        long originatingSituationRevision,
        long terminalSituationRevision)
    {
        CandidateId = DecisionCoreContractGuard.Identifier(
            candidateId,
            128,
            nameof(candidateId));
        CandidateFingerprint = DecisionCoreContractGuard.Identifier(
            candidateFingerprint,
            128,
            nameof(candidateFingerprint));
        GoalId = DecisionCoreContractGuard.Identifier(
            goalId,
            128,
            nameof(goalId));
        PairId = DecisionCoreContractGuard.Identifier(
            pairId,
            128,
            nameof(pairId));
        InquiryId = DecisionCoreContractGuard.Identifier(
            inquiryId,
            128,
            nameof(inquiryId));
        if (!Enum.IsDefined(typeof(EvidenceTerminalReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (originatingSituationRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(originatingSituationRevision));
        }
        if (terminalSituationRevision < originatingSituationRevision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminalSituationRevision));
        }
        if (reason == EvidenceTerminalReason.SituationRevisionChanged
            && terminalSituationRevision == originatingSituationRevision)
        {
            throw new ArgumentException(
                "Situation revision retirement requires a later revision.",
                nameof(terminalSituationRevision));
        }

        Reason = reason;
        OriginatingSituationRevision = originatingSituationRevision;
        TerminalSituationRevision = terminalSituationRevision;
    }

    internal bool HasSameIdentity(EvidenceTerminalState other)
        => other != null
            && string.Equals(
                CandidateId,
                other.CandidateId,
                StringComparison.Ordinal)
            && string.Equals(
                CandidateFingerprint,
                other.CandidateFingerprint,
                StringComparison.Ordinal)
            && string.Equals(GoalId, other.GoalId, StringComparison.Ordinal)
            && string.Equals(PairId, other.PairId, StringComparison.Ordinal)
            && string.Equals(
                InquiryId,
                other.InquiryId,
                StringComparison.Ordinal)
            && Reason == other.Reason
            && OriginatingSituationRevision
                == other.OriginatingSituationRevision
            && TerminalSituationRevision
                == other.TerminalSituationRevision;
}

internal sealed class GoalState
{
    internal string Id { get; }
    internal string SessionId { get; }
    internal GoalSource Source { get; }
    internal GoalPriorityClass PriorityClass { get; }
    internal GoalStatus Status { get; }
    internal GoalPersistenceScope PersistenceScope { get; }
    internal DecisionDisposition DesiredDisposition { get; }
    internal DecisionEvidence RequiredEvidence { get; }
    internal double Urgency { get; }
    internal double AttentionCost { get; }
    internal double InterruptionCost { get; }
    internal bool RequiresAttentionBudget { get; }
    internal bool RequiresConsent { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset? Deadline { get; }
    internal DateTimeOffset? ExpiresAt { get; }
    internal string? EvidenceCandidateId { get; }

    internal GoalState(
        string id,
        string sessionId,
        GoalSource source,
        GoalPriorityClass priorityClass,
        GoalStatus status,
        GoalPersistenceScope persistenceScope,
        DecisionDisposition desiredDisposition,
        DecisionEvidence requiredEvidence,
        double urgency,
        double interruptionCost,
        bool requiresConsent,
        DateTimeOffset createdAt,
        DateTimeOffset? deadline = null,
        DateTimeOffset? expiresAt = null,
        string? evidenceCandidateId = null,
        double attentionCost = 0d,
        bool requiresAttentionBudget = false)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        SessionId = DecisionCoreContractGuard.Identifier(sessionId, 128, nameof(sessionId));
        if (!Enum.IsDefined(typeof(GoalSource), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(typeof(GoalPriorityClass), priorityClass))
            throw new ArgumentOutOfRangeException(nameof(priorityClass));
        if (DecisionCoreContractGuard.PriorityClassFor(source) != priorityClass)
        {
            throw new ArgumentException(
                "Goal source cannot claim a different priority class.",
                nameof(priorityClass));
        }
        if (!Enum.IsDefined(typeof(GoalStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(typeof(GoalPersistenceScope), persistenceScope))
            throw new ArgumentOutOfRangeException(nameof(persistenceScope));
        if (desiredDisposition is not DecisionDisposition.Respond
            and not DecisionDisposition.Act
            and not DecisionDisposition.AskConsent)
        {
            throw new ArgumentOutOfRangeException(nameof(desiredDisposition));
        }
        if ((requiredEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(requiredEvidence));
        if (double.IsNaN(urgency) || urgency < 0 || urgency > 1)
            throw new ArgumentOutOfRangeException(nameof(urgency));
        if (double.IsNaN(interruptionCost) || interruptionCost < 0 || interruptionCost > 1)
            throw new ArgumentOutOfRangeException(nameof(interruptionCost));
        if (double.IsNaN(attentionCost)
            || attentionCost < 0d
            || attentionCost > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(attentionCost));
        }
        if (requiresAttentionBudget
            && (source != GoalSource.Initiative || attentionCost <= 0d))
        {
            throw new ArgumentException(
                "Only a non-zero Initiative attention cost may require an attention budget.",
                nameof(requiresAttentionBudget));
        }
        if (persistenceScope == GoalPersistenceScope.DurableCommitment
            && source != GoalSource.ExplicitCommitment)
        {
            throw new ArgumentException(
                "Only an explicit commitment may request durable persistence.",
                nameof(persistenceScope));
        }

        var normalizedCreatedAt = DecisionCoreContractGuard.Timestamp(createdAt, nameof(createdAt));
        var normalizedDeadline = DecisionCoreContractGuard.OptionalTimestamp(deadline, nameof(deadline));
        var normalizedExpiresAt = DecisionCoreContractGuard.OptionalTimestamp(expiresAt, nameof(expiresAt));
        if (normalizedDeadline.HasValue && normalizedDeadline.Value < normalizedCreatedAt)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        if (normalizedExpiresAt.HasValue && normalizedExpiresAt.Value <= normalizedCreatedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));

        Source = source;
        PriorityClass = priorityClass;
        Status = status;
        PersistenceScope = persistenceScope;
        DesiredDisposition = desiredDisposition;
        RequiredEvidence = requiredEvidence;
        Urgency = urgency;
        AttentionCost = attentionCost;
        InterruptionCost = interruptionCost;
        RequiresAttentionBudget = requiresAttentionBudget;
        RequiresConsent = requiresConsent;
        CreatedAt = normalizedCreatedAt;
        Deadline = normalizedDeadline;
        ExpiresAt = normalizedExpiresAt;
        if (evidenceCandidateId != null)
        {
            EvidenceCandidateId = DecisionCoreContractGuard.Identifier(
                evidenceCandidateId,
                128,
                nameof(evidenceCandidateId));
        }
    }
}

internal sealed class AttentionState
{
    internal static AttentionState None { get; } = new(
        AttentionFocusKind.None,
        focusedGoalId: null,
        focusedSince: null);

    internal AttentionFocusKind Kind { get; }
    internal string? FocusedGoalId { get; }
    internal DateTimeOffset? FocusedSince { get; }

    internal AttentionState(
        AttentionFocusKind kind,
        string? focusedGoalId,
        DateTimeOffset? focusedSince)
    {
        if (!Enum.IsDefined(typeof(AttentionFocusKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == AttentionFocusKind.None)
        {
            if (focusedGoalId != null || focusedSince.HasValue)
                throw new ArgumentException("Empty attention cannot reference a goal.");
            Kind = kind;
            return;
        }

        FocusedGoalId = DecisionCoreContractGuard.Identifier(
            focusedGoalId,
            128,
            nameof(focusedGoalId));
        FocusedSince = DecisionCoreContractGuard.OptionalTimestamp(
            focusedSince,
            nameof(focusedSince))
            ?? throw new ArgumentNullException(nameof(focusedSince));
        Kind = kind;
    }
}

internal sealed class SituationSnapshot
{
    internal const int MaximumGoals = 64;
    internal const int MaximumEvidenceTerminalStates = 64;

    internal Guid Id { get; }
    internal long Sequence { get; }
    internal string SessionId { get; }
    internal DateTimeOffset AsOf { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal SituationTrigger Trigger { get; }
    internal SituationConflictLevel ConflictLevel { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool IsInitiativeCooldownActive { get; }
    internal DecisionEvidence AvailableEvidence { get; }
    internal DecisionBudgets Budgets { get; }
    internal AttentionState Attention { get; }
    internal IReadOnlyList<GoalState> Goals { get; }
    internal IReadOnlyList<EvidenceTerminalState> EvidenceTerminalStates { get; }

    internal SituationSnapshot(
        Guid id,
        long sequence,
        string sessionId,
        DateTimeOffset asOf,
        DateTimeOffset validUntil,
        SituationTrigger trigger,
        SituationConflictLevel conflictLevel,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool isInitiativeCooldownActive,
        DecisionEvidence availableEvidence,
        DecisionBudgets? budgets,
        AttentionState? attention,
        IReadOnlyList<GoalState>? goals,
        IReadOnlyList<EvidenceTerminalState>? evidenceTerminalStates = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Snapshot ID cannot be empty.", nameof(id));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (trigger == null)
            throw new ArgumentNullException(nameof(trigger));
        if (!Enum.IsDefined(typeof(SituationConflictLevel), conflictLevel))
            throw new ArgumentOutOfRangeException(nameof(conflictLevel));
        if ((availableEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(availableEvidence));

        var normalizedSessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        var normalizedAsOf = DecisionCoreContractGuard.Timestamp(asOf, nameof(asOf));
        var normalizedValidUntil = DecisionCoreContractGuard.Timestamp(validUntil, nameof(validUntil));
        if (normalizedValidUntil <= normalizedAsOf)
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        if (trigger.ObservedAt > normalizedAsOf)
            throw new ArgumentException("Trigger cannot occur after the snapshot.", nameof(trigger));

        var sourceGoals = goals ?? Array.Empty<GoalState>();
        if (sourceGoals.Count > MaximumGoals)
            throw new ArgumentOutOfRangeException(nameof(goals));
        var copiedGoals = new GoalState[sourceGoals.Count];
        var goalIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sourceGoals.Count; index++)
        {
            var goal = sourceGoals[index]
                ?? throw new ArgumentException("Goal collection cannot contain null.", nameof(goals));
            if (!string.Equals(goal.SessionId, normalizedSessionId, StringComparison.Ordinal))
                throw new ArgumentException("Every goal must belong to the snapshot session.", nameof(goals));
            if (!goalIds.Add(goal.Id))
                throw new ArgumentException("Goal IDs must be unique within a snapshot.", nameof(goals));
            copiedGoals[index] = goal;
        }

        var resolvedAttention = attention ?? AttentionState.None;
        if (resolvedAttention.FocusedGoalId != null
            && !goalIds.Contains(resolvedAttention.FocusedGoalId))
        {
            throw new ArgumentException(
                "Focused attention must reference a goal in the snapshot.",
                nameof(attention));
        }
        if (resolvedAttention.FocusedSince > normalizedAsOf)
            throw new ArgumentException("Attention cannot begin after the snapshot.", nameof(attention));

        var sourceTerminalStates =
            evidenceTerminalStates ?? Array.Empty<EvidenceTerminalState>();
        if (sourceTerminalStates.Count > MaximumEvidenceTerminalStates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidenceTerminalStates));
        }
        var copiedTerminalStates =
            new EvidenceTerminalState[sourceTerminalStates.Count];
        var terminalCandidateIds = new HashSet<string>(StringComparer.Ordinal);
        var terminalPairIds = new HashSet<string>(StringComparer.Ordinal);
        var terminalInquiryIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sourceTerminalStates.Count; index++)
        {
            var terminal = sourceTerminalStates[index]
                ?? throw new ArgumentException(
                    "Evidence terminal collection cannot contain null.",
                    nameof(evidenceTerminalStates));
            if (terminal.TerminalSituationRevision > sequence)
            {
                throw new ArgumentException(
                    "Evidence terminal revision cannot follow the snapshot.",
                    nameof(evidenceTerminalStates));
            }
            if (!terminalCandidateIds.Add(terminal.CandidateId)
                || !terminalPairIds.Add(terminal.PairId)
                || !terminalInquiryIds.Add(terminal.InquiryId))
            {
                throw new ArgumentException(
                    "Evidence terminal identities must be unique.",
                    nameof(evidenceTerminalStates));
            }
            copiedTerminalStates[index] = terminal;
        }

        Id = id;
        Sequence = sequence;
        SessionId = normalizedSessionId;
        AsOf = normalizedAsOf;
        ValidUntil = normalizedValidUntil;
        Trigger = trigger;
        ConflictLevel = conflictLevel;
        IsAvatarUnderDirectPlayerControl = isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        IsInitiativeCooldownActive = isInitiativeCooldownActive;
        AvailableEvidence = availableEvidence;
        Budgets = budgets ?? DecisionBudgets.ShadowDefault;
        Attention = resolvedAttention;
        Goals = Array.AsReadOnly(copiedGoals);
        EvidenceTerminalStates = Array.AsReadOnly(copiedTerminalStates);
    }

    internal EvidenceTerminalState? FindEvidenceTerminalForGoal(
        GoalState goal)
    {
        if (goal == null)
            throw new ArgumentNullException(nameof(goal));
        if (goal.EvidenceCandidateId == null)
            return null;
        foreach (var terminal in EvidenceTerminalStates)
        {
            if (string.Equals(
                    terminal.GoalId,
                    goal.Id,
                    StringComparison.Ordinal)
                && string.Equals(
                    terminal.CandidateId,
                    goal.EvidenceCandidateId,
                    StringComparison.Ordinal))
            {
                return terminal;
            }
        }
        return null;
    }

    internal EvidenceTerminalState? FindEvidenceTerminalForCandidate(
        string candidateId)
    {
        var normalizedCandidateId = DecisionCoreContractGuard.Identifier(
            candidateId,
            128,
            nameof(candidateId));
        foreach (var terminal in EvidenceTerminalStates)
        {
            if (string.Equals(
                    terminal.CandidateId,
                    normalizedCandidateId,
                    StringComparison.Ordinal))
            {
                return terminal;
            }
        }
        return null;
    }

    internal SituationSnapshot WithEvidenceTerminal(
        EvidenceTerminalState terminalState)
    {
        if (terminalState == null)
            throw new ArgumentNullException(nameof(terminalState));
        var existing = FindEvidenceTerminalForCandidate(
            terminalState.CandidateId);
        if (existing != null)
        {
            if (existing.HasSameIdentity(terminalState))
                return this;
            throw new InvalidOperationException(
                "Candidate already has a different Evidence terminal state.");
        }

        var terminals =
            new EvidenceTerminalState[EvidenceTerminalStates.Count + 1];
        for (var index = 0; index < EvidenceTerminalStates.Count; index++)
            terminals[index] = EvidenceTerminalStates[index];
        terminals[terminals.Length - 1] = terminalState;
        return new SituationSnapshot(
            Id,
            Sequence,
            SessionId,
            AsOf,
            ValidUntil,
            Trigger,
            ConflictLevel,
            IsAvatarUnderDirectPlayerControl,
            IsConversationBusy,
            HasPendingExecution,
            IsQuietPeriod,
            IsInitiativeCooldownActive,
            AvailableEvidence,
            Budgets,
            Attention,
            Goals,
            terminals);
    }
}

internal sealed class InvocationPlan
{
    internal DecisionEvidence RequiredEvidence { get; }
    internal int MaximumMemoryItems { get; }
    internal int MaximumContextCharacters { get; }
    internal int MaximumReasoningCalls { get; }

    internal InvocationPlan(
        DecisionEvidence requiredEvidence,
        int maximumMemoryItems,
        int maximumContextCharacters,
        int maximumReasoningCalls)
    {
        if (requiredEvidence == DecisionEvidence.None
            || (requiredEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredEvidence));
        }
        if (maximumMemoryItems < 0 || maximumMemoryItems > 64)
            throw new ArgumentOutOfRangeException(nameof(maximumMemoryItems));
        if (maximumContextCharacters < 0 || maximumContextCharacters > 32_000)
            throw new ArgumentOutOfRangeException(nameof(maximumContextCharacters));
        if (maximumReasoningCalls < 0 || maximumReasoningCalls > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumReasoningCalls));
        if (maximumMemoryItems == 0
            && maximumContextCharacters == 0
            && maximumReasoningCalls == 0)
        {
            throw new ArgumentException("Invocation plan must contain a non-zero bound.");
        }

        RequiredEvidence = requiredEvidence;
        MaximumMemoryItems = maximumMemoryItems;
        MaximumContextCharacters = maximumContextCharacters;
        MaximumReasoningCalls = maximumReasoningCalls;
    }
}

internal sealed class DecisionOutcome
{
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal string? SelectedGoalId { get; }
    internal InvocationPlan? Invocation { get; }

    internal DecisionOutcome(
        DecisionDisposition disposition,
        DecisionReason reason,
        string? selectedGoalId,
        InvocationPlan? invocation)
    {
        if (!Enum.IsDefined(typeof(DecisionDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(DecisionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (selectedGoalId != null)
        {
            SelectedGoalId = DecisionCoreContractGuard.Identifier(
                selectedGoalId,
                128,
                nameof(selectedGoalId));
        }
        if ((disposition is DecisionDisposition.Respond
                or DecisionDisposition.Act
                or DecisionDisposition.AskConsent)
            && SelectedGoalId == null)
        {
            throw new ArgumentException(
                "Behavior proposals must reference the selected goal.",
                nameof(selectedGoalId));
        }
        if (invocation != null && disposition != DecisionDisposition.Defer)
            throw new ArgumentException("Evidence invocation must defer the current decision.", nameof(invocation));
        if (invocation != null && reason != DecisionReason.EvidenceInvocationPlanned)
            throw new ArgumentException("Invocation requires the matching reason code.", nameof(invocation));
        if (reason == DecisionReason.EvidenceInvocationPlanned && invocation == null)
            throw new ArgumentNullException(nameof(invocation));

        Disposition = disposition;
        Reason = reason;
        Invocation = invocation;
    }
}

internal sealed class DecisionTrace
{
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SnapshotSequence { get; }
    internal string CoreVersion { get; }
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal string? SelectedGoalId { get; }
    internal IReadOnlyList<string> ConsideredGoalIds { get; }

    internal DecisionTrace(
        Guid decisionId,
        SituationSnapshot snapshot,
        DecisionOutcome outcome,
        IReadOnlyList<string> consideredGoalIds,
        string coreVersion)
    {
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.", nameof(decisionId));
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));

        DecisionId = decisionId;
        SnapshotId = snapshot.Id;
        SnapshotSequence = snapshot.Sequence;
        CoreVersion = DecisionCoreContractGuard.Identifier(coreVersion, 64, nameof(coreVersion));
        Disposition = outcome.Disposition;
        Reason = outcome.Reason;
        SelectedGoalId = outcome.SelectedGoalId;
        ConsideredGoalIds = DecisionCoreContractGuard.IdentifierList(
            consideredGoalIds,
            SituationSnapshot.MaximumGoals,
            nameof(consideredGoalIds));
    }
}

internal sealed class DecisionEvaluation
{
    internal DecisionOutcome Outcome { get; }
    internal DecisionTrace Trace { get; }

    internal DecisionEvaluation(DecisionOutcome outcome, DecisionTrace trace)
    {
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }
}

internal sealed class OutcomeObservation
{
    internal Guid DecisionId { get; }
    internal OutcomeStatus Status { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal bool HasDirectExecutionCorrelation { get; }
    internal bool HasConflictingIntervention { get; }

    internal OutcomeObservation(
        Guid decisionId,
        OutcomeStatus status,
        DateTimeOffset observedAt,
        bool hasDirectExecutionCorrelation,
        bool hasConflictingIntervention)
    {
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.", nameof(decisionId));
        if (!Enum.IsDefined(typeof(OutcomeStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));

        DecisionId = decisionId;
        Status = status;
        ObservedAt = DecisionCoreContractGuard.Timestamp(observedAt, nameof(observedAt));
        HasDirectExecutionCorrelation = hasDirectExecutionCorrelation;
        HasConflictingIntervention = hasConflictingIntervention;
    }
}

internal sealed class AdaptationProposal
{
    internal AdaptationProposalKind Kind { get; }
    internal CausalConfidence Confidence { get; }
    internal bool MayApplyAutomatically => false;

    internal AdaptationProposal(
        AdaptationProposalKind kind,
        CausalConfidence confidence)
    {
        if (!Enum.IsDefined(typeof(AdaptationProposalKind), kind)
            || kind == AdaptationProposalKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!Enum.IsDefined(typeof(CausalConfidence), confidence)
            || confidence == CausalConfidence.None)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        Kind = kind;
        Confidence = confidence;
    }
}

internal sealed class OutcomeAppraisal
{
    internal Guid DecisionId { get; }
    internal OutcomeStatus Status { get; }
    internal CausalConfidence CausalConfidence { get; }
    internal AdaptationProposal? Proposal { get; }

    internal OutcomeAppraisal(
        Guid decisionId,
        OutcomeStatus status,
        CausalConfidence causalConfidence,
        AdaptationProposal? proposal)
    {
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.", nameof(decisionId));
        if (!Enum.IsDefined(typeof(OutcomeStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(typeof(CausalConfidence), causalConfidence))
            throw new ArgumentOutOfRangeException(nameof(causalConfidence));
        if (proposal != null && causalConfidence < CausalConfidence.Medium)
            throw new ArgumentException(
                "Low-confidence appraisal cannot propose adaptation.",
                nameof(proposal));

        DecisionId = decisionId;
        Status = status;
        CausalConfidence = causalConfidence;
        Proposal = proposal;
    }
}

internal static class DecisionCoreContractGuard
{
    internal const DecisionEvidence AllEvidence =
        DecisionEvidence.Memory
        | DecisionEvidence.Reasoning
        | DecisionEvidence.CharacterState
        | DecisionEvidence.RelationshipState
        | DecisionEvidence.CapabilityState;

    internal static GoalPriorityClass PriorityClassFor(GoalSource source)
        => source switch
        {
            GoalSource.Safety => GoalPriorityClass.Safety,
            GoalSource.ExplicitPlayerRequest =>
                GoalPriorityClass.ExplicitPlayerRequest,
            GoalSource.ExplicitCommitment =>
                GoalPriorityClass.ExplicitCommitment,
            GoalSource.ConversationContinuity =>
                GoalPriorityClass.ConversationContinuity,
            GoalSource.CharacterContinuity =>
                GoalPriorityClass.CharacterContinuity,
            GoalSource.Initiative => GoalPriorityClass.Initiative,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    internal static string Identifier(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        foreach (var character in normalized)
        {
            if (char.IsControl(character))
                throw new ArgumentException("Identifier cannot contain control characters.", parameterName);
        }
        return normalized;
    }

    internal static DateTimeOffset Timestamp(DateTimeOffset value, string parameterName)
    {
        if (value == default || value.Year < 2000 || value.Year > 9998)
            throw new ArgumentOutOfRangeException(parameterName);
        return value.ToUniversalTime();
    }

    internal static DateTimeOffset? OptionalTimestamp(
        DateTimeOffset? value,
        string parameterName)
        => value.HasValue ? Timestamp(value.Value, parameterName) : null;

    internal static IReadOnlyList<string> IdentifierList(
        IReadOnlyList<string>? source,
        int maximumCount,
        string parameterName)
    {
        if (source == null || source.Count == 0)
            return Array.Empty<string>();
        if (source.Count > maximumCount)
            throw new ArgumentOutOfRangeException(parameterName);

        var copy = new string[source.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < source.Count; index++)
        {
            var identifier = Identifier(source[index], 128, parameterName);
            if (!unique.Add(identifier))
                throw new ArgumentException("Identifier list cannot contain duplicates.", parameterName);
            copy[index] = identifier;
        }
        return Array.AsReadOnly(copy);
    }
}
