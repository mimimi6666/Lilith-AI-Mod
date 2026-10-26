using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace LilithTextInjector;

internal enum NativeNoteWriterOrigin
{
    Invalid = 0,
    ModOneTimeTestNote = 1,
    ModLegacyAiScheduler = 2,
    ModLivingContinuityOwnedProactiveNote = 3,
    OfficialSleepStartup = 4,
    OfficialSecondLaunch = 5,
    OfficialWriteNoteBehavior = 6,
    OfficialBirthdayNoteBehavior = 7,
    OfficialAnniversaryNote = 8,
    OfficialPlaytimeMilestoneNote = 9,
    OfficialDialogueSilentNote = 10,
    OfficialGameManagerWriteNoteFeedback = 11,
    OfficialOrCommonSinkUnattributed = 12,
    ModHermesNoteInboxTool = 13
}

internal enum NativeNoteWriteTerminalStatus
{
    Invalid = 0,
    Succeeded = 1,
    SinkThrew = 2,
    SinkReturnedArtifactUnverified = 3
}

/// <summary>
/// Content-free terminal receipt for one observed native Note write attempt.
/// Artifact metadata is present only after a caller has already completed and
/// independently identified a successful existing write.
/// </summary>
internal sealed class NativeNoteWriteProvenanceReceipt
{
    internal string ProcessEpoch { get; }
    internal long Sequence { get; }
    internal long ObservedAtMonotonicTicks { get; }
    internal NativeNoteWriterOrigin WriterOrigin { get; }
    internal NativeNoteWriteTerminalStatus TerminalStatus { get; }
    internal long? ByteSize { get; }
    internal string? FileSha256 { get; }

    internal NativeNoteWriteProvenanceReceipt(
        string processEpoch,
        long sequence,
        long observedAtMonotonicTicks,
        NativeNoteWriterOrigin writerOrigin,
        NativeNoteWriteTerminalStatus terminalStatus,
        long? byteSize,
        string? fileSha256)
    {
        ProcessEpoch = NativeNoteWriteProvenanceGuard.ProcessEpoch(
            processEpoch,
            nameof(processEpoch));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (observedAtMonotonicTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAtMonotonicTicks));
        }
        NativeNoteWriteProvenanceGuard.WriterOrigin(
            writerOrigin,
            nameof(writerOrigin));
        NativeNoteWriteProvenanceGuard.TerminalStatus(
            terminalStatus,
            nameof(terminalStatus));

        if (terminalStatus == NativeNoteWriteTerminalStatus.Succeeded)
        {
            if (!byteSize.HasValue || byteSize.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(byteSize));
            fileSha256 = NativeNoteWriteProvenanceGuard.FileSha256(
                fileSha256,
                nameof(fileSha256));
        }
        else if (byteSize.HasValue
                 || fileSha256 != null)
        {
            throw new ArgumentException(
                "Failed Note-write receipts cannot contain artifact metadata.",
                nameof(byteSize));
        }

        Sequence = sequence;
        ObservedAtMonotonicTicks = observedAtMonotonicTicks;
        WriterOrigin = writerOrigin;
        TerminalStatus = terminalStatus;
        ByteSize = byteSize;
        FileSha256 = fileSha256;
    }
}

/// <summary>
/// Process-local bounded ledger for content-free native Note write receipts.
/// It performs no file access, persistence, publication or caller inference.
/// </summary>
internal sealed class NativeNoteWriteProvenance
{
    internal const int DefaultCapacity = 32;
    internal const int MaximumCapacity = 64;

    private readonly object _gate = new();
    private readonly Func<long> _monotonicClock;
    private readonly Queue<NativeNoteWriteProvenanceReceipt> _receipts;
    private long _lastObservedAtMonotonicTicks = -1;
    private long _lastSequence;
    private long _evictedCount;

    internal string ProcessEpoch { get; }
    internal int Capacity { get; }
    internal long TotalRecorded
    {
        get
        {
            lock (_gate)
                return _lastSequence;
        }
    }
    internal long EvictedCount
    {
        get
        {
            lock (_gate)
                return _evictedCount;
        }
    }

    internal NativeNoteWriteProvenance(
        string processEpoch,
        int capacity = DefaultCapacity,
        Func<long>? monotonicClock = null)
    {
        ProcessEpoch = NativeNoteWriteProvenanceGuard.ProcessEpoch(
            processEpoch,
            nameof(processEpoch));
        if (capacity <= 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _receipts = new Queue<NativeNoteWriteProvenanceReceipt>(capacity);
        _monotonicClock = monotonicClock ?? Stopwatch.GetTimestamp;
    }

    internal NativeNoteWriteProvenanceReceipt
        RecordSuccessfulExistingWrite(
            NativeNoteWriterOrigin writerOrigin,
            long byteSize,
            string fileSha256)
    {
        NativeNoteWriteProvenanceGuard.WriterOrigin(
            writerOrigin,
            nameof(writerOrigin));
        if (byteSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteSize));
        var normalizedSha256 =
            NativeNoteWriteProvenanceGuard.FileSha256(
                fileSha256,
                nameof(fileSha256));

        return RecordTerminal(
            writerOrigin,
            NativeNoteWriteTerminalStatus.Succeeded,
            byteSize,
            normalizedSha256);
    }

    internal NativeNoteWriteProvenanceReceipt RecordSinkThrew(
        NativeNoteWriterOrigin writerOrigin)
    {
        NativeNoteWriteProvenanceGuard.WriterOrigin(
            writerOrigin,
            nameof(writerOrigin));
        return RecordTerminal(
            writerOrigin,
            NativeNoteWriteTerminalStatus.SinkThrew,
            byteSize: null,
            fileSha256: null);
    }

    internal NativeNoteWriteProvenanceReceipt RecordArtifactUnverified(
        NativeNoteWriterOrigin writerOrigin)
    {
        NativeNoteWriteProvenanceGuard.WriterOrigin(
            writerOrigin,
            nameof(writerOrigin));
        return RecordTerminal(
            writerOrigin,
            NativeNoteWriteTerminalStatus
                .SinkReturnedArtifactUnverified,
            byteSize: null,
            fileSha256: null);
    }

    internal IReadOnlyList<NativeNoteWriteProvenanceReceipt> Snapshot()
    {
        lock (_gate)
        {
            return new ReadOnlyCollection<
                NativeNoteWriteProvenanceReceipt>(_receipts.ToArray());
        }
    }

    private NativeNoteWriteProvenanceReceipt RecordTerminal(
        NativeNoteWriterOrigin writerOrigin,
        NativeNoteWriteTerminalStatus terminalStatus,
        long? byteSize,
        string? fileSha256)
    {
        lock (_gate)
        {
            var observedAt = _monotonicClock();
            if (observedAt < 0
                || observedAt < _lastObservedAtMonotonicTicks)
            {
                throw new InvalidOperationException(
                    "Native Note provenance clock was not monotonic.");
            }

            var sequence = checked(_lastSequence + 1);
            var receipt = new NativeNoteWriteProvenanceReceipt(
                ProcessEpoch,
                sequence,
                observedAt,
                writerOrigin,
                terminalStatus,
                byteSize,
                fileSha256);

            if (_receipts.Count == Capacity)
            {
                _receipts.Dequeue();
                _evictedCount = checked(_evictedCount + 1);
            }
            _receipts.Enqueue(receipt);
            _lastObservedAtMonotonicTicks = observedAt;
            _lastSequence = sequence;
            return receipt;
        }
    }
}

internal static class NativeNoteWriteProvenanceGuard
{
    private const int MaximumProcessEpochLength = 128;

    internal static string ProcessEpoch(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumProcessEpochLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || ContainsControlCharacter(value))
        {
            throw new ArgumentException(
                "Process epoch must be bounded opaque metadata.",
                parameterName);
        }
        return value;
    }

    internal static void WriterOrigin(
        NativeNoteWriterOrigin value,
        string parameterName)
    {
        if (!Enum.IsDefined(typeof(NativeNoteWriterOrigin), value)
            || value == NativeNoteWriterOrigin.Invalid)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal static void TerminalStatus(
        NativeNoteWriteTerminalStatus value,
        string parameterName)
    {
        if (!Enum.IsDefined(typeof(NativeNoteWriteTerminalStatus), value)
            || value == NativeNoteWriteTerminalStatus.Invalid)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal static string FileSha256(
        string? value,
        string parameterName)
    {
        if (value == null || value.Length != 64)
            throw new ArgumentException("SHA-256 must contain 64 hex digits.", parameterName);
        for (var index = 0; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                throw new ArgumentException(
                    "SHA-256 must contain 64 hex digits.",
                    parameterName);
            }
        }
        return value.ToUpperInvariant();
    }

    private static bool ContainsControlCharacter(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsControl(value[index]))
                return true;
        }
        return false;
    }
}

internal static class NativeNoteWriterOriginClassifier
{
    internal static NativeNoteWriterOrigin FromEntryPoint(
        string? declaringTypeName,
        string? methodName)
    {
        if (string.IsNullOrWhiteSpace(declaringTypeName)
            || string.IsNullOrWhiteSpace(methodName))
        {
            return NativeNoteWriterOrigin
                .OfficialOrCommonSinkUnattributed;
        }

        if (declaringTypeName.Contains(
                "SaveSleepStartupNoteNextFrame",
                StringComparison.Ordinal)
            || declaringTypeName == "LilithStateManager"
                && methodName is (
                    "TrySaveSleepStartupNote"
                    or "SaveSleepStartupNoteNextFrame"))
        {
            return NativeNoteWriterOrigin.OfficialSleepStartup;
        }
        if (declaringTypeName.Contains(
                "SaveSecondLaunchNoteNextFrame",
                StringComparison.Ordinal)
            || declaringTypeName == "LilithStateManager"
                && methodName is (
                    "TrySaveSecondLaunchNote"
                    or "SaveSecondLaunchNoteNextFrame"))
        {
            return NativeNoteWriterOrigin.OfficialSecondLaunch;
        }

        return (declaringTypeName, methodName) switch
        {
            ("WriteNoteBehavior", "Begin" or "Tick") =>
                NativeNoteWriterOrigin.OfficialWriteNoteBehavior,
            ("BirthdayNoteBehavior", "Begin" or "Tick") =>
                NativeNoteWriterOrigin.OfficialBirthdayNoteBehavior,
            ("AnniversaryNote", "Tick") =>
                NativeNoteWriterOrigin.OfficialAnniversaryNote,
            ("PlaytimeMilestoneNote", "Tick") =>
                NativeNoteWriterOrigin.OfficialPlaytimeMilestoneNote,
            ("DialogueManager", "TrySaveNoteSilently") =>
                NativeNoteWriterOrigin.OfficialDialogueSilentNote,
            ("GameManagerSystem", "TriggerWriteNoteWithFeedback") =>
                NativeNoteWriterOrigin
                    .OfficialGameManagerWriteNoteFeedback,
            _ => NativeNoteWriterOrigin
                .OfficialOrCommonSinkUnattributed
        };
    }
}
