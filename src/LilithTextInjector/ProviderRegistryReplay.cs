using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithTextInjector;

internal enum ProviderRegistryReplayOperationKind
{
    Invalid = 0,
    ReplaceSnapshot = 1,
    AdmitRoute = 2
}

internal enum ProviderRegistryTransitionAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    RegistryVersionMismatch = 2,
    RegistryGenerationMismatch = 3,
    RegistryGenerationExhausted = 4,
    NewInstanceConfigurationGenerationMismatch = 5,
    UnchangedInstanceConfigurationGenerationMismatch = 6,
    ChangedInstanceConfigurationGenerationMismatch = 7,
    InstanceConfigurationGenerationExhausted = 8
}

internal sealed class ProviderRegistryStageAAuthorityCounters
{
    internal static ProviderRegistryStageAAuthorityCounters Zero { get; } =
        new();

    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int MemoryWrites => 0;
    internal int DecisionOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int ExecutionOutputs => 0;
    internal int CredentialLeaseResolutions => 0;
    internal int ModelTransportLeaseResolutions => 0;
    internal int PersistenceWrites => 0;
    internal int Total => 0;
    internal bool IsZero => true;
    internal string Fingerprint { get; } =
        ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-REGISTRY-STAGE-A-AUTHORITY/1",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0");

    private ProviderRegistryStageAAuthorityCounters()
    {
    }
}

internal sealed class ProviderRegistryTransitionResult
{
    internal ProviderRegistryTransitionAdmission Admission { get; }
    internal ProviderRegistrySnapshot? AcceptedSnapshot { get; }
    internal bool Accepted =>
        Admission
            == ProviderRegistryTransitionAdmission
                .AcceptedForContractReplay;
    internal bool AllowsProviderCall => false;
    internal ProviderRegistryStageAAuthorityCounters AuthorityCounters =>
        ProviderRegistryStageAAuthorityCounters.Zero;

    private ProviderRegistryTransitionResult(
        ProviderRegistryTransitionAdmission admission,
        ProviderRegistrySnapshot? acceptedSnapshot)
    {
        if (!Enum.IsDefined(
                typeof(ProviderRegistryTransitionAdmission),
                admission)
            || admission == ProviderRegistryTransitionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderRegistryTransitionAdmission
                    .AcceptedForContractReplay)
            != (acceptedSnapshot != null))
        {
            throw new ArgumentException(
                "Only an accepted transition may carry a snapshot.",
                nameof(acceptedSnapshot));
        }

        Admission = admission;
        AcceptedSnapshot = acceptedSnapshot;
    }

    internal static ProviderRegistryTransitionResult Accept(
        ProviderRegistrySnapshot snapshot)
        => new(
            ProviderRegistryTransitionAdmission
                .AcceptedForContractReplay,
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    internal static ProviderRegistryTransitionResult Reject(
        ProviderRegistryTransitionAdmission admission)
    {
        if (admission
                == ProviderRegistryTransitionAdmission
                    .AcceptedForContractReplay
            || admission == ProviderRegistryTransitionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        return new ProviderRegistryTransitionResult(admission, null);
    }
}

internal static class ProviderRegistryTransitionPolicy
{
    internal static ProviderRegistryTransitionResult Evaluate(
        ProviderRegistrySnapshot currentSnapshot,
        ProviderRegistrySnapshot candidateSnapshot)
    {
        if (currentSnapshot == null)
            throw new ArgumentNullException(nameof(currentSnapshot));
        if (candidateSnapshot == null)
            throw new ArgumentNullException(nameof(candidateSnapshot));

        if (candidateSnapshot.RegistryVersion
            != currentSnapshot.RegistryVersion)
        {
            return ProviderRegistryTransitionResult.Reject(
                ProviderRegistryTransitionAdmission
                    .RegistryVersionMismatch);
        }
        if (currentSnapshot.RegistryGeneration == long.MaxValue)
        {
            return ProviderRegistryTransitionResult.Reject(
                ProviderRegistryTransitionAdmission
                    .RegistryGenerationExhausted);
        }
        if (candidateSnapshot.RegistryGeneration
            != currentSnapshot.RegistryGeneration + 1)
        {
            return ProviderRegistryTransitionResult.Reject(
                ProviderRegistryTransitionAdmission
                    .RegistryGenerationMismatch);
        }

        var currentInstances =
            new Dictionary<string, ProviderEndpointInstance>(
                StringComparer.Ordinal);
        foreach (var instance in currentSnapshot.Instances)
            currentInstances.Add(instance.InstanceId.Value, instance);

        foreach (var candidate in candidateSnapshot.Instances)
        {
            if (!currentInstances.TryGetValue(
                    candidate.InstanceId.Value,
                    out var current))
            {
                if (candidate.ConfigurationGeneration != 1)
                {
                    return ProviderRegistryTransitionResult.Reject(
                        ProviderRegistryTransitionAdmission
                            .NewInstanceConfigurationGenerationMismatch);
                }
                continue;
            }

            var configurationUnchanged = string.Equals(
                current.ConfigurationFingerprint,
                candidate.ConfigurationFingerprint,
                StringComparison.Ordinal);
            if (configurationUnchanged)
            {
                if (candidate.ConfigurationGeneration
                    != current.ConfigurationGeneration)
                {
                    return ProviderRegistryTransitionResult.Reject(
                        ProviderRegistryTransitionAdmission
                            .UnchangedInstanceConfigurationGenerationMismatch);
                }
                continue;
            }

            if (current.ConfigurationGeneration == long.MaxValue)
            {
                return ProviderRegistryTransitionResult.Reject(
                    ProviderRegistryTransitionAdmission
                        .InstanceConfigurationGenerationExhausted);
            }
            if (candidate.ConfigurationGeneration
                != current.ConfigurationGeneration + 1)
            {
                return ProviderRegistryTransitionResult.Reject(
                    ProviderRegistryTransitionAdmission
                        .ChangedInstanceConfigurationGenerationMismatch);
            }
        }

        return ProviderRegistryTransitionResult.Accept(candidateSnapshot);
    }
}

internal abstract class ProviderRegistryReplayOperation
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal ProviderRegistryReplayOperationKind Kind { get; }
    internal abstract string Fingerprint { get; }

    protected ProviderRegistryReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        ProviderRegistryReplayOperationKind kind)
    {
        OperationId = ProviderRegistryContractGuard.Identifier(
            operationId,
            nameof(operationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (!Enum.IsDefined(
                typeof(ProviderRegistryReplayOperationKind),
                kind)
            || kind == ProviderRegistryReplayOperationKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Sequence = sequence;
        Kind = kind;
    }
}

internal sealed class ProviderRegistryReplaceSnapshotOperation :
    ProviderRegistryReplayOperation
{
    internal ProviderRegistrySnapshot CandidateSnapshot { get; }
    internal override string Fingerprint { get; }

    internal ProviderRegistryReplaceSnapshotOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        ProviderRegistrySnapshot candidateSnapshot)
        : base(
            operationId,
            sequence,
            occurredAt,
            ProviderRegistryReplayOperationKind.ReplaceSnapshot)
    {
        CandidateSnapshot = candidateSnapshot
            ?? throw new ArgumentNullException(nameof(candidateSnapshot));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-REGISTRY-REPLACE-OPERATION/1",
            OperationId,
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Timestamp(OccurredAt),
            CandidateSnapshot.Fingerprint);
    }
}

internal sealed class ProviderRouteAdmissionReplayOperation :
    ProviderRegistryReplayOperation
{
    internal ProviderRouteSelection Selection { get; }
    internal ProviderEndpointNetworkEvidence EndpointEvidence { get; }
    internal string ActiveProcessingPolicyFingerprint { get; }
    internal override string Fingerprint { get; }

    internal ProviderRouteAdmissionReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        ProviderRouteSelection selection,
        ProviderEndpointNetworkEvidence endpointEvidence,
        string activeProcessingPolicyFingerprint)
        : base(
            operationId,
            sequence,
            occurredAt,
            ProviderRegistryReplayOperationKind.AdmitRoute)
    {
        Selection = selection
            ?? throw new ArgumentNullException(nameof(selection));
        EndpointEvidence = endpointEvidence
            ?? throw new ArgumentNullException(nameof(endpointEvidence));
        ActiveProcessingPolicyFingerprint =
            ProviderRegistryContractGuard.Sha256(
                activeProcessingPolicyFingerprint,
                nameof(activeProcessingPolicyFingerprint));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-ROUTE-ADMISSION-OPERATION/1",
            OperationId,
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Timestamp(OccurredAt),
            Selection.RouteFingerprint,
            EndpointEvidence.Fingerprint,
            ActiveProcessingPolicyFingerprint);
    }
}

internal sealed class ProviderRegistryReplayRecord
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal ProviderRegistryReplayOperationKind Kind { get; }
    internal string OperationFingerprint { get; }
    internal string SnapshotBeforeFingerprint { get; }
    internal string SnapshotAfterFingerprint { get; }
    internal string? CandidateSnapshotFingerprint { get; }
    internal string? RouteSelectionFingerprint { get; }
    internal ProviderRegistryTransitionAdmission? TransitionAdmission {
        get;
    }
    internal ProviderRouteAdmission? RouteAdmission { get; }
    internal ProviderEndpointAdmission? EndpointAdmission { get; }
    internal bool Accepted { get; }
    internal bool SnapshotChanged =>
        !string.Equals(
            SnapshotBeforeFingerprint,
            SnapshotAfterFingerprint,
            StringComparison.Ordinal);
    internal bool AllowsProviderCall => false;
    internal ProviderRegistryStageAAuthorityCounters AuthorityCounters =>
        ProviderRegistryStageAAuthorityCounters.Zero;
    internal string Fingerprint { get; }
    internal string DeterministicFingerprint => Fingerprint;

    private ProviderRegistryReplayRecord(
        ProviderRegistryReplayOperation operation,
        ProviderRegistrySnapshot snapshotBefore,
        ProviderRegistrySnapshot snapshotAfter,
        string? candidateSnapshotFingerprint,
        string? routeSelectionFingerprint,
        ProviderRegistryTransitionAdmission? transitionAdmission,
        ProviderRouteAdmission? routeAdmission,
        ProviderEndpointAdmission? endpointAdmission,
        bool accepted)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (snapshotBefore == null)
            throw new ArgumentNullException(nameof(snapshotBefore));
        if (snapshotAfter == null)
            throw new ArgumentNullException(nameof(snapshotAfter));
        if (transitionAdmission.HasValue
            == routeAdmission.HasValue)
        {
            throw new ArgumentException(
                "A replay record must contain exactly one admission kind.");
        }

        OperationId = operation.OperationId;
        Sequence = operation.Sequence;
        OccurredAt = operation.OccurredAt;
        Kind = operation.Kind;
        OperationFingerprint = operation.Fingerprint;
        SnapshotBeforeFingerprint = snapshotBefore.Fingerprint;
        SnapshotAfterFingerprint = snapshotAfter.Fingerprint;
        CandidateSnapshotFingerprint = candidateSnapshotFingerprint;
        RouteSelectionFingerprint = routeSelectionFingerprint;
        TransitionAdmission = transitionAdmission;
        RouteAdmission = routeAdmission;
        EndpointAdmission = endpointAdmission;
        Accepted = accepted;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-REGISTRY-REPLAY-RECORD/1",
            OperationId,
            ProviderRegistryCanonicalHash.Number(Sequence),
            ProviderRegistryCanonicalHash.Timestamp(OccurredAt),
            ProviderRegistryCanonicalHash.Number((int)Kind),
            OperationFingerprint,
            SnapshotBeforeFingerprint,
            SnapshotAfterFingerprint,
            CandidateSnapshotFingerprint ?? "-",
            RouteSelectionFingerprint ?? "-",
            transitionAdmission.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)transitionAdmission.Value)
                : "-",
            routeAdmission.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)routeAdmission.Value)
                : "-",
            endpointAdmission.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)endpointAdmission.Value)
                : "-",
            Accepted ? "1" : "0",
            AuthorityCounters.Fingerprint);
    }

    internal static ProviderRegistryReplayRecord ForTransition(
        ProviderRegistryReplaceSnapshotOperation operation,
        ProviderRegistrySnapshot snapshotBefore,
        ProviderRegistrySnapshot snapshotAfter,
        ProviderRegistryTransitionResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        return new ProviderRegistryReplayRecord(
            operation,
            snapshotBefore,
            snapshotAfter,
            operation.CandidateSnapshot.Fingerprint,
            routeSelectionFingerprint: null,
            result.Admission,
            routeAdmission: null,
            endpointAdmission: null,
            result.Accepted);
    }

    internal static ProviderRegistryReplayRecord ForRoute(
        ProviderRouteAdmissionReplayOperation operation,
        ProviderRegistrySnapshot snapshot,
        ProviderRouteAdmissionResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        return new ProviderRegistryReplayRecord(
            operation,
            snapshot,
            snapshot,
            candidateSnapshotFingerprint: null,
            operation.Selection.RouteFingerprint,
            transitionAdmission: null,
            result.Admission,
            result.EndpointAdmission,
            result.Accepted);
    }
}

internal sealed class ProviderRegistryStageAReplayResult
{
    internal ProviderRegistrySnapshot InitialSnapshot { get; }
    internal ProviderRegistrySnapshot FinalSnapshot { get; }
    internal IReadOnlyList<ProviderRegistryReplayRecord> Records { get; }
    internal IReadOnlyList<ProviderRegistryReplayRecord> Steps => Records;
    internal ProviderRegistryStageAAuthorityCounters AuthorityCounters =>
        ProviderRegistryStageAAuthorityCounters.Zero;
    internal bool AllowsProviderCall => false;
    internal string DeterministicFingerprint { get; }

    internal ProviderRegistryStageAReplayResult(
        ProviderRegistrySnapshot initialSnapshot,
        ProviderRegistrySnapshot finalSnapshot,
        IReadOnlyList<ProviderRegistryReplayRecord> records)
    {
        InitialSnapshot = initialSnapshot
            ?? throw new ArgumentNullException(nameof(initialSnapshot));
        FinalSnapshot = finalSnapshot
            ?? throw new ArgumentNullException(nameof(finalSnapshot));
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        var copied = new ProviderRegistryReplayRecord[records.Count];
        var recordFingerprints = new string[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            copied[index] = records[index]
                ?? throw new ArgumentException(
                    "Replay records cannot contain null.",
                    nameof(records));
            recordFingerprints[index] = copied[index].Fingerprint;
        }

        Records = new ReadOnlyCollection<ProviderRegistryReplayRecord>(
            copied);
        DeterministicFingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-REGISTRY-STAGE-A-REPLAY/1",
            InitialSnapshot.Fingerprint,
            FinalSnapshot.Fingerprint,
            ProviderRegistryCanonicalHash.Of(
                "D027/PROVIDER-REGISTRY-REPLAY-RECORDS/1",
                recordFingerprints),
            AuthorityCounters.Fingerprint);
    }
}

internal static class ProviderRegistryStageAReplay
{
    internal const int MaximumOperations = 256;

    internal static ProviderRegistryStageAReplayResult Run(
        ProviderRegistrySnapshot initialSnapshot,
        IReadOnlyList<ProviderRegistryReplayOperation> operations)
    {
        if (initialSnapshot == null)
            throw new ArgumentNullException(nameof(initialSnapshot));
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));
        if (operations.Count > MaximumOperations)
            throw new ArgumentOutOfRangeException(nameof(operations));

        var records =
            new List<ProviderRegistryReplayRecord>(operations.Count);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var currentSnapshot = initialSnapshot;
        DateTimeOffset? lastOccurredAt = null;

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index]
                ?? throw new ArgumentException(
                    "Replay operations cannot contain null.",
                    nameof(operations));
            if (operation.Sequence != index + 1L)
            {
                throw new ArgumentException(
                    "Replay sequence must be consecutive and start at one.",
                    nameof(operations));
            }
            if (!operationIds.Add(operation.OperationId))
            {
                throw new ArgumentException(
                    "Replay operation identifiers must be unique.",
                    nameof(operations));
            }
            if (lastOccurredAt.HasValue
                && operation.OccurredAt < lastOccurredAt.Value)
            {
                throw new ArgumentException(
                    "Replay time cannot move backwards.",
                    nameof(operations));
            }

            switch (operation)
            {
                case ProviderRegistryReplaceSnapshotOperation replace:
                {
                    if (replace.Kind
                        != ProviderRegistryReplayOperationKind
                            .ReplaceSnapshot)
                    {
                        throw new ArgumentException(
                            "Replay operation kind does not match payload.",
                            nameof(operations));
                    }
                    var before = currentSnapshot;
                    var transition =
                        ProviderRegistryTransitionPolicy.Evaluate(
                            before,
                            replace.CandidateSnapshot);
                    if (transition.Accepted)
                    {
                        currentSnapshot = transition.AcceptedSnapshot
                            ?? throw new InvalidOperationException(
                                "Accepted transition lost its snapshot.");
                    }
                    records.Add(
                        ProviderRegistryReplayRecord.ForTransition(
                            replace,
                            before,
                            currentSnapshot,
                            transition));
                    break;
                }
                case ProviderRouteAdmissionReplayOperation route:
                {
                    if (route.Kind
                        != ProviderRegistryReplayOperationKind.AdmitRoute)
                    {
                        throw new ArgumentException(
                            "Replay operation kind does not match payload.",
                            nameof(operations));
                    }
                    var admission = ProviderRouteAdmissionPolicy.Evaluate(
                        currentSnapshot,
                        route.Selection,
                        route.EndpointEvidence,
                        route.ActiveProcessingPolicyFingerprint,
                        route.OccurredAt);
                    records.Add(
                        ProviderRegistryReplayRecord.ForRoute(
                            route,
                            currentSnapshot,
                            admission));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operations),
                        "Unknown replay operation.");
            }

            lastOccurredAt = operation.OccurredAt;
        }

        return new ProviderRegistryStageAReplayResult(
            initialSnapshot,
            currentSnapshot,
            records);
    }
}
