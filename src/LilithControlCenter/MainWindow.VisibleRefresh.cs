using System.Windows;

namespace LilithControlCenter;

public partial class MainWindow
{
    private string _visiblePage = "Home";
    private bool _visibleRefreshRunning, _visibleRefreshRequested, _visibleRefreshManual;
    private DateTimeOffset _nextVisibleRefresh = DateTimeOffset.MinValue;
    private int _automaticHostRecoveries;

    // All entry points coalesce here. This is observation only, never a retry
    // of a setting mutation, a chat, model discovery or a remote balance read.
    internal void RequestVisiblePageRefresh()
    {
        _visibleRefreshRequested = true;
        _ = DrainVisiblePageRefreshAsync();
    }

    private async Task ObserveVisiblePageAsync()
    {
        if (_shutdownStarted || !IsVisible || WindowState == WindowState.Minimized) return;
        await RefreshAsync();
        if (!_visibleRefreshRunning && DateTimeOffset.UtcNow >= _nextVisibleRefresh)
            RequestVisiblePageRefresh();
    }

    private bool EditorsAllowHostRecovery => !HermesManagementBusy
        && !UsageBudgetSettingsPanel.Model.IsDirty && !UsageBudgetSettingsPanel.Model.IsBusy
        && !_lettersSettingsBusy
        && LilithMemoryManagementPanel.CanRetireMemoryProjection
        && LilithExpressionPanel.CanRetireExpressionProjection
        && _nativeSettings is not { IsOperationBusy: true }
        && _nativeSettings is not { HasPendingChanges: true }
        && !ProviderSetupSettingsPanel.Model.HasPendingChanges
        && !_telegramVoiceEditor.CanSave
        && HermesCredentialApiKeyInput.SecurePassword.Length == 0
        && TelegramBotTokenInput.SecurePassword.Length == 0;

    private async Task DrainVisiblePageRefreshAsync()
    {
        if (_visibleRefreshRunning || _shutdownStarted) return;
        _visibleRefreshRunning = true;
        try
        {
            while (_visibleRefreshRequested && !_shutdownStarted)
            {
                if (!IsVisible || WindowState == WindowState.Minimized) break;
                if (HermesManagementBusy || _workbenchConnectionRecovery is not null) break;
                _visibleRefreshRequested = false;
                var manual = _visibleRefreshManual;
                _visibleRefreshManual = false;
                _nextVisibleRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
                if (_hermesWorkbenchHost is { RequiresProjectionRecovery: true })
                {
                    // An open Workbench owns its own bounded automatic recovery.
                    // Never run a second owner or consume its unsent draft.
                    if (_hermesWorkbenchWindow is { IsLoaded: true })
                    {
                        if (manual) await RecoverFaultedHermesWorkbenchAsync();
                        break;
                    }
                    if (!EditorsAllowHostRecovery || (!manual && _automaticHostRecoveries >= 2)) break;
                    if (!manual) _automaticHostRecoveries++;
                    await RecoverFaultedHermesWorkbenchAsync();
                    if (_hermesWorkbenchHost is { RequiresProjectionRecovery: true }) break;
                }

                var page = _visiblePage;
                if (page == "Providers")
                {
                    await RefreshHermesCredentialStatusAsync(background: !manual);
                    if (page != _visiblePage || _shutdownStarted) continue;
                }
                // Heavy setup/catalog forms are loaded on demand, not once a second.
                if (ProviderSetupDialogIsOpen)
                    await RefreshProviderSetupAsync(background: !manual);
                if (TelegramDialogIsOpen)
                    await RefreshTelegramMessagingAsync(background: !manual);
                if (page != _visiblePage || _shutdownStarted) continue;
                if (page == "Settings" && !UsageBudgetSettingsPanel.IsKeyboardFocusWithin
                    && !UsageBudgetSettingsPanel.Model.IsDirty && !UsageBudgetSettingsPanel.Model.IsBusy)
                {
                    GetOrCreateHermesWorkbenchHost();
                    await UsageBudgetSettingsPanel.ObserveAsync();
                }
                if (page == "Interaction")
                {
                    await UpdateManualNoteSettingsAsync(false, background: !manual);
                    await UpdateLettersSettingsAsync(false, background: !manual);
                }
                if (page == "Lilith" && manual)
                {
                    if (LilithMemoryManagementPanel.CanRetireMemoryProjection)
                        await RefreshLilithMemoryManagementAsync();
                    if (!LilithExpressionPanel.HasUnsavedDraft && !LilithExpressionPanel.IsOperationBusy)
                        await LilithExpressionPanel.RefreshExpressionAsync();
                }
                if (HermesNativeToolsOverlay.IsVisible && _nativeSettings is { CanObserve: true }
                    && (_nativeSettings.NeedsReadbackRecovery || !HermesNativeToolsOverlay.IsKeyboardFocusWithin))
                {
                    if (manual) await _nativeSettings.RefreshAsync();
                    else await _nativeSettings.ObserveAsync();
                }
            }
        }
        catch (OperationCanceledException) when (_providerLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            // Individual readers retain their precise failure state. A failed
            // read must not end the timer or start a blind mutation retry.
            _nextVisibleRefresh = DateTimeOffset.UtcNow.AddSeconds(10);
        }
        finally
        {
            var earliest = DateTimeOffset.UtcNow.AddSeconds(5);
            if (_nextVisibleRefresh < earliest) _nextVisibleRefresh = earliest;
            _visibleRefreshRunning = false;
        }
    }
}
