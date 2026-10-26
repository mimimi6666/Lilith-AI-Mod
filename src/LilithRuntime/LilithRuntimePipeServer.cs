using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LilithRuntimeContracts;
using ContractRuntimeState = LilithRuntimeContracts.LilithRuntimeState;

namespace LilithRuntime;

public sealed class LilithRuntimePipeServer :
    IAsyncDisposable,
    ILilithRuntimePipeRunner
{
    internal const string PinnedHermesVersion = "0.20.1";
    private const int MaximumGenerationRequestIds = 1_000_000;
    private static readonly TimeSpan ClientDisconnectVoiceCleanupTimeout =
        TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan CredentialMutationResponseFlushTimeout =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan CredentialMutationWriterShutdownTimeout =
        TimeSpan.FromSeconds(1);

    private readonly LilithRuntimeSnapshot _snapshot;
    private readonly LilithRuntimeRequestRouter _router;
    private readonly LilithDailyNoteApprovalBroker? _dailyApprovals;
    private readonly Func<LilithRuntimeState> _runtimeState;
    private readonly Action<Action<HermesGatewayEvent>> _subscribeGatewayEvents;
    private readonly Action<Action<HermesGatewayEvent>> _unsubscribeGatewayEvents;
    private readonly Func<NamedPipeServerStream> _createPipeServer;
    private readonly TimeSpan? _connectionDispatchDrainTimeout;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly HashSet<Guid> _requestIds = new();
    private PipeConnection? _activeConnection;
    private Task<LilithRuntimePipeRunDisposition>? _runTask;
    private long _outboundSequence;
    private int _credentialMutationRestartRequired;
    private int _lastConnectionDispatchDrainTimedOut;
    private bool _disposed;

    public LilithRuntimePipeServer(
        LilithRuntimeService runtime,
        LilithRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _snapshot = RequireReadySnapshot(snapshot);
        _dailyApprovals = runtime.DailyNoteApprovals;
        _runtimeState = () => runtime.State;
        _subscribeGatewayEvents = handler => runtime.GatewayEvent += handler;
        _unsubscribeGatewayEvents = handler => runtime.GatewayEvent -= handler;
        _createPipeServer = LilithRuntimePipeEndpoint.CreateCurrentUserServer;

        _router = new LilithRuntimeRequestRouter(
            runtime,
            snapshot.GenerationId,
            Path.Combine(snapshot.HermesHomePath, "workspace"),
            snapshot.Profile,
            CredentialMutationDispatched);
    }

    internal LilithRuntimePipeServer(
        LilithRuntimeSnapshot snapshot,
        Func<Action<string>, LilithRuntimeRequestRouter> createRouter,
        Func<NamedPipeServerStream> createPipeServer,
        TimeSpan? connectionDispatchDrainTimeout = null,
        LilithDailyNoteApprovalBroker? dailyApprovals = null)
    {
        _snapshot = RequireReadySnapshot(snapshot);
        _dailyApprovals = dailyApprovals;
        ArgumentNullException.ThrowIfNull(createRouter);
        _router = createRouter(CredentialMutationDispatched)
            ?? throw new ArgumentException(
                "The Runtime router factory returned no router.",
                nameof(createRouter));
        _runtimeState = () => _snapshot.State;
        _subscribeGatewayEvents = static _ => { };
        _unsubscribeGatewayEvents = static _ => { };
        _createPipeServer = createPipeServer
            ?? throw new ArgumentNullException(nameof(createPipeServer));
        if (connectionDispatchDrainTimeout.HasValue
            && connectionDispatchDrainTimeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionDispatchDrainTimeout));
        }
        _connectionDispatchDrainTimeout = connectionDispatchDrainTimeout;
    }

    private static LilithRuntimeSnapshot RequireReadySnapshot(
        LilithRuntimeSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.GenerationId == Guid.Empty
            || snapshot.State != LilithRuntimeState.Ready
            || !snapshot.GatewayReady)
        {
            throw new ArgumentException(
                "A ready Runtime snapshot is required.",
                nameof(snapshot));
        }
        return snapshot;
    }

    public Task<LilithRuntimePipeRunDisposition> RunAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateLock)
        {
            if (_runTask is not null)
                throw new InvalidOperationException(
                    "The Lilith Runtime pipe server has already been started.");
            _runTask = RunCoreAsync(cancellationToken);
            return _runTask;
        }
    }

    internal bool LastConnectionDispatchDrainTimedOut
        => Volatile.Read(ref _lastConnectionDispatchDrainTimedOut) == 1;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();

        PipeConnection? active;
        Task<LilithRuntimePipeRunDisposition>? runTask;
        lock (_stateLock)
        {
            active = _activeConnection;
            runTask = _runTask;
        }
        active?.Abort();
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
    }

    private async Task<LilithRuntimePipeRunDisposition> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        _subscribeGatewayEvents(RuntimeGatewayEvent);
        if (_dailyApprovals is not null) _dailyApprovals.UiEvent += DailyUiEvent;
        try
        {
            while (!linked.IsCancellationRequested)
            {
                await using var pipe = _createPipeServer();
                try
                {
                    await pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }

                var connection = new PipeConnection(
                    pipe,
                    _snapshot.GenerationId,
                    _router,
                    TryClaimClientRequestId,
                    NextServerRequestId,
                    NextOutboundSequence,
                    CredentialMutationRestartIsRequired,
                    CompleteCredentialMutationGeneration,
                    _connectionDispatchDrainTimeout,
                    dailyApprovals: _dailyApprovals);
                var hello = BuildHello();
                var status = BuildStatus();
                lock (_stateLock)
                {
                    _activeConnection = connection;
                    connection.Enqueue(
                        NextServerRequestId(),
                        hello);
                    connection.Enqueue(
                        NextServerRequestId(),
                        status);
                }

                try
                {
                    await connection.RunAsync(linked.Token).ConfigureAwait(false);
                }
                catch (Exception)
                    when (CredentialMutationRestartIsRequired())
                {
                    // Once Hermes received a credential mutation, every
                    // transport/protocol teardown outcome belongs to the
                    // retiring generation. Never escape into a replacement
                    // accept or supersede the required rollover disposition.
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }
                catch (IOException)
                    when (ConnectionFailureAllowsReplacementClient(connection))
                {
                }
                catch (InvalidDataException)
                    when (ConnectionFailureAllowsReplacementClient(connection))
                {
                }
                finally
                {
                    connection.Abort();
                    if (connection.DispatchDrainTimedOut)
                    {
                        Interlocked.Exchange(
                            ref _lastConnectionDispatchDrainTimedOut,
                            1);
                    }
                    if (!CredentialMutationRestartIsRequired())
                    {
                        using var cleanup = new CancellationTokenSource(
                            ClientDisconnectVoiceCleanupTimeout);
                        try
                        {
                            await _router.HandleClientDisconnectedAsync(
                                cleanup.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (cleanup.IsCancellationRequested)
                        {
                            // A bounded disconnect cleanup may not hold the
                            // pipe generation open. Credential rollover skips
                            // this cleanup entirely because the whole router
                            // and Hermes process tree are being retired.
                        }
                    }
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_activeConnection, connection))
                            _activeConnection = null;
                    }
                }
                if (CredentialMutationRestartIsRequired())
                    break;
            }
        }
        catch (Exception) when (CredentialMutationRestartIsRequired())
        {
            // Pipe disposal and other connection-scope teardown faults cannot
            // override an already-dispatched credential mutation. The whole
            // generation is retired below instead of attempting replacement.
        }
        finally
        {
            _unsubscribeGatewayEvents(RuntimeGatewayEvent);
            if (_dailyApprovals is not null) _dailyApprovals.UiEvent -= DailyUiEvent;
            lock (_stateLock)
            {
                _activeConnection?.Abort();
                _activeConnection = null;
            }
        }

        return CredentialMutationRestartIsRequired()
            ? LilithRuntimePipeRunDisposition
                .CredentialMutationRestartRequired
            : LilithRuntimePipeRunDisposition.Shutdown;
    }

    private void CredentialMutationDispatched(string method)
    {
        if (!IsGenerationMutation(method))
            throw new InvalidDataException(
                "Only a credential or messaging mutation may request Runtime generation rollover.");
        Interlocked.Exchange(ref _credentialMutationRestartRequired, 1);
    }

    private static bool IsGenerationMutation(string method)
        => method is "model.replace_key" or "model.remove_key"
            or "provider.setup.save" or "provider.setup.remove"
            or "messaging.telegram.replace" or "messaging.telegram.remove";

    private bool CredentialMutationRestartIsRequired()
        => Volatile.Read(ref _credentialMutationRestartRequired) == 1;

    internal static bool ConnectionFailureAllowsReplacementClient(
        PipeConnection connection)
        => !connection.DispatchDrainTimedOut;

    private void CompleteCredentialMutationGeneration()
    {
        if (!CredentialMutationRestartIsRequired())
            throw new InvalidOperationException(
                "Runtime generation rollover was requested without a dispatched credential mutation.");
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // A late completion from a boundedly retired credential request
            // cannot revive or fault the already-disposed pipe generation.
        }
    }

    private void RuntimeGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        if (_dailyApprovals?.ObserveNativeEvent(gatewayEvent) == true) return;
        _router.ProjectGatewayEvent(gatewayEvent, EnqueueGatewayEvent);
    }

    private void EnqueueGatewayEvent(HermesGatewayEvent gatewayEvent)
    {
        lock (_stateLock)
        {
            if (_activeConnection is null)
                return;
            try
            {
                _activeConnection.Enqueue(
                    NextServerRequestId(),
                    new LilithRuntimeHermesEvent(
                        gatewayEvent.Type,
                        gatewayEvent.Parameters));
            }
            catch
            {
                // A frame that cannot be forwarded exactly must terminate this
                // projection instead of being truncated or silently skipped.
                _activeConnection.Abort();
            }
        }
    }

    private void DailyUiEvent(string type, System.Text.Json.JsonElement payload)
    {
        lock (_stateLock)
        {
            if (_activeConnection is null) return;
            try { _activeConnection.Enqueue(NextServerRequestId(), new LilithRuntimeHermesEvent(type, payload)); }
            catch { _activeConnection.Abort(); }
        }
    }

    internal static void EnqueueResponseAndActivationProjection(
        LilithRuntimeOutboundQueue outbound,
        LilithRuntimeRequestRouter router,
        Guid responseRequestId,
        LilithRuntimeRequest request,
        LilithRuntimeResponse response,
        Func<Guid> nextServerRequestId)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(nextServerRequestId);

        router.CompleteSessionActivationProjection(
            request.Method,
            response,
            gatewayEvents =>
            {
                var batch = new List<(Guid RequestId, LilithRuntimeMessage Message)>
                {
                    (responseRequestId, response)
                };
                foreach (var gatewayEvent in gatewayEvents)
                {
                    batch.Add((
                        nextServerRequestId(),
                        new LilithRuntimeHermesEvent(
                            gatewayEvent.Type,
                            gatewayEvent.Parameters)));
                }
                outbound.EnqueueBatch(batch);
            });
    }

    private LilithRuntimeHello BuildHello()
    {
        var capabilities = BuildCapabilities();
        return new LilithRuntimeHello(
            RuntimeVersion,
            PinnedHermesVersion,
            LilithRuntimeOptions.PinnedHermesCommit,
            capabilities,
            ready: _runtimeState() == LilithRuntimeState.Ready);
    }

    internal static string RuntimeVersion
        => typeof(LilithRuntimePipeServer)
            .Assembly
            .GetName()
            .Version?
            .ToString(3) ?? "0.0.0";

    internal static IReadOnlyList<string> BuildCapabilities()
        => LilithRuntimeRequestRouter.PublishedMethods
            .Concat(new[]
            {
                "hermes.events.lossless",
                "hermes.native-tools-config-v1",
                "native.effects.hermes-configured",
                "session.native-surface-attestation",
                "rollback.runtime-workspace-only",
                "identity.strict-lilith-package-v1"
                , LilithDailyNoteApprovalProtocol.PresenceMethod
                , LilithDailyNoteApprovalProtocol.RespondMethod
            })
            .ToArray();

    private LilithRuntimeStatus BuildStatus()
    {
        var state = _runtimeState();
        return new LilithRuntimeStatus(
            MapState(state),
            "runtime." + state.ToString().ToLowerInvariant(),
            string.Empty,
            _router.ActiveSessionId);
    }

    private bool TryClaimClientRequestId(Guid requestId)
    {
        lock (_stateLock)
        {
            return _requestIds.Count < MaximumGenerationRequestIds
                && _requestIds.Add(requestId);
        }
    }

    private Guid NextServerRequestId()
    {
        lock (_stateLock)
        {
            if (_requestIds.Count >= MaximumGenerationRequestIds)
                throw new InvalidDataException(
                    "Runtime generation request capacity was exhausted.");
            Guid requestId;
            do
            {
                requestId = Guid.NewGuid();
            }
            while (!_requestIds.Add(requestId));
            return requestId;
        }
    }

    private long NextOutboundSequence()
    {
        var sequence = Interlocked.Increment(ref _outboundSequence);
        if (sequence <= 0)
            throw new InvalidDataException(
                "Runtime outbound sequence capacity was exhausted.");
        return sequence;
    }

    private static ContractRuntimeState MapState(LilithRuntimeState state)
        => state switch
        {
            LilithRuntimeState.Starting => ContractRuntimeState.Starting,
            LilithRuntimeState.Ready => ContractRuntimeState.Ready,
            LilithRuntimeState.Stopping => ContractRuntimeState.Stopping,
            LilithRuntimeState.Stopped => ContractRuntimeState.Stopped,
            LilithRuntimeState.Faulted => ContractRuntimeState.Faulted,
            _ => ContractRuntimeState.Faulted
        };

    internal sealed class PipeConnection
    {
        private const int MaximumInFlightDispatches = 1024;
        internal static readonly TimeSpan DispatchDrainTimeout =
            TimeSpan.FromSeconds(2);
        private readonly NamedPipeServerStream _pipe;
        private readonly LilithRuntimeRequestRouter _router;
        private readonly LilithDailyNoteApprovalBroker? _dailyApprovals;
        private readonly Guid _dailyConnectionId = Guid.NewGuid();
        private readonly LilithRuntimeInboundRequestGuard _guard;
        private readonly Func<Guid> _nextServerRequestId;
        private readonly Func<long> _nextOutboundSequence;
        private readonly Func<bool> _credentialMutationRestartIsRequired;
        private readonly Action _completeCredentialMutationGeneration;
        private readonly TimeSpan _dispatchDrainTimeout;
        private readonly TimeSpan _emergencyBarrierWaitTimeout;
        private readonly LilithRuntimeOutboundQueue _outbound = new();
        private readonly CancellationTokenSource _abort = new();
        private readonly object _dispatchLock = new();
        private readonly HashSet<Task> _inFlightDispatches = new();
        private Task _ordinaryDispatchTail = Task.CompletedTask;
        private Task _emergencyBarrierTail = Task.CompletedTask;
        private Task _emergencyDispatchTail = Task.CompletedTask;
        private Exception? _dispatchFailure;
        private int _dispatchDrainTimedOut;

        internal PipeConnection(
            NamedPipeServerStream pipe,
            Guid generationId,
            LilithRuntimeRequestRouter router,
            Func<Guid, bool> tryClaimRequestId,
            Func<Guid> nextServerRequestId,
            Func<long> nextOutboundSequence,
            Func<bool> credentialMutationRestartIsRequired,
            Action completeCredentialMutationGeneration,
            TimeSpan? dispatchDrainTimeout = null,
            TimeSpan? emergencyBarrierWaitTimeout = null,
            LilithDailyNoteApprovalBroker? dailyApprovals = null)
        {
            _pipe = pipe;
            _router = router;
            _dailyApprovals = dailyApprovals;
            _guard = new LilithRuntimeInboundRequestGuard(
                generationId,
                tryClaimRequestId);
            _nextServerRequestId = nextServerRequestId;
            _nextOutboundSequence = nextOutboundSequence;
            _credentialMutationRestartIsRequired =
                credentialMutationRestartIsRequired;
            _completeCredentialMutationGeneration =
                completeCredentialMutationGeneration;
            _dispatchDrainTimeout = dispatchDrainTimeout
                ?? DispatchDrainTimeout;
            if (_dispatchDrainTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(dispatchDrainTimeout));
            _emergencyBarrierWaitTimeout = emergencyBarrierWaitTimeout
                ?? LilithRuntimeRequestRouter.EmergencyInterruptMutationWaitTimeout;
            if (_emergencyBarrierWaitTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(emergencyBarrierWaitTimeout));
            }
        }

        internal bool DispatchDrainTimedOut
            => Volatile.Read(ref _dispatchDrainTimedOut) == 1;

        internal static async Task<Exception?> AbortAndJoinWriterAsync(
            Task writer,
            Action abort,
            TimeSpan timeout)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(abort);
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            var completedBeforeAbort = writer.IsCompleted;
            abort();
            try
            {
                await writer.WaitAsync(timeout).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ObjectDisposedException) when (!completedBeforeAbort)
            {
                // Abort deliberately closes an outstanding async write/flush.
                // This is a joined writer, not a failure to complete teardown.
                // Faults that were already present before abort remain visible.
                return null;
            }
            catch (TimeoutException exception)
            {
                _ = writer.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted
                        | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return exception;
            }
            catch (Exception exception)
            {
                // The writer fault is observed here and returned so teardown
                // policy can preserve the authoritative generation outcome.
                return exception;
            }
        }

        internal void Enqueue(Guid requestId, LilithRuntimeMessage message)
            => _outbound.Enqueue(requestId, message);

        internal async Task RunAsync(CancellationToken cancellationToken)
        {
            _dailyApprovals?.ConnectUi(_dailyConnectionId);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _abort.Token);
            var writer = WriteLoopAsync(linked.Token);
            _ = writer.ContinueWith(
                _ =>
                {
                    try
                    {
                        linked.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.NotOnRanToCompletion
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            try
            {
                while (!linked.IsCancellationRequested && _pipe.IsConnected)
                {
                    var envelope = await LilithRuntimeFrameCodec.ReadAsync(
                        _pipe,
                        linked.Token).ConfigureAwait(false);
                    var request = _guard.Accept(envelope);
                    if (_credentialMutationRestartIsRequired())
                    {
                        // A queued refresh/heartbeat must not abort the linked
                        // token of the mutation which is retiring this worker.
                        // That mutation may have persisted config but not yet
                        // written its durable receipt. Reject the new request,
                        // without dispatch or replay, and let the original
                        // bounded response/cutover path finish its transaction.
                        EnqueueRetiringGeneration(envelope.RequestId, request.Method);
                        continue;
                    }
                    if (TryHandleDailyPresence(envelope.RequestId, request)) continue;
                    Dispatch(
                        envelope.RequestId,
                        request,
                        linked.Token);
                }
            }
            finally
            {
                linked.Cancel();
                _dailyApprovals?.DisconnectUi(_dailyConnectionId);
                await DrainDispatchesAsync().ConfigureAwait(false);
                _outbound.Complete();
                // No teardown may wait forever for a client that stopped
                // reading. Dispose the pipe first so an outstanding async
                // write is broken, then join it only for a bounded interval.
                var writerFailure = await AbortAndJoinWriterAsync(
                    writer,
                    Abort,
                    CredentialMutationWriterShutdownTimeout).ConfigureAwait(false);
                if (writerFailure is not null
                    && !_credentialMutationRestartIsRequired()
                    && !DispatchDrainTimedOut)
                {
                    throw new IOException(
                        "Runtime pipe writer did not complete bounded teardown.",
                        writerFailure);
                }
            }

            Exception? dispatchFailure;
            lock (_dispatchLock)
                dispatchFailure = _dispatchFailure;
            if (dispatchFailure is not null
                && !_credentialMutationRestartIsRequired())
            {
                throw new IOException(
                    "Runtime pipe request dispatch failed.",
                    dispatchFailure);
            }
        }

        private static bool IsEmergencyInterrupt(LilithRuntimeRequest request)
            => string.Equals(
                request.Method,
                "session.interrupt",
                StringComparison.Ordinal);

        internal static bool CanEmergencyInterruptOvertake(
            LilithRuntimeRequest request)
            => LilithRuntimeEmergencyInterruptPolicy.CanOvertake(request.Method);

        private void Dispatch(
            Guid requestId,
            LilithRuntimeRequest request,
            CancellationToken cancellationToken)
        {
            Task dispatch;
            lock (_dispatchLock)
            {
                if (_inFlightDispatches.Count >= MaximumInFlightDispatches)
                {
                    throw new IOException(
                        "Runtime pipe request dispatch capacity was exhausted.");
                }

                if (IsEmergencyInterrupt(request))
                {
                    if (!_emergencyDispatchTail.IsCompleted)
                    {
                        dispatch = Task.Run(
                            () => EnqueueInterruptInFlight(requestId),
                            CancellationToken.None);
                    }
                    else
                    {
                        var barrier = Task.WhenAll(
                            _emergencyBarrierTail,
                            _emergencyDispatchTail);
                        dispatch = Task.Run(
                            async () =>
                            {
                                try
                                {
                                    await barrier.WaitAsync(
                                            _emergencyBarrierWaitTimeout,
                                            cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (TimeoutException)
                                {
                                    EnqueueInterruptDeferred(requestId);
                                    return;
                                }
                                cancellationToken.ThrowIfCancellationRequested();
                                await ProcessRequestAsync(
                                    requestId,
                                    request,
                                    cancellationToken).ConfigureAwait(false);
                            },
                            CancellationToken.None);
                        _emergencyDispatchTail = dispatch;
                    }
                }
                else
                {
                    var predecessor = _ordinaryDispatchTail;
                    var independentCatalogRead = LilithRuntimeCatalogReadPolicy.Contains(request.Method);
                    var emergencyPredecessor =
                        CanEmergencyInterruptOvertake(request)
                            ? Task.CompletedTask
                            : _emergencyDispatchTail;
                    dispatch = Task.Run(
                        async () =>
                        {
                            await predecessor.ConfigureAwait(false);
                            await emergencyPredecessor.ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                            await ProcessRequestAsync(
                                requestId,
                                request,
                                cancellationToken).ConfigureAwait(false);
                        },
                        CancellationToken.None);
                    // Catalogs wait for earlier mutations, but never become
                    // predecessors of later human replies or mutations. The
                    // router validates authority/scope again after the read.
                    if (!independentCatalogRead)
                        _ordinaryDispatchTail = dispatch;
                    if (!CanEmergencyInterruptOvertake(request))
                        _emergencyBarrierTail = dispatch;
                }
                _inFlightDispatches.Add(dispatch);
            }

            _ = dispatch.ContinueWith(
                completed => CompleteDispatch(completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void EnqueueInterruptInFlight(Guid requestId)
        {
            _outbound.Enqueue(
                requestId,
                new LilithRuntimeResponse(
                    "session.interrupt",
                    succeeded: false,
                    System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        code = "runtime.session_interrupt_in_flight",
                        message = "An exact stop request is already in flight.",
                        hermesCode = (int?)null
                    })));
        }

        private void EnqueueInterruptDeferred(Guid requestId)
        {
            _outbound.Enqueue(
                requestId,
                new LilithRuntimeResponse(
                    "session.interrupt",
                    succeeded: false,
                    System.Text.Json.JsonSerializer.SerializeToElement(new
                    {
                        code = "runtime.session_interrupt_deferred",
                        message = "A protected Runtime mutation did not finish within the bounded stop window; retry Stop.",
                        hermesCode = (int?)null
                    })));
        }

        private async Task ProcessRequestAsync(
            Guid requestId,
            LilithRuntimeRequest request,
            CancellationToken cancellationToken)
        {
            var isCredentialMutation = IsGenerationMutation(request.Method);
            LilithRuntimeResponse response;
            try
            {
                if (_dailyApprovals is not null && request.Method == LilithDailyNoteApprovalProtocol.RespondMethod)
                    response = await _router.RouteDailyApprovalAsync(_dailyConnectionId, _dailyApprovals,
                        LilithDailyNoteApprovalProtocol.ReadResponse(request.HermesPayload), cancellationToken).ConfigureAwait(false);
                else
                    response = await _router.RouteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch when (isCredentialMutation
                        && _credentialMutationRestartIsRequired())
            {
                _completeCredentialMutationGeneration();
                return;
            }

            if (!isCredentialMutation
                || !_credentialMutationRestartIsRequired())
            {
                EnqueueResponseAndActivationProjection(
                    _outbound,
                    _router,
                    requestId,
                    request,
                    response,
                    _nextServerRequestId);
                return;
            }

            Task written;
            try
            {
                written = _outbound.EnqueueWithAcknowledgement(
                    requestId,
                    response);
            }
            catch
            {
                _completeCredentialMutationGeneration();
                throw;
            }
            _ = await LilithRuntimeCredentialMutationCutover
                .AwaitResponseOrTimeoutAsync(
                    written,
                    _completeCredentialMutationGeneration,
                    CredentialMutationResponseFlushTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            // The canonical response has either been flushed to the client or
            // proved undeliverable. No ordinary request from this generation
            // may be dispatched after a credential mutation.
        }

        private void EnqueueRetiringGeneration(Guid requestId, string method)
        {
            _outbound.Enqueue(requestId, new LilithRuntimeResponse(method,
                succeeded: false, System.Text.Json.JsonSerializer.SerializeToElement(new
                {
                    code = "runtime.generation_retiring",
                    message = "This Runtime generation is completing a configuration change; the new request was not dispatched.",
                    hermesCode = (int?)null
                })));
        }

        private void CompleteDispatch(Task completed)
        {
            lock (_dispatchLock)
            {
                _inFlightDispatches.Remove(completed);
                if (completed.IsFaulted && _dispatchFailure is null)
                {
                    _dispatchFailure = completed.Exception?.GetBaseException()
                        ?? new IOException("Runtime pipe dispatch faulted.");
                }
            }
            if (completed.IsFaulted)
            {
                _ = completed.Exception;
                Abort();
            }
        }

        private bool TryHandleDailyPresence(Guid requestId, LilithRuntimeRequest request)
        {
            if (_dailyApprovals is null || request.Method != LilithDailyNoteApprovalProtocol.PresenceMethod) return false;
            // This is liveness metadata, not a model/native permission request.
            // Keep it independent of long-running foreground/approval RPCs.
            var presence = LilithDailyNoteApprovalProtocol.ReadPresence(request.HermesPayload);
            var receipt = _dailyApprovals.Presence(_dailyConnectionId, presence);
            _outbound.Enqueue(requestId, new LilithRuntimeResponse(request.Method, true,
                LilithDailyNoteApprovalProtocol.EncodePresenceReceipt(receipt)));
            return true;
        }

        private async Task DrainDispatchesAsync()
        {
            Task[] pending;
            lock (_dispatchLock)
                pending = _inFlightDispatches.ToArray();
            if (pending.Length == 0)
                return;
            try
            {
                await Task.WhenAll(pending)
                    .WaitAsync(_dispatchDrainTimeout)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                Interlocked.Exchange(ref _dispatchDrainTimedOut, 1);
                lock (_dispatchLock)
                    _dispatchFailure ??= exception;
            }
            catch
            {
                // CompleteDispatch records and observes the authoritative
                // failure. Connection teardown reports it after writer cleanup.
            }
        }

        internal void Abort()
        {
            if (_abort.IsCancellationRequested)
                return;
            _abort.Cancel();
            _dailyApprovals?.DisconnectUi(_dailyConnectionId);
            _outbound.Complete();
            try
            {
                _pipe.Dispose();
            }
            catch
            {
            }
        }

        private async Task WriteLoopAsync(CancellationToken cancellationToken)
        {
            await foreach (var item in _outbound.ReadAllAsync(cancellationToken))
            {
                var envelope = new LilithRuntimeEnvelope(
                    _guard.GenerationId,
                    item.RequestId,
                    _nextOutboundSequence(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    item.Message);
                try
                {
                    await LilithRuntimeFrameCodec.WriteAsync(
                        _pipe,
                        envelope,
                        cancellationToken).ConfigureAwait(false);
                    item.WriteAcknowledgement?.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    item.WriteAcknowledgement?.TrySetException(exception);
                    throw;
                }
            }
        }
    }
}

internal static class LilithRuntimeEmergencyInterruptPolicy
{
    internal static bool CanOvertake(string method)
        => method is "session.usage" or "learning.frames" or "model.options"
            or "tools.catalog" or "session.context_breakdown"
            or "model.credential_status" or "delegation.status"
            or HermesCapabilityCatalogContract.Method or HermesCommandCatalogContract.Method
            or "tools.settings.get" or HermesLatestUsageContract.GetMethod;
}

internal static class LilithRuntimeCredentialMutationCutover
{
    internal static async Task<bool> AwaitResponseOrTimeoutAsync(
        Task responseWriteAcknowledgement,
        Action completeCredentialMutationGeneration,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseWriteAcknowledgement);
        ArgumentNullException.ThrowIfNull(completeCredentialMutationGeneration);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        try
        {
            await responseWriteAcknowledgement
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            // Dispatch already made the old credential state uncertain. The
            // response flush receives only this bounded opportunity; success,
            // transport failure, cancellation, and timeout all retire G1.
            completeCredentialMutationGeneration();
        }
    }
}

internal sealed class LilithRuntimeOutboundQueue
{
    internal const int Capacity = 1024;

    private readonly Channel<LilithRuntimeOutboundBatch> _channel =
        Channel.CreateBounded<LilithRuntimeOutboundBatch>(
            new BoundedChannelOptions(Capacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    private readonly object _enqueueLock = new();
    private int _queuedMessageCount;

    internal void Enqueue(Guid requestId, LilithRuntimeMessage message)
        => EnqueueCore(requestId, message, writeAcknowledgement: null);

    internal Task EnqueueWithAcknowledgement(
        Guid requestId,
        LilithRuntimeMessage message)
    {
        var acknowledgement = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueCore(requestId, message, acknowledgement);
        return acknowledgement.Task;
    }

    internal void EnqueueBatch(
        IReadOnlyList<(Guid RequestId, LilithRuntimeMessage Message)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException(
                "Runtime outbound batch must not be empty.",
                nameof(items));
        var messages = new LilithRuntimeOutboundMessage[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            ValidateOutboundMessage(item.RequestId, item.Message);
            messages[index] = new LilithRuntimeOutboundMessage(
                item.RequestId,
                item.Message,
                WriteAcknowledgement: null);
        }
        EnqueueBatchCore(new LilithRuntimeOutboundBatch(messages));
    }

    private void EnqueueCore(
        Guid requestId,
        LilithRuntimeMessage message,
        TaskCompletionSource<bool>? writeAcknowledgement)
    {
        ValidateOutboundMessage(requestId, message);
        EnqueueBatchCore(
            new LilithRuntimeOutboundBatch(
            [
                new LilithRuntimeOutboundMessage(
                    requestId,
                    message,
                    writeAcknowledgement)
            ]));
    }

    private void EnqueueBatchCore(LilithRuntimeOutboundBatch batch)
    {
        lock (_enqueueLock)
        {
            if (batch.Messages.Count > Capacity - _queuedMessageCount
                || !_channel.Writer.TryWrite(batch))
            {
                throw new IOException(
                    "Runtime pipe output capacity was exhausted or closed.");
            }
            _queuedMessageCount += batch.Messages.Count;
        }
    }

    private static void ValidateOutboundMessage(
        Guid requestId,
        LilithRuntimeMessage message)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException(
                "Runtime outbound request id must not be empty.",
                nameof(requestId));
        ArgumentNullException.ThrowIfNull(message);
    }

    internal async IAsyncEnumerable<LilithRuntimeOutboundMessage> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            lock (_enqueueLock)
                _queuedMessageCount -= batch.Messages.Count;
            foreach (var message in batch.Messages)
                yield return message;
        }
    }

    internal void Complete() => _channel.Writer.TryComplete();
}

internal sealed record LilithRuntimeOutboundMessage(
    Guid RequestId,
    LilithRuntimeMessage Message,
    TaskCompletionSource<bool>? WriteAcknowledgement);

internal sealed record LilithRuntimeOutboundBatch(
    IReadOnlyList<LilithRuntimeOutboundMessage> Messages);

internal sealed class LilithRuntimeInboundRequestGuard
{
    private readonly Func<Guid, bool> _tryClaimRequestId;
    private long _nextSequence = 1;

    internal Guid GenerationId { get; }

    internal LilithRuntimeInboundRequestGuard(
        Guid generationId,
        Func<Guid, bool> tryClaimRequestId)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException(
                "Runtime generation must not be empty.",
                nameof(generationId));
        GenerationId = generationId;
        _tryClaimRequestId = tryClaimRequestId
            ?? throw new ArgumentNullException(nameof(tryClaimRequestId));
    }

    internal LilithRuntimeRequest Accept(LilithRuntimeEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.GenerationId != GenerationId)
            throw new InvalidDataException(
                "Runtime request generation was rejected.");
        if (envelope.Message is not LilithRuntimeRequest request)
            throw new InvalidDataException(
                "Runtime pipe accepts request messages only.");
        if (envelope.Sequence != _nextSequence || envelope.Sequence == long.MaxValue)
            throw new InvalidDataException(
                "Runtime request sequence was rejected.");
        if (!_tryClaimRequestId(envelope.RequestId))
            throw new InvalidDataException(
                "Runtime request identity was replayed or capacity was exhausted.");

        _nextSequence++;
        return request;
    }
}
