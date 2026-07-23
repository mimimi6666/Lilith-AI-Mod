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
    internal static void LoadMemory()
    {
        try
        {
            Directory.CreateDirectory(MemoryDirectory);
            if (!File.Exists(MemoryPath))
                return;
            var loaded = JsonSerializer.Deserialize<List<ChatTurn>>(File.ReadAllText(MemoryPath));
            if (loaded == null)
                return;
            lock (MemoryLock)
            {
                RecentConversation.Clear();
                RecentConversation.AddRange(loaded.GetRange(Math.Max(0, loaded.Count - MaxRememberedTurns), Math.Min(MaxRememberedTurns, loaded.Count)));
            }
            Plugin.PluginLog.LogInfo($"Loaded {RecentConversation.Count} remembered chat turns.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not load chat memory: {exception.Message}");
        }
    }

    private static void AddMemoryTurn(string role, string text)
    {
        lock (MemoryLock)
        {
            RecentConversation.Add(new ChatTurn { Role = role, Text = text });
            while (RecentConversation.Count > MaxRememberedTurns)
                RecentConversation.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(MemoryDirectory);
                File.WriteAllText(MemoryPath, JsonSerializer.Serialize(RecentConversation, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not save chat memory: {exception.Message}");
            }
        }
    }

    public sealed class ChatTurn
    {
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

}
