using System;
using System.Threading;
using UnityEngine;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal enum OldMaidCue { Start, PlayerPair, LilithPair, PlayerNoPair, LilithNoPair, PlayerWon, LilithWon }

internal static partial class DialogueManagerUpdatePatch
{
    private static readonly MusicPlayerClickFocusState OldMaidClickFocus = new();
    private static bool _oldMaidInputFaultLogged;

    private static void UpdateOldMaid()
    {
        if (!OldMaidPresentation.IsOpen) return;
        try
        {
            var modal = ReadOldMaidModal();
            OldMaidPresentation.Update(IsMusicShortcutGameForeground(), modal);
            _oldMaidInputFaultLogged = false;
        }
        catch (Exception error)
        {
            // Never let an optional local game interrupt the F7/F10 update loop.
            OldMaidPresentation.Pause();
            if (!_oldMaidInputFaultLogged)
                Plugin.PluginLog.LogDebug("Old Maid input guard unavailable: " + error.GetType().Name);
            _oldMaidInputFaultLogged = true;
        }
    }

    internal static bool ReadOldMaidForeground() => IsMusicShortcutGameForeground();

    internal static bool ReadOldMaidModal() => ReadMiniGameModalState().InputBlocked;

    internal static bool ReadMiniGamePresentationHidden() => ReadMiniGameModalState().HidePresentation;

    private static MiniGameModalState ReadMiniGameModalState() => MiniGameModalPresentation.Evaluate(
        _inputBubble != null && _inputBubble.activeSelf,
        OldMaidTextControlKeyHeld(),
        D046GameControlCaptureLease.IsActive()
            || (_conversationHistoryWindow != null && _conversationHistoryWindow.activeSelf)
            || _activeGameHermesVoiceCapture != null || _microphoneRecording
            || OldMaidOtherControlKeyHeld() || TransparentWindowNew.IsForceClickThrough,
        HasMusicShortcutTextFocus());

    private static bool OldMaidControlKeyHeld()
    {
        // Read, never consume, the effective hotkeys before this frame's card
        // timer advances. The ordinary handlers still own their exact latches.
        return OldMaidTextControlKeyHeld() || OldMaidOtherControlKeyHeld();
    }

    private static bool OldMaidTextControlKeyHeld() =>
        IsD046GameControlKeyCurrentlyDown(EffectiveD046TextChatKey(),
            D046GameControlProjection.Effective?.TextChatPortableKey);

    private static bool OldMaidOtherControlKeyHeld() =>
        IsD046GameControlKeyCurrentlyDown(EffectiveD046StopKey(),
                D046GameControlProjection.Effective?.StopPortableKey)
            || IsD046GameControlKeyCurrentlyDown(EffectiveD046ConversationHistoryKey(),
                D046GameControlProjection.Effective?.ConversationHistoryPortableKey)
            || IsD046GameControlKeyCurrentlyDown(EffectiveD046PushToTalkKey(),
                D046GameControlProjection.Effective?.PushToTalkPortableKey);

    // Approved local catalogue only; model chat and official voice remain owners.
    internal static bool TryShowOldMaidCue(OldMaidCue cue)
    {
        try
        {
            if (!CanPresentOldMaidLocalUi()) return false;
            var mapped = cue switch
            {
                OldMaidCue.Start => MiniGameDialogueCue.OldMaidStart,
                OldMaidCue.PlayerPair => MiniGameDialogueCue.OldMaidPlayerPair,
                OldMaidCue.LilithPair => MiniGameDialogueCue.OldMaidLilithPair,
                OldMaidCue.PlayerNoPair => MiniGameDialogueCue.OldMaidPlayerNoPair,
                OldMaidCue.LilithNoPair => MiniGameDialogueCue.OldMaidLilithNoPair,
                OldMaidCue.PlayerWon => MiniGameDialogueCue.OldMaidPlayerWon,
                OldMaidCue.LilithWon => MiniGameDialogueCue.OldMaidLilithWon,
                _ => (MiniGameDialogueCue?)null
            };
            if (!mapped.HasValue) return false;
            if (MiniGameDialoguePolicy.IsResult(mapped.Value))
                return TryPresentMiniGameDialogue("old-maid", mapped.Value, OldMaidPresentation.DialogueRevision);
            OfferMiniGameDialogue("old-maid", mapped.Value, OldMaidPresentation.DialogueRevision);
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static bool CanPresentOldMaidLocalUi()
    {
        try
        {
            var manager = DialogueManager.instance;
            if (!OldMaidPresentation.IsOpen || !OldMaidNativeBridge.IsSnackActive
                || !IsMusicShortcutGameForeground() || OldMaidControlKeyHeld()
                || manager == null || manager.IsBusy || _requestInFlight || _transcriptionInFlight
                || _microphoneRecording || _activeGameHermesVoiceCapture != null
                || _pendingVoiceSubmitText != null || _aiPagesAwaitingAdvance
                || !PendingTranscripts.IsEmpty || D046GameControlCaptureLease.IsActive()
                || (_inputBubble != null && _inputBubble.activeSelf)
                || (_conversationHistoryWindow != null && _conversationHistoryWindow.activeSelf)
                || HasMusicShortcutTextFocus() || HasActivePublishedPlayerReplySpeech() || MiniGameDialogueVoiceActive
                || Volatile.Read(ref _activeReminderSpeechCount) > 0
                || Volatile.Read(ref _reminderSpeechPipelineBusy) != 0
                || !PendingReminderSpeech.IsEmpty || !PendingVoiceAudio.IsEmpty
                || Volatile.Read(ref _pendingGameHermesNotificationCount) > 0) return false;
            var choices = UnityEngine.Object.FindObjectOfType<PlayerLineController>();
            if (choices != null && choices.HasActiveOrPendingOptions) return false;
            return true;
        }
        catch (Exception) { return false; }
    }

    // Only a fresh press on an owned, visible card/control can ask Windows to
    // activate this exact game HWND. No hover, timer, keyboard or focus loop.
    internal static bool TryActivateOldMaidPointer()
    {
        if (!Input.GetMouseButtonDown(0) || D046GameControlCaptureLease.IsActive()
            || (_inputBubble != null && _inputBubble.activeSelf)
            || (_conversationHistoryWindow != null && _conversationHistoryWindow.activeSelf)
            || HasMusicShortcutTextFocus() || TransparentWindowNew.IsForceClickThrough)
            return false;
        if (!OperatingSystem.IsWindows()) return Application.isFocused;
        var game = PlatformService.Current.GetAppWindowHandle();
        var foreground = GetForegroundWindow();
        var gamePid = MusicClickOwner(game);
        var foregroundPid = MusicClickOwner(foreground);
        if (game == IntPtr.Zero || MusicClickPointerRoot() != game
            || gamePid != (uint)Environment.ProcessId
            || !IsWindowVisible(game) || !MusicClickIsWindowEnabled(game)) return false;
        if (foreground == game) return true;
        if (!OldMaidClickFocus.TryRequest(Time.frameCount, true, true, false,
                game, (uint)Environment.ProcessId, gamePid,
                game, gamePid, foreground, foregroundPid)) return false;
        if (PlatformService.Current.GetAppWindowHandle() != game
            || MusicClickOwner(game) != gamePid || MusicClickPointerRoot() != game
            || GetForegroundWindow() != foreground || MusicClickOwner(foreground) != foregroundPid
            || !IsWindowVisible(game) || !MusicClickIsWindowEnabled(game)) return false;
        // Suppress any Ctrl chord held in the previously focused application.
        MusicShortcuts.Invalidate();
        return SetForegroundWindow(game) && IsMusicShortcutGameForeground();
    }
}
