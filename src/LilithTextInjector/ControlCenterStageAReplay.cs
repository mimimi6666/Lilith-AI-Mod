using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithTextInjector;

internal enum ControlCenterStageAReplayOperationKind
{
    Invalid = 0,
    ValidateSettingsTransaction = 1,
    ValidateDesktopActivation = 2
}

internal abstract class ControlCenterStageAReplayOperation
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal ControlCenterStageAReplayOperationKind Kind { get; }
    internal abstract string Fingerprint { get; }

    protected ControlCenterStageAReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        ControlCenterStageAReplayOperationKind kind)
    {
        OperationId = ControlCenterStageAContractGuard.Identifier(
            operationId,
            nameof(operationId));
        Sequence = ControlCenterStageAContractGuard.Positive(
            sequence,
            nameof(sequence));
        OccurredAt = ControlCenterStageAContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        Kind = ControlCenterStageAContractGuard.Defined(
            kind,
            nameof(kind));
    }
}

internal sealed class ControlCenterSettingsReplayOperation :
    ControlCenterStageAReplayOperation
{
    internal SettingsChangeTransaction Transaction { get; }
    internal override string Fingerprint { get; }

    internal ControlCenterSettingsReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        SettingsChangeTransaction transaction)
        : base(
            operationId,
            sequence,
            occurredAt,
            ControlCenterStageAReplayOperationKind
                .ValidateSettingsTransaction)
    {
        Transaction = transaction
            ?? throw new ArgumentNullException(nameof(transaction));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTINGS-REPLAY-OPERATION/1",
            OperationId,
            ControlCenterStageACanonicalHash.Number(Sequence),
            ControlCenterStageACanonicalHash.Timestamp(OccurredAt),
            Transaction.Fingerprint);
    }
}

internal sealed class DesktopSurfaceActivationReplayOperation :
    ControlCenterStageAReplayOperation
{
    internal DesktopSurfaceSession Session { get; }
    internal DesktopSurfaceActivationRequest Request { get; }
    internal override string Fingerprint { get; }

    internal DesktopSurfaceActivationReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        DesktopSurfaceSession session,
        DesktopSurfaceActivationRequest request)
        : base(
            operationId,
            sequence,
            occurredAt,
            ControlCenterStageAReplayOperationKind
                .ValidateDesktopActivation)
    {
        Session = session
            ?? throw new ArgumentNullException(nameof(session));
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DESKTOP-REPLAY-OPERATION/1",
            OperationId,
            ControlCenterStageACanonicalHash.Number(Sequence),
            ControlCenterStageACanonicalHash.Timestamp(OccurredAt),
            Session.Fingerprint,
            Request.Fingerprint);
    }
}

internal sealed class ControlCenterStageAReplayRecord
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal ControlCenterStageAReplayOperationKind Kind { get; }
    internal string OperationFingerprint { get; }
    internal ControlCenterSettingsAdmission? SettingsAdmission { get; }
    internal DesktopSurfaceActivationAdmission? DesktopAdmission { get; }
    internal string ReceiptFingerprint { get; }
    internal bool AllowsConfigurationWrite => false;
    internal bool AllowsRuntimeMutation => false;
    internal bool AllowsDesktopArtifactCreation => false;
    internal bool AllowsActivation => false;
    internal bool AllowsCapabilityOutput => false;
    internal ControlCenterStageAAuthorityCounters AuthorityCounters =>
        ControlCenterStageAAuthorityCounters.Zero;
    internal string Fingerprint { get; }
    internal string DeterministicFingerprint => Fingerprint;

    private ControlCenterStageAReplayRecord(
        ControlCenterStageAReplayOperation operation,
        ControlCenterSettingsAdmission? settingsAdmission,
        DesktopSurfaceActivationAdmission? desktopAdmission,
        string receiptFingerprint)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (settingsAdmission.HasValue == desktopAdmission.HasValue)
        {
            throw new ArgumentException(
                "A replay record must contain exactly one admission.");
        }

        OperationId = operation.OperationId;
        Sequence = operation.Sequence;
        OccurredAt = operation.OccurredAt;
        Kind = operation.Kind;
        OperationFingerprint = operation.Fingerprint;
        SettingsAdmission = settingsAdmission;
        DesktopAdmission = desktopAdmission;
        ReceiptFingerprint =
            ControlCenterStageAContractGuard.Sha256(
                receiptFingerprint,
                nameof(receiptFingerprint));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/CONTROL-CENTER-REPLAY-RECORD/1",
            OperationId,
            ControlCenterStageACanonicalHash.Number(Sequence),
            ControlCenterStageACanonicalHash.Timestamp(OccurredAt),
            ControlCenterStageACanonicalHash.Number((int)Kind),
            OperationFingerprint,
            settingsAdmission.HasValue
                ? ControlCenterStageACanonicalHash.Number(
                    (int)settingsAdmission.Value)
                : "-",
            desktopAdmission.HasValue
                ? ControlCenterStageACanonicalHash.Number(
                    (int)desktopAdmission.Value)
                : "-",
            ReceiptFingerprint,
            AuthorityCounters.Fingerprint);
    }

    internal static ControlCenterStageAReplayRecord ForSettings(
        ControlCenterSettingsReplayOperation operation,
        SettingsAdmissionReceipt receipt)
    {
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));
        return new ControlCenterStageAReplayRecord(
            operation,
            receipt.Admission,
            desktopAdmission: null,
            receipt.Fingerprint);
    }

    internal static ControlCenterStageAReplayRecord ForDesktop(
        DesktopSurfaceActivationReplayOperation operation,
        DesktopSurfaceActivationReceipt receipt)
    {
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));
        return new ControlCenterStageAReplayRecord(
            operation,
            settingsAdmission: null,
            receipt.Admission,
            receipt.Fingerprint);
    }
}

internal sealed class ControlCenterStageAReplayResult
{
    internal string InitialSchemaFingerprint { get; }
    internal string FinalSchemaFingerprint { get; }
    internal string InitialDesiredFingerprint { get; }
    internal string FinalDesiredFingerprint { get; }
    internal string? InitialEffectiveFingerprint { get; }
    internal string? FinalEffectiveFingerprint { get; }
    internal IReadOnlyList<ControlCenterStageAReplayRecord> Records {
        get;
    }
    internal IReadOnlyList<ControlCenterStageAReplayRecord> Steps =>
        Records;
    internal bool StateChanged =>
        !string.Equals(
            InitialSchemaFingerprint,
            FinalSchemaFingerprint,
            StringComparison.Ordinal)
        || !string.Equals(
            InitialDesiredFingerprint,
            FinalDesiredFingerprint,
            StringComparison.Ordinal)
        || !string.Equals(
            InitialEffectiveFingerprint,
            FinalEffectiveFingerprint,
            StringComparison.Ordinal);
    internal bool AllowsConfigurationWrite => false;
    internal bool AllowsRuntimeMutation => false;
    internal bool AllowsDesktopArtifactCreation => false;
    internal bool AllowsActivation => false;
    internal bool AllowsCapabilityOutput => false;
    internal ControlCenterStageAAuthorityCounters AuthorityCounters =>
        ControlCenterStageAAuthorityCounters.Zero;
    internal string DeterministicFingerprint { get; }

    internal ControlCenterStageAReplayResult(
        ControlCenterSchema schema,
        DesiredSettingsSnapshot desired,
        EffectiveRuntimeSnapshot? effective,
        IReadOnlyList<ControlCenterStageAReplayRecord> records)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (desired == null)
            throw new ArgumentNullException(nameof(desired));
        if (records == null)
            throw new ArgumentNullException(nameof(records));

        InitialSchemaFingerprint = schema.Fingerprint;
        FinalSchemaFingerprint = schema.Fingerprint;
        InitialDesiredFingerprint = desired.Fingerprint;
        FinalDesiredFingerprint = desired.Fingerprint;
        InitialEffectiveFingerprint = effective?.Fingerprint;
        FinalEffectiveFingerprint = effective?.Fingerprint;

        var copied = new ControlCenterStageAReplayRecord[records.Count];
        var recordFingerprints = new string[records.Count];
        for (var index = 0; index < records.Count; index++)
        {
            copied[index] = records[index]
                ?? throw new ArgumentException(
                    "Replay records cannot contain null.",
                    nameof(records));
            recordFingerprints[index] = copied[index].Fingerprint;
        }
        Records = new ReadOnlyCollection<ControlCenterStageAReplayRecord>(
            copied);
        DeterministicFingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/CONTROL-CENTER-STAGE-A-REPLAY/1",
            InitialSchemaFingerprint,
            FinalSchemaFingerprint,
            InitialDesiredFingerprint,
            FinalDesiredFingerprint,
            InitialEffectiveFingerprint ?? "-",
            FinalEffectiveFingerprint ?? "-",
            ControlCenterStageACanonicalHash.Of(
                "D028/CONTROL-CENTER-REPLAY-RECORDS/1",
                recordFingerprints),
            AuthorityCounters.Fingerprint);
    }
}

internal static class ControlCenterStageAReplay
{
    internal const int MaximumOperations = 256;

    internal static ControlCenterStageAReplayResult Run(
        ControlCenterSchema schema,
        DesiredSettingsSnapshot desired,
        EffectiveRuntimeSnapshot? effective,
        IReadOnlyList<ControlCenterStageAReplayOperation> operations)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (desired == null)
            throw new ArgumentNullException(nameof(desired));
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));
        if (operations.Count > MaximumOperations)
            throw new ArgumentOutOfRangeException(nameof(operations));

        var records =
            new List<ControlCenterStageAReplayRecord>(operations.Count);
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
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
                case ControlCenterSettingsReplayOperation settings:
                {
                    if (settings.Kind
                        != ControlCenterStageAReplayOperationKind
                            .ValidateSettingsTransaction)
                    {
                        throw new ArgumentException(
                            "Replay operation kind does not match payload.",
                            nameof(operations));
                    }
                    var receipt =
                        ControlCenterSettingsAdmissionPolicy.Evaluate(
                            schema,
                            desired,
                            effective,
                            settings.Transaction,
                            settings.OccurredAt);
                    records.Add(
                        ControlCenterStageAReplayRecord.ForSettings(
                            settings,
                            receipt));
                    break;
                }
                case DesktopSurfaceActivationReplayOperation desktop:
                {
                    if (desktop.Kind
                        != ControlCenterStageAReplayOperationKind
                            .ValidateDesktopActivation)
                    {
                        throw new ArgumentException(
                            "Replay operation kind does not match payload.",
                            nameof(operations));
                    }
                    var receipt =
                        DesktopSurfaceStageAAdmissionPolicy.Evaluate(
                            desktop.Session,
                            desktop.Request,
                            desktop.OccurredAt);
                    records.Add(
                        ControlCenterStageAReplayRecord.ForDesktop(
                            desktop,
                            receipt));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operations),
                        "Unknown replay operation.");
            }

            lastOccurredAt = operation.OccurredAt;
        }

        return new ControlCenterStageAReplayResult(
            schema,
            desired,
            effective,
            records);
    }
}
