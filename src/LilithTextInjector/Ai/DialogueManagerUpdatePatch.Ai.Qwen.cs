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
    private static async Task RequestQwenResponsesAsync(string systemInstruction, string userText,
        PoseContext poseContext, bool japaneseVoiceMode, bool useWebSearch, bool forceWebSearch, bool desktopToolsEnabled)
    {
        var baseUrl = NormalizeQwenBaseUrl(Plugin.QwenBaseUrl.Value);
        var session = new QwenAgentSession
        {
            Url = baseUrl + "/responses",
            SystemInstruction = systemInstruction,
            UserText = userText,
            PoseContext = poseContext,
            JapaneseVoiceMode = japaneseVoiceMode,
            UseWebSearch = useWebSearch,
            ForceWebSearch = forceWebSearch,
            DesktopToolsEnabled = desktopToolsEnabled,
            Input = BuildQwenInput().ToList()
        };
        await SendQwenAgentRequestAsync(session).ConfigureAwait(false);
    }

    private static object[] BuildQwenInput()
    {
        var input = new List<object>();
        lock (MemoryLock)
        {
            foreach (var turn in RecentConversation)
                input.Add(new { role = turn.Role == "model" ? "assistant" : "user", content = turn.Text });
        }
        return input.ToArray();
    }

    private static async Task SendQwenAgentRequestAsync(QwenAgentSession session)
    {
        var requestTimer = Stopwatch.StartNew();
        var payload = new Dictionary<string, object>
        {
            ["model"] = Plugin.QwenModel.Value.Trim(),
            ["instructions"] = session.SystemInstruction,
            ["input"] = session.Input,
            ["max_output_tokens"] = 1536,
            ["store"] = false,
            ["reasoning"] = new { effort = "none" }
        };
        var tools = BuildQwenTools(session.UseWebSearch, session.ForceWebSearch ? false : session.DesktopToolsEnabled);
        if (tools.Length > 0)
            payload["tools"] = tools;
        if (session.ForceWebSearch)
            payload["tool_choice"] = "required";

        using var request = new HttpRequestMessage(HttpMethod.Post, session.Url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Plugin.QwenApiKey.Value.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qwen HTTP {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var calls = new List<GeminiFunctionCallData>();
        var reply = new StringBuilder();
        var webSearchCalls = 0;
        if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
                if (string.Equals(type, "message", StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var partType)
                            && string.Equals(partType.GetString(), "output_text", StringComparison.OrdinalIgnoreCase)
                            && part.TryGetProperty("text", out var textElement))
                            reply.Append(textElement.GetString());
                    }
                }
                else if (string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase))
                {
                    var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                    var callId = item.TryGetProperty("call_id", out var callIdElement) ? callIdElement.GetString() ?? string.Empty : string.Empty;
                    var arguments = item.TryGetProperty("arguments", out var argumentsElement) ? argumentsElement.GetString() ?? "{}" : "{}";
                    try
                    {
                        using var argumentsDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
                        calls.Add(new GeminiFunctionCallData { Name = name, Id = callId, Args = argumentsDocument.RootElement.Clone() });
                    }
                    catch
                    {
                        calls.Add(new GeminiFunctionCallData { Name = name, Id = callId, Args = JsonSerializer.SerializeToElement(new { }) });
                    }
                }
                else if (string.Equals(type, "web_search_call", StringComparison.OrdinalIgnoreCase))
                {
                    webSearchCalls++;
                    session.Input.Add(item.Clone());
                }
                else if (string.Equals(type, "web_extractor_call", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    session.Input.Add(item.Clone());
                }
            }
        }

        var status = document.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "unknown"
            : "unknown";
        var usageSummary = "unavailable";
        if (document.RootElement.TryGetProperty("usage", out var usage))
        {
            var inputTokens = usage.TryGetProperty("input_tokens", out var inputElement) ? inputElement.GetInt32() : -1;
            var outputTokens = usage.TryGetProperty("output_tokens", out var outputElement) ? outputElement.GetInt32() : -1;
            usageSummary = $"input={inputTokens}, output={outputTokens}";
        }
        Plugin.PluginLog.LogInfo($"Qwen agent turn completed in {requestTimer.Elapsed.TotalSeconds:F2}s: status={status}, calls={calls.Count}, webSearchCalls={webSearchCalls}, rawChars={reply.Length}, round={session.ToolRounds}, {usageSummary}.");

        if (calls.Count > 0)
        {
            if (session.ToolRounds >= 4)
            {
                CompleteAiReply(ApiKeyText("電腦操作步驟太多了，我先停在這裡。", "电脑操作步骤太多了，我先停在这里。", "PC操作の手順が多すぎるから、ここで止めておくね。", "There were too many computer-control steps, so I stopped here."), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
                return;
            }
            session.ToolRounds++;
            PendingQwenToolBatches.Enqueue(new QwenToolBatch { Session = session, Calls = calls });
            return;
        }

        CompleteAiReply(reply.ToString(), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
    }

    private static object[] BuildQwenTools(bool includeWebSearch, bool includeDesktopTools)
    {
        static object Parameters(object properties, params string[] required) => new
        {
            type = "object",
            properties,
            required,
            additionalProperties = false
        };

        var tools = new List<object>();
        if (includeWebSearch)
            tools.Add(new { type = "web_search" });
        if (!includeDesktopTools)
            return tools.ToArray();

        tools.Add(new { type = "function", name = "open_application", description = "Open or focus an installed application or game by its common user-facing name. Use only when the user wants an app opened; pass a concise app name, never a path or command.", parameters = Parameters(new { name = new { type = "string", description = "Common application or game name, such as Spotify, Discord, Steam, VALORANT, or Notepad." } }, "name") });
        tools.Add(new { type = "function", name = "open_folder", description = "Open one allowlisted common Windows folder. Never accepts arbitrary paths.", parameters = Parameters(new { folder = new { type = "string", description = "One of: downloads, desktop, documents, pictures, music, videos, screenshots, mod, recycle_bin." } }, "folder") });
        tools.Add(new { type = "function", name = "window_action", description = "Perform a reversible window-management action. Closing apps is unavailable.", parameters = Parameters(new { action = new { type = "string", description = "One of: show_desktop, task_view, switch_previous, minimize, maximize, restore, snap_left, snap_right." } }, "action") });
        tools.Add(new { type = "function", name = "media_control", description = "Control the active system media session with a standard media key.", parameters = Parameters(new { action = new { type = "string", description = "One of: play_pause, next, previous, stop, mute, volume_up, volume_down." } }, "action") });
        tools.Add(new { type = "function", name = "take_screenshot", description = "Capture all local monitors to the user's Pictures/Lilith Screenshots folder. The screenshot remains local and is never uploaded or returned to the model.", parameters = Parameters(new { }) });
        tools.Add(new { type = "function", name = "copy_text", description = "Write user-specified non-sensitive text to the local clipboard. Never use for passwords, API keys, OTPs, tokens, private identifiers, or other credentials. Clipboard reading is unavailable.", parameters = Parameters(new { text = new { type = "string", description = "The exact non-sensitive text the user explicitly wants copied." } }, "text") });
        tools.Add(new { type = "function", name = "browser_search", description = "Open the default browser with a Google search only when the user asks to see results in their browser. Prefer the built-in web search tool for factual lookups.", parameters = Parameters(new { query = new { type = "string", description = "Search query explicitly requested by the user." } }, "query") });
        tools.Add(new { type = "function", name = "get_system_status", description = "Read a non-personal local system status value.", parameters = Parameters(new { category = new { type = "string", description = "One of: battery, memory, storage, network." } }, "category") });
        tools.Add(new { type = "function", name = "keyboard_shortcut", description = "Send one allowlisted reversible shortcut to the most recent non-Lilith foreground app. Arbitrary keys and typing are unavailable.", parameters = Parameters(new { action = new { type = "string", description = "One of: undo, redo, save, select_all, find, refresh, fullscreen, escape." } }, "action") });
        tools.Add(new { type = "function", name = "set_timer", description = "Create a local timer that Lilith will announce. Use a duration from 0.1 to 1440 minutes.", parameters = Parameters(new { minutes = new { type = "number", description = "Timer duration in minutes." }, message = new { type = "string", description = "Short announcement when the timer ends; omit personal or sensitive information." } }, "minutes", "message") });
        tools.Add(new { type = "function", name = "cancel_timers", description = "Cancel all pending Lilith local timers when the user asks to cancel them.", parameters = Parameters(new { }) });
        tools.Add(new { type = "function", name = "lock_computer", description = "Lock Windows. Call only when the user explicitly asks to lock the computer; never infer it from context.", parameters = Parameters(new { }) });
        tools.Add(new { type = "function", name = "sleep_computer", description = "Put Windows into sleep. Call only when the user explicitly asks the computer to sleep; never infer it because the user is sleepy or leaving.", parameters = Parameters(new { }) });
        tools.Add(new { type = "function", name = "cancel_system_action", description = "Cancel a pending lock or sleep action before it runs.", parameters = Parameters(new { }) });
        return tools.ToArray();
    }

    private static void ProcessQwenToolBatches()
    {
        if (!PendingQwenToolBatches.TryDequeue(out var batch))
            return;
        try
        {
            foreach (var call in batch.Calls.Take(8))
            {
                var result = ExecuteGeminiComputerTool(call);
                batch.Session.Input.Add(new
                {
                    type = "function_call",
                    name = call.Name,
                    arguments = call.Args.ValueKind == JsonValueKind.Undefined ? "{}" : call.Args.GetRawText(),
                    call_id = call.Id
                });
                batch.Session.Input.Add(new
                {
                    type = "function_call_output",
                    call_id = call.Id,
                    output = JsonSerializer.Serialize(new { success = result.Success, result = result.Message })
                });
            }
            foreach (var call in batch.Calls.Skip(8))
            {
                batch.Session.Input.Add(new { type = "function_call", name = call.Name, arguments = "{}", call_id = call.Id });
                batch.Session.Input.Add(new { type = "function_call_output", call_id = call.Id, output = "Too many actions were requested in one turn." });
            }
            _ = ContinueQwenAgentRequestAsync(batch.Session);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Qwen desktop tool execution failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("剛才的電腦操作沒有成功。", "刚才的电脑操作没有成功。", "さっきのPC操作はうまくいかなかった……", "The computer action did not work."));
        }
    }

    private static async Task ContinueQwenAgentRequestAsync(QwenAgentSession session)
    {
        try
        {
            await SendQwenAgentRequestAsync(session).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Qwen tool continuation failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("操作已經停下來了，但回覆沒有順利接上。", "操作已经停下来了，但回复没有顺利接上。", "操作は止めたけれど、返事をうまく続けられなかった。", "The actions stopped, but I couldn't complete the follow-up response."));
        }
    }

    private static string NormalizeQwenBaseUrl(string? value)
    {
        var baseUrl = (value ?? string.Empty).Trim().TrimEnd('/');
        return baseUrl.Length > 0 ? baseUrl : "https://dashscope.aliyuncs.com/compatible-mode/v1";
    }

    private static bool IsQwenAccountUnavailable(Exception exception)
        => Regex.IsMatch(exception.ToString(), "Arrearage|overdue-payment|account is in good standing", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string QwenAccountUnavailableReply()
        => ApiKeyText(
            "千問帳戶目前被服務端拒絕了。請到阿里雲模型服務檢查免費額度或開通計費後再試。",
            "千问账户目前被服务端拒绝了。请到阿里云模型服务检查免费额度或开通计费后再试。",
            "千問アカウントがサーバー側で拒否されているよ。無料枠または課金状態を確認してから、もう一度試してね。",
            "The Qwen account was rejected by the service. Check the free quota or billing status in Model Studio and try again.");

}
