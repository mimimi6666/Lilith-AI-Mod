using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static readonly MusicPlayerClickFocusState MusicClickFocus = new();
    private static bool _musicClickFocusFaultReported;
    private static int _lastMusicClickFocusDiagnosticFrame = -1;

    // Only the two native mouse adapters below can request this. Keyboard
    // sampling, hover, programmatic playback and background timers cannot.
    internal static void RestoreMusicShortcutFocusFromNativeClick(string source)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !Input.GetMouseButtonDown(0)) return;
            var player = MusicPlayerController.s_instance;
            var blockedReason = string.Empty;
            if (player == null || !player.enabled || !player.gameObject.activeInHierarchy)
                blockedReason = "player_unavailable";
            else if (player._externalMode) blockedReason = "external_player";
            else if (player.TempHidden || player.IsPointerBusy) blockedReason = "player_busy";
            else if (D046GameControlCaptureLease.IsActive()) blockedReason = "capture";
            else if ((_inputBubble != null && _inputBubble.activeSelf) || HasMusicShortcutTextFocus())
                blockedReason = "text_focus";
            else if (TransparentWindowNew.IsForceClickThrough) blockedReason = "force_passthrough";

            // Even if Windows activated the click before this callback, do not
            // replay a chord held while the Unity update loop was unfocused.
            MusicShortcuts.Invalidate();
            var game = PlatformService.Current.GetAppWindowHandle();
            var foreground = GetForegroundWindow();
            var gamePid = MusicClickOwner(game);
            var foregroundPid = MusicClickOwner(foreground);
            var pointerRoot = MusicClickPointerRoot();
            var pointerPid = MusicClickOwner(pointerRoot);
            if (blockedReason.Length == 0
                && (game == IntPtr.Zero || !IsWindowVisible(game) || !MusicClickIsWindowEnabled(game)))
                blockedReason = "window_unavailable";
            if (!MusicClickFocus.TryRequest(Time.frameCount, true, true, blockedReason.Length != 0,
                    game, (uint)Environment.ProcessId, gamePid,
                    pointerRoot, pointerPid, foreground, foregroundPid))
            {
                var reason = blockedReason.Length != 0 ? blockedReason
                    : game == IntPtr.Zero || gamePid != (uint)Environment.ProcessId
                        || pointerRoot != game || pointerPid != (uint)Environment.ProcessId
                        || foreground == IntPtr.Zero || foregroundPid == 0 ? "window_identity"
                    : foreground == game ? "already_foreground"
                    : foregroundPid == (uint)Environment.ProcessId ? "same_process_dialog"
                    : "duplicate_click";
                ReportMusicClickFocusOutcome(source, reason);
                return;
            }

            // Revalidate the same identities immediately before the one-shot
            // action. A changed target or denied activation has no retry path.
            if (PlatformService.Current.GetAppWindowHandle() != game
                || MusicClickOwner(game) != gamePid
                || !IsWindowVisible(game) || !MusicClickIsWindowEnabled(game)
                || MusicClickPointerRoot() != game
                || GetForegroundWindow() != foreground
                || MusicClickOwner(foreground) != foregroundPid)
            {
                ReportMusicClickFocusOutcome(source, "identity_changed");
                return;
            }
            var requested = SetForegroundWindow(game);
            var activated = requested && GetForegroundWindow() == game
                && MusicClickOwner(game) == (uint)Environment.ProcessId;
            ReportMusicClickFocusOutcome(source, activated ? "activated" : "activation_denied");
            _musicClickFocusFaultReported = false;
        }
        catch (Exception error)
        {
            ReportMusicClickFocusFault(error);
        }
    }

    private static void ReportMusicClickFocusOutcome(string source, string reason)
    {
        // Bounded to one real native click per frame. Never log pointer
        // coordinates, window titles/handles, typed content or hover samples.
        if (_lastMusicClickFocusDiagnosticFrame == Time.frameCount) return;
        _lastMusicClickFocusDiagnosticFrame = Time.frameCount;
        Plugin.PluginLog.LogInfo("Music shortcut click focus: source=" + source + " outcome=" + reason + ".");
    }

    internal static void ReportMusicClickFocusFault(Exception error)
    {
        MusicShortcuts.Invalidate();
        if (!_musicClickFocusFaultReported)
            Plugin.PluginLog.LogWarning("Music click focus unavailable; native interaction retained. kind="
                + error.GetType().Name);
        _musicClickFocusFaultReported = true;
    }

    private static uint MusicClickOwner(IntPtr window)
    {
        if (window == IntPtr.Zero) return 0;
        return GetWindowThreadProcessId(window, out var processId) == 0 ? 0 : processId;
    }

    private static IntPtr MusicClickPointerRoot()
    {
        if (!MusicClickGetCursorPos(out var point)) return IntPtr.Zero;
        var hit = MusicClickWindowFromPoint(point);
        return hit == IntPtr.Zero ? IntPtr.Zero : MusicClickGetAncestor(hit, 2); // GA_ROOT
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MusicClickPoint { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MusicClickGetCursorPos(out MusicClickPoint point);

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern IntPtr MusicClickWindowFromPoint(MusicClickPoint point);

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    private static extern IntPtr MusicClickGetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MusicClickIsWindowEnabled(IntPtr window);
}

[HarmonyPatch(typeof(CharacterInteractionHandler), "HandleMouseDown")]
internal static class MusicShortcutCharacterClickFocusPatch
{
    private static void Prefix(CharacterInteractionHandler __instance, out bool __state)
    {
        __state = false;
        try
        {
            __state = __instance != null && __instance.enabled
                && __instance.gameObject.activeInHierarchy && __instance.InteractionEnabled
                && !__instance._isPressed && Input.GetMouseButtonDown(0);
        }
        catch (Exception error) { DialogueManagerUpdatePatch.ReportMusicClickFocusFault(error); }
    }

    private static void Postfix(CharacterInteractionHandler __instance, bool __state)
    {
        try
        {
            if (__state && __instance != null && __instance._isPressed
                && __instance.IsHitCharacter(__instance._pressHit))
                DialogueManagerUpdatePatch.RestoreMusicShortcutFocusFromNativeClick("character");
        }
        catch (Exception error) { DialogueManagerUpdatePatch.ReportMusicClickFocusFault(error); }
    }
}

[HarmonyPatch(typeof(MusicPlayerController), "Update")]
internal static class MusicShortcutPlayerClickFocusPatch
{
    private static void Postfix(MusicPlayerController __instance)
    {
        try
        {
            // The native panel has no pointer-down callback. Require a NEW
            // Unity left press and the native panel hit test, not hover/Poke.
            // The shared adapter additionally checks the OS topmost hit target.
            if (!Input.GetMouseButtonDown(0) || __instance == null
                || __instance != MusicPlayerController.s_instance || !__instance.enabled
                || !__instance.gameObject.activeInHierarchy || __instance._externalMode
                || __instance.TempHidden || __instance.IsPointerBusy) return;
            var canvas = __instance._canvasGroup;
            if (canvas == null || !canvas.enabled || canvas.alpha <= 0.01f
                || !canvas.interactable || !canvas.blocksRaycasts
                || !__instance.IsPointerOverPanel()) return;
            DialogueManagerUpdatePatch.RestoreMusicShortcutFocusFromNativeClick("player_panel");
        }
        catch (Exception error) { DialogueManagerUpdatePatch.ReportMusicClickFocusFault(error); }
    }
}
