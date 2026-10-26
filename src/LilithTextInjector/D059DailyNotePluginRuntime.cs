using System;
using System.IO;
using LilithRuntimeContracts;
using UnityEngine;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static LilithNativeDailyNoteBridge? _dailyNoteBridge;
    private static bool _dailyNoteWriteInFlight;
    private static float _nextDailyNoteOpportunityAt;
    private static bool? _lastDailyNoteSafe;

    private static void UpdateDailyNoteOnMainThread()
    {
        // No profile/root/receipt IO or transport creation while Off or isolated.
        if (!Plugin.HermesDailyNoteEnabled.Value || !Plugin.AiNotesEnabled.Value
            || _d029IsolatedTestProfile is not null)
        {
            if (_dailyNoteBridge is not null)
            {
                _dailyNoteBridge.RevokeOnMainThread(disable: true);
                _dailyNoteBridge.Dispose();
                _dailyNoteBridge = null;
            }
            _lastDailyNoteSafe = null;
            return;
        }
        // The product profile is resolved on every computer; no operator path
        // or historical CharacterProfileIdentity is a daily storage identity.
        if (!IsDailyNoteProductProfile())
        {
            _dailyNoteBridge?.Dispose(); _dailyNoteBridge = null; _lastDailyNoteSafe = null;
            return;
        }
        _dailyNoteBridge ??= new LilithNativeDailyNoteBridge(exception =>
            ReportNoteInboxExecutionFault("daily_transport", exception));
        var safe = CaptureDailyNoteSafeState();
        if (_lastDailyNoteSafe != safe || Time.unscaledTime >= _nextDailyNoteOpportunityAt)
        {
            var absent = false;
            try { absent = !LilithActivityTracker.PlayerPresent(); }
            catch { /* Unknown original presence is never absence. */ }
            _dailyNoteBridge.PublishOnMainThread(true, safe, DailyNoteLocale(), absent);
            _lastDailyNoteSafe = safe;
            _nextDailyNoteOpportunityAt = Time.unscaledTime + 1f;
        }
        _dailyNoteBridge.UpdateOnMainThread(ExecuteDailyNoteOnMainThread);
    }

    private static bool IsDailyNoteProductProfile()
    {
        try
        {
            var expectedProfile = PlayerDataStorage.BuildProfileDirectory(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            return string.Equals(Path.GetFullPath(PlayerProfileDirectory), Path.GetFullPath(expectedProfile), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool CaptureDailyNoteSafeState()
    {
        var state = UnityEngine.Object.FindObjectOfType<LilithStateManager>();
        bool legacyNote;
        lock (LivingProactiveNoteLock) legacyNote = _livingProactiveNoteActive is not null;
        return DailyNoteSafetyPolicy.IsSafe(new DailyNoteSafetyFacts(
            state is not null, state?.IsDrag == true,
            _requestInFlight || _transcriptionInFlight || _microphoneRecording || _aiPagesAwaitingAdvance
                || _activeGameHermesVoiceCapture != null || _pendingVoiceSubmitText != null
                || !PendingTranscripts.IsEmpty || !PendingReplies.IsEmpty || HasActivePublishedPlayerReplySpeech(),
            _actionShowcasePendingOperation != ActionShowcasePendingOperation.None
                || _actionShowcaseWindowWalkContinuationActive
                || state?.IsStateMachineLocked == true || state?.IsAnyActionAnimPlaying == true,
            state is null, // Quiet PNG writing does not wake her or require daytime clothing.
            legacyNote || _aiNoteGenerationInFlight || _dailyNoteWriteInFlight));
    }

    private static bool MayWriteDailyNote(LilithDailyNoteScope scope)
        => Plugin.HermesDailyNoteEnabled.Value && Plugin.AiNotesEnabled.Value
            && _d029IsolatedTestProfile is null && _dailyNoteBridge?.Matches(scope, requireSafe: true) == true
            && CaptureDailyNoteSafeState();

    private static LilithNoteInboxResult ExecuteDailyNoteOnMainThread(LilithDailyNoteCommand command)
    {
        // Repeated before any path resolution, receipt access or native call.
        if (_d029IsolatedTestProfile is not null)
            return new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Rejected, "daily.isolated_sink_unavailable");
        if (!Plugin.HermesDailyNoteEnabled.Value || !Plugin.AiNotesEnabled.Value
            || _dailyNoteBridge?.Matches(command.Scope, requireSafe: false) != true)
            return new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Rejected, "daily.lease_revoked");
        if (command.Action == LilithNoteInboxAction.Deliver && !MayWriteDailyNote(command.Scope))
            return new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Deferred, "daily.unsafe");
        if (command.Action == LilithNoteInboxAction.Deliver && ContainsSensitiveNoteData(command.Text!))
            return new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Rejected, "daily.sensitive_content");
        var nativeRoot = ResolveCurrentNativeNotesDirectory();
        var store = new NoteInboxDeliveryStore(Path.Combine(PlayerProfileDirectory, "noteinbox-receipts-v1"));
        var wrote = false;
        var executor = new NoteInboxDeliveryExecutor(store, nativeRoot, text =>
        {
            // No local substitute renderer. The verified original writer is sole sink.
            _dailyNoteWriteInFlight = true;
            try
            {
                var path = SaveNativeNoteWithWriterOrigin(text, NativeNoteWriterOrigin.ModHermesNoteInboxTool);
                wrote = true; return path;
            }
            finally { _dailyNoteWriteInFlight = false; }
        });
        var result = command.Action == LilithNoteInboxAction.Status
            ? executor.Query(command) : executor.Execute(command, () => MayWriteDailyNote(command.Scope));
        if (wrote)
        {
            try { NoteInbox.NotifySaved(); }
            catch (Exception exception) { ReportNoteInboxExecutionFault("daily_refresh", exception); }
        }
        return result;
    }

    private static void StopDailyNoteDispatch()
    {
        _dailyNoteBridge?.RevokeOnMainThread();
        _lastDailyNoteSafe = null;
        _nextDailyNoteOpportunityAt = Time.unscaledTime + 1f;
    }

    private static string DailyNoteLocale()
        => LilithGamePersonaLocaleProjector.Resolve(GetAgentTurnLocale()).DisplayLocale switch
        {
            LilithGameDisplayLocale.Japanese => "ja", LilithGameDisplayLocale.English => "en",
            LilithGameDisplayLocale.SimplifiedChinese => "zh-CN", _ => "zh-TW"
        };
}
