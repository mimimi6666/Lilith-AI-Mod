using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;
using Microsoft.Win32.SafeHandles;

namespace LilithRuntime;

internal delegate ILilithGameBridgePeerLease? LilithGameBridgePeerAdmission(
    NamedPipeServerStream pipe);

internal interface ILilithGameBridgePeerLease : IDisposable
{
    uint ProcessId { get; }

    ulong ProcessCreationTime { get; }

    CancellationToken PeerExited { get; }

    bool IsValid();
}

internal sealed class LilithGameBridgeBooleanAdmissionLease :
    ILilithGameBridgePeerLease
{
    internal static LilithGameBridgeBooleanAdmissionLease Instance { get; } = new();

    public uint ProcessId => 0;

    public ulong ProcessCreationTime => 0;

    public CancellationToken PeerExited => CancellationToken.None;

    public bool IsValid() => true;

    public void Dispose()
    {
    }
}

internal sealed class LilithGameBridgePipeServer :
    ILilithRuntimePipeRunner,
    IAsyncDisposable
{
    private const int MaximumGenerationRequestIds = 1_000_000;
    internal const int DefaultMaximumActiveConnections = 8;

    internal static readonly TimeSpan DefaultHelloDeadline =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan DefaultRequestDeadline =
        TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultResponseDeadline =
        TimeSpan.FromSeconds(5);

    private readonly LilithRuntimeSnapshot _snapshot;
    private readonly ILilithGameHermesSessionHost _host;
    private readonly ILilithGameNotesHostBridge _notesHost;
    private readonly ILilithGameNoteInboxHostBridge _noteInboxHost;
    private readonly ILilithDailyNoteGameHost _dailyNoteHost;
    private readonly LilithPlayerDisplayNameSync? _playerNameSync;
    private readonly Func<NamedPipeServerStream> _createPipe;
    private readonly LilithGameBridgePeerAdmission _admitPeer;
    private readonly SemaphoreSlim _activeConnections;
    private readonly TimeSpan _helloDeadline;
    private readonly TimeSpan _requestDeadline;
    private readonly TimeSpan _responseDeadline;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly ConcurrentDictionary<Guid, byte> _requestIds = new();
    private long _nextConnectionId;
    private Task<LilithRuntimePipeRunDisposition>? _runTask;
    private bool _disposed;

    internal LilithGameBridgePipeServer(
        LilithRuntimeService runtime,
        LilithRuntimeSnapshot snapshot)
        : this(
            snapshot,
            new LilithGameHermesSessionHost(runtime, snapshot),
            LilithGameBridgeEndpoint.CreateCurrentUserServer,
            LilithGameBridgePeerIdentity.TryAcquireAdmissionLease,
            DefaultMaximumActiveConnections,
            DefaultHelloDeadline,
            DefaultRequestDeadline,
            DefaultResponseDeadline,
            new LilithGameNotesHostBridge(runtime, snapshot))
    {
        _noteInboxHost = new LilithGameNoteInboxHostBridge(runtime, snapshot, _host);
        _dailyNoteHost = runtime.Letters = new LilithLettersCoordinator(runtime, snapshot, (LilithGameHermesSessionHost)_host);
        _playerNameSync = new LilithPlayerDisplayNameSync(snapshot.HermesHomePath, snapshot.GenerationId);
    }

    internal LilithGameBridgePipeServer(
        LilithRuntimeSnapshot snapshot,
        ILilithGameHermesSessionHost host,
        Func<NamedPipeServerStream> createPipe,
        Func<NamedPipeServerStream, bool> admitPeer)
        : this(
            snapshot,
            host,
            createPipe,
            AdaptBooleanAdmission(admitPeer),
            DefaultMaximumActiveConnections,
            DefaultHelloDeadline,
            DefaultRequestDeadline,
            DefaultResponseDeadline)
    {
    }

    internal LilithGameBridgePipeServer(
        LilithRuntimeSnapshot snapshot,
        ILilithGameHermesSessionHost host,
        Func<NamedPipeServerStream> createPipe,
        LilithGameBridgePeerAdmission admitPeer,
        int maximumActiveConnections,
        TimeSpan helloDeadline,
        TimeSpan requestDeadline,
        TimeSpan responseDeadline,
        ILilithGameNotesHostBridge? notesHost = null,
        ILilithGameNoteInboxHostBridge? noteInboxHost = null,
        ILilithDailyNoteGameHost? dailyNoteHost = null,
        LilithPlayerDisplayNameSync? playerNameSync = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.GenerationId == Guid.Empty
            || snapshot.State != LilithRuntimeState.Ready
            || !snapshot.GatewayReady)
        {
            throw new ArgumentException(
                "A ready Runtime snapshot is required.",
                nameof(snapshot));
        }
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _notesHost = notesHost ?? LilithUnavailableGameNotesHostBridge.Instance;
        _noteInboxHost = noteInboxHost ?? LilithUnavailableGameNoteInboxHostBridge.Instance;
        _dailyNoteHost = dailyNoteHost ?? LilithUnavailableDailyNoteGameHost.Instance;
        _playerNameSync = playerNameSync;
        _createPipe = createPipe ?? throw new ArgumentNullException(nameof(createPipe));
        _admitPeer = admitPeer ?? throw new ArgumentNullException(nameof(admitPeer));
        if (maximumActiveConnections is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumActiveConnections));
        }
        RequireDeadline(helloDeadline, nameof(helloDeadline));
        RequireDeadline(requestDeadline, nameof(requestDeadline));
        RequireDeadline(responseDeadline, nameof(responseDeadline));
        _activeConnections = new SemaphoreSlim(
            maximumActiveConnections,
            maximumActiveConnections);
        _helloDeadline = helloDeadline;
        _requestDeadline = requestDeadline;
        _responseDeadline = responseDeadline;
    }

    public Task<LilithRuntimePipeRunDisposition> RunAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runTask is not null)
            throw new InvalidOperationException(
                "The game bridge pipe server has already been started.");
        _runTask = RunCoreAsync(cancellationToken);
        return _runTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        await _dailyNoteHost.DisposeAsync().ConfigureAwait(false);
        await _notesHost.DisposeAsync().ConfigureAwait(false);
        await _noteInboxHost.DisposeAsync().ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task<LilithRuntimePipeRunDisposition> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var connectionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(
            connectionLifetime.Token,
            _host.VoiceRecoveryRequested);
        try
        {
            while (!admission.IsCancellationRequested)
            {
                var pipe = _createPipe();
                try
                {
                    await pipe.WaitForConnectionAsync(admission.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (!_activeConnections.Wait(0))
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                var connectionId = Interlocked.Increment(ref _nextConnectionId);
                var task = AdmitAndHandleConnectionAsync(
                    pipe,
                    connectionLifetime.Token);
                if (!_connections.TryAdd(connectionId, task))
                {
                    throw new InvalidOperationException(
                        "Game bridge connection identity collided.");
                }
                _ = task.ContinueWith(
                    completedTask =>
                    {
                        _ = completedTask.Exception;
                        _connections.TryRemove(
                            connectionId,
                            out var removedTask);
                        _ = removedTask;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (admission.IsCancellationRequested)
        {
        }
        finally
        {
            if (_host.VoiceRecoveryRequired)
            {
                // Recovery may be requested by this generation's in-flight
                // Stop/Cancel request. Do not return a restart disposition
                // until the Host proves ACTIVE_PROCESS_ZERO and sentinel
                // retirement; active connections remain alive long enough to
                // flush their bounded terminal response afterward.
                await _host.VoiceRecoveryCompletion.ConfigureAwait(false);
            }
            var active = _connections.Values.ToArray();
            if (active.Length != 0)
            {
                try
                {
                    await Task.WhenAll(active).WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Generation shutdown owns the Hermes Job tree. A game
                    // client cannot extend Runtime shutdown indefinitely.
                }
            }
        }
        return _host.VoiceRecoveryRequired
            ? LilithRuntimePipeRunDisposition.VoiceRecoveryRestartRequired
            : LilithRuntimePipeRunDisposition.Shutdown;
    }

    private async Task AdmitAndHandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        ILilithGameBridgePeerLease? peerLease = null;
        try
        {
            // Admission may hash large installed files. Yield first so the
            // accept loop always creates the next listener; the semaphore
            // bounds the number of concurrent admission/connection tasks.
            await Task.Yield();
            try
            {
                peerLease = _admitPeer(pipe);
            }
            catch
            {
                // Admission is fail-closed per connection. A malformed
                // or racing peer must never stop the listener generation.
            }
            if (peerLease is null || !IsLeaseValid(peerLease))
                return;

            using (var connection = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                peerLease.PeerExited))
            {
                try
                {
                    if (!IsLeaseValid(peerLease))
                        return;
                    using (var hello = CreateDeadline(
                        connection.Token,
                        _helloDeadline))
                    {
                        await LilithGameBridgeProtocol.WriteAsync(
                            pipe,
                            new LilithGameBridgeHello(
                                _snapshot.GenerationId,
                                sequence: 1,
                                ready: true),
                            hello.Token).ConfigureAwait(false);
                    }

                    LilithGameBridgeFrame request;
                    using (var requestDeadline = CreateDeadline(
                        connection.Token,
                        _requestDeadline))
                    {
                        request = await LilithGameBridgeProtocol.ReadAsync(
                            pipe,
                            requestDeadline.Token).ConfigureAwait(false);
                    }
                    if (!IsLeaseValid(peerLease)
                        || request.GenerationId != _snapshot.GenerationId
                        || request.Sequence != 1
                        || request is not LilithGameBridgeClientRequest clientRequest
                        || !_requestIds.TryAdd(clientRequest.RequestId, 0)
                        || _requestIds.Count > MaximumGenerationRequestIds)
                    {
                        return;
                    }

                    // The fixed process handle, creation time and file handles
                    // are checked again at the last boundary before Host work.
                    if (!IsLeaseValid(peerLease))
                        return;
                    LilithGameBridgeFrame response;
                    using (var dispatchLifetime =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            connection.Token))
                    {
                        // A request owns this pipe until its one response. A
                        // zero-byte read now means the exact client abandoned
                        // the request; cancel only this dispatch so an admitted
                        // Hermes turn can reconcile through its own Stop path.
                        var disconnect = MonitorClientDisconnectAsync(
                            pipe,
                            dispatchLifetime);
                        try
                        {
                            response = await DispatchAsync(
                                clientRequest,
                                peerLease,
                                dispatchLifetime.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            dispatchLifetime.Cancel();
                            await disconnect.ConfigureAwait(false);
                        }
                    }
                    if (!IsLeaseValid(peerLease))
                        return;
                    using var responseDeadline = CreateDeadline(
                        connection.Token,
                        _responseDeadline);
                    await LilithGameBridgeProtocol.WriteAsync(
                        pipe,
                        response,
                        responseDeadline.Token).ConfigureAwait(false);
                    if (response is LilithGameBridgeDailyNotePollResponse { Command: { } dailyCommand })
                    {
                        // The daily client keeps this exact admitted connection
                        // as its execution lease. Generation/owner revocation
                        // closes the pipe; ordinary/manual exchanges are unchanged.
                        await HoldDailyDeliveryLeaseAsync(pipe, dailyCommand.Scope, peerLease,
                            connection.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        finally
        {
            peerLease?.Dispose();
            try
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            _activeConnections.Release();
        }
    }

    private static LilithGameBridgePeerAdmission AdaptBooleanAdmission(
        Func<NamedPipeServerStream, bool> admitPeer)
    {
        ArgumentNullException.ThrowIfNull(admitPeer);
        return pipe => admitPeer(pipe)
            ? LilithGameBridgeBooleanAdmissionLease.Instance
            : null;
    }

    private async Task HoldDailyDeliveryLeaseAsync(NamedPipeServerStream pipe,
        LilithDailyNoteScope scope, ILilithGameBridgePeerLease peerLease, CancellationToken cancellationToken)
    {
        var lease = _dailyNoteHost.GetDeliveryLeaseCancellation(scope,
            new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease);
        lifetime.CancelAfter(TimeSpan.FromSeconds(5));
        // No second command is admitted on this connection. EOF or any extra
        // byte retires it, as does the exact host-owned cancellation token.
        var probe = new byte[1];
        _ = await pipe.ReadAsync(probe.AsMemory(), lifetime.Token).ConfigureAwait(false);
    }

    private static bool IsLeaseValid(ILilithGameBridgePeerLease peerLease)
    {
        try
        {
            return peerLease.IsValid();
        }
        catch
        {
            return false;
        }
    }

    private static async Task MonitorClientDisconnectAsync(
        NamedPipeServerStream pipe,
        CancellationTokenSource dispatchLifetime)
    {
        var probe = new byte[1];
        try
        {
            _ = await pipe.ReadAsync(
                    probe.AsMemory(),
                    dispatchLifetime.Token)
                .ConfigureAwait(false);
            if (!dispatchLifetime.IsCancellationRequested)
                dispatchLifetime.Cancel();
        }
        catch (OperationCanceledException)
            when (dispatchLifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            if (!dispatchLifetime.IsCancellationRequested)
                dispatchLifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            if (!dispatchLifetime.IsCancellationRequested)
                dispatchLifetime.Cancel();
        }
    }

    private static CancellationTokenSource CreateDeadline(
        CancellationToken cancellationToken,
        TimeSpan deadline)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        source.CancelAfter(deadline);
        return source;
    }

    private static void RequireDeadline(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(name);
    }

    private async Task<LilithGameBridgeFrame> DispatchAsync(
        LilithGameBridgeClientRequest request,
        ILilithGameBridgePeerLease peerLease,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (request)
            {
                case LilithGameBridgePlayerNameRequest playerName:
                    cancellationToken.ThrowIfCancellationRequested();
                    return _playerNameSync?.Apply(playerName, peerLease)
                        ?? FailureResponse(playerName, "runtime.player_name_unavailable");
                case LilithGameBridgeRequest submit:
                {
                    var outcome = await _host.SubmitAsync(
                        submit.TurnId,
                        submit.Surface,
                        submit.Text,
                        submit.DisplayLocale,
                        submit.SpeechLocale,
                        submit.MiniGameContext,
                        submit.MiniGameDiagnostic,
                        submit.MiniGameAnswerRepair,
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeResponse(
                        _snapshot.GenerationId,
                        submit.RequestId,
                        sequence: 2,
                        submit.TurnId,
                        outcome.Succeeded,
                        outcome.UserCommitted,
                        outcome.Terminal,
                        outcome.AssistantText,
                        outcome.SpeechText,
                        outcome.Code,
                        outcome.SpeechEmotion,
                        outcome.MiniGameDiagnostic,
                        outcome.MiniGameEventReceipt);
                }
                case LilithGameBridgeHistoryRequest history:
                {
                    var page = await _host.ReadHistoryAsync(
                        history.Cursor,
                        history.PageSize,
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeHistoryResponse(
                        _snapshot.GenerationId,
                        history.RequestId,
                        sequence: 2,
                        page.Succeeded,
                        page.NextCursor,
                        page.HasMore,
                        page.Entries,
                        page.Code);
                }
                case LilithGameBridgeNotificationRequest notifications:
                {
                    var page = await _host.ReadNotificationsAsync(
                        notifications.AfterSequence,
                        notifications.WaitMilliseconds,
                        notifications.DisplayLocale,
                        notifications.SpeechLocale,
                        notifications.TailOnly,
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeNotificationResponse(
                        _snapshot.GenerationId,
                        notifications.RequestId,
                        sequence: 2,
                        page.Succeeded,
                        page.OldestAvailableSequence,
                        page.LatestSequence,
                        page.HasMore,
                        page.Entries,
                        page.Code);
                }
                case LilithGameBridgeInterruptRequest interrupt:
                {
                    var succeeded = await _host.InterruptAsync(
                        interrupt.TurnId,
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeInterruptResponse(
                        _snapshot.GenerationId,
                        interrupt.RequestId,
                        sequence: 2,
                        interrupt.TurnId,
                        succeeded,
                        succeeded ? "ok" : "runtime.turn_not_active");
                }
                case LilithGameBridgeNativeAssistantCommitRequest nativeCommit:
                {
                    var outcome = await _host.CommitNativeAssistantAsync(
                        nativeCommit.EventId,
                        nativeCommit.ProactiveDialogueEnabled,
                        nativeCommit.Perception,
                        cancellationToken,
                        nativeCommit.MiniGameContext).ConfigureAwait(false);
                    return new LilithGameBridgeNativeAssistantCommitResponse(
                        _snapshot.GenerationId,
                        nativeCommit.RequestId,
                        sequence: 2,
                        nativeCommit.EventId,
                        outcome.Accepted,
                        outcome.AlreadyCommitted,
                        outcome.Retryable,
                        outcome.Code,
                        outcome.MiniGameEventsPending,
                        outcome.MiniGameEventReceipt);
                }
                case LilithGameBridgeNoteDraftRequest draft:
                {
                    var outcome = await _host.GenerateNoteDraftAsync(
                        draft.RequestId, draft.EventId, draft.Category, draft.Topic,
                        draft.Context, draft.DisplayLocale, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new LilithGameBridgeNoteDraftResponse(
                        _snapshot.GenerationId, draft.RequestId, 2, draft.EventId,
                        outcome.State, outcome.Text);
                }
                case LilithGameBridgeForegroundProactiveRequest foregroundProactive:
                {
                    var outcome = await _host.RequestForegroundProactiveAsync(
                        foregroundProactive.OperationId,
                        foregroundProactive.ObservedAtUnixMilliseconds,
                        foregroundProactive.ApplicationId,
                        foregroundProactive.ApplicationDisplayName,
                        foregroundProactive.ApplicationCategory,
                        foregroundProactive.DisplayLocale,
                        cancellationToken,
                        foregroundProactive.MiniGameContext).ConfigureAwait(false);
                    return new LilithGameBridgeForegroundProactiveResponse(
                        _snapshot.GenerationId,
                        foregroundProactive.RequestId,
                        sequence: 2,
                        foregroundProactive.OperationId,
                        outcome.Accepted,
                        outcome.Succeeded,
                        outcome.State,
                        outcome.AlreadyProcessed,
                        outcome.Retryable,
                        outcome.Code,
                        outcome.MiniGameEventReceipt);
                }
                case LilithGameBridgeVoiceStartRequest voiceStart:
                {
                    var outcome = await _host.StartVoiceAsync(
                        voiceStart.CaptureId,
                        voiceStart.Language,
                        voiceStart.MaximumDurationMilliseconds,
                        new LilithGameVoicePeerIdentity(
                            peerLease.ProcessId,
                            peerLease.ProcessCreationTime,
                            peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeVoiceStartResponse(
                        _snapshot.GenerationId,
                        voiceStart.RequestId,
                        sequence: 2,
                        voiceStart.CaptureId,
                        outcome.Succeeded,
                        outcome.Code);
                }
                case LilithGameBridgeVoiceStopRequest voiceStop:
                {
                    var outcome = await _host.StopVoiceAsync(
                        voiceStop.CaptureId,
                        voiceStop.Disposition,
                        new LilithGameVoicePeerIdentity(
                            peerLease.ProcessId,
                            peerLease.ProcessCreationTime,
                            peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeVoiceStopResponse(
                        _snapshot.GenerationId,
                        voiceStop.RequestId,
                        sequence: 2,
                        voiceStop.CaptureId,
                        outcome.Succeeded,
                        outcome.Terminal,
                        outcome.Transcript,
                        outcome.Code);
                }
                case LilithGameBridgeMemoPollRequest memoPoll:
                {
                    var command = await _notesHost.PollAsync(
                        memoPoll.WaitMilliseconds,
                        new LilithGameNotesPeerIdentity(
                            peerLease.ProcessId,
                            peerLease.ProcessCreationTime,
                            peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeMemoPollResponse(
                        _snapshot.GenerationId,
                        memoPoll.RequestId,
                        sequence: 2,
                        succeeded: true,
                        command,
                        code: "ok");
                }
                case LilithGameBridgeMemoCompleteRequest memoComplete:
                {
                    var accepted = await _notesHost.CompleteAsync(
                        memoComplete.Result,
                        new LilithGameNotesPeerIdentity(
                            peerLease.ProcessId,
                            peerLease.ProcessCreationTime,
                            peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeMemoCompleteResponse(
                        _snapshot.GenerationId,
                        memoComplete.RequestId,
                        sequence: 2,
                        memoComplete.OperationId,
                        accepted,
                        accepted ? "ok" : "runtime.memo_completion_rejected");
                }
                case LilithGameBridgeDailyNoteOpportunityRequest dailyOpportunity:
                {
                    var accepted = await _dailyNoteHost.OfferAsync(dailyOpportunity.Opportunity,
                        new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeDailyNoteOpportunityResponse(_snapshot.GenerationId, dailyOpportunity.RequestId,
                        2, accepted, accepted ? "ok" : "runtime.daily_opportunity_rejected");
                }
                case LilithGameBridgeDailyNotePollRequest dailyPoll:
                {
                    var command = await _dailyNoteHost.PollAsync(dailyPoll.ProfileId, dailyPoll.GameEpoch, dailyPoll.WaitMilliseconds,
                        new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeDailyNotePollResponse(_snapshot.GenerationId, dailyPoll.RequestId,
                        2, true, command, "ok");
                }
                case LilithGameBridgeDailyNoteCompleteRequest dailyComplete:
                {
                    var accepted = await _dailyNoteHost.CompleteAsync(dailyComplete.Scope, dailyComplete.Result,
                        new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeDailyNoteCompleteResponse(_snapshot.GenerationId, dailyComplete.RequestId,
                        2, dailyComplete.Scope, accepted, accepted ? "ok" : "runtime.daily_completion_rejected");
                }
                case LilithGameBridgeNoteInboxPollRequest notePoll:
                {
                    var command = await _noteInboxHost.PollAsync(notePoll.WaitMilliseconds,
                        new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeNoteInboxPollResponse(_snapshot.GenerationId, notePoll.RequestId,
                        2, true, command, "ok");
                }
                case LilithGameBridgeNoteInboxCompleteRequest noteComplete:
                {
                    var accepted = await _noteInboxHost.CompleteAsync(noteComplete.Result,
                        new LilithGameNotesPeerIdentity(peerLease.ProcessId, peerLease.ProcessCreationTime, peerLease.PeerExited),
                        cancellationToken).ConfigureAwait(false);
                    return new LilithGameBridgeNoteInboxCompleteResponse(_snapshot.GenerationId, noteComplete.RequestId,
                        2, noteComplete.Result.OperationId, noteComplete.Result.DeliveryId, accepted,
                        accepted ? "ok" : "runtime.note_completion_rejected");
                }
                default:
                    throw new InvalidDataException(
                        "Game bridge request type was rejected.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LilithGameBridgeOperationException exception)
        {
            return FailureResponse(request, exception.Code);
        }
        catch
        {
            return FailureResponse(request, "runtime.game_bridge_failed");
        }
    }

    private LilithGameBridgeFrame FailureResponse(
        LilithGameBridgeClientRequest request,
        string code)
        => request switch
            {
                LilithGameBridgePlayerNameRequest playerName =>
                    new LilithGameBridgePlayerNameResponse(_snapshot.GenerationId,
                        playerName.RequestId, 2, playerName.GameEpoch,
                        playerName.ObservationSequence, false, 0),
                LilithGameBridgeRequest submit => new LilithGameBridgeResponse(
                    _snapshot.GenerationId,
                    submit.RequestId,
                    sequence: 2,
                    submit.TurnId,
                    succeeded: false,
                    userCommitted: false,
                    LilithGameBridgeTerminal.Failed,
                    string.Empty,
                    code),
                LilithGameBridgeHistoryRequest history =>
                    new LilithGameBridgeHistoryResponse(
                        _snapshot.GenerationId,
                        history.RequestId,
                        sequence: 2,
                        succeeded: false,
                        nextCursor: string.Empty,
                        hasMore: false,
                        Array.Empty<LilithGameBridgeHistoryEntry>(),
                        code),
                LilithGameBridgeNotificationRequest notifications =>
                    new LilithGameBridgeNotificationResponse(
                        _snapshot.GenerationId,
                        notifications.RequestId,
                        sequence: 2,
                        succeeded: false,
                        oldestAvailableSequence: 0,
                        latestSequence: 0,
                        hasMore: false,
                        Array.Empty<LilithGameBridgeNotificationEntry>(),
                        code),
                LilithGameBridgeInterruptRequest interrupt =>
                    new LilithGameBridgeInterruptResponse(
                        _snapshot.GenerationId,
                        interrupt.RequestId,
                        sequence: 2,
                        interrupt.TurnId,
                        succeeded: false,
                        code),
                LilithGameBridgeNativeAssistantCommitRequest nativeCommit =>
                    new LilithGameBridgeNativeAssistantCommitResponse(
                        _snapshot.GenerationId,
                        nativeCommit.RequestId,
                        sequence: 2,
                        nativeCommit.EventId,
                        accepted: false,
                        alreadyCommitted: false,
                        retryable:
                            LilithGameHermesSessionHost
                                .IsRetryableNativeAssistantCommitCode(code)
                            || string.Equals(
                                code,
                                "runtime.game_bridge_failed",
                                StringComparison.Ordinal),
                        code),
                LilithGameBridgeNoteDraftRequest draft =>
                    new LilithGameBridgeNoteDraftResponse(
                        _snapshot.GenerationId, draft.RequestId, 2, draft.EventId,
                        LilithGameBridgeNoteDraftState.Failed, string.Empty),
                LilithGameBridgeForegroundProactiveRequest foregroundProactive =>
                    new LilithGameBridgeForegroundProactiveResponse(
                        _snapshot.GenerationId,
                        foregroundProactive.RequestId,
                        sequence: 2,
                        foregroundProactive.OperationId,
                        accepted: false,
                        succeeded: false,
                        LilithGameBridgeForegroundProactiveState.Abandoned,
                        alreadyProcessed: false,
                        retryable: string.Equals(
                            code,
                            "runtime.game_turn_busy",
                            StringComparison.Ordinal)
                            || string.Equals(
                                code,
                                "runtime.game_bridge_failed",
                                StringComparison.Ordinal),
                        code),
                LilithGameBridgeVoiceStartRequest voiceStart =>
                    new LilithGameBridgeVoiceStartResponse(
                        _snapshot.GenerationId,
                        voiceStart.RequestId,
                        sequence: 2,
                        voiceStart.CaptureId,
                        succeeded: false,
                        code),
                LilithGameBridgeVoiceStopRequest voiceStop =>
                    new LilithGameBridgeVoiceStopResponse(
                        _snapshot.GenerationId,
                        voiceStop.RequestId,
                        sequence: 2,
                        voiceStop.CaptureId,
                        succeeded: false,
                        LilithGameBridgeTerminal.Failed,
                        string.Empty,
                        code),
                LilithGameBridgeMemoPollRequest memoPoll =>
                    new LilithGameBridgeMemoPollResponse(
                        _snapshot.GenerationId,
                        memoPoll.RequestId,
                        sequence: 2,
                        succeeded: false,
                        command: null,
                        code),
                LilithGameBridgeMemoCompleteRequest memoComplete =>
                    new LilithGameBridgeMemoCompleteResponse(
                        _snapshot.GenerationId,
                        memoComplete.RequestId,
                        sequence: 2,
                        memoComplete.OperationId,
                        accepted: false,
                        code),
                LilithGameBridgeDailyNoteOpportunityRequest dailyOpportunity =>
                    new LilithGameBridgeDailyNoteOpportunityResponse(_snapshot.GenerationId, dailyOpportunity.RequestId,
                        2, false, code),
                LilithGameBridgeDailyNotePollRequest dailyPoll =>
                    new LilithGameBridgeDailyNotePollResponse(_snapshot.GenerationId, dailyPoll.RequestId,
                        2, false, null, code),
                LilithGameBridgeDailyNoteCompleteRequest dailyComplete =>
                    new LilithGameBridgeDailyNoteCompleteResponse(_snapshot.GenerationId, dailyComplete.RequestId,
                        2, dailyComplete.Scope, false, code),
                LilithGameBridgeNoteInboxPollRequest notePoll =>
                    new LilithGameBridgeNoteInboxPollResponse(_snapshot.GenerationId, notePoll.RequestId,
                        2, false, null, code),
                LilithGameBridgeNoteInboxCompleteRequest noteComplete =>
                    new LilithGameBridgeNoteInboxCompleteResponse(_snapshot.GenerationId, noteComplete.RequestId,
                        2, noteComplete.Result.OperationId, noteComplete.Result.DeliveryId, false, code),
                _ => throw new InvalidDataException(
                    "Game bridge failure response type was rejected.")
            };
}

internal static class LilithGameBridgePeerIdentity
{
    internal const string TrustManifestPathEnvironmentVariable =
        LilithGameBridgeTrustManifest.ManifestPathEnvironmentVariable;
    internal const string TrustManifestSha256EnvironmentVariable =
        LilithGameBridgeTrustManifest.ManifestSha256EnvironmentVariable;
    internal const string TrustManifestSchema =
        LilithGameBridgeTrustManifest.Schema;

    private const int MaximumManifestBytes =
        LilithGameBridgeTrustManifest.MaximumManifestBytes;
    private const long MaximumTrustedFileBytes = 2L * 1024 * 1024 * 1024;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitTimeout = 0x00000102;
    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private static readonly AdmissionCache ProcessLifetimeCache = new();

    internal static ILilithGameBridgePeerLease? TryAcquireAdmissionLease(
        NamedPipeServerStream pipe)
    {
        var manifestPath = Environment.GetEnvironmentVariable(
            TrustManifestPathEnvironmentVariable);
        var manifestSha256 = Environment.GetEnvironmentVariable(
            TrustManifestSha256EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(manifestPath)
            || string.IsNullOrWhiteSpace(manifestSha256))
        {
            return null;
        }
        return ProcessLifetimeCache.TryAcquire(
            pipe,
            manifestPath,
            manifestSha256);
    }

    internal static ILilithGameBridgePeerLease? TryAcquireAdmissionLease(
        NamedPipeServerStream pipe,
        string manifestPath,
        string expectedManifestSha256)
        => ProcessLifetimeCache.TryAcquire(
            pipe,
            manifestPath,
            expectedManifestSha256);

    internal static AdmissionCache CreateAdmissionCacheForTesting()
        => new();

    internal static AdmissionCacheKey CreateAdmissionCacheKeyForTesting(
        string manifestPath,
        string manifestSha256,
        uint processId,
        ulong processCreationTime)
        => new(
            Path.GetFullPath(manifestPath).ToUpperInvariant(),
            manifestSha256.ToUpperInvariant(),
            processId,
            processCreationTime);

    internal sealed class AdmissionCache : IDisposable
    {
        private readonly object _sync = new();
        private readonly Dictionary<AdmissionCacheKey, SharedProcessAdmission>
            _entries = new();
        private readonly HashSet<SharedProcessAdmission> _pendingReleases = new();
        private long _trustMaterializationCount;
        private long _releasedEntryCount;
        private bool _disposed;

        internal long TrustMaterializationCount =>
            Interlocked.Read(ref _trustMaterializationCount);

        internal long ReleasedEntryCount =>
            Interlocked.Read(ref _releasedEntryCount);

        internal int ActiveEntryCount
        {
            get
            {
                lock (_sync)
                    return checked(_entries.Count + _pendingReleases.Count);
            }
        }

        internal ILilithGameBridgePeerLease? TryAcquire(
            NamedPipeServerStream pipe,
            string manifestPath,
            string expectedManifestSha256)
        {
            ArgumentNullException.ThrowIfNull(pipe);
            if (!OperatingSystem.IsWindows()
                || !pipe.IsConnected
                || !GetNamedPipeClientProcessId(
                    pipe.SafePipeHandle,
                    out var processId)
                || processId == 0
                || !IsSha256(expectedManifestSha256))
            {
                return null;
            }
            var normalizedManifestPath = NormalizeAbsoluteLocalPath(manifestPath);
            if (normalizedManifestPath is null)
                return null;

            SafeProcessHandle? process = null;
            PeerTrustBundle? trust = null;
            try
            {
                process = OpenProcess(
                    ProcessQueryLimitedInformation | Synchronize,
                    inheritHandle: false,
                    processId);
                if (process.IsInvalid
                    || !TryGetProcessCreationTime(
                        process,
                        out var creationTime))
                {
                    return null;
                }
                var processImage = QueryProcessImage(process);
                if (processImage is null)
                    return null;
                var key = new AdmissionCacheKey(
                    normalizedManifestPath.ToUpperInvariant(),
                    expectedManifestSha256.ToUpperInvariant(),
                    processId,
                    creationTime);

                while (true)
                {
                    SharedProcessAdmission? release = null;
                    ILilithGameBridgePeerLease? acquired = null;
                    var retryAfterRelease = false;
                    lock (_sync)
                    {
                        if (_disposed)
                            return null;
                        if (_entries.TryGetValue(key, out var existing))
                        {
                            if (existing.IsValid())
                            {
                                process!.Dispose();
                                process = null;
                                return new SharedProcessLease(existing);
                            }
                            _entries.Remove(key);
                            _pendingReleases.Add(existing);
                            release = existing;
                            retryAfterRelease = true;
                        }
                        else
                        {
                            trust = PeerTrustBundle.Open(
                                normalizedManifestPath,
                                expectedManifestSha256,
                                processImage);
                            if (!ProcessIsStillBound(
                                    process!,
                                    creationTime,
                                    trust.ExecutablePath)
                                || !trust.IsValid())
                            {
                                return null;
                            }
                            var created = new SharedProcessAdmission(
                                this,
                                key,
                                process!,
                                trust);
                            process = null;
                            trust = null;
                            _entries.Add(key, created);
                            Interlocked.Increment(ref _trustMaterializationCount);
                            if (!created.IsValid())
                            {
                                _entries.Remove(key);
                                _pendingReleases.Add(created);
                                release = created;
                            }
                            else
                            {
                                acquired = new SharedProcessLease(created);
                            }
                        }
                    }
                    // Disposing cancels peer-exit callbacks synchronously, so
                    // it must happen after the cache lock is released.
                    release?.Dispose();
                    if (retryAfterRelease)
                        continue;
                    return acquired;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                process?.Dispose();
                trust?.Dispose();
            }
        }

        private void ProcessExited(
            AdmissionCacheKey key,
            SharedProcessAdmission entry)
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var current)
                    && ReferenceEquals(current, entry))
                {
                    _entries.Remove(key);
                    _pendingReleases.Add(entry);
                }
            }
            entry.Dispose();
        }

        private void EntryReleased(SharedProcessAdmission entry)
        {
            lock (_sync)
            {
                _pendingReleases.Remove(entry);
                Interlocked.Increment(ref _releasedEntryCount);
            }
        }

        public void Dispose()
        {
            SharedProcessAdmission[] entries;
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                entries = _entries.Values.ToArray();
                _entries.Clear();
                foreach (var entry in entries)
                    _pendingReleases.Add(entry);
            }
            foreach (var entry in entries)
                entry.Dispose();
        }

        private sealed class SharedProcessAdmission : IDisposable
        {
            private readonly AdmissionCache _owner;
            private readonly SafeProcessHandle _process;
            private readonly PeerTrustBundle _trust;
            private readonly CancellationTokenSource _peerExited = new();
            private readonly CancellationToken _peerExitedToken;
            private readonly ProcessWaitHandle _processWaitHandle;
            private RegisteredWaitHandle? _registeredWait;
            private int _disposed;

            internal SharedProcessAdmission(
                AdmissionCache owner,
                AdmissionCacheKey key,
                SafeProcessHandle process,
                PeerTrustBundle trust)
            {
                _owner = owner;
                Key = key;
                _process = process;
                _trust = trust;
                _peerExitedToken = _peerExited.Token;
                _processWaitHandle = new ProcessWaitHandle(process);
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    _processWaitHandle,
                    static (state, timedOut) =>
                    {
                        if (!timedOut
                            && state is SharedProcessAdmission admission)
                        {
                            admission._owner.ProcessExited(
                                admission.Key,
                                admission);
                        }
                    },
                    this,
                    Timeout.Infinite,
                    executeOnlyOnce: true);
            }

            internal AdmissionCacheKey Key { get; }

            internal CancellationToken PeerExited => _peerExitedToken;

            internal bool IsValid()
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return false;
                try
                {
                    return _trust.IsValid()
                        && ProcessIsStillBound(
                            _process,
                            Key.ProcessCreationTime,
                            _trust.ExecutablePath);
                }
                catch
                {
                    return false;
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                try
                {
                    _peerExited.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                _ = _registeredWait?.Unregister(null);
                _registeredWait = null;
                _processWaitHandle.Dispose();
                _process.Dispose();
                _trust.Dispose();
                _peerExited.Dispose();
                _owner.EntryReleased(this);
            }
        }

        private sealed class SharedProcessLease : ILilithGameBridgePeerLease
        {
            private readonly SharedProcessAdmission _admission;
            private int _disposed;

            internal SharedProcessLease(SharedProcessAdmission admission)
            {
                _admission = admission;
            }

            public uint ProcessId => _admission.Key.ProcessId;

            public ulong ProcessCreationTime =>
                _admission.Key.ProcessCreationTime;

            public CancellationToken PeerExited => _admission.PeerExited;

            public bool IsValid()
                => Volatile.Read(ref _disposed) == 0
                    && _admission.IsValid();

            public void Dispose()
            {
                Interlocked.Exchange(ref _disposed, 1);
            }
        }
    }

    internal readonly record struct AdmissionCacheKey(
        string ManifestPath,
        string ManifestSha256,
        uint ProcessId,
        ulong ProcessCreationTime);

    internal static bool VerifyTrustManifestFiles(
        string manifestPath,
        string expectedManifestSha256)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var trust = PeerTrustBundle.Open(
                manifestPath,
                expectedManifestSha256);
            return trust.IsValid();
        }
        catch
        {
            return false;
        }
    }

    private static bool ProcessIsStillBound(
        SafeProcessHandle process,
        ulong expectedCreationTime,
        string expectedImage)
    {
        if (process.IsInvalid
            || process.IsClosed
            || WaitForSingleObject(process, 0) != WaitTimeout
            || !TryGetProcessCreationTime(process, out var creationTime)
            || creationTime != expectedCreationTime)
        {
            return false;
        }
        var image = QueryProcessImage(process);
        return image is not null
            && string.Equals(
                image,
                expectedImage,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProcessCreationTime(
        SafeProcessHandle process,
        out ulong creationTime)
    {
        creationTime = 0;
        if (!GetProcessTimes(
                process,
                out var creation,
                out _,
                out _,
                out _))
        {
            return false;
        }
        creationTime = ((ulong)creation.High << 32) | creation.Low;
        return creationTime != 0;
    }

    private static string? QueryProcessImage(SafeProcessHandle process)
    {
        var capacity = 32_768;
        var buffer = new StringBuilder(capacity);
        if (!QueryFullProcessImageName(
                process,
                flags: 0,
                buffer,
                ref capacity)
            || capacity <= 0)
        {
            return null;
        }
        return NormalizeAbsoluteLocalPath(buffer.ToString());
    }

    private static string? NormalizeAbsoluteLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 32_767
            || !Path.IsPathFullyQualified(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFinalHandlePath(string path)
    {
        const string extendedUnc = @"\\?\UNC\";
        const string extended = @"\\?\";
        if (path.StartsWith(extendedUnc, StringComparison.OrdinalIgnoreCase))
        {
            path = @"\\" + path[extendedUnc.Length..];
        }
        else if (path.StartsWith(extended, StringComparison.OrdinalIgnoreCase))
        {
            path = path[extended.Length..];
        }
        return Path.GetFullPath(path);
    }

    private static string GetFinalHandlePath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32_768);
        var length = GetFinalPathNameByHandle(
            handle,
            buffer,
            (uint)buffer.Capacity,
            flags: 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("A bound peer path could not be resolved.");
        return NormalizeFinalHandlePath(buffer.ToString());
    }

    private static string HashHandle(SafeFileHandle handle)
    {
        var length = RandomAccess.GetLength(handle);
        if (length < 0 || length > MaximumTrustedFileBytes)
            throw new InvalidDataException("A trusted peer file exceeded its bound.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        try
        {
            long offset = 0;
            while (offset < length)
            {
                var requested = (int)Math.Min(buffer.Length, length - offset);
                var read = RandomAccess.Read(
                    handle,
                    buffer.AsSpan(0, requested),
                    offset);
                if (read == 0)
                    throw new EndOfStreamException("A trusted peer file changed while reading.");
                hash.AppendData(buffer, 0, read);
                offset += read;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static byte[] ReadBoundedManifest(SafeFileHandle handle)
    {
        var length = RandomAccess.GetLength(handle);
        if (length is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("The peer trust manifest exceeded its bound.");
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = RandomAccess.Read(
                handle,
                bytes.AsSpan(offset),
                offset);
            if (read == 0)
                throw new EndOfStreamException("The peer trust manifest ended early.");
            offset += read;
        }
        return bytes;
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return true;
    }

    private static void RequireExactProperties(
        JsonElement value,
        params string[] expectedNames)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The peer trust object was rejected.");
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("The peer trust property was rejected.");
        }
        if (!seen.SetEquals(expected))
            throw new InvalidDataException("The peer trust property was missing.");
    }

    private static string RequiredString(
        JsonElement value,
        string name,
        int maximumLength)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("The peer trust string was rejected.");
        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
            throw new InvalidDataException("The peer trust string was rejected.");
        return text;
    }

    private static ParsedManifest ParseManifest(byte[] bytes)
    {
        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        var root = document.RootElement;
        RequireExactProperties(
            root,
            "schema",
            "gameRoot",
            "gameExecutable",
            "plugin");
        if (!string.Equals(
                RequiredString(root, "schema", 128),
                TrustManifestSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The peer trust schema was rejected.");
        }
        var gameRoot = NormalizeAbsoluteLocalPath(
            RequiredString(root, "gameRoot", 32_767))
            ?? throw new InvalidDataException("The game root was rejected.");
        var rootPath = Path.GetPathRoot(gameRoot)
            ?? throw new InvalidDataException("The game root was rejected.");
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(gameRoot),
                Path.TrimEndingDirectorySeparator(rootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A volume root cannot be a game root.");
        }

        var executable = ParseManifestFile(
            root.GetProperty("gameExecutable"),
            gameRoot);
        var plugin = ParseManifestFile(
            root.GetProperty("plugin"),
            gameRoot);
        if (string.Equals(
                executable.FullPath,
                plugin.FullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The executable and plugin identities must differ.");
        }
        return new ParsedManifest(gameRoot, executable, plugin);
    }

    private static ParsedManifestFile ParseManifestFile(
        JsonElement value,
        string gameRoot)
    {
        RequireExactProperties(value, "relativePath", "sha256");
        var relativePath = RequiredString(value, "relativePath", 1_024);
        if (Path.IsPathRooted(relativePath)
            || Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("A peer trust relative path was rejected.");
        }
        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.None);
        if (segments.Length == 0
            || segments.Any(static segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException("A peer trust relative path was rejected.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(gameRoot, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(gameRoot)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A peer trust relative path escaped its root.");
        var sha256 = RequiredString(value, "sha256", 64);
        if (!IsSha256(sha256))
            throw new InvalidDataException("A peer trust digest was rejected.");
        return new ParsedManifestFile(fullPath, sha256);
    }

    private static void OpenDirectoryChain(
        string directory,
        ICollection<BoundPath> boundPaths)
    {
        var fullDirectory = NormalizeAbsoluteLocalPath(directory)
            ?? throw new InvalidDataException("A peer directory was rejected.");
        var root = Path.GetPathRoot(fullDirectory)
            ?? throw new InvalidDataException("A peer directory root was rejected.");
        var current = root;
        boundPaths.Add(BoundPath.Open(current, isDirectory: true));
        var relative = Path.GetRelativePath(root, fullDirectory);
        if (relative == ".")
            return;
        foreach (var segment in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                throw new InvalidDataException("A peer directory escaped its root.");
            current = Path.Combine(current, segment);
            boundPaths.Add(BoundPath.Open(current, isDirectory: true));
        }
    }

    private sealed record ParsedManifest(
        string GameRoot,
        ParsedManifestFile Executable,
        ParsedManifestFile Plugin);

    private sealed record ParsedManifestFile(
        string FullPath,
        string Sha256);

    private sealed class PeerTrustBundle : IDisposable
    {
        private readonly IReadOnlyList<BoundPath> _boundPaths;
        private bool _disposed;

        private PeerTrustBundle(
            string executablePath,
            string pluginPath,
            IReadOnlyList<BoundPath> boundPaths)
        {
            ExecutablePath = executablePath;
            PluginPath = pluginPath;
            _boundPaths = boundPaths;
        }

        internal string ExecutablePath { get; }

        internal string PluginPath { get; }

        internal static PeerTrustBundle Open(
            string manifestPath,
            string expectedManifestSha256,
            string? requiredExecutablePath = null)
        {
            if (!IsSha256(expectedManifestSha256))
                throw new InvalidDataException("The configured manifest digest was rejected.");
            var fullManifestPath = NormalizeAbsoluteLocalPath(manifestPath)
                ?? throw new InvalidDataException("The configured manifest path was rejected.");
            var manifestDirectory = Path.GetDirectoryName(fullManifestPath)
                ?? throw new InvalidDataException("The configured manifest path was rejected.");
            var boundPaths = new List<BoundPath>();
            try
            {
                OpenDirectoryChain(manifestDirectory, boundPaths);
                var manifestFile = BoundPath.Open(
                    fullManifestPath,
                    isDirectory: false);
                boundPaths.Add(manifestFile);
                var manifestBytes = ReadBoundedManifest(manifestFile.Handle);
                var actualManifestSha256 = Convert.ToHexString(
                    SHA256.HashData(manifestBytes));
                if (!string.Equals(
                        actualManifestSha256,
                        expectedManifestSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The peer trust manifest digest changed.");
                }
                var manifest = ParseManifest(manifestBytes);
                if (requiredExecutablePath is not null
                    && !string.Equals(
                        requiredExecutablePath,
                        manifest.Executable.FullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The peer process image did not match the trusted game executable.");
                }

                OpenDirectoryChain(manifest.GameRoot, boundPaths);
                var pluginDirectory = Path.GetDirectoryName(
                    manifest.Plugin.FullPath)
                    ?? throw new InvalidDataException("The plugin path was rejected.");
                OpenDirectoryChain(pluginDirectory, boundPaths);
                var plugin = BoundPath.Open(
                    manifest.Plugin.FullPath,
                    isDirectory: false);
                boundPaths.Add(plugin);
                if (!string.Equals(
                        HashHandle(plugin.Handle),
                        manifest.Plugin.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The game plugin digest changed.");
                }

                var executableDirectory = Path.GetDirectoryName(
                    manifest.Executable.FullPath)
                    ?? throw new InvalidDataException("The executable path was rejected.");
                OpenDirectoryChain(executableDirectory, boundPaths);
                var executable = BoundPath.Open(
                    manifest.Executable.FullPath,
                    isDirectory: false);
                boundPaths.Add(executable);
                // D-055 owner-selected update compatibility: the live process
                // image must remain this exact held, non-reparse path, while
                // its manifest SHA-256 is recovery evidence rather than an
                // admission blocker. The Plugin digest remains authoritative.

                return new PeerTrustBundle(
                    manifest.Executable.FullPath,
                    manifest.Plugin.FullPath,
                    boundPaths.AsReadOnly());
            }
            catch
            {
                foreach (var boundPath in boundPaths.Reverse<BoundPath>())
                    boundPath.Dispose();
                throw;
            }
        }

        internal bool IsValid()
            => !_disposed && _boundPaths.All(static path => path.IsValid());

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var boundPath in _boundPaths.Reverse())
                boundPath.Dispose();
        }
    }

    private sealed class BoundPath : IDisposable
    {
        private readonly uint _volumeSerialNumber;
        private readonly ulong _fileIndex;
        private readonly bool _isDirectory;
        private readonly ulong _fileSize;
        private readonly ulong _lastWriteTime;

        private BoundPath(
            SafeFileHandle handle,
            uint volumeSerialNumber,
            ulong fileIndex,
            bool isDirectory,
            ulong fileSize,
            ulong lastWriteTime)
        {
            Handle = handle;
            _volumeSerialNumber = volumeSerialNumber;
            _fileIndex = fileIndex;
            _isDirectory = isDirectory;
            _fileSize = fileSize;
            _lastWriteTime = lastWriteTime;
        }

        internal SafeFileHandle Handle { get; }

        internal static BoundPath Open(string path, bool isDirectory)
        {
            var fullPath = NormalizeAbsoluteLocalPath(path)
                ?? throw new InvalidDataException("A bound peer path was rejected.");
            var flags = FileFlagOpenReparsePoint
                | (isDirectory ? FileFlagBackupSemantics : 0u);
            var desiredAccess = FileReadAttributes
                | (isDirectory ? 0u : GenericRead);
            var handle = CreateFile(
                fullPath,
                desiredAccess,
                FileShare.Read,
                IntPtr.Zero,
                OpenExisting,
                flags,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new IOException("A bound peer path could not be opened.");
            }
            try
            {
                if (!GetFileInformationByHandle(handle, out var information)
                    || (information.FileAttributes & FileAttributeReparsePoint) != 0
                    || ((information.FileAttributes & FileAttributeDirectory) != 0)
                        != isDirectory
                    || !string.Equals(
                        GetFinalHandlePath(handle),
                        fullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A bound peer path identity was rejected.");
                }
                var fileIndex = ((ulong)information.FileIndexHigh << 32)
                    | information.FileIndexLow;
                var fileSize = ((ulong)information.FileSizeHigh << 32)
                    | information.FileSizeLow;
                var lastWriteTime =
                    ((ulong)information.LastWriteTime.High << 32)
                    | information.LastWriteTime.Low;
                return new BoundPath(
                    handle,
                    information.VolumeSerialNumber,
                    fileIndex,
                    isDirectory,
                    fileSize,
                    lastWriteTime);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        internal bool IsValid()
        {
            if (Handle.IsInvalid
                || Handle.IsClosed
                || !GetFileInformationByHandle(Handle, out var information)
                || (information.FileAttributes & FileAttributeReparsePoint) != 0
                || ((information.FileAttributes & FileAttributeDirectory) != 0)
                    != _isDirectory)
            {
                return false;
            }
            var fileIndex = ((ulong)information.FileIndexHigh << 32)
                | information.FileIndexLow;
            if (information.VolumeSerialNumber != _volumeSerialNumber
                || fileIndex != _fileIndex)
            {
                return false;
            }
            if (_isDirectory)
                return true;
            var fileSize = ((ulong)information.FileSizeHigh << 32)
                | information.FileSizeLow;
            var lastWriteTime =
                ((ulong)information.LastWriteTime.High << 32)
                | information.LastWriteTime.Low;
            return fileSize == _fileSize
                && lastWriteTime == _lastWriteTime;
        }

        public void Dispose() => Handle.Dispose();
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        internal ProcessWaitHandle(SafeProcessHandle process)
        {
            SafeWaitHandle = new SafeWaitHandle(
                process.DangerousGetHandle(),
                ownsHandle: false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        internal uint Low;
        internal uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal NativeFileTime CreationTime;
        internal NativeFileTime LastAccessTime;
        internal NativeFileTime LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        SafeProcessHandle handle,
        uint milliseconds);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
}

internal sealed class LilithRuntimeCompositePipeRunner :
    ILilithRuntimePipeRunner,
    IAsyncDisposable
{
    private readonly ILilithRuntimePipeRunner _workbench;
    private readonly ILilithRuntimePipeRunner _game;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<LilithRuntimePipeRunDisposition>? _runTask;
    private bool _disposed;

    internal LilithRuntimeCompositePipeRunner(
        ILilithRuntimePipeRunner workbench,
        ILilithRuntimePipeRunner game)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    public Task<LilithRuntimePipeRunDisposition> RunAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runTask is not null)
            throw new InvalidOperationException(
                "The composite Runtime pipe runner has already been started.");
        _runTask = RunCoreAsync(cancellationToken);
        return _runTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_game is IAsyncDisposable game)
            await game.DisposeAsync().ConfigureAwait(false);
        if (_workbench is IAsyncDisposable workbench)
            await workbench.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async Task<LilithRuntimePipeRunDisposition> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        var workbenchTask = _workbench.RunAsync(linked.Token);
        var gameTask = _game.RunAsync(linked.Token);
        var completed = await Task.WhenAny(workbenchTask, gameTask)
            .ConfigureAwait(false);
        if (completed == gameTask
            && !linked.IsCancellationRequested)
        {
            var gameDisposition = await gameTask.ConfigureAwait(false);
            if (gameDisposition
                != LilithRuntimePipeRunDisposition
                    .VoiceRecoveryRestartRequired)
            {
                linked.Cancel();
                throw new LilithRuntimeException(
                    "The game bridge pipe server stopped unexpectedly.");
            }

            linked.Cancel();
            try
            {
                _ = await workbenchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            return gameDisposition;
        }

        var disposition = await workbenchTask.ConfigureAwait(false);
        linked.Cancel();
        try
        {
            _ = await gameTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        return disposition;
    }
}
