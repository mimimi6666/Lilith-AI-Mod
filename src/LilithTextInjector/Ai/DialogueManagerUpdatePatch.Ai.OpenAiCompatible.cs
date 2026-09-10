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

// Handles every provider that speaks the OpenAI "/chat/completions" wire format:
// OpenAI and DeepSeek's cloud APIs today, and any local/self-hosted server the player
// points at through the [LocalAI] config section (Ollama, LM Studio, llama.cpp server,
// vLLM, text-generation-webui, and similar all implement this same format).
//
// To add a provider whose request/response shape does NOT match this format, do not
// extend this method - add a sibling file instead, following Ai.Qwen.cs or Ai.Gemini.cs
// as a template, then add one branch for it in RequestGeminiAsync's provider dispatch
// (Ai.Gemini.cs) and a case in NormalizeAiProvider/GetActiveChatApiKey (Ai.Shared.cs).
// See Ai/README.md for the full walkthrough.
internal static partial class DialogueManagerUpdatePatch
{
    private static async Task RequestOpenAiCompatibleAsync(string provider, string systemInstruction, string userText,
        PoseContext poseContext, bool japaneseVoiceMode)
    {
        var endpoint = provider switch
        {
            "DeepSeek" => "https://api.deepseek.com/chat/completions",
            "LocalAI" => Plugin.LocalAiBaseUrl.Value.Trim(),
            _ => "https://api.openai.com/v1/chat/completions",
        };
        var model = provider switch
        {
            "DeepSeek" => Plugin.DeepSeekModel.Value.Trim(),
            "LocalAI" => Plugin.LocalAiModel.Value.Trim(),
            _ => Plugin.OpenAiModel.Value.Trim(),
        };
        var key = provider switch
        {
            "DeepSeek" => Plugin.DeepSeekApiKey.Value.Trim(),
            "LocalAI" => Plugin.LocalAiApiKey.Value.Trim(),
            _ => Plugin.OpenAiApiKey.Value.Trim(),
        };
        var messages = BuildOpenAiMessages(systemInstruction);
        var payload = new { model, messages, max_tokens = 1024, temperature = 0.8 };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        // Local servers commonly accept requests with no Authorization header at all;
        // only send one when a key was actually configured.
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{provider} HTTP {(int)response.StatusCode}: {responseBody}");
        using var document = JsonDocument.Parse(responseBody);
        var choice = document.RootElement.GetProperty("choices")[0];
        var rawReply = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var usageSummary = "unavailable";
        if (document.RootElement.TryGetProperty("usage", out var usage))
        {
            var prompt = usage.TryGetProperty("prompt_tokens", out var promptElement) ? promptElement.GetInt32() : -1;
            var output = usage.TryGetProperty("completion_tokens", out var outputElement) ? outputElement.GetInt32() : -1;
            usageSummary = $"prompt={prompt}, output={output}";
        }
        Plugin.PluginLog.LogInfo($"{provider} completed: finish={finishReason}, rawChars={rawReply.Length}, {usageSummary}.");
        CompleteAiReply(rawReply, userText, poseContext, japaneseVoiceMode);
    }

    private static object[] BuildOpenAiMessages(string systemInstruction)
    {
        var messages = new List<object> { new { role = "system", content = systemInstruction } };
        lock (MemoryLock)
        {
            foreach (var turn in RecentConversation)
                messages.Add(new { role = turn.Role == "model" ? "assistant" : "user", content = turn.Text });
        }
        return messages.ToArray();
    }

}
