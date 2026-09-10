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
    private static void Postfix(DialogueManager __instance)
    {
        UpdateSettingsUiSafely();
        EnsureLocalVoiceHost();
        ObserveForegroundWindow();
        ProcessGeminiToolBatches();
        ProcessGeminiCompatibilityFallbacks();
        ProcessQwenToolBatches();
        ProcessLocalTimers(__instance);
        ProcessPendingSystemAction();
        EnsureApiKeyTrayMenu();
        ProcessApiKeyOpenRequest();
        ConfigureApiKeyDialog();
        UpdateInputPlaceholderLocalization();
        ObserveVoiceLanguageSelection();
        TryCreateOneTestNote();
        ProcessAiNoteScheduler();
        ObserveCurrentNativeNode(__instance);
        PollCodexBridgeEvents();
        ProcessPendingCodexSignal(__instance);
        if (!_nativeDatabaseDumpCompleted)
            TryDumpNativeDialogueDatabases(__instance);
        if (!_localizedLineDatabasesDumped)
            TryDumpLocalizedLineDatabases();

        if (_voicePitchResetAt >= 0f && Time.unscaledTime >= _voicePitchResetAt)
        {
            SetVoicePitch(1f);
            _voicePitchResetAt = -1f;
        }

        if (_delayedSpeechAudio != null && Time.unscaledTime >= _delayedSpeechPlayAt)
        {
            try
            {
                var pitch = Math.Clamp(Plugin.ReactionFollowupPitch.Value, 0.8f, 1.2f);
                SetVoicePitch(pitch);
                var clip = PlayWav(_delayedSpeechAudio, "generated speech");
                _voicePitchResetAt = Time.unscaledTime + clip.length / Math.Max(0.01f, pitch) + 0.05f;
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogError($"Could not play generated voice: {exception}");
            }
            _delayedSpeechAudio = null;
            _delayedSpeechPlayAt = -1f;
        }
        else if (_delayedSpeechAudio == null && PendingVoiceAudio.TryDequeue(out var sequence))
        {
            try
            {
                if (sequence.Reaction != null)
                {
                    SetVoicePitch(1f);
                    var reactionClip = PlayWav(sequence.Reaction, "native reaction");
                    _delayedSpeechAudio = sequence.Speech;
                    _delayedSpeechPlayAt = Time.unscaledTime + reactionClip.length + 0.03f;
                }
                else
                {
                    SetVoicePitch(1f);
                    PlayWav(sequence.Speech, "generated speech");
                }
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogError($"Could not play voice sequence: {exception}");
                _delayedSpeechAudio = null;
                _delayedSpeechPlayAt = -1f;
            }
        }

        if (_requestInFlight && PendingReplies.TryDequeue(out var pendingReply))
        {
            _requestInFlight = false;
            _aiPagesAwaitingAdvance = !PendingReplies.IsEmpty;
            _currentAiPageText = pendingReply;
            _aiTypingFinishedAt = -1f;
            if (PendingAiEmotions.TryDequeue(out var emotion))
                PlayAiEmotion(emotion);
            __instance.ForceSay(pendingReply, string.Empty, 30f);
        }

        HandleVoiceInput(__instance);
        if (PendingTranscriptionErrors.TryDequeue(out var transcriptionError))
            __instance.ForceSay(transcriptionError, string.Empty, 6f);
        if (PendingTranscripts.TryDequeue(out var transcript))
        {
            _pendingVoiceSubmitText = transcript;
            _pendingVoiceSubmitAt = Time.unscaledTime + 1.5f;
            __instance.ForceSay(
                ApiKeyText($"我聽見了：「{transcript}」", $"我听见了：“{transcript}”", $"「{transcript}」と聞こえたよ。", $"I heard: “{transcript}”"),
                string.Empty,
                4f);
        }
        if (_pendingVoiceSubmitText != null && Time.unscaledTime >= _pendingVoiceSubmitAt)
        {
            var voiceText = _pendingVoiceSubmitText;
            _pendingVoiceSubmitText = null;
            _pendingVoiceSubmitAt = -1f;
            SubmitAiInput(__instance, voiceText);
        }

        var textInputKeyDown = IsKeyCurrentlyDown(Plugin.TextInputKey.Value);
        var textInputKeyPressed = textInputKeyDown && !_textInputKeyWasDown;
        _textInputKeyWasDown = textInputKeyDown;
        if (_keyBindingTarget == 0 && textInputKeyPressed)
        {
            if (_requestInFlight || _aiPagesAwaitingAdvance)
            {
                Plugin.PluginLog.LogInfo($"Ignored {Plugin.TextInputKey.Value} text input hotkey because a dialogue or AI request is active.");
                return;
            }
            ToggleInputBubble();
            return;
        }

        if (_inputBubble != null && _inputField != null && _inputBubble.activeSelf)
        {
            if (!IsLilithForeground())
            {
                // TMP_InputField cannot reliably recover after this transparent
                // desktop window loses native focus. Close and dispose the bubble;
                // the next shortcut invocation creates a clean input session.
                CloseInputBubble(clear: true);
                return;
            }

            if (_focusNextFrame)
            {
                _focusNextFrame = false;
                _inputField.ActivateInputField();
                _inputField.Select();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseInputBubble(clear: false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var submitted = _inputField!.text.Trim();
                if (submitted.Length > 0)
                {
                    CloseInputBubble(clear: true);
                    SubmitAiInput(__instance, submitted);
                }
                return;
            }
        }
    }

    private static void SubmitAiInput(DialogueManager manager, string submitted)
    {
        submitted = submitted.Trim();
        if (submitted.Length == 0)
            return;
        if (_requestInFlight)
        {
            manager.ForceSay(ApiKeyText("先等我說完。", "先等我说完。", "先に話し終えさせて……", "Let me finish speaking first."), string.Empty, 4f);
            return;
        }
        UpdatePendingAiNoteEvents(submitted);
        var activeProvider = NormalizeAiProvider(Plugin.AiProvider.Value);
        var useModelComputerTools = (string.Equals(activeProvider, "Gemini", StringComparison.Ordinal)
                || string.Equals(activeProvider, "Qwen", StringComparison.Ordinal))
            && Plugin.AdvancedComputerActionsEnabled.Value;
        // Qwen may answer a desktop request in prose without emitting a function call.
        // Route its explicit, locally verifiable commands before asking the model.
        var preferLocalComputerRouter = string.Equals(activeProvider, "Qwen", StringComparison.Ordinal)
            && Plugin.AdvancedComputerActionsEnabled.Value;
        if ((!useModelComputerTools || preferLocalComputerRouter) && TryHandleScreenshotCommand(submitted, out var screenshotReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", screenshotReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(screenshotReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(screenshotReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if ((!useModelComputerTools || preferLocalComputerRouter) && TryHandleComputerCommand(submitted, out var computerReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", computerReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(computerReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(computerReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if ((!useModelComputerTools || preferLocalComputerRouter) && TryHandleMediaCommand(submitted, out var mediaReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", mediaReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(mediaReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(mediaReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if ((!useModelComputerTools || preferLocalComputerRouter) && TryLaunchApplicationCommand(submitted, out var launchReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", launchReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(launchReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(launchReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if (string.IsNullOrWhiteSpace(GetActiveChatApiKey()))
        {
            manager.ForceSay(ApiKeyText("還沒有設定目前模型的 API Key。", "还没有设置当前模型的 API Key。", "現在のモデルのAPIキーがまだ設定されていないよ。", "The current model does not have an API key yet."), string.Empty, 6f);
            return;
        }
        _requestInFlight = true;
        manager.ForceSay("……", string.Empty, 30f);
        AddMemoryTurn("user", submitted);
        if (!useModelComputerTools && UsesTraditionalChineseInterface() && TryBuildLocalTimeReply(submitted, out var localTimeReply))
        {
            AddMemoryTurn("model", localTimeReply);
            PendingReplies.Enqueue(localTimeReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(localTimeReply);
            Plugin.PluginLog.LogInfo("Answered time/date question from the local system clock.");
        }
        else
        {
            var playerName = Archive.Instance != null ? Archive.Instance.playerName : string.Empty;
            if (PlayerNameRule.IsUnsetName(playerName))
                playerName = string.Empty;
            _ = RequestGeminiAsync(submitted, playerName, CapturePoseContext());
        }
        Plugin.PluginLog.LogInfo($"Submitted AI input ({submitted.Length} chars).");
    }

    private static void ObserveCurrentNativeNode(DialogueManager manager)
    {
        try
        {
            var node = manager.CurrentNode;
            if (node == null || node.id <= 0)
            {
                _lastObservedNativeNodeId = -1;
                return;
            }
            if (node.id == _lastObservedNativeNodeId)
                return;
            _lastObservedNativeNodeId = node.id;
            Plugin.PluginLog.LogInfo($"Observed current native node={node.id}, line={node.lineId}, action={node.actionType}.");
            RecordUnvoicedNativeNode(node);
            TryPlayInjectedNativeVoice(node);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not observe current native dialogue node: {exception.Message}");
        }
    }

}
