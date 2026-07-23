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
    private static void PollCodexBridgeEvents()
    {
        if (!Plugin.CodexBridgeEnabled.Value || Time.unscaledTime < _nextCodexBridgePollAt)
            return;
        _nextCodexBridgePollAt = Time.unscaledTime + 0.25f;

        try
        {
            Directory.CreateDirectory(CodexBridgeEventsDirectory);
            var files = Directory.GetFiles(CodexBridgeEventsDirectory, "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(16)
                .ToArray();

            foreach (var path in files)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                    var root = document.RootElement;
                    var eventName = root.TryGetProperty("hookEventName", out var eventElement)
                        ? eventElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (eventName is not ("UserPromptSubmit" or "PermissionRequest" or "Stop"))
                        continue;

                    var timestamp = DateTimeOffset.UtcNow;
                    if (root.TryGetProperty("timestampUtc", out var timestampElement)
                        && DateTimeOffset.TryParse(
                            timestampElement.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var parsedTimestamp))
                    {
                        timestamp = parsedTimestamp;
                    }
                    if (DateTimeOffset.UtcNow - timestamp > TimeSpan.FromMinutes(5))
                        continue;

                    var sessionId = root.TryGetProperty("sessionId", out var sessionElement)
                        ? sessionElement.GetString() ?? string.Empty
                        : string.Empty;
                    var turnId = root.TryGetProperty("turnId", out var turnElement)
                        ? turnElement.GetString() ?? string.Empty
                        : string.Empty;
                    PendingCodexSignals.Enqueue(new CodexBridgeSignal
                    {
                        EventName = eventName,
                        SessionId = sessionId,
                        TurnId = turnId
                    });
                    while (PendingCodexSignals.Count > 24 && PendingCodexSignals.TryDequeue(out _)) { }
                    Plugin.PluginLog.LogInfo($"Codex bridge received {eventName} (session={ShortOpaqueId(sessionId)}, turn={ShortOpaqueId(turnId)})." );
                }
                catch (Exception exception)
                {
                    Plugin.PluginLog.LogWarning($"Could not read Codex bridge event '{Path.GetFileName(path)}': {exception.Message}");
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not poll Codex bridge events: {exception.Message}");
            _nextCodexBridgePollAt = Time.unscaledTime + 3f;
        }
    }

    private static void ProcessPendingCodexSignal(DialogueManager manager)
    {
        if (!Plugin.CodexBridgeEnabled.Value
            || Time.unscaledTime < _nextCodexSignalAt
            || _requestInFlight
            || _aiPagesAwaitingAdvance
            || (_inputBubble != null && _inputBubble.activeSelf)
            || !PendingCodexSignals.TryDequeue(out var signal))
        {
            return;
        }

        string traditional;
        string simplified;
        string japanese;
        string english;
        string emotion;
        switch (signal.EventName)
        {
            case "PermissionRequest":
                traditional = "這一步要你點頭，我才能繼續。";
                simplified = "这一步要你点头，我才能继续。";
                japanese = "ここから先は、君の許可が必要みたい。";
                english = "I need your approval before I can continue.";
                emotion = "emoji_daze_1";
                break;
            case "Stop":
                traditional = "做好了。你來看看吧。";
                simplified = "做好了。你来看看吧。";
                japanese = "終わったよ。見てみて。";
                english = "It is ready. Come take a look.";
                emotion = "emoji_smile_1";
                break;
            default:
                traditional = "嗯，我看看……";
                simplified = "嗯，我看看……";
                japanese = "うん、ちょっと見てみるね……";
                english = "Mm, let me take a look…";
                emotion = "emoji_calm_1";
                break;
        }

        var displayText = ApiKeyText(traditional, simplified, japanese, english);
        var speechText = IsJapaneseVoiceMode() ? japanese : traditional;
        PlayAiEmotion(emotion);
        manager.ForceSay(displayText, string.Empty, 8f);
        if (Plugin.CodexBridgeVoiceEnabled.Value && Plugin.VoiceEnabled.Value)
            _ = RequestSpeechAsync(speechText, null, CapturePoseContext().VoiceStyle, IsJapaneseVoiceMode());
        _nextCodexSignalAt = Time.unscaledTime + 0.8f;
    }

    private static string ShortOpaqueId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value[..Math.Min(8, value.Length)];
    }

}
