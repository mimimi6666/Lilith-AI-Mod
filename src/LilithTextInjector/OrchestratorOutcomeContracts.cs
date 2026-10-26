using System;

namespace LilithTextInjector;

internal sealed class OrchestratorDecisionRecord
{
    internal Guid Id => Evaluation.Trace.DecisionId;
    internal string CorrelationId { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal DecisionEvaluation Evaluation { get; }
    internal bool SupportsOutcomeRegistration =>
        Evaluation.Outcome.SelectedGoalId != null
        && Evaluation.Outcome.Invocation == null
        && Evaluation.Outcome.Disposition is DecisionDisposition.Respond
            or DecisionDisposition.Act
            or DecisionDisposition.AskConsent;

    internal OrchestratorDecisionRecord(
        string correlationId,
        DateTimeOffset evaluatedAt,
        DecisionEvaluation evaluation)
    {
        CorrelationId = DecisionCoreContractGuard.Identifier(
            correlationId,
            128,
            nameof(correlationId));
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        Evaluation = evaluation
            ?? throw new ArgumentNullException(nameof(evaluation));
    }
}

internal sealed class OrchestratorOutcomeExpectationEnvelope
    : OrchestratorEventEnvelope
{
    internal Guid DecisionId { get; }
    internal OutcomeExpectationKind ExpectationKind { get; }
    internal DateTimeOffset ObserveUntil { get; }

    internal OrchestratorOutcomeExpectationEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        Guid decisionId,
        OutcomeExpectationKind expectationKind,
        DateTimeOffset observeUntil)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.OutcomeExpectationRegistrationRequested,
            source,
            owner)
    {
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Decision ID cannot be empty.",
                nameof(decisionId));
        }
        if (!Enum.IsDefined(
                typeof(OutcomeExpectationKind),
                expectationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(expectationKind));
        }

        DecisionId = decisionId;
        ExpectationKind = expectationKind;
        ObserveUntil = DecisionCoreContractGuard.Timestamp(
            observeUntil,
            nameof(observeUntil));
    }
}

internal sealed class OrchestratorOutcomeSignalEnvelope
    : OrchestratorEventEnvelope
{
    private const OutcomeConfounder AllConfounders =
        OutcomeConfounder.PlayerTakeover
        | OutcomeConfounder.ConcurrentIntervention
        | OutcomeConfounder.ExternalStateChange
        | OutcomeConfounder.MissingTerminalEvidence;

    internal Guid DecisionId { get; }
    internal OutcomeStatus Status { get; }
    internal OutcomeSignalSource SignalSource { get; }
    internal bool HasDirectExecutionCorrelation { get; }
    internal OutcomeConfounder Confounders { get; }

    internal OrchestratorOutcomeSignalEnvelope(
        string protocolVersion,
        string id,
        string correlationId,
        long sequence,
        string sessionId,
        DateTimeOffset occurredAt,
        OrchestratorSourceKind source,
        OrchestratorSessionOwner owner,
        Guid decisionId,
        OutcomeStatus status,
        OutcomeSignalSource signalSource,
        bool hasDirectExecutionCorrelation,
        OutcomeConfounder confounders = OutcomeConfounder.None)
        : base(
            protocolVersion,
            id,
            correlationId,
            sequence,
            sessionId,
            occurredAt,
            OrchestratorEventKind.OutcomeSignalReceived,
            source,
            owner)
    {
        if (decisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Decision ID cannot be empty.",
                nameof(decisionId));
        }
        if (!Enum.IsDefined(typeof(OutcomeStatus), status)
            || status == OutcomeStatus.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        if (!Enum.IsDefined(typeof(OutcomeSignalSource), signalSource))
            throw new ArgumentOutOfRangeException(nameof(signalSource));
        if ((confounders & ~AllConfounders) != 0)
            throw new ArgumentOutOfRangeException(nameof(confounders));

        DecisionId = decisionId;
        Status = status;
        SignalSource = signalSource;
        HasDirectExecutionCorrelation = hasDirectExecutionCorrelation;
        Confounders = confounders;
    }

    internal OutcomeSignal ToSignal()
        => new(
            Id,
            DecisionId,
            CorrelationId,
            Status,
            SignalSource,
            OccurredAt,
            HasDirectExecutionCorrelation,
            Confounders);
}
