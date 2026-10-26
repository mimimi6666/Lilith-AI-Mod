using System.Windows;

namespace LilithControlCenter;

public partial class MainWindow
{
    private SettingsSurfaceWindow? _providerSetupDialog;
    private SettingsSurfaceWindow? _telegramSettingsDialog;
    private bool _settingsDialogLifetimeAttached;

    private bool ProviderSetupDialogIsOpen => _providerSetupDialog?.IsVisible == true;
    private bool TelegramDialogIsOpen => _telegramSettingsDialog?.IsVisible == true;

    private bool AdmitSettingsHostRetirement(SettingsMutationSurface origin)
    {
        var conflict = SettingsMutationDraftGuard.ConflictLabel(origin, new(
            HermesCredentialApiKeyInput.SecurePassword.Length > 0,
            ProviderSetupSettingsPanel.Model.HasPendingChanges,
            TelegramBotTokenInput.SecurePassword.Length > 0,
            _telegramVoiceEditor.CanSave || _telegramVoiceEditor.IsBusy,
            UsageBudgetSettingsPanel.Model.IsDirty || UsageBudgetSettingsPanel.Model.IsBusy,
            _nativeSettings is { HasPendingChanges: true } or { IsOperationBusy: true },
            !LilithMemoryManagementPanel.CanRetireMemoryProjection,
            !LilithExpressionPanel.CanRetireExpressionProjection));
        if (conflict is null) return true;
        // This is a local pre-dispatch refusal, never an uncertain save result.
        var message = Texts.Format("settings.dialog.pendingOther", Texts[conflict]);
        if (origin == SettingsMutationSurface.Credentials) HermesCredentialStatusText.Text = message;
        else if (origin == SettingsMutationSurface.TelegramToken) TelegramStatusText.Text = message;
        else ProviderSetupSettingsPanel.Model.SetStatus("settings.dialog.pendingOtherShort");
        return false;
    }

    private async void OpenCustomProviderSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted) return;
        AttachSettingsDialogLifetime();
        if (_providerSetupDialog is null)
        {
            SettingsDialogParkingPanel.Children.Remove(ProviderSetupSettingsPanel);
            var dialog = new SettingsSurfaceWindow(ProviderSetupSettingsPanel, Texts["provider.setup.title"],
                () => _providerSetupOperation, "provider.setup.title") { Owner = this };
            _providerSetupDialog = dialog;
            dialog.Closed += (_, _) =>
            {
                dialog.DetachEditor();
                ProviderSetupSettingsPanel.ClearSecret();
                SettingsDialogParkingPanel.Children.Add(ProviderSetupSettingsPanel);
                if (ReferenceEquals(_providerSetupDialog, dialog)) _providerSetupDialog = null;
            };
        }
        _providerSetupDialog.ShowEditor();
        // Reopening a retained draft is not a request to discard it. The
        // normal refresh coordinator handles freshness without recreating UI.
        if (ProviderSetupSettingsPanel.Model.Catalog is null)
            await RefreshProviderSetupAsync();
    }

    private async void OpenTelegramSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted) return;
        AttachSettingsDialogLifetime();
        if (_telegramSettingsDialog is null)
        {
            SettingsDialogParkingPanel.Children.Remove(TelegramMessagingCard);
            var dialog = new SettingsSurfaceWindow(TelegramMessagingCard, "Telegram",
                () => _telegramMessagingBusy || _telegramVoiceEditor.IsBusy) { Owner = this };
            _telegramSettingsDialog = dialog;
            dialog.Closed += (_, _) =>
            {
                dialog.DetachEditor();
                TelegramBotTokenInput.Clear();
                SettingsDialogParkingPanel.Children.Add(TelegramMessagingCard);
                if (ReferenceEquals(_telegramSettingsDialog, dialog)) _telegramSettingsDialog = null;
            };
        }
        _telegramSettingsDialog.ShowEditor();
        if (!_telegramStatusAuthoritative && TelegramBotTokenInput.SecurePassword.Length == 0
            && !_telegramVoiceEditor.CanSave)
            await RefreshTelegramMessagingAsync();
    }

    private void AttachSettingsDialogLifetime()
    {
        if (_settingsDialogLifetimeAttached) return;
        _settingsDialogLifetimeAttached = true;
        IsVisibleChanged += SettingsDialogOwnerVisibilityChanged;
        Closed += (_, _) =>
        {
            IsVisibleChanged -= SettingsDialogOwnerVisibilityChanged;
            ProviderSetupSettingsPanel.ClearSecret();
            TelegramBotTokenInput.Clear();
        };
    }

    private void SettingsDialogOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) return;
        // Tray hiding does not persist or discard non-secret drafts, nor does
        // it cancel a dispatched write. Secrets never remain in hidden UI.
        ProviderSetupSettingsPanel.ClearSecret();
        TelegramBotTokenInput.Clear();
        _providerSetupDialog?.Hide();
        _telegramSettingsDialog?.Hide();
    }

    private void CloseSettingsDialogsForShutdown()
    {
        _providerSetupDialog?.CloseForOwnerShutdown();
        _telegramSettingsDialog?.CloseForOwnerShutdown();
        ProviderSetupSettingsPanel.ClearSecret();
        TelegramBotTokenInput.Clear();
    }
}
