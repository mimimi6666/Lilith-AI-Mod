using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum InitiativeMasterState
{
    Off = 0,
    Paused = 1,
    On = 2
}

internal enum InitiativePromotionMode
{
    Shadow = 0,
    Observe = 1,
    Assist = 2
}

internal enum InitiativeSensitivity
{
    LocalGameMetadata = 0,
    PersonalMetadata = 1,
    OperatingSystemContext = 2,
    VisualContext = 3,
    AudioContext = 4
}

internal enum InitiativeCandidateKind
{
    MotionOpportunity = 0,
    AiNoteDue = 1,
    OriginalScheduledEvent = 2,
    ConversationOpportunity = 3
}

internal enum InitiativeReasonCode
{
    MotionDecisionOpportunity = 0,
    ScheduledNoteDue = 1,
    OriginalScheduledEventDue = 2,
    LocalInputResumedAfterIdle = 3
}

internal enum InitiativeCapabilityClass
{
    None = 0,
    Response = 1,
    Motion = 2,
    Capability = 3
}

internal enum InitiativeCandidateWithdrawalReason
{
    SourceCancelled = 0,
    SourceCompleted = 1
}

internal enum InitiativeCandidateRejectionReason
{
    None = 0,
    MasterOff = 1,
    Paused = 2,
    PromotionModeUnauthorized = 3,
    AuthorityBudgetUnauthorized = 4,
    DecisionBudgetUnauthorized = 5,
    SourceDisabled = 6,
    SensitivityUnauthorized = 7,
    CandidateExpired = 8,
    DuplicateCandidate = 9,
    CandidateCapacityReached = 10,
    CandidateHistoryCapacityReached = 11,
    SessionRetired = 12,
    CandidateNotActive = 13,
    CandidateSourceMismatch = 14
}

internal sealed class InitiativeAuthorityBudget
{
    internal static InitiativeAuthorityBudget I1Zero { get; } = new(
        maximumEvidenceInvocations: 0,
        maximumBehaviorProposals: 0);

    internal int MaximumEvidenceInvocations { get; }
    internal int MaximumBehaviorProposals { get; }
    internal bool IsZero =>
        MaximumEvidenceInvocations == 0
        && MaximumBehaviorProposals == 0;

    internal InitiativeAuthorityBudget(
        int maximumEvidenceInvocations,
        int maximumBehaviorProposals)
    {
        if (maximumEvidenceInvocations < 0
            || maximumEvidenceInvocations > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEvidenceInvocations));
        }
        if (maximumBehaviorProposals < 0
            || maximumBehaviorProposals > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBehaviorProposals));
        }

        MaximumEvidenceInvocations = maximumEvidenceInvocations;
        MaximumBehaviorProposals = maximumBehaviorProposals;
    }
}

internal sealed class InitiativeControlState
{
    internal const int MaximumEnabledSources = 16;

    internal InitiativeMasterState MasterState { get; }
    internal InitiativePromotionMode PromotionMode { get; }
    internal InitiativeSensitivity MaximumSensitivity { get; }
    internal bool IsQuietPeriod { get; }
    internal bool IsCooldownActive { get; }
    internal InitiativeAuthorityBudget AuthorityBudget { get; }
    internal IReadOnlyList<string> EnabledSourceIds { get; }

    internal InitiativeControlState(
        InitiativeMasterState masterState,
        InitiativePromotionMode promotionMode,
        InitiativeSensitivity maximumSensitivity,
        bool isQuietPeriod,
        bool isCooldownActive,
        InitiativeAuthorityBudget? authorityBudget,
        IReadOnlyList<string>? enabledSourceIds)
    {
        if (!Enum.IsDefined(typeof(InitiativeMasterState), masterState))
            throw new ArgumentOutOfRangeException(nameof(masterState));
        if (!Enum.IsDefined(typeof(InitiativePromotionMode), promotionMode))
            throw new ArgumentOutOfRangeException(nameof(promotionMode));
        if (!Enum.IsDefined(typeof(InitiativeSensitivity), maximumSensitivity))
            throw new ArgumentOutOfRangeException(nameof(maximumSensitivity));

        var source = enabledSourceIds ?? Array.Empty<string>();
        if (source.Count > MaximumEnabledSources)
            throw new ArgumentOutOfRangeException(nameof(enabledSourceIds));
        var copy = new string[source.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < source.Count; index++)
        {
            var id = DecisionCoreContractGuard.Identifier(
                source[index],
                128,
                nameof(enabledSourceIds));
            if (!seen.Add(id))
            {
                throw new ArgumentException(
                    "Enabled source IDs must be unique.",
                    nameof(enabledSourceIds));
            }
            copy[index] = id;
        }
        Array.Sort(copy, StringComparer.Ordinal);

        MasterState = masterState;
        PromotionMode = promotionMode;
        MaximumSensitivity = maximumSensitivity;
        IsQuietPeriod = isQuietPeriod;
        IsCooldownActive = isCooldownActive;
        AuthorityBudget = authorityBudget
            ?? InitiativeAuthorityBudget.I1Zero;
        EnabledSourceIds = Array.AsReadOnly(copy);
    }

    internal bool AllowsSource(string sourceId)
    {
        var normalized = DecisionCoreContractGuard.Identifier(
            sourceId,
            128,
            nameof(sourceId));
        for (var index = 0; index < EnabledSourceIds.Count; index++)
        {
            if (string.Equals(
                    EnabledSourceIds[index],
                    normalized,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

internal sealed class InitiativeCandidate
{
    internal static readonly TimeSpan MaximumLifetime =
        TimeSpan.FromDays(7);

    internal string Id { get; }
    internal string SessionId { get; }
    internal string CorrelationId { get; }
    internal string SourceId { get; }
    internal string SupersessionKey { get; }
    internal InitiativeCandidateKind Kind { get; }
    internal InitiativeReasonCode Reason { get; }
    internal InitiativeSensitivity Sensitivity { get; }
    internal InitiativeCapabilityClass CapabilityClass { get; }
    internal SituationTriggerKind TriggerKind { get; }
    internal DecisionDisposition DesiredDisposition { get; }
    internal DecisionEvidence RequiredEvidence { get; }
    internal double Urgency { get; }
    internal double AttentionCost { get; }
    internal double InterruptionCost { get; }
    internal bool RequiresConsent { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal InitiativeCandidate(
        string id,
        string sessionId,
        string correlationId,
        string sourceId,
        string supersessionKey,
        InitiativeCandidateKind kind,
        InitiativeReasonCode reason,
        InitiativeSensitivity sensitivity,
        InitiativeCapabilityClass capabilityClass,
        SituationTriggerKind triggerKind,
        DecisionDisposition desiredDisposition,
        DecisionEvidence requiredEvidence,
        double urgency,
        double attentionCost,
        double interruptionCost,
        bool requiresConsent,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        SessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        CorrelationId = DecisionCoreContractGuard.Identifier(
            correlationId,
            128,
            nameof(correlationId));
        SourceId = DecisionCoreContractGuard.Identifier(
            sourceId,
            128,
            nameof(sourceId));
        SupersessionKey = DecisionCoreContractGuard.Identifier(
            supersessionKey,
            128,
            nameof(supersessionKey));
        if (!Enum.IsDefined(typeof(InitiativeCandidateKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(InitiativeReasonCode), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (!Enum.IsDefined(typeof(InitiativeSensitivity), sensitivity))
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (!Enum.IsDefined(
                typeof(InitiativeCapabilityClass),
                capabilityClass)
            || capabilityClass == InitiativeCapabilityClass.None)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityClass));
        }
        if (!Enum.IsDefined(typeof(SituationTriggerKind), triggerKind)
            || triggerKind is SituationTriggerKind.None
                or SituationTriggerKind.UserMessage)
        {
            throw new ArgumentOutOfRangeException(nameof(triggerKind));
        }
        if (desiredDisposition is not DecisionDisposition.Respond
            and not DecisionDisposition.Act
            and not DecisionDisposition.AskConsent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredDisposition));
        }
        if ((requiredEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(requiredEvidence));
        ValidateNormalizedScore(urgency, nameof(urgency));
        ValidateNormalizedScore(attentionCost, nameof(attentionCost));
        ValidateNormalizedScore(interruptionCost, nameof(interruptionCost));
        ValidateCapabilityClass(desiredDisposition, capabilityClass);

        var normalizedOccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        var normalizedObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        var normalizedExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (normalizedObservedAt < normalizedOccurredAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                "Candidate cannot be observed before it occurs.");
        }
        if (normalizedExpiresAt <= normalizedObservedAt
            || normalizedExpiresAt - normalizedOccurredAt > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        Kind = kind;
        Reason = reason;
        Sensitivity = sensitivity;
        CapabilityClass = capabilityClass;
        TriggerKind = triggerKind;
        DesiredDisposition = desiredDisposition;
        RequiredEvidence = requiredEvidence;
        Urgency = urgency;
        AttentionCost = attentionCost;
        InterruptionCost = interruptionCost;
        RequiresConsent = requiresConsent;
        OccurredAt = normalizedOccurredAt;
        ObservedAt = normalizedObservedAt;
        ExpiresAt = normalizedExpiresAt;
    }

    private static void ValidateNormalizedScore(
        double value,
        string parameterName)
    {
        if (double.IsNaN(value) || value < 0d || value > 1d)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateCapabilityClass(
        DecisionDisposition disposition,
        InitiativeCapabilityClass capabilityClass)
    {
        if (disposition == DecisionDisposition.Respond
            && capabilityClass != InitiativeCapabilityClass.Response)
        {
            throw new ArgumentException(
                "Respond candidates require the response capability class.",
                nameof(capabilityClass));
        }
        if (disposition == DecisionDisposition.Act
            && capabilityClass is not InitiativeCapabilityClass.Motion
                and not InitiativeCapabilityClass.Capability)
        {
            throw new ArgumentException(
                "Act candidates require a motion or capability class.",
                nameof(capabilityClass));
        }
    }
}

internal sealed class InitiativeEvaluationRequest
{
    internal Guid SnapshotId { get; }
    internal long SnapshotSequence { get; }
    internal string TriggerId { get; }
    internal SituationTriggerKind TriggerKind { get; }
    internal DateTimeOffset AsOf { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal TimeSpan SnapshotValidity { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal DecisionEvidence AvailableEvidence { get; }
    internal DecisionBudgets DecisionBudgets { get; }
    internal string? FocusedCandidateId { get; }

    internal InitiativeEvaluationRequest(
        Guid snapshotId,
        long snapshotSequence,
        string triggerId,
        SituationTriggerKind triggerKind,
        DateTimeOffset asOf,
        DateTimeOffset evaluatedAt,
        TimeSpan snapshotValidity,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        DecisionEvidence availableEvidence,
        DecisionBudgets? decisionBudgets = null,
        string? focusedCandidateId = null)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException(
                "Snapshot ID cannot be empty.",
                nameof(snapshotId));
        if (snapshotSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotSequence));
        TriggerId = DecisionCoreContractGuard.Identifier(
            triggerId,
            128,
            nameof(triggerId));
        if (!Enum.IsDefined(typeof(SituationTriggerKind), triggerKind)
            || triggerKind is SituationTriggerKind.None
                or SituationTriggerKind.UserMessage)
        {
            throw new ArgumentOutOfRangeException(nameof(triggerKind));
        }
        var normalizedAsOf = DecisionCoreContractGuard.Timestamp(
            asOf,
            nameof(asOf));
        var normalizedEvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (normalizedEvaluatedAt < normalizedAsOf)
            throw new ArgumentOutOfRangeException(nameof(evaluatedAt));
        if (snapshotValidity <= TimeSpan.Zero
            || snapshotValidity > SituationSnapshotAssembler.MaximumValidity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshotValidity));
        }
        if ((availableEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(availableEvidence));

        SnapshotId = snapshotId;
        SnapshotSequence = snapshotSequence;
        TriggerKind = triggerKind;
        AsOf = normalizedAsOf;
        EvaluatedAt = normalizedEvaluatedAt;
        SnapshotValidity = snapshotValidity;
        IsAvatarUnderDirectPlayerControl =
            isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        AvailableEvidence = availableEvidence;
        DecisionBudgets = decisionBudgets
            ?? DecisionBudgets.ShadowDefault;
        if (focusedCandidateId != null)
        {
            FocusedCandidateId = DecisionCoreContractGuard.Identifier(
                focusedCandidateId,
                128,
                nameof(focusedCandidateId));
        }
    }
}

internal sealed class InitiativeCandidateWithdrawal
{
    internal string CandidateId { get; }
    internal string SessionId { get; }
    internal string SourceId { get; }
    internal string CorrelationId { get; }
    internal InitiativeCandidateWithdrawalReason Reason { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }

    internal InitiativeCandidateWithdrawal(
        string candidateId,
        string sessionId,
        string sourceId,
        string correlationId,
        InitiativeCandidateWithdrawalReason reason,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt)
    {
        CandidateId = DecisionCoreContractGuard.Identifier(
            candidateId,
            128,
            nameof(candidateId));
        SessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        SourceId = DecisionCoreContractGuard.Identifier(
            sourceId,
            128,
            nameof(sourceId));
        CorrelationId = DecisionCoreContractGuard.Identifier(
            correlationId,
            128,
            nameof(correlationId));
        if (!Enum.IsDefined(
                typeof(InitiativeCandidateWithdrawalReason),
                reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (ObservedAt < OccurredAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        Reason = reason;
    }
}

internal sealed class InitiativeSideEffectCounters
{
    internal static InitiativeSideEffectCounters Zero { get; } = new(
        providerCalls: 0,
        memoryCalls: 0,
        stateWrites: 0,
        queueWrites: 0,
        executions: 0);

    internal int ProviderCalls { get; }
    internal int MemoryCalls { get; }
    internal int StateWrites { get; }
    internal int QueueWrites { get; }
    internal int Executions { get; }
    internal bool IsZero =>
        ProviderCalls == 0
        && MemoryCalls == 0
        && StateWrites == 0
        && QueueWrites == 0
        && Executions == 0;

    internal InitiativeSideEffectCounters(
        int providerCalls,
        int memoryCalls,
        int stateWrites,
        int queueWrites,
        int executions)
    {
        if (providerCalls < 0)
            throw new ArgumentOutOfRangeException(nameof(providerCalls));
        if (memoryCalls < 0)
            throw new ArgumentOutOfRangeException(nameof(memoryCalls));
        if (stateWrites < 0)
            throw new ArgumentOutOfRangeException(nameof(stateWrites));
        if (queueWrites < 0)
            throw new ArgumentOutOfRangeException(nameof(queueWrites));
        if (executions < 0)
            throw new ArgumentOutOfRangeException(nameof(executions));

        ProviderCalls = providerCalls;
        MemoryCalls = memoryCalls;
        StateWrites = stateWrites;
        QueueWrites = queueWrites;
        Executions = executions;
    }
}

internal static class InitiativeI1Policy
{
    internal static InitiativeCandidateRejectionReason
        CheckControl(InitiativeControlState control)
    {
        if (control == null)
            throw new ArgumentNullException(nameof(control));
        if (control.MasterState == InitiativeMasterState.Off)
            return InitiativeCandidateRejectionReason.MasterOff;
        if (control.MasterState == InitiativeMasterState.Paused)
            return InitiativeCandidateRejectionReason.Paused;
        if (control.PromotionMode != InitiativePromotionMode.Shadow)
        {
            return InitiativeCandidateRejectionReason
                .PromotionModeUnauthorized;
        }
        if (!control.AuthorityBudget.IsZero)
        {
            return InitiativeCandidateRejectionReason
                .AuthorityBudgetUnauthorized;
        }
        return InitiativeCandidateRejectionReason.None;
    }

    internal static InitiativeCandidateRejectionReason CheckCandidate(
        InitiativeControlState control,
        InitiativeCandidate candidate,
        DateTimeOffset observedAt)
    {
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));
        var controlRejection = CheckControl(control);
        if (controlRejection != InitiativeCandidateRejectionReason.None)
            return controlRejection;
        if (!control.AllowsSource(candidate.SourceId))
            return InitiativeCandidateRejectionReason.SourceDisabled;
        if (candidate.Sensitivity > control.MaximumSensitivity
            || candidate.Sensitivity
                != InitiativeSensitivity.LocalGameMetadata)
        {
            return InitiativeCandidateRejectionReason
                .SensitivityUnauthorized;
        }
        var normalizedObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        return normalizedObservedAt >= candidate.ExpiresAt
            ? InitiativeCandidateRejectionReason.CandidateExpired
            : InitiativeCandidateRejectionReason.None;
    }

    internal static InitiativeCandidateRejectionReason
        CheckEvaluation(
            InitiativeControlState control,
            InitiativeEvaluationRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        var controlRejection = CheckControl(control);
        if (controlRejection != InitiativeCandidateRejectionReason.None)
            return controlRejection;
        return request.DecisionBudgets.MaximumMemoryItems != 0
               || request.DecisionBudgets.MaximumContextCharacters != 0
               || request.DecisionBudgets.MaximumReasoningCalls != 0
               || request.DecisionBudgets.MaximumAttentionCost.HasValue
            ? InitiativeCandidateRejectionReason
                .DecisionBudgetUnauthorized
            : InitiativeCandidateRejectionReason.None;
    }
}

internal static class InitiativeCandidateProjector
{
    internal const int MaximumCandidates = 64;

    internal static DecisionEvaluation Evaluate(
        string sessionId,
        InitiativeControlState control,
        IReadOnlyList<InitiativeCandidate> candidates,
        InitiativeEvaluationRequest request)
    {
        var normalizedSessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        if (control == null)
            throw new ArgumentNullException(nameof(control));
        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (candidates.Count > MaximumCandidates)
            throw new ArgumentOutOfRangeException(nameof(candidates));

        var goals = new GoalState[candidates.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index]
                ?? throw new ArgumentException(
                    "Candidate collection cannot contain null.",
                    nameof(candidates));
            if (!string.Equals(
                    candidate.SessionId,
                    normalizedSessionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every candidate must belong to the evaluation session.",
                    nameof(candidates));
            }
            if (!ids.Add(candidate.Id))
            {
                throw new ArgumentException(
                    "Candidate IDs must be unique.",
                    nameof(candidates));
            }
            if (request.AsOf >= candidate.ExpiresAt)
            {
                throw new ArgumentException(
                    "Expired candidates cannot enter a snapshot.",
                    nameof(candidates));
            }
            goals[index] = ToGoal(candidate);
        }

        AttentionState attention = AttentionState.None;
        if (request.FocusedCandidateId != null)
        {
            if (!ids.Contains(request.FocusedCandidateId))
            {
                throw new ArgumentException(
                    "Focused candidate must be part of the snapshot.",
                    nameof(request));
            }
            attention = new AttentionState(
                AttentionFocusKind.Initiative,
                request.FocusedCandidateId,
                request.AsOf);
        }

        var snapshot = new SituationSnapshot(
            request.SnapshotId,
            request.SnapshotSequence,
            normalizedSessionId,
            request.AsOf,
            request.AsOf.Add(request.SnapshotValidity),
            new SituationTrigger(
                request.TriggerId,
                request.TriggerKind,
                request.AsOf),
            SituationConflictLevel.None,
            request.IsAvatarUnderDirectPlayerControl,
            request.IsConversationBusy,
            request.HasPendingExecution,
            control.IsQuietPeriod,
            control.IsCooldownActive,
            request.AvailableEvidence,
            request.DecisionBudgets,
            attention,
            goals);
        return LocalDecisionCore.Evaluate(
            snapshot,
            request.EvaluatedAt);
    }

    internal static GoalState ToGoal(InitiativeCandidate candidate)
        => new(
            candidate.Id,
            candidate.SessionId,
            GoalSource.Initiative,
            GoalPriorityClass.Initiative,
            GoalStatus.Candidate,
            GoalPersistenceScope.Session,
            candidate.DesiredDisposition,
            candidate.RequiredEvidence,
            candidate.Urgency,
            candidate.InterruptionCost,
            candidate.RequiresConsent,
            candidate.ObservedAt,
            deadline: null,
            candidate.ExpiresAt,
            evidenceCandidateId: candidate.Id,
            attentionCost: candidate.AttentionCost,
            requiresAttentionBudget:
                candidate.Kind
                    == InitiativeCandidateKind.ConversationOpportunity);
}
