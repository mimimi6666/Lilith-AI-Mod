namespace LilithControlCenter;

public partial class MainWindow
{
    private async Task RefreshLocalCompatibilityAsync()
    {
        if (_shutdownStarted || _workbenchConnectionRecovery is not null) return;
        try { GetOrCreateHermesWorkbenchHost(); await LocalCompatibilitySettingsPanel.RefreshAsync(); }
        catch (Exception error) when (error is not OutOfMemoryException) { LocalCompatibilitySettingsPanel.Detach(); }
    }
}
