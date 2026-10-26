using System;
using System.IO;
using System.Threading;
using LilithRuntime.Configuration;
using LilithRuntimeContracts;
using UnityEngine;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static LilithNativeNoteInboxToolBridge? _noteInboxToolBridge;
    private static NoteInboxToolOwner? _noteInboxToolOwner;
    private static D059ManualNotePreferencesStore? _manualNotePreferencesStore;
    private static readonly D059ManualNotePreferenceProjection ManualNotePreferences = new();
    private static float _manualNoteNextRefreshAt;

    private sealed record NoteInboxToolOwner(
        Guid ClientTurnId, PlayerTurnOperationHandle Operation, CancellationToken Cancellation);

    private static NoteInboxToolOwner RegisterNoteInboxToolOwner(
        Guid clientTurnId, PlayerTurnOperationHandle operation, CancellationToken cancellation)
    {
        var owner = new NoteInboxToolOwner(clientTurnId, operation, cancellation);
        Volatile.Write(ref _noteInboxToolOwner, owner);
        return owner;
    }

    private static void RetireNoteInboxToolOwner(NoteInboxToolOwner owner)
        => Interlocked.CompareExchange(ref _noteInboxToolOwner, null, owner);

    private static bool IsManualNoteInboxToolEnabled()
        => ManualNotePreferences.Effective(Plugin.HermesNoteInboxToolEnabled.Value);

    private static void RefreshManualNotePreferencesOnMainThread()
    {
        if (Time.unscaledTime < _manualNoteNextRefreshAt) return;
        _manualNoteNextRefreshAt = Time.unscaledTime + 1f;
        try
        {
            _manualNotePreferencesStore ??= D059ManualNotePreferencesStore.ForCurrentUser();
            ManualNotePreferences.Observe(_manualNotePreferencesStore.Load());
        }
        catch
        {
            ManualNotePreferences.Observe(new D059ManualNoteLoadResult(
                new(0, null, DateTimeOffset.UnixEpoch), D059ManualNoteLoadDisposition.Invalid));
        }
    }

    private static void UpdateNoteInboxToolOnMainThread()
    {
        RefreshManualNotePreferencesOnMainThread();
        // Default-Off does not start a poller or inspect/create any receipt path.
        if (!IsManualNoteInboxToolEnabled() || !Plugin.AiNotesEnabled.Value)
        {
            _noteInboxToolBridge?.Dispose();
            _noteInboxToolBridge = null;
            return;
        }
        _noteInboxToolBridge ??= new LilithNativeNoteInboxToolBridge(logFault: exception =>
            Plugin.PluginLog?.LogWarning("Picture note tool transport unavailable ("
                + exception.GetType().Name + ")."));
        _noteInboxToolBridge.UpdateOnMainThread(ExecuteNoteInboxToolOnMainThread);
    }

    private static string EvaluateNoteInboxTool(LilithNoteInboxCommand command)
    {
        var owner = Volatile.Read(ref _noteInboxToolOwner);
        var exactOwner = owner is not null
            && owner.ClientTurnId == command.ClientTurnId
            && PlayerTurnOperations.IsActive(owner.Operation)
            && owner.Operation.RuntimeGeneration == CurrentPlayerTurnRuntimeGeneration();
        var state = UnityEngine.Object.FindObjectOfType<LilithStateManager>();
        bool activeLegacyNote;
        lock (LivingProactiveNoteLock) activeLegacyNote = _livingProactiveNoteActive is not null;
        return NoteInboxToolPolicy.Evaluate(new NoteInboxToolFacts(
            IsManualNoteInboxToolEnabled(),
            Plugin.AiNotesEnabled.Value,
            exactOwner,
            _noteInboxToolBridge?.MatchesRuntime(command.RuntimeGeneration) == true,
            owner is null || owner.Cancellation.IsCancellationRequested,
            DateTimeOffset.UtcNow < command.ExpiresAtUtc,
            state?.IsDrag == true,
            (_requestInFlight && !exactOwner)
                || _transcriptionInFlight || _microphoneRecording || _aiPagesAwaitingAdvance
                || _activeGameHermesVoiceCapture != null || _pendingVoiceSubmitText != null
                || !PendingTranscripts.IsEmpty || !PendingReplies.IsEmpty
                || HasActivePublishedPlayerReplySpeech(),
            _actionShowcasePendingOperation != ActionShowcasePendingOperation.None
                || _actionShowcaseWindowWalkContinuationActive
                || state?.IsStateMachineLocked == true || state?.IsAnyActionAnimPlaying == true,
            state == null || state.IsSleep || state.ClothingState == LilithClothingState.Pajamas,
            activeLegacyNote || _aiNoteGenerationInFlight));
    }

    private static LilithNoteInboxResult ExecuteNoteInboxToolOnMainThread(LilithNoteInboxCommand command)
    {
        var stage = "policy";
        try
        {
            var reason = EvaluateNoteInboxTool(command);
            // Even status requires a currently authenticated owning tool turn,
            // but does not reserve a delivery or consume a delivery budget.
            if (reason != "ok")
                return new LilithNoteInboxResult(command.OperationId, command.DeliveryId,
                    reason is "busy" or "direct_control" or "quiet" or "pending_execution" or "note_busy"
                        ? LilithNoteInboxStatus.Deferred : LilithNoteInboxStatus.Rejected,
                    reason);
            stage = "content_check";
            if (command.Action == LilithNoteInboxAction.Deliver
                && ContainsSensitiveNoteData(command.Text!))
                return new LilithNoteInboxResult(command.OperationId, command.DeliveryId,
                    LilithNoteInboxStatus.Rejected, "sensitive_content");

            // Use the established resolver: the native computed directory in
            // production, or the already-verified D029 isolated root. A raw
            // folder field is not proof of an absolute destination. Never
            // patch the native getter or accept a model-selected path.
            stage = "native_root";
            var nativeRoot = ResolveCurrentNativeNotesDirectory();
            stage = "receipt_store";
            var store = new NoteInboxDeliveryStore(Path.Combine(PlayerProfileDirectory, "noteinbox-receipts-v1"));
            var wroteNativeImage = false;
            stage = "executor_setup";
            var executor = new NoteInboxDeliveryExecutor(store, nativeRoot, text =>
            {
                try
                {
                    // The original game remains the sole renderer/writer.
                    var path = SaveNativeNoteWithWriterOrigin(text, NativeNoteWriterOrigin.ModHermesNoteInboxTool);
                    wroteNativeImage = true;
                    return path;
                }
                catch (Exception exception)
                {
                    ReportNoteInboxExecutionFault("sink", exception);
                    throw;
                }
            });
            stage = "execute";
            var result = executor.Execute(command, () => EvaluateNoteInboxTool(command) == "ok");
            if (wroteNativeImage)
            {
                try { NoteInbox.NotifySaved(); }
                catch (Exception exception)
                {
                    Plugin.PluginLog.LogWarning("NoteInbox refresh failed after a native write ("
                        + exception.GetType().Name + "). No note will be re-sent.");
                }
            }
            return result;
        }
        catch (Exception exception)
        {
            ReportNoteInboxExecutionFault(stage, exception);
            // The transport keeps its conservative executor_fault/Unknown.
            // Logging a failure never grants permission to re-send a note.
            throw;
        }
    }

    private static void ReportNoteInboxExecutionFault(string stage, Exception exception)
    {
        // Separate execution diagnostics from the transport's log-once flag.
        // Stage is a local literal; never include text, paths or exception data.
        try
        {
            Plugin.PluginLog?.LogWarning("Picture note execution failed (stage="
                + stage + ", type=" + exception.GetType().Name + ").");
        }
        catch { /* Diagnostics must not change the original outcome. */ }
    }
}
