using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class DecisionCoreReplayStep
{
    internal SituationEvent Event { get; }
    internal Guid SnapshotId { get; }
    internal long SnapshotSequence { get; }
    internal TimeSpan SnapshotValidity { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal DecisionBudgets Budgets { get; }
    internal IReadOnlyList<EvidenceTerminalState> EvidenceTerminalStates { get; }

    internal DecisionCoreReplayStep(
        SituationEvent situationEvent,
        Guid snapshotId,
        long snapshotSequence,
        TimeSpan snapshotValidity,
        DateTimeOffset evaluatedAt,
        DecisionBudgets? budgets = null,
        IReadOnlyList<EvidenceTerminalState>? evidenceTerminalStates = null)
    {
        Event = situationEvent ?? throw new ArgumentNullException(nameof(situationEvent));
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot ID cannot be empty.", nameof(snapshotId));
        if (snapshotSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotSequence));
        if (snapshotValidity <= TimeSpan.Zero
            || snapshotValidity > SituationSnapshotAssembler.MaximumValidity)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotValidity));
        }
        var normalizedEvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (normalizedEvaluatedAt < situationEvent.OccurredAt)
            throw new ArgumentOutOfRangeException(nameof(evaluatedAt));

        SnapshotId = snapshotId;
        SnapshotSequence = snapshotSequence;
        SnapshotValidity = snapshotValidity;
        EvaluatedAt = normalizedEvaluatedAt;
        Budgets = budgets ?? DecisionBudgets.ShadowDefault;
        var sourceTerminalStates =
            evidenceTerminalStates ?? Array.Empty<EvidenceTerminalState>();
        var copiedTerminalStates =
            new EvidenceTerminalState[sourceTerminalStates.Count];
        for (var index = 0; index < sourceTerminalStates.Count; index++)
        {
            copiedTerminalStates[index] = sourceTerminalStates[index]
                ?? throw new ArgumentException(
                    "Replay terminal states cannot contain null.",
                    nameof(evidenceTerminalStates));
        }
        EvidenceTerminalStates = Array.AsReadOnly(copiedTerminalStates);
    }
}

internal sealed class DecisionCoreReplayStepResult
{
    internal string EventId { get; }
    internal SituationProjection Projection { get; }
    internal SituationSnapshot Snapshot { get; }
    internal DecisionEvaluation Evaluation { get; }

    internal DecisionCoreReplayStepResult(
        SituationEvent situationEvent,
        SituationProjection projection,
        SituationSnapshot snapshot,
        DecisionEvaluation evaluation)
    {
        if (situationEvent == null)
            throw new ArgumentNullException(nameof(situationEvent));
        EventId = situationEvent.Id;
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
    }
}

internal sealed class DecisionCoreReplayResult
{
    internal string SessionId { get; }
    internal SituationProjection FinalProjection { get; }
    internal IReadOnlyList<DecisionCoreReplayStepResult> Steps { get; }

    internal DecisionCoreReplayResult(
        string sessionId,
        SituationProjection finalProjection,
        IReadOnlyList<DecisionCoreReplayStepResult> steps)
    {
        SessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        FinalProjection = finalProjection
            ?? throw new ArgumentNullException(nameof(finalProjection));
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));

        var copy = new DecisionCoreReplayStepResult[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            copy[index] = steps[index]
                ?? throw new ArgumentException(
                    "Replay result cannot contain a null step.",
                    nameof(steps));
        }
        Steps = Array.AsReadOnly(copy);
    }
}

internal static class DecisionCoreReplay
{
    internal const int MaximumSteps = 512;

    internal static DecisionCoreReplayResult Run(
        string sessionId,
        DateTimeOffset initialAsOf,
        IReadOnlyList<DecisionCoreReplayStep> steps)
    {
        var normalizedSessionId = DecisionCoreContractGuard.Identifier(
            sessionId,
            128,
            nameof(sessionId));
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));
        if (steps.Count == 0 || steps.Count > MaximumSteps)
            throw new ArgumentOutOfRangeException(nameof(steps));

        var projection = SituationProjection.Create(
            normalizedSessionId,
            initialAsOf);
        var results = new DecisionCoreReplayStepResult[steps.Count];
        var snapshotIds = new HashSet<Guid>();
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        long previousSnapshotSequence = 0;

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index]
                ?? throw new ArgumentException(
                    "Replay cannot contain a null step.",
                    nameof(steps));
            if (!string.Equals(
                    step.Event.SessionId,
                    normalizedSessionId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Replay event belongs to another session.",
                    nameof(steps));
            }
            if (step.SnapshotSequence <= previousSnapshotSequence)
            {
                throw new ArgumentException(
                    "Snapshot sequence must increase strictly.",
                    nameof(steps));
            }
            if (!snapshotIds.Add(step.SnapshotId))
            {
                throw new ArgumentException(
                    "Snapshot IDs must be unique within a replay.",
                    nameof(steps));
            }
            if (!eventIds.Add(step.Event.Id))
            {
                throw new ArgumentException(
                    "Event IDs must be unique within a replay.",
                    nameof(steps));
            }

            projection = SituationEventReducer.Apply(projection, step.Event);
            var snapshot = SituationSnapshotAssembler.Assemble(
                projection,
                step.SnapshotId,
                step.SnapshotSequence,
                step.SnapshotValidity,
                step.Budgets,
                step.EvidenceTerminalStates);
            var evaluation = LocalDecisionCore.Evaluate(
                snapshot,
                step.EvaluatedAt);
            results[index] = new DecisionCoreReplayStepResult(
                step.Event,
                projection,
                snapshot,
                evaluation);
            previousSnapshotSequence = step.SnapshotSequence;
        }

        return new DecisionCoreReplayResult(
            normalizedSessionId,
            projection,
            Array.AsReadOnly(results));
    }
}
