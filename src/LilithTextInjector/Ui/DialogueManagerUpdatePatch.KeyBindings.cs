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
    private static void ApplyKeyBinding(KeyCode key)
    {
        if (_keyBindingTarget == 1)
        {
            var previous = Plugin.TextInputKey.Value;
            if (key == Plugin.VoiceInputKey.Value)
                Plugin.VoiceInputKey.Value = previous;
            Plugin.TextInputKey.Value = key;
            Plugin.PluginLog.LogInfo($"Text input hotkey changed to {key}.");
        }
        else if (_keyBindingTarget == 2)
        {
            var previous = Plugin.VoiceInputKey.Value;
            if (key == Plugin.TextInputKey.Value)
                Plugin.TextInputKey.Value = previous;
            Plugin.VoiceInputKey.Value = key;
            Plugin.PluginLog.LogInfo($"Push-to-talk hotkey changed to {key}.");
        }
        _keyBindingTarget = 0;
        _keyBindingStartedAt = -1f;
        RebindingHeldVirtualKeys.Clear();
        _textInputKeyWasDown = IsKeyCurrentlyDown(Plugin.TextInputKey.Value);
        _voiceInputKeyWasDown = IsKeyCurrentlyDown(Plugin.VoiceInputKey.Value);
        UpdateKeyBindingTexts();
    }

    private static void UpdateKeyBindingTexts()
    {
        if (_textInputKeyRow != null)
        {
            var label = FindFirstText(_textInputKeyRow.transform);
            if (label != null && label != _textInputKeyValue)
                label.text = ApiKeyText("文字輸入按鍵", "文字输入按键", "文字入力キー", "Text input key");
        }
        if (_voiceInputKeyRow != null)
        {
            var label = FindFirstText(_voiceInputKeyRow.transform);
            if (label != null && label != _voiceInputKeyValue)
                label.text = ApiKeyText("按住說話按鍵", "按住说话按键", "長押し会話キー", "Push-to-talk key");
        }
        if (_textInputKeyValue != null)
            _textInputKeyValue.text = _keyBindingTarget == 1
                ? ApiKeyText("按任意鍵…", "按任意键…", "キーを押す…", "Press a key…")
                : FormatKeyCode(Plugin.TextInputKey.Value);
        if (_voiceInputKeyValue != null)
            _voiceInputKeyValue.text = _keyBindingTarget == 2
                ? ApiKeyText("按任意鍵…", "按任意键…", "キーを押す…", "Press a key…")
                : FormatKeyCode(Plugin.VoiceInputKey.Value);
        if (_textInputKeyButton != null && _textInputKeyButton.IsOn)
            _textInputKeyButton.SetValue(false, false);
        if (_voiceInputKeyButton != null && _voiceInputKeyButton.IsOn)
            _voiceInputKeyButton.SetValue(false, false);
    }

    private static string FormatKeyCode(KeyCode key)
    {
        var name = key.ToString();
        if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6)
            return name.Substring(5);
        return name
            .Replace("LeftControl", "L Ctrl", StringComparison.Ordinal)
            .Replace("RightControl", "R Ctrl", StringComparison.Ordinal)
            .Replace("LeftShift", "L Shift", StringComparison.Ordinal)
            .Replace("RightShift", "R Shift", StringComparison.Ordinal)
            .Replace("LeftAlt", "L Alt", StringComparison.Ordinal)
            .Replace("RightAlt", "R Alt", StringComparison.Ordinal)
            .Replace("Keypad", "Num ", StringComparison.Ordinal);
    }

    private static void ResetKeyBindingUiReferences()
    {
        _textInputKeyRow = null;
        _voiceInputKeyRow = null;
        _textInputKeyButton = null;
        _voiceInputKeyButton = null;
        _textInputKeyButtonRect = null;
        _voiceInputKeyButtonRect = null;
        _textInputKeyValue = null;
        _voiceInputKeyValue = null;
        _keyBindingTarget = 0;
        _keyBindingStartedAt = -1f;
        RebindingHeldVirtualKeys.Clear();
    }

}
