using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithTextInjector;

internal enum CodexBridgeQueueClassification
{
    Accepted = 1,
    Stale = 2,
    Unsupported = 3,
    Malformed = 4,
    ReadFailed = 5
}

internal enum CodexBridgeQueueEventKind
{
    UserPromptSubmit = 1,
    PermissionRequest = 2,
    Stop = 3
}

internal enum CodexBridgeQueueRetentionReason
{
    None = 0,
    Stale = 1,
    Unsupported = 2,
    Malformed = 3,
    ReadFailed = 4,
    ClockUntrusted = 5,
    FutureTimestamp = 6,
    TrackingCapacity = 7,
    AdmissionFailed = 8,
    DeleteFailed = 9,
    SourceSuperseded = 10
}

internal enum CodexBridgeQueueDeleteDisposition
{
    NotAttempted = 0,
    Deleted = 1,
    Superseded = 2,
    TransientFailure = 3
}

internal enum CodexBridgeQueueQuarantineDisposition
{
    Unauthorized = 0,
    Quarantined = 1,
    Superseded = 2,
    TransientFailure = 3
}

internal readonly struct CodexBridgeQueueClockSample
{
    internal DateTimeOffset UtcNow { get; }
    internal long MonotonicTimeSpanTicks { get; }

    internal CodexBridgeQueueClockSample(
        DateTimeOffset utcNow,
        long monotonicTimeSpanTicks)
    {
        if (monotonicTimeSpanTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monotonicTimeSpanTicks));
        }

        UtcNow = utcNow.ToUniversalTime();
        MonotonicTimeSpanTicks = monotonicTimeSpanTicks;
    }
}

internal interface ICodexBridgeQueueClock
{
    CodexBridgeQueueClockSample Capture();
}

internal sealed class CodexBridgeQueueSourceRevision
{
    private static readonly byte[] FingerprintDomain =
        Encoding.UTF8.GetBytes("Lilith.CodexBridge.Source.v1\0");
    private readonly byte[] _contentDigest;

    private CodexBridgeQueueSourceRevision(byte[] contentDigest)
    {
        _contentDigest = contentDigest;
    }

    internal static CodexBridgeQueueSourceRevision FromJson(string json)
    {
        if (json == null)
            throw new ArgumentNullException(nameof(json));
        return new CodexBridgeQueueSourceRevision(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal bool Matches(CodexBridgeQueueSourceRevision? other)
        => other != null
            && CryptographicOperations.FixedTimeEquals(
                _contentDigest,
                other._contentDigest);

    internal string CreateQueueItemFingerprint(string queueItemIdentity)
    {
        if (string.IsNullOrWhiteSpace(queueItemIdentity))
        {
            throw new ArgumentException(
                "A queue-item identity is required.",
                nameof(queueItemIdentity));
        }

        var identityBytes = Encoding.UTF8.GetBytes(queueItemIdentity);
        var input = new byte[
            FingerprintDomain.Length
            + identityBytes.Length
            + _contentDigest.Length];
        Buffer.BlockCopy(
            FingerprintDomain,
            0,
            input,
            0,
            FingerprintDomain.Length);
        Buffer.BlockCopy(
            identityBytes,
            0,
            input,
            FingerprintDomain.Length,
            identityBytes.Length);
        Buffer.BlockCopy(
            _contentDigest,
            0,
            input,
            FingerprintDomain.Length + identityBytes.Length,
            _contentDigest.Length);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}

internal readonly struct CodexBridgeQueueReadResult
{
    internal bool Succeeded { get; }
    internal string? Json { get; }
    internal CodexBridgeQueueSourceRevision? SourceRevision { get; }

    private CodexBridgeQueueReadResult(
        bool succeeded,
        string? json,
        CodexBridgeQueueSourceRevision? sourceRevision)
    {
        Succeeded = succeeded;
        Json = json;
        SourceRevision = sourceRevision;
    }

    internal static CodexBridgeQueueReadResult Success(string json)
        => new(
            succeeded: true,
            json ?? throw new ArgumentNullException(nameof(json)),
            CodexBridgeQueueSourceRevision.FromJson(json));

    internal static CodexBridgeQueueReadResult Failure()
        => new(
            succeeded: false,
            json: null,
            sourceRevision: null);
}

internal interface ICodexBridgeQueueFileAccess
{
    CodexBridgeQueueReadResult Read(
        string queueItemIdentity,
        int maximumCharacters);

    CodexBridgeQueueDeleteDisposition DeleteTerminal(
        string queueItemIdentity,
        CodexBridgeQueueSourceRevision expectedRevision);
}

internal interface ICodexBridgeQueueSignalAdmission
{
    CodexBridgeBoundedSignalQueue.AdmissionProof? TryAdmit(
        string queueItemFingerprint,
        CodexBridgeQueueSignal signal);

    void ConfirmSourceFinalized(
        CodexBridgeBoundedSignalQueue.AdmissionProof proof);
}

internal sealed class CodexBridgeQueueSignal
{
    internal CodexBridgeQueueEventKind EventKind { get; }
    internal string SessionId { get; }
    internal string TurnId { get; }
    internal bool ContainsRawContent => false;

    internal CodexBridgeQueueSignal(
        CodexBridgeQueueEventKind eventKind,
        string sessionId,
        string turnId)
    {
        if (!Enum.IsDefined(typeof(CodexBridgeQueueEventKind), eventKind))
            throw new ArgumentOutOfRangeException(nameof(eventKind));

        EventKind = eventKind;
        SessionId = BoundedOpaqueId(sessionId, nameof(sessionId));
        TurnId = BoundedOpaqueId(turnId, nameof(turnId));
    }

    private static string BoundedOpaqueId(string value, string parameterName)
    {
        if (value == null)
            throw new ArgumentNullException(parameterName);
        if (value.Length > CodexBridgeQueueConsumer.MaximumOpaqueIdCharacters)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal sealed class CodexBridgeBoundedSignalQueue
    : ICodexBridgeQueueSignalAdmission
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<AdmittedSignal> _signals = new();
    private readonly Dictionary<string, AdmissionProof>
        _admissionProofs =
        new(StringComparer.Ordinal);

    internal sealed class AdmissionProof
    {
        private readonly CodexBridgeBoundedSignalQueue _issuer;
        private readonly string _queueItemFingerprint;

        private AdmissionProof(
            CodexBridgeBoundedSignalQueue issuer,
            string queueItemFingerprint)
        {
            _issuer = issuer;
            _queueItemFingerprint = queueItemFingerprint;
        }

        internal static AdmissionProof Issue(
            CodexBridgeBoundedSignalQueue issuer,
            string queueItemFingerprint)
            => new(issuer, queueItemFingerprint);

        internal bool AuthorizesDeleteFor(
            ICodexBridgeQueueSignalAdmission admission,
            string queueItemFingerprint)
            => ReferenceEquals(_issuer, admission)
                && string.Equals(
                    _queueItemFingerprint,
                    queueItemFingerprint,
                    StringComparison.Ordinal);
    }

    internal CodexBridgeBoundedSignalQueue(int capacity)
    {
        if (capacity is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public AdmissionProof? TryAdmit(
        string queueItemFingerprint,
        CodexBridgeQueueSignal signal)
    {
        if (string.IsNullOrWhiteSpace(queueItemFingerprint))
        {
            throw new ArgumentException(
                "A content-free source fingerprint is required.",
                nameof(queueItemFingerprint));
        }
        if (signal == null)
            throw new ArgumentNullException(nameof(signal));

        lock (_gate)
        {
            if (_admissionProofs.TryGetValue(
                    queueItemFingerprint,
                    out var existingProof))
            {
                return existingProof;
            }
            if (_signals.Count >= _capacity
                || _admissionProofs.Count >= _capacity)
            {
                return null;
            }

            var proof = AdmissionProof.Issue(
                this,
                queueItemFingerprint);
            _signals.Enqueue(new AdmittedSignal(signal));
            _admissionProofs.Add(queueItemFingerprint, proof);
            return proof;
        }
    }

    public void ConfirmSourceFinalized(AdmissionProof proof)
    {
        if (proof == null)
            throw new ArgumentNullException(nameof(proof));

        lock (_gate)
        {
            string? matchedFingerprint = null;
            foreach (var pair in _admissionProofs)
            {
                if (ReferenceEquals(pair.Value, proof))
                {
                    matchedFingerprint = pair.Key;
                    break;
                }
            }

            if (matchedFingerprint != null)
                _admissionProofs.Remove(matchedFingerprint);
        }
    }

    internal bool TryDequeue(out CodexBridgeQueueSignal? signal)
    {
        lock (_gate)
        {
            if (!_signals.TryDequeue(out var admitted))
            {
                signal = null;
                return false;
            }

            signal = admitted.Signal;
            return true;
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _signals.Count;
        }
    }

    private sealed class AdmittedSignal
    {
        internal CodexBridgeQueueSignal Signal { get; }

        internal AdmittedSignal(CodexBridgeQueueSignal signal)
        {
            Signal = signal;
        }
    }
}

internal sealed class CodexBridgeQueueReceipt
{
    private readonly object _issuerCapability;
    internal long Sequence { get; }
    internal string QueueItemFingerprint { get; }
    internal CodexBridgeQueueClassification Classification { get; }
    internal CodexBridgeQueueEventKind? EventKind { get; }
    internal CodexBridgeQueueRetentionReason RetentionReason { get; }
    internal long ObservedAtMonotonicTimeSpanTicks { get; }
    internal bool IsTerminal { get; }
    internal bool AdmissionAttempted { get; }
    internal bool AdmissionSucceeded { get; }
    internal CodexBridgeQueueDeleteDisposition DeleteDisposition { get; }
    internal bool DeleteAttempted { get; }
    internal bool DeleteSucceeded { get; }
    internal bool QuarantineEligible { get; }
    internal bool ContainsRawContent => false;

    internal CodexBridgeQueueReceipt(
        object issuerCapability,
        long sequence,
        string queueItemFingerprint,
        CodexBridgeQueueClassification classification,
        CodexBridgeQueueEventKind? eventKind,
        CodexBridgeQueueRetentionReason retentionReason,
        long observedAtMonotonicTimeSpanTicks,
        bool isTerminal,
        bool admissionAttempted,
        bool admissionSucceeded,
        CodexBridgeQueueDeleteDisposition deleteDisposition,
        bool quarantineEligible)
    {
        _issuerCapability = issuerCapability
            ?? throw new ArgumentNullException(nameof(issuerCapability));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (string.IsNullOrWhiteSpace(queueItemFingerprint))
            throw new ArgumentException(
                "A content-free queue-item fingerprint is required.",
                nameof(queueItemFingerprint));
        if (!Enum.IsDefined(
                typeof(CodexBridgeQueueClassification),
                classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }
        if (eventKind.HasValue
            && !Enum.IsDefined(
                typeof(CodexBridgeQueueEventKind),
                eventKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(eventKind));
        }
        if (!Enum.IsDefined(
                typeof(CodexBridgeQueueRetentionReason),
                retentionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(retentionReason));
        }
        if (observedAtMonotonicTimeSpanTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAtMonotonicTimeSpanTicks));
        }
        if (!Enum.IsDefined(
                typeof(CodexBridgeQueueDeleteDisposition),
                deleteDisposition))
        {
            throw new ArgumentOutOfRangeException(nameof(deleteDisposition));
        }
        var deleteAttempted = deleteDisposition
            != CodexBridgeQueueDeleteDisposition.NotAttempted;
        var deleteSucceeded = deleteDisposition
            == CodexBridgeQueueDeleteDisposition.Deleted;
        if (admissionSucceeded && !admissionAttempted)
        {
            throw new ArgumentException(
                "A successful admission must have been attempted.",
                nameof(admissionSucceeded));
        }
        if (admissionAttempted
            && classification != CodexBridgeQueueClassification.Accepted)
        {
            throw new ArgumentException(
                "Only accepted events may enter signal admission.",
                nameof(admissionAttempted));
        }
        if (deleteAttempted
            && (classification != CodexBridgeQueueClassification.Accepted
                || !admissionSucceeded))
        {
            throw new ArgumentException(
                "Only an accepted and successfully admitted item may be deleted.",
                nameof(deleteAttempted));
        }
        if (classification == CodexBridgeQueueClassification.Stale
            && (admissionAttempted || deleteAttempted))
        {
            throw new ArgumentException(
                "Stale items must be retained for explicit quarantine policy.",
                nameof(classification));
        }
        var terminalDisposition = deleteDisposition is
            CodexBridgeQueueDeleteDisposition.Deleted
            or CodexBridgeQueueDeleteDisposition.Superseded;
        if (isTerminal != terminalDisposition)
        {
            throw new ArgumentException(
                "Terminal state must be backed by a final source disposition.",
                nameof(isTerminal));
        }

        Sequence = sequence;
        QueueItemFingerprint = queueItemFingerprint;
        Classification = classification;
        EventKind = eventKind;
        RetentionReason = retentionReason;
        ObservedAtMonotonicTimeSpanTicks =
            observedAtMonotonicTimeSpanTicks;
        IsTerminal = isTerminal;
        AdmissionAttempted = admissionAttempted;
        AdmissionSucceeded = admissionSucceeded;
        DeleteDisposition = deleteDisposition;
        DeleteAttempted = deleteAttempted;
        DeleteSucceeded = deleteSucceeded;
        QuarantineEligible = quarantineEligible;
    }

    internal bool AuthorizesQuarantineConfirmation(object issuerCapability)
        => QuarantineEligible
            && ReferenceEquals(_issuerCapability, issuerCapability);
}

internal sealed class CodexBridgeQueueOutcome
{
    internal CodexBridgeQueueReceipt Receipt { get; }

    internal CodexBridgeQueueOutcome(CodexBridgeQueueReceipt receipt)
    {
        Receipt = receipt
            ?? throw new ArgumentNullException(nameof(receipt));
    }
}

internal sealed class CodexBridgeQueueQuarantinePermit
{
    private readonly CodexBridgeQueueReceipt _receipt;
    private readonly object _issuerCapability;

    internal CodexBridgeQueueQuarantinePermit(
        CodexBridgeQueueReceipt receipt,
        object issuerCapability)
    {
        _receipt = receipt
            ?? throw new ArgumentNullException(nameof(receipt));
        _issuerCapability = issuerCapability
            ?? throw new ArgumentNullException(nameof(issuerCapability));
    }

    internal bool Authorizes(CodexBridgeQueueReceipt receipt)
        => ReferenceEquals(_receipt, receipt)
            && receipt.AuthorizesQuarantineConfirmation(
                _issuerCapability);
}

internal sealed class CodexBridgeQueueConsumer
{
    internal const int MaximumJsonCharacters = 4_096;
    internal const int MaximumOpaqueIdCharacters = 128;

    private readonly object _gate = new();
    private readonly ICodexBridgeQueueFileAccess _fileAccess;
    private readonly ICodexBridgeQueueSignalAdmission _signalAdmission;
    private readonly ICodexBridgeQueueClock _clock;
    private readonly long _maximumAgeTicks;
    private readonly long _maximumFutureSkewTicks;
    private readonly long _utcMonotonicDriftToleranceTicks;
    private readonly int _receiptCapacity;
    private readonly int _trackingCapacity;
    private readonly object _receiptIssuerCapability = new();
    private readonly List<CodexBridgeQueueReceipt> _receipts = new();
    private readonly Dictionary<string, long> _firstObservedAt =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _trackedSourceByIdentity =
        new(StringComparer.Ordinal);
    private readonly Dictionary<
        string,
        CodexBridgeBoundedSignalQueue.AdmissionProof>
        _admissionProofBySource = new(StringComparer.Ordinal);
    private long _nextReceiptSequence;
    private bool _utcTrusted;
    private bool _hasClockSample;
    private DateTimeOffset _lastUtc;
    private long _lastMonotonicTicks;

    internal CodexBridgeQueueConsumer(
        ICodexBridgeQueueFileAccess fileAccess,
        ICodexBridgeQueueSignalAdmission signalAdmission,
        ICodexBridgeQueueClock clock,
        TimeSpan maximumAge,
        TimeSpan maximumFutureSkew,
        TimeSpan utcMonotonicDriftTolerance,
        bool initialUtcTrusted,
        int receiptCapacity = 64,
        int trackingCapacity = 128)
    {
        _fileAccess = fileAccess
            ?? throw new ArgumentNullException(nameof(fileAccess));
        _signalAdmission = signalAdmission
            ?? throw new ArgumentNullException(nameof(signalAdmission));
        _clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
        if (maximumAge <= TimeSpan.Zero
            || maximumAge > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }
        if (maximumFutureSkew < TimeSpan.Zero
            || maximumFutureSkew > maximumAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFutureSkew));
        }
        if (utcMonotonicDriftTolerance < TimeSpan.Zero
            || utcMonotonicDriftTolerance > maximumAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(utcMonotonicDriftTolerance));
        }
        if (receiptCapacity is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(receiptCapacity));
        if (trackingCapacity is < 1 or > 1_024)
            throw new ArgumentOutOfRangeException(nameof(trackingCapacity));

        _maximumAgeTicks = maximumAge.Ticks;
        _maximumFutureSkewTicks = maximumFutureSkew.Ticks;
        _utcMonotonicDriftToleranceTicks =
            utcMonotonicDriftTolerance.Ticks;
        _utcTrusted = initialUtcTrusted;
        _receiptCapacity = receiptCapacity;
        _trackingCapacity = trackingCapacity;
    }

    internal CodexBridgeQueueOutcome Process(string queueItemIdentity)
    {
        if (string.IsNullOrWhiteSpace(queueItemIdentity)
            || queueItemIdentity.Length > 1_024)
        {
            throw new ArgumentException(
                "A bounded opaque queue-item identity is required.",
                nameof(queueItemIdentity));
        }

        lock (_gate)
        {
            var identityFingerprint = FingerprintIdentity(queueItemIdentity);
            CodexBridgeQueueReadResult read;
            try
            {
                read = _fileAccess.Read(
                    queueItemIdentity,
                    MaximumJsonCharacters);
            }
            catch
            {
                return Retained(
                    identityFingerprint,
                    CodexBridgeQueueClassification.ReadFailed,
                    eventKind: null,
                    CodexBridgeQueueRetentionReason.ReadFailed,
                    quarantineEligible: false);
            }

            if (!read.Succeeded || read.Json == null)
            {
                return Retained(
                    identityFingerprint,
                    CodexBridgeQueueClassification.ReadFailed,
                    eventKind: null,
                    CodexBridgeQueueRetentionReason.ReadFailed,
                    quarantineEligible: false);
            }

            var sourceRevision = read.SourceRevision!;
            var fingerprint = sourceRevision
                .CreateQueueItemFingerprint(queueItemIdentity);

            var parsed = Parse(read.Json);
            if (parsed.Classification
                == CodexBridgeQueueClassification.Unsupported)
            {
                return Retained(
                    fingerprint,
                    parsed.Classification,
                    parsed.EventKind,
                    CodexBridgeQueueRetentionReason.Unsupported,
                    quarantineEligible: true);
            }
            if (parsed.Classification
                == CodexBridgeQueueClassification.Malformed)
            {
                return Retained(
                    fingerprint,
                    parsed.Classification,
                    parsed.EventKind,
                    CodexBridgeQueueRetentionReason.Malformed,
                    quarantineEligible: true);
            }

            var time = EvaluateTime(
                identityFingerprint,
                fingerprint,
                parsed.PublishedAtUtc!.Value);
            if (!time.IsTerminal)
            {
                return AddOutcome(
                    fingerprint,
                    CodexBridgeQueueClassification.Accepted,
                    parsed.EventKind,
                    time.RetentionReason,
                    time.ObservedAtMonotonicTicks,
                    isTerminal: false,
                    admissionAttempted: false,
                    admissionSucceeded: false,
                    deleteDisposition:
                        CodexBridgeQueueDeleteDisposition.NotAttempted,
                    quarantineEligible: false);
            }

            if (time.IsStale)
            {
                return AddOutcome(
                    fingerprint,
                    CodexBridgeQueueClassification.Stale,
                    parsed.EventKind,
                    CodexBridgeQueueRetentionReason.Stale,
                    time.ObservedAtMonotonicTicks,
                    isTerminal: false,
                    admissionAttempted: false,
                    admissionSucceeded: false,
                    deleteDisposition:
                        CodexBridgeQueueDeleteDisposition.NotAttempted,
                    quarantineEligible: true);
            }

            CodexBridgeBoundedSignalQueue.AdmissionProof?
                admissionProof = null;
            try
            {
                admissionProof = _signalAdmission.TryAdmit(
                    fingerprint,
                    parsed.Signal!);
            }
            catch
            {
                admissionProof = null;
            }
            var admissionSucceeded = admissionProof != null
                && admissionProof.AuthorizesDeleteFor(
                    _signalAdmission,
                    fingerprint);
            if (!admissionSucceeded)
            {
                return AddOutcome(
                    fingerprint,
                    CodexBridgeQueueClassification.Accepted,
                    parsed.EventKind,
                    CodexBridgeQueueRetentionReason.AdmissionFailed,
                    time.ObservedAtMonotonicTicks,
                    isTerminal: false,
                    admissionAttempted: true,
                    admissionSucceeded: false,
                    deleteDisposition:
                        CodexBridgeQueueDeleteDisposition.NotAttempted,
                    quarantineEligible: false);
            }
            _admissionProofBySource[fingerprint] = admissionProof!;

            var deleteDisposition =
                CodexBridgeQueueDeleteDisposition.TransientFailure;
            try
            {
                deleteDisposition = _fileAccess.DeleteTerminal(
                    queueItemIdentity,
                    sourceRevision);
                if (!Enum.IsDefined(
                        typeof(CodexBridgeQueueDeleteDisposition),
                        deleteDisposition)
                    || deleteDisposition
                        == CodexBridgeQueueDeleteDisposition.NotAttempted)
                {
                    deleteDisposition =
                        CodexBridgeQueueDeleteDisposition.TransientFailure;
                }
            }
            catch
            {
                deleteDisposition =
                    CodexBridgeQueueDeleteDisposition.TransientFailure;
            }

            var sourceFinalized = deleteDisposition is
                CodexBridgeQueueDeleteDisposition.Deleted
                or CodexBridgeQueueDeleteDisposition.Superseded;
            if (sourceFinalized)
            {
                ReleaseTrackedSource(fingerprint);
                try
                {
                    ReleaseAdmissionProof(fingerprint, admissionProof!);
                }
                catch
                {
                    // Admission already succeeded and this source revision is
                    // either gone or no longer current at its queue identity.
                    // Confirmation is only an in-memory dedupe release.
                }
            }

            return AddOutcome(
                fingerprint,
                CodexBridgeQueueClassification.Accepted,
                parsed.EventKind,
                deleteDisposition switch
                {
                    CodexBridgeQueueDeleteDisposition.Deleted =>
                        CodexBridgeQueueRetentionReason.None,
                    CodexBridgeQueueDeleteDisposition.Superseded =>
                        CodexBridgeQueueRetentionReason.SourceSuperseded,
                    _ => CodexBridgeQueueRetentionReason.DeleteFailed
                },
                time.ObservedAtMonotonicTicks,
                isTerminal: sourceFinalized,
                admissionAttempted: true,
                admissionSucceeded: true,
                deleteDisposition: deleteDisposition,
                quarantineEligible: false);
        }
    }

    internal ReadOnlyCollection<CodexBridgeQueueReceipt> GetReceipts()
    {
        lock (_gate)
            return Array.AsReadOnly(_receipts.ToArray());
    }

    internal CodexBridgeQueueQuarantinePermit?
        TryCreateQuarantinePermit(CodexBridgeQueueReceipt receipt)
    {
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));

        lock (_gate)
        {
            return receipt.AuthorizesQuarantineConfirmation(
                _receiptIssuerCapability)
                ? new CodexBridgeQueueQuarantinePermit(
                    receipt,
                    _receiptIssuerCapability)
                : null;
        }
    }

    internal bool ConfirmRejectedSourceFinalized(
        CodexBridgeQueueQuarantinePermit permit,
        CodexBridgeQueueReceipt receipt)
    {
        if (permit == null)
            throw new ArgumentNullException(nameof(permit));
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));

        lock (_gate)
        {
            if (!permit.Authorizes(receipt)
                || !receipt.AuthorizesQuarantineConfirmation(
                    _receiptIssuerCapability))
            {
                return false;
            }

            ReleaseTrackedSource(receipt.QueueItemFingerprint);
            return true;
        }
    }

    private CodexBridgeQueueOutcome Retained(
        string fingerprint,
        CodexBridgeQueueClassification classification,
        CodexBridgeQueueEventKind? eventKind,
        CodexBridgeQueueRetentionReason retentionReason,
        bool quarantineEligible)
    {
        var observedAt = TryCaptureMonotonicTicks();
        return AddOutcome(
            fingerprint,
            classification,
            eventKind,
            retentionReason,
            observedAt,
            isTerminal: false,
            admissionAttempted: false,
            admissionSucceeded: false,
            deleteDisposition:
                CodexBridgeQueueDeleteDisposition.NotAttempted,
            quarantineEligible);
    }

    private CodexBridgeQueueOutcome AddOutcome(
        string fingerprint,
        CodexBridgeQueueClassification classification,
        CodexBridgeQueueEventKind? eventKind,
        CodexBridgeQueueRetentionReason retentionReason,
        long observedAtMonotonicTicks,
        bool isTerminal,
        bool admissionAttempted,
        bool admissionSucceeded,
        CodexBridgeQueueDeleteDisposition deleteDisposition,
        bool quarantineEligible)
    {
        var receipt = new CodexBridgeQueueReceipt(
            _receiptIssuerCapability,
            checked(++_nextReceiptSequence),
            fingerprint,
            classification,
            eventKind,
            retentionReason,
            observedAtMonotonicTicks,
            isTerminal,
            admissionAttempted,
            admissionSucceeded,
            deleteDisposition,
            quarantineEligible);
        _receipts.Add(receipt);
        if (_receipts.Count > _receiptCapacity)
            _receipts.RemoveAt(0);
        return new CodexBridgeQueueOutcome(receipt);
    }

    private TimeEvaluation EvaluateTime(
        string identityFingerprint,
        string fingerprint,
        DateTimeOffset publishedAtUtc)
    {
        if (!TryTrackSourceRevision(identityFingerprint, fingerprint))
        {
            return new TimeEvaluation(
                isTerminal: false,
                isStale: false,
                observedAtMonotonicTicks: 0,
                retentionReason:
                    CodexBridgeQueueRetentionReason.TrackingCapacity);
        }

        CodexBridgeQueueClockSample sample;
        try
        {
            sample = _clock.Capture();
        }
        catch
        {
            _utcTrusted = false;
            return PendingTime(
                fingerprint,
                observedAtMonotonicTicks: 0,
                CodexBridgeQueueRetentionReason.ClockUntrusted);
        }

        ObserveClockContinuity(sample);
        if (!_firstObservedAt.TryGetValue(
                fingerprint,
                out var firstObservedAt))
        {
            if (_firstObservedAt.Count >= _trackingCapacity)
            {
                return new TimeEvaluation(
                    isTerminal: false,
                    isStale: false,
                    sample.MonotonicTimeSpanTicks,
                    CodexBridgeQueueRetentionReason.TrackingCapacity);
            }

            firstObservedAt = sample.MonotonicTimeSpanTicks;
            _firstObservedAt.Add(fingerprint, firstObservedAt);
        }

        if (!_utcTrusted)
        {
            return MonotonicPendingOrStale(
                firstObservedAt,
                sample.MonotonicTimeSpanTicks,
                CodexBridgeQueueRetentionReason.ClockUntrusted);
        }

        long ageTicks;
        try
        {
            ageTicks = checked(
                (sample.UtcNow - publishedAtUtc.ToUniversalTime()).Ticks);
        }
        catch
        {
            return PendingTime(
                fingerprint,
                sample.MonotonicTimeSpanTicks,
                CodexBridgeQueueRetentionReason.ClockUntrusted);
        }

        if (ageTicks > _maximumAgeTicks)
        {
            return new TimeEvaluation(
                isTerminal: true,
                isStale: true,
                sample.MonotonicTimeSpanTicks,
                CodexBridgeQueueRetentionReason.None);
        }
        if (ageTicks < -_maximumFutureSkewTicks)
        {
            return MonotonicPendingOrStale(
                firstObservedAt,
                sample.MonotonicTimeSpanTicks,
                CodexBridgeQueueRetentionReason.FutureTimestamp);
        }

        return new TimeEvaluation(
            isTerminal: true,
            isStale: false,
            sample.MonotonicTimeSpanTicks,
            CodexBridgeQueueRetentionReason.None);
    }

    private bool TryTrackSourceRevision(
        string identityFingerprint,
        string sourceFingerprint)
    {
        if (_trackedSourceByIdentity.TryGetValue(
                identityFingerprint,
                out var previousSourceFingerprint))
        {
            if (!string.Equals(
                    previousSourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal))
            {
                _firstObservedAt.Remove(previousSourceFingerprint);
                if (_admissionProofBySource.TryGetValue(
                        previousSourceFingerprint,
                        out var supersededProof))
                {
                    try
                    {
                        ReleaseAdmissionProof(
                            previousSourceFingerprint,
                            supersededProof);
                    }
                    catch
                    {
                        // Fail closed: the new revision may continue through
                        // time classification, but bounded admission will
                        // reject it if the old proof still owns capacity.
                    }
                }
                _trackedSourceByIdentity[identityFingerprint] =
                    sourceFingerprint;
            }
            return true;
        }

        if (_trackedSourceByIdentity.Count >= _trackingCapacity)
            return false;
        _trackedSourceByIdentity.Add(
            identityFingerprint,
            sourceFingerprint);
        return true;
    }

    private void ReleaseTrackedSource(string sourceFingerprint)
    {
        _firstObservedAt.Remove(sourceFingerprint);
        string? identityToRemove = null;
        foreach (var pair in _trackedSourceByIdentity)
        {
            if (string.Equals(
                    pair.Value,
                    sourceFingerprint,
                    StringComparison.Ordinal))
            {
                identityToRemove = pair.Key;
                break;
            }
        }

        if (identityToRemove != null)
            _trackedSourceByIdentity.Remove(identityToRemove);
    }

    private void ReleaseAdmissionProof(
        string sourceFingerprint,
        CodexBridgeBoundedSignalQueue.AdmissionProof proof)
    {
        _signalAdmission.ConfirmSourceFinalized(proof);
        _admissionProofBySource.Remove(sourceFingerprint);
    }

    private void ObserveClockContinuity(
        CodexBridgeQueueClockSample sample)
    {
        if (_hasClockSample)
        {
            var monotonicDelta =
                sample.MonotonicTimeSpanTicks - _lastMonotonicTicks;
            long utcDelta;
            try
            {
                utcDelta = checked((sample.UtcNow - _lastUtc).Ticks);
            }
            catch
            {
                utcDelta = long.MinValue;
            }

            if (monotonicDelta < 0
                || utcDelta < 0
                || AbsoluteDifference(utcDelta, monotonicDelta)
                    > _utcMonotonicDriftToleranceTicks)
            {
                _utcTrusted = false;
            }
        }

        _lastUtc = sample.UtcNow;
        _lastMonotonicTicks = sample.MonotonicTimeSpanTicks;
        _hasClockSample = true;
    }

    private TimeEvaluation PendingTime(
        string fingerprint,
        long observedAtMonotonicTicks,
        CodexBridgeQueueRetentionReason reason)
    {
        if (!_firstObservedAt.TryGetValue(
                fingerprint,
                out var firstObservedAt))
        {
            if (_firstObservedAt.Count >= _trackingCapacity)
            {
                return new TimeEvaluation(
                    isTerminal: false,
                    isStale: false,
                    observedAtMonotonicTicks,
                    CodexBridgeQueueRetentionReason.TrackingCapacity);
            }

            firstObservedAt = observedAtMonotonicTicks;
            _firstObservedAt.Add(fingerprint, firstObservedAt);
        }

        return MonotonicPendingOrStale(
            firstObservedAt,
            observedAtMonotonicTicks,
            reason);
    }

    private TimeEvaluation MonotonicPendingOrStale(
        long firstObservedAt,
        long observedAt,
        CodexBridgeQueueRetentionReason pendingReason)
    {
        var elapsed = observedAt >= firstObservedAt
            ? observedAt - firstObservedAt
            : 0;
        return elapsed >= _maximumAgeTicks
            ? new TimeEvaluation(
                isTerminal: true,
                isStale: true,
                observedAt,
                CodexBridgeQueueRetentionReason.None)
            : new TimeEvaluation(
                isTerminal: false,
                isStale: false,
                observedAt,
                pendingReason);
    }

    private long TryCaptureMonotonicTicks()
    {
        try
        {
            return _clock.Capture().MonotonicTimeSpanTicks;
        }
        catch
        {
            return 0;
        }
    }

    private static ParsedEvent Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)
            || json.Length > MaximumJsonCharacters)
        {
            return ParsedEvent.Malformed();
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 4
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ParsedEvent.Malformed();

            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasUnknownProperty = false;
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    return ParsedEvent.Malformed();
                if (property.Name is not (
                    "version"
                    or "hookEventName"
                    or "sessionId"
                    or "turnId"
                    or "timestampUtc"))
                {
                    hasUnknownProperty = true;
                }
            }

            if (!root.TryGetProperty("version", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
            {
                return ParsedEvent.Malformed();
            }
            if (version != 1)
                return ParsedEvent.Unsupported(eventKind: null);

            if (!TryBoundedString(
                    root,
                    "hookEventName",
                    maximumCharacters: 64,
                    out var eventName))
            {
                return ParsedEvent.Malformed();
            }
            if (!TryEventKind(eventName!, out var eventKind))
                return ParsedEvent.Unsupported(eventKind: null);
            if (hasUnknownProperty)
                return ParsedEvent.Unsupported(eventKind);

            if (!TryBoundedString(
                    root,
                    "sessionId",
                    MaximumOpaqueIdCharacters,
                    out var sessionId)
                || !TryBoundedString(
                    root,
                    "turnId",
                    MaximumOpaqueIdCharacters,
                    out var turnId)
                || !TryBoundedString(
                    root,
                    "timestampUtc",
                    maximumCharacters: 64,
                    out var timestampText)
                || !DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var publishedAtUtc))
            {
                return ParsedEvent.Malformed(eventKind);
            }

            return ParsedEvent.Accepted(
                eventKind,
                sessionId!,
                turnId!,
                publishedAtUtc.ToUniversalTime());
        }
        catch (JsonException)
        {
            return ParsedEvent.Malformed();
        }
        catch (ArgumentException)
        {
            return ParsedEvent.Malformed();
        }
    }

    private static bool TryBoundedString(
        JsonElement root,
        string propertyName,
        int maximumCharacters,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value != null && value.Length <= maximumCharacters;
    }

    private static bool TryEventKind(
        string value,
        out CodexBridgeQueueEventKind eventKind)
    {
        eventKind = value switch
        {
            "UserPromptSubmit" =>
                CodexBridgeQueueEventKind.UserPromptSubmit,
            "PermissionRequest" =>
                CodexBridgeQueueEventKind.PermissionRequest,
            "Stop" => CodexBridgeQueueEventKind.Stop,
            _ => default
        };
        return eventKind != default;
    }

    private static string FingerprintIdentity(string value)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Lilith.CodexBridge.Identity.v1\0" + value));
        return Convert.ToHexString(bytes);
    }

    private static long AbsoluteDifference(long left, long right)
    {
        var difference = (decimal)left - right;
        return difference >= long.MaxValue
            ? long.MaxValue
            : difference <= long.MinValue
                ? long.MaxValue
                : (long)Math.Abs(difference);
    }

    private readonly struct TimeEvaluation
    {
        internal bool IsTerminal { get; }
        internal bool IsStale { get; }
        internal long ObservedAtMonotonicTicks { get; }
        internal CodexBridgeQueueRetentionReason RetentionReason { get; }

        internal TimeEvaluation(
            bool isTerminal,
            bool isStale,
            long observedAtMonotonicTicks,
            CodexBridgeQueueRetentionReason retentionReason)
        {
            IsTerminal = isTerminal;
            IsStale = isStale;
            ObservedAtMonotonicTicks = observedAtMonotonicTicks;
            RetentionReason = retentionReason;
        }
    }

    private sealed class ParsedEvent
    {
        internal CodexBridgeQueueClassification Classification { get; }
        internal CodexBridgeQueueEventKind? EventKind { get; }
        internal DateTimeOffset? PublishedAtUtc { get; }
        internal CodexBridgeQueueSignal? Signal { get; }

        private ParsedEvent(
            CodexBridgeQueueClassification classification,
            CodexBridgeQueueEventKind? eventKind,
            DateTimeOffset? publishedAtUtc,
            CodexBridgeQueueSignal? signal)
        {
            Classification = classification;
            EventKind = eventKind;
            PublishedAtUtc = publishedAtUtc;
            Signal = signal;
        }

        internal static ParsedEvent Accepted(
            CodexBridgeQueueEventKind eventKind,
            string sessionId,
            string turnId,
            DateTimeOffset publishedAtUtc)
            => new(
                CodexBridgeQueueClassification.Accepted,
                eventKind,
                publishedAtUtc,
                new CodexBridgeQueueSignal(
                    eventKind,
                    sessionId,
                    turnId));

        internal static ParsedEvent Unsupported(
            CodexBridgeQueueEventKind? eventKind)
            => new(
                CodexBridgeQueueClassification.Unsupported,
                eventKind,
                publishedAtUtc: null,
                signal: null);

        internal static ParsedEvent Malformed(
            CodexBridgeQueueEventKind? eventKind = null)
            => new(
                CodexBridgeQueueClassification.Malformed,
                eventKind,
                publishedAtUtc: null,
                signal: null);
    }
}
