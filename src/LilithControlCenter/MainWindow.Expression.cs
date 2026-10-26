namespace LilithControlCenter;

public partial class MainWindow
{
    private bool _expressionExitConfirmed;
    private void InitializeExpressionEditor()
    {
        LilithExpressionPanel.EnsureConnection = () =>
        {
            if (_shutdownStarted || HermesManagementBusy || _hermesDefaultModelBusy)
                throw new InvalidOperationException("Connection recovery is active.");
            _ = GetOrCreateHermesWorkbenchHost();
        };
        LilithExpressionPanel.ReconcileConnection = async () =>
        {
            if (!_shutdownStarted && _hermesWorkbenchHost is { RequiresProjectionRecovery: true } host
                && !OwnsConnectionRecoveryHost(host))
                await RetireFaultedHermesWorkbenchHostAsync(host);
        };
    }
    private bool ProtectExpressionBeforeExit()
    {
        if (_expressionExitConfirmed) { _expressionExitConfirmed = false; return true; }
        if (LilithExpressionPanel.IsOperationBusy)
        {
            ShowFromTray();
            SelectPageCore("Lilith", refresh: false);
            LilithExpressionPanel.ShowExitBusy();
            return false;
        }
        if (!LilithExpressionPanel.HasUnsavedDraft) return true;
        ShowFromTray();
        SelectPageCore("Lilith", refresh: false);
        LilithExpressionPanel.ShowExitConfirmation(() => { _expressionExitConfirmed = true; RequestExit(); });
        return false;
    }
}
