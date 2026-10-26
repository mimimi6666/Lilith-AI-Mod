using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LilithControlCenter;

public partial class D046HermesWorkbenchWindow
{
    private long _connectionBindingEpoch;
    private int _boundWindowOperations;
    private bool _connectionRecoveryPending;
    private bool _connectionRecoveryObserving;
    private D046HermesWorkbenchHost? _connectionRecoveryPreviousHost;
    private bool _connectionReplacementCommitted;
    private bool _connectionCallbacksAttached;
    private bool _connectionWindowClosed;
    private string _connectionRecoveryStatusKey = string.Empty;
    private readonly WorkbenchConnectionRecoveryPolicy _automaticRecoveryPolicy = new();
    private bool _automaticRecoveryScheduled;
    private bool _connectionRecoveryLoadedHookAttached;
    private (string RequestId, D046HermesSecureInputKind InputKind)? _displayedSecureInputIdentity;

    internal D046HermesWorkbenchHost BoundHost => _host;
    internal bool CanBeginConnectionRecovery => !_connectionRecoveryPending
        && !_connectionWindowClosed && _boundWindowOperations == 0 && _viewModel.CanReconnect;

    private void AttachConnectionCallbacks()
    {
        if (_connectionCallbacksAttached) throw new InvalidOperationException("Connection callbacks already attached.");
        _connectionCallbacksAttached = true;
        if (!_connectionRecoveryLoadedHookAttached)
        {
            _connectionRecoveryLoadedHookAttached = true;
            Loaded += (_, _) => QueueAutomaticConnectionRecovery();
        }
        _host.ProjectionApplied += Host_ProjectionApplied;
        _viewModel.PropertyChanged += ConnectionViewModel_PropertyChanged;
        _viewModel.ConversationScopeChanging += ViewModel_ConversationScopeChanging;
        _viewModel.TimelineChanging += ViewModel_TimelineChanging;
        _viewModel.TimelineChanged += ViewModel_TimelineChanged;
        _viewModel.TaskRailChanging += ViewModel_TaskRailChanging;
        _viewModel.TaskRailChanged += ViewModel_TaskRailChanged;
        HermesSecureInputPasswordBox.Clear();
        _displayedSecureInputIdentity = _viewModel.SecureInputIdentity;
        RefreshConnectionRecoveryControls();
    }

    private void DetachConnectionCallbacks()
    {
        if (!_connectionCallbacksAttached) return;
        _connectionCallbacksAttached = false;
        _host.ProjectionApplied -= Host_ProjectionApplied;
        _viewModel.PropertyChanged -= ConnectionViewModel_PropertyChanged;
        _viewModel.ConversationScopeChanging -= ViewModel_ConversationScopeChanging;
        _viewModel.TimelineChanging -= ViewModel_TimelineChanging;
        _viewModel.TimelineChanged -= ViewModel_TimelineChanged;
        _viewModel.TaskRailChanging -= ViewModel_TaskRailChanging;
        _viewModel.TaskRailChanged -= ViewModel_TaskRailChanged;
        HermesSecureInputPasswordBox.Clear();
        _displayedSecureInputIdentity = null;
    }

    private void ConnectionViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _viewModel) && _connectionCallbacksAttached)
        {
            if (e.PropertyName == nameof(D046HermesWorkbenchViewModel.SecureInputIdentity)
                && _displayedSecureInputIdentity != _viewModel.SecureInputIdentity)
            {
                // PasswordBox is intentionally not bound to a string property.
                // A value belongs only to this request, never the next card or
                // a replacement Runtime connection (even if an id is reused).
                HermesSecureInputPasswordBox.Clear();
                _displayedSecureInputIdentity = _viewModel.SecureInputIdentity;
            }
            RefreshConnectionRecoveryControls();
        }
    }

    internal IDisposable TrackBoundOperation()
    {
        ++_boundWindowOperations;
        RefreshConnectionRecoveryControls();
        return new BoundWindowOperation(this);
    }

    private sealed class BoundWindowOperation(D046HermesWorkbenchWindow window) : IDisposable
    {
        private D046HermesWorkbenchWindow? _window = window;
        public void Dispose()
        {
            var owner = _window;
            if (owner is null) return;
            _window = null;
            --owner._boundWindowOperations;
            owner.RefreshConnectionRecoveryControls();
        }
    }

    internal void BeginConnectionRecovery()
    {
        if (!CanBeginConnectionRecovery) throw new InvalidOperationException("Window operations have not settled.");
        _connectionRecoveryPending = true;
        _connectionRecoveryPreviousHost = _host;
        _connectionReplacementCommitted = false;
        _runtimeGenerationRetired = true;
        ++_connectionBindingEpoch;
        ++_timelineRenderRequest;
        ++_taskRailRenderRequest;
        _timelineAnchor = null;
        _taskRailAnchor = null;
        _voiceMousePressed = false;
        _voiceKeyboardPressed = false;
        if (Mouse.Captured is not null) Mouse.Capture(null);
        _sendCue.Stop();
        _hermesAudioPlayer.Stop();
        _hermesAudioPlayer.Close();
        CloseRenameTaskOverlay(restoreFocus: false);
        CloseDeleteTaskOverlay(restoreFocus: false);
        HermesChatApprovalModeButton.ContextMenu.IsOpen = false;
        foreach (var item in HermesTaskList.Items)
            if (HermesTaskList.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListBoxItem { ContextMenu: { } menu })
                menu.IsOpen = false;
        DetachConnectionCallbacks();
        // Only the captured, task/lineage-bound draft leaves this view model.
        // Disconnect bindings before Dispose clears the old VM's composer.
        DataContext = null;
        HermesConnectedContent.IsEnabled = false;
        ReportConnectionRecovery("chat.reconnect.retiring");
    }

    internal void ReplaceConnectionHost(D046HermesWorkbenchHost expected, D046HermesWorkbenchHost replacement)
    {
        if (!_connectionRecoveryPending || _connectionReplacementCommitted
            || !ReferenceEquals(expected, _connectionRecoveryPreviousHost)
            || ReferenceEquals(expected, replacement)
            || (!ReferenceEquals(_host, expected) && !ReferenceEquals(_host, replacement))
            || _connectionWindowClosed || _boundWindowOperations != 0)
            throw new InvalidOperationException("Window connection ownership changed.");
        DetachConnectionCallbacks();
        _host = replacement;
        _viewModel = replacement.ViewModel;
        _runtimeGenerationRetired = false;
        ++_connectionBindingEpoch;
        _timelineAutoFollow = true;
        DataContext = _viewModel;
        AttachConnectionCallbacks();
        _connectionReplacementCommitted = true;
    }

    internal void AbortConnectionRecoveryBeforeRetirement()
    {
        // Called only before old-host disposal starts: its original draft is
        // still authoritative in memory, even if a visual detach partly failed.
        _connectionRecoveryPending = false;
        _runtimeGenerationRetired = false;
        DataContext = _viewModel;
        if (!_connectionCallbacksAttached) AttachConnectionCallbacks();
        HermesConnectedContent.IsEnabled = true;
        ReportConnectionRecovery("chat.reconnect.failed");
    }

    internal void CompleteConnectionRecovery(bool connected, bool restoreFocus = true)
    {
        _connectionRecoveryPending = false;
        HermesConnectedContent.IsEnabled = true;
        ReportConnectionRecovery(connected ? "chat.reconnect.connected" : "chat.reconnect.failed");
        if (connected && restoreFocus) { FocusPendingApprovalOrComposer(); ScrollTimelineToEnd(); }
    }

    internal void SetConnectionRecoveryObservation(bool observing)
    {
        _connectionRecoveryObserving = observing;
        RefreshConnectionRecoveryControls();
    }

    internal void ReportConnectionRecovery(string key)
    {
        _connectionRecoveryStatusKey = key;
        RefreshConnectionRecoveryControls();
    }

    internal void ClearHealthyConnectionRecoveryNotice()
    {
        if (_connectionRecoveryPending || _host.RequiresProjectionRecovery) return;
        _connectionRecoveryStatusKey = string.Empty;
        RefreshConnectionRecoveryControls();
    }

    private void QueueAutomaticConnectionRecovery()
    {
        if (_automaticRecoveryScheduled || _connectionWindowClosed || !IsLoaded
            || _connectionRecoveryPending || _connectionRecoveryObserving
            || !_host.RequiresProjectionRecovery || Owner is not MainWindow owner
            || owner.WorkbenchOwnerIsShuttingDown
            || !_automaticRecoveryPolicy.TryReserveAutomaticAttempt(_host)) return;

        _automaticRecoveryScheduled = true;
        _ = ObserveAutomaticConnectionRecoveryAsync(owner, _host, _connectionBindingEpoch);
    }

    private async Task ObserveAutomaticConnectionRecoveryAsync(
        MainWindow owner, D046HermesWorkbenchHost expected, long epoch)
    {
        try
        {
            // One bounded readiness episode per failed owner. Busy controls,
            // unsaved edits and attachments remain untouched while waiting.
            for (var observation = 0;
                observation < WorkbenchConnectionRecoveryPolicy.MaximumReadinessObservations;
                ++observation)
            {
                await Task.Delay(WorkbenchConnectionRecoveryPolicy.ReadinessObservationInterval);
                if (_connectionWindowClosed || !IsLoaded || !ReferenceEquals(Owner, owner)
                    || owner.WorkbenchOwnerIsShuttingDown || !ReferenceEquals(_host, expected)
                    || _connectionBindingEpoch != epoch || _connectionRecoveryPending
                    || !_host.RequiresProjectionRecovery) return;
                if (!owner.CanAutomaticallyRecoverWorkbenchWindow(this)) continue;
                await owner.ReconnectHermesWorkbenchWindowAsync(this, automatic: true);
                return;
            }
        }
        catch (Exception)
        {
            // Retain the typed projection fault and manual recovery entry.
            // Automatic presentation recovery never escalates or resends.
            if (!_connectionWindowClosed && ReferenceEquals(_host, expected))
                ReportConnectionRecovery("chat.reconnect.failed");
        }
        finally
        {
            _automaticRecoveryScheduled = false;
            // A replacement can itself fail. The policy allows at most one
            // more automatic owner, never an unbounded reconnect carousel.
            QueueAutomaticConnectionRecovery();
        }
    }

    private void RefreshConnectionRecoveryControls()
    {
        if (_connectionWindowClosed || HermesConnectionRecoveryPanel is null) return;
        var needsRecovery = _host.RequiresProjectionRecovery;
        var visible = needsRecovery || _connectionRecoveryPending || _connectionRecoveryStatusKey.Length != 0;
        HermesConnectionRecoveryPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var key = _connectionRecoveryStatusKey.Length == 0 ? "chat.reconnect.available" : _connectionRecoveryStatusKey;
        HermesConnectionRecoveryStatusText.Text = ControlCenterLocalization.Current[key];
        HermesConnectionRecoveryButton.Content = ControlCenterLocalization.Current[
            _connectionRecoveryPending ? "chat.reconnect.retry" : "chat.reconnect.button"];
        HermesConnectionRecoveryButton.Visibility = needsRecovery || _connectionRecoveryPending ? Visibility.Visible : Visibility.Collapsed;
        HermesConnectionRecoveryButton.IsEnabled = Owner is MainWindow
            && !_connectionRecoveryObserving && (_connectionRecoveryPending || CanBeginConnectionRecovery);
        QueueAutomaticConnectionRecovery();
    }

    private async void ConnectionRecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow owner && !_connectionRecoveryObserving)
            await owner.ReconnectHermesWorkbenchWindowAsync(this);
    }

    private void ConnectionRecoveryWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_connectionRecoveryPending && Owner is MainWindow { WorkbenchOwnerIsShuttingDown: false })
        {
            e.Cancel = true;
            ReportConnectionRecovery("chat.reconnect.closeWaiting");
        }
    }
}
