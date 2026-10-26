using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static readonly MusicPlayerShortcutState MusicShortcuts = new();
    private static bool _musicShortcutFaultReported;

    // Called every Unity Update, before chat/history early returns. These are
    // local player inputs, not model tools or global Windows media shortcuts.
    private static void UpdateMusicPlayerShortcuts()
    {
        try
        {
            var allowed = IsMusicShortcutGameForeground()
                && !D046GameControlCaptureLease.IsActive()
                && !(_inputBubble != null && _inputBubble.activeSelf)
                && !HasMusicShortcutTextFocus();
            var command = MusicShortcuts.Observe(allowed,
                IsKeyCurrentlyDown(KeyCode.LeftControl) || IsKeyCurrentlyDown(KeyCode.RightControl),
                IsKeyCurrentlyDown(KeyCode.LeftShift) || IsKeyCurrentlyDown(KeyCode.RightShift)
                    || IsKeyCurrentlyDown(KeyCode.LeftAlt) || IsKeyCurrentlyDown(KeyCode.RightAlt)
                    || IsVirtualKeyDown(0x5B) || IsVirtualKeyDown(0x5C),
                IsKeyCurrentlyDown(KeyCode.LeftArrow),
                IsKeyCurrentlyDown(KeyCode.RightArrow),
                IsKeyCurrentlyDown(KeyCode.Space));
            if (command == MusicPlayerShortcut.None) return;

            // Read the existing instance only; don't lazy-create a player or
            // fall back to system media keys (which could control another app).
            var player = MusicPlayerController.s_instance;
            if (player == null || !player.enabled || !player.gameObject.activeInHierarchy
                || player._externalMode || player.TempHidden || player.IsPointerBusy)
                return;

            // Do not gate on _shown or IsUserMusicPlaying: native idle hiding
            // and a paused track must not prevent the user from resuming.
            switch (command)
            {
                case MusicPlayerShortcut.Previous: player.OnPrev(); break;
                case MusicPlayerShortcut.Next: player.OnNext(); break;
                case MusicPlayerShortcut.TogglePause: player.OnPlayPause(); break;
            }
            _musicShortcutFaultReported = false;
        }
        catch (Exception error)
        {
            // No queued replay and no exception escaping into F7/F8 updates.
            MusicShortcuts.Invalidate();
            if (!_musicShortcutFaultReported)
                Plugin.PluginLog.LogWarning("Music shortcut unavailable; native controls retained. kind="
                    + error.GetType().Name);
            _musicShortcutFaultReported = true;
        }
    }

    private static bool IsMusicShortcutGameForeground()
    {
        if (!OperatingSystem.IsWindows())
            return Application.isFocused;

        // Do not depend on Unity's separate focus flag after reactivation.
        // Check the exact native game window, not only the owning process:
        // a same-process file picker must not accept music shortcuts.
        var gameWindow = PlatformService.Current.GetAppWindowHandle();
        var foreground = GetForegroundWindow();
        if (gameWindow == IntPtr.Zero || foreground != gameWindow)
            return false;
        GetWindowThreadProcessId(foreground, out var processId);
        return MusicPlayerShortcutFocus.IsWindowsGameForeground(
                gameWindow, foreground, (uint)Environment.ProcessId, processId)
            && GetForegroundWindow() == foreground;
    }

    private static bool HasMusicShortcutTextFocus()
    {
        if (!string.IsNullOrEmpty(Input.compositionString)) return true;
        var events = EventSystem.current;
        var selected = events == null ? null : events.currentSelectedGameObject;
        if (selected == null) return false;
        // Suppress even before activation finishes; editing with Ctrl+arrows
        // or toggling an IME must not also change music.
        return selected.GetComponentInParent<TMP_InputField>() != null
            || selected.GetComponentInParent<InputField>() != null;
    }
}
