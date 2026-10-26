using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
    : ID046HermesWorkbenchProjection,
      ID054HermesCredentialProjection,
      ID052HermesTaskPinProjection
{
    // Runtime publishes its pipe only after its bounded 90-second startup and
    // native-surface attestation complete. The final Workbench connection is
    // the readiness probe, so leave a small attestation margin without opening
    // a competing probe client against the single-client current-user pipe.
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(20);
    // Global model selection is four bounded native phases (catalog, prepare,
    // read-back, commit), not a single RPC. Keep the correlated response alive
    // through their 4 x 30s budgets plus receipt/transport margin. This never
    // retries the mutation or admits an unverified result.
    private static readonly TimeSpan DefaultModelSelectionResponseTimeout =
        TimeSpan.FromSeconds(150);
    // A cold session.create/session.resume returns a lazy activation while
    // Hermes builds the native agent. Tool discovery and model metadata can
    // legitimately outlive the ordinary RPC deadline, so only the subsequent
    // no-effect session.info attestation receives this longer bounded wait.
    private static readonly TimeSpan DefaultSessionActivationTimeout =
        TimeSpan.FromSeconds(120);
    // Permanent approval is a bounded three-phase transaction: Hermes prepares
    // the config mutation, Runtime commits its durable receipt, then Hermes
    // releases the waiting action. Its transport deadline must outlive both
    // bounded Hermes RPCs; using the ordinary 20-second UI deadline can sever
    // the pipe while Runtime is still completing a valid transaction.
    private static readonly TimeSpan DefaultPermanentApprovalResponseTimeout =
        TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SessionCompressionTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ImageCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly ID046HermesWorkbenchRuntimeProtocol _protocol;
    private readonly ID046HermesRuntimePipeConnectionFactory _connectionFactory;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _responseTimeout;
    private readonly TimeSpan _defaultModelSelectionResponseTimeout;
    private readonly TimeSpan _sessionActivationTimeout;
    private readonly TimeSpan _permanentApprovalResponseTimeout;
    private readonly Func<
        D046HermesRollbackRestoreReceipt,
        D046HermesRollbackRestoreRequest,
        D046HermesRollbackRestoreReceipt> _rollbackRestoreReceiptValidator;
    private readonly Func<Task>? _beforeRollbackHistoryAdoption;
    private readonly Func<D046HermesRuntimeRequestBinding, Task>?
        _beforeSerializedRequestWrite;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    internal D046HermesReadDiagnostics ReadDiagnostics { get; } = new();
    private readonly SemaphoreSlim _voiceOperationGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<bool> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Guid, D046PendingRuntimeResponse> _pending = new();

    private ID046HermesRuntimePipeConnection? _connection;
    private Task? _receiveTask;
    private Task? _faultConnectionCleanupTask;
    private Task? _disposalTask;
    private Guid _runtimeGeneration;
    private long _lastInboundSequence;
    private long _nextOutboundSequence;
    private long _lastProjectionSequence;
    private bool _projectionCursorEstablished;
    private bool _canonicalInitialSnapshotInProgress;
    private bool _canonicalDeleteReplacementInProgress;
    private bool _canonicalAwaitingSessionSelection;
    private bool _helloReceived;
    private D046HermesVoiceCaptureState _voiceCaptureState =
        D046HermesVoiceCaptureState.Off;
    private long _voiceStatusVersion;
    private bool _voiceCaptureFrozen;
    private bool _voiceQuarantined;
    private string _voiceRuntimeGeneration = string.Empty;
    private string _voiceTaskId = string.Empty;
    private string _voiceSessionId = string.Empty;
    private bool _rollbackRestoreQuarantined;
    private bool _disposed;
    private Exception? _terminalFault;

    internal event EventHandler<D046HermesProjectionFault>? Faulted;

    internal D046HermesProjectionFault? TerminalFault
    {
        get
        {
            lock (_stateGate)
                return _terminalFault is null ? null
                    : D046HermesProjectionFault.FromException(_terminalFault);
        }
    }

    internal bool IsFaultedClosed
    {
        get
        {
            lock (_stateGate)
                return _terminalFault is not null;
        }
    }

    public D046HermesWorkbenchRuntimePipeProjection(
        ID046HermesWorkbenchRuntimeProtocol protocol)
        : this(
            protocol,
            new D046CurrentUserRuntimePipeConnectionFactory(),
            DefaultConnectTimeout,
            DefaultResponseTimeout,
            permanentApprovalResponseTimeout: DefaultPermanentApprovalResponseTimeout,
            sessionActivationTimeout: DefaultSessionActivationTimeout,
            defaultModelSelectionResponseTimeout: DefaultModelSelectionResponseTimeout)
    {
    }

    internal D046HermesWorkbenchRuntimePipeProjection(
        ID046HermesWorkbenchRuntimeProtocol protocol,
        ID046HermesRuntimePipeConnectionFactory connectionFactory,
        TimeSpan connectTimeout,
        TimeSpan responseTimeout,
        Func<
            D046HermesRollbackRestoreReceipt,
            D046HermesRollbackRestoreRequest,
            D046HermesRollbackRestoreReceipt>? rollbackRestoreReceiptValidator = null,
        Func<Task>? beforeRollbackHistoryAdoption = null,
        Func<D046HermesRuntimeRequestBinding, Task>?
            beforeSerializedRequestWrite = null,
        TimeSpan? permanentApprovalResponseTimeout = null,
        TimeSpan? sessionActivationTimeout = null,
        TimeSpan? defaultModelSelectionResponseTimeout = null)
    {
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
        if (string.IsNullOrWhiteSpace(protocol.RequiredCapability))
        {
            throw new ArgumentException(
                "The Runtime protocol capability must not be empty.",
                nameof(protocol));
        }
        if (protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
        {
            if (string.IsNullOrWhiteSpace(canonical.RequiredHermesCommit)
                || canonical.RequiredCapabilities is null
                || canonical.RequiredCapabilities.Count == 0
                || canonical.RequiredCapabilities.Any(string.IsNullOrWhiteSpace)
                || canonical.RequiredCapabilities.Distinct(StringComparer.Ordinal).Count()
                    != canonical.RequiredCapabilities.Count
                || !canonical.RequiredCapabilities.Contains(
                    protocol.RequiredCapability,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    "The canonical Runtime protocol identity is incomplete.",
                    nameof(protocol));
            }
        }
        if (connectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        if (responseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
        if (permanentApprovalResponseTimeout is { } approvalTimeout
            && approvalTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(permanentApprovalResponseTimeout));
        }
        if (sessionActivationTimeout is { } activationTimeout
            && activationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionActivationTimeout));
        }
        _connectTimeout = connectTimeout;
        if (defaultModelSelectionResponseTimeout is { } selectionTimeout
            && selectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultModelSelectionResponseTimeout));
        }
        _responseTimeout = responseTimeout;
        _defaultModelSelectionResponseTimeout =
            defaultModelSelectionResponseTimeout ?? responseTimeout;
        _sessionActivationTimeout = sessionActivationTimeout ?? responseTimeout;
        _permanentApprovalResponseTimeout =
            permanentApprovalResponseTimeout ?? responseTimeout;
        _rollbackRestoreReceiptValidator = rollbackRestoreReceiptValidator
            ?? ValidateRollbackRestoreReceipt;
        _beforeRollbackHistoryAdoption = beforeRollbackHistoryAdoption;
        _beforeSerializedRequestWrite = beforeSerializedRequestWrite;
    }

    public event EventHandler<D046HermesProjectionEvent>? EventReceived;

    public event EventHandler<D046HermesVoiceEvent>? VoiceEventReceived;

    internal Guid RuntimeGeneration
    {
        get
        {
            lock (_stateGate)
                return _runtimeGeneration;
        }
    }

    public Task PreconnectAsync(CancellationToken cancellationToken)
        => EnsureReadyAsync(cancellationToken);

    public async Task<D046HermesWorkbenchSnapshot> GetInitialSnapshotAsync(
        CancellationToken cancellationToken)
    {
        using var timing = ReadDiagnostics.Begin(D046HermesReadOperation.InitialSnapshot, cancellationToken);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing.Advance(D046HermesReadPhase.Readiness);
            if (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                lock (_stateGate)
                {
                    ThrowIfUnavailableLocked();
                    _canonicalInitialSnapshotInProgress = true;
                }
                try
                {
                    return timing.Success(await GetCanonicalInitialSnapshotAsync(
                        canonical,
                        cancellationToken,
                        timing).ConfigureAwait(false));
                }
                catch (D046HermesSessionPreflightException exception)
                {
                    RetainSessionlessInventoryState(exception);
                    throw;
                }
                finally
                {
                    lock (_stateGate)
                        _canonicalInitialSnapshotInProgress = false;
                }
            }
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            timing.Advance(D046HermesReadPhase.Response);
            return timing.Success(await SendSnapshotRequestAsync(
                () => _protocol.BindGetInitialSnapshot(),
                cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesWorkbenchSnapshot> OpenTaskAsync(
        string taskId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                return await ActivateCanonicalTaskAsync(
                    canonical,
                    canonical.BindOpenTask(taskId),
                    cancellationToken).ConfigureAwait(false);
            }
            return await SendSnapshotRequestAsync(
                () => _protocol.BindOpenTask(taskId),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesWorkbenchSnapshot> CreateTaskAsync(
        CancellationToken cancellationToken)
        => await CreateTaskCoreAsync(null, cancellationToken).ConfigureAwait(false);

    public async Task<D046HermesWorkbenchSnapshot> CreateTaskAsync(
        D046HermesModelOption selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return await CreateTaskCoreAsync(selection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<D046HermesWorkbenchSnapshot> CreateTaskCoreAsync(
        D046HermesModelOption? selection,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                return await CreateCanonicalTaskAsync(
                    canonical,
                    selection,
                    cancellationToken,
                    readyAlreadyEstablished: false,
                    contextSnapshot: null).ConfigureAwait(false);
            }
            if (selection is not null)
            {
                throw new NotSupportedException(
                    "Pre-session model selection requires the canonical Hermes protocol.");
            }
            return await SendSnapshotRequestAsync(
                () => _protocol.BindCreateTask(),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    async Task<D052HermesTaskPinReceipt>
        ID052HermesTaskPinProjection.SetTaskPinAsync(
            D052HermesTaskPinRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
                ThrowIfUnavailableLocked();
            var taskPinProtocol = _protocol as ID052HermesTaskPinProtocol
                ?? throw new NotSupportedException(
                    "Task pinning requires the canonical Hermes projection.");
            return taskPinProtocol.SetTaskPin(request);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesDeleteTaskReceipt> DeleteTaskAsync(
        D046HermesDeleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateTaskMutationRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var canonical = _protocol as ID046CanonicalHermesWorkbenchRuntimeProtocol
                ?? throw new NotSupportedException(
                    "Task deletion requires the canonical Hermes Runtime protocol.");
            var binding = canonical.BindDeleteTask(request);

            // The correlated delete decoder removes the selected task before
            // this caller resumes. Cover that receive-loop gap as well as the
            // following authoritative list/activation. Only global inventory
            // invalidations with no selected task may be coalesced below.
            lock (_stateGate)
                _canonicalDeleteReplacementInProgress = true;

            // Once dispatch starts, close/delete must run to a bounded receipt;
            // caller cancellation cannot safely turn it into a replayable command.
            D046HermesDeleteTaskReceipt receipt;
            var dispatchStarted = false;
            try
            {
                receipt = await SendRequestAsync(
                    binding,
                    (response, envelope) => ValidateDeleteTaskReceipt(
                        canonical.DecodeCanonicalDeleteTaskReceipt(
                            response,
                            envelope,
                            binding),
                        request),
                    CancellationToken.None,
                    validateStateLocked: () =>
                    {
                        ValidateBoundCommand(
                            request.RuntimeGeneration, request.TaskId,
                            request.SessionId, request.LastObservedSequence);
                        // Recheck the original intent after waiting for the write
                        // gate; never adopt a newer cursor or another target.
                        _ = canonical.BindDeleteTask(request);
                    },
                    dispatchStarting: () => dispatchStarted = true).ConfigureAwait(false);
            }
            catch (D046HermesPreDispatchRejectedException) when (!dispatchStarted)
            {
                throw;
            }
            catch (Exception exception)
            {
                FailClosed(exception);
                throw;
            }
            if (receipt.TransportUncertain)
            {
                var exception = new InvalidOperationException(
                    "The Hermes task-delete outcome is uncertain; reconnect before continuing.");
                FailClosed(exception);
                throw exception;
            }

            if (!receipt.Deleted)
            {
                if (!receipt.RecoveryRequired)
                    return receipt;
                try
                {
                    var recovered = await ActivateCanonicalTaskAsync(
                        canonical,
                        canonical.BindOpenTask(request.TaskId),
                        CancellationToken.None).ConfigureAwait(false);
                    return receipt with { Snapshot = recovered };
                }
                catch (Exception exception)
                {
                    FailClosed(exception);
                    throw;
                }
            }

            try
            {
                var listed = await SendCanonicalSnapshotStepAsync(
                    canonical,
                    canonical.BindListTasks(),
                    CancellationToken.None).ConfigureAwait(false);
                if (listed.Tasks.Any(task => string.Equals(
                        task.TaskId,
                        request.TaskId,
                        StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        "The deleted Hermes task was still present in the authoritative list.");
                }
                var snapshot = listed.Tasks.Count == 0
                    ? await CreateCanonicalTaskAsync(
                        canonical,
                        selection: null,
                        CancellationToken.None,
                        readyAlreadyEstablished: true,
                        contextSnapshot: listed).ConfigureAwait(false)
                    : await ActivateCanonicalTaskAsync(
                        canonical,
                        canonical.BindOpenTask(listed.Tasks[0].TaskId),
                        CancellationToken.None,
                        readyAlreadyEstablished: true).ConfigureAwait(false);
                return receipt with { Snapshot = snapshot };
            }
            catch (D046HermesSessionPreflightException exception)
            {
                RetainSessionlessInventoryState(exception);
                throw new D046HermesDeletedTaskRecoveryException(receipt, exception);
            }
            catch (Exception exception)
            {
                FailClosed(exception);
                throw new D046HermesDeletedTaskRecoveryException(receipt, exception);
            }
        }
        finally
        {
            lock (_stateGate)
                _canonicalDeleteReplacementInProgress = false;
            _operationGate.Release();
        }
    }

    public async Task<D046HermesRenameTaskReceipt> RenameTaskAsync(
        D046HermesRenameTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateTaskMutationRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.Title)
                || !string.Equals(request.Title, request.Title.Trim(), StringComparison.Ordinal)
                || request.Title.Any(char.IsControl))
            {
                throw new ArgumentException("Task title was rejected.", nameof(request));
            }
            var canonical = _protocol as ID046CanonicalHermesWorkbenchRuntimeProtocol
                ?? throw new NotSupportedException(
                    "Task rename requires the canonical Hermes Runtime protocol.");
            var binding = canonical.BindRenameTask(request);
            D046HermesRenameTaskReceipt receipt;
            var dispatchStarted = false;
            try
            {
                receipt = await SendRequestAsync(
                    binding,
                    (response, envelope) => ValidateRenameTaskReceipt(
                        canonical.DecodeCanonicalRenameTaskReceipt(
                            response,
                            envelope,
                            binding),
                        request),
                    CancellationToken.None,
                    validateStateLocked: () =>
                    {
                        ValidateBoundCommand(
                            request.RuntimeGeneration, request.TaskId,
                            request.SessionId, request.LastObservedSequence);
                        _ = canonical.BindRenameTask(request);
                    },
                    dispatchStarting: () => dispatchStarted = true).ConfigureAwait(false);
            }
            catch (D046HermesPreDispatchRejectedException) when (!dispatchStarted)
            {
                throw;
            }
            catch (Exception exception)
            {
                FailClosed(exception);
                throw;
            }
            if (receipt.TransportUncertain)
            {
                var exception = new InvalidOperationException(
                    "The Hermes task-title outcome is uncertain; reconnect before continuing.");
                FailClosed(exception);
                throw exception;
            }
            return receipt;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesSubmitReceipt> SubmitAsync(
        D046HermesSubmitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.Text))
                throw new ArgumentException("Message text must not be empty.", nameof(request));

            var binding = _protocol.BindSubmit(request);
            return await SendRequestAsync(
                binding,
                (response, context) => DecodeAndValidateSubmitReceipt(
                    response,
                    context,
                    binding),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesSubmitReceipt> SubmitWithImageAsync(
        D046HermesSubmitWithImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Submit);
        ArgumentNullException.ThrowIfNull(request.Image);
        var frozenBytes = request.Image.Content.ToArray();
        var frozenRequest = request with
        {
            Image = request.Image with { Content = frozenBytes }
        };
        var operationGateHeld = false;
        string? attachedPath = null;
        var promptDispatchStarted = false;
        try
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            operationGateHeld = true;
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                frozenRequest.Submit.RuntimeGeneration,
                frozenRequest.Submit.TaskId,
                frozenRequest.Submit.SessionId,
                frozenRequest.Submit.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(frozenRequest.Submit.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Image submission requires the canonical Hermes protocol.");
            }

            D046HermesImageAttachReceipt attachReceipt;
            try
            {
                var attachBinding = canonical.BindImageAttachBytes(frozenRequest);
                attachReceipt = await SendRequestAsync(
                    attachBinding,
                    (response, context) => canonical.DecodeCanonicalImageAttachReceipt(
                        response,
                        context,
                        attachBinding),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                FailClosed(exception);
                throw;
            }

            ValidateImageAttachReceipt(
                attachReceipt,
                frozenRequest.Submit,
                frozenRequest.Image.Content.Length);
            if (!attachReceipt.Attached)
            {
                return new D046HermesSubmitReceipt(
                    Accepted: false,
                    attachReceipt.Code,
                    attachReceipt.RuntimeGeneration,
                    attachReceipt.TaskId,
                    attachReceipt.SessionId,
                    TurnId: string.Empty,
                    UserMessageId: string.Empty,
                    AssistantMessageId: string.Empty);
            }

            attachedPath = attachReceipt.Path;
            if (attachReceipt.Count != 1)
            {
                var cleaned = await TryDetachImageAsync(
                    canonical,
                    frozenRequest.Submit,
                    attachedPath).ConfigureAwait(false);
                var exception = new InvalidDataException(
                    "Hermes reported an unexpected pending image count.");
                FailClosed(exception);
                if (!cleaned)
                {
                    throw new InvalidDataException(
                        "Hermes image cleanup failed after an unexpected count.",
                        exception);
                }
                throw exception;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var submitBinding = canonical.BindSubmit(frozenRequest.Submit);
                promptDispatchStarted = true;
                var receipt = await SendRequestAsync(
                    submitBinding,
                    (response, context) => DecodeAndValidateSubmitReceipt(
                        response,
                        context,
                        submitBinding),
                    cancellationToken).ConfigureAwait(false);
                if (receipt.Accepted)
                    return receipt with
                    {
                        Images = new[]
                        {
                            new D046HermesImageAttachment(attachedPath,
                                frozenRequest.Image.FileName, frozenRequest.Image.MediaType,
                                frozenBytes.LongLength,
                                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frozenBytes)))
                        }
                    };

                if (!await TryDetachImageAsync(
                        canonical,
                        frozenRequest.Submit,
                        attachedPath!).ConfigureAwait(false))
                {
                    var cleanupFailure = new InvalidDataException(
                        "Hermes rejected the prompt and image cleanup did not complete.");
                    FailClosed(cleanupFailure);
                    throw cleanupFailure;
                }
                attachedPath = null;
                return receipt;
            }
            catch (OperationCanceledException exception)
                when (!promptDispatchStarted)
            {
                if (!await TryDetachImageAsync(
                        canonical,
                        frozenRequest.Submit,
                        attachedPath!).ConfigureAwait(false))
                {
                    FailClosed(exception);
                }
                attachedPath = null;
                throw;
            }
            catch (Exception)
                when (!promptDispatchStarted && attachedPath is not null)
            {
                _ = await TryDetachImageAsync(
                    canonical,
                    frozenRequest.Submit,
                    attachedPath).ConfigureAwait(false);
                attachedPath = null;
                throw;
            }
            catch (OperationCanceledException exception)
            {
                // Once prompt.submit may have crossed the pipe, detaching can race
                // the native image claim. Preserve uncertainty and never replay.
                FailClosed(exception);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frozenBytes);
            if (operationGateHeld)
                _operationGate.Release();
        }
    }

    public async Task<D046HermesCommandReceipt> StopAsync(
        D046HermesStopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Stop is the emergency path for an in-flight turn. It must not queue
        // behind an ordinary snapshot, observability, or approval operation;
        // SendRequestAsync still serializes the pipe write itself.
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        ValidateBoundInterrupt(
            request.RuntimeGeneration,
            request.TaskId,
            request.SessionId,
            request.LastObservedSequence);
        if (string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            throw new ArgumentException(
                "Client request ID must not be empty.",
                nameof(request));
        }

        var binding = _protocol.BindStop(request);
        return await SendRequestAsync(
            binding,
            (response, context) => DecodeAndValidateReceipt(
                response,
                context,
                binding),
            cancellationToken).ConfigureAwait(false);
    }

    private void ValidateBoundInterrupt(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (!_projectionCursorEstablished
                || !string.Equals(
                    runtimeGeneration,
                    _runtimeGeneration.ToString("N"),
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(taskId)
                || string.IsNullOrWhiteSpace(sessionId))
            {
                throw new D046HermesPreDispatchRejectedException(
                    D046HermesPreDispatchRejectionReason.StaleTaskMapping,
                    "The Workbench task mapping changed before interrupt dispatch.");
            }
            if (lastObservedSequence < 0
                || lastObservedSequence > _lastProjectionSequence)
            {
                throw new D046HermesPreDispatchRejectedException(
                    D046HermesPreDispatchRejectionReason.StaleProjectionCursor,
                    "The Workbench interrupt cursor was invalid.");
            }
        }
    }

    public async Task<D046HermesApprovalResponseReceipt> RespondApprovalAsync(
        D046HermesApprovalResponseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (request.Choice is not ("once" or "session" or "always" or "deny"))
                throw new ArgumentException("Approval choice was rejected.", nameof(request));

            var binding = _protocol.BindApprovalResponse(request);
            return await SendRequestAsync(
                binding,
                (response, context) => ValidateApprovalReceipt(
                    _protocol.DecodeApprovalResponseReceipt(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    request),
                cancellationToken,
                responseTimeout: request.Choice == "always"
                    ? _permanentApprovalResponseTimeout
                    : null).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesClarificationResponseReceipt> RespondClarificationAsync(
        D046HermesClarificationResponseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId)
                || string.IsNullOrWhiteSpace(request.ClarificationId)
                || string.IsNullOrWhiteSpace(request.Answer))
            {
                throw new ArgumentException(
                    "Clarification response fields must not be empty.",
                    nameof(request));
            }
            var binding = _protocol.BindClarificationResponse(request);
            return await SendRequestAsync(
                binding,
                (response, context) => _protocol.DecodeClarificationResponseReceipt(
                    response,
                    context,
                    binding),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesSecureInputResponseReceipt> RespondSecureInputAsync(
        D046HermesSecureInputResponseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId)
                || string.IsNullOrWhiteSpace(request.RequestId)
                || !Enum.IsDefined(
                    typeof(D046HermesSecureInputKind),
                    request.InputKind))
            {
                throw new ArgumentException(
                    "Secure-input response fields were rejected.",
                    nameof(request));
            }
            var binding = _protocol.BindSecureInputResponse(request);
            return await SendRequestAsync(
                binding,
                (response, context) => _protocol.DecodeSecureInputResponseReceipt(
                    response,
                    context,
                    binding),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesCompressionReceipt> CompressAsync(
        D046HermesCompressionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Compression requires the canonical Hermes protocol.");
            }

            var binding = canonical.BindCompress(request);
            return await SendRequestAsync(
                binding,
                (response, context) => ValidateCompressionReceipt(
                    canonical.DecodeCanonicalCompressionReceipt(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    request),
                cancellationToken,
                SessionCompressionTimeout).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesModelCatalog> GetModelOptionsAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        using var timing = ReadDiagnostics.Begin(D046HermesReadOperation.SessionModels, cancellationToken);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing.Advance(D046HermesReadPhase.Readiness);
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            timing.Advance(D046HermesReadPhase.Response);
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Model options require the canonical Hermes protocol.");
            }

            var binding = canonical.BindModelOptions(taskId, sessionId);
            return timing.Success(await SendRequestAsync(
                binding,
                (response, context) => ValidateModelCatalog(
                    canonical.DecodeCanonicalModelCatalog(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    taskId,
                    sessionId),
                cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesModelCatalog> GetDefaultModelOptionsAsync(
        CancellationToken cancellationToken)
    {
        using var timing = ReadDiagnostics.Begin(D046HermesReadOperation.DefaultModels, cancellationToken);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing.Advance(D046HermesReadPhase.Readiness);
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            timing.Advance(D046HermesReadPhase.Response);
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Default model options require the canonical Hermes protocol.");
            }

            return timing.Success(await GetSessionlessModelOptionsCoreAsync(
                canonical,
                contextSnapshot: null,
                cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesModelSelectionReceipt> SelectModelAsync(
        D046HermesModelSelectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.ProviderId)
                || string.IsNullOrWhiteSpace(request.ModelId))
            {
                throw new ArgumentException(
                    "Provider and model must not be empty.",
                    nameof(request));
            }
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Model selection requires the canonical Hermes protocol.");
            }

            var binding = canonical.BindSelectModel(request);
            var receipt = await SendRequestAsync(
                binding,
                (response, context) => ValidateModelSelectionReceipt(
                    canonical.DecodeCanonicalModelSelectionReceipt(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    request),
                cancellationToken).ConfigureAwait(false);
            if (binding.RequiresCredentialRecoveryAttestation && receipt.Accepted)
            {
                try
                {
                    // A config.set receipt is not native-surface attestation. The
                    // matching ready session.info must arrive before history and
                    // normal commands are exposed again; never replay the setter.
                    await canonical.WaitForNoEffectSessionInfoAsync(
                            request.TaskId, request.SessionId, cancellationToken)
                        .WaitAsync(_sessionActivationTimeout, cancellationToken)
                        .ConfigureAwait(false);
                    var snapshot = await SendCanonicalSnapshotStepAsync(
                        canonical, canonical.BindCredentialRecoveryHistory(request),
                        cancellationToken, adoptSnapshot: true).ConfigureAwait(false);
                    var selected = snapshot.Tasks.Single(task => task.TaskId == request.TaskId);
                    if (!snapshot.NativeSurfaceAttested || snapshot.CredentialRecoveryAvailable
                        || selected.SessionId != request.SessionId
                        || selected.ProviderId != request.ProviderId
                        || selected.ModelDisplayName != request.ModelId)
                        throw new InvalidDataException("Recovered model was not authoritatively confirmed.");
                    receipt = receipt with { RecoverySnapshot = snapshot };
                }
                catch (Exception exception)
                {
                    FailClosed(exception);
                    throw;
                }
            }
            return receipt;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesDefaultModelSelectionReceipt>
        SelectDefaultModelAsync(
            D046HermesDefaultModelSelectionRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.ProviderId)
                || string.IsNullOrWhiteSpace(request.ModelId))
            {
                throw new ArgumentException(
                    "Provider and model must not be empty.",
                    nameof(request));
            }
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Default model selection requires the canonical Hermes protocol.");
            }

            var binding = canonical.BindSelectDefaultModel(request);
            return await SendRequestAsync(
                binding,
                (response, context) => ValidateDefaultModelSelectionReceipt(
                    canonical.DecodeCanonicalDefaultModelSelectionReceipt(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    request),
                cancellationToken,
                responseTimeout: _defaultModelSelectionResponseTimeout).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesNativeToolsCatalog> GetNativeToolsCatalogAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
        {
            throw new InvalidOperationException(
                "Native tools require the canonical Hermes protocol.");
        }

        var binding = canonical.BindNativeToolsCatalog(taskId, sessionId);
        return await SendCatalogReadAsync(
            binding,
            (response, context) => ValidateNativeToolsCatalog(
                canonical.DecodeCanonicalNativeToolsCatalog(
                    response,
                    context,
                    binding),
                context.GenerationId.ToString("N"),
                taskId,
                sessionId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<D046HermesNativeToolsSetReceipt> SetNativeToolsAsync(
        D046HermesNativeToolsSetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateBoundCommand(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.ClientRequestId)
                || request.EnabledToolsets is null)
            {
                throw new ArgumentException(
                    "Native tool update correlation was invalid.",
                    nameof(request));
            }
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Native tools require the canonical Hermes protocol.");
            }

            var binding = canonical.BindNativeToolsSet(request);
            return await SendRequestAsync(
                binding,
                (response, context) => ValidateNativeToolsSetReceipt(
                    canonical.DecodeCanonicalNativeToolsSetReceipt(
                        response,
                        context,
                        binding),
                    context.GenerationId.ToString("N"),
                    request),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesObservabilitySnapshot> GetObservabilityAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            if (_protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol canonical)
            {
                throw new InvalidOperationException(
                    "Native observability requires the canonical Hermes protocol.");
            }

            // Prefer the settled session.info snapshot. Some native resume
            // responses do not include usage, so an explicit manual refresh may
            // fall back once to session.usage. The operation remains idle-only
            // because that native method can also refresh account-credit data.
            var usage = canonical.ReadCurrentUsage(taskId, sessionId);
            if (!usage.IsAvailable)
            {
                var usageBinding = canonical.BindUsage(taskId, sessionId);
                usage = await SendRequestAsync(
                    usageBinding,
                    (response, envelope) => canonical.DecodeCanonicalUsage(
                        response,
                        envelope,
                        usageBinding),
                    cancellationToken).ConfigureAwait(false);
            }

            var contextBinding = canonical.BindContextBreakdown(taskId, sessionId);
            var context = await SendRequestAsync(
                contextBinding,
                (response, envelope) => canonical.DecodeCanonicalContextBreakdown(
                    response,
                    envelope,
                    contextBinding),
                cancellationToken).ConfigureAwait(false);

            var learningBinding = canonical.BindLearningFrames(taskId, sessionId);
            var learning = await SendRequestAsync(
                learningBinding,
                (response, envelope) => canonical.DecodeCanonicalLearningFrames(
                    response,
                    envelope,
                    learningBinding),
                cancellationToken).ConfigureAwait(false);

            string generation;
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                generation = _runtimeGeneration.ToString("N");
            }
            return ValidateObservabilitySnapshot(new D046HermesObservabilitySnapshot(
                generation,
                taskId,
                sessionId,
                usage,
                context,
                learning));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesDelegationSnapshot> GetDelegationStatusAsync(
        D046HermesDelegationStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateDelegationRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var canonical = RequireCanonicalDelegationProtocol();
            var binding = canonical.BindDelegationStatus(request);
            return await SendRequestAsync(
                binding,
                (response, envelope) => ValidateDelegationSnapshot(
                    canonical.DecodeCanonicalDelegationStatus(
                        response,
                        envelope,
                        binding),
                    request),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesSubagentSteerReceipt> SteerSubagentAsync(
        D046HermesSubagentSteerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateDelegationRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.SubagentId)
                || string.IsNullOrWhiteSpace(request.Text))
            {
                throw new ArgumentException(
                    "Subagent identity and steering text must not be empty.",
                    nameof(request));
            }
            var canonical = RequireCanonicalDelegationProtocol();
            var binding = canonical.BindSubagentSteer(request);
            return await SendRequestAsync(
                binding,
                (response, envelope) => ValidateSubagentSteerReceipt(
                    canonical.DecodeCanonicalSubagentSteerReceipt(
                        response,
                        envelope,
                        binding),
                    request),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesSubagentInterruptReceipt> InterruptSubagentAsync(
        D046HermesSubagentInterruptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateDelegationRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (string.IsNullOrWhiteSpace(request.SubagentId))
            {
                throw new ArgumentException(
                    "Subagent identity must not be empty.",
                    nameof(request));
            }
            var canonical = RequireCanonicalDelegationProtocol();
            var binding = canonical.BindSubagentInterrupt(request);
            return await SendRequestAsync(
                binding,
                (response, envelope) => ValidateSubagentInterruptReceipt(
                    canonical.DecodeCanonicalSubagentInterruptReceipt(
                        response,
                        envelope,
                        binding),
                    request),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesRollbackCatalog> ListRollbackCheckpointsAsync(
        D046HermesRollbackListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateRollbackRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var canonical = RequireCanonicalRollbackProtocol();
            var binding = canonical.BindRollbackList(request);
            var catalog = await ExecuteRollbackRequestAsync(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                binding,
                (response, envelope) => canonical.DecodeCanonicalRollbackCatalog(
                    response,
                    envelope,
                    binding),
                quarantineRestore: false,
                cancellationToken).ConfigureAwait(false);
            return ValidateRollbackCatalog(catalog, request);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesRollbackDiffReceipt> GetRollbackDiffAsync(
        D046HermesRollbackDiffRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateRollbackRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var canonical = RequireCanonicalRollbackProtocol();
            var binding = canonical.BindRollbackDiff(request);
            var receipt = await ExecuteRollbackRequestAsync(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                binding,
                (response, envelope) => canonical.DecodeCanonicalRollbackDiffReceipt(
                    response,
                    envelope,
                    binding),
                quarantineRestore: false,
                cancellationToken).ConfigureAwait(false);
            return ValidateRollbackDiffReceipt(receipt, request);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesRollbackRestoreReceipt> RestoreRollbackAsync(
        D046HermesRollbackRestoreRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            ValidateRollbackRequest(
                request.ClientRequestId,
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (!request.Confirm)
            {
                throw new D046HermesRollbackOperationException(
                    D046HermesRollbackFailureKind.StateConflict,
                    "rollback.confirmation_required",
                    "Rollback requires explicit confirmation before dispatch.",
                    transportUncertain: false);
            }
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfRollbackRestoreQuarantinedLocked();
            }

            var canonical = RequireCanonicalRollbackProtocol();
            var binding = canonical.BindRollbackRestore(request);
            var receipt = await ExecuteRollbackRequestAsync(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                binding,
                (response, envelope) =>
                    canonical.DecodeCanonicalRollbackRestoreReceipt(
                        response,
                        envelope,
                        binding),
                quarantineRestore: true,
                cancellationToken).ConfigureAwait(false);
            try
            {
                receipt = _rollbackRestoreReceiptValidator(receipt, request);
            }
            catch (Exception exception) when (exception is InvalidDataException
                                              or InvalidOperationException
                                              or ArgumentException)
            {
                var failure = QuarantineRollbackRestore(
                    D046HermesRollbackFailureKind.TransportUncertain,
                    "rollback.restore.post_mutation_validation_failed",
                    "The committed rollback receipt could not be adopted; reconnect.",
                    transportUncertain: true,
                    exception);
                FailClosed(failure);
                throw failure;
            }

            if (!receipt.Succeeded
                && (receipt.Partial
                    || (receipt.WorkspaceRestored && !receipt.HistoryRewound)))
            {
                throw QuarantineRollbackRestore(
                    D046HermesRollbackFailureKind.ProtocolRejected,
                    "rollback.restore.partial",
                    "Rollback restore reported a partial workspace mutation.",
                    transportUncertain: false,
                    innerException: null,
                    restoreReceipt: receipt);
            }

            if (receipt.Succeeded || receipt.HistoryRewound)
            {
                try
                {
                    var refresh = canonical.BindRollbackHistoryRefresh(request);
                    var snapshot = await SendCanonicalSnapshotStepAsync(
                        canonical,
                        refresh,
                        CancellationToken.None).ConfigureAwait(false);
                    if (_beforeRollbackHistoryAdoption is not null)
                    {
                        await _beforeRollbackHistoryAdoption()
                            .ConfigureAwait(false);
                    }
                    snapshot = ValidateAndAdoptSnapshot(
                        snapshot,
                        request.RuntimeGeneration);
                    receipt = receipt with
                    {
                        ThroughSequence = snapshot.ThroughSequence,
                        Snapshot = snapshot
                    };
                }
                catch (Exception exception) when (exception is IOException
                                                  or TimeoutException
                                                  or InvalidDataException
                                                  or InvalidOperationException
                                                  or ArgumentException)
                {
                    var failure = QuarantineRollbackRestore(
                        D046HermesRollbackFailureKind.TransportUncertain,
                        "rollback.restore.rehydrate_failed",
                        "Rollback was committed, but authoritative history could not be adopted; reconnect.",
                        transportUncertain: true,
                        exception);
                    FailClosed(failure);
                    throw failure;
                }
            }
            else
            {
                receipt = receipt with
                {
                    Snapshot = ValidateAndAdoptSnapshot(
                        receipt.Snapshot,
                        request.RuntimeGeneration)
                };
            }
            return receipt;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<D046HermesVoiceStatusReceipt> GetVoiceStatusAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _voiceOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var canonical = RequireCanonicalVoiceProtocol();
            var binding = canonical.BindVoiceStatus(request);
            AdoptVoiceRequestCorrelation(request);
            var receipt = await ExecuteVoiceRequestAsync(
                request,
                binding,
                (response, envelope) => canonical.DecodeCanonicalVoiceStatusReceipt(
                    response,
                    envelope,
                    binding),
                cancellationToken).ConfigureAwait(false);
            ValidateVoiceReceiptCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId,
                request);
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                if (!_voiceCaptureFrozen)
                {
                    _voiceCaptureState = receipt.Enabled
                        ? D046HermesVoiceCaptureState.Idle
                        : D046HermesVoiceCaptureState.Off;
                }
            }
            return receipt;
        }
        finally
        {
            _voiceOperationGate.Release();
        }
    }

    public async Task<D046HermesVoiceOnReceipt> EnableVoiceAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _voiceOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var canonical = RequireCanonicalVoiceProtocol();
            var binding = canonical.BindVoiceOn(request);
            AdoptVoiceRequestCorrelation(request);
            var receipt = await ExecuteVoiceRequestAsync(
                request,
                binding,
                (response, envelope) => canonical.DecodeCanonicalVoiceOnReceipt(
                    response,
                    envelope,
                    binding),
                cancellationToken).ConfigureAwait(false);
            ValidateVoiceReceiptCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId,
                request);
            if (!receipt.Enabled)
                throw QuarantineVoiceProtocolFailure(
                    request,
                    "voice.on_not_enabled",
                    "Hermes did not enable native voice.");
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                _voiceCaptureState = D046HermesVoiceCaptureState.Idle;
                _voiceCaptureFrozen = false;
            }
            return receipt;
        }
        finally
        {
            _voiceOperationGate.Release();
        }
    }

    public async Task<D046HermesVoiceOffReceipt> DisableVoiceAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _voiceOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var canonical = RequireCanonicalVoiceProtocol();
            var binding = canonical.BindVoiceOff(request);
            AdoptVoiceRequestCorrelation(request);
            var receipt = await ExecuteVoiceRequestAsync(
                request,
                binding,
                (response, envelope) => canonical.DecodeCanonicalVoiceOffReceipt(
                    response,
                    envelope,
                    binding),
                cancellationToken).ConfigureAwait(false);
            ValidateVoiceReceiptCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId,
                request);
            if (receipt.Enabled)
                throw QuarantineVoiceProtocolFailure(
                    request,
                    "voice.off_not_disabled",
                    "Hermes did not disable native voice.");
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                _voiceCaptureState = D046HermesVoiceCaptureState.Off;
                _voiceCaptureFrozen = false;
            }
            return receipt;
        }
        finally
        {
            _voiceOperationGate.Release();
        }
    }

    public async Task<D046HermesVoiceStartReceipt> StartVoiceCaptureAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _voiceOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var reserved = false;
        var statusVersionAtReservation = 0L;
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var canonical = RequireCanonicalVoiceProtocol();
            var binding = canonical.BindVoiceStart(request);
            AdoptVoiceRequestCorrelation(request);
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                if (_voiceCaptureFrozen
                    || _voiceCaptureState != D046HermesVoiceCaptureState.Idle)
                {
                    throw VoiceStateConflict(
                        "voice.capture_not_idle",
                        "Native voice capture is not idle.");
                }
                _voiceCaptureFrozen = true;
                statusVersionAtReservation = _voiceStatusVersion;
                reserved = true;
            }

            D046HermesVoiceStartReceipt receipt;
            try
            {
                receipt = await ExecuteVoiceRequestAsync(
                    request,
                    binding,
                    (response, envelope) => canonical.DecodeCanonicalVoiceStartReceipt(
                        response,
                        envelope,
                        binding),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ReleaseUndispatchedVoiceStartReservation();
                throw;
            }
            ValidateVoiceReceiptCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId,
                request);
            if (receipt.Started == receipt.Busy)
            {
                throw QuarantineVoiceProtocolFailure(
                    request,
                    "voice.start_disposition",
                    "Hermes returned an invalid native voice start disposition.");
            }

            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                if (receipt.Started)
                {
                    if (_voiceCaptureFrozen
                        && _voiceStatusVersion == statusVersionAtReservation
                        && _voiceCaptureState == D046HermesVoiceCaptureState.Idle)
                    {
                        _voiceCaptureState = D046HermesVoiceCaptureState.Listening;
                    }
                }
                else if (_voiceCaptureState == D046HermesVoiceCaptureState.Idle)
                {
                    _voiceCaptureFrozen = false;
                }
            }
            return receipt;
        }
        finally
        {
            if (reserved)
            {
                lock (_stateGate)
                {
                    if (_voiceQuarantined)
                        _voiceCaptureFrozen = true;
                }
            }
            _voiceOperationGate.Release();
        }
    }

    public async Task<D046HermesVoiceStopReceipt> StopVoiceCaptureAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _voiceOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var canonical = RequireCanonicalVoiceProtocol();
            var binding = canonical.BindVoiceStop(request);
            AdoptVoiceRequestCorrelation(request);
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                if (!_voiceCaptureFrozen
                    || _voiceCaptureState is D046HermesVoiceCaptureState.Off
                        or D046HermesVoiceCaptureState.Idle)
                {
                    throw VoiceStateConflict(
                        "voice.capture_not_active",
                        "Native voice capture is not active.");
                }
            }

            var receipt = await ExecuteVoiceRequestAsync(
                request,
                binding,
                (response, envelope) => canonical.DecodeCanonicalVoiceStopReceipt(
                    response,
                    envelope,
                    binding),
                cancellationToken).ConfigureAwait(false);
            ValidateVoiceReceiptCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId,
                request);
            if (!receipt.Stopped)
            {
                throw QuarantineVoiceProtocolFailure(
                    request,
                    "voice.stop_not_stopped",
                    "Hermes did not stop native voice capture.");
            }
            // The native stop receipt only acknowledges the command. Capture
            // remains frozen until voice.status reports idle.
            return receipt;
        }
        finally
        {
            _voiceOperationGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        ID046HermesRuntimePipeConnection? connection;
        Task? receiveTask;
        Task? faultConnectionCleanupTask;
        TaskCompletionSource<bool>? owner = null;
        Task disposalTask;
        lock (_stateGate)
        {
            if (_disposalTask is not null)
                return new ValueTask(_disposalTask);
            _disposed = true;
            connection = _connection;
            receiveTask = _receiveTask;
            faultConnectionCleanupTask = _faultConnectionCleanupTask;
            owner = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            disposalTask = owner.Task;
            _disposalTask = disposalTask;
        }

        // Publish the shared task before starting transport cleanup so even a
        // synchronous re-entrant DisposeAsync call waits for the same drain.
        _ = CompleteDisposeAsync(
            connection,
            receiveTask,
            faultConnectionCleanupTask,
            owner);
        return new ValueTask(disposalTask);
    }

    private async Task CompleteDisposeAsync(
        ID046HermesRuntimePipeConnection? connection,
        Task? receiveTask,
        Task? faultConnectionCleanupTask,
        TaskCompletionSource<bool> owner)
    {
        try
        {
            await DisposeCoreAsync(
                connection,
                receiveTask,
                faultConnectionCleanupTask).ConfigureAwait(false);
            owner.TrySetResult(true);
        }
        catch (Exception exception)
        {
            owner.TrySetException(exception);
        }
    }

    private async Task DisposeCoreAsync(
        ID046HermesRuntimePipeConnection? connection,
        Task? receiveTask,
        Task? faultConnectionCleanupTask)
    {
        _lifetime.Cancel();
        FailPending(new ObjectDisposedException(GetType().FullName));
        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);
        if (faultConnectionCleanupTask is not null)
            await faultConnectionCleanupTask.ConfigureAwait(false);
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _lifetime.Dispose();
        _startGate.Dispose();
        _operationGate.Dispose();
        _voiceOperationGate.Dispose();
        _writeGate.Dispose();
    }

    private async Task<D046HermesWorkbenchSnapshot> SendSnapshotRequestAsync(
        Func<D046HermesRuntimeRequestBinding> bind,
        CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await SendRequestAsync(
            bind(),
            DecodeAndValidateSnapshot,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<D046HermesWorkbenchSnapshot> GetCanonicalInitialSnapshotAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        CancellationToken cancellationToken,
        D046HermesReadDiagnostics.Measurement timing)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        timing.Advance(D046HermesReadPhase.Response);
        _ = await SendCanonicalSnapshotStepAsync(
            canonical,
            canonical.BindGetInitialSnapshot(),
            cancellationToken).ConfigureAwait(false);
        var listed = await SendCanonicalSnapshotStepAsync(
            canonical,
            canonical.BindListTasks(),
            cancellationToken).ConfigureAwait(false);
        if (listed.Tasks.Count == 0)
        {
            return await CreateCanonicalTaskAsync(
                canonical,
                selection: null,
                cancellationToken,
                readyAlreadyEstablished: true,
                contextSnapshot: listed).ConfigureAwait(false);
        }
        var activation = canonical.BindOpenTask(listed.Tasks[0].TaskId);
        return await ActivateCanonicalTaskAsync(
            canonical,
            activation,
            cancellationToken,
            readyAlreadyEstablished: true).ConfigureAwait(false);
    }

    private void RetainSessionlessInventoryState(D046HermesSessionPreflightException exception)
    {
        // A verified empty inventory may legitimately wait for the user to
        // select a model or configure its provider. There is no session to
        // project a trailing inventory invalidation onto during that state.
        // This state grants no tool/model/session authority and has no timer;
        // the next validated selection retires it below.
        var snapshot = exception.ContextSnapshot;
        if (snapshot is null || !snapshot.IsConnected || snapshot.Tasks.Count != 0
            || !string.IsNullOrEmpty(snapshot.SelectedTaskId)
            || snapshot.SelectedSessionEvents.Count != 0
            || snapshot.SelectedPendingApprovals.Count != 0
            || snapshot.SelectedPendingClarification is not null
            || snapshot.SelectedPendingSecureInput is not null)
            return;
        lock (_stateGate)
        {
            if (!_disposed && _terminalFault is null
                && string.Equals(snapshot.RuntimeGeneration, _runtimeGeneration.ToString("N"),
                    StringComparison.Ordinal))
                _canonicalAwaitingSessionSelection = true;
        }
    }

    private async Task<D046HermesWorkbenchSnapshot> CreateCanonicalTaskAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        D046HermesModelOption? selection,
        CancellationToken cancellationToken,
        bool readyAlreadyEstablished,
        D046HermesWorkbenchSnapshot? contextSnapshot)
    {
        if (!readyAlreadyEstablished)
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        var catalog = await GetSessionlessModelOptionsCoreAsync(
            canonical,
            contextSnapshot,
            cancellationToken).ConfigureAwait(false);
        D046HermesModelOption? admitted;
        if (selection is not null)
        {
            admitted = catalog.Options.SingleOrDefault(option =>
                string.Equals(
                    option.ProviderId,
                    selection.ProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    option.ModelId,
                    selection.ModelId,
                    StringComparison.Ordinal));
            if (admitted is null)
            {
                throw new D046HermesSessionPreflightException(
                    catalog.Options.Count == 0
                        ? D046HermesSessionPreflightRequirement.CredentialRequired
                        : D046HermesSessionPreflightRequirement.ModelSelectionRequired,
                    catalog,
                    contextSnapshot);
            }
        }
        else if (catalog.HasExactSelection)
        {
            admitted = catalog.Options.Single(option =>
                string.Equals(
                    option.ProviderId,
                    catalog.ExactProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    option.ModelId,
                    catalog.ExactModelId,
                    StringComparison.Ordinal));
        }
        else
        {
            throw new D046HermesSessionPreflightException(
                catalog.Options.Count == 0
                    ? D046HermesSessionPreflightRequirement.CredentialRequired
                    : D046HermesSessionPreflightRequirement.ModelSelectionRequired,
                catalog,
                contextSnapshot);
        }

        return await ActivateCanonicalTaskAsync(
            canonical,
            canonical.BindCreateTask(admitted.ProviderId, admitted.ModelId),
            cancellationToken,
            readyAlreadyEstablished: true).ConfigureAwait(false);
    }

    private async Task<D046HermesModelCatalog> GetSessionlessModelOptionsCoreAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        D046HermesWorkbenchSnapshot? contextSnapshot,
        CancellationToken cancellationToken)
    {
        var binding = canonical.BindSessionlessModelOptions();
        try
        {
            var discovery = await SendRequestAsync(
                binding,
                (response, context) => response.Succeeded
                    ? new D046SessionlessModelDiscoveryResult(
                        Available: true,
                        ValidateModelCatalog(
                            canonical.DecodeCanonicalModelCatalog(
                                response,
                                context,
                                binding),
                            context.GenerationId.ToString("N"),
                            string.Empty,
                            string.Empty),
                        Code: string.Empty)
                    : new D046SessionlessModelDiscoveryResult(
                        Available: false,
                        EmptySessionlessModelCatalog(context.GenerationId),
                        Code: DefaultModelCatalogPresentation.SafeReadFailureCode(response.HermesPayload)),
                cancellationToken,
                preserveLateResponseOnTimeout: true).ConfigureAwait(false);
            if (discovery.Available)
                return discovery.Catalog;

            throw new D046HermesSessionPreflightException(
                D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable,
                discovery.Catalog,
                contextSnapshot,
                discovery.Code);
        }
        catch (TimeoutException exception)
        {
            throw new D046HermesSessionPreflightException(
                D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable,
                EmptySessionlessModelCatalog(RuntimeGeneration),
                contextSnapshot,
                "model.discovery_timeout",
                exception);
        }
    }

    private static D046HermesModelCatalog EmptySessionlessModelCatalog(
        Guid runtimeGeneration)
        => new(
            runtimeGeneration.ToString("N"),
            string.Empty,
            string.Empty,
            Array.Empty<D046HermesModelOption>());

    private async Task<D046HermesWorkbenchSnapshot> ActivateCanonicalTaskAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        D046HermesRuntimeRequestBinding activation,
        CancellationToken cancellationToken,
        bool readyAlreadyEstablished = false)
    {
        if (!readyAlreadyEstablished)
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        if (activation.Method is not ("session.create" or "session.resume"))
            throw new InvalidDataException("The canonical activation method was rejected.");

        var activated = await SendCanonicalSnapshotStepAsync(
            canonical,
            activation,
            cancellationToken).ConfigureAwait(false);
        var selected = activated.Tasks.SingleOrDefault(task => string.Equals(
                task.TaskId,
                activated.SelectedTaskId,
                StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                "Canonical activation did not select exactly one task.");
        if (string.IsNullOrWhiteSpace(selected.SessionId))
            throw new InvalidDataException("Canonical activation omitted the runtime session.");

        try
        {
            await canonical.WaitForNoEffectSessionInfoAsync(
                    selected.TaskId,
                    selected.SessionId,
                    cancellationToken)
                .WaitAsync(_sessionActivationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException
                                          or D046HermesSessionActivationException)
        {
            var catalog = await GetCredentialStatusCoreAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!canonical.TryAdmitReadOnlySessionAfterAttestationFailure(
                    selected.TaskId,
                    selected.SessionId,
                    catalog))
            {
                FailClosed(exception);
                throw;
            }
        }

        var history = await SendCanonicalSnapshotStepAsync(
            canonical,
            canonical.BindHistory(selected.SessionId),
            cancellationToken,
            adoptSnapshot: true).ConfigureAwait(false);
        return history;
    }

    private Task<D046HermesWorkbenchSnapshot> SendCanonicalSnapshotStepAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        D046HermesRuntimeRequestBinding binding,
        CancellationToken cancellationToken,
        bool adoptSnapshot = false)
        => SendRequestAsync(
            binding,
            (response, context) =>
            {
                var snapshot = canonical.DecodeCanonicalSnapshotStep(
                    response,
                    context,
                    binding);
                var generation = context.GenerationId.ToString("N");
                var validated = adoptSnapshot
                    ? ValidateAndAdoptSnapshot(snapshot, generation)
                    : ValidateSnapshotShape(
                        snapshot,
                        generation,
                        canonicalOnly: true);
                if (!string.IsNullOrEmpty(validated.SelectedTaskId))
                {
                    lock (_stateGate)
                        _canonicalAwaitingSessionSelection = false;
                }
                return validated;
            },
            cancellationToken);

    private async Task<T> SendRequestAsync<T>(
        D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        CancellationToken cancellationToken,
        TimeSpan? responseTimeout = null,
        bool isDecodeFailureIsolated = false,
        bool preserveLateResponseOnTimeout = false,
        Action? validateStateLocked = null,
        Action? dispatchStarting = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        var requestMessage = new LilithRuntimeRequest(
            binding.Method,
            binding.HermesPayload);
        var requestId = Guid.NewGuid();
        var pending = new D046PendingRuntimeResponse(
            binding.Method,
            (response, envelope) => decode(response, envelope)!,
            isDecodeFailureIsolated);
        if (!_pending.TryAdd(requestId, pending))
            throw new InvalidOperationException("Runtime request identity collided.");

        var awaitingResponse = false;
        var preservePending = false;
        try
        {
            await WriteSerializedRequestAsync(
                binding,
                requestId,
                requestMessage,
                validateStateLocked,
                dispatchStarting,
                cancellationToken).ConfigureAwait(false);

            awaitingResponse = true;
            var result = await pending.Completion.Task.WaitAsync(
                responseTimeout ?? _responseTimeout,
                cancellationToken).ConfigureAwait(false);
            return (T)result;
        }
        catch (TimeoutException) when (preserveLateResponseOnTimeout
                                       && awaitingResponse)
        {
            // Keep the exact request pending so a late receipt remains
            // correlated. It must still pass the ordinary method and decoder
            // validation; timeout recovery never weakens successful-response
            // shape or authenticated-claim checks.
            preservePending = true;
            _ = pending.Completion.Task.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
        catch (OperationCanceledException exception) when (awaitingResponse)
        {
            // Once the frame has crossed the pipe, abandoning its correlated
            // pending response would turn a late receipt into an unknown
            // request and poison the shared connection. Retire the connection
            // instead; caller cancellation never creates a reusable half-turn.
            FailClosed(exception);
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or TimeoutException)
        {
            FailClosed(exception);
            throw;
        }
        finally
        {
            if (!preservePending)
                _pending.TryRemove(requestId, out _);
        }
    }

    private async Task<T> ExecuteRollbackRequestAsync<T>(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        bool quarantineRestore,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendRollbackRequestAsync(
                runtimeGeneration,
                taskId,
                sessionId,
                binding,
                decode,
                quarantineRestore,
                cancellationToken).ConfigureAwait(false);
        }
        catch (D046HermesRollbackOperationException exception)
        {
            if (quarantineRestore
                && exception.FailureKind ==
                    D046HermesRollbackFailureKind.TransportUncertain)
            {
                FailClosed(exception);
            }
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or InvalidOperationException
                                          or ArgumentException)
        {
            if (quarantineRestore)
            {
                var failure = QuarantineRollbackRestore(
                    D046HermesRollbackFailureKind.ProtocolRejected,
                    "rollback.response_rejected",
                    "The rollback response was rejected.",
                    transportUncertain: false,
                    exception);
                FailClosed(failure);
                throw failure;
            }
            throw new D046HermesRollbackOperationException(
                D046HermesRollbackFailureKind.ProtocolRejected,
                "rollback.response_rejected",
                "The rollback response was rejected.",
                transportUncertain: false,
                exception);
        }
    }

    private async Task<T> SendRollbackRequestAsync<T>(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        bool quarantineRestore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        var requestMessage = new LilithRuntimeRequest(
            binding.Method,
            binding.HermesPayload);

        var requestId = Guid.NewGuid();
        var pending = new D046PendingRuntimeResponse(
            binding.Method,
            (response, envelope) => decode(response, envelope)!,
            isDecodeFailureIsolated: true);
        if (!_pending.TryAdd(requestId, pending))
            throw new InvalidOperationException("Runtime request identity collided.");
        var dispatchStarted = false;
        var preservePending = false;
        try
        {
            Action? validateStateLocked = quarantineRestore
                ? ThrowIfRollbackRestoreQuarantinedLocked
                : null;
            await WriteSerializedRequestAsync(
                binding,
                requestId,
                requestMessage,
                validateStateLocked,
                () => dispatchStarted = true,
                cancellationToken).ConfigureAwait(false);

            var result = await pending.Completion.Task.WaitAsync(
                _responseTimeout,
                cancellationToken).ConfigureAwait(false);
            return (T)result;
        }
        catch (Exception exception) when (dispatchStarted
                                          && exception is (IOException
                                              or TimeoutException
                                              or OperationCanceledException))
        {
            preservePending = true;
            _ = pending.Completion.Task.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            if (quarantineRestore)
            {
                throw QuarantineRollbackRestore(
                    D046HermesRollbackFailureKind.TransportUncertain,
                    "rollback.restore.transport_uncertain",
                    "The rollback restore outcome is uncertain.",
                    transportUncertain: true,
                    exception);
            }
            throw new D046HermesRollbackOperationException(
                D046HermesRollbackFailureKind.TransportUncertain,
                "rollback.transport_uncertain",
                "The rollback read outcome is uncertain.",
                transportUncertain: true,
                exception);
        }
        finally
        {
            if (!preservePending)
                _pending.TryRemove(requestId, out _);
        }
    }

    private async Task<T> ExecuteVoiceRequestAsync<T>(
        D046HermesVoiceCommandRequest request,
        D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendVoiceRequestAsync(
                request,
                binding,
                decode,
                cancellationToken).ConfigureAwait(false);
        }
        catch (D046HermesVoiceOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or InvalidOperationException
                                          or ArgumentException)
        {
            throw QuarantineVoice(
                request,
                D046HermesVoiceFailureKind.ProtocolRejected,
                "voice.response_rejected",
                "The native voice response was rejected.",
                transportUncertain: false,
                exception,
                binding.Method,
                "{}");
        }
    }

    private async Task<T> SendVoiceRequestAsync<T>(
        D046HermesVoiceCommandRequest request,
        D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        var requestMessage = new LilithRuntimeRequest(
            binding.Method,
            binding.HermesPayload);

        var requestId = Guid.NewGuid();
        var pending = new D046PendingRuntimeResponse(
            binding.Method,
            (response, envelope) => decode(response, envelope)!,
            isDecodeFailureIsolated: true);
        if (!_pending.TryAdd(requestId, pending))
            throw new InvalidOperationException("Runtime request identity collided.");
        var dispatchStarted = false;
        var preservePending = false;
        try
        {
            await WriteSerializedRequestAsync(
                binding,
                requestId,
                requestMessage,
                ThrowIfVoiceQuarantinedLocked,
                () => dispatchStarted = true,
                cancellationToken).ConfigureAwait(false);

            var result = await pending.Completion.Task.WaitAsync(
                _responseTimeout,
                cancellationToken).ConfigureAwait(false);
            return (T)result;
        }
        catch (Exception exception) when (dispatchStarted
                                          && exception is (IOException
                                              or TimeoutException
                                              or OperationCanceledException))
        {
            // The command may already have crossed the transport. Keep the
            // request tombstone so a late receipt cannot fault the chat pipe,
            // quarantine voice, and never replay the command.
            preservePending = true;
            _ = pending.Completion.Task.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw QuarantineVoice(
                request,
                D046HermesVoiceFailureKind.TransportUncertain,
                "voice.transport_uncertain",
                "The native voice command outcome is uncertain.",
                transportUncertain: true,
                exception,
                binding.Method,
                "{}");
        }
        finally
        {
            if (!preservePending)
                _pending.TryRemove(requestId, out _);
        }
    }

    private async Task WriteSerializedRequestAsync(
        D046HermesRuntimeRequestBinding binding,
        Guid requestId,
        LilithRuntimeRequest requestMessage,
        Action? validateStateLocked,
        Action? dispatchStarting,
        CancellationToken cancellationToken)
    {
        if (_beforeSerializedRequestWrite is not null)
        {
            await _beforeSerializedRequestWrite(binding).ConfigureAwait(false);
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            LilithRuntimeEnvelope envelope;
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                validateStateLocked?.Invoke();
                var generation = _runtimeGeneration;
                if (generation == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        "Runtime handshake is incomplete.");
                }
                var sequence = checked(++_nextOutboundSequence);
                envelope = new LilithRuntimeEnvelope(
                    generation,
                    requestId,
                    sequence,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    requestMessage);
            }
            dispatchStarting?.Invoke();
            using var boundedWrite = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token);
            boundedWrite.CancelAfter(_responseTimeout);
            try
            {
                // Caller cancellation is deliberately no longer observed after
                // sequence admission. The length prefix, body, and flush must
                // complete as one transport commitment or the connection is
                // retired below.
                await GetConnection().WriteAsync(
                    envelope,
                    boundedWrite.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                var failure = new IOException(
                    "Runtime pipe write did not complete after sequence admission.",
                    exception);
                FailClosed(failure);
                throw failure;
            }
            catch (IOException exception)
            {
                FailClosed(exception);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (_connection is null)
            {
                var connection = _connectionFactory.Create();
                try
                {
                    await connection.ConnectAsync(cancellationToken).WaitAsync(
                        _connectTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                lock (_stateGate)
                {
                    ThrowIfUnavailableLocked();
                    _connection = connection;
                    _receiveTask = ReceiveLoopAsync(connection, _lifetime.Token);
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or TimeoutException)
        {
            FailClosed(exception);
            throw;
        }
        finally
        {
            _startGate.Release();
        }

        try
        {
            await _ready.Task.WaitAsync(
                _connectTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            FailClosed(exception);
            throw;
        }
        ThrowIfUnavailable();
    }

    private async Task ReceiveLoopAsync(
        ID046HermesRuntimePipeConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var envelope = await connection.ReadAsync(
                    cancellationToken).ConfigureAwait(false);
                ProcessInboundEnvelope(envelope);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailClosed(exception);
        }
    }

    private void ProcessInboundEnvelope(LilithRuntimeEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        D046HermesRuntimeEnvelopeContext context;
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (_runtimeGeneration == Guid.Empty)
            {
                // Runtime sequence numbers are generation-wide, not
                // connection-local. A reconnect therefore starts from the
                // current hello cursor and only requires subsequent frames
                // to remain contiguous.
                _runtimeGeneration = envelope.GenerationId;
            }
            else
            {
                if (envelope.GenerationId != _runtimeGeneration)
                    throw new InvalidDataException("Runtime generation changed in place.");
                if (envelope.Sequence != _lastInboundSequence + 1)
                    throw new InvalidDataException("Runtime sequence is not contiguous.");
            }

            _lastInboundSequence = envelope.Sequence;
            context = ToContext(envelope);
            if (!_helloReceived
                && envelope.Kind != LilithRuntimeMessageKind.RuntimeHello)
            {
                throw new InvalidDataException("Runtime hello must be the first frame.");
            }
        }

        switch (envelope.Message)
        {
            case LilithRuntimeHello hello:
                ProcessHello(hello);
                break;
            case LilithRuntimeStatus status:
                ProcessRuntimeStatus(status);
                break;
            case LilithRuntimeResponse response:
                ProcessRuntimeResponse(envelope.RequestId, response, context);
                break;
            case LilithRuntimeHermesEvent hermesEvent:
                ProcessHermesEvent(hermesEvent, context);
                break;
            case LilithRuntimeRequest:
                throw new InvalidDataException("Runtime sent a request to the UI client.");
            default:
                throw new InvalidDataException("Runtime message kind was not recognized.");
        }
    }

    private void ProcessHello(LilithRuntimeHello hello)
    {
        lock (_stateGate)
        {
            if (_helloReceived)
                throw new InvalidDataException("Runtime hello was duplicated.");
            var requiredCapabilities =
                _protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical
                    ? canonical.RequiredCapabilities
                    : new[] { _protocol.RequiredCapability };
            if (!hello.Ready
                || requiredCapabilities.Any(capability =>
                    !hello.Capabilities.Contains(capability, StringComparer.Ordinal))
                || (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonicalProtocol
                    && !string.Equals(
                        hello.HermesCommit,
                        canonicalProtocol.RequiredHermesCommit,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Runtime is not ready for the requested Workbench protocol.");
            }

            CaptureCapabilityCatalogHelloIdentity(hello);
            CaptureUsageBudgetHello(hello);
            CaptureProviderSetupHello(hello);
            CaptureLatestUsageHello(hello);
            _helloReceived = true;
        }
        _ready.TrySetResult(true);
    }

    private static void ProcessRuntimeStatus(LilithRuntimeStatus status)
    {
        if (status.State is LilithRuntimeState.Stopping
            or LilithRuntimeState.Stopped
            or LilithRuntimeState.Faulted)
        {
            throw new InvalidDataException(
                "Runtime entered a terminal state: " + status.Code);
        }
    }

    private void ProcessRuntimeResponse(
        Guid requestId,
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext context)
    {
        if (!_pending.TryRemove(requestId, out var pending))
            throw new InvalidDataException("Runtime response did not match a request.");
        if (!string.Equals(
                response.Method,
                pending.ExpectedMethod,
                StringComparison.Ordinal))
        {
            var mismatch = new InvalidDataException(
                "Runtime response method did not match.");
            pending.Completion.TrySetException(mismatch);
            if (!pending.IsDecodeFailureIsolated)
                throw mismatch;
            return;
        }

        try
        {
            var value = pending.Decode(response, context);
            pending.Completion.TrySetResult(value);
        }
        catch (Exception exception)
        {
            switch (pending.ExpectedMethod)
            {
                case "session.create":
                case "session.resume":
                    D046HermesProjectionFault.MarkDecodeStage(exception,
                        D046HermesProjectionDecodeStage.ActivationResponse);
                    break;
                case "session.history":
                    D046HermesProjectionFault.MarkDecodeStage(exception,
                        D046HermesProjectionDecodeStage.HistoryResponse);
                    break;
            }
            pending.Completion.TrySetException(exception);
            if (!pending.IsDecodeFailureIsolated
                && exception is not D046HermesSessionTransitionRejectedException)
                throw;
        }
    }

    private void ProcessHermesEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext context)
    {
        if (TryProcessDailyApprovalEvent(message, context)) return;
        if (message.Method.StartsWith("voice.", StringComparison.Ordinal))
        {
            ProcessVoiceEvent(message, context);
            return;
        }

        var sessionlessManagementOrInventoryHandoff = false;
        lock (_stateGate)
            sessionlessManagementOrInventoryHandoff = !_projectionCursorEstablished
                || _canonicalInitialSnapshotInProgress
                || _canonicalDeleteReplacementInProgress
                || _canonicalAwaitingSessionSelection;
        // Preconnect and the management pages deliberately do not select a chat.
        // F7/TG may still append to the shared DB and emit a global invalidation.
        // The canonical decoder itself requires an empty selected task and a
        // strictly global sessions.changed frame. A selected chat (even during
        // handoff), targeted events and all other methods use the full path below.
        if (sessionlessManagementOrInventoryHandoff
            && _protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical
            && canonical.TryCoalesceStartupGlobalSessionInvalidation(message, context))
        {
            return;
        }

        D046HermesProjectionEvent? projectionEvent;
        bool decoded;
        try
        {
            decoded = _protocol.TryDecodeEvent(message, context, out projectionEvent);
        }
        catch (System.Text.Json.JsonException)
        {
            // Keep a closed phase-specific cause; JSON diagnostics may contain
            // player payload bytes and must never become UI/log content.
            throw new InvalidDataException("Hermes event JSON was rejected.");
        }
        catch (InvalidDataException exception) when (message.Method == "session.info")
        {
            D046HermesProjectionFault.MarkDecodeStage(exception,
                D046HermesProjectionDecodeStage.SessionInfoEvent);
            throw;
        }
        if (!decoded || projectionEvent is null)
        {
            throw new InvalidDataException(
                "Hermes event could not be projected without loss.");
        }

        ValidateLiveProjectionEvent(message, projectionEvent, context.GenerationId);
        if (projectionEvent.Payload is D046HermesToolPayload
            {
                IsHostEffectTool: true
            }
            && _protocol is not ID046CanonicalHermesWorkbenchRuntimeProtocol)
        {
            throw new InvalidDataException(
                "A non-canonical protocol emitted a host-effect tool event.");
        }
        lock (_stateGate)
        {
            if (!_projectionCursorEstablished)
            {
                if (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol)
                    return;
                throw new InvalidDataException(
                    "Hermes event arrived before a projection snapshot.");
            }
            if (projectionEvent.Sequence != _lastProjectionSequence + 1)
            {
                throw new InvalidDataException(
                    "Hermes projection sequence is not contiguous.");
            }
            _lastProjectionSequence = projectionEvent.Sequence;
        }

        EventReceived?.Invoke(this, projectionEvent);
    }

    private void ProcessVoiceEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext context)
    {
        D046HermesVoiceEvent voiceEvent;
        try
        {
            var canonical = RequireCanonicalVoiceProtocol();
            if (!canonical.TryDecodeVoiceEvent(message, context, out voiceEvent)
                || voiceEvent is null)
            {
                throw new InvalidDataException(
                    "Hermes emitted an unsupported native voice event.");
            }
            ValidateLiveVoiceEvent(message, voiceEvent, context.GenerationId);

            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                ThrowIfVoiceQuarantinedLocked();
                if (string.IsNullOrWhiteSpace(_voiceTaskId)
                    || !string.Equals(
                        voiceEvent.RuntimeGeneration,
                        _voiceRuntimeGeneration,
                        StringComparison.Ordinal)
                    || !string.Equals(voiceEvent.TaskId, _voiceTaskId, StringComparison.Ordinal)
                    || !string.Equals(
                        voiceEvent.SessionId,
                        _voiceSessionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes voice event escaped the active voice binding.");
                }

                switch (voiceEvent.Payload)
                {
                    case D046HermesVoiceStatusPayload status:
                        _voiceStatusVersion = checked(_voiceStatusVersion + 1);
                        if (status.State is D046HermesVoiceCaptureState.Listening
                            or D046HermesVoiceCaptureState.Transcribing)
                        {
                            if (!_voiceCaptureFrozen)
                            {
                                throw new InvalidDataException(
                                    "Hermes voice capture changed without an active start.");
                            }
                            _voiceCaptureState = status.State;
                        }
                        else if (status.State == D046HermesVoiceCaptureState.Idle)
                        {
                            _voiceCaptureState = D046HermesVoiceCaptureState.Idle;
                            _voiceCaptureFrozen = false;
                        }
                        else
                        {
                            throw new InvalidDataException(
                                "Hermes emitted an invalid native voice status.");
                        }
                        break;
                    case D046HermesVoiceTranscriptPayload:
                        if (!_voiceCaptureFrozen
                            || _voiceCaptureState is D046HermesVoiceCaptureState.Off
                                or D046HermesVoiceCaptureState.Idle)
                        {
                            throw new InvalidDataException(
                                "Hermes emitted a transcript outside an active capture.");
                        }
                        break;
                    default:
                        throw new InvalidDataException(
                            "Hermes emitted an invalid native voice payload.");
                }
            }
        }
        catch (D046HermesVoiceOperationException)
        {
            return;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or InvalidOperationException
                                          or ArgumentException)
        {
            _ = QuarantineVoice(
                request: null,
                D046HermesVoiceFailureKind.ProtocolRejected,
                "voice.event_rejected",
                "A native voice event was rejected.",
                transportUncertain: false,
                exception,
                message.Method,
                message.HermesPayload.GetRawText());
            return;
        }

        VoiceEventReceived?.Invoke(this, voiceEvent);
    }

    private D046HermesWorkbenchSnapshot DecodeAndValidateSnapshot(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext context)
    {
        var snapshot = _protocol.DecodeSnapshot(response, context)
            ?? throw new InvalidDataException("Runtime snapshot was null.");
        return ValidateAndAdoptSnapshot(
            snapshot,
            context.GenerationId.ToString("N"));
    }

    private D046HermesWorkbenchSnapshot ValidateAndAdoptSnapshot(
        D046HermesWorkbenchSnapshot snapshot,
        string generation)
    {
        snapshot = ValidateSnapshotShape(
            snapshot,
            generation,
            canonicalOnly:
                _protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol);

        lock (_stateGate)
        {
            if (_projectionCursorEstablished
                && snapshot.ThroughSequence < _lastProjectionSequence)
            {
                throw new InvalidDataException(
                    "Runtime snapshot moved the projection cursor backwards.");
            }
            _lastProjectionSequence = snapshot.ThroughSequence;
            _projectionCursorEstablished = true;
        }
        return snapshot;
    }

    private static D046HermesWorkbenchSnapshot ValidateSnapshotShape(
        D046HermesWorkbenchSnapshot snapshot,
        string generation,
        bool canonicalOnly)
    {
        try
        {
            return ValidateSnapshotShapeCore(snapshot, generation, canonicalOnly);
        }
        catch (InvalidDataException exception)
        {
            D046HermesProjectionFault.MarkDecodeStage(exception,
                D046HermesProjectionDecodeStage.SnapshotShape);
            throw;
        }
    }

    private static D046HermesWorkbenchSnapshot ValidateSnapshotShapeCore(
        D046HermesWorkbenchSnapshot snapshot,
        string generation,
        bool canonicalOnly)
    {
        if (snapshot is null)
            throw new InvalidDataException("Runtime snapshot was null.");
        if (!string.Equals(
                snapshot.RuntimeGeneration,
                generation,
                StringComparison.Ordinal)
            || snapshot.ThroughSequence < 0
            || snapshot.Tasks is null
            || snapshot.SelectedSessionEvents is null
            || snapshot.SelectedPendingApprovals is null)
        {
            throw new InvalidDataException("Runtime snapshot binding was rejected.");
        }

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in snapshot.Tasks)
        {
            if (task is null
                || string.IsNullOrWhiteSpace(task.TaskId)
                || string.IsNullOrWhiteSpace(task.LineageId)
                || !taskIds.Add(task.TaskId))
            {
                throw new InvalidDataException("Runtime task projection was rejected.");
            }
        }
        var selectedTask = string.IsNullOrEmpty(snapshot.SelectedTaskId)
            ? null
            : snapshot.Tasks.SingleOrDefault(task => string.Equals(
                task.TaskId,
                snapshot.SelectedTaskId,
                StringComparison.Ordinal));
        if ((!string.IsNullOrEmpty(snapshot.SelectedTaskId)
                && (selectedTask is null
                    || string.IsNullOrWhiteSpace(selectedTask.SessionId)))
            || (string.IsNullOrEmpty(snapshot.SelectedTaskId)
                && snapshot.SelectedSessionEvents.Count != 0)
            || (snapshot.NativeToolsConfigStale
                && (snapshot.NativeSurfaceAttested || selectedTask is null))
            // Computer use is one optional Hermes-native toolset. A valid
            // tools.config change may intentionally disable it, so it cannot
            // be used as a transport/session baseline invariant. Keep the
            // canonical connection requirement here; individual computer-use
            // commands remain gated by their attested tool surface.
            || (canonicalOnly && !snapshot.IsConnected)
            || (canonicalOnly
                && snapshot.Tasks.Any(task =>
                    !ReferenceEquals(task, selectedTask)
                    && !string.IsNullOrEmpty(task.SessionId))))
        {
            throw new InvalidDataException("Runtime selected task was rejected.");
        }

        long prior = 0;
        foreach (var projectionEvent in snapshot.SelectedSessionEvents)
        {
            ValidateStoredProjectionEvent(projectionEvent, generation);
            if (projectionEvent.Sequence <= prior
                || projectionEvent.Sequence > snapshot.ThroughSequence)
            {
                throw new InvalidDataException(
                    "Runtime snapshot event sequence was rejected.");
            }
            if (selectedTask is null
                || !string.Equals(
                    projectionEvent.TaskId,
                    selectedTask.TaskId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    projectionEvent.SessionId,
                    selectedTask.SessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime snapshot event session was rejected.");
            }
            prior = projectionEvent.Sequence;
        }
        D046HermesWorkbenchSnapshotApprovalValidator.Validate(snapshot);
        return snapshot;
    }

    private D046HermesCommandReceipt DecodeAndValidateReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext context,
        D046HermesRuntimeRequestBinding binding)
    {
        var receipt = (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical
                ? canonical.DecodeCanonicalCommandReceipt(
                    response,
                    context,
                    binding)
                : _protocol.DecodeCommandReceipt(response, context))
            ?? throw new InvalidDataException("Runtime command receipt was null.");
        if (!string.Equals(
                receipt.RuntimeGeneration,
                context.GenerationId.ToString("N"),
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || string.IsNullOrWhiteSpace(receipt.TaskId)
            || string.IsNullOrWhiteSpace(receipt.SessionId))
        {
            throw new InvalidDataException("Runtime command receipt was rejected.");
        }
        return receipt;
    }

    private D046HermesSubmitReceipt DecodeAndValidateSubmitReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext context,
        D046HermesRuntimeRequestBinding binding)
    {
        var receipt = (_protocol is ID046CanonicalHermesWorkbenchRuntimeProtocol canonical
                ? canonical.DecodeCanonicalSubmitReceipt(
                    response,
                    context,
                    binding)
                : _protocol.DecodeSubmitReceipt(response, context))
            ?? throw new InvalidDataException("Runtime submit receipt was null.");
        if (!string.Equals(
                receipt.RuntimeGeneration,
                context.GenerationId.ToString("N"),
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || string.IsNullOrWhiteSpace(receipt.TaskId)
            || string.IsNullOrWhiteSpace(receipt.SessionId)
            || (receipt.Accepted
                && !receipt.ConsumesInput)
            || (!receipt.Accepted
                && (receipt.TurnId.Length != 0
                    || receipt.UserMessageId.Length != 0
                    || receipt.AssistantMessageId.Length != 0)))
        {
            throw new InvalidDataException("Runtime submit receipt was rejected.");
        }
        return receipt;
    }

    private ID046CanonicalHermesWorkbenchRuntimeProtocol RequireCanonicalVoiceProtocol()
        => _protocol as ID046CanonicalHermesWorkbenchRuntimeProtocol
            ?? throw new InvalidOperationException(
                "Native voice requires the canonical Hermes protocol.");

    private void AdoptVoiceRequestCorrelation(D046HermesVoiceCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientRequestId)
            || request.ClientRequestId.Length > 1024
            || request.ClientRequestId.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.RuntimeGeneration)
            || string.IsNullOrWhiteSpace(request.TaskId)
            || string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException(
                "Native voice request correlation was invalid.",
                nameof(request));
        }
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            ThrowIfVoiceQuarantinedLocked();
            if (!string.Equals(
                    request.RuntimeGeneration,
                    _runtimeGeneration.ToString("N"),
                    StringComparison.Ordinal))
            {
                throw VoiceStateConflict(
                    "voice.generation_changed",
                    "The native voice runtime generation changed.");
            }
            if (_voiceTaskId.Length != 0
                && (!string.Equals(_voiceRuntimeGeneration, request.RuntimeGeneration,
                        StringComparison.Ordinal)
                    || !string.Equals(_voiceTaskId, request.TaskId, StringComparison.Ordinal)
                    || !string.Equals(_voiceSessionId, request.SessionId,
                        StringComparison.Ordinal))
                && (_voiceCaptureFrozen
                    || _voiceCaptureState is D046HermesVoiceCaptureState.Listening
                        or D046HermesVoiceCaptureState.Transcribing))
            {
                throw VoiceStateConflict(
                    "voice.capture_binding_changed",
                    "The active native voice capture belongs to another task.");
            }
            _voiceRuntimeGeneration = request.RuntimeGeneration;
            _voiceTaskId = request.TaskId;
            _voiceSessionId = request.SessionId;
        }
    }

    private static void ValidateVoiceReceiptCorrelation(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesVoiceCommandRequest request)
    {
        if (!string.Equals(runtimeGeneration, request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(taskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(sessionId, request.SessionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The native voice receipt was not correlated.");
        }
    }

    private void ThrowIfVoiceQuarantinedLocked()
    {
        if (_voiceQuarantined)
        {
            throw VoiceStateConflict(
                "voice.quarantined",
                "Native voice is quarantined until the Workbench reconnects.");
        }
    }

    private static D046HermesVoiceOperationException VoiceStateConflict(
        string code,
        string message)
        => new(
            D046HermesVoiceFailureKind.StateConflict,
            code,
            message,
            transportUncertain: false);

    private void ReleaseUndispatchedVoiceStartReservation()
    {
        lock (_stateGate)
        {
            if (!_voiceQuarantined
                && _voiceCaptureState == D046HermesVoiceCaptureState.Idle)
            {
                _voiceCaptureFrozen = false;
            }
        }
    }

    private D046HermesVoiceOperationException QuarantineVoiceProtocolFailure(
        D046HermesVoiceCommandRequest request,
        string code,
        string message)
        => QuarantineVoice(
            request,
            D046HermesVoiceFailureKind.ProtocolRejected,
            code,
            message,
            transportUncertain: false,
            innerException: null,
            sourceMethod: "voice.local.failure",
            canonicalPayloadJson: "{}");

    private D046HermesVoiceOperationException QuarantineVoice(
        D046HermesVoiceCommandRequest? request,
        D046HermesVoiceFailureKind failureKind,
        string code,
        string message,
        bool transportUncertain,
        Exception? innerException,
        string sourceMethod,
        string canonicalPayloadJson)
    {
        D046HermesVoiceEvent? failureEvent = null;
        lock (_stateGate)
        {
            if (!_disposed && !_voiceQuarantined)
            {
                if (request is not null)
                {
                    _voiceRuntimeGeneration = request.RuntimeGeneration;
                    _voiceTaskId = request.TaskId;
                    _voiceSessionId = request.SessionId;
                }
                _voiceQuarantined = true;
                _voiceCaptureFrozen = true;
                _voiceCaptureState = D046HermesVoiceCaptureState.Quarantined;
                failureEvent = new D046HermesVoiceEvent(
                    Guid.NewGuid().ToString("N"),
                    _voiceRuntimeGeneration,
                    _voiceTaskId,
                    _voiceSessionId,
                    _lastInboundSequence,
                    DateTimeOffset.UtcNow,
                    sourceMethod,
                    new D046HermesVoiceFailurePayload(
                        failureKind,
                        code,
                        message,
                        transportUncertain),
                    canonicalPayloadJson);
            }
        }
        if (failureEvent is not null)
            VoiceEventReceived?.Invoke(this, failureEvent);
        return new D046HermesVoiceOperationException(
            failureKind,
            code,
            message,
            transportUncertain,
            innerException);
    }

    private static void ValidateLiveVoiceEvent(
        LilithRuntimeHermesEvent source,
        D046HermesVoiceEvent value,
        Guid generation)
    {
        if (value is null
            || string.IsNullOrWhiteSpace(value.EventId)
            || !string.Equals(
                value.RuntimeGeneration,
                generation.ToString("N"),
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value.TaskId)
            || string.IsNullOrWhiteSpace(value.SessionId)
            || value.RuntimeSequence <= 0
            || !string.Equals(value.SourceMethod, source.Method, StringComparison.Ordinal)
            || value.Payload is null
            || !string.Equals(
                value.CanonicalPayloadJson,
                source.HermesPayload.GetRawText(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes native voice event was not preserved.");
        }
    }

    private static void ValidateImageAttachReceipt(
        D046HermesImageAttachReceipt receipt,
        D046HermesSubmitRequest request,
        int expectedBytes)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code))
        {
            throw new InvalidDataException("Runtime image attach receipt was not correlated.");
        }
        if (!receipt.Attached)
        {
            if (receipt.Path.Length != 0 || receipt.Count != 0 || receipt.Bytes != 0)
                throw new InvalidDataException("Runtime image attach failure was malformed.");
            return;
        }
        if (string.IsNullOrWhiteSpace(receipt.Path)
            || receipt.Path.Any(char.IsControl)
            || receipt.Count < 1
            || receipt.Bytes != expectedBytes)
        {
            throw new InvalidDataException("Runtime image attach receipt was rejected.");
        }
    }

    private async Task<bool> TryDetachImageAsync(
        ID046CanonicalHermesWorkbenchRuntimeProtocol canonical,
        D046HermesSubmitRequest request,
        string attachedPath)
    {
        using var cleanup = new CancellationTokenSource(ImageCleanupTimeout);
        try
        {
            var binding = canonical.BindImageDetach(request, attachedPath);
            var receipt = await SendRequestAsync(
                binding,
                (response, context) => canonical.DecodeCanonicalImageDetachReceipt(
                    response,
                    context,
                    binding),
                cleanup.Token,
                ImageCleanupTimeout).ConfigureAwait(false);
            return receipt.Detached
                && receipt.Count == 0
                && string.Equals(
                    receipt.RuntimeGeneration,
                    request.RuntimeGeneration,
                    StringComparison.Ordinal)
                && string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
                && string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(receipt.Code);
        }
        catch (Exception exception) when (exception is IOException
                                          or TimeoutException
                                          or OperationCanceledException
                                          or InvalidDataException
                                          or InvalidOperationException)
        {
            return false;
        }
    }

    private static D046HermesApprovalResponseReceipt ValidateApprovalReceipt(
        D046HermesApprovalResponseReceipt receipt,
        string generation,
        D046HermesApprovalResponseRequest request)
    {
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || !string.Equals(receipt.Choice, request.Choice, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.Resolved is < 0 or > 1
            || receipt.Accepted != (receipt.Resolved == 1)
            || (receipt.RetryAllowed
                ? receipt.Accepted || receipt.Code != RuntimeApprovalRetryProof.FailureCode
                    || string.IsNullOrWhiteSpace(receipt.RetryApprovalId)
                : receipt.RetryApprovalId.Length != 0))
        {
            throw new InvalidDataException("Runtime approval receipt was rejected.");
        }
        return receipt;
    }

    private static D046HermesModelCatalog ValidateModelCatalog(
        D046HermesModelCatalog catalog,
        string generation,
        string taskId,
        string sessionId)
    {
        if (catalog is null
            || !string.Equals(catalog.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(catalog.TaskId, taskId, StringComparison.Ordinal)
            || !string.Equals(catalog.SessionId, sessionId, StringComparison.Ordinal)
            || catalog.Options is null)
        {
            throw new InvalidDataException("Runtime model catalog was rejected.");
        }

        var pairs = new HashSet<(string ProviderId, string ModelId)>();
        foreach (var option in catalog.Options)
        {
            if (option is null
                || string.IsNullOrWhiteSpace(option.ProviderId)
                || string.IsNullOrWhiteSpace(option.ProviderDisplayName)
                || string.IsNullOrWhiteSpace(option.ModelId)
                || !pairs.Add((option.ProviderId, option.ModelId)))
            {
                throw new InvalidDataException("Runtime model option was rejected.");
            }
        }
        if (string.IsNullOrEmpty(catalog.ExactProviderId)
                != string.IsNullOrEmpty(catalog.ExactModelId)
            || (catalog.HasExactSelection
                && !pairs.Contains((catalog.ExactProviderId, catalog.ExactModelId))))
        {
            throw new InvalidDataException(
                "Runtime exact model selection was rejected.");
        }
        return catalog;
    }

    private static D046HermesNativeToolsCatalog ValidateNativeToolsCatalog(
        D046HermesNativeToolsCatalog catalog,
        string generation,
        string taskId,
        string sessionId)
    {
        if (catalog is null
            || !string.Equals(catalog.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(catalog.TaskId, taskId, StringComparison.Ordinal)
            || !string.Equals(catalog.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(catalog.Platform, "cli", StringComparison.Ordinal)
            || catalog.EnabledToolsets is null
            || catalog.Toolsets is null
            || catalog.Toolsets.Count > 256)
        {
            throw new InvalidDataException("Runtime native tool catalog was rejected.");
        }

        var enabled = new HashSet<string>(catalog.EnabledToolsets, StringComparer.Ordinal);
        if (enabled.Count != catalog.EnabledToolsets.Count)
            throw new InvalidDataException("Runtime native tool catalog duplicated an enabled toolset.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in catalog.Toolsets)
        {
            if (row is null
                || string.IsNullOrWhiteSpace(row.Name)
                || string.IsNullOrWhiteSpace(row.Label)
                || row.Description is null
                || row.Tools is null
                || !names.Add(row.Name)
                || row.Enabled != enabled.Contains(row.Name)
                || row.Configured
                    != (row.Readiness == D046HermesNativeToolsetReadiness.Ready))
            {
                throw new InvalidDataException("Runtime native toolset row was rejected.");
            }
        }
        if (!enabled.IsSubsetOf(names))
            throw new InvalidDataException("Runtime enabled an unknown native toolset.");
        return catalog;
    }

    private static D046HermesNativeToolsSetReceipt ValidateNativeToolsSetReceipt(
        D046HermesNativeToolsSetReceipt receipt,
        string generation,
        D046HermesNativeToolsSetRequest request)
    {
        if (receipt is null
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.Catalog is null)
        {
            throw new InvalidDataException("Runtime native tool receipt was rejected.");
        }
        var catalog = ValidateNativeToolsCatalog(
            receipt.Catalog,
            generation,
            request.TaskId,
            request.SessionId);
        if (receipt.Accepted)
        {
            var requested = new HashSet<string>(
                request.EnabledToolsets,
                StringComparer.Ordinal);
            if (receipt.ConfigSha256.Length != 64
                || receipt.ConfigSha256.Any(character => !Uri.IsHexDigit(character))
                || !catalog.NewSessionRequired
                || catalog.ApprovalMode != request.ApprovalMode
                || !requested.SetEquals(catalog.EnabledToolsets))
            {
                throw new InvalidDataException("Runtime native tool receipt was not exact.");
            }
        }
        else if (receipt.ConfigSha256.Length != 0)
        {
            throw new InvalidDataException("Runtime native tool failure claimed a configuration hash.");
        }
        return receipt;
    }

    private static D046HermesObservabilitySnapshot ValidateObservabilitySnapshot(
        D046HermesObservabilitySnapshot snapshot)
    {
        if (snapshot is null
            || string.IsNullOrWhiteSpace(snapshot.RuntimeGeneration)
            || string.IsNullOrWhiteSpace(snapshot.TaskId)
            || string.IsNullOrWhiteSpace(snapshot.SessionId)
            || snapshot.Usage is null
            || snapshot.Context is null
            || snapshot.Learning is null
            || string.IsNullOrWhiteSpace(snapshot.Usage.StatusCode)
            || string.IsNullOrWhiteSpace(snapshot.Context.StatusCode)
            || string.IsNullOrWhiteSpace(snapshot.Learning.StatusCode)
            || snapshot.Context.Categories is null
            || snapshot.Learning.Summary is null
            || snapshot.Learning.RecentItems is null)
        {
            throw new InvalidDataException("Runtime observability snapshot was rejected.");
        }
        return snapshot;
    }

    private ID046CanonicalHermesWorkbenchRuntimeProtocol
        RequireCanonicalDelegationProtocol()
        => _protocol as ID046CanonicalHermesWorkbenchRuntimeProtocol
            ?? throw new InvalidOperationException(
                "Native delegation requires the canonical Hermes protocol.");

    private ID046CanonicalHermesWorkbenchRuntimeProtocol
        RequireCanonicalRollbackProtocol()
        => _protocol as ID046CanonicalHermesWorkbenchRuntimeProtocol
            ?? throw new InvalidOperationException(
                "Rollback requires the canonical Hermes protocol.");

    private void ValidateRollbackRequest(
        string clientRequestId,
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)
            || clientRequestId.Length > 1024
            || clientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Rollback client request ID was invalid.",
                nameof(clientRequestId));
        }
        ValidateBoundCommand(
            runtimeGeneration,
            taskId,
            sessionId,
            lastObservedSequence);
    }

    private static D046HermesRollbackCatalog ValidateRollbackCatalog(
        D046HermesRollbackCatalog catalog,
        D046HermesRollbackListRequest request)
    {
        if (catalog is null
            || !string.Equals(catalog.RuntimeGeneration, request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(catalog.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(catalog.SessionId, request.SessionId,
                StringComparison.Ordinal)
            || catalog.ThroughSequence < request.LastObservedSequence
            || catalog.Checkpoints is null
            || (!catalog.Enabled && catalog.Checkpoints.Count != 0))
        {
            throw new InvalidDataException("Rollback catalog was rejected.");
        }
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var checkpoint in catalog.Checkpoints)
        {
            if (checkpoint is null
                || checkpoint.Hash.Length != 40
                || checkpoint.Hash.Any(character => character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f'))
                || !hashes.Add(checkpoint.Hash)
                || checkpoint.Message is null)
            {
                throw new InvalidDataException("Rollback checkpoint was rejected.");
            }
        }
        return catalog;
    }

    private static D046HermesRollbackDiffReceipt ValidateRollbackDiffReceipt(
        D046HermesRollbackDiffReceipt receipt,
        D046HermesRollbackDiffRequest request)
    {
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId,
                StringComparison.Ordinal)
            || receipt.ThroughSequence < request.LastObservedSequence
            || !string.Equals(receipt.CheckpointHash, request.CheckpointHash,
                StringComparison.Ordinal)
            || receipt.Stat is null
            || receipt.Diff is null
            || receipt.ConfirmationToken is null
            || (receipt.Succeeded
                ? string.IsNullOrWhiteSpace(receipt.ConfirmationToken)
                : receipt.ConfirmationToken.Length != 0))
        {
            throw new InvalidDataException("Rollback diff receipt was rejected.");
        }
        return receipt;
    }

    private static D046HermesRollbackRestoreReceipt ValidateRollbackRestoreReceipt(
        D046HermesRollbackRestoreReceipt receipt,
        D046HermesRollbackRestoreRequest request)
    {
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId,
                StringComparison.Ordinal)
            || receipt.ThroughSequence < request.LastObservedSequence
            || !string.Equals(receipt.CheckpointHash, request.CheckpointHash,
                StringComparison.Ordinal)
            || receipt.RestoredTo is null
            || receipt.Reason is null
            || receipt.Error is null
            || receipt.Snapshot is null
            || receipt.HistoryRemoved < 0
            || (receipt.Succeeded
                && (!receipt.WorkspaceRestored
                    || !receipt.HistoryRewound
                    || receipt.Partial
                    || receipt.Error.Length != 0))
            || (!receipt.Succeeded && string.IsNullOrWhiteSpace(receipt.Error)))
        {
            throw new InvalidDataException("Rollback restore receipt was rejected.");
        }
        return receipt;
    }

    private void ThrowIfRollbackRestoreQuarantinedLocked()
    {
        if (_rollbackRestoreQuarantined)
        {
            throw new D046HermesRollbackOperationException(
                D046HermesRollbackFailureKind.StateConflict,
                "rollback.restore.quarantined",
                "Rollback restore is quarantined until the Workbench reconnects.",
                transportUncertain: false);
        }
    }

    private D046HermesRollbackOperationException QuarantineRollbackRestore(
        D046HermesRollbackFailureKind failureKind,
        string code,
        string message,
        bool transportUncertain,
        Exception? innerException,
        D046HermesRollbackRestoreReceipt? restoreReceipt = null)
    {
        lock (_stateGate)
        {
            if (!_disposed)
                _rollbackRestoreQuarantined = true;
        }
        return new D046HermesRollbackOperationException(
            failureKind,
            code,
            message,
            transportUncertain,
            innerException,
            restoreReceipt);
    }

    private void ValidateDelegationRequest(
        string clientRequestId,
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId))
        {
            throw new ArgumentException(
                "Client request ID must not be empty.",
                nameof(clientRequestId));
        }
        ValidateBoundCommand(
            runtimeGeneration,
            taskId,
            sessionId,
            lastObservedSequence);
    }

    private static D046HermesDelegationSnapshot ValidateDelegationSnapshot(
        D046HermesDelegationSnapshot snapshot,
        D046HermesDelegationStatusRequest request)
    {
        if (snapshot is null
            || !string.Equals(
                snapshot.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(snapshot.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SessionId, request.SessionId, StringComparison.Ordinal)
            || snapshot.ThroughSequence < request.LastObservedSequence
            || snapshot.MaxSpawnDepth <= 0
            || snapshot.MaxConcurrentChildren is < 1 or > 2
            || snapshot.ActiveChildren is null
            || snapshot.ActiveChildren.Count > 2
            || snapshot.ActiveChildren.Count > snapshot.MaxConcurrentChildren)
        {
            throw new InvalidDataException("Runtime delegation snapshot was rejected.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in snapshot.ActiveChildren)
        {
            if (child is null
                || string.IsNullOrWhiteSpace(child.SubagentId)
                || !ids.Add(child.SubagentId)
                || child.State != D046HermesSubagentState.Running
                || child.Depth < 0
                || child.ToolCount < 0
                || child.StartedUtc is null
                || child.DurationSeconds is not null
                || child.Toolsets is null)
            {
                throw new InvalidDataException("Runtime delegation child was rejected.");
            }
        }
        return snapshot;
    }

    private static D046HermesSubagentSteerReceipt ValidateSubagentSteerReceipt(
        D046HermesSubagentSteerReceipt receipt,
        D046HermesSubagentSteerRequest request)
    {
        if (receipt is null
            || !string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || !string.Equals(receipt.SubagentId, request.SubagentId, StringComparison.Ordinal)
            || !string.Equals(receipt.Text, request.Text, StringComparison.Ordinal)
            || receipt.Status is not ("queued" or "rejected")
            || receipt.Queued != (receipt.Status == "queued"))
        {
            throw new InvalidDataException("Runtime subagent steer receipt was rejected.");
        }
        return receipt;
    }

    private static D046HermesSubagentInterruptReceipt ValidateSubagentInterruptReceipt(
        D046HermesSubagentInterruptReceipt receipt,
        D046HermesSubagentInterruptRequest request)
    {
        if (receipt is null
            || !string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || !string.Equals(receipt.SubagentId, request.SubagentId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Runtime subagent interrupt receipt was rejected.");
        }
        return receipt;
    }

    private static D046HermesModelSelectionReceipt ValidateModelSelectionReceipt(
        D046HermesModelSelectionReceipt receipt,
        string generation,
        D046HermesModelSelectionRequest request)
    {
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || !string.Equals(receipt.ProviderId, request.ProviderId, StringComparison.Ordinal)
            || !string.Equals(receipt.ModelId, request.ModelId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.Warning is null
            || receipt.ConfirmationMessage is null
            || (receipt.ConfirmationRequired && receipt.Accepted))
        {
            throw new InvalidDataException("Runtime model selection receipt was rejected.");
        }
        return receipt;
    }

    private static D046HermesDefaultModelSelectionReceipt
        ValidateDefaultModelSelectionReceipt(
            D046HermesDefaultModelSelectionReceipt receipt,
            string generation,
            D046HermesDefaultModelSelectionRequest request)
    {
        if (receipt is null
            || !string.Equals(
                receipt.RuntimeGeneration,
                generation,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.ProviderId,
                request.ProviderId,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.ModelId,
                request.ModelId,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.Warning is null
            || receipt.ConfirmationMessage is null
            || (receipt.ConfirmationRequired && receipt.Accepted))
        {
            throw new InvalidDataException(
                "Runtime default model selection receipt was rejected.");
        }
        return receipt;
    }

    private D046HermesCompressionReceipt ValidateCompressionReceipt(
        D046HermesCompressionReceipt receipt,
        string generation,
        D046HermesCompressionRequest request)
    {
        var hasCounts = receipt?.BeforeMessages.HasValue == true
            && receipt.AfterMessages.HasValue
            && receipt.BeforeTokens.HasValue
            && receipt.AfterTokens.HasValue;
        var hasNoCounts = receipt is not null
            && !receipt.BeforeMessages.HasValue
            && !receipt.AfterMessages.HasValue
            && !receipt.BeforeTokens.HasValue
            && !receipt.AfterTokens.HasValue;
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.Headline is null
            || receipt.TokenLine is null
            || receipt.Note is null
            || receipt.Snapshot is null
            || (!hasCounts && !hasNoCounts)
            || (receipt.Aborted && receipt.Accepted)
            || (hasCounts
                && (receipt.BeforeMessages < 0
                    || receipt.AfterMessages < 0
                    || receipt.BeforeTokens < 0
                    || receipt.AfterTokens < 0)))
        {
            throw new InvalidDataException("Runtime compression receipt was rejected.");
        }

        var snapshot = ValidateAndAdoptSnapshot(receipt.Snapshot, generation);
        return receipt with { Snapshot = snapshot };
    }

    private void ValidateTaskMutationRequest(
        string clientRequestId,
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)
            || clientRequestId.Length > 1024
            || clientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Task-mutation client request ID was rejected.",
                nameof(clientRequestId));
        }
        ValidateBoundCommand(
            runtimeGeneration,
            taskId,
            sessionId,
            lastObservedSequence);
    }

    private D046HermesDeleteTaskReceipt ValidateDeleteTaskReceipt(
        D046HermesDeleteTaskReceipt receipt,
        D046HermesDeleteTaskRequest request)
    {
        var generation = _runtimeGeneration.ToString("N");
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || receipt.LocalPreferenceWarningCode is null
            || receipt.LocalPreferenceWarningCode.Length != 0
                && (!receipt.Deleted
                    || !string.Equals(
                        receipt.LocalPreferenceWarningCode,
                        D046HermesDeleteTaskReceipt.TaskPinCleanupPendingWarningCode,
                        StringComparison.Ordinal))
            || receipt.Snapshot is null
            || receipt.RecoveryRequired && receipt.TransportUncertain
            || receipt.Deleted && (receipt.RecoveryRequired || receipt.TransportUncertain)
            || receipt.Deleted && receipt.Snapshot.Tasks.Any(task => string.Equals(
                task.TaskId,
                request.TaskId,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Runtime task-delete receipt was rejected.");
        }

        var snapshot = ValidateAndAdoptSnapshot(receipt.Snapshot, generation);
        return receipt with { Snapshot = snapshot };
    }

    private D046HermesRenameTaskReceipt ValidateRenameTaskReceipt(
        D046HermesRenameTaskReceipt receipt,
        D046HermesRenameTaskRequest request)
    {
        var generation = _runtimeGeneration.ToString("N");
        if (receipt is null
            || !string.Equals(receipt.RuntimeGeneration, generation, StringComparison.Ordinal)
            || !string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            || !string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.Code)
            || string.IsNullOrWhiteSpace(receipt.Title)
            || receipt.Snapshot is null
            || receipt.Accepted && receipt.TransportUncertain
            || receipt.Pending && !receipt.Accepted
            || receipt.Accepted && !string.Equals(
                receipt.Title,
                request.Title,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Runtime task-title receipt was rejected.");
        }

        var snapshot = ValidateAndAdoptSnapshot(receipt.Snapshot, generation);
        return receipt with { Snapshot = snapshot };
    }

    private void ValidateBoundCommand(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (!_projectionCursorEstablished
                || !string.Equals(
                    runtimeGeneration,
                    _runtimeGeneration.ToString("N"),
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(taskId)
                || string.IsNullOrWhiteSpace(sessionId))
            {
                throw new D046HermesPreDispatchRejectedException(
                    D046HermesPreDispatchRejectionReason.StaleTaskMapping,
                    "The Workbench task mapping changed before dispatch.");
            }
            if (lastObservedSequence != _lastProjectionSequence)
            {
                throw new D046HermesPreDispatchRejectedException(
                    D046HermesPreDispatchRejectionReason.StaleProjectionCursor,
                    "The Workbench projection changed before dispatch.");
            }
        }
    }

    private static void ValidateLiveProjectionEvent(
        LilithRuntimeHermesEvent source,
        D046HermesProjectionEvent value,
        Guid generation)
    {
        ValidateStoredProjectionEvent(value, generation.ToString("N"));
        var sourceTurnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
            source.Method,
            source.HermesPayload);
        var expectedScope = sourceTurnId.Length == 0
            ? D046HermesEventScopeKind.Session
            : D046HermesEventScopeKind.Turn;
        if (value.ScopeKind == D046HermesEventScopeKind.LegacyUnscoped
            || value.ScopeKind != expectedScope
            || !string.Equals(value.TurnId, sourceTurnId, StringComparison.Ordinal)
            || !string.Equals(value.SourceMethod, source.Method, StringComparison.Ordinal)
            || !string.Equals(
                value.CanonicalPayloadJson,
                source.HermesPayload.GetRawText(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes source method or canonical payload was not preserved.");
        }
    }

    private static void ValidateStoredProjectionEvent(
        D046HermesProjectionEvent value,
        string generation)
    {
        if (value is null
            || string.IsNullOrWhiteSpace(value.EventId)
            || value.Sequence <= 0
            || !string.Equals(
                value.RuntimeGeneration,
                generation,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value.TaskId)
            || string.IsNullOrWhiteSpace(value.SessionId)
            || string.IsNullOrWhiteSpace(value.SourceMethod)
            || value.Payload is null
            || string.IsNullOrWhiteSpace(value.CanonicalPayloadJson))
        {
            throw new InvalidDataException("Hermes projection event was rejected.");
        }

        var validScope = value.ScopeKind switch
        {
            D046HermesEventScopeKind.Turn =>
                HermesTurnCorrelationContract.IsIdentity(value.TurnId),
            D046HermesEventScopeKind.Session => value.TurnId.Length == 0,
            D046HermesEventScopeKind.LegacyUnscoped =>
                value.TurnId.Length == 0
                && string.Equals(
                    value.SourceMethod,
                    "session.history",
                    StringComparison.Ordinal),
            _ => false
        };
        if (!validScope)
            throw new InvalidDataException("Hermes projection event scope was rejected.");

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(
                value.CanonicalPayloadJson);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException(
                "Hermes canonical payload JSON was rejected.",
                exception);
        }
    }

    private static D046HermesRuntimeEnvelopeContext ToContext(
        LilithRuntimeEnvelope envelope)
        => new(
            envelope.GenerationId,
            envelope.RequestId,
            envelope.Sequence,
            DateTimeOffset.FromUnixTimeMilliseconds(
                envelope.SentAtUnixTimeMilliseconds));

    private ID046HermesRuntimePipeConnection GetConnection()
    {
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            return _connection
                ?? throw new InvalidOperationException("Runtime pipe is not connected.");
        }
    }

    private void ThrowIfUnavailable()
    {
        lock (_stateGate)
            ThrowIfUnavailableLocked();
    }

    private void ThrowIfUnavailableLocked()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
        if (_terminalFault is not null)
        {
            throw new InvalidOperationException(
                "Runtime pipe projection is faulted closed.",
                _terminalFault);
        }
    }

    private void FailClosed(Exception exception)
    {
        ID046HermesRuntimePipeConnection? connection;
        lock (_stateGate)
        {
            if (_disposed || _terminalFault is not null)
                return;
            _terminalFault = exception;
            connection = _connection;
            _connection = null;
            if (connection is not null)
            {
                _faultConnectionCleanupTask =
                    DisposeFaultedConnectionAsync(connection);
            }
        }
        _ready.TrySetException(exception);
        FailPending(exception);
        try
        {
            _lifetime.Cancel();
        }
        finally
        {
            // A receive-loop failure otherwise stays invisible until the next
            // command. Notify once, after closing, without allowing a broken
            // observer to interfere with transport cleanup or other observers.
            var fault = D046HermesProjectionFault.FromException(exception);
            foreach (var observer in Faulted?.GetInvocationList()
                     ?? Array.Empty<Delegate>())
            {
                try
                {
                    ((EventHandler<D046HermesProjectionFault>)observer)(this, fault);
                }
                catch
                {
                    // Presentation notification never grants transport recovery.
                }
            }
        }
    }

    private static async Task DisposeFaultedConnectionAsync(
        ID046HermesRuntimePipeConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The projection is already terminal. Cleanup must still be
            // observed without replacing the authoritative transport fault.
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var entry in _pending.ToArray())
        {
            if (_pending.TryRemove(entry.Key, out var pending))
                pending.Completion.TrySetException(exception);
        }
    }
}

internal sealed class D046PendingRuntimeResponse
{
    internal D046PendingRuntimeResponse(
        string expectedMethod,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, object> decode,
        bool isDecodeFailureIsolated = false)
    {
        ExpectedMethod = expectedMethod;
        Decode = decode;
        IsDecodeFailureIsolated = isDecodeFailureIsolated;
    }

    internal string ExpectedMethod { get; }
    internal Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, object> Decode { get; }
    internal bool IsDecodeFailureIsolated { get; }
    internal TaskCompletionSource<object> Completion { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record D046SessionlessModelDiscoveryResult(
    bool Available,
    D046HermesModelCatalog Catalog,
    string Code);

internal interface ID046HermesRuntimePipeConnectionFactory
{
    ID046HermesRuntimePipeConnection Create();
}

internal interface ID046HermesRuntimePipeConnection : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task WriteAsync(
        LilithRuntimeEnvelope envelope,
        CancellationToken cancellationToken);

    Task<LilithRuntimeEnvelope> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class D046CurrentUserRuntimePipeConnectionFactory
    : ID046HermesRuntimePipeConnectionFactory
{
    public ID046HermesRuntimePipeConnection Create()
        => new D046CurrentUserRuntimePipeConnection(
            LilithRuntimePipeEndpoint.CreateCurrentUserClient());
}

internal sealed class D046CurrentUserRuntimePipeConnection
    : ID046HermesRuntimePipeConnection
{
    private readonly NamedPipeClientStream _stream;

    internal D046CurrentUserRuntimePipeConnection(NamedPipeClientStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
        => _stream.ConnectAsync(cancellationToken);

    public Task WriteAsync(
        LilithRuntimeEnvelope envelope,
        CancellationToken cancellationToken)
        => LilithRuntimeFrameCodec.WriteAsync(
            _stream,
            envelope,
            cancellationToken);

    public Task<LilithRuntimeEnvelope> ReadAsync(CancellationToken cancellationToken)
        => LilithRuntimeFrameCodec.ReadAsync(_stream, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }
}
