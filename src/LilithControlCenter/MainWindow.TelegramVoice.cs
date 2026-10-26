using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class MainWindow
{
    private readonly HermesTelegramVoiceEditorState _telegramVoiceEditor = new();
    private CancellationTokenSource? _telegramVoiceOperation;

    private async void TelegramVoiceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || HermesManagementBusy
            || sender is not Button { Tag: HermesTelegramApprovedUser user }
            || !IsTelegramVoiceAccountApproved(user.UserId))
            return;
        InvalidateTelegramVoiceEditor(clearSelection: true);
        _telegramVoiceEditor.Select(user.UserId);
        TelegramVoiceAccountText.Text = $"語音回覆 · Telegram 帳號 {user.UserId}";
        TelegramVoicePanel.Visibility = Visibility.Visible;
        await RunTelegramVoiceOperationAsync(save: false);
    }

    private async void TelegramVoiceRefresh_Click(object sender, RoutedEventArgs e)
        => await RunTelegramVoiceOperationAsync(save: false);

    private async void TelegramVoiceSave_Click(object sender, RoutedEventArgs e)
        => await RunTelegramVoiceOperationAsync(save: true);

    private void TelegramVoiceDraft_Click(object sender, RoutedEventArgs e)
    {
        _telegramVoiceEditor.Edit(
            TelegramVoiceEnabledCheckBox.IsChecked == true,
            TelegramVoiceEveryReplyCheckBox.IsChecked == true);
        TelegramVoiceStatusText.Text = _telegramVoiceEditor.CanSave
            ? "尚未儲存；按「儲存語音設定」後才會生效。" : "目前與已讀取的設定相同。";
        UpdateTelegramVoiceControls();
    }

    private void TelegramVoiceCancel_Click(object sender, RoutedEventArgs e)
    {
        InvalidateTelegramVoiceEditor(clearSelection: false);
        TelegramVoiceStatusText.Text = "已停止等待；若儲存已送出，設定仍可能已變更。請讀取設定確認。";
    }

    private bool IsTelegramVoiceAccountApproved(string userId)
        => _telegramStatusAuthoritative && _telegramStatus?.Configured == true
            && TelegramApprovedUserList.ItemsSource is IEnumerable<HermesTelegramApprovedUser> users
            && users.Any(user => string.Equals(user.UserId, userId, StringComparison.Ordinal));

    private async Task RunTelegramVoiceOperationAsync(bool save)
    {
        if (_shutdownStarted || HermesManagementBusy
            || _telegramVoiceEditor.UserId is not { } userId
            || !IsTelegramVoiceAccountApproved(userId)
            || (save && !_telegramVoiceEditor.CanSave))
            return;
        var expected = _telegramVoiceEditor.Status;
        var desired = _telegramVoiceEditor.DraftMode;
        var epoch = _telegramVoiceEditor.Begin();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_providerLifetime.Token);
        operation.CancelAfter(TimeSpan.FromSeconds(30));
        _telegramVoiceOperation = operation;
        D046HermesWorkbenchHost? host = null;
        UpdateTelegramMessagingControls();
        UpdateHermesCredentialControls();
        TelegramVoiceStatusText.Text = save
            ? "正在儲存並確認語音設定…" : "正在讀取語音回覆設定…";
        try
        {
            host = GetOrCreateHermesWorkbenchHost();
            var status = save
                ? await host.SetTelegramVoiceModeAsync(userId, desired, expected!.Revision, operation.Token)
                : await host.GetTelegramVoiceStatusAsync(userId, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_shutdownStarted || !ReferenceEquals(_hermesWorkbenchHost, host)
                || !IsTelegramVoiceAccountApproved(userId)
                || !_telegramVoiceEditor.Apply(epoch, status))
            {
                if (_telegramVoiceEditor.IsCurrent(epoch, userId))
                {
                    _telegramVoiceEditor.Invalidate();
                    TelegramVoiceStatusText.Text = "連線或帳號狀態已更新；請重新讀取設定確認。";
                }
                return;
            }
            TelegramVoiceConfigurationText.Text = status.Configured
                ? $"使用控制中心的{(status.Locale == HermesTelegramVoiceLocale.Ja ? "日文" : "中文")}聲線；文字回覆不變。語音是否可用，請在 Telegram 試聽確認。"
                : "目前沒有可用的控制中心聲線設定；語音暫不可用，文字回覆仍保留。";
            TelegramVoiceStatusText.Text = save
                ? "語音設定已儲存；請在 Telegram 確認播放效果。"
                : "已讀取此帳號的語音設定。";
        }
        catch (Exception exception)
        {
            if (!_shutdownStarted && _telegramVoiceEditor.IsCurrent(epoch, userId))
            {
                _telegramVoiceEditor.Invalidate();
                TelegramVoiceStatusText.Text = save
                    ? "目前無法確認儲存結果；設定可能已變更，請讀取設定確認後再儲存。"
                    : exception is OperationCanceledException
                        ? "讀取已停止或逾時；請重新讀取設定。"
                        : "目前無法讀取語音設定；請確認服務與帳號狀態後再試。";
                TelegramVoiceConfigurationText.Text = "聲線設定目前未確認。";
            }
            // A sent request that is cancelled or times out retires the shared
            // pipe. Do not send the next read back into that same failed host.
            // This cleanup also applies after Stop waiting invalidated the epoch.
            if (!_shutdownStarted && host is not null
                && ReferenceEquals(_hermesWorkbenchHost, host)
                && host.RequiresProjectionRecovery)
            {
                try
                {
                    await RetireFaultedHermesWorkbenchHostAsync(host);
                    if (!_shutdownStarted && _hermesWorkbenchHost is null)
                        TelegramStatusText.Text = "背景服務連線已重設；請重新整理 Telegram，再開啟此帳號的語音設定。"
                            + (save ? "若儲存已送出，設定仍可能已變更；重新讀取後才能確認。" : string.Empty);
                }
                catch (Exception)
                {
                    if (!_shutdownStarted && ReferenceEquals(_hermesWorkbenchHost, host))
                        TelegramStatusText.Text = "背景服務尚未重新連線；請稍後重新整理，再確認語音設定。";
                }
            }
        }
        finally
        {
            _telegramVoiceEditor.Finish(epoch);
            if (ReferenceEquals(_telegramVoiceOperation, operation))
                _telegramVoiceOperation = null;
            if (!_shutdownStarted)
            {
                UpdateTelegramMessagingControls();
                UpdateHermesCredentialControls();
            }
        }
    }

    private void InvalidateTelegramVoiceEditor(bool clearSelection)
    {
        _telegramVoiceOperation?.Cancel();
        if (clearSelection)
        {
            _telegramVoiceEditor.Select(null);
            TelegramVoicePanel.Visibility = Visibility.Collapsed;
        }
        else
            _telegramVoiceEditor.Invalidate();
        TelegramVoiceConfigurationText.Text = "聲線設定尚未讀取。";
        UpdateTelegramMessagingControls();
        UpdateHermesCredentialControls();
    }

    private void UpdateTelegramVoiceControls()
    {
        // Click alone changes the draft. Applying a remote snapshot never writes.
        var mode = _telegramVoiceEditor.DraftMode;
        var editable = !HermesManagementBusy && _telegramVoiceEditor.Status is not null
            && _telegramVoiceEditor.UserId is { } userId && IsTelegramVoiceAccountApproved(userId);
        TelegramVoiceEnabledCheckBox.IsChecked = mode != HermesTelegramVoiceMode.Off;
        TelegramVoiceEveryReplyCheckBox.IsChecked = mode == HermesTelegramVoiceMode.All;
        TelegramVoiceEnabledCheckBox.IsEnabled = editable;
        TelegramVoiceEveryReplyCheckBox.IsEnabled = editable && mode != HermesTelegramVoiceMode.Off;
        TelegramVoiceRefreshButton.IsEnabled = !HermesManagementBusy
            && _telegramVoiceEditor.UserId is { } selected && IsTelegramVoiceAccountApproved(selected);
        TelegramVoiceSaveButton.IsEnabled = editable && _telegramVoiceEditor.CanSave;
        TelegramVoiceCancelButton.Visibility = _telegramVoiceEditor.IsBusy
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
