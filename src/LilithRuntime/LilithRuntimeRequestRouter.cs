using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal enum LilithRuntimeSessionSurface
{
    Workbench = 0,
    GameBridge = 1
}

internal sealed record LilithRuntimeSessionModelObservation(
    string ProviderId,
    string ModelId,
    bool Running,
    long Revision);

internal sealed partial class LilithRuntimeRequestRouter
{
    internal static readonly IReadOnlyList<string> AllowedMethods =
        Array.AsReadOnly(new[]
        {
            "runtime.status",
            "runtime.letters.get",
            "runtime.letters.set",
            HermesCapabilityCatalogContract.Method,
            HermesCommandCatalogContract.Method,
            "tools.catalog",
            "tools.set",
            "tools.settings.get",
            "tools.settings.set",
            HermesUsageBudgetContract.GetMethod,
            HermesUsageBudgetContract.SetMethod,
            HermesUsageBudgetPreviewContract.PreviewMethod,
            HermesLatestUsageContract.GetMethod,
            "memory.catalog",
            "memory.entry.add",
            "memory.entry.pin.set",
            "memory.entry.promote",
            "memory.entry.correct",
            "memory.entry.visibility.set",
            "memory.entry.reject",
            "memory.entry.delete",
            "memory.preferences.get",
            "memory.preferences.set",
            "memory.export",
            "expression.get",
            "expression.version.get",
            "expression.save",
            "expression.revert",
            "session.create",
            "session.list",
            "session.resume",
            "session.status",
            "session.history",
            "session.append_assistant_event",
            "session.extend_native_assistant_event",
            "session.generate_foreground_proactive",
            "session.delete",
            "session.title",
            "session.interrupt",
            "session.compress",
            "session.usage",
            "session.context_breakdown",
            "session.close",
            "prompt.submit",
            "prompt.project_speech",
            "prompt.draft_note",
            "image.attach_bytes",
            "image.detach",
            "model.options",
            "model.credential_status",
            "model.replace_key",
            "model.remove_key",
            "provider.setup.catalog",
            "provider.setup.models",
            "provider.setup.save",
            "provider.setup.remove",
            "messaging.telegram.status",
            "messaging.telegram.replace",
            "messaging.telegram.remove",
            "messaging.telegram.pairing.list",
            "messaging.telegram.pairing.approve",
            "messaging.telegram.pairing.revoke",
            "messaging.telegram.voice.status",
            "messaging.telegram.voice.set",
            "model.default.set",
            "config.set",
            "learning.frames",
            "approval.respond",
            "clarify.respond",
            "sudo.respond",
            "secret.respond",
            "delegation.status",
            "subagent.steer",
            "subagent.interrupt",
            "voice.toggle",
            "voice.record",
            "rollback.list",
            "rollback.diff",
            "rollback.restore"
        });

    private const int MaximumPromptUtf8Bytes = 4 * 1024 * 1024;
    private const int MaximumNativeAssistantEventCharacters =
        LilithGameBridgeProtocol.MaximumNativeAssistantEventCharacters;
    private const int MaximumTitleUtf8Bytes = 4 * 1024;
    private const int MaximumModelUtf8Bytes = 1024;
    private const int MaximumProviderUtf8Bytes = 256;
    private const int MaximumCredentialUtf8Bytes = 16 * 1024;
    private const int MaximumCredentialOperationIdUtf8Bytes = 128;
    private const int MaximumMemoryOperationIdUtf8Bytes = 128;
    private const int MaximumMemoryEntryIdUtf8Bytes = 1024;
    private const int MaximumMemoryContentUtf8Bytes = 16 * 1024;
    private const int MaximumMemoryEvidenceUtf8Bytes = 4 * 1024;
    private const int MaximumMemoryCatalogEntries = 512;
    private const int MaximumMemoryManagedEntries = 1_000_000;
    private const int MaximumMemoryExportPathUtf8Bytes = 32 * 1024;
    private const int MaximumCredentialProviders = 256;
    private const int MaximumTelegramBotTokenUtf8Bytes = 512;
    private const int MaximumTelegramPairingEntries = 256;
    private const int MaximumTelegramUserIdUtf8Bytes = 256;
    private const int MaximumTelegramUserNameUtf8Bytes = 1024;
    private const int TelegramPairingRequestIdCharacters = 16;
    private static readonly Regex TelegramBotTokenPattern = new(
        @"^\d+:[A-Za-z0-9_-]{30,}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private const int MaximumDeferredSessionActivationEvents = 256;
    private const int MaximumDurableLineageSessions = 2_048;
    private const int MaximumCompressionFocusUtf8Bytes = 4 * 1024;
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private const int MaximumImageFilenameUtf8Bytes = 1024;
    private const int MaximumImagePathUtf8Bytes = 32 * 1024;
    private const int MaximumImageTextUtf8Bytes = 64 * 1024;
    private const int MaximumSubagentIdUtf8Bytes = 1024;
    private const int MaximumSubagentSteerUtf8Bytes = 64 * 1024;
    private const int MaximumVoiceRecordKeyUtf8Bytes = 256;
    private const int MaximumVoiceDetailsUtf8Bytes = 4 * 1024;
    private const int CheckpointHashCharacters = 40;
    private const int MaximumCheckpointTokenUtf8Bytes = 256;
    private const int MaximumCheckpointListEntries = 20;
    private const int MaximumCheckpointTimestampUtf8Bytes = 128;
    private const int MaximumCheckpointMessageUtf8Bytes = 4 * 1024;
    private const int MaximumCheckpointStatUtf8Bytes = 64 * 1024;
    private const int MaximumCheckpointDiffUtf8Bytes = 64 * 1024;
    private const int MaximumCheckpointReceiptTextUtf8Bytes = 4 * 1024;
    private const int MaximumCheckpointOperationIdUtf8Bytes = 128;
    private static readonly TimeSpan CheckpointConfirmationLifetime =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NativeConfigAbortTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PermanentApprovalTransactionTimeout =
        TimeSpan.FromSeconds(75);
    internal static readonly TimeSpan EmergencyInterruptMutationWaitTimeout =
        TimeSpan.FromSeconds(5);
    private const int MaximumImageBase64Characters =
        ((MaximumImageBytes + 2) / 3) * 4;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    // Native103 seals the management handlers and the supported paid routes.
    // Unsupported routes still fail explicitly before dispatch, not as a UI save.
    internal static readonly IReadOnlyList<string> PublishedMethods = AllowedMethods;

    private static readonly HashSet<string> AllowedMethodSet =
        new(AllowedMethods, StringComparer.Ordinal);

    private readonly Func<
        string,
        object?,
        CancellationToken,
        Task<JsonElement>> _requestAsync;
    private readonly Func<LilithRuntimeState> _runtimeState;
    private readonly Func<LilithDailyNoteDiagnostic?> _dailyNoteDiagnostic = static () => null;
    private readonly Func<LilithLettersCoordinator?> _letters = static () => null;
    private readonly Func<(string ProviderId, string ModelId)> _modelIdentity;
    private readonly Func<LilithRuntimeApprovedModelCatalog> _approvedCatalog;
    private readonly Func<
        LilithRuntimeModelAuthorityLease,
        Guid,
        LilithRuntimeApprovedModelCatalog,
        string,
        string,
        bool> _applyNativeModelDefault;
    private readonly LilithRuntimeModelAuthority _modelAuthority;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string, FileAttributes?> _workspacePathAttributes;
    private readonly Action<string> _credentialMutationDispatched;
    private readonly LilithRuntimeSessionSurface _sessionSurface;
    private readonly LilithRuntimeVoiceCaptureArbiter _voiceCaptureArbiter;
    private readonly TimeSpan _permanentApprovalTransactionTimeout;
    private readonly Guid _generationId;
    private readonly string _workspacePath;
    private readonly string _hermesImagesPath;
    private readonly string _hermesHomePath;
    private readonly LilithProfileProvisioningReceipt? _profile;
    private readonly object _sessionLock = new();
    // Covers admission AND outbound publication, not provider work. An event
    // admitted for the old selection cannot overtake the next activation receipt.
    private readonly object _eventProjectionLock = new();
    private readonly HashSet<string> _noteDraftOperations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _noteDraftEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionNativeSurfaceState> _sessionStates =
        new(StringComparer.Ordinal);
    // Receipt-bound Workbench identities, never inferred from callback payloads.
    // This distinguishes delayed inactive builders from a warm resume target.
    private readonly Dictionary<string, string> _workbenchStoredSessionIds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<PendingNativeApproval>> _pendingApprovals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionImageLifecycle> _sessionImages =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionTurnCorrelation> _sessionTurns =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, LilithRuntimeSessionModelObservation>
        _sessionModelObservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckpointWorkspaceAttestationState>
        _checkpointWorkspaceStates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _imageLifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly SemaphoreSlim _nativeConfigGate = new(1, 1);
    private readonly object _routeAuthorityBorrowLock = new();
    private string? _activeSessionId;
    private string? _activeStoredSessionId;
    private LilithGameSessionLineageAttestation? _activeGameLineageAttestation;
    private bool _sessionDeleteQuarantined;
    private VoiceLifecycleState _voiceState = VoiceLifecycleState.Off;
    private string? _voiceSessionId;
    private Guid _voiceCaptureId;
    private bool _voiceRequestInFlight;
    private LilithRuntimeModelAuthorityLease? _voiceRequestModelAuthority;
    private int _voicePhysicalCleanupBorrowers;
    private TaskCompletionSource? _voicePhysicalCleanupBorrowersDrained;
    private long _voiceOperationEpoch;
    private bool _sessionLifecycleInFlight;
    private PendingSessionActivation? _pendingSessionActivation;
    private HermesGatewayEvent? _lastAllowedSessionInfo;
    private bool _sessionInfoProjectionDirty;
    private bool _sessionActivationQuarantined;
    private bool _voiceDisableAwaitingIdle;
    private bool _voiceCleanupAttempted;
    private bool _voiceQuarantineRequiresDrain;
    private VoiceLifecycleState _voiceStopOrigin = VoiceLifecycleState.Off;
    private VoiceLifecycleState _voiceOffOrigin = VoiceLifecycleState.Off;
    private TaskCompletionSource? _voiceIdleSignal;
    private CheckpointRestoreState _checkpointRestoreState =
        CheckpointRestoreState.Ready;
    private HashSet<string> _checkpointCatalog = new(StringComparer.Ordinal);
    private long _checkpointCatalogRevision;
    private PendingCheckpointConfirmation? _checkpointConfirmation;
    private string? _checkpointOperationId;
    private bool _checkpointMutationObserved;
    private bool _checkpointRestoreRequestInFlight;
    private ObservedCheckpointRestoreEvent? _checkpointObservedRestoreEvent;
    private bool _nativeToolsNewSessionRequired;
    private string? _nativeConfigInvalidatedSessionId;
    private string? _nativeConfigMutationInFlightSessionId;
    private bool _nativeConfigQuarantined;
    private long _observedNativeConfigRevision;
    private readonly Dictionary<string, long> _sessionNativeConfigRevisions =
        new(StringComparer.Ordinal);
    private string? _emergencyInterruptSessionId;
    private int _emergencyInterruptReservations;
    private int _emergencyBarrierRoutesInFlight;
    private TaskCompletionSource? _emergencyBarrierDrained;
    private long _sessionModelObservationRevision;
    private PendingCredentialModelRecovery? _pendingCredentialModelRecovery;
    private LilithRuntimeModelAuthorityLease? _activeRouteModelAuthority;
    private string? _activeRouteMethod;
    private int _emergencyRouteAuthorityBorrowers;
    private TaskCompletionSource? _emergencyRouteAuthorityBorrowersDrained;

    internal LilithRuntimeRequestRouter(
        LilithRuntimeService runtime,
        Guid generationId,
        string workspacePath,
        LilithProfileProvisioningReceipt profile,
        Action<string>? credentialMutationDispatched = null,
        LilithRuntimeSessionSurface sessionSurface =
            LilithRuntimeSessionSurface.Workbench)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _requestAsync = runtime.RequestAsync;
        _runtimeState = () => runtime.State;
        _dailyNoteDiagnostic = () => runtime.DailyNoteDiagnostic;
        _letters = () => runtime.Letters;
        _modelIdentity = runtime.EffectiveModelIdentity;
        _approvedCatalog = () => runtime.ApprovedModelCatalog;
        _applyNativeModelDefault = runtime.ApplyAttestedNativeModelDefault;
        _modelAuthority = runtime.ModelAuthority;
        _observedNativeConfigRevision = _modelAuthority.Snapshot()
            .NativeConfigRevision;
        _utcNow = static () => DateTimeOffset.UtcNow;
        _workspacePathAttributes =
            LilithRuntimeWorkspacePathGuard.ReadExistingAttributes;
        _credentialMutationDispatched = credentialMutationDispatched
            ?? (static _ => { });
        _voiceCaptureArbiter = runtime.VoiceCaptureArbiter;
        _permanentApprovalTransactionTimeout =
            PermanentApprovalTransactionTimeout;
        if (!Enum.IsDefined(sessionSurface))
            throw new ArgumentOutOfRangeException(nameof(sessionSurface));
        _sessionSurface = sessionSurface;
        ValidateIdentity(
            generationId,
            workspacePath,
            _workspacePathAttributes);
        _generationId = generationId;
        _workspacePath = CanonicalPath(workspacePath);
        _hermesImagesPath = HermesImagesPath(_workspacePath);
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _hermesHomePath = CanonicalPath(profile.HermesHomePath);
        if (!PathsEqual(
                _hermesHomePath,
                Path.GetDirectoryName(
                    Path.TrimEndingDirectorySeparator(_workspacePath))!))
        {
            throw new ArgumentException(
                "The Runtime profile and workspace Hermes homes must match.",
                nameof(profile));
        }
    }

    internal LilithRuntimeRequestRouter(
        Guid generationId,
        string workspacePath,
        Func<string, object?, CancellationToken, Task<JsonElement>> requestAsync,
        Func<LilithRuntimeState> runtimeState,
        LilithRuntimeApprovedModelCatalog? approvedCatalog = null,
        Func<DateTimeOffset>? utcNow = null,
        Func<string, FileAttributes?>? workspacePathAttributes = null,
        LilithProfileProvisioningReceipt? profile = null,
        Action<string>? credentialMutationDispatched = null,
        LilithRuntimeSessionSurface sessionSurface =
            LilithRuntimeSessionSurface.Workbench,
        LilithRuntimeVoiceCaptureArbiter? voiceCaptureArbiter = null,
        Func<(string ProviderId, string ModelId)>? modelIdentity = null,
        Func<
            LilithRuntimeModelAuthorityLease,
            Guid,
            LilithRuntimeApprovedModelCatalog,
            string,
            string,
            bool>? applyNativeModelDefault = null,
        LilithRuntimeModelAuthority? modelAuthority = null,
        TimeSpan? permanentApprovalTransactionTimeout = null,
        Func<LilithDailyNoteDiagnostic?>? dailyNoteDiagnostic = null)
    {
        _requestAsync = requestAsync
            ?? throw new ArgumentNullException(nameof(requestAsync));
        _dailyNoteDiagnostic = dailyNoteDiagnostic ?? (static () => null);
        _runtimeState = runtimeState
            ?? throw new ArgumentNullException(nameof(runtimeState));
        var catalog = approvedCatalog ?? new LilithRuntimeApprovedModelCatalog(
            "test-provider",
            "model-1",
            new[]
            {
                ("test-provider", (IEnumerable<string>)new[] { "model-1" })
            });
        _modelIdentity = modelIdentity ?? (() => (
            catalog.DefaultProviderId,
            catalog.DefaultModelId));
        _approvedCatalog = () => catalog;
        _modelAuthority = modelAuthority ?? new LilithRuntimeModelAuthority();
        if (modelAuthority is null)
        {
            _modelAuthority.BeginGeneration(generationId);
            if (!_modelAuthority.PublishCatalog(generationId, catalog))
            {
                throw new InvalidOperationException(
                    "The test model authority could not publish its catalog.");
            }
        }
        _observedNativeConfigRevision = _modelAuthority.Snapshot()
            .NativeConfigRevision;
        _applyNativeModelDefault = applyNativeModelDefault
            ?? ((lease, generation, attested, provider, model) =>
            {
                if (generation != generationId
                    || !attested.HasDefault
                    || !string.Equals(
                        attested.DefaultProviderId,
                        provider,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        attested.DefaultModelId,
                        model,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                if (!_modelAuthority.ApplyAttestedDefault(
                        lease,
                        generation,
                        attested,
                        provider,
                        model))
                {
                    return false;
                }
                catalog = attested;
                return true;
            });
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        _workspacePathAttributes = workspacePathAttributes
            ?? LilithRuntimeWorkspacePathGuard.ReadExistingAttributes;
        _credentialMutationDispatched = credentialMutationDispatched
            ?? (static _ => { });
        _voiceCaptureArbiter = voiceCaptureArbiter
            ?? new LilithRuntimeVoiceCaptureArbiter();
        _permanentApprovalTransactionTimeout =
            permanentApprovalTransactionTimeout
            ?? PermanentApprovalTransactionTimeout;
        if (_permanentApprovalTransactionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permanentApprovalTransactionTimeout));
        }
        if (!Enum.IsDefined(sessionSurface))
            throw new ArgumentOutOfRangeException(nameof(sessionSurface));
        _sessionSurface = sessionSurface;
        ValidateIdentity(
            generationId,
            workspacePath,
            _workspacePathAttributes);
        _generationId = generationId;
        _workspacePath = CanonicalPath(workspacePath);
        _hermesImagesPath = HermesImagesPath(_workspacePath);
        _hermesHomePath = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(_workspacePath))
            ?? throw new ArgumentException(
                "The Runtime workspace requires a Hermes home parent.",
                nameof(workspacePath));
        _profile = profile;
    }

    private static void ValidateIdentity(
        Guid generationId,
        string workspacePath,
        Func<string, FileAttributes?> workspacePathAttributes)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException(
                "Runtime generation must not be empty.",
                nameof(generationId));
        if (string.IsNullOrWhiteSpace(workspacePath)
            || !Path.IsPathFullyQualified(workspacePath))
        {
            throw new ArgumentException(
                "An absolute Runtime workspace is required.",
                nameof(workspacePath));
        }
        var canonicalWorkspace = CanonicalPath(workspacePath);
        var root = Path.GetPathRoot(canonicalWorkspace);
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(root)
            || PathsEqual(canonicalWorkspace, root)
            || !string.IsNullOrWhiteSpace(userProfile)
                && PathsEqual(canonicalWorkspace, CanonicalPath(userProfile)))
        {
            throw new ArgumentException(
                "A bounded Runtime workspace below a filesystem root and user profile is required.",
                nameof(workspacePath));
        }
        LilithRuntimeWorkspacePathGuard.EnsureSafe(
            canonicalWorkspace,
            workspacePathAttributes);
    }

    private static string HermesImagesPath(string workspacePath)
    {
        var canonicalWorkspace = CanonicalPath(workspacePath);
        var hermesHome = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(canonicalWorkspace));
        if (string.IsNullOrWhiteSpace(hermesHome))
        {
            throw new ArgumentException(
                "The Runtime workspace must have a Hermes home parent.",
                nameof(workspacePath));
        }
        return Path.GetFullPath(Path.Combine(hermesHome, "images"));
    }

    private static string CanonicalPath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal string? ActiveSessionId
    {
        get
        {
            lock (_sessionLock)
                return _activeSessionId;
        }
    }

    internal string? ActiveStoredSessionId
    {
        get
        {
            lock (_sessionLock)
                return _activeStoredSessionId;
        }
    }

    internal LilithGameSessionLineageAttestation? ActiveGameLineageAttestation
    {
        get
        {
            lock (_sessionLock)
                return _activeGameLineageAttestation;
        }
    }

    internal bool TryGetAttestedSessionModelObservation(
        string sessionId,
        out LilithRuntimeSessionModelObservation observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sessionLock)
        {
            if (string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && _sessionStates.TryGetValue(sessionId, out var state)
                && state == SessionNativeSurfaceState.Attested
                && _sessionModelObservations.TryGetValue(
                    sessionId,
                    out var current))
            {
                observation = current;
                return true;
            }
        }
        observation = null!;
        return false;
    }

    internal LilithGameSessionLineageAttestation RefreshGameSessionLineagePostcondition(
        string sessionId,
        JsonElement terminalPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sessionLock)
        {
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
                || !string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(sessionId, out var state)
                || state != SessionNativeSurfaceState.Attested
                || _activeGameLineageAttestation is not { } previous
                || !string.Equals(
                    _activeStoredSessionId,
                    previous.ResolvedStoredSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The active hidden game session was not eligible for a terminal lineage refresh.");
            }
            if (terminalPayload.ValueKind != JsonValueKind.Object
                || !terminalPayload.TryGetProperty(
                    "stored_session_id",
                    out var storedValue)
                || storedValue.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "Hermes omitted the terminal durable game session identity.");
            }
            var storedSessionId = storedValue.GetString()!;
            ValidateText(
                storedSessionId,
                "stored_session_id",
                LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);

            LilithGameSessionLineageAttestation current;
            try
            {
                current = DecodeGameDurableLineage(
                    previous.RequestedStoredSessionId,
                    storedSessionId,
                    terminalPayload);
            }
            catch (Exception exception) when (exception is
                LilithGameSessionLineageRejectedException
                    or ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException(
                    "Hermes terminal durable game session lineage was rejected.",
                    exception);
            }

            if (current.SessionIds.Count < previous.SessionIds.Count
                || !current.SessionIds.Take(previous.SessionIds.Count)
                    .SequenceEqual(previous.SessionIds, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes terminal durable game session lineage did not extend the admitted chain.");
            }

            _activeStoredSessionId = storedSessionId;
            _activeGameLineageAttestation = current;
            return current;
        }
    }

    internal void QuarantineGameSessionLineagePostcondition(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_sessionLock)
        {
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
                || !string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }
            SetSessionState(sessionId, SessionNativeSurfaceState.Rejected);
            _pendingApprovals.Remove(sessionId);
            _activeSessionId = null;
            _activeStoredSessionId = null;
            _activeGameLineageAttestation = null;
            _lastAllowedSessionInfo = null;
            _sessionInfoProjectionDirty = false;
            InvalidateCheckpointCatalogUnsafe();
            _voiceSessionId = null;
            _voiceState = VoiceLifecycleState.Off;
            _voiceRequestInFlight = false;
            _voiceDisableAwaitingIdle = false;
            _voiceCleanupAttempted = false;
            _voiceQuarantineRequiresDrain = false;
        }
    }

    internal bool IsSessionAttested(string sessionId)
    {
        lock (_sessionLock)
        {
            return _sessionStates.TryGetValue(sessionId, out var state)
                && state == SessionNativeSurfaceState.Attested;
        }
    }

    internal async Task<LilithRuntimeResponse>
        EnableGameSessionApprovalBypassAsync(
            string sessionId,
            LilithRuntimeModelAuthorityLease modelAuthority,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_modelAuthority.OwnsActiveLease(modelAuthority, _generationId))
        {
            throw new InvalidOperationException(
                "The game approval bypass requires an active model-authority lease.");
        }
        var snapshot = modelAuthority.Snapshot;
        if (snapshot.GenerationId != _generationId
            || snapshot.State != LilithRuntimeModelAuthorityState.Ready)
        {
            return Failure(
                "config.set",
                snapshot.State == LilithRuntimeModelAuthorityState.Quarantined
                    ? "runtime.native_config_quarantined"
                    : "runtime.native_surface_attestation_required",
                "The hidden game approval bypass lost Runtime authority.");
        }
        lock (_sessionLock)
        {
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
                || !string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(sessionId, out var state)
                || state != SessionNativeSurfaceState.Attested
                || !_sessionNativeConfigRevisions.TryGetValue(
                    sessionId,
                    out var nativeRevision)
                || nativeRevision != snapshot.NativeConfigRevision)
            {
                return Failure(
                    "config.set",
                    "runtime.native_surface_attestation_required",
                    "The hidden game session is stale or unattested.");
            }
        }

        try
        {
            var result = await _requestAsync(
                "config.set",
                new
                {
                    session_id = sessionId,
                    key = "yolo",
                    value = "on",
                    scope = "session"
                },
                cancellationToken).ConfigureAwait(false);
            return Success("config.set", result);
        }
        catch (HermesRpcException exception) when (exception.Code == 5035)
        {
            QuarantineNativeConfig(modelAuthority);
            return Failure(
                "config.set",
                "runtime.native_config_quarantined",
                "Hermes native config authority failed; Runtime restart and profile recovery are required.",
                exception.Code);
        }
    }

    internal string ImageLifecycleState(string sessionId)
    {
        lock (_sessionLock)
        {
            return _sessionImages.TryGetValue(sessionId, out var lifecycle)
                ? lifecycle.State.ToString()
                : SessionImageLifecycleState.Idle.ToString();
        }
    }

    internal string VoiceLifecycleStateName
    {
        get
        {
            lock (_sessionLock)
                return _voiceState.ToString();
        }
    }

    internal string CheckpointRestoreStateName
    {
        get
        {
            lock (_sessionLock)
                return _checkpointRestoreState.ToString();
        }
    }

    internal void CompleteSessionActivationProjection(
        string method,
        LilithRuntimeResponse response,
        Action<IReadOnlyList<HermesGatewayEvent>> project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(project);
        lock (_eventProjectionLock)
        lock (_sessionLock)
        {
            if (_pendingSessionActivation is not
                { ProjectionPending: true } pending)
            {
                project(Array.Empty<HermesGatewayEvent>());
                return;
            }

            try
            {
                if (!response.Succeeded
                    || !string.Equals(response.Method, method, StringComparison.Ordinal)
                    || !string.Equals(pending.Method, method, StringComparison.Ordinal)
                    || pending.BoundSessionId is null
                    || !string.Equals(
                        ResultSessionId(response.HermesPayload),
                        pending.BoundSessionId,
                        StringComparison.Ordinal)
                    || _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                        && _activeGameLineageAttestation is null
                    || pending.RejectionMessage is not null)
                {
                    throw new InvalidDataException(
                        pending.RejectionMessage
                        ?? "The bound Hermes session activation event could not be projected exactly.");
                }

                var projectedEvents = new List<HermesGatewayEvent>();
                if (pending.BufferedSessionInfo is not null)
                {
                    var projected = CloneGatewayEvent(
                        pending.BufferedSessionInfo);
                    projectedEvents.Add(projected);
                    _lastAllowedSessionInfo = projected;
                    _sessionInfoProjectionDirty = false;
                }
                foreach (var deferred in pending.DeferredEvents)
                {
                    if (!string.IsNullOrWhiteSpace(deferred.SessionId)
                        && !string.Equals(
                            deferred.SessionId,
                            pending.BoundSessionId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (ObserveAndAllowGatewayEventCore(
                            deferred,
                            deferForSessionActivation: false))
                    {
                        projectedEvents.Add(CloneGatewayEvent(deferred));
                    }
                }
                project(projectedEvents);
            }
            catch
            {
                _sessionActivationQuarantined = true;
                throw;
            }
            finally
            {
                _pendingSessionActivation = null;
                _sessionLifecycleInFlight = false;
            }
        }
    }

    internal bool ObserveAndAllowGatewayEvent(HermesGatewayEvent gatewayEvent)
        => ObserveAndAllowGatewayEventCore(
            gatewayEvent,
            deferForSessionActivation: true);

    internal void ProjectGatewayEvent(
        HermesGatewayEvent gatewayEvent,
        Action<HermesGatewayEvent> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_eventProjectionLock)
        {
            if (ObserveAndAllowGatewayEvent(gatewayEvent))
                project(gatewayEvent);
        }
    }

    private bool ObserveAndAllowGatewayEventCore(
        HermesGatewayEvent gatewayEvent,
        bool deferForSessionActivation)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        // Owned host diagnostics never establish a turn, enter UI projection,
        // or participate in session activation. The game host consumes its
        // exact leased receipt independently of conversational event authority.
        if (gatewayEvent.Type == "game.context_diagnostic")
            return false;
        string turnId;
        try
        {
            turnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
                gatewayEvent.Type,
                gatewayEvent.Parameters);
            ValidateGatewayMessagePayload(
                gatewayEvent.Type,
                gatewayEvent.Payload);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        if (deferForSessionActivation
            && !string.Equals(
                gatewayEvent.Type,
                "session.info",
                StringComparison.Ordinal)
            && TryDeferSessionActivationEvent(gatewayEvent))
        {
            return false;
        }
        if (turnId.Length != 0
            && !AdmitGatewayTurnEvent(gatewayEvent, turnId))
        {
            return false;
        }
        if (gatewayEvent.Type is "voice.status" or "voice.transcript")
        {
            return ObserveAllowedGatewayEvent(
                gatewayEvent,
                turnId,
                ObserveVoiceGatewayEvent(gatewayEvent));
        }
        if (gatewayEvent.Type.StartsWith("voice.", StringComparison.Ordinal))
        {
            QuarantineVoice();
            return false;
        }
        if (string.Equals(
                gatewayEvent.Type,
                "rollback.restored",
                StringComparison.Ordinal))
        {
            return ObserveAllowedGatewayEvent(
                gatewayEvent,
                turnId,
                ObserveRollbackRestoredGatewayEvent(gatewayEvent));
        }
        if (gatewayEvent.Type.StartsWith("rollback.", StringComparison.Ordinal))
        {
            QuarantineCheckpointRestore();
            return false;
        }
        if (string.Equals(
                gatewayEvent.Type,
                "approval.request",
                StringComparison.Ordinal))
        {
            return ObserveAllowedGatewayEvent(
                gatewayEvent,
                turnId,
                ObserveApprovalRequest(gatewayEvent));
        }
        if (gatewayEvent.Type.StartsWith("tool.", StringComparison.Ordinal)
            && !HermesNativeSessionInfoSurface.IsBoundedToolEvent(gatewayEvent))
        {
            ObserveGatewayEvent(gatewayEvent);
            return false;
        }
        if (string.Equals(
                gatewayEvent.Type,
                "session.info",
                StringComparison.Ordinal))
        {
            return ObserveSessionInfoGatewayEvent(gatewayEvent);
        }
        // Ordinary callbacks from inactive warm/game sessions must not reach the
        // selected UI. Keep the specialized turn/voice/rollback/approval checks
        // above: malformed authority events still quarantine their own surfaces.
        lock (_sessionLock)
        {
            var global = gatewayEvent.Type == "sessions.changed"
                && gatewayEvent.SessionId.Length == 0;
            if (!global && (string.IsNullOrEmpty(_activeSessionId)
                || !string.Equals(_activeSessionId, gatewayEvent.SessionId, StringComparison.Ordinal)))
                return false;
        }
        ObserveGatewayEvent(gatewayEvent);
        return ObserveAllowedGatewayEvent(gatewayEvent, turnId, allowed: true);
    }

    private bool ObserveAllowedGatewayEvent(
        HermesGatewayEvent gatewayEvent,
        string turnId,
        bool allowed)
    {
        if (!allowed || turnId.Length == 0)
            return allowed;

        lock (_sessionLock)
        {
            if (string.Equals(
                    _activeSessionId,
                    gatewayEvent.SessionId,
                    StringComparison.Ordinal))
            {
                _sessionInfoProjectionDirty = true;
            }
        }
        return true;
    }

    private static void ValidateGatewayMessagePayload(
        string type,
        JsonElement payload)
    {
        if (string.Equals(
                type,
                HermesTurnCorrelationContract.CronCompletionEventType,
                StringComparison.Ordinal))
        {
            var cronValues = ReadExactObject(
                payload,
                new[] { "schema", "success", "text" });
            if (cronValues.Count != 3
                || !string.Equals(
                    RequiredGatewayMessageText(
                        cronValues,
                        "schema",
                        allowEmpty: false),
                    HermesTurnCorrelationContract.CronCompletionSchema,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "cron.complete payload schema was malformed.");
            }
            _ = RequireGatewayBoolean(cronValues, "success");
            _ = RequiredGatewayMessageText(
                cronValues,
                "text",
                allowEmpty: false);
            return;
        }

        if (type is not ("message.user" or "message.start" or "message.delta"
            or "message.interim" or "message.complete"))
        {
            return;
        }

        var allowed = type switch
        {
            "message.user" => new[] { "message_id", "text", "durable" },
            "message.start" => new[] { "message_id" },
            "message.delta" => new[] { "message_id", "text", "rendered" },
            "message.interim" => new[]
            {
                "message_id", "text", "already_streamed"
            },
            _ => new[]
            {
                "message_id", "text", "status", "rendered", "usage",
                "reasoning", "warning", "response_previewed", "billing",
                "failure_reason", "error", "recoverable", "partial",
                "budget_error", "budget_constraints",
                "stored_session_id", "durable_lineage", "durable",
                "native_assistant_extension_status",
                "foreground_proactive_status", "game_event_receipt"
            }
        };
        var values = ReadExactObject(payload, allowed);
        var messageId = RequiredGatewayMessageText(
            values,
            "message_id",
            allowEmpty: false);
        _ = HermesTurnCorrelationContract.RequireIdentity(
            messageId,
            type + " message_id");

        if (type == "message.start")
        {
            RequireExactGatewayProperties(values, "message_id");
            return;
        }

        _ = RequiredGatewayMessageText(
            values,
            "text",
            allowEmpty: type != "message.user");
        if (values.TryGetValue("durable", out var durable))
            ValidateGatewayDurableMessageReceipt(durable);
        if (type == "message.user")
        {
            var expectedPropertyCount = values.ContainsKey("durable") ? 3 : 2;
            if (values.Count != expectedPropertyCount
                || !values.ContainsKey("message_id")
                || !values.ContainsKey("text"))
            {
                throw new InvalidDataException(
                    "message.user durable receipt union was malformed.");
            }
            return;
        }

        if (type == "message.interim")
        {
            RequireExactGatewayProperties(
                values,
                "message_id",
                "text",
                "already_streamed");
            _ = RequireGatewayBoolean(values, "already_streamed");
            return;
        }

        if (values.TryGetValue("rendered", out var rendered))
        {
            _ = ValidateGatewayMessageTextValue(
                rendered,
                "rendered",
                allowEmpty: true);
        }
        if (type == "message.delta")
        {
            if (!values.ContainsKey("message_id") || !values.ContainsKey("text"))
            {
                throw new InvalidDataException(
                    "message.delta omitted a required property.");
            }
            return;
        }

        if (!values.ContainsKey("message_id")
            || !values.ContainsKey("text")
            || !values.ContainsKey("status"))
        {
            throw new InvalidDataException(
                "message.complete omitted a required property.");
        }
        if (values.TryGetValue("usage", out var usage)
            && usage.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("message.complete usage was malformed.");
        }
        if (values.TryGetValue("reasoning", out var reasoning))
        {
            _ = ValidateGatewayMessageTextValue(
                reasoning,
                "reasoning",
                allowEmpty: true);
        }
        if (values.TryGetValue("warning", out var warning))
        {
            _ = ValidateGatewayMessageTextValue(
                warning,
                "warning",
                allowEmpty: true);
        }
        if (values.TryGetValue("response_previewed", out _))
            _ = RequireGatewayBoolean(values, "response_previewed");
        if (values.TryGetValue("billing", out var billing)
            && billing.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("message.complete billing was malformed.");
        }
        if (values.TryGetValue("failure_reason", out var failureReason)
            && failureReason.ValueKind is not (
                JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException(
                "message.complete failure reason was malformed.");
        }
        if (failureReason.ValueKind == JsonValueKind.String)
        {
            _ = ValidateGatewayMessageTextValue(
                failureReason,
                "failure_reason",
                allowEmpty: true);
        }
        var hasStoredSessionId = values.TryGetValue(
            "stored_session_id",
            out var terminalStoredSessionId);
        var hasDurableLineage = values.TryGetValue(
            "durable_lineage",
            out var terminalDurableLineage);
        if (hasStoredSessionId != hasDurableLineage)
        {
            throw new InvalidDataException(
                "message.complete durable lineage envelope was incomplete.");
        }
        if (hasStoredSessionId)
        {
            if (terminalStoredSessionId.ValueKind != JsonValueKind.String
                || terminalDurableLineage.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "message.complete durable lineage envelope was malformed.");
            }
            ValidateText(
                terminalStoredSessionId.GetString()!,
                "stored_session_id",
                LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
        }

        var status = RequiredGatewayMessageText(
            values,
            "status",
            allowEmpty: false);
        if (status is not ("complete" or "interrupted" or "error"))
        {
            throw new InvalidDataException(
                "message.complete status was not recognized.");
        }
        if (values.TryGetValue("game_event_receipt", out var gameEventReceipt))
        {
            _ = LilithGameBridgeContracts.LilithMiniGameEventReceipt.Read(gameEventReceipt);
            if (status != "complete" || string.IsNullOrWhiteSpace(values["text"].GetString())
                || !values.ContainsKey("durable"))
                throw new InvalidDataException("Game events require a completed durable conversation turn.");
        }
        // The native publisher adds these bounded disclosures to level-six
        // terminals. They describe retained constraints, never grant authority.
        _ = HermesUsageBudgetTerminalMetadata.DecodeConstraints(payload);
        _ = HermesUsageBudgetTerminalMetadata.DecodeError(payload, isError: status == "error");
        if (values.TryGetValue(
                "native_assistant_extension_status",
                out _))
        {
            var extensionStatus = RequiredGatewayMessageText(
                values,
                "native_assistant_extension_status",
                allowEmpty: false);
            var text = values["text"].GetString()!;
            var durableMatchesNativeExtension =
                values.TryGetValue("durable", out var extensionDurable)
                && extensionDurable.ValueKind == JsonValueKind.Object
                && extensionDurable.TryGetProperty(
                    "turn_origin",
                    out var turnOrigin)
                && turnOrigin.ValueKind == JsonValueKind.String
                && string.Equals(
                    turnOrigin.GetString(),
                    "synthetic",
                    StringComparison.Ordinal)
                && extensionDurable.TryGetProperty(
                    "turn_origin_kind",
                    out var turnOriginKind)
                && turnOriginKind.ValueKind == JsonValueKind.String
                && string.Equals(
                    turnOriginKind.GetString(),
                    "lilith-native-extension",
                    StringComparison.Ordinal)
                && extensionDurable.TryGetProperty(
                    "conversation_activity",
                    out var conversationActivity)
                && conversationActivity.ValueKind == JsonValueKind.True;
            var validExtensionTerminal = extensionStatus switch
            {
                "completed" => status == "complete"
                    && !string.IsNullOrWhiteSpace(text)
                    && durableMatchesNativeExtension,
                "silent" => status == "complete" && text.Length == 0,
                "abandoned" => status == "interrupted" && text.Length == 0,
                _ => false
            };
            if (!validExtensionTerminal)
            {
                throw new InvalidDataException(
                    "message.complete native assistant extension terminal was malformed.");
            }
        }
        if (values.TryGetValue("foreground_proactive_status", out _))
        {
            if (values.ContainsKey("native_assistant_extension_status"))
            {
                throw new InvalidDataException(
                    "message.complete synthetic assistant terminal kind was ambiguous.");
            }
            var proactiveStatus = RequiredGatewayMessageText(
                values,
                "foreground_proactive_status",
                allowEmpty: false);
            var text = values["text"].GetString()!;
            var durableMatchesForegroundProactive =
                values.TryGetValue("durable", out var proactiveDurable)
                && proactiveDurable.ValueKind == JsonValueKind.Object
                && proactiveDurable.TryGetProperty(
                    "turn_origin",
                    out var turnOrigin)
                && turnOrigin.ValueKind == JsonValueKind.String
                && string.Equals(
                    turnOrigin.GetString(),
                    "synthetic",
                    StringComparison.Ordinal)
                && proactiveDurable.TryGetProperty(
                    "turn_origin_kind",
                    out var turnOriginKind)
                && turnOriginKind.ValueKind == JsonValueKind.String
                && string.Equals(
                    turnOriginKind.GetString(),
                    "lilith-foreground-proactive",
                    StringComparison.Ordinal)
                && proactiveDurable.TryGetProperty(
                    "conversation_activity",
                    out var conversationActivity)
                && conversationActivity.ValueKind == JsonValueKind.True;
            var validProactiveTerminal = proactiveStatus switch
            {
                "completed" => status == "complete"
                    && !string.IsNullOrWhiteSpace(text)
                    && durableMatchesForegroundProactive,
                "silent" => status == "complete" && text.Length == 0,
                "abandoned" => status == "interrupted" && text.Length == 0,
                _ => false
            };
            if (!validProactiveTerminal)
            {
                throw new InvalidDataException(
                    "message.complete foreground proactive terminal was malformed.");
            }
        }
        if (status == "error")
        {
            if (values.ContainsKey("response_previewed"))
            {
                throw new InvalidDataException(
                    "message.complete error cannot claim a previewed response.");
            }
            _ = RequiredGatewayMessageText(
                values,
                "error",
                allowEmpty: false);
            _ = RequireGatewayBoolean(values, "recoverable");
            if (values.TryGetValue("partial", out _))
            {
                if (!RequireGatewayBoolean(values, "partial"))
                {
                    throw new InvalidDataException(
                        "message.complete partial must be true when present.");
                }
                if (values["text"].GetString()!.Length == 0)
                {
                    throw new InvalidDataException(
                        "message.complete partial text was empty.");
                }
            }
            return;
        }

        if (values.ContainsKey("error")
            || values.ContainsKey("recoverable")
            || values.ContainsKey("partial"))
        {
            throw new InvalidDataException(
                "message.complete terminal union was malformed.");
        }
    }

    private static void RequireExactGatewayProperties(
        IReadOnlyDictionary<string, JsonElement> values,
        params string[] required)
    {
        if (values.Count != required.Length
            || required.Any(property => !values.ContainsKey(property)))
        {
            throw new InvalidDataException(
                "Hermes message payload was not exact.");
        }
    }

    private static string RequiredGatewayMessageText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException($"Hermes message omitted '{name}'.");
        return ValidateGatewayMessageTextValue(value, name, allowEmpty);
    }

    private static string ValidateGatewayMessageTextValue(
        JsonElement value,
        string name,
        bool allowEmpty)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Hermes message '{name}' was malformed.");
        var text = value.GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text)
            || text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > MaximumPromptUtf8Bytes)
        {
            throw new InvalidDataException($"Hermes message '{name}' was rejected.");
        }
        return text;
    }

    private static bool RequireGatewayBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Hermes message '{name}' was malformed.");
        }
        return value.GetBoolean();
    }

    private static void ValidateGatewayDurableMessageReceipt(
        JsonElement receipt)
    {
        var values = ReadExactObject(receipt, new[]
        {
            "source",
            "row_id",
            "timestamp",
            "conversation_activity",
            "turn_origin",
            "turn_origin_kind"
        });
        if (values.Count != 6
            || !values.TryGetValue("source", out var source)
            || source.ValueKind != JsonValueKind.String
            || !string.Equals(source.GetString(), "state.db", StringComparison.Ordinal)
            || !values.TryGetValue("row_id", out var rowId)
            || rowId.ValueKind != JsonValueKind.Number
            || !rowId.TryGetInt64(out var parsedRowId)
            || parsedRowId <= 0
            || !values.TryGetValue("timestamp", out var timestamp)
            || timestamp.ValueKind != JsonValueKind.Number
            || !timestamp.TryGetDouble(out var parsedTimestamp)
            || !double.IsFinite(parsedTimestamp)
            || parsedTimestamp <= 0
            || parsedTimestamp > 253402300799.999d
            || !values.TryGetValue("conversation_activity", out var activity)
            || activity.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !values.TryGetValue("turn_origin", out var turnOrigin)
            || turnOrigin.ValueKind != JsonValueKind.String
            || turnOrigin.GetString() is not ("user" or "synthetic")
            || !values.TryGetValue("turn_origin_kind", out var turnOriginKind)
            || turnOriginKind.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes durable message receipt was malformed.");
        }
        _ = ValidateGatewayMessageTextValue(
            turnOriginKind,
            "durable.turn_origin_kind",
            allowEmpty: true);
    }

    private bool AdmitGatewayTurnEvent(
        HermesGatewayEvent gatewayEvent,
        string turnId)
    {
        if (string.IsNullOrWhiteSpace(gatewayEvent.SessionId))
            return false;
        string userMessageId = string.Empty;
        string assistantMessageId = string.Empty;
        var primaryAssistantEvent = gatewayEvent.Type is
            "message.start" or "message.delta" or "message.complete";
        var interimAssistantEvent = string.Equals(
            gatewayEvent.Type,
            "message.interim",
            StringComparison.Ordinal);
        if (string.Equals(gatewayEvent.Type, "message.user", StringComparison.Ordinal))
        {
            var payload = gatewayEvent.Payload;
            if (payload.ValueKind != JsonValueKind.Object)
                return false;
            var properties = payload.EnumerateObject().ToArray();
            var hasDurableReceipt = payload.TryGetProperty("durable", out _);
            var expectedPropertyCount = hasDurableReceipt ? 3 : 2;
            if (properties.Length != expectedPropertyCount
                || properties.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count()
                    != expectedPropertyCount
                || !payload.TryGetProperty("message_id", out var messageId)
                || messageId.ValueKind != JsonValueKind.String
                || !payload.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            try
            {
                userMessageId = HermesTurnCorrelationContract.RequireIdentity(
                    messageId.GetString(),
                    "message.user message_id");
            }
            catch (InvalidDataException)
            {
                return false;
            }
            if (string.Equals(turnId, userMessageId, StringComparison.Ordinal))
                return false;
        }
        else if (primaryAssistantEvent || interimAssistantEvent)
        {
            var payload = gatewayEvent.Payload;
            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("message_id", out var messageId)
                || messageId.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            try
            {
                assistantMessageId = HermesTurnCorrelationContract.RequireIdentity(
                    messageId.GetString(),
                    gatewayEvent.Type + " message_id");
            }
            catch (InvalidDataException)
            {
                return false;
            }
            if (string.Equals(turnId, assistantMessageId, StringComparison.Ordinal))
                return false;
        }

        lock (_sessionLock)
        {
            if (!_sessionTurns.TryGetValue(gatewayEvent.SessionId, out var turn))
                return false;
            if (turn.AwaitingNativeAssistantContinuationReceipt
                && userMessageId.Length != 0)
            {
                return false;
            }
            var knownTurnIds = new HashSet<string>(
                turn.KnownTurnIds,
                StringComparer.Ordinal);
            var claimedUserMessageIds = new Dictionary<string, string>(
                turn.ClaimedUserMessageIds,
                StringComparer.Ordinal);
            var claimedAssistantMessageIds = new Dictionary<string, string>(
                turn.ClaimedAssistantMessageIds,
                StringComparer.Ordinal);
            var claimedAuxiliaryAssistantMessageIds =
                new HashSet<(string TurnId, string MessageId)>(
                    turn.ClaimedAuxiliaryAssistantMessageIds);
            var claimedAuxiliaryAssistantClaims =
                new Dictionary<
                    (string TurnId, string MessageId),
                    AuxiliaryAssistantClaim>(
                    turn.ClaimedAuxiliaryAssistantClaims);
            var completedTurnIds = new HashSet<string>(
                turn.CompletedTurnIds,
                StringComparer.Ordinal);
            var activeAssistantMessageIds = new Dictionary<string, string>(
                turn.ActiveAssistantMessageIds,
                StringComparer.Ordinal);
            var claimedInterimAssistantMessageIds =
                new HashSet<(string TurnId, string MessageId)>(
                    turn.ClaimedInterimAssistantMessageIds);
            var lastInterimAssistantMessageIds = new Dictionary<string, string>(
                turn.LastInterimAssistantMessageIds,
                StringComparer.Ordinal);
            var pendingTurnId = turn.PendingTurnId;
            var pendingUserMessageId = turn.PendingUserMessageId;
            var pendingAssistantMessageId = turn.PendingAssistantMessageId;
            var awaitingQueuedTurn = turn.AwaitingQueuedTurn;
            var claimedQueuedTurn = false;

            if (completedTurnIds.Contains(turnId)
                && gatewayEvent.Type is "message.user" or "message.start"
                    or "message.delta" or "message.interim"
                    or "message.complete")
            {
                return false;
            }

            if (!knownTurnIds.Contains(turnId))
            {
                if (!turn.AwaitingReceipt && !awaitingQueuedTurn)
                    return false;
                if (pendingTurnId.Length != 0
                    && !string.Equals(
                        pendingTurnId,
                        turnId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                claimedQueuedTurn = !turn.AwaitingReceipt && awaitingQueuedTurn;
                pendingTurnId = turnId;
                knownTurnIds.Add(turnId);
            }
            if (userMessageId.Length != 0)
            {
                if (claimedAuxiliaryAssistantMessageIds.Contains(
                        (turnId, userMessageId))
                    || (claimedAssistantMessageIds.TryGetValue(
                        turnId,
                        out var claimedAssistantMessageId)
                    && string.Equals(
                        claimedAssistantMessageId,
                        userMessageId,
                        StringComparison.Ordinal)))
                {
                    return false;
                }
                if (claimedUserMessageIds.TryGetValue(turnId, out var expected)
                    && !string.Equals(expected, userMessageId, StringComparison.Ordinal))
                {
                    return false;
                }
                claimedUserMessageIds[turnId] = userMessageId;
                if (turn.AwaitingReceipt
                    && string.Equals(
                        pendingTurnId,
                        turnId,
                        StringComparison.Ordinal))
                {
                    pendingUserMessageId = userMessageId;
                }
            }
            if (assistantMessageId.Length != 0)
            {
                if (claimedUserMessageIds.TryGetValue(
                        turnId,
                        out var claimedUserMessageId)
                    && string.Equals(
                        claimedUserMessageId,
                        assistantMessageId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                if (primaryAssistantEvent)
                {
                    if (claimedAuxiliaryAssistantMessageIds.Contains(
                            (turnId, assistantMessageId))
                        || (claimedAssistantMessageIds.TryGetValue(
                            turnId,
                            out var expected)
                        && !string.Equals(
                            expected,
                            assistantMessageId,
                            StringComparison.Ordinal)))
                    {
                        return false;
                    }
                    claimedAssistantMessageIds[turnId] = assistantMessageId;
                    if (gatewayEvent.Type is "message.start" or "message.delta")
                    {
                        if (activeAssistantMessageIds.TryGetValue(
                                turnId,
                                out var activeMessageId)
                            && !string.Equals(
                                activeMessageId,
                                assistantMessageId,
                                StringComparison.Ordinal))
                        {
                            return false;
                        }
                        activeAssistantMessageIds[turnId] = assistantMessageId;
                    }
                    if (turn.AwaitingReceipt
                        && string.Equals(
                            pendingTurnId,
                            turnId,
                            StringComparison.Ordinal))
                    {
                        pendingAssistantMessageId = assistantMessageId;
                    }
                }
                else if ((claimedAssistantMessageIds.TryGetValue(
                              turnId,
                              out var primaryAssistantMessageId)
                          && string.Equals(
                              primaryAssistantMessageId,
                              assistantMessageId,
                              StringComparison.Ordinal))
                    || !claimedAuxiliaryAssistantMessageIds.Add(
                        (turnId, assistantMessageId)))
                {
                    return false;
                }
                else
                {
                    var alreadyStreamed = gatewayEvent.Payload
                        .GetProperty("already_streamed")
                        .GetBoolean();
                    if (alreadyStreamed
                        && (!claimedAssistantMessageIds.TryGetValue(
                                turnId,
                                out var primaryMessageId)
                            || !activeAssistantMessageIds.TryGetValue(
                                turnId,
                                out var activeMessageId)
                            || !string.Equals(
                                primaryMessageId,
                                activeMessageId,
                                StringComparison.Ordinal)))
                    {
                        return false;
                    }
                    if (alreadyStreamed)
                        activeAssistantMessageIds.Remove(turnId);
                    claimedInterimAssistantMessageIds.Add(
                        (turnId, assistantMessageId));
                    lastInterimAssistantMessageIds[turnId] = assistantMessageId;
                    claimedAuxiliaryAssistantClaims[
                        (turnId, assistantMessageId)] = new AuxiliaryAssistantClaim(
                            "interim",
                            gatewayEvent.Payload.GetProperty("text").GetString()!);
                }
            }
            if (claimedQueuedTurn)
            {
                awaitingQueuedTurn = false;
                pendingTurnId = string.Empty;
                pendingUserMessageId = string.Empty;
                pendingAssistantMessageId = string.Empty;
            }
            if (string.Equals(
                    gatewayEvent.Type,
                    "message.complete",
                    StringComparison.Ordinal))
            {
                if (gatewayEvent.Payload.TryGetProperty(
                        "response_previewed",
                        out var responsePreviewed)
                    && responsePreviewed.GetBoolean()
                    && (!lastInterimAssistantMessageIds.TryGetValue(
                            turnId,
                            out var previewMessageId)
                        || !claimedInterimAssistantMessageIds.Contains(
                            (turnId, previewMessageId))))
                {
                    return false;
                }
                activeAssistantMessageIds.Remove(turnId);
                completedTurnIds.Add(turnId);
            }

            turn.KnownTurnIds.Clear();
            turn.KnownTurnIds.UnionWith(knownTurnIds);
            turn.ClaimedUserMessageIds.Clear();
            foreach (var claim in claimedUserMessageIds)
                turn.ClaimedUserMessageIds.Add(claim.Key, claim.Value);
            turn.ClaimedAssistantMessageIds.Clear();
            foreach (var claim in claimedAssistantMessageIds)
                turn.ClaimedAssistantMessageIds.Add(claim.Key, claim.Value);
            turn.ClaimedAuxiliaryAssistantMessageIds.Clear();
            turn.ClaimedAuxiliaryAssistantMessageIds.UnionWith(
                claimedAuxiliaryAssistantMessageIds);
            turn.ClaimedAuxiliaryAssistantClaims.Clear();
            foreach (var claim in claimedAuxiliaryAssistantClaims)
                turn.ClaimedAuxiliaryAssistantClaims.Add(claim.Key, claim.Value);
            turn.CompletedTurnIds.Clear();
            turn.CompletedTurnIds.UnionWith(completedTurnIds);
            turn.ActiveAssistantMessageIds.Clear();
            foreach (var claim in activeAssistantMessageIds)
                turn.ActiveAssistantMessageIds.Add(claim.Key, claim.Value);
            turn.ClaimedInterimAssistantMessageIds.Clear();
            turn.ClaimedInterimAssistantMessageIds.UnionWith(
                claimedInterimAssistantMessageIds);
            turn.LastInterimAssistantMessageIds.Clear();
            foreach (var claim in lastInterimAssistantMessageIds)
                turn.LastInterimAssistantMessageIds.Add(claim.Key, claim.Value);
            turn.PendingTurnId = pendingTurnId;
            turn.PendingUserMessageId = pendingUserMessageId;
            turn.PendingAssistantMessageId = pendingAssistantMessageId;
            turn.AwaitingQueuedTurn = awaitingQueuedTurn;
            return true;
        }
    }

    internal async Task HandleClientDisconnectedAsync(
        CancellationToken cancellationToken)
    {
        string? sessionId = null;
        TaskCompletionSource? waitOnlySignal = null;
        lock (_sessionLock)
        {
            if (_voiceState == VoiceLifecycleState.Draining)
            {
                _voiceIdleSignal ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                waitOnlySignal = _voiceIdleSignal;
            }

            var requiresCleanup = _voiceState is
                    VoiceLifecycleState.Enabling
                    or VoiceLifecycleState.Starting
                    or VoiceLifecycleState.Recording
                    or VoiceLifecycleState.Stopping
                    or VoiceLifecycleState.Transcribing
                    or VoiceLifecycleState.Quarantined;
            if (waitOnlySignal is null
                && (!requiresCleanup
                || _voiceCleanupAttempted
                || _voiceRequestInFlight
                || _voiceSessionId is null))
            {
                return;
            }

            if (waitOnlySignal is not null)
                sessionId = null;
            else
            {
                sessionId = _voiceSessionId;
                _voiceCleanupAttempted = true;
                _voiceRequestInFlight = true;
                _voiceOffOrigin = _voiceState;
                _voiceDisableAwaitingIdle = _voiceQuarantineRequiresDrain
                    || _voiceState is VoiceLifecycleState.Enabling
                        or VoiceLifecycleState.Starting
                        or VoiceLifecycleState.Recording
                        or VoiceLifecycleState.Stopping
                        or VoiceLifecycleState.Transcribing;
                _voiceState = VoiceLifecycleState.Disabling;
            }
        }

        if (waitOnlySignal is not null)
        {
            await AwaitVoiceIdleOrQuarantineAsync(
                waitOnlySignal,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        LilithRuntimeVoiceCaptureOwner? cleanupOwner = null;
        if (_voiceCaptureArbiter.TryGetOwner(out var admittedOwner, out _)
            && admittedOwner.GenerationId == _generationId
            && admittedOwner.Surface == _sessionSurface
            && string.Equals(
                admittedOwner.SessionId,
                sessionId,
                StringComparison.Ordinal))
        {
            cleanupOwner = admittedOwner;
        }

        try
        {
            var parameters = JsonSerializer.SerializeToElement(
                new { action = "off" });
            var result = await _requestAsync(
                "voice.toggle",
                parameters,
                cancellationToken).ConfigureAwait(false);
            var disposition = ValidateVoiceResult(
                "voice.toggle",
                "off",
                result);
            CompleteVoiceRequest(
                "voice.toggle",
                "off",
                sessionId!,
                disposition);
            CompleteVoiceOwnerRequest(
                "voice.toggle",
                "off",
                disposition,
                cleanupOwner);
        }
        catch (HermesRequestNotSentException)
        {
            RestoreVoiceAfterNotSent("voice.toggle", "off");
            RestoreVoiceOwnerAfterNotSent(
                "voice.toggle",
                "off",
                cleanupOwner);
        }
        catch
        {
            QuarantineVoice();
        }
        finally
        {
            lock (_sessionLock)
                _voiceRequestInFlight = false;
        }

        TaskCompletionSource? idleSignal = null;
        lock (_sessionLock)
        {
            if (_voiceState == VoiceLifecycleState.Draining)
            {
                _voiceIdleSignal ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                idleSignal = _voiceIdleSignal;
            }
        }
        if (idleSignal is null)
            return;

        await AwaitVoiceIdleOrQuarantineAsync(
            idleSignal,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AwaitVoiceIdleOrQuarantineAsync(
        TaskCompletionSource idleSignal,
        CancellationToken cancellationToken)
    {
        try
        {
            await idleSignal.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_sessionLock)
            {
                if (ReferenceEquals(_voiceIdleSignal, idleSignal))
                    _voiceIdleSignal = null;
                if (_voiceState == VoiceLifecycleState.Draining)
                    QuarantineVoiceUnsafe();
            }
        }
    }

    private bool IsActiveAttestedSession(string sessionId)
    {
        lock (_sessionLock)
        {
            return string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && _sessionStates.TryGetValue(sessionId, out var state)
                && state == SessionNativeSurfaceState.Attested;
        }
    }

    private bool IsActiveNativeToolsCatalogSession(string sessionId)
    {
        lock (_sessionLock)
        {
            return string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && _sessionStates.TryGetValue(sessionId, out var state)
                && state is SessionNativeSurfaceState.Attested
                    or SessionNativeSurfaceState.Pending;
        }
    }

    private bool IsActiveCheckpointWorkspaceAttested(string sessionId)
    {
        lock (_sessionLock)
        {
            return string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && _sessionStates.TryGetValue(sessionId, out var surface)
                && surface == SessionNativeSurfaceState.Attested
                && _checkpointWorkspaceStates.TryGetValue(
                    sessionId,
                    out var workspace)
                && workspace == CheckpointWorkspaceAttestationState.Attested;
        }
    }

    private bool TryEnsureWorkspacePathSafe()
    {
        try
        {
            LilithRuntimeWorkspacePathGuard.EnsureSafe(
                _workspacePath,
                _workspacePathAttributes);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            lock (_sessionLock)
            {
                if (_activeSessionId is not null)
                {
                    _sessionStates[_activeSessionId] =
                        SessionNativeSurfaceState.Rejected;
                    _checkpointWorkspaceStates[_activeSessionId] =
                        CheckpointWorkspaceAttestationState.Rejected;
                }
                if (_checkpointRestoreRequestInFlight)
                    _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                InvalidateCheckpointCatalogUnsafe();
            }
            return false;
        }
    }

    private bool ObserveSessionInfoGatewayEvent(
        HermesGatewayEvent gatewayEvent)
    {
        // Snapshot authority before taking the session lock. A concurrent
        // native-config publication is reconciled by Apply's monotonic
        // revision check while it commits the session state.
        var observedNativeConfigRevision = _modelAuthority.Snapshot()
            .NativeConfigRevision;
        lock (_sessionLock)
        {
            if (_pendingSessionActivation is
                    { ProjectionPending: true } pendingProjection)
            {
                if (pendingProjection.RejectionMessage is not null)
                    return false;
                if (!string.Equals(
                        pendingProjection.BoundSessionId,
                        gatewayEvent.SessionId,
                        StringComparison.Ordinal))
                {
                    // The receipt has already bound an exact session. A callback
                    // from another live session is irrelevant, not corrupt authority.
                    return false;
                }
                if (HermesNativeSessionInfoSurface.Inspect(
                        gatewayEvent.Payload).Disposition
                    == HermesNativeSessionInfoDisposition.Invalid)
                {
                    pendingProjection.RejectionMessage =
                        "Hermes emitted malformed session.info before the bound activation response was projected.";
                    _sessionActivationQuarantined = true;
                    return false;
                }
                if (pendingProjection.BufferedSessionInfo is null)
                {
                    pendingProjection.BufferedSessionInfo =
                        CloneGatewayEvent(gatewayEvent);
                    ApplyAuthoritativeSessionInfo(
                        gatewayEvent.SessionId,
                        gatewayEvent.Payload,
                        observedNativeConfigRevision);
                    return false;
                }
                if (SameSessionInfoEvent(
                        pendingProjection.BufferedSessionInfo,
                        gatewayEvent))
                {
                    return false;
                }

                pendingProjection.RejectionMessage =
                    "Hermes emitted a conflicting session.info event before the bound activation event was projected.";
                return false;
            }

            if (!string.Equals(
                    _activeSessionId,
                    gatewayEvent.SessionId,
                    StringComparison.Ordinal))
            {
                if (_pendingSessionActivation is null)
                    return false;

                if (_sessionSurface == LilithRuntimeSessionSurface.Workbench
                    && _workbenchStoredSessionIds.TryGetValue(
                        gatewayEvent.SessionId, out var knownStoredSessionId)
                    && (_pendingSessionActivation.Method == "session.create"
                        || _pendingSessionActivation.RequestedStoredSessionId is { } requested
                            && !string.Equals(knownStoredSessionId, requested, StringComparison.Ordinal)))
                {
                    // Before the next receipt arrives, a previously bound,
                    // inactive session can still finish its deferred build.
                    // It is not evidence for this activation. Unknown sessions
                    // and the requested warm target retain strict early binding.
                    return false;
                }

                if (_pendingSessionActivation.BufferedSessionInfo is null)
                {
                    _pendingSessionActivation.BufferedSessionInfo =
                        CloneGatewayEvent(gatewayEvent);
                    return false;
                }

                if (SameSessionInfoEvent(
                        _pendingSessionActivation.BufferedSessionInfo,
                        gatewayEvent))
                {
                    return false;
                }

                _pendingSessionActivation.RejectionMessage =
                    "Hermes emitted conflicting session.info events before the session activation receipt.";
                return false;
            }
            if (_lastAllowedSessionInfo is not null
                && SameSessionInfoEvent(
                    _lastAllowedSessionInfo,
                    gatewayEvent))
            {
                // A provider may not expose changing usage, so the settled
                // running=false snapshot can be byte-identical across turns.
                // It is still causally new after an allowed turn event and the
                // Workbench needs it to settle its session-level projection.
                var authoritativeStateChanged = ApplyAuthoritativeSessionInfo(
                    gatewayEvent.SessionId,
                    gatewayEvent.Payload,
                    observedNativeConfigRevision);
                var allow = authoritativeStateChanged
                    || _sessionInfoProjectionDirty;
                if (allow)
                    _sessionInfoProjectionDirty = false;
                return allow;
            }
            _lastAllowedSessionInfo = CloneGatewayEvent(gatewayEvent);
            _sessionInfoProjectionDirty = false;
        }

        ApplyAuthoritativeSessionInfo(
            gatewayEvent.SessionId,
            gatewayEvent.Payload,
            observedNativeConfigRevision);
        return true;
    }

    private bool TryDeferSessionActivationEvent(
        HermesGatewayEvent gatewayEvent)
    {
        lock (_sessionLock)
        {
            if (_pendingSessionActivation is null)
                return false;
            if (_pendingSessionActivation.DeferredEvents.Count
                >= MaximumDeferredSessionActivationEvents)
            {
                _pendingSessionActivation.RejectionMessage =
                    "Hermes exceeded the bounded session activation event projection capacity.";
                return true;
            }
            _pendingSessionActivation.DeferredEvents.Add(
                CloneGatewayEvent(gatewayEvent));
            return true;
        }
    }

    private bool ApplyAuthoritativeSessionInfo(
        string sessionId,
        JsonElement info,
        long sharedNativeConfigRevision,
        bool refreshesNativeConfigBinding = false)
    {
        var inspection = HermesNativeSessionInfoSurface.Inspect(info);
        var workspaceAttested = inspection.Disposition
                == HermesNativeSessionInfoDisposition.Ready
            && TryEnsureWorkspacePathSafe()
            && TryReadAuthoritativeWorkspace(info, out var workspace)
            && string.Equals(
                workspace,
                _workspacePath,
                StringComparison.OrdinalIgnoreCase);
        var authoritativelySettled = workspaceAttested
            && TryGetSingleProperty(info, "running", out var running)
            && running.ValueKind == JsonValueKind.False;
        var state = inspection.Disposition switch
        {
            HermesNativeSessionInfoDisposition.Pending =>
                SessionNativeSurfaceState.Pending,
            HermesNativeSessionInfoDisposition.Ready when workspaceAttested =>
                SessionNativeSurfaceState.Attested,
            _ => SessionNativeSurfaceState.Rejected
        };
        var expiredPendingApproval = false;
        lock (_sessionLock)
        {
            var revisionIsCurrent = sharedNativeConfigRevision
                == _observedNativeConfigRevision;
            if (state == SessionNativeSurfaceState.Attested
                && ((!refreshesNativeConfigBinding
                        && string.Equals(
                        _nativeConfigInvalidatedSessionId,
                        sessionId,
                        StringComparison.Ordinal))
                    || !_sessionNativeConfigRevisions.TryGetValue(
                        sessionId,
                        out var admittedRevision)
                    || admittedRevision != sharedNativeConfigRevision
                    || !revisionIsCurrent))
            {
                state = SessionNativeSurfaceState.Pending;
            }
            if (_sessionStates.TryGetValue(sessionId, out var existing)
                && existing == SessionNativeSurfaceState.Rejected)
            {
                state = SessionNativeSurfaceState.Rejected;
            }

            SetSessionStateUnsafe(sessionId, state);
            ObserveSessionModelIdentityUnsafe(sessionId, info, state);
            ObservePendingCredentialModelInfoUnsafe(
                sessionId, info, state, sharedNativeConfigRevision);
            if (authoritativelySettled)
                expiredPendingApproval = _pendingApprovals.Remove(sessionId);

            // An old session.info must not acknowledge a newer native-config
            // mutation. The admitted revision and the monotonic observation
            // above are checked while this state transition is still atomic.
            if (state == SessionNativeSurfaceState.Attested && revisionIsCurrent)
            {
                _nativeToolsNewSessionRequired = false;
                _nativeConfigInvalidatedSessionId = null;
            }
        }
        ObserveCheckpointWorkspaceAttestation(
            sessionId,
            info,
            inspection.Disposition
                != HermesNativeSessionInfoDisposition.Invalid);
        return expiredPendingApproval;
    }

    private void ObservePendingCredentialModelInfoUnsafe(
        string sessionId, JsonElement info, SessionNativeSurfaceState state,
        long nativeConfigRevision)
    {
        // A lazy surface alone is never permission to mutate a session. Retain
        // only its identity, then require a separate, revision-bound native
        // initialization failure and fresh credential/catalog evidence.
        if (sessionId != _activeSessionId)
            return;
        if (state != SessionNativeSurfaceState.Pending
            || HermesNativeSessionInfoSurface.Inspect(info).Disposition
                != HermesNativeSessionInfoDisposition.Pending
            || !TryReadAuthoritativeWorkspace(info, out var workspace)
            || !string.Equals(workspace, _workspacePath, StringComparison.OrdinalIgnoreCase)
            || !TryGetSingleProperty(info, "provider", out var provider)
            || provider.ValueKind != JsonValueKind.String
            || provider.GetString() is not { } providerId || !IsSafeProviderToken(providerId)
            || !TryGetSingleProperty(info, "model", out var model)
            || model.ValueKind != JsonValueKind.String
            || model.GetString() is not { } modelId || !IsSafeModelToken(modelId)
            || info.TryGetProperty("running", out var running)
                && running.ValueKind != JsonValueKind.False)
        {
            _pendingCredentialModelRecovery = null;
            return;
        }
        // Pinned native _deferred_session_record/_runtime_revision_locked start
        // at zero; _lazy_resume_info omits the revision. A nonzero failure may
        // not borrow that initial identity. Explicit revisions must be exact.
        long runtimeRevision = 0;
        if (info.TryGetProperty("runtime_revision", out _)
            && (!TryGetSingleProperty(info, "runtime_revision", out var revision)
                || revision.ValueKind != JsonValueKind.Number
                || !revision.TryGetInt64(out runtimeRevision) || runtimeRevision < 0))
        {
            _pendingCredentialModelRecovery = null;
            return;
        }
        var previous = _pendingCredentialModelRecovery;
        if (previous is not null && previous.SessionId == sessionId
            && previous.ProviderId == providerId && previous.ModelId == modelId
            && previous.NativeConfigRevision == nativeConfigRevision
            && previous.RuntimeRevision == runtimeRevision)
            return;
        _pendingCredentialModelRecovery = new(
            sessionId, providerId, modelId, nativeConfigRevision, runtimeRevision);
    }

    private void ObserveCredentialInitializationFailure(HermesGatewayEvent gatewayEvent)
    {
        lock (_sessionLock)
        {
            var recovery = _pendingCredentialModelRecovery;
            if (recovery is null || gatewayEvent.SessionId != recovery.SessionId
                || gatewayEvent.SessionId != _activeSessionId)
                return;
            recovery.FailureObserved = false;
            var payload = gatewayEvent.Payload;
            if (payload.ValueKind != JsonValueKind.Object
                || payload.EnumerateObject().Count() != 3
                || !TryGetSingleProperty(payload, "runtime_ready", out var ready)
                || ready.ValueKind != JsonValueKind.False
                || !TryGetSingleProperty(payload, "runtime_revision", out var revision)
                || revision.ValueKind != JsonValueKind.Number
                || !revision.TryGetInt64(out var runtimeRevision)
                || runtimeRevision != recovery.RuntimeRevision
                || !TryGetSingleProperty(payload, "message", out var message)
                || message.ValueKind != JsonValueKind.String
                || message.GetString() is not { } text || text.Length > 4096
                || gatewayEvent.Parameters.ValueKind == JsonValueKind.Object
                    && gatewayEvent.Parameters.TryGetProperty("turn_id", out _))
                return;
            // Pinned runtime_provider missing_api_key AuthError, wrapped only
            // by the native deferred-build error event. No fuzzy "key" match,
            // HTTP 401, tool rejection, or generic agent-init error is enough.
            var prefix = "agent init failed: No usable credentials found for provider '"
                + recovery.ProviderId + "'.";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                return;
            var hint = text[prefix.Length..];
            if (hint.Length != 0)
            {
                if (!hint.StartsWith(" Set ", StringComparison.Ordinal)
                    || !hint.EndsWith(".", StringComparison.Ordinal))
                    return;
                var names = hint[5..^1].Split(", ", StringSplitOptions.None);
                if (names.Any(name => name.Length == 0
                    || name.Any(character => !char.IsAsciiLetterUpper(character)
                        && !char.IsAsciiDigit(character) && character != '_')))
                    return;
            }
            recovery.FailureObserved = true;
        }
    }

    private bool CanRecoverPendingCredentialModelUnsafe(
        PendingCredentialModelRecovery recovery, LilithRuntimeModelAuthorityLease authority)
        => ReferenceEquals(recovery, _pendingCredentialModelRecovery)
            && recovery.FailureObserved
            && _sessionSurface == LilithRuntimeSessionSurface.Workbench
            && _runtimeState() == LilithRuntimeState.Ready
            && authority.Snapshot.GenerationId == _generationId
            && authority.Snapshot.State == LilithRuntimeModelAuthorityState.Ready
            && authority.Snapshot.NativeConfigRevision == recovery.NativeConfigRevision
            && _observedNativeConfigRevision == recovery.NativeConfigRevision
            && _activeSessionId == recovery.SessionId
            && _sessionStates.TryGetValue(recovery.SessionId, out var state)
            && state == SessionNativeSurfaceState.Pending
            && _sessionNativeConfigRevisions.TryGetValue(recovery.SessionId, out var revision)
            && revision == recovery.NativeConfigRevision
            && !_sessionActivationQuarantined && !_sessionDeleteQuarantined
            && !_sessionLifecycleInFlight && _pendingSessionActivation is null
            && !_nativeConfigQuarantined && !_nativeToolsNewSessionRequired
            && _nativeConfigInvalidatedSessionId is null
            && _nativeConfigMutationInFlightSessionId is null
            && _emergencyInterruptReservations == 0
            && _voiceState == VoiceLifecycleState.Off && !_voiceRequestInFlight
            && _checkpointRestoreState == CheckpointRestoreState.Ready
            && !_checkpointRestoreRequestInFlight
            && (!_pendingApprovals.TryGetValue(recovery.SessionId, out var approvals)
                || approvals.Count == 0)
            && _sessionImages.TryGetValue(recovery.SessionId, out var image)
            && image.State == SessionImageLifecycleState.Idle && image.Attachment is null
            && _sessionTurns.TryGetValue(recovery.SessionId, out var turn)
            && !turn.AwaitingReceipt && !turn.AwaitingNativeAssistantContinuationReceipt
            && !turn.AwaitingQueuedTurn && turn.PendingTurnId.Length == 0
            && turn.PendingUserMessageId.Length == 0 && turn.PendingAssistantMessageId.Length == 0
            && turn.ActiveAssistantMessageIds.Count == 0;

    private async Task<LilithRuntimeResponse> RoutePendingCredentialModelRecoveryAsync(
        LilithRuntimeRequest request, JsonElement parameters, string sessionId,
        LilithRuntimeModelAuthorityLease authority, CancellationToken cancellationToken)
    {
        LilithRuntimeResponse Unavailable() => Failure("config.set",
            "runtime.native_surface_attestation_required",
            "Only a settled, verified missing-credential initialization may recover its session model.");
        if (!_modelAuthority.OwnsActiveLease(authority, _generationId))
            return Unavailable();
        PendingCredentialModelRecovery recovery;
        lock (_sessionLock)
        {
            if (_pendingCredentialModelRecovery is not { } candidate
                || candidate.SessionId != sessionId
                || !CanRecoverPendingCredentialModelUnsafe(candidate, authority))
                return Unavailable();
            recovery = candidate;
        }
        if (!TryEnsureWorkspacePathSafe())
            return Unavailable();
        // Canonicalize has already accepted only the typed model pair and
        // generated --session; callers cannot pass prompt, tools, or flags.
        var providerId = request.HermesPayload.GetProperty("provider").GetString()!;
        var modelId = request.HermesPayload.GetProperty("model").GetString()!;
        var credentials = SanitizeCredentialResult("model.credential_status", Json("{}"),
            await _requestAsync("model.credential_status", Json("{}"), cancellationToken)
                .ConfigureAwait(false));
        bool? Configured(string provider) => credentials.GetProperty("providers")
            .EnumerateArray().Where(row => row.GetProperty("slug").GetString() == provider)
            .Select(row => (bool?)row.GetProperty("configured").GetBoolean()).SingleOrDefault();
        if (Configured(recovery.ProviderId) != false || Configured(providerId) != true)
            return Unavailable();
        lock (_sessionLock)
            if (!CanRecoverPendingCredentialModelUnsafe(recovery, authority))
                return Unavailable();
        var options = await _requestAsync("model.options", JsonSerializer.SerializeToElement(new
        {
            session_id = sessionId, explicit_only = true, include_unconfigured = false, refresh = false
        }), cancellationToken).ConfigureAwait(false);
        if (!ModelOptionsHasValidAvailabilitySubset(options, providerId)
            || !ModelOptionsContainsExactPair(IntersectModelOptions(options), providerId, modelId))
            return Unavailable();
        lock (_sessionLock)
            if (!CanRecoverPendingCredentialModelUnsafe(recovery, authority))
                return Unavailable();
        var preserveConfirmation = false;
        try
        {
            var result = await _requestAsync("config.set", parameters, cancellationToken)
                .ConfigureAwait(false);
            var values = ReadExactObject(result, new[]
            {
                "key", "value", "warning", "confirm_required", "confirm_message",
                "scope", "runtime_revision", "runtime_ready"
            });
            if (values.Count != 8
                || values["key"].ValueKind != JsonValueKind.String || values["key"].GetString() != "model"
                || values["value"].ValueKind != JsonValueKind.String || values["value"].GetString() != modelId
                || values["scope"].ValueKind != JsonValueKind.String || values["scope"].GetString() != "session"
                || values["warning"].ValueKind != JsonValueKind.String
                || values["confirm_message"].ValueKind != JsonValueKind.String
                || values["confirm_required"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || values["runtime_revision"].ValueKind != JsonValueKind.Number
                || !values["runtime_revision"].TryGetInt64(out var revision)
                || values["runtime_ready"].ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Native session model recovery receipt was malformed.");
            preserveConfirmation = values["confirm_required"].GetBoolean();
            if (preserveConfirmation)
            {
                lock (_sessionLock)
                    preserveConfirmation = CanRecoverPendingCredentialModelUnsafe(recovery, authority)
                        && revision == recovery.RuntimeRevision
                        && values["runtime_ready"].ValueKind == JsonValueKind.False
                        && !string.IsNullOrWhiteSpace(values["confirm_message"].GetString());
                if (!preserveConfirmation)
                    throw new InvalidDataException("Native model confirmation changed recovery identity.");
            }
            else if (revision <= recovery.RuntimeRevision
                || values["runtime_ready"].ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Native replacement runtime did not become ready.");
            lock (_sessionLock)
            {
                if (_activeSessionId != sessionId
                    || !_sessionStates.TryGetValue(sessionId, out var state)
                    || state == SessionNativeSurfaceState.Rejected
                    || state == SessionNativeSurfaceState.Attested
                        && (!_sessionModelObservations.TryGetValue(sessionId, out var observed)
                            || observed.ProviderId != providerId || observed.ModelId != modelId
                            || observed.Running))
                    throw new InvalidDataException("Native model recovery contradicted the active session.");
            }
            // Native ready session.info remains the ONLY attestation path.
            // A success receipt must not manufacture tool or prompt authority.
            return Success("config.set", result);
        }
        catch (InvalidDataException)
        {
            lock (_sessionLock)
                SetSessionStateUnsafe(sessionId, SessionNativeSurfaceState.Rejected);
            throw;
        }
        finally
        {
            if (!preserveConfirmation)
            {
                lock (_sessionLock)
                    if (ReferenceEquals(recovery, _pendingCredentialModelRecovery))
                        _pendingCredentialModelRecovery = null;
            }
        }
    }

    private void ObserveSessionModelIdentityUnsafe(
        string sessionId,
        JsonElement info,
        SessionNativeSurfaceState state)
    {
        var providerId = string.Empty;
        var modelId = string.Empty;
        var runningValue = false;
        var valid = false;
        if (state == SessionNativeSurfaceState.Attested
            && TryGetSingleProperty(info, "provider", out var provider)
            && provider.ValueKind == JsonValueKind.String
            && provider.GetString() is { } observedProviderId
            && IsSafeProviderToken(observedProviderId)
            && TryGetSingleProperty(info, "model", out var model)
            && model.ValueKind == JsonValueKind.String
            && model.GetString() is { } observedModelId
            && IsSafeModelToken(observedModelId)
            && TryGetSingleProperty(info, "running", out var running)
            && running.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            // This is observation evidence, not model authority. A durable
            // session can legitimately report an old pair whose key or menu
            // entry was removed; GameHost must still see that mismatch so it
            // can switch to the separately approved effective global pair.
            providerId = observedProviderId;
            modelId = observedModelId;
            runningValue = running.GetBoolean();
            valid = true;
        }
        if (!valid)
        {
            _sessionModelObservations.Remove(sessionId);
            return;
        }
        _sessionModelObservations[sessionId] = new(
            providerId,
            modelId,
            runningValue,
            checked(++_sessionModelObservationRevision));
    }

    private static HermesGatewayEvent CloneGatewayEvent(
        HermesGatewayEvent gatewayEvent)
        => new(
            gatewayEvent.Type,
            gatewayEvent.SessionId,
            gatewayEvent.Payload.Clone(),
            gatewayEvent.Parameters.Clone());

    private static bool SameSessionInfoEvent(
        HermesGatewayEvent first,
        HermesGatewayEvent second)
        => string.Equals(
                first.SessionId,
                second.SessionId,
                StringComparison.Ordinal)
            && SameJson(first.Payload, second.Payload);

    private static bool SameJson(JsonElement first, JsonElement second)
    {
        if (first.ValueKind != second.ValueKind)
            return false;
        if (first.ValueKind == JsonValueKind.Undefined)
            return true;
        return string.Equals(
            first.GetRawText(),
            second.GetRawText(),
            StringComparison.Ordinal);
    }

    internal void ObserveGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        ArgumentNullException.ThrowIfNull(gatewayEvent);
        if (string.IsNullOrWhiteSpace(gatewayEvent.SessionId))
            return;

        if (string.Equals(gatewayEvent.Type, "session.info", StringComparison.Ordinal))
        {
            _ = ObserveSessionInfoGatewayEvent(gatewayEvent);
            return;
        }

        if (string.Equals(gatewayEvent.Type, "approval.request", StringComparison.Ordinal))
        {
            ObserveApprovalRequest(gatewayEvent);
            return;
        }

        if (string.Equals(gatewayEvent.Type, "message.start", StringComparison.Ordinal))
        {
            lock (_sessionLock)
                if (_pendingCredentialModelRecovery?.SessionId == gatewayEvent.SessionId)
                    _pendingCredentialModelRecovery = null;
            ObservePromptStart(gatewayEvent.SessionId);
            return;
        }

        if (string.Equals(gatewayEvent.Type, "error", StringComparison.Ordinal)
            || string.Equals(
                gatewayEvent.Type,
                "message.complete",
                StringComparison.Ordinal))
        {
            if (gatewayEvent.Type == "error")
                ObserveCredentialInitializationFailure(gatewayEvent);
            ObservePromptTerminalBeforeStart(gatewayEvent.SessionId);
            return;
        }

        if (gatewayEvent.Type.StartsWith("tool.", StringComparison.Ordinal))
        {
            if (!HermesNativeSessionInfoSurface.IsBoundedToolEvent(gatewayEvent))
            {
                lock (_sessionLock)
                {
                    if (string.Equals(
                            _activeSessionId,
                            gatewayEvent.SessionId,
                            StringComparison.Ordinal))
                    {
                        SetSessionState(
                            gatewayEvent.SessionId,
                            SessionNativeSurfaceState.Rejected);
                    }
                }
            }
            return;
        }

        // Inference/provider failures do not describe the session tool or MCP
        // surface. They are forwarded to clients, but cannot revoke (or grant)
        // the independent approved-native-surface attestation.
    }

    private void ObserveCheckpointWorkspaceAttestation(
        string sessionId,
        JsonElement info,
        bool surfaceAttested)
    {
        var next = CheckpointWorkspaceAttestationState.Rejected;
        if (surfaceAttested && info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("lazy", out var lazy)
                && lazy.ValueKind == JsonValueKind.True)
            {
                next = CheckpointWorkspaceAttestationState.Pending;
            }
            else if ((!info.TryGetProperty("lazy", out lazy)
                        || lazy.ValueKind == JsonValueKind.False)
                     && TryReadAuthoritativeWorkspace(info, out var workspace)
                     && string.Equals(
                         workspace,
                         _workspacePath,
                         StringComparison.OrdinalIgnoreCase))
            {
                next = CheckpointWorkspaceAttestationState.Attested;
            }
        }

        lock (_sessionLock)
        {
            if (_checkpointWorkspaceStates.TryGetValue(sessionId, out var existing)
                && existing == CheckpointWorkspaceAttestationState.Rejected)
            {
                return;
            }
            _checkpointWorkspaceStates[sessionId] = next;
            if (next != CheckpointWorkspaceAttestationState.Attested
                && string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                if (_checkpointRestoreRequestInFlight)
                    _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                InvalidateCheckpointCatalogUnsafe();
            }
        }
    }

    private static bool TryReadAuthoritativeWorkspace(
        JsonElement info,
        out string workspace)
    {
        workspace = string.Empty;
        var count = 0;
        JsonElement value = default;
        foreach (var property in info.EnumerateObject())
        {
            if (!property.NameEquals("cwd"))
                continue;
            count++;
            value = property.Value;
        }
        if (count != 1
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            return false;
        }

        try
        {
            var candidate = value.GetString()!;
            if (!Path.IsPathFullyQualified(candidate))
                return false;
            workspace = CanonicalPath(candidate);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            return false;
        }
    }

    private bool ObserveRollbackRestoredGatewayEvent(
        HermesGatewayEvent gatewayEvent)
    {
        try
        {
            var values = ReadExactObject(gatewayEvent.Payload, new[]
            {
                "success", "operation_id", "workspace_restored",
                "history_rewound", "history_removed", "partial",
                "restored_to", "reason", "pre_snapshot", "error"
            });
            if (!TryRequiredBoolean(values, "success", out var success)
                || !TryRequiredBoolean(
                    values,
                    "workspace_restored",
                    out var workspaceRestored)
                || !workspaceRestored
                || !TryRequiredBoolean(
                    values,
                    "history_rewound",
                    out var historyRewound)
                || !TryRequiredBoolean(values, "partial", out var partial))
            {
                throw new InvalidDataException(
                    "Hermes returned a malformed workspace restore event.");
            }
            var historyRemoved = RequiredInteger(
                values,
                "history_removed",
                0,
                int.MaxValue);
            var operationId = RequiredText(
                values,
                "operation_id",
                MaximumCheckpointOperationIdUtf8Bytes);
            if (!IsCanonicalCheckpointOperationId(operationId))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid workspace restore operation id.");
            }
            var restoredTo = RequiredMultilineTextAllowEmpty(
                values,
                "restored_to",
                MaximumCheckpointReceiptTextUtf8Bytes);
            if (!IsCanonicalCheckpointHashReference(restoredTo))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid restored checkpoint reference.");
            }
            ValidateOptionalCheckpointReceiptText(values, "reason");
            var preSnapshot = RequiredMultilineTextAllowEmpty(
                values,
                "pre_snapshot",
                MaximumCheckpointReceiptTextUtf8Bytes);
            if (!IsFullCheckpointHash(preSnapshot))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid pre-restore checkpoint reference.");
            }
            ValidateOptionalCheckpointReceiptText(values, "error");

            lock (_sessionLock)
            {
                if (!_checkpointRestoreRequestInFlight
                    || _checkpointRestoreState != CheckpointRestoreState.InFlight
                    || !string.Equals(
                        _activeSessionId,
                        gatewayEvent.SessionId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        _checkpointOperationId,
                        operationId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes returned a workspace restore event outside the reserved operation.");
                }
                _checkpointMutationObserved = true;
                _checkpointObservedRestoreEvent = new ObservedCheckpointRestoreEvent(
                    success,
                    historyRewound,
                    historyRemoved,
                    partial,
                    restoredTo,
                    preSnapshot);
            }
        }
        catch
        {
            QuarantineCheckpointRestore();
        }

        // Native restore events are consumed as safety attestations. They are
        // never projected onto the Control Center event surface.
        return false;
    }

    private bool ObserveApprovalRequest(HermesGatewayEvent gatewayEvent)
    {
        if (!TryReadNativeApproval(
                gatewayEvent.Payload,
                out var approval))
        {
            lock (_sessionLock)
            {
                if (string.Equals(
                        _activeSessionId,
                        gatewayEvent.SessionId,
                        StringComparison.Ordinal))
                {
                    SetSessionState(
                        gatewayEvent.SessionId,
                        SessionNativeSurfaceState.Rejected);
                }
            }
            return false;
        }

        lock (_sessionLock)
        {
            if (!string.Equals(
                    _activeSessionId,
                    gatewayEvent.SessionId,
                    StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(
                    gatewayEvent.SessionId,
                    out var state)
                || state != SessionNativeSurfaceState.Attested)
            {
                return false;
            }
            if (_checkpointRestoreRequestInFlight)
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;

            if (!_pendingApprovals.TryGetValue(
                    gatewayEvent.SessionId,
                    out var approvals))
            {
                approvals = new Queue<PendingNativeApproval>();
                _pendingApprovals.Add(gatewayEvent.SessionId, approvals);
            }
            approvals.Enqueue(approval);
            return true;
        }
    }

    private static bool TryReadNativeApproval(
        JsonElement payload,
        out PendingNativeApproval approval)
    {
        approval = null!;
        if (payload.ValueKind != JsonValueKind.Object)
            return false;
        var allowedProperties = new HashSet<string>(new[]
        {
            "pattern_key", "pattern_keys", "command", "description",
            "allow_permanent", "allow_session", "smart_denied", "choices"
        }, StringComparer.Ordinal);
        var observedProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name)
                || !observedProperties.Add(property.Name))
            {
                return false;
            }
        }
        if (!TryGetSingleProperty(payload, "pattern_key", out var key)
            || key.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var patternKey = key.GetString() ?? string.Empty;
        if (!IsBoundedApprovalText(patternKey, 256, allowMultiline: false))
        {
            return false;
        }

        if (!TryGetOptionalSingleProperty(
                payload,
                "pattern_keys",
                out var patternKeys,
                out var hasPatternKeys)
            || hasPatternKeys && !IsValidPatternKeys(patternKeys, patternKey))
        {
            return false;
        }

        if (!TryGetOptionalSingleProperty(
                payload,
                "command",
                out var command,
                out var hasCommand)
            || hasCommand
                && (command.ValueKind != JsonValueKind.String
                    || !IsBoundedApprovalText(
                        command.GetString(),
                        16 * 1024,
                        allowMultiline: true)))
        {
            return false;
        }

        if (!TryGetOptionalSingleProperty(
                payload,
                "description",
                out var description,
                out var hasDescription)
            || hasDescription
                && (description.ValueKind != JsonValueKind.String
                    || !IsBoundedApprovalText(
                        description.GetString(),
                        16 * 1024,
                        allowMultiline: true)))
        {
            return false;
        }
        foreach (var booleanName in new[]
                 {
                     "allow_permanent", "allow_session", "smart_denied"
                 })
        {
            if (!TryGetOptionalSingleProperty(
                    payload,
                    booleanName,
                    out var boolean,
                    out var hasBoolean)
                || hasBoolean
                    && boolean.ValueKind is not (
                        JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }
        }

        if (!TryGetSingleProperty(payload, "choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (choices.GetArrayLength() is < 1 or > 4)
            return false;
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.String
                || !observed.Add(choice.GetString()!)
                || choice.GetString() is not ("once" or "session" or "always" or "deny"))
            {
                return false;
            }
            ordered.Add(choice.GetString()!);
        }
        approval = new PendingNativeApproval(
            patternKey,
            Array.AsReadOnly(ordered.ToArray()));
        return true;
    }

    private static bool IsValidPatternKeys(
        JsonElement patternKeys,
        string primary)
    {
        if (patternKeys.ValueKind != JsonValueKind.Array
            || patternKeys.GetArrayLength() is < 1 or > 64)
        {
            return false;
        }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in patternKeys.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !IsBoundedApprovalText(
                    item.GetString(),
                    256,
                    allowMultiline: false)
                || !unique.Add(item.GetString()!))
            {
                return false;
            }
        }
        return unique.Contains(primary);
    }

    private static bool IsBoundedApprovalText(
        string? value,
        int maximumCharacters,
        bool allowMultiline)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumCharacters
            && !value.Any(character => char.IsControl(character)
                && (!allowMultiline
                    || character is not '\r' and not '\n' and not '\t'));

    private static bool TryGetSingleProperty(
        JsonElement value,
        string propertyName,
        out JsonElement propertyValue)
    {
        propertyValue = default;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
                continue;
            count++;
            propertyValue = property.Value;
        }
        return count == 1;
    }

    private static bool TryGetOptionalSingleProperty(
        JsonElement value,
        string propertyName,
        out JsonElement propertyValue,
        out bool present)
    {
        propertyValue = default;
        present = false;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.NameEquals(propertyName))
                continue;
            if (present)
                return false;
            present = true;
            propertyValue = property.Value;
        }
        return true;
    }

    private async Task<LilithRuntimeResponse> RouteApprovalResponseAsync(
        JsonElement parameters,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority,
        LilithDailyNoteApprovalReservation? daily = null)
    {
        // For daily work this is the native queue ownership key, not a chat
        // session. No entry is added to _sessionStates or the selected task.
        var sessionId = daily?.Request.Scope.AttemptId.ToString("N") ?? OptionalSessionId(parameters)
            ?? throw new InvalidDataException("Runtime approval requires a session id.");
        var choice = parameters.GetProperty("choice").GetString()
            ?? throw new InvalidDataException("Runtime approval requires a choice.");
        var persistsNativeConfig = string.Equals(
            choice,
            "always",
            StringComparison.Ordinal);
        if (persistsNativeConfig && _profile is null)
        {
            return Failure(
                "approval.respond",
                "runtime.native_config_receipt_unavailable",
                "Runtime has no strict profile receipt for permanent native approval.");
        }

        var nativeConfigGateEntered = false;
        try
        {
            if (persistsNativeConfig)
            {
                var authorityFailure = NativeModelAuthorityMutationFailure(
                    "approval.respond",
                    modelAuthority.Snapshot);
                if (authorityFailure is not null)
                    return authorityFailure;

                await _nativeConfigGate.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                nativeConfigGateEntered = true;
            }
            using var permanentApprovalLifetime = persistsNativeConfig
                ? new CancellationTokenSource(
                    _permanentApprovalTransactionTimeout)
                : null;
            var transactionCancellationToken = permanentApprovalLifetime?.Token
                ?? cancellationToken;

            PendingNativeApproval approval;
            if (!(daily is null
                ? TryReserveOldestApproval(sessionId, choice, out approval)
                : TryReserveDailyNativeApproval(daily, out approval)))
            {
                return Failure(
                    "approval.respond",
                    "runtime.pending_native_approval_required",
                    "Only an offered choice for the oldest observed native approval may be answered.");
            }

            var resolved = false;
            var retainForSafeRetry = false;
            try
            {
                LilithNativeConfigMutationState? previous = null;
                if (persistsNativeConfig)
                {
                    try
                    {
                        previous = await LilithNativeConfigMutationReceiptStore
                            .CaptureCurrentAsync(
                                _hermesHomePath,
                                _profile!.CanonicalLearningConfigSha256,
                                 _profile.IdentityFingerprintSha256,
                                 _profile.SoulSha256,
                                 _profile.HermesPersonalitySha256,
                                 transactionCancellationToken).ConfigureAwait(false);
                        var dispatchConfigSha256 =
                            await LilithNativeConfigMutationReceiptStore
                                 .ReadConfigSha256Async(
                                     _hermesHomePath,
                                     transactionCancellationToken).ConfigureAwait(false);
                        if (!string.Equals(
                                dispatchConfigSha256,
                                previous.ConfigSha256,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "Hermes native config changed before permanent approval dispatch.");
                        }
                    }
                    catch (OperationCanceledException)
                        when (transactionCancellationToken.IsCancellationRequested)
                    {
                        // No Hermes mutation RPC has crossed the boundary yet.
                        // Preserve the exact FIFO approval so a verified retry or
                        // emergency Stop remains possible without false quarantine.
                        retainForSafeRetry = true;
                        throw;
                    }
                    catch
                    {
                        QuarantineNativeConfig(modelAuthority);
                        throw new LilithNativeConfigMutationUncertainException();
                    }
                }

                JsonElement result;
                try
                {
                    if (daily is not null && !daily.IsCurrent)
                        return Failure("approval.respond", "runtime.daily_approval_expired", "The exact daily approval was revoked before dispatch.");
                    result = await _requestAsync(
                        daily is null ? "approval.respond" : LilithDailyNoteApprovalProtocol.RespondMethod,
                        parameters,
                        transactionCancellationToken).ConfigureAwait(false);
                }
                catch (HermesRequestNotSentException)
                {
                    retainForSafeRetry = true;
                    if (daily is null && persistsNativeConfig)
                        return await ApprovalNoMutationFailureAsync(
                            sessionId, choice, approval, previous!, modelAuthority)
                            .ConfigureAwait(false);
                    throw;
                }
                catch (HermesRpcException exception)
                    when (persistsNativeConfig && exception.Code == 5035)
                {
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                catch (HermesRpcException exception)
                    when (persistsNativeConfig && exception.Code == 4090)
                {
                    retainForSafeRetry = true;
                    if (daily is null)
                        return await ApprovalNoMutationFailureAsync(
                            sessionId, choice, approval, previous!, modelAuthority)
                            .ConfigureAwait(false);
                    throw;
                }
                catch (HermesRpcException exception)
                    when (persistsNativeConfig
                          && exception.Code is 5034 or -32602)
                {
                    // Hermes proves these rejections before committing a native
                    // config mutation (or after a verified byte-for-byte rollback).
                    // They are definite failures, not mutation uncertainty.
                    retainForSafeRetry = true;
                    if (daily is null)
                        return await ApprovalNoMutationFailureAsync(
                            sessionId, choice, approval, previous!, modelAuthority)
                            .ConfigureAwait(false);
                    throw;
                }
                catch (OperationCanceledException)
                    when (persistsNativeConfig
                          && transactionCancellationToken.IsCancellationRequested)
                {
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                catch (HermesRpcException)
                    when (persistsNativeConfig)
                {
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                catch (HermesRpcException)
                {
                    throw;
                }
                catch when (persistsNativeConfig)
                {
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }

                HermesNativeApprovalResponseResult? directResolution = null;
                try
                {
                    directResolution =
                        HermesNativeToolsContract.DecodeApprovalResolutionResult(result);
                }
                catch
                {
                    // Permanent approval uses a different strict prepare receipt.
                    // Non-permanent malformed results are rejected below.
                }

                if (directResolution?.Resolved == 0)
                {
                    return Failure(
                        "approval.respond",
                        "runtime.pending_native_approval_expired",
                        "The pending native approval had already expired; no action was released.");
                }

                if (!persistsNativeConfig)
                {
                    if (directResolution?.Resolved != 1)
                    {
                        return Failure(
                            "approval.respond",
                            "runtime.approval_resolution_rejected",
                            "Hermes did not resolve exactly one native approval.");
                    }
                    resolved = true;
                    return Success("approval.respond", result);
                }

                HermesNativeApprovalPersistenceReceipt? receipt = null;
                LilithNativeConfigMutationCommit durableCommit;
                try
                {
                    receipt = HermesNativeToolsContract
                        .DecodeApprovalAlwaysPrepareResult(result)
                        .Persistence;
                    durableCommit = await LilithNativeConfigMutationReceiptStore
                        .CommitApprovalAlwaysAsync(
                            _hermesHomePath,
                            previous!,
                            receipt,
                            CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    if (receipt is not null)
                    {
                        await TryAbortPreparedApprovalAsync(
                            sessionId,
                            receipt.CommitNonce, daily).ConfigureAwait(false);
                    }
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }

                if (daily is not null && !daily.IsCurrent)
                {
                    // Phase one may already have persisted permission. Do not
                    // release an action after this UI/task was revoked, and do
                    // not misreport the committed config as having no effects.
                    await TryAbortPreparedApprovalAsync(sessionId, receipt.CommitNonce, daily).ConfigureAwait(false);
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }

                try
                {
                    var commitResult = await _requestAsync(
                        daily is null ? "approval.commit" : "lilith.daily_note.approval.commit",
                        daily is not null ? daily.NativePhaseTwo(HermesNativeToolsContract.ApprovalCommitAuthority,
                            receipt.CommitNonce, durableCommit.ReceiptSha256, abort: false) : new
                        {
                            session_id = sessionId,
                            authority = HermesNativeToolsContract
                                .ApprovalCommitAuthority,
                            commit_nonce = receipt.CommitNonce,
                            receipt_sha256 = durableCommit.ReceiptSha256
                        },
                        transactionCancellationToken).ConfigureAwait(false);
                    _ = HermesNativeToolsContract.DecodeApprovalResolvedResult(
                        commitResult);
                }
                catch (HermesRequestNotSentException)
                {
                    // Phase one already changed native config and committed its
                    // durable receipt. A definitely-not-sent phase-two release
                    // therefore does not make the whole transaction side-effect
                    // free and cannot safely preserve the pre-mutation authority.
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                catch
                {
                    // Once the phase-two RPC is attempted, its response may have
                    // been lost after Hermes released the action. It must never be
                    // retried or rolled back as though it were known not to run.
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }

                if (!_modelAuthority.PublishNativeConfigMutation(
                        modelAuthority,
                        _generationId))
                {
                    QuarantineNativeConfig(modelAuthority);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                ObserveSharedNativeConfigRevision(_modelAuthority.Snapshot());

                // The shared revision invalidates the real foreground chat
                // while preserving its emergency Stop surface. A daily owner
                // must not replace that identity with its background attempt.
                if (daily is null) lock (_sessionLock)
                {
                    _nativeToolsNewSessionRequired = true;
                    _nativeConfigInvalidatedSessionId = sessionId;
                    if (string.Equals(
                            _activeSessionId,
                            sessionId,
                            StringComparison.Ordinal))
                    {
                        _sessionStates[sessionId] =
                            SessionNativeSurfaceState.Pending;
                    }
                    _pendingApprovals.Remove(sessionId);
                }
                resolved = true;
                return Success("approval.respond", Json("{\"resolved\":1}"));
            }
            finally
            {
                if (daily is null)
                    CompleteApprovalReservation(sessionId, approval, resolved, retainForSafeRetry);
                else
                    ReleaseDailyNativeApproval(sessionId);
            }
        }
        finally
        {
            if (nativeConfigGateEntered)
                _nativeConfigGate.Release();
        }
    }

    private async Task<LilithRuntimeResponse> ApprovalNoMutationFailureAsync(
        string sessionId,
        string choice,
        PendingNativeApproval approval,
        LilithNativeConfigMutationState previous,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        // Only the closed phase-one rejection branches call this helper. A
        // phase-two failure must never gain a retry proof, even if not sent.
        try
        {
            using var timeout = new CancellationTokenSource(NativeConfigAbortTimeout);
            var current = await LilithNativeConfigMutationReceiptStore.CaptureCurrentAsync(
                _hermesHomePath, _profile!.CanonicalLearningConfigSha256,
                _profile.IdentityFingerprintSha256, _profile.SoulSha256,
                _profile.HermesPersonalitySha256, timeout.Token).ConfigureAwait(false);
            if ((current with { EnabledToolsets = previous.EnabledToolsets }) != previous
                || !current.EnabledToolsets.SequenceEqual(previous.EnabledToolsets, StringComparer.Ordinal))
                throw new InvalidDataException("The no-mutation approval state changed.");
        }
        catch
        {
            QuarantineNativeConfig(modelAuthority);
            throw new LilithNativeConfigMutationUncertainException();
        }

        lock (_sessionLock)
        {
            if (_nativeConfigQuarantined
                || _modelAuthority.Snapshot() != modelAuthority.Snapshot
                || !string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(sessionId, out var state)
                || state != SessionNativeSurfaceState.Attested
                || !_pendingApprovals.TryGetValue(sessionId, out var pending)
                || pending.Count == 0 || !ReferenceEquals(pending.Peek(), approval)
                || !approval.InFlight)
                return Failure("approval.respond", "runtime.approval_retry_unavailable",
                    "The no-mutation approval no longer has the same active FIFO owner.");

            return new LilithRuntimeResponse("approval.respond", succeeded: false,
                JsonSerializer.SerializeToElement(new
                {
                    code = RuntimeApprovalRetryProof.FailureCode,
                    message = "No native mutation occurred; the exact pending approval may be answered again.",
                    retry_proof = RuntimeApprovalRetryProof.Create(_generationId,
                        sessionId, choice, approval.PatternKey, approval.Choices)
                }));
        }
    }

    private async Task TryAbortPreparedApprovalAsync(
        string sessionId,
        string commitNonce,
        LilithDailyNoteApprovalReservation? daily = null)
    {
        try
        {
            using var timeout = new CancellationTokenSource(
                NativeConfigAbortTimeout);
            var result = await _requestAsync(
                    daily is null ? "approval.abort" : "lilith.daily_note.approval.abort",
                    daily is not null ? daily.NativePhaseTwo(HermesNativeToolsContract.ApprovalCommitAuthority,
                        commitNonce, "runtime_receipt_commit_failed", abort: true) : new
                    {
                        session_id = sessionId,
                        authority = HermesNativeToolsContract.ApprovalCommitAuthority,
                        commit_nonce = commitNonce,
                        reason = "runtime_receipt_commit_failed"
                    },
                    timeout.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            var values = ReadExactObject(
                result,
                new[] { "aborted", "quarantined" });
            if (values.Count != 2
                || RequiredInteger(values, "aborted", 1, 1) != 1
                || !TryRequiredBoolean(
                    values,
                    "quarantined",
                    out var quarantined)
                || !quarantined)
            {
                throw new InvalidDataException(
                    "Hermes did not quarantine the prepared permanent approval.");
            }
        }
        catch
        {
            // Runtime is quarantined by the caller either way. A failed abort
            // leaves Hermes' pending-transition gate as the fail-closed owner.
        }
    }

    private bool TryReserveOldestApproval(
        string sessionId,
        string choice,
        out PendingNativeApproval approval)
    {
        lock (_sessionLock)
        {
            approval = null!;
            if (!string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(sessionId, out var state)
                || state != SessionNativeSurfaceState.Attested
                || !_pendingApprovals.TryGetValue(sessionId, out var approvals)
                || approvals.Count == 0
                || approvals.Peek().InFlight
                || !approvals.Peek().Choices.Contains(
                    choice,
                    StringComparer.Ordinal))
            {
                return false;
            }

            approval = approvals.Peek();
            approval.InFlight = true;
            if (string.Equals(choice, "always", StringComparison.Ordinal))
            {
                if (_nativeConfigMutationInFlightSessionId is not null)
                {
                    approval.InFlight = false;
                    return false;
                }
                _nativeConfigMutationInFlightSessionId = sessionId;
            }
            return true;
        }
    }

    private void CompleteApprovalReservation(
        string sessionId,
        PendingNativeApproval approval,
        bool resolved,
        bool retainForSafeRetry)
    {
        lock (_sessionLock)
        {
            if (string.Equals(
                    _nativeConfigMutationInFlightSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                _nativeConfigMutationInFlightSessionId = null;
            }
            if (!_pendingApprovals.TryGetValue(sessionId, out var approvals)
                || approvals.Count == 0
                || !ReferenceEquals(approvals.Peek(), approval))
            {
                return;
            }

            if (retainForSafeRetry)
            {
                approval.InFlight = false;
                return;
            }

            if (!resolved)
            {
                _pendingApprovals.Remove(sessionId);
                return;
            }

            approvals.Dequeue();
            if (approvals.Count == 0)
                _pendingApprovals.Remove(sessionId);
        }
    }

    internal async Task<LilithRuntimeResponse> RouteAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken)
        => await RouteAsync(
            request,
            cancellationToken,
            ownedModelAuthority: null).ConfigureAwait(false);

    internal async Task<LilithRuntimeResponse> RouteAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease? ownedModelAuthority)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method is "runtime.letters.get" or "runtime.letters.set")
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Settings (especially Off) must not wait behind model work. The
            // authenticated Workbench channel is the only public writer.
            try
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench || _runtimeState() != LilithRuntimeState.Ready)
                    return Failure(request.Method, "runtime.letters_unavailable", "Letters settings unavailable.");
                var letters = _letters() ?? throw new InvalidOperationException();
                LilithNoteInboxProtocol.Exact(request.HermesPayload, request.Method == "runtime.letters.get"
                    ? Array.Empty<string>() : new[] { "enabled", "daily_limit" });
                var result = request.Method == "runtime.letters.get" ? letters.Status()
                    : letters.Configure(request.HermesPayload.GetProperty("enabled").GetBoolean(), request.HermesPayload.GetProperty("daily_limit").GetInt32());
                return Success(request.Method, JsonSerializer.SerializeToElement(result));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return Failure(request.Method, "runtime.letters_unavailable", "Letters settings unavailable."); }
        }
        if (cancellationToken.IsCancellationRequested
            && TryObservePreCancelledVoiceOff(request))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        var definitelyNoDispatch = TryRejectDefinitelyNoDispatch(request);
        if (definitelyNoDispatch is not null)
            return definitelyNoDispatch;
        if (ownedModelAuthority is not null)
        {
            if (!_modelAuthority.OwnsActiveLease(
                    ownedModelAuthority,
                    _generationId))
            {
                throw new InvalidOperationException(
                    "The borrowed Runtime model-authority lease is not active.");
            }
            return await RouteWithModelAuthorityAsync(
                    request,
                    cancellationToken,
                    ownedModelAuthority)
                .ConfigureAwait(false);
        }

        if (LilithRuntimeCatalogReadPolicy.Contains(request.Method))
            return await RouteIndependentCatalogReadAsync(request, cancellationToken).ConfigureAwait(false);

        var emergencyBorrow = TryBorrowEmergencyRouteAuthority(request.Method);
        if (emergencyBorrow is not null)
        {
            try
            {
                return await RouteWithModelAuthorityAsync(
                        request,
                        cancellationToken,
                        emergencyBorrow)
                    .ConfigureAwait(false);
            }
            finally
            {
                ReleaseEmergencyRouteAuthorityBorrower();
            }
        }

        using var modelAuthority = await _modelAuthority.AcquireAsync(
                _generationId,
                cancellationToken)
            .ConfigureAwait(false);
        PublishActiveRouteAuthority(modelAuthority, request.Method);
        try
        {
            return await RouteWithModelAuthorityAsync(
                    request,
                    cancellationToken,
                    modelAuthority)
                .ConfigureAwait(false);
        }
        finally
        {
            await RetireActiveRouteAuthorityAsync(modelAuthority)
                .ConfigureAwait(false);
        }
    }

    private bool TryObservePreCancelledVoiceOff(LilithRuntimeRequest request)
    {
        if (!string.Equals(
                request.Method,
                "voice.toggle",
                StringComparison.Ordinal))
        {
            return false;
        }
        JsonElement parameters;
        try
        {
            parameters = Canonicalize(request.Method, request.HermesPayload);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or ArgumentException)
        {
            return false;
        }
        var action = parameters.GetProperty("action").GetString();
        if (!string.Equals(action, "off", StringComparison.Ordinal))
            return false;
        var sessionId = OptionalSessionId(parameters);
        if (sessionId is null)
            return false;
        var rejection = TryBeginVoiceRequest(
            request.Method,
            action!,
            sessionId);
        if (rejection is not null)
            return false;
        // No authority lease or RPC was acquired. Preserve the physical
        // microphone cleanup obligation exactly as a gateway-level
        // definitely-not-sent voice.toggle/off would have done.
        RestoreVoiceAfterNotSent(request.Method, action!);
        lock (_sessionLock)
            _voiceRequestInFlight = false;
        return true;
    }

    private LilithRuntimeResponse? TryRejectDefinitelyNoDispatch(
        LilithRuntimeRequest request)
    {
        // These are local, monotonic reservations that prove this call cannot
        // dispatch. Rejecting them before the shared authority gate preserves
        // responsive Stop/voice/checkpoint controls without permitting any
        // stale RPC to cross a concurrent authority publication.
        if (_modelAuthority.Snapshot().State
            == LilithRuntimeModelAuthorityState.Quarantined)
        {
            return null;
        }
        if (!string.Equals(
                request.Method,
                "session.interrupt",
                StringComparison.Ordinal)
            && !string.Equals(
                request.Method,
                "voice.record",
                StringComparison.Ordinal)
            && !string.Equals(
                request.Method,
                "approval.respond",
                StringComparison.Ordinal)
            && !IsConversationMutation(request.Method))
        {
            return null;
        }
        lock (_sessionLock)
        {
            if (string.Equals(
                    request.Method,
                    "session.interrupt",
                    StringComparison.Ordinal)
                && _emergencyInterruptReservations != 0)
            {
                return Failure(
                    request.Method,
                    "runtime.session_interrupt_in_flight",
                    "An interrupt is already in flight for the active Runtime session.");
            }
            if (IsConversationMutation(request.Method)
                && _checkpointRestoreRequestInFlight)
            {
                return Failure(
                    request.Method,
                    "runtime.checkpoint_restore_active",
                    "Wait for the active workspace restore to finish before changing the conversation.");
            }
            if (IsConversationMutation(request.Method)
                && (_voiceRequestInFlight
                    || _voiceState is VoiceLifecycleState.Enabling
                        or VoiceLifecycleState.Starting
                        or VoiceLifecycleState.Recording
                        or VoiceLifecycleState.Stopping
                        or VoiceLifecycleState.Transcribing
                        or VoiceLifecycleState.Disabling
                        or VoiceLifecycleState.Draining))
            {
                return Failure(
                    request.Method,
                    "runtime.voice_capture_active",
                    $"Wait for native voice capture and transcription to become idle (current state: {_voiceState}).");
            }
        }
        JsonElement parameters;
        try
        {
            parameters = Canonicalize(request.Method, request.HermesPayload);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or ArgumentException)
        {
            return null;
        }

        lock (_sessionLock)
        {
            if (string.Equals(
                    request.Method,
                    "approval.respond",
                    StringComparison.Ordinal))
            {
                var approvalSessionId = OptionalSessionId(parameters);
                if (approvalSessionId is not null
                    && _pendingApprovals.TryGetValue(
                        approvalSessionId,
                        out var approvals)
                    && approvals.Count != 0
                    && approvals.Peek().InFlight)
                {
                    return Failure(
                        request.Method,
                        "runtime.pending_native_approval_required",
                        "Only an offered choice for the oldest observed native approval may be answered.");
                }
            }
            if (string.Equals(
                    request.Method,
                    "voice.record",
                    StringComparison.Ordinal)
                && parameters.TryGetProperty("action", out var voiceAction)
                && voiceAction.ValueKind == JsonValueKind.String
                && string.Equals(
                    voiceAction.GetString(),
                    "stop",
                    StringComparison.Ordinal)
                && _voiceRequestInFlight)
            {
                return Failure(
                    request.Method,
                    "runtime.voice_busy",
                    "A native voice request is already in flight.");
            }
        }
        return null;
    }

    private void PublishActiveRouteAuthority(
        LilithRuntimeModelAuthorityLease modelAuthority,
        string method)
    {
        lock (_routeAuthorityBorrowLock)
        {
            if (_activeRouteModelAuthority is not null
                || _emergencyRouteAuthorityBorrowers != 0)
            {
                throw new InvalidOperationException(
                    "The Runtime route authority publication overlapped another owner.");
            }
            _activeRouteModelAuthority = modelAuthority;
            _activeRouteMethod = method;
        }
    }

    private LilithRuntimeModelAuthorityLease? TryBorrowEmergencyRouteAuthority(
        string requestedMethod)
    {
        if (!string.Equals(
                requestedMethod,
                "session.interrupt",
                StringComparison.Ordinal))
        {
            return null;
        }
        LilithRuntimeModelAuthorityLease? borrowed;
        lock (_routeAuthorityBorrowLock)
        {
            borrowed = _activeRouteModelAuthority;
            if (borrowed is null
                || _activeRouteMethod is null
                || !LilithRuntimeEmergencyInterruptPolicy.CanOvertake(
                    _activeRouteMethod))
            {
                return null;
            }
            _emergencyRouteAuthorityBorrowers++;
        }
        if (_modelAuthority.OwnsActiveLease(borrowed, _generationId)
            && borrowed.Snapshot.State == LilithRuntimeModelAuthorityState.Ready)
        {
            return borrowed;
        }
        ReleaseEmergencyRouteAuthorityBorrower();
        return null;
    }

    private async Task RetireActiveRouteAuthorityAsync(
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        Task? borrowersDrained = null;
        lock (_routeAuthorityBorrowLock)
        {
            if (!ReferenceEquals(
                    _activeRouteModelAuthority,
                    modelAuthority))
            {
                throw new InvalidOperationException(
                    "The Runtime route authority owner changed before retirement.");
            }
            _activeRouteModelAuthority = null;
            _activeRouteMethod = null;
            if (_emergencyRouteAuthorityBorrowers != 0)
            {
                _emergencyRouteAuthorityBorrowersDrained ??=
                    new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                borrowersDrained =
                    _emergencyRouteAuthorityBorrowersDrained.Task;
            }
        }
        if (borrowersDrained is not null)
            await borrowersDrained.ConfigureAwait(false);
    }

    private void ReleaseEmergencyRouteAuthorityBorrower()
    {
        TaskCompletionSource? drained = null;
        lock (_routeAuthorityBorrowLock)
        {
            if (_emergencyRouteAuthorityBorrowers <= 0)
            {
                throw new InvalidOperationException(
                    "The Runtime emergency route authority count underflowed.");
            }
            _emergencyRouteAuthorityBorrowers--;
            if (_emergencyRouteAuthorityBorrowers == 0)
            {
                drained = _emergencyRouteAuthorityBorrowersDrained;
                _emergencyRouteAuthorityBorrowersDrained = null;
            }
        }
        drained?.TrySetResult();
    }

    private async Task<LilithRuntimeResponse> RouteWithModelAuthorityAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        if (string.Equals(
                request.Method,
                "session.interrupt",
                StringComparison.Ordinal))
        {
            return await RouteEmergencyInterruptAsync(
                request,
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
        }

        if (!RequiresEmergencyMutationSerialization(request.Method))
        {
            return await RouteOrdinaryAsync(
                request,
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
        }

        var barrierFailure = TryBeginEmergencyBarrier();
        if (barrierFailure is not null)
        {
            return Failure(
                request.Method,
                barrierFailure.Value.Code,
                barrierFailure.Value.Message);
        }
        try
        {
            return await RouteOrdinaryAsync(
                request,
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
        }
        finally
        {
            CompleteEmergencyBarrier();
        }
    }

    private async Task<LilithRuntimeResponse> RouteEmergencyInterruptAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        string sessionId;
        try
        {
            var parameters = Canonicalize(request.Method, request.HermesPayload);
            sessionId = OptionalSessionId(parameters)
                ?? throw new InvalidDataException(
                    "Runtime interrupt requires a session id.");
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                request.Method,
                "runtime.payload_rejected",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Failure(
                request.Method,
                "runtime.payload_rejected",
                exception.Message);
        }

        var reservationFailure = TryReserveEmergencyInterrupt(
            sessionId,
            out var barriersDrained);
        if (reservationFailure is not null)
        {
            return Failure(
                request.Method,
                reservationFailure.Value.Code,
                reservationFailure.Value.Message);
        }

        try
        {
            using var boundedWait = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            boundedWait.CancelAfter(EmergencyInterruptMutationWaitTimeout);
            try
            {
                await barriersDrained.WaitAsync(boundedWait.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    request.Method,
                    "runtime.session_interrupt_deferred",
                    "A protected Runtime mutation did not finish within the bounded stop window; retry Stop.");
            }

            if (!IsEmergencyInterruptReservationCurrent(sessionId))
            {
                return Failure(
                    request.Method,
                    "runtime.active_session_required",
                    "Stop is available only for the exact active Runtime session.");
            }
            return await RouteAllowedMethodAsync(
                request,
                cancellationToken,
                modelAuthority,
                emergencyInterruptReserved: true).ConfigureAwait(false);
        }
        finally
        {
            ReleaseEmergencyInterruptReservation(sessionId);
        }
    }

    private async Task<LilithRuntimeResponse> RouteOrdinaryAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        var sharedModelAuthority = modelAuthority.Snapshot;
        ObserveSharedNativeConfigRevision(sharedModelAuthority);
        if (sharedModelAuthority.GenerationId != _generationId
            || sharedModelAuthority.State
                == LilithRuntimeModelAuthorityState.Retired)
        {
            return Failure(
                request.Method,
                "runtime.native_surface_attestation_required",
                "The Runtime model authority does not belong to this generation.");
        }
        if (sharedModelAuthority.State
            == LilithRuntimeModelAuthorityState.Quarantined)
        {
            return NativeConfigQuarantineFailure(
                request.Method,
                "Hermes native config delivery is uncertain; Runtime restart and profile recovery are required.");
        }
        if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge
            && request.Method is "session.create" or "session.resume"
            && !sharedModelAuthority.HasApprovedDefault)
        {
            return Failure(
                request.Method,
                "runtime.game_model_selection_required",
                "The hidden game conversation requires an authenticated native global model default.");
        }
        lock (_sessionLock)
        {
            if (_sessionActivationQuarantined)
            {
                return Failure(
                    request.Method,
                    "runtime.session_activation_quarantined",
                    "Hermes session activation identity is uncertain; Runtime restart is required.");
            }
            if (_nativeConfigQuarantined)
            {
                return Failure(
                    request.Method,
                    "runtime.native_config_quarantined",
                    "Hermes native config delivery is uncertain; Runtime restart and profile recovery are required.");
            }
            if (_nativeConfigMutationInFlightSessionId is not null)
            {
                return Failure(
                    request.Method,
                    "runtime.native_config_mutation_in_flight",
                    "A permanent Hermes native approval is committing; no new operation may be dispatched.");
            }
        }
        if (!AllowedMethodSet.Contains(request.Method))
        {
            return Failure(
                request.Method,
                "runtime.method_not_allowed",
                "The method is outside the B0 Runtime allowlist.");
        }

        if (!RequiresImageLifecycleSerialization(request.Method))
        {
            return await RouteAllowedMethodAsync(
                request,
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
        }

        await _imageLifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var sessionLifecycleStarted = false;
        try
        {
            if (IsSessionLifecycleMethod(request.Method))
            {
                var rejection = TryBeginSessionLifecycle(request.Method, request.HermesPayload);
                if (rejection is not null)
                {
                    return Failure(
                        request.Method,
                        rejection.Value.Code,
                        rejection.Value.Message);
                }
                sessionLifecycleStarted = true;
            }
            return await RouteAllowedMethodAsync(
                request,
                cancellationToken,
                modelAuthority).ConfigureAwait(false);
        }
        finally
        {
            if (sessionLifecycleStarted)
            {
                lock (_sessionLock)
                {
                    if (_pendingSessionActivation is not
                        { ProjectionPending: true })
                    {
                        _sessionLifecycleInFlight = false;
                        _pendingSessionActivation = null;
                    }
                }
            }
            _imageLifecycleGate.Release();
        }
    }

    private static bool RequiresImageLifecycleSerialization(string method)
        => method is "session.create" or "session.resume" or "session.close"
            or "session.delete"
            or "prompt.submit" or "session.append_assistant_event"
            or "session.extend_native_assistant_event"
            or "session.generate_foreground_proactive"
            or "image.attach_bytes" or "image.detach"
            or "rollback.restore";

    private static bool IsSessionLifecycleMethod(string method)
        => method is "session.create" or "session.resume" or "session.close"
            or "session.delete";

    private static bool RequiresEmergencyMutationSerialization(string method)
        => !string.Equals(method, "session.interrupt", StringComparison.Ordinal)
            && !LilithRuntimeEmergencyInterruptPolicy.CanOvertake(method);

    private (string Code, string Message)? TryBeginEmergencyBarrier()
    {
        lock (_sessionLock)
        {
            if (_emergencyInterruptReservations != 0)
            {
                return (
                    "runtime.session_interrupt_pending",
                    "An admitted stop request must finish before another state mutation may begin.");
            }
            _emergencyBarrierRoutesInFlight = checked(
                _emergencyBarrierRoutesInFlight + 1);
            if (_emergencyBarrierRoutesInFlight == 1)
            {
                _emergencyBarrierDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return null;
        }
    }

    private void CompleteEmergencyBarrier()
    {
        lock (_sessionLock)
        {
            if (_emergencyBarrierRoutesInFlight <= 0
                || _emergencyBarrierDrained is null)
            {
                throw new InvalidOperationException(
                    "Runtime emergency barrier accounting was lost.");
            }
            _emergencyBarrierRoutesInFlight--;
            if (_emergencyBarrierRoutesInFlight != 0)
                return;
            var drained = _emergencyBarrierDrained;
            _emergencyBarrierDrained = null;
            drained.TrySetResult();
        }
    }

    private (string Code, string Message)? TryReserveEmergencyInterrupt(
        string sessionId,
        out Task barriersDrained)
    {
        barriersDrained = Task.CompletedTask;
        lock (_sessionLock)
        {
            if (_sessionActivationQuarantined)
            {
                return (
                    "runtime.session_activation_quarantined",
                    "Hermes session activation identity is uncertain; Runtime restart is required.");
            }
            if (_sessionDeleteQuarantined
                || _sessionLifecycleInFlight
                || _pendingSessionActivation is not null)
            {
                return (
                    "runtime.session_lifecycle_uncertain",
                    "The active Runtime session identity is changing or uncertain; Stop was not dispatched.");
            }
            if (!string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                return (
                    "runtime.active_session_required",
                    "Stop is available only for the exact active Runtime session.");
            }
            if (!HasEmergencyInterruptSurfaceAuthorityUnsafe(sessionId))
            {
                return (
                    "runtime.active_session_required",
                    "Stop is available only for the exact active Runtime session.");
            }
            if (_emergencyInterruptReservations != 0)
            {
                return (
                    "runtime.session_interrupt_in_flight",
                    "An exact stop request is already in flight for the active Runtime session.");
            }

            _emergencyInterruptSessionId = sessionId;
            _emergencyInterruptReservations = 1;
            barriersDrained = _emergencyBarrierRoutesInFlight == 0
                ? Task.CompletedTask
                : _emergencyBarrierDrained?.Task
                    ?? throw new InvalidOperationException(
                        "Runtime emergency barrier signal was lost.");
            return null;
        }
    }

    private bool IsEmergencyInterruptReservationCurrent(string sessionId)
    {
        lock (_sessionLock)
        {
            return _emergencyInterruptReservations > 0
                && string.Equals(
                    _emergencyInterruptSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && IsEmergencyInterruptIdentityAdmissibleUnsafe(sessionId);
        }
    }

    private bool IsEmergencyInterruptIdentityAdmissibleUnsafe(string sessionId)
        => !_sessionActivationQuarantined
            && !_sessionDeleteQuarantined
            && !_sessionLifecycleInFlight
            && _pendingSessionActivation is null
            && string.Equals(
                _activeSessionId,
                sessionId,
                StringComparison.Ordinal)
            && HasEmergencyInterruptSurfaceAuthorityUnsafe(sessionId);

    private bool HasEmergencyInterruptSurfaceAuthorityUnsafe(string sessionId)
        => _nativeConfigQuarantined
            || string.Equals(
                _nativeConfigInvalidatedSessionId,
                sessionId,
                StringComparison.Ordinal)
            || (_sessionStates.TryGetValue(sessionId, out var state)
                && state == SessionNativeSurfaceState.Attested);

    private void ReleaseEmergencyInterruptReservation(string sessionId)
    {
        lock (_sessionLock)
        {
            if (_emergencyInterruptReservations <= 0
                || !string.Equals(
                    _emergencyInterruptSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime stop reservation identity was lost.");
            }
            _emergencyInterruptReservations--;
            if (_emergencyInterruptReservations == 0)
                _emergencyInterruptSessionId = null;
        }
    }

    private (string Code, string Message)? TryBeginSessionLifecycle(
        string method,
        JsonElement parameters)
    {
        lock (_sessionLock)
        {
            if (_sessionActivationQuarantined)
            {
                return (
                    "runtime.session_activation_quarantined",
                    "Hermes session activation identity is uncertain; Runtime restart is required.");
            }
            if (_sessionDeleteQuarantined)
            {
                return (
                    "runtime.session_delete_quarantined",
                    "A session-delete outcome is uncertain; Runtime restart is required before changing sessions.");
            }
            if (_sessionLifecycleInFlight)
            {
                return (
                    "runtime.session_lifecycle_busy",
                    "A session transition is already awaiting its canonical projection.");
            }
            if (_checkpointRestoreRequestInFlight)
            {
                return (
                    "runtime.checkpoint_restore_active",
                    "Wait for the active workspace restore to finish before changing sessions.");
            }
            if (_voiceState == VoiceLifecycleState.Quarantined)
            {
                return (
                    "runtime.voice_quarantined",
                    "Voice state is uncertain; Runtime restart is required before changing sessions.");
            }
            var canSwitchIdleVoiceOwner = method is
                    "session.create" or "session.resume"
                && _sessionSurface == LilithRuntimeSessionSurface.Workbench
                && _voiceState == VoiceLifecycleState.Ready;
            if (_voiceState != VoiceLifecycleState.Off
                && !canSwitchIdleVoiceOwner
                || _voiceRequestInFlight)
            {
                return (
                    "runtime.voice_lifecycle_blocked",
                    "Turn native voice mode off before changing sessions.");
            }
            LilithRuntimeVoiceCaptureOwner? readyVoiceOwner = null;
            if (canSwitchIdleVoiceOwner)
            {
                if (!_voiceCaptureArbiter.TryGetOwner(out var owner, out var phase)
                    || phase != LilithRuntimeVoiceCapturePhase.Ready
                    || owner.GenerationId != _generationId
                    || owner.Surface != _sessionSurface
                    || !string.Equals(owner.SessionId, _voiceSessionId, StringComparison.Ordinal)
                    || owner.CaptureId != _voiceCaptureId)
                {
                    return (
                        "runtime.voice_lifecycle_blocked",
                        "The idle voice owner changed before the session transition.");
                }
                readyVoiceOwner = owner;
            }
            _sessionLifecycleInFlight = true;
            if (method is "session.create" or "session.resume")
            {
                // This is only an exclusion hint until Canonicalize validates
                // the request; it cannot authorize a session or its events.
                var requestedStoredSessionId = method == "session.resume"
                    && parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("session_id", out var requested)
                    && requested.ValueKind == JsonValueKind.String
                        ? requested.GetString() : null;
                _pendingSessionActivation = new PendingSessionActivation(
                    method, readyVoiceOwner, requestedStoredSessionId);
            }
            return null;
        }
    }

    private bool CanRouteForImageLifecycle(string method, string? sessionId)
    {
        lock (_sessionLock)
        {
            if (_activeSessionId is null
                || !_sessionImages.TryGetValue(_activeSessionId, out var lifecycle))
            {
                return true;
            }

            if (method == "session.resume"
                && sessionId is not null
                && _sessionImages.TryGetValue(sessionId, out var resumed)
                && resumed.State != SessionImageLifecycleState.Idle)
            {
                return false;
            }

            if (lifecycle.State == SessionImageLifecycleState.Idle)
                return true;

            if (lifecycle.State == SessionImageLifecycleState.Attached)
            {
                if (method is "session.create" or "session.resume" or "image.attach_bytes")
                    return false;
                return method is not ("session.close" or "session.delete")
                    || string.Equals(
                        sessionId,
                        _activeSessionId,
                        StringComparison.Ordinal);
            }

            return (method is "session.close" or "session.delete")
                && string.Equals(
                    sessionId,
                    _activeSessionId,
                    StringComparison.Ordinal);
        }
    }

    private async Task<LilithRuntimeResponse> RouteAllowedMethodAsync(
        LilithRuntimeRequest request,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority,
        bool emergencyInterruptReserved = false)
    {
        try
        {
            if (request.Method is "session.create" or "session.resume"
                    or "rollback.list" or "rollback.diff" or "rollback.restore"
                && !TryEnsureWorkspacePathSafe())
            {
                return Failure(
                    request.Method,
                    "runtime.workspace_path_unsafe",
                    "The fixed Runtime workspace path could not be safely verified.");
            }
            var parameters = Canonicalize(request.Method, request.HermesPayload);
            if (request.Method is "tools.settings.get" or "tools.settings.set")
                return await RouteNativeSettingsAsync(request.Method, parameters, modelAuthority, cancellationToken).ConfigureAwait(false);
            if (HermesProviderSetupContract.IsMethod(request.Method))
                return await RouteProviderSetupAsync(request.Method, parameters,
                    modelAuthority, cancellationToken).ConfigureAwait(false);
            if (request.Method is HermesUsageBudgetContract.GetMethod or HermesUsageBudgetContract.SetMethod
                or HermesUsageBudgetPreviewContract.PreviewMethod or HermesLatestUsageContract.GetMethod)
                return await RouteUsageBudgetAsync(request.Method, parameters, cancellationToken).ConfigureAwait(false);
            if (request.Method == HermesCommandCatalogContract.Method)
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                    return Failure(request.Method, "runtime.workbench_surface_required",
                        "Command discovery requires the authenticated Workbench surface.");
                var native = await _requestAsync("commands.catalog", parameters, cancellationToken).ConfigureAwait(false);
                return Success(request.Method, JsonSerializer.SerializeToElement(
                    HermesCapabilityCatalogAssembler.AssembleCommands(native)));
            }
            if (string.Equals(request.Method, "runtime.status", StringComparison.Ordinal))
            {
                return await RouteRuntimeStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    HermesCapabilityCatalogContract.Method,
                    StringComparison.Ordinal))
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                {
                    return Failure(
                        request.Method,
                        "runtime.workbench_surface_required",
                        "The full Hermes capability catalog is available only from the Workbench control surface.");
                }
                return await RouteCapabilityCatalogAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            if (IsExpressionManagementRpc(request.Method))
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                    return Failure(request.Method, "runtime.workbench_surface_required",
                        "Expression editing requires the authenticated Workbench surface.");
                return await RouteExpressionManagementAsync(request.Method, parameters, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (IsMemoryManagementRpc(request.Method))
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                {
                    return Failure(
                        request.Method,
                        "runtime.workbench_surface_required",
                        "Durable memory management is available only from the Workbench control surface.");
                }
                return await RouteMemoryManagementAsync(
                    request.Method,
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "tools.catalog", StringComparison.Ordinal))
            {
                var nativeToolsSessionId = OptionalSessionId(parameters);
                if (nativeToolsSessionId is null
                    || !IsActiveNativeToolsCatalogSession(nativeToolsSessionId))
                {
                    return Failure(
                        request.Method,
                        "runtime.active_session_required",
                        "Native tools may be read only for the active attested or newly pending session.");
                }
                return await RouteNativeToolsCatalogAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "tools.set", StringComparison.Ordinal))
            {
                var nativeToolsSessionId = OptionalSessionId(parameters);
                if (nativeToolsSessionId is null
                    || !IsActiveAttestedSession(nativeToolsSessionId))
                {
                    return Failure(
                        request.Method,
                        "runtime.active_attested_session_required",
                        "Native tools may be changed only for the active attested session.");
                }
                return await RouteNativeToolsSetAsync(
                    parameters,
                    cancellationToken,
                    modelAuthority).ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    "model.default.set",
                    StringComparison.Ordinal))
            {
                if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                {
                    return Failure(
                        request.Method,
                        "runtime.workbench_surface_required",
                        "The native default model may be changed only from the Workbench control surface.");
                }
                return await RouteNativeModelDefaultSetAsync(
                    parameters,
                    cancellationToken,
                    modelAuthority).ConfigureAwait(false);
            }

            var sessionId = OptionalSessionId(parameters);
            if (string.Equals(
                    request.Method,
                    "prompt.project_speech",
                    StringComparison.Ordinal)
                && _sessionSurface != LilithRuntimeSessionSurface.GameBridge)
            {
                return Failure(
                    request.Method,
                    "runtime.game_bridge_surface_required",
                    "Speech projection is available only for the hidden game conversation.");
            }
            if (string.Equals(
                    request.Method,
                    "session.interrupt",
                    StringComparison.Ordinal)
                && (!emergencyInterruptReserved
                    || sessionId is null
                    || !IsEmergencyInterruptReservationCurrent(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.active_session_required",
                    "Stop is available only for the exact reserved active Runtime session.");
            }
            if (!emergencyInterruptReserved
                && !CanRouteForImageLifecycle(request.Method, sessionId)
                && !IsOwnedVoiceOffCleanup(
                    request.Method,
                    parameters,
                    sessionId))
            {
                return Failure(
                    request.Method,
                    "runtime.image_lifecycle_blocked",
                    "The active session image lifecycle must be resolved by an exact session close or Runtime restart.");
            }
            if (string.Equals(request.Method, "approval.respond", StringComparison.Ordinal))
            {
                return await RouteApprovalResponseAsync(
                    parameters,
                    cancellationToken,
                    modelAuthority).ConfigureAwait(false);
            }

            if (request.Method == "config.set"
                && sessionId is not null && !IsSessionAttested(sessionId))
            {
                return await RoutePendingCredentialModelRecoveryAsync(
                    request, parameters, sessionId, modelAuthority, cancellationToken)
                    .ConfigureAwait(false);
            }

            if ((request.Method is "prompt.submit" or "prompt.project_speech" or "prompt.draft_note"
                    or "session.append_assistant_event"
                    or "session.extend_native_assistant_event"
                    or "session.generate_foreground_proactive"
                    or "config.set" or "session.compress"
                    or "session.usage" or "session.context_breakdown")
                && (sessionId is null || !IsSessionAttested(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.native_surface_attestation_required",
                    "The session has not proved the exact approved native tool and empty MCP surface.");
            }
            if (request.Method is "session.usage" or "session.context_breakdown"
                && !string.Equals(sessionId, ActiveSessionId, StringComparison.Ordinal))
            {
                return Failure(
                    request.Method,
                    "runtime.active_session_required",
                    "Observability is available only for the active attested session.");
            }
            if (request.Method is "prompt.submit" or "prompt.project_speech" or "prompt.draft_note"
                    or "session.append_assistant_event"
                    or "session.extend_native_assistant_event"
                    or "session.generate_foreground_proactive"
                && (sessionId is null || !IsActiveAttestedSession(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.active_attested_session_required",
                    "Prompts are available only for the active attested session.");
            }
            if (request.Method is "clarify.respond" or "sudo.respond" or "secret.respond"
                && (sessionId is null || !IsActiveAttestedSession(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.active_attested_session_required",
                    "Interactive responses are available only for the active attested session.");
            }
            if (string.Equals(request.Method, "learning.frames", StringComparison.Ordinal))
            {
                var activeSession = ActiveSessionId;
                if (activeSession is null || !IsSessionAttested(activeSession))
                {
                    return Failure(
                        request.Method,
                        "runtime.native_surface_attestation_required",
                        "The active session has not proved the exact approved native tool and empty MCP surface.");
                }
            }

            if (request.Method is "image.attach_bytes" or "image.detach"
                && (sessionId is null || !IsActiveAttestedSession(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.active_attested_session_required",
                    "Image attachments are available only for the active attested session.");
            }
            if (IsDelegationRpc(request.Method)
                && (sessionId is null || !IsActiveAttestedSession(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.active_attested_session_required",
                    "Native delegation is available only for the active attested session.");
            }
            if (IsVoiceRpc(request.Method)
                && (sessionId is null || !IsActiveAttestedSession(sessionId))
                && !IsOwnedVoiceOffCleanup(
                    request.Method,
                    parameters,
                    sessionId))
            {
                return Failure(
                    request.Method,
                    "runtime.active_attested_session_required",
                    "Native voice is available only for the active attested session.");
            }
            if (IsCheckpointRpc(request.Method)
                && (sessionId is null
                    || !IsActiveCheckpointWorkspaceAttested(sessionId)))
            {
                return Failure(
                    request.Method,
                    "runtime.checkpoint_workspace_attestation_required",
                    "Workspace rollback is available only for the active attested Runtime workspace.");
            }
            if (IsConversationMutation(request.Method)
                && VoiceBlocksConversationMutation())
            {
                var voiceState = VoiceLifecycleStateName;
                return Failure(
                    request.Method,
                    "runtime.voice_capture_active",
                    $"Wait for native voice capture and transcription to become idle (current state: {voiceState}).");
            }
            if (IsConversationMutation(request.Method)
                && CheckpointRestoreBlocksConversationMutation())
            {
                return Failure(
                    request.Method,
                    "runtime.checkpoint_restore_active",
                    "Wait for the active workspace restore to finish before changing the conversation.");
            }
            if (IsVoiceRpc(request.Method))
            {
                return await RouteVoiceAsync(
                    request.Method,
                    parameters,
                    cancellationToken,
                    modelAuthority).ConfigureAwait(false);
            }
            if (IsCheckpointRpc(request.Method))
            {
                return await RouteCheckpointAsync(
                    request.Method,
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "image.detach", StringComparison.Ordinal))
            {
                return await RouteImageDetachAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "image.attach_bytes", StringComparison.Ordinal))
            {
                return await RouteImageAttachAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "prompt.submit", StringComparison.Ordinal))
            {
                return await RoutePromptSubmitAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    "session.append_assistant_event",
                    StringComparison.Ordinal))
            {
                return await RouteNativeAssistantEventAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    "session.extend_native_assistant_event",
                    StringComparison.Ordinal))
            {
                return await RouteNativeAssistantContinuationAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    "session.generate_foreground_proactive",
                    StringComparison.Ordinal))
            {
                return await RouteForegroundProactiveAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "prompt.draft_note", StringComparison.Ordinal))
            {
                return await RouteNoteDraftAsync(parameters, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (string.Equals(
                    request.Method,
                    "prompt.project_speech",
                    StringComparison.Ordinal))
            {
                return await RouteSpeechProjectionAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "session.close", StringComparison.Ordinal))
            {
                return await RouteSessionCloseAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "session.delete", StringComparison.Ordinal))
            {
                return await RouteSessionDeleteAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (string.Equals(request.Method, "session.title", StringComparison.Ordinal))
            {
                return await RouteSessionTitleAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }
            if (IsDelegationControlRpc(request.Method))
            {
                return await RouteDelegationControlAsync(
                    request.Method,
                    parameters,
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsGenerationMutationRpc(request.Method))
                _credentialMutationDispatched(request.Method);
            var result = await _requestAsync(
                request.Method,
                parameters,
                cancellationToken).ConfigureAwait(false);
            if (IsCredentialRpc(request.Method))
            {
                result = SanitizeCredentialResult(
                    request.Method,
                    parameters,
                    result);
            }
            if (IsTelegramMessagingRpc(request.Method))
            {
                result = SanitizeTelegramMessagingResult(
                    request.Method,
                    parameters,
                    result);
            }
            if (IsDelegationRpc(request.Method))
                ValidateDelegationResult(request.Method, parameters, result);
            if (string.Equals(
                    request.Method,
                    "session.interrupt",
                    StringComparison.Ordinal))
            {
                ValidateSessionInterruptResult(result);
            }
            ObserveGatewayResult(request.Method, parameters, result);
            if (string.Equals(request.Method, "model.options", StringComparison.Ordinal))
                result = IntersectModelOptions(result);
            return Success(request.Method, result);
        }
        catch (LilithNativeConfigMutationUncertainException exception)
        {
            return NativeConfigQuarantineFailure(
                request.Method,
                exception.Message);
        }
        catch (LilithSessionActivationEventException exception)
        {
            return Failure(
                request.Method,
                "runtime.session_activation_event_rejected",
                exception.Message);
        }
        catch (LilithGameSessionLineageRejectedException exception)
        {
            return Failure(
                request.Method,
                "runtime.game_session_lineage_rejected",
                exception.Message);
        }
        catch (LilithGameModelSelectionRequiredException exception)
        {
            return Failure(
                request.Method,
                "runtime.game_model_selection_required",
                exception.Message);
        }
        catch (HermesRpcException exception) when (exception.Code == 5035)
        {
            QuarantineNativeConfig(modelAuthority);
            return Failure(
                request.Method,
                "runtime.native_config_quarantined",
                "Hermes native config authority failed; Runtime restart and profile recovery are required.",
                exception.Code);
        }
        catch (HermesRpcException exception)
            when (IsCredentialMutationRpc(request.Method)
                  && IsDefiniteCredentialRejectionCode(exception.Code))
        {
            return Failure(
                request.Method,
                "hermes.credential_rejected",
                "Hermes rejected the provider credential operation.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (IsCredentialRpc(request.Method))
        {
            // Hermes 5037/5038 intentionally cover both a completed rollback
            // and catastrophic rollback-incomplete failure. The redacted RPC
            // boundary cannot distinguish those outcomes, so only structural
            // prevalidation/guaranteed-rollback codes may claim rejection.
            return Failure(
                request.Method,
                "hermes.credential_result_uncertain",
                "Hermes could not prove the provider credential outcome.",
                exception.Code);
        }
        catch (HermesRpcException exception)
            when (IsTelegramMessagingRpc(request.Method))
        {
            var uncertain = IsTelegramMessagingMutationRpc(request.Method)
                || request.Method == TelegramVoiceSetMethod;
            return Failure(
                request.Method,
                uncertain
                    ? "hermes.messaging_result_uncertain"
                    : "hermes.messaging_rejected",
                uncertain
                    ? "Hermes could not prove the Telegram configuration outcome."
                    : "Hermes rejected the Telegram operation.",
                exception.Code);
        }
        catch (HermesRpcException exception)
            when (IsMemoryManagementRpc(request.Method))
        {
            // Memory-provider failures can originate in local persistence and
            // may contain paths or private provider diagnostics.  Preserve the
            // closed Hermes code, but never project its free-form text across
            // the public Runtime boundary.
            return Failure(
                request.Method,
                "memory.operation_failed",
                "Hermes rejected the memory operation.",
                exception.Code);
        }
        catch (HermesRpcException exception)
        {
            return Failure(
                request.Method,
                "hermes.rpc_rejected",
                exception.Message,
                exception.Code);
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                request.Method,
                "runtime.payload_rejected",
                exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Failure(
                request.Method,
                "runtime.payload_rejected",
                exception.Message);
        }
        catch (HermesRequestNotSentException exception)
            when (request.Method is "session.create" or "session.resume")
        {
            return Failure(
                request.Method,
                "runtime.session_activation_not_sent",
                exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            QuarantineUncertainSessionActivation(request.Method);
            throw;
        }
        catch (Exception)
        {
            if (QuarantineUncertainSessionActivation(request.Method))
            {
                return Failure(
                    request.Method,
                    "runtime.session_activation_quarantined",
                    "Hermes session activation may have crossed the send boundary; Runtime restart and exact reconciliation are required.");
            }
            return Failure(
                request.Method,
                "runtime.gateway_failed",
                "The Runtime gateway request failed.");
        }
    }

    private bool QuarantineUncertainSessionActivation(string method)
    {
        if (method is not ("session.create" or "session.resume"))
            return false;
        lock (_sessionLock)
        {
            if (_pendingSessionActivation is null
                || !string.Equals(
                    _pendingSessionActivation.Method,
                    method,
                    StringComparison.Ordinal))
            {
                return false;
            }
            _sessionActivationQuarantined = true;
            return true;
        }
    }

    private async Task<LilithRuntimeResponse> RouteMemoryManagementAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var result = await _requestAsync(
            "memory.provider.manage",
            JsonSerializer.SerializeToElement(new
            {
                action = MemoryManagementAction(method),
                request = parameters
            }),
            cancellationToken).ConfigureAwait(false);
        if (TryDecodeMemoryProviderFailure(
                result,
                out var failureCode,
                out var failureMessage))
        {
            return Failure(method, failureCode, failureMessage);
        }
        ValidateMemoryManagementResult(method, parameters, result);
        return Success(method, result);
    }

    private static bool TryDecodeMemoryProviderFailure(
        JsonElement result,
        out string code,
        out string message)
    {
        code = string.Empty;
        message = string.Empty;
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("code", out _))
        {
            return false;
        }
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "code",
            "message");
        ValidateMemorySchema(values);
        code = RequiredTrimmedMemoryResultText(values, "code", 128);
        if (code is not (
                "memory.invalid_request" or "memory.revision_conflict"
                or "memory.entry_not_found" or "memory.secret_rejected"
                or "memory.export_failed" or "memory.operation_failed"))
        {
            throw new InvalidDataException(
                "Hermes memory failure code was rejected.");
        }
        _ = RequiredTrimmedMemoryResultText(values, "message", 1024);
        message = code switch
        {
            "memory.invalid_request" => "Hermes rejected the memory request.",
            "memory.revision_conflict" => "The memory catalog changed; refresh and try again.",
            "memory.entry_not_found" => "The selected memory no longer exists.",
            "memory.secret_rejected" => "Sensitive credentials cannot be stored as memory.",
            "memory.export_failed" => "Hermes could not create the memory export.",
            _ => "Hermes could not complete the memory operation."
        };
        return true;
    }

    private static string MemoryManagementAction(string method)
        => method switch
        {
            "memory.catalog" => "catalog",
            "memory.entry.add" => "entry.add",
            "memory.entry.pin.set" => "entry.pin.set",
            "memory.entry.promote" => "entry.promote",
            "memory.entry.correct" => "entry.correct",
            "memory.entry.visibility.set" => "entry.visibility.set",
            "memory.entry.reject" => "entry.reject",
            "memory.entry.delete" => "entry.delete",
            "memory.preferences.get" => "preferences.get",
            "memory.preferences.set" => "preferences.set",
            "memory.export" => "export",
            _ => throw new InvalidDataException(
                "Runtime memory management method was rejected.")
        };

    private static void ValidateMemoryManagementResult(
        string method,
        JsonElement request,
        JsonElement result)
    {
        switch (method)
        {
            case "memory.catalog":
                ValidateMemoryCatalogResult(request, result);
                return;
            case "memory.preferences.get":
            case "memory.preferences.set":
                ValidateMemoryPreferencesResult(method, request, result);
                return;
            case "memory.entry.add":
            case "memory.entry.pin.set":
            case "memory.entry.promote":
            case "memory.entry.correct":
            case "memory.entry.visibility.set":
            case "memory.entry.reject":
                ValidateMemoryMutationResult(method, request, result);
                return;
            case "memory.entry.delete":
                ValidateMemoryDeleteResult(request, result);
                return;
            case "memory.export":
                ValidateMemoryExportResult(request, result);
                return;
            default:
                throw new InvalidDataException(
                    "Runtime memory management method was rejected.");
        }
    }

    private static void ValidateMemoryCatalogResult(
        JsonElement request,
        JsonElement result)
    {
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "catalog_revision",
            "export_supported",
            "entries",
            "next_offset");
        ValidateMemorySchema(values);
        RequiredInt64(values, "catalog_revision", 0, long.MaxValue);
        RequiredBoolean(values, "export_supported");
        var entries = RequiredMemoryArray(values, "entries");
        if (entries.GetArrayLength() > MaximumMemoryCatalogEntries)
        {
            throw new InvalidDataException(
                "Hermes memory catalog exceeded the Runtime limit.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            var entryId = ValidateMemoryEntryResult(entry);
            if (!ids.Add(entryId))
            {
                throw new InvalidDataException(
                    "Hermes memory catalog contained duplicate entries.");
            }
        }

        var offset = request.TryGetProperty("offset", out var offsetValue)
            ? offsetValue.GetInt32()
            : 0;
        var nextOffset = values["next_offset"];
        if (nextOffset.ValueKind == JsonValueKind.Null)
            return;
        if (nextOffset.ValueKind != JsonValueKind.Number
            || !nextOffset.TryGetInt32(out var decodedNextOffset)
            || decodedNextOffset <= offset
            || decodedNextOffset > MaximumMemoryManagedEntries
            || decodedNextOffset != offset + entries.GetArrayLength())
        {
            throw new InvalidDataException(
                "Hermes memory catalog continuation was rejected.");
        }
    }

    private static void ValidateMemoryPreferencesResult(
        string method,
        JsonElement request,
        JsonElement result)
    {
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "preferences_revision",
            "auto_memory_enabled");
        ValidateMemorySchema(values);
        var revision = RequiredInt64(
            values,
            "preferences_revision",
            0,
            long.MaxValue);
        var enabled = RequiredBoolean(values, "auto_memory_enabled");
        if (method == "memory.preferences.set"
            && (revision != ExpectedMemoryRevisionSuccessor(
                    request,
                    "expected_preferences_revision")
                || enabled != request.GetProperty("auto_memory_enabled").GetBoolean()))
        {
            throw new InvalidDataException(
                "Hermes memory preferences result did not match the request.");
        }
    }

    private static void ValidateMemoryMutationResult(
        string method,
        JsonElement request,
        JsonElement result)
    {
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "operation_id",
            "catalog_revision",
            "entry");
        ValidateMemorySchema(values);
        ValidateMemoryResultOperationId(request, values);
        var catalogRevision = RequiredInt64(
            values,
            "catalog_revision",
            0,
            long.MaxValue);
        if (catalogRevision != ExpectedMemoryRevisionSuccessor(
                request,
                "expected_catalog_revision"))
        {
            throw new InvalidDataException(
                "Hermes memory catalog revision did not match the request.");
        }

        var entry = RequiredMemoryObject(values, "entry");
        var entryId = ValidateMemoryEntryResult(entry);
        if (method != "memory.entry.add")
        {
            if (!string.Equals(
                    entryId,
                    request.GetProperty("entry_id").GetString(),
                    StringComparison.Ordinal)
                || entry.GetProperty("entry_version").GetInt64()
                    != ExpectedMemoryRevisionSuccessor(
                        request,
                        "expected_entry_version"))
            {
                throw new InvalidDataException(
                    "Hermes memory mutation result did not match the selected entry.");
            }
        }
        else if (entry.GetProperty("entry_version").GetInt64() != 1
            || entry.GetProperty("state").GetString() != "active"
            || entry.GetProperty("source").GetString() != "operator"
            || entry.GetProperty("category").GetString()
                != request.GetProperty("category").GetString()
            || entry.GetProperty("content").GetString()
                != request.GetProperty("content").GetString()
            || entry.GetProperty("pinned").GetBoolean()
                != request.GetProperty("pinned").GetBoolean())
        {
            throw new InvalidDataException(
                "Hermes memory add result did not match the request.");
        }

        var resultState = entry.GetProperty("state").GetString();
        var requestMatched = method switch
        {
            "memory.entry.pin.set" =>
                entry.GetProperty("pinned").GetBoolean()
                    == request.GetProperty("pinned").GetBoolean(),
            "memory.entry.promote" =>
                entry.GetProperty("category").GetString()
                    == request.GetProperty("target_category").GetString()
                && entry.GetProperty("expires_at_utc").ValueKind
                    == JsonValueKind.Null
                && !entry.GetProperty("pinned").GetBoolean(),
            "memory.entry.correct" =>
                entry.GetProperty("content").GetString()
                    == request.GetProperty("replacement_content").GetString(),
            "memory.entry.visibility.set" =>
                resultState == (request.GetProperty("hidden").GetBoolean()
                    ? "hidden"
                    : "active"),
            "memory.entry.reject" => resultState == "rejected"
                && !entry.GetProperty("pinned").GetBoolean(),
            _ => true
        };
        if (!requestMatched)
        {
            throw new InvalidDataException(
                "Hermes memory mutation outcome did not match the request.");
        }
    }

    private static void ValidateMemoryDeleteResult(
        JsonElement request,
        JsonElement result)
    {
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "operation_id",
            "catalog_revision",
            "entry_id",
            "deleted");
        ValidateMemorySchema(values);
        ValidateMemoryResultOperationId(request, values);
        var catalogRevision = RequiredInt64(
            values,
            "catalog_revision",
            0,
            long.MaxValue);
        var entryId = RequiredMemoryEntryId(values, "entry_id");
        if (!string.Equals(
                entryId,
                request.GetProperty("entry_id").GetString(),
                StringComparison.Ordinal)
            || catalogRevision != ExpectedMemoryRevisionSuccessor(
                request,
                "expected_catalog_revision")
            || !RequiredBoolean(values, "deleted"))
        {
            throw new InvalidDataException(
                "Hermes memory deletion result did not match the request.");
        }
    }

    private static long ExpectedMemoryRevisionSuccessor(
        JsonElement request,
        string propertyName)
    {
        var current = request.GetProperty(propertyName).GetInt64();
        if (current == long.MaxValue)
        {
            throw new InvalidDataException(
                "Runtime memory revision cannot be advanced.");
        }
        return current + 1;
    }

    private static void ValidateMemoryExportResult(
        JsonElement request,
        JsonElement result)
    {
        var values = ReadExactMemoryResult(
            result,
            "schema_version",
            "operation_id",
            "path",
            "sha256",
            "entry_count",
            "created_at_utc");
        ValidateMemorySchema(values);
        ValidateMemoryResultOperationId(request, values);
        var path = RequiredTrimmedMemoryResultText(
            values,
            "path",
            MaximumMemoryExportPathUtf8Bytes);
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Hermes memory export path was rejected.");
        var sha256 = RequiredTrimmedMemoryResultText(values, "sha256", 64);
        if (sha256.Length != 64 || sha256.Any(static value => !Uri.IsHexDigit(value)))
            throw new InvalidDataException("Hermes memory export hash was rejected.");
        RequiredInteger(values, "entry_count", 0, MaximumMemoryManagedEntries);
        _ = RequiredMemoryTimestamp(values, "created_at_utc");
    }

    private static string ValidateMemoryEntryResult(JsonElement entry)
    {
        var values = ReadExactMemoryResult(
            entry,
            "entry_id",
            "entry_version",
            "category",
            "state",
            "content",
            "source",
            "observed_at_utc",
            "updated_at_utc",
            "expires_at_utc",
            "pinned",
            "evidence_excerpt");
        var entryId = RequiredMemoryEntryId(values, "entry_id");
        RequiredInt64(values, "entry_version", 1, long.MaxValue);
        var category = ValidateMemoryCategory(
            RequiredTrimmedMemoryResultText(values, "category", 64),
            allowCandidate: true,
            allowTemporary: true);
        ValidateMemoryState(RequiredTrimmedMemoryResultText(values, "state", 64));
        _ = RequiredMemoryResultContent(values, "content", MaximumMemoryContentUtf8Bytes);
        ValidateMemorySource(RequiredTrimmedMemoryResultText(values, "source", 64));
        var observed = RequiredMemoryTimestamp(values, "observed_at_utc");
        var updated = RequiredMemoryTimestamp(values, "updated_at_utc");
        if (updated < observed)
            throw new InvalidDataException("Hermes memory timestamps were rejected.");
        _ = OptionalMemoryTimestamp(values, "expires_at_utc");
        var pinned = RequiredBoolean(values, "pinned");
        if (pinned && category != "relationship_core")
            throw new InvalidDataException("Hermes returned an invalid pinned memory.");
        _ = OptionalMemoryResultContent(
            values,
            "evidence_excerpt",
            MaximumMemoryEvidenceUtf8Bytes);
        return entryId;
    }

    private static Dictionary<string, JsonElement> ReadExactMemoryResult(
        JsonElement value,
        params string[] allowedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes memory result was rejected.");
        var allowed = allowedProperties.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException(
                    "Hermes memory result contained an unknown or duplicate field.");
            }
        }
        if (values.Count != allowed.Count
            || allowed.Any(name => !values.ContainsKey(name)))
        {
            throw new InvalidDataException("Hermes memory result was incomplete.");
        }
        return values;
    }

    private static void ValidateMemorySchema(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        if (!values.TryGetValue("schema_version", out var schema)
            || schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var version)
            || version != 1)
        {
            throw new InvalidDataException("Hermes memory schema was rejected.");
        }
    }

    private static JsonElement RequiredMemoryArray(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Hermes memory result requires '{name}'.");
        }
        return value;
    }

    private static JsonElement RequiredMemoryObject(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Hermes memory result requires '{name}'.");
        }
        return value;
    }

    private static void ValidateMemoryResultOperationId(
        JsonElement request,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var operationId = RequiredTrimmedMemoryResultText(
            values,
            "operation_id",
            MaximumMemoryOperationIdUtf8Bytes);
        ValidateMemoryOperationId(operationId);
        if (!string.Equals(
                operationId,
                request.GetProperty("operation_id").GetString(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes memory operation result did not match the request.");
        }
    }

    private static string RequiredMemoryEntryId(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var entryId = RequiredTrimmedMemoryResultText(
            values,
            name,
            MaximumMemoryEntryIdUtf8Bytes);
        ValidateMemoryEntryId(entryId);
        return entryId;
    }

    private static string RequiredTrimmedMemoryResultText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text)
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Any(char.IsControl)
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Hermes memory result field '{name}' was rejected.");
        }
        return text;
    }

    private static string RequiredMemoryResultContent(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text)
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Hermes memory result field '{name}' was rejected.");
        }
        return text;
    }

    private static string? OptionalMemoryResultContent(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException("Hermes memory result was incomplete.");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return RequiredMemoryResultContent(values, name, maximumUtf8Bytes);
    }

    private static DateTimeOffset RequiredMemoryTimestamp(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var text = RequiredTrimmedMemoryResultText(values, name, 128);
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            throw new InvalidDataException(
                $"Hermes memory timestamp '{name}' was rejected.");
        }
        return timestamp.ToUniversalTime();
    }

    private static DateTimeOffset? OptionalMemoryTimestamp(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException("Hermes memory result was incomplete.");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return RequiredMemoryTimestamp(values, name);
    }

    private static string RequiredMemoryContent(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text)
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > MaximumMemoryContentUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime memory request field '{name}' was rejected.");
        }
        return text;
    }

    private static void ValidateMemoryEntryMutation(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        ValidateMemoryOperationId(RequiredTrimmedText(
            values,
            "operation_id",
            MaximumMemoryOperationIdUtf8Bytes));
        RequiredInt64(
            values,
            "expected_catalog_revision",
            0,
            long.MaxValue - 1);
        ValidateMemoryEntryId(RequiredTrimmedText(
            values,
            "entry_id",
            MaximumMemoryEntryIdUtf8Bytes));
        RequiredInt64(
            values,
            "expected_entry_version",
            1,
            long.MaxValue - 1);
    }

    private static void ValidateMemoryOperationId(string value)
    {
        if (!char.IsAsciiLetterOrDigit(value[0])
            || value.Skip(1).Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new InvalidDataException(
                "Runtime memory operation identity was rejected.");
        }
    }

    private static void ValidateMemoryEntryId(string value)
    {
        if (value.Length > 256
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "Runtime memory entry identity was rejected.");
        }
    }

    private static string ValidateMemoryCategory(
        string value,
        bool allowCandidate,
        bool allowTemporary)
    {
        if (value is "relationship_core" or "long_term" or "relationship_episode"
            || allowCandidate && value == "candidate"
            || allowTemporary && value == "temporary")
        {
            return value;
        }
        throw new InvalidDataException("Runtime memory category was rejected.");
    }

    private static string ValidateMemoryPromotionCategory(string value)
        => value is "relationship_core" or "long_term"
            ? value
            : throw new InvalidDataException(
                "Runtime memory promotion category was rejected.");

    private static void ValidateMemoryState(string value)
    {
        if (value is not (
                "active" or "hidden" or "rejected" or "expired" or "superseded"))
        {
            throw new InvalidDataException("Hermes memory state was rejected.");
        }
    }

    private static void ValidateMemorySource(string value)
    {
        if (value is not (
                "explicit_user" or "inferred" or "operator"
                or "relationship_review"))
        {
            throw new InvalidDataException("Hermes memory source was rejected.");
        }
    }

    private JsonElement Canonicalize(string method, JsonElement payload)
    {
        var values = ReadExactObject(payload, AllowedProperties(method));
        switch (method)
        {
            case HermesUsageBudgetContract.GetMethod:
            case HermesUsageBudgetPreviewContract.PreviewMethod:
            case HermesLatestUsageContract.GetMethod:
                HermesUsageBudgetContract.ValidateGetRequest(payload);
                break;
            case HermesUsageBudgetContract.SetMethod:
                HermesUsageBudgetContract.ParseUpdate(payload);
                break;
            case "runtime.status":
            case HermesCapabilityCatalogContract.Method:
            case HermesCommandCatalogContract.Method:
                break;
            case "expression.get":
            case "expression.version.get":
            case "expression.save":
            case "expression.revert":
                ValidateExpressionRequest(method, values);
                break;
            case "memory.catalog":
                if (!RequiredBoolean(values, "include_hidden")
                    || RequiredInteger(
                        values,
                        "limit",
                        1,
                        MaximumMemoryCatalogEntries)
                        != MaximumMemoryCatalogEntries)
                {
                    throw new InvalidDataException(
                        "Runtime memory catalog scope was rejected.");
                }
                if (values.TryGetValue("offset", out var offset)
                    && (offset.ValueKind != JsonValueKind.Number
                        || !offset.TryGetInt32(out var decodedOffset)
                        || decodedOffset < 0
                        || decodedOffset > MaximumMemoryManagedEntries))
                {
                    throw new InvalidDataException(
                        "Runtime memory catalog offset was rejected.");
                }
                break;
            case "memory.preferences.get":
                break;
            case "memory.entry.add":
            {
                ValidateMemoryOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumMemoryOperationIdUtf8Bytes));
                RequiredInt64(
                    values,
                    "expected_catalog_revision",
                    0,
                    long.MaxValue - 1);
                var category = ValidateMemoryCategory(
                    RequiredTrimmedText(values, "category", 64),
                    allowCandidate: false,
                    allowTemporary: true);
                if (category == "relationship_episode")
                {
                    throw new InvalidDataException(
                        "Relationship episodes cannot be added manually.");
                }
                _ = RequiredMemoryContent(values, "content");
                var pinned = RequiredBoolean(values, "pinned");
                if (pinned && category != "relationship_core")
                {
                    throw new InvalidDataException(
                        "Only relationship-core memories may be pinned when added.");
                }
                break;
            }
            case "memory.entry.pin.set":
                ValidateMemoryEntryMutation(values);
                RequiredBoolean(values, "pinned");
                break;
            case "memory.entry.promote":
                ValidateMemoryEntryMutation(values);
                _ = ValidateMemoryPromotionCategory(RequiredTrimmedText(
                    values,
                    "target_category",
                    64));
                break;
            case "memory.entry.correct":
                ValidateMemoryEntryMutation(values);
                _ = RequiredMemoryContent(values, "replacement_content");
                break;
            case "memory.entry.visibility.set":
                ValidateMemoryEntryMutation(values);
                RequiredBoolean(values, "hidden");
                break;
            case "memory.entry.reject":
                ValidateMemoryEntryMutation(values);
                break;
            case "memory.entry.delete":
                ValidateMemoryEntryMutation(values);
                if (!RequiredBoolean(values, "permanent"))
                {
                    throw new InvalidDataException(
                        "Runtime memory deletion must be explicitly permanent.");
                }
                break;
            case "memory.preferences.set":
                ValidateMemoryOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumMemoryOperationIdUtf8Bytes));
                RequiredInt64(
                    values,
                    "expected_preferences_revision",
                    0,
                    long.MaxValue - 1);
                RequiredBoolean(values, "auto_memory_enabled");
                break;
            case "memory.export":
                ValidateMemoryOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumMemoryOperationIdUtf8Bytes));
                RequiredInt64(
                    values,
                    "expected_catalog_revision",
                    0,
                    long.MaxValue);
                if (!string.Equals(
                        RequiredTrimmedText(values, "format", 16),
                        "json",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime memory export format was rejected.");
                }
                break;
            case "tools.settings.get":
                break;
            case "provider.setup.catalog":
            case "provider.setup.models":
            case "provider.setup.save":
            case "provider.setup.remove":
                return HermesProviderSetupContract.CanonicalizeRequest(method, payload);
            case "tools.settings.set":
                var expectedNativeConfig = RequiredTrimmedText(values, "expected_config_sha256", 64);
                if (expectedNativeConfig.Length != 64 || expectedNativeConfig.Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("Expected native settings digest is invalid.");
                _ = DecodeNativeToolsSetClientPayload(payload);
                break;
            case "tools.catalog":
                RequiredSessionId(values);
                break;
            case "tools.set":
                var nativeToolsSessionId = RequiredSessionId(values);
                var nativeToolsRequest = DecodeNativeToolsSetClientPayload(payload);
                var nativeToolsPayload = HermesNativeToolsContract.EncodeSetRequest(nativeToolsRequest)
                    .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
                nativeToolsPayload["session_id"] = JsonSerializer.SerializeToElement(nativeToolsSessionId);
                return JsonSerializer.SerializeToElement(nativeToolsPayload);
            case "session.create":
                OptionalText(values, "title", MaximumTitleUtf8Bytes);
                var createModel = OptionalText(
                    values,
                    "model",
                    MaximumModelUtf8Bytes);
                var createProvider = OptionalText(
                    values,
                    "provider",
                    MaximumProviderUtf8Bytes);
                if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge
                    && (createProvider is not null || createModel is not null))
                {
                    throw new InvalidDataException(
                        "The hidden game session model is owned by the exact native global default.");
                }
                ValidateOptionalApprovedPair(createProvider, createModel);
                if (createProvider is null && createModel is null)
                {
                    var approved = _approvedCatalog();
                    var effective = _modelIdentity();
                    if (approved.Allows(
                            effective.ProviderId,
                            effective.ModelId))
                    {
                        values["provider"] = JsonSerializer.SerializeToElement(
                            effective.ProviderId);
                        values["model"] = JsonSerializer.SerializeToElement(
                            effective.ModelId);
                    }
                    else if (_sessionSurface
                        == LilithRuntimeSessionSurface.GameBridge)
                    {
                        throw new LilithGameModelSelectionRequiredException();
                    }
                }
                OptionalText(values, "reasoning_effort", 64);
                OptionalBoolean(values, "fast");
                OptionalInteger(values, "cols", 20, 1000);
                values["source"] = Json(
                    _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                        ? "\"lilith-game\""
                        : "\"lilith-runtime\"");
                if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge)
                {
                    values["archived"] = Json("true");
                    values["durable"] = Json("true");
                }
                values["cwd"] = JsonSerializer.SerializeToElement(_workspacePath);
                values["close_on_disconnect"] = Json("true");
                break;
            case "session.list":
                OptionalInteger(values, "limit", 1, 1000);
                break;
            case "session.resume":
                RequiredSessionId(values);
                OptionalInteger(values, "cols", 20, 1000);
                OptionalBoolean(values, "omit_messages");
                values["source"] = Json(
                    _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                        ? "\"lilith-game\""
                        : "\"lilith-runtime\"");
                if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge)
                {
                    var approved = _approvedCatalog();
                    var effective = _modelIdentity();
                    if (!approved.Allows(
                            effective.ProviderId,
                            effective.ModelId))
                    {
                        throw new LilithGameModelSelectionRequiredException();
                    }
                    // A resumed game conversation must keep the same native
                    // Hermes routing identity as a newly-created game
                    // conversation.  These values are host-owned: callers are
                    // not allowed to provide either property through
                    // AllowedProperties, so a game client cannot retag an
                    // arbitrary session or keep it live after Runtime leaves.
                    values["close_on_disconnect"] = Json("true");
                    values["require_durable_lineage"] = Json("true");
                    values["expected_source"] = Json("\"lilith-game\"");
                    values["expected_archived"] = Json("true");
                    values["game_model_route"] = JsonSerializer.SerializeToElement(new
                    {
                        model = effective.ModelId,
                        provider = effective.ProviderId
                    });
                }
                break;
            case "session.status":
            case "session.history":
            case "session.interrupt":
            case "session.close":
            case "session.usage":
            case "session.context_breakdown":
                RequiredSessionId(values);
                break;
            case "session.append_assistant_event":
                if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                {
                    throw new InvalidDataException(
                        "Native assistant events are available only for the hidden game conversation.");
                }
                RequiredSessionId(values);
                var nativeAssistantEventId = RequiredTrimmedText(
                    values,
                    "event_id",
                    32);
                if (!Guid.TryParseExact(
                        nativeAssistantEventId,
                        "N",
                        out var nativeAssistantEventGuid)
                    || nativeAssistantEventGuid == Guid.Empty
                    || !string.Equals(
                        nativeAssistantEventId,
                        nativeAssistantEventGuid.ToString("N"),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime native assistant event identity was rejected.");
                }
                var nativeAssistantText = RequiredConversationText(
                    values,
                    "text",
                    MaximumPromptUtf8Bytes);
                if (nativeAssistantText.Length > MaximumNativeAssistantEventCharacters)
                {
                    throw new InvalidDataException(
                        "Runtime native assistant event text was too long.");
                }
                _ = RequiredInt64(
                    values,
                    "occurred_at_unix_ms",
                    1,
                    253402300799999);
                _ = RequiredMetadataText(
                    values,
                    "node_id",
                    LilithGameBridgeProtocol.MaximumNativeAssistantNodeIdCharacters,
                    allowEmpty: true);
                _ = RequiredMetadataText(
                    values,
                    "line_id",
                    LilithGameBridgeProtocol.MaximumNativeAssistantLineIdCharacters,
                    allowEmpty: true);
                _ = RequiredMetadataText(
                    values,
                    "trigger",
                    LilithGameBridgeProtocol.MaximumNativeAssistantTriggerCharacters,
                    allowEmpty: true);
                _ = RequiredMetadataText(
                    values,
                    "locale",
                    LilithGameBridgeProtocol.MaximumNativeAssistantLocaleCharacters,
                    allowEmpty: false);
                values["source"] = Json("\"lilith-native\"");
                values["require_durable_lineage"] = Json("true");
                break;
            case "session.extend_native_assistant_event":
                if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                {
                    throw new InvalidDataException(
                        "Native assistant continuation is available only for the hidden game conversation.");
                }
                RequiredSessionId(values);
                var continuationEventId = RequiredTrimmedText(
                    values,
                    "event_id",
                    HermesTurnCorrelationContract.IdentityCharacters);
                if (!Guid.TryParseExact(
                        continuationEventId,
                        "N",
                        out var continuationEventGuid)
                    || continuationEventGuid == Guid.Empty
                    || !string.Equals(
                        continuationEventId,
                        continuationEventGuid.ToString("N"),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime native assistant continuation identity was rejected.");
                }
                if (!RequiredBoolean(values, "proactive_dialogue_enabled"))
                {
                    throw new InvalidDataException(
                        "Runtime cannot dispatch a disabled native assistant continuation.");
                }
                if (!values.TryGetValue("perception", out var perceptionValue))
                {
                    throw new InvalidDataException(
                        "Runtime native perception snapshot was missing.");
                }
                var perception = ReadExactObject(
                    perceptionValue,
                    new[]
                    {
                        "schema_version", "source", "text",
                        "occurred_at_unix_ms", "node_id", "line_id",
                        "trigger", "locale", "application"
                    });
                var perceptionSchemaVersion = RequiredInt64(
                    perception,
                    "schema_version",
                    LilithGameBridgeNativePerceptionSnapshot.CurrentSchemaVersion,
                    LilithGameBridgeNativePerceptionSnapshot.CurrentSchemaVersion);
                var perceptionSource = RequiredMetadataText(
                    perception,
                    "source",
                    LilithGameBridgeNativePerceptionSnapshot
                        .OfficialNativeDialogueSource.Length,
                    allowEmpty: false);
                if (!string.Equals(
                        perceptionSource,
                        LilithGameBridgeNativePerceptionSnapshot
                            .OfficialNativeDialogueSource,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime native perception source was rejected.");
                }
                var perceptionText = RequiredConversationText(
                    perception,
                    "text",
                    MaximumPromptUtf8Bytes);
                if (perceptionText.Length > MaximumNativeAssistantEventCharacters)
                {
                    throw new InvalidDataException(
                        "Runtime native perception text was too long.");
                }
                var perceptionOccurredAt = RequiredInt64(
                    perception,
                    "occurred_at_unix_ms",
                    1,
                    253402300799999);
                var perceptionNodeId = RequiredMetadataText(
                    perception,
                    "node_id",
                    LilithGameBridgeProtocol.MaximumNativeAssistantNodeIdCharacters,
                    allowEmpty: true);
                var perceptionLineId = RequiredMetadataText(
                    perception,
                    "line_id",
                    LilithGameBridgeProtocol.MaximumNativeAssistantLineIdCharacters,
                    allowEmpty: true);
                var perceptionTrigger = RequiredMetadataText(
                    perception,
                    "trigger",
                    LilithGameBridgeProtocol.MaximumNativeAssistantTriggerCharacters,
                    allowEmpty: true);
                var perceptionLocale = RequiredMetadataText(
                    perception,
                    "locale",
                    LilithGameBridgeProtocol.MaximumNativeAssistantLocaleCharacters,
                    allowEmpty: false);
                var canonicalPerception = new Dictionary<string, object>
                {
                    ["schema_version"] = perceptionSchemaVersion,
                    ["source"] = perceptionSource,
                    ["text"] = perceptionText,
                    ["occurred_at_unix_ms"] = perceptionOccurredAt,
                    ["node_id"] = perceptionNodeId,
                    ["line_id"] = perceptionLineId,
                    ["trigger"] = perceptionTrigger,
                    ["locale"] = perceptionLocale
                };
                if (perception.TryGetValue("application", out var applicationObservation))
                {
                    var observedApplication = LilithGameBridgeNativeApplicationObservation
                        .DecodeNative(applicationObservation);
                    if (!LilithGameBridgeNativeApplicationObservation.IsApplicationTrigger(perceptionTrigger)
                        || observedApplication.ObservedAtUnixMilliseconds > perceptionOccurredAt
                        || perceptionOccurredAt - observedApplication.ObservedAtUnixMilliseconds > 30_000)
                        throw new InvalidDataException("Native application observation is unrelated or stale.");
                    canonicalPerception["application"] = observedApplication.ToNativePayload();
                }
                values["perception"] = JsonSerializer.SerializeToElement(canonicalPerception);
                values["source"] = Json("\"lilith-native\"");
                values["require_durable_lineage"] = Json("true");
                if (values.TryGetValue("game_context", out var extensionGameContext))
                    _ = LilithGameBridgeContracts.LilithMiniGamePublicContext.Read(extensionGameContext);
                break;
            case "session.generate_foreground_proactive":
                if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                {
                    throw new InvalidDataException(
                        "Foreground proactive dialogue is available only for the hidden game conversation.");
                }
                RequiredSessionId(values);
                var proactiveEventId = RequiredTrimmedText(
                    values,
                    "event_id",
                    HermesTurnCorrelationContract.IdentityCharacters);
                if (!Guid.TryParseExact(
                        proactiveEventId,
                        "N",
                        out var proactiveEventGuid)
                    || proactiveEventGuid == Guid.Empty
                    || !string.Equals(
                        proactiveEventId,
                        proactiveEventGuid.ToString("N"),
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime foreground proactive identity was rejected.");
                }
                if (!RequiredBoolean(values, "proactive_dialogue_enabled"))
                {
                    throw new InvalidDataException(
                        "Runtime cannot dispatch disabled foreground proactive dialogue.");
                }
                if (!values.TryGetValue(
                        "perception",
                        out var proactivePerceptionValue))
                {
                    throw new InvalidDataException(
                        "Runtime foreground perception snapshot was missing.");
                }
                var proactivePerception = ReadExactObject(
                    proactivePerceptionValue,
                    new[]
                    {
                        "schema_version", "source", "application_id",
                        "application_name", "category", "observed_at_unix_ms",
                        "locale"
                    });
                var proactiveSchemaVersion = RequiredInt64(
                    proactivePerception,
                    "schema_version",
                    1,
                    1);
                var proactiveSource = RequiredMetadataText(
                    proactivePerception,
                    "source",
                    "foreground-application".Length,
                    allowEmpty: false);
                if (!string.Equals(
                        proactiveSource,
                        "foreground-application",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Runtime foreground perception source was rejected.");
                }
                var applicationId = RequiredMetadataText(
                    proactivePerception,
                    "application_id",
                    LilithGameBridgeProtocol.MaximumForegroundApplicationIdCharacters,
                    allowEmpty: false);
                var applicationName = RequiredMetadataText(
                    proactivePerception,
                    "application_name",
                    LilithGameBridgeProtocol
                        .MaximumForegroundApplicationDisplayNameCharacters,
                    allowEmpty: false);
                var applicationCategory = RequiredMetadataText(
                    proactivePerception,
                    "category",
                    LilithGameBridgeProtocol
                        .MaximumForegroundApplicationCategoryCharacters,
                    allowEmpty: false);
                var observedAtUnixMilliseconds = RequiredInt64(
                    proactivePerception,
                    "observed_at_unix_ms",
                    1,
                    253402300799999);
                var proactiveLocale = RequiredMetadataText(
                    proactivePerception,
                    "locale",
                    LilithGameBridgeProtocol.MaximumNativeAssistantLocaleCharacters,
                    allowEmpty: false);
                values["perception"] = JsonSerializer.SerializeToElement(new
                {
                    schema_version = proactiveSchemaVersion,
                    source = proactiveSource,
                    application_id = applicationId,
                    application_name = applicationName,
                    category = applicationCategory,
                    observed_at_unix_ms = observedAtUnixMilliseconds,
                    locale = proactiveLocale
                });
                values["source"] = Json("\"lilith-foreground-proactive\"");
                values["require_durable_lineage"] = Json("true");
                if (values.TryGetValue("game_context", out var proactiveGameContext))
                    _ = LilithGameBridgeContracts.LilithMiniGamePublicContext.Read(proactiveGameContext);
                break;
            case "session.delete":
                RequiredSessionId(values);
                RequiredTrimmedText(
                    values,
                    "stored_session_id",
                    LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
                break;
            case "session.compress":
                RequiredSessionId(values);
                var focusTopic = OptionalText(
                    values,
                    "focus_topic",
                    MaximumCompressionFocusUtf8Bytes);
                if (focusTopic is not null)
                {
                    values["focus_topic"] = JsonSerializer.SerializeToElement(
                        focusTopic.Trim());
                }
                break;
            case "session.title":
                RequiredSessionId(values);
                var title = RequiredTrimmedText(
                    values,
                    "title",
                    MaximumTitleUtf8Bytes);
                values["title"] = JsonSerializer.SerializeToElement(title);
                break;
            case "prompt.submit":
                RequiredSessionId(values);
                if (values.TryGetValue("game_context", out var publicGameContext))
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                        throw new InvalidDataException("Runtime public game context was supplied outside the game bridge.");
                    _ = LilithGameBridgeContracts.LilithMiniGamePublicContext.Read(publicGameContext);
                }
                if (values.TryGetValue("game_context_diagnostic", out var diagnostic))
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                        throw new InvalidDataException("Mini-game diagnostic outside game bridge rejected.");
                    _ = LilithMiniGameDiagnosticRequest.Parse(diagnostic);
                }
                if (values.TryGetValue("game_answer_repair", out var repairValue))
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
                        || !values.TryGetValue("game_context", out var repairContext))
                        throw new InvalidDataException("Mini-game answer repair outside an active game context rejected.");
                    var repair = LilithGameBridgeContracts.LilithMiniGameAnswerRepair.Read(repairValue);
                    if (!repair.IsSupportedBy(LilithGameBridgeContracts.LilithMiniGamePublicContext.Read(repairContext)))
                        throw new InvalidDataException("Unsupported mini-game answer requirements rejected.");
                }
                var uiLocale = OptionalText(values, "ui_locale", maximumUtf8Bytes: 16);
                if (uiLocale is not null && (_sessionSurface != LilithRuntimeSessionSurface.Workbench
                    || uiLocale is not ("zh-TW" or "zh-CN" or "ja-JP" or "en-US")))
                    throw new InvalidDataException("Runtime Workbench UI locale was rejected.");
                // Empty captions are legal only after the route below claims an
                // exact native image attachment; text-only blank submits fail.
                RequiredConversationText(values, "text", MaximumPromptUtf8Bytes, allowBlank: true);
                var optionalDisplayLocale = OptionalText(
                    values,
                    "display_locale",
                    maximumUtf8Bytes: 16);
                if (optionalDisplayLocale is not null)
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                    {
                        throw new InvalidDataException(
                            "Runtime game display locale was supplied outside the game bridge.");
                    }
                    ValidateDisplayLocale(optionalDisplayLocale);
                }
                var optionalSpeechLocale = OptionalText(values, "speech_locale", maximumUtf8Bytes: 16);
                if (optionalSpeechLocale is not null)
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge || optionalDisplayLocale is null)
                        throw new InvalidDataException("Paired speech requires the game display locale.");
                    ValidateSpeechLocale(optionalSpeechLocale);
                }
                break;
            case "prompt.draft_note":
                if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                    throw new InvalidDataException("Note drafts require the hidden game surface.");
                RequiredSessionId(values);
                var draftOperation = RequiredNoteIdentity(values, "operation_id");
                var draftEvent = RequiredNoteIdentity(values, "event_id");
                var draftLocale = RequiredText(values, "display_locale", 16) switch
                {
                    "zh-TW" => LilithGameBridgeDisplayLocale.TraditionalChinese,
                    "zh-CN" => LilithGameBridgeDisplayLocale.SimplifiedChinese,
                    "ja" => LilithGameBridgeDisplayLocale.Japanese,
                    "en" => LilithGameBridgeDisplayLocale.English,
                    _ => throw new InvalidDataException("Note draft locale was rejected.")
                };
                _ = new LilithGameBridgeNoteDraftRequest(
                    _generationId, draftOperation, 1, draftEvent,
                    RequiredText(values, "category", 32),
                    RequiredNoteText(values, "topic", 360, false),
                    RequiredNoteText(values, "reply_context", 360, true), draftLocale);
                break;
            case "prompt.project_speech":
                RequiredSessionId(values);
                if (values.ContainsKey("turn_id"))
                    RequiredText(values, "turn_id", 128);
                if (values.ContainsKey("notification_event_id") || values.ContainsKey("notification_message_id"))
                {
                    if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                        throw new InvalidDataException("Notification speech requires the hidden game surface.");
                    var notificationIds = new[] { "turn_id", "notification_event_id", "notification_message_id" }
                        .Select(name => RequiredText(values, name, 32)).ToArray();
                    if (notificationIds.Any(id => !Guid.TryParseExact(id, "N", out var parsed)
                            || parsed == Guid.Empty || id != parsed.ToString("N"))
                        || notificationIds.Distinct(StringComparer.Ordinal).Count() != notificationIds.Length)
                        throw new InvalidDataException("Notification speech correlation was rejected.");
                }
                RequiredConversationText(values, "text", MaximumPromptUtf8Bytes);
                ValidateDisplayLocale(RequiredText(
                    values,
                    "display_locale",
                    maximumUtf8Bytes: 16));
                ValidateSpeechLocale(RequiredText(
                    values,
                    "speech_locale",
                    maximumUtf8Bytes: 16));
                break;
            case "image.attach_bytes":
                values = CanonicalizeImageAttach(values);
                break;
            case "image.detach":
                RequiredSessionId(values);
                RequiredText(values, "path", MaximumImagePathUtf8Bytes);
                break;
            case "model.options":
                OptionalSessionId(values);
                OptionalBoolean(values, "explicit_only");
                OptionalBoolean(values, "include_unconfigured");
                OptionalBoolean(values, "refresh");
                values["explicit_only"] = Json("true");
                values["include_unconfigured"] = Json("false");
                break;
            case "model.credential_status":
                break;
            case "model.replace_key":
                ValidateCredentialProviderSlug(RequiredTrimmedText(
                    values,
                    "slug",
                    MaximumProviderUtf8Bytes));
                RequiredTrimmedText(
                    values,
                    "api_key",
                    MaximumCredentialUtf8Bytes);
                ValidateCredentialOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumCredentialOperationIdUtf8Bytes));
                break;
            case "model.remove_key":
                ValidateCredentialProviderSlug(RequiredTrimmedText(
                    values,
                    "slug",
                    MaximumProviderUtf8Bytes));
                ValidateCredentialOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumCredentialOperationIdUtf8Bytes));
                break;
            case "messaging.telegram.status":
            case "messaging.telegram.pairing.list":
                break;
            case "messaging.telegram.replace":
                ValidateTelegramBotToken(RequiredTrimmedText(
                    values,
                    "bot_token",
                    MaximumTelegramBotTokenUtf8Bytes));
                ValidateCredentialOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumCredentialOperationIdUtf8Bytes));
                break;
            case "messaging.telegram.remove":
                ValidateCredentialOperationId(RequiredTrimmedText(
                    values,
                    "operation_id",
                    MaximumCredentialOperationIdUtf8Bytes));
                break;
            case "messaging.telegram.pairing.approve":
                ValidateTelegramPairingRequestId(RequiredTrimmedText(
                    values,
                    "request_id",
                    TelegramPairingRequestIdCharacters));
                break;
            case "messaging.telegram.pairing.revoke":
                ValidateTelegramUserId(RequiredTrimmedText(
                    values,
                    "user_id",
                    MaximumTelegramUserIdUtf8Bytes));
                break;
            case TelegramVoiceStatusMethod:
            case TelegramVoiceSetMethod:
                ValidateTelegramPrivateVoiceUser(RequiredText(values, "user_id", 19));
                if (method == TelegramVoiceSetMethod)
                {
                    ValidateTelegramVoiceMode(RequiredText(values, "mode", 16));
                    ValidateTelegramVoiceRevision(RequiredText(values, "expected_revision", 64));
                }
                break;
            case "model.default.set":
                var defaultProvider = RequiredText(
                    values,
                    "provider",
                    MaximumProviderUtf8Bytes);
                var defaultModel = RequiredText(
                    values,
                    "model",
                    MaximumModelUtf8Bytes);
                RequiredBoolean(values, "confirm_expensive_model");
                ValidateApprovedPair(defaultProvider, defaultModel);
                break;
            case "config.set":
                var switchSession = RequiredSessionId(values);
                var switchProvider = RequiredText(
                    values,
                    "provider",
                    MaximumProviderUtf8Bytes);
                var switchModel = RequiredText(
                    values,
                    "model",
                    MaximumModelUtf8Bytes);
                OptionalBoolean(values, "confirm_expensive_model");
                ValidateApprovedPair(switchProvider, switchModel);
                var upstream = new Dictionary<string, JsonElement>(
                    StringComparer.Ordinal)
                {
                    ["session_id"] = JsonSerializer.SerializeToElement(switchSession),
                    ["key"] = Json("\"model\""),
                    ["value"] = JsonSerializer.SerializeToElement(
                        switchModel
                        + " --provider "
                        + switchProvider
                        + " --session")
                };
                if (values.TryGetValue(
                        "confirm_expensive_model",
                        out var confirmExpensive))
                {
                    upstream["confirm_expensive_model"] = confirmExpensive;
                }
                values = upstream;
                break;
            case "learning.frames":
                OptionalInteger(values, "cols", 40, 240);
                OptionalInteger(values, "rows", 10, 80);
                OptionalInteger(values, "frames", 2, 2);
                values["cols"] = Json("80");
                values["rows"] = Json("16");
                values["frames"] = Json("2");
                break;
            case "approval.respond":
                var approvalSession = RequiredSessionId(values);
                var approvalChoice = RequiredText(values, "choice", 16);
                if (approvalChoice is not (
                    "once" or "session" or "always" or "deny"))
                {
                    throw new InvalidDataException(
                        "Runtime approval choice was rejected.");
                }
                values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["session_id"] = JsonSerializer.SerializeToElement(
                        approvalSession),
                    ["choice"] = JsonSerializer.SerializeToElement(
                        approvalChoice)
                };
                break;
            case "clarify.respond":
                RequiredSessionId(values);
                RequiredTrimmedText(values, "request_id", 1024);
                RequiredTrimmedText(values, "answer", MaximumPromptUtf8Bytes);
                break;
            case "sudo.respond":
                RequiredSessionId(values);
                RequiredTrimmedText(values, "request_id", 1024);
                RequiredSensitiveTextAllowEmpty(
                    values,
                    "password",
                    MaximumCredentialUtf8Bytes);
                break;
            case "secret.respond":
                RequiredSessionId(values);
                RequiredTrimmedText(values, "request_id", 1024);
                RequiredSensitiveTextAllowEmpty(
                    values,
                    "value",
                    MaximumCredentialUtf8Bytes);
                break;
            case "delegation.status":
                RequiredSessionId(values);
                break;
            case "subagent.interrupt":
                RequiredSessionId(values);
                RequiredTrimmedText(
                    values,
                    "subagent_id",
                    MaximumSubagentIdUtf8Bytes);
                break;
            case "subagent.steer":
                RequiredSessionId(values);
                RequiredTrimmedText(
                    values,
                    "subagent_id",
                    MaximumSubagentIdUtf8Bytes);
                RequiredTrimmedText(
                    values,
                    "text",
                    MaximumSubagentSteerUtf8Bytes);
                break;
            case "voice.toggle":
            {
                RequiredSessionId(values);
                var action = RequiredTrimmedText(values, "action", 16);
                if (action is not ("status" or "on" or "off"))
                {
                    throw new InvalidDataException(
                        "Runtime native voice toggle action was rejected.");
                }
                break;
            }
            case "voice.record":
            {
                RequiredSessionId(values);
                var action = RequiredTrimmedText(values, "action", 16);
                if (action is not ("start" or "stop"))
                {
                    throw new InvalidDataException(
                        "Runtime native voice record action was rejected.");
                }
                var hasCapture = values.TryGetValue("capture_id", out var captureValue);
                var hasLanguage = values.TryGetValue("language", out var languageValue);
                var hasMaximum = values.TryGetValue(
                    "maximum_duration_ms",
                    out var maximumValue);
                var hasDisposition = values.TryGetValue(
                    "disposition",
                    out var dispositionValue);
                if (action == "start")
                {
                    if (hasDisposition
                        || hasCapture != hasLanguage
                        || hasCapture != hasMaximum
                        || _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                            && !hasCapture)
                    {
                        throw new InvalidDataException(
                            "Runtime native voice start capture parameters were rejected.");
                    }
                    if (hasCapture)
                    {
                        _ = RequiredCaptureId(captureValue, "capture_id");
                        var language = RequiredTrimmedText(values, "language", 8);
                        if (language is not ("auto" or "ja" or "zh" or "en"))
                            throw new InvalidDataException(
                                "Runtime native voice language was rejected.");
                        _ = RequiredInteger(
                            values,
                            "maximum_duration_ms",
                            LilithGameBridgeProtocol.MinimumVoiceCaptureDurationMilliseconds,
                            LilithGameBridgeProtocol.MaximumVoiceCaptureDurationMilliseconds);
                    }
                }
                else
                {
                    if (hasLanguage || hasMaximum
                        || hasCapture != hasDisposition
                        || _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                            && !hasCapture)
                    {
                        throw new InvalidDataException(
                            "Runtime native voice stop capture parameters were rejected.");
                    }
                    if (hasCapture)
                    {
                        _ = RequiredCaptureId(captureValue, "capture_id");
                        var disposition = RequiredTrimmedText(
                            values,
                            "disposition",
                            16);
                        if (disposition is not ("transcribe" or "cancel"))
                            throw new InvalidDataException(
                                "Runtime native voice stop disposition was rejected.");
                    }
                }
                break;
            }
            case "rollback.list":
                RequiredSessionId(values);
                break;
            case "rollback.diff":
                RequiredSessionId(values);
                RequiredCheckpointHash(values);
                break;
            case "rollback.restore":
                RequiredSessionId(values);
                RequiredCheckpointHash(values);
                RequiredTrimmedText(
                    values,
                    "confirmation_token",
                    MaximumCheckpointTokenUtf8Bytes);
                break;
            default:
                throw new InvalidDataException("The Runtime method was rejected.");
        }

        return JsonSerializer.SerializeToElement(values);
    }

    private static Dictionary<string, JsonElement> CanonicalizeImageAttach(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var sessionId = RequiredSessionId(values);
        var contentBase64 = RequiredText(
            values,
            "content_base64",
            MaximumImageBase64Characters);
        var filename = RequiredText(
            values,
            "filename",
            MaximumImageFilenameUtf8Bytes);

        if (contentBase64.Any(char.IsWhiteSpace)
            || contentBase64.Length > MaximumImageBase64Characters)
        {
            throw new InvalidDataException(
                "Runtime image base64 was rejected.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "Runtime image base64 was rejected.",
                exception);
        }
        try
        {
            if (decoded.Length is < 1 or > MaximumImageBytes
                || !string.Equals(
                    Convert.ToBase64String(decoded),
                    contentBase64,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime image base64 was rejected.");
            }

            var detectedExtension = DetectImageExtension(decoded);
            var hintedExtension = CanonicalImageExtension(filename);
            if (!string.Equals(
                    detectedExtension,
                    hintedExtension,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime image filename did not match its bytes.");
            }

            var canonicalFilename = Convert.ToHexString(SHA256.HashData(decoded))
                + detectedExtension;
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["session_id"] = JsonSerializer.SerializeToElement(sessionId),
                ["content_base64"] = JsonSerializer.SerializeToElement(contentBase64),
                ["filename"] = JsonSerializer.SerializeToElement(canonicalFilename)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static string DetectImageExtension(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> png =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.StartsWith(png))
            return ".png";
        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return ".jpg";
        }
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }
        throw new InvalidDataException(
            "Runtime image type was rejected.");
    }

    private static string CanonicalImageExtension(string filename)
    {
        string extension;
        try
        {
            extension = Path.GetExtension(filename).ToLowerInvariant();
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Runtime image filename was rejected.",
                exception);
        }
        return extension switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".webp" => ".webp",
            _ => throw new InvalidDataException(
                "Runtime image filename extension was rejected.")
        };
    }

    private async Task<LilithRuntimeResponse> RouteImageAttachAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var expectation = ImageAttachExpectation.FromCanonical(parameters);
        if (!TryBeginImageAttach(expectation.SessionId))
        {
            return Failure(
                "image.attach_bytes",
                "runtime.image_lifecycle_blocked",
                "Only one image may be pending in the active attested session.");
        }

        try
        {
            var result = await _requestAsync(
                "image.attach_bytes",
                parameters,
                cancellationToken).ConfigureAwait(false);
            var attachment = ValidateImageAttachResult(result, expectation);
            if (!TryCommitImageAttach(expectation.SessionId, attachment))
            {
                throw new InvalidDataException(
                    "The image attach completed after its session ownership changed.");
            }
            return Success("image.attach_bytes", result);
        }
        catch
        {
            MarkImageLifecycleUncertain(expectation.SessionId);
            throw;
        }
    }

    private async Task<LilithRuntimeResponse> RouteImageDetachAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime image detach requires a session id.");
        var path = parameters.GetProperty("path").GetString()
            ?? throw new InvalidDataException(
                "Runtime image detach requires a path.");
        if (!TryBeginImageDetach(sessionId, path))
        {
            return Failure(
                "image.detach",
                "runtime.image_path_not_owned",
                "Only the pending image path attached by this Runtime session may be detached.");
        }

        try
        {
            var result = await _requestAsync(
                "image.detach",
                parameters,
                cancellationToken).ConfigureAwait(false);
            ValidateImageDetachResult(result);
            if (!TryCommitImageDetach(sessionId, path))
            {
                throw new InvalidDataException(
                    "The image detach completed after its session ownership changed.");
            }
            return Success("image.detach", result);
        }
        catch
        {
            MarkImageLifecycleUncertain(sessionId);
            throw;
        }
    }

    private async Task<LilithRuntimeResponse> RoutePromptSubmitAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (parameters.TryGetProperty("game_context_diagnostic", out var diagnosticValue))
        {
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                return Failure("prompt.submit", "runtime.minigame_context_rejected",
                    "The request-time game diagnostic is unavailable.");
            if (!LilithMiniGameDiagnosticRequest.Parse(diagnosticValue)
                    .IsActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                parameters = JsonSerializer.SerializeToElement(parameters.EnumerateObject()
                    .Where(property => property.Name != "game_context_diagnostic")
                    .ToDictionary(property => property.Name, property => property.Value));
        }
        if (parameters.TryGetProperty("game_context", out var miniGameValue))
        {
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                return Failure("prompt.submit", "runtime.minigame_context_rejected",
                    "The request-time public game context is unavailable.");
            var miniGameContext = LilithGameBridgeContracts.LilithMiniGamePublicContext.Read(miniGameValue);
            if (!miniGameContext.IsFresh(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                // Optional observations may age while waiting for admission.
                // Drop only this valid observation, never the player's message.
                parameters = JsonSerializer.SerializeToElement(parameters.EnumerateObject()
                    .Where(property => property.Name is not ("game_context" or "game_answer_repair"))
                    .ToDictionary(property => property.Name, property => property.Value));
            }
        }
        // Schema/surface validation above still rejects malformed legacy
        // traffic. A valid old opt-in is inert: retain its fresh observation
        // and exact human message, but never authorize an auxiliary question.
        if (parameters.TryGetProperty("game_answer_repair", out _))
            parameters = JsonSerializer.SerializeToElement(parameters.EnumerateObject()
                .Where(property => property.Name != "game_answer_repair")
                .ToDictionary(property => property.Name, property => property.Value));
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime prompt submit requires a session id.");
        if (!TryBeginPromptSubmit(sessionId, out var hasPendingImage))
        {
            return Failure(
                "prompt.submit",
                "runtime.image_lifecycle_blocked",
                "The active session image lifecycle is not ready for a prompt.");
        }

        if (!hasPendingImage && string.IsNullOrWhiteSpace(parameters.GetProperty("text").GetString()))
        {
            RestoreAfterRejectedPrompt(sessionId, hasPendingImage);
            return Failure("prompt.submit", "runtime.empty_prompt",
                "A prompt requires text or a verified attached image.");
        }

        JsonElement result;
        try
        {
            result = await _requestAsync(
                "prompt.submit",
                parameters,
                cancellationToken).ConfigureAwait(false);
            var promptResult = HermesTurnCorrelationContract.DecodePromptSubmitResult(result);
            var disposition = ValidatePromptSubmitResult(
                promptResult,
                result,
                hasPendingImage);
            if (!CompletePromptReceipt(
                    sessionId,
                    disposition,
                    hasPendingImage))
            {
                return Failure(
                    "prompt.submit",
                    "runtime.image_lifecycle_uncertain",
                    "Hermes prompt ownership could not be proved; close the session or restart Runtime.");
            }
            if (!CompletePromptTurnReceipt(sessionId, promptResult))
            {
                MarkImageLifecycleUncertain(sessionId);
                return Failure(
                    "prompt.submit",
                    "runtime.prompt_turn_outcome_uncertain",
                    "Hermes prompt turn ownership could not be proved; reconnect before continuing.");
            }
        }
        catch (HermesRpcException)
        {
            RestoreAfterRejectedPrompt(sessionId, hasPendingImage);
            throw;
        }
        catch
        {
            MarkImageLifecycleUncertain(sessionId);
            throw;
        }
        return Success("prompt.submit", result);
    }

    private async Task<LilithRuntimeResponse> RouteNativeAssistantEventAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        const string method = "session.append_assistant_event";
        try
        {
            var result = await _requestAsync(
                method,
                parameters,
                cancellationToken).ConfigureAwait(false);
            return Success(method, result);
        }
        catch (HermesRequestNotSentException exception)
        {
            return Failure(
                method,
                "runtime.native_assistant_append_not_sent",
                exception.Message);
        }
        catch (HermesRpcException exception) when (exception.Code == 4009)
        {
            return Failure(
                method,
                "runtime.native_assistant_busy",
                "The hidden game conversation is busy; retry the same native event identity.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4001)
        {
            return Failure(
                method,
                "runtime.native_assistant_session_unavailable",
                "The hidden game conversation is temporarily unavailable; retry the same native event identity.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4030)
        {
            return Failure(
                method,
                "runtime.native_assistant_scope_rejected",
                "Hermes rejected the native assistant event session scope.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4094)
        {
            return Failure(
                method,
                "runtime.game_session_lineage_rejected",
                "Hermes rejected the hidden durable game-session lineage.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4004)
        {
            return Failure(
                method,
                "runtime.native_assistant_event_invalid",
                "Hermes rejected the native assistant event shape.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4092)
        {
            return Failure(
                method,
                "runtime.native_assistant_event_conflict",
                "The native assistant event identity was already bound to different content.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 5006)
        {
            return Failure(
                method,
                "runtime.native_assistant_persistence_retryable",
                "Hermes could not durably commit the native assistant event; retry the same event identity.",
                exception.Code);
        }
        catch (HermesRpcException exception)
        {
            return Failure(
                method,
                "runtime.native_assistant_rejected",
                exception.Message,
                exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The request may have crossed the WebSocket send boundary. The
            // exact event id is upstream's durable idempotency key, so only a
            // retry with that same id can safely reconcile the ambiguity.
            return Failure(
                method,
                "runtime.native_assistant_append_uncertain",
                "Hermes did not return a trustworthy native assistant commit receipt; retry the same event identity.");
        }
    }

    private async Task<LilithRuntimeResponse>
        RouteNativeAssistantContinuationAsync(
            JsonElement parameters,
            CancellationToken cancellationToken)
    {
        const string method = "session.extend_native_assistant_event";
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime native assistant continuation requires a session id.");
        if (!TryBeginNativeAssistantContinuation(sessionId))
        {
            return Failure(
                method,
                "runtime.native_assistant_extension_busy",
                "The hidden game conversation was not idle for proactive continuation.");
        }
        try
        {
            var result = await _requestAsync(
                method,
                parameters,
                cancellationToken).ConfigureAwait(false);
            var continuation = HermesTurnCorrelationContract
                .DecodeNativeAssistantContinuationResult(result);
            var expectedEventId = parameters.GetProperty("event_id").GetString();
            if (!string.Equals(
                    continuation.EventId,
                    expectedEventId,
                    StringComparison.Ordinal)
                || !CompleteNativeAssistantContinuationReceipt(
                    sessionId,
                    continuation))
            {
                MarkNativeAssistantContinuationUncertain(sessionId);
                return Failure(
                    method,
                    "runtime.native_assistant_extension_uncertain",
                    "Hermes did not prove exact proactive continuation ownership.");
            }
            return Success(method, result);
        }
        catch (HermesRequestNotSentException exception)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_not_sent",
                exception.Message);
        }
        catch (HermesRpcException exception) when (exception.Code == 4009)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_busy",
                "The hidden game conversation was not idle for proactive continuation.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4001)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_session_unavailable",
                "The hidden game conversation was unavailable for proactive continuation.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4030)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_scope_rejected",
                "Hermes rejected the proactive continuation session scope.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4094)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.game_session_lineage_rejected",
                "Hermes rejected the hidden durable game-session lineage.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4004)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_invalid",
                "Hermes rejected the proactive continuation shape.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4092)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_conflict",
                "The native event identity was already bound to another perception snapshot.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 5006)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_persistence_failed",
                "Hermes could not durably claim the proactive continuation.",
                exception.Code);
        }
        catch (HermesRpcException exception)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_rejected",
                exception.Message,
                exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkNativeAssistantContinuationUncertain(sessionId);
            throw;
        }
        catch (Exception)
        {
            MarkNativeAssistantContinuationUncertain(sessionId);
            return Failure(
                method,
                "runtime.native_assistant_extension_uncertain",
                "Hermes did not return a trustworthy proactive continuation receipt.");
        }
    }

    private async Task<LilithRuntimeResponse> RouteForegroundProactiveAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        const string method = "session.generate_foreground_proactive";
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime foreground proactive dialogue requires a session id.");
        if (!TryBeginNativeAssistantContinuation(sessionId))
        {
            return Failure(
                method,
                "runtime.foreground_proactive_busy",
                "The hidden game conversation was not idle for foreground proactive dialogue.");
        }
        try
        {
            var result = await _requestAsync(
                method,
                parameters,
                cancellationToken).ConfigureAwait(false);
            var continuation = HermesTurnCorrelationContract
                .DecodeNativeAssistantContinuationResult(result);
            var expectedEventId = parameters.GetProperty("event_id").GetString();
            if (!string.Equals(
                    continuation.EventId,
                    expectedEventId,
                    StringComparison.Ordinal)
                || !CompleteNativeAssistantContinuationReceipt(
                    sessionId,
                    continuation))
            {
                MarkNativeAssistantContinuationUncertain(sessionId);
                return Failure(
                    method,
                    "runtime.foreground_proactive_uncertain",
                    "Hermes did not prove exact foreground proactive ownership.");
            }
            return Success(method, result);
        }
        catch (HermesRequestNotSentException exception)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_not_sent",
                exception.Message);
        }
        catch (HermesRpcException exception) when (exception.Code == 4009)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_busy",
                "The hidden game conversation was not idle for foreground proactive dialogue.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4001)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_session_unavailable",
                "The hidden game conversation was unavailable for foreground proactive dialogue.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4030)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_scope_rejected",
                "Hermes rejected the foreground proactive session scope.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4094)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.game_session_lineage_rejected",
                "Hermes rejected the hidden durable game-session lineage.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4004)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_invalid",
                "Hermes rejected the foreground proactive perception shape.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 4092)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_conflict",
                "The foreground operation identity was already bound to another perception snapshot.",
                exception.Code);
        }
        catch (HermesRpcException exception) when (exception.Code == 5006)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_persistence_failed",
                "Hermes could not durably claim foreground proactive dialogue.",
                exception.Code);
        }
        catch (HermesRpcException exception)
        {
            RestoreAfterRejectedNativeAssistantContinuation(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_rejected",
                exception.Message,
                exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkNativeAssistantContinuationUncertain(sessionId);
            throw;
        }
        catch (Exception)
        {
            MarkNativeAssistantContinuationUncertain(sessionId);
            return Failure(
                method,
                "runtime.foreground_proactive_uncertain",
                "Hermes did not return a trustworthy foreground proactive receipt.");
        }
    }

    private bool TryBeginNativeAssistantContinuation(string sessionId)
    {
        lock (_sessionLock)
        {
            if (!IsActiveAttestedSessionLocked(sessionId))
                return false;
            var turn = GetOrCreateTurnCorrelationLocked(sessionId);
            if (turn.AwaitingReceipt || turn.AwaitingQueuedTurn)
                return false;
            var lifecycle = GetOrCreateImageLifecycleLocked(sessionId);
            if (lifecycle.State != SessionImageLifecycleState.Idle
                || lifecycle.Attachment is not null)
            {
                return false;
            }
            turn.AwaitingReceipt = true;
            turn.AwaitingNativeAssistantContinuationReceipt = true;
            turn.PendingTurnId = string.Empty;
            turn.PendingUserMessageId = string.Empty;
            turn.PendingAssistantMessageId = string.Empty;
            return true;
        }
    }

    private bool CompleteNativeAssistantContinuationReceipt(
        string sessionId,
        HermesNativeAssistantContinuationResult result)
    {
        lock (_sessionLock)
        {
            if (!_sessionTurns.TryGetValue(sessionId, out var turn)
                || !turn.AwaitingReceipt
                || !turn.AwaitingNativeAssistantContinuationReceipt
                || turn.PendingUserMessageId.Length != 0)
            {
                return false;
            }
            if ((turn.PendingTurnId.Length != 0
                    && !string.Equals(
                        turn.PendingTurnId,
                        result.TurnId,
                        StringComparison.Ordinal))
                || (turn.PendingAssistantMessageId.Length != 0
                    && !string.Equals(
                        turn.PendingAssistantMessageId,
                        result.AssistantMessageId,
                        StringComparison.Ordinal))
                || turn.ClaimedUserMessageIds.ContainsKey(result.TurnId))
            {
                return false;
            }
            var eventClaimedTurn = string.Equals(
                turn.PendingTurnId,
                result.TurnId,
                StringComparison.Ordinal);
            var knownIdempotentReconciliation = result.AlreadyProcessed
                && turn.KnownTurnIds.Contains(result.TurnId)
                && turn.ClaimedAssistantMessageIds.TryGetValue(
                    result.TurnId,
                    out var reconciledAssistantMessageId)
                && string.Equals(
                    reconciledAssistantMessageId,
                    result.AssistantMessageId,
                    StringComparison.Ordinal);
            if ((turn.KnownTurnIds.Contains(result.TurnId)
                    && !eventClaimedTurn
                    && !knownIdempotentReconciliation)
                || (turn.ClaimedAssistantMessageIds.TryGetValue(
                        result.TurnId,
                        out var claimedAssistantMessageId)
                    && !string.Equals(
                        claimedAssistantMessageId,
                        result.AssistantMessageId,
                        StringComparison.Ordinal)))
            {
                return false;
            }
            turn.KnownTurnIds.Add(result.TurnId);
            turn.ClaimedAssistantMessageIds[result.TurnId] =
                result.AssistantMessageId;
            if (!result.CreatesOrRejoinsTurn)
                turn.CompletedTurnIds.Add(result.TurnId);
            turn.AwaitingReceipt = false;
            turn.AwaitingNativeAssistantContinuationReceipt = false;
            turn.PendingTurnId = string.Empty;
            turn.PendingUserMessageId = string.Empty;
            turn.PendingAssistantMessageId = string.Empty;
            return true;
        }
    }

    private void RestoreAfterRejectedNativeAssistantContinuation(string sessionId)
    {
        lock (_sessionLock)
        {
            if (!_sessionTurns.TryGetValue(sessionId, out var turn)
                || !turn.AwaitingNativeAssistantContinuationReceipt)
            {
                return;
            }
            turn.AwaitingReceipt = false;
            turn.AwaitingNativeAssistantContinuationReceipt = false;
            turn.PendingTurnId = string.Empty;
            turn.PendingUserMessageId = string.Empty;
            turn.PendingAssistantMessageId = string.Empty;
        }
    }

    private void MarkNativeAssistantContinuationUncertain(string sessionId)
    {
        lock (_sessionLock)
        {
            if (_sessionTurns.TryGetValue(sessionId, out var turn))
            {
                turn.AwaitingReceipt = true;
                turn.AwaitingNativeAssistantContinuationReceipt = true;
            }
        }
    }

    internal void ReconcileNativeAssistantContinuationAfterInterrupt(
        string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        RestoreAfterRejectedNativeAssistantContinuation(sessionId);
    }

    private async Task<LilithRuntimeResponse> RouteNoteDraftAsync(
        JsonElement parameters, CancellationToken cancellationToken)
    {
        const string method = "prompt.draft_note";
        var sessionId = parameters.GetProperty("session_id").GetString()!;
        var operationId = parameters.GetProperty("operation_id").GetString()!;
        var eventId = parameters.GetProperty("event_id").GetString()!;
        LilithRuntimeResponse Terminal(string status) => Success(method,
            JsonSerializer.SerializeToElement(new
            {
                operation_id = operationId, event_id = eventId, status, text = string.Empty
            }));
        var authority = _modelAuthority.Snapshot();
        var lineage = ActiveGameLineageAttestation;
        lock (_sessionLock)
        {
            // No eviction/replay and no draft text retained in the ledger.
            if (_noteDraftOperations.Count >= 4096 || _noteDraftEvents.Count >= 4096)
                return Terminal("failed");
            var newOperation = _noteDraftOperations.Add(operationId);
            var newEvent = _noteDraftEvents.Add(eventId);
            if (!newOperation || !newEvent)
                return Terminal("failed");
            if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
                || lineage is null || !lineage.Archived || lineage.Source != "lilith-game"
                || !authority.HasApprovedDefault || authority.GenerationId != _generationId)
                return Terminal("failed");
            if (VoiceBlocksConversationMutation() || CheckpointRestoreBlocksConversationMutation()
                || _sessionTurns.TryGetValue(sessionId, out var turn)
                    && (turn.AwaitingReceipt || turn.AwaitingQueuedTurn
                        || turn.ActiveAssistantMessageIds.Count != 0))
                return Terminal("busy");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LilithGameBridgeProtocol.MaximumNoteDraftMilliseconds);
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            // This auxiliary RPC has no turn/history/event projection or delivery sink.
            var pending = _requestAsync(method, parameters, deadline.Token);
            var result = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            var current = _modelAuthority.Snapshot();
            if (current != authority || !IsActiveAttestedSession(sessionId)
                || ActiveGameLineageAttestation?.FingerprintSha256 != lineage.FingerprintSha256)
                return Terminal("failed");
            var values = ReadExactObject(result, new[] { "operation_id", "event_id", "status", "text" });
            if (RequiredNoteIdentity(values, "operation_id").ToString("N") != operationId
                || RequiredNoteIdentity(values, "event_id").ToString("N") != eventId)
                throw new InvalidDataException("Note draft receipt identities were rejected.");
            var state = LilithGameBridgeProtocol.ParseNoteDraftState(RequiredText(values, "status", 16));
            var text = RequiredNoteText(values, "text", 1200, state != LilithGameBridgeNoteDraftState.Ready);
            var receipt = new LilithGameBridgeNoteDraftResponse(
                _generationId, Guid.ParseExact(operationId, "N"), 2, Guid.ParseExact(eventId, "N"), state, text);
            return Success(method, JsonSerializer.SerializeToElement(new
            {
                operation_id = operationId, event_id = eventId,
                status = LilithGameBridgeProtocol.NoteDraftStateToken(receipt.State), text = receipt.Text
            }));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Terminal("failed");
        }
    }

    private static Guid RequiredNoteIdentity(
        IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var text = RequiredText(values, name, 32);
        if (!Guid.TryParseExact(text, "N", out var identity) || identity == Guid.Empty
            || identity.ToString("N") != text)
            throw new InvalidDataException("An exact note draft identity is required.");
        return identity;
    }

    private static string RequiredNoteText(
        IReadOnlyDictionary<string, JsonElement> values, string name, int maximum, bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A note draft text field is required.");
        var text = value.GetString()!;
        if (text.Length > maximum || (!allowEmpty && string.IsNullOrWhiteSpace(text))
            || text.Any(static c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > maximum * 4)
            throw new InvalidDataException("A note draft text field was rejected.");
        return text;
    }

    private async Task<LilithRuntimeResponse> RouteSpeechProjectionAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var result = await _requestAsync(
            "prompt.project_speech",
            parameters,
            cancellationToken).ConfigureAwait(false);
        var values = ReadExactObject(result, new[] { "speech_text", "speech_emotion" });
        var speechText = RequiredConversationText(
            values,
            "speech_text",
            MaximumPromptUtf8Bytes);
        var speechEmotion = OptionalText(values, "speech_emotion", maximumUtf8Bytes: 16) ?? string.Empty;
        if (!LilithJapaneseSpeechEmotion.IsValid(speechEmotion)
            || (speechEmotion.Length != 0
                && (!parameters.TryGetProperty("speech_locale", out var locale) || locale.GetString() != "ja"
                    || !parameters.TryGetProperty("turn_id", out _))))
            throw new InvalidDataException("Speech delivery metadata was rejected.");
        var speechResult = new Dictionary<string, object> { ["speech_text"] = speechText };
        if (speechEmotion.Length != 0) speechResult["speech_emotion"] = speechEmotion;
        return Success("prompt.project_speech", JsonSerializer.SerializeToElement(speechResult));
    }

    private async Task<LilithRuntimeResponse> RouteSessionCloseAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime session close requires a session id.");
        try
        {
            var result = await _requestAsync(
                "session.close",
                parameters,
                cancellationToken).ConfigureAwait(false);
            _ = ValidateSessionCloseResult(result);
            CompleteSessionClose(sessionId);
            return Success("session.close", result);
        }
        catch
        {
            MarkImageLifecycleUncertain(sessionId);
            throw;
        }
    }

    private async Task<LilithRuntimeResponse> RouteSessionDeleteAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var runtimeSessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime session delete requires a runtime session id.");
        var storedSessionId = parameters.GetProperty("stored_session_id").GetString()!;
        lock (_sessionLock)
        {
            if (!string.Equals(
                    _activeSessionId,
                    runtimeSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    _activeStoredSessionId,
                    storedSessionId,
                    StringComparison.Ordinal))
            {
                return Failure(
                    "session.delete",
                    "runtime.session_delete_mapping_changed",
                    "Only the exact active Runtime and durable session pair may be deleted.");
            }
        }

        bool closed;
        try
        {
            var closeResult = await _requestAsync(
                "session.close",
                JsonSerializer.SerializeToElement(new
                {
                    session_id = runtimeSessionId
                }),
                cancellationToken).ConfigureAwait(false);
            closed = ValidateSessionCloseResult(closeResult);
        }
        catch (HermesRequestNotSentException)
        {
            return Failure(
                "session.delete",
                "runtime.session_delete_close_not_sent",
                "The active session close was not sent; the durable session was preserved.");
        }
        catch (HermesRpcException exception)
        {
            return Failure(
                "session.delete",
                "runtime.session_delete_close_rejected",
                "Hermes rejected the active session close; the durable session was preserved.",
                exception.Code);
        }
        catch
        {
            MarkImageLifecycleUncertain(runtimeSessionId);
            QuarantineSessionDelete();
            return Failure(
                "session.delete",
                "runtime.session_delete_close_unverified",
                "The close receipt could not be verified; no durable delete was dispatched.");
        }

        CompleteSessionClose(runtimeSessionId);

        try
        {
            var deleteResult = await _requestAsync(
                "session.delete",
                JsonSerializer.SerializeToElement(new
                {
                    session_id = storedSessionId
                }),
                cancellationToken).ConfigureAwait(false);
            ValidateSessionDeleteResult(deleteResult, storedSessionId);
        }
        catch (HermesRequestNotSentException)
        {
            return Failure(
                "session.delete",
                "runtime.session_delete_delete_not_sent",
                "The durable delete was not sent; the closed session may be safely resumed.");
        }
        catch (HermesRpcException exception) when (exception.Code == 4007)
        {
            // Absence is the requested authoritative postcondition. This also
            // makes a retry after a lost success response idempotent.
        }
        catch (HermesRpcException exception)
        {
            return Failure(
                "session.delete",
                "runtime.session_delete_delete_rejected",
                "Hermes rejected the durable delete; the closed session may be safely resumed.",
                exception.Code);
        }
        catch
        {
            QuarantineSessionDelete();
            return Failure(
                "session.delete",
                "runtime.session_delete_outcome_uncertain",
                "The durable session-delete outcome is uncertain; Runtime restart is required.");
        }

        return Success(
            "session.delete",
            JsonSerializer.SerializeToElement(new
            {
                closed,
                deleted = storedSessionId
            }));
    }

    private async Task<LilithRuntimeResponse> RouteSessionTitleAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime session title requires a session id.");
        lock (_sessionLock)
        {
            if (!string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal))
            {
                return Failure(
                    "session.title",
                    "runtime.session_title_mapping_changed",
                    "Only the exact active Runtime session may be renamed.");
            }
        }
        var expectedTitle = parameters.GetProperty("title").GetString()!;
        try
        {
            var result = await _requestAsync(
                "session.title",
                parameters,
                cancellationToken).ConfigureAwait(false);
            ValidateSessionTitleResult(result, expectedTitle);
            return Success("session.title", result);
        }
        catch (HermesRequestNotSentException)
        {
            return Failure(
                "session.title",
                "runtime.session_title_not_sent",
                "The session title change was not sent.");
        }
        catch (HermesRpcException exception)
        {
            return Failure(
                "session.title",
                "runtime.session_title_rejected",
                "Hermes rejected the session title change.",
                exception.Code);
        }
        catch
        {
            return Failure(
                "session.title",
                "runtime.session_title_outcome_uncertain",
                "The session title outcome is uncertain; reconnect before continuing.");
        }
    }

    private bool TryBeginImageAttach(string sessionId)
    {
        lock (_sessionLock)
        {
            if (!IsActiveAttestedSessionLocked(sessionId))
                return false;
            var lifecycle = GetOrCreateImageLifecycleLocked(sessionId);
            if (lifecycle.State != SessionImageLifecycleState.Idle
                || lifecycle.Attachment is not null)
            {
                return false;
            }
            lifecycle.State = SessionImageLifecycleState.AttachInFlight;
            return true;
        }
    }

    private bool TryCommitImageAttach(
        string sessionId,
        TrackedImageAttachment attachment)
    {
        lock (_sessionLock)
        {
            if (!IsActiveAttestedSessionLocked(sessionId)
                || !_sessionImages.TryGetValue(sessionId, out var lifecycle)
                || lifecycle.State != SessionImageLifecycleState.AttachInFlight
                || lifecycle.Attachment is not null)
            {
                return false;
            }
            lifecycle.Attachment = attachment;
            lifecycle.State = SessionImageLifecycleState.Attached;
            return true;
        }
    }

    private bool TryBeginImageDetach(string sessionId, string path)
    {
        lock (_sessionLock)
        {
            if (!IsActiveAttestedSessionLocked(sessionId)
                || !_sessionImages.TryGetValue(sessionId, out var lifecycle)
                || lifecycle.State != SessionImageLifecycleState.Attached
                || lifecycle.Attachment is null
                || !string.Equals(
                    lifecycle.Attachment.Path,
                    path,
                    StringComparison.Ordinal))
            {
                return false;
            }
            lifecycle.State = SessionImageLifecycleState.DetachInFlight;
            return true;
        }
    }

    private bool TryCommitImageDetach(string sessionId, string path)
    {
        lock (_sessionLock)
        {
            if (!_sessionImages.TryGetValue(sessionId, out var lifecycle)
                || lifecycle.State != SessionImageLifecycleState.DetachInFlight
                || lifecycle.Attachment is null
                || !string.Equals(
                    lifecycle.Attachment.Path,
                    path,
                    StringComparison.Ordinal))
            {
                return false;
            }
            lifecycle.Attachment = null;
            lifecycle.State = SessionImageLifecycleState.Idle;
            return true;
        }
    }

    private bool TryBeginPromptSubmit(string sessionId, out bool hasPendingImage)
    {
        lock (_sessionLock)
        {
            hasPendingImage = false;
            if (!IsActiveAttestedSessionLocked(sessionId))
                return false;
            var turn = GetOrCreateTurnCorrelationLocked(sessionId);
            if (turn.AwaitingReceipt || turn.AwaitingQueuedTurn)
                return false;
            turn.AwaitingReceipt = true;
            turn.PendingTurnId = string.Empty;
            turn.PendingUserMessageId = string.Empty;
            turn.PendingAssistantMessageId = string.Empty;
            var lifecycle = GetOrCreateImageLifecycleLocked(sessionId);
            if (lifecycle.State == SessionImageLifecycleState.Idle
                && lifecycle.Attachment is null)
            {
                lifecycle.TerminalObservedBeforeReceipt = false;
                lifecycle.InlineReceiptConfirmed = false;
                lifecycle.State = SessionImageLifecycleState.SubmitAwaitingStart;
                return true;
            }
            if (lifecycle.State != SessionImageLifecycleState.Attached
                || lifecycle.Attachment is null)
            {
                turn.AwaitingReceipt = false;
                return false;
            }
            lifecycle.TerminalObservedBeforeReceipt = false;
            lifecycle.InlineReceiptConfirmed = false;
            lifecycle.State = SessionImageLifecycleState.SubmitAwaitingStart;
            hasPendingImage = true;
            return true;
        }
    }

    private bool CompletePromptReceipt(
        string sessionId,
        PromptImageDisposition disposition,
        bool hadPendingImage)
    {
        lock (_sessionLock)
        {
            if (!_sessionImages.TryGetValue(sessionId, out var lifecycle))
                return false;
            if (lifecycle.State == SessionImageLifecycleState.Uncertain)
                return false;
            if (lifecycle.State == SessionImageLifecycleState.Idle
                && lifecycle.Attachment is null)
            {
                return true;
            }
            if (lifecycle.State != SessionImageLifecycleState.SubmitAwaitingStart
                || (lifecycle.Attachment is not null) != hadPendingImage)
            {
                lifecycle.State = SessionImageLifecycleState.Uncertain;
                return false;
            }
            if (disposition == PromptImageDisposition.Consumed)
            {
                lifecycle.Attachment = null;
                lifecycle.State = SessionImageLifecycleState.Idle;
                lifecycle.TerminalObservedBeforeReceipt = false;
                lifecycle.InlineReceiptConfirmed = false;
                return true;
            }
            if (disposition == PromptImageDisposition.AwaitQueuedStart
                && _sessionTurns.TryGetValue(sessionId, out var turn)
                && turn.PendingTurnId.Length != 0)
            {
                // A correlated event for the queued turn may beat the RPC
                // receipt.  That event is already the proof normally supplied
                // by message.start; a terminal-first turn must not leave the
                // image/prompt gate waiting for an event that will never come.
                lifecycle.Attachment = null;
                lifecycle.State = SessionImageLifecycleState.Idle;
                lifecycle.TerminalObservedBeforeReceipt = false;
                lifecycle.InlineReceiptConfirmed = false;
                return true;
            }
            lifecycle.InlineReceiptConfirmed =
                disposition == PromptImageDisposition.AwaitInlineStart;
            if (!lifecycle.InlineReceiptConfirmed
                || !lifecycle.TerminalObservedBeforeReceipt)
                return true;
            lifecycle.State = SessionImageLifecycleState.Uncertain;
            return false;
        }
    }

    private void RestoreAfterRejectedPrompt(
        string sessionId,
        bool hadPendingImage)
    {
        lock (_sessionLock)
        {
            if (_sessionImages.TryGetValue(sessionId, out var lifecycle)
                && lifecycle.State == SessionImageLifecycleState.SubmitAwaitingStart)
            {
                if ((lifecycle.Attachment is not null) != hadPendingImage)
                {
                    lifecycle.State = SessionImageLifecycleState.Uncertain;
                }
                else
                {
                    lifecycle.State = hadPendingImage
                        ? SessionImageLifecycleState.Attached
                        : SessionImageLifecycleState.Idle;
                    lifecycle.TerminalObservedBeforeReceipt = false;
                    lifecycle.InlineReceiptConfirmed = false;
                }
            }
            else if (lifecycle is not null
                && lifecycle.State == SessionImageLifecycleState.Idle)
            {
                // A start event proves Hermes claimed the image, but an RPC
                // rejection means the caller did not receive the accepted
                // receipt and may still display it.  Only close/restart can
                // safely reconcile that cross-layer disagreement.
                lifecycle.State = SessionImageLifecycleState.Uncertain;
            }
            if (_sessionTurns.TryGetValue(sessionId, out var turn))
            {
                turn.AwaitingReceipt = false;
                turn.AwaitingQueuedTurn = false;
                turn.PendingTurnId = string.Empty;
                turn.PendingUserMessageId = string.Empty;
                turn.PendingAssistantMessageId = string.Empty;
            }
        }
    }

    private bool CompletePromptTurnReceipt(
        string sessionId,
        HermesPromptSubmitResult result)
    {
        lock (_sessionLock)
        {
            if (!_sessionTurns.TryGetValue(sessionId, out var turn)
                || !turn.AwaitingReceipt)
            {
                return false;
            }
            if (result.CreatesTurn)
            {
                if ((turn.PendingTurnId.Length != 0
                        && !string.Equals(
                            turn.PendingTurnId,
                            result.TurnId,
                            StringComparison.Ordinal))
                    || (turn.PendingUserMessageId.Length != 0
                        && !string.Equals(
                            turn.PendingUserMessageId,
                            result.UserMessageId,
                            StringComparison.Ordinal))
                    || (turn.PendingAssistantMessageId.Length != 0
                        && !string.Equals(
                            turn.PendingAssistantMessageId,
                            result.AssistantMessageId,
                            StringComparison.Ordinal)))
                {
                    return false;
                }
                var eventClaimedTurn = string.Equals(
                    turn.PendingTurnId,
                    result.TurnId,
                    StringComparison.Ordinal);
                if (turn.KnownTurnIds.Contains(result.TurnId) && !eventClaimedTurn)
                    return false;
                if (turn.ClaimedAuxiliaryAssistantMessageIds.Contains(
                        (result.TurnId, result.UserMessageId))
                    || turn.ClaimedAuxiliaryAssistantMessageIds.Contains(
                        (result.TurnId, result.AssistantMessageId)))
                {
                    return false;
                }
                if (turn.ClaimedUserMessageIds.TryGetValue(
                        result.TurnId,
                        out var claimedUserMessageId)
                    && !string.Equals(
                        claimedUserMessageId,
                        result.UserMessageId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                if (turn.ClaimedAssistantMessageIds.TryGetValue(
                        result.TurnId,
                        out var claimedAssistantMessageId)
                    && !string.Equals(
                        claimedAssistantMessageId,
                        result.AssistantMessageId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                turn.KnownTurnIds.Add(result.TurnId);
                turn.ClaimedUserMessageIds[result.TurnId] = result.UserMessageId;
                turn.ClaimedAssistantMessageIds[result.TurnId] =
                    result.AssistantMessageId;
                turn.AwaitingReceipt = false;
                turn.PendingTurnId = string.Empty;
                turn.PendingUserMessageId = string.Empty;
                turn.PendingAssistantMessageId = string.Empty;
                return true;
            }

            var queuedTurnAlreadyObserved = turn.PendingTurnId.Length != 0;
            if (queuedTurnAlreadyObserved
                && result.Disposition != HermesPromptSubmitDisposition.Queued)
            {
                return false;
            }
            turn.AwaitingReceipt = false;
            turn.AwaitingQueuedTurn =
                result.Disposition == HermesPromptSubmitDisposition.Queued
                && !queuedTurnAlreadyObserved;
            turn.PendingTurnId = string.Empty;
            turn.PendingUserMessageId = string.Empty;
            turn.PendingAssistantMessageId = string.Empty;
            return true;
        }
    }

    private void ObservePromptStart(string sessionId)
    {
        lock (_sessionLock)
        {
            if (_sessionImages.TryGetValue(sessionId, out var lifecycle)
                && lifecycle.State == SessionImageLifecycleState.SubmitAwaitingStart)
            {
                lifecycle.Attachment = null;
                lifecycle.State = SessionImageLifecycleState.Idle;
                lifecycle.TerminalObservedBeforeReceipt = false;
                lifecycle.InlineReceiptConfirmed = false;
            }
        }
    }

    private void ObservePromptTerminalBeforeStart(string sessionId)
    {
        lock (_sessionLock)
        {
            if (_sessionImages.TryGetValue(sessionId, out var lifecycle)
                && lifecycle.State == SessionImageLifecycleState.SubmitAwaitingStart)
            {
                if (lifecycle.InlineReceiptConfirmed)
                {
                    lifecycle.State = SessionImageLifecycleState.Uncertain;
                }
                else
                {
                    lifecycle.TerminalObservedBeforeReceipt = true;
                }
            }
        }
    }

    private void MarkImageLifecycleUncertain(string sessionId)
    {
        lock (_sessionLock)
        {
            if (!string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
                && !_sessionImages.ContainsKey(sessionId))
            {
                return;
            }
            GetOrCreateImageLifecycleLocked(sessionId).State =
                SessionImageLifecycleState.Uncertain;
        }
    }

    private bool IsActiveAttestedSessionLocked(string sessionId)
        => string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
            && _sessionStates.TryGetValue(sessionId, out var state)
            && state == SessionNativeSurfaceState.Attested;

    private SessionImageLifecycle GetOrCreateImageLifecycleLocked(string sessionId)
    {
        if (!_sessionImages.TryGetValue(sessionId, out var lifecycle))
        {
            lifecycle = new SessionImageLifecycle();
            _sessionImages.Add(sessionId, lifecycle);
        }
        return lifecycle;
    }

    private TrackedImageAttachment ValidateImageAttachResult(
        JsonElement result,
        ImageAttachExpectation expectation)
    {
        var values = ReadExactObject(
            result,
            new[]
            {
                "attached", "path", "name", "count", "bytes", "remainder",
                "text", "width", "height", "token_estimate"
            });
        if (!values.TryGetValue("attached", out var attached)
            || attached.ValueKind != JsonValueKind.True)
        {
            throw new InvalidDataException(
                "Hermes did not confirm the exact image attachment.");
        }

        var path = RequiredText(values, "path", MaximumImagePathUtf8Bytes);
        var name = RequiredText(values, "name", MaximumImageFilenameUtf8Bytes);
        var remainder = RequiredTextAllowEmpty(
            values,
            "remainder",
            MaximumImageTextUtf8Bytes);
        var text = RequiredText(values, "text", MaximumImageTextUtf8Bytes);
        var count = RequiredInteger(values, "count", 1, int.MaxValue);
        var bytes = RequiredInteger(values, "bytes", 1, MaximumImageBytes);
        string canonicalPath;
        string? parent;
        try
        {
            canonicalPath = Path.GetFullPath(path);
            parent = Path.GetDirectoryName(canonicalPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidDataException(
                "Hermes returned a non-canonical image path.",
                exception);
        }
        if (bytes != expectation.Bytes
            || remainder.Length != 0
            || count != 1
            || !Path.IsPathFullyQualified(path)
            || !PathsEqual(path, canonicalPath)
            || parent is null
            || !PathsEqual(parent, _hermesImagesPath)
            || !string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal)
            || !string.Equals(
                Path.GetExtension(path),
                expectation.Extension,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetExtension(name),
                expectation.Extension,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                text,
                $"[User attached image: {name}]",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched image attachment.");
        }

        var hasWidth = values.ContainsKey("width");
        var hasHeight = values.ContainsKey("height");
        var hasTokenEstimate = values.ContainsKey("token_estimate");
        if (hasWidth != hasHeight || hasWidth != hasTokenEstimate)
        {
            throw new InvalidDataException(
                "Hermes returned incomplete image metadata.");
        }
        if (hasWidth)
        {
            var width = RequiredInteger(values, "width", 1, int.MaxValue);
            var height = RequiredInteger(values, "height", 1, int.MaxValue);
            var tokenEstimate = RequiredInteger(
                values,
                "token_estimate",
                85,
                int.MaxValue);
            var expectedTokens = checked(
                (((long)width + 511L) / 512L)
                * (((long)height + 511L) / 512L)
                * 85L);
            if (expectedTokens > int.MaxValue || tokenEstimate != expectedTokens)
            {
                throw new InvalidDataException(
                    "Hermes returned mismatched image metadata.");
            }
        }

        ValidateAttachedImageFile(canonicalPath, expectation);
        return new TrackedImageAttachment(
            canonicalPath,
            expectation.Sha256,
            expectation.Bytes,
            expectation.Extension);
    }

    private static void ValidateImageDetachResult(JsonElement result)
    {
        var values = ReadExactObject(result, new[] { "detached", "count" });
        if (!values.TryGetValue("detached", out var detached)
            || detached.ValueKind != JsonValueKind.True)
        {
            throw new InvalidDataException(
                "Hermes did not confirm the exact image detach.");
        }
        if (RequiredInteger(values, "count", 0, int.MaxValue) != 0)
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched image detach count.");
        }
    }

    private static PromptImageDisposition ValidatePromptSubmitResult(
        HermesPromptSubmitResult promptResult,
        JsonElement result,
        bool hadPendingImage)
    {
        _ = result;
        return promptResult.Disposition switch
        {
            HermesPromptSubmitDisposition.Streaming when promptResult.TurnIsolation =>
                PromptImageDisposition.Consumed,
            HermesPromptSubmitDisposition.Streaming =>
                PromptImageDisposition.AwaitInlineStart,
            HermesPromptSubmitDisposition.Queued => hadPendingImage
                ? PromptImageDisposition.Consumed
                : PromptImageDisposition.AwaitQueuedStart,
            HermesPromptSubmitDisposition.Steered
                or HermesPromptSubmitDisposition.Redirected when !hadPendingImage =>
                PromptImageDisposition.Consumed,
            HermesPromptSubmitDisposition.VoiceStopped when !hadPendingImage =>
                PromptImageDisposition.Consumed,
            _ => throw new InvalidDataException(
                "Hermes returned a prompt receipt incompatible with image ownership.")
        };
    }

    private static bool ValidateSessionCloseResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session close receipt.");
        }
        var properties = result.EnumerateObject().ToArray();
        if (properties.Length != 1
            || !properties[0].NameEquals("closed")
            || properties[0].Value.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session close receipt.");
        }
        return properties[0].Value.GetBoolean();
    }

    private static void ValidateSessionDeleteResult(
        JsonElement result,
        string expectedStoredSessionId)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session delete receipt.");
        }
        var properties = result.EnumerateObject().ToArray();
        if (properties.Length != 1
            || !properties[0].NameEquals("deleted")
            || properties[0].Value.ValueKind != JsonValueKind.String
            || !string.Equals(
                properties[0].Value.GetString(),
                expectedStoredSessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session delete receipt.");
        }
    }

    private static void ValidateSessionTitleResult(
        JsonElement result,
        string expectedTitle)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session title receipt.");
        }
        var properties = result.EnumerateObject().ToArray();
        if (properties.Length != 2
            || !result.TryGetProperty("pending", out var pending)
            || pending.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !result.TryGetProperty("title", out var title)
            || title.ValueKind != JsonValueKind.String
            || !string.Equals(title.GetString(), expectedTitle, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes returned a malformed session title receipt.");
        }
    }

    private void ValidateAttachedImageFile(
        string path,
        ImageAttachExpectation expectation)
    {
        try
        {
            var imageDirectory = new DirectoryInfo(_hermesImagesPath);
            imageDirectory.Refresh();
            var imageFile = new FileInfo(path);
            imageFile.Refresh();
            if (!imageDirectory.Exists
                || (imageDirectory.Attributes & FileAttributes.ReparsePoint) != 0
                || imageDirectory.LinkTarget is not null
                || !imageFile.Exists
                || (imageFile.Attributes & FileAttributes.ReparsePoint) != 0
                || imageFile.LinkTarget is not null
                || imageFile.Length != expectation.Bytes)
            {
                throw new InvalidDataException(
                    "Hermes image storage did not match the attach receipt.");
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length != expectation.Bytes
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(stream)),
                    expectation.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes image bytes did not match the attach request.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or NotSupportedException)
        {
            throw new InvalidDataException(
                "Hermes image storage could not be verified.",
                exception);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string RequiredTextAllowEmpty(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Runtime result requires '{name}'.");
        }
        var text = value.GetString()!;
        if (text.Any(char.IsControl)
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime result parameter '{name}' was rejected.");
        }
        return text;
    }

    private static int RequiredInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int minimum,
        int maximum)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var integer)
            || integer < minimum
            || integer > maximum)
        {
            throw new InvalidDataException(
                $"Runtime result parameter '{name}' was rejected.");
        }
        return integer;
    }

    private static void OptionalResultInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int minimum,
        int maximum)
    {
        if (!values.ContainsKey(name))
            return;
        RequiredInteger(values, name, minimum, maximum);
    }

    private sealed record ImageAttachExpectation(
        string SessionId,
        int Bytes,
        string Extension,
        string Sha256)
    {
        internal static ImageAttachExpectation FromCanonical(JsonElement parameters)
        {
            var sessionId = parameters.GetProperty("session_id").GetString()!;
            var base64 = parameters.GetProperty("content_base64").GetString()!;
            var filename = parameters.GetProperty("filename").GetString()!;
            var padding = base64.EndsWith("==", StringComparison.Ordinal)
                ? 2
                : base64.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;
            var bytes = checked((base64.Length / 4) * 3 - padding);
            return new(
                sessionId,
                bytes,
                Path.GetExtension(filename).ToLowerInvariant(),
                Path.GetFileNameWithoutExtension(filename));
        }
    }

    private sealed record TrackedImageAttachment(
        string Path,
        string Sha256,
        int Bytes,
        string Extension);

    private void ValidateOptionalApprovedPair(
        string? providerId,
        string? modelId)
    {
        if (providerId is null && modelId is null)
            return;
        if (providerId is null || modelId is null)
        {
            throw new InvalidDataException(
                "Runtime provider and model must be selected together.");
        }
        ValidateApprovedPair(providerId, modelId);
    }

    private void ValidateApprovedPair(string providerId, string modelId)
    {
        if (!IsSafeProviderToken(providerId)
            || !IsSafeModelToken(modelId)
            || !_approvedCatalog().Allows(providerId, modelId))
        {
            throw new InvalidDataException(
                "Runtime provider and model selection was rejected.");
        }
    }

    private static bool IsSafeProviderToken(string value)
    {
        // A named native endpoint is a model identity, not a built-in mutable
        // credential slug. Permit precisely that prefix, never arbitrary ':'
        // or whitespace that could become model-switch command arguments.
        var name = value.StartsWith("custom:", StringComparison.Ordinal) ? value[7..] : value;
        return name.Length > 0
            && name[0] != '-'
            && name.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_');
    }

    private static bool IsSafeModelToken(string value)
        => value.Length > 0
            && value[0] != '-'
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or '/' or ':' or '@' or '+');

    private JsonElement IntersectModelOptions(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed provider catalog.");
        }

        var root = JsonNode.Parse(result.GetRawText()) as JsonObject
            ?? throw new InvalidDataException(
                "Hermes returned a malformed provider catalog.");
        if (!root.TryGetPropertyValue("providers", out var providersNode)
            || providersNode is not JsonArray providers)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed provider catalog.");
        }

        var approved = _approvedCatalog();
        var projected = new JsonArray();
        var observedProviders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var providerNode in providers)
        {
            if (providerNode is not JsonObject provider
                || !TryString(provider, "slug", out var providerId))
            {
                throw new InvalidDataException(
                    "Hermes returned a malformed provider catalog.");
            }
            if (!approved.Providers.TryGetValue(providerId, out var approvedModels))
                continue;
            if (!observedProviders.Add(providerId))
            {
                throw new InvalidDataException(
                    "Hermes returned a duplicate provider catalog.");
            }
            if (!provider.TryGetPropertyValue("models", out var modelsNode)
                || modelsNode is not JsonArray models)
            {
                throw new InvalidDataException(
                    "Hermes returned a malformed provider catalog.");
            }

            var selectedModels = new List<string>();
            var observedModels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var modelNode in models)
            {
                if (modelNode is not JsonValue modelValue
                    || !modelValue.TryGetValue<string>(out var modelId))
                {
                    throw new InvalidDataException(
                        "Hermes returned a malformed provider catalog.");
                }
                if (approvedModels.Contains(modelId)
                    && observedModels.Add(modelId))
                {
                    selectedModels.Add(modelId);
                }
            }
            if (selectedModels.Count == 0)
                continue;

            var copy = (JsonObject)provider.DeepClone();
            copy["models"] = new JsonArray(
                selectedModels
                    .Select(static model => (JsonNode?)JsonValue.Create(model))
                    .ToArray());
            copy["total_models"] = selectedModels.Count;
            FilterStringArray(copy, "featured_models", observedModels);
            FilterStringArray(copy, "unavailable_models", observedModels);
            FilterModelObject(copy, "pricing", observedModels);
            FilterModelObject(copy, "capabilities", observedModels);
            projected.Add(copy);
        }
        root["providers"] = projected;

        var selectedProvider = TryString(root, "provider", out var currentProvider)
            ? currentProvider
            : string.Empty;
        var selectedModel = TryString(root, "model", out var model)
            ? model
            : string.Empty;
        if (!approved.Allows(selectedProvider, selectedModel)
            && approved.HasDefault)
        {
            root["provider"] = approved.DefaultProviderId;
            root["model"] = approved.DefaultModelId;
        }
        else if (!approved.Allows(selectedProvider, selectedModel))
        {
            root["provider"] = string.Empty;
            root["model"] = string.Empty;
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static void FilterStringArray(
        JsonObject provider,
        string property,
        IReadOnlySet<string> selectedModels)
    {
        if (!provider.TryGetPropertyValue(property, out var node) || node is null)
            return;
        if (node is not JsonArray values)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed provider catalog.");
        }
        var filtered = new JsonArray();
        foreach (var value in values)
        {
            if (value is not JsonValue item
                || !item.TryGetValue<string>(out var modelId))
            {
                throw new InvalidDataException(
                    "Hermes returned a malformed provider catalog.");
            }
            if (selectedModels.Contains(modelId))
                filtered.Add(modelId);
        }
        provider[property] = filtered;
    }

    private static void FilterModelObject(
        JsonObject provider,
        string property,
        IReadOnlySet<string> selectedModels)
    {
        if (!provider.TryGetPropertyValue(property, out var node) || node is null)
            return;
        if (node is not JsonObject values)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed provider catalog.");
        }
        var filtered = new JsonObject();
        foreach (var value in values)
        {
            if (selectedModels.Contains(value.Key))
                filtered[value.Key] = value.Value?.DeepClone();
        }
        provider[property] = filtered;
    }

    private static bool TryString(
        JsonObject value,
        string property,
        out string text)
    {
        text = string.Empty;
        if (!value.TryGetPropertyValue(property, out var node)
            || node is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out var candidate)
            || candidate is null)
        {
            return false;
        }
        text = candidate;
        return true;
    }

    private void ObserveGatewayResult(
        string method,
        JsonElement parameters,
        JsonElement result)
    {
        if (method is "session.create" or "session.resume")
        {
            try
            {
                var sessionId = ResultSessionId(result);
                var storedSessionId = OptionalResultStoredSessionId(method, result);
                LilithGameSessionLineageAttestation? gameLineageAttestation = null;
                if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge)
                {
                    if (storedSessionId is null)
                    {
                        throw new LilithGameSessionLineageRejectedException(
                            "Hermes omitted the durable game session identity.");
                    }
                    var requestedStoredSessionId = string.Equals(
                            method,
                            "session.resume",
                            StringComparison.Ordinal)
                        ? OptionalSessionId(parameters)
                            ?? throw new LilithGameSessionLineageRejectedException(
                                "session.resume omitted its requested durable game session identity.")
                        : storedSessionId;
                    if (result.TryGetProperty("durable_lineage", out _))
                    {
                        gameLineageAttestation = DecodeGameDurableLineage(
                            requestedStoredSessionId,
                            storedSessionId,
                            result);
                    }
                    else if (string.Equals(
                                 method,
                                 "session.resume",
                                 StringComparison.Ordinal))
                    {
                        // Only a pinned Hermes durable-lineage receipt may
                        // authorize parent-to-compression-tip substitution.
                        // A legacy/malformed success is isolated without
                        // weakening Workbench's exact-ID rule below.
                        throw new LilithGameSessionLineageRejectedException(
                            "Hermes did not attest the resumed game session lineage.");
                    }
                }
                else if (string.Equals(
                             method,
                             "session.resume",
                             StringComparison.Ordinal))
                {
                    var requestedStoredSessionId = OptionalSessionId(parameters)
                        ?? throw new InvalidDataException(
                            "session.resume omitted its requested durable session identity.");
                    if (storedSessionId is null
                        || !string.Equals(
                            requestedStoredSessionId,
                            storedSessionId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Hermes resumed a durable session other than the one requested.");
                    }
                }
                // This lock protects the activation identity. Do not acquire
                // ModelAuthority while holding it: native-config writers take
                // the authority first and then invalidate this session.
                var activationNativeConfigRevision = _modelAuthority.Snapshot()
                    .NativeConfigRevision;
                HermesGatewayEvent? bufferedSessionInfo;
                lock (_sessionLock)
                {
                    if (_pendingSessionActivation is null
                        || !string.Equals(
                            _pendingSessionActivation.Method,
                            method,
                            StringComparison.Ordinal))
                    {
                        _sessionActivationQuarantined = true;
                        throw new LilithSessionActivationEventException(
                            "Runtime could not bind the Hermes session activation receipt to its pending lifecycle request.");
                    }
                    if (_pendingSessionActivation.RejectionMessage is not null)
                    {
                        _sessionActivationQuarantined = true;
                        throw new LilithSessionActivationEventException(
                            _pendingSessionActivation.RejectionMessage);
                    }
                    bufferedSessionInfo =
                        _pendingSessionActivation.BufferedSessionInfo;
                    if (bufferedSessionInfo is not null
                        && !string.Equals(
                            bufferedSessionInfo.SessionId,
                            sessionId,
                            StringComparison.Ordinal))
                    {
                        _sessionActivationQuarantined = true;
                        throw new LilithSessionActivationEventException(
                            "Hermes session.info did not match the canonical session activation receipt.");
                    }
                    if (_activeSessionId is not null
                        && _sessionImages.TryGetValue(
                            _activeSessionId,
                            out var activeLifecycle)
                        && activeLifecycle.State != SessionImageLifecycleState.Idle)
                    {
                        throw new InvalidDataException(
                            "The active image lifecycle changed during the session switch.");
                    }
                    if (_sessionImages.TryGetValue(sessionId, out var targetLifecycle)
                        && (targetLifecycle.State != SessionImageLifecycleState.Idle
                            || targetLifecycle.Attachment is not null))
                    {
                        throw new InvalidDataException(
                            "The resumed session has unresolved image ownership.");
                    }

                    // Build every fallible correlation and session-info candidate
                    // before changing the active-session authority.
                    var turnCorrelation = new SessionTurnCorrelation();
                    ObserveResultTurnClaimsLocked(result, turnCorrelation);
                    if (result.TryGetProperty("info", out var candidateInfo))
                    {
                        if (HermesNativeSessionInfoSurface.Inspect(candidateInfo)
                                .Disposition
                            == HermesNativeSessionInfoDisposition.Invalid)
                        {
                            throw new InvalidDataException(
                                "Hermes session activation info was malformed.");
                        }
                    }
                    if (bufferedSessionInfo is not null)
                    {
                        if (HermesNativeSessionInfoSurface.Inspect(
                                bufferedSessionInfo.Payload).Disposition
                            == HermesNativeSessionInfoDisposition.Invalid)
                        {
                            throw new InvalidDataException(
                                "Hermes buffered session.info was malformed.");
                        }
                    }

                    if (_pendingSessionActivation.ReadyVoiceOwner is { } readyVoiceOwner
                        && (_voiceState != VoiceLifecycleState.Ready
                            || !_voiceCaptureArbiter.TryRebindReady(readyVoiceOwner, sessionId)))
                    {
                        // The native switch succeeded, but its original idle
                        // microphone owner was lost. Preserve the other owner
                        // and isolate this uncertain activation; never steal it.
                        QuarantineVoiceUnsafe();
                        throw new InvalidDataException(
                            "The idle voice owner changed during session activation.");
                    }

                    if (_activeSessionId is not null
                        && !string.Equals(
                            _activeSessionId,
                            sessionId,
                            StringComparison.Ordinal))
                    {
                        _sessionImages.Remove(_activeSessionId);
                        _sessionModelObservations.Remove(_activeSessionId);
                        _sessionNativeConfigRevisions.Remove(_activeSessionId);
                    }
                    _pendingApprovals.Clear();
                    _activeSessionId = sessionId;
                    _activeStoredSessionId = storedSessionId;
                    if (_sessionSurface == LilithRuntimeSessionSurface.Workbench
                        && storedSessionId is not null)
                        _workbenchStoredSessionIds[sessionId] = storedSessionId;
                    _activeGameLineageAttestation = gameLineageAttestation;
                    _sessionNativeConfigRevisions[sessionId] =
                        activationNativeConfigRevision;
                    _lastAllowedSessionInfo = null;
                    _sessionInfoProjectionDirty = false;
                    InvalidateCheckpointCatalogUnsafe();
                    _checkpointWorkspaceStates[sessionId] =
                        CheckpointWorkspaceAttestationState.Pending;
                    if (_voiceState == VoiceLifecycleState.Ready)
                        _voiceSessionId = sessionId;
                    _sessionImages[sessionId] = new SessionImageLifecycle();
                    _sessionTurns[sessionId] = turnCorrelation;
                    _sessionStates.TryAdd(
                        sessionId,
                        SessionNativeSurfaceState.Pending);
                    if (result.TryGetProperty("info", out var info))
                    {
                        ApplyAuthoritativeSessionInfo(
                            sessionId,
                            info,
                            activationNativeConfigRevision,
                            refreshesNativeConfigBinding: true);
                    }
                    if (bufferedSessionInfo is not null
                        && (!result.TryGetProperty("info", out var resultInfo)
                            || !SameJson(
                                resultInfo,
                                bufferedSessionInfo.Payload)))
                    {
                        ApplyAuthoritativeSessionInfo(
                            sessionId,
                            bufferedSessionInfo.Payload,
                            activationNativeConfigRevision,
                            refreshesNativeConfigBinding: true);
                    }
                    _pendingSessionActivation.BoundSessionId = sessionId;
                    _pendingSessionActivation.ProjectionPending = true;
                }
            }
            catch (LilithSessionActivationEventException)
            {
                throw;
            }
            catch (LilithGameSessionLineageRejectedException)
            {
                throw;
            }
            catch (InvalidDataException exception)
            {
                lock (_sessionLock)
                    _sessionActivationQuarantined = true;
                throw new LilithSessionActivationEventException(
                    "Hermes returned a malformed session activation receipt: "
                    + exception.Message);
            }
            return;
        }

        if (method == "session.history")
        {
            var sessionId = OptionalSessionId(parameters)
                ?? throw new InvalidDataException("session.history omitted its session identity.");
            lock (_sessionLock)
            {
                var turn = GetOrCreateTurnCorrelationLocked(sessionId);
                ObserveResultTurnClaimsLocked(result, turn);
            }
            return;
        }

        if (method == "session.interrupt")
        {
            var sessionId = OptionalSessionId(parameters);
            if (sessionId is not null)
            {
                lock (_sessionLock)
                    _pendingApprovals.Remove(sessionId);
            }
        }
    }

    private void CompleteSessionClose(string sessionId)
    {
        lock (_sessionLock)
        {
            _sessionStates.Remove(sessionId);
            _workbenchStoredSessionIds.Remove(sessionId);
            _checkpointWorkspaceStates.Remove(sessionId);
            _pendingApprovals.Remove(sessionId);
            _sessionImages.Remove(sessionId);
            _sessionTurns.Remove(sessionId);
            _sessionModelObservations.Remove(sessionId);
            if (_pendingCredentialModelRecovery?.SessionId == sessionId)
                _pendingCredentialModelRecovery = null;
            if (string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                _activeSessionId = null;
                _activeStoredSessionId = null;
                _activeGameLineageAttestation = null;
                _lastAllowedSessionInfo = null;
                _sessionInfoProjectionDirty = false;
                InvalidateCheckpointCatalogUnsafe();
                _voiceSessionId = null;
                _voiceState = VoiceLifecycleState.Off;
                _voiceRequestInFlight = false;
                _voiceDisableAwaitingIdle = false;
                _voiceCleanupAttempted = false;
                _voiceQuarantineRequiresDrain = false;
            }
        }
    }

    private SessionTurnCorrelation GetOrCreateTurnCorrelationLocked(string sessionId)
    {
        if (!_sessionTurns.TryGetValue(sessionId, out var turn))
        {
            turn = new SessionTurnCorrelation();
            _sessionTurns.Add(sessionId, turn);
        }
        return turn;
    }

    private static void ObserveResultTurnClaimsLocked(
        JsonElement result,
        SessionTurnCorrelation turn)
    {
        var knownTurnIds = new HashSet<string>(
            turn.KnownTurnIds,
            StringComparer.Ordinal);
        var claimedUserMessageIds = new Dictionary<string, string>(
            turn.ClaimedUserMessageIds,
            StringComparer.Ordinal);
        var claimedAssistantMessageIds = new Dictionary<string, string>(
            turn.ClaimedAssistantMessageIds,
            StringComparer.Ordinal);
        var claimedAuxiliaryAssistantMessageIds =
            new HashSet<(string TurnId, string MessageId)>(
                turn.ClaimedAuxiliaryAssistantMessageIds);
        var claimedAuxiliaryAssistantClaims =
            new Dictionary<
                (string TurnId, string MessageId),
                AuxiliaryAssistantClaim>(
                turn.ClaimedAuxiliaryAssistantClaims);
        var completedTurnIds = new HashSet<string>(
            turn.CompletedTurnIds,
            StringComparer.Ordinal);
        var activeAssistantMessageIds = new Dictionary<string, string>(
            turn.ActiveAssistantMessageIds,
            StringComparer.Ordinal);
        var claimedInterimAssistantMessageIds =
            new HashSet<(string TurnId, string MessageId)>(
                turn.ClaimedInterimAssistantMessageIds);
        var lastInterimAssistantMessageIds = new Dictionary<string, string>(
            turn.LastInterimAssistantMessageIds,
            StringComparer.Ordinal);
        var terminalErrorMarkerTurnIds = new HashSet<string>(
            turn.TerminalErrorMarkerTurnIds,
            StringComparer.Ordinal);
        var terminalErrorMarkerMessageIds = new Dictionary<string, string>(
            turn.TerminalErrorMarkerMessageIds,
            StringComparer.Ordinal);
        var awaitingQueuedTurn = turn.AwaitingQueuedTurn;
        var observedAssistantMessageIds = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        foreach (var claim in claimedAssistantMessageIds)
        {
            observedAssistantMessageIds[claim.Key] = new HashSet<string>(
                new[] { claim.Value },
                StringComparer.Ordinal);
        }
        var historyUserTurns = new HashSet<string>(StringComparer.Ordinal);
        var historyPrimaryAssistantTurns = new HashSet<string>(StringComparer.Ordinal);
        var historyAuxiliaryAssistantClaims =
            new HashSet<(string TurnId, string MessageId)>();
        var historyTerminalErrorMarkerTurns =
            new HashSet<string>(StringComparer.Ordinal);
        var inflightTurnIdClaim = string.Empty;

        if (result.TryGetProperty("queued", out var queued)
            && queued.ValueKind != JsonValueKind.Null)
        {
            if (queued.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Hermes queued prompt snapshot was malformed.");
            }
            var queuedProperties = queued.EnumerateObject().ToArray();
            if (queuedProperties.Length != 1
                || !queued.TryGetProperty("user", out var queuedUser)
                || queuedUser.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(queuedUser.GetString()))
            {
                throw new InvalidDataException(
                    "Hermes queued prompt snapshot was not exact.");
            }
            awaitingQueuedTurn = true;
        }

        void ObserveAssistantMessage(string turnId, string messageId)
        {
            if (claimedUserMessageIds.TryGetValue(turnId, out var userMessageId)
                && string.Equals(userMessageId, messageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes history assistant identity was aliased to the user.");
            }
            if (!observedAssistantMessageIds.TryGetValue(turnId, out var identities))
            {
                identities = new HashSet<string>(StringComparer.Ordinal);
                observedAssistantMessageIds.Add(turnId, identities);
            }
            identities.Add(messageId);
        }

        void ClaimUserMessage(string turnId, string messageId)
        {
            if (claimedAuxiliaryAssistantMessageIds.Contains((turnId, messageId))
                || (observedAssistantMessageIds.TryGetValue(
                        turnId,
                        out var assistantIds)
                && assistantIds.Contains(messageId))
                )
            {
                throw new InvalidDataException(
                    "Hermes history user and primary assistant identities were aliased.");
            }
            if (claimedUserMessageIds.TryGetValue(turnId, out var existingMessageId)
                && !string.Equals(existingMessageId, messageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes history changed its user-message identity.");
            }
            claimedUserMessageIds[turnId] = messageId;
        }

        void ClaimPrimaryAssistantMessage(string turnId, string messageId)
        {
            ObserveAssistantMessage(turnId, messageId);
            if (claimedAuxiliaryAssistantMessageIds.Contains((turnId, messageId)))
            {
                throw new InvalidDataException(
                    "Hermes history primary assistant reused an auxiliary identity.");
            }
            if (claimedAssistantMessageIds.TryGetValue(
                    turnId,
                    out var existingMessageId)
                && !string.Equals(existingMessageId, messageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes history changed its primary assistant identity.");
            }
            claimedAssistantMessageIds[turnId] = messageId;
        }

        var hasInflight = result.TryGetProperty("inflight", out var inflight)
            && inflight.ValueKind != JsonValueKind.Null;
        var hasTerminalError = result.TryGetProperty(
                "terminal_error",
                out var terminalError)
            && terminalError.ValueKind != JsonValueKind.Null;
        if (hasInflight && hasTerminalError)
        {
            throw new InvalidDataException(
                "Hermes result carried both inflight and terminal-error state.");
        }

        if (hasInflight)
        {
            var inflightValues = ReadExactObject(inflight, new[]
            {
                "assistant", "streaming", "turn_id", "user_message_id",
                "assistant_message_id", "user", "corrections", "error",
                "status", "recoverable"
            });
            var turnId = HermesTurnCorrelationContract.RequireIdentity(
                RequiredGatewayMessageText(
                    inflightValues,
                    "turn_id",
                    allowEmpty: false),
                "inflight turn_id");
            var messageId = HermesTurnCorrelationContract.RequireIdentity(
                RequiredGatewayMessageText(
                    inflightValues,
                    "user_message_id",
                    allowEmpty: false),
                "inflight user_message_id");
            var assistantMessageId = HermesTurnCorrelationContract.RequireIdentity(
                RequiredGatewayMessageText(
                    inflightValues,
                    "assistant_message_id",
                    allowEmpty: false),
                "inflight assistant_message_id");
            if (new HashSet<string>(StringComparer.Ordinal)
                {
                    turnId,
                    messageId,
                    assistantMessageId
                }.Count != 3)
            {
                throw new InvalidDataException(
                    "Hermes inflight identities were not distinct.");
            }
            var user = RequiredGatewayMessageText(
                inflightValues,
                "user",
                allowEmpty: true);
            var assistant = RequiredGatewayMessageText(
                inflightValues,
                "assistant",
                allowEmpty: true);
            var streaming = RequireGatewayBoolean(inflightValues, "streaming");
            if (user.Length == 0 && assistant.Length == 0 && !streaming)
            {
                throw new InvalidDataException(
                    "Hermes inflight turn was empty.");
            }
            if (inflightValues.TryGetValue("corrections", out var corrections)
                && (corrections.ValueKind != JsonValueKind.Array
                    || corrections.EnumerateArray().Any(value =>
                        value.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(value.GetString()))))
            {
                throw new InvalidDataException(
                    "Hermes inflight corrections were malformed.");
            }
            var inflightHasError = inflightValues.ContainsKey("error");
            if (inflightHasError)
            {
                _ = RequiredGatewayMessageText(
                    inflightValues,
                    "error",
                    allowEmpty: false);
                if (!inflightValues.TryGetValue("status", out var errorStatus)
                    || errorStatus.ValueKind != JsonValueKind.String
                    || !string.Equals(
                        errorStatus.GetString(),
                        "error",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes inflight error status was malformed.");
                }
                _ = RequireGatewayBoolean(inflightValues, "recoverable");
            }
            else if (inflightValues.ContainsKey("status")
                || inflightValues.ContainsKey("recoverable"))
            {
                throw new InvalidDataException(
                    "Hermes inflight turn carried orphaned error state.");
            }
            knownTurnIds.Add(turnId);
            ClaimUserMessage(turnId, messageId);
            ClaimPrimaryAssistantMessage(turnId, assistantMessageId);
            if (inflightHasError)
            {
                activeAssistantMessageIds.Remove(turnId);
                completedTurnIds.Add(turnId);
            }
            else
            {
                if (streaming)
                    activeAssistantMessageIds[turnId] = assistantMessageId;
                inflightTurnIdClaim = turnId;
            }
        }
        else if (hasTerminalError)
        {
            var terminalValues = ReadExactObject(terminalError, new[]
            {
                "error", "recoverable", "status", "turn_id"
            });
            RequireExactGatewayProperties(
                terminalValues,
                "error",
                "recoverable",
                "status",
                "turn_id");
            var turnId = HermesTurnCorrelationContract.RequireIdentity(
                RequiredGatewayMessageText(
                    terminalValues,
                    "turn_id",
                    allowEmpty: false),
                "terminal_error turn_id");
            _ = RequiredGatewayMessageText(
                terminalValues,
                "error",
                allowEmpty: false);
            _ = RequireGatewayBoolean(terminalValues, "recoverable");
            if (!string.Equals(
                    RequiredGatewayMessageText(
                        terminalValues,
                        "status",
                        allowEmpty: false),
                    "error",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes terminal_error status was malformed.");
            }
            knownTurnIds.Add(turnId);
            activeAssistantMessageIds.Remove(turnId);
            completedTurnIds.Add(turnId);
        }

        if (result.TryGetProperty("messages", out var messages))
        {
            if (messages.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Hermes history messages were malformed.");
            foreach (var item in messages.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Hermes history message was malformed.");
                var hasTurnId = item.TryGetProperty("turn_id", out var itemTurnId);
                var hasMessageId = item.TryGetProperty("message_id", out var itemMessageId);
                if (!hasTurnId && !hasMessageId)
                {
                    if (item.TryGetProperty("message_kind", out _)
                        || item.TryGetProperty("display_metadata", out var unscopedMetadata)
                        && unscopedMetadata.ValueKind == JsonValueKind.Object
                        && unscopedMetadata.TryGetProperty(
                            "terminal_error_turn_id",
                            out _))
                    {
                        throw new InvalidDataException(
                            "Hermes history metadata lacked canonical correlation.");
                    }
                    continue;
                }
                if (!hasTurnId || !hasMessageId
                    || itemTurnId.ValueKind != JsonValueKind.String
                    || itemMessageId.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Hermes history correlation was partially missing.");
                }
                var turnId = HermesTurnCorrelationContract.RequireIdentity(
                    itemTurnId.GetString(),
                    "history turn_id");
                var messageId = HermesTurnCorrelationContract.RequireIdentity(
                    itemMessageId.GetString(),
                    "history message_id");
                var nativeAssistantEvent =
                    IsExactNativeAssistantHistoryCorrelation(
                        item,
                        turnId,
                        messageId);
                if (string.Equals(turnId, messageId, StringComparison.Ordinal)
                    && !nativeAssistantEvent)
                {
                    throw new InvalidDataException("Hermes history identities were not distinct.");
                }
                if (!item.TryGetProperty("role", out var roleValue)
                    || roleValue.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Hermes correlated history role was malformed.");
                }
                var role = roleValue.GetString();
                if (role is not ("user" or "assistant" or "tool" or "system"))
                    throw new InvalidDataException("Hermes correlated history role was unknown.");

                var hasMessageKind = item.TryGetProperty(
                    "message_kind",
                    out var messageKind);
                if (hasMessageKind && messageKind.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Hermes correlated history message_kind was malformed.");
                }
                var decodedMessageKind = hasMessageKind
                    ? messageKind.GetString() ?? string.Empty
                    : string.Empty;
                if (decodedMessageKind.Length != 0
                    && decodedMessageKind is not ("interim" or "primary" or "tool"
                        or "tool_call" or "user"))
                {
                    throw new InvalidDataException(
                        "Hermes correlated history message_kind was unknown.");
                }
                if ((role == "user"
                        && decodedMessageKind.Length != 0
                        && decodedMessageKind != "user")
                    || (role == "assistant"
                        && decodedMessageKind.Length != 0
                        && decodedMessageKind is not (
                            "primary" or "interim" or "tool_call"))
                    || (role == "tool"
                        && decodedMessageKind.Length != 0
                        && decodedMessageKind != "tool")
                    || (role == "system" && decodedMessageKind.Length != 0))
                {
                    throw new InvalidDataException(
                        "Hermes history role disagreed with message_kind.");
                }

                var terminalErrorMarker = false;
                if (item.TryGetProperty("display_metadata", out var metadata)
                    && metadata.ValueKind != JsonValueKind.Null)
                {
                    if (metadata.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException(
                            "Hermes history display_metadata was malformed.");
                    }
                    if (metadata.TryGetProperty(
                            "terminal_error_turn_id",
                            out var marker))
                    {
                        var markedTurnId = HermesTurnCorrelationContract.RequireIdentity(
                            marker.ValueKind == JsonValueKind.String
                                ? marker.GetString()
                                : null,
                            "terminal_error_turn_id");
                        if (role != "assistant"
                            || !string.Equals(
                                markedTurnId,
                                turnId,
                                StringComparison.Ordinal)
                            || decodedMessageKind.Length != 0
                                && decodedMessageKind != "primary")
                        {
                            throw new InvalidDataException(
                                "Hermes terminal-error marker was not bound to its primary assistant row.");
                        }
                        if (!historyTerminalErrorMarkerTurns.Add(turnId))
                        {
                            throw new InvalidDataException(
                                "Hermes history duplicated a terminal-error marker.");
                        }
                        if (terminalErrorMarkerMessageIds.TryGetValue(
                                turnId,
                                out var existingMarkerMessageId)
                            && !string.Equals(
                                existingMarkerMessageId,
                                messageId,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "Hermes history changed its terminal-error marker identity.");
                        }
                        terminalErrorMarkerMessageIds[turnId] = messageId;
                        terminalErrorMarkerTurnIds.Add(turnId);
                        completedTurnIds.Add(turnId);
                        terminalErrorMarker = true;
                    }
                }

                knownTurnIds.Add(turnId);
                if (string.Equals(role, "user", StringComparison.Ordinal))
                {
                    if (!historyUserTurns.Add(turnId))
                    {
                        throw new InvalidDataException(
                            "Hermes history contained multiple user rows for one turn.");
                    }
                    ClaimUserMessage(turnId, messageId);
                    continue;
                }
                if (!string.Equals(role, "assistant", StringComparison.Ordinal))
                    continue;
                var assistantText = item.TryGetProperty("text", out var historyText)
                    ? ValidateGatewayMessageTextValue(
                        historyText,
                        "history text",
                        allowEmpty: true)
                    : throw new InvalidDataException(
                        "Hermes assistant history omitted text.");
                var isPrimary = terminalErrorMarker
                    || string.Equals(
                        decodedMessageKind,
                        "primary",
                        StringComparison.Ordinal);
                if (isPrimary)
                {
                    if (!historyPrimaryAssistantTurns.Add(turnId))
                    {
                        throw new InvalidDataException(
                            "Hermes history contained multiple primary assistant rows for one turn.");
                    }
                    ClaimPrimaryAssistantMessage(turnId, messageId);
                    activeAssistantMessageIds.Remove(turnId);
                    completedTurnIds.Add(turnId);
                }
                else
                {
                    ObserveAssistantMessage(turnId, messageId);
                    if (claimedAssistantMessageIds.TryGetValue(
                            turnId,
                            out var primaryMessageId)
                        && string.Equals(
                            primaryMessageId,
                            messageId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Hermes history non-primary assistant reused the primary identity.");
                    }
                    var auxiliaryIdentity = (turnId, messageId);
                    if (!historyAuxiliaryAssistantClaims.Add(auxiliaryIdentity))
                    {
                        throw new InvalidDataException(
                            "Hermes history duplicated an auxiliary assistant row.");
                    }
                    var descriptor = new AuxiliaryAssistantClaim(
                        decodedMessageKind,
                        assistantText);
                    if (claimedAuxiliaryAssistantClaims.TryGetValue(
                            auxiliaryIdentity,
                            out var existingDescriptor)
                        && existingDescriptor != descriptor
                        && !(string.Equals(
                                existingDescriptor.MessageKind,
                                "interim",
                                StringComparison.Ordinal)
                            && string.Equals(
                                descriptor.MessageKind,
                                "tool_call",
                                StringComparison.Ordinal)
                            && string.Equals(
                                existingDescriptor.Text,
                                descriptor.Text,
                                StringComparison.Ordinal)))
                    {
                        throw new InvalidDataException(
                            "Hermes history changed an auxiliary assistant row.");
                    }
                    // Hermes persists a tool-call row before projecting its
                    // visible narration as message.interim. That live frame
                    // can therefore claim the durable row's exact identity
                    // and text before session.history restores its canonical
                    // tool_call kind. Permit only that one-way kind upgrade;
                    // identity and text drift remain fail-closed above.
                    claimedAuxiliaryAssistantMessageIds.Add(auxiliaryIdentity);
                    claimedAuxiliaryAssistantClaims[auxiliaryIdentity] = descriptor;
                    if (decodedMessageKind == "interim")
                    {
                        claimedInterimAssistantMessageIds.Add(auxiliaryIdentity);
                        lastInterimAssistantMessageIds[turnId] = messageId;
                    }
                }
            }
        }

        if (inflightTurnIdClaim.Length != 0
            && completedTurnIds.Contains(inflightTurnIdClaim))
        {
            throw new InvalidDataException(
                "Hermes inflight turn was also projected as completed history.");
        }

        turn.KnownTurnIds.Clear();
        turn.KnownTurnIds.UnionWith(knownTurnIds);
        turn.ClaimedUserMessageIds.Clear();
        foreach (var claim in claimedUserMessageIds)
            turn.ClaimedUserMessageIds.Add(claim.Key, claim.Value);
        turn.ClaimedAssistantMessageIds.Clear();
        foreach (var claim in claimedAssistantMessageIds)
            turn.ClaimedAssistantMessageIds.Add(claim.Key, claim.Value);
        turn.ClaimedAuxiliaryAssistantMessageIds.Clear();
        turn.ClaimedAuxiliaryAssistantMessageIds.UnionWith(
            claimedAuxiliaryAssistantMessageIds);
        turn.ClaimedAuxiliaryAssistantClaims.Clear();
        foreach (var claim in claimedAuxiliaryAssistantClaims)
            turn.ClaimedAuxiliaryAssistantClaims.Add(claim.Key, claim.Value);
        turn.CompletedTurnIds.Clear();
        turn.CompletedTurnIds.UnionWith(completedTurnIds);
        turn.ActiveAssistantMessageIds.Clear();
        foreach (var claim in activeAssistantMessageIds)
            turn.ActiveAssistantMessageIds.Add(claim.Key, claim.Value);
        turn.ClaimedInterimAssistantMessageIds.Clear();
        turn.ClaimedInterimAssistantMessageIds.UnionWith(
            claimedInterimAssistantMessageIds);
        turn.LastInterimAssistantMessageIds.Clear();
        foreach (var claim in lastInterimAssistantMessageIds)
            turn.LastInterimAssistantMessageIds.Add(claim.Key, claim.Value);
        turn.TerminalErrorMarkerTurnIds.Clear();
        turn.TerminalErrorMarkerTurnIds.UnionWith(terminalErrorMarkerTurnIds);
        turn.TerminalErrorMarkerMessageIds.Clear();
        foreach (var claim in terminalErrorMarkerMessageIds)
            turn.TerminalErrorMarkerMessageIds.Add(claim.Key, claim.Value);
        turn.AwaitingQueuedTurn = awaitingQueuedTurn;
    }

    private static bool IsExactNativeAssistantHistoryCorrelation(
        JsonElement message,
        string turnId,
        string messageId)
        => string.Equals(turnId, messageId, StringComparison.Ordinal)
            && message.TryGetProperty("display_kind", out var displayKind)
            && displayKind.ValueKind == JsonValueKind.String
            && string.Equals(
                displayKind.GetString(),
                "native_assistant_event",
                StringComparison.Ordinal)
            && message.TryGetProperty("role", out var role)
            && role.ValueKind == JsonValueKind.String
            && string.Equals(role.GetString(), "assistant", StringComparison.Ordinal)
            && message.TryGetProperty("message_kind", out var messageKind)
            && messageKind.ValueKind == JsonValueKind.String
            && string.Equals(
                messageKind.GetString(),
                "primary",
                StringComparison.Ordinal)
            && message.TryGetProperty("display_metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty(
                "lilith_native_event_id",
                out var nativeEventId)
            && nativeEventId.ValueKind == JsonValueKind.String
            && string.Equals(
                nativeEventId.GetString(),
                messageId,
                StringComparison.Ordinal)
            && metadata.TryGetProperty(
                "lilith_native_source",
                out var nativeSource)
            && nativeSource.ValueKind == JsonValueKind.String
            && string.Equals(
                nativeSource.GetString(),
                "lilith-native",
                StringComparison.Ordinal);

    private void QuarantineSessionDelete()
    {
        lock (_sessionLock)
            _sessionDeleteQuarantined = true;
    }

    private async Task<LilithRuntimeResponse> RouteNativeToolsCatalogAsync(
        CancellationToken cancellationToken)
    {
        var result = await _requestAsync(
            "tools.catalog",
            Json("{}"),
            cancellationToken).ConfigureAwait(false);
        _ = HermesNativeToolsContract.DecodeCatalog(result);
        return Success("tools.catalog", result);
    }

    private async Task<LilithRuntimeResponse> RouteNativeModelDefaultSetAsync(
        JsonElement parameters,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        if (_profile is null)
        {
            return Failure(
                "model.default.set",
                "runtime.native_config_receipt_unavailable",
                "Runtime has no strict profile receipt for native model rotation.");
        }
        var providerId = parameters.GetProperty("provider").GetString()!;
        var modelId = parameters.GetProperty("model").GetString()!;
        var confirmExpensiveModel = parameters
            .GetProperty("confirm_expensive_model")
            .GetBoolean();

        if (modelAuthority.Snapshot.GenerationId != _generationId
            || modelAuthority.Snapshot.State
                is LilithRuntimeModelAuthorityState.Unavailable
                    or LilithRuntimeModelAuthorityState.Retired)
        {
            return Failure(
                "model.default.set",
                "runtime.native_surface_attestation_required",
                "The Runtime model authority does not belong to this ready generation.");
        }
        if (modelAuthority.Snapshot.State
            == LilithRuntimeModelAuthorityState.Quarantined)
        {
            throw new LilithNativeConfigMutationUncertainException();
        }

        var phase = LilithNativeModelMutationPhase.CapturePrevious;
        void QuarantineModelFailure(Exception? exception = null)
        {
            _modelAuthority.RecordNativeModelFailure(modelAuthority, _generationId,
                LilithNativeModelMutationDiagnostic.Create(phase, exception));
            QuarantineNativeConfig(modelAuthority);
        }

        await _nativeConfigGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await LilithNativeConfigMutationReceiptStore
                .CaptureCurrentAsync(
                    _hermesHomePath,
                    _profile.CanonicalLearningConfigSha256,
                    _profile.IdentityFingerprintSha256,
                    _profile.SoulSha256,
                    _profile.HermesPersonalitySha256,
                    cancellationToken).ConfigureAwait(false);
            // Re-read the configured native options immediately before the
            // mutation. The startup catalog is the maximum approved bundle;
            // this projection is its current authenticated intersection.
            phase = LilithNativeModelMutationPhase.ReadOptions;
            var currentOptions = await _requestAsync(
                "model.options",
                Json("{\"explicit_only\":true,\"include_unconfigured\":false,\"refresh\":false}"),
                cancellationToken).ConfigureAwait(false);
            if (!ModelOptionsHasValidAvailabilitySubset(
                    currentOptions,
                    providerId))
            {
                return Failure(
                    "model.default.set",
                    "runtime.model_default_selection_rejected",
                    "The selected provider returned a malformed native model availability set.");
            }
            var currentIntersection = IntersectModelOptions(currentOptions);
            if (!ModelOptionsContainsExactPair(
                    currentIntersection,
                    providerId,
                    modelId))
            {
                return Failure(
                    "model.default.set",
                    "runtime.model_default_selection_rejected",
                    "The selected provider and model are not in the current approved native model options.");
            }

            phase = LilithNativeModelMutationPhase.BeforeDispatch;
            var dispatchConfigSha256 = await LilithNativeConfigMutationReceiptStore
                .ReadConfigSha256Async(
                    _hermesHomePath,
                    cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    dispatchConfigSha256,
                    previous.ConfigSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes native config changed before model.default.set dispatch; no mutation was sent.");
            }

            var currentCatalog = HermesNativeStartupAttestation.InspectModelCatalog(currentOptions).ApprovedModelCatalog;
            if (currentCatalog.HasDefault
                && string.Equals(currentCatalog.DefaultProviderId, providerId, StringComparison.Ordinal)
                && string.Equals(currentCatalog.DefaultModelId, modelId, StringComparison.Ordinal))
            {
                // Opening the picker and saving its current value is a read-only
                // no-op. Do not invoke alias resolution or manufacture a commit.
                return Failure("model.default.set", "runtime.model_default_unchanged",
                    "The selected model is already the current default; no setting was changed.");
            }

            JsonElement receipt;
            try
            {
                phase = LilithNativeModelMutationPhase.ConfigSet;
                receipt = await _requestAsync(
                    "config.set",
                    JsonSerializer.SerializeToElement(new
                    {
                        key = "model",
                        value = modelId
                            + " --provider "
                            + providerId
                            + " --global",
                        confirm_expensive_model = confirmExpensiveModel
                    }),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HermesRequestNotSentException)
            {
                throw;
            }
            catch (HermesRpcException exception) when (exception.Code == 4090)
            {
                // Hermes rejected this before native config persistence because
                // a live turn, tool, build, setter, or child still owns the
                // transition boundary.  The outcome is known and retryable;
                // never quarantine healthy native-config authority for it.
                return Failure(
                    "model.default.set",
                    "runtime.native_config_transition_busy",
                    exception.Message,
                    exception.Code);
            }
            catch (HermesRpcException exception) when (exception.Code is 4094 or 4096)
            {
                // The pinned native handler rejects pair substitution before
                // model persistence. Independently verify the old config and
                // receipt before treating it as a harmless selection rejection.
                try
                {
                    var unchanged = await LilithNativeConfigMutationReceiptStore
                        .CaptureCurrentAsync(
                            _hermesHomePath,
                            _profile.CanonicalLearningConfigSha256,
                            _profile.IdentityFingerprintSha256,
                            _profile.SoulSha256,
                            _profile.HermesPersonalitySha256,
                            CancellationToken.None).ConfigureAwait(false);
                    if (unchanged.Sequence != previous.Sequence
                        || unchanged.ConfigSha256 != previous.ConfigSha256
                        || unchanged.PreviousConfigSha256 != previous.PreviousConfigSha256
                        || unchanged.CanonicalBaseSha256 != previous.CanonicalBaseSha256
                        || unchanged.Source != previous.Source
                        || unchanged.ApprovalMode != previous.ApprovalMode
                        || unchanged.OperationNonce != previous.OperationNonce
                        || !unchanged.EnabledToolsets.SequenceEqual(previous.EnabledToolsets))
                        throw new InvalidDataException("Model rejection changed config authority.");
                }
                catch (Exception verificationException)
                {
                    QuarantineModelFailure(verificationException);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                return Failure(
                    "model.default.set",
                    "runtime.model_default_selection_rejected",
                    exception.Code == 4096
                        ? "The provider could not resolve the model. No setting was saved."
                        : "The model resolved to a different selection. No setting was saved; refresh the model list.",
                    exception.Code);
            }
            catch (HermesRpcException exception) when (exception.Code is 5001 or 5035)
            {
                // Hermes uses 5001 for the whole model-switch transaction,
                // including failures that may occur after config persistence.
                // Without an exact receipt and post-read, the global outcome
                // is not safe to classify as a rejection.
                QuarantineModelFailure(exception);
                throw new LilithNativeConfigMutationUncertainException();
            }
            catch (HermesRpcException)
            {
                throw;
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                QuarantineModelFailure(exception);
                throw;
            }
            catch (Exception exception)
            {
                QuarantineModelFailure(exception);
                throw new LilithNativeConfigMutationUncertainException();
            }

            phase = LilithNativeModelMutationPhase.VerifyNativeReceipt;
            if (IsExactNativeModelConfirmationReceipt(receipt, modelId))
            {
                var confirmationConfigSha256 =
                    await LilithNativeConfigMutationReceiptStore
                        .ReadConfigSha256Async(
                            _hermesHomePath,
                            CancellationToken.None).ConfigureAwait(false);
                if (!string.Equals(
                        confirmationConfigSha256,
                        previous.ConfigSha256,
                        StringComparison.Ordinal))
                {
                    QuarantineModelFailure();
                    throw new LilithNativeConfigMutationUncertainException();
                }
                return Success("model.default.set", receipt);
            }
            if (!LilithRuntimeService.IsExactNativeGlobalModelReceipt(
                    receipt,
                    modelId))
            {
                QuarantineModelFailure();
                throw new LilithNativeConfigMutationUncertainException();
            }

            try
            {
                phase = LilithNativeModelMutationPhase.ReadBack;
                var verifiedOptions = await _requestAsync(
                    "model.options",
                    Json("{\"explicit_only\":true,\"include_unconfigured\":false,\"refresh\":false}"),
                    cancellationToken).ConfigureAwait(false);
                if (!ModelOptionsHasValidAvailabilitySubset(
                        verifiedOptions,
                        providerId)
                    || !ModelOptionsContainsExactPair(
                        IntersectModelOptions(verifiedOptions),
                        providerId,
                        modelId))
                {
                    QuarantineModelFailure();
                    throw new LilithNativeConfigMutationUncertainException();
                }
                var verifiedCatalog = HermesNativeStartupAttestation
                    .InspectModelCatalog(verifiedOptions)
                    .ApprovedModelCatalog;
                if (!verifiedCatalog.HasDefault
                    || !string.Equals(
                        verifiedCatalog.DefaultProviderId,
                        providerId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        verifiedCatalog.DefaultModelId,
                        modelId,
                        StringComparison.Ordinal))
                {
                    QuarantineModelFailure();
                    throw new LilithNativeConfigMutationUncertainException();
                }
                var committedConfigSha256 =
                    await LilithNativeConfigMutationReceiptStore
                        .ReadConfigSha256Async(
                            _hermesHomePath,
                            CancellationToken.None).ConfigureAwait(false);
                phase = LilithNativeModelMutationPhase.WriteReceipt;
                var durableCommit = await LilithNativeConfigMutationReceiptStore
                    .CommitModelDefaultAsync(
                        _hermesHomePath,
                        previous,
                        committedConfigSha256,
                        CancellationToken.None).ConfigureAwait(false);
                JsonElement commitResult;
                try
                {
                    phase = LilithNativeModelMutationPhase.CommitAcknowledgement;
                    commitResult = await _requestAsync(
                        "model.default.commit",
                        JsonSerializer.SerializeToElement(new
                        {
                            authority = HermesNativeToolsContract
                                .NativeConfigCommitAuthority,
                            previous_config_sha256 = previous.ConfigSha256,
                            config_sha256 = committedConfigSha256,
                            receipt_sha256 = durableCommit.ReceiptSha256
                        }),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Phase one already changed config.yaml and Runtime has
                    // durably committed the matching receipt. Even a
                    // definitely-not-sent phase-two result cannot be retried
                    // or treated as the pre-mutation authority.
                    QuarantineModelFailure(exception);
                    throw new LilithNativeConfigMutationUncertainException();
                }
                if (!IsExactNativeModelCommitResult(commitResult))
                {
                    QuarantineModelFailure();
                    throw new LilithNativeConfigMutationUncertainException();
                }
                phase = LilithNativeModelMutationPhase.PublishAuthority;
                if (!_applyNativeModelDefault(
                        modelAuthority,
                        _generationId,
                        verifiedCatalog,
                        providerId,
                        modelId))
                {
                    QuarantineModelFailure();
                    throw new LilithNativeConfigMutationUncertainException();
                }
                ObserveSharedNativeConfigRevision(
                    _modelAuthority.Snapshot());
            }
            catch (LilithNativeConfigMutationUncertainException)
            {
                throw;
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                QuarantineModelFailure(exception);
                throw;
            }
            catch (Exception exception)
            {
                QuarantineModelFailure(exception);
                throw new LilithNativeConfigMutationUncertainException();
            }

            return Success("model.default.set", receipt);
        }
        finally
        {
            _nativeConfigGate.Release();
        }
    }

    private static bool IsExactNativeModelConfirmationReceipt(
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
            && confirmation.ValueKind == JsonValueKind.True
            && receipt.TryGetProperty("confirm_message", out var message)
            && message.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(message.GetString())
            && receipt.TryGetProperty("scope", out var scope)
            && scope.ValueKind == JsonValueKind.String
            && string.Equals(
                scope.GetString(),
                "global",
                StringComparison.Ordinal)
            && receipt.TryGetProperty("runtime_revision", out var runtimeRevision)
            && runtimeRevision.ValueKind == JsonValueKind.Number
            && runtimeRevision.TryGetInt64(out var revision)
            && revision == 0
            && receipt.TryGetProperty("runtime_ready", out var runtimeReady)
            && runtimeReady.ValueKind == JsonValueKind.True;
    }

    private static bool IsExactNativeModelCommitResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return false;
        var properties = result.EnumerateObject().ToArray();
        return properties.Length == 1
            && string.Equals(
                properties[0].Name,
                "committed",
                StringComparison.Ordinal)
            && properties[0].Value.ValueKind == JsonValueKind.Number
            && properties[0].Value.TryGetInt32(out var committed)
            && committed == 1;
    }

    private static bool ModelOptionsContainsExactPair(
        JsonElement options,
        string expectedProviderId,
        string expectedModelId)
    {
        if (options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty("providers", out var providers)
            || providers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var provider in providers.EnumerateArray())
        {
            if (provider.ValueKind != JsonValueKind.Object
                || !provider.TryGetProperty("slug", out var slug)
                || slug.ValueKind != JsonValueKind.String
                || !string.Equals(
                    slug.GetString(),
                    expectedProviderId,
                    StringComparison.Ordinal)
                || !provider.TryGetProperty("authenticated", out var authenticated)
                || authenticated.ValueKind != JsonValueKind.True
                || !provider.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var modelPresent = models.EnumerateArray().Any(model =>
                model.ValueKind == JsonValueKind.String
                && string.Equals(
                    model.GetString(),
                    expectedModelId,
                    StringComparison.Ordinal));
            if (!modelPresent)
                return false;
            if (!provider.TryGetProperty("unavailable_models", out var unavailable))
                return true;
            if (unavailable.ValueKind != JsonValueKind.Array)
                return false;
            return !unavailable.EnumerateArray().Any(model =>
                model.ValueKind != JsonValueKind.String
                || string.Equals(
                    model.GetString(),
                    expectedModelId,
                    StringComparison.Ordinal));
        }
        return false;
    }

    private static bool ModelOptionsHasValidAvailabilitySubset(
        JsonElement options,
        string expectedProviderId)
    {
        if (options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty("providers", out var providers)
            || providers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var provider in providers.EnumerateArray())
        {
            if (provider.ValueKind != JsonValueKind.Object
                || !provider.TryGetProperty("slug", out var slug)
                || slug.ValueKind != JsonValueKind.String
                || !string.Equals(
                    slug.GetString(),
                    expectedProviderId,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (!provider.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var modelSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.String
                    || model.GetString() is not { } modelId
                    || !modelSet.Add(modelId))
                {
                    return false;
                }
            }
            if (!provider.TryGetProperty(
                    "unavailable_models",
                    out var unavailable))
            {
                return true;
            }
            if (unavailable.ValueKind != JsonValueKind.Array)
                return false;
            var unavailableSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var model in unavailable.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.String
                    || model.GetString() is not { } modelId
                    || !modelSet.Contains(modelId)
                    || !unavailableSet.Add(modelId))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private async Task<LilithRuntimeResponse> RouteNativeToolsSetAsync(
        JsonElement parameters,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority,
        string? expectedConfigSha256 = null)
    {
        if (_profile is null)
        {
            return Failure(
                "tools.set",
                "runtime.native_config_receipt_unavailable",
                "Runtime has no strict profile receipt for native config rotation.");
        }

        var authorityFailure = NativeModelAuthorityMutationFailure(
            "tools.set",
            modelAuthority.Snapshot);
        if (authorityFailure is not null)
            return authorityFailure;

        await _nativeConfigGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = DecodeNativeToolsSetClientPayload(parameters);
            var previous = await LilithNativeConfigMutationReceiptStore
                .CaptureCurrentAsync(
                    _hermesHomePath,
                    _profile.CanonicalLearningConfigSha256,
                    _profile.IdentityFingerprintSha256,
                    _profile.SoulSha256,
                    _profile.HermesPersonalitySha256,
                    cancellationToken).ConfigureAwait(false);
            var dispatchConfigSha256 = await LilithNativeConfigMutationReceiptStore
                .ReadConfigSha256Async(
                    _hermesHomePath,
                    cancellationToken).ConfigureAwait(false);
            if (expectedConfigSha256 is not null && !string.Equals(expectedConfigSha256, previous.ConfigSha256, StringComparison.OrdinalIgnoreCase))
                return Failure("tools.set", "runtime.settings_changed", "Native settings changed since the catalog was read; no mutation was sent.");
            if (!string.Equals(
                    dispatchConfigSha256,
                    previous.ConfigSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes native config changed before tools.set dispatch; no mutation was sent.");
            }
            JsonElement result;
            try
            {
                result = await _requestAsync(
                    "tools.set",
                    HermesNativeToolsContract.EncodeSetRequest(request),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HermesRequestNotSentException)
            {
                throw;
            }
            catch (HermesRpcException exception)
                when (exception.Code is -32602 or 4090)
            {
                throw;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                QuarantineNativeConfig(modelAuthority);
                throw;
            }
            catch
            {
                QuarantineNativeConfig(modelAuthority);
                throw new LilithNativeConfigMutationUncertainException();
            }

            try
            {
                var receipt = HermesNativeToolsContract.DecodeSetResult(result);
                if (receipt.ApprovalMode != request.ApprovalMode
                    || receipt.SetupSelection != request.SetupSelection
                    || receipt.McpSetup != request.McpSetup
                    || (request.ToolDiscoveryEnabled is { } requestedDiscovery
                        && receipt.ToolDiscoveryEnabled != requestedDiscovery)
                    || !receipt.EnabledToolsets.ToHashSet(StringComparer.Ordinal)
                        .SetEquals(request.EnabledToolsets))
                {
                    throw new InvalidDataException(
                        "Hermes tools.set result did not match the requested native config.");
                }

                if (request.ToolDiscoveryEnabled is { } discoveryEnabled)
                {
                    var readback = HermesNativeToolsContract.DecodeCatalog(await _requestAsync(
                        "tools.catalog", Json("{}"), CancellationToken.None).ConfigureAwait(false));
                    if (readback.ToolDiscoveryEnabled != discoveryEnabled
                        || readback.ApprovalMode != request.ApprovalMode
                        || !readback.EnabledToolsets.ToHashSet(StringComparer.Ordinal).SetEquals(request.EnabledToolsets))
                        throw new InvalidDataException("Native tool discovery setting was not independently read back.");
                }

                if (request.SetupSelection is { } setup)
                {
                    var readback = HermesNativeToolsContract.DecodeCatalog(await _requestAsync(
                        "tools.catalog", Json("{}"), CancellationToken.None).ConfigureAwait(false));
                    if (readback.ApprovalMode != request.ApprovalMode
                        || !readback.EnabledToolsets.ToHashSet(StringComparer.Ordinal).SetEquals(request.EnabledToolsets)
                        || !readback.Toolsets.Any(row => row.Name == setup.Toolset
                            && row.SetupProviders?.Any(provider => provider.Name == setup.Provider && provider.Selected
                                && (setup.Model is null || provider.CurrentModel == setup.Model)) == true))
                        throw new InvalidDataException("Native provider selection was not independently read back.");
                }

                if (request.McpSetup is { } mcpSetup)
                {
                    var readback = HermesNativeToolsContract.DecodeCatalog(await _requestAsync(
                        "tools.catalog", Json("{}"), CancellationToken.None).ConfigureAwait(false));
                    if (readback.ApprovalMode != request.ApprovalMode
                        || !readback.EnabledToolsets.ToHashSet(StringComparer.Ordinal).SetEquals(request.EnabledToolsets)
                        || !HermesNativeToolsContract.McpSetupMatchesCatalog(mcpSetup, readback.McpCatalog, requireRegistered: true))
                        throw new InvalidDataException("Native MCP setup was not independently read back.");
                }

                await LilithNativeConfigMutationReceiptStore.CommitAsync(
                    _hermesHomePath,
                    previous,
                    receipt,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                QuarantineNativeConfig(modelAuthority);
                throw new LilithNativeConfigMutationUncertainException();
            }
            if (!_modelAuthority.PublishNativeConfigMutation(
                    modelAuthority,
                    _generationId))
            {
                QuarantineNativeConfig(modelAuthority);
                throw new LilithNativeConfigMutationUncertainException();
            }
            ObserveSharedNativeConfigRevision(_modelAuthority.Snapshot());
            lock (_sessionLock)
            {
                _nativeToolsNewSessionRequired = true;
                if (_activeSessionId is not null)
                {
                    _nativeConfigInvalidatedSessionId = _activeSessionId;
                    _sessionStates[_activeSessionId] =
                        SessionNativeSurfaceState.Pending;
                    _pendingApprovals.Remove(_activeSessionId);
                }
            }
            return Success("tools.set", result);
        }
        finally
        {
            _nativeConfigGate.Release();
        }
    }

    private LilithRuntimeResponse? NativeModelAuthorityMutationFailure(
        string method,
        LilithRuntimeModelAuthoritySnapshot snapshot)
    {
        if (snapshot.GenerationId != _generationId
            || snapshot.State is LilithRuntimeModelAuthorityState.Unavailable
                or LilithRuntimeModelAuthorityState.Retired)
        {
            return Failure(
                method,
                "runtime.native_surface_attestation_required",
                "The Runtime model authority does not belong to this ready generation.");
        }
        if (snapshot.State == LilithRuntimeModelAuthorityState.Quarantined)
        {
            return NativeConfigQuarantineFailure(
                method,
                "Hermes native config authority failed; Runtime restart and profile recovery are required.");
        }
        return null;
    }

    private void ObserveSharedNativeConfigRevision(
        LilithRuntimeModelAuthoritySnapshot snapshot)
    {
        if (snapshot.GenerationId != _generationId)
            return;
        lock (_sessionLock)
        {
            if (snapshot.NativeConfigRevision <= _observedNativeConfigRevision)
                return;
            _observedNativeConfigRevision = snapshot.NativeConfigRevision;
            _nativeToolsNewSessionRequired = true;
            if (_activeSessionId is null
                || _sessionNativeConfigRevisions.TryGetValue(
                    _activeSessionId,
                    out var admittedRevision)
                    && admittedRevision == snapshot.NativeConfigRevision)
            {
                return;
            }
            _nativeConfigInvalidatedSessionId = _activeSessionId;
            _sessionStates[_activeSessionId] = SessionNativeSurfaceState.Pending;
            _pendingApprovals.Remove(_activeSessionId);
        }
    }

    private void QuarantineNativeConfig(
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        if (!_modelAuthority.Quarantine(modelAuthority, _generationId))
        {
            throw new InvalidOperationException(
                "Runtime could not quarantine native config under the active authority lease.");
        }
        lock (_sessionLock)
        {
            _nativeConfigQuarantined = true;
            _nativeConfigMutationInFlightSessionId = null;
            _pendingApprovals.Clear();
            if (_activeSessionId is not null)
            {
                _sessionStates[_activeSessionId] =
                    SessionNativeSurfaceState.Rejected;
            }
        }
    }

    private async Task<LilithRuntimeResponse> RouteRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        string? active;
        bool activeAttested;
        lock (_sessionLock)
        {
            active = _activeSessionId;
            activeAttested = active is not null
                && _sessionStates.TryGetValue(active, out var state)
                && state == SessionNativeSurfaceState.Attested;
        }

        var nativeToolsPayload = await _requestAsync(
            "tools.catalog",
            Json("{}"),
            cancellationToken).ConfigureAwait(false);
        var nativeTools = HermesNativeToolsContract.DecodeCatalog(
            nativeToolsPayload);
        bool newSessionRequired;
        lock (_sessionLock)
            newSessionRequired = _nativeToolsNewSessionRequired;
        var model = _modelIdentity();
        return Success("runtime.status", JsonSerializer.SerializeToElement(new
        {
            generationId = _generationId.ToString("N"),
            state = _runtimeState().ToString().ToLowerInvariant(),
            hermesCommit = LilithRuntimeOptions.PinnedHermesCommit,
            effectPolicy = new HermesNativeEffectPolicy(
                nativeTools.ApprovalMode,
                nativeTools.EnabledToolsets,
                newSessionRequired),
            provider = model.ProviderId,
            model = model.ModelId,
            activeSessionId = active,
            activeSessionSurfaceAttested = activeAttested,
            dailyNote = DailyNoteDiagnosticPayload(),
            nativeVoice = new
            {
                state = VoiceLifecycleStateName.ToLowerInvariant(),
                submitMode = "draft",
                autoSubmit = false,
                tts = false,
                wake = false
            }
        }));
    }

    private object DailyNoteDiagnosticPayload()
    {
        var value = _dailyNoteDiagnostic();
        if (value?.GenerationId != _generationId) value = null;
        return new
        {
            observed = value is not null,
            reason = (value?.Reason ?? LilithDailyNoteDiagnosticReason.NotObserved).ToString(),
            observedAtUtc = value?.ObservedAtUtc,
            retryNotBeforeUtc = value?.RetryNotBeforeUtc,
            grantsAuthority = false
        };
    }

    private async Task<LilithRuntimeResponse> RouteCapabilityCatalogAsync(
        CancellationToken cancellationToken)
    {
        // These six reads are independent and read-only. Run them together so
        // one slow family does not add its full latency to every other family;
        // each result is then projected or safely downgraded on its own.
        var reads = await Task.WhenAll(
            ReadCapabilityCatalogSectionAsync(
                "commands.catalog",
                Json("{}"),
                cancellationToken),
            ReadCapabilityCatalogSectionAsync(
                "tools.list",
                Json("{}"),
                cancellationToken),
            ReadCapabilityCatalogSectionAsync(
                "tools.catalog",
                Json("{}"),
                cancellationToken),
            ReadCapabilityCatalogSectionAsync(
                "model.options",
                Json(
                    "{\"explicit_only\":false,\"include_unconfigured\":true,\"refresh\":false}"),
                cancellationToken),
            ReadCapabilityCatalogSectionAsync(
                "skills.manage",
                Json("{\"action\":\"list\"}"),
                cancellationToken),
            ReadCapabilityCatalogSectionAsync(
                "plugins.manage",
                Json("{\"action\":\"list\"}"),
                cancellationToken)).ConfigureAwait(false);

        var cached = SnapshotCapabilityCatalogSessionState();
        var catalog = HermesCapabilityCatalogAssembler.Assemble(
            reads[0],
            reads[1],
            reads[2],
            cached.NewSessionRequired,
            cached.ToolSnapshotAttested,
            cached.LoadedTools,
            reads[3],
            reads[4],
            reads[5],
            cached.Mcp);
        return Success(
            HermesCapabilityCatalogContract.Method,
            JsonSerializer.SerializeToElement(catalog));
    }

    private async Task<HermesCapabilityCatalogNativeRead>
        ReadCapabilityCatalogSectionAsync(
            string method,
            JsonElement parameters,
            CancellationToken cancellationToken)
    {
        try
        {
            return HermesCapabilityCatalogNativeRead.Available(
                await _requestAsync(
                    method,
                    parameters,
                    cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HermesRpcException exception) when (exception.Code == -32601)
        {
            return HermesCapabilityCatalogNativeRead.Unavailable(
                HermesCapabilitySectionReason.Unsupported);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never project native exception text, paths, or transport details
            // into the public catalog. The closed reason token is sufficient.
            return HermesCapabilityCatalogNativeRead.Unavailable(
                HermesCapabilitySectionReason.RequestFailed);
        }
    }

    private (
        HermesCapabilityMcpCatalog Mcp,
        bool NewSessionRequired,
        bool ToolSnapshotAttested,
        IReadOnlyList<string> LoadedTools)
        SnapshotCapabilityCatalogSessionState()
    {
        JsonElement? sessionInfo = null;
        bool newSessionRequired;
        lock (_sessionLock)
        {
            newSessionRequired = _nativeToolsNewSessionRequired;
            if (_activeSessionId is not null
                && _sessionStates.TryGetValue(_activeSessionId, out var state)
                && state == SessionNativeSurfaceState.Attested
                && _lastAllowedSessionInfo is not null
                && string.Equals(
                    _lastAllowedSessionInfo.SessionId,
                    _activeSessionId,
                    StringComparison.Ordinal))
            {
                sessionInfo = _lastAllowedSessionInfo.Payload.Clone();
            }
        }

        return (
            sessionInfo.HasValue
                ? HermesNativeSessionInfoSurface.ProjectAttestedMcpCatalog(
                    sessionInfo.Value)
                : new HermesCapabilityMcpCatalog(
                    HermesCapabilityMcpSnapshotState.Unavailable,
                    Array.Empty<HermesCapabilityMcpServer>()),
            newSessionRequired,
            sessionInfo.HasValue,
            sessionInfo.HasValue
                ? HermesNativeSessionInfoSurface.ProjectAttestedToolNames(
                    sessionInfo.Value)
                : Array.Empty<string>());
    }

    private static string[] AllowedProperties(string method)
        => method switch
        {
            "runtime.status" => Array.Empty<string>(),
            HermesUsageBudgetContract.GetMethod => Array.Empty<string>(),
            HermesUsageBudgetPreviewContract.PreviewMethod => Array.Empty<string>(),
            HermesLatestUsageContract.GetMethod => Array.Empty<string>(),
            HermesUsageBudgetContract.SetMethod => new[] { "schema_version", "operation_id", "expected_revision", "level" },
            HermesCapabilityCatalogContract.Method => Array.Empty<string>(),
            HermesCommandCatalogContract.Method => Array.Empty<string>(),
            "expression.get" => Array.Empty<string>(),
            "expression.version.get" => new[] { "revision" },
            "expression.save" => new[] { "operation_id", "expected_revision", "content", "reason" },
            "expression.revert" => new[] { "operation_id", "expected_revision", "target_revision", "reason" },
            "memory.catalog" => new[] { "include_hidden", "limit", "offset" },
            "memory.preferences.get" => Array.Empty<string>(),
            "memory.entry.add" => new[]
            {
                "operation_id", "expected_catalog_revision", "category",
                "content", "pinned"
            },
            "memory.entry.pin.set" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "pinned"
            },
            "memory.entry.promote" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "target_category"
            },
            "memory.entry.correct" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "replacement_content"
            },
            "memory.entry.visibility.set" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "hidden"
            },
            "memory.entry.reject" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version"
            },
            "memory.entry.delete" => new[]
            {
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "permanent"
            },
            "memory.preferences.set" => new[]
            {
                "operation_id", "expected_preferences_revision",
                "auto_memory_enabled"
            },
            "memory.export" => new[]
            {
                "operation_id", "expected_catalog_revision", "format"
            },
            "tools.settings.get" => Array.Empty<string>(),
            "tools.settings.set" => new[] { "expected_config_sha256", "enabled_toolsets", "approval_mode", "setup_selection", "mcp_setup", "tool_discovery_enabled" },
            "tools.catalog" => new[] { "session_id" },
            "tools.set" => new[]
            {
                "session_id", "enabled_toolsets", "approval_mode", "setup_selection", "mcp_setup", "tool_discovery_enabled"
            },
            "session.create" => new[]
            {
                "title", "model", "provider", "reasoning_effort", "fast", "cols"
            },
            "session.list" => new[] { "limit" },
            "session.resume" => new[] { "session_id", "cols", "omit_messages" },
            "session.status" or "session.history" or "session.interrupt"
                or "session.close" or "session.usage"
                or "session.context_breakdown" => new[] { "session_id" },
            "session.append_assistant_event" => new[]
            {
                "session_id", "event_id", "text", "occurred_at_unix_ms",
                "node_id", "line_id", "trigger", "locale"
            },
            "session.extend_native_assistant_event" => new[]
            {
                "session_id", "event_id", "proactive_dialogue_enabled",
                "perception", "game_context"
            },
            "session.generate_foreground_proactive" => new[]
            {
                "session_id", "event_id", "proactive_dialogue_enabled",
                "perception", "game_context"
            },
            "session.delete" => new[] { "session_id", "stored_session_id" },
            "session.compress" => new[] { "session_id", "focus_topic" },
            "session.title" => new[] { "session_id", "title" },
            "prompt.submit" => new[]
            {
                "session_id", "text", "display_locale", "speech_locale", "ui_locale", "game_context", "game_context_diagnostic", "game_answer_repair"
            },
            "prompt.project_speech" => new[]
            {
                "session_id", "text", "display_locale", "speech_locale", "turn_id",
                "notification_event_id", "notification_message_id"
            },
            "prompt.draft_note" => new[]
            {
                "session_id", "operation_id", "event_id", "category", "topic",
                "reply_context", "display_locale"
            },
            "image.attach_bytes" => new[]
            {
                "session_id", "content_base64", "filename"
            },
            "image.detach" => new[] { "session_id", "path" },
            "model.options" => new[]
            {
                "session_id", "explicit_only", "include_unconfigured", "refresh"
            },
            "model.credential_status" => Array.Empty<string>(),
            "model.replace_key" => new[]
            {
                "slug", "api_key", "operation_id"
            },
            "model.remove_key" => new[] { "slug", "operation_id" },
            "provider.setup.catalog" or "provider.setup.models"
                or "provider.setup.save" or "provider.setup.remove"
                => HermesProviderSetupContract.RequestKeys(method).ToArray(),
            "messaging.telegram.status" => Array.Empty<string>(),
            "messaging.telegram.replace" => new[]
            {
                "bot_token", "operation_id"
            },
            "messaging.telegram.remove" => new[] { "operation_id" },
            "messaging.telegram.pairing.list" => Array.Empty<string>(),
            "messaging.telegram.pairing.approve" => new[] { "request_id" },
            "messaging.telegram.pairing.revoke" => new[] { "user_id" },
            TelegramVoiceStatusMethod => new[] { "user_id" },
            TelegramVoiceSetMethod => new[] { "user_id", "mode", "expected_revision" },
            "model.default.set" => new[]
            {
                "provider", "model", "confirm_expensive_model"
            },
            "config.set" => new[]
            {
                "session_id", "provider", "model", "confirm_expensive_model"
            },
            "learning.frames" => new[] { "cols", "rows", "frames" },
            "approval.respond" => new[] { "session_id", "choice" },
            "clarify.respond" => new[] { "session_id", "request_id", "answer" },
            "sudo.respond" => new[] { "session_id", "request_id", "password" },
            "secret.respond" => new[] { "session_id", "request_id", "value" },
            "delegation.status" => new[] { "session_id" },
            "subagent.interrupt" => new[] { "session_id", "subagent_id" },
            "subagent.steer" => new[] { "session_id", "subagent_id", "text" },
            "voice.toggle" => new[] { "session_id", "action" },
            "voice.record" => new[]
            {
                "session_id", "action", "capture_id", "language",
                "maximum_duration_ms", "disposition"
            },
            "rollback.list" => new[] { "session_id" },
            "rollback.diff" => new[] { "session_id", "hash" },
            "rollback.restore" => new[]
            {
                "session_id", "hash", "confirmation_token"
            },
            _ => throw new InvalidDataException("The Runtime method was rejected.")
        };

    private static HermesNativeToolsSetRequest DecodeNativeToolsSetClientPayload(
        JsonElement parameters)
        => HermesNativeToolsContract.DecodeSetRequest(
            JsonSerializer.SerializeToElement(parameters.EnumerateObject()
                .Where(p => p.Name is "enabled_toolsets" or "approval_mode" or "setup_selection" or "mcp_setup" or "tool_discovery_enabled")
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)));

    private static bool IsDelegationRpc(string method)
        => method is "delegation.status" or "subagent.steer"
            or "subagent.interrupt";

    private static bool IsMemoryManagementRpc(string method)
        => method is "memory.catalog" or "memory.entry.add"
            or "memory.entry.pin.set" or "memory.entry.promote"
            or "memory.entry.correct" or "memory.entry.visibility.set"
            or "memory.entry.reject" or "memory.entry.delete"
            or "memory.preferences.get" or "memory.preferences.set"
            or "memory.export";

    private static bool IsCredentialRpc(string method)
        => method is "model.credential_status" or "model.replace_key"
            or "model.remove_key";

    private static bool IsCredentialMutationRpc(string method)
        => method is "model.replace_key" or "model.remove_key";

    private static bool IsTelegramMessagingRpc(string method)
        => method is "messaging.telegram.status"
            or "messaging.telegram.replace"
            or "messaging.telegram.remove"
            or "messaging.telegram.pairing.list"
            or "messaging.telegram.pairing.approve"
            or "messaging.telegram.pairing.revoke"
            or TelegramVoiceStatusMethod
            or TelegramVoiceSetMethod;

    private static bool IsTelegramMessagingMutationRpc(string method)
        => method is "messaging.telegram.replace"
            or "messaging.telegram.remove";

    private static bool IsGenerationMutationRpc(string method)
        => IsCredentialMutationRpc(method)
            || HermesProviderSetupContract.IsMutation(method)
            || IsTelegramMessagingMutationRpc(method);

    private static bool IsDefiniteCredentialRejectionCode(int code)
        => code is 4001 or 4002 or 4006;

    private static void ValidateCredentialProviderSlug(string value)
    {
        if (value.Length is < 1 or > 128
            || !IsLowerAsciiLetterOrDigit(value[0])
            || value.Skip(1).Any(static character =>
                !IsLowerAsciiLetterOrDigit(character)
                && character is not ('-' or '_')))
        {
            throw new InvalidDataException(
                "Runtime provider credential identity was rejected.");
        }
    }

    private static void ValidateCredentialOperationId(string value)
    {
        if (value.Length is < 1 or > MaximumCredentialOperationIdUtf8Bytes
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Skip(1).Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new InvalidDataException(
                "Runtime provider credential operation identity was rejected.");
        }
    }

    private static void ValidateTelegramBotToken(string value)
    {
        var match = TelegramBotTokenPattern.Match(value);
        if (!match.Success
            || match.Index != 0
            || match.Length != value.Length)
        {
            throw new InvalidDataException(
                "Runtime Telegram bot token was rejected.");
        }
    }

    private static void ValidateTelegramPairingRequestId(string value)
    {
        if (value.Length != TelegramPairingRequestIdCharacters
            || value.Any(static character => character is not (
                >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                "Runtime Telegram pairing request identity was rejected.");
        }
    }

    private static void ValidateTelegramUserId(string value)
    {
        if (value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not (':' or '-' or '_' or '.')))
        {
            throw new InvalidDataException(
                "Runtime Telegram user identity was rejected.");
        }
    }

    private static bool IsLowerAsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateDisplayLocale(string value)
    {
        if (value is not ("zh-Hant" or "zh-Hans" or "ja" or "en"))
        {
            throw new InvalidDataException(
                "Runtime game display locale was rejected.");
        }
    }

    private static void ValidateSpeechLocale(string value)
    {
        if (value is not ("zh" or "ja"))
        {
            throw new InvalidDataException(
                "Runtime game speech locale was rejected.");
        }
    }

    private static JsonElement SanitizeTelegramMessagingResult(
        string method,
        JsonElement request,
        JsonElement result)
    {
        if (method is TelegramVoiceStatusMethod or TelegramVoiceSetMethod)
            return SanitizeTelegramVoiceResult(method, request, result);

        if (string.Equals(
                method,
                "messaging.telegram.status",
                StringComparison.Ordinal))
        {
            var values = ReadExactObject(
                result,
                new[] { "configured", "gateway_running", "state" });
            if (!TryRequiredBoolean(values, "configured", out var configured)
                || !TryRequiredBoolean(
                    values,
                    "gateway_running",
                    out var gatewayRunning))
            {
                throw new InvalidDataException(
                    "Hermes returned invalid Telegram status state.");
            }
            var state = RequiredText(values, "state", 32);
            var consistent = (configured, gatewayRunning, state) switch
            {
                (false, false, "unconfigured") => true,
                (true, _, "starting") => true,
                (true, true, "connected") => true,
                (true, _, "attention") => true,
                _ => false
            };
            if (!consistent)
            {
                throw new InvalidDataException(
                    "Hermes returned contradictory Telegram status state.");
            }
            return JsonSerializer.SerializeToElement(new
            {
                configured,
                gateway_running = gatewayRunning,
                state
            });
        }

        if (method is "messaging.telegram.replace"
            or "messaging.telegram.remove")
        {
            var values = ReadExactObject(
                result,
                new[] { "operation_id", "configured", "changed" });
            var operationId = RequiredText(
                values,
                "operation_id",
                MaximumCredentialOperationIdUtf8Bytes);
            ValidateCredentialOperationId(operationId);
            var requestedOperationId = request.GetProperty("operation_id")
                .GetString();
            if (!string.Equals(
                    operationId,
                    requestedOperationId,
                    StringComparison.Ordinal)
                || !TryRequiredBoolean(values, "configured", out var configured)
                || !TryRequiredBoolean(values, "changed", out var changed)
                || configured != string.Equals(
                    method,
                    "messaging.telegram.replace",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid Telegram configuration receipt.");
            }
            return JsonSerializer.SerializeToElement(new
            {
                operation_id = operationId,
                configured,
                changed
            });
        }

        if (string.Equals(
                method,
                "messaging.telegram.pairing.list",
                StringComparison.Ordinal))
        {
            var root = ReadExactObject(result, new[] { "pending", "approved" });
            if (!root.TryGetValue("pending", out var pending)
                || pending.ValueKind != JsonValueKind.Array
                || pending.GetArrayLength() > MaximumTelegramPairingEntries
                || !root.TryGetValue("approved", out var approved)
                || approved.ValueKind != JsonValueKind.Array
                || approved.GetArrayLength() > MaximumTelegramPairingEntries)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid Telegram pairing catalog.");
            }

            var safePending = new List<object>(pending.GetArrayLength());
            var pendingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in pending.EnumerateArray())
            {
                var values = ReadExactObject(
                    entry,
                    new[] { "request_id", "user_id", "user_name", "age_minutes" });
                var requestId = RequiredText(
                    values,
                    "request_id",
                    TelegramPairingRequestIdCharacters);
                ValidateTelegramPairingRequestId(requestId);
                if (!pendingIds.Add(requestId))
                    throw new InvalidDataException(
                        "Hermes returned a duplicate Telegram pairing request.");
                var userId = RequiredText(
                    values,
                    "user_id",
                    MaximumTelegramUserIdUtf8Bytes);
                ValidateTelegramUserId(userId);
                var userName = RequiredTextAllowEmpty(
                    values,
                    "user_name",
                    MaximumTelegramUserNameUtf8Bytes);
                var ageMinutes = RequiredInteger(
                    values,
                    "age_minutes",
                    0,
                    int.MaxValue);
                safePending.Add(new
                {
                    request_id = requestId,
                    user_id = userId,
                    user_name = userName,
                    age_minutes = ageMinutes
                });
            }

            var safeApproved = new List<object>(approved.GetArrayLength());
            var approvedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in approved.EnumerateArray())
            {
                var values = ReadExactObject(entry, new[] { "user_id" });
                var userId = RequiredText(
                    values,
                    "user_id",
                    MaximumTelegramUserIdUtf8Bytes);
                ValidateTelegramUserId(userId);
                if (!approvedIds.Add(userId))
                    throw new InvalidDataException(
                        "Hermes returned a duplicate Telegram approved user.");
                safeApproved.Add(new { user_id = userId });
            }
            return JsonSerializer.SerializeToElement(new
            {
                pending = safePending,
                approved = safeApproved
            });
        }

        if (string.Equals(
                method,
                "messaging.telegram.pairing.approve",
                StringComparison.Ordinal))
        {
            var values = ReadExactObject(
                result,
                new[] { "request_id", "user_id", "approved" });
            var requestId = RequiredText(
                values,
                "request_id",
                TelegramPairingRequestIdCharacters);
            ValidateTelegramPairingRequestId(requestId);
            var userId = RequiredText(
                values,
                "user_id",
                MaximumTelegramUserIdUtf8Bytes);
            ValidateTelegramUserId(userId);
            if (!string.Equals(
                    requestId,
                    request.GetProperty("request_id").GetString(),
                    StringComparison.Ordinal)
                || !TryRequiredBoolean(values, "approved", out var approved)
                || !approved)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid Telegram pairing approval receipt.");
            }
            return JsonSerializer.SerializeToElement(new
            {
                request_id = requestId,
                user_id = userId,
                approved = true
            });
        }

        if (string.Equals(
                method,
                "messaging.telegram.pairing.revoke",
                StringComparison.Ordinal))
        {
            var values = ReadExactObject(result, new[] { "user_id", "revoked" });
            var userId = RequiredText(
                values,
                "user_id",
                MaximumTelegramUserIdUtf8Bytes);
            ValidateTelegramUserId(userId);
            if (!string.Equals(
                    userId,
                    request.GetProperty("user_id").GetString(),
                    StringComparison.Ordinal)
                || !TryRequiredBoolean(values, "revoked", out var revoked)
                || !revoked)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid Telegram pairing revocation receipt.");
            }
            return JsonSerializer.SerializeToElement(new
            {
                user_id = userId,
                revoked = true
            });
        }

        throw new InvalidDataException(
            "Hermes returned an unknown Telegram operation receipt.");
    }

    private static JsonElement SanitizeCredentialResult(
        string method,
        JsonElement request,
        JsonElement result)
    {
        if (string.Equals(
                method,
                "model.credential_status",
                StringComparison.Ordinal))
        {
            var root = ReadExactObject(result, new[] { "providers" });
            if (!root.TryGetValue("providers", out var providers)
                || providers.ValueKind != JsonValueKind.Array
                || providers.GetArrayLength() > MaximumCredentialProviders)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid credential catalog.");
            }

            var safe = new List<object>(providers.GetArrayLength());
            var slugs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var provider in providers.EnumerateArray())
            {
                var region = HermesCredentialRegionMetadata.Parse(provider);
                var values = ReadExactObject(provider, HermesCredentialRegionMetadata.RowKeys(provider));
                var slug = RequiredTrimmedText(
                    values,
                    "slug",
                    MaximumProviderUtf8Bytes);
                ValidateCredentialProviderSlug(slug);
                if (!slugs.Add(slug))
                {
                    throw new InvalidDataException(
                        "Hermes returned a duplicate credential provider.");
                }
                var name = RequiredTrimmedText(
                    values,
                    "name",
                    MaximumProviderUtf8Bytes);
                if (!TryRequiredBoolean(values, "configured", out var configured)
                    || !TryRequiredBoolean(
                        values,
                        "legacy_sources_present",
                        out var legacySourcesPresent))
                {
                    throw new InvalidDataException(
                        "Hermes returned invalid credential state.");
                }
                var row = new Dictionary<string, object>
                {
                    ["slug"] = slug,
                    ["name"] = name,
                    ["configured"] = configured,
                    ["legacy_sources_present"] = legacySourcesPresent
                };
                if (region is not null)
                {
                    row["family_id"] = region.FamilyId;
                    row["family_name"] = region.FamilyName;
                    row["variant_id"] = region.VariantId;
                    row["base_url"] = region.BaseUrl;
                }
                safe.Add(row);
            }
            return JsonSerializer.SerializeToElement(new { providers = safe });
        }

        var mutation = ReadExactObject(
            result,
            new[]
            {
                "slug",
                "name",
                "configured",
                "changed",
                "legacy_sources_cleared"
            });
        var mutationSlug = RequiredTrimmedText(
            mutation,
            "slug",
            MaximumProviderUtf8Bytes);
        ValidateCredentialProviderSlug(mutationSlug);
        var requestedSlug = request.GetProperty("slug").GetString()
            ?? throw new InvalidDataException(
                "Runtime provider credential request identity was rejected.");
        if (!string.Equals(
                mutationSlug,
                requestedSlug,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes returned a credential receipt for a different provider.");
        }
        var mutationName = RequiredTrimmedText(
            mutation,
            "name",
            MaximumProviderUtf8Bytes);
        if (!TryRequiredBoolean(mutation, "configured", out var isConfigured)
            || !TryRequiredBoolean(mutation, "changed", out var changed)
            || !TryRequiredBoolean(
                mutation,
                "legacy_sources_cleared",
                out var legacySourcesCleared))
        {
            throw new InvalidDataException(
                "Hermes returned an invalid credential mutation receipt.");
        }
        var expectedConfigured = string.Equals(
            method,
            "model.replace_key",
            StringComparison.Ordinal);
        if (isConfigured != expectedConfigured)
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory credential mutation receipt.");
        }
        return JsonSerializer.SerializeToElement(new
        {
            slug = mutationSlug,
            name = mutationName,
            configured = isConfigured,
            changed,
            legacy_sources_cleared = legacySourcesCleared
        });
    }

    private static bool IsDelegationControlRpc(string method)
        => method is "subagent.steer" or "subagent.interrupt";

    private static void ValidateSessionInterruptResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Hermes returned a malformed session interrupt receipt.");
        var statusSeen = false;
        foreach (var property in result.EnumerateObject())
        {
            if (property.NameEquals("status"))
            {
                if (statusSeen
                    || property.Value.ValueKind != JsonValueKind.String
                    || !string.Equals(
                        property.Value.GetString(),
                        "interrupted",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes did not prove that the session was interrupted.");
                }
                statusSeen = true;
                continue;
            }
            if (property.NameEquals("turn_isolation")
                && property.Value.ValueKind is JsonValueKind.True
                    or JsonValueKind.False)
            {
                continue;
            }
            throw new InvalidDataException(
                "Hermes returned an unknown session interrupt receipt field.");
        }
        if (!statusSeen)
            throw new InvalidDataException(
                "Hermes session interrupt receipt omitted its terminal status.");
    }

    private static bool IsVoiceRpc(string method)
        => method is "voice.toggle" or "voice.record";

    private static bool IsCheckpointRpc(string method)
        => method is "rollback.list" or "rollback.diff" or "rollback.restore";

    private static bool IsConversationMutation(string method)
        => method is "prompt.submit" or "session.append_assistant_event"
            or "session.extend_native_assistant_event"
            or "session.generate_foreground_proactive"
            or "config.set" or "image.attach_bytes"
            or "image.detach" or "session.compress" or "clarify.respond"
            or "sudo.respond" or "secret.respond";

    private bool VoiceBlocksConversationMutation()
    {
        lock (_sessionLock)
        {
            return _voiceRequestInFlight
                || _voiceState is VoiceLifecycleState.Enabling
                    or VoiceLifecycleState.Starting
                    or VoiceLifecycleState.Recording
                    or VoiceLifecycleState.Stopping
                    or VoiceLifecycleState.Transcribing
                    or VoiceLifecycleState.Disabling
                    or VoiceLifecycleState.Draining;
        }
    }

    private bool CheckpointRestoreBlocksConversationMutation()
    {
        lock (_sessionLock)
            return _checkpointRestoreRequestInFlight;
    }

    private async Task<LilithRuntimeResponse> RouteCheckpointAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return method switch
            {
                "rollback.list" => await RouteCheckpointListAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false),
                "rollback.diff" => await RouteCheckpointDiffAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false),
                "rollback.restore" => await RouteCheckpointRestoreAsync(
                    parameters,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidDataException(
                    "The Runtime checkpoint method was rejected.")
            };
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    private async Task<LilithRuntimeResponse> RouteCheckpointListAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = parameters.GetProperty("session_id").GetString()!;
        long catalogRevision;
        lock (_sessionLock)
        {
            if (!IsCheckpointWorkspaceAttestedUnsafe(sessionId))
            {
                return Failure(
                    "rollback.list",
                    "runtime.checkpoint_workspace_attestation_changed",
                    "The active Runtime workspace changed before checkpoints were listed.");
            }
            InvalidateCheckpointCatalogUnsafe();
            catalogRevision = _checkpointCatalogRevision;
        }
        var upstream = JsonSerializer.SerializeToElement(new
        {
            session_id = sessionId,
            expected_cwd = _workspacePath
        });
        var result = await _requestAsync(
            "rollback.list",
            upstream,
            cancellationToken).ConfigureAwait(false);
        var receipt = ValidateCheckpointListResult(result);

        lock (_sessionLock)
        {
            if (!IsCheckpointWorkspaceAttestedUnsafe(sessionId)
                || catalogRevision != _checkpointCatalogRevision)
            {
                return Failure(
                    "rollback.list",
                    "runtime.checkpoint_workspace_attestation_changed",
                    "The active Runtime workspace changed while checkpoints were listed.");
            }
            _checkpointCatalog = new HashSet<string>(
                receipt.Entries.Select(static entry => entry.Hash),
                StringComparer.Ordinal);
        }

        return Success("rollback.list", receipt.SafePayload);
    }

    private async Task<LilithRuntimeResponse> RouteCheckpointDiffAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = parameters.GetProperty("session_id").GetString()!;
        var hash = parameters.GetProperty("hash").GetString()!;
        long catalogRevision;
        lock (_sessionLock)
        {
            _checkpointConfirmation = null;
            if (!_checkpointCatalog.Contains(hash))
            {
                return Failure(
                    "rollback.diff",
                    "runtime.checkpoint_catalog_member_required",
                    "Refresh the checkpoint list and select an exact listed checkpoint.");
            }
            catalogRevision = _checkpointCatalogRevision;
        }

        var upstream = JsonSerializer.SerializeToElement(new
        {
            session_id = sessionId,
            hash,
            expected_cwd = _workspacePath
        });
        var result = await _requestAsync(
            "rollback.diff",
            upstream,
            cancellationToken).ConfigureAwait(false);
        var receipt = ValidateCheckpointDiffResult(result);
        if (!receipt.Success)
            return Success("rollback.diff", receipt.SafePayload);

        string token;
        lock (_sessionLock)
        {
            if (!IsCheckpointWorkspaceAttestedUnsafe(sessionId)
                || catalogRevision != _checkpointCatalogRevision
                || !_checkpointCatalog.Contains(hash))
            {
                return Failure(
                    "rollback.diff",
                    "runtime.checkpoint_catalog_changed",
                    "The checkpoint catalog changed while the diff was read; refresh it before restoring.");
            }

            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
                .ToLowerInvariant();
            _checkpointConfirmation = new PendingCheckpointConfirmation(
                token,
                _generationId,
                sessionId,
                hash,
                catalogRevision,
                _utcNow().Add(CheckpointConfirmationLifetime));
        }

        var safe = new JsonObject
        {
            ["success"] = true,
            ["stat"] = receipt.Stat,
            ["diff"] = receipt.Diff,
            ["truncated"] = receipt.Truncated,
            ["confirmation_token"] = token
        };
        return Success(
            "rollback.diff",
            JsonSerializer.SerializeToElement(safe));
    }

    private async Task<LilithRuntimeResponse> RouteCheckpointRestoreAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!TryReserveCheckpointRestore(
                parameters,
                out var reservation,
                out var rejection))
        {
            return Failure(
                "rollback.restore",
                rejection.Code,
                rejection.Message);
        }

        try
        {
            var delegationParameters = JsonSerializer.SerializeToElement(new
            {
                session_id = reservation.SessionId
            });
            var delegation = await _requestAsync(
                "delegation.status",
                delegationParameters,
                cancellationToken).ConfigureAwait(false);
            ValidateDelegationResult(
                "delegation.status",
                delegationParameters,
                delegation);
            if (delegation.GetProperty("active").GetArrayLength() != 0)
            {
                AbortCheckpointRestoreBeforeDispatch(reservation);
                return Failure(
                    "rollback.restore",
                    "runtime.checkpoint_delegation_active",
                    "Interrupt all active delegated work before restoring the workspace.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AbortCheckpointRestoreBeforeDispatch(reservation);
            throw;
        }
        catch (HermesRpcException exception) when (exception.Code == 5035)
        {
            AbortCheckpointRestoreBeforeDispatch(reservation);
            throw;
        }
        catch (HermesRpcException exception)
        {
            AbortCheckpointRestoreBeforeDispatch(reservation);
            return Failure(
                "rollback.restore",
                "runtime.checkpoint_preflight_rejected",
                exception.Message,
                exception.Code);
        }
        catch (Exception)
        {
            AbortCheckpointRestoreBeforeDispatch(reservation);
            return Failure(
                "rollback.restore",
                "runtime.checkpoint_preflight_failed",
                "The delegated-work preflight could not be attested; no restore was sent.");
        }

        lock (_sessionLock)
        {
            if (!IsCheckpointRestoreReservationValidUnsafe(reservation))
            {
                AbortCheckpointRestoreBeforeDispatchUnsafe(reservation);
                return Failure(
                    "rollback.restore",
                    "runtime.checkpoint_restore_state_changed",
                    "Runtime state changed before restore dispatch; no restore was sent.");
            }
        }

        if (!TryEnsureWorkspacePathSafe())
        {
            AbortCheckpointRestoreBeforeDispatch(reservation);
            return Failure(
                "rollback.restore",
                "runtime.workspace_path_unsafe",
                "The fixed Runtime workspace path changed before restore dispatch.");
        }

        var upstream = JsonSerializer.SerializeToElement(new
        {
            session_id = reservation.SessionId,
            hash = reservation.Hash,
            expected_cwd = _workspacePath,
            confirm = true,
            operation_id = reservation.OperationId
        });

        JsonElement result = default;
        var resultReceived = false;
        try
        {
            result = await _requestAsync(
                "rollback.restore",
                upstream,
                cancellationToken).ConfigureAwait(false);
            resultReceived = true;
            var receipt = ValidateCheckpointRestoreResult(
                result,
                reservation.OperationId,
                reservation.Hash);
            var completion = CompleteCheckpointRestoreReceipt(
                reservation,
                receipt);
            if (completion == CheckpointRestoreCompletion.Rejected)
            {
                if (receipt.WorkspaceRestored || receipt.HistoryRewound)
                {
                    return Failure(
                        "rollback.restore",
                        "runtime.checkpoint_post_mutation_uncertain",
                        "The workspace changed, but the restore attestation was inconsistent; reconnect before continuing.");
                }
                return new LilithRuntimeResponse(
                    "rollback.restore",
                    succeeded: false,
                    receipt.SafePayload);
            }
            return Success("rollback.restore", receipt.SafePayload);
        }
        catch (HermesRequestNotSentException exception)
        {
            CompleteCheckpointRestoreNotSent(reservation);
            if (cancellationToken.IsCancellationRequested
                && exception.InnerException is OperationCanceledException)
            {
                throw new OperationCanceledException(
                    "The workspace restore was canceled before gateway send.",
                    exception,
                    cancellationToken);
            }
            return Failure(
                "rollback.restore",
                "runtime.gateway_not_sent",
                "The workspace restore was not sent and may be confirmed again after a fresh diff.");
        }
        catch (HermesRpcException exception) when (exception.Code == 5035)
        {
            QuarantineCheckpointRestore(reservation);
            throw;
        }
        catch (HermesRpcException exception)
            when (IsDeterministicCheckpointPreMutationCode(exception.Code))
        {
            CompleteCheckpointRestoreDeterministicFailure(reservation);
            return Failure(
                "rollback.restore",
                "hermes.rpc_rejected",
                exception.Message,
                exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            QuarantineCheckpointRestore(reservation);
            throw;
        }
        catch (Exception)
        {
            QuarantineCheckpointRestore(reservation);
            if (resultReceived && ClaimsCheckpointHistoryMutation(result))
            {
                return Failure(
                    "rollback.restore",
                    "runtime.checkpoint_post_mutation_uncertain",
                    "The restore result may have changed the workspace and conversation history; reconnect before continuing.");
            }
            return Failure(
                "rollback.restore",
                "runtime.checkpoint_restore_quarantined",
                "The workspace restore outcome is uncertain; restore is disabled until Runtime restart.");
        }
    }

    private static bool ClaimsCheckpointHistoryMutation(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return false;
        var workspaceRestored = false;
        var historyRewound = false;
        foreach (var property in result.EnumerateObject())
        {
            if (property.NameEquals("workspace_restored")
                && property.Value.ValueKind == JsonValueKind.True)
            {
                workspaceRestored = true;
            }
            else if (property.NameEquals("history_rewound")
                     && property.Value.ValueKind == JsonValueKind.True)
            {
                historyRewound = true;
            }
        }
        return workspaceRestored && historyRewound;
    }

    private bool TryReserveCheckpointRestore(
        JsonElement parameters,
        out CheckpointRestoreReservation reservation,
        out (string Code, string Message) rejection)
    {
        var sessionId = parameters.GetProperty("session_id").GetString()!;
        var hash = parameters.GetProperty("hash").GetString()!;
        var token = parameters.GetProperty("confirmation_token").GetString()!;
        lock (_sessionLock)
        {
            reservation = null!;
            rejection = default;
            if (_checkpointRestoreState == CheckpointRestoreState.Quarantined)
            {
                rejection = (
                    "runtime.checkpoint_restore_quarantined",
                    "Workspace restore is quarantined until Runtime restart.");
                return false;
            }
            if (_checkpointRestoreRequestInFlight)
            {
                rejection = (
                    "runtime.checkpoint_restore_active",
                    "Another workspace restore is already in flight.");
                return false;
            }
            if (!IsCheckpointWorkspaceAttestedUnsafe(sessionId)
                || !_checkpointCatalog.Contains(hash))
            {
                rejection = (
                    "runtime.checkpoint_catalog_member_required",
                    "Refresh the checkpoint list and select an exact listed checkpoint.");
                return false;
            }

            var confirmation = _checkpointConfirmation;
            if (confirmation is null
                || !string.Equals(
                    confirmation.Token,
                    token,
                    StringComparison.Ordinal)
                || confirmation.GenerationId != _generationId
                || !string.Equals(
                    confirmation.SessionId,
                    sessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    confirmation.Hash,
                    hash,
                    StringComparison.Ordinal)
                || confirmation.CatalogRevision != _checkpointCatalogRevision
                || confirmation.ExpiresUtc <= _utcNow())
            {
                if (confirmation is not null
                    && confirmation.ExpiresUtc <= _utcNow())
                {
                    _checkpointConfirmation = null;
                }
                rejection = (
                    "runtime.checkpoint_confirmation_required",
                    "Read a fresh checkpoint diff and use its one-time confirmation token.");
                return false;
            }
            _checkpointConfirmation = null;
            if (!CheckpointRestoreEnvironmentReadyUnsafe(sessionId))
            {
                rejection = (
                    "runtime.checkpoint_restore_not_ready",
                    "Restore requires idle image and voice state, no session transition, and no pending approval.");
                return false;
            }

            var operationId = Guid.NewGuid().ToString("N");
            reservation = new CheckpointRestoreReservation(
                sessionId,
                hash,
                _checkpointCatalogRevision,
                operationId);
            _checkpointRestoreState = CheckpointRestoreState.InFlight;
            _checkpointRestoreRequestInFlight = true;
            _checkpointOperationId = operationId;
            _checkpointMutationObserved = false;
            _checkpointObservedRestoreEvent = null;
            return true;
        }
    }

    private bool CheckpointRestoreEnvironmentReadyUnsafe(string sessionId)
        => !_sessionLifecycleInFlight
            && !_voiceRequestInFlight
            && (_voiceState == VoiceLifecycleState.Off
                || _voiceState == VoiceLifecycleState.Ready
                    && string.Equals(
                        _voiceSessionId,
                        sessionId,
                        StringComparison.Ordinal))
            && _sessionImages.TryGetValue(sessionId, out var image)
            && image.State == SessionImageLifecycleState.Idle
            && image.Attachment is null
            && (!_pendingApprovals.TryGetValue(sessionId, out var approvals)
                || approvals.Count == 0);

    private bool IsCheckpointRestoreReservationValidUnsafe(
        CheckpointRestoreReservation reservation)
        => _checkpointRestoreRequestInFlight
            && _checkpointRestoreState == CheckpointRestoreState.InFlight
            && !_checkpointMutationObserved
            && string.Equals(
                _checkpointOperationId,
                reservation.OperationId,
                StringComparison.Ordinal)
            && reservation.CatalogRevision == _checkpointCatalogRevision
            && _checkpointCatalog.Contains(reservation.Hash)
            && IsCheckpointWorkspaceAttestedUnsafe(reservation.SessionId)
            && CheckpointRestoreEnvironmentReadyUnsafe(reservation.SessionId);

    private void AbortCheckpointRestoreBeforeDispatch(
        CheckpointRestoreReservation reservation)
    {
        lock (_sessionLock)
            AbortCheckpointRestoreBeforeDispatchUnsafe(reservation);
    }

    private void AbortCheckpointRestoreBeforeDispatchUnsafe(
        CheckpointRestoreReservation reservation)
    {
        if (!string.Equals(
                _checkpointOperationId,
                reservation.OperationId,
                StringComparison.Ordinal))
        {
            return;
        }
        _checkpointRestoreState = _checkpointMutationObserved
            || _checkpointRestoreState == CheckpointRestoreState.Quarantined
                ? CheckpointRestoreState.Quarantined
                : CheckpointRestoreState.Ready;
        _checkpointRestoreRequestInFlight = false;
        _checkpointOperationId = null;
        _checkpointObservedRestoreEvent = null;
    }

    private void CompleteCheckpointRestoreNotSent(
        CheckpointRestoreReservation reservation)
        => CompleteCheckpointRestoreBeforeMutation(reservation);

    private void CompleteCheckpointRestoreDeterministicFailure(
        CheckpointRestoreReservation reservation)
        => CompleteCheckpointRestoreBeforeMutation(reservation);

    private void CompleteCheckpointRestoreBeforeMutation(
        CheckpointRestoreReservation reservation)
    {
        lock (_sessionLock)
        {
            if (!string.Equals(
                    _checkpointOperationId,
                    reservation.OperationId,
                    StringComparison.Ordinal))
            {
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                _checkpointRestoreRequestInFlight = false;
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
                return;
            }
            _checkpointRestoreState = _checkpointMutationObserved
                || _checkpointRestoreState == CheckpointRestoreState.Quarantined
                    ? CheckpointRestoreState.Quarantined
                    : CheckpointRestoreState.Ready;
            _checkpointRestoreRequestInFlight = false;
            _checkpointOperationId = null;
            _checkpointObservedRestoreEvent = null;
        }
    }

    private CheckpointRestoreCompletion CompleteCheckpointRestoreReceipt(
        CheckpointRestoreReservation reservation,
        ValidatedCheckpointRestoreReceipt receipt)
    {
        lock (_sessionLock)
        {
            if (!string.Equals(
                    _checkpointOperationId,
                    reservation.OperationId,
                    StringComparison.Ordinal))
            {
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                _checkpointRestoreRequestInFlight = false;
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
                return CheckpointRestoreCompletion.Rejected;
            }

            if (receipt.Disposition == CheckpointRestoreDisposition.Partial)
            {
                var acceptedPartial =
                    _checkpointRestoreState == CheckpointRestoreState.InFlight
                    && _checkpointMutationObserved
                    && CheckpointRestoreEventMatchesUnsafe(receipt);
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                _checkpointRestoreRequestInFlight = false;
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
                return acceptedPartial
                    ? CheckpointRestoreCompletion.Partial
                    : CheckpointRestoreCompletion.Rejected;
            }

            var accepted = receipt.Disposition switch
            {
                CheckpointRestoreDisposition.Succeeded =>
                    _checkpointMutationObserved
                    && CheckpointRestoreEventMatchesUnsafe(receipt)
                    && _checkpointRestoreState == CheckpointRestoreState.InFlight,
                CheckpointRestoreDisposition.NotMutated =>
                    !_checkpointMutationObserved
                    && _checkpointRestoreState == CheckpointRestoreState.InFlight,
                _ => false
            };
            if (!accepted)
            {
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                _checkpointRestoreRequestInFlight = false;
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
                return CheckpointRestoreCompletion.Rejected;
            }

            _checkpointRestoreState = CheckpointRestoreState.Ready;
            _checkpointRestoreRequestInFlight = false;
            _checkpointOperationId = null;
            _checkpointObservedRestoreEvent = null;
            if (receipt.Disposition == CheckpointRestoreDisposition.Succeeded)
                InvalidateCheckpointCatalogUnsafe();
            return CheckpointRestoreCompletion.Accepted;
        }
    }

    private void QuarantineCheckpointRestore(
        CheckpointRestoreReservation reservation)
    {
        lock (_sessionLock)
        {
            if (string.Equals(
                    _checkpointOperationId,
                    reservation.OperationId,
                    StringComparison.Ordinal))
            {
                _checkpointRestoreState = CheckpointRestoreState.Quarantined;
                _checkpointRestoreRequestInFlight = false;
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
            }
        }
    }

    private void QuarantineCheckpointRestore()
    {
        lock (_sessionLock)
        {
            _checkpointRestoreState = CheckpointRestoreState.Quarantined;
            if (!_checkpointRestoreRequestInFlight)
            {
                _checkpointOperationId = null;
                _checkpointObservedRestoreEvent = null;
            }
            _checkpointConfirmation = null;
        }
    }

    private static bool IsDeterministicCheckpointPreMutationCode(int code)
        => code is 4004 or 4009 or 4014;

    private bool CheckpointRestoreEventMatchesUnsafe(
        ValidatedCheckpointRestoreReceipt receipt)
        => _checkpointObservedRestoreEvent is { } observed
            && observed.Success == receipt.Success
            && observed.HistoryRewound == receipt.HistoryRewound
            && observed.HistoryRemoved == receipt.HistoryRemoved
            && observed.Partial == receipt.Partial
            && string.Equals(
                observed.RestoredTo,
                receipt.RestoredTo,
                StringComparison.Ordinal)
            && string.Equals(
                observed.PreSnapshot,
                receipt.PreSnapshot,
                StringComparison.Ordinal);

    private bool IsOwnedVoiceOffCleanup(
        string method,
        JsonElement parameters,
        string? sessionId)
    {
        if (method != "voice.toggle"
            || sessionId is null
            || !parameters.TryGetProperty("action", out var action)
            || action.ValueKind != JsonValueKind.String
            || !string.Equals(action.GetString(), "off", StringComparison.Ordinal))
        {
            return false;
        }

        lock (_sessionLock)
        {
            return string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && string.Equals(
                    _voiceSessionId,
                    sessionId,
                    StringComparison.Ordinal)
                && _voiceState != VoiceLifecycleState.Off;
        }
    }

    private async Task<LilithRuntimeResponse> RouteVoiceAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime native voice requires a session id.");
        var action = parameters.GetProperty("action").GetString()!;
        if (ReconcileStaleReadyVoiceOwnerForCaptureStart(
                method,
                action,
                sessionId))
        {
            // GameBridge physically toggles the process-global Hermes voice
            // off after every bounded F6 turn. A Workbench router can retain a
            // stale local Ready projection if it did not project the other
            // surface's event frames. Re-enable before its next capture; never
            // dispatch voice.record into a globally-off gateway.
            var enabled = await RouteVoiceAsync(
                    "voice.toggle",
                    JsonSerializer.SerializeToElement(new
                    {
                        session_id = sessionId,
                        action = "on"
                    }),
                    cancellationToken,
                    modelAuthority)
                .ConfigureAwait(false);
            if (!enabled.Succeeded)
            {
                var code = enabled.HermesPayload.ValueKind == JsonValueKind.Object
                        && enabled.HermesPayload.TryGetProperty("code", out var codeValue)
                        && codeValue.ValueKind == JsonValueKind.String
                        && codeValue.GetString() is { Length: > 0 } enabledCode
                    ? enabledCode
                    : "runtime.voice_enable_failed";
                var message = enabled.HermesPayload.ValueKind == JsonValueKind.Object
                        && enabled.HermesPayload.TryGetProperty(
                            "message",
                            out var messageValue)
                        && messageValue.ValueKind == JsonValueKind.String
                        && messageValue.GetString() is { Length: > 0 } enabledMessage
                    ? enabledMessage
                    : "Hermes native voice could not be re-enabled for this capture.";
                return Failure(method, code, message);
            }
        }
        var rejection = TryBeginVoiceRequest(method, action, sessionId);
        if (rejection is not null)
        {
            return Failure(method, rejection.Value.Code, rejection.Value.Message);
        }

        var ownerAdmission = TryAdmitVoiceOwner(
            method,
            action,
            sessionId,
            parameters);
        if (ownerAdmission.Rejection is { } ownerRejection)
        {
            RestoreVoiceAfterNotSent(method, action);
            lock (_sessionLock)
                _voiceRequestInFlight = false;
            return Failure(method, ownerRejection.Code, ownerRejection.Message);
        }
        var voiceOwner = ownerAdmission.Owner;
        long operationEpoch;
        lock (_sessionLock)
        {
            operationEpoch = ++_voiceOperationEpoch;
            if (_voiceRequestModelAuthority is not null)
            {
                throw new InvalidOperationException(
                    "A native voice authority lease is already published.");
            }
            _voiceRequestModelAuthority = modelAuthority;
        }

        var responseReceived = false;
        try
        {
            var upstreamParameters = method == "voice.toggle"
                ? JsonSerializer.SerializeToElement(new { action })
                : parameters;
            var result = await _requestAsync(
                method,
                upstreamParameters,
                cancellationToken).ConfigureAwait(false);
            responseReceived = true;
            if (!IsCurrentVoiceOperation(operationEpoch))
            {
                return Failure(
                    method,
                    "runtime.voice_cancelled",
                    "The native voice operation was superseded by an exact cancellation.");
            }
            var disposition = ValidateVoiceResult(
                method,
                action,
                result,
                voiceOwner?.CaptureId ?? Guid.Empty);
            CompleteVoiceRequest(method, action, sessionId, disposition);
            CompleteVoiceOwnerRequest(
                method,
                action,
                disposition,
                voiceOwner);
            if (disposition == VoiceResultDisposition.Busy)
            {
                return Failure(
                    method,
                    "runtime.voice_busy",
                    "Hermes native voice is already owned by another capture surface.");
            }
            return Success(method, result);
        }
        catch (HermesRequestNotSentException exception)
        {
            if (!IsCurrentVoiceOperation(operationEpoch))
            {
                return Failure(
                    method,
                    "runtime.voice_cancelled",
                    "The native voice operation was superseded by an exact cancellation.");
            }
            RestoreVoiceAfterNotSent(method, action);
            RestoreVoiceOwnerAfterNotSent(method, action, voiceOwner);
            if (cancellationToken.IsCancellationRequested
                && exception.InnerException is OperationCanceledException)
            {
                throw new OperationCanceledException(
                    "The native voice request was canceled before gateway send.",
                    exception,
                    cancellationToken);
            }
            throw;
        }
        catch
        {
            if (!IsCurrentVoiceOperation(operationEpoch))
            {
                return Failure(
                    method,
                    "runtime.voice_cancelled",
                    "The native voice operation was superseded by an exact cancellation.");
            }
            if (action != "status" || responseReceived)
                QuarantineVoice();
            throw;
        }
        finally
        {
            Task? physicalCleanupDrained = null;
            lock (_sessionLock)
            {
                if (_voiceOperationEpoch == operationEpoch)
                    _voiceRequestInFlight = false;
                if (ReferenceEquals(
                        _voiceRequestModelAuthority,
                        modelAuthority))
                {
                    _voiceRequestModelAuthority = null;
                    if (_voicePhysicalCleanupBorrowers != 0)
                    {
                        _voicePhysicalCleanupBorrowersDrained ??=
                            new TaskCompletionSource(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                        physicalCleanupDrained =
                            _voicePhysicalCleanupBorrowersDrained.Task;
                    }
                }
            }
            if (physicalCleanupDrained is not null)
                await physicalCleanupDrained.ConfigureAwait(false);
        }
    }

    private bool ReconcileStaleReadyVoiceOwnerForCaptureStart(
        string method,
        string action,
        string sessionId)
    {
        if (_sessionSurface != LilithRuntimeSessionSurface.Workbench
            || method != "voice.record"
            || action != "start")
        {
            return false;
        }

        lock (_sessionLock)
        {
            if (_voiceState != VoiceLifecycleState.Ready
                || !string.Equals(
                    _voiceSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                return false;
            }
            if (_voiceCaptureArbiter.TryGetOwner(out var owner, out _)
                && owner.GenerationId == _generationId
                && owner.Surface == _sessionSurface
                && string.Equals(
                    owner.SessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            _voiceState = VoiceLifecycleState.Off;
            _voiceSessionId = null;
            _voiceCaptureId = Guid.Empty;
            _voiceDisableAwaitingIdle = false;
            _voiceCleanupAttempted = false;
            _voiceQuarantineRequiresDrain = false;
            return true;
        }
    }

    private bool IsCurrentVoiceOperation(long operationEpoch)
    {
        lock (_sessionLock)
            return _voiceOperationEpoch == operationEpoch;
    }

    internal async Task<bool> CancelGameVoiceCaptureAsync(
        LilithRuntimeVoiceCaptureOwner owner,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease? ownedModelAuthority = null)
    {
        if (ownedModelAuthority is not null)
        {
            if (!_modelAuthority.OwnsActiveLease(
                    ownedModelAuthority,
                    _generationId)
                || ownedModelAuthority.Snapshot.State
                    != LilithRuntimeModelAuthorityState.Ready)
            {
                return false;
            }
            return await CancelGameVoiceCaptureCoreAsync(
                    owner,
                    cancellationToken,
                    ownedModelAuthority)
                .ConfigureAwait(false);
        }

        LilithRuntimeModelAuthorityLease? borrowedVoiceAuthority;
        lock (_sessionLock)
        {
            borrowedVoiceAuthority = _voiceRequestModelAuthority;
            if (borrowedVoiceAuthority is not null)
                _voicePhysicalCleanupBorrowers++;
        }
        if (borrowedVoiceAuthority is not null)
        {
            try
            {
                // Physical cancel is the one deliberate concurrent borrower:
                // it may supersede a long transcribe RPC, but it uses that
                // operation's still-owned shared lease and can never publish
                // a transcript or outlive lease disposal.
                if (!_modelAuthority.OwnsActiveLease(
                        borrowedVoiceAuthority,
                        _generationId)
                    || borrowedVoiceAuthority.Snapshot.State
                        != LilithRuntimeModelAuthorityState.Ready)
                {
                    return false;
                }
                return await CancelGameVoiceCaptureCoreAsync(
                        owner,
                        cancellationToken,
                        borrowedVoiceAuthority)
                    .ConfigureAwait(false);
            }
            finally
            {
                ReleaseVoicePhysicalCleanupBorrower();
            }
        }
        using var modelAuthority = await _modelAuthority.AcquireAsync(
                _generationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (modelAuthority.Snapshot.GenerationId != _generationId
            || modelAuthority.Snapshot.State
                != LilithRuntimeModelAuthorityState.Ready)
        {
            return false;
        }
        return await CancelGameVoiceCaptureCoreAsync(
                owner,
                cancellationToken,
                modelAuthority)
            .ConfigureAwait(false);
    }

    private void ReleaseVoicePhysicalCleanupBorrower()
    {
        TaskCompletionSource? drained = null;
        lock (_sessionLock)
        {
            if (_voicePhysicalCleanupBorrowers <= 0)
            {
                throw new InvalidOperationException(
                    "The native voice cleanup lease count underflowed.");
            }
            _voicePhysicalCleanupBorrowers--;
            if (_voicePhysicalCleanupBorrowers == 0)
            {
                drained = _voicePhysicalCleanupBorrowersDrained;
                _voicePhysicalCleanupBorrowersDrained = null;
            }
        }
        drained?.TrySetResult();
    }

    private async Task<bool> CancelGameVoiceCaptureCoreAsync(
        LilithRuntimeVoiceCaptureOwner owner,
        CancellationToken cancellationToken,
        LilithRuntimeModelAuthorityLease modelAuthority)
    {
        ArgumentNullException.ThrowIfNull(owner);
        owner.Validate();
        if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge
            || owner.Surface != LilithRuntimeSessionSurface.GameBridge
            || owner.GenerationId != _generationId)
        {
            return false;
        }

        long cancellationEpoch;
        lock (_sessionLock)
        {
            if (!_voiceCaptureArbiter.IsOwnedBy(owner)
                || !string.Equals(
                    _activeSessionId,
                    owner.SessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    _voiceSessionId,
                    owner.SessionId,
                    StringComparison.Ordinal)
                || _voiceCaptureId != owner.CaptureId
                || _voiceState is not (
                    VoiceLifecycleState.Starting
                    or VoiceLifecycleState.Recording
                    or VoiceLifecycleState.Stopping
                    or VoiceLifecycleState.Transcribing
                    or VoiceLifecycleState.Ready))
            {
                return false;
            }

            // Invalidate a possibly long-running transcribe RPC. Its eventual
            // response is still consumed from the gateway, but can no longer
            // mutate this router or clear a newer capture's in-flight flag.
            cancellationEpoch = ++_voiceOperationEpoch;
            _voiceRequestInFlight = true;
            _voiceState = VoiceLifecycleState.Stopping;
        }

        try
        {
            var stopped = await _requestAsync(
                "voice.record",
                JsonSerializer.SerializeToElement(new
                {
                    session_id = owner.SessionId,
                    action = "stop",
                    capture_id = owner.CaptureId.ToString("N"),
                    disposition = "cancel"
                }),
                cancellationToken).ConfigureAwait(false);
            _ = ValidateVoiceResult(
                "voice.record",
                "stop",
                stopped,
                owner.CaptureId);

            lock (_sessionLock)
            {
                if (_voiceOperationEpoch != cancellationEpoch)
                    return false;
                _voiceState = VoiceLifecycleState.Disabling;
                _voiceDisableAwaitingIdle = false;
                _voiceCleanupAttempted = true;
            }
            var disabled = await _requestAsync(
                "voice.toggle",
                JsonSerializer.SerializeToElement(new { action = "off" }),
                cancellationToken).ConfigureAwait(false);
            _ = ValidateVoiceResult("voice.toggle", "off", disabled);

            lock (_sessionLock)
            {
                if (_voiceOperationEpoch != cancellationEpoch)
                    return false;
                _voiceState = VoiceLifecycleState.Off;
                _voiceDisableAwaitingIdle = false;
                _voiceCleanupAttempted = false;
                _voiceQuarantineRequiresDrain = false;
                ReleaseVoiceOwnerUnsafe();
                _voiceSessionId = null;
                _voiceRequestInFlight = false;
                SignalVoiceIdleUnsafe();
            }
            return true;
        }
        catch (HermesRpcException exception) when (exception.Code == 5035)
        {
            QuarantineNativeConfig(modelAuthority);
            lock (_sessionLock)
            {
                if (_voiceOperationEpoch == cancellationEpoch)
                {
                    _voiceRequestInFlight = false;
                    QuarantineVoiceUnsafe();
                }
            }
            return false;
        }
        catch
        {
            lock (_sessionLock)
            {
                if (_voiceOperationEpoch == cancellationEpoch)
                {
                    _voiceRequestInFlight = false;
                    QuarantineVoiceUnsafe();
                }
            }
            return false;
        }
    }

    private (
        LilithRuntimeVoiceCaptureOwner? Owner,
        (string Code, string Message)? Rejection) TryAdmitVoiceOwner(
            string method,
            string action,
            string sessionId,
            JsonElement parameters)
    {
        if (method == "voice.toggle" && action == "status")
            return (null, null);

        LilithRuntimeVoiceCaptureOwner owner;
        if (_sessionSurface == LilithRuntimeSessionSurface.GameBridge)
        {
            if (!_voiceCaptureArbiter.TryGetOwner(
                    out owner,
                    out _)
                || owner.GenerationId != _generationId
                || owner.Surface != LilithRuntimeSessionSurface.GameBridge
                || !string.Equals(
                    owner.SessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                return (null, (
                    "runtime.voice_busy",
                    "Hermes native voice is owned by another Runtime surface."));
            }
            if (method == "voice.record"
                && (!parameters.TryGetProperty("capture_id", out var gameCapture)
                    || RequiredCaptureId(gameCapture, "capture_id")
                        != owner.CaptureId))
            {
                return (null, (
                    "runtime.voice_capture_mismatch",
                    "The native voice capture identity did not match its admitted game peer."));
            }
        }
        else
        {
            if (method == "voice.toggle" && action == "on")
            {
                owner = new LilithRuntimeVoiceCaptureOwner(
                    _generationId,
                    _sessionSurface,
                    sessionId,
                    Guid.Empty,
                    PeerProcessId: 0,
                    PeerProcessCreationTime: 0);
            }
            else if (method == "voice.record" && action == "start")
            {
                var captureId = parameters.TryGetProperty(
                        "capture_id",
                        out var requestedCapture)
                    ? RequiredCaptureId(requestedCapture, "capture_id")
                    : Guid.NewGuid();
                owner = new LilithRuntimeVoiceCaptureOwner(
                    _generationId,
                    _sessionSurface,
                    sessionId,
                    captureId,
                    PeerProcessId: 0,
                    PeerProcessCreationTime: 0);
            }
            else if (_voiceCaptureArbiter.TryGetOwner(
                    out var current,
                    out _)
                && current.GenerationId == _generationId
                && current.Surface == _sessionSurface
                && string.Equals(
                    current.SessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                owner = current;
            }
            else
            {
                owner = new LilithRuntimeVoiceCaptureOwner(
                    _generationId,
                    _sessionSurface,
                    sessionId,
                    Guid.Empty,
                    PeerProcessId: 0,
                    PeerProcessCreationTime: 0);
            }
        }

        var admitted = (method, action) switch
        {
            ("voice.toggle", "on") when
                _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                => _voiceCaptureArbiter.IsOwnedBy(owner),
            ("voice.toggle", "on")
                => _voiceCaptureArbiter.TryAcquireReady(owner),
            ("voice.toggle", "off")
                => _voiceCaptureArbiter.IsOwnedBy(owner),
            ("voice.record", "start")
                => _voiceCaptureArbiter.TryBeginCapture(owner),
            ("voice.record", "stop")
                => _voiceCaptureArbiter.TryMarkTranscribing(owner),
            _ => false
        };
        if (!admitted)
        {
            return (null, (
                "runtime.voice_busy",
                "Hermes native voice is already owned by another capture surface."));
        }

        if (method == "voice.record" && action == "start")
        {
            lock (_sessionLock)
                _voiceCaptureId = owner.CaptureId;
        }
        return (owner, null);
    }

    private void CompleteVoiceOwnerRequest(
        string method,
        string action,
        VoiceResultDisposition disposition,
        LilithRuntimeVoiceCaptureOwner? owner)
    {
        if (owner is null)
            return;
        if (disposition == VoiceResultDisposition.Busy)
        {
            _ = _voiceCaptureArbiter.TryMarkReady(owner);
            return;
        }
        if (method == "voice.toggle" && action == "off")
        {
            lock (_sessionLock)
            {
                if (_voiceState == VoiceLifecycleState.Off)
                {
                    _ = _voiceCaptureArbiter.Release(owner);
                    _voiceCaptureId = Guid.Empty;
                }
            }
        }
    }

    private void RestoreVoiceOwnerAfterNotSent(
        string method,
        string action,
        LilithRuntimeVoiceCaptureOwner? owner)
    {
        if (owner is null)
            return;
        if (method == "voice.toggle" && action == "on")
        {
            _ = _voiceCaptureArbiter.Release(owner);
            return;
        }
        if (method == "voice.record" && action is "start" or "stop")
            _ = _voiceCaptureArbiter.TryMarkReady(owner);
    }

    private (string Code, string Message)? TryBeginVoiceRequest(
        string method,
        string action,
        string sessionId)
    {
        lock (_sessionLock)
        {
            if (_checkpointRestoreRequestInFlight)
            {
                return (
                    "runtime.checkpoint_restore_active",
                    "Wait for the active workspace restore to finish before changing voice state.");
            }
            if (_sessionLifecycleInFlight)
            {
                return (
                    "runtime.session_lifecycle_busy",
                    "A session transition is already in flight.");
            }
            if (_voiceState == VoiceLifecycleState.Quarantined)
            {
                if (method == "voice.toggle"
                    && action == "off"
                    && !_voiceCleanupAttempted
                    && _voiceSessionId is not null
                    && string.Equals(
                        _voiceSessionId,
                        sessionId,
                        StringComparison.Ordinal))
                {
                    _voiceCleanupAttempted = true;
                    _voiceRequestInFlight = true;
                    _voiceOffOrigin = VoiceLifecycleState.Quarantined;
                    _voiceDisableAwaitingIdle = _voiceQuarantineRequiresDrain;
                    _voiceState = VoiceLifecycleState.Disabling;
                    return null;
                }
                return (
                    "runtime.voice_quarantined",
                    "Native voice state is uncertain; Runtime restart is required.");
            }
            if (_voiceRequestInFlight
                || _voiceState is VoiceLifecycleState.Enabling
                    or VoiceLifecycleState.Disabling
                    or VoiceLifecycleState.Starting
                    or VoiceLifecycleState.Stopping)
            {
                return (
                    "runtime.voice_busy",
                    "Another native voice transition is already in flight.");
            }
            if (_voiceState != VoiceLifecycleState.Off
                && !string.Equals(
                    _voiceSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                return (
                    "runtime.voice_session_mismatch",
                    "Native voice is owned by a different active session.");
            }

            var allowed = (method, action, _voiceState) switch
            {
                ("voice.toggle", "status", VoiceLifecycleState.Off) => true,
                ("voice.toggle", "status", VoiceLifecycleState.Ready) => true,
                ("voice.toggle", "status", VoiceLifecycleState.Recording) => true,
                ("voice.toggle", "status", VoiceLifecycleState.Transcribing) => true,
                ("voice.toggle", "status", VoiceLifecycleState.Draining) => true,
                ("voice.toggle", "on", VoiceLifecycleState.Off) => true,
                ("voice.toggle", "off", VoiceLifecycleState.Ready) => true,
                ("voice.toggle", "off", VoiceLifecycleState.Recording) => true,
                ("voice.toggle", "off", VoiceLifecycleState.Transcribing) => true,
                ("voice.record", "start", VoiceLifecycleState.Ready) => true,
                ("voice.record", "stop", VoiceLifecycleState.Recording) => true,
                ("voice.record", "stop", VoiceLifecycleState.Transcribing) => true,
                ("voice.record", "stop", VoiceLifecycleState.Ready) => true,
                _ => false
            };
            if (!allowed)
            {
                return (
                    "runtime.voice_state_conflict",
                    "The native voice action is not valid in the current voice state.");
            }

            _voiceRequestInFlight = true;
            switch (method, action)
            {
                case ("voice.toggle", "on"):
                    _voiceSessionId = sessionId;
                    _voiceCleanupAttempted = false;
                    _voiceQuarantineRequiresDrain = false;
                    _voiceIdleSignal = null;
                    _voiceState = VoiceLifecycleState.Enabling;
                    break;
                case ("voice.toggle", "off"):
                    _voiceCleanupAttempted = true;
                    _voiceOffOrigin = _voiceState;
                    _voiceDisableAwaitingIdle = _voiceState is
                        VoiceLifecycleState.Recording
                        or VoiceLifecycleState.Transcribing;
                    _voiceState = VoiceLifecycleState.Disabling;
                    break;
                case ("voice.record", "start"):
                    _voiceState = VoiceLifecycleState.Starting;
                    break;
                case ("voice.record", "stop"):
                    _voiceStopOrigin = _voiceState;
                    _voiceState = VoiceLifecycleState.Stopping;
                    break;
            }
            return null;
        }
    }

    private void CompleteVoiceRequest(
        string method,
        string action,
        string sessionId,
        VoiceResultDisposition disposition)
    {
        lock (_sessionLock)
        {
            if (_voiceState == VoiceLifecycleState.Quarantined
                || _voiceSessionId is not null
                    && !string.Equals(
                        _voiceSessionId,
                        sessionId,
                        StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes native voice state changed outside the accepted session scope.");
            }

            switch (method, action)
            {
                case ("voice.toggle", "status"):
                    return;
                case ("voice.record", "start")
                    when disposition == VoiceResultDisposition.Busy
                        && _voiceState == VoiceLifecycleState.Starting:
                    _voiceState = VoiceLifecycleState.Ready;
                    return;
                case ("voice.record", "stop")
                    when disposition == VoiceResultDisposition.Busy
                        && _voiceState == VoiceLifecycleState.Stopping:
                    _voiceState = _voiceStopOrigin;
                    return;
                case ("voice.toggle", "on")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Enabling:
                    _voiceState = VoiceLifecycleState.Ready;
                    return;
                case ("voice.toggle", "off")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Off:
                    _voiceDisableAwaitingIdle = false;
                    _voiceQuarantineRequiresDrain = false;
                    _voiceCleanupAttempted = false;
                    _voiceSessionId = null;
                    return;
                case ("voice.toggle", "off")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Disabling
                        && _voiceDisableAwaitingIdle:
                    _voiceState = VoiceLifecycleState.Draining;
                    return;
                case ("voice.toggle", "off")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Disabling:
                    _voiceState = VoiceLifecycleState.Off;
                    _voiceQuarantineRequiresDrain = false;
                    _voiceCleanupAttempted = false;
                    _voiceSessionId = null;
                    return;
                case ("voice.record", "start")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Starting:
                    _voiceState = VoiceLifecycleState.Recording;
                    return;
                case ("voice.record", "start")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Recording:
                    return;
                case ("voice.record", "start")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState is (
                            VoiceLifecycleState.Transcribing
                            or VoiceLifecycleState.Ready
                            or VoiceLifecycleState.Draining
                            or VoiceLifecycleState.Off):
                    return;
                case ("voice.record", "stop")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState == VoiceLifecycleState.Stopping:
                    _voiceState = _voiceStopOrigin == VoiceLifecycleState.Ready
                        ? VoiceLifecycleState.Ready
                        : VoiceLifecycleState.Transcribing;
                    return;
                case ("voice.record", "stop")
                    when disposition == VoiceResultDisposition.Succeeded
                        && _voiceState is (
                            VoiceLifecycleState.Transcribing
                            or VoiceLifecycleState.Ready
                            or VoiceLifecycleState.Draining
                            or VoiceLifecycleState.Off):
                    return;
                default:
                    throw new InvalidDataException(
                        "Hermes native voice returned outside the accepted lifecycle state.");
            }
        }
    }

    private VoiceResultDisposition ValidateVoiceResult(
        string method,
        string action,
        JsonElement result,
        Guid expectedCaptureId = default)
    {
        if (method == "voice.record")
        {
            var values = ReadExactObject(
                result,
                new[] { "status", "reason", "capture_id" });
            var status = RequiredText(values, "status", 32);
            var expected = action == "start" ? "recording" : "stopped";
            var hasCapture = values.TryGetValue("capture_id", out var captureValue);
            if (hasCapture
                && (expectedCaptureId == Guid.Empty
                    || RequiredCaptureId(captureValue, "capture_id")
                        != expectedCaptureId)
                || _sessionSurface == LilithRuntimeSessionSurface.GameBridge
                    && (!hasCapture || expectedCaptureId == Guid.Empty))
            {
                throw new InvalidDataException(
                    "Hermes returned a mismatched native voice capture receipt.");
            }
            if (string.Equals(status, expected, StringComparison.Ordinal)
                && values.Count == (hasCapture ? 2 : 1))
            {
                return VoiceResultDisposition.Succeeded;
            }
            if (string.Equals(status, "busy", StringComparison.Ordinal)
                && (values.Count == (hasCapture ? 2 : 1)
                    || values.Count == (hasCapture ? 3 : 2)
                    && values.TryGetValue("reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String
                    && string.Equals(
                        reason.GetString(),
                        "wake_owned",
                        StringComparison.Ordinal)))
            {
                return VoiceResultDisposition.Busy;
            }
            else
            {
                throw new InvalidDataException(
                    "Hermes returned a noncanonical native voice recording receipt.");
            }
        }

        var allowed = action == "status"
            ? new[]
            {
                "enabled", "record_key", "tts", "available",
                "audio_available", "stt_available", "details"
            }
            : new[] { "enabled", "record_key", "tts", "stop_hint" };
        var toggle = ReadExactObject(result, allowed);
        var expectedPropertyCount = action == "status"
            ? toggle.ContainsKey("available") ? 7 : 3
            : 4;
        if (toggle.Count != expectedPropertyCount
            || !TryRequiredBoolean(toggle, "enabled", out var enabled)
            || !TryRequiredBoolean(toggle, "tts", out var tts)
            || tts
            || !toggle.TryGetValue("record_key", out var recordKey)
            || recordKey.ValueKind != JsonValueKind.String
            || recordKey.GetString() is not { } key
            || string.IsNullOrWhiteSpace(key)
            || key.Any(char.IsControl)
            || StrictUtf8.GetByteCount(key) > MaximumVoiceRecordKeyUtf8Bytes)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed native voice toggle receipt.");
        }

        var expectedEnabled = action switch
        {
            "on" => true,
            "off" => false,
            _ => IsVoiceModeExpectedEnabled()
        };
        if (enabled != expectedEnabled)
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched native voice mode state.");
        }

        if (action == "status")
        {
            if (toggle.Count == 7
                && (!TryRequiredBoolean(toggle, "available", out _)
                    || !TryRequiredBoolean(toggle, "audio_available", out _)
                    || !TryRequiredBoolean(toggle, "stt_available", out _)
                    || !toggle.TryGetValue("details", out var details)
                    || details.ValueKind != JsonValueKind.String
                    || details.GetString() is not { } detailText
                    || detailText.Any(static character =>
                        char.IsControl(character)
                        && character is not ('\r' or '\n' or '\t'))
                    || StrictUtf8.GetByteCount(detailText)
                        > MaximumVoiceDetailsUtf8Bytes))
            {
                throw new InvalidDataException(
                    "Hermes returned malformed native voice availability details.");
            }
            return VoiceResultDisposition.Succeeded;
        }

        if (!toggle.TryGetValue("stop_hint", out var stopHint)
            || stopHint.ValueKind != JsonValueKind.String
            || !string.Equals(stopHint.GetString(), string.Empty, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native voice stop phrases are not disabled.");
        }
        return VoiceResultDisposition.Succeeded;
    }

    private bool IsVoiceModeExpectedEnabled()
    {
        lock (_sessionLock)
            return _voiceState is VoiceLifecycleState.Enabling
                or VoiceLifecycleState.Ready
                or VoiceLifecycleState.Starting
                or VoiceLifecycleState.Recording
                or VoiceLifecycleState.Stopping
                or VoiceLifecycleState.Transcribing;
    }

    private static bool TryRequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out bool result)
    {
        result = false;
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }
        result = value.GetBoolean();
        return true;
    }

    private bool ObserveVoiceGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        lock (_sessionLock)
        {
            if (_voiceState == VoiceLifecycleState.Quarantined)
            {
                if (IsOwnedCanonicalIdleWhileQuarantined(gatewayEvent))
                    _voiceQuarantineRequiresDrain = false;
                return false;
            }
            if (!_voiceCaptureArbiter.TryGetOwner(
                    out var processOwner,
                    out _)
                || processOwner.GenerationId != _generationId
                || processOwner.Surface != _sessionSurface
                || !string.Equals(
                    processOwner.SessionId,
                    _voiceSessionId,
                    StringComparison.Ordinal))
            {
                if (_voiceState == VoiceLifecycleState.Off)
                {
                    // Every Runtime router observes the shared Hermes gateway
                    // stream. Canonical events owned by the other surface are
                    // unrelated input for an Off router, not evidence that its
                    // own protocol was violated.
                    return false;
                }
                if (_voiceState == VoiceLifecycleState.Ready)
                {
                    // A process-wide Ready lease may move to the other surface.
                    // Retire this router's stale projection without treating
                    // the new owner's canonical events as a protocol attack.
                    _voiceState = VoiceLifecycleState.Off;
                    _voiceSessionId = null;
                    _voiceCaptureId = Guid.Empty;
                    return false;
                }
                QuarantineVoiceUnsafe();
                return false;
            }
            if (_voiceSessionId is null
                || !string.Equals(
                    gatewayEvent.SessionId,
                    _voiceSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    _activeSessionId,
                    _voiceSessionId,
                    StringComparison.Ordinal)
                || !_sessionStates.TryGetValue(
                    _voiceSessionId,
                    out var surface)
                || surface != SessionNativeSurfaceState.Attested
                    && _voiceState is not (
                        VoiceLifecycleState.Disabling
                        or VoiceLifecycleState.Draining))
            {
                QuarantineVoiceUnsafe();
                return false;
            }

            var accepted = gatewayEvent.Type == "voice.status"
                ? ObserveVoiceStatusEvent(gatewayEvent.Payload)
                : ObserveVoiceTranscriptEvent(gatewayEvent.Payload);
            if (!accepted)
                QuarantineVoiceUnsafe();
            return accepted;
        }
    }

    private bool IsOwnedCanonicalIdleWhileQuarantined(
        HermesGatewayEvent gatewayEvent)
    {
        if (gatewayEvent.Type != "voice.status"
            || _voiceSessionId is null
            || !string.Equals(
                gatewayEvent.SessionId,
                _voiceSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                _activeSessionId,
                _voiceSessionId,
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var values = ReadExactObject(
                gatewayEvent.Payload,
                new[] { "state", "capture_id" });
            var hasCapture = values.TryGetValue(
                "capture_id",
                out var captureValue);
            return values.Count == (hasCapture ? 2 : 1)
                && values.TryGetValue("state", out var state)
                && state.ValueKind == JsonValueKind.String
                && string.Equals(
                    state.GetString(),
                    "idle",
                    StringComparison.Ordinal)
                && VoiceEventCaptureMatches(hasCapture, captureValue);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private bool ObserveVoiceStatusEvent(JsonElement payload)
    {
        Dictionary<string, JsonElement> values;
        try
        {
            values = ReadExactObject(payload, new[] { "state", "capture_id" });
        }
        catch (InvalidDataException)
        {
            return false;
        }
        var hasCapture = values.TryGetValue("capture_id", out var captureValue);
        if (values.Count != (hasCapture ? 2 : 1)
            || !values.TryGetValue("state", out var stateValue)
            || stateValue.ValueKind != JsonValueKind.String
            || !VoiceEventCaptureMatches(hasCapture, captureValue))
        {
            return false;
        }

        switch (stateValue.GetString())
        {
            case "listening"
                when _voiceState is VoiceLifecycleState.Starting
                    or VoiceLifecycleState.Recording:
                _voiceState = VoiceLifecycleState.Recording;
                return true;
            case "transcribing"
                when _voiceState is VoiceLifecycleState.Recording
                    or VoiceLifecycleState.Stopping
                    or VoiceLifecycleState.Transcribing:
                _voiceState = VoiceLifecycleState.Transcribing;
                MarkVoiceOwnerTranscribingUnsafe();
                return true;
            case "transcribing"
                when _voiceState is VoiceLifecycleState.Disabling
                    or VoiceLifecycleState.Draining:
                return true;
            case "idle"
                when _voiceState is VoiceLifecycleState.Recording
                    or VoiceLifecycleState.Stopping
                    or VoiceLifecycleState.Transcribing
                    or VoiceLifecycleState.Ready:
                _voiceState = VoiceLifecycleState.Ready;
                // A GameBridge idle frame proves only terminal projection. Its
                // single-shot recorder deliberately keeps the persistent
                // InputStream until Host completes canonical toggle-off. Keep
                // the process-wide owner non-transferable until that physical
                // shutdown receipt; otherwise Workbench can steal the lease in
                // the idle-to-finalize window and strand the game microphone.
                if (_sessionSurface != LilithRuntimeSessionSurface.GameBridge)
                    MarkVoiceOwnerReadyUnsafe();
                return true;
            case "idle" when _voiceState == VoiceLifecycleState.Disabling:
                _voiceDisableAwaitingIdle = false;
                _voiceState = VoiceLifecycleState.Off;
                SignalVoiceIdleUnsafe();
                ReleaseVoiceOwnerUnsafe();
                return true;
            case "idle" when _voiceState == VoiceLifecycleState.Draining:
                _voiceDisableAwaitingIdle = false;
                _voiceQuarantineRequiresDrain = false;
                _voiceCleanupAttempted = false;
                _voiceState = VoiceLifecycleState.Off;
                SignalVoiceIdleUnsafe();
                ReleaseVoiceOwnerUnsafe();
                _voiceSessionId = null;
                return true;
            default:
                return false;
        }
    }

    private bool ObserveVoiceTranscriptEvent(JsonElement payload)
    {
        if (_voiceState is not (
                VoiceLifecycleState.Recording
                or VoiceLifecycleState.Stopping
                or VoiceLifecycleState.Transcribing
                or VoiceLifecycleState.Disabling
                or VoiceLifecycleState.Draining))
        {
            return false;
        }

        Dictionary<string, JsonElement> values;
        try
        {
            values = ReadExactObject(
                payload,
                new[]
                {
                    "text", "no_speech_limit", "stop_phrase", "capture_id"
                });
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var hasCapture = values.TryGetValue("capture_id", out var captureValue);
        if (!VoiceEventCaptureMatches(hasCapture, captureValue))
            return false;
        var capturePropertyCount = hasCapture ? 1 : 0;
        var transcript = values.Count == 1 + capturePropertyCount
            && values.TryGetValue("text", out var text)
            && text.ValueKind == JsonValueKind.String
            && text.GetString() is { } transcriptText
            && !string.IsNullOrWhiteSpace(transcriptText)
            && !transcriptText.Any(char.IsControl)
            && StrictUtf8.GetByteCount(transcriptText) <= MaximumPromptUtf8Bytes;
        var silent = values.Count == 1 + capturePropertyCount
            && values.TryGetValue("no_speech_limit", out var noSpeech)
            && noSpeech.ValueKind == JsonValueKind.True;
        var stopPhrase = values.Count == 2 + capturePropertyCount
            && values.TryGetValue("stop_phrase", out var stop)
            && stop.ValueKind == JsonValueKind.True
            && values.TryGetValue("text", out var stopText)
            && stopText.ValueKind == JsonValueKind.String
            && stopText.GetString() is { } phraseText
            && !string.IsNullOrWhiteSpace(phraseText)
            && !phraseText.Any(char.IsControl)
            && StrictUtf8.GetByteCount(phraseText) <= MaximumPromptUtf8Bytes;
        if (!transcript && !silent && !stopPhrase)
            return false;
        if (stopPhrase)
        {
            _voiceDisableAwaitingIdle = true;
            _voiceState = VoiceLifecycleState.Draining;
        }
        else if (_voiceState is not (
            VoiceLifecycleState.Disabling or VoiceLifecycleState.Draining))
        {
            _voiceState = VoiceLifecycleState.Transcribing;
        }
        return true;
    }

    private bool VoiceEventCaptureMatches(
        bool hasCapture,
        JsonElement captureValue)
    {
        if (!hasCapture)
            return _sessionSurface != LilithRuntimeSessionSurface.GameBridge;
        try
        {
            var captureId = RequiredCaptureId(captureValue, "capture_id");
            return _voiceCaptureId != Guid.Empty
                && captureId == _voiceCaptureId;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private void MarkVoiceOwnerReadyUnsafe()
    {
        if (_voiceCaptureArbiter.TryGetOwner(out var owner, out _)
            && owner.GenerationId == _generationId
            && owner.Surface == _sessionSurface
            && string.Equals(owner.SessionId, _voiceSessionId, StringComparison.Ordinal)
            && (_voiceCaptureId == Guid.Empty
                || owner.CaptureId == _voiceCaptureId))
        {
            _ = _voiceCaptureArbiter.TryMarkReady(owner);
        }
    }

    private void MarkVoiceOwnerTranscribingUnsafe()
    {
        if (_voiceCaptureArbiter.TryGetOwner(out var owner, out _)
            && owner.GenerationId == _generationId
            && owner.Surface == _sessionSurface
            && string.Equals(owner.SessionId, _voiceSessionId, StringComparison.Ordinal)
            && (_voiceCaptureId == Guid.Empty
                || owner.CaptureId == _voiceCaptureId))
        {
            _ = _voiceCaptureArbiter.TryMarkTranscribing(owner);
        }
    }

    private void ReleaseVoiceOwnerUnsafe()
    {
        if (_voiceCaptureArbiter.TryGetOwner(out var owner, out _)
            && owner.GenerationId == _generationId
            && owner.Surface == _sessionSurface
            && string.Equals(owner.SessionId, _voiceSessionId, StringComparison.Ordinal))
        {
            _ = _voiceCaptureArbiter.Release(owner);
        }
        _voiceCaptureId = Guid.Empty;
    }

    private void QuarantineVoice()
    {
        lock (_sessionLock)
        {
            QuarantineVoiceUnsafe();
        }
    }

    private void RestoreVoiceAfterNotSent(string method, string action)
    {
        lock (_sessionLock)
        {
            switch (method, action)
            {
                case ("voice.toggle", "status"):
                    return;
                case ("voice.toggle", "on")
                    when _voiceState == VoiceLifecycleState.Enabling:
                    _voiceState = VoiceLifecycleState.Off;
                    _voiceSessionId = null;
                    _voiceDisableAwaitingIdle = false;
                    _voiceCleanupAttempted = false;
                    _voiceQuarantineRequiresDrain = false;
                    _voiceOffOrigin = VoiceLifecycleState.Off;
                    return;
                case ("voice.record", "start")
                    when _voiceState == VoiceLifecycleState.Starting:
                    _voiceState = VoiceLifecycleState.Ready;
                    _voiceDisableAwaitingIdle = false;
                    _voiceQuarantineRequiresDrain = false;
                    return;
                case ("voice.record", "stop")
                    when _voiceState == VoiceLifecycleState.Stopping:
                    _voiceState = _voiceStopOrigin;
                    return;
                case ("voice.record", "stop")
                    when _voiceState is VoiceLifecycleState.Transcribing
                        or VoiceLifecycleState.Ready
                        or VoiceLifecycleState.Draining
                        or VoiceLifecycleState.Off:
                    return;
                case ("voice.toggle", "off")
                    when _voiceState == VoiceLifecycleState.Off:
                    _voiceSessionId = null;
                    _voiceDisableAwaitingIdle = false;
                    _voiceCleanupAttempted = false;
                    _voiceQuarantineRequiresDrain = false;
                    _voiceOffOrigin = VoiceLifecycleState.Off;
                    return;
                case ("voice.toggle", "off"):
                    _voiceCleanupAttempted = false;
                    _voiceDisableAwaitingIdle = false;
                    _voiceQuarantineRequiresDrain =
                        _voiceQuarantineRequiresDrain
                        || VoiceStateRequiresDrain(_voiceOffOrigin)
                        || _voiceState == VoiceLifecycleState.Draining;
                    _voiceState = VoiceLifecycleState.Quarantined;
                    return;
                default:
                    QuarantineVoiceUnsafe();
                    return;
            }
        }
    }

    private static bool VoiceStateRequiresDrain(VoiceLifecycleState state)
        => state is VoiceLifecycleState.Enabling
            or VoiceLifecycleState.Starting
            or VoiceLifecycleState.Recording
            or VoiceLifecycleState.Stopping
            or VoiceLifecycleState.Transcribing
            or VoiceLifecycleState.Disabling
            or VoiceLifecycleState.Draining;

    private void QuarantineVoiceUnsafe()
    {
        _voiceQuarantineRequiresDrain = _voiceQuarantineRequiresDrain
            || _voiceDisableAwaitingIdle
            || _voiceState is VoiceLifecycleState.Starting
                or VoiceLifecycleState.Recording
                or VoiceLifecycleState.Stopping
                or VoiceLifecycleState.Transcribing
                or VoiceLifecycleState.Disabling
                or VoiceLifecycleState.Draining;
        _voiceState = VoiceLifecycleState.Quarantined;
    }

    private void SignalVoiceIdleUnsafe()
    {
        var signal = _voiceIdleSignal;
        _voiceIdleSignal = null;
        signal?.TrySetResult();
    }

    private async Task<LilithRuntimeResponse> RouteDelegationControlAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var sessionId = OptionalSessionId(parameters)
            ?? throw new InvalidDataException(
                "Runtime delegation control requires a session id.");
        try
        {
            var result = await _requestAsync(
                method,
                parameters,
                cancellationToken).ConfigureAwait(false);
            ValidateDelegationResult(method, parameters, result);
            return Success(method, result);
        }
        catch
        {
            // A steer/interrupt is not safely replayable. Once dispatched,
            // any uncertain or noncanonical outcome permanently revokes this
            // session's native control surface until exact close or restart.
            SetSessionState(sessionId, SessionNativeSurfaceState.Rejected);
            throw;
        }
    }

    private static void ValidateDelegationResult(
        string method,
        JsonElement parameters,
        JsonElement result)
    {
        switch (method)
        {
            case "delegation.status":
            {
                var values = ReadExactObject(result, new[]
                {
                    "active", "paused", "max_spawn_depth",
                    "max_concurrent_children"
                });
                if (!values.TryGetValue("active", out var active)
                    || active.ValueKind != JsonValueKind.Array
                    || !values.TryGetValue("paused", out var paused)
                    || paused.ValueKind is not (
                        JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException(
                        "Hermes returned a malformed delegation status receipt.");
                }
                var requestedSession = parameters.GetProperty("session_id")
                    .GetString();
                foreach (var item in active.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object
                        || !TryGetSingleProperty(
                            item,
                            "session_id",
                            out var itemSession)
                        || itemSession.ValueKind != JsonValueKind.String
                        || !string.Equals(
                            itemSession.GetString(),
                            requestedSession,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Hermes returned delegation status outside the active session scope.");
                    }
                }
                RequiredInteger(values, "max_spawn_depth", 1, int.MaxValue);
                RequiredInteger(
                    values,
                    "max_concurrent_children",
                    1,
                    int.MaxValue);
                return;
            }
            case "subagent.interrupt":
            {
                var values = ReadExactObject(result, new[]
                {
                    "found", "subagent_id"
                });
                if (!values.TryGetValue("found", out var found)
                    || found.ValueKind is not (
                        JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException(
                        "Hermes returned a malformed subagent interrupt receipt.");
                }
                ValidateExactEcho(values, parameters, "subagent_id");
                return;
            }
            case "subagent.steer":
            {
                var values = ReadExactObject(result, new[]
                {
                    "status", "subagent_id", "text"
                });
                var status = RequiredText(values, "status", 16);
                if (status is not ("queued" or "rejected"))
                {
                    throw new InvalidDataException(
                        "Hermes returned an unknown subagent steer status.");
                }
                ValidateExactEcho(values, parameters, "subagent_id");
                ValidateExactEcho(values, parameters, "text");
                return;
            }
            default:
                throw new InvalidDataException(
                    "The Runtime delegation method was rejected.");
        }
    }

    private static void ValidateExactEcho(
        IReadOnlyDictionary<string, JsonElement> result,
        JsonElement parameters,
        string propertyName)
    {
        if (!result.TryGetValue(propertyName, out var actual)
            || actual.ValueKind != JsonValueKind.String
            || !parameters.TryGetProperty(propertyName, out var expected)
            || expected.ValueKind != JsonValueKind.String
            || !string.Equals(
                actual.GetString(),
                expected.GetString(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched native delegation receipt.");
        }
    }

    private static ValidatedCheckpointListReceipt ValidateCheckpointListResult(
        JsonElement result)
    {
        var values = ReadExactObject(
            result,
            new[] { "enabled", "checkpoints" });
        if (values.Count != 2
            || !TryRequiredBoolean(values, "enabled", out var enabled)
            || !values.TryGetValue("checkpoints", out var checkpoints)
            || checkpoints.ValueKind != JsonValueKind.Array
            || checkpoints.GetArrayLength() > MaximumCheckpointListEntries)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed checkpoint catalog.");
        }

        var entries = new List<CheckpointCatalogEntry>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var safeEntries = new JsonArray();
        foreach (var item in checkpoints.EnumerateArray())
        {
            var entry = ReadExactObject(
                item,
                new[] { "hash", "timestamp", "message" });
            if (entry.Count != 3)
            {
                throw new InvalidDataException(
                    "Hermes returned an incomplete checkpoint catalog entry.");
            }
            var hash = RequiredCheckpointHash(entry);
            var timestamp = RequiredTextAllowEmpty(
                entry,
                "timestamp",
                MaximumCheckpointTimestampUtf8Bytes);
            var message = RequiredTextAllowEmpty(
                entry,
                "message",
                MaximumCheckpointMessageUtf8Bytes);
            if (!hashes.Add(hash)
                || string.IsNullOrWhiteSpace(timestamp)
                || !DateTimeOffset.TryParse(
                    timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid checkpoint catalog entry.");
            }
            entries.Add(new CheckpointCatalogEntry(hash, timestamp, message));
            safeEntries.Add(new JsonObject
            {
                ["hash"] = hash,
                ["timestamp"] = timestamp,
                ["message"] = message
            });
        }
        if (!enabled && entries.Count != 0)
        {
            throw new InvalidDataException(
                "Hermes returned checkpoints while checkpointing was disabled.");
        }

        var safe = new JsonObject
        {
            ["enabled"] = enabled,
            ["checkpoints"] = safeEntries
        };
        return new ValidatedCheckpointListReceipt(
            entries,
            JsonSerializer.SerializeToElement(safe));
    }

    private static ValidatedCheckpointDiffReceipt ValidateCheckpointDiffResult(
        JsonElement result)
    {
        var values = ReadExactObject(result, new[]
        {
            "success", "stat", "diff", "truncated", "rendered", "error"
        });
        if (!TryRequiredBoolean(values, "success", out var success)
            || !TryRequiredBoolean(values, "truncated", out var truncated))
        {
            throw new InvalidDataException(
                "Hermes returned a malformed checkpoint diff receipt.");
        }
        var stat = RequiredMultilineTextAllowEmpty(
            values,
            "stat",
            MaximumCheckpointStatUtf8Bytes);
        var diff = RequiredMultilineTextAllowEmpty(
            values,
            "diff",
            MaximumCheckpointDiffUtf8Bytes);
        if (values.TryGetValue("rendered", out var rendered)
            && (rendered.ValueKind != JsonValueKind.String
                || rendered.GetString() is not { } renderedText
                || StrictUtf8.GetByteCount(renderedText)
                    > MaximumCheckpointDiffUtf8Bytes))
        {
            throw new InvalidDataException(
                "Hermes returned an invalid rendered checkpoint diff.");
        }

        if (success)
        {
            if (values.ContainsKey("error")
                || values.Count is not (4 or 5))
            {
                throw new InvalidDataException(
                    "Hermes returned a contradictory checkpoint diff receipt.");
            }
            var safe = JsonSerializer.SerializeToElement(new
            {
                success = true,
                stat,
                diff,
                truncated
            });
            return new ValidatedCheckpointDiffReceipt(
                true,
                stat,
                diff,
                truncated,
                safe);
        }

        _ = RequiredText(
            values,
            "error",
            MaximumCheckpointReceiptTextUtf8Bytes);
        if (values.Count != 5
            || values.ContainsKey("rendered")
            || stat.Length != 0
            || diff.Length != 0
            || truncated)
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory failed checkpoint diff receipt.");
        }
        var failed = JsonSerializer.SerializeToElement(new
        {
            success = false,
            stat = string.Empty,
            diff = string.Empty,
            truncated = false,
            confirmation_token = string.Empty
        });
        return new ValidatedCheckpointDiffReceipt(
            false,
            string.Empty,
            string.Empty,
            false,
            failed);
    }

    private static ValidatedCheckpointRestoreReceipt
        ValidateCheckpointRestoreResult(
            JsonElement result,
            string expectedOperationId,
            string expectedHash)
    {
        var values = ReadExactObject(result, new[]
        {
            "success", "operation_id", "replayed", "workspace_restored",
            "history_rewound", "history_removed", "partial", "restored_to",
            "reason", "pre_snapshot", "file", "error", "directory", "debug"
        });
        if (!TryRequiredBoolean(values, "success", out var success)
            || !TryRequiredBoolean(values, "replayed", out var replayed)
            || !TryRequiredBoolean(
                values,
                "workspace_restored",
                out var workspaceRestored)
            || !TryRequiredBoolean(
                values,
                "history_rewound",
                out var historyRewound)
            || !TryRequiredBoolean(values, "partial", out var partial))
        {
            throw new InvalidDataException(
                "Hermes returned a malformed checkpoint restore receipt.");
        }
        var historyRemoved = RequiredInteger(
            values,
            "history_removed",
            0,
            int.MaxValue);
        var operationId = RequiredText(
            values,
            "operation_id",
            MaximumCheckpointOperationIdUtf8Bytes);
        if (replayed
            || !IsCanonicalCheckpointOperationId(operationId)
            || !string.Equals(
                operationId,
                expectedOperationId,
                StringComparison.Ordinal)
            || values.ContainsKey("file"))
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched full-workspace restore receipt.");
        }

        var restoredTo = RequiredMultilineTextAllowEmpty(
            values,
            "restored_to",
            MaximumCheckpointReceiptTextUtf8Bytes);
        var reason = RequiredMultilineTextAllowEmpty(
            values,
            "reason",
            MaximumCheckpointReceiptTextUtf8Bytes);
        var error = RequiredMultilineTextAllowEmpty(
            values,
            "error",
            MaximumCheckpointReceiptTextUtf8Bytes);
        string? preSnapshot = null;
        if (values.ContainsKey("pre_snapshot"))
        {
            preSnapshot = RequiredMultilineTextAllowEmpty(
                values,
                "pre_snapshot",
                MaximumCheckpointReceiptTextUtf8Bytes);
            if (preSnapshot.Length != 0
                && !IsCanonicalCheckpointHashReference(preSnapshot))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid pre-restore checkpoint reference.");
            }
        }
        foreach (var property in new[] { "directory", "debug" })
            ValidateOptionalCheckpointReceiptText(values, property);

        CheckpointRestoreDisposition disposition;
        if (success
            && workspaceRestored
            && historyRewound
            && !partial
            && !string.IsNullOrWhiteSpace(restoredTo)
            && !string.IsNullOrWhiteSpace(reason)
            && error.Length == 0)
        {
            disposition = CheckpointRestoreDisposition.Succeeded;
        }
        else if (!success
                 && !workspaceRestored
                 && !historyRewound
                 && !partial
                 && historyRemoved == 0
                 && restoredTo.Length == 0
                 && reason.Length == 0
                 && !string.IsNullOrWhiteSpace(error))
        {
            disposition = CheckpointRestoreDisposition.NotMutated;
        }
        else if (!success
                 && workspaceRestored
                 && !historyRewound
                 && partial
                 && historyRemoved == 0
                 && !string.IsNullOrWhiteSpace(restoredTo)
                 && !string.IsNullOrWhiteSpace(reason)
                 && !string.IsNullOrWhiteSpace(error))
        {
            disposition = CheckpointRestoreDisposition.Partial;
        }
        else
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory checkpoint restore receipt.");
        }

        if (disposition is CheckpointRestoreDisposition.Succeeded
                or CheckpointRestoreDisposition.Partial
            && (!IsCanonicalCheckpointHashReference(restoredTo)
                || !expectedHash.StartsWith(
                    restoredTo,
                    StringComparison.Ordinal)
                || preSnapshot is null
                || !IsFullCheckpointHash(preSnapshot)))
        {
            throw new InvalidDataException(
                "Hermes restored a checkpoint other than the requested target.");
        }

        var safe = new JsonObject
        {
            ["success"] = success,
            ["restored_to"] = restoredTo,
            ["reason"] = reason,
            ["workspace_restored"] = workspaceRestored,
            ["history_rewound"] = historyRewound,
            ["history_removed"] = historyRemoved,
            ["partial"] = partial,
            ["error"] = error
        };
        return new ValidatedCheckpointRestoreReceipt(
            disposition,
            success,
            workspaceRestored,
            historyRewound,
            historyRemoved,
            partial,
            restoredTo,
            preSnapshot,
            JsonSerializer.SerializeToElement(safe));
    }

    private static bool IsCanonicalCheckpointHashReference(string value)
        => value.Length is 8 or CheckpointHashCharacters
            && value.All(static character => character is
                >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsFullCheckpointHash(string value)
        => value.Length == CheckpointHashCharacters
            && value.All(static character => character is
                >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string RequiredMultilineTextAllowEmpty(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Runtime result requires '{name}'.");
        }
        var text = value.GetString()!;
        if (text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime result parameter '{name}' was rejected.");
        }
        return text;
    }

    private static void ValidateOptionalCheckpointReceiptText(
        IReadOnlyDictionary<string, JsonElement> values,
        string property)
    {
        if (!values.ContainsKey(property))
            return;
        RequiredMultilineTextAllowEmpty(
            values,
            property,
            MaximumCheckpointReceiptTextUtf8Bytes);
    }

    private static void CopyOptionalCheckpointReceiptText(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonObject destination,
        string property)
    {
        if (values.TryGetValue(property, out var value))
            destination[property] = SafeMessage(value.GetString()!);
    }

    private static bool IsCanonicalCheckpointOperationId(string operationId)
    {
        if (operationId.Length is < 1 or > 128
            || !char.IsAsciiLetterOrDigit(operationId[0]))
        {
            return false;
        }
        return operationId.Skip(1).All(static character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or ':' or '-');
    }

    private bool IsCheckpointWorkspaceAttestedUnsafe(string sessionId)
        => string.Equals(_activeSessionId, sessionId, StringComparison.Ordinal)
            && _sessionStates.TryGetValue(sessionId, out var surface)
            && surface == SessionNativeSurfaceState.Attested
            && _checkpointWorkspaceStates.TryGetValue(
                sessionId,
                out var workspace)
            && workspace == CheckpointWorkspaceAttestationState.Attested;

    private void InvalidateCheckpointCatalogUnsafe()
    {
        _checkpointCatalog.Clear();
        _checkpointConfirmation = null;
        _checkpointCatalogRevision = _checkpointCatalogRevision == long.MaxValue
            ? 1
            : _checkpointCatalogRevision + 1;
    }

    private static Dictionary<string, JsonElement> ReadExactObject(
        JsonElement payload,
        IEnumerable<string> allowedProperties)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Runtime request parameters must be an object.");

        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException(
                    "Runtime request contained an unknown or duplicate parameter.");
            }
        }

        return values;
    }

    private static string RequiredSessionId(
        IReadOnlyDictionary<string, JsonElement> values)
        => RequiredText(
            values,
            "session_id",
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);

    private static string RequiredCheckpointHash(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var value = RequiredText(values, "hash", CheckpointHashCharacters);
        if (value.Length != CheckpointHashCharacters
            || value.Any(static character => character is not (
                >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                "Runtime checkpoint hash must be a full lowercase SHA-1 commit id.");
        }
        return value;
    }

    private static string? OptionalSessionId(
        IReadOnlyDictionary<string, JsonElement> values)
        => OptionalText(
            values,
            "session_id",
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);

    private static string? OptionalSessionId(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object
            || !values.TryGetProperty("session_id", out var session))
        {
            return null;
        }
        if (session.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Runtime session id was rejected.");
        var sessionId = session.GetString()!;
        ValidateText(
            sessionId,
            "session_id",
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
        return sessionId;
    }

    private static string RequiredText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Runtime request requires '{name}'.");
        }

        var text = value.GetString()!;
        ValidateText(text, name, maximumUtf8Bytes);
        return text;
    }

    private static string RequiredConversationText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes,
        bool allowBlank = false)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Runtime request requires '{name}'.");
        }

        var text = value.GetString()!;
        if ((!allowBlank && string.IsNullOrWhiteSpace(text))
            || text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return text;
    }

    private static string RequiredTrimmedText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        var text = RequiredText(values, name, maximumUtf8Bytes);
        if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' must not contain outer whitespace.");
        }
        return text;
    }

    private static string RequiredSensitiveTextAllowEmpty(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Runtime request requires '{name}'.");
        }
        var text = value.GetString()!;
        if (text.Any(char.IsControl)
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return text;
    }

    private static string? OptionalText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes)
    {
        if (!values.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Runtime request parameter '{name}' was rejected.");
        var text = value.GetString()!;
        ValidateText(text, name, maximumUtf8Bytes);
        return text;
    }

    private static void ValidateText(
        string text,
        string name,
        int maximumUtf8Bytes)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.Any(char.IsControl)
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
    }

    private static void OptionalBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (values.TryGetValue(name, out var value)
            && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return value.GetBoolean();
    }

    private static long RequiredInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        long minimum,
        long maximum)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var integer)
            || integer < minimum
            || integer > maximum)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return integer;
    }

    private static string RequiredMetadataText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumCharacters,
        bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Runtime request requires '{name}'.");
        }
        var text = value.GetString()!;
        if (maximumCharacters <= 0
            || (!allowEmpty && string.IsNullOrWhiteSpace(text))
            || text.Length > maximumCharacters
            || text.Any(char.IsControl)
            || StrictUtf8.GetByteCount(text) > maximumCharacters * 4)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return text;
    }

    private static void OptionalInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int minimum,
        int maximum)
    {
        if (!values.TryGetValue(name, out var value))
            return;
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var integer)
            || integer < minimum
            || integer > maximum)
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
    }

    private static Guid RequiredCaptureId(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || !Guid.TryParseExact(text, "N", out var captureId)
            || captureId == Guid.Empty
            || !string.Equals(
                text,
                captureId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Runtime request parameter '{name}' was rejected.");
        }
        return captureId;
    }

    private static string ResultSessionId(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("session_id", out var session)
            || session.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes did not return a canonical runtime session id.");
        }

        var sessionId = session.GetString()!;
        ValidateText(
            sessionId,
            "session_id",
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
        return sessionId;
    }

    private static string? OptionalResultStoredSessionId(
        string method,
        JsonElement result)
    {
        var property = string.Equals(method, "session.create", StringComparison.Ordinal)
            ? "stored_session_id"
            : "session_key";
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty(property, out var stored))
        {
            return null;
        }
        if (stored.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed durable session id.");
        }
        var storedSessionId = stored.GetString()!;
        ValidateText(
            storedSessionId,
            property,
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
        if (string.Equals(method, "session.resume", StringComparison.Ordinal)
            && result.TryGetProperty("resumed", out var resumed)
            && (resumed.ValueKind != JsonValueKind.String
                || !string.Equals(
                    resumed.GetString(),
                    storedSessionId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Hermes resumed a different durable session id.");
        }
        return storedSessionId;
    }

    internal static LilithGameSessionLineageAttestation DecodeGameDurableLineage(
        string requestedStoredSessionId,
        string resolvedStoredSessionId,
        JsonElement result)
    {
        try
        {
            ValidateText(
                requestedStoredSessionId,
                "requested_session_id",
                LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
            ValidateText(
                resolvedStoredSessionId,
                "resolved_session_id",
                LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
            if (result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("durable_lineage", out var lineage)
                || lineage.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Hermes omitted the durable game session lineage receipt.");
            }

            var properties = lineage.EnumerateObject().ToArray();
            var propertyNames = properties
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (properties.Length != 5
                || propertyNames.Count != 5
                || !propertyNames.SetEquals(new[]
                {
                    "requested_session_id",
                    "resolved_session_id",
                    "session_ids",
                    "source",
                    "archived"
                }))
            {
                throw new InvalidDataException(
                    "Hermes returned a non-canonical durable game session lineage receipt.");
            }

            var attestedRequested = RequiredLineageString(
                lineage,
                "requested_session_id");
            var attestedResolved = RequiredLineageString(
                lineage,
                "resolved_session_id");
            var source = RequiredLineageString(lineage, "source");
            if (!string.Equals(
                    attestedRequested,
                    requestedStoredSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    attestedResolved,
                    resolvedStoredSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(source, "lilith-game", StringComparison.Ordinal)
                || !lineage.TryGetProperty("archived", out var archived)
                || archived.ValueKind != JsonValueKind.True)
            {
                throw new InvalidDataException(
                    "Hermes durable game session lineage did not match the requested hidden session policy.");
            }

            if (!lineage.TryGetProperty("session_ids", out var sessionIdsValue)
                || sessionIdsValue.ValueKind != JsonValueKind.Array
                || sessionIdsValue.GetArrayLength() is <= 0
                    or > MaximumDurableLineageSessions)
            {
                throw new InvalidDataException(
                    "Hermes durable game session lineage length was rejected.");
            }

            var sessionIds = new List<string>(sessionIdsValue.GetArrayLength());
            var uniqueSessionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in sessionIdsValue.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Hermes durable game session lineage contained a malformed identity.");
                }
                var sessionId = value.GetString()!;
                ValidateText(
                    sessionId,
                    "session_ids",
                    LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
                if (!uniqueSessionIds.Add(sessionId))
                {
                    throw new InvalidDataException(
                        "Hermes durable game session lineage contained a cycle or duplicate identity.");
                }
                sessionIds.Add(sessionId);
            }

            if (!string.Equals(
                    sessionIds[0],
                    requestedStoredSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    sessionIds[^1],
                    resolvedStoredSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes durable game session lineage endpoints were rejected.");
            }

            return LilithGameSessionLineageAttestation.Create(
                attestedRequested,
                attestedResolved,
                sessionIds,
                source,
                archived: true);
        }
        catch (LilithGameSessionLineageRejectedException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new LilithGameSessionLineageRejectedException(
                exception.Message,
                exception);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException
                or FormatException or OverflowException)
        {
            throw new LilithGameSessionLineageRejectedException(
                "Hermes returned a malformed durable game session lineage receipt.",
                exception);
        }
    }

    private static string RequiredLineageString(
        JsonElement lineage,
        string property)
    {
        if (!lineage.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes durable game session lineage contained a malformed string.");
        }
        var text = value.GetString()!;
        ValidateText(
            text,
            property,
            LilithRuntimeProtocol.MaximumSessionIdUtf8Bytes);
        return text;
    }

    private void SetSessionState(
        string sessionId,
        SessionNativeSurfaceState state)
    {
        lock (_sessionLock)
            SetSessionStateUnsafe(sessionId, state);
    }

    private void SetSessionStateUnsafe(
        string sessionId,
        SessionNativeSurfaceState state)
    {
        if (state == SessionNativeSurfaceState.Rejected
            && _sessionImages.TryGetValue(sessionId, out var lifecycle)
            && lifecycle.State != SessionImageLifecycleState.Idle)
        {
            lifecycle.State = SessionImageLifecycleState.Uncertain;
        }
        if (_sessionStates.TryGetValue(sessionId, out var existing)
            && existing == SessionNativeSurfaceState.Rejected)
        {
            return;
        }
        _sessionStates[sessionId] = state;
        if (state == SessionNativeSurfaceState.Rejected)
        {
            _pendingApprovals.Remove(sessionId);
            _sessionModelObservations.Remove(sessionId);
            _checkpointWorkspaceStates[sessionId] =
                CheckpointWorkspaceAttestationState.Rejected;
            if (string.Equals(
                    _activeSessionId,
                    sessionId,
                    StringComparison.Ordinal))
            {
                if (_checkpointRestoreRequestInFlight)
                {
                    _checkpointRestoreState =
                        CheckpointRestoreState.Quarantined;
                }
                InvalidateCheckpointCatalogUnsafe();
            }
        }
    }

    private sealed record PendingNativeApproval(
        string PatternKey,
        IReadOnlyList<string> Choices)
    {
        internal bool InFlight { get; set; }
    }

    private sealed class PendingSessionActivation
    {
        internal string Method { get; }

        internal LilithRuntimeVoiceCaptureOwner? ReadyVoiceOwner { get; }

        internal string? RequestedStoredSessionId { get; }

        internal HermesGatewayEvent? BufferedSessionInfo { get; set; }

        internal string? RejectionMessage { get; set; }

        internal string? BoundSessionId { get; set; }

        internal bool ProjectionPending { get; set; }

        internal List<HermesGatewayEvent> DeferredEvents { get; } = new();

        internal PendingSessionActivation(
            string method,
            LilithRuntimeVoiceCaptureOwner? readyVoiceOwner,
            string? requestedStoredSessionId)
        {
            Method = method;
            ReadyVoiceOwner = readyVoiceOwner;
            RequestedStoredSessionId = requestedStoredSessionId;
        }
    }

    private sealed class LilithGameSessionLineageRejectedException :
        Exception
    {
        internal LilithGameSessionLineageRejectedException(string message)
            : base(message)
        {
        }

        internal LilithGameSessionLineageRejectedException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class LilithGameModelSelectionRequiredException :
        InvalidOperationException
    {
        internal LilithGameModelSelectionRequiredException()
            : base(
                "The hidden game conversation requires an exact Hermes native model selection.")
        {
        }
    }

    private sealed record CheckpointCatalogEntry(
        string Hash,
        string Timestamp,
        string Message);

    private sealed record ValidatedCheckpointListReceipt(
        IReadOnlyList<CheckpointCatalogEntry> Entries,
        JsonElement SafePayload);

    private sealed record ValidatedCheckpointDiffReceipt(
        bool Success,
        string Stat,
        string Diff,
        bool Truncated,
        JsonElement SafePayload);

    private sealed record PendingCheckpointConfirmation(
        string Token,
        Guid GenerationId,
        string SessionId,
        string Hash,
        long CatalogRevision,
        DateTimeOffset ExpiresUtc);

    private sealed record CheckpointRestoreReservation(
        string SessionId,
        string Hash,
        long CatalogRevision,
        string OperationId);

    private sealed record ValidatedCheckpointRestoreReceipt(
        CheckpointRestoreDisposition Disposition,
        bool Success,
        bool WorkspaceRestored,
        bool HistoryRewound,
        int HistoryRemoved,
        bool Partial,
        string RestoredTo,
        string? PreSnapshot,
        JsonElement SafePayload);

    private sealed record ObservedCheckpointRestoreEvent(
        bool Success,
        bool HistoryRewound,
        int HistoryRemoved,
        bool Partial,
        string RestoredTo,
        string? PreSnapshot);

    private sealed class SessionImageLifecycle
    {
        internal SessionImageLifecycleState State { get; set; } =
            SessionImageLifecycleState.Idle;
        internal TrackedImageAttachment? Attachment { get; set; }
        internal bool TerminalObservedBeforeReceipt { get; set; }
        internal bool InlineReceiptConfirmed { get; set; }
    }

    private sealed class SessionTurnCorrelation
    {
        internal bool AwaitingReceipt { get; set; }
        internal bool AwaitingNativeAssistantContinuationReceipt { get; set; }
        internal bool AwaitingQueuedTurn { get; set; }
        internal string PendingTurnId { get; set; } = string.Empty;
        internal string PendingUserMessageId { get; set; } = string.Empty;
        internal string PendingAssistantMessageId { get; set; } = string.Empty;
        internal HashSet<string> KnownTurnIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> ClaimedUserMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> ClaimedAssistantMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<(string TurnId, string MessageId)>
            ClaimedAuxiliaryAssistantMessageIds { get; } = new();
        internal Dictionary<
            (string TurnId, string MessageId),
            AuxiliaryAssistantClaim> ClaimedAuxiliaryAssistantClaims { get; } = new();
        internal HashSet<string> CompletedTurnIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> ActiveAssistantMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<(string TurnId, string MessageId)>
            ClaimedInterimAssistantMessageIds { get; } = new();
        internal Dictionary<string, string> LastInterimAssistantMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<string> TerminalErrorMarkerTurnIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> TerminalErrorMarkerMessageIds { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed record PendingCredentialModelRecovery(
        string SessionId, string ProviderId, string ModelId,
        long NativeConfigRevision, long RuntimeRevision)
    {
        internal bool FailureObserved { get; set; }
    }

    private sealed record AuxiliaryAssistantClaim(
        string MessageKind,
        string Text);

    private static LilithRuntimeResponse Success(string method, JsonElement payload)
        => new(method, succeeded: true, payload);

    private static LilithRuntimeResponse Failure(
        string method,
        string code,
        string message,
        int? hermesCode = null)
        => new(
            method,
            succeeded: false,
            JsonSerializer.SerializeToElement(new
            {
                code,
                message = SafeMessage(message),
                hermesCode
            }));

    private static string SafeMessage(string value)
    {
        var sanitized = new string((value ?? string.Empty)
            .Where(character => !char.IsControl(character) || character == ' ')
            .Take(1024)
            .ToArray());
        return sanitized.Length == 0
            ? "The Runtime request was rejected."
            : sanitized;
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private enum SessionNativeSurfaceState
    {
        Pending = 0,
        Attested = 1,
        Rejected = 2
    }

    private enum CheckpointWorkspaceAttestationState
    {
        Pending = 0,
        Attested = 1,
        Rejected = 2
    }

    private enum CheckpointRestoreState
    {
        Ready = 0,
        InFlight = 1,
        Quarantined = 2
    }

    private enum CheckpointRestoreDisposition
    {
        Succeeded = 0,
        NotMutated = 1,
        Partial = 2
    }

    private enum CheckpointRestoreCompletion
    {
        Accepted = 0,
        Partial = 1,
        Rejected = 2
    }

    private enum VoiceLifecycleState
    {
        Off = 0,
        Enabling = 1,
        Ready = 2,
        Starting = 3,
        Recording = 4,
        Stopping = 5,
        Transcribing = 6,
        Disabling = 7,
        Draining = 8,
        Quarantined = 9
    }

    private enum VoiceResultDisposition
    {
        Succeeded = 0,
        Busy = 1
    }

    private enum SessionImageLifecycleState
    {
        Idle = 0,
        AttachInFlight = 1,
        Attached = 2,
        SubmitAwaitingStart = 3,
        DetachInFlight = 4,
        Uncertain = 5
    }

    private enum PromptImageDisposition
    {
        AwaitInlineStart = 0,
        AwaitQueuedStart = 1,
        Consumed = 2
    }
}
