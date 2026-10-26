using System;

namespace LilithTextInjector;

internal enum OutcomeExpectationKind
{
    ResponseDelivery = 0,
    ConsentDecision = 1,
    CapabilityExecution = 2,
    AvatarTransition = 3
}

internal enum OutcomeSignalSource
{
    Policy = 0,
    Consent = 1,
    ResponseSurface = 2,
    CapabilityExecutor = 3,
    OutcomeVerifier = 4
}

[Flags]
internal enum OutcomeConfounder
{
    None = 0,
    PlayerTakeover = 1 << 0,
    ConcurrentIntervention = 1 << 1,
    ExternalStateChange = 1 << 2,
    MissingTerminalEvidence = 1 << 3
}

internal enum OutcomeCorrelationDisposition
{
    Rejected = 0,
    AcceptedProgress = 1,
    AcceptedTerminal = 2
}

internal enum OutcomeCorrelationReason
{
    UnknownDecision = 0,
    CorrelationMismatch = 1,
    BeforeObservationWindow = 2,
    AfterObservationWindow = 3,
    SourceNotAllowed = 4,
    StatusNotAllowed = 5,
    AlreadyTerminal = 6,
    SignalLimitReached = 7,
    ProgressAccepted = 8,
    TerminalAccepted = 9,
    ExpectationAbandoned = 10
}

internal sealed class OutcomeExpectation
{
    internal static readonly TimeSpan MaximumObservationWindow =
        TimeSpan.FromMinutes(10);

    internal Guid DecisionId { get; }
    internal string GoalId { get; }
    internal string CorrelationId { get; }
    internal DecisionDisposition DecisionDisposition { get; }
    internal OutcomeExpectationKind Kind { get; }
    internal DateTimeOffset RegisteredAt { get; }
    internal DateTimeOffset ObserveUntil { get; }

    private OutcomeExpectation(
        Guid decisionId,
        string goalId,
        string correlationId,
        DecisionDisposition decisionDisposition,
        OutcomeExpectationKind kind,
        DateTimeOffset registeredAt,
        DateTimeOffset observeUntil)
    {
        DecisionId = decisionId;
        GoalId = goalId;
        CorrelationId = correlationId;
        DecisionDisposition = decisionDisposition;
        Kind = kind;
        RegisteredAt = registeredAt;
        ObserveUntil = observeUntil;
    }

    internal static OutcomeExpectation Create(
        DecisionEvaluation evaluation,
        string correlationId,
        OutcomeExpectationKind kind,
        DateTimeOffset registeredAt,
        DateTimeOffset observeUntil)
    {
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        if (!Enum.IsDefined(typeof(OutcomeExpectationKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var disposition = evaluation.Outcome.Disposition;
        if (!IsCompatible(disposition, kind))
        {
            throw new ArgumentException(
                "Expectation kind is incompatible with the decision disposition.",
                nameof(kind));
        }
        var goalId = evaluation.Outcome.SelectedGoalId;
        if (goalId == null)
        {
            throw new ArgumentException(
                "Only a selected behavior proposal can register an outcome expectation.",
                nameof(evaluation));
        }

        var normalizedRegisteredAt = DecisionCoreContractGuard.Timestamp(
            registeredAt,
            nameof(registeredAt));
        var normalizedObserveUntil = DecisionCoreContractGuard.Timestamp(
            observeUntil,
            nameof(observeUntil));
        var duration = normalizedObserveUntil - normalizedRegisteredAt;
        if (duration <= TimeSpan.Zero || duration > MaximumObservationWindow)
            throw new ArgumentOutOfRangeException(nameof(observeUntil));

        return new OutcomeExpectation(
            evaluation.Trace.DecisionId,
            goalId,
            DecisionCoreContractGuard.Identifier(
                correlationId,
                128,
                nameof(correlationId)),
            disposition,
            kind,
            normalizedRegisteredAt,
            normalizedObserveUntil);
    }

    private static bool IsCompatible(
        DecisionDisposition disposition,
        OutcomeExpectationKind kind)
        => disposition switch
        {
            DecisionDisposition.Respond =>
                kind == OutcomeExpectationKind.ResponseDelivery,
            DecisionDisposition.AskConsent =>
                kind == OutcomeExpectationKind.ConsentDecision,
            DecisionDisposition.Act =>
                kind is OutcomeExpectationKind.CapabilityExecution
                    or OutcomeExpectationKind.AvatarTransition,
            _ => false
        };
}

internal sealed class OutcomeSignal
{
    private const OutcomeConfounder AllConfounders =
        OutcomeConfounder.PlayerTakeover
        | OutcomeConfounder.ConcurrentIntervention
        | OutcomeConfounder.ExternalStateChange
        | OutcomeConfounder.MissingTerminalEvidence;

    internal string Id { get; }
    internal Guid DecisionId { get; }
    internal string CorrelationId { get; }
    internal OutcomeStatus Status { get; }
    internal OutcomeSignalSource Source { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal bool HasDirectExecutionCorrelation { get; }
    internal OutcomeConfounder Confounders { get; }
    internal bool IsTerminal =>
        Status is OutcomeStatus.Rejected
            or OutcomeStatus.Completed
            or OutcomeStatus.Interrupted
            or OutcomeStatus.Failed
            or OutcomeStatus.TimedOut;

    internal OutcomeSignal(
        string id,
        Guid decisionId,
        string correlationId,
        OutcomeStatus status,
        OutcomeSignalSource source,
        DateTimeOffset observedAt,
        bool hasDirectExecutionCorrelation,
        OutcomeConfounder confounders = OutcomeConfounder.None)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.", nameof(decisionId));
        DecisionId = decisionId;
        CorrelationId = DecisionCoreContractGuard.Identifier(
            correlationId,
            128,
            nameof(correlationId));
        if (!Enum.IsDefined(typeof(OutcomeStatus), status)
            || status == OutcomeStatus.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        if (!Enum.IsDefined(typeof(OutcomeSignalSource), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if ((confounders & ~AllConfounders) != 0)
            throw new ArgumentOutOfRangeException(nameof(confounders));

        Status = status;
        Source = source;
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        HasDirectExecutionCorrelation = hasDirectExecutionCorrelation;
        Confounders = confounders;
    }
}

internal sealed class OutcomeCorrelationResult
{
    internal OutcomeCorrelationDisposition Disposition { get; }
    internal OutcomeCorrelationReason Reason { get; }
    internal string SignalId { get; }
    internal OutcomeExpectation? Expectation { get; }
    internal OutcomeObservation? Observation { get; }
    internal CausalConfidence CausalConfidence { get; }

    internal OutcomeCorrelationResult(
        OutcomeCorrelationDisposition disposition,
        OutcomeCorrelationReason reason,
        string signalId,
        OutcomeExpectation? expectation,
        OutcomeObservation? observation,
        CausalConfidence causalConfidence)
    {
        if (!Enum.IsDefined(typeof(OutcomeCorrelationDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(OutcomeCorrelationReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (!Enum.IsDefined(typeof(CausalConfidence), causalConfidence))
            throw new ArgumentOutOfRangeException(nameof(causalConfidence));
        if (disposition == OutcomeCorrelationDisposition.AcceptedTerminal
            && observation == null)
        {
            throw new ArgumentNullException(nameof(observation));
        }
        if (disposition != OutcomeCorrelationDisposition.AcceptedTerminal
            && observation != null)
        {
            throw new ArgumentException(
                "Only an accepted terminal signal can produce an observation.",
                nameof(observation));
        }
        if (disposition == OutcomeCorrelationDisposition.Rejected
            && causalConfidence != CausalConfidence.None)
        {
            throw new ArgumentException(
                "Rejected correlation cannot claim causal confidence.",
                nameof(causalConfidence));
        }
        if (disposition != OutcomeCorrelationDisposition.Rejected
            && expectation == null)
        {
            throw new ArgumentNullException(nameof(expectation));
        }
        if (observation != null
            && expectation != null
            && observation.DecisionId != expectation.DecisionId)
        {
            throw new ArgumentException(
                "Observation and expectation must reference the same decision.",
                nameof(observation));
        }
        if (disposition == OutcomeCorrelationDisposition.AcceptedProgress
            && causalConfidence != CausalConfidence.None)
        {
            throw new ArgumentException(
                "Progress is not a terminal causal appraisal.",
                nameof(causalConfidence));
        }
        if (disposition == OutcomeCorrelationDisposition.AcceptedTerminal
            && causalConfidence == CausalConfidence.None)
        {
            throw new ArgumentException(
                "Accepted terminal result must expose bounded causal confidence.",
                nameof(causalConfidence));
        }

        Disposition = disposition;
        Reason = reason;
        SignalId = DecisionCoreContractGuard.Identifier(
            signalId,
            128,
            nameof(signalId));
        Expectation = expectation;
        Observation = observation;
        CausalConfidence = causalConfidence;
    }
}
