using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum OrchestratorObserveHostStatus
{
    Off = 0,
    Observe = 1,
    StoppedByFault = 2,
    CapacityReached = 3
}

internal enum OrchestratorObserveSourceKind
{
    PlayerTurnLifecycle = 0,
    AvatarInteractionGates = 1,
    ExistingExecutionLifecycle = 2
}

internal enum OrchestratorObserveSourceStatus
{
    Disabled = 0,
    Enabled = 1,
    Faulted = 2
}

internal enum OrchestratorObserveDiagnosticKind
{
    EventAccepted = 0,
    EventRejected = 1,
    EventCoalesced = 2,
    EventDropped = 3,
    SourceFaulted = 4,
    InvocationSuppressed = 5
}

internal sealed class OrchestratorObserveDiagnosticEntry
{
    internal DateTimeOffset OccurredAt { get; }
    internal OrchestratorObserveSourceKind Source { get; }
    internal OrchestratorObserveDiagnosticKind Kind { get; }
    internal OrchestratorEventKind? EventKind { get; }
    internal OrchestratorProcessDisposition? ProcessDisposition { get; }
    internal OrchestratorProcessReason? ProcessReason { get; }
    internal DecisionDisposition? DecisionDisposition { get; }
    internal DecisionReason? DecisionReason { get; }
    internal string CorrelationSuffix { get; }

    internal OrchestratorObserveDiagnosticEntry(
        DateTimeOffset occurredAt,
        OrchestratorObserveSourceKind source,
        OrchestratorObserveDiagnosticKind kind,
        OrchestratorEventKind? eventKind,
        OrchestratorProcessDisposition? processDisposition,
        OrchestratorProcessReason? processReason,
        DecisionDisposition? decisionDisposition,
        DecisionReason? decisionReason,
        string correlationSuffix)
    {
        if (!Enum.IsDefined(typeof(OrchestratorObserveSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(typeof(OrchestratorObserveDiagnosticKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ValidateOptionalEnum(eventKind, nameof(eventKind));
        ValidateOptionalEnum(processDisposition, nameof(processDisposition));
        ValidateOptionalEnum(processReason, nameof(processReason));
        ValidateOptionalEnum(decisionDisposition, nameof(decisionDisposition));
        ValidateOptionalEnum(decisionReason, nameof(decisionReason));

        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        Source = source;
        Kind = kind;
        EventKind = eventKind;
        ProcessDisposition = processDisposition;
        ProcessReason = processReason;
        DecisionDisposition = decisionDisposition;
        DecisionReason = decisionReason;
        CorrelationSuffix = DecisionCoreContractGuard.Identifier(
            correlationSuffix,
            16,
            nameof(correlationSuffix));
    }

    private static void ValidateOptionalEnum<T>(
        T? value,
        string parameterName)
        where T : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(typeof(T), value.Value))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

internal sealed class OrchestratorObserveDiagnosticBuffer
{
    internal const int MaximumEntries = 128;

    internal static OrchestratorObserveDiagnosticBuffer Empty { get; } =
        new(Array.Empty<OrchestratorObserveDiagnosticEntry>(), 0);

    internal IReadOnlyList<OrchestratorObserveDiagnosticEntry> Entries { get; }
    internal long DroppedEntryCount { get; }

    private OrchestratorObserveDiagnosticBuffer(
        IReadOnlyList<OrchestratorObserveDiagnosticEntry> entries,
        long droppedEntryCount)
    {
        if (entries == null)
            throw new ArgumentNullException(nameof(entries));
        if (entries.Count > MaximumEntries)
            throw new ArgumentOutOfRangeException(nameof(entries));
        if (droppedEntryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(droppedEntryCount));

        var copy = new OrchestratorObserveDiagnosticEntry[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            copy[index] = entries[index]
                ?? throw new ArgumentException(
                    "Diagnostic buffer cannot contain null.",
                    nameof(entries));
        }
        Entries = Array.AsReadOnly(copy);
        DroppedEntryCount = droppedEntryCount;
    }

    internal OrchestratorObserveDiagnosticBuffer Append(
        OrchestratorObserveDiagnosticEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        var targetCount = Math.Min(Entries.Count + 1, MaximumEntries);
        var copy = new OrchestratorObserveDiagnosticEntry[targetCount];
        var sourceStart = Entries.Count >= MaximumEntries ? 1 : 0;
        var destination = 0;
        for (var index = sourceStart; index < Entries.Count; index++)
            copy[destination++] = Entries[index];
        copy[^1] = entry;
        return new OrchestratorObserveDiagnosticBuffer(
            Array.AsReadOnly(copy),
            DroppedEntryCount + (sourceStart == 0 ? 0 : 1));
    }
}
