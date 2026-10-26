using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LilithControlCenter;

internal sealed class D046HermesWorkbenchHost : IAsyncDisposable
{
    private readonly object _initializationGate = new();
    private readonly ID046HermesWorkbenchProjection _projection;
    private readonly ID054HermesCredentialProjection? _credentialProjection;
    private readonly IHermesTelegramMessagingProjection? _telegramProjection;
    private readonly IHermesTelegramVoiceProjection? _telegramVoiceProjection;
    private readonly D046HermesWorkbenchRuntimePipeProjection?
        _runtimePipeProjection;
    private readonly Dispatcher _dispatcher;
    private readonly D046HermesInitializationEventHandoff _eventHandoff = new();
    private Task? _initializationTask;
    private Task? _disposalTask;
    private bool _disposed;

    internal D046HermesWorkbenchHost(
        ID046HermesWorkbenchProjection projection,
        Dispatcher dispatcher,
        D046HermesFaultRecoveryDraft? faultRecoveryDraft = null)
    {
        _projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        _credentialProjection = projection as ID054HermesCredentialProjection;
        _telegramProjection = projection as IHermesTelegramMessagingProjection;
        _telegramVoiceProjection = projection as IHermesTelegramVoiceProjection;
        _runtimePipeProjection = projection
            as D046HermesWorkbenchRuntimePipeProjection;
        _dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        ViewModel = new D046HermesWorkbenchViewModel(
            _projection,
            new D046HermesSnapshotHandoffCallbacks(
                BeginSnapshotHandoff,
                CommitSnapshotHandoff,
                AbortSnapshotHandoff,
                FailSnapshotHandoff),
            faultRecoveryDraft);
        _projection.EventReceived += Projection_EventReceived;
        _projection.VoiceEventReceived += Projection_VoiceEventReceived;
        if (_runtimePipeProjection is not null)
        {
            _runtimePipeProjection.Faulted += Projection_Faulted;
            _runtimePipeProjection.ReadDiagnostics.Completed += TraceReadTiming;
            if (_runtimePipeProjection.TerminalFault is { } fault)
                Projection_Faulted(_runtimePipeProjection, fault);
        }
    }

    internal D046HermesWorkbenchViewModel ViewModel { get; }
    private static void TraceReadTiming(D046HermesReadTiming timing)
        => App.TraceStartup(timing.ToSafeLogLine());
    internal ID059DailyNoteApprovalProjection? DailyNoteApprovalProjection => _projection as ID059DailyNoteApprovalProjection;
    internal IProviderSetupProjection ProviderSetupProjection => _projection as IProviderSetupProjection
        ?? throw new NotSupportedException("Provider setup is unavailable.");
    internal Guid RuntimeGeneration =>
        _runtimePipeProjection?.RuntimeGeneration ?? Guid.Empty;
    internal bool RequiresProjectionRecovery => ViewModel.HasProjectionFault
        || _eventHandoff.IsFaulted
        || _runtimePipeProjection?.IsFaultedClosed == true;
    internal event Action<D046HermesProjectionEvent>? ProjectionApplied;

    internal Task<D054HermesCredentialCatalog> GetCredentialStatusAsync(
        CancellationToken cancellationToken)
        => RequireCredentialProjection().GetCredentialStatusAsync(
            cancellationToken);

    internal Task<D054HermesCredentialMutationReceipt> ReplaceCredentialAsync(
        string providerId,
        string apiKey,
        CancellationToken cancellationToken)
        => RequireCredentialProjection().ReplaceCredentialAsync(
            providerId,
            apiKey,
            cancellationToken);

    internal Task<D054HermesCredentialMutationReceipt> RemoveCredentialAsync(
        string providerId,
        CancellationToken cancellationToken)
        => RequireCredentialProjection().RemoveCredentialAsync(
            providerId,
            cancellationToken);

    internal Task<HermesTelegramStatus> GetTelegramStatusAsync(
        CancellationToken cancellationToken)
        => RequireTelegramProjection().GetTelegramStatusAsync(cancellationToken);

    internal Task<HermesTelegramVoiceStatus> GetTelegramVoiceStatusAsync(
        string userId, CancellationToken cancellationToken)
        => RequireTelegramVoiceProjection().GetTelegramVoiceStatusAsync(userId, cancellationToken);

    internal Task<HermesTelegramVoiceStatus> SetTelegramVoiceModeAsync(
        string userId, HermesTelegramVoiceMode mode, string expectedRevision,
        CancellationToken cancellationToken)
        => RequireTelegramVoiceProjection().SetTelegramVoiceModeAsync(
            userId, mode, expectedRevision, cancellationToken);

    internal Task<HermesTelegramTokenMutationReceipt> ReplaceTelegramTokenAsync(
        string botToken,
        CancellationToken cancellationToken)
        => RequireTelegramProjection().ReplaceTelegramTokenAsync(
            botToken,
            cancellationToken);

    internal Task<HermesTelegramTokenMutationReceipt> RemoveTelegramTokenAsync(
        CancellationToken cancellationToken)
        => RequireTelegramProjection().RemoveTelegramTokenAsync(cancellationToken);

    internal Task<HermesTelegramPairingCatalog> GetTelegramPairingAsync(
        CancellationToken cancellationToken)
        => RequireTelegramProjection().GetTelegramPairingAsync(cancellationToken);

    internal Task<HermesTelegramPairingApprovalReceipt>
        ApproveTelegramPairingAsync(
            string requestId,
            CancellationToken cancellationToken)
        => RequireTelegramProjection().ApproveTelegramPairingAsync(
            requestId,
            cancellationToken);

    internal Task<HermesTelegramPairingRevokeReceipt> RevokeTelegramUserAsync(
        string userId,
        CancellationToken cancellationToken)
        => RequireTelegramProjection().RevokeTelegramUserAsync(
            userId,
            cancellationToken);

    internal Task<D046HermesModelCatalog> GetDefaultModelOptionsAsync(
        CancellationToken cancellationToken)
        => _projection.GetDefaultModelOptionsAsync(cancellationToken);

    internal Task<D046HermesDefaultModelSelectionReceipt>
        SelectDefaultModelAsync(
            D046HermesDefaultModelSelectionRequest request,
            CancellationToken cancellationToken)
        => _projection.SelectDefaultModelAsync(request, cancellationToken);

    internal Task PreconnectAsync(CancellationToken cancellationToken)
        => _runtimePipeProjection?.PreconnectAsync(cancellationToken)
            ?? Task.CompletedTask;

    private ID054HermesCredentialProjection RequireCredentialProjection()
        => _credentialProjection
            ?? throw new NotSupportedException(
                "目前的背景服務不支援 API Key 管理。");

    private IHermesTelegramMessagingProjection RequireTelegramProjection()
        => _telegramProjection
            ?? throw new NotSupportedException(
                "目前的背景服務不支援 Telegram 管理。");

    private IHermesTelegramVoiceProjection RequireTelegramVoiceProjection()
        => _telegramVoiceProjection
            ?? throw new NotSupportedException("目前的背景服務不支援 Telegram 語音設定。");

    internal Task InitializeAsync()
    {
        if (_dispatcher.CheckAccess())
            return InitializeOnDispatcher();

        return _dispatcher.InvokeAsync(InitializeOnDispatcher).Task.Unwrap();
    }

    private Task InitializeOnDispatcher()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_initializationGate)
            return _initializationTask ??= InitializeAndVerifyHandoffAsync();
    }

    private async Task InitializeAndVerifyHandoffAsync()
    {
        try
        {
            await ViewModel.InitializeAsync();
            _eventHandoff.EnsureBaselineEstablished();
        }
        catch (Exception exception)
        {
            _eventHandoff.Fail(exception);
            throw;
        }
    }

    private D046HermesSnapshotHandoffEpoch BeginSnapshotHandoff()
    {
        RequireDispatcherForSnapshotHandoff();
        while (true)
        {
            DrainEventHandoff();
            if (_eventHandoff.TryBeginSnapshotEpoch(out var epoch))
                return epoch;
        }
    }

    private void CommitSnapshotHandoff(D046HermesSnapshotHandoffEpoch epoch)
    {
        RequireDispatcherForSnapshotHandoff();
        if (_eventHandoff.CommitSnapshotEpoch(epoch))
            DrainEventHandoff();
    }

    private void AbortSnapshotHandoff(
        D046HermesSnapshotHandoffEpoch epoch,
        Exception exception)
    {
        RequireDispatcherForSnapshotHandoff();
        if (_eventHandoff.AbortSnapshotEpoch(epoch, exception))
            DrainEventHandoff();
    }

    private void FailSnapshotHandoff(
        D046HermesSnapshotHandoffEpoch epoch,
        Exception exception)
    {
        RequireDispatcherForSnapshotHandoff();
        _eventHandoff.FailSnapshotEpoch(epoch, exception);
    }

    private void RequireDispatcherForSnapshotHandoff()
    {
        if (_dispatcher.CheckAccess())
            return;
        var exception = new InvalidOperationException(
            "The Hermes snapshot event handoff left the UI dispatcher.");
        _eventHandoff.Fail(exception);
        throw exception;
    }

    private void Projection_Faulted(object? sender, D046HermesProjectionFault fault)
    {
        if (!ReferenceEquals(sender, _runtimePipeProjection))
            return;
        void ApplyFault()
        {
            if (_disposed)
                return;
            try { App.TraceStartup(fault.ToSafeLogLine(ViewModel.ProjectionDiagnosticPhase)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { /* Diagnostic storage must never prevent the normal fault UI. */ }
            _eventHandoff.Fail(new IOException("Runtime projection channel lost."));
            ViewModel.NotifyProjectionFault(fault);
        }
        if (_dispatcher.CheckAccess())
            ApplyFault();
        else if (!_dispatcher.HasShutdownStarted)
            _ = _dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(ApplyFault));
    }

    private void Projection_EventReceived(
        object? sender,
        D046HermesProjectionEvent value)
    {
        D046HermesInitializationEnqueueDisposition disposition;
        try
        {
            disposition = _eventHandoff.EnqueueProjection(value);
        }
        catch (InvalidOperationException) when (_eventHandoff.IsFaulted)
        {
            return;
        }
        ScheduleEventHandoffDrain(disposition);
    }

    private void Projection_VoiceEventReceived(
        object? sender,
        D046HermesVoiceEvent value)
    {
        D046HermesInitializationEnqueueDisposition disposition;
        try
        {
            disposition = _eventHandoff.EnqueueVoice(value);
        }
        catch (InvalidOperationException) when (_eventHandoff.IsFaulted)
        {
            return;
        }
        ScheduleEventHandoffDrain(disposition);
    }

    private void ScheduleEventHandoffDrain(
        D046HermesInitializationEnqueueDisposition disposition)
    {
        if (disposition is not D046HermesInitializationEnqueueDisposition.ScheduleDrain)
            return;
        if (_dispatcher.CheckAccess())
        {
            DrainEventHandoff();
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new Action(DrainEventHandoff));
        }
        catch (Exception exception)
        {
            _eventHandoff.Fail(exception);
            throw;
        }
    }

    private void DrainEventHandoff()
    {
        // A transport fault can already have retired the queue while an older
        // dispatcher drain was waiting. Do not turn that notification into a
        // second unhandled UI exception.
        if (_disposed || _eventHandoff.IsFaulted)
            return;
        if (!_dispatcher.CheckAccess())
        {
            var exception = new InvalidOperationException(
                "The Hermes event handoff drain left the UI dispatcher.");
            _eventHandoff.Fail(exception);
            throw exception;
        }

        try
        {
            while (_eventHandoff.TryTakeNext(out var item))
            {
                switch (item)
                {
                    case
                    {
                        Kind: D046HermesInitializationEventKind.Projection,
                        Projection: not null,
                        Voice: null
                    }:
                        ViewModel.Consume(item.Projection);
                        ProjectionApplied?.Invoke(item.Projection);
                        break;
                    case
                    {
                        Kind: D046HermesInitializationEventKind.Voice,
                        Projection: null,
                        Voice: not null
                    }:
                        ViewModel.ConsumeVoiceEvent(item.Voice);
                        break;
                    default:
                        throw new InvalidDataException(
                            "The Hermes snapshot event handoff was not discriminated.");
                }
            }
        }
        catch (Exception exception)
        {
            _eventHandoff.Fail(exception);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool>? owner = null;
        Task disposalTask;
        lock (_initializationGate)
        {
            if (_disposalTask is not null)
                return new ValueTask(_disposalTask);
            owner = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            disposalTask = owner.Task;
            _disposalTask = disposalTask;
        }

        // Publish the shared task before starting user/transport cleanup so a
        // synchronous re-entrant DisposeAsync call joins the same operation.
        _ = CompleteDisposeAsync(owner);
        return new ValueTask(disposalTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource<bool> owner)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            owner.TrySetResult(true);
        }
        catch (Exception exception)
        {
            owner.TrySetException(exception);
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (!_eventHandoff.Close())
            return;
        _disposed = true;
        _projection.EventReceived -= Projection_EventReceived;
        _projection.VoiceEventReceived -= Projection_VoiceEventReceived;
        if (_runtimePipeProjection is not null)
        {
            _runtimePipeProjection.Faulted -= Projection_Faulted;
            _runtimePipeProjection.ReadDiagnostics.Completed -= TraceReadTiming;
        }
        await ViewModel.ShutdownVoiceAsync();
        ViewModel.Dispose();
        await _projection.DisposeAsync().ConfigureAwait(false);
    }
}
