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
    private static void ToggleInputBubble()
    {
        if (_inputBubble == null && !TryCreateInputBubble())
            return;

        UpdateInputPlaceholderLocalization();

        if (_inputBubble!.activeSelf)
        {
            // The global shortcut can still be detected while another desktop
            // application owns the keyboard. In that case the visible bubble is
            // not being toggled off: the user is asking to type into it again.
            if (!IsLilithForeground())
            {
                TryBringLilithToForeground();
                _focusNextFrame = true;
                return;
            }
            CloseInputBubble(clear: false);
            return;
        }

        _inputBubble.SetActive(true);
        var canvasGroup = _inputBubble.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        TryBringLilithToForeground();
        _focusNextFrame = true;
    }

    private static bool IsLilithForeground()
    {
        if (!OperatingSystem.IsWindows())
            return Application.isFocused;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static void TryBringLilithToForeground()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var process = Process.GetCurrentProcess();
            var gameWindow = process.MainWindowHandle;
            if (gameWindow == IntPtr.Zero)
            {
                Plugin.PluginLog.LogWarning("Could not focus text input because the Lilith window handle was not found.");
                return;
            }

            ShowWindow(gameWindow, 9);
            if (SetForegroundWindow(gameWindow))
                return;

            // Windows can reject a foreground change made by a background
            // process. Temporarily join the current foreground input queue so
            // the user-initiated chat shortcut can activate Lilith reliably.
            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero
                ? 0u
                : GetWindowThreadProcessId(foreground, out _);
            var currentThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != currentThread
                && AttachThreadInput(currentThread, foregroundThread, true);
            try
            {
                BringWindowToTop(gameWindow);
                SetForegroundWindow(gameWindow);
                SetFocus(gameWindow);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not return Windows focus to the text input bubble: {exception.Message}");
        }
    }

    private static bool TryCreateInputBubble()
    {
        try
        {
            var sourceUi = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>();
            if (sourceUi == null)
            {
                Plugin.PluginLog.LogWarning("DialogueBubbleUI was not found yet.");
                return false;
            }

            var sourceText = sourceUi.gameObject.GetComponentInChildren<TextMeshProUGUI>();
            if (sourceText == null)
            {
                Plugin.PluginLog.LogWarning("Dialogue bubble text component was not found.");
                return false;
            }

            var bubbleSprite = FindSprite("Choise_Bubble");
            if (bubbleSprite == null)
                throw new InvalidOperationException("Sprite 'Choise_Bubble' was not found.");

            _inputBubble = new GameObject(
                "LilithAiInputBubble",
                Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(),
                Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(),
                Il2CppInterop.Runtime.Il2CppType.Of<Image>(),
                Il2CppInterop.Runtime.Il2CppType.Of<TMP_InputField>());
            _inputBubble.transform.SetParent(sourceUi.transform.parent, false);

            var rootRect = _inputBubble.GetComponent<RectTransform>();
            var sourceRect = sourceUi.GetComponent<RectTransform>();
            rootRect.anchorMin = sourceRect.anchorMin;
            rootRect.anchorMax = sourceRect.anchorMax;
            rootRect.pivot = sourceRect.pivot;
            rootRect.sizeDelta = new Vector2(203f, 39f);
            rootRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, 45f);

            var background = _inputBubble.GetComponent<Image>();
            background.sprite = bubbleSprite;
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            var viewportObject = new GameObject(
                "Text Area",
                Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(),
                Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(),
                Il2CppInterop.Runtime.Il2CppType.Of<RectMask2D>());
            viewportObject.transform.SetParent(_inputBubble.transform, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(20f, 5f);
            viewport.offsetMax = new Vector2(-20f, -5f);

            var inputTextObject = UnityEngine.Object.Instantiate(sourceText.gameObject, viewport);
            inputTextObject.name = "Text";
            var typewriter = inputTextObject.GetComponent<TypewriterEffect>();
            if (typewriter != null)
                typewriter.enabled = false;
            var inputText = inputTextObject.GetComponent<TextMeshProUGUI>();
            inputText.text = string.Empty;
            inputText.raycastTarget = true;
            inputText.fontSize = 15f;
            inputText.enableWordWrapping = false;
            inputText.overflowMode = TextOverflowModes.Masking;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            var inputTextRect = inputText.rectTransform;
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = Vector2.zero;
            inputTextRect.offsetMax = Vector2.zero;

            var placeholderObject = UnityEngine.Object.Instantiate(inputText.gameObject, viewport);
            placeholderObject.name = "Placeholder";
            var placeholder = placeholderObject.GetComponent<TextMeshProUGUI>();
            _inputPlaceholder = placeholder;
            UpdateInputPlaceholderLocalization();
            placeholder.fontSize = 15f;
            var placeholderColor = placeholder.color;
            placeholderColor.a = 0.45f;
            placeholder.color = placeholderColor;

            _inputField = _inputBubble.GetComponent<TMP_InputField>();
            if (_inputField == null)
                _inputField = _inputBubble.AddComponent<TMP_InputField>();

            _inputField.textComponent = inputText;
            _inputField.placeholder = placeholder;
            _inputField.textViewport = viewport;
            _inputField.targetGraphic = background;
            _inputField.lineType = TMP_InputField.LineType.SingleLine;
            _inputField.characterLimit = 240;

            _inputBubble.SetActive(false);
            Plugin.PluginLog.LogInfo("Created input field from the native dialogue bubble.");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError(exception);
            if (_inputBubble != null)
                UnityEngine.Object.Destroy(_inputBubble);
            _inputBubble = null;
            _inputField = null;
            _inputPlaceholder = null;
            _focusNextFrame = false;
            return false;
        }
    }

    private static void UpdateInputPlaceholderLocalization()
    {
        if (_inputPlaceholder == null)
            return;
        _inputPlaceholder.text = ApiKeyText(
            "想對莉莉絲說什麼……",
            "想对莉莉丝说什么……",
            "リリスに何を話そう……",
            "What would you like to say to Lilith…");
    }

    private static string[] SplitIntoBubblePages(string text)
    {
        var completeText = text.Trim();
        return completeText.Length == 0 ? Array.Empty<string>() : new[] { completeText };
    }

    internal static bool TryAdvanceAiPage(DialogueManager manager)
    {
        if (!_aiPagesAwaitingAdvance)
            return false;
        var currentNode = manager.CurrentNode;
        if (currentNode == null || !string.Equals(currentNode.text, _currentAiPageText, StringComparison.Ordinal))
        {
            CancelPendingAiPages("Native dialogue took control.");
            return false;
        }
        if (!PendingReplies.TryDequeue(out var page))
        {
            _aiPagesAwaitingAdvance = false;
            return false;
        }
        _aiPagesAwaitingAdvance = !PendingReplies.IsEmpty;
        _currentAiPageText = page;
        _aiTypingFinishedAt = -1f;
        manager.ForceSay(page, string.Empty, 30f);
        return true;
    }

    private static void CancelPendingAiPages(string reason)
    {
        while (PendingReplies.TryDequeue(out _)) { }
        _aiPagesAwaitingAdvance = false;
        _currentAiPageText = string.Empty;
        Plugin.PluginLog.LogInfo($"Cancelled pending AI pages: {reason}");
    }

    internal static void NotifyAiTypingState(bool isTyping)
    {
        if (string.IsNullOrEmpty(_currentAiPageText))
            return;
        _aiTypingFinishedAt = isTyping ? -1f : Time.unscaledTime;
    }

    internal static bool ShouldDelayAiCompletion(DialogueManager manager)
    {
        var node = manager.CurrentNode;
        if (node == null || !string.Equals(node.text, _currentAiPageText, StringComparison.Ordinal))
            return false;
        // Keep the current bubble alive while another AI page is waiting for the
        // user's click. Speech is generated for the complete reply and may continue
        // past the first page, so allowing Unity to close here makes the text look
        // truncated even though the response itself is complete.
        if (_aiPagesAwaitingAdvance)
            return true;
        if (_aiTypingFinishedAt < 0f)
            return true;
        return Time.unscaledTime - _aiTypingFinishedAt < Math.Max(0f, Plugin.PostTypingHoldSeconds.Value);
    }

    private static void CloseInputBubble(bool clear)
    {
        if (_inputField != null)
        {
            _inputField.DeactivateInputField();
            if (clear)
                _inputField.text = string.Empty;
        }

        // TMP_InputField can retain a stale activation state after its parent is
        // hidden in this IL2CPP build. Recreate the lightweight bubble on the next
        // invocation so every F7 session starts with a clean input component.
        if (_inputBubble != null)
            UnityEngine.Object.Destroy(_inputBubble);
        _inputBubble = null;
        _inputField = null;
        _inputPlaceholder = null;
        _focusNextFrame = false;
    }
}
