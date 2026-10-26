using System.Windows;
using LilithRuntime.Configuration;

namespace LilithControlCenter;

public partial class MainWindow
{
    private D059ManualNoteSettingsModel? _manualNotesModel;
    private D059ManualNoteLoadResult? _manualNotesReadback;
    private readonly D059ManualNoteSettingsOperations _manualNotesOperations = new();
    private string? _manualNotesMessage;

    private async void ManualNotes_Loaded(object sender, RoutedEventArgs e)
        => await UpdateManualNoteSettingsAsync(false);
    private async void ManualNotes_Click(object sender, RoutedEventArgs e)
        => await UpdateManualNoteSettingsAsync(true);

    private async Task UpdateManualNoteSettingsAsync(bool save, bool background = false)
    {
        if (ManualNotesEnabled is null || _shutdownStarted) return;
        if (background && ManualNotesEnabled.IsKeyboardFocusWithin) return;
        if (save && (_manualNotesOperations.SavePending
            || _manualNotesReadback is null || _manualNotesReadback.RequiresExplicitRepair)) return;
        var selected = ManualNotesEnabled.IsChecked == true;
        var revision = _manualNotesReadback?.Snapshot.Revision ?? 0;
        var readEpoch = _manualNotesOperations.IntentEpoch;
        if (save) ManualNotesEnabled.IsEnabled = false;
        try
        {
            _manualNotesModel ??= new(D059ManualNotePreferencesStore.ForCurrentUser());
            var model = _manualNotesModel;
            D059ManualNoteLoadResult latest;
            if (save)
            {
                var result = await _manualNotesOperations.SaveAsync(revision, selected,
                    (expected, value) => Task.Run(() => model.Save(expected, value, DateTimeOffset.UtcNow)));
                if (result is null) return;
                latest = result.Readback;
                _manualNotesMessage = result.Outcome switch
                {
                    D059ManualNoteSaveOutcome.Saved => "manual.notes.saved",
                    D059ManualNoteSaveOutcome.Conflict or D059ManualNoteSaveOutcome.Changed => "manual.notes.changed",
                    D059ManualNoteSaveOutcome.InvalidPreferences => "manual.notes.invalid",
                    _ => "manual.notes.unavailable"
                };
            }
            else
            {
                var loaded = await _manualNotesOperations.ReadAsync(() => Task.Run(model.Load));
                if (loaded is null || !_manualNotesOperations.CanPublishRead(readEpoch)) return;
                latest = loaded;
            }
            if (_shutdownStarted) return;
            var changed = _manualNotesReadback is null
                || latest.Disposition != _manualNotesReadback.Disposition
                || latest.Snapshot.Revision != _manualNotesReadback.Snapshot.Revision
                || latest.Snapshot.Enabled != _manualNotesReadback.Snapshot.Enabled;
            _manualNotesReadback = latest;
            if (!save && !changed) return;
            if (!save) _manualNotesMessage = latest.RequiresExplicitRepair ? "manual.notes.invalid" : null;
            ManualNotesEnabled.IsChecked = D059ManualNoteSettingsModel.SelectionForEditor(latest.Snapshot.Enabled);
            RenderManualNoteSettingsStatus();
        }
        catch
        {
            if (!save && !_manualNotesOperations.CanPublishRead(readEpoch)) return;
            _manualNotesReadback = null;
            _manualNotesMessage = "manual.notes.unavailable";
            RenderManualNoteSettingsStatus();
        }
        finally
        {
            if (!_shutdownStarted && !_manualNotesOperations.SavePending
                && (save || _manualNotesOperations.CanPublishRead(readEpoch)))
                ManualNotesEnabled.IsEnabled = _manualNotesReadback is { RequiresExplicitRepair: false };
        }
    }

    private void RenderManualNoteSettingsStatus()
    {
        if (ManualNotesStatusText is null) return;
        if (_manualNotesReadback is null || _manualNotesReadback.RequiresExplicitRepair)
        {
            ManualNotesStatusText.Text = Texts[_manualNotesMessage ?? "manual.notes.unavailable"];
            return;
        }
        var value = _manualNotesReadback?.Snapshot.Enabled;
        var state = value is null ? "manual.notes.legacy" : value.Value ? "manual.notes.on" : "manual.notes.off";
        ManualNotesStatusText.Text = (_manualNotesMessage is null ? "" : Texts[_manualNotesMessage] + " ")
            + Texts[state];
    }
}
