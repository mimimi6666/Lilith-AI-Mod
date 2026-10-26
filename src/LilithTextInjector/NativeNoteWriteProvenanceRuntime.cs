using System;
using System.IO;
using System.Security.Cryptography;

namespace LilithTextInjector;

/// <summary>
/// Default-Off runtime adapter around the pure bounded provenance ledger.
/// The adapter observes only an already-completed common native Note sink and
/// never changes the sink result, retries it or publishes Note content.
/// </summary>
internal static class NativeNoteWriteProvenanceRuntime
{
    internal const long MaximumArtifactBytes = 8L * 1024 * 1024;

    [ThreadStatic]
    private static NativeNoteWriterOrigin _currentWriterOrigin;

    private static readonly object Gate = new();
    private static NativeNoteWriteProvenance? _ledger;

    internal static NativeNoteWriterScope EnterWriter(
        bool enabled,
        NativeNoteWriterOrigin writerOrigin)
    {
        if (!enabled)
            return default;

        NativeNoteWriteProvenanceGuard.WriterOrigin(
            writerOrigin,
            nameof(writerOrigin));
        var previous = _currentWriterOrigin;
        if (previous != NativeNoteWriterOrigin.Invalid
            && OriginPrecedence(writerOrigin) <= OriginPrecedence(previous))
        {
            return default;
        }
        _currentWriterOrigin = writerOrigin;
        return new NativeNoteWriterScope(active: true, previous);
    }

    internal static NativeNoteSinkAttempt BeginCommonSink(bool enabled)
    {
        if (!enabled)
            return NativeNoteSinkAttempt.Disabled;

        var origin = _currentWriterOrigin == NativeNoteWriterOrigin.Invalid
            ? NativeNoteWriterOrigin.OfficialOrCommonSinkUnattributed
            : _currentWriterOrigin;
        return new NativeNoteSinkAttempt(enabled: true, origin);
    }

    internal static NativeNoteWriteProvenanceReceipt?
        TryCompleteSuccessfulExistingWrite(
            NativeNoteSinkAttempt? attempt,
            string? writtenPath,
            string? nativeNotesDirectory)
    {
        if (attempt == null || !attempt.TryBeginTerminal())
            return null;

        try
        {
            if (!TryResolveDirectNativeNote(
                    writtenPath,
                    nativeNotesDirectory,
                    out var resolvedPath)
                || !TryInspectStableArtifact(
                    resolvedPath,
                    out var byteSize,
                    out var fileSha256))
            {
                return TryRecordArtifactUnverifiedAfterTerminalClaim(
                    attempt.WriterOrigin);
            }

            return Ledger().RecordSuccessfulExistingWrite(
                attempt.WriterOrigin,
                byteSize,
                fileSha256);
        }
        catch
        {
            return TryRecordArtifactUnverifiedAfterTerminalClaim(
                attempt.WriterOrigin);
        }
    }

    internal static NativeNoteWriteProvenanceReceipt? TryCompleteSinkThrew(
        NativeNoteSinkAttempt? attempt)
    {
        if (attempt == null || !attempt.TryBeginTerminal())
            return null;
        return TryRecordSinkThrewAfterTerminalClaim(attempt.WriterOrigin);
    }

    internal static NativeNoteWriteProvenanceReceipt[] Snapshot()
    {
        lock (Gate)
        {
            if (_ledger == null)
                return Array.Empty<NativeNoteWriteProvenanceReceipt>();
            var snapshot = _ledger.Snapshot();
            var copy = new NativeNoteWriteProvenanceReceipt[snapshot.Count];
            for (var index = 0; index < snapshot.Count; index++)
                copy[index] = snapshot[index];
            return copy;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            _ledger = null;
        _currentWriterOrigin = NativeNoteWriterOrigin.Invalid;
    }

    internal static void RestoreWriterOrigin(
        NativeNoteWriterOrigin previous)
        => _currentWriterOrigin = previous;

    private static NativeNoteWriteProvenance Ledger()
    {
        lock (Gate)
        {
            return _ledger ??= new NativeNoteWriteProvenance(
                Guid.NewGuid().ToString("N"));
        }
    }

    private static NativeNoteWriteProvenanceReceipt?
        TryRecordSinkThrewAfterTerminalClaim(
            NativeNoteWriterOrigin writerOrigin)
    {
        try
        {
            return Ledger().RecordSinkThrew(writerOrigin);
        }
        catch
        {
            return null;
        }
    }

    private static NativeNoteWriteProvenanceReceipt?
        TryRecordArtifactUnverifiedAfterTerminalClaim(
            NativeNoteWriterOrigin writerOrigin)
    {
        try
        {
            return Ledger().RecordArtifactUnverified(writerOrigin);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryResolveDirectNativeNote(
        string? writtenPath,
        string? nativeNotesDirectory,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(writtenPath)
            || string.IsNullOrWhiteSpace(nativeNotesDirectory))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(writtenPath);
            var fullDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(nativeNotesDirectory));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(
                    Path.GetDirectoryName(fullPath),
                    fullDirectory,
                    comparison)
                || !string.Equals(
                    Path.GetExtension(fullPath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolvedPath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInspectStableArtifact(
        string path,
        out long byteSize,
        out string fileSha256)
    {
        byteSize = 0;
        fileSha256 = string.Empty;
        if (!TryHashOneBoundedSnapshot(path, out var firstSize, out var first)
            || !TryHashOneBoundedSnapshot(path, out var secondSize, out var second)
            || firstSize != secondSize
            || !CryptographicOperations.FixedTimeEquals(first, second))
        {
            return false;
        }

        byteSize = firstSize;
        fileSha256 = Convert.ToHexString(first);
        return true;
    }

    private static bool TryHashOneBoundedSnapshot(
        string path,
        out long byteSize,
        out byte[] hash)
    {
        byteSize = 0;
        hash = Array.Empty<byte>();
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var expectedSize = stream.Length;
            if (expectedSize <= 0 || expectedSize > MaximumArtifactBytes)
                return false;

            using var incremental = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            var remaining = expectedSize;
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, requested);
                if (read <= 0)
                    return false;
                incremental.AppendData(buffer, 0, read);
                remaining -= read;
            }
            if (stream.ReadByte() != -1 || stream.Length != expectedSize)
                return false;

            byteSize = expectedSize;
            hash = incremental.GetHashAndReset();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int OriginPrecedence(NativeNoteWriterOrigin origin)
    {
        return origin switch
        {
            NativeNoteWriterOrigin.ModOneTimeTestNote
                or NativeNoteWriterOrigin.ModLegacyAiScheduler
                or NativeNoteWriterOrigin
                    .ModLivingContinuityOwnedProactiveNote => 3,
            NativeNoteWriterOrigin.OfficialSleepStartup
                or NativeNoteWriterOrigin.OfficialSecondLaunch
                or NativeNoteWriterOrigin.OfficialWriteNoteBehavior
                or NativeNoteWriterOrigin.OfficialBirthdayNoteBehavior
                or NativeNoteWriterOrigin.OfficialAnniversaryNote
                or NativeNoteWriterOrigin.OfficialPlaytimeMilestoneNote => 2,
            NativeNoteWriterOrigin.OfficialDialogueSilentNote
                or NativeNoteWriterOrigin
                    .OfficialGameManagerWriteNoteFeedback
                or NativeNoteWriterOrigin
                    .OfficialOrCommonSinkUnattributed => 1,
            _ => 0
        };
    }
}

internal readonly struct NativeNoteWriterScope : IDisposable
{
    private readonly bool _active;
    private readonly NativeNoteWriterOrigin _previous;

    internal NativeNoteWriterScope(
        bool active,
        NativeNoteWriterOrigin previous)
    {
        _active = active;
        _previous = previous;
    }

    public void Dispose()
    {
        if (_active)
            NativeNoteWriteProvenanceRuntime.RestoreWriterOrigin(_previous);
    }
}

internal sealed class NativeNoteSinkAttempt
{
    internal static NativeNoteSinkAttempt Disabled { get; } = new(
        enabled: false,
        NativeNoteWriterOrigin.Invalid);

    private readonly bool _enabled;
    private bool _terminalClaimed;

    internal NativeNoteWriterOrigin WriterOrigin { get; }

    internal NativeNoteSinkAttempt(
        bool enabled,
        NativeNoteWriterOrigin writerOrigin)
    {
        if (enabled)
        {
            NativeNoteWriteProvenanceGuard.WriterOrigin(
                writerOrigin,
                nameof(writerOrigin));
        }
        else if (writerOrigin != NativeNoteWriterOrigin.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(writerOrigin));
        }

        _enabled = enabled;
        WriterOrigin = writerOrigin;
    }

    internal bool TryBeginTerminal()
    {
        if (!_enabled || _terminalClaimed)
            return false;
        _terminalClaimed = true;
        return true;
    }
}
