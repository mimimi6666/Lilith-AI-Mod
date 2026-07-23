using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UI.Common;
using UI.TraySetting;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static void ProcessLocalTimers(DialogueManager manager)
    {
        if (LocalTimers.Count == 0 || manager.IsBusy || _requestInFlight)
            return;
        var now = DateTimeOffset.Now;
        var due = LocalTimers.Where(timer => timer.DueAt <= now).OrderBy(timer => timer.DueAt).ToList();
        if (due.Count == 0)
            return;
        foreach (var timer in due)
            LocalTimers.Remove(timer);
        var message = due.Count == 1
            ? due[0].Message
            : ApiKeyText($"有 {due.Count} 個計時器到時間了。{due[0].Message}", $"有 {due.Count} 个计时器到时间了。{due[0].Message}", $"{due.Count}件のタイマーが時間になったよ。{due[0].Message}", $"{due.Count} timers are due. {due[0].Message}");
        manager.ForceSay(message, string.Empty, 12f);
        if (Plugin.VoiceEnabled.Value)
            _ = RequestSpeechAsync(message, poseStyle: CapturePoseContext().VoiceStyle);
        Plugin.PluginLog.LogInfo($"Delivered {due.Count} local Lilith timer(s)." );
    }

    private static void ProcessPendingSystemAction()
    {
        var pending = _pendingSystemAction;
        if (pending == null)
            return;
        if (_requestInFlight || !PendingReplies.IsEmpty)
        {
            pending.ExecuteAfter = Math.Max(pending.ExecuteAfter, Time.unscaledTime + 12f);
            return;
        }
        if (Time.unscaledTime < pending.ExecuteAfter)
            return;
        _pendingSystemAction = null;
        try
        {
            var success = pending.Action switch
            {
                "lock" => LockWorkStation(),
                "sleep" => SetSuspendState(false, false, false),
                _ => false
            };
            if (!success)
                Plugin.PluginLog.LogWarning($"Pending system action '{pending.Action}' was rejected by Windows or is unavailable on this PC.");
            else
                Plugin.PluginLog.LogInfo($"Executed explicit user-requested system action '{pending.Action}'.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Pending system action '{pending.Action}' failed: {exception.Message}");
        }
    }

    private static double GetWindowsIdleSeconds()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.Time) / 1000d;
    }

}
