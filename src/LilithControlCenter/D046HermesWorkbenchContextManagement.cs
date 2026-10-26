using System.IO;

namespace LilithControlCenter;

internal sealed partial class D046HermesWorkbenchViewModel
{
    private sealed record ContextManagementResult(D046HermesWorkbenchSnapshot Snapshot,
        D046HermesRenameTaskReceipt? Rename, D046HermesDeleteTaskReceipt? Delete, bool NotDispatched = false);

    // Native mutation still admits and attests the exact target session. Its
    // intermediate snapshots are never applied to the visible conversation.
    // The existing snapshot handoff buffers those events until the original
    // conversation is reattested. No second session/database authority.
    private async Task<ContextManagementResult?> ManageOtherTaskAsync(string taskId, string? title)
    {
        if (!CanSwitchTask || !_isConnected || _projectionFault || _sequenceGapDetected
            || SelectedTask is not { CanManage: true } original || original.TaskId != _activeTaskId
            || !Tasks.Any(t => t.TaskId == taskId && t.CanManage)) return null;
        var normalized = title?.Trim();
        if (title is not null && (string.IsNullOrEmpty(normalized) || normalized.Any(char.IsControl))) return null;
        var generation = _runtimeGeneration;
        string? targetSessionId = null;
        D046HermesDeleteTaskReceipt? confirmedDelete = null;
        SetCommandState(true, ControlCenterLocalization.Current["chat.managingOther"]);
        try
        {
            var result = await ExecuteSnapshotHandoffAsync(
                () => CanCreateOrSwitchTaskDuringSnapshotCommand() && SelectedTask == original,
                async () =>
                {
                    async Task VerifyVoiceOff(string task, string session)
                    {
                        LastDeleteStage = D046HermesDeleteStage.VoicePreparation;
                        var request = CreateVoiceRequest(generation, task, session);
                        var status = await _projection.GetVoiceStatusAsync(request, _lifetime.Token);
                        void Verify(D046HermesVoiceStatusReceipt state)
                        {
                            if (state.RuntimeGeneration != generation || state.TaskId != task || state.SessionId != session)
                                throw new InvalidDataException("Context management voice scope mismatch.");
                        }
                        Verify(status);
                        if (status.Enabled)
                        {
                            var disabled = await _projection.DisableVoiceAsync(request, _lifetime.Token);
                            if (disabled.RuntimeGeneration != generation || disabled.TaskId != task
                                || disabled.SessionId != session || disabled.Enabled)
                                throw new InvalidDataException("Context management voice shutdown unverified.");
                            status = await _projection.GetVoiceStatusAsync(request, _lifetime.Token);
                            Verify(status);
                        }
                        if (status.Enabled) throw new InvalidDataException("Context management requires voice off.");
                    }
                    await VerifyVoiceOff(original.TaskId, original.SessionId);
                    LastDeleteStage = D046HermesDeleteStage.TargetActivation;
                    var targetSnapshot = await _projection.OpenTaskAsync(taskId, _lifetime.Token);
                    var target = targetSnapshot.Tasks.Single(t => t.TaskId == taskId);
                    targetSessionId = target.SessionId;
                    if (targetSnapshot.RuntimeGeneration != generation || targetSnapshot.SelectedTaskId != taskId
                        || !targetSnapshot.NativeSurfaceAttested || target.State is not (D046HermesTaskState.Idle or D046HermesTaskState.Failed))
                        throw new InvalidDataException("Context management target was not idle and attested.");
                    await VerifyVoiceOff(target.TaskId, target.SessionId);
                    D046HermesRenameTaskReceipt? rename = null;
                    D046HermesDeleteTaskReceipt? delete = null;
                    var notDispatched = false;
                    try
                    {
                        if (normalized is not null)
                        {
                            rename = await _projection.RenameTaskAsync(new(Guid.NewGuid().ToString("N"), generation,
                                target.TaskId, target.SessionId, targetSnapshot.ThroughSequence, normalized), _lifetime.Token);
                            if (rename.TransportUncertain || rename.RuntimeGeneration != generation || rename.TaskId != target.TaskId
                                || rename.SessionId != target.SessionId || rename.Accepted && rename.Title != normalized)
                                throw new InvalidDataException("Context rename receipt mismatch.");
                        }
                        else
                        {
                            LastDeleteOutcome = D046HermesDeleteOutcome.AwaitingResult;
                            LastDeleteStage = D046HermesDeleteStage.Dispatch;
                            delete = await _projection.DeleteTaskAsync(new(Guid.NewGuid().ToString("N"), generation,
                                target.TaskId, target.SessionId, targetSnapshot.ThroughSequence), _lifetime.Token);
                            if (delete.TransportUncertain || delete.RuntimeGeneration != generation || delete.TaskId != target.TaskId
                                || delete.SessionId != target.SessionId)
                                throw new InvalidDataException("Context delete receipt mismatch.");
                            LastDeleteStage = D046HermesDeleteStage.Receipt;
                            if (IsConfirmedDeleteReceipt(delete))
                            {
                                confirmedDelete = delete;
                                LastDeleteOutcome = D046HermesDeleteOutcome.ConfirmedDeleted;
                            }
                            else if (delete.Deleted)
                                throw new InvalidDataException("Context delete receipt was contradictory.");
                            else
                                LastDeleteOutcome = D046HermesDeleteOutcome.Rejected;
                        }
                    }
                    catch (D046HermesPreDispatchRejectedException exception)
                    {
                        // No mutation was sent, but native activation already
                        // moved to the menu target. Reattest the original below;
                        // never pretend its old visible snapshot is still current.
                        notDispatched = true;
                        if (normalized is null)
                        {
                            LastDeleteOutcome = D046HermesDeleteOutcome.NotDispatched;
                            RecordDeleteFailure(exception);
                        }
                    }
                    LastDeleteStage = D046HermesDeleteStage.Replacement;
                    var restored = await _projection.OpenTaskAsync(original.TaskId, _lifetime.Token);
                    if (restored.RuntimeGeneration != generation || restored.SelectedTaskId != original.TaskId
                        || restored.Tasks.Single(t => t.TaskId == original.TaskId).SessionId != original.SessionId)
                        throw new InvalidDataException("Context management did not restore the displayed conversation.");
                    return new ContextManagementResult(restored, rename, delete, notDispatched);
                }, candidate => candidate.Snapshot);
            if (result is not null)
            {
                await RefreshSnapshotAuxiliaryStateAsync();
                _statusText = ControlCenterLocalization.Current[result.NotDispatched
                    ? "chat.managementNotDispatched"
                    : result.Rename?.Accepted == true || result.Delete?.Deleted == true
                        ? "chat.managedOther" : "chat.managementRejected"];
                OnPropertyChanged(nameof(StatusText));
            }
            return result;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return null; }
        catch (D046HermesDeletedTaskRecoveryException exception)
            when (normalized is null && exception.Receipt.RuntimeGeneration == generation
                && exception.Receipt.TaskId == taskId && exception.Receipt.SessionId == targetSessionId
                && IsConfirmedDeleteReceipt(exception.Receipt))
        {
            LastDeleteOutcome = D046HermesDeleteOutcome.ConfirmedDeleted;
            LastDeleteStage = D046HermesDeleteStage.Replacement;
            RecordDeleteFailure(exception);
            ShowConfirmedDeletionRecovery(exception.InnerException);
            return new ContextManagementResult(exception.Receipt.Snapshot, null, exception.Receipt);
        }
        catch (Exception exception)
        {
            if (normalized is null) RecordDeleteFailure(exception);
            if (confirmedDelete is not null)
            {
                ShowConfirmedDeletionRecovery(exception);
                return new ContextManagementResult(confirmedDelete.Snapshot, null, confirmedDelete);
            }
            // A partial native transition cannot be silently treated as the old
            // active session. Preserve its draft and require normal recovery.
            SetProjectionFault(ProjectionFailureStatus("chat.context_management", exception));
            return null;
        }
        finally { SetCommandState(false); }
    }
}
