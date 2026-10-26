using System;
using System.Threading.Tasks;

namespace LilithControlCenter;

public partial class MainWindow
{
    private static readonly TimeSpan WorkbenchRecoveryObservationTimeout = TimeSpan.FromSeconds(5);
    private WorkbenchConnectionRecovery? _workbenchConnectionRecovery;
    internal bool WorkbenchOwnerIsShuttingDown => _shutdownStarted;

    private sealed class WorkbenchConnectionRecovery(
        D046HermesWorkbenchWindow window, D046HermesWorkbenchHost previous, bool restoreFocus)
    {
        internal D046HermesWorkbenchWindow Window { get; } = window;
        internal D046HermesWorkbenchHost Previous { get; } = previous;
        internal Task? Cleanup { get; set; }
        internal D046HermesWorkbenchHost? Candidate { get; set; }
        internal bool CandidateBound { get; set; }
        internal Task? Initialization { get; set; }
        internal bool Observing { get; set; }
        internal bool RestoreFocus { get; } = restoreFocus;
    }

    private bool OwnsConnectionRecoveryHost(D046HermesWorkbenchHost host)
        => _workbenchConnectionRecovery is { } recovery
            && (ReferenceEquals(host, recovery.Previous)
                || ReferenceEquals(host, recovery.Candidate));

    internal bool CanAutomaticallyRecoverWorkbenchWindow(D046HermesWorkbenchWindow window)
        => !_shutdownStarted && ReferenceEquals(_hermesWorkbenchWindow, window) && window.IsLoaded
            && _workbenchConnectionRecovery is null && _hermesWorkbenchHost is { } previous
            && ReferenceEquals(window.BoundHost, previous) && previous.RequiresProjectionRecovery
            && window.CanBeginConnectionRecovery && !HermesManagementBusy && !_hermesDefaultModelBusy
            && EditorsAllowHostRecovery && !_telegramVoiceEditor.CanSave
            && LilithMemoryManagementPanel.CanRetireMemoryProjection
            && LilithExpressionPanel.CanRetireExpressionProjection
            && _nativeSettings is not { CanRefreshNativeTools: false }
            && _nativeSettings is not { CanCancelNativeTools: true };

    internal async Task ReconnectHermesWorkbenchWindowAsync(
        D046HermesWorkbenchWindow window, bool automatic = false)
    {
        if (_shutdownStarted || !ReferenceEquals(_hermesWorkbenchWindow, window) || !window.IsLoaded)
            return;

        var recovery = _workbenchConnectionRecovery;
        if (recovery is not null && (!ReferenceEquals(recovery.Window, window) || recovery.Observing))
            return;
        if (recovery is null)
        {
            var previous = _hermesWorkbenchHost;
            if (previous is not null && ReferenceEquals(window.BoundHost, previous)
                && !previous.RequiresProjectionRecovery)
            {
                // A general page refresh is not a failed reconnect. Never
                // accuse a healthy connection of pending user operations.
                window.ClearHealthyConnectionRecoveryNotice();
                return;
            }
            if (previous is null || !ReferenceEquals(window.BoundHost, previous)
                || !window.CanBeginConnectionRecovery
                || HermesManagementBusy || _hermesDefaultModelBusy
                || !EditorsAllowHostRecovery || _telegramVoiceEditor.CanSave
                || !LilithMemoryManagementPanel.CanRetireMemoryProjection
                || !LilithExpressionPanel.CanRetireExpressionProjection
                || _nativeSettings is { CanRefreshNativeTools: false }
                || _nativeSettings is { CanCancelNativeTools: true })
            {
                window.ReportConnectionRecovery("chat.reconnect.waitForOperations");
                return;
            }
            if (!previous.ViewModel.TryCaptureReconnectDraft(out var draft))
            {
                window.ReportConnectionRecovery("chat.reconnect.draftBlocked");
                return;
            }
            if (_hermesFaultRecoveryDraft is not null)
            {
                window.ReportConnectionRecovery("chat.reconnect.draftBlocked");
                return;
            }

            // Transfer only a complete bounded unsent draft. Never a request,
            // approval receipt, attachment capability, or native operation.
            _hermesFaultRecoveryDraft = draft;
            recovery = new WorkbenchConnectionRecovery(window, previous, restoreFocus: !automatic);
            _workbenchConnectionRecovery = recovery;
            // The shared memory projection must stay idle throughout the
            // first awaited teardown step, not just when it is detached.
            LilithMemoryManagementPanel.IsEnabled = false;
            LilithExpressionPanel.IsEnabled = false;
            try { window.BeginConnectionRecovery(); }
            catch (Exception)
            {
                _workbenchConnectionRecovery = null;
                _hermesFaultRecoveryDraft = null;
                LilithMemoryManagementPanel.IsEnabled = true;
                LilithExpressionPanel.IsEnabled = true;
                window.AbortConnectionRecoveryBeforeRetirement();
                return;
            }
            recovery.Cleanup = RetireConnectionRecoveryOwnerAsync(recovery);
        }

        recovery.Observing = true;
        window.SetConnectionRecoveryObservation(true);
        try
        {
            window.ReportConnectionRecovery("chat.reconnect.retiring");
            // Five seconds changes presentation to waiting, not ownership.
            // Continue observing this exact operation automatically, bounded
            // to thirty seconds; never create another cleanup or owner.
            if (!await WorkbenchConnectionRecoveryPolicy.ObserveSameOperationAsync(
                recovery.Cleanup!, WorkbenchRecoveryObservationTimeout,
                () => IsCurrentConnectionRecovery(recovery),
                () => window.ReportConnectionRecovery("chat.reconnect.cleanupWaiting")))
                return;

            if (recovery.Candidate is null)
            {
                recovery.Candidate = GetOrCreateHermesWorkbenchHost(
                    ensureRuntimeStarted: false, connectionRecoveryOwner: true);
            }
            if (!recovery.CandidateBound)
            {
                window.ReplaceConnectionHost(recovery.Previous, recovery.Candidate);
                recovery.CandidateBound = true;
            }
            if (recovery.Initialization is null)
            {
                recovery.Initialization = recovery.Candidate.InitializeAsync();
            }
            window.ReportConnectionRecovery("chat.reconnect.connecting");
            if (!await WorkbenchConnectionRecoveryPolicy.ObserveSameOperationAsync(
                recovery.Initialization!, WorkbenchRecoveryObservationTimeout,
                () => IsCurrentConnectionRecovery(recovery),
                () => window.ReportConnectionRecovery("chat.reconnect.connectionWaiting")))
                return;
            if (recovery.Candidate.RequiresProjectionRecovery || !recovery.Candidate.ViewModel.HasAttestedConnection)
                throw new InvalidOperationException("Fresh connection is not attested.");

            _workbenchConnectionRecovery = null;
            window.CompleteConnectionRecovery(connected: true, restoreFocus: recovery.RestoreFocus);
            RequestVisiblePageRefresh();
        }
        catch (TimeoutException)
        {
            if (IsCurrentConnectionRecovery(recovery))
                window.ReportConnectionRecovery(recovery.Candidate is null
                    ? "chat.reconnect.cleanupWaiting" : "chat.reconnect.connectionWaiting");
        }
        catch (Exception)
        {
            if (!IsCurrentConnectionRecovery(recovery))
                return;
            if (recovery.Candidate is null && _hermesWorkbenchHost is { } partiallyAttached
                && !ReferenceEquals(partiallyAttached, recovery.Previous))
                recovery.Candidate = partiallyAttached;
            if (recovery.Candidate is { } candidate && recovery.Initialization?.IsCompleted == true)
            {
                candidate.ViewModel.NotifyProjectionFault(new D046HermesProjectionFault("projection_failed"));
                _workbenchConnectionRecovery = null;
                window.CompleteConnectionRecovery(connected: false);
            }
            else
            {
                // A failed/unfinished cleanup cannot be bypassed. Keep this
                // owner and its draft so an explicit retry observes it again.
                window.ReportConnectionRecovery("chat.reconnect.cleanupFailed");
            }
        }
        finally
        {
            recovery.Observing = false;
            if (!_shutdownStarted && _workbenchConnectionRecovery is null)
            {
                LilithMemoryManagementPanel.IsEnabled = true;
                LilithExpressionPanel.IsEnabled = true;
            }
            if (!_shutdownStarted && ReferenceEquals(_hermesWorkbenchWindow, window))
                window.SetConnectionRecoveryObservation(false);
        }
    }

    private bool IsCurrentConnectionRecovery(WorkbenchConnectionRecovery recovery)
        => !_shutdownStarted && ReferenceEquals(_workbenchConnectionRecovery, recovery)
            && ReferenceEquals(_hermesWorkbenchWindow, recovery.Window) && recovery.Window.IsLoaded;

    private async Task RetireConnectionRecoveryOwnerAsync(WorkbenchConnectionRecovery recovery)
    {
        await StopDailyNoteApprovalAsync(recovery.Previous);
        if (!ReferenceEquals(_hermesWorkbenchHost, recovery.Previous))
            throw new InvalidOperationException("Connection owner changed during retirement.");
        DetachHermesWorkbenchHostPresentation(recovery.Previous, recovery.Window);
        await recovery.Previous.DisposeAsync();
    }
}
