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
    private sealed class VoiceSequence
    {
        public byte[]? Reaction { get; set; }
        public byte[] Speech { get; set; } = Array.Empty<byte>();
    }

    private sealed class CodexBridgeSignal
    {
        public string EventName { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string TurnId { get; set; } = string.Empty;
    }

    private sealed class GeminiAgentSession
    {
        public string Url { get; set; } = string.Empty;
        public string SystemInstruction { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public PoseContext PoseContext { get; set; } = PoseContext.Default;
        public bool JapaneseVoiceMode { get; set; }
        public bool UseGoogleSearch { get; set; }
        public bool DesktopToolsEnabled { get; set; }
        public int ToolRounds { get; set; }
        public List<object> Contents { get; set; } = new();
    }

    private sealed class GeminiFunctionCallData
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public JsonElement Args { get; set; }
    }

    private sealed class GeminiToolBatch
    {
        public GeminiAgentSession Session { get; set; } = new();
        public List<GeminiFunctionCallData> Calls { get; set; } = new();
    }

    private sealed class GeminiToolResult
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class QwenAgentSession
    {
        public string Url { get; set; } = string.Empty;
        public string SystemInstruction { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public PoseContext PoseContext { get; set; } = PoseContext.Default;
        public bool JapaneseVoiceMode { get; set; }
        public bool UseWebSearch { get; set; }
        public bool ForceWebSearch { get; set; }
        public bool DesktopToolsEnabled { get; set; }
        public int ToolRounds { get; set; }
        public List<object> Input { get; set; } = new();
    }

    private sealed class QwenToolBatch
    {
        public QwenAgentSession Session { get; set; } = new();
        public List<GeminiFunctionCallData> Calls { get; set; } = new();
    }

    private sealed class LocalTimer
    {
        public DateTimeOffset DueAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PendingSystemAction
    {
        public string Action { get; set; } = string.Empty;
        public float ExecuteAfter { get; set; }
    }

    private sealed class NativeReaction
    {
        public byte[] Audio { get; set; } = Array.Empty<byte>();
        public VoiceStyle Style { get; set; }
    }

    private enum VoiceStyle
    {
        Calm,
        Excited,
        Wronged,
        Sleepy
    }

    private sealed class PoseContext
    {
        public static readonly PoseContext Default = new(string.Empty, VoiceStyle.Calm);
        public string Prompt { get; }
        public VoiceStyle VoiceStyle { get; }

        public PoseContext(string prompt, VoiceStyle voiceStyle)
        {
            Prompt = prompt;
            VoiceStyle = voiceStyle;
        }
    }

}
