using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class OutcomeLedgerEntry
{
    internal OutcomeExpectation Expectation { get; }
    internal int SignalCount { get; }
    internal bool IsTerminal { get; }
    internal bool IsAbandoned { get; }
    internal OutcomeStatus LastStatus { get; }
    internal DateTimeOffset? LastObservedAt { get; }
    internal DateTimeOffset? AbandonedAt { get; }
    internal bool IsPending => !IsTerminal && !IsAbandoned;

    internal OutcomeLedgerEntry(
        OutcomeExpectation expectation,
        int signalCount,
        bool isTerminal,
        bool isAbandoned,
        OutcomeStatus lastStatus,
        DateTimeOffset? lastObservedAt,
        DateTimeOffset? abandonedAt)
    {
        Expectation = expectation ?? throw new ArgumentNullException(nameof(expectation));
        if (signalCount < 0 || signalCount > OutcomeCorrelationLedger.MaximumSignalsPerExpectation)
            throw new ArgumentOutOfRangeException(nameof(signalCount));
        if (!Enum.IsDefined(typeof(OutcomeStatus), lastStatus))
            throw new ArgumentOutOfRangeException(nameof(lastStatus));
        if (signalCount == 0
            && (lastStatus != OutcomeStatus.Unknown || lastObservedAt.HasValue))
        {
            throw new ArgumentException("An untouched expectation cannot have an outcome.");
        }
        if (signalCount > 0
            && (!lastObservedAt.HasValue || lastStatus == OutcomeStatus.Unknown))
        {
            throw new ArgumentException("A correlated signal requires status and time.");
        }
        if (isTerminal && isAbandoned)
            throw new ArgumentException("A terminal expectation cannot be abandoned.");
        if (isAbandoned != abandonedAt.HasValue)
            throw new ArgumentException("Abandoned state requires exactly one timestamp.");
        if (abandonedAt.HasValue
            && abandonedAt.Value < expectation.RegisteredAt)
        {
            throw new ArgumentOutOfRangeException(nameof(abandonedAt));
        }
        if (abandonedAt.HasValue
            && lastObservedAt.HasValue
            && abandonedAt.Value < lastObservedAt.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(abandonedAt));
        }

        SignalCount = signalCount;
        IsTerminal = isTerminal;
        IsAbandoned = isAbandoned;
        LastStatus = lastStatus;
        LastObservedAt = lastObservedAt;
        AbandonedAt = abandonedAt;
    }

    internal static OutcomeLedgerEntry Create(OutcomeExpectation expectation)
        => new(
            expectation,
            signalCount: 0,
            isTerminal: false,
            isAbandoned: false,
            OutcomeStatus.Unknown,
            lastObservedAt: null,
            abandonedAt: null);

    internal OutcomeLedgerEntry WithSignal(OutcomeSignal signal)
        => new(
            Expectation,
            SignalCount + 1,
            signal.IsTerminal,
            isAbandoned: false,
            signal.Status,
            signal.ObservedAt,
            abandonedAt: null);

    internal OutcomeLedgerEntry Abandon(DateTimeOffset abandonedAt)
    {
        if (!IsPending)
            return this;
        return new OutcomeLedgerEntry(
            Expectation,
            SignalCount,
            isTerminal: false,
            isAbandoned: true,
            LastStatus,
            LastObservedAt,
            DecisionCoreContractGuard.Timestamp(
                abandonedAt,
                nameof(abandonedAt)));
    }
}

internal sealed class OutcomeCorrelationLedger
{
    internal const int MaximumExpectations = 64;
    internal const int MaximumSignalsPerExpectation = 16;

    internal static OutcomeCorrelationLedger Empty { get; } =
        new(Array.Empty<OutcomeLedgerEntry>());

    internal IReadOnlyList<OutcomeLedgerEntry> Entries { get; }

    internal int PendingExpectationCount
    {
        get
        {
            var count = 0;
            foreach (var entry in Entries)
            {
                if (entry.IsPending)
                    count++;
            }
            return count;
        }
    }

    private OutcomeCorrelationLedger(IReadOnlyList<OutcomeLedgerEntry> entries)
    {
        if (entries == null)
            throw new ArgumentNullException(nameof(entries));
        if (entries.Count > MaximumExpectations)
            throw new ArgumentOutOfRangeException(nameof(entries));

        var copy = new OutcomeLedgerEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            copy[index] = entries[index]
                ?? throw new ArgumentException(
                    "Outcome ledger cannot contain null.",
                    nameof(entries));
        }
        Entries = Array.AsReadOnly(copy);
    }

    internal OutcomeCorrelationLedger Register(OutcomeExpectation expectation)
    {
        if (expectation == null)
            throw new ArgumentNullException(nameof(expectation));
        if (Entries.Count >= MaximumExpectations)
            throw new InvalidOperationException("Outcome expectation limit reached.");

        foreach (var entry in Entries)
        {
            if (entry.Expectation.DecisionId == expectation.DecisionId)
            {
                throw new InvalidOperationException(
                    "Decision already has an outcome expectation.");
            }
            if (string.Equals(
                    entry.Expectation.CorrelationId,
                    expectation.CorrelationId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Correlation ID is already registered.");
            }
        }

        var copy = new OutcomeLedgerEntry[Entries.Count + 1];
        for (var index = 0; index < Entries.Count; index++)
            copy[index] = Entries[index];
        copy[Entries.Count] = OutcomeLedgerEntry.Create(expectation);
        Array.Sort(
            copy,
            (left, right) =>
                left.Expectation.DecisionId.CompareTo(right.Expectation.DecisionId));
        return new OutcomeCorrelationLedger(Array.AsReadOnly(copy));
    }

    internal OutcomeCorrelationUpdate Correlate(OutcomeSignal signal)
    {
        if (signal == null)
            throw new ArgumentNullException(nameof(signal));

        var entryIndex = FindDecision(signal.DecisionId);
        if (entryIndex < 0)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.UnknownDecision,
                expectation: null);
        }
        var entry = Entries[entryIndex];
        var expectation = entry.Expectation;
        if (!string.Equals(
                signal.CorrelationId,
                expectation.CorrelationId,
                StringComparison.Ordinal))
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.CorrelationMismatch,
                expectation);
        }
        if (signal.ObservedAt < expectation.RegisteredAt)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.BeforeObservationWindow,
                expectation);
        }
        if (signal.ObservedAt > expectation.ObserveUntil)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.AfterObservationWindow,
                expectation);
        }
        if (!IsSourceAllowed(expectation.Kind, signal.Source))
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.SourceNotAllowed,
                expectation);
        }
        if (!IsStatusAllowed(expectation.Kind, signal.Status))
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.StatusNotAllowed,
                expectation);
        }
        if (entry.IsTerminal)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.AlreadyTerminal,
                expectation);
        }
        if (entry.IsAbandoned)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.ExpectationAbandoned,
                expectation);
        }
        if (entry.SignalCount >= MaximumSignalsPerExpectation)
        {
            return Rejected(
                signal,
                OutcomeCorrelationReason.SignalLimitReached,
                expectation);
        }

        var updatedEntry = entry.WithSignal(signal);
        var updatedLedger = Replace(entryIndex, updatedEntry);
        if (!signal.IsTerminal)
        {
            var progress = new OutcomeCorrelationResult(
                OutcomeCorrelationDisposition.AcceptedProgress,
                OutcomeCorrelationReason.ProgressAccepted,
                signal.Id,
                expectation,
                observation: null,
                CausalConfidence.None);
            return new OutcomeCorrelationUpdate(updatedLedger, progress);
        }

        var confidence = EvaluateCausalConfidence(signal);
        var observation = new OutcomeObservation(
            signal.DecisionId,
            signal.Status,
            signal.ObservedAt,
            signal.HasDirectExecutionCorrelation,
            signal.Confounders != OutcomeConfounder.None);
        var terminal = new OutcomeCorrelationResult(
            OutcomeCorrelationDisposition.AcceptedTerminal,
            OutcomeCorrelationReason.TerminalAccepted,
            signal.Id,
            expectation,
            observation,
            confidence);
        return new OutcomeCorrelationUpdate(updatedLedger, terminal);
    }

    internal OutcomeCorrelationLedger AbandonPending(
        DateTimeOffset abandonedAt)
    {
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            abandonedAt,
            nameof(abandonedAt));
        if (PendingExpectationCount == 0)
            return this;

        var copy = new OutcomeLedgerEntry[Entries.Count];
        for (var index = 0; index < Entries.Count; index++)
            copy[index] = Entries[index].Abandon(normalizedAt);
        return new OutcomeCorrelationLedger(Array.AsReadOnly(copy));
    }

    private int FindDecision(Guid decisionId)
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].Expectation.DecisionId == decisionId)
                return index;
        }
        return -1;
    }

    private OutcomeCorrelationLedger Replace(
        int index,
        OutcomeLedgerEntry replacement)
    {
        var copy = new OutcomeLedgerEntry[Entries.Count];
        for (var current = 0; current < Entries.Count; current++)
        {
            copy[current] = current == index
                ? replacement
                : Entries[current];
        }
        return new OutcomeCorrelationLedger(Array.AsReadOnly(copy));
    }

    private OutcomeCorrelationUpdate Rejected(
        OutcomeSignal signal,
        OutcomeCorrelationReason reason,
        OutcomeExpectation? expectation)
    {
        var result = new OutcomeCorrelationResult(
            OutcomeCorrelationDisposition.Rejected,
            reason,
            signal.Id,
            expectation,
            observation: null,
            CausalConfidence.None);
        return new OutcomeCorrelationUpdate(this, result);
    }

    private static bool IsSourceAllowed(
        OutcomeExpectationKind kind,
        OutcomeSignalSource source)
        => kind switch
        {
            OutcomeExpectationKind.ResponseDelivery =>
                source is OutcomeSignalSource.ResponseSurface
                    or OutcomeSignalSource.OutcomeVerifier,
            OutcomeExpectationKind.ConsentDecision =>
                source is OutcomeSignalSource.Consent
                    or OutcomeSignalSource.OutcomeVerifier,
            OutcomeExpectationKind.CapabilityExecution =>
                source is OutcomeSignalSource.Policy
                    or OutcomeSignalSource.Consent
                    or OutcomeSignalSource.CapabilityExecutor
                    or OutcomeSignalSource.OutcomeVerifier,
            OutcomeExpectationKind.AvatarTransition =>
                source is OutcomeSignalSource.CapabilityExecutor
                    or OutcomeSignalSource.OutcomeVerifier,
            _ => false
        };

    private static bool IsStatusAllowed(
        OutcomeExpectationKind kind,
        OutcomeStatus status)
        => kind switch
        {
            OutcomeExpectationKind.ResponseDelivery =>
                status is OutcomeStatus.Started
                    or OutcomeStatus.Completed
                    or OutcomeStatus.Interrupted
                    or OutcomeStatus.Failed
                    or OutcomeStatus.TimedOut,
            OutcomeExpectationKind.ConsentDecision =>
                status is OutcomeStatus.ConsentPending
                    or OutcomeStatus.Rejected
                    or OutcomeStatus.Completed
                    or OutcomeStatus.TimedOut,
            OutcomeExpectationKind.CapabilityExecution
                or OutcomeExpectationKind.AvatarTransition =>
                status is OutcomeStatus.ConsentPending
                    or OutcomeStatus.Rejected
                    or OutcomeStatus.Started
                    or OutcomeStatus.Completed
                    or OutcomeStatus.Interrupted
                    or OutcomeStatus.Failed
                    or OutcomeStatus.TimedOut,
            _ => false
        };

    private static CausalConfidence EvaluateCausalConfidence(
        OutcomeSignal signal)
    {
        if (!signal.HasDirectExecutionCorrelation)
            return CausalConfidence.Low;
        return signal.Confounders == OutcomeConfounder.None
            ? CausalConfidence.High
            : CausalConfidence.Low;
    }
}

internal sealed class OutcomeCorrelationUpdate
{
    internal OutcomeCorrelationLedger Ledger { get; }
    internal OutcomeCorrelationResult Result { get; }

    internal OutcomeCorrelationUpdate(
        OutcomeCorrelationLedger ledger,
        OutcomeCorrelationResult result)
    {
        Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}
