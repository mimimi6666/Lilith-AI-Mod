using System.Windows;

namespace LilithControlCenter;

public partial class MainWindow
{
    private bool _playerSavePrepared;
    private bool _playerSaveExitInProgress;

    private async Task<bool> PreparePlayerSaveAsync()
    {
        if (_playerSavePrepared) return true;
        var chat = _hermesWorkbenchHost?.ViewModel;
        var liveProjection = _hermesWorkbenchHost is { RequiresProjectionRecovery: false };
        if (_shutdownStarted || _workbenchConnectionRecovery is not null
            || _hermesFaultRecoveryDraft is not null
            || LilithExpressionPanel.HasUnsavedDraft || LilithExpressionPanel.IsOperationBusy
            || !LilithMemoryManagementPanel.CanRetireMemoryProjection
            || _nativeSettings?.HasPendingChanges == true || _nativeSettings?.IsOperationBusy == true
            || _hermesCredentialBusy || _hermesDefaultModelBusy || _telegramMessagingBusy || _telegramVoiceEditor.IsBusy
            || (chat is not null && (chat.DraftText.Length != 0 || chat.HasPendingImage || chat.IsFaultRecoveryDraftPending
                || (liveProjection && (chat.HasRunningConversation || chat.HasActiveSubagents || chat.IsVoiceCaptureBusy))
                || chat.IsRollbackOperationInFlight || chat.CanCancelNativeTools)))
        {
            SetPlayerSaveStatus("playerSave.finishDraft");
            return false;
        }

        // Once projections are detached they cannot be reused, even if the offline
        // operation fails. No provider token is used by the independent save client.
        _playerSavePrepared = true;
        _shutdownStarted = true;
        ControlCenterPlayerSaveState.RequireRestart();
        _liveTelemetryTimer.Stop();
        HermesCapabilityCatalogOverlay.Visibility = Visibility.Collapsed;
        HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
        if (_hermesWorkbenchWindow is { } window)
        {
            window.IsEnabled = false;
            window.Close();
        }
        foreach (var page in _pages.Where(pair => pair.Key != "Settings")) page.Value.IsEnabled = false;
        _shutdownTask = ShutdownOwnedResourcesAsync();
        await _shutdownTask;
        return true;
    }

    private async Task CloseAfterPlayerSaveAsync()
    {
        if (_playerSaveBusy || _playerSaveExitInProgress) return;
        _playerSaveExitInProgress = true;
        try
        {
            if (Application.Current is App && !await ControlCenterRuntimeExit.StopAsync())
            {
                MessageBox.Show(this, Texts["tray.exitFailed"], "Lilith", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DisposeDesktopIntegration();
            _shutdownCompleted = true;
            ScheduleFinalClose();
        }
        finally { _playerSaveExitInProgress = false; }
    }
}
