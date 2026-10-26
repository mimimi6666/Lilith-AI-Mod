namespace LilithControlCenter;

internal sealed partial class D046HermesWorkbenchViewModel
{
    private void RefreshLanguage()
    {
        var texts = ControlCenterLocalization.Current;
        // Fixed UI status copy only. Transport faults, history, drafts, native
        // messages, summaries and tool arguments are deliberately left alone.
        _statusText = texts.RefreshOwnedUiLabel(_statusText);
        _compressionStatusText = texts.RefreshOwnedUiLabel(_compressionStatusText);
        _observabilityStatusText = texts.RefreshOwnedUiLabel(_observabilityStatusText);
        _usageStatusText = texts.RefreshOwnedUiLabel(_usageStatusText);
        _contextStatusText = texts.RefreshOwnedUiLabel(_contextStatusText);
        _compressionCountStatusText = texts.RefreshOwnedUiLabel(_compressionCountStatusText);
        _learningStatusText = texts.RefreshOwnedUiLabel(_learningStatusText);
        _delegationStatusText = texts.RefreshOwnedUiLabel(_delegationStatusText);
        _rollbackStatusText = texts.RefreshOwnedUiLabel(_rollbackStatusText);
        _nativeToolsStatusText = texts.RefreshOwnedUiLabel(_nativeToolsStatusText);
        _nativeToolsActivationText = texts.RefreshOwnedUiLabel(_nativeToolsActivationText);
        _approvalStatusText = texts.RefreshOwnedUiLabel(_approvalStatusText);
        _capabilityCatalogStatusText = texts.RefreshOwnedUiLabel(_capabilityCatalogStatusText);
        _capabilityCatalogBoundaryText = texts.RefreshOwnedUiLabel(_capabilityCatalogBoundaryText);
        _capabilityCatalogSummaryText = texts.RefreshOwnedUiLabel(_capabilityCatalogSummaryText);
        foreach (var task in Tasks) task.RefreshLanguage();
        foreach (var toolset in NativeToolsets) toolset.RefreshLanguage();
        foreach (var mode in NativeApprovalModes) mode.RefreshLanguage();
        foreach (var turn in Turns) turn.RefreshLanguage();
        RefreshCapabilityCatalogLanguage();
        OnPropertyChanged(null);
    }
}
