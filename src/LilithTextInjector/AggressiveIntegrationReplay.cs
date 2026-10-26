using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithTextInjector;

internal sealed class D029IntegrationReplayRecord
{
    internal D029IntegrationStep Step { get; }
    internal int Sequence { get; }
    internal D029IntegrationRunState State { get; }
    internal D029IntegrationStopReason StopReason { get; }
    internal string ObservationFingerprint { get; }
    internal string Fingerprint { get; }

    internal D029IntegrationReplayRecord(
        D029IntegrationStepObservation observation,
        D029IntegrationRunState state,
        D029IntegrationStopReason stopReason)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        if (!Enum.IsDefined(typeof(D029IntegrationRunState), state)
            || state == D029IntegrationRunState.Invalid
            || state == D029IntegrationRunState.Off
            || state == D029IntegrationRunState.Armed)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        if (!Enum.IsDefined(
                typeof(D029IntegrationStopReason),
                stopReason))
        {
            throw new ArgumentOutOfRangeException(nameof(stopReason));
        }
        if ((state == D029IntegrationRunState.Stopped
                || state == D029IntegrationRunState.Fault
                || state == D029IntegrationRunState.Capacity)
            != (stopReason != D029IntegrationStopReason.None))
        {
            throw new ArgumentException(
                "Terminal failure state and stop reason must agree.");
        }
        Step = observation.Step;
        Sequence = observation.Sequence;
        State = state;
        StopReason = stopReason;
        ObservationFingerprint = observation.Fingerprint;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/REPLAY-RECORD/1",
            ProviderRegistryCanonicalHash.Number((int)Step),
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Number((int)State),
            ProviderRegistryCanonicalHash.Number((int)StopReason),
            ObservationFingerprint);
    }
}

internal sealed class D029IntegrationReplayResult
{
    internal D029IntegrationRunState State { get; }
    internal D029IntegrationStopReason StopReason { get; }
    internal int AcceptedSteps { get; }
    internal int ProviderCalls { get; }
    internal IReadOnlyList<D029IntegrationReplayRecord> Records { get; }
    internal D029IntegrationAuthorityCounters AuthorityOutputs =>
        D029IntegrationAuthorityCounters.Zero;
    internal string DeterministicFingerprint { get; }

    internal D029IntegrationReplayResult(
        D029IntegrationRunState state,
        D029IntegrationStopReason stopReason,
        int acceptedSteps,
        int providerCalls,
        IReadOnlyList<D029IntegrationReplayRecord> records)
    {
        State = state;
        StopReason = stopReason;
        AcceptedSteps = acceptedSteps;
        ProviderCalls = providerCalls;
        Records = new ReadOnlyCollection<D029IntegrationReplayRecord>(
            records == null
                ? throw new ArgumentNullException(nameof(records))
                : new List<D029IntegrationReplayRecord>(records));
        DeterministicFingerprint = ProviderRegistryCanonicalHash.Of(
            "D029/REPLAY-RESULT/1",
            ProviderRegistryCanonicalHash.Number((int)State),
            ProviderRegistryCanonicalHash.Number((int)StopReason),
            ProviderRegistryCanonicalHash.Number(AcceptedSteps),
            ProviderRegistryCanonicalHash.Number(ProviderCalls),
            ProviderRegistryCanonicalHash.Of(
                "D029/REPLAY-RECORDS/1",
                new List<D029IntegrationReplayRecord>(Records)
                    .ConvertAll(item => item.Fingerprint)
                    .ToArray()),
            AuthorityOutputs.Fingerprint);
    }
}

internal static class D029AggressiveIntegrationReplay
{
    internal static D029IntegrationReplayResult Run(
        D029IntegrationPlan plan,
        D029IntegrationRunIdentity run,
        IReadOnlyList<D029IntegrationStepObservation> observations)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (run == null)
            throw new ArgumentNullException(nameof(run));
        if (observations == null)
            throw new ArgumentNullException(nameof(observations));
        if (observations.Count > D029IntegrationCeilings.MaximumSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(observations));
        }

        var records = new List<D029IntegrationReplayRecord>();
        var seen = new HashSet<D029IntegrationStep>();
        var accepted = 0;
        var providerCalls = 0;
        var state = D029IntegrationRunState.Running;
        var stopReason = D029IntegrationStopReason.None;

        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index]
                ?? throw new ArgumentException(
                    "Observations cannot contain null.",
                    nameof(observations));
            var reason = Evaluate(
                plan,
                run,
                observation,
                index + 1,
                seen,
                providerCalls);
            if (reason != D029IntegrationStopReason.None)
            {
                state = reason
                    == D029IntegrationStopReason
                        .CrashOrUnhandledFault
                    ? D029IntegrationRunState.Fault
                    : reason
                            == D029IntegrationStopReason.CapacityAnomaly
                        ? D029IntegrationRunState.Capacity
                        : D029IntegrationRunState.Stopped;
                stopReason = reason;
                records.Add(
                    new D029IntegrationReplayRecord(
                        observation,
                        state,
                        reason));
                break;
            }

            seen.Add(observation.Step);
            accepted++;
            providerCalls += observation.Counters.ProviderCalls;
            var acceptedState =
                accepted == D029IntegrationCeilings.MaximumSteps
                    ? D029IntegrationRunState.Completed
                    : D029IntegrationRunState.Running;
            records.Add(
                new D029IntegrationReplayRecord(
                    observation,
                    acceptedState,
                    D029IntegrationStopReason.None));
            state = acceptedState;
        }

        return new D029IntegrationReplayResult(
            state,
            stopReason,
            accepted,
            providerCalls,
            records);
    }

    private static D029IntegrationStopReason Evaluate(
        D029IntegrationPlan plan,
        D029IntegrationRunIdentity run,
        D029IntegrationStepObservation observation,
        int expectedSequence,
        HashSet<D029IntegrationStep> seen,
        int providerCalls)
    {
        if (!run.Matches(observation.Run))
            return D029IntegrationStopReason.RunIdentityMismatch;
        if (observation.Sequence != expectedSequence
            || (int)observation.Step != expectedSequence)
        {
            return D029IntegrationStopReason.InvalidSequence;
        }
        if (!seen.Add(observation.Step))
            return D029IntegrationStopReason.DuplicateStep;
        seen.Remove(observation.Step);
        if (observation.ObservedAt < run.StartedAt)
            return D029IntegrationStopReason.ObservationNotYetValid;
        if (observation.ObservedAt >= run.ExpiresAt)
            return D029IntegrationStopReason.ObservationExpired;
        if (observation.CrashOrUnhandledFault)
            return D029IntegrationStopReason.CrashOrUnhandledFault;
        if (observation.DuplicateOwnership)
            return D029IntegrationStopReason.DuplicateOwnership;
        if (observation.DuplicateOutput)
            return D029IntegrationStopReason.DuplicateOutput;
        if (observation.WrongWriter)
            return D029IntegrationStopReason.WrongWriter;
        if (observation.StaleOrLateAdmission)
            return D029IntegrationStopReason.StaleOrLateAdmission;
        if (observation.CancellationFailure)
            return D029IntegrationStopReason.CancellationFailure;
        if (observation.CapacityAnomaly
            || observation.Counters.MaximumConcurrentProviders
                > D029IntegrationCeilings
                    .MaximumConcurrentProviderCalls
            || observation.Counters.MaximumConcurrentProactive
                > D029IntegrationCeilings
                    .MaximumConcurrentProactiveOccurrences
            || observation.Counters.MaximumConcurrentMotion
                > D029IntegrationCeilings
                    .MaximumConcurrentMotionAssists
            || observation.Counters.DiagnosticRecordCount
                > D029IntegrationCeilings.MaximumDiagnosticRecords
            || providerCalls + observation.Counters.ProviderCalls
                > D029IntegrationCeilings
                    .MaximumProviderCallsPerRun)
        {
            return D029IntegrationStopReason.CapacityAnomaly;
        }
        if (observation.Counters.UnexpectedAuthorityOutputs != 0)
            return D029IntegrationStopReason.AuthorityBoundaryCrossed;
        if (observation.Counters.UnexpectedPersistenceWrites != 0)
        {
            return D029IntegrationStopReason
                .UnexpectedPersistenceMutation;
        }
        return plan.For(observation.Step)
                .Matches(observation.Counters)
            ? D029IntegrationStopReason.None
            : D029IntegrationStopReason.CounterMismatch;
    }
}
