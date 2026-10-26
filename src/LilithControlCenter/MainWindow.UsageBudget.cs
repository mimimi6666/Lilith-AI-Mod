namespace LilithControlCenter;

public partial class MainWindow
{
    private async Task RefreshUsageBudgetAsync()
    {
        if (_shutdownStarted || _workbenchConnectionRecovery is not null) return;
        try
        {
            GetOrCreateHermesWorkbenchHost();
            await UsageBudgetSettingsPanel.RefreshAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { UsageBudgetSettingsPanel.Detach(); }
    }
}
