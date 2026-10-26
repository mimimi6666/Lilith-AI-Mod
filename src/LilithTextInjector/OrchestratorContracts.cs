using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal static class OrchestratorProtocol
{
    internal const string CurrentVersion = "lilith/1";
}

internal enum OrchestratorEventKind
{
    SessionOpened = 0,
    SituationChanged = 1,
    SessionCancellationRequested = 2,
    InvocationResultReceived = 3,
    OutcomeExpectationRegistrationRequested = 4,
    OutcomeSignalReceived = 5,
    SessionCloseRequested = 6,
    SessionRetirementRequested = 7
}

internal enum OrchestratorSourceKind
{
    LocalGameSurface = 0,
    LocalDesktopPetSurface = 1,
    LocalVoiceSurface = 2,
    PairedRemoteSurface = 3,
    InternalStateService = 4,
    TestHarness = 5
}

internal enum OrchestratorSessionStatus
{
    Active = 0,
    Cancelled = 1,
    Closed = 2
}

internal enum OrchestratorCancellationReason
{
    PlayerRequested = 0,
    SurfaceDisconnected = 1,
    SessionSuperseded = 2,
    TimedOut = 3,
    SafetyStop = 4,
    Shutdown = 5
}

internal enum OrchestratorSessionCloseReason
{
    Completed = 0,
    HostStopped = 1,
    CapacityRotation = 2
}

internal enum OrchestratorProcessDisposition
{
    Accepted = 0,
    Rejected = 1
}

internal enum OrchestratorProcessReason
{
    SessionOpened = 0,
    SituationEvaluated = 1,
    SessionCancelled = 2,
    UnsupportedProtocol = 3,
    SessionLimitReached = 4,
    SessionAlreadyExists = 5,
    SessionMissing = 6,
    SessionNotActive = 7,
    OwnerMismatch = 8,
    SequenceMismatch = 9,
    TimestampReversal = 10,
    DuplicateEventIdentity = 11,
    EventLimitReached = 12,
    MissingEvaluationStamp = 13,
    UnexpectedEvaluationStamp = 14,
    SnapshotIdentityDuplicate = 15,
    SituationEnvelopeMismatch = 16,
    SituationRejected = 17,
    InvocationLimitReached = 18,
    InvocationMissing = 19,
    InvocationNotPending = 20,
    InvocationResultMismatch = 21,
    InvocationResultLate = 22,
    InvocationResultAccepted = 23,
    OutcomeDecisionMissing = 24,
    OutcomeRegistrationRejected = 25,
    OutcomeExpectationLimitReached = 26,
    OutcomeExpectationRegistered = 27,
    OutcomeSignalRejected = 28,
    OutcomeProgressAccepted = 29,
    OutcomeTerminalAccepted = 30,
    SessionClosed = 31,
    SessionClosePendingInvocation = 32,
    SessionClosePendingOutcome = 33,
    SessionRetired = 34,
    SessionRetirementRequiresTerminal = 35,
    SessionRetirementPendingWork = 36,
    RetiredSessionLimitReached = 37,
    SessionAlreadyRetired = 38
}

internal enum OrchestratorInvocationStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
    TimedOut = 4
}

internal enum OrchestratorInvocationSourceKind
{
    MemoryService = 0,
    ReasoningAdapter = 1
}

internal sealed class OrchestratorSessionOwner
{
    internal string ActorId { get; }
    internal string DeviceId { get; }

    internal OrchestratorSessionOwner(string actorId, string deviceId)
    {
        ActorId = DecisionCoreContractGuard.Identifier(
            actorId,
            128,
            nameof(actorId));
        DeviceId = DecisionCoreContractGuard.Identifier(
            deviceId,
            128,
            nameof(deviceId));
    }

    internal bool Matches(OrchestratorSessionOwner other)
        => other != null
            && string.Equals(
                ActorId,
                other.ActorId,
                StringComparison.Ordinal)
            && string.Equals(
                DeviceId,
                other.DeviceId,
                StringComparison.Ordinal);
}

internal abstract class OrchestratorEventEnvelope
{
    internal string ProtocolVersion { get; }
    internal string Id { get; }
    internal string CorrelationId { get; }
    internal long Sequence { get; }
    internal string SessionId { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal OrchestratorEventKind Kind { get; }
    internal OrchestratorSourceKind Source { get; }
    internal OrchestratorSessionOwner Owner { get; }

    protected OrchestratorEventEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorEventKind kind,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner)
    {
        ProtocolVersion = DecisionCoreContractGuard.Identifier(
            protocolVersion,
            32,
            nameof(protocolVersion));
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        CorrelationId = DecisionCoreContractGuard.Identifier(
            correlationId,
            128,
            nameof(correlationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        SessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (!Enum.IsDefined(typeof(OrchestratorEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(OrchestratorSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));

        Sequence = sequence;
        Kind = kind;
        Source = source;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }
}

internal sealed class OrchestratorSessionOpenedEnvelope
    : OrchestratorEventEnvelope
{
    internal OrchestratorSessionOpenedEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence: 1,
            sessionId,
            occurredAt,
            OrchestratorEventKind.SessionOpened,
            source,
            owner)
    {
    }
}

internal sealed class OrchestratorSituationEnvelope
    : OrchestratorEventEnvelope
{
    internal SituationEvent Event { get; }

    internal OrchestratorSituationEnvelope(
        string protocolVersion,
        string correlationId,
        long sequence,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        SituationEvent situationEvent)
        : base(
            protocolVersion,
            (situationEvent
                ?? throw new ArgumentNullException(nameof(situationEvent))).Id,
            correlationId,
            sequence,
            situationEvent.SessionId,
            situationEvent.OccurredAt,
            OrchestratorEventKind.SituationChanged,
            source,
            owner)
    {
        Event = situationEvent;
    }
}

internal sealed class OrchestratorCancellationEnvelope
    : OrchestratorEventEnvelope
{
    internal OrchestratorCancellationReason Reason { get; }

    internal OrchestratorCancellationEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        OrchestratorCancellationReason reason)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.SessionCancellationRequested,
            source,
            owner)
    {
        if (!Enum.IsDefined(typeof(OrchestratorCancellationReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
    }
}

internal sealed class OrchestratorSessionCloseEnvelope
    : OrchestratorEventEnvelope
{
    internal OrchestratorSessionCloseReason Reason { get; }

    internal OrchestratorSessionCloseEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        OrchestratorSessionCloseReason reason)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.SessionCloseRequested,
            source,
            owner)
    {
        if (!Enum.IsDefined(typeof(OrchestratorSessionCloseReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
    }
}

internal sealed class OrchestratorSessionRetirementEnvelope
    : OrchestratorEventEnvelope
{
    internal OrchestratorSessionRetirementEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.SessionRetirementRequested,
            source,
            owner)
    {
    }
}

internal sealed class OrchestratorInvocationResultEnvelope
    : OrchestratorEventEnvelope
{
    internal Guid InvocationId { get; }
    internal OrchestratorInvocationStatus Status { get; }
    internal OrchestratorInvocationSourceKind InvocationSource { get; }
    internal DecisionEvidence ProvidedEvidence { get; }
    internal int MemoryItemCount { get; }
    internal int EvidenceCharacterCount { get; }
    internal int ReasoningCallCount { get; }

    internal OrchestratorInvocationResultEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        Guid invocationId,
        OrchestratorInvocationStatus status,
        OrchestratorInvocationSourceKind invocationSource,
        DecisionEvidence providedEvidence,
        int memoryItemCount,
        int evidenceCharacterCount,
        int reasoningCallCount)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.InvocationResultReceived,
            source,
            owner)
    {
        if (invocationId == Guid.Empty)
            throw new ArgumentException(
                "Invocation ID cannot be empty.",
                nameof(invocationId));
        if (!Enum.IsDefined(typeof(OrchestratorInvocationStatus), status)
            || status == OrchestratorInvocationStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        if (!Enum.IsDefined(
                typeof(OrchestratorInvocationSourceKind),
                invocationSource))
        {
            throw new ArgumentOutOfRangeException(nameof(invocationSource));
        }
        if ((providedEvidence & ~DecisionCoreContractGuard.AllEvidence) != 0)
            throw new ArgumentOutOfRangeException(nameof(providedEvidence));
        if (memoryItemCount < 0 || memoryItemCount > 64)
            throw new ArgumentOutOfRangeException(nameof(memoryItemCount));
        if (evidenceCharacterCount < 0 || evidenceCharacterCount > 32_000)
            throw new ArgumentOutOfRangeException(nameof(evidenceCharacterCount));
        if (reasoningCallCount < 0 || reasoningCallCount > 1)
            throw new ArgumentOutOfRangeException(nameof(reasoningCallCount));
        if (status != OrchestratorInvocationStatus.Completed
            && (providedEvidence != DecisionEvidence.None
                || memoryItemCount != 0
                || evidenceCharacterCount != 0
                || reasoningCallCount != 0))
        {
            throw new ArgumentException(
                "Non-completed results cannot claim evidence or usage.");
        }
        if (status == OrchestratorInvocationStatus.Completed
            && providedEvidence == DecisionEvidence.None)
        {
            throw new ArgumentException(
                "A completed result must identify provided evidence.",
                nameof(providedEvidence));
        }

        InvocationId = invocationId;
        Status = status;
        InvocationSource = invocationSource;
        ProvidedEvidence = providedEvidence;
        MemoryItemCount = memoryItemCount;
        EvidenceCharacterCount = evidenceCharacterCount;
        ReasoningCallCount = reasoningCallCount;
    }
}

internal sealed class OrchestratorInvocationRecord
{
    internal static readonly TimeSpan MaximumDuration =
        TimeSpan.FromMinutes(1);

    internal Guid Id { get; }
    internal Guid DecisionId { get; }
    internal string SessionId { get; }
    internal string GoalId { get; }
    internal string CorrelationId { get; }
    internal InvocationPlan Plan { get; }
    internal OrchestratorInvocationStatus Status { get; }
    internal DateTimeOffset RegisteredAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal DateTimeOffset? CompletedAt { get; }
    internal DecisionEvidence ProvidedEvidence { get; }
    internal OrchestratorInvocationSourceKind? ResultSource { get; }
    internal int MemoryItemCount { get; }
    internal int EvidenceCharacterCount { get; }
    internal int ReasoningCallCount { get; }
    internal DecisionEvidence RemainingEvidence =>
        Plan.RequiredEvidence & ~ProvidedEvidence;

    private OrchestratorInvocationRecord(
        Guid id,
        Guid decisionId,
        string sessionId,
        string goalId,
        string correlationId,
        InvocationPlan plan,
        OrchestratorInvocationStatus status,
        DateTimeOffset registeredAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? completedAt,
        DecisionEvidence providedEvidence,
        OrchestratorInvocationSourceKind? resultSource,
        int memoryItemCount,
        int evidenceCharacterCount,
        int reasoningCallCount)
    {
        Id = id;
        DecisionId = decisionId;
        SessionId = sessionId;
        GoalId = goalId;
        CorrelationId = correlationId;
        Plan = plan;
        Status = status;
        RegisteredAt = registeredAt;
        ExpiresAt = expiresAt;
        CompletedAt = completedAt;
        ProvidedEvidence = providedEvidence;
        ResultSource = resultSource;
        MemoryItemCount = memoryItemCount;
        EvidenceCharacterCount = evidenceCharacterCount;
        ReasoningCallCount = reasoningCallCount;
    }

    internal static OrchestratorInvocationRecord Create(
        string sessionId,
        string correlationId,
        DecisionEvaluation evaluation,
        DateTimeOffset registeredAt)
    {
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        var plan = evaluation.Outcome.Invocation
            ?? throw new ArgumentException(
                "Decision does not contain an invocation plan.",
                nameof(evaluation));
        var goalId = evaluation.Outcome.SelectedGoalId
            ?? throw new ArgumentException(
                "Invocation decision does not reference a goal.",
                nameof(evaluation));
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            registeredAt,
            nameof(registeredAt));

        return new OrchestratorInvocationRecord(
            CreateDeterministicId(evaluation.Trace.DecisionId),
            evaluation.Trace.DecisionId,
            DecisionCoreContractGuard.Identifier(
                sessionId,
                128,
                nameof(sessionId)),
            goalId,
            DecisionCoreContractGuard.Identifier(
                correlationId,
                128,
                nameof(correlationId)),
            plan,
            OrchestratorInvocationStatus.Pending,
            normalizedAt,
            normalizedAt.Add(MaximumDuration),
            completedAt: null,
            DecisionEvidence.None,
            resultSource: null,
            memoryItemCount: 0,
            evidenceCharacterCount: 0,
            reasoningCallCount: 0);
    }

    internal bool IsEquivalentPending(
        string goalId,
        DecisionEvidence requiredEvidence)
        => Status == OrchestratorInvocationStatus.Pending
            && string.Equals(GoalId, goalId, StringComparison.Ordinal)
            && RemainingEvidence == requiredEvidence;

    internal OrchestratorInvocationRecord Complete(
        OrchestratorInvocationResultEnvelope result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (Status != OrchestratorInvocationStatus.Pending)
            throw new InvalidOperationException("Invocation is not pending.");

        var evidence = ProvidedEvidence | result.ProvidedEvidence;
        var status = result.Status;
        if (status == OrchestratorInvocationStatus.Completed
            && evidence != Plan.RequiredEvidence)
        {
            status = OrchestratorInvocationStatus.Pending;
        }
        return new OrchestratorInvocationRecord(
            Id,
            DecisionId,
            SessionId,
            GoalId,
            CorrelationId,
            Plan,
            status,
            RegisteredAt,
            ExpiresAt,
            status == OrchestratorInvocationStatus.Pending
                ? null
                : result.OccurredAt,
            evidence,
            result.InvocationSource,
            MemoryItemCount + result.MemoryItemCount,
            EvidenceCharacterCount + result.EvidenceCharacterCount,
            ReasoningCallCount + result.ReasoningCallCount);
    }

    internal OrchestratorInvocationRecord Cancel(DateTimeOffset cancelledAt)
    {
        if (Status != OrchestratorInvocationStatus.Pending)
            return this;
        return new OrchestratorInvocationRecord(
            Id,
            DecisionId,
            SessionId,
            GoalId,
            CorrelationId,
            Plan,
            OrchestratorInvocationStatus.Cancelled,
            RegisteredAt,
            ExpiresAt,
            DecisionCoreContractGuard.Timestamp(
                cancelledAt,
                nameof(cancelledAt)),
            ProvidedEvidence,
            resultSource: null,
            MemoryItemCount,
            EvidenceCharacterCount,
            ReasoningCallCount);
    }

    private static Guid CreateDeterministicId(Guid decisionId)
    {
        var bytes = decisionId.ToByteArray();
        bytes[14] ^= 0x4c;
        bytes[15] ^= 0x49;
        var id = new Guid(bytes);
        if (id == Guid.Empty)
            throw new InvalidOperationException("Invocation identity is invalid.");
        return id;
    }
}

internal sealed class OrchestratorEvaluationStamp
{
    internal Guid SnapshotId { get; }
    internal TimeSpan SnapshotValidity { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal DecisionBudgets Budgets { get; }

    internal OrchestratorEvaluationStamp(
        Guid snapshotId,
        TimeSpan snapshotValidity,
        DateTimeOffset evaluatedAt,
        DecisionBudgets? budgets = null)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException(
                "Snapshot ID cannot be empty.",
                nameof(snapshotId));
        if (snapshotValidity <= TimeSpan.Zero
            || snapshotValidity > SituationSnapshotAssembler.MaximumValidity)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotValidity));
        }

        SnapshotId = snapshotId;
        SnapshotValidity = snapshotValidity;
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        Budgets = budgets ?? DecisionBudgets.ShadowDefault;
    }
}

internal sealed class OrchestratorShadowStep
{
    internal OrchestratorEventEnvelope Envelope { get; }
    internal OrchestratorEvaluationStamp? EvaluationStamp { get; }

    internal OrchestratorShadowStep(
        OrchestratorEventEnvelope envelope,
        OrchestratorEvaluationStamp? evaluationStamp = null)
    {
        Envelope = envelope
            ?? throw new ArgumentNullException(nameof(envelope));
        EvaluationStamp = evaluationStamp;
    }
}

internal sealed class OrchestratorProcessResult
{
    internal string EventId { get; }
    internal OrchestratorProcessDisposition Disposition { get; }
    internal OrchestratorProcessReason Reason { get; }
    internal SituationSnapshot? Snapshot { get; }
    internal DecisionEvaluation? Evaluation { get; }
    internal OrchestratorInvocationRecord? Invocation { get; }
    internal OutcomeExpectation? OutcomeExpectation { get; }
    internal OutcomeCorrelationResult? OutcomeCorrelation { get; }
    internal OutcomeAppraisal? OutcomeAppraisal { get; }
    internal bool Accepted =>
        Disposition == OrchestratorProcessDisposition.Accepted;

    internal OrchestratorProcessResult(
        string eventId,
        OrchestratorProcessDisposition disposition,
        OrchestratorProcessReason reason,
        SituationSnapshot? snapshot,
        DecisionEvaluation? evaluation,
        OrchestratorInvocationRecord? invocation = null,
        OutcomeExpectation? outcomeExpectation = null,
        OutcomeCorrelationResult? outcomeCorrelation = null,
        OutcomeAppraisal? outcomeAppraisal = null)
    {
        EventId = DecisionCoreContractGuard.Identifier(
            eventId,
            128,
            nameof(eventId));
        if (!Enum.IsDefined(
                typeof(OrchestratorProcessDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (!Enum.IsDefined(typeof(OrchestratorProcessReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if ((snapshot == null) != (evaluation == null))
        {
            throw new ArgumentException(
                "Snapshot and evaluation must be returned together.");
        }
        if (evaluation != null
            && evaluation.Trace.SnapshotId != snapshot!.Id)
        {
            throw new ArgumentException(
                "Evaluation must reference the returned snapshot.");
        }
        if (disposition == OrchestratorProcessDisposition.Rejected
            && invocation != null)
        {
            throw new ArgumentException(
                "Rejected results cannot publish invocation state.",
                nameof(invocation));
        }
        if (invocation != null
            && reason != OrchestratorProcessReason.SituationEvaluated
            && reason != OrchestratorProcessReason.InvocationResultAccepted)
        {
            throw new ArgumentException(
                "Invocation state is not valid for this process reason.",
                nameof(invocation));
        }
        if (outcomeAppraisal != null
            && (outcomeCorrelation == null
                || outcomeCorrelation.Disposition
                    != OutcomeCorrelationDisposition.AcceptedTerminal))
        {
            throw new ArgumentException(
                "Outcome appraisal requires an accepted terminal correlation.",
                nameof(outcomeAppraisal));
        }
        if (outcomeCorrelation != null
            && outcomeCorrelation.Disposition
                == OutcomeCorrelationDisposition.AcceptedTerminal
            && outcomeAppraisal == null)
        {
            throw new ArgumentException(
                "Accepted terminal correlation requires an appraisal.",
                nameof(outcomeAppraisal));
        }
        if (outcomeExpectation != null
            && reason
                != OrchestratorProcessReason.OutcomeExpectationRegistered)
        {
            throw new ArgumentException(
                "Outcome expectation is not valid for this process reason.",
                nameof(outcomeExpectation));
        }
        if (reason == OrchestratorProcessReason.OutcomeExpectationRegistered
            && outcomeExpectation == null)
        {
            throw new ArgumentNullException(nameof(outcomeExpectation));
        }
        if (outcomeCorrelation != null
            && reason != OrchestratorProcessReason.OutcomeSignalRejected
            && reason != OrchestratorProcessReason.OutcomeProgressAccepted
            && reason != OrchestratorProcessReason.OutcomeTerminalAccepted)
        {
            throw new ArgumentException(
                "Outcome correlation is not valid for this process reason.",
                nameof(outcomeCorrelation));
        }
        if (reason == OrchestratorProcessReason.OutcomeSignalRejected
            && (outcomeCorrelation == null
                || outcomeCorrelation.Disposition
                    != OutcomeCorrelationDisposition.Rejected))
        {
            throw new ArgumentException(
                "Rejected outcome signal requires rejected correlation.",
                nameof(outcomeCorrelation));
        }
        if (reason == OrchestratorProcessReason.OutcomeProgressAccepted
            && (outcomeCorrelation == null
                || outcomeCorrelation.Disposition
                    != OutcomeCorrelationDisposition.AcceptedProgress))
        {
            throw new ArgumentException(
                "Progress result requires accepted progress correlation.",
                nameof(outcomeCorrelation));
        }
        if (reason == OrchestratorProcessReason.OutcomeTerminalAccepted
            && (outcomeCorrelation == null
                || outcomeCorrelation.Disposition
                    != OutcomeCorrelationDisposition.AcceptedTerminal))
        {
            throw new ArgumentException(
                "Terminal result requires accepted terminal correlation.",
                nameof(outcomeCorrelation));
        }

        Disposition = disposition;
        Reason = reason;
        Snapshot = snapshot;
        Evaluation = evaluation;
        Invocation = invocation;
        OutcomeExpectation = outcomeExpectation;
        OutcomeCorrelation = outcomeCorrelation;
        OutcomeAppraisal = outcomeAppraisal;
    }
}

internal sealed class OrchestratorShadowTransition
{
    internal OrchestratorControlPlaneState State { get; }
    internal OrchestratorProcessResult Result { get; }

    internal OrchestratorShadowTransition(
        OrchestratorControlPlaneState state,
        OrchestratorProcessResult result)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}

internal sealed class OrchestratorSessionState
{
    internal const int MaximumEvents = 512;
    internal const int MaximumOperationalEvents = MaximumEvents - 2;
    internal const int MaximumInvocations = 32;
    internal const int MaximumPendingInvocations = 4;
    internal const int MaximumRetainedDecisions = 64;

    internal string Id { get; }
    internal OrchestratorSessionOwner Owner { get; }
    internal OrchestratorSessionStatus Status { get; }
    internal DateTimeOffset OpenedAt { get; }
    internal DateTimeOffset UpdatedAt { get; }
    internal long LastEnvelopeSequence { get; }
    internal long LastSnapshotSequence { get; }
    internal SituationProjection Situation { get; }
    internal OrchestratorCancellationReason? CancellationReason { get; }
    internal OrchestratorSessionCloseReason? CloseReason { get; }
    internal IReadOnlyList<string> ProcessedEventIds { get; }
    internal IReadOnlyList<Guid> SnapshotIds { get; }
    internal IReadOnlyList<OrchestratorInvocationRecord> Invocations { get; }
    internal IReadOnlyList<OrchestratorDecisionRecord> Decisions { get; }
    internal OutcomeCorrelationLedger Outcomes { get; }

    private OrchestratorSessionState(
        string id,
        OrchestratorSessionOwner owner,
        OrchestratorSessionStatus status,
        DateTimeOffset openedAt,
        DateTimeOffset updatedAt,
        long lastEnvelopeSequence,
        long lastSnapshotSequence,
        SituationProjection situation,
        OrchestratorCancellationReason? cancellationReason,
        OrchestratorSessionCloseReason? closeReason,
        IReadOnlyList<string> processedEventIds,
        IReadOnlyList<Guid> snapshotIds,
        IReadOnlyList<OrchestratorInvocationRecord> invocations,
        IReadOnlyList<OrchestratorDecisionRecord> decisions,
        OutcomeCorrelationLedger outcomes)
    {
        if (!Enum.IsDefined(typeof(OrchestratorSessionStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status == OrchestratorSessionStatus.Active
            && (cancellationReason.HasValue || closeReason.HasValue))
        {
            throw new ArgumentException(
                "An active session cannot have a terminal reason.");
        }
        if (status == OrchestratorSessionStatus.Cancelled
            && (!cancellationReason.HasValue || closeReason.HasValue))
        {
            throw new ArgumentException(
                "A cancelled session requires only a cancellation reason.");
        }
        if (status == OrchestratorSessionStatus.Closed
            && (cancellationReason.HasValue || !closeReason.HasValue))
        {
            throw new ArgumentException(
                "A closed session requires only a close reason.");
        }
        if (processedEventIds == null)
            throw new ArgumentNullException(nameof(processedEventIds));
        if (processedEventIds.Count == 0
            || processedEventIds.Count > MaximumEvents)
        {
            throw new ArgumentOutOfRangeException(nameof(processedEventIds));
        }

        Id = id;
        Owner = owner;
        Status = status;
        OpenedAt = openedAt;
        UpdatedAt = updatedAt;
        LastEnvelopeSequence = lastEnvelopeSequence;
        LastSnapshotSequence = lastSnapshotSequence;
        Situation = situation;
        CancellationReason = cancellationReason;
        CloseReason = closeReason;
        ProcessedEventIds = processedEventIds;
        SnapshotIds = snapshotIds;
        Invocations = invocations;
        Decisions = decisions;
        Outcomes = outcomes
            ?? throw new ArgumentNullException(nameof(outcomes));
    }

    internal static OrchestratorSessionState Open(
        OrchestratorSessionOpenedEnvelope envelope)
        => new(
            envelope.SessionId,
            envelope.Owner,
            OrchestratorSessionStatus.Active,
            envelope.OccurredAt,
            envelope.OccurredAt,
            envelope.Sequence,
            lastSnapshotSequence: 0,
            SituationProjection.Create(
                envelope.SessionId,
                envelope.OccurredAt),
            cancellationReason: null,
            closeReason: null,
            Array.AsReadOnly(new[] { envelope.Id }),
            Array.Empty<Guid>(),
            Array.Empty<OrchestratorInvocationRecord>(),
            Array.Empty<OrchestratorDecisionRecord>(),
            OutcomeCorrelationLedger.Empty);

    internal OrchestratorSessionState AcceptSituation(
        OrchestratorSituationEnvelope envelope,
        SituationProjection situation,
        Guid snapshotId,
        DateTimeOffset evaluatedAt,
        DecisionEvaluation evaluation,
        OrchestratorInvocationRecord? invocation)
        => new(
            Id,
            Owner,
            Status,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            LastSnapshotSequence + 1,
            situation,
            CancellationReason,
            CloseReason,
            Append(ProcessedEventIds, envelope.Id),
            Append(SnapshotIds, snapshotId),
            invocation == null
                ? Invocations
                : Append(Invocations, invocation),
            RetainDecision(
                Decisions,
                envelope.CorrelationId,
                evaluatedAt,
                evaluation),
            Outcomes);

    internal OrchestratorSessionState AcceptInvocationResult(
        OrchestratorInvocationResultEnvelope envelope,
        OrchestratorInvocationRecord invocation,
        SituationProjection situation,
        Guid? snapshotId,
        DateTimeOffset? evaluatedAt,
        DecisionEvaluation? evaluation)
    {
        if (evaluatedAt.HasValue != (evaluation != null))
        {
            throw new ArgumentException(
                "Evaluation timestamp and evaluation must be supplied together.");
        }
        return new(
            Id,
            Owner,
            Status,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            snapshotId.HasValue
                ? LastSnapshotSequence + 1
                : LastSnapshotSequence,
            situation,
            CancellationReason,
            CloseReason,
            Append(ProcessedEventIds, envelope.Id),
            snapshotId.HasValue
                ? Append(SnapshotIds, snapshotId.Value)
                : SnapshotIds,
            Replace(Invocations, invocation),
            evaluation == null
                ? Decisions
                : RetainDecision(
                    Decisions,
                    envelope.CorrelationId,
                    evaluatedAt!.Value,
                    evaluation),
            Outcomes);
    }

    internal OrchestratorSessionState AcceptOutcomeRegistration(
        OrchestratorOutcomeExpectationEnvelope envelope,
        OutcomeCorrelationLedger outcomes)
        => new(
            Id,
            Owner,
            Status,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            LastSnapshotSequence,
            Situation,
            CancellationReason,
            CloseReason,
            Append(ProcessedEventIds, envelope.Id),
            SnapshotIds,
            Invocations,
            Decisions,
            outcomes);

    internal OrchestratorSessionState AcceptOutcomeSignal(
        OrchestratorOutcomeSignalEnvelope envelope,
        OutcomeCorrelationLedger outcomes)
        => new(
            Id,
            Owner,
            Status,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            LastSnapshotSequence,
            Situation,
            CancellationReason,
            CloseReason,
            Append(ProcessedEventIds, envelope.Id),
            SnapshotIds,
            Invocations,
            Decisions,
            outcomes);

    internal OrchestratorSessionState Cancel(
        OrchestratorCancellationEnvelope envelope)
        => new(
            Id,
            Owner,
            OrchestratorSessionStatus.Cancelled,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            LastSnapshotSequence,
            Situation,
            envelope.Reason,
            closeReason: null,
            Append(ProcessedEventIds, envelope.Id),
            SnapshotIds,
            CancelPending(Invocations, envelope.OccurredAt),
            Decisions,
            Outcomes.AbandonPending(envelope.OccurredAt));

    internal OrchestratorSessionState Close(
        OrchestratorSessionCloseEnvelope envelope)
        => new(
            Id,
            Owner,
            OrchestratorSessionStatus.Closed,
            OpenedAt,
            envelope.OccurredAt,
            envelope.Sequence,
            LastSnapshotSequence,
            Situation,
            cancellationReason: null,
            envelope.Reason,
            Append(ProcessedEventIds, envelope.Id),
            SnapshotIds,
            Invocations,
            Decisions,
            Outcomes);

    internal bool ContainsEventId(string eventId)
    {
        foreach (var existing in ProcessedEventIds)
        {
            if (string.Equals(existing, eventId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal bool ContainsSnapshotId(Guid snapshotId)
    {
        foreach (var existing in SnapshotIds)
        {
            if (existing == snapshotId)
                return true;
        }
        return false;
    }

    internal OrchestratorInvocationRecord? FindInvocation(Guid invocationId)
    {
        foreach (var invocation in Invocations)
        {
            if (invocation.Id == invocationId)
                return invocation;
        }
        return null;
    }

    internal OrchestratorDecisionRecord? FindDecision(Guid decisionId)
    {
        foreach (var decision in Decisions)
        {
            if (decision.Id == decisionId)
                return decision;
        }
        return null;
    }

    internal OrchestratorInvocationRecord? FindEquivalentPendingInvocation(
        string goalId,
        DecisionEvidence requiredEvidence)
    {
        foreach (var invocation in Invocations)
        {
            if (invocation.IsEquivalentPending(goalId, requiredEvidence))
                return invocation;
        }
        return null;
    }

    internal int PendingInvocationCount
    {
        get
        {
            var count = 0;
            foreach (var invocation in Invocations)
            {
                if (invocation.Status == OrchestratorInvocationStatus.Pending)
                    count++;
            }
            return count;
        }
    }

    internal int PendingOutcomeExpectationCount =>
        Outcomes.PendingExpectationCount;

    internal bool IsRetirable =>
        Status is OrchestratorSessionStatus.Cancelled
            or OrchestratorSessionStatus.Closed
        && PendingInvocationCount == 0
        && PendingOutcomeExpectationCount == 0;

    private static IReadOnlyList<string> Append(
        IReadOnlyList<string> source,
        string value)
    {
        var copy = new string[source.Count + 1];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index];
        copy[^1] = value;
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<Guid> Append(
        IReadOnlyList<Guid> source,
        Guid value)
    {
        var copy = new Guid[source.Count + 1];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index];
        copy[^1] = value;
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<OrchestratorInvocationRecord> Append(
        IReadOnlyList<OrchestratorInvocationRecord> source,
        OrchestratorInvocationRecord value)
    {
        var copy = new OrchestratorInvocationRecord[source.Count + 1];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index];
        copy[^1] = value;
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<OrchestratorInvocationRecord> Replace(
        IReadOnlyList<OrchestratorInvocationRecord> source,
        OrchestratorInvocationRecord value)
    {
        var copy = new OrchestratorInvocationRecord[source.Count];
        var replaced = false;
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index].Id == value.Id)
            {
                copy[index] = value;
                replaced = true;
            }
            else
            {
                copy[index] = source[index];
            }
        }
        if (!replaced)
            throw new InvalidOperationException("Invocation does not exist.");
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<OrchestratorInvocationRecord> CancelPending(
        IReadOnlyList<OrchestratorInvocationRecord> source,
        DateTimeOffset cancelledAt)
    {
        if (source.Count == 0)
            return source;
        var copy = new OrchestratorInvocationRecord[source.Count];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index].Cancel(cancelledAt);
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<OrchestratorDecisionRecord> RetainDecision(
        IReadOnlyList<OrchestratorDecisionRecord> source,
        string correlationId,
        DateTimeOffset evaluatedAt,
        DecisionEvaluation evaluation)
    {
        var decision = new OrchestratorDecisionRecord(
            correlationId,
            evaluatedAt,
            evaluation);
        if (!decision.SupportsOutcomeRegistration)
            return source;
        foreach (var existing in source)
        {
            if (existing.Id == decision.Id)
                return source;
        }

        var targetCount = Math.Min(
            source.Count + 1,
            MaximumRetainedDecisions);
        var copy = new OrchestratorDecisionRecord[targetCount];
        var sourceStart = source.Count >= MaximumRetainedDecisions
            ? source.Count - MaximumRetainedDecisions + 1
            : 0;
        var destination = 0;
        for (var index = sourceStart; index < source.Count; index++)
            copy[destination++] = source[index];
        copy[^1] = decision;
        return Array.AsReadOnly(copy);
    }
}

internal sealed class OrchestratorRetiredSessionRecord
{
    internal string Id { get; }
    internal OrchestratorSessionOwner Owner { get; }
    internal OrchestratorSessionStatus TerminalStatus { get; }
    internal DateTimeOffset RetiredAt { get; }
    internal OrchestratorCancellationReason? CancellationReason { get; }
    internal OrchestratorSessionCloseReason? CloseReason { get; }
    internal IReadOnlyList<string> ProcessedEventIds { get; }

    internal OrchestratorRetiredSessionRecord(
        OrchestratorSessionState session,
        OrchestratorSessionRetirementEnvelope envelope)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (!session.IsRetirable)
            throw new ArgumentException("Session is not safe to retire.", nameof(session));
        if (!string.Equals(
                session.Id,
                envelope.SessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Retirement envelope must target the session.",
                nameof(envelope));
        }
        if (!session.Owner.Matches(envelope.Owner))
            throw new ArgumentException("Retirement owner mismatch.", nameof(envelope));
        if (envelope.Sequence != session.LastEnvelopeSequence + 1)
            throw new ArgumentException("Retirement sequence mismatch.", nameof(envelope));
        if (envelope.OccurredAt < session.UpdatedAt)
            throw new ArgumentException("Retirement timestamp reversal.", nameof(envelope));
        if (session.ProcessedEventIds.Count >= OrchestratorSessionState.MaximumEvents)
            throw new ArgumentException("Retirement event capacity is exhausted.", nameof(session));
        if (session.ContainsEventId(envelope.Id))
            throw new ArgumentException("Retirement event identity is duplicated.", nameof(envelope));

        var eventIds = new string[session.ProcessedEventIds.Count + 1];
        for (var index = 0; index < session.ProcessedEventIds.Count; index++)
            eventIds[index] = session.ProcessedEventIds[index];
        eventIds[^1] = envelope.Id;

        Id = session.Id;
        Owner = session.Owner;
        TerminalStatus = session.Status;
        RetiredAt = envelope.OccurredAt;
        CancellationReason = session.CancellationReason;
        CloseReason = session.CloseReason;
        ProcessedEventIds = Array.AsReadOnly(eventIds);
    }

    internal bool ContainsEventId(string eventId)
    {
        foreach (var existing in ProcessedEventIds)
        {
            if (string.Equals(existing, eventId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

internal sealed class OrchestratorControlPlaneState
{
    internal const int MaximumSessions = 64;
    internal const int MaximumRetiredSessions = 64;

    internal static OrchestratorControlPlaneState Empty { get; } =
        new(
            Array.Empty<OrchestratorSessionState>(),
            Array.Empty<OrchestratorRetiredSessionRecord>());

    internal IReadOnlyList<OrchestratorSessionState> Sessions { get; }
    internal IReadOnlyList<OrchestratorRetiredSessionRecord> RetiredSessions { get; }

    private OrchestratorControlPlaneState(
        IReadOnlyList<OrchestratorSessionState> sessions,
        IReadOnlyList<OrchestratorRetiredSessionRecord> retiredSessions)
    {
        Sessions = sessions;
        RetiredSessions = retiredSessions;
    }

    internal OrchestratorSessionState? FindSession(string sessionId)
    {
        foreach (var session in Sessions)
        {
            if (string.Equals(session.Id, sessionId, StringComparison.Ordinal))
                return session;
        }
        return null;
    }

    internal OrchestratorRetiredSessionRecord? FindRetiredSession(
        string sessionId)
    {
        foreach (var session in RetiredSessions)
        {
            if (string.Equals(session.Id, sessionId, StringComparison.Ordinal))
                return session;
        }
        return null;
    }

    internal bool ContainsEventId(string eventId)
    {
        foreach (var session in Sessions)
        {
            if (session.ContainsEventId(eventId))
                return true;
        }
        foreach (var session in RetiredSessions)
        {
            if (session.ContainsEventId(eventId))
                return true;
        }
        return false;
    }

    internal OrchestratorControlPlaneState Add(
        OrchestratorSessionState session)
    {
        if (Sessions.Count >= MaximumSessions)
            throw new InvalidOperationException("Session limit reached.");
        var copy = new OrchestratorSessionState[Sessions.Count + 1];
        for (var index = 0; index < Sessions.Count; index++)
            copy[index] = Sessions[index];
        copy[^1] = session;
        Array.Sort(
            copy,
            (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return new OrchestratorControlPlaneState(
            Array.AsReadOnly(copy),
            RetiredSessions);
    }

    internal OrchestratorControlPlaneState Replace(
        OrchestratorSessionState session)
    {
        var copy = new OrchestratorSessionState[Sessions.Count];
        var replaced = false;
        for (var index = 0; index < Sessions.Count; index++)
        {
            if (string.Equals(
                    Sessions[index].Id,
                    session.Id,
                    StringComparison.Ordinal))
            {
                copy[index] = session;
                replaced = true;
            }
            else
            {
                copy[index] = Sessions[index];
            }
        }
        if (!replaced)
            throw new InvalidOperationException("Session does not exist.");
        return new OrchestratorControlPlaneState(
            Array.AsReadOnly(copy),
            RetiredSessions);
    }

    internal OrchestratorControlPlaneState Retire(
        OrchestratorSessionState session,
        OrchestratorSessionRetirementEnvelope envelope)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (RetiredSessions.Count >= MaximumRetiredSessions)
            throw new InvalidOperationException("Retired session limit reached.");

        var sourceIndex = -1;
        for (var index = 0; index < Sessions.Count; index++)
        {
            if (string.Equals(Sessions[index].Id, session.Id, StringComparison.Ordinal))
            {
                sourceIndex = index;
                break;
            }
        }
        if (sourceIndex < 0)
            throw new InvalidOperationException("Session does not exist.");
        if (!ReferenceEquals(Sessions[sourceIndex], session))
            throw new InvalidOperationException("Session state is stale.");
        if (ContainsEventId(envelope.Id))
            throw new InvalidOperationException("Event identity is duplicated.");

        var active = new OrchestratorSessionState[Sessions.Count - 1];
        var destination = 0;
        for (var index = 0; index < Sessions.Count; index++)
        {
            if (index != sourceIndex)
                active[destination++] = Sessions[index];
        }

        var retired =
            new OrchestratorRetiredSessionRecord[RetiredSessions.Count + 1];
        for (var index = 0; index < RetiredSessions.Count; index++)
            retired[index] = RetiredSessions[index];
        retired[^1] = new OrchestratorRetiredSessionRecord(session, envelope);
        Array.Sort(
            retired,
            (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return new OrchestratorControlPlaneState(
            Array.AsReadOnly(active),
            Array.AsReadOnly(retired));
    }
}
