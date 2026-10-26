using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

internal sealed partial class D046HermesWorkbenchViewModel
{
    public string ChatApprovalModeText => !_nativeToolsCatalogReady
        ? ControlCenterLocalization.Current["ui.approval.menu"]
        : _savedNativeApprovalMode switch
        {
            D046HermesNativeApprovalMode.Manual => ControlCenterLocalization.Current["approval.manual"] + " ▾",
            D046HermesNativeApprovalMode.Smart => ControlCenterLocalization.Current["approval.smart"] + " ▾",
            D046HermesNativeApprovalMode.Off => ControlCenterLocalization.Current["approval.off"] + " ▾",
            _ => ControlCenterLocalization.Current["ui.approval.unknown"]
        };

    public bool CanChangeChatApprovalMode => CanEditNativeTools
        && !_nativeToolsDirty
        && !HasPendingApproval && !HasPendingClarification && !HasPendingSecureInput
        && !HasActiveSubagents && !IsVoiceCaptureBusy && !HasPendingImage;

    public bool IsChatApprovalManual => _nativeToolsCatalogReady
        && _savedNativeApprovalMode == D046HermesNativeApprovalMode.Manual;
    public bool IsChatApprovalSmart => _nativeToolsCatalogReady
        && _savedNativeApprovalMode == D046HermesNativeApprovalMode.Smart;
    public bool IsChatApprovalOff => _nativeToolsCatalogReady
        && _savedNativeApprovalMode == D046HermesNativeApprovalMode.Off;

    internal async Task<bool> ChangeChatApprovalModeAsync(D046HermesNativeApprovalMode mode)
    {
        if (!CanChangeChatApprovalMode || !Enum.IsDefined(typeof(D046HermesNativeApprovalMode), mode))
            return false;
        var task = SelectedTask;
        var scope = new D046HermesSessionStatusCorrelation(
            _runtimeGeneration, task?.TaskId ?? string.Empty, task?.SessionId ?? string.Empty);
        if (task is null || !await RefreshNativeToolsAsync()
            || !CanChangeChatApprovalMode || !ReferenceEquals(task, SelectedTask)
            || !scope.Matches(_runtimeGeneration, task.TaskId, task.SessionId))
            return false;
        if (_savedNativeApprovalMode == mode)
            return true;

        // A player-originated click changes only approval mode. Refresh above
        // replaces drafts with native truth; dirty toolset edits block entry.
        SelectedNativeApprovalMode = NativeApprovalModes.Single(option => option.Value == mode);
        if (!await ApplyNativeToolsAsync())
            return false;

        // Re-enter the same durable conversation through authenticated native
        // activation/read-back. Never delete history or silently create a chat.
        var resumed = await RunSnapshotCommandAsync(
            () => CanCreateOrSwitchTaskDuringSnapshotCommand()
                && string.Equals(_activeTaskId, task.TaskId, StringComparison.Ordinal),
            () => _projection.OpenTaskAsync(task.TaskId, _lifetime.Token),
            "正在套用核准方式並重新連接目前聊天…");
        if (!resumed || _activeSessionConfigStale || !_nativeSurfaceAttested)
        {
            _nativeToolsStatusText = "設定已保存，但目前聊天尚未完成重新驗證；請重新連線。";
            RaiseNativeToolsProperties();
            return false;
        }
        var refreshed = await RefreshNativeToolsAsync();
        return refreshed && _savedNativeApprovalMode == mode;
    }

    private void RaiseChatApprovalProperties()
    {
        OnPropertyChanged(nameof(ChatApprovalModeText));
        OnPropertyChanged(nameof(CanChangeChatApprovalMode));
        OnPropertyChanged(nameof(IsChatApprovalManual));
        OnPropertyChanged(nameof(IsChatApprovalSmart));
        OnPropertyChanged(nameof(IsChatApprovalOff));
    }
}

public partial class D046HermesWorkbenchWindow
{
    private void ChatApprovalMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !_viewModel.CanChangeChatApprovalMode)
            return;

        // Opening the menu only presents the last confirmed native setting.
        // A selected mode is refreshed and revalidated before any native write.
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.DataContext = _viewModel;
        button.ContextMenu.IsOpen = true;
    }

    private async void ChatApprovalMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;
        var mode = (item.Tag as string) switch
        {
            "manual" => D046HermesNativeApprovalMode.Manual,
            "smart" => D046HermesNativeApprovalMode.Smart,
            "off" => D046HermesNativeApprovalMode.Off,
            _ => (D046HermesNativeApprovalMode?)null
        };
        if (mode.HasValue)
            _ = await _viewModel.ChangeChatApprovalModeAsync(mode.Value);
    }
}
