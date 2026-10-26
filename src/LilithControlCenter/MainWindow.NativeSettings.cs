using System.Windows;
using System.Windows.Controls;

namespace LilithControlCenter;

public partial class MainWindow
{
    private NativeToolsSettingsViewModel? _nativeSettings;
    private void NativeToolPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) _nativeSettings?.SelectPreset(id);
    }
    private async void NativeSettingsNewChat_Click(object sender, RoutedEventArgs e)
        => await OpenNativeSetupChatAsync(null);
    private async void HermesNativeSetupGuide_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: D046HermesNativeToolsetViewModel row }) await OpenNativeSetupChatAsync(row);
    }
    private async Task OpenNativeSetupChatAsync(D046HermesNativeToolsetViewModel? row)
    {
        if (_shutdownStarted || _nativeSettings is not { } settings
            || !settings.TryBeginChatSetup(requiresSetupHelper: row is not null)) return;
        var opened = false;
        try
        {
            var host = GetOrCreateHermesWorkbenchHost();
            await host.InitializeAsync();
            // An explicit new-chat command preserves every old transcript and draft.
            if (!await host.ViewModel.CreateNativeSettingsChatAsync()) return;
            if (_shutdownStarted) return;
            if (row is not null)
                host.ViewModel.DraftText = Texts.Format("chat.setupPrompt", row.Label, row.Name,
                    row.SelectedSetupProvider?.Name ?? "");
            settings.MarkFreshChat();
            HermesNativeToolsOverlay.Visibility = Visibility.Collapsed;
            OpenWorkbench_Click(this, new RoutedEventArgs());
            opened = true;
        }
        catch (Exception) { /* Keep the editor visible with a localized, retryable failure. */ }
        finally { settings.FinishChatSetup(opened); }
    }
}

internal sealed partial class D046HermesWorkbenchViewModel
{
    internal Task<bool> CreateNativeSettingsChatAsync() => CreateTaskWithSelectionAsync(null);
    internal void NativeSettingsSaved()
    {
        InvalidateCapabilityCatalog();
        if (SelectedTask is not null)
        {
            _activeSessionConfigStale = true;
            _nativeSurfaceAttested = false;
            if (HasPendingImage) ClearPendingImage(ControlCenterLocalization.Current["tools.pending"]);
        }
        _nativeToolsActivationText = ControlCenterLocalization.Current["tools.pending"];
        RaiseNativeToolsProperties();
        SetCommandState(false);
    }
}
