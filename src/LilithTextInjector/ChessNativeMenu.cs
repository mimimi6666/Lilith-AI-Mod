using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using NativeEntries = Il2CppSystem.Collections.Generic.IReadOnlyList<PlayerLineEntry>;
using NativeEntryList = Il2CppSystem.Collections.Generic.List<PlayerLineEntry>;
using NativeSelection = Il2CppSystem.Action<PlayerLineEntry>;

namespace LilithTextInjector;

internal enum ChessNotice { ProgressUnavailable, OpponentUnavailable, InvalidClaim }

internal static partial class OldMaidNativeBridge
{
    private const int ChessEntryId = -184301;
    private static ChessChoices? _chessChoices;
    private static bool _showingChess;

    internal static void ShowChessModes()
    {
        if (!TryShowChessChoices(new[] { DialogueManagerUpdatePatch.MiniGameText("mode.casual"),
            DialogueManagerUpdatePatch.MiniGameText("mode.challenge"), DialogueManagerUpdatePatch.MiniGameText("snack.back") }, i =>
            { if (i < 2) ChessPresentation.Open(i == 0 ? ChessMode.Casual : ChessMode.Challenge); else ShowSnackMenu(); }))
            ShowSnackMenu();
    }

    internal static bool TryShowChessChoices(string[] labels, Action<int> select)
    {
        if (!IsSnackActive || labels.Length is < 2 or > 4) return false;
        var controller = _menuController ?? UnityEngine.Object.FindObjectOfType<PlayerLineController>();
        if (controller == null || controller.HasActiveOrPendingOptions) return false;
        try
        {
            var binding = new ChessChoices(controller, labels, select);
            binding.Callback = DelegateSupport.ConvertDelegate<NativeSelection>(new Action<PlayerLineEntry>(binding.Select))
                ?? throw new InvalidOperationException("Chess menu callback unavailable.");
            _chessChoices = binding;
            _showingChess = true;
            controller.ShowEntryOptions(binding.Entries.Cast<NativeEntries>(), binding.Callback);
            return true;
        }
        catch (Exception error) { ClearChessChoices(); Warn("chess_options", error); return false; }
        finally { _showingChess = false; }
    }

    private static void ObserveChessMenuReplacement(PlayerLineController controller)
    {
        if (!_showingChess && _chessChoices != null && controller != null && _chessChoices.Controller.Pointer == controller.Pointer)
            _chessChoices = null;
    }

    internal static bool ChessChoicesVisible => _chessChoices != null && _chessChoices.Visible;

    // Native choices own board input even away from their buttons. This is a
    // separate concern from dialogue/voice busy, which only gates new speech.
    internal static bool ChessInputChoicesPending()
    {
        try
        {
            var controller = _menuController ?? UnityEngine.Object.FindObjectOfType<PlayerLineController>();
            return controller == null || controller.HasActiveOrPendingOptions;
        }
        catch (Exception) { return true; }
    }

    internal static void ClearChessChoices()
    {
        var old = _chessChoices;
        _chessChoices = null;
        try { if (old?.Visible == true) old.Controller.Hide(); }
        catch (Exception error) { Warn("chess_options_cleanup", error); }
    }

    private sealed class ChessChoices
    {
        internal readonly PlayerLineController Controller;
        internal readonly NativeEntryList Entries = new();
        internal NativeSelection Callback = null!;
        private readonly Action<int> select;
        internal ChessChoices(PlayerLineController controller, string[] labels, Action<int> callback)
        {
            Controller = controller; select = callback;
            for (var i = 0; i < labels.Length; i++) Entries.Add(new PlayerLineEntry { id = ChessEntryId - 1 - i, LineID = ChessEntryId - 1 - i, text = labels[i] });
        }
        internal bool Visible => Controller != null && Controller._isShowing && Controller._isEntrySelectMode
            && Controller._entrySelectCallback != null && Callback != null && Controller._entrySelectCallback.Pointer == Callback.Pointer;
        internal void Select(PlayerLineEntry entry)
        {
            if (!ReferenceEquals(_chessChoices, this) || entry == null || !IsSnackActive) return;
            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Pointer != entry.Pointer) continue;
                ChessPresentation.ConsumeChoiceInput();
                _chessChoices = null;
                try { select(i); } catch (Exception error) { ChessPresentation.Close(); Warn("chess_selection", error); ShowSnackMenu(); }
                return;
            }
        }
    }
}

internal static partial class DialogueManagerUpdatePatch
{
    internal static string ChessProgressPath => System.IO.Path.Combine(PlayerStateDirectory, "chess-progress-v1.json");
    private static void UpdateChess()
    {
        try { ChessPresentation.Update(ReadOldMaidForeground(), ReadOldMaidModal()); }
        catch (Exception error) { ChessPresentation.Close(); Plugin.PluginLog.LogWarning("Chess stopped safely: " + error.GetType().Name); }
    }

    internal static bool ChessDialogueBusy()
    {
        var manager = DialogueManager.instance;
        return manager == null || manager.IsBusy || _requestInFlight || _transcriptionInFlight
            || _microphoneRecording || _activeGameHermesVoiceCapture != null || _pendingVoiceSubmitText != null
            || !PendingTranscripts.IsEmpty
            || _aiPagesAwaitingAdvance || HasActivePublishedPlayerReplySpeech() || MiniGameDialogueVoiceActive
            || !PendingVoiceAudio.IsEmpty || !PendingReminderSpeech.IsEmpty
            || System.Threading.Volatile.Read(ref _activeReminderSpeechCount) > 0
            || System.Threading.Volatile.Read(ref _reminderSpeechPipelineBusy) != 0
            || System.Threading.Volatile.Read(ref _pendingGameHermesNotificationCount) > 0;
    }

    internal static bool TryChessResultCue(int outcome)
    {
        if (!ChessPresentation.IsOpen || ReadOldMaidModal() || !ReadOldMaidForeground() || ChessDialogueBusy()) return false;
        var cue = ChessPresentation.DialogueResultCue;
        return !cue.HasValue || TryPresentMiniGameDialogue("chess", cue.Value, ChessPresentation.DialogueRevision);
    }

    internal static void TryChessNotice(ChessNotice notice)
    {
        if (ReadOldMaidModal() || !ReadOldMaidForeground() || ChessDialogueBusy() || !OldMaidNativeBridge.IsSnackActive) return;
        var text = notice switch
        {
            ChessNotice.ProgressUnavailable => MiniGameText("notice.progress"),
            ChessNotice.OpponentUnavailable => MiniGameText("notice.opponent"),
            ChessNotice.InvalidClaim => MiniGameText("notice.claim"),
            _ => null
        };
        var manager = DialogueManager.instance;
        if (text != null && manager != null) ForceSaySynthetic(manager, text, string.Empty, 3f);
    }
}
