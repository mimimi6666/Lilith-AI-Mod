using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record LilithGameTurnOutcome(
    bool Succeeded,
    bool UserCommitted,
    LilithGameBridgeTerminal Terminal,
    string AssistantText,
    string SpeechText,
    string Code)
{
    internal string SpeechEmotion { get; init; } = string.Empty;
    internal LilithMiniGameDiagnosticReceipt? MiniGameDiagnostic { get; init; }
    internal LilithMiniGameEventReceipt? MiniGameEventReceipt { get; init; }
    internal LilithGameTurnOutcome(
        bool succeeded,
        bool userCommitted,
        LilithGameBridgeTerminal terminal,
        string assistantText,
        string code)
        : this(
            succeeded,
            userCommitted,
            terminal,
            assistantText,
            succeeded ? assistantText : string.Empty,
            code)
    {
    }
}

internal sealed record LilithGameTerminalEvent(
    string Status,
    string Text,
    JsonElement Payload);

internal sealed record LilithGameHistoryPage(
    IReadOnlyList<LilithGameBridgeHistoryEntry> Entries,
    string NextCursor,
    bool HasMore,
    string Code)
{
    internal bool Succeeded => string.Equals(Code, "ok", StringComparison.Ordinal);
}

internal sealed record LilithGameNotificationPage(
    IReadOnlyList<LilithGameBridgeNotificationEntry> Entries,
    long OldestAvailableSequence,
    long LatestSequence,
    bool HasMore,
    string Code)
{
    internal bool Succeeded => string.Equals(Code, "ok", StringComparison.Ordinal);
}

internal sealed record LilithGameNativeAssistantCommitOutcome(
    bool Accepted,
    bool AlreadyCommitted,
    bool Retryable,
    string Code)
{
    internal bool MiniGameEventsPending { get; init; }
    internal LilithMiniGameEventReceipt? MiniGameEventReceipt { get; init; }
}

internal sealed record LilithGameForegroundProactiveOutcome(
    bool Accepted,
    bool Succeeded,
    LilithGameBridgeForegroundProactiveState State,
    bool AlreadyProcessed,
    bool Retryable,
    string Code)
{
    internal LilithMiniGameEventReceipt? MiniGameEventReceipt { get; init; }
}

internal sealed record NativeAssistantCommitReceipt(
    bool AlreadyCommitted,
    string MessageId);

internal sealed record LilithGameVoicePeerIdentity(
    uint ProcessId,
    ulong ProcessCreationTime,
    CancellationToken PeerExited)
{
    internal LilithGameVoicePeerIdentity Validate()
    {
        if (ProcessId == 0 || ProcessCreationTime == 0
            || !PeerExited.CanBeCanceled)
        {
            throw new ArgumentException(
                "An exact live game peer identity is required.");
        }
        return this;
    }
}

internal sealed record LilithGameVoiceStartOutcome(bool Succeeded, string Code);

internal sealed record LilithGameVoiceStopOutcome(
    bool Succeeded,
    LilithGameBridgeTerminal Terminal,
    string Transcript,
    string Code);

internal sealed class LilithGameVoiceTerminalClaim
{
    private int _claimed;

    internal bool TryClaim()
        => Interlocked.Exchange(ref _claimed, 1) == 0;
}

internal sealed class LilithGameVoiceAdmissionTombstones
{
    private const int MaximumCancelledCapturesPerGeneration = 4_096;
    private readonly object _sync = new();
    private readonly Guid _generationId;
    private readonly string _sessionScope;
    private readonly HashSet<CancelledCapture> _cancelled = new();
    private readonly HashSet<CancelledCapture> _admitted = new();
    private bool _saturated;

    internal LilithGameVoiceAdmissionTombstones(
        Guid generationId,
        string sessionScope)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("A Runtime generation is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        _generationId = generationId;
        _sessionScope = sessionScope;
    }

    internal LilithGameVoiceCancelAdmissionDisposition RecordCancel(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime)
    {
        var key = Key(captureId, peerProcessId, peerProcessCreationTime);
        lock (_sync)
        {
            if (_saturated)
                return LilithGameVoiceCancelAdmissionDisposition.RecoveryRequired;
            if (_cancelled.Contains(key))
            {
                return _admitted.Contains(key)
                    ? LilithGameVoiceCancelAdmissionDisposition.ActiveAdmission
                    : LilithGameVoiceCancelAdmissionDisposition.CancelledBeforeAdmission;
            }
            if (_cancelled.Count >= MaximumCancelledCapturesPerGeneration)
            {
                // A trusted peer flooding unique out-of-order cancellations
                // cannot evict an older safety tombstone and resurrect its
                // already-admitted Start. Fail this generation closed.
                _saturated = true;
                _cancelled.Clear();
                return LilithGameVoiceCancelAdmissionDisposition.RecoveryRequired;
            }
            _cancelled.Add(key);
            return _admitted.Contains(key)
                ? LilithGameVoiceCancelAdmissionDisposition.ActiveAdmission
                : LilithGameVoiceCancelAdmissionDisposition.CancelledBeforeAdmission;
        }
    }

    internal LilithGameVoiceAdmissionDisposition AdmitStart(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime)
    {
        var key = Key(captureId, peerProcessId, peerProcessCreationTime);
        lock (_sync)
        {
            if (_saturated)
                return LilithGameVoiceAdmissionDisposition.RecoveryRequired;
            return _cancelled.Contains(key)
                ? LilithGameVoiceAdmissionDisposition.Cancelled
                : LilithGameVoiceAdmissionDisposition.Admitted;
        }
    }

    internal LilithGameVoiceAdmissionDisposition CommitStart(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime)
    {
        var key = Key(captureId, peerProcessId, peerProcessCreationTime);
        lock (_sync)
        {
            if (_saturated)
                return LilithGameVoiceAdmissionDisposition.RecoveryRequired;
            if (_cancelled.Contains(key))
                return LilithGameVoiceAdmissionDisposition.Cancelled;
            _admitted.Add(key);
            return LilithGameVoiceAdmissionDisposition.Admitted;
        }
    }

    internal void ReleaseStart(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime)
    {
        var key = Key(captureId, peerProcessId, peerProcessCreationTime);
        lock (_sync)
            _admitted.Remove(key);
    }

    internal LilithGameVoiceTerminalAdmissionDisposition ClaimTerminal(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime,
        Func<bool> tryClaim)
    {
        ArgumentNullException.ThrowIfNull(tryClaim);
        var key = Key(captureId, peerProcessId, peerProcessCreationTime);
        lock (_sync)
        {
            // RecordCancel uses this same lock before waiting for _voiceGate.
            // Therefore cancellation intent and terminal publication have one
            // linearization point: intent-first can never publish Completed.
            var cancelled = _saturated || _cancelled.Contains(key);
            if (!tryClaim())
                return LilithGameVoiceTerminalAdmissionDisposition.AlreadyClaimed;
            return cancelled
                ? LilithGameVoiceTerminalAdmissionDisposition.Cancelled
                : LilithGameVoiceTerminalAdmissionDisposition.Completed;
        }
    }

    private CancelledCapture Key(
        Guid captureId,
        uint peerProcessId,
        ulong peerProcessCreationTime)
    {
        if (captureId == Guid.Empty
            || peerProcessId == 0
            || peerProcessCreationTime == 0)
        {
            throw new ArgumentException(
                "An exact capture and peer process identity is required.");
        }
        return new CancelledCapture(
            _generationId,
            _sessionScope,
            captureId,
            peerProcessId,
            peerProcessCreationTime);
    }

    private sealed record CancelledCapture(
        Guid GenerationId,
        string SessionScope,
        Guid CaptureId,
        uint PeerProcessId,
        ulong PeerProcessCreationTime);
}

internal enum LilithGameVoiceAdmissionDisposition
{
    Admitted = 0,
    Cancelled = 1,
    RecoveryRequired = 2
}

internal enum LilithGameVoiceCancelAdmissionDisposition
{
    CancelledBeforeAdmission = 0,
    ActiveAdmission = 1,
    RecoveryRequired = 2
}

internal enum LilithGameVoiceTerminalAdmissionDisposition
{
    Completed = 0,
    Cancelled = 1,
    AlreadyClaimed = 2
}

internal sealed class LilithGameSessionLineageAttestation
{
    private const int MaximumSessionIds = 2_048;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal string RequestedStoredSessionId { get; }

    internal string ResolvedStoredSessionId { get; }

    internal IReadOnlyList<string> SessionIds { get; }

    internal string Source { get; }

    internal bool Archived { get; }

    internal string FingerprintSha256 { get; }

    private LilithGameSessionLineageAttestation(
        string requestedStoredSessionId,
        string resolvedStoredSessionId,
        IReadOnlyList<string> sessionIds,
        string source,
        bool archived,
        string fingerprintSha256)
    {
        RequestedStoredSessionId = requestedStoredSessionId;
        ResolvedStoredSessionId = resolvedStoredSessionId;
        SessionIds = sessionIds;
        Source = source;
        Archived = archived;
        FingerprintSha256 = fingerprintSha256;
    }

    internal static LilithGameSessionLineageAttestation Create(
        string requestedStoredSessionId,
        string resolvedStoredSessionId,
        IEnumerable<string> sessionIds,
        string source,
        bool archived)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedStoredSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedStoredSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(sessionIds);
        var identities = sessionIds.ToArray();
        if (identities.Length is <= 0 or > MaximumSessionIds
            || identities.Any(static identity =>
                string.IsNullOrWhiteSpace(identity)
                || identity.Any(char.IsControl)
                || StrictUtf8.GetByteCount(identity)
                    > LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes)
            || identities.Distinct(StringComparer.Ordinal).Count()
                != identities.Length
            || !string.Equals(
                identities[0],
                requestedStoredSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                identities[^1],
                resolvedStoredSessionId,
                StringComparison.Ordinal)
            || !string.Equals(source, "lilith-game", StringComparison.Ordinal)
            || !archived)
        {
            throw new ArgumentException(
                "The durable hidden-session lineage was rejected.",
                nameof(sessionIds));
        }
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            requested_session_id = requestedStoredSessionId,
            resolved_session_id = resolvedStoredSessionId,
            session_ids = identities,
            source,
            archived
        });
        return new LilithGameSessionLineageAttestation(
            requestedStoredSessionId,
            resolvedStoredSessionId,
            Array.AsReadOnly(identities),
            source,
            archived,
            Convert.ToHexString(SHA256.HashData(canonical)));
    }
}

internal sealed record LilithGameNoteDraftOutcome(
    LilithGameBridgeNoteDraftState State, string Text);

internal interface ILilithGameHermesSessionHost : IAsyncDisposable
{
    CancellationToken VoiceRecoveryRequested => CancellationToken.None;

    Task VoiceRecoveryCompletion => Task.CompletedTask;

    bool VoiceRecoveryRequired => false;

    bool TryResolveCurrentNoteInboxOwner(string sessionId, string originTurnId,
        out Guid clientTurnId, out CancellationToken turnCancellation)
    {
        clientTurnId = Guid.Empty; turnCancellation = CancellationToken.None;
        return false;
    }

    Task<bool> WaitForNoteInboxOwnerAsync(string sessionId, CancellationToken cancellationToken)
        => Task.FromResult(false);

    Task<LilithGameNoteDraftOutcome> GenerateNoteDraftAsync(
        Guid requestId, Guid eventId, string category, string topic, string context,
        LilithGameBridgeDisplayLocale displayLocale, CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameNoteDraftOutcome(
            LilithGameBridgeNoteDraftState.Failed, string.Empty));

    Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        CancellationToken cancellationToken);

    Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
        => SubmitAsync(
            clientTurnId,
            surface,
            text,
            cancellationToken);

    Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale, cancellationToken);

    Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        LilithMiniGameDiagnosticRequest? miniGameDiagnostic,
        CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale, miniGameContext, cancellationToken);

    Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId, LilithGameBridgeInputSurface surface, string text,
        LilithGameBridgeDisplayLocale displayLocale, LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext, LilithMiniGameDiagnosticRequest? miniGameDiagnostic,
        LilithMiniGameAnswerRepair? miniGameAnswerRepair, CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale, miniGameContext, miniGameDiagnostic, cancellationToken);

    Task<LilithGameHistoryPage> ReadHistoryAsync(
        string cursor,
        int pageSize,
        CancellationToken cancellationToken);

    Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameNotificationPage(
            Array.Empty<LilithGameBridgeNotificationEntry>(),
            OldestAvailableSequence: 0,
            LatestSequence: 0,
            HasMore: false,
            Code: "ok"));

    Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
        => ReadNotificationsAsync(
            afterSequence,
            waitMilliseconds,
            displayLocale,
            speechLocale,
            tailOnly: false,
            cancellationToken);

    Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        bool tailOnly,
        CancellationToken cancellationToken)
        => ReadNotificationsAsync(
            afterSequence,
            waitMilliseconds,
            cancellationToken);

    Task<bool> InterruptAsync(
        Guid clientTurnId,
        CancellationToken cancellationToken);

    Task<LilithGameNativeAssistantCommitOutcome> CommitNativeAssistantAsync(
        Guid eventId,
        string text,
        long occurredAtUnixMilliseconds,
        string nodeId,
        string lineId,
        string trigger,
        string locale,
        CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameNativeAssistantCommitOutcome(
            false,
            false,
            false,
            "runtime.native_assistant_unavailable"));

    Task<LilithGameNativeAssistantCommitOutcome> CommitNativeAssistantAsync(
        Guid eventId,
        bool proactiveDialogueEnabled,
        LilithGameBridgeNativePerceptionSnapshot perception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(perception);
        return CommitNativeAssistantAsync(
            eventId,
            perception.Text,
            perception.OccurredAtUnixMilliseconds,
            perception.NodeId,
            perception.LineId,
            perception.Trigger,
            perception.Locale,
            cancellationToken);
    }

    Task<LilithGameNativeAssistantCommitOutcome> CommitNativeAssistantAsync(
        Guid eventId, bool proactiveDialogueEnabled,
        LilithGameBridgeNativePerceptionSnapshot perception,
        CancellationToken cancellationToken, LilithMiniGamePublicContext? miniGameContext)
        => CommitNativeAssistantAsync(eventId, proactiveDialogueEnabled, perception, cancellationToken);

    Task<LilithGameForegroundProactiveOutcome> RequestForegroundProactiveAsync(
        Guid operationId,
        long observedAtUnixMilliseconds,
        string applicationId,
        string applicationDisplayName,
        string applicationCategory,
        LilithGameBridgeDisplayLocale displayLocale,
        CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameForegroundProactiveOutcome(
            Accepted: false,
            Succeeded: false,
            LilithGameBridgeForegroundProactiveState.Abandoned,
            AlreadyProcessed: false,
            Retryable: false,
            Code: "runtime.foreground_proactive_unavailable"));

    Task<LilithGameForegroundProactiveOutcome> RequestForegroundProactiveAsync(
        Guid operationId, long observedAtUnixMilliseconds, string applicationId,
        string applicationDisplayName, string applicationCategory,
        LilithGameBridgeDisplayLocale displayLocale, CancellationToken cancellationToken,
        LilithMiniGamePublicContext? miniGameContext)
        => RequestForegroundProactiveAsync(operationId, observedAtUnixMilliseconds,
            applicationId, applicationDisplayName, applicationCategory, displayLocale, cancellationToken);

    Task<LilithGameVoiceStartOutcome> StartVoiceAsync(
        Guid captureId,
        LilithGameBridgeVoiceLanguage language,
        int maximumDurationMilliseconds,
        LilithGameVoicePeerIdentity peer,
        CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameVoiceStartOutcome(
            false,
            "runtime.voice_unavailable"));

    Task<LilithGameVoiceStopOutcome> StopVoiceAsync(
        Guid captureId,
        LilithGameBridgeVoiceStopDisposition disposition,
        LilithGameVoicePeerIdentity peer,
        CancellationToken cancellationToken)
        => Task.FromResult(new LilithGameVoiceStopOutcome(
            false,
            LilithGameBridgeTerminal.Failed,
            string.Empty,
            "runtime.voice_unavailable"));
}

internal sealed partial class LilithGameHermesSessionHost :
    ILilithGameHermesSessionHost
{
    private const int HandleSchemaVersion = 1;
    private const int AuthorityReceiptSchemaVersion = 2;
    private const int MaximumTerminalCache = 64;
    private const int MaximumHistorySnapshotMessages = 20_000;
    private const int MaximumNotificationRetention = 64;
    private const string VoiceCleanupRecoveryRequired =
        "runtime.voice_cleanup_recovery_required";
    private const string GameAgentInitializationFailure =
        "runtime.game_agent_initialization_failed";
    private const string NativeAssistantCommitQuarantined =
        "runtime.native_assistant_commit_quarantined";
    private static readonly TimeSpan SessionAttestationTimeout =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TurnInterruptTimeout =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TurnTerminalReconciliationTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NativeAssistantContinuationDispatchTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TurnShutdownDrainTimeout =
        TurnInterruptTimeout
        + TurnTerminalReconciliationTimeout
        + TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HistorySnapshotLifetime =
        TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VoiceTerminalTimeout =
        TimeSpan.FromSeconds(90);
    private static readonly TimeSpan VoiceIdleTimeout =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VoiceProjectionGrace =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VoiceGatewayDispatchTimeout =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VoiceCleanupTimeout =
        TimeSpan.FromSeconds(10);
    private static readonly TimeSpan VoiceGenerationRecoveryTimeout =
        TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly LilithRuntimeService _runtime;
    private readonly LilithRuntimeSnapshot _snapshot;
    private readonly LilithRuntimeRequestRouter _router;
    private readonly string _handlePath;
    private readonly string _authorityReceiptPath;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private NoteInboxTurnOwner? _noteInboxTurnOwner;
    private string? _noteInboxDispatchSession;
    private Guid _noteInboxDispatchClient;
    private TaskCompletionSource<bool>? _noteInboxOwnerReady;
    private sealed record NoteInboxTurnOwner(string SessionId, string NativeTurnId,
        Guid ClientTurnId, CancellationToken Cancellation, string LineageFingerprint);
    private readonly object _noteDraftAdmissionLock = new();
    private readonly HashSet<Guid> _noteDraftOperations = new();
    private readonly HashSet<Guid> _noteDraftEvents = new();
    private readonly CancellationTokenSource _noteDraftShutdown = new();
    private readonly object _notificationProjectionAdmissionLock = new();
    private CancellationTokenSource? _activeNotificationProjection;
    private NativeAssistantContinuationLease? _nativeAssistantContinuation;
    private bool _notificationProjectionAdmissionClosed;
    private int _foregroundTurnDemand;
    private int _playerTurnDemand;
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _voiceGate = new(1, 1);
    private readonly object _eventLock = new();
    private readonly object _notificationLock = new();
    private readonly object _voiceRecoveryLock = new();
    private readonly CancellationTokenSource _voiceRecoveryRequested = new();
    private readonly LilithGameTurnAdmissionRegistry _turnAdmissions;
    private readonly LilithGameVoiceAdmissionTombstones _voiceAdmissionTombstones;
    private readonly Dictionary<string, TaskCompletionSource<LilithGameTerminalEvent>>
        _terminalWaiters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LilithGameTerminalEvent> _terminalCache =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _terminalCacheOrder = new();
    private readonly Queue<LilithGameBridgeNotificationEntry> _notifications = new();
    private readonly HashSet<string> _nativeAssistantNotificationKeys = new(
        StringComparer.Ordinal);
    private readonly Queue<string> _nativeAssistantNotificationKeyOrder = new();
    private readonly HashSet<Task> _voicePeerExitTasks = new();
    private TaskCompletionSource<bool> _sessionInfoSignal = NewSignal();
    private TaskCompletionSource<bool> _notificationSignal = NewSignal();
    private long _notificationSequence;
    private readonly Dictionary<long, LilithGameBridgeNotificationEntry> _syntheticNotificationSpeech = new();
    private long _syntheticPresentationInvalidatedAt;
    private string? _sessionActivationRetryRequiredSessionId;
    private QuarantinedGameTurn? _quarantinedTurn;
    private HistorySnapshot? _historySnapshot;
    private Action<LilithLetterTurnReference>? _lettersCompleted;
    internal string LettersCompletionStatus { get; private set; } = "not_observed";
    private LilithGameSessionLineageAttestation? _admittedLineage;
    private ActiveGameVoiceCapture? _voiceCapture;
    private GameVoiceOwnerLease? _voiceOwnerLease;
    private Task? _voiceRecoveryTask;
    private string? _modelSyncReceiptRequiredSessionId;
    private long? _admittedNativeConfigRevision;
    private bool _sessionApprovalEnabled;
    private int _nativeAssistantCommitQuarantined;
    private int _nativeAssistantContinuationRecoveryRequired;
    private bool _disposed;

    public CancellationToken VoiceRecoveryRequested
        => _voiceRecoveryRequested.Token;

    public Task VoiceRecoveryCompletion
    {
        get
        {
            lock (_voiceRecoveryLock)
                return _voiceRecoveryTask ?? Task.CompletedTask;
        }
    }

    public bool VoiceRecoveryRequired
    {
        get
        {
            lock (_voiceRecoveryLock)
                return _voiceRecoveryTask is not null;
        }
    }

    internal static TimeSpan MaximumVoiceRecoveryEnvelope
        => VoiceCleanupTimeout + VoiceGenerationRecoveryTimeout;

    internal static TimeSpan MaximumVoiceStartFailureEnvelope
        => VoiceGatewayDispatchTimeout + MaximumVoiceRecoveryEnvelope;

    internal static TimeSpan MaximumVoiceCancelFailureEnvelope
        => MaximumVoiceRecoveryEnvelope;

    internal static TimeSpan MaximumVoiceTranscribeFailureEnvelope
        => VoiceGatewayDispatchTimeout
            + VoiceTerminalTimeout
            + VoiceIdleTimeout
            + MaximumVoiceRecoveryEnvelope;

    internal static string VoiceLanguageTokenForHermes(
        LilithGameBridgeVoiceLanguage language)
        => language switch
        {
            LilithGameBridgeVoiceLanguage.Automatic => "auto",
            LilithGameBridgeVoiceLanguage.Japanese => "ja",
            LilithGameBridgeVoiceLanguage.TraditionalChinese => "zh",
            LilithGameBridgeVoiceLanguage.English => "en",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    internal LilithGameHermesSessionHost(
        LilithRuntimeService runtime,
        LilithRuntimeSnapshot snapshot,
        LilithRuntimeRequestRouter? router = null,
        Action<LilithLetterTurnReference>? lettersCompleted = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _lettersCompleted = lettersCompleted;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.GenerationId == Guid.Empty
            || snapshot.State != LilithRuntimeState.Ready
            || !snapshot.GatewayReady)
        {
            throw new ArgumentException(
                "A ready Runtime snapshot is required.",
                nameof(snapshot));
        }

        var runtimeDirectory = Path.Combine(snapshot.HermesHomePath, "runtime");
        Directory.CreateDirectory(runtimeDirectory);
        _handlePath = Path.Combine(runtimeDirectory, "game-session-v1.json");
        _authorityReceiptPath = Path.Combine(
            runtimeDirectory,
            "game-session-authority-v2.json");
        _turnAdmissions = new LilithGameTurnAdmissionRegistry(
            snapshot.GenerationId,
            Path.GetFullPath(_handlePath));
        _voiceAdmissionTombstones = new LilithGameVoiceAdmissionTombstones(
            snapshot.GenerationId,
            Path.GetFullPath(_handlePath));
        _router = router ?? new LilithRuntimeRequestRouter(
            runtime,
            snapshot.GenerationId,
            Path.Combine(snapshot.HermesHomePath, "workspace"),
            snapshot.Profile,
            sessionSurface: LilithRuntimeSessionSurface.GameBridge);
        runtime.GatewayEvent += RuntimeGatewayEvent;
    }

    public bool TryResolveCurrentNoteInboxOwner(string sessionId, string originTurnId,
        out Guid clientTurnId, out CancellationToken turnCancellation)
    {
        clientTurnId = Guid.Empty; turnCancellation = CancellationToken.None;
        if (!_router.IsSessionAttested(sessionId) || _router.ActiveSessionId != sessionId)
            return false;
        var lineage = _router.ActiveGameLineageAttestation?.FingerprintSha256;
        lock (_eventLock)
        {
            var owner = _noteInboxTurnOwner;
            if (_disposed || owner is null || owner.Cancellation.IsCancellationRequested
                || owner.SessionId != sessionId || owner.NativeTurnId != originTurnId
                || owner.LineageFingerprint != lineage || _terminalCache.ContainsKey(originTurnId))
                return false;
            clientTurnId = owner.ClientTurnId; turnCancellation = owner.Cancellation;
            return true;
        }
    }

    public async Task<bool> WaitForNoteInboxOwnerAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_router.IsSessionAttested(sessionId) || _router.ActiveSessionId != sessionId) return false;
        Task<bool>? ready;
        lock (_eventLock)
            ready = !_disposed && _noteInboxDispatchSession == sessionId ? _noteInboxOwnerReady?.Task : null;
        if (ready is null) return false;
        try { return await ready.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }

    public Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        CancellationToken cancellationToken)
        => SubmitAsync(
            clientTurnId,
            surface,
            text,
            LilithGameBridgeDisplayLocale.TraditionalChinese,
            LilithGameBridgeSpeechLocale.Chinese,
            cancellationToken);

    public Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale, null, cancellationToken);

    public Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale,
            miniGameContext, null, cancellationToken);

    public Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        LilithMiniGameDiagnosticRequest? miniGameDiagnostic,
        CancellationToken cancellationToken)
        => SubmitAsync(clientTurnId, surface, text, displayLocale, speechLocale,
            miniGameContext, miniGameDiagnostic, null, cancellationToken);

    public Task<LilithGameTurnOutcome> SubmitAsync(
        Guid clientTurnId, LilithGameBridgeInputSurface surface, string text,
        LilithGameBridgeDisplayLocale displayLocale, LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext, LilithMiniGameDiagnosticRequest? miniGameDiagnostic,
        LilithMiniGameAnswerRepair? miniGameAnswerRepair, CancellationToken cancellationToken)
    {
        var diagnostic = miniGameDiagnostic?.IsActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) == true
            ? new LilithMiniGameDiagnosticCollector(miniGameDiagnostic, miniGameContext is not null)
            : null;
        var turn = SubmitCoreAsync(clientTurnId, surface, text, displayLocale, speechLocale,
            miniGameContext, diagnostic, miniGameAnswerRepair, cancellationToken);
        return diagnostic is null ? turn : AttachMiniGameDiagnosticAsync(turn, diagnostic);
    }

    private static async Task<LilithGameTurnOutcome> AttachMiniGameDiagnosticAsync(
        Task<LilithGameTurnOutcome> turn, LilithMiniGameDiagnosticCollector diagnostic)
    {
        var outcome = await turn.ConfigureAwait(false);
        return outcome with
        {
            MiniGameDiagnostic = diagnostic.Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        };
    }

    private LilithMiniGameDiagnosticCollector? _miniGameDiagnostic;

    private async Task<LilithGameTurnOutcome> SubmitCoreAsync(
        Guid clientTurnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        LilithMiniGameDiagnosticCollector? diagnostic,
        LilithMiniGameAnswerRepair? miniGameAnswerRepair,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (clientTurnId == Guid.Empty)
            throw new ArgumentException("Client turn identity is required.", nameof(clientTurnId));
        if (!Enum.IsDefined(surface))
            throw new ArgumentOutOfRangeException(nameof(surface));
        _ = new LilithGameBridgeRequest(
            _snapshot.GenerationId,
            Guid.NewGuid(),
            1,
            clientTurnId,
            surface,
            text,
            displayLocale,
            speechLocale);

        var admission = _turnAdmissions.Admit(
            clientTurnId,
            out var interruptRequested);
        if (admission == LilithGameTurnAdmissionDisposition.Duplicate)
        {
            throw new InvalidOperationException(
                "The client turn identity was already active.");
        }
        if (admission == LilithGameTurnAdmissionDisposition.RecoveryRequired)
        {
            return new LilithGameTurnOutcome(
                false,
                false,
                LilithGameBridgeTerminal.Failed,
                string.Empty,
                string.Empty,
                "runtime.turn_admission_recovery_required");
        }
        if (admission
            == LilithGameTurnAdmissionDisposition.CancelledBeforeAdmission)
        {
            return InterruptedTurn(userCommitted: false);
        }

        using var turnCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                interruptRequested);
        var gateEntered = false;
        var promptDispatchStarted = false;
        var promptDispatchCompleted = false;
        var userCommitted = false;
        LilithMiniGamePublicContext? dispatchedMiniGameContext = null;
        string? promptTurnId = null;
        Task<LilithGameTerminalEvent>? exactTerminalTask = null;
        var retainForQuarantine = false;
        RegisterForegroundTurnDemand(
            playerTurn: true,
            preemptNativeContinuation: true);
        try
        {
            await _turnGate.WaitAsync(turnCancellation.Token)
                .ConfigureAwait(false);
            gateEntered = true;
            var quarantineFailure = ResolveQuarantinedTurn();
            if (quarantineFailure is not null)
            {
                return new LilithGameTurnOutcome(
                    false,
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    string.Empty,
                    quarantineFailure);
            }
            string sessionId;
            HermesPromptSubmitResult prompt;
            using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    turnCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                RequireUsableModelAuthority(modelAuthority.Snapshot);
                await EnsureSessionAsync(
                        modelAuthority,
                        turnCancellation.Token)
                    .ConfigureAwait(false);
                sessionId = _router.ActiveSessionId
                    ?? throw new InvalidOperationException(
                        "The game Hermes session has no active identity.");
                if (!_turnAdmissions.TryBeginPromptDispatch(clientTurnId))
                    return InterruptedTurn(userCommitted: false);
                promptDispatchStarted = true;
                lock (_eventLock)
                {
                    _noteInboxDispatchSession = sessionId;
                    _noteInboxDispatchClient = clientTurnId;
                    _noteInboxOwnerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                var promptParameters = new Dictionary<string, object>
                {
                    ["session_id"] = sessionId,
                    ["text"] = text,
                    ["display_locale"] = LilithGameBridgeProtocol.DisplayLocaleToken(displayLocale)
                };
                if (speechLocale == LilithGameBridgeSpeechLocale.Japanese)
                    promptParameters["speech_locale"] = "ja";
                // A frozen reference for this player-initiated turn only. Waiting
                // for session/model admission must not turn old facts into now.
                var contextNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var contextFresh = miniGameContext?.IsFresh(contextNow) == true;
                if (contextFresh)
                {
                    dispatchedMiniGameContext = miniGameContext;
                    promptParameters["game_context"] = miniGameContext!.ToJsonElement();
                }
                // Legacy answer-repair parameters remain ABI-compatible but
                // are not dispatched on the plain companion-context path.
                if (diagnostic?.BeginDispatch(sessionId,
                        miniGameContext is null ? "absent" : contextFresh ? "present" : "stale", contextNow) == true)
                {
                    // Exact current submit only. No ordinary chat allocation,
                    // cached snapshot, persistent log or extra provider request.
                    lock (_eventLock)
                        _miniGameDiagnostic = diagnostic;
                    promptParameters["game_context_diagnostic"] = diagnostic.Request.ToJson();
                }
                var response = await _router.RouteAsync(
                    new LilithRuntimeRequest(
                        "prompt.submit",
                        JsonSerializer.SerializeToElement(promptParameters)),
                    turnCancellation.Token,
                    modelAuthority).ConfigureAwait(false);
                if (!response.Succeeded)
                {
                    _turnAdmissions.CompletePromptDispatch(
                        clientTurnId,
                        remoteTurnMayBeActive: false);
                    promptDispatchCompleted = true;
                    return new LilithGameTurnOutcome(
                        false,
                        false,
                        LilithGameBridgeTerminal.Failed,
                        string.Empty,
                        string.Empty,
                        GameRpcFailureCode(response, "prompt"));
                }

                try
                {
                    prompt = HermesTurnCorrelationContract.DecodePromptSubmitResult(
                        response.HermesPayload);
                }
                catch (InvalidDataException)
                {
                    _turnAdmissions.CompleteUntrackedPromptFailure(clientTurnId);
                    promptDispatchCompleted = true;
                    return new LilithGameTurnOutcome(
                        false,
                        false,
                        LilithGameBridgeTerminal.Failed,
                        string.Empty,
                        string.Empty,
                        "runtime.prompt_receipt_rejected");
                }
                if (!prompt.CreatesTurn)
                {
                    _turnAdmissions.CompletePromptDispatch(
                        clientTurnId,
                        remoteTurnMayBeActive: false);
                    promptDispatchCompleted = true;
                    return new LilithGameTurnOutcome(
                        false,
                        false,
                        LilithGameBridgeTerminal.Failed,
                        string.Empty,
                        string.Empty,
                        "runtime.prompt_not_started");
                }
                userCommitted = true;
                promptTurnId = prompt.TurnId;
                diagnostic?.BindTurn(prompt.TurnId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var noteLineage = _router.ActiveGameLineageAttestation?.FingerprintSha256;
                lock (_eventLock)
                {
                    if (!turnCancellation.IsCancellationRequested && noteLineage is not null
                        && !_terminalCache.ContainsKey(prompt.TurnId))
                        _noteInboxTurnOwner = new(sessionId, prompt.TurnId, clientTurnId,
                            turnCancellation.Token, noteLineage);
                    _noteInboxOwnerReady?.TrySetResult(_noteInboxTurnOwner?.ClientTurnId == clientTurnId);
                    _noteInboxDispatchSession = null;
                }
                _turnAdmissions.CompletePromptDispatch(
                    clientTurnId,
                    remoteTurnMayBeActive: true);
                promptDispatchCompleted = true;
            }
            finally
            {
                modelAuthority.Dispose();
            }

            Task<LilithGameTerminalEvent> terminalTask;
            lock (_eventLock)
            {
                if (_terminalCache.Remove(prompt.TurnId, out var cached))
                {
                    terminalTask = Task.FromResult(cached);
                }
                else
                {
                    var waiter = new TaskCompletionSource<LilithGameTerminalEvent>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _terminalWaiters.Add(prompt.TurnId, waiter);
                    terminalTask = waiter.Task;
                }
                exactTerminalTask = terminalTask;
            }

            var terminal = await terminalTask.WaitAsync(
                TurnTimeout,
                turnCancellation.Token).ConfigureAwait(false);
            _turnAdmissions.ObserveRemoteTerminal(clientTurnId);
            try
            {
                await CommitTerminalAuthorityAsync(
                        sessionId,
                        terminal,
                        turnCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (LilithGameBridgeOperationException exception)
            {
                return new LilithGameTurnOutcome(
                    false,
                    true,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    string.Empty,
                    exception.Code);
            }
            catch (Exception exception) when (exception is
                InvalidDataException or InvalidOperationException)
            {
                return new LilithGameTurnOutcome(
                    false,
                    true,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    string.Empty,
                    "runtime.post_turn_lineage_rejected");
            }
            PersistHandleIfAvailable();
            var speech = terminal.Status == "complete"
                ? await ResolveSpeechProjectionAsync(
                        sessionId,
                        terminal.Text,
                        displayLocale,
                        speechLocale,
                        turnCancellation.Token,
                        prompt.TurnId)
                    .ConfigureAwait(false)
                : (Text: string.Empty, Emotion: string.Empty);
            if (terminal.Status == "complete" && _lettersCompleted is not null)
            {
                // Only after terminal authority was committed, never from history
                // display rows, interim text, failed turns or native proactive lines.
                try
                {
                    var lineage = RequireExactActiveLineage(sessionId);
                    _lettersCompleted(LilithLettersHistory.Completed(LilithDailyNoteProtocol.ProfileId,
                        lineage.ResolvedStoredSessionId, prompt.TurnId, DateTimeOffset.UtcNow, text, terminal.Text));
                    LettersCompletionStatus = "observed";
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Optional indexing must never turn a completed chat into a
                    // failure. A later bounded replay may recover a missed index.
                    LettersCompletionStatus = "index_unavailable";
                }
            }
            return terminal.Status switch
            {
                "complete" => new LilithGameTurnOutcome(
                    true,
                    true,
                    LilithGameBridgeTerminal.Completed,
                    terminal.Text,
                    speech.Text,
                    "ok") { SpeechEmotion = speech.Emotion,
                        MiniGameEventReceipt = ReadMiniGameEventReceipt(terminal.Payload, dispatchedMiniGameContext) },
                "interrupted" => new LilithGameTurnOutcome(
                    false,
                    true,
                    LilithGameBridgeTerminal.Interrupted,
                    string.Empty,
                    string.Empty,
                    "hermes.turn_interrupted"),
                _ => new LilithGameTurnOutcome(
                    false,
                    true,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    string.Empty,
                    "hermes.turn_failed")
            };
        }
        catch (TimeoutException)
        {
            _turnAdmissions.RecordInterrupt(clientTurnId);
            return new LilithGameTurnOutcome(
                false,
                userCommitted,
                LilithGameBridgeTerminal.Interrupted,
                string.Empty,
                string.Empty,
                "runtime.turn_timeout");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _turnAdmissions.RecordInterrupt(clientTurnId);
            throw;
        }
        catch (OperationCanceledException)
            when (interruptRequested.IsCancellationRequested)
        {
            return InterruptedTurn(userCommitted);
        }
        catch
        {
            if (promptDispatchStarted)
                _turnAdmissions.RecordInterrupt(clientTurnId);
            throw;
        }
        finally
        {
            lock (_eventLock)
            {
                if (ReferenceEquals(_miniGameDiagnostic, diagnostic))
                    _miniGameDiagnostic = null;
                if (_noteInboxTurnOwner?.ClientTurnId == clientTurnId)
                    _noteInboxTurnOwner = null;
                if (_noteInboxDispatchClient == clientTurnId)
                {
                    _noteInboxOwnerReady?.TrySetResult(false);
                    _noteInboxOwnerReady = null;
                    _noteInboxDispatchSession = null;
                    _noteInboxDispatchClient = Guid.Empty;
                }
            }
            if (promptDispatchStarted && !promptDispatchCompleted)
            {
                // A cancelled/failed transport after dispatch is uncertain:
                // prompt.submit may already have created a Hermes turn even
                // though its receipt never reached Runtime.
                _turnAdmissions.CompleteUntrackedPromptFailure(clientTurnId);
            }
            if (_turnAdmissions.TryClaimRemoteInterrupt(clientTurnId))
            {
                _ = await InterruptSessionForTurnCancellationAsync()
                    .ConfigureAwait(false);
                var terminalObserved = exactTerminalTask is not null
                    && await TryAwaitExactTurnTerminalAsync(exactTerminalTask)
                        .ConfigureAwait(false);
                if (terminalObserved)
                {
                    _turnAdmissions.ObserveRemoteTerminal(clientTurnId);
                }
                else
                {
                    QuarantineTurn(
                        clientTurnId,
                        promptTurnId,
                        exactTerminalTask);
                    retainForQuarantine = true;
                }
            }
            if (promptTurnId is not null && !retainForQuarantine)
            {
                lock (_eventLock)
                    _terminalWaiters.Remove(promptTurnId);
            }
            if (gateEntered)
                _turnGate.Release();
            RetireForegroundTurnDemand(playerTurn: true);
            if (!retainForQuarantine)
                _turnAdmissions.Retire(clientTurnId);
        }
    }

    public async Task<LilithGameNativeAssistantCommitOutcome>
        CommitNativeAssistantAsync(
            Guid eventId,
            string text,
            long occurredAtUnixMilliseconds,
            string nodeId,
            string lineId,
            string trigger,
            string locale,
            CancellationToken cancellationToken)
        => await CommitNativeAssistantAsync(
                eventId,
                proactiveDialogueEnabled: false,
                new LilithGameBridgeNativePerceptionSnapshot(
                    text,
                    occurredAtUnixMilliseconds,
                    nodeId,
                    lineId,
                    trigger,
                    locale),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<LilithGameNativeAssistantCommitOutcome>
        CommitNativeAssistantAsync(
            Guid eventId,
            bool proactiveDialogueEnabled,
            LilithGameBridgeNativePerceptionSnapshot perception,
            CancellationToken cancellationToken)
        => await CommitNativeAssistantAsync(eventId, proactiveDialogueEnabled, perception,
            cancellationToken, null).ConfigureAwait(false);

    public async Task<LilithGameNativeAssistantCommitOutcome> CommitNativeAssistantAsync(
        Guid eventId, bool proactiveDialogueEnabled, LilithGameBridgeNativePerceptionSnapshot perception,
        CancellationToken cancellationToken, LilithMiniGamePublicContext? miniGameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validated = new LilithGameBridgeNativeAssistantCommitRequest(
            _snapshot.GenerationId,
            Guid.NewGuid(),
            1,
            eventId,
            proactiveDialogueEnabled,
            perception, miniGameContext);

        var gateEntered = false;
        var gateTransferred = false;
        RegisterForegroundTurnDemand(
            playerTurn: false,
            preemptNativeContinuation: false);
        try
        {
            if (!await _turnGate.WaitAsync(
                    millisecondsTimeout: 0,
                    cancellationToken).ConfigureAwait(false))
            {
                return RetryableNativeAssistantCommit(
                    "runtime.game_turn_busy");
            }
            gateEntered = true;
            if (Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0)
            {
                return RetryableNativeAssistantCommit(
                    "runtime.native_assistant_continuation_recovery_required");
            }

            using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    cancellationToken)
                .ConfigureAwait(false);
            RequireUsableModelAuthority(modelAuthority.Snapshot);
            await EnsureSessionAsync(modelAuthority, cancellationToken)
                .ConfigureAwait(false);
            var sessionId = _router.ActiveSessionId
                ?? throw new InvalidOperationException(
                    "The game Hermes session has no active identity.");
            _ = RequireExactActiveDurableLineage(sessionId);

            var response = await _router.RouteAsync(
                new LilithRuntimeRequest(
                    "session.append_assistant_event",
                    JsonSerializer.SerializeToElement(new
                    {
                        session_id = sessionId,
                        event_id = validated.EventId.ToString("N"),
                        text = validated.Text,
                        occurred_at_unix_ms =
                            validated.OccurredAtUnixMilliseconds,
                        node_id = validated.NodeId,
                        line_id = validated.LineId,
                        trigger = validated.Trigger,
                        locale = validated.Locale
                    })),
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
            if (!response.Succeeded)
            {
                var code = FailureCode(
                    response,
                    "runtime.native_assistant_rejected");
                if (string.Equals(
                        code,
                        "runtime.native_assistant_session_unavailable",
                        StringComparison.Ordinal))
                {
                    SignalSessionActivationFailure(sessionId);
                }
                if (string.Equals(
                        code,
                        "runtime.game_session_lineage_rejected",
                        StringComparison.Ordinal))
                {
                    QuarantineNativeAssistantLineage(sessionId);
                }
                return new LilithGameNativeAssistantCommitOutcome(
                    false,
                    false,
                    IsRetryableNativeAssistantCommitCode(code),
                    code);
            }

            NativeAssistantCommitReceipt receipt;
            try
            {
                receipt = DecodeNativeAssistantCommitReceipt(
                    sessionId,
                    validated,
                    response.HermesPayload);
            }
            catch (InvalidDataException)
            {
                return RetryableNativeAssistantCommit(
                    "runtime.native_assistant_receipt_uncertain");
            }

            try
            {
                await CommitNativeAssistantAuthorityAsync(
                        sessionId,
                        response.HermesPayload,
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new LilithGameNativeAssistantCommitOutcome(
                    false,
                    false,
                    false,
                    NativeAssistantCommitQuarantined);
            }

            LilithMiniGameEventReceipt? gameReceipt = null;
            if (validated.ProactiveDialogueEnabled)
            {
                (gateTransferred, gameReceipt) = await TryStartNativeAssistantContinuationAsync(
                        sessionId,
                        validated,
                        modelAuthority)
                    .ConfigureAwait(false);
            }

            return new LilithGameNativeAssistantCommitOutcome(
                true,
                receipt.AlreadyCommitted,
                false,
                "ok")
            {
                MiniGameEventsPending = gateTransferred && miniGameContext?.Events is not null,
                MiniGameEventReceipt = gameReceipt
            };
        }
        finally
        {
            if (gateEntered && !gateTransferred)
                _turnGate.Release();
            if (!gateTransferred)
                RetireForegroundTurnDemand(playerTurn: false);
        }
    }

    public Task<LilithGameForegroundProactiveOutcome>
        RequestForegroundProactiveAsync(
            Guid operationId,
            long observedAtUnixMilliseconds,
            string applicationId,
            string applicationDisplayName,
            string applicationCategory,
            LilithGameBridgeDisplayLocale displayLocale,
            CancellationToken cancellationToken)
        => RequestForegroundProactiveAsync(operationId, observedAtUnixMilliseconds,
            applicationId, applicationDisplayName, applicationCategory, displayLocale,
            cancellationToken, null);

    public async Task<LilithGameForegroundProactiveOutcome> RequestForegroundProactiveAsync(
        Guid operationId, long observedAtUnixMilliseconds, string applicationId,
        string applicationDisplayName, string applicationCategory,
        LilithGameBridgeDisplayLocale displayLocale, CancellationToken cancellationToken,
        LilithMiniGamePublicContext? miniGameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validated = new LilithGameBridgeForegroundProactiveRequest(
            _snapshot.GenerationId,
            Guid.NewGuid(),
            1,
            operationId,
            observedAtUnixMilliseconds,
            applicationId,
            applicationDisplayName,
            applicationCategory,
            displayLocale,
            miniGameContext);
        var eventId = validated.OperationId.ToString("N");

        lock (_notificationProjectionAdmissionLock)
        {
            if (_nativeAssistantContinuation is
                    { Kind: SyntheticAssistantContinuationKind.ForegroundProactive }
                active)
            {
                return string.Equals(
                        active.EventId,
                        eventId,
                        StringComparison.Ordinal)
                    ? new LilithGameForegroundProactiveOutcome(
                        Accepted: true,
                        Succeeded: false,
                        LilithGameBridgeForegroundProactiveState.Streaming,
                        AlreadyProcessed: false,
                        Retryable: false,
                        Code: "ok")
                    : FailedForegroundProactive(
                        "runtime.foreground_proactive_busy",
                        retryable: true);
            }
        }

        var gateEntered = false;
        var gateTransferred = false;
        NativeAssistantContinuationLease? lease = null;
        RegisterForegroundTurnDemand(
            playerTurn: false,
            preemptNativeContinuation: false);
        try
        {
            if (!await _turnGate.WaitAsync(
                    millisecondsTimeout: 0,
                    cancellationToken).ConfigureAwait(false))
            {
                return FailedForegroundProactive(
                    "runtime.game_turn_busy",
                    retryable: true);
            }
            gateEntered = true;
            if (Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0)
            {
                return FailedForegroundProactive(
                    "runtime.native_assistant_continuation_recovery_required",
                    retryable: true);
            }

            using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    cancellationToken)
                .ConfigureAwait(false);
            RequireUsableModelAuthority(modelAuthority.Snapshot);
            await EnsureSessionAsync(modelAuthority, cancellationToken)
                .ConfigureAwait(false);
            var sessionId = _router.ActiveSessionId
                ?? throw new InvalidOperationException(
                    "The game Hermes session has no active identity.");
            _ = RequireExactActiveDurableLineage(sessionId);

            lease = TryAdmitNativeAssistantContinuation(
                SyntheticAssistantContinuationKind.ForegroundProactive);
            if (lease is null)
            {
                return FailedForegroundProactive(
                    "runtime.foreground_proactive_busy",
                    retryable: true);
            }
            lease.EventId = eventId;

            using var dispatch = new CancellationTokenSource();
            dispatch.CancelAfter(NativeAssistantContinuationDispatchTimeout);
            LilithRuntimeResponse response;
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    ["session_id"] = sessionId,
                    ["event_id"] = eventId,
                    ["proactive_dialogue_enabled"] = true,
                    ["perception"] = new
                    {
                        schema_version = 1,
                        source = "foreground-application",
                        application_id = validated.ApplicationId,
                        application_name = validated.ApplicationDisplayName,
                        category = validated.ApplicationCategory,
                        observed_at_unix_ms = validated.ObservedAtUnixMilliseconds,
                        locale = LilithGameBridgeProtocol.DisplayLocaleToken(validated.DisplayLocale)
                    }
                };
                // Preserve the frozen operation binding for reconciliation. The
                // native admission validates freshness before any first attempt.
                if (miniGameContext is not null)
                    parameters["game_context"] = miniGameContext.ToJsonElement();
                response = await _router.RouteAsync(
                    new LilithRuntimeRequest(
                        "session.generate_foreground_proactive",
                        JsonSerializer.SerializeToElement(parameters)),
                    dispatch.Token,
                    modelAuthority).ConfigureAwait(false);
            }
            catch
            {
                await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                    .ConfigureAwait(false);
                return FailedForegroundProactive(
                    "runtime.foreground_proactive_uncertain",
                    retryable: true);
            }

            if (!response.Succeeded)
            {
                var code = FailureCode(
                    response,
                    "runtime.foreground_proactive_rejected");
                if (string.Equals(
                        code,
                        "runtime.foreground_proactive_uncertain",
                        StringComparison.Ordinal))
                {
                    await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                        .ConfigureAwait(false);
                }
                if (string.Equals(
                        code,
                        "runtime.foreground_proactive_session_unavailable",
                        StringComparison.Ordinal))
                {
                    SignalSessionActivationFailure(sessionId);
                }
                if (string.Equals(
                        code,
                        "runtime.game_session_lineage_rejected",
                        StringComparison.Ordinal))
                {
                    QuarantineNativeAssistantLineage(sessionId);
                }
                return FailedForegroundProactive(
                    code,
                    IsRetryableForegroundProactiveCode(code));
            }

            HermesNativeAssistantContinuationResult continuation;
            try
            {
                continuation = HermesTurnCorrelationContract
                    .DecodeNativeAssistantContinuationResult(
                        response.HermesPayload);
                if (!string.Equals(
                        continuation.EventId,
                        eventId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes continued a different foreground operation identity.");
                }
            }
            catch (InvalidDataException)
            {
                await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                    .ConfigureAwait(false);
                return FailedForegroundProactive(
                    "runtime.foreground_proactive_uncertain",
                    retryable: true);
            }

            if (!continuation.CreatesOrRejoinsTurn)
            {
                return CompletedForegroundProactive(continuation) with
                {
                    MiniGameEventReceipt = continuation.Disposition == HermesNativeAssistantContinuationDisposition.Completed
                        ? ReadMiniGameEventReceipt(response.HermesPayload, miniGameContext) : null
                };
            }

            var terminalTask = TakeOrCreateTerminalTask(continuation.TurnId);
            lease.SessionId = sessionId;
            lease.TurnId = continuation.TurnId;
            lease.AssistantMessageId = continuation.AssistantMessageId;
            var retainedLease = lease;
            _ = Task.Run(
                () => ObserveNativeAssistantContinuationAsync(
                    retainedLease,
                    terminalTask));
            gateTransferred = true;
            lease = null;
            return new LilithGameForegroundProactiveOutcome(
                Accepted: true,
                Succeeded: false,
                LilithGameBridgeForegroundProactiveState.Streaming,
                continuation.AlreadyProcessed,
                Retryable: false,
                Code: "ok");
        }
        finally
        {
            if (lease is not null)
                RetireNativeAssistantContinuation(lease);
            if (gateEntered && !gateTransferred)
                _turnGate.Release();
            if (!gateTransferred)
                RetireForegroundTurnDemand(playerTurn: false);
        }
    }

    public async Task<LilithGameNoteDraftOutcome> GenerateNoteDraftAsync(
        Guid requestId, Guid eventId, string category, string topic, string context,
        LilithGameBridgeDisplayLocale displayLocale, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validated = new LilithGameBridgeNoteDraftRequest(
            _snapshot.GenerationId, requestId, 1, eventId, category, topic, context, displayLocale);
        // Tombstones are generation-local, contain no text and are never evicted:
        // capacity exhaustion fails closed, including a busy first attempt.
        lock (_noteDraftAdmissionLock)
        {
            if (_disposed || _noteDraftOperations.Count >= 4096 || _noteDraftEvents.Count >= 4096)
                return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
            // Burn both independent identities even when the other one is a
            // duplicate; a changed request/event cannot launder a used identity.
            var newOperation = _noteDraftOperations.Add(requestId);
            var newEvent = _noteDraftEvents.Add(eventId);
            if (!newOperation || !newEvent)
                return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _noteDraftShutdown.Token);
        deadline.CancelAfter(LilithGameBridgeProtocol.MaximumNoteDraftMilliseconds);
        var entered = false;
        try
        {
            if (!await _turnGate.WaitAsync(0, deadline.Token).ConfigureAwait(false))
                return new(LilithGameBridgeNoteDraftState.Busy, string.Empty);
            entered = true;
            if (_disposed || Volatile.Read(ref _foregroundTurnDemand) != 0
                || Volatile.Read(ref _nativeAssistantContinuationRecoveryRequired) != 0
                || Volatile.Read(ref _nativeAssistantCommitQuarantined) != 0
                || _quarantinedTurn is not null)
                return new(LilithGameBridgeNoteDraftState.Busy, string.Empty);
            using var authority = await _runtime.ModelAuthority.AcquireAsync(
                _snapshot.GenerationId, deadline.Token).ConfigureAwait(false);
            RequireUsableModelAuthority(authority.Snapshot);
            // The candidate came from a published player turn. A cold or changed
            // session is not created/resumed as a side effect of a note draft.
            var sessionId = _router.ActiveSessionId;
            if (sessionId is null || !_router.IsSessionAttested(sessionId))
                return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
            var lineage = RequireExactActiveDurableLineage(sessionId);
            var response = await _router.RouteAsync(new LilithRuntimeRequest(
                "prompt.draft_note", JsonSerializer.SerializeToElement(new
                {
                    session_id = sessionId,
                    operation_id = validated.RequestId.ToString("N"),
                    event_id = validated.EventId.ToString("N"),
                    category = validated.Category,
                    topic = validated.Topic,
                    reply_context = validated.Context,
                    display_locale = LilithGameBridgeProtocol.NoteDraftDisplayLocaleToken(displayLocale)
                })), deadline.Token, authority).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            var currentAuthority = _runtime.ModelAuthority.Snapshot();
            RequireUsableModelAuthority(currentAuthority);
            if (_disposed || currentAuthority.Revision != authority.Snapshot.Revision
                || !IsExactActiveLineage(sessionId, lineage) || !response.Succeeded)
                return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
            var payload = response.HermesPayload;
            if (payload.GetProperty("operation_id").GetString() != requestId.ToString("N")
                || payload.GetProperty("event_id").GetString() != eventId.ToString("N"))
                return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
            var receipt = new LilithGameBridgeNoteDraftResponse(
                _snapshot.GenerationId, requestId, 2, eventId,
                LilithGameBridgeProtocol.ParseNoteDraftState(payload.GetProperty("status").GetString()!),
                payload.GetProperty("text").GetString()!);
            return new(receipt.State, receipt.Text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or InvalidOperationException or ArgumentException or LilithRuntimeException
            or LilithGameBridgeOperationException)
        {
            return new(LilithGameBridgeNoteDraftState.Failed, string.Empty);
        }
        finally
        {
            if (entered)
                _turnGate.Release();
        }
    }

    private async Task<(bool Transferred, LilithMiniGameEventReceipt? Receipt)> TryStartNativeAssistantContinuationAsync(
        string sessionId,
        LilithGameBridgeNativeAssistantCommitRequest request,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        var lease = TryAdmitNativeAssistantContinuation(
            SyntheticAssistantContinuationKind.NativeAssistantExtension);
        if (lease is null)
            return (false, null);

        var retainLease = false;
        try
        {
            if (lease.Preemption.IsCancellationRequested)
                return (false, null);
            using var dispatch = new CancellationTokenSource();
            dispatch.CancelAfter(NativeAssistantContinuationDispatchTimeout);
            LilithRuntimeResponse response;
            try
            {
                var parameters = new Dictionary<string, object?>
                {
                    ["session_id"] = sessionId,
                    ["event_id"] = request.EventId.ToString("N"),
                    ["proactive_dialogue_enabled"] = true,
                    ["perception"] = NativePerceptionPayload(request.Perception)
                };
                if (request.MiniGameContext is not null)
                    parameters["game_context"] = request.MiniGameContext.ToJsonElement();
                response = await _router.RouteAsync(
                    new LilithRuntimeRequest(
                        "session.extend_native_assistant_event",
                        JsonSerializer.SerializeToElement(parameters)),
                    dispatch.Token,
                    modelAuthority).ConfigureAwait(false);
            }
            catch
            {
                await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                    .ConfigureAwait(false);
                return (false, null);
            }

            if (!response.Succeeded)
            {
                var code = FailureCode(
                    response,
                    "runtime.native_assistant_extension_rejected");
                if (string.Equals(
                        code,
                        "runtime.native_assistant_extension_uncertain",
                        StringComparison.Ordinal))
                {
                    await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                        .ConfigureAwait(false);
                }
                return (false, null);
            }

            HermesNativeAssistantContinuationResult continuation;
            try
            {
                continuation = HermesTurnCorrelationContract
                    .DecodeNativeAssistantContinuationResult(
                        response.HermesPayload);
                if (!string.Equals(
                        continuation.EventId,
                        request.EventId.ToString("N"),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes continued a different native event identity.");
                }
            }
            catch (InvalidDataException)
            {
                await ReconcileOrQuarantineNativeAssistantContinuationAsync()
                    .ConfigureAwait(false);
                return (false, null);
            }

            if (!continuation.CreatesOrRejoinsTurn)
                return (false, continuation.Disposition == HermesNativeAssistantContinuationDisposition.Completed
                    ? ReadMiniGameEventReceipt(response.HermesPayload, request.MiniGameContext) : null);

            var terminalTask = TakeOrCreateTerminalTask(continuation.TurnId);
            lease.SessionId = sessionId;
            lease.TurnId = continuation.TurnId;
            lease.EventId = continuation.EventId;
            lease.AssistantMessageId = continuation.AssistantMessageId;
            lease.MiniGameContext = request.MiniGameContext;
            _ = Task.Run(
                () => ObserveNativeAssistantContinuationAsync(
                    lease,
                    terminalTask));
            retainLease = true;
            return (true, null);
        }
        finally
        {
            if (!retainLease)
                RetireNativeAssistantContinuation(lease);
        }
    }

    private NativeAssistantContinuationLease?
        TryAdmitNativeAssistantContinuation(
            SyntheticAssistantContinuationKind kind)
    {
        lock (_notificationProjectionAdmissionLock)
        {
            if (_notificationProjectionAdmissionClosed
                || Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0
                || _playerTurnDemand != 0
                || _nativeAssistantContinuation is not null)
            {
                return null;
            }
            var lease = new NativeAssistantContinuationLease(kind);
            _nativeAssistantContinuation = lease;
            return lease;
        }
    }

    private static Dictionary<string, object> NativePerceptionPayload(LilithGameBridgeNativePerceptionSnapshot value)
    {
        var result = new Dictionary<string, object>
        {
            ["schema_version"] = value.SchemaVersion, ["source"] = value.Source, ["text"] = value.Text,
            ["occurred_at_unix_ms"] = value.OccurredAtUnixMilliseconds, ["node_id"] = value.NodeId,
            ["line_id"] = value.LineId, ["trigger"] = value.Trigger, ["locale"] = value.Locale
        };
        if (value.Application is not null) result["application"] = value.Application.ToNativePayload();
        return result;
    }

    private async Task ObserveNativeAssistantContinuationAsync(
        NativeAssistantContinuationLease lease,
        Task<LilithGameTerminalEvent> terminalTask)
    {
        LilithGameTerminalEvent? presentation = null;
        LilithMiniGameEventReceipt? gameReceipt = null;
        long completedAt = 0;
        try
        {
            LilithGameTerminalEvent terminal;
            try
            {
                terminal = await terminalTask.WaitAsync(
                        TurnTimeout,
                        lease.Preemption.Token)
                    .ConfigureAwait(false);
                completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            catch (Exception exception) when (exception is
                TimeoutException or OperationCanceledException)
            {
                if (!await TryInterruptNativeAssistantContinuationAsync()
                        .ConfigureAwait(false))
                {
                    QuarantineNativeAssistantContinuation();
                }
                return;
            }

            try
            {
                using var authorityTimeout = new CancellationTokenSource(
                    SessionAttestationTimeout);
                await CommitTerminalAuthorityAsync(
                        lease.SessionId,
                        terminal,
                        authorityTimeout.Token)
                    .ConfigureAwait(false);
                if (IsCompletedNativeAssistantContinuation(
                        terminal,
                        lease.Kind))
                {
                    presentation = terminal;
                    gameReceipt = ReadMiniGameEventReceipt(terminal.Payload, lease.MiniGameContext);
                }
            }
            catch
            {
                QuarantineNativeAssistantLineage(lease.SessionId);
            }
        }
        catch
        {
            if (!await TryInterruptNativeAssistantContinuationAsync()
                    .ConfigureAwait(false))
            {
                QuarantineNativeAssistantContinuation();
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(lease.TurnId))
            {
                lock (_eventLock)
                    _terminalWaiters.Remove(lease.TurnId);
            }
            RetireNativeAssistantContinuation(lease);
            _turnGate.Release();
            RetireForegroundTurnDemand(playerTurn: false);
            if (lease.Kind == SyntheticAssistantContinuationKind.NativeAssistantExtension
                && lease.MiniGameContext?.Events is not null)
                PublishMiniGameEventsSettlement(lease.EventId, gameReceipt);
        }
        if (presentation is not null)
        {
            PublishNativeAssistantContinuation(
                lease.EventId,
                lease.AssistantMessageId,
                lease.TurnId,
                completedAt,
                presentation.Text,
                lease.Kind);
        }
    }

    private void PublishMiniGameEventsSettlement(string eventId, LilithMiniGameEventReceipt? receipt)
    {
        if (!Guid.TryParseExact(eventId, "N", out var id)) return;
        TaskCompletionSource<bool> signal;
        lock (_notificationLock)
        {
            if (_disposed) return;
            var sequence = checked(_notificationSequence + 1);
            var entry = new LilithGameBridgeNotificationEntry(sequence,
                LilithGameBridgeProtocol.MiniGameEventsSettledEventType,
                LilithGameBridgeProtocol.MiniGameEventsSettledSchema,
                true, string.Empty, string.Empty, LilithGameBridgeSpeechLocale.Chinese,
                string.Empty, null, id, receipt);
            _notificationSequence = sequence;
            _notifications.Enqueue(entry);
            while (_notifications.Count > MaximumNotificationRetention)
                _syntheticNotificationSpeech.Remove(_notifications.Dequeue().Sequence);
            signal = _notificationSignal;
            _notificationSignal = NewSignal();
        }
        signal.TrySetResult(true);
    }

    private static bool IsCompletedNativeAssistantContinuation(
        LilithGameTerminalEvent terminal,
        SyntheticAssistantContinuationKind kind)
        => string.Equals(terminal.Status, "complete", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(terminal.Text)
            && terminal.Payload.ValueKind == JsonValueKind.Object
            && terminal.Payload.TryGetProperty(
                kind == SyntheticAssistantContinuationKind.ForegroundProactive
                    ? "foreground_proactive_status"
                    : "native_assistant_extension_status",
                out var extensionStatus)
            && extensionStatus.ValueKind == JsonValueKind.String
            && string.Equals(
                extensionStatus.GetString(),
                "completed",
                StringComparison.Ordinal);

    private void PublishNativeAssistantContinuation(
        string eventId,
        string assistantMessageId,
        string turnId,
        long completedAt,
        string text,
        SyntheticAssistantContinuationKind kind)
    {
        try
        {
            TaskCompletionSource<bool> signal;
            lock (_notificationLock)
            {
                if (_disposed)
                    return;
                var projectionKey = eventId + ":" + assistantMessageId;
                var sequence = checked(_notificationSequence + 1);
                var foregroundProactive =
                    kind == SyntheticAssistantContinuationKind.ForegroundProactive;
                var entry = new LilithGameBridgeNotificationEntry(
                    sequence,
                    foregroundProactive
                        ? LilithGameBridgeProtocol.ForegroundProactiveEventType
                        : LilithGameBridgeProtocol
                            .NativeAssistantContinuationEventType,
                    foregroundProactive
                        ? LilithGameBridgeProtocol.ForegroundProactiveSchema
                        : LilithGameBridgeProtocol
                            .NativeAssistantContinuationSchema,
                    succeeded: true,
                    text, string.Empty, LilithGameBridgeSpeechLocale.Chinese, string.Empty,
                    new LilithGameBridgeContinuationContext(eventId, turnId, assistantMessageId,
                        completedAt, completedAt + LilithGameBridgeContinuationContext.LifetimeMilliseconds));
                if (!_nativeAssistantNotificationKeys.Add(projectionKey))
                    return;
                _notificationSequence = sequence;
                _notifications.Enqueue(entry);
                while (_notifications.Count > MaximumNotificationRetention)
                    _syntheticNotificationSpeech.Remove(_notifications.Dequeue().Sequence);
                _nativeAssistantNotificationKeyOrder.Enqueue(projectionKey);
                while (_nativeAssistantNotificationKeyOrder.Count
                    > MaximumNotificationRetention)
                {
                    _nativeAssistantNotificationKeys.Remove(
                        _nativeAssistantNotificationKeyOrder.Dequeue());
                }
                signal = _notificationSignal;
                _notificationSignal = NewSignal();
            }
            signal.TrySetResult(true);
        }
        catch
        {
            // The reply is already durable in Hermes. A presentation-only
            // projection failure must not alter history or session authority.
        }
    }

    private Task<LilithGameTerminalEvent> TakeOrCreateTerminalTask(string turnId)
    {
        lock (_eventLock)
        {
            if (_terminalCache.Remove(turnId, out var cached))
                return Task.FromResult(cached);
            var waiter = new TaskCompletionSource<LilithGameTerminalEvent>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _terminalWaiters.Add(turnId, waiter);
            return waiter.Task;
        }
    }

    private async Task<bool> TryInterruptNativeAssistantContinuationAsync()
    {
        using var timeout = new CancellationTokenSource(TurnInterruptTimeout);
        try
        {
            var sessionId = _router.ActiveSessionId;
            var interrupted = await InterruptSessionAsync(timeout.Token)
                .ConfigureAwait(false);
            if (interrupted && sessionId is not null)
            {
                _router.ReconcileNativeAssistantContinuationAfterInterrupt(
                    sessionId);
            }
            return interrupted;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReconcileOrQuarantineNativeAssistantContinuationAsync()
    {
        if (!await TryInterruptNativeAssistantContinuationAsync()
                .ConfigureAwait(false))
        {
            QuarantineNativeAssistantContinuation();
        }
    }

    private void QuarantineNativeAssistantContinuation()
    {
        if (Interlocked.Exchange(
                ref _nativeAssistantContinuationRecoveryRequired,
                1) != 0)
        {
            return;
        }
        _ = RequireNativeAssistantContinuationGenerationRecoveryAsync();
    }

    private Task RequireNativeAssistantContinuationGenerationRecoveryAsync()
    {
        TaskCompletionSource? completion = null;
        Task recovery;
        lock (_voiceRecoveryLock)
        {
            if (_voiceRecoveryTask is not null)
                return _voiceRecoveryTask;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            recovery = completion.Task;
            _voiceRecoveryTask = recovery;
        }
        _voiceRecoveryRequested.Cancel();
        _ = CompleteNativeAssistantContinuationGenerationRecoveryAsync(
            completion);
        return recovery;
    }

    private async Task CompleteNativeAssistantContinuationGenerationRecoveryAsync(
        TaskCompletionSource completion)
    {
        try
        {
            using var recovery = new CancellationTokenSource(
                VoiceGenerationRecoveryTimeout);
            var receipt = await _runtime.StopVoiceRecoveryGenerationAsync(
                    recovery.Token)
                .ConfigureAwait(false);
            if (receipt.GenerationId != _snapshot.GenerationId
                || !receipt.ProcessExited
                || !receipt.ReadySentinelRemoved)
            {
                throw new LilithRuntimeException(
                    "Native assistant recovery did not retire the exact Hermes generation.");
            }
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void RetireNativeAssistantContinuation(
        NativeAssistantContinuationLease lease)
    {
        lock (_notificationProjectionAdmissionLock)
        {
            if (ReferenceEquals(_nativeAssistantContinuation, lease))
                _nativeAssistantContinuation = null;
        }
        lease.Dispose();
    }

    private static LilithGameForegroundProactiveOutcome
        CompletedForegroundProactive(
            HermesNativeAssistantContinuationResult continuation)
        => continuation.Disposition switch
        {
            HermesNativeAssistantContinuationDisposition.Completed => new(
                Accepted: true,
                Succeeded: true,
                LilithGameBridgeForegroundProactiveState.Completed,
                continuation.AlreadyProcessed,
                Retryable: false,
                Code: "ok"),
            HermesNativeAssistantContinuationDisposition.Silent => new(
                Accepted: true,
                Succeeded: false,
                LilithGameBridgeForegroundProactiveState.Silent,
                continuation.AlreadyProcessed,
                Retryable: false,
                Code: "ok"),
            HermesNativeAssistantContinuationDisposition.Abandoned => new(
                Accepted: false,
                Succeeded: false,
                LilithGameBridgeForegroundProactiveState.Abandoned,
                continuation.AlreadyProcessed,
                Retryable: false,
                Code: "runtime.foreground_proactive_abandoned"),
            _ => throw new InvalidDataException(
                "Hermes foreground proactive terminal was not complete.")
        };

    private static LilithMiniGameEventReceipt? ReadMiniGameEventReceipt(
        JsonElement payload, LilithMiniGamePublicContext? expected)
    {
        // Optional metadata must never consume data without exact host proof or
        // turn ordinary text/speech into failure when a receipt is unavailable.
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("game_event_receipt", out var value)) return null;
        try
        {
            var receipt = LilithMiniGameEventReceipt.Read(value);
            return receipt.Matches(expected) ? receipt : null;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private static LilithGameForegroundProactiveOutcome
        FailedForegroundProactive(string code, bool retryable)
        => new(
            Accepted: false,
            Succeeded: false,
            LilithGameBridgeForegroundProactiveState.Abandoned,
            AlreadyProcessed: false,
            Retryable: retryable,
            Code: code);

    internal static bool IsRetryableForegroundProactiveCode(string code)
        => code is "runtime.game_turn_busy"
            or "runtime.session_activation_not_sent"
            or "runtime.foreground_proactive_not_sent"
            or "runtime.foreground_proactive_busy"
            or "runtime.foreground_proactive_session_unavailable"
            or "runtime.foreground_proactive_persistence_failed"
            or "runtime.foreground_proactive_uncertain"
            or "runtime.voice_capture_active"
            or "runtime.checkpoint_restore_active"
            or "runtime.native_config_mutation_in_flight"
            or "runtime.session_lifecycle_busy"
            or "runtime.native_assistant_continuation_recovery_required";

    private static LilithGameNativeAssistantCommitOutcome
        RetryableNativeAssistantCommit(string code)
        => new(false, false, true, code);

    internal static bool IsRetryableNativeAssistantCommitCode(string code)
        => code is "runtime.game_turn_busy"
            or "runtime.session_activation_not_sent"
            or "runtime.native_assistant_append_not_sent"
            or "runtime.native_assistant_busy"
            or "runtime.native_assistant_session_unavailable"
            or "runtime.native_assistant_persistence_retryable"
            or "runtime.native_assistant_append_uncertain"
            or "runtime.native_assistant_receipt_uncertain"
            or "runtime.voice_capture_active"
            or "runtime.checkpoint_restore_active"
            or "runtime.native_config_mutation_in_flight"
            or "runtime.session_lifecycle_busy"
            or "runtime.native_assistant_continuation_recovery_required";

    internal static NativeAssistantCommitReceipt
        DecodeNativeAssistantCommitReceipt(
            string expectedSessionId,
            LilithGameBridgeNativeAssistantCommitRequest request,
            JsonElement result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSessionId);
        ArgumentNullException.ThrowIfNull(request);
        RequireExactProperties(
            result,
            "accepted", "already_committed", "retryable", "code",
            "session_id", "event_id", "message_id", "content_sha256",
            "stored_session_id", "durable_lineage", "durable", "message");

        var accepted = RequiredBoolean(result, "accepted");
        var alreadyCommitted = RequiredBoolean(result, "already_committed");
        var retryable = RequiredBoolean(result, "retryable");
        var code = RequiredString(result, "code");
        var expectedCode = alreadyCommitted
            ? "native_assistant_event_duplicate"
            : "native_assistant_event_committed";
        var eventId = request.EventId.ToString("N");
        if (!accepted
            || retryable
            || !string.Equals(code, expectedCode, StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(result, "session_id"),
                expectedSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(result, "event_id"),
                eventId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(result, "message_id"),
                eventId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native assistant commit identity was rejected.");
        }

        var contentSha256 = RequiredString(result, "content_sha256");
        var expectedContentSha256 = Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(request.Text)))
            .ToLowerInvariant();
        if (!IsLowerHex(contentSha256, 64)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(contentSha256),
                Encoding.ASCII.GetBytes(expectedContentSha256)))
        {
            throw new InvalidDataException(
                "Hermes native assistant content digest was rejected.");
        }

        var storedSessionId = RequiredBoundedIdentity(
            result,
            "stored_session_id");
        var lineage = result.GetProperty("durable_lineage");
        RequireExactProperties(
            lineage,
            "requested_session_id", "resolved_session_id", "session_ids",
            "source", "archived");
        if (!string.Equals(
                RequiredBoundedIdentity(lineage, "resolved_session_id"),
                storedSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(lineage, "source"),
                "lilith-game",
                StringComparison.Ordinal)
            || !RequiredBoolean(lineage, "archived"))
        {
            throw new InvalidDataException(
                "Hermes native assistant durable lineage was rejected.");
        }
        _ = RequiredBoundedIdentity(lineage, "requested_session_id");
        var lineageIds = lineage.GetProperty("session_ids");
        if (lineageIds.ValueKind != JsonValueKind.Array
            || lineageIds.GetArrayLength() is <= 0 or > 2_048)
        {
            throw new InvalidDataException(
                "Hermes native assistant durable lineage was rejected.");
        }
        var lineageSessions = lineageIds.EnumerateArray()
            .Select(static value =>
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Hermes native assistant durable lineage was rejected.");
                }
                var identity = value.GetString()!;
                if (string.IsNullOrWhiteSpace(identity)
                    || identity.Any(char.IsControl)
                    || StrictUtf8.GetByteCount(identity)
                        > LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes)
                {
                    throw new InvalidDataException(
                        "Hermes native assistant durable lineage was rejected.");
                }
                return identity;
            })
            .ToArray();
        if (lineageSessions.Distinct(StringComparer.Ordinal).Count()
                != lineageSessions.Length
            || !string.Equals(
                lineageSessions[0],
                RequiredString(lineage, "requested_session_id"),
                StringComparison.Ordinal)
            || !string.Equals(
                lineageSessions[^1],
                storedSessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native assistant durable lineage was rejected.");
        }

        var durable = result.GetProperty("durable");
        RequireExactProperties(
            durable,
            "source", "row_id", "session_id", "timestamp",
            "conversation_activity");
        var durableSessionId = RequiredBoundedIdentity(durable, "session_id");
        if (!string.Equals(
                RequiredString(durable, "source"),
                "state.db",
                StringComparison.Ordinal)
            || !RequiredBoolean(durable, "conversation_activity")
            || !lineageSessions.Contains(
                durableSessionId,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native assistant durable row was rejected.");
        }
        _ = RequiredPositiveInt64(durable, "row_id");
        var durableTimestamp = RequiredFiniteTimestamp(durable, "timestamp");

        var message = result.GetProperty("message");
        RequireExactProperties(
            message,
            "message_id", "role", "text", "timestamp", "display_kind",
            "display_metadata");
        if (!string.Equals(
                RequiredString(message, "message_id"),
                eventId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(message, "role"),
                "assistant",
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(message, "text"),
                request.Text,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(message, "display_kind"),
                "native_assistant_event",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native assistant message was rejected.");
        }
        var messageTimestamp = RequiredFiniteTimestamp(message, "timestamp");
        if (messageTimestamp != durableTimestamp
            || Math.Abs(
                messageTimestamp * 1000d
                - request.OccurredAtUnixMilliseconds) > 1d)
        {
            throw new InvalidDataException(
                "Hermes native assistant timestamp was rejected.");
        }

        var metadata = message.GetProperty("display_metadata");
        RequireExactProperties(
            metadata,
            "lilith_native_event_id", "lilith_native_source",
            "lilith_native_occurred_at_unix_ms", "lilith_native_node_id",
            "lilith_native_line_id", "lilith_native_trigger",
            "lilith_native_locale");
        if (!string.Equals(
                RequiredString(metadata, "lilith_native_event_id"),
                eventId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(metadata, "lilith_native_source"),
                "lilith-native",
                StringComparison.Ordinal)
            || RequiredPositiveInt64(
                    metadata,
                    "lilith_native_occurred_at_unix_ms")
                != request.OccurredAtUnixMilliseconds
            || !string.Equals(
                RequiredString(metadata, "lilith_native_node_id"),
                request.NodeId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(metadata, "lilith_native_line_id"),
                request.LineId,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(metadata, "lilith_native_trigger"),
                request.Trigger,
                StringComparison.Ordinal)
            || !string.Equals(
                RequiredString(metadata, "lilith_native_locale"),
                request.Locale,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native assistant metadata was rejected.");
        }

        return new NativeAssistantCommitReceipt(
            alreadyCommitted,
            eventId);
    }

    private static void RequireExactProperties(
        JsonElement value,
        params string[] expectedNames)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes native assistant receipt was malformed.");
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        var actual = value.EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        if (actual.Length != expected.Count
            || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
            || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt shape was rejected.");
        }
    }

    private static string RequiredString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var result)
            || result.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt string was rejected.");
        }
        return result.GetString()!;
    }

    private static bool RequiredBoolean(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var result)
            || result.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt boolean was rejected.");
        }
        return result.GetBoolean();
    }

    private static string RequiredBoundedIdentity(
        JsonElement value,
        string property)
    {
        var identity = RequiredString(value, property);
        if (string.IsNullOrWhiteSpace(identity)
            || identity.Any(char.IsControl)
            || StrictUtf8.GetByteCount(identity)
                > LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes)
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt identity was rejected.");
        }
        return identity;
    }

    private static long RequiredPositiveInt64(
        JsonElement value,
        string property)
    {
        if (!value.TryGetProperty(property, out var result)
            || result.ValueKind != JsonValueKind.Number
            || !result.TryGetInt64(out var integer)
            || integer <= 0)
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt integer was rejected.");
        }
        return integer;
    }

    private static double RequiredFiniteTimestamp(
        JsonElement value,
        string property)
    {
        if (!value.TryGetProperty(property, out var result)
            || result.ValueKind != JsonValueKind.Number
            || !result.TryGetDouble(out var timestamp)
            || !double.IsFinite(timestamp)
            || timestamp <= 0)
        {
            throw new InvalidDataException(
                "Hermes native assistant receipt timestamp was rejected.");
        }
        return timestamp;
    }

    private static bool IsLowerHex(string value, int length)
        => value.Length == length
            && value.All(static character => character is
                >= '0' and <= '9' or >= 'a' and <= 'f');

    private void RegisterForegroundTurnDemand(
        bool playerTurn,
        bool preemptNativeContinuation)
    {
        CancellationTokenSource? activeProjection;
        CancellationTokenSource? nativeContinuationPreemption;
        lock (_notificationProjectionAdmissionLock)
        {
            _foregroundTurnDemand++;
            if (playerTurn)
                _playerTurnDemand++;
            activeProjection = _activeNotificationProjection;
            nativeContinuationPreemption = preemptNativeContinuation
                ? _nativeAssistantContinuation?.Preemption
                : null;
        }
        if (playerTurn) InvalidateSyntheticPresentations();
        // A reminder projection is auxiliary and retryable.  A newly admitted
        // F6/F7 turn preempts it so foreground input never waits for background
        // translation on the single Hermes model rail.  Cancel outside the
        // admission lock because cancellation continuations may complete the
        // projection inline and need that same lock to retire their lease.
        try
        {
            if (activeProjection != null)
                _ = activeProjection.CancelAsync();
            if (nativeContinuationPreemption != null)
                _ = nativeContinuationPreemption.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The projection completed after its lease was observed.  Its gate
            // release is already in progress and the demand count still blocks
            // a replacement background projection.
        }
    }

    private void RetireForegroundTurnDemand(bool playerTurn)
    {
        lock (_notificationProjectionAdmissionLock)
        {
            if (_foregroundTurnDemand <= 0)
            {
                throw new InvalidOperationException(
                    "Foreground game-turn demand underflowed.");
            }
            _foregroundTurnDemand--;
            if (playerTurn)
            {
                if (_playerTurnDemand <= 0)
                {
                    throw new InvalidOperationException(
                        "Player game-turn demand underflowed.");
                }
                _playerTurnDemand--;
            }
        }
    }

    private void RequestNativeAssistantContinuationPreemption()
    {
        CancellationTokenSource? preemption;
        lock (_notificationProjectionAdmissionLock)
            preemption = _nativeAssistantContinuation?.Preemption;
        try
        {
            if (preemption != null)
                _ = preemption.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The continuation retired after its lease was observed.
        }
    }

    private async Task<string> ResolveSpeechTextAsync(
        string sessionId,
        string assistantText,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken,
        string? completedTurnId = null)
        => (await ResolveSpeechProjectionAsync(sessionId, assistantText, displayLocale,
            speechLocale, cancellationToken, completedTurnId).ConfigureAwait(false)).Text;

    private async Task<(string Text, string Emotion)> ResolveSpeechProjectionAsync(
        string sessionId,
        string assistantText,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken,
        string? completedTurnId = null,
        LilithGameBridgeContinuationContext? continuation = null)
    {
        if (SpeechMatchesDisplay(displayLocale, speechLocale)
            && !(completedTurnId is not null && speechLocale == LilithGameBridgeSpeechLocale.Japanese))
            return (assistantText, string.Empty);

        try
        {
            var parameters = new Dictionary<string, object>
            {
                ["session_id"] = sessionId,
                ["text"] = assistantText,
                ["display_locale"] = LilithGameBridgeProtocol.DisplayLocaleToken(displayLocale),
                ["speech_locale"] = LilithGameBridgeProtocol.SpeechLocaleToken(speechLocale)
            };
            if (completedTurnId is not null)
                parameters["turn_id"] = completedTurnId;
            if (continuation is not null)
            {
                parameters["notification_event_id"] = continuation.EventId;
                parameters["notification_message_id"] = continuation.MessageId;
            }
            var response = await _router.RouteAsync(
                new LilithRuntimeRequest(
                    "prompt.project_speech",
                    JsonSerializer.SerializeToElement(parameters)),
                cancellationToken).ConfigureAwait(false);
            if (!response.Succeeded)
                return (string.Empty, string.Empty);
            var speechText = response.HermesPayload.GetProperty("speech_text").GetString() ?? string.Empty;
            var emotion = response.HermesPayload.TryGetProperty("speech_emotion", out var hint)
                ? hint.GetString() ?? string.Empty : string.Empty;
            if (!LilithJapaneseSpeechEmotion.IsValid(emotion))
                return (string.Empty, string.Empty);
            return (speechText, emotion);
        }
        catch
        {
            // Speech projection is auxiliary and memory-isolated. The durable
            // primary turn remains successful even when projection is not.
            return (string.Empty, string.Empty);
        }
    }

    private static bool SpeechMatchesDisplay(
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale)
        => speechLocale switch
        {
            LilithGameBridgeSpeechLocale.Chinese => displayLocale is
                LilithGameBridgeDisplayLocale.TraditionalChinese
                or LilithGameBridgeDisplayLocale.SimplifiedChinese,
            LilithGameBridgeSpeechLocale.Japanese =>
                displayLocale == LilithGameBridgeDisplayLocale.Japanese,
            _ => false
        };

    public async Task<LilithGameHistoryPage> ReadHistoryAsync(
        string cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validatedRequest = new LilithGameBridgeHistoryRequest(
            _snapshot.GenerationId,
            Guid.NewGuid(),
            1,
            cursor,
            pageSize);
        await _historyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    cancellationToken)
                .ConfigureAwait(false);
            RequireUsableModelAuthority(modelAuthority.Snapshot);
            await EnsureSessionAsync(
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
            HistorySnapshot snapshot;
            var offset = 0;
            if (validatedRequest.Cursor.Length == 0)
            {
                snapshot = await CaptureHistorySnapshotAsync(
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
                _historySnapshot = snapshot;
            }
            else if (!TryResolveCursor(validatedRequest.Cursor, out snapshot, out offset))
            {
                return new LilithGameHistoryPage(
                    Array.Empty<LilithGameBridgeHistoryEntry>(),
                    string.Empty,
                    false,
                    "runtime.history_cursor_expired");
            }

            var entries = snapshot.Entries
                .Skip(offset)
                .Take(validatedRequest.PageSize)
                .ToArray();
            var nextOffset = checked(offset + entries.Length);
            var hasMore = nextOffset < snapshot.Entries.Count;
            var nextCursor = hasMore
                ? snapshot.Id.ToString("N")
                    + "."
                    + nextOffset.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return new LilithGameHistoryPage(entries, nextCursor, hasMore, "ok");
        }
        catch (InvalidDataException)
        {
            return new LilithGameHistoryPage(
                Array.Empty<LilithGameBridgeHistoryEntry>(),
                string.Empty,
                false,
                "runtime.history_rejected");
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        CancellationToken cancellationToken)
        => await ReadNotificationsAsync(
                afterSequence,
                waitMilliseconds,
                LilithGameBridgeDisplayLocale.TraditionalChinese,
                LilithGameBridgeSpeechLocale.Chinese,
                tailOnly: false,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
        => await ReadNotificationsAsync(
                afterSequence,
                waitMilliseconds,
                displayLocale,
                speechLocale,
                tailOnly: false,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<LilithGameNotificationPage> ReadNotificationsAsync(
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        bool tailOnly,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = new LilithGameBridgeNotificationRequest(
            _snapshot.GenerationId,
            Guid.NewGuid(),
            sequence: 1,
            afterSequence,
            waitMilliseconds,
            displayLocale,
            speechLocale,
            tailOnly);

        Task? signal = null;
        LilithGameNotificationPage page;
        lock (_notificationLock)
        {
            page = CaptureNotificationPage(afterSequence);
            if (tailOnly)
            {
                return page with
                {
                    Entries = Array.Empty<
                        LilithGameBridgeNotificationEntry>(),
                    HasMore = false
                };
            }
            if (page.Succeeded
                && page.Entries.Count == 0
                && waitMilliseconds != 0)
            {
                signal = _notificationSignal.Task;
            }
        }

        if (signal is not null)
        {
            try
            {
                await signal.WaitAsync(
                        TimeSpan.FromMilliseconds(waitMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A bounded empty long poll is a successful empty page. The
                // caller may issue the next poll without advancing its cursor.
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_notificationLock)
                page = CaptureNotificationPage(afterSequence);
        }

        return await ProjectNotificationSpeechAsync(
                page,
                displayLocale,
                speechLocale,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<LilithGameNotificationPage>
        ProjectNotificationSpeechAsync(
            LilithGameNotificationPage page,
            LilithGameBridgeDisplayLocale displayLocale,
            LilithGameBridgeSpeechLocale speechLocale,
            CancellationToken cancellationToken)
    {
        if (!page.Succeeded || page.Entries.Count == 0)
            return page;

        // A speech projection is a model turn.  Return one notification at a
        // time so a retained burst cannot hold one game long-poll through many
        // serial model calls.  HasMore keeps the remaining entries reachable
        // on the next cursor exchange.
        var entry = page.Entries[0];
        page = page with
        {
            Entries = new[] { entry },
            HasMore = page.HasMore || page.Entries.Count > 1
        };
        if (string.Equals(
                entry.Type,
                LilithGameBridgeProtocol.ForegroundProactivePlayerReplyEventType,
                StringComparison.Ordinal)
            || entry.Type == LilithGameBridgeProtocol.MiniGameEventsSettledEventType)
        {
            // This is a content-free control signal for the Plugin's local
            // proactive timer. It must never enter speech projection.
            return page;
        }
        var directAssistantReply = string.Equals(
                entry.Type,
                LilithGameBridgeProtocol.NativeAssistantContinuationEventType,
                StringComparison.Ordinal)
            || string.Equals(
                entry.Type,
                LilithGameBridgeProtocol.ForegroundProactiveEventType,
                StringComparison.Ordinal);
        if (directAssistantReply)
        {
            // This lane must never withhold a durable bubble behind auxiliary
            // speech, or retry a failed projection on every notification poll.
            var syntheticEntry = await ProjectSyntheticNotificationOnceAsync(entry, speechLocale, cancellationToken)
                .ConfigureAwait(false);
            return page with { Entries = new[] { syntheticEntry } };
        }
        var body = directAssistantReply
            ? entry.Text.Trim()
            : NotificationBody(entry.Text);
        var sourceLocale = DetectNotificationSourceLocale(body);
        var source = NotificationSpeechSource(entry, body, sourceLocale);
        var sourceMatchesSpeech = SpeechMatchesDisplay(
            sourceLocale,
            speechLocale);
        var sessionId = _router.ActiveSessionId;
        var speechText = sourceMatchesSpeech
            ? source
            : string.Empty;
        if (!sourceMatchesSpeech && !string.IsNullOrWhiteSpace(sessionId))
        {
            // Background speech never queues ahead of foreground F6/F7 work.
            // The caller retains its cursor and retries after every admitted
            // foreground turn has left the single model rail.
            if (HasForegroundTurnDemand())
            {
                return RetryNotificationProjection(
                    page,
                    "runtime.notification_projection_deferred");
            }
            if (NotificationProjectionAdmissionIsClosed())
            {
                return RetryNotificationProjection(
                    page,
                    "runtime.notification_projection_deferred");
            }
            var gateEntered = false;
            CancellationTokenSource? projectionCancellation = null;
            var preemptedByForeground = false;
            try
            {
                bool gateAvailable;
                try
                {
                    gateAvailable = await _turnGate.WaitAsync(
                            millisecondsTimeout: 0,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return RetryNotificationProjection(
                        page,
                        "runtime.notification_projection_deferred");
                }
                if (!gateAvailable)
                {
                    return RetryNotificationProjection(
                        page,
                        "runtime.notification_projection_deferred");
                }
                gateEntered = true;
                lock (_notificationProjectionAdmissionLock)
                {
                    if (_notificationProjectionAdmissionClosed
                        || Volatile.Read(
                            ref _nativeAssistantContinuationRecoveryRequired) != 0
                        || _foregroundTurnDemand != 0
                        || _activeNotificationProjection != null)
                    {
                        return RetryNotificationProjection(
                            page,
                            "runtime.notification_projection_deferred");
                    }
                    projectionCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                    _activeNotificationProjection =
                        projectionCancellation;
                }
                speechText = await ResolveSpeechTextAsync(
                        sessionId,
                        source,
                        sourceLocale,
                        speechLocale,
                        projectionCancellation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (projectionCancellation != null)
                {
                    preemptedByForeground =
                        projectionCancellation.IsCancellationRequested
                        && !cancellationToken.IsCancellationRequested;
                    lock (_notificationProjectionAdmissionLock)
                    {
                        if (ReferenceEquals(
                                _activeNotificationProjection,
                                projectionCancellation))
                        {
                            _activeNotificationProjection = null;
                        }
                    }
                    projectionCancellation.Dispose();
                }
                if (gateEntered)
                    _turnGate.Release();
            }
            if (preemptedByForeground)
            {
                return RetryNotificationProjection(
                    page,
                    "runtime.notification_projection_deferred");
            }
        }
        if (!sourceMatchesSpeech && string.IsNullOrWhiteSpace(speechText))
        {
            return RetryNotificationProjection(
                page,
                "runtime.notification_projection_failed");
        }
        var projected = new LilithGameBridgeNotificationEntry(
            entry.Sequence,
            entry.Type,
            entry.Schema,
            entry.Succeeded,
            entry.Text,
            speechText,
            speechLocale);
        return page with { Entries = new[] { projected } };
    }

    private async Task<LilithGameBridgeNotificationEntry> ProjectSyntheticNotificationOnceAsync(
        LilithGameBridgeNotificationEntry entry, LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
    {
        bool Retired() => entry.Continuation is { } value
            && value.CompletedAtUnixMilliseconds <= Interlocked.Read(ref _syntheticPresentationInvalidatedAt);
        LilithGameBridgeNotificationEntry WithSpeech(string text, string emotion = "")
        {
            var retired = Retired();
            return new(entry.Sequence, entry.Type, entry.Schema, entry.Succeeded, entry.Text,
                retired ? string.Empty : text, speechLocale, retired ? string.Empty : emotion,
                retired ? entry.Continuation?.Retire() : entry.Continuation);
        }
        var empty = WithSpeech(string.Empty);
        lock (_notificationLock)
        {
            if (_syntheticNotificationSpeech.TryGetValue(entry.Sequence, out var cached))
                return cached.SpeechLocale == speechLocale && !Retired() ? cached : WithSpeech(string.Empty);
            // Claim before any await. Concurrent polls, cancellation, locale
            // changes and failures can only receive text, never mint a retry.
            _syntheticNotificationSpeech[entry.Sequence] = empty;
            while (_syntheticNotificationSpeech.Count > MaximumNotificationRetention)
                _syntheticNotificationSpeech.Remove(_syntheticNotificationSpeech.Keys.Min());
        }
        var context = entry.Continuation;
        if (context is null || Retired() || !context.IsCurrent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) return WithSpeech(string.Empty);
        var sourceLocale = DetectNotificationSourceLocale(entry.Text);
        if (SpeechMatchesDisplay(sourceLocale, speechLocale) && speechLocale != LilithGameBridgeSpeechLocale.Japanese)
        {
            var direct = WithSpeech(entry.Text);
            lock (_notificationLock) _syntheticNotificationSpeech[entry.Sequence] = direct;
            return direct;
        }
        if (HasForegroundTurnDemand() || NotificationProjectionAdmissionIsClosed()) return WithSpeech(string.Empty);
        var sessionId = _router.ActiveSessionId;
        if (string.IsNullOrWhiteSpace(sessionId) || !await _turnGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return WithSpeech(string.Empty);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Native recovery has a 20-second one-attempt budget. Allow transport
        // overhead without ever extending the presentation's own deadline.
        bounded.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(22_000,
            context.ExpiresAtUnixMilliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))));
        try
        {
            lock (_notificationProjectionAdmissionLock)
            {
                if (_notificationProjectionAdmissionClosed || _foregroundTurnDemand != 0
                    || _activeNotificationProjection != null) return WithSpeech(string.Empty);
                _activeNotificationProjection = bounded;
            }
            var speech = await ResolveSpeechProjectionAsync(sessionId, entry.Text, sourceLocale,
                speechLocale, bounded.Token, context.TurnId, context).ConfigureAwait(false);
            if (bounded.IsCancellationRequested || Retired() || !context.IsCurrent(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) return WithSpeech(string.Empty);
            var result = WithSpeech(speech.Text, speech.Emotion);
            lock (_notificationLock) _syntheticNotificationSpeech[entry.Sequence] = result;
            return result;
        }
        catch (OperationCanceledException) { return WithSpeech(string.Empty); }
        finally
        {
            lock (_notificationProjectionAdmissionLock)
                if (ReferenceEquals(_activeNotificationProjection, bounded)) _activeNotificationProjection = null;
            _turnGate.Release();
        }
    }

    private void InvalidateSyntheticPresentations()
    {
        lock (_notificationLock)
            Interlocked.Exchange(ref _syntheticPresentationInvalidatedAt,
                Math.Max(_syntheticPresentationInvalidatedAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private bool HasForegroundTurnDemand()
    {
        lock (_notificationProjectionAdmissionLock)
            return _foregroundTurnDemand != 0
                || Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0;
    }

    private bool NotificationProjectionAdmissionIsClosed()
    {
        lock (_notificationProjectionAdmissionLock)
            return _notificationProjectionAdmissionClosed
                || Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0;
    }

    private static LilithGameNotificationPage RetryNotificationProjection(
        LilithGameNotificationPage page,
        string code)
        => page with
        {
            Entries = Array.Empty<LilithGameBridgeNotificationEntry>(),
            HasMore = false,
            Code = code
        };

    private static string NotificationBody(string displayText)
    {
        var newline = displayText.IndexOf('\n');
        return newline >= 0 && newline + 1 < displayText.Length
            ? displayText[(newline + 1)..].Trim()
            : displayText.Trim();
    }

    private static LilithGameBridgeDisplayLocale
        DetectNotificationSourceLocale(string text)
    {
        if (Regex.IsMatch(
                text,
                "[\\u3040-\\u30ff\\u31f0-\\u31ff\\uff66-\\uff9f]"))
        {
            return LilithGameBridgeDisplayLocale.Japanese;
        }
        if (Regex.IsMatch(
                text,
                "[\\u3400-\\u4dbf\\u4e00-\\u9fff\\uf900-\\ufaff]"))
        {
            return LilithGameBridgeDisplayLocale.TraditionalChinese;
        }
        return LilithGameBridgeDisplayLocale.English;
    }

    private static string NotificationSpeechSource(
        LilithGameBridgeNotificationEntry entry,
        string body,
        LilithGameBridgeDisplayLocale sourceLocale)
    {
        if (entry.Succeeded)
            return body;
        var prefix = sourceLocale switch
        {
            LilithGameBridgeDisplayLocale.TraditionalChinese =>
                "排程未完成。",
            LilithGameBridgeDisplayLocale.SimplifiedChinese =>
                "排程未完成。",
            LilithGameBridgeDisplayLocale.Japanese =>
                "予定は完了しなかったよ。",
            LilithGameBridgeDisplayLocale.English =>
                "The scheduled task did not complete. ",
            _ => string.Empty
        };
        return prefix + body;
    }

    private LilithGameNotificationPage CaptureNotificationPage(
        long afterSequence)
    {
        var latestSequence = _notificationSequence;
        var oldestAvailableSequence = _notifications.Count == 0
            ? 0
            : _notifications.Peek().Sequence;
        if (afterSequence > latestSequence)
        {
            return new LilithGameNotificationPage(
                Array.Empty<LilithGameBridgeNotificationEntry>(),
                oldestAvailableSequence,
                latestSequence,
                HasMore: false,
                Code: "runtime.notification_cursor_ahead");
        }
        if (oldestAvailableSequence > 0
            && afterSequence < oldestAvailableSequence - 1)
        {
            return new LilithGameNotificationPage(
                Array.Empty<LilithGameBridgeNotificationEntry>(),
                oldestAvailableSequence,
                latestSequence,
                HasMore: false,
                Code: "runtime.notification_cursor_expired");
        }

        var entries = _notifications
            .Where(entry => entry.Sequence > afterSequence)
            .Take(LilithGameBridgeProtocol.MaximumNotificationPageSize)
            .ToArray();
        var hasMore = entries.Length > 0
            && entries[^1].Sequence < latestSequence;
        return new LilithGameNotificationPage(
            entries,
            oldestAvailableSequence,
            latestSequence,
            hasMore,
            "ok");
    }

    public Task<bool> InterruptAsync(
        Guid clientTurnId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (clientTurnId == Guid.Empty)
            throw new ArgumentException("Client turn identity is required.", nameof(clientTurnId));
        // Once the complete, authenticated Interrupt frame reaches Host, its
        // exact turn intent is committed independently of whether the caller
        // keeps the one-response pipe open. The token controls transport, not
        // cancellation authority.
        var committed = CommitInterruptAfterAuthenticatedFrameAsync(
            _turnAdmissions,
            clientTurnId);
        InvalidateSyntheticPresentations();
        RequestNativeAssistantContinuationPreemption();
        return committed;
    }

    internal static Task<bool> CommitInterruptAfterAuthenticatedFrameAsync(
        LilithGameTurnAdmissionRegistry turnAdmissions,
        Guid clientTurnId)
    {
        ArgumentNullException.ThrowIfNull(turnAdmissions);
        var disposition = turnAdmissions.RecordInterrupt(clientTurnId);
        return Task.FromResult(
            disposition
                != LilithGameTurnInterruptDisposition.RecoveryRequired);
    }

    public async Task<LilithGameVoiceStartOutcome> StartVoiceAsync(
        Guid captureId,
        LilithGameBridgeVoiceLanguage language,
        int maximumDurationMilliseconds,
        LilithGameVoicePeerIdentity peer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        if (!Enum.IsDefined(language))
            throw new ArgumentOutOfRangeException(nameof(language));
        if (maximumDurationMilliseconds is
            < LilithGameBridgeProtocol.MinimumVoiceCaptureDurationMilliseconds
            or > LilithGameBridgeProtocol.MaximumVoiceCaptureDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDurationMilliseconds));
        }
        peer.Validate();

        // Bound the whole pre-admission path, including the hidden-session
        // activation and the gateway request.  Physical cleanup/recovery has
        // its own independent bound and must not inherit a disconnected
        // client's cancellation token.
        using var startDeadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        startDeadline.CancelAfter(VoiceGatewayDispatchTimeout);
        var startToken = startDeadline.Token;
        LilithRuntimeModelAuthorityLease? modelAuthority = null;
        var turnGateEntered = false;
        RegisterForegroundTurnDemand(
            playerTurn: true,
            preemptNativeContinuation: true);
        try
        {
            await _turnGate.WaitAsync(startToken).ConfigureAwait(false);
            turnGateEntered = true;
            if (Volatile.Read(
                    ref _nativeAssistantContinuationRecoveryRequired) != 0)
            {
                return new LilithGameVoiceStartOutcome(
                    false,
                    "runtime.native_assistant_continuation_recovery_required");
            }
            await _voiceGate.WaitAsync(startToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            if (VoiceRecoveryRequired)
            {
                return new LilithGameVoiceStartOutcome(
                    false,
                    "runtime.voice_generation_restarting");
            }
            if (peer.PeerExited.IsCancellationRequested)
                return new LilithGameVoiceStartOutcome(false, "runtime.voice_peer_exited");
            var admission = _voiceAdmissionTombstones.AdmitStart(
                    captureId,
                    peer.ProcessId,
                    peer.ProcessCreationTime);
            if (admission == LilithGameVoiceAdmissionDisposition.Cancelled)
            {
                return new LilithGameVoiceStartOutcome(
                    false,
                    "runtime.voice_cancelled");
            }
            lock (_eventLock)
            {
                if (_voiceCapture is not null)
                    return new LilithGameVoiceStartOutcome(false, "runtime.voice_busy");
            }

            modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    startToken)
                .ConfigureAwait(false);
            RequireUsableModelAuthority(modelAuthority.Snapshot);
            await EnsureSessionAsync(modelAuthority, startToken)
                .ConfigureAwait(false);
            var sessionId = _router.ActiveSessionId
                ?? throw new InvalidOperationException(
                    "The game Hermes session has no active identity.");
            var owner = new LilithRuntimeVoiceCaptureOwner(
                _snapshot.GenerationId,
                LilithRuntimeSessionSurface.GameBridge,
                sessionId,
                captureId,
                peer.ProcessId,
                peer.ProcessCreationTime).Validate();
            admission = _voiceAdmissionTombstones.CommitStart(
                captureId,
                peer.ProcessId,
                peer.ProcessCreationTime);
            if (admission == LilithGameVoiceAdmissionDisposition.Cancelled)
            {
                return new LilithGameVoiceStartOutcome(
                    false,
                    "runtime.voice_cancelled");
            }
            if (admission == LilithGameVoiceAdmissionDisposition.RecoveryRequired)
            {
                // Tombstone saturation is not permission to evict an older
                // cancel and resurrect a queued Start. Retire this exact
                // generation before responding, while no microphone owner has
                // been admitted yet.
                modelAuthority.Dispose();
                modelAuthority = null;
                await RequireVoiceGenerationRecoveryAsync(owner)
                    .ConfigureAwait(false);
                return new LilithGameVoiceStartOutcome(
                    false,
                    "runtime.voice_generation_restarted");
            }
            if (!_runtime.VoiceCaptureArbiter.TryBeginCapture(owner))
            {
                _voiceAdmissionTombstones.ReleaseStart(
                    captureId,
                    peer.ProcessId,
                    peer.ProcessCreationTime);
                return new LilithGameVoiceStartOutcome(false, "runtime.voice_busy");
            }

            var capture = new ActiveGameVoiceCapture(owner);
            lock (_eventLock)
                _voiceCapture = capture;
            try
            {
                ReplaceVoiceOwnerLease(owner, peer.PeerExited);
            }
            catch
            {
                _ = _runtime.VoiceCaptureArbiter.Release(owner);
                ClearVoiceOwnerState(owner, releaseLease: true);
                throw;
            }
            using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(
                startToken);
            dispatch.CancelAfter(VoiceGatewayDispatchTimeout);
            try
            {
                if (string.Equals(
                        _router.VoiceLifecycleStateName,
                        "Off",
                        StringComparison.Ordinal))
                {
                    var toggle = await _router.RouteAsync(
                        new LilithRuntimeRequest(
                            "voice.toggle",
                            JsonSerializer.SerializeToElement(new
                            {
                                session_id = sessionId,
                                action = "on"
                        })),
                        dispatch.Token,
                        modelAuthority).ConfigureAwait(false);
                    if (!toggle.Succeeded)
                    {
                        modelAuthority.Dispose();
                        modelAuthority = null;
                        var cleaned = await CleanupVoiceOwnerOrRecoverAsync(owner)
                            .ConfigureAwait(false);
                        return new LilithGameVoiceStartOutcome(
                            false,
                            cleaned
                                ? FailureCode(toggle, "runtime.voice_enable_failed")
                                : "runtime.voice_generation_restarted");
                    }
                }
                else if (!string.Equals(
                    _router.VoiceLifecycleStateName,
                    "Ready",
                    StringComparison.Ordinal))
                {
                    modelAuthority.Dispose();
                    modelAuthority = null;
                    var cleaned = await CleanupVoiceOwnerOrRecoverAsync(owner)
                        .ConfigureAwait(false);
                    return new LilithGameVoiceStartOutcome(
                        false,
                        cleaned
                            ? "runtime.voice_busy"
                            : "runtime.voice_generation_restarted");
                }

                var response = await _router.RouteAsync(
                    new LilithRuntimeRequest(
                        "voice.record",
                        JsonSerializer.SerializeToElement(new
                        {
                            session_id = sessionId,
                            action = "start",
                            capture_id = captureId.ToString("N"),
                            language = VoiceLanguageTokenForHermes(language),
                            maximum_duration_ms = maximumDurationMilliseconds
                    })),
                    dispatch.Token,
                    modelAuthority).ConfigureAwait(false);
                modelAuthority.Dispose();
                modelAuthority = null;
                if (!response.Succeeded)
                {
                    var cleaned = await CleanupVoiceOwnerOrRecoverAsync(owner)
                        .ConfigureAwait(false);
                    return new LilithGameVoiceStartOutcome(
                        false,
                        cleaned
                            ? FailureCode(response, "runtime.voice_start_failed")
                            : "runtime.voice_generation_restarted");
                }
                return new LilithGameVoiceStartOutcome(true, "ok");
            }
            catch
            {
                modelAuthority?.Dispose();
                modelAuthority = null;
                await CleanupVoiceOwnerOrRecoverAsync(owner).ConfigureAwait(false);
                throw;
            }
            }
            finally
            {
                modelAuthority?.Dispose();
                _voiceGate.Release();
            }
        }
        finally
        {
            if (turnGateEntered)
                _turnGate.Release();
            RetireForegroundTurnDemand(playerTurn: true);
        }
    }

    public async Task<LilithGameVoiceStopOutcome> StopVoiceAsync(
        Guid captureId,
        LilithGameBridgeVoiceStopDisposition disposition,
        LilithGameVoicePeerIdentity peer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        peer.Validate();

        ActiveGameVoiceCapture capture;
        Task<LilithRuntimeResponse>? transcribeRequest = null;
        var terminalAlreadyProjected = false;
        if (disposition == LilithGameBridgeVoiceStopDisposition.Cancel)
        {
            // Publish cancellation intent before waiting for the per-capture
            // gate.  A Start can be blocked in hidden-session activation long
            // after its client disconnects; this exact generation/peer/
            // session-scope/capture tombstone is what prevents that late
            // Start from opening a microphone after Plugin has reconciled.
            var cancelAdmission = _voiceAdmissionTombstones.RecordCancel(
                captureId,
                peer.ProcessId,
                peer.ProcessCreationTime);
            if (cancelAdmission
                == LilithGameVoiceCancelAdmissionDisposition.CancelledBeforeAdmission)
            {
                return new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Interrupted,
                    string.Empty,
                    "runtime.voice_cancelled");
            }
            if (cancelAdmission
                == LilithGameVoiceCancelAdmissionDisposition.RecoveryRequired)
            {
                var recoveryOwner = new LilithRuntimeVoiceCaptureOwner(
                    _snapshot.GenerationId,
                    LilithRuntimeSessionSurface.GameBridge,
                    _router.ActiveSessionId ?? Path.GetFullPath(_handlePath),
                    captureId,
                    peer.ProcessId,
                    peer.ProcessCreationTime).Validate();
                await RequireVoiceGenerationRecoveryAsync(recoveryOwner)
                    .ConfigureAwait(false);
                return new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    "runtime.voice_generation_restarted");
            }
        }
        await _voiceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (VoiceRecoveryRequired)
            {
                await VoiceRecoveryCompletion.ConfigureAwait(false);
                return new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    "runtime.voice_generation_restarted");
            }
            ActiveGameVoiceCapture? candidate;
            lock (_eventLock)
                candidate = _voiceCapture;
            if (candidate is null
                || candidate.Owner.CaptureId != captureId
                || candidate.Owner.PeerProcessId != peer.ProcessId
                || candidate.Owner.PeerProcessCreationTime
                    != peer.ProcessCreationTime
                || !_runtime.VoiceCaptureArbiter.IsOwnedBy(candidate.Owner))
            {
                if (disposition == LilithGameBridgeVoiceStopDisposition.Cancel)
                {
                    return new LilithGameVoiceStopOutcome(
                        false,
                        LilithGameBridgeTerminal.Interrupted,
                        string.Empty,
                        "runtime.voice_cancelled");
                }
                return new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    "runtime.voice_capture_mismatch");
            }
            capture = candidate;

            if (disposition == LilithGameBridgeVoiceStopDisposition.Cancel)
            {
                // Claim the capture terminal before cleanup so a concurrent
                // transcript waiter cannot publish Completed after Cancel.
                // Cleanup owns its own 10-second dispatch bound and, on any
                // uncertainty, waits for exact old-Job retirement before this
                // response lets Plugin clear local busy.
                if (!capture.TryClaimTerminal())
                {
                    return new LilithGameVoiceStopOutcome(
                        false,
                        LilithGameBridgeTerminal.Failed,
                        string.Empty,
                        "runtime.voice_terminal_already_claimed");
                }
                var cancelled = await CleanupVoiceOwnerOrRecoverAsync(
                        capture.Owner)
                    .ConfigureAwait(false);
                return new LilithGameVoiceStopOutcome(
                    false,
                    cancelled
                        ? LilithGameBridgeTerminal.Interrupted
                        : LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    cancelled
                        ? "runtime.voice_cancelled"
                        : "runtime.voice_generation_restarted");
            }

            if (capture.TranscribeRequested)
            {
                return new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    "runtime.voice_busy");
            }
            capture.TranscribeRequested = true;
            terminalAlreadyProjected = capture.Terminal.Task.IsCompletedSuccessfully;
            if (!terminalAlreadyProjected)
            {
                transcribeRequest = RouteVoiceWithDeadlineAsync(
                    new LilithRuntimeRequest(
                        "voice.record",
                        JsonSerializer.SerializeToElement(new
                        {
                            session_id = capture.Owner.SessionId,
                            action = "stop",
                            capture_id = captureId.ToString("N"),
                            disposition = "transcribe"
                        })),
                    cancellationToken);
            }
        }
        finally
        {
            _voiceGate.Release();
        }

        VoiceCaptureTerminal terminal;
        try
        {
            if (terminalAlreadyProjected)
            {
                // VAD or the maximum-duration bound may have already emitted
                // terminal+idle and released the Hermes capture before F6 is
                // released. Do not send a second, mismatching upstream stop.
                terminal = await AwaitVoiceTerminalThenIdleAsync(
                        capture.Terminal.Task,
                        capture.Idle.Task,
                        VoiceTerminalTimeout,
                        VoiceIdleTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var response = await transcribeRequest!.ConfigureAwait(false);
                if (!response.Succeeded)
                {
                    // The terminal frames can already be queued on the
                    // gateway event pump while Hermes rejects this redundant
                    // stop as capture-mismatch. Give that exact capture a
                    // short projection window before discarding its transcript.
                    var projected = await TryAwaitProjectedVoiceCompletionAsync(
                            capture.Terminal.Task,
                            capture.Idle.Task,
                            VoiceProjectionGrace,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!projected.Succeeded)
                    {
                        await _voiceGate.WaitAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        try
                        {
                            var cleaned = await CleanupVoiceOwnerOrRecoverAsync(
                                    capture.Owner)
                                .ConfigureAwait(false);
                            if (!cleaned)
                            {
                                return new LilithGameVoiceStopOutcome(
                                    false,
                                    LilithGameBridgeTerminal.Failed,
                                    string.Empty,
                                    "runtime.voice_generation_restarted");
                            }
                        }
                        finally
                        {
                            _voiceGate.Release();
                        }
                        return new LilithGameVoiceStopOutcome(
                            false,
                            LilithGameBridgeTerminal.Failed,
                            string.Empty,
                            FailureCode(response, "runtime.voice_stop_failed"));
                    }
                    terminal = projected.Value!;
                }
                else
                {
                    terminal = await AwaitVoiceTerminalThenIdleAsync(
                            capture.Terminal.Task,
                            capture.Idle.Task,
                            VoiceTerminalTimeout,
                            VoiceIdleTimeout,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await _voiceGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await CleanupVoiceOwnerOrRecoverAsync(capture.Owner)
                    .ConfigureAwait(false);
            }
            finally
            {
                _voiceGate.Release();
            }
            throw;
        }

        // Terminal+idle already prove the capture result. Client disconnect or
        // timeout after that point must not abandon the ownership transition
        // and strand this generation permanently busy.
        LilithGameVoiceStopOutcome finalized;
        var recoveryRequired = false;
        await _voiceGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                    _snapshot.GenerationId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                RequireUsableModelAuthority(modelAuthority.Snapshot);
                finalized = await FinalizeVoiceTerminalAsync(
                        capture,
                        terminal,
                        modelAuthority)
                    .ConfigureAwait(false);
                recoveryRequired = string.Equals(
                    finalized.Code,
                    VoiceCleanupRecoveryRequired,
                    StringComparison.Ordinal);
            }
            catch (LilithGameBridgeOperationException exception)
            {
                recoveryRequired = true;
                finalized = new LilithGameVoiceStopOutcome(
                    false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    exception.Code);
            }
        }
        finally
        {
            _voiceGate.Release();
        }
        if (!recoveryRequired)
            return finalized;

        await RequireVoiceGenerationRecoveryAsync(capture.Owner)
            .ConfigureAwait(false);
        return new LilithGameVoiceStopOutcome(
            false,
            LilithGameBridgeTerminal.Failed,
            string.Empty,
            "runtime.voice_generation_restarted");
    }

    public async ValueTask DisposeAsync()
    {
        lock (_eventLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _noteInboxTurnOwner = null;
            _noteInboxOwnerReady?.TrySetResult(false);
            _noteInboxOwnerReady = null;
            _noteInboxDispatchSession = null;
        }
        lock (_notificationLock)
            _notificationSignal.TrySetCanceled();
        await _noteDraftShutdown.CancelAsync().ConfigureAwait(false);
        CancellationTokenSource? activeNotificationProjection;
        CancellationTokenSource? activeNativeContinuation;
        lock (_notificationProjectionAdmissionLock)
        {
            _notificationProjectionAdmissionClosed = true;
            activeNotificationProjection = _activeNotificationProjection;
            activeNativeContinuation =
                _nativeAssistantContinuation?.Preemption;
        }
        try
        {
            if (activeNotificationProjection != null)
            {
                await activeNotificationProjection.CancelAsync()
                    .ConfigureAwait(false);
            }
            if (activeNativeContinuation != null)
            {
                await activeNativeContinuation.CancelAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            // Completion already retired the captured projection lease.
        }
        // Server shutdown may overtake a long prompt connection. Cancel every
        // exact admitted turn first, but keep gateway events and terminal
        // waiters alive until the owner has completed bounded interrupt/
        // terminal reconciliation. Never dispose synchronization primitives
        // underneath an in-flight Submit finally.
        _turnAdmissions.SealAndCancelAllAdmissions();
        var turnsDrained = await _turnAdmissions.WaitForEmptyAsync(
                TurnShutdownDrainTimeout,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!turnsDrained)
        {
            // The caller is the generation-owning serve loop. Throwing here
            // is the explicit fail-closed escalation that makes its existing
            // finally call StopAsync and terminate the old Hermes Job tree.
            throw new LilithRuntimeException(
                "Game turn reconciliation did not drain; the Runtime generation must be retired.");
        }
        var notificationProjectionDrained = await _turnGate.WaitAsync(
                TurnShutdownDrainTimeout,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!notificationProjectionDrained)
        {
            throw new LilithRuntimeException(
                "Game reminder projection did not drain; the Runtime generation must be retired.");
        }
        lock (_notificationProjectionAdmissionLock)
        {
            if (_activeNotificationProjection != null)
            {
                throw new LilithRuntimeException(
                    "Game reminder projection retained an active lease after its model rail drained.");
            }
            if (_nativeAssistantContinuation != null)
            {
                throw new LilithRuntimeException(
                    "Native assistant continuation retained an active lease after its model rail drained.");
            }
        }
        // Keep the closed model rail acquired until the semaphore is disposed.
        // A notification read that passed its public disposed check just before
        // shutdown can no longer acquire and later release the semaphore after
        // its lifetime has ended.
        await _voiceGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_voiceOwnerLease is { } lease)
                await CleanupVoiceOwnerOrRecoverAsync(lease.Owner)
                    .ConfigureAwait(false);
        }
        finally
        {
            _voiceGate.Release();
        }
        await DrainVoicePeerExitTasksAsync().ConfigureAwait(false);
        _runtime.GatewayEvent -= RuntimeGatewayEvent;
        lock (_eventLock)
        {
            foreach (var waiter in _terminalWaiters.Values)
                waiter.TrySetCanceled();
            _terminalWaiters.Clear();
            _terminalCache.Clear();
            _terminalCacheOrder.Clear();
            _sessionInfoSignal.TrySetCanceled();
        }
        lock (_notificationLock)
            _notifications.Clear();
        _turnAdmissions.Dispose();
        _turnGate.Dispose();
        _historyGate.Dispose();
        _sessionGate.Dispose();
        _voiceGate.Dispose();
        _voiceRecoveryRequested.Dispose();
    }

    private void ReplaceVoiceOwnerLease(
        LilithRuntimeVoiceCaptureOwner owner,
        CancellationToken peerExited)
    {
        _voiceOwnerLease?.Dispose();
        var lease = new GameVoiceOwnerLease(owner);
        lease.PeerExitRegistration = peerExited.Register(
            () => QueueVoicePeerExit(owner));
        _voiceOwnerLease = lease;
    }

    internal static async Task<T> AwaitVoiceTerminalThenIdleAsync<T>(
        Task<T> terminal,
        Task idle,
        TimeSpan terminalTimeout,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(idle);
        if (terminalTimeout <= TimeSpan.Zero || idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(terminalTimeout));
        var value = await terminal.WaitAsync(terminalTimeout, cancellationToken)
            .ConfigureAwait(false);
        await idle.WaitAsync(idleTimeout, cancellationToken).ConfigureAwait(false);
        return value;
    }

    internal static async Task<(bool Succeeded, T? Value)>
        TryAwaitProjectedVoiceCompletionAsync<T>(
            Task<T> terminal,
            Task idle,
            TimeSpan projectionGrace,
            CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(idle);
        if (projectionGrace <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(projectionGrace));
        try
        {
            var value = await terminal.WaitAsync(
                    projectionGrace,
                    cancellationToken)
                .ConfigureAwait(false);
            await idle.WaitAsync(projectionGrace, cancellationToken)
                .ConfigureAwait(false);
            return (true, value);
        }
        catch (TimeoutException)
        {
            return (false, null);
        }
    }

    private async Task<LilithGameVoiceStopOutcome> FinalizeVoiceTerminalAsync(
        ActiveGameVoiceCapture capture,
        VoiceCaptureTerminal terminal,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        var current = false;
        lock (_eventLock)
            current = ReferenceEquals(_voiceCapture, capture);
        var terminalAdmission = current
            && _runtime.VoiceCaptureArbiter.IsOwnedBy(capture.Owner)
            ? _voiceAdmissionTombstones.ClaimTerminal(
                capture.Owner.CaptureId,
                capture.Owner.PeerProcessId,
                capture.Owner.PeerProcessCreationTime,
                capture.TryClaimTerminal)
            : LilithGameVoiceTerminalAdmissionDisposition.AlreadyClaimed;
        if (!current
            || !_runtime.VoiceCaptureArbiter.IsOwnedBy(capture.Owner)
            || terminalAdmission
                == LilithGameVoiceTerminalAdmissionDisposition.AlreadyClaimed)
        {
            return new LilithGameVoiceStopOutcome(
                false,
                LilithGameBridgeTerminal.Interrupted,
                string.Empty,
                "runtime.voice_cancelled");
        }
        if (terminalAdmission
                == LilithGameVoiceTerminalAdmissionDisposition.Cancelled
            || terminal.Cancelled)
        {
            var cleaned = await CleanupVoiceOwnerBoundedAsync(
                    capture.Owner,
                    modelAuthority)
                .ConfigureAwait(false);
            return new LilithGameVoiceStopOutcome(
                false,
                LilithGameBridgeTerminal.Interrupted,
                string.Empty,
                cleaned
                    ? "runtime.voice_cancelled"
                    : VoiceCleanupRecoveryRequired);
        }
        if (terminal.NoSpeech)
        {
            var cleaned = await CleanupVoiceOwnerBoundedAsync(
                    capture.Owner,
                    modelAuthority)
                .ConfigureAwait(false);
            return new LilithGameVoiceStopOutcome(
                false,
                LilithGameBridgeTerminal.Failed,
                string.Empty,
                cleaned
                    ? "hermes.voice_no_speech"
                    : VoiceCleanupRecoveryRequired);
        }
        if (terminal.Rejected || string.IsNullOrWhiteSpace(terminal.Transcript))
        {
            var cleaned = await CleanupVoiceOwnerBoundedAsync(
                    capture.Owner,
                    modelAuthority)
                .ConfigureAwait(false);
            return new LilithGameVoiceStopOutcome(
                false,
                LilithGameBridgeTerminal.Failed,
                string.Empty,
                cleaned
                    ? "runtime.voice_transcript_rejected"
                    : VoiceCleanupRecoveryRequired);
        }

        // F6 is a bounded push-to-talk capture. Hermes may keep its local STT
        // model warm, but the native InputStream must be physically shut down
        // before a successful transcript releases Plugin busy. The canonical
        // toggle-off path requires exact recorder shutdown proof; failure
        // retires the entire old generation and never returns transcript text.
        var transcriptCleaned = await CleanupVoiceOwnerBoundedAsync(
                capture.Owner,
                modelAuthority)
            .ConfigureAwait(false);
        if (!transcriptCleaned)
        {
            return new LilithGameVoiceStopOutcome(
                false,
                LilithGameBridgeTerminal.Failed,
                string.Empty,
                VoiceCleanupRecoveryRequired);
        }
        return new LilithGameVoiceStopOutcome(
            true,
            LilithGameBridgeTerminal.Completed,
            terminal.Transcript,
            "ok");
    }

    private void QueueVoicePeerExit(LilithRuntimeVoiceCaptureOwner owner)
    {
        Task task;
        lock (_eventLock)
        {
            if (_disposed)
                return;
            task = Task.Run(async () =>
            {
                try
                {
                    await _voiceGate.WaitAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    try
                    {
                        if (_voiceOwnerLease is { } lease
                            && lease.Owner == owner)
                        {
                            await CleanupVoiceOwnerOrRecoverAsync(owner)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _voiceGate.Release();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch
                {
                    // RequireVoiceGenerationRecoveryAsync owns the authoritative
                    // failure task observed by the pipe generation. This
                    // background peer callback is tracked and drained so its
                    // secondary exception cannot become unobserved.
                }
            });
            _voicePeerExitTasks.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_eventLock)
                    _voicePeerExitTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task DrainVoicePeerExitTasksAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_eventLock)
                pending = _voicePeerExitTasks.ToArray();
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending).WaitAsync(
                    MaximumVoiceRecoveryEnvelope + TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> CleanupVoiceOwnerBoundedAsync(
        LilithRuntimeVoiceCaptureOwner owner,
        LilithRuntimeModelAuthorityLease? modelAuthority = null)
    {
        using var cleanup = new CancellationTokenSource(VoiceCleanupTimeout);
        try
        {
            return await CleanupVoiceOwnerAsync(
                    owner,
                    cleanup.Token,
                    modelAuthority)
                .ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async Task<LilithRuntimeResponse> RouteVoiceWithDeadlineAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        dispatch.CancelAfter(VoiceGatewayDispatchTimeout);
        return await _router.RouteAsync(request, dispatch.Token)
            .ConfigureAwait(false);
    }

    private async Task<bool> CleanupVoiceOwnerOrRecoverAsync(
        LilithRuntimeVoiceCaptureOwner owner)
    {
        if (await CleanupVoiceOwnerBoundedAsync(owner).ConfigureAwait(false))
            return true;

        await RequireVoiceGenerationRecoveryAsync(owner).ConfigureAwait(false);
        return false;
    }

    private Task RequireVoiceGenerationRecoveryAsync(
        LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        TaskCompletionSource? completion = null;
        Task recovery;
        lock (_voiceRecoveryLock)
        {
            if (_voiceRecoveryTask is not null)
                return _voiceRecoveryTask;

            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            recovery = completion.Task;
            _voiceRecoveryTask = recovery;
        }

        // Cancel only new game-bridge admission. Existing connections retain
        // their independent lifetime long enough for this exact retirement to
        // complete and for a bounded terminal response to be flushed.
        _voiceRecoveryRequested.Cancel();
        _ = CompleteVoiceGenerationRecoveryAsync(owner, completion);
        return recovery;
    }

    private async Task CompleteVoiceGenerationRecoveryAsync(
        LilithRuntimeVoiceCaptureOwner owner,
        TaskCompletionSource completion)
    {
        try
        {
            using var recovery = new CancellationTokenSource(
                VoiceGenerationRecoveryTimeout);
            var receipt = await _runtime.StopVoiceRecoveryGenerationAsync(
                    recovery.Token)
                .ConfigureAwait(false);
            if (receipt.GenerationId != _snapshot.GenerationId
                || !receipt.ProcessExited
                || !receipt.ReadySentinelRemoved)
            {
                throw new LilithRuntimeException(
                    "Voice recovery did not retire the exact Hermes generation.");
            }

            // StopCore resets the process-wide arbiter only after the old Job
            // is physically contained. Clear the Host projection and peer
            // registration after that proof, never before it.
            ClearVoiceOwnerState(owner, releaseLease: true);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(new LilithRuntimeException(
                $"Voice generation recovery failed ({exception.GetType().Name})."));
        }
    }

    private async Task<bool> CleanupVoiceOwnerAsync(
        LilithRuntimeVoiceCaptureOwner owner,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease? modelAuthority = null)
    {
        if (!_runtime.VoiceCaptureArbiter.IsOwnedBy(owner))
        {
            ClearVoiceOwnerState(owner, releaseLease: true);
            return true;
        }
        if (_runtime.State != LilithRuntimeState.Ready
            || _snapshot.GenerationId == Guid.Empty)
        {
            // Stopping/Faulted is not physical microphone-retirement proof.
            // StopCore keeps the generation-retiring gate closed and clears
            // the owner only after ACTIVE_PROCESS_ZERO. Reopening the arbiter
            // here would permit another surface to race the dying gateway.
            return false;
        }

        var state = _router.VoiceLifecycleStateName;
        if (state is "Starting" or "Recording" or "Stopping" or "Transcribing")
        {
            if (await _router.CancelGameVoiceCaptureAsync(
                    owner,
                    cancellationToken,
                    modelAuthority)
                .ConfigureAwait(false))
            {
                ClearVoiceOwnerState(owner, releaseLease: true);
                return true;
            }
            state = _router.VoiceLifecycleStateName;
        }

        if (!string.Equals(state, "Off", StringComparison.Ordinal))
        {
            var off = await _router.RouteAsync(
                new LilithRuntimeRequest(
                    "voice.toggle",
                    JsonSerializer.SerializeToElement(new
                    {
                        session_id = owner.SessionId,
                        action = "off"
                    })),
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
            if (!off.Succeeded)
                return false;
            if (string.Equals(
                    _router.VoiceLifecycleStateName,
                    "Draining",
                    StringComparison.Ordinal)
                && TryGetActiveVoiceCapture(owner, out var active))
            {
                await active.Idle.Task.WaitAsync(
                        VoiceIdleTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (!string.Equals(
                _router.VoiceLifecycleStateName,
                "Off",
                StringComparison.Ordinal))
        {
            return false;
        }
        _ = _runtime.VoiceCaptureArbiter.Release(owner);
        ClearVoiceOwnerState(owner, releaseLease: true);
        return true;
    }

    private bool TryGetActiveVoiceCapture(
        LilithRuntimeVoiceCaptureOwner owner,
        out ActiveGameVoiceCapture capture)
    {
        lock (_eventLock)
        {
            if (_voiceCapture is { } active && active.Owner == owner)
            {
                capture = active;
                return true;
            }
        }
        capture = null!;
        return false;
    }

    private void ClearVoiceOwnerState(
        LilithRuntimeVoiceCaptureOwner owner,
        bool releaseLease)
    {
        lock (_eventLock)
        {
            if (_voiceCapture is { } active && active.Owner == owner)
            {
                active.Terminal.TrySetResult(
                    new VoiceCaptureTerminal(
                        string.Empty,
                        NoSpeech: false,
                        Rejected: false,
                        Cancelled: true));
                active.Idle.TrySetResult();
                _voiceCapture = null;
            }
        }
        _voiceAdmissionTombstones.ReleaseStart(
            owner.CaptureId,
            owner.PeerProcessId,
            owner.PeerProcessCreationTime);
        if (releaseLease
            && _voiceOwnerLease is { } lease
            && lease.Owner == owner)
        {
            _voiceOwnerLease = null;
            lease.Dispose();
        }
    }

    private async Task EnsureSessionAsync(
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        RequireUsableModelAuthority(modelAuthority.Snapshot);
        if (Volatile.Read(ref _nativeAssistantCommitQuarantined) != 0)
        {
            throw new LilithGameBridgeOperationException(
                NativeAssistantCommitQuarantined);
        }
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _nativeAssistantCommitQuarantined) != 0)
            {
                throw new LilithGameBridgeOperationException(
                    NativeAssistantCommitQuarantined);
            }
            var nativeRevisionRefreshRequired =
                _admittedNativeConfigRevision is { } admittedNativeRevision
                && admittedNativeRevision
                    != modelAuthority.Snapshot.NativeConfigRevision;
            var activeSessionId = _router.ActiveSessionId;
            string? activationRetrySessionId = null;
            lock (_eventLock)
            {
                if (activeSessionId is not null
                    && string.Equals(
                        _sessionActivationRetryRequiredSessionId,
                        activeSessionId,
                        StringComparison.Ordinal))
                {
                    activationRetrySessionId = activeSessionId;
                }
            }
            if (nativeRevisionRefreshRequired
                || activationRetrySessionId is not null)
            {
                _sessionApprovalEnabled = false;
                _historySnapshot = null;
                if (activeSessionId is null)
                {
                    throw new LilithGameBridgeOperationException(
                        "runtime.game_session_lineage_rejected");
                }

                var previouslyTrustedLineage =
                    activationRetrySessionId is not null
                        ? RequireExactActiveDurableLineage(activeSessionId)
                        : _admittedLineage;
                if (previouslyTrustedLineage is null
                    || !IsExactActiveDurableLineage(
                        activeSessionId,
                        previouslyTrustedLineage))
                {
                    throw new LilithGameBridgeOperationException(
                        "runtime.game_session_lineage_rejected");
                }

                await ResumeTrustedDurableSessionAsync(
                        previouslyTrustedLineage,
                        activationRetrySessionId,
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (_router.ActiveSessionId is { } active
                && _router.IsSessionAttested(active)
                && _sessionApprovalEnabled
                && _admittedLineage is { } admitted
                && IsExactActiveLineage(active, admitted))
            {
                await SynchronizeSessionModelAsync(
                        active,
                        admitted,
                        modelAuthority.Snapshot.ProviderId,
                        modelAuthority.Snapshot.ModelId,
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
                PersistHandleIfAvailable();
                return;
            }

            if (_router.ActiveSessionId is { } existingSessionId)
            {
                await WaitForSessionAttestationAsync(
                        existingSessionId,
                        cancellationToken)
                    .ConfigureAwait(false);
                var existingLineage = RequireExactActiveLineage(
                    existingSessionId);
                await SynchronizeSessionModelAsync(
                        existingSessionId,
                        existingLineage,
                        modelAuthority.Snapshot.ProviderId,
                        modelAuthority.Snapshot.ModelId,
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
                PersistHandleIfAvailable();
                await EnableSessionApprovalAsync(
                        existingSessionId,
                        existingLineage,
                        modelAuthority,
                        cancellationToken)
                    .ConfigureAwait(false);
                PersistHandleIfAvailable();
                return;
            }
            _sessionApprovalEnabled = false;
            _admittedLineage = null;

            if (File.Exists(_handlePath))
            {
                if (!TryReadHandle(out var storedSessionId))
                {
                    QuarantineHandle();
                }
                else
                {
                    var resumed = await ActivateAsync(
                        "session.resume",
                        JsonSerializer.SerializeToElement(new
                        {
                            session_id = storedSessionId,
                            omit_messages = true
                        }),
                        modelAuthority,
                        cancellationToken).ConfigureAwait(false);
                    if (!resumed.Succeeded)
                    {
                        if (!IsDefiniteUntrustedHandleRejection(resumed))
                        {
                            throw new LilithGameBridgeOperationException(
                                SessionActivationFailureCode(resumed));
                        }
                        QuarantineHandle();
                    }
                }
            }

            if (_router.ActiveSessionId is null)
            {
                var created = await ActivateAsync(
                    "session.create",
                    JsonSerializer.SerializeToElement(new
                    {
                        title = "Lilith game conversation"
                    }),
                    modelAuthority,
                    cancellationToken).ConfigureAwait(false);
                if (!created.Succeeded)
                {
                    throw new LilithGameBridgeOperationException(
                        SessionActivationFailureCode(created));
                }
            }

            var sessionId = _router.ActiveSessionId
                ?? throw new InvalidOperationException(
                    "Hermes did not return a hidden game session identity.");
            PersistHandleIfAvailable();
            await WaitForSessionAttestationAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            var lineage = RequireExactActiveLineage(sessionId);
            await SynchronizeSessionModelAsync(
                    sessionId,
                    lineage,
                    modelAuthority.Snapshot.ProviderId,
                    modelAuthority.Snapshot.ModelId,
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistHandleIfAvailable();
            await EnableSessionApprovalAsync(
                    sessionId,
                    lineage,
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistHandleIfAvailable();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task ResumeTrustedDurableSessionAsync(
        LilithGameSessionLineageAttestation previouslyTrustedLineage,
        string? activationRetrySessionId,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        try
        {
            await ResumeTrustedDurableSessionCoreAsync(
                    previouslyTrustedLineage,
                    activationRetrySessionId,
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            PreserveDurableResumeRetryIfSafe(previouslyTrustedLineage);
            throw;
        }
    }

    private async Task ResumeTrustedDurableSessionCoreAsync(
        LilithGameSessionLineageAttestation previouslyTrustedLineage,
        string? activationRetrySessionId,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        // A native revision or a failed deferred Agent build invalidates only
        // the live Runtime binding. Resume the exact verified compression tip;
        // neither recovery path may fork the game's durable memory.
        var resumed = await ActivateAsync(
            "session.resume",
            JsonSerializer.SerializeToElement(new
            {
                session_id = previouslyTrustedLineage.ResolvedStoredSessionId,
                omit_messages = true
            }),
            modelAuthority,
            cancellationToken,
            activationRetrySessionId).ConfigureAwait(false);
        if (!resumed.Succeeded)
        {
            throw new LilithGameBridgeOperationException(
                SessionActivationFailureCode(resumed));
        }

        var refreshedSessionId = _router.ActiveSessionId
            ?? throw new InvalidOperationException(
                "Hermes did not return a refreshed hidden game session identity.");
        var refreshedLineage = RequireExactActiveDurableLineage(
            refreshedSessionId);
        if (!IsDurableResumeContinuation(
                previouslyTrustedLineage,
                refreshedLineage))
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_session_lineage_rejected");
        }

        // The activation receipt already proves durable continuity. Retain
        // that exact tip for a later retry even if fresh native-surface
        // attestation fails; approval remains disabled until every
        // postcondition below succeeds.
        _admittedLineage = refreshedLineage;
        PersistHandleIfAvailable();
        await WaitForSessionAttestationAsync(
                refreshedSessionId,
                cancellationToken)
            .ConfigureAwait(false);
        var attestedLineage = RequireExactActiveLineage(refreshedSessionId);
        if (!string.Equals(
                refreshedLineage.FingerprintSha256,
                attestedLineage.FingerprintSha256,
                StringComparison.Ordinal))
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_session_lineage_rejected");
        }
        await SynchronizeSessionModelAsync(
                refreshedSessionId,
                attestedLineage,
                modelAuthority.Snapshot.ProviderId,
                modelAuthority.Snapshot.ModelId,
                modelAuthority,
                cancellationToken)
            .ConfigureAwait(false);
        PersistHandleIfAvailable();
        await EnableSessionApprovalAsync(
                refreshedSessionId,
                attestedLineage,
                modelAuthority,
                cancellationToken)
            .ConfigureAwait(false);
        PersistHandleIfAvailable();
    }

    private void PreserveDurableResumeRetryIfSafe(
        LilithGameSessionLineageAttestation previouslyTrustedLineage)
    {
        var activeSessionId = _router.ActiveSessionId;
        var activeLineage = _router.ActiveGameLineageAttestation;
        if (activeSessionId is null
            || activeLineage is null
            || !IsExactActiveDurableLineage(activeSessionId, activeLineage)
            || (!string.Equals(
                    activeLineage.FingerprintSha256,
                    previouslyTrustedLineage.FingerprintSha256,
                    StringComparison.Ordinal)
                && !IsDurableResumeContinuation(
                    previouslyTrustedLineage,
                    activeLineage)))
        {
            return;
        }
        SignalSessionActivationFailure(activeSessionId);
    }

    private async Task<LilithRuntimeResponse> ActivateAsync(
        string method,
        JsonElement payload,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken,
        string? activationRetrySessionId = null)
    {
        if (activationRetrySessionId is not null)
        {
            // Clear only the exact old failure immediately before dispatch.
            // A same-live-session fast resume may emit a new terminal while
            // the RPC is in flight; that later failure must remain latched.
            lock (_eventLock)
            {
                if (string.Equals(
                        _sessionActivationRetryRequiredSessionId,
                        activationRetrySessionId,
                        StringComparison.Ordinal))
                {
                    _sessionActivationRetryRequiredSessionId = null;
                }
            }
        }
        var response = await _router.RouteAsync(
            new LilithRuntimeRequest(method, payload),
            cancellationToken,
            modelAuthority).ConfigureAwait(false);
        _router.CompleteSessionActivationProjection(
            method,
            response,
            ObserveSessionActivationProjectionEvents);
        return response;
    }

    private void ObserveSessionActivationProjectionEvents(
        IReadOnlyList<HermesGatewayEvent> gatewayEvents)
    {
        var sessionId = _router.ActiveSessionId;
        if (sessionId is null)
            return;
        foreach (var gatewayEvent in gatewayEvents)
        {
            if (IsSessionActivationTerminalError(gatewayEvent, sessionId))
            {
                SignalSessionActivationFailure(sessionId);
                return;
            }
        }
    }

    private async Task WaitForSessionAttestationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(SessionAttestationTimeout);
        while (true)
        {
            var attested = _router.IsSessionAttested(sessionId);
            Task signal;
            lock (_eventLock)
            {
                ThrowIfSessionActivationFailed(sessionId);
                if (attested)
                    return;
                if (_sessionInfoSignal.Task.IsCompleted)
                {
                    _sessionInfoSignal = NewSignal();
                    continue;
                }
                signal = _sessionInfoSignal.Task;
            }
            try
            {
                await signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Hermes did not attest the hidden game session in time.");
            }
        }
    }

    private void ThrowIfSessionActivationFailed(string sessionId)
    {
        if (string.Equals(
                _sessionActivationRetryRequiredSessionId,
                sessionId,
                StringComparison.Ordinal))
        {
            throw new LilithGameBridgeOperationException(
                GameAgentInitializationFailure);
        }
    }

    private void SignalSessionActivationFailure(string sessionId)
    {
        lock (_eventLock)
        {
            _sessionActivationRetryRequiredSessionId = sessionId;
            _sessionInfoSignal.TrySetResult(true);
        }
    }

    internal static bool IsSessionActivationTerminalError(
        HermesGatewayEvent gatewayEvent,
        string sessionId)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        if (string.IsNullOrWhiteSpace(sessionId)
            || !string.Equals(gatewayEvent.Type, "error", StringComparison.Ordinal)
            || !string.Equals(
                gatewayEvent.SessionId,
                sessionId,
                StringComparison.Ordinal)
            || gatewayEvent.Payload.ValueKind != JsonValueKind.Object
            || !gatewayEvent.Payload.TryGetProperty(
                "runtime_ready",
                out var runtimeReady)
            || runtimeReady.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        try
        {
            return HermesTurnCorrelationContract.ValidateGatewayEvent(
                gatewayEvent.Type,
                gatewayEvent.Parameters).Length == 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private async Task SynchronizeSessionModelAsync(
        string sessionId,
        LilithGameSessionLineageAttestation lineage,
        string providerId,
        string modelId,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        await SynchronizeSessionModelCoreAsync(
                sessionId,
                lineage,
                providerId,
                modelId,
                modelAuthority,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SynchronizeSessionModelCoreAsync(
        string sessionId,
        LilithGameSessionLineageAttestation lineage,
        string providerId,
        string modelId,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        var observed = await WaitForSettledSessionModelAsync(
                sessionId,
                expectedProviderId: null,
                expectedModelId: null,
                minimumRevisionExclusive: null,
                cancellationToken)
            .ConfigureAwait(false);
        var receiptRequired = string.Equals(
            _modelSyncReceiptRequiredSessionId,
            sessionId,
            StringComparison.Ordinal);
        if (!receiptRequired
            && string.Equals(
                observed.ProviderId,
                providerId,
                StringComparison.Ordinal)
            && string.Equals(
                observed.ModelId,
                modelId,
                StringComparison.Ordinal))
        {
            if (!IsExactActiveLineage(sessionId, lineage))
            {
                throw new LilithGameBridgeOperationException(
                    "runtime.game_session_lineage_rejected");
            }
            return;
        }

        _modelSyncReceiptRequiredSessionId = sessionId;
        var response = await _router.RouteAsync(
            new LilithRuntimeRequest(
                "config.set",
                JsonSerializer.SerializeToElement(new
                {
                    session_id = sessionId,
                    provider = providerId,
                    model = modelId,
                    confirm_expensive_model = true
                })),
            cancellationToken,
            modelAuthority).ConfigureAwait(false);
        if (!response.Succeeded)
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_model_sync_rejected");
        }
        if (!IsExactNativeSessionModelReceipt(
                response.HermesPayload,
                modelId))
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_model_sync_receipt_rejected");
        }

        _ = await WaitForSettledSessionModelAsync(
                sessionId,
                providerId,
                modelId,
                observed.Revision,
                cancellationToken)
            .ConfigureAwait(false);
        if (!IsExactActiveLineage(sessionId, lineage))
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_session_lineage_rejected");
        }
        _modelSyncReceiptRequiredSessionId = null;
    }

    private async Task<LilithRuntimeSessionModelObservation>
        WaitForSettledSessionModelAsync(
            string sessionId,
            string? expectedProviderId,
            string? expectedModelId,
            long? minimumRevisionExclusive,
            CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(SessionAttestationTimeout);
        while (true)
        {
            if (_router.TryGetAttestedSessionModelObservation(
                    sessionId,
                    out var observed)
                && IsSettledSessionModelObservation(
                    observed,
                    expectedProviderId,
                    expectedModelId,
                    minimumRevisionExclusive))
            {
                lock (_eventLock)
                {
                    ThrowIfSessionActivationFailed(sessionId);
                    return observed;
                }
            }

            Task signal;
            lock (_eventLock)
            {
                ThrowIfSessionActivationFailed(sessionId);
                if (_sessionInfoSignal.Task.IsCompleted)
                {
                    _sessionInfoSignal = NewSignal();
                    continue;
                }
                signal = _sessionInfoSignal.Task;
            }
            try
            {
                await signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new LilithGameBridgeOperationException(
                    "runtime.game_model_sync_unverified");
            }
        }
    }

    internal static bool IsSettledSessionModelObservation(
        LilithRuntimeSessionModelObservation observed,
        string? expectedProviderId,
        string? expectedModelId,
        long? minimumRevisionExclusive)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return !observed.Running
            && (minimumRevisionExclusive is null
                || observed.Revision > minimumRevisionExclusive.Value)
            && (expectedProviderId is null
                || string.Equals(
                    observed.ProviderId,
                    expectedProviderId,
                    StringComparison.Ordinal))
            && (expectedModelId is null
                || string.Equals(
                    observed.ModelId,
                    expectedModelId,
                    StringComparison.Ordinal));
    }

    private void RequireUsableModelAuthority(
        LilithRuntimeModelAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.GenerationId != _snapshot.GenerationId
            || authority.State == LilithRuntimeModelAuthorityState.Retired)
        {
            throw new LilithGameBridgeOperationException(
                "runtime.voice_generation_restarting");
        }
        if (authority.State == LilithRuntimeModelAuthorityState.Quarantined)
        {
            throw new LilithGameBridgeOperationException(
                "runtime.native_config_quarantined");
        }
        if (!authority.HasApprovedDefault)
        {
            throw new LilithGameBridgeOperationException(
                "runtime.game_model_selection_required");
        }
    }

    internal static bool IsExactNativeSessionModelReceipt(
        JsonElement receipt,
        string expectedModelId)
    {
        if (receipt.ValueKind != JsonValueKind.Object)
            return false;
        var properties = receipt.EnumerateObject().ToArray();
        if (properties.Length != 8
            || !properties.Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[]
                {
                    "key", "value", "warning", "confirm_required",
                    "confirm_message", "scope", "runtime_revision",
                    "runtime_ready"
                }))
        {
            return false;
        }
        return receipt.TryGetProperty("key", out var key)
            && key.ValueKind == JsonValueKind.String
            && string.Equals(key.GetString(), "model", StringComparison.Ordinal)
            && receipt.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expectedModelId, StringComparison.Ordinal)
            && receipt.TryGetProperty("warning", out var warning)
            && warning.ValueKind == JsonValueKind.String
            && receipt.TryGetProperty("confirm_required", out var confirmation)
            && confirmation.ValueKind == JsonValueKind.False
            && receipt.TryGetProperty("confirm_message", out var message)
            && message.ValueKind == JsonValueKind.String
            && receipt.TryGetProperty("scope", out var scope)
            && scope.ValueKind == JsonValueKind.String
            && string.Equals(scope.GetString(), "session", StringComparison.Ordinal)
            && receipt.TryGetProperty("runtime_revision", out var runtimeRevision)
            && runtimeRevision.ValueKind == JsonValueKind.Number
            && runtimeRevision.TryGetInt64(out var revision)
            && revision > 0
            && receipt.TryGetProperty("runtime_ready", out var runtimeReady)
            && runtimeReady.ValueKind == JsonValueKind.True;
    }

    private async Task EnableSessionApprovalAsync(
        string sessionId,
        LilithGameSessionLineageAttestation lineage,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        if (_sessionApprovalEnabled
            && _admittedNativeConfigRevision
                == modelAuthority.Snapshot.NativeConfigRevision
            && _admittedLineage is { } admitted
            && string.Equals(
                admitted.FingerprintSha256,
                lineage.FingerprintSha256,
                StringComparison.Ordinal)
            && IsExactActiveLineage(sessionId, admitted))
        {
            return;
        }
        if (!IsExactActiveLineage(sessionId, lineage))
        {
            throw new InvalidDataException(
                "The hidden game session durable authority changed before approval bypass.");
        }
        var response = await _router.EnableGameSessionApprovalBypassAsync(
                sessionId,
                modelAuthority,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.Succeeded)
        {
            throw new LilithGameBridgeOperationException(
                GameRpcFailureCode(response, "approval"));
        }
        if (!ExactSessionYoloReceipt(response.HermesPayload))
            throw new InvalidDataException(
                "Hermes did not prove session-scoped approval bypass.");
        if (!IsExactActiveLineage(sessionId, lineage))
        {
            throw new InvalidDataException(
                "The hidden game session durable authority changed while approval bypass was committing.");
        }
        WriteAuthorityReceipt(sessionId, lineage);
        _admittedLineage = lineage;
        _admittedNativeConfigRevision =
            modelAuthority.Snapshot.NativeConfigRevision;
        _sessionApprovalEnabled = true;
    }

    private async Task CommitTerminalAuthorityAsync(
        string sessionId,
        LilithGameTerminalEvent terminal,
        CancellationToken cancellationToken)
    {
        using var modelAuthority = await _runtime.ModelAuthority.AcquireAsync(
                _snapshot.GenerationId,
                cancellationToken)
            .ConfigureAwait(false);
        RequireUsableModelAuthority(modelAuthority.Snapshot);
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_admittedNativeConfigRevision
                != modelAuthority.Snapshot.NativeConfigRevision)
            {
                _sessionApprovalEnabled = false;
                _admittedLineage = null;
                throw new LilithGameBridgeOperationException(
                    "runtime.native_config_session_stale");
            }
            var lineage = _router.RefreshGameSessionLineagePostcondition(
                sessionId,
                terminal.Payload);
            if (_admittedLineage is null
                || !string.Equals(
                    _admittedLineage.FingerprintSha256,
                    lineage.FingerprintSha256,
                    StringComparison.Ordinal))
            {
                _sessionApprovalEnabled = false;
            }
            await EnableSessionApprovalAsync(
                    sessionId,
                    lineage,
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistHandleIfAvailable();
        }
        catch
        {
            _sessionApprovalEnabled = false;
            _admittedLineage = null;
            _router.QuarantineGameSessionLineagePostcondition(sessionId);
            QuarantineHandle();
            throw;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task CommitNativeAssistantAuthorityAsync(
        string sessionId,
        JsonElement receipt,
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        if (!_runtime.ModelAuthority.OwnsActiveLease(
                modelAuthority,
                _snapshot.GenerationId))
        {
            throw new InvalidOperationException(
                "The native assistant commit lost Runtime model authority.");
        }
        RequireUsableModelAuthority(modelAuthority.Snapshot);
        var gateEntered = false;
        try
        {
            await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;
            if (_admittedNativeConfigRevision
                != modelAuthority.Snapshot.NativeConfigRevision)
            {
                _sessionApprovalEnabled = false;
                _admittedLineage = null;
                throw new LilithGameBridgeOperationException(
                    "runtime.native_config_session_stale");
            }
            var lineage = _router.RefreshGameSessionLineagePostcondition(
                sessionId,
                receipt);
            if (_admittedLineage is null
                || !string.Equals(
                    _admittedLineage.FingerprintSha256,
                    lineage.FingerprintSha256,
                    StringComparison.Ordinal))
            {
                _sessionApprovalEnabled = false;
            }
            await EnableSessionApprovalAsync(
                    sessionId,
                    lineage,
                    modelAuthority,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistHandleIfAvailable();
        }
        catch
        {
            QuarantineNativeAssistantLineage(sessionId);
            throw;
        }
        finally
        {
            if (gateEntered)
                _sessionGate.Release();
        }
    }

    private void QuarantineNativeAssistantLineage(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        // The append may already be durable. Preserve every known locator and
        // fail this generation closed so restart can reconcile the exact tip;
        // clearing it here would let EnsureSession create a second lineage.
        Interlocked.Exchange(ref _nativeAssistantCommitQuarantined, 1);
        _sessionApprovalEnabled = false;
    }

    private async Task<bool> InterruptSessionAsync(
        CancellationToken cancellationToken)
    {
        var sessionId = _router.ActiveSessionId;
        if (sessionId is null || !_router.IsSessionAttested(sessionId))
            return false;
        var response = await _router.RouteAsync(
            new LilithRuntimeRequest(
                "session.interrupt",
                JsonSerializer.SerializeToElement(new { session_id = sessionId })),
            cancellationToken).ConfigureAwait(false);
        return response.Succeeded;
    }

    private async Task<bool> InterruptSessionForTurnCancellationAsync()
    {
        using var timeout = new CancellationTokenSource(TurnInterruptTimeout);
        try
        {
            return await InterruptSessionAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static async Task<bool> TryAwaitExactTurnTerminalAsync(
        Task<LilithGameTerminalEvent> terminalTask)
    {
        try
        {
            var terminal = await terminalTask.WaitAsync(
                    TurnTerminalReconciliationTimeout,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return IsHermesTurnTerminalStatus(terminal.Status);
        }
        catch (Exception exception) when (exception is
            TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    internal static bool IsHermesTurnTerminalStatus(string status)
        => status is "complete" or "interrupted" or "error";

    private void QuarantineTurn(
        Guid clientTurnId,
        string? promptTurnId,
        Task<LilithGameTerminalEvent>? exactTerminalTask)
    {
        QuarantinedGameTurn quarantined;
        lock (_eventLock)
        {
            if (_quarantinedTurn is not null)
            {
                throw new InvalidOperationException(
                    "More than one game turn required terminal reconciliation.");
            }
            quarantined = new QuarantinedGameTurn(
                clientTurnId,
                promptTurnId,
                exactTerminalTask);
            _quarantinedTurn = quarantined;
        }
        if (exactTerminalTask is not null)
        {
            _ = ObserveExactTerminalAndFinalizeAsync(
                exactTerminalTask,
                () => FinalizeQuarantinedTurn(quarantined));
        }
    }

    internal static async Task ObserveExactTerminalAndFinalizeAsync(
        Task<LilithGameTerminalEvent> exactTerminalTask,
        Action finalize)
    {
        ArgumentNullException.ThrowIfNull(exactTerminalTask);
        ArgumentNullException.ThrowIfNull(finalize);
        try
        {
            var terminal = await exactTerminalTask.ConfigureAwait(false);
            if (IsHermesTurnTerminalStatus(terminal.Status))
                finalize();
        }
        catch
        {
            // A faulted/cancelled waiter cannot attest a remote terminal. The
            // quarantine remains authoritative until generation retirement.
        }
    }

    private string? ResolveQuarantinedTurn()
    {
        if (Volatile.Read(
                ref _nativeAssistantContinuationRecoveryRequired) != 0)
        {
            return "runtime.native_assistant_continuation_recovery_required";
        }
        QuarantinedGameTurn? quarantined;
        lock (_eventLock)
            quarantined = _quarantinedTurn;
        if (quarantined is null)
            return null;
        if (quarantined.PromptTurnId is null
            || quarantined.ExactTerminalTask is null)
        {
            // Crossing prompt.submit's send boundary without a trustworthy
            // turn id cannot be reconciled with session-wide idle. No later
            // prompt may dispatch in this generation.
            return "runtime.turn_generation_recovery_no_receipt";
        }
        if (!quarantined.ExactTerminalTask.IsCompleted)
            return "runtime.turn_reconciliation_pending";
        if (!quarantined.ExactTerminalTask.IsCompletedSuccessfully)
        {
            return "runtime.turn_generation_recovery_waiter_failed";
        }
        if (!IsHermesTurnTerminalStatus(
                quarantined.ExactTerminalTask.Result.Status))
        {
            // Keep the rejected exact terminal quarantined. This bounded code
            // identifies the failed contract without exposing its payload or
            // confusing a stopped turn with an unavailable provider.
            return "runtime.turn_generation_recovery_terminal_rejected";
        }

        FinalizeQuarantinedTurn(quarantined);
        if (!quarantined.Resolution.IsCompleted)
            return "runtime.turn_reconciliation_pending";
        return quarantined.Resolution.IsCompletedSuccessfully
            && quarantined.Resolution.Result
            ? null
            : "runtime.turn_generation_recovery_retirement_failed";
    }

    private void FinalizeQuarantinedTurn(QuarantinedGameTurn quarantined)
    {
        if (!quarantined.TryClaimResolution())
            return;
        try
        {
            lock (_eventLock)
            {
                if (!ReferenceEquals(_quarantinedTurn, quarantined))
                    throw new InvalidOperationException(
                        "The quarantined game turn owner changed unexpectedly.");
                if (quarantined.PromptTurnId is not null)
                    _terminalWaiters.Remove(quarantined.PromptTurnId);
            }
            _turnAdmissions.ObserveRemoteTerminal(quarantined.ClientTurnId);
            _turnAdmissions.Retire(quarantined.ClientTurnId);
        }
        catch
        {
            quarantined.FailResolution();
            return;
        }
        lock (_eventLock)
        {
            if (ReferenceEquals(_quarantinedTurn, quarantined))
                _quarantinedTurn = null;
        }
        quarantined.CompleteResolution();
    }

    private static LilithGameTurnOutcome InterruptedTurn(bool userCommitted)
        => new(
            false,
            userCommitted,
            LilithGameBridgeTerminal.Interrupted,
            string.Empty,
            string.Empty,
            "hermes.turn_interrupted");

    private async Task<HistorySnapshot> CaptureHistorySnapshotAsync(
        LilithRuntimeModelAuthorityLease modelAuthority,
        CancellationToken cancellationToken)
    {
        var sessionId = _router.ActiveSessionId
            ?? throw new InvalidDataException("The game session is not active.");
        var response = await _router.RouteAsync(
            new LilithRuntimeRequest(
                "session.history",
                JsonSerializer.SerializeToElement(new { session_id = sessionId })),
            cancellationToken,
            modelAuthority).ConfigureAwait(false);
        if (!response.Succeeded
            || response.HermesPayload.ValueKind != JsonValueKind.Object
            || !response.HermesPayload.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Hermes history was rejected.");
        }

        var entries = DecodeVisibleHistoryEntries(messages).ToList();
        entries.Reverse();
        return new HistorySnapshot(
            Guid.NewGuid(),
            sessionId,
            DateTimeOffset.UtcNow,
            entries.AsReadOnly());
    }

    internal static IReadOnlyList<LilithGameBridgeHistoryEntry>
        DecodeVisibleHistoryEntries(JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array
            || messages.GetArrayLength() > MaximumHistorySnapshotMessages)
        {
            throw new InvalidDataException("Hermes history was rejected.");
        }

        var entries = new List<LilithGameBridgeHistoryEntry>();
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("role", out var roleValue)
                || roleValue.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Hermes history message was malformed.");
            }
            var role = roleValue.GetString()!;
            if (role is not ("user" or "assistant"))
                continue;
            if (message.TryGetProperty("display_kind", out var displayKind)
                && displayKind.ValueKind == JsonValueKind.String
                && displayKind.GetString() is "model_switch" or "personality_switch"
                    or "auto_continue" or "async_delegation_complete" or "hidden")
            {
                // Hermes represents timeline-only events as user-shaped rows so
                // its desktop can render them in place. F10 is the final dialogue
                // projection, so those rows must not invalidate or enter the page.
                continue;
            }
            if (!message.TryGetProperty("text", out var textValue)
                || textValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(textValue.GetString()))
            {
                continue;
            }
            string messageKind;
            string messageId;
            string turnId;
            if (role == "user"
                && TryProjectLegacyUnscopedUserIdentity(
                    message,
                    out messageId,
                    out turnId))
            {
                // Older messaging-gateway failures could persist the player's
                // text before a canonical turn claim existed. A positive durable
                // row id is sufficient to give that read-only F10 projection a
                // stable identity without rewriting Hermes or weakening checks
                // for partially populated/corrupt identity metadata.
                messageKind = "user";
            }
            else
            {
                messageKind = RequiredHistoryString(message, "message_kind");
                messageId = RequiredHistoryString(message, "message_id");
                turnId = RequiredHistoryString(message, "turn_id");
            }
            if (role == "assistant"
                && !string.Equals(
                    messageKind,
                    "primary",
                    StringComparison.Ordinal))
            {
                // Hermes persists visible interim/tool-call assistant rows as
                // separate transcript records. F10 is the final-conversation
                // projection, so a non-primary row is intentionally omitted
                // instead of being allowed to poison the whole history page.
                continue;
            }
            if (role == "user"
                && !string.Equals(
                    messageKind,
                    "user",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes history user message kind was malformed.");
            }
            if (!message.TryGetProperty("timestamp", out var timestamp)
                || timestamp.ValueKind != JsonValueKind.Number
                || !timestamp.TryGetDouble(out var seconds)
                || !double.IsFinite(seconds)
                || seconds < 0
                || seconds > 253402300799.999)
            {
                throw new InvalidDataException(
                    "Hermes history timestamp was malformed.");
            }
            var milliseconds = checked((long)Math.Round(
                seconds * 1000d,
                MidpointRounding.AwayFromZero));
            entries.Add(new LilithGameBridgeHistoryEntry(
                messageId,
                turnId,
                messageKind,
                role,
                textValue.GetString()!,
                milliseconds));
        }
        return entries.AsReadOnly();
    }

    private static bool TryProjectLegacyUnscopedUserIdentity(
        JsonElement message,
        out string messageId,
        out string turnId)
    {
        messageId = string.Empty;
        turnId = string.Empty;
        if (message.TryGetProperty("message_kind", out _)
            || message.TryGetProperty("message_id", out _)
            || message.TryGetProperty("turn_id", out _)
            || !message.TryGetProperty("row_id", out var rowIdValue)
            || rowIdValue.ValueKind != JsonValueKind.Number
            || !rowIdValue.TryGetInt64(out var rowId)
            || rowId <= 0)
        {
            return false;
        }

        messageId = CreateLegacyHistoryIdentity("message", rowId);
        turnId = CreateLegacyHistoryIdentity("turn", rowId);
        return true;
    }

    private static string CreateLegacyHistoryIdentity(string domain, long rowId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            "lilith-f10-legacy-user-v1:"
            + domain
            + ":"
            + rowId.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    private bool TryResolveCursor(
        string cursor,
        out HistorySnapshot snapshot,
        out int offset)
    {
        snapshot = null!;
        offset = 0;
        var separator = cursor.IndexOf('.', StringComparison.Ordinal);
        if (separator != 32
            || !Guid.TryParseExact(cursor[..separator], "N", out var snapshotId)
            || !int.TryParse(
                cursor[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out offset)
            || offset < 0)
        {
            return false;
        }
        var candidate = _historySnapshot;
        if (candidate is null
            || candidate.Id != snapshotId
            || !string.Equals(
                candidate.SessionId,
                _router.ActiveSessionId,
                StringComparison.Ordinal)
            || DateTimeOffset.UtcNow - candidate.CreatedAt > HistorySnapshotLifetime
            || offset > candidate.Entries.Count)
        {
            return false;
        }
        snapshot = candidate;
        return true;
    }

    private void RuntimeGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        if (string.Equals(gatewayEvent.Type, LilithMiniGameDiagnosticCollector.EventType, StringComparison.Ordinal))
        {
            // Observation must never claim a pending turn in the normal router.
            // The accepted prompt receipt, not this event, supplies authority.
            if (gatewayEvent.SessionId == _router.ActiveSessionId
                && _router.IsSessionAttested(gatewayEvent.SessionId))
            {
                lock (_eventLock)
                    _miniGameDiagnostic?.Observe(gatewayEvent, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            return;
        }
        bool allowed;
        try
        {
            allowed = _router.ObserveAndAllowGatewayEvent(gatewayEvent);
        }
        catch
        {
            return;
        }
        if (!allowed)
            TryPublishForegroundProactivePlayerReplySignal(gatewayEvent);
        if (string.Equals(gatewayEvent.Type, "session.info", StringComparison.Ordinal))
        {
            lock (_eventLock)
                _sessionInfoSignal.TrySetResult(true);
            if (allowed)
                PersistHandleIfAvailable();
            return;
        }
        if (string.Equals(
                gatewayEvent.Type,
                HermesTurnCorrelationContract.CronCompletionEventType,
                StringComparison.Ordinal))
        {
            if (allowed
                && string.Equals(
                    gatewayEvent.SessionId,
                    _router.ActiveSessionId,
                    StringComparison.Ordinal))
            {
                ObserveCronCompletion(gatewayEvent);
            }
            return;
        }
        if (allowed
            && string.Equals(gatewayEvent.Type, "error", StringComparison.Ordinal)
            && _router.ActiveSessionId is { } activeSessionId
            && (!_router.IsSessionAttested(activeSessionId)
                || string.Equals(
                    _modelSyncReceiptRequiredSessionId,
                    activeSessionId,
                    StringComparison.Ordinal))
            && IsSessionActivationTerminalError(gatewayEvent, activeSessionId))
        {
            SignalSessionActivationFailure(activeSessionId);
            return;
        }
        if (gatewayEvent.Type is "voice.status" or "voice.transcript")
        {
            if (allowed)
                ObserveGameVoiceGatewayEvent(gatewayEvent);
            return;
        }
        if (!allowed
            || !string.Equals(
                gatewayEvent.Type,
                "message.complete",
                StringComparison.Ordinal))
        {
            if (!allowed
                && string.Equals(
                    gatewayEvent.Type,
                    "message.complete",
                    StringComparison.Ordinal)
                && string.Equals(
                    gatewayEvent.SessionId,
                    _router.ActiveSessionId,
                    StringComparison.Ordinal))
            {
                SignalRejectedTerminal(gatewayEvent);
            }
            return;
        }

        try
        {
            var turnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
                gatewayEvent.Type,
                gatewayEvent.Parameters);
            var status = RequiredHistoryString(gatewayEvent.Payload, "status");
            var allowEmptyCompletedExtension =
                string.Equals(status, "complete", StringComparison.Ordinal)
                && gatewayEvent.Payload.TryGetProperty(
                    "native_assistant_extension_status",
                    out var extensionStatus)
                && extensionStatus.ValueKind == JsonValueKind.String
                && string.Equals(
                    extensionStatus.GetString(),
                    "silent",
                    StringComparison.Ordinal);
            var text = RequiredHistoryString(
                gatewayEvent.Payload,
                "text",
                allowEmpty: status != "complete"
                    || allowEmptyCompletedExtension);
            var terminal = new LilithGameTerminalEvent(
                status,
                text,
                gatewayEvent.Payload.Clone());
            lock (_eventLock)
            {
                if (_noteInboxTurnOwner?.NativeTurnId == turnId)
                    _noteInboxTurnOwner = null;
                if (_terminalWaiters.TryGetValue(turnId, out var waiter))
                {
                    waiter.TrySetResult(terminal);
                    return;
                }
                if (_terminalCache.TryAdd(turnId, terminal))
                    _terminalCacheOrder.Enqueue(turnId);
                while (_terminalCacheOrder.Count > MaximumTerminalCache)
                {
                    var expired = _terminalCacheOrder.Dequeue();
                    _terminalCache.Remove(expired);
                }
            }
        }
        catch
        {
            // The router already rejected malformed terminal frames. A second
            // projection parser failure cannot be converted into a reply.
        }
    }

    private void TryPublishForegroundProactivePlayerReplySignal(
        HermesGatewayEvent gatewayEvent)
    {
        // GameBridge-admitted F6/F7 turns re-arm locally before Hermes
        // dispatch. An unadmitted durable user event is therefore the narrow
        // cross-surface path: a Control Center prompt for this exact hidden
        // game session.
        if (!string.Equals(
                gatewayEvent.Type,
                "message.user",
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var sessionId = _router.ActiveSessionId;
            var lineage = _router.ActiveGameLineageAttestation;
            if (string.IsNullOrWhiteSpace(sessionId)
                || lineage is null
                || !string.Equals(
                    gatewayEvent.SessionId,
                    sessionId,
                    StringComparison.Ordinal)
                || !IsExactActiveDurableLineage(sessionId, lineage))
            {
                return;
            }

            var turnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
                gatewayEvent.Type,
                gatewayEvent.Parameters);
            RequireExactProperties(
                gatewayEvent.Payload,
                "message_id", "text", "durable");
            _ = HermesTurnCorrelationContract.RequireIdentity(
                RequiredString(gatewayEvent.Payload, "message_id"),
                "message.user message_id");
            if (!gatewayEvent.Payload.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var durable = gatewayEvent.Payload.GetProperty("durable");
            RequireExactProperties(
                durable,
                "source", "row_id", "timestamp", "conversation_activity",
                "turn_origin", "turn_origin_kind");
            if (!string.Equals(
                    RequiredString(durable, "source"),
                    "state.db",
                    StringComparison.Ordinal)
                || RequiredPositiveInt64(durable, "row_id") <= 0
                || RequiredFiniteTimestamp(durable, "timestamp") <= 0d
                || !RequiredBoolean(durable, "conversation_activity")
                || !string.Equals(
                    RequiredString(durable, "turn_origin"),
                    "user",
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(
                    RequiredString(durable, "turn_origin_kind")))
            {
                return;
            }

            PublishForegroundProactivePlayerReplySignal(turnId);
        }
        catch (InvalidDataException)
        {
            // Malformed or non-canonical events cannot advance the local
            // proactive timer.
        }
    }

    private void PublishForegroundProactivePlayerReplySignal(string turnId)
    {
        InvalidateSyntheticPresentations();
        TaskCompletionSource<bool> signal;
        lock (_notificationLock)
        {
            if (_disposed)
                return;
            var projectionKey = "foreground-player-reply:" + turnId;
            if (!_nativeAssistantNotificationKeys.Add(projectionKey))
                return;
            var sequence = checked(_notificationSequence + 1);
            var entry = new LilithGameBridgeNotificationEntry(
                sequence,
                LilithGameBridgeProtocol.ForegroundProactivePlayerReplyEventType,
                LilithGameBridgeProtocol.ForegroundProactivePlayerReplySchema,
                succeeded: true,
                text: string.Empty,
                speechText: string.Empty,
                speechLocale: LilithGameBridgeSpeechLocale.Chinese);
            _notificationSequence = sequence;
            _notifications.Enqueue(entry);
            while (_notifications.Count > MaximumNotificationRetention)
                _notifications.Dequeue();
            _nativeAssistantNotificationKeyOrder.Enqueue(projectionKey);
            while (_nativeAssistantNotificationKeyOrder.Count
                > MaximumNotificationRetention)
            {
                _nativeAssistantNotificationKeys.Remove(
                    _nativeAssistantNotificationKeyOrder.Dequeue());
            }
            signal = _notificationSignal;
            _notificationSignal = NewSignal();
        }
        signal.TrySetResult(true);
    }

    private void ObserveCronCompletion(HermesGatewayEvent gatewayEvent)
    {
        try
        {
            if (gatewayEvent.Payload.ValueKind != JsonValueKind.Object)
                return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in gatewayEvent.Payload.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    return;
            }
            if (!names.SetEquals(new[] { "schema", "success", "text" })
                || !gatewayEvent.Payload.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || !string.Equals(
                    schema.GetString(),
                    HermesTurnCorrelationContract.CronCompletionSchema,
                    StringComparison.Ordinal)
                || !gatewayEvent.Payload.TryGetProperty("success", out var success)
                || success.ValueKind is not
                    (JsonValueKind.True or JsonValueKind.False)
                || !gatewayEvent.Payload.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String
                || text.GetString() is not { } completionText)
            {
                return;
            }

            TaskCompletionSource<bool> signal;
            lock (_notificationLock)
            {
                if (_disposed)
                    return;
                var sequence = checked(_notificationSequence + 1);
                var entry = new LilithGameBridgeNotificationEntry(
                    sequence,
                    HermesTurnCorrelationContract.CronCompletionEventType,
                    HermesTurnCorrelationContract.CronCompletionSchema,
                    success.GetBoolean(),
                    completionText);
                _notificationSequence = sequence;
                _notifications.Enqueue(entry);
                while (_notifications.Count > MaximumNotificationRetention)
                    _notifications.Dequeue();
                signal = _notificationSignal;
                _notificationSignal = NewSignal();
            }
            signal.TrySetResult(true);
        }
        catch
        {
            // The router already schema-validated this session event. A
            // defensive projection failure must remain presentation-only.
        }
    }

    private void ObserveGameVoiceGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        lock (_eventLock)
        {
            var capture = _voiceCapture;
            if (capture is null
                || !string.Equals(
                    gatewayEvent.SessionId,
                    capture.Owner.SessionId,
                    StringComparison.Ordinal)
                || gatewayEvent.Payload.ValueKind != JsonValueKind.Object
                || !gatewayEvent.Payload.TryGetProperty(
                    "capture_id",
                    out var captureValue)
                || captureValue.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(
                    captureValue.GetString(),
                    "N",
                    out var captureId)
                || captureId != capture.Owner.CaptureId)
            {
                return;
            }

            if (gatewayEvent.Type == "voice.status")
            {
                if (gatewayEvent.Payload.TryGetProperty("state", out var state)
                    && state.ValueKind == JsonValueKind.String
                    && string.Equals(
                        state.GetString(),
                        "idle",
                        StringComparison.Ordinal))
                {
                    capture.Idle.TrySetResult();
                }
                return;
            }

            if (gatewayEvent.Payload.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                && text.GetString() is { } transcript
                && !string.IsNullOrWhiteSpace(transcript)
                && !gatewayEvent.Payload.TryGetProperty("stop_phrase", out _))
            {
                capture.Terminal.TrySetResult(
                    new VoiceCaptureTerminal(
                        transcript,
                        NoSpeech: false,
                        Rejected: false,
                        Cancelled: false));
                return;
            }
            if (gatewayEvent.Payload.TryGetProperty(
                    "no_speech_limit",
                    out var noSpeech)
                && noSpeech.ValueKind == JsonValueKind.True)
            {
                capture.Terminal.TrySetResult(
                    new VoiceCaptureTerminal(
                        string.Empty,
                        NoSpeech: true,
                        Rejected: false,
                        Cancelled: false));
                return;
            }
            capture.Terminal.TrySetResult(
                new VoiceCaptureTerminal(
                    string.Empty,
                    NoSpeech: false,
                    Rejected: true,
                    Cancelled: false));
        }
    }

    private void SignalRejectedTerminal(HermesGatewayEvent gatewayEvent)
    {
        try
        {
            var turnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
                gatewayEvent.Type,
                gatewayEvent.Parameters);
            var terminal = new LilithGameTerminalEvent(
                "authority_error",
                string.Empty,
                gatewayEvent.Payload.Clone());
            lock (_eventLock)
            {
                if (_noteInboxTurnOwner?.NativeTurnId == turnId)
                    _noteInboxTurnOwner = null;
                if (_terminalWaiters.TryGetValue(turnId, out var waiter))
                {
                    waiter.TrySetResult(terminal);
                    return;
                }
                if (_terminalCache.TryAdd(turnId, terminal))
                    _terminalCacheOrder.Enqueue(turnId);
                while (_terminalCacheOrder.Count > MaximumTerminalCache)
                {
                    var expired = _terminalCacheOrder.Dequeue();
                    _terminalCache.Remove(expired);
                }
            }
        }
        catch (InvalidDataException)
        {
            // A frame without a canonical turn identity cannot close a turn.
        }
    }

    private bool TryReadHandle(out string storedSessionId)
    {
        storedSessionId = string.Empty;
        try
        {
            if (!File.Exists(_handlePath))
                return false;
            using var document = JsonDocument.Parse(File.ReadAllBytes(_handlePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Select(static p => p.Name)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(new[] { "schemaVersion", "storedSessionId" }) is false
                || root.GetProperty("schemaVersion").GetInt32()
                    != HandleSchemaVersion
                || root.GetProperty("storedSessionId").ValueKind
                    != JsonValueKind.String)
            {
                return false;
            }
            var value = root.GetProperty("storedSessionId").GetString()!;
            if (string.IsNullOrWhiteSpace(value)
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
                || StrictUtf8.GetByteCount(value)
                    > LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes
                || value.Any(char.IsControl))
            {
                return false;
            }
            storedSessionId = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void QuarantineHandle()
    {
        if (!File.Exists(_handlePath))
            return;
        var rejectedPath = _handlePath + ".rejected";
        try
        {
            File.Move(_handlePath, rejectedPath, overwrite: true);
            _historySnapshot = null;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The untrusted game session handle could not be isolated.",
                exception);
        }
    }

    internal static bool IsDefiniteUntrustedHandleRejection(
        LilithRuntimeResponse response)
    {
        if (response.Succeeded
            || response.HermesPayload.ValueKind != JsonValueKind.Object
            || !response.HermesPayload.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        if (string.Equals(
                code.GetString(),
                "runtime.game_session_lineage_rejected",
                StringComparison.Ordinal))
        {
            return true;
        }
        return string.Equals(
                code.GetString(),
                "hermes.rpc_rejected",
                StringComparison.Ordinal)
            && response.HermesPayload.TryGetProperty(
                "hermesCode",
                out var hermesCode)
            && hermesCode.ValueKind == JsonValueKind.Number
            && hermesCode.TryGetInt32(out var value)
            && value == 4094;
    }

    private LilithGameSessionLineageAttestation RequireExactActiveLineage(
        string sessionId)
    {
        var lineage = _router.ActiveGameLineageAttestation;
        if (lineage is null || !IsExactActiveLineage(sessionId, lineage))
        {
            throw new InvalidDataException(
                "Hermes did not prove the active hidden game session's durable lineage.");
        }
        return lineage;
    }

    private LilithGameSessionLineageAttestation RequireExactActiveDurableLineage(
        string sessionId)
    {
        var lineage = _router.ActiveGameLineageAttestation;
        if (lineage is null || !IsExactActiveDurableLineage(sessionId, lineage))
        {
            throw new InvalidDataException(
                "Hermes did not prove the active hidden game session's durable lineage.");
        }
        return lineage;
    }

    private static bool IsDurableResumeContinuation(
        LilithGameSessionLineageAttestation previous,
        LilithGameSessionLineageAttestation refreshed)
        => string.Equals(
                refreshed.RequestedStoredSessionId,
                previous.ResolvedStoredSessionId,
                StringComparison.Ordinal)
            && refreshed.Archived
            && string.Equals(
                refreshed.Source,
                "lilith-game",
                StringComparison.Ordinal);

    private bool IsExactActiveLineage(
        string sessionId,
        LilithGameSessionLineageAttestation lineage)
        => _router.IsSessionAttested(sessionId)
            && IsExactActiveDurableLineage(sessionId, lineage);

    private bool IsExactActiveDurableLineage(
        string sessionId,
        LilithGameSessionLineageAttestation lineage)
    {
        var activeLineage = _router.ActiveGameLineageAttestation;
        return !string.IsNullOrWhiteSpace(sessionId)
            && string.Equals(
                _router.ActiveSessionId,
                sessionId,
                StringComparison.Ordinal)
            && string.Equals(
                _router.ActiveStoredSessionId,
                lineage.ResolvedStoredSessionId,
                StringComparison.Ordinal)
            && activeLineage is not null
            && string.Equals(
                activeLineage.FingerprintSha256,
                lineage.FingerprintSha256,
                StringComparison.Ordinal)
            && lineage.Archived
            && string.Equals(lineage.Source, "lilith-game", StringComparison.Ordinal)
            && lineage.SessionIds.Count > 0
            && string.Equals(
                lineage.SessionIds[0],
                lineage.RequestedStoredSessionId,
                StringComparison.Ordinal)
            && string.Equals(
                lineage.SessionIds[^1],
                lineage.ResolvedStoredSessionId,
                StringComparison.Ordinal);
    }

    private void PersistHandleIfAvailable()
    {
        var stored = _router.ActiveStoredSessionId;
        var sessionId = _router.ActiveSessionId;
        var activeLineage = _router.ActiveGameLineageAttestation;
        if (string.IsNullOrWhiteSpace(stored)
            || sessionId is null
            || activeLineage is null
            || !string.Equals(
                stored,
                activeLineage.ResolvedStoredSessionId,
                StringComparison.Ordinal)
            || !IsExactActiveDurableLineage(sessionId, activeLineage))
        {
            return;
        }
        WriteAtomicJson(
            _handlePath,
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = HandleSchemaVersion,
                storedSessionId = stored
            }));
    }

    private void WriteAuthorityReceipt(
        string sessionId,
        LilithGameSessionLineageAttestation lineage)
    {
        WriteAtomicJson(
            _authorityReceiptPath,
            CreateAuthorityReceiptPayload(
                _snapshot.GenerationId,
                sessionId,
                lineage,
                DateTimeOffset.UtcNow));
    }

    internal static byte[] CreateAuthorityReceiptPayload(
        Guid generationId,
        string sessionId,
        LilithGameSessionLineageAttestation lineage,
        DateTimeOffset recordedAt)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("Runtime generation is required.", nameof(generationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(lineage);
        if (!lineage.Archived
            || !string.Equals(lineage.Source, "lilith-game", StringComparison.Ordinal)
            || lineage.SessionIds.Count == 0)
        {
            throw new InvalidDataException(
                "Only an exact hidden game lineage may receive an authority receipt.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = AuthorityReceiptSchemaVersion,
            generationId = generationId.ToString("N"),
            sessionSha256 = Sha256(sessionId),
            requestedStoredSessionSha256 = Sha256(
                lineage.RequestedStoredSessionId),
            resolvedStoredSessionSha256 = Sha256(
                lineage.ResolvedStoredSessionId),
            durableLineageSha256 = lineage.FingerprintSha256,
            durableLineageLength = lineage.SessionIds.Count,
            durableSource = lineage.Source,
            durableArchived = lineage.Archived,
            durableLineageVerified = true,
            approvalKey = "yolo",
            scope = "session",
            enabled = true,
            globalApprovalsMutated = false,
            recordedAtUnixTimeMilliseconds =
                recordedAt.ToUnixTimeMilliseconds()
        });
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void WriteAtomicJson(string path, byte[] payload)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static bool ExactSessionYoloReceipt(JsonElement receipt)
    {
        if (receipt.ValueKind != JsonValueKind.Object)
            return false;
        var values = receipt.EnumerateObject().ToArray();
        return values.Length == 3
            && receipt.TryGetProperty("key", out var key)
            && key.ValueKind == JsonValueKind.String
            && string.Equals(key.GetString(), "yolo", StringComparison.Ordinal)
            && receipt.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), "1", StringComparison.Ordinal)
            && receipt.TryGetProperty("scope", out var scope)
            && scope.ValueKind == JsonValueKind.String
            && string.Equals(scope.GetString(), "session", StringComparison.Ordinal);
    }

    internal static string GameRpcFailureCode(LilithRuntimeResponse response, string phase)
    {
        var fallback = phase switch
        {
            "prompt" => "runtime.prompt_rejected",
            "approval" => "runtime.game_approval_bypass_rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        if (response.Succeeded) return fallback;
        var code = FailureCode(response, fallback);
        if (code != "hermes.rpc_rejected") return code;
        // Metadata only. This diagnoses the rejected RPC, never proves that a
        // user turn is safe to replay or that a restart caused the rejection.
        // Unknown values and all provider prose remain outside the game log.
        var native = response.HermesPayload.TryGetProperty("hermesCode", out var raw)
            && raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var value)
            ? value : 0;
        var suffix = native switch
        {
            4001 => "4001", 4004 => "4004", 4009 => "4009", 4014 => "4014",
            4030 => "4030", 4090 => "4090", 4091 => "4091", 4094 => "4094",
            5000 => "5000", 5004 => "5004", 5005 => "5005", 5006 => "5006",
            5008 => "5008", 5030 => "5030", 5034 => "5034", 5035 => "5035",
            _ => "unknown"
        };
        return "hermes." + phase + "_rpc_rejected_" + suffix;
    }

    private static string FailureCode(
        LilithRuntimeResponse response,
        string fallback)
    {
        if (response.HermesPayload.ValueKind == JsonValueKind.Object
            && response.HermesPayload.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.String)
        {
            var value = code.GetString()!;
            if (!string.IsNullOrWhiteSpace(value)
                && value.Length <= 128
                && value.All(character =>
                    character is >= 'a' and <= 'z'
                        or >= '0' and <= '9'
                        or '.' or '_' or '-'))
            {
                return value;
            }
        }
        return fallback;
    }

    internal static string SessionActivationFailureCode(
        LilithRuntimeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var code = FailureCode(response, string.Empty);
        if (string.Equals(
                code,
                "runtime.game_session_lineage_rejected",
                StringComparison.Ordinal))
        {
            return code;
        }
        if (string.Equals(
                code,
                "runtime.game_model_selection_required",
                StringComparison.Ordinal))
        {
            return code;
        }
        if (string.Equals(
                code,
                "runtime.session_activation_not_sent",
                StringComparison.Ordinal)
            || string.Equals(
                code,
                "runtime.session_activation_quarantined",
                StringComparison.Ordinal))
        {
            return code;
        }
        if (string.Equals(
                code,
                "runtime.native_config_quarantined",
                StringComparison.Ordinal)
            || string.Equals(
                code,
                "runtime.native_surface_attestation_required",
                StringComparison.Ordinal))
        {
            return code;
        }
        if (response.HermesPayload.ValueKind == JsonValueKind.Object
            && response.HermesPayload.TryGetProperty(
                "hermesCode",
                out var hermesCode)
            && hermesCode.ValueKind == JsonValueKind.Number
            && hermesCode.TryGetInt32(out var value))
        {
            return value switch
            {
                4091 => "runtime.game_model_selection_required",
                5006 => "runtime.game_session_persistence_failed",
                _ => "runtime.game_session_rejected"
            };
        }
        return "runtime.game_session_rejected";
    }

    private static string RequiredHistoryString(
        JsonElement value,
        string property,
        bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes history string was malformed.");
        }
        var text = element.GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Hermes history string was empty.");
        return text;
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ActiveGameVoiceCapture
    {
        internal ActiveGameVoiceCapture(LilithRuntimeVoiceCaptureOwner owner)
        {
            Owner = owner;
        }

        internal LilithRuntimeVoiceCaptureOwner Owner { get; }

        internal TaskCompletionSource<VoiceCaptureTerminal> Terminal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Idle { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool TranscribeRequested { get; set; }

        private LilithGameVoiceTerminalClaim TerminalClaim { get; } = new();

        internal bool TryClaimTerminal()
            => TerminalClaim.TryClaim();
    }

    private enum SyntheticAssistantContinuationKind
    {
        NativeAssistantExtension = 1,
        ForegroundProactive = 2
    }

    private sealed class NativeAssistantContinuationLease : IDisposable
    {
        private int _disposed;

        internal NativeAssistantContinuationLease(
            SyntheticAssistantContinuationKind kind)
        {
            Kind = kind;
        }

        internal SyntheticAssistantContinuationKind Kind { get; }

        internal CancellationTokenSource Preemption { get; } = new();

        internal string SessionId { get; set; } = string.Empty;

        internal string TurnId { get; set; } = string.Empty;

        internal string EventId { get; set; } = string.Empty;

        internal string AssistantMessageId { get; set; } = string.Empty;
        internal LilithMiniGamePublicContext? MiniGameContext { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Preemption.Dispose();
        }
    }

    private sealed class GameVoiceOwnerLease : IDisposable
    {
        private int _disposed;

        internal GameVoiceOwnerLease(LilithRuntimeVoiceCaptureOwner owner)
        {
            Owner = owner;
        }

        internal LilithRuntimeVoiceCaptureOwner Owner { get; }

        internal CancellationTokenRegistration PeerExitRegistration { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            PeerExitRegistration.Dispose();
        }
    }

    private sealed record VoiceCaptureTerminal(
        string Transcript,
        bool NoSpeech,
        bool Rejected,
        bool Cancelled);

    private sealed class QuarantinedGameTurn
    {
        private readonly TaskCompletionSource<bool> _resolution = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _resolutionClaimed;

        internal QuarantinedGameTurn(
            Guid clientTurnId,
            string? promptTurnId,
            Task<LilithGameTerminalEvent>? exactTerminalTask)
        {
            ClientTurnId = clientTurnId;
            PromptTurnId = promptTurnId;
            ExactTerminalTask = exactTerminalTask;
        }

        internal Guid ClientTurnId { get; }

        internal string? PromptTurnId { get; }

        internal Task<LilithGameTerminalEvent>? ExactTerminalTask { get; }

        internal Task<bool> Resolution => _resolution.Task;

        internal bool TryClaimResolution()
            => Interlocked.CompareExchange(
                ref _resolutionClaimed,
                value: 1,
                comparand: 0) == 0;

        internal void CompleteResolution() => _resolution.TrySetResult(true);

        internal void FailResolution() => _resolution.TrySetResult(false);
    }

    private sealed record HistorySnapshot(
        Guid Id,
        string SessionId,
        DateTimeOffset CreatedAt,
        IReadOnlyList<LilithGameBridgeHistoryEntry> Entries);
}

internal sealed class LilithGameBridgeOperationException : Exception
{
    internal string Code { get; }

    internal LilithGameBridgeOperationException(string code)
        : base(code)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > 128
            || code.Any(character =>
                character is not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '.'
                    and not '_'
                    and not '-'))
        {
            throw new ArgumentException(
                "A safe game bridge failure code is required.",
                nameof(code));
        }
        Code = code;
    }
}
