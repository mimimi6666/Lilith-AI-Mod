using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum D043SpeechCancellationReason
{
    None = 0,
    VoiceDisabled = 1,
    PublicationRetired = 2,
    RuntimeGenerationChanged = 3,
    LanguageGenerationChanged = 4,
    ConfigurationChanged = 5,
    NativeVoiceTakeover = 6,
    Shutdown = 7,
    LifetimeExceeded = 8,
    CapacityExceeded = 9,
    SynthesisFailed = 10,
    MonotonicDiscontinuity = 11,
    ProtocolViolation = 12,
    UserRequested = 13
}

internal enum D043SpeechTerminalKind
{
    None = 0,
    Completed = 1,
    Canceled = 2,
    Failed = 3
}

internal enum D043SpeechEffectKind
{
    DispatchSynthesis = 1,
    CancelSynthesis = 2,
    StartPlayback = 3,
    StopPlayback = 4,
    EmitTerminalReceipt = 5
}

internal enum D043SpeechEventDisposition
{
    Applied = 0,
    LateOrDuplicateDropped = 1,
    ForeignIdentityDropped = 2,
    ForeignCommandDropped = 3,
    OutOfOrderDropped = 4,
    InvalidEventRejected = 5,
    TerminalAlreadySealed = 6
}

internal readonly struct D043SpeechSchedulerCaps
{
    private const int V1MaximumSegments = 64;
    private const int V1MaximumBufferedSegments = 2;
    private const int V1MaximumPendingTextUtf8Bytes = 128 * 1024;
    private const int V1MaximumEncodedBytesPerSegment = 8 * 1024 * 1024;
    private const int V1MaximumBufferedEncodedBytes = 16 * 1024 * 1024;
    private const long V1MaximumUtteranceLifetimeMicroseconds = 300_000_000;
    internal static D043SpeechSchedulerCaps V1 { get; } = new(
        maximumSegments: V1MaximumSegments,
        maximumBufferedSegments: V1MaximumBufferedSegments,
        maximumPendingTextUtf8Bytes: V1MaximumPendingTextUtf8Bytes,
        maximumEncodedBytesPerSegment: V1MaximumEncodedBytesPerSegment,
        maximumBufferedEncodedBytes: V1MaximumBufferedEncodedBytes,
        maximumUtteranceLifetimeMicroseconds:
            V1MaximumUtteranceLifetimeMicroseconds);

    internal int MaximumSegments { get; }
    internal int MaximumBufferedSegments { get; }
    internal int MaximumPendingTextUtf8Bytes { get; }
    internal int MaximumEncodedBytesPerSegment { get; }
    internal int MaximumBufferedEncodedBytes { get; }
    internal long MaximumUtteranceLifetimeMicroseconds { get; }
    internal bool IsWithinV1Policy => MaximumSegments is >= 1 and <= V1MaximumSegments
        && MaximumBufferedSegments is >= 1 and <= V1MaximumBufferedSegments
        && MaximumPendingTextUtf8Bytes is >= 1 and <= V1MaximumPendingTextUtf8Bytes
        && MaximumEncodedBytesPerSegment is >= 44
            and <= V1MaximumEncodedBytesPerSegment
        && MaximumBufferedEncodedBytes >= MaximumEncodedBytesPerSegment
        && MaximumBufferedEncodedBytes <= V1MaximumBufferedEncodedBytes
        && MaximumUtteranceLifetimeMicroseconds is >= 1
            and <= V1MaximumUtteranceLifetimeMicroseconds;

    internal D043SpeechSchedulerCaps(
        int maximumSegments,
        int maximumBufferedSegments,
        int maximumPendingTextUtf8Bytes,
        int maximumEncodedBytesPerSegment,
        int maximumBufferedEncodedBytes,
        long maximumUtteranceLifetimeMicroseconds)
    {
        if (maximumSegments < 1
            || maximumSegments > V1MaximumSegments
            || maximumBufferedSegments < 1
            || maximumBufferedSegments > V1MaximumBufferedSegments
            || maximumPendingTextUtf8Bytes < 1
            || maximumPendingTextUtf8Bytes > V1MaximumPendingTextUtf8Bytes
            || maximumEncodedBytesPerSegment < 44
            || maximumEncodedBytesPerSegment > V1MaximumEncodedBytesPerSegment
            || maximumBufferedEncodedBytes < maximumEncodedBytesPerSegment
            || maximumBufferedEncodedBytes > V1MaximumBufferedEncodedBytes
            || maximumUtteranceLifetimeMicroseconds < 1
            || maximumUtteranceLifetimeMicroseconds
                > V1MaximumUtteranceLifetimeMicroseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        }

        MaximumSegments = maximumSegments;
        MaximumBufferedSegments = maximumBufferedSegments;
        MaximumPendingTextUtf8Bytes = maximumPendingTextUtf8Bytes;
        MaximumEncodedBytesPerSegment = maximumEncodedBytesPerSegment;
        MaximumBufferedEncodedBytes = maximumBufferedEncodedBytes;
        MaximumUtteranceLifetimeMicroseconds = maximumUtteranceLifetimeMicroseconds;
    }
}

internal sealed class D043SpeechPublicationAuthority
{
    internal string UtteranceFingerprint { get; }
    internal string SourceFingerprint { get; }
    internal string OperationFingerprint { get; }
    internal long PublicationSequence { get; }
    internal long RuntimeGeneration { get; }
    internal long LanguageGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal string AuthorityFingerprint { get; }

    internal D043SpeechPublicationAuthority(
        string utteranceFingerprint,
        string sourceFingerprint,
        string operationFingerprint,
        long publicationSequence,
        long runtimeGeneration,
        long languageGeneration,
        string configurationFingerprint)
    {
        UtteranceFingerprint = RequireFingerprint(
            utteranceFingerprint,
            nameof(utteranceFingerprint));
        SourceFingerprint = RequireFingerprint(
            sourceFingerprint,
            nameof(sourceFingerprint));
        OperationFingerprint = RequireFingerprint(
            operationFingerprint,
            nameof(operationFingerprint));
        ConfigurationFingerprint = RequireFingerprint(
            configurationFingerprint,
            nameof(configurationFingerprint));
        if (publicationSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(publicationSequence));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (languageGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(languageGeneration));

        PublicationSequence = publicationSequence;
        RuntimeGeneration = runtimeGeneration;
        LanguageGeneration = languageGeneration;
        AuthorityFingerprint = Hash(
            "d043-publication-authority-v1",
            UtteranceFingerprint,
            SourceFingerprint,
            OperationFingerprint,
            PublicationSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RuntimeGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LanguageGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ConfigurationFingerprint);
    }

    private static string RequireFingerprint(string value, string parameterName)
    {
        if (value is null
            || value.Length != 64
            || !string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected an uppercase SHA-256 fingerprint.", parameterName);
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'A' && character <= 'F')))
            {
                throw new ArgumentException(
                    "Expected an uppercase SHA-256 fingerprint.",
                    parameterName);
            }
        }

        return value;
    }

    internal static string Hash(params string[] values)
    {
        using var sha = SHA256.Create();
        var builder = new StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index] ?? string.Empty;
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public override string ToString()
        => "D043SpeechPublicationAuthority(sequence="
           + PublicationSequence
           + ",runtime="
           + RuntimeGeneration
           + ",language="
           + LanguageGeneration
           + ")";
}

internal sealed class D043SpeechSegmentPlan
{
    internal const int FirstSegmentMaximumTextElements = 120;
    internal const int SteadySegmentMaximumTextElements = 120;
    internal int Index { get; }
    internal string SegmentFingerprint { get; }
    internal int TextElementCount { get; }
    internal int Utf8ByteCount { get; }
    internal bool HasFirstSegmentPrelude { get; }

    internal D043SpeechSegmentPlan(
        int index,
        string segmentFingerprint,
        int textElementCount,
        int utf8ByteCount,
        bool hasFirstSegmentPrelude)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        var maximumTextElements = index == 0
            ? FirstSegmentMaximumTextElements
            : SteadySegmentMaximumTextElements;
        if (textElementCount < 1 || textElementCount > maximumTextElements)
            throw new ArgumentOutOfRangeException(nameof(textElementCount));
        if (utf8ByteCount < 1 || utf8ByteCount > 2 * 1024)
            throw new ArgumentOutOfRangeException(nameof(utf8ByteCount));
        if (hasFirstSegmentPrelude && index != 0)
            throw new ArgumentException("Only segment zero may own a prelude.", nameof(hasFirstSegmentPrelude));

        Index = index;
        SegmentFingerprint = D043AdaptiveTtsFingerprint.Require(
            segmentFingerprint,
            nameof(segmentFingerprint));
        TextElementCount = textElementCount;
        Utf8ByteCount = utf8ByteCount;
        HasFirstSegmentPrelude = hasFirstSegmentPrelude;
    }
}

internal sealed class D043SpeechEffect
{
    internal D043SpeechEffectKind Kind { get; }
    internal string UtteranceFingerprint { get; }
    internal string CommandFingerprint { get; }
    internal int SegmentIndex { get; }
    internal int Attempt { get; }
    internal string? AudioFingerprint { get; }
    internal int EncodedBytes { get; }
    internal long AudioDurationMicroseconds { get; }
    internal bool HasFirstSegmentPrelude { get; }
    internal D043SpeechTerminalKind TerminalKind { get; }
    internal D043SpeechCancellationReason TerminalReason { get; }

    internal D043SpeechEffect(
        D043SpeechEffectKind kind,
        string utteranceFingerprint,
        string commandFingerprint,
        int segmentIndex,
        int attempt,
        string? audioFingerprint,
        int encodedBytes,
        long audioDurationMicroseconds,
        bool hasFirstSegmentPrelude,
        D043SpeechTerminalKind terminalKind,
        D043SpeechCancellationReason terminalReason)
    {
        Kind = kind;
        UtteranceFingerprint = utteranceFingerprint;
        CommandFingerprint = commandFingerprint;
        SegmentIndex = segmentIndex;
        Attempt = attempt;
        AudioFingerprint = audioFingerprint;
        EncodedBytes = encodedBytes;
        AudioDurationMicroseconds = audioDurationMicroseconds;
        HasFirstSegmentPrelude = hasFirstSegmentPrelude;
        TerminalKind = terminalKind;
        TerminalReason = terminalReason;
    }

    public override string ToString()
        => "D043SpeechEffect(kind="
           + Kind
           + ",segment="
           + SegmentIndex
           + ",attempt="
           + Attempt
           + ",bytes="
           + EncodedBytes
           + ",terminal="
           + TerminalKind
           + ")";
}

internal sealed class D043SpeechSchedulerSnapshot
{
    internal bool IsTerminal { get; }
    internal D043SpeechTerminalKind TerminalKind { get; }
    internal D043SpeechCancellationReason TerminalReason { get; }
    internal int SegmentCount { get; }
    internal int CompletedPlaybackCount { get; }
    internal int? SynthesisSegmentIndex { get; }
    internal int? PlaybackSegmentIndex { get; }
    internal int ReadySegmentCount { get; }
    internal int BufferedEncodedBytes { get; }
    internal int PendingTextUtf8Bytes { get; }
    internal long LastMonotonicMicroseconds { get; }

    internal D043SpeechSchedulerSnapshot(
        bool isTerminal,
        D043SpeechTerminalKind terminalKind,
        D043SpeechCancellationReason terminalReason,
        int segmentCount,
        int completedPlaybackCount,
        int? synthesisSegmentIndex,
        int? playbackSegmentIndex,
        int readySegmentCount,
        int bufferedEncodedBytes,
        int pendingTextUtf8Bytes,
        long lastMonotonicMicroseconds)
    {
        IsTerminal = isTerminal;
        TerminalKind = terminalKind;
        TerminalReason = terminalReason;
        SegmentCount = segmentCount;
        CompletedPlaybackCount = completedPlaybackCount;
        SynthesisSegmentIndex = synthesisSegmentIndex;
        PlaybackSegmentIndex = playbackSegmentIndex;
        ReadySegmentCount = readySegmentCount;
        BufferedEncodedBytes = bufferedEncodedBytes;
        PendingTextUtf8Bytes = pendingTextUtf8Bytes;
        LastMonotonicMicroseconds = lastMonotonicMicroseconds;
    }
}

internal sealed class D043SpeechSchedulerTransition
{
    internal D043SpeechEventDisposition Disposition { get; }
    internal IReadOnlyList<D043SpeechEffect> Effects { get; }
    internal D043SpeechSchedulerSnapshot Snapshot { get; }

    internal D043SpeechSchedulerTransition(
        D043SpeechEventDisposition disposition,
        IReadOnlyList<D043SpeechEffect> effects,
        D043SpeechSchedulerSnapshot snapshot)
    {
        Disposition = disposition;
        Effects = effects;
        Snapshot = snapshot;
    }
}

internal sealed class D043AdaptiveTtsEffectScheduler
{
    private sealed class ReadyAudio
    {
        internal D043SpeechSegmentPlan Plan { get; }
        internal D043AdmittedWavDescriptor Descriptor { get; }

        internal ReadyAudio(
            D043SpeechSegmentPlan plan,
            D043AdmittedWavDescriptor descriptor)
        {
            Plan = plan;
            Descriptor = descriptor;
        }
    }

    private readonly D043SpeechPublicationAuthority _authority;
    private readonly ReadOnlyCollection<D043SpeechSegmentPlan> _plans;
    private readonly D043SpeechSchedulerCaps _caps;
    private readonly Queue<ReadyAudio> _ready = new();
    private readonly long _deadlineMicroseconds;
    private long _lastMicroseconds;
    private int _nextSynthesisIndex;
    private int _completedPlaybackCount;
    private int _bufferedBytes;
    private int _pendingTextUtf8Bytes;
    private int? _synthesisIndex;
    private string? _synthesisCommand;
    private int? _playbackIndex;
    private string? _playbackCommand;
    private ReadyAudio? _playing;
    private D043SpeechTerminalKind _terminalKind;
    private D043SpeechCancellationReason _terminalReason;
    private bool _terminalReceiptEmitted;

    private D043AdaptiveTtsEffectScheduler(
        D043SpeechPublicationAuthority authority,
        ReadOnlyCollection<D043SpeechSegmentPlan> plans,
        long startedAtMicroseconds,
        long deadlineMicroseconds,
        D043SpeechSchedulerCaps caps)
    {
        _authority = authority;
        _plans = plans;
        _lastMicroseconds = startedAtMicroseconds;
        _deadlineMicroseconds = deadlineMicroseconds;
        _caps = caps;
    }

    internal static D043SpeechSchedulerTransition Start(
        D043SpeechPublicationAuthority authority,
        IReadOnlyList<D043SpeechSegmentPlan> plans,
        long startedAtMicroseconds,
        long lifetimeMicroseconds,
        D043SpeechSchedulerCaps caps,
        out D043AdaptiveTtsEffectScheduler? scheduler)
    {
        if (authority is null)
            throw new ArgumentNullException(nameof(authority));
        if (plans is null)
            throw new ArgumentNullException(nameof(plans));
        if (!caps.IsWithinV1Policy
            || startedAtMicroseconds < 0
            || lifetimeMicroseconds < 1
            || lifetimeMicroseconds > caps.MaximumUtteranceLifetimeMicroseconds
            || startedAtMicroseconds > long.MaxValue - lifetimeMicroseconds)
        {
            scheduler = null;
            return RejectedSnapshot(D043SpeechEventDisposition.InvalidEventRejected);
        }
        if (plans.Count < 1 || plans.Count > caps.MaximumSegments)
        {
            scheduler = null;
            return RejectedSnapshot(D043SpeechEventDisposition.InvalidEventRejected);
        }

        var copied = new D043SpeechSegmentPlan[plans.Count];
        var pendingTextUtf8Bytes = 0;
        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (plan is null
                || plan.Index != index
                || (plan.HasFirstSegmentPrelude && index != 0)
                || plan.TextElementCount < 1
                || plan.TextElementCount > (index == 0
                    ? D043SpeechSegmentPlan.FirstSegmentMaximumTextElements
                    : D043SpeechSegmentPlan.SteadySegmentMaximumTextElements)
                || plan.Utf8ByteCount < 1
                || plan.Utf8ByteCount > 2 * 1024)
            {
                scheduler = null;
                return RejectedSnapshot(D043SpeechEventDisposition.InvalidEventRejected);
            }
            copied[index] = plan;
            if (pendingTextUtf8Bytes > caps.MaximumPendingTextUtf8Bytes
                - plan.Utf8ByteCount)
            {
                scheduler = null;
                return RejectedSnapshot(D043SpeechEventDisposition.InvalidEventRejected);
            }
            pendingTextUtf8Bytes += plan.Utf8ByteCount;
        }

        scheduler = new D043AdaptiveTtsEffectScheduler(
            authority,
            Array.AsReadOnly(copied),
            startedAtMicroseconds,
            startedAtMicroseconds + lifetimeMicroseconds,
            caps);
        scheduler._pendingTextUtf8Bytes = pendingTextUtf8Bytes;
        var effects = new List<D043SpeechEffect>();
        scheduler.DispatchNextSynthesis(effects);
        return scheduler.Transition(D043SpeechEventDisposition.Applied, effects);
    }

    internal D043SpeechSchedulerTransition SynthesisSucceeded(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        D043AdmittedWavDescriptor descriptor,
        long monotonicMicroseconds)
    {
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (_synthesisIndex is null)
            return Transition(D043SpeechEventDisposition.LateOrDuplicateDropped);
        if (_synthesisIndex.Value != segmentIndex)
            return Transition(D043SpeechEventDisposition.OutOfOrderDropped);
        if (!string.Equals(_synthesisCommand, commandFingerprint, StringComparison.Ordinal))
            return Transition(D043SpeechEventDisposition.ForeignCommandDropped);
        if (!AdmitMonotonic(monotonicMicroseconds, out var timeTransition))
            return timeTransition!;

        _synthesisIndex = null;
        _synthesisCommand = null;
        _pendingTextUtf8Bytes = checked(
            _pendingTextUtf8Bytes - _plans[segmentIndex].Utf8ByteCount);
        if (descriptor is null
            || !descriptor.IsParserSealed
            || !D043BoundedWavAdmission.IsWithinV1PlaybackEnvelope(descriptor)
            || descriptor.EncodedBytes < 44
            || descriptor.EncodedBytes > _caps.MaximumEncodedBytesPerSegment
            || descriptor.DurationMicroseconds < 1
            || descriptor.EncodedSha256.Length != 64)
        {
            return Fail(D043SpeechCancellationReason.CapacityExceeded);
        }

        var projectedBytes = (long)_bufferedBytes + descriptor.EncodedBytes;
        if (projectedBytes > _caps.MaximumBufferedEncodedBytes
            || BufferedSegmentCount >= _caps.MaximumBufferedSegments)
        {
            return Fail(D043SpeechCancellationReason.CapacityExceeded);
        }

        _nextSynthesisIndex++;
        _ready.Enqueue(new ReadyAudio(_plans[segmentIndex], descriptor));
        _bufferedBytes = checked(_bufferedBytes + descriptor.EncodedBytes);

        var effects = new List<D043SpeechEffect>();
        StartReadyPlayback(effects);
        DispatchNextSynthesis(effects);
        return Transition(D043SpeechEventDisposition.Applied, effects);
    }

    internal D043SpeechSchedulerTransition SynthesisFailed(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (_synthesisIndex is null)
            return Transition(D043SpeechEventDisposition.LateOrDuplicateDropped);
        if (_synthesisIndex.Value != segmentIndex)
            return Transition(D043SpeechEventDisposition.OutOfOrderDropped);
        if (!string.Equals(_synthesisCommand, commandFingerprint, StringComparison.Ordinal))
            return Transition(D043SpeechEventDisposition.ForeignCommandDropped);
        if (!AdmitMonotonic(monotonicMicroseconds, out var timeTransition))
            return timeTransition!;
        _synthesisIndex = null;
        _synthesisCommand = null;
        _pendingTextUtf8Bytes = checked(
            _pendingTextUtf8Bytes - _plans[segmentIndex].Utf8ByteCount);
        return Fail(D043SpeechCancellationReason.SynthesisFailed);
    }

    internal D043SpeechSchedulerTransition PlaybackFailed(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (_playbackIndex is null)
            return Transition(D043SpeechEventDisposition.LateOrDuplicateDropped);
        if (_playbackIndex.Value != segmentIndex)
            return Transition(D043SpeechEventDisposition.OutOfOrderDropped);
        if (!string.Equals(_playbackCommand, commandFingerprint, StringComparison.Ordinal))
            return Transition(D043SpeechEventDisposition.ForeignCommandDropped);
        if (!AdmitMonotonic(monotonicMicroseconds, out var timeTransition))
            return timeTransition!;
        _playbackIndex = null;
        _playbackCommand = null;
        _playing = null;
        _ready.Clear();
        _bufferedBytes = 0;
        return Fail(D043SpeechCancellationReason.ProtocolViolation);
    }

    internal D043SpeechSchedulerTransition PlaybackCompleted(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (_playbackIndex is null)
            return Transition(D043SpeechEventDisposition.LateOrDuplicateDropped);
        if (_playbackIndex.Value != segmentIndex
            || segmentIndex != _completedPlaybackCount)
        {
            return Transition(D043SpeechEventDisposition.OutOfOrderDropped);
        }
        if (!string.Equals(_playbackCommand, commandFingerprint, StringComparison.Ordinal))
            return Transition(D043SpeechEventDisposition.ForeignCommandDropped);
        if (!AdmitMonotonic(monotonicMicroseconds, out var timeTransition))
            return timeTransition!;

        if (_playing is null)
            return Fail(D043SpeechCancellationReason.ProtocolViolation);
        _bufferedBytes = checked(_bufferedBytes - _playing.Descriptor.EncodedBytes);
        _playing = null;
        _playbackIndex = null;
        _playbackCommand = null;
        _completedPlaybackCount++;

        var effects = new List<D043SpeechEffect>();
        StartReadyPlayback(effects);
        DispatchNextSynthesis(effects);
        if (_completedPlaybackCount == _plans.Count
            && _nextSynthesisIndex == _plans.Count
            && _synthesisIndex is null
            && _playbackIndex is null
            && _ready.Count == 0)
        {
            SealTerminal(
                D043SpeechTerminalKind.Completed,
                D043SpeechCancellationReason.None,
                effects);
        }
        return Transition(D043SpeechEventDisposition.Applied, effects);
    }

    internal D043SpeechSchedulerTransition Cancel(
        string authorityFingerprint,
        D043SpeechCancellationReason reason,
        long monotonicMicroseconds)
    {
        if (!Enum.IsDefined(typeof(D043SpeechCancellationReason), reason)
            || reason is D043SpeechCancellationReason.None
            or D043SpeechCancellationReason.LifetimeExceeded
            or D043SpeechCancellationReason.CapacityExceeded
            or D043SpeechCancellationReason.SynthesisFailed
            or D043SpeechCancellationReason.MonotonicDiscontinuity
            or D043SpeechCancellationReason.ProtocolViolation)
        {
            return Transition(D043SpeechEventDisposition.InvalidEventRejected);
        }
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (!AdmitMonotonic(
                monotonicMicroseconds,
                out var timeTransition,
                enforceDeadline: false))
        {
            return timeTransition!;
        }
        return CancelInternal(reason, D043SpeechTerminalKind.Canceled);
    }

    internal D043SpeechSchedulerTransition Advance(
        string authorityFingerprint,
        long monotonicMicroseconds)
    {
        if (!PreflightIdentity(authorityFingerprint, out var identityTransition))
            return identityTransition!;
        if (!AdmitMonotonic(monotonicMicroseconds, out var timeTransition))
            return timeTransition!;
        return Transition(D043SpeechEventDisposition.Applied);
    }

    internal D043SpeechSchedulerSnapshot Snapshot => CreateSnapshot();

    private int BufferedSegmentCount => _ready.Count + (_playing is null ? 0 : 1);

    private bool PreflightIdentity(
        string authorityFingerprint,
        out D043SpeechSchedulerTransition? transition)
    {
        if (!string.Equals(
                authorityFingerprint,
                _authority.AuthorityFingerprint,
                StringComparison.Ordinal))
        {
            transition = Transition(D043SpeechEventDisposition.ForeignIdentityDropped);
            return false;
        }
        if (_terminalKind != D043SpeechTerminalKind.None)
        {
            transition = Transition(D043SpeechEventDisposition.LateOrDuplicateDropped);
            return false;
        }
        transition = null;
        return true;
    }

    private bool AdmitMonotonic(
        long monotonicMicroseconds,
        out D043SpeechSchedulerTransition? transition,
        bool enforceDeadline = true)
    {
        if (monotonicMicroseconds < _lastMicroseconds)
        {
            transition = CancelInternal(
                D043SpeechCancellationReason.MonotonicDiscontinuity,
                D043SpeechTerminalKind.Failed);
            return false;
        }
        _lastMicroseconds = monotonicMicroseconds;
        if (enforceDeadline && monotonicMicroseconds > _deadlineMicroseconds)
        {
            transition = CancelInternal(
                D043SpeechCancellationReason.LifetimeExceeded,
                D043SpeechTerminalKind.Failed);
            return false;
        }
        transition = null;
        return true;
    }

    private D043SpeechSchedulerTransition Fail(D043SpeechCancellationReason reason)
        => CancelInternal(reason, D043SpeechTerminalKind.Failed);

    private D043SpeechSchedulerTransition CancelInternal(
        D043SpeechCancellationReason reason,
        D043SpeechTerminalKind terminalKind)
    {
        var effects = new List<D043SpeechEffect>();
        if (_synthesisIndex is not null && _synthesisCommand is not null)
        {
            effects.Add(CreateEffect(
                D043SpeechEffectKind.CancelSynthesis,
                _synthesisIndex.Value,
                _synthesisCommand,
                null,
                0,
                0,
                false,
                D043SpeechTerminalKind.None,
                D043SpeechCancellationReason.None));
        }
        if (_playbackIndex is not null && _playbackCommand is not null)
        {
            effects.Add(CreateEffect(
                D043SpeechEffectKind.StopPlayback,
                _playbackIndex.Value,
                _playbackCommand,
                _playing?.Descriptor.EncodedSha256,
                _playing?.Descriptor.EncodedBytes ?? 0,
                _playing?.Descriptor.DurationMicroseconds ?? 0,
                _playing?.Plan.HasFirstSegmentPrelude ?? false,
                D043SpeechTerminalKind.None,
                D043SpeechCancellationReason.None));
        }

        _synthesisIndex = null;
        _synthesisCommand = null;
        _playbackIndex = null;
        _playbackCommand = null;
        _playing = null;
        _ready.Clear();
        _bufferedBytes = 0;
        _pendingTextUtf8Bytes = 0;
        SealTerminal(terminalKind, reason, effects);
        return Transition(D043SpeechEventDisposition.Applied, effects);
    }

    private void StartReadyPlayback(List<D043SpeechEffect> effects)
    {
        if (_playbackIndex is not null || _ready.Count == 0)
            return;
        var ready = _ready.Peek();
        if (ready.Plan.Index != _completedPlaybackCount)
            return;
        _ready.Dequeue();
        _playing = ready;
        _playbackIndex = ready.Plan.Index;
        _playbackCommand = Command(
            D043SpeechEffectKind.StartPlayback,
            ready.Plan.Index,
            1,
            ready.Descriptor.EncodedSha256);
        effects.Add(CreateEffect(
            D043SpeechEffectKind.StartPlayback,
            ready.Plan.Index,
            _playbackCommand,
            ready.Descriptor.EncodedSha256,
            ready.Descriptor.EncodedBytes,
            ready.Descriptor.DurationMicroseconds,
            ready.Plan.HasFirstSegmentPrelude,
            D043SpeechTerminalKind.None,
            D043SpeechCancellationReason.None));
    }

    private void DispatchNextSynthesis(List<D043SpeechEffect> effects)
    {
        if (_terminalKind != D043SpeechTerminalKind.None
            || _synthesisIndex is not null
            || _nextSynthesisIndex >= _plans.Count
            || BufferedSegmentCount >= _caps.MaximumBufferedSegments)
        {
            return;
        }

        var plan = _plans[_nextSynthesisIndex];
        _synthesisIndex = plan.Index;
        _synthesisCommand = Command(
            D043SpeechEffectKind.DispatchSynthesis,
            plan.Index,
            1,
            plan.SegmentFingerprint);
        effects.Add(CreateEffect(
            D043SpeechEffectKind.DispatchSynthesis,
            plan.Index,
            _synthesisCommand,
            null,
            0,
            0,
            plan.HasFirstSegmentPrelude,
            D043SpeechTerminalKind.None,
            D043SpeechCancellationReason.None));
    }

    private void SealTerminal(
        D043SpeechTerminalKind kind,
        D043SpeechCancellationReason reason,
        List<D043SpeechEffect> effects)
    {
        if (_terminalReceiptEmitted)
            return;
        _terminalKind = kind;
        _terminalReason = reason;
        _terminalReceiptEmitted = true;
        var command = Command(
            D043SpeechEffectKind.EmitTerminalReceipt,
            _completedPlaybackCount,
            1,
            kind + ":" + reason);
        effects.Add(CreateEffect(
            D043SpeechEffectKind.EmitTerminalReceipt,
            _completedPlaybackCount,
            command,
            null,
            0,
            0,
            false,
            kind,
            reason));
    }

    private string Command(
        D043SpeechEffectKind kind,
        int segmentIndex,
        int attempt,
        string binding)
        => D043SpeechPublicationAuthority.Hash(
            "d043-speech-effect-v1",
            _authority.AuthorityFingerprint,
            kind.ToString(),
            segmentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            binding);

    private D043SpeechEffect CreateEffect(
        D043SpeechEffectKind kind,
        int segmentIndex,
        string command,
        string? audioFingerprint,
        int encodedBytes,
        long audioDurationMicroseconds,
        bool hasFirstSegmentPrelude,
        D043SpeechTerminalKind terminalKind,
        D043SpeechCancellationReason terminalReason)
        => new(
            kind,
            _authority.UtteranceFingerprint,
            command,
            segmentIndex,
            1,
            audioFingerprint,
            encodedBytes,
            audioDurationMicroseconds,
            hasFirstSegmentPrelude,
            terminalKind,
            terminalReason);

    private D043SpeechSchedulerTransition Transition(
        D043SpeechEventDisposition disposition,
        List<D043SpeechEffect>? effects = null)
        => new(
            disposition,
            (effects ?? new List<D043SpeechEffect>()).AsReadOnly(),
            CreateSnapshot());

    private D043SpeechSchedulerSnapshot CreateSnapshot()
        => new(
            _terminalKind != D043SpeechTerminalKind.None,
            _terminalKind,
            _terminalReason,
            _plans.Count,
            _completedPlaybackCount,
            _synthesisIndex,
            _playbackIndex,
            _ready.Count,
            _bufferedBytes,
            _pendingTextUtf8Bytes,
            _lastMicroseconds);

    private static D043SpeechSchedulerTransition RejectedSnapshot(
        D043SpeechEventDisposition disposition)
        => new(
            disposition,
            Array.Empty<D043SpeechEffect>(),
            new D043SpeechSchedulerSnapshot(
                false,
                D043SpeechTerminalKind.None,
                D043SpeechCancellationReason.None,
                0,
                0,
                null,
                null,
                0,
                0,
                0,
                0));
}
