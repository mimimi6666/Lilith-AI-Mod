using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace LilithControlCenter;

public partial class MainWindow
{
    private bool _playerSaveBusy;
    private bool _playerSaveImportUncertain;

    // These are offline data operations. Do not gate them on Runtime readiness,
    // model availability, credentials, or the network-management state.
    private async void PlayerSaveExport_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || _playerSaveBusy || _playerSaveImportUncertain
            || ControlCenterPlayerSaveClient.RestartRequired) return;
        SetPlayerSaveBusy(true);
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = Texts["playerSave.export"],
                Filter = Texts["playerSave.filter"],
                DefaultExt = ".lilithsave",
                FileName = "Lilith-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".lilithsave",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true
            };
            if (dialog.ShowDialog(this) != true || _shutdownStarted)
                return;
            await RunPlayerSaveAsync(dialog.FileName, import: false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SetPlayerSaveStatus("playerSave.exportFailed");
        }
        finally { SetPlayerSaveBusy(false); }
    }

    private async void PlayerSaveImport_Click(object sender, RoutedEventArgs e)
    {
        if (_shutdownStarted || _playerSaveBusy || _playerSaveImportUncertain
            || ControlCenterPlayerSaveClient.RestartRequired) return;
        SetPlayerSaveBusy(true);
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = Texts["playerSave.import"],
                Filter = Texts["playerSave.filter"],
                DefaultExt = ".lilithsave",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true || _shutdownStarted)
                return;
            var confirmation = new ThemedConfirmationDialog(
                Texts["playerSave.confirmTitle"],
                Texts["playerSave.confirmBody"],
                Texts["playerSave.confirmButton"])
            {
                Owner = this
            };
            if (confirmation.ShowDialog() != true || _shutdownStarted)
                return;
            await RunPlayerSaveAsync(dialog.FileName, import: true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Exceptions before the client is invoked cannot have mutated data.
            // RunPlayerSaveAsync separately handles every uncertain client outcome.
            SetPlayerSaveStatus("playerSave.fileDialogFailed");
        }
        finally { SetPlayerSaveBusy(false); }
    }

    private async Task RunPlayerSaveAsync(string path, bool import)
    {
        SetPlayerSaveStatus(import ? "playerSave.importing" : "playerSave.exporting");
        try
        {
            if (!await PreparePlayerSaveAsync()) return;
            var client = new ControlCenterPlayerSaveClient();
            var result = import
                ? await client.ImportAsync(path, CancellationToken.None)
                : await client.ExportAsync(path, CancellationToken.None);
            if (result.Succeeded)
            {
                SetPlayerSaveStatus(import ? "playerSave.imported" : "playerSave.exported",
                    result.ConversationCount, result.MessageCount, result.MemoryCount, result.EpisodeCount);
                return;
            }
            var key = result.Code switch
            {
                "player_save.busy" => "playerSave.busy",
                "player_save.invalid" => "playerSave.invalid",
                "player_save.conversion_failed" => "playerSave.conversionFailed",
                "player_save.newer_format" => "playerSave.newerFormat",
                "player_save.io_failed" => "playerSave.ioFailed",
                "player_save.unavailable" => "playerSave.unavailable",
                "player_save.cancelled" => "playerSave.cancelled",
                _ => import ? "playerSave.importUncertain" : "playerSave.exportFailed"
            };
            if (key == "playerSave.importUncertain") _playerSaveImportUncertain = true;
            SetPlayerSaveStatus(key);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A lost response is not evidence that an overwrite did not happen.
            // Never retry an import automatically or display a false no-change claim.
            if (import) _playerSaveImportUncertain = true;
            SetPlayerSaveStatus(import ? "playerSave.importUncertain" : "playerSave.exportFailed");
        }
    }

    private void SetPlayerSaveBusy(bool busy)
    {
        _playerSaveBusy = busy;
        var restartRequired = ControlCenterPlayerSaveClient.RestartRequired;
        PlayerSaveExportButton.IsEnabled = !busy && !_playerSaveImportUncertain && !restartRequired && !_shutdownStarted;
        PlayerSaveImportButton.IsEnabled = !busy && !_playerSaveImportUncertain && !restartRequired && !_shutdownStarted;
        PlayerSaveProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PlayerSaveRestartText.Visibility = restartRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPlayerSaveStatus(string key, params object[] values)
    {
        // Bind only product-owned messages, so changing the UI language also
        // updates the most recent status without touching paths or player data.
        PlayerSaveStatusText.SetBinding(TextBlock.TextProperty,
            new Binding(nameof(ControlCenterLocalization.Language))
            {
                Source = Texts,
                Mode = BindingMode.OneWay,
                Converter = new PlayerSaveStatusConverter(key, values)
            });
    }

    private sealed class PlayerSaveStatusConverter(string key, object[] values) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => values.Length == 0 ? Texts[key] : Texts.Format(key, values);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
