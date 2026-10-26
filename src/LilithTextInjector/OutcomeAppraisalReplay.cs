using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum OutcomeReplayOperationKind
{
    RegisterExpectation = 0,
    ObserveSignal = 1
}

internal sealed class OutcomeReplayOperation
{
    internal string Id { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal OutcomeReplayOperationKind Kind { get; }
    internal OutcomeExpectation? Expectation { get; }
    internal OutcomeSignal? Signal { get; }

    private OutcomeReplayOperation(
        string id,
        long sequence,
        DateTimeOffset occurredAt,
        OutcomeReplayOperationKind kind,
        OutcomeExpectation? expectation,
        OutcomeSignal? signal)
    {
        Id = DecisionCoreContractGuard.Identifier(id, 128, nameof(id));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        Kind = kind;
        Expectation = expectation;
        Signal = signal;
    }

    internal static OutcomeReplayOperation Register(
        string id,
        long sequence,
        OutcomeExpectation expectation)
    {
        if (expectation == null)
            throw new ArgumentNullException(nameof(expectation));
        return new OutcomeReplayOperation(
            id,
            sequence,
            expectation.RegisteredAt,
            OutcomeReplayOperationKind.RegisterExpectation,
            expectation,
            signal: null);
    }

    internal static OutcomeReplayOperation Observe(
        string id,
        long sequence,
        OutcomeSignal signal)
    {
        if (signal == null)
            throw new ArgumentNullException(nameof(signal));
        return new OutcomeReplayOperation(
            id,
            sequence,
            signal.ObservedAt,
            OutcomeReplayOperationKind.ObserveSignal,
            expectation: null,
            signal);
    }
}

internal sealed class OutcomeAppraisalReplayStepResult
{
    internal string OperationId { get; }
    internal OutcomeCorrelationResult? Correlation { get; }
    internal OutcomeAppraisal? Appraisal { get; }
    internal OutcomeCorrelationLedger Ledger { get; }

    internal OutcomeAppraisalReplayStepResult(
        string operationId,
        OutcomeCorrelationResult? correlation,
        OutcomeAppraisal? appraisal,
        OutcomeCorrelationLedger ledger)
    {
        OperationId = DecisionCoreContractGuard.Identifier(
            operationId,
            128,
            nameof(operationId));
        if (appraisal != null
            && (correlation == null
                || correlation.Disposition
                    != OutcomeCorrelationDisposition.AcceptedTerminal))
        {
            throw new ArgumentException(
                "Appraisal requires an accepted terminal correlation.",
                nameof(appraisal));
        }
        Correlation = correlation;
        Appraisal = appraisal;
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }
}

internal sealed class OutcomeAppraisalReplayResult
{
    internal OutcomeCorrelationLedger FinalLedger { get; }
    internal IReadOnlyList<OutcomeAppraisalReplayStepResult> Steps { get; }

    internal OutcomeAppraisalReplayResult(
        OutcomeCorrelationLedger finalLedger,
        IReadOnlyList<OutcomeAppraisalReplayStepResult> steps)
    {
        FinalLedger = finalLedger
            ?? throw new ArgumentNullException(nameof(finalLedger));
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));

        var copy = new OutcomeAppraisalReplayStepResult[steps.Count];
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

internal static class OutcomeAppraisalReplay
{
    internal const int MaximumOperations = 256;

    internal static OutcomeAppraisalReplayResult Run(
        IReadOnlyList<OutcomeReplayOperation> operations)
    {
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));
        if (operations.Count == 0 || operations.Count > MaximumOperations)
            throw new ArgumentOutOfRangeException(nameof(operations));

        var ledger = OutcomeCorrelationLedger.Empty;
        var results = new OutcomeAppraisalReplayStepResult[operations.Count];
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? previousTime = null;

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index]
                ?? throw new ArgumentException(
                    "Replay cannot contain a null operation.",
                    nameof(operations));
            if (operation.Sequence != index + 1)
            {
                throw new ArgumentException(
                    "Replay operation sequence must be consecutive.",
                    nameof(operations));
            }
            if (!operationIds.Add(operation.Id))
            {
                throw new ArgumentException(
                    "Replay operation IDs must be unique.",
                    nameof(operations));
            }
            if (previousTime.HasValue
                && operation.OccurredAt < previousTime.Value)
            {
                throw new ArgumentException(
                    "Replay operations must be in timestamp order.",
                    nameof(operations));
            }

            OutcomeCorrelationResult? correlation = null;
            OutcomeAppraisal? appraisal = null;
            switch (operation.Kind)
            {
                case OutcomeReplayOperationKind.RegisterExpectation:
                    ledger = ledger.Register(
                        operation.Expectation
                        ?? throw new InvalidOperationException(
                            "Registration operation has no expectation."));
                    break;
                case OutcomeReplayOperationKind.ObserveSignal:
                    var update = ledger.Correlate(
                        operation.Signal
                        ?? throw new InvalidOperationException(
                            "Observation operation has no signal."));
                    ledger = update.Ledger;
                    correlation = update.Result;
                    appraisal = OutcomeAppraiser.Appraise(correlation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operations),
                        "Unknown outcome replay operation.");
            }

            results[index] = new OutcomeAppraisalReplayStepResult(
                operation.Id,
                correlation,
                appraisal,
                ledger);
            previousTime = operation.OccurredAt;
        }

        return new OutcomeAppraisalReplayResult(
            ledger,
            Array.AsReadOnly(results));
    }
}
