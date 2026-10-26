namespace LilithControlCenter;

internal enum D046HermesDeleteOutcome
{
    NotStarted,
    NotDispatched,
    AwaitingResult,
    Rejected,
    ConfirmedDeleted,
    Unknown
}

internal enum D046HermesDeleteStage
{
    TargetActivation,
    VoicePreparation,
    Dispatch,
    Receipt,
    Replacement
}

internal sealed partial class D046HermesWorkbenchViewModel
{
    private bool _recoveryDisposed;
    private string? _recoveryBlockKey;
    private string _deleteFailureCode = string.Empty;
    internal D046HermesDeleteOutcome LastDeleteOutcome { get; private set; }
    internal D046HermesDeleteStage LastDeleteStage { get; private set; }
    public bool NeedsConnectionRecovery => _projectionFault;
    internal bool HasAttestedConnection => _isConnected && !_projectionFault
        && !_sequenceGapDetected && !_recoveryDisposed;

    // Only retire a settled local owner. A stale snapshot saying Streaming is
    // not permission to stop/replay that turn; fresh initialization must ask
    // Runtime for current truth. All outstanding local operations must settle.
    public bool CanReconnect => _projectionFault && !_recoveryDisposed
        && !_lifetime.IsCancellationRequested
        && !_commandInFlight && !_stopInFlight && !_taskPinInFlight
        && !_approvalResponseInFlight && !_clarificationResponseInFlight
        && !_secureInputResponseInFlight && !_nativeToolsInFlight
        && !_observabilityInFlight && !_delegationStatusInFlight
        && !_delegationControlInFlight && !IsRollbackOperationInFlight
        && !_voiceLifecycleInFlight && !_voiceStartInFlight && !_voiceStopInFlight
        && !IsVoiceCaptureBusy && !_isApplyingSnapshot
        && !_capabilityCatalogCache.IsReading;

    public string ConnectionRecoveryBlockText => _recoveryBlockKey is null
        ? string.Empty : ControlCenterLocalization.Current[_recoveryBlockKey];

    public string DeleteResultText
    {
        get
        {
            var key = LastDeleteOutcome switch
            {
                D046HermesDeleteOutcome.NotStarted or D046HermesDeleteOutcome.NotDispatched
                    => _projectionFault ? "chat.delete.notSentRecovery" : "chat.delete.notDispatched",
                D046HermesDeleteOutcome.ConfirmedDeleted => "chat.delete.confirmedRecovery",
                D046HermesDeleteOutcome.Rejected => "chat.delete.rejectedRecovery",
                _ => "chat.delete.unconfirmed"
            };
            var message = ControlCenterLocalization.Current[key];
            // Closed stage/code vocabulary only: no exception text, title,
            // prompt, session identifier or credential in failure diagnostics.
            return _deleteFailureCode.Length == 0 ? message
                : message + " · " + LastDeleteStage.ToString().ToLowerInvariant()
                    + "/" + _deleteFailureCode;
        }
    }

    internal bool TryCaptureReconnectDraft(out D046HermesFaultRecoveryDraft? recovery)
    {
        recovery = null;
        _recoveryBlockKey = null;
        OnPropertyChanged(nameof(ConnectionRecoveryBlockText));
        // Images are scope-bound disposable bytes, not represented by the
        // historical draft's had-image hint. Never silently discard them.
        if (HasPendingImage)
            return BlockReconnectDraft("chat.reconnect.imagePending");
        var pending = _faultRecoveryDrafts.PendingForRecapture;
        if (pending is not null)
        {
            if (DraftText.Length != 0
                && (!_faultRecoveryDrafts.IsBoundTo(SelectedTask?.TaskId, SelectedTask?.LineageId)
                    || !string.Equals(DraftText, pending.DraftText, StringComparison.Ordinal)))
                return BlockReconnectDraft("chat.reconnect.draftConflict");
            recovery = pending;
            return true;
        }
        if (DraftText.Length == 0)
            return true;
        if (SelectedTask is not { } task || task.TaskId != _activeTaskId
            || !D046HermesFaultRecoveryDraft.TryCreate(task.TaskId, task.LineageId,
                DraftText, hadPendingImage: false, out recovery))
            return BlockReconnectDraft("chat.reconnect.draftUnbound");
        return true;
    }

    private bool BlockReconnectDraft(string key)
    {
        _recoveryBlockKey = key;
        OnPropertyChanged(nameof(ConnectionRecoveryBlockText));
        return false;
    }

    private void BeginDeleteOutcome()
    {
        LastDeleteOutcome = D046HermesDeleteOutcome.NotDispatched;
        LastDeleteStage = D046HermesDeleteStage.TargetActivation;
        _deleteFailureCode = string.Empty;
    }

    private void RecordDeleteFailure(Exception exception)
    {
        if (LastDeleteOutcome == D046HermesDeleteOutcome.AwaitingResult)
            LastDeleteOutcome = D046HermesDeleteOutcome.Unknown;
        _deleteFailureCode = exception is D046HermesPreDispatchRejectedException
            ? "not_dispatched" : D046HermesProjectionFault.FromException(exception).Code;
        OnPropertyChanged(nameof(DeleteResultText));
    }

    private static bool IsConfirmedDeleteReceipt(D046HermesDeleteTaskReceipt receipt)
        => receipt.Deleted && !receipt.TransportUncertain && !receipt.RecoveryRequired
            && receipt.Snapshot is not null
            && !receipt.Snapshot.Tasks.Any(task => task.TaskId == receipt.TaskId);
}
