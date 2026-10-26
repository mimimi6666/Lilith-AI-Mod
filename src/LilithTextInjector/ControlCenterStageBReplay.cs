using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithTextInjector;

internal enum ControlCenterStageBReplayOperationKind
{
    Invalid = 0,
    Observe = 1,
    Disable = 2
}

internal sealed class ControlCenterStageBReplayOperation
{
    internal ControlCenterStageBReplayOperationKind Kind { get; }
    internal LegacyControlCenterProjectionInput? Input { get; }

    internal ControlCenterStageBReplayOperation(
        ControlCenterStageBReplayOperationKind kind,
        LegacyControlCenterProjectionInput? input = null)
    {
        Kind = kind;
        Input = input;
    }
}

internal sealed class ControlCenterStageBReplayRecord
{
    internal int Sequence { get; }
    internal LegacyControlCenterProjectionAdmission Admission { get; }
    internal string SemanticFingerprint { get; }
    internal string ObservationFingerprint { get; }

    internal ControlCenterStageBReplayRecord(
        int sequence,
        LegacyControlCenterProjectionAdmission admission,
        LegacyControlCenterProjection? projection)
    {
        Sequence = sequence;
        Admission = admission;
        SemanticFingerprint = projection?.SemanticFingerprint ?? string.Empty;
        ObservationFingerprint =
            projection?.ObservationFingerprint ?? string.Empty;
    }
}

internal sealed class ControlCenterStageBReplayResult
{
    internal LegacyControlCenterProjection? Current { get; }
    internal IReadOnlyList<ControlCenterStageBReplayRecord> Records { get; }
    internal ControlCenterStageBAuthorityCounters AuthorityCounters { get; }
    internal string Fingerprint { get; }

    internal ControlCenterStageBReplayResult(
        LegacyControlCenterProjection? current,
        IReadOnlyList<ControlCenterStageBReplayRecord> records)
    {
        Current = current;
        Records = records;
        AuthorityCounters = ControlCenterStageBAuthorityCounters.Zero;
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/STAGE-B-REPLAY/1",
            current?.ObservationFingerprint,
            string.Join(
                ",",
                records.Select(
                    record => ControlCenterStageACanonicalHash.Of(
                        "D028/STAGE-B-RECORD/1",
                        ControlCenterStageACanonicalHash.Number(
                            record.Sequence),
                        ControlCenterStageACanonicalHash.Number(
                            (int)record.Admission),
                        record.SemanticFingerprint,
                        record.ObservationFingerprint))));
    }
}

internal sealed class ControlCenterStageBProjectionHost
{
    internal LegacyControlCenterProjection? Current { get; private set; }

    internal LegacyControlCenterProjectionResult Observe(
        LegacyControlCenterProjectionInput input)
    {
        var projected =
            ControlCenterStageBLegacyProjectionAdapter.Project(input);
        if (projected.Admission
                != LegacyControlCenterProjectionAdmission.Accepted
            || projected.Projection == null)
        {
            return projected;
        }
        var next = projected.Projection;
        if (Current == null)
        {
            Current = next;
            return projected;
        }
        if (!Current.EffectiveSnapshot.RuntimeIdentity.Matches(
                next.EffectiveSnapshot.RuntimeIdentity))
        {
            return Reject(
                LegacyControlCenterProjectionAdmission.CrossRuntimeRejected);
        }
        if (next.EffectiveSnapshot.ConfigurationGeneration
            < Current.EffectiveSnapshot.ConfigurationGeneration)
        {
            return Reject(
                LegacyControlCenterProjectionAdmission
                    .StaleConfigurationRejected);
        }
        if (next.EffectiveSnapshot.ConfigurationGeneration
                == Current.EffectiveSnapshot.ConfigurationGeneration
            && !string.Equals(
                next.SemanticFingerprint,
                Current.SemanticFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                LegacyControlCenterProjectionAdmission
                    .ConflictingConfigurationRejected);
        }
        if (string.Equals(
                next.SemanticFingerprint,
                Current.SemanticFingerprint,
                StringComparison.Ordinal))
        {
            Current = next;
            return new LegacyControlCenterProjectionResult(
                LegacyControlCenterProjectionAdmission
                    .CoalescedTimeRefresh,
                next);
        }
        Current = next;
        return projected;
    }

    internal LegacyControlCenterProjectionResult Disable()
    {
        Current = null;
        return new LegacyControlCenterProjectionResult(
            LegacyControlCenterProjectionAdmission.DisabledAndCleared,
            null);
    }

    private static LegacyControlCenterProjectionResult Reject(
        LegacyControlCenterProjectionAdmission admission)
        => new(admission, null);
}

internal static class ControlCenterStageBReplay
{
    internal const int MaximumOperations = 128;

    internal static ControlCenterStageBReplayResult Run(
        IEnumerable<ControlCenterStageBReplayOperation> operations)
    {
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));
        var values = operations.ToArray();
        if (values.Length == 0 || values.Length > MaximumOperations)
            throw new ArgumentOutOfRangeException(nameof(operations));
        var host = new ControlCenterStageBProjectionHost();
        var records = new List<ControlCenterStageBReplayRecord>();
        for (var index = 0; index < values.Length; index++)
        {
            var operation = values[index]
                ?? throw new ArgumentException(
                    "Replay operations cannot contain null.",
                    nameof(operations));
            LegacyControlCenterProjectionResult result;
            switch (operation.Kind)
            {
                case ControlCenterStageBReplayOperationKind.Observe:
                    if (operation.Input == null)
                    {
                        throw new ArgumentException(
                            "Observe requires an input.",
                            nameof(operations));
                    }
                    result = host.Observe(operation.Input);
                    break;
                case ControlCenterStageBReplayOperationKind.Disable:
                    if (operation.Input != null)
                    {
                        throw new ArgumentException(
                            "Disable cannot carry an input.",
                            nameof(operations));
                    }
                    result = host.Disable();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operations));
            }
            records.Add(
                new ControlCenterStageBReplayRecord(
                    index + 1,
                    result.Admission,
                    result.Projection));
        }
        return new ControlCenterStageBReplayResult(
            host.Current,
            new ReadOnlyCollection<ControlCenterStageBReplayRecord>(
                records));
    }
}
