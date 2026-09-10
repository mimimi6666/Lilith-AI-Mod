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
    private static void EnsureApiKeyTrayMenu()
    {
        try
        {
            var trays = Resources.FindObjectsOfTypeAll<ShowSystemTray>();
            if (trays == null || trays.Length == 0 || trays[0] == null || trays[0].tray == null)
                return;
            var tray = trays[0].tray;
            if (tray.Pointer == _apiKeyTrayPointer)
                return;
            var trayTable = LocalizationSettings.StringDatabase?.GetTable("TrayUI", LocalizationSettings.SelectedLocale);
            if (trayTable == null)
                return;
            trayTable.AddEntry("AddApiKey", ApiKeyText("加入 API KEY", "加入 API KEY", "APIキーを追加", "Add API Key"));
            trayTable.AddEntry("Gemini", "Gemini");
            trayTable.AddEntry("Qwen", ApiKeyText("千問", "千问", "Qwen（千問）", "Qwen"));
            trayTable.AddEntry("OpenAI", "OpenAI");
            trayTable.AddEntry("DeepSeek", "DeepSeek");
            var providers = new[] { "Gemini", "Qwen", "OpenAI", "DeepSeek" };
            var children = new Il2CppReferenceArray<ShowSystemTray.MenuItemData>(providers.Length);
            for (var index = 0; index < providers.Length; index++)
            {
                var selectedProvider = providers[index];
                var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                    new System.Action(() => OpenApiKeyDialog(selectedProvider)));
                children[index] = new ShowSystemTray.MenuItemData
                {
                    tableEntryKey = selectedProvider,
                    callback = callback!,
                    isSeparator = false
                };
            }
            var parent = new ShowSystemTray.MenuItemData
            {
                tableEntryKey = "AddApiKey",
                children = children,
                isSeparator = false
            };
            trays[0].StartCoroutine(trays[0].ResolveAndAddSubMenu(parent));
            _apiKeyTrayPointer = tray.Pointer;
            Plugin.PluginLog.LogInfo("Added localized API key item to the system tray menu.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add API key tray item: {exception.Message}");
        }
    }

    private static void OpenApiKeyDialog(string provider)
    {
        // Native tray callbacks run outside Unity's main thread. Defer every
        // Unity object access until the next DialogueManager.Update.
        _pendingApiKeyProvider = provider;
        _apiKeyOpenRequested = true;
        Plugin.PluginLog.LogInfo($"Queued the {_pendingApiKeyProvider} API key window request for Unity's main thread.");
    }

    private static void ProcessApiKeyOpenRequest()
    {
        if (!_apiKeyOpenRequested)
            return;
        _apiKeyOpenRequested = false;
        try
        {
            _apiKeyDialogMode = true;
            _apiKeyMissingViewLogged = false;
            _apiKeyOpenRequestedAt = Time.unscaledTime;
            var trays = Resources.FindObjectsOfTypeAll<ShowSystemTray>();
            if (trays == null || trays.Length == 0 || trays[0] == null)
                throw new InvalidOperationException("ShowSystemTray was not found.");
            trays[0].RequestOpenGiftExchange();
            var liveView = FindLiveGiftExchangeView();
            if (liveView == null)
                throw new InvalidOperationException("A live GiftExchangeView was not found in the current scene.");
            _apiKeyView = liveView;
            CaptureApiKeyDialogState(liveView);
            // RequestOpenGiftExchange prepares the native view, while Show is
            // what actually presents it in this game build.
            liveView.Show(null!);
            Plugin.PluginLog.LogInfo("Opened the native API key input window on Unity's main thread.");
        }
        catch (Exception exception)
        {
            EndApiKeyDialogMode(_apiKeyView);
            Plugin.PluginLog.LogWarning($"Could not open API key input window: {exception.Message}");
        }
    }

    private static void ConfigureApiKeyDialog()
    {
        if (!_apiKeyDialogMode)
            return;
        try
        {
            var liveView = _apiKeyView;
            if (liveView == null)
            {
                liveView = FindLiveGiftExchangeView(requireVisible: true);
                if (liveView != null)
                {
                    _apiKeyView = liveView;
                    CaptureApiKeyDialogState(liveView);
                    Plugin.PluginLog.LogInfo("Attached API key mode to the visible native gift exchange window.");
                }
            }
            if (liveView == null)
            {
                if (!_apiKeyMissingViewLogged && _apiKeyOpenRequestedAt >= 0f
                    && Time.unscaledTime - _apiKeyOpenRequestedAt > 2f)
                {
                    _apiKeyMissingViewLogged = true;
                    Plugin.PluginLog.LogWarning("API key mode is active, but GiftExchangeView was not found after 2 seconds.");
                }
                return;
            }
            CaptureApiKeyDialogState(liveView);
            var input = liveView._giftKeyInputField;
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Password;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.characterLimit = 512;
            }
            foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (label == null || label.transform == null || !label.transform.IsChildOf(liveView.transform))
                    continue;
                var current = label.text ?? string.Empty;
                if (Regex.IsMatch(current, "兌換|兑换|redeem|exchange|コード", RegexOptions.IgnoreCase))
                    label.text = ApiKeyText($"輸入 {_pendingApiKeyProvider} API 密鑰", $"输入 {_pendingApiKeyProvider} API 密钥", $"{_pendingApiKeyProvider} APIキーを入力", $"Enter {_pendingApiKeyProvider} API Key");
                else if (Regex.IsMatch(current, "好了|確認|确认|submit|confirm|redeem|交換", RegexOptions.IgnoreCase))
                    label.text = ApiKeyText("儲存", "保存", "保存", "Save");
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not configure API key input window: {exception.Message}");
        }
    }

    private static GiftExchangeView? FindLiveGiftExchangeView(bool requireVisible = false)
    {
        foreach (var view in Resources.FindObjectsOfTypeAll<GiftExchangeView>())
        {
            if (view != null && view.gameObject != null && view.gameObject.scene.IsValid()
                && (!requireVisible || view.gameObject.activeInHierarchy))
                return view;
        }
        return null;
    }

    private static void CaptureApiKeyDialogState(GiftExchangeView view)
    {
        if (_apiKeyDialogStateCaptured)
            return;

        ApiKeyDialogLabels.Clear();
        var input = view._giftKeyInputField;
        if (input != null)
        {
            _apiKeyOriginalContentType = input.contentType;
            _apiKeyOriginalLineType = input.lineType;
            _apiKeyOriginalCharacterLimit = input.characterLimit;
        }

        foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (label == null || label.transform == null || !label.transform.IsChildOf(view.transform))
                continue;
            ApiKeyDialogLabels.Add(new ApiKeyDialogLabelSnapshot(label, label.text ?? string.Empty));
        }
        _apiKeyDialogStateCaptured = true;
    }

    private static void RestoreApiKeyDialogState(GiftExchangeView? view)
    {
        if (_apiKeyDialogStateCaptured)
        {
            foreach (var snapshot in ApiKeyDialogLabels)
            {
                if (snapshot.Label != null)
                    snapshot.Label.text = snapshot.Text;
            }

            var input = view?._giftKeyInputField;
            if (input != null)
            {
                input.text = string.Empty;
                input.contentType = _apiKeyOriginalContentType;
                input.lineType = _apiKeyOriginalLineType;
                input.characterLimit = _apiKeyOriginalCharacterLimit;
            }
        }

        ApiKeyDialogLabels.Clear();
        _apiKeyDialogStateCaptured = false;
    }

    private static void EndApiKeyDialogMode(GiftExchangeView? view)
    {
        ReleaseApiKeyDialogInput(view);
        RestoreApiKeyDialogState(view);
        _apiKeyDialogMode = false;
        _apiKeyView = null;
        _apiKeyOpenRequestedAt = -1f;
        _apiKeyMissingViewLogged = false;
    }

    private static void ReleaseApiKeyDialogInput(GiftExchangeView? view)
    {
        try
        {
            var input = view?._giftKeyInputField;
            input?.DeactivateInputField();
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            var selected = eventSystem?.currentSelectedGameObject;
            if (eventSystem != null && selected != null
                && view != null && selected.transform.IsChildOf(view.transform))
                eventSystem.SetSelectedGameObject(null);
            GUIUtility.keyboardControl = 0;
            if (OperatingSystem.IsWindows())
                ReleaseCapture();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not fully release API key dialog input focus: {exception.Message}");
        }
    }

    internal static void NotifyGiftExchangeViewHidden(GiftExchangeView view)
    {
        if (!_apiKeyDialogMode || _apiKeyView == null || view.Pointer != _apiKeyView.Pointer)
            return;
        EndApiKeyDialogMode(view);
        Plugin.PluginLog.LogInfo("Closed API key mode and restored the native gift redemption window.");
    }

    internal static bool TrySaveApiKey(GiftExchangeView view)
    {
        if (!_apiKeyDialogMode || _apiKeyView == null || view.Pointer != _apiKeyView.Pointer)
            return false;
        try
        {
            var input = view._giftKeyInputField;
            var key = input?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                Plugin.PluginLog.LogWarning("API key input was empty; nothing was saved.");
                return true;
            }
            switch (_pendingApiKeyProvider)
            {
                case "Qwen":
                    Plugin.QwenApiKey.Value = key;
                    break;
                case "OpenAI":
                    Plugin.OpenAiApiKey.Value = key;
                    break;
                case "DeepSeek":
                    Plugin.DeepSeekApiKey.Value = key;
                    break;
                default:
                    Plugin.GeminiApiKey.Value = key;
                    _pendingApiKeyProvider = "Gemini";
                    break;
            }
            Plugin.AiProvider.Value = _pendingApiKeyProvider;
            if (input != null)
                input.text = string.Empty;
            // Restore native redemption state and release input before Hide.
            // This also prevents the Hide postfix from seeing stale API mode.
            EndApiKeyDialogMode(view);
            view.Hide();
            Plugin.PluginLog.LogInfo($"{_pendingApiKeyProvider} API key was saved and selected (value hidden).");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not save API key: {exception.Message}");
            return true;
        }
    }

    private sealed class ApiKeyDialogLabelSnapshot
    {
        internal ApiKeyDialogLabelSnapshot(TMP_Text label, string text)
        {
            Label = label;
            Text = text;
        }

        internal TMP_Text Label { get; }
        internal string Text { get; }
    }

}
