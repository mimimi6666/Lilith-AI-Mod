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
    private static async Task RequestGeminiAsync(string userText, string playerName, PoseContext poseContext)
    {
        GeminiAgentSession? agentSession = null;
        try
        {
            var japaneseVoiceMode = IsJapaneseVoiceMode();
            var interfaceLanguage = GetAiInterfaceLanguage();
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var contents = BuildGeminiContents();
            var nameContext = BuildPlayerNameContext(userText, playerName);
            var timeContext = BuildLocalTimeContext();
            var weatherContext = await BuildWeatherContextAsync().ConfigureAwait(false);
            var useGoogleSearch = ShouldUseGeminiGoogleSearch(userText);
            var activeProvider = NormalizeAiProvider(Plugin.AiProvider.Value);
            var systemInstruction = Plugin.PersonaPrompt.Value + "\n角色事實：" + Plugin.CharacterLore.Value
                + "\n情緒表達：" + Plugin.EmotionGuidance.Value + nameContext + poseContext.Prompt + timeContext + weatherContext
                + BuildCanonicalStyleGuide(poseContext)
                + $"\n語言規則：目前遊戲介面語言是{interfaceLanguage.Name}。無論使用者輸入哪種語言，氣泡顯示內容都必須使用{interfaceLanguage.Name}；只有無法翻譯的專有名詞可以保留原文。若角色設定中原有的語言要求不同，以本條規則為準。每次回答必須完成最後一句，不可停在半句、連接詞或未閉合的引號。{interfaceLanguage.ExtraRule}"
                + (japaneseVoiceMode
                    ? $"\n目前為日文語音模式。只輸出一個 JSON 物件，格式為 {{\"display_text\":\"{interfaceLanguage.Example}\",\"speech_ja\":\"語意相同但適合自然口語演出的日文\"}}。display_text 必須使用{interfaceLanguage.Name}；speech_ja 必須使用日文且不可逐字硬譯，要保留莉莉絲的情緒、停頓與女性口吻。兩個欄位都必須是完整句子，不要輸出 JSON 以外內容。"
                    : string.Empty);
            if (useGoogleSearch)
                systemInstruction += "\nThis question explicitly requests a lookup or depends on current facts. You must use the available web-search tool before answering, answer concisely in character, and never invent facts absent from the results.";
            else if (string.Equals(activeProvider, "Qwen", StringComparison.Ordinal))
                systemInstruction += "\nA web-search tool is available. Use it whenever the answer materially depends on recent or changeable facts such as news, current people or policies, prices, weather, schedules, software/model versions, service availability, or product features. Do not search for casual conversation, roleplay, personal advice, or stable facts.";
            var desktopToolsEnabled = Plugin.AdvancedComputerActionsEnabled.Value;
            if (desktopToolsEnabled
                && (string.Equals(activeProvider, "Gemini", StringComparison.Ordinal)
                    || string.Equals(activeProvider, "Qwen", StringComparison.Ordinal)))
            {
                systemInstruction += "\nDesktop agent policy: You may use the declared local desktop tools whenever they help fulfill the user's intent. Prefer tools over asking the user to repeat an exact command, and you may call several independent tools in parallel to complete a routine. Never claim an action succeeded unless its function result says success. All tools operate locally. Never request or expose passwords, API keys, OTPs, clipboard contents, file contents, browsing history, screenshots, precise location, or personal data. Never infer sleep or lock merely because the user says they are tired; call those tools only when the user explicitly asks the computer to sleep or lock. Destructive file operations, closing apps, shutdown, restart, arbitrary typing, arbitrary shortcuts, shell commands, and privilege elevation are unavailable. If a tool is unavailable, explain naturally without pretending it ran.";
            }
            if (string.Equals(activeProvider, "Qwen", StringComparison.Ordinal))
            {
                await RequestQwenResponsesAsync(systemInstruction, userText, poseContext, japaneseVoiceMode,
                    useWebSearch: true, forceWebSearch: useGoogleSearch, desktopToolsEnabled).ConfigureAwait(false);
                return;
            }
            if (!string.Equals(activeProvider, "Gemini", StringComparison.Ordinal))
            {
                await RequestOpenAiCompatibleAsync(activeProvider, systemInstruction, userText, poseContext, japaneseVoiceMode).ConfigureAwait(false);
                return;
            }
            agentSession = new GeminiAgentSession
            {
                Url = url,
                SystemInstruction = systemInstruction,
                UserText = userText,
                PoseContext = poseContext,
                JapaneseVoiceMode = japaneseVoiceMode,
                UseGoogleSearch = useGoogleSearch,
                DesktopToolsEnabled = desktopToolsEnabled,
                Contents = contents.Cast<object>().ToList()
            };
            await SendGeminiAgentRequestAsync(agentSession).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (agentSession?.DesktopToolsEnabled == true
                && exception is HttpRequestException
                && Regex.IsMatch(exception.Message, "Gemini HTTP (400|404|422)", RegexOptions.IgnoreCase))
            {
                Plugin.PluginLog.LogWarning($"Gemini desktop tools were rejected by model '{Plugin.GeminiModel.Value}'. Falling back safely: {exception.Message}");
                PendingGeminiCompatibilityFallbacks.Enqueue(agentSession);
                return;
            }
            Plugin.PluginLog.LogError(exception);
            if (string.Equals(NormalizeAiProvider(Plugin.AiProvider.Value), "Qwen", StringComparison.Ordinal)
                && IsQwenAccountUnavailable(exception))
            {
                PendingReplies.Enqueue(QwenAccountUnavailableReply());
                return;
            }
            PendingReplies.Enqueue(ApiKeyText("連線好像出了點問題。晚點再試吧。", "连接好像出了点问题。稍后再试吧。", "接続に少し問題があるみたい。あとでまた試してみて。", "There seems to be a connection problem. Please try again later."));
        }
    }

    private static async Task SendGeminiAgentRequestAsync(GeminiAgentSession session)
    {
        var payload = new Dictionary<string, object>
        {
            ["system_instruction"] = new { parts = new[] { new { text = session.SystemInstruction } } },
            ["contents"] = session.Contents,
            ["generationConfig"] = new
            {
                maxOutputTokens = 1024,
                thinkingConfig = new { thinkingLevel = "MINIMAL" }
            }
        };
        if (session.DesktopToolsEnabled)
        {
            payload["tools"] = BuildGeminiDesktopTools(session.UseGoogleSearch);
            if (session.UseGoogleSearch)
                payload["toolConfig"] = new { includeServerSideToolInvocations = true };
        }
        else if (session.UseGoogleSearch)
        {
            payload["tools"] = new object[] { new { googleSearch = new { } } };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, session.Url);
        request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var candidate = document.RootElement.GetProperty("candidates")[0];
        var content = candidate.GetProperty("content");
        var parts = content.GetProperty("parts");
        var calls = new List<GeminiFunctionCallData>();
        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textPart))
                builder.Append(textPart.GetString());
            if (!part.TryGetProperty("functionCall", out var functionCall))
                continue;
            var name = functionCall.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            var id = functionCall.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var args = functionCall.TryGetProperty("args", out var argsElement)
                ? argsElement.Clone()
                : JsonSerializer.SerializeToElement(new { });
            if (!string.IsNullOrWhiteSpace(name))
                calls.Add(new GeminiFunctionCallData { Name = name, Id = id, Args = args });
        }

        var finishReason = candidate.TryGetProperty("finishReason", out var finishElement)
            ? finishElement.GetString() ?? "UNKNOWN"
            : "UNKNOWN";
        var usageSummary = "unavailable";
        if (document.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            var promptTokens = usage.TryGetProperty("promptTokenCount", out var promptCount) ? promptCount.GetInt32() : -1;
            var outputTokens = usage.TryGetProperty("candidatesTokenCount", out var outputCount) ? outputCount.GetInt32() : -1;
            var thoughtTokens = usage.TryGetProperty("thoughtsTokenCount", out var thoughtCount) ? thoughtCount.GetInt32() : -1;
            usageSummary = $"prompt={promptTokens}, output={outputTokens}, thoughts={thoughtTokens}";
        }
        var grounded = candidate.TryGetProperty("groundingMetadata", out _);
        Plugin.PluginLog.LogInfo($"Gemini agent turn completed: finish={finishReason}, calls={calls.Count}, rawChars={builder.Length}, round={session.ToolRounds}, {usageSummary}, searchRequested={session.UseGoogleSearch}, grounded={grounded}.");

        if (calls.Count > 0)
        {
            if (session.ToolRounds >= 4)
            {
                CompleteAiReply(ApiKeyText("電腦操作步驟太多了，我先停在這裡。", "电脑操作步骤太多了，我先停在这里。", "PC操作の手順が多すぎるから、ここで止めておくね。", "There were too many computer-control steps, so I stopped here."), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
                return;
            }
            session.ToolRounds++;
            session.Contents.Add(content.Clone());
            PendingGeminiToolBatches.Enqueue(new GeminiToolBatch { Session = session, Calls = calls });
            return;
        }

        CompleteAiReply(builder.ToString(), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
    }

    private static object[] BuildGeminiDesktopTools(bool includeGoogleSearch)
    {
        static object Parameters(object properties, params string[] required) => new
        {
            type = "OBJECT",
            properties,
            required
        };

        var declarations = new object[]
        {
            new { name = "open_application", description = "Open or focus an installed application or game by its common user-facing name. Use only when the user wants an app opened; pass a concise app name, never a path or command.", parameters = Parameters(new { name = new { type = "STRING", description = "Common application or game name, such as Spotify, Discord, Steam, VALORANT, or Notepad." } }, "name") },
            new { name = "open_folder", description = "Open one allowlisted common Windows folder. Never accepts arbitrary paths.", parameters = Parameters(new { folder = new { type = "STRING", description = "One of: downloads, desktop, documents, pictures, music, videos, screenshots, mod, recycle_bin." } }, "folder") },
            new { name = "window_action", description = "Perform a reversible window-management action. Do not use close because closing apps is unavailable.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: show_desktop, task_view, switch_previous, minimize, maximize, restore, snap_left, snap_right." } }, "action") },
            new { name = "media_control", description = "Control the active system media session with a standard media key.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: play_pause, next, previous, stop, mute, volume_up, volume_down." } }, "action") },
            new { name = "take_screenshot", description = "Capture all local monitors to the user's Pictures/Lilith Screenshots folder. The screenshot remains local and is never uploaded or returned to the model.", parameters = Parameters(new { }) },
            new { name = "copy_text", description = "Write user-specified non-sensitive text to the local clipboard. Never use for passwords, API keys, OTPs, tokens, private identifiers, or other credentials. Clipboard reading is unavailable.", parameters = Parameters(new { text = new { type = "STRING", description = "The exact non-sensitive text the user explicitly wants copied." } }, "text") },
            new { name = "browser_search", description = "Open the default browser with a Google search. Use when the user explicitly wants results opened in their browser; ordinary factual questions can use Google Search instead.", parameters = Parameters(new { query = new { type = "STRING", description = "Search query explicitly requested by the user." } }, "query") },
            new { name = "get_system_status", description = "Read a non-personal local system status value.", parameters = Parameters(new { category = new { type = "STRING", description = "One of: battery, memory, storage, network." } }, "category") },
            new { name = "keyboard_shortcut", description = "Send one allowlisted reversible shortcut to the most recent non-Lilith foreground app. Arbitrary keys and typing are unavailable.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: undo, redo, save, select_all, find, refresh, fullscreen, escape." } }, "action") },
            new { name = "set_timer", description = "Create a local timer that Lilith will announce. Use a duration from 0.1 to 1440 minutes.", parameters = Parameters(new { minutes = new { type = "NUMBER", description = "Timer duration in minutes." }, message = new { type = "STRING", description = "Short announcement when the timer ends; omit personal or sensitive information." } }, "minutes", "message") },
            new { name = "cancel_timers", description = "Cancel all pending Lilith local timers when the user asks to cancel them.", parameters = Parameters(new { }) },
            new { name = "lock_computer", description = "Lock Windows. Call only when the user explicitly asks to lock the computer; never infer it from context.", parameters = Parameters(new { }) },
            new { name = "sleep_computer", description = "Put Windows into sleep. Call only when the user explicitly asks the computer to sleep; never infer it because the user is sleepy or leaving.", parameters = Parameters(new { }) },
            new { name = "cancel_system_action", description = "Cancel a pending lock or sleep action before it runs.", parameters = Parameters(new { }) }
        };
        var tools = new List<object>();
        if (includeGoogleSearch)
            tools.Add(new { googleSearch = new { } });
        tools.Add(new { functionDeclarations = declarations });
        return tools.ToArray();
    }

    private static void ProcessGeminiToolBatches()
    {
        if (!PendingGeminiToolBatches.TryDequeue(out var batch))
            return;
        try
        {
            var results = new List<GeminiToolResult>();
            foreach (var call in batch.Calls.Take(8))
                results.Add(ExecuteGeminiComputerTool(call));
            foreach (var call in batch.Calls.Skip(8))
                results.Add(new GeminiToolResult { Name = call.Name, Id = call.Id, Success = false, Message = "Too many actions were requested in one turn." });

            var responseParts = new List<object>();
            foreach (var result in results)
            {
                var functionResponse = new Dictionary<string, object>
                {
                    ["name"] = result.Name,
                    ["response"] = new { success = result.Success, result = result.Message }
                };
                if (!string.IsNullOrWhiteSpace(result.Id))
                    functionResponse["id"] = result.Id;
                responseParts.Add(new Dictionary<string, object> { ["functionResponse"] = functionResponse });
            }
            batch.Session.Contents.Add(new { role = "user", parts = responseParts.ToArray() });
            _ = ContinueGeminiAgentRequestAsync(batch.Session);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini desktop tool execution failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("剛才的電腦操作沒有成功。", "刚才的电脑操作没有成功。", "さっきのPC操作はうまくいかなかった……", "The computer action did not work."));
        }
    }

    private static void ProcessGeminiCompatibilityFallbacks()
    {
        if (!PendingGeminiCompatibilityFallbacks.TryDequeue(out var session))
            return;
        try
        {
            string reply;
            var handled = TryHandleScreenshotCommand(session.UserText, out reply)
                || TryHandleComputerCommand(session.UserText, out reply)
                || TryHandleMediaCommand(session.UserText, out reply)
                || TryLaunchApplicationCommand(session.UserText, out reply);
            if (handled)
            {
                AddMemoryTurn("model", reply);
                PendingAiEmotions.Enqueue("emoji_smile_1");
                PendingReplies.Enqueue(reply);
                if (Plugin.VoiceEnabled.Value)
                    _ = RequestSpeechAsync(reply, poseStyle: session.PoseContext.VoiceStyle);
                Plugin.PluginLog.LogInfo("Completed a desktop command through the compatibility fallback router.");
                return;
            }

            session.DesktopToolsEnabled = false;
            session.SystemInstruction += "\nDesktop function calling is unavailable on the selected model or API version. Do not claim that a computer action was performed. Continue as normal conversation and briefly explain if the requested action cannot be completed.";
            _ = ContinueGeminiAgentRequestAsync(session);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini compatibility fallback failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("這個模型目前不能使用電腦工具。", "这个模型目前不能使用电脑工具。", "このモデルでは今、PCツールを使えないみたい。", "This model cannot use the computer tools right now."));
        }
    }

    private static async Task ContinueGeminiAgentRequestAsync(GeminiAgentSession session)
    {
        try
        {
            await SendGeminiAgentRequestAsync(session).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini tool continuation failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("操作已經停下來了，但回覆沒有順利接上。", "操作已经停下来了，但回复没有顺利接上。", "操作は止めたけれど、返事をうまく続けられなかった。", "The actions stopped, but I couldn't complete the follow-up response."));
        }
    }

    private static GeminiToolResult ExecuteGeminiComputerTool(GeminiFunctionCallData call)
    {
        if (!Plugin.AdvancedComputerActionsEnabled.Value)
            return ToolResult(call, false, ApiKeyText("進階電腦操作目前是關閉的。", "高级电脑操作目前已关闭。", "高度なPC操作は今オフになっているよ。", "Advanced PC controls are currently disabled."));
        try
        {
            switch (call.Name)
            {
                case "open_application":
                    return ExecuteOpenApplicationTool(call, GetToolString(call.Args, "name", 120));
                case "open_folder":
                    return ExecuteOpenFolderTool(call, GetToolString(call.Args, "folder", 40));
                case "window_action":
                    return ExecuteWindowTool(call, GetToolString(call.Args, "action", 40));
                case "media_control":
                    return ExecuteMediaTool(call, GetToolString(call.Args, "action", 40));
                case "take_screenshot":
                    return TryHandleScreenshotCommand("幫我截圖", out var screenshotReply)
                        ? ToolResultFromReply(call, screenshotReply)
                        : ToolResult(call, false, "Screenshot action was not available.");
                case "copy_text":
                    return ExecuteCopyTextTool(call, GetToolString(call.Args, "text", 4000));
                case "browser_search":
                    return ExecuteBrowserSearchTool(call, GetToolString(call.Args, "query", 500));
                case "get_system_status":
                    return ExecuteSystemStatusTool(call, GetToolString(call.Args, "category", 40));
                case "keyboard_shortcut":
                    return ExecuteKeyboardShortcutTool(call, GetToolString(call.Args, "action", 40));
                case "set_timer":
                    return ExecuteSetTimerTool(call, GetToolDouble(call.Args, "minutes"), GetToolString(call.Args, "message", 160));
                case "cancel_timers":
                    var count = LocalTimers.Count;
                    LocalTimers.Clear();
                    return ToolResult(call, true, ApiKeyText($"已取消 {count} 個計時器。", $"已取消 {count} 个计时器。", $"{count}件のタイマーを取り消したよ。", $"Cancelled {count} timer(s)."));
                case "lock_computer":
                    _pendingSystemAction = new PendingSystemAction { Action = "lock", ExecuteAfter = Time.unscaledTime + 8f };
                    return ToolResult(call, true, ApiKeyText("已安排在回覆後鎖定電腦。", "已安排在回复后锁定电脑。", "返事のあとでPCをロックするね。", "The computer will lock after this reply."));
                case "sleep_computer":
                    _pendingSystemAction = new PendingSystemAction { Action = "sleep", ExecuteAfter = Time.unscaledTime + 8f };
                    return ToolResult(call, true, ApiKeyText("已安排在回覆後讓電腦睡眠。", "已安排在回复后让电脑睡眠。", "返事のあとでPCをスリープさせるね。", "The computer will sleep after this reply."));
                case "cancel_system_action":
                    var hadAction = _pendingSystemAction != null;
                    _pendingSystemAction = null;
                    return ToolResult(call, true, hadAction
                        ? ApiKeyText("已取消待執行的系統動作。", "已取消待执行的系统操作。", "待機中のシステム操作を取り消したよ。", "The pending system action was cancelled.")
                        : ApiKeyText("目前沒有待執行的系統動作。", "目前没有待执行的系统操作。", "待機中のシステム操作はないよ。", "There is no pending system action."));
                default:
                    return ToolResult(call, false, "Unknown or unavailable desktop tool.");
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Desktop tool '{call.Name}' failed: {exception.Message}");
            return ToolResult(call, false, $"The local action failed: {exception.Message}");
        }
    }

    private static GeminiToolResult ExecuteOpenApplicationTool(GeminiFunctionCallData call, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name, "[\\\\/:*?\"<>|]"))
            return ToolResult(call, false, "A safe application name was not provided.");
        if (TryFocusRunningApplication(name))
            return ToolResult(call, true, ApiKeyText($"已切換到{name}。", $"已切换到{name}。", $"{name}に切り替えたよ。", $"Focused {name}."));
        var registered = FindConfiguredApplication(name) ?? FindOfficialApplication(name);
        if (registered != null)
        {
            Process.Start(new ProcessStartInfo(registered.Target)
            {
                Arguments = registered.Arguments ?? string.Empty,
                UseShellExecute = true
            });
            Plugin.PluginLog.LogInfo($"Opened registered allowlisted application '{registered.Name}'.");
            return ToolResult(call, true, ApiKeyText($"已開啟{registered.Name}。", $"已打开{registered.Name}。", $"{registered.Name}を開いたよ。", $"Opened {registered.Name}."));
        }
        var startApplication = ResolveWindowsStartApplication(name);
        if (startApplication != null && TryLaunchWindowsStartApplication(startApplication))
            return ToolResult(call, true, ApiKeyText($"正在開啟{startApplication.Name}。", $"正在打开{startApplication.Name}。", $"{startApplication.Name}を開いているよ。", $"Opening {startApplication.Name}."));
        var shortcut = ResolveWindowsShortcut(new[] { name });
        if (string.IsNullOrWhiteSpace(shortcut))
            shortcut = ResolveFuzzyWindowsShortcut(name);
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            if (TryLaunchApplicationCommand("開啟 " + name, out var builtInReply))
                return ToolResultFromReply(call, builtInReply);
            return ToolResult(call, false, ApiKeyText($"沒有在這台電腦找到{name}。", $"没有在这台电脑找到{name}。", $"このPCでは{name}を見つけられなかった。", $"I couldn't find {name} on this PC."));
        }
        Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
        Plugin.PluginLog.LogInfo($"Opened an installed application from a Windows shortcut named '{Path.GetFileNameWithoutExtension(shortcut)}'.");
        return ToolResult(call, true, ApiKeyText($"已開啟{name}。", $"已打开{name}。", $"{name}を開いたよ。", $"Opened {name}."));
    }

    private static GeminiToolResult ExecuteOpenFolderTool(GeminiFunctionCallData call, string folder)
    {
        var command = folder.Trim().ToLowerInvariant() switch
        {
            "downloads" => "打開下載資料夾",
            "desktop" => "打開桌面資料夾",
            "documents" => "打開文件資料夾",
            "pictures" => "打開圖片資料夾",
            "music" => "打開音樂資料夾",
            "videos" => "打開影片資料夾",
            "screenshots" => "打開截圖資料夾",
            "mod" => "打開MOD資料夾",
            "recycle_bin" => "打開資源回收筒",
            _ => string.Empty
        };
        return command.Length > 0 && TryOpenKnownFolder(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That folder is not in the local allowlist.");
    }

    private static GeminiToolResult ExecuteWindowTool(GeminiFunctionCallData call, string action)
    {
        var command = action.Trim().ToLowerInvariant() switch
        {
            "show_desktop" => "顯示桌面",
            "task_view" => "打開工作檢視",
            "switch_previous" => "切換到上一個視窗",
            "minimize" => "最小化視窗",
            "maximize" => "最大化視窗",
            "restore" => "還原視窗",
            "snap_left" => "把視窗排到左側",
            "snap_right" => "把視窗排到右側",
            _ => string.Empty
        };
        return command.Length > 0 && TryHandleWindowCommand(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That window action is not available.");
    }

    private static GeminiToolResult ExecuteMediaTool(GeminiFunctionCallData call, string action)
    {
        var command = action.Trim().ToLowerInvariant() switch
        {
            "play_pause" => "暫停音樂",
            "next" => "下一首",
            "previous" => "上一首",
            "stop" => "停止音樂",
            "mute" => "靜音",
            "volume_up" => "音量大一點",
            "volume_down" => "音量小一點",
            _ => string.Empty
        };
        return command.Length > 0 && TryHandleMediaCommand(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That media action is not available.");
    }

    private static GeminiToolResult ExecuteCopyTextTool(GeminiFunctionCallData call, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ToolResult(call, false, "No text was provided.");
        if (ContainsSensitiveNoteData(content))
            return ToolResult(call, false, "Credential-like or personal text was blocked and was not copied.");
        GUIUtility.systemCopyBuffer = content;
        Plugin.PluginLog.LogInfo($"AI tool copied user-specified text locally ({content.Length} chars; content hidden)." );
        return ToolResult(call, true, ApiKeyText("文字已複製到本機剪貼簿。", "文字已复制到本机剪贴板。", "文字をローカルのクリップボードにコピーしたよ。", "The text was copied to the local clipboard."));
    }

    private static GeminiToolResult ExecuteBrowserSearchTool(GeminiFunctionCallData call, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult(call, false, "No search query was provided.");
        if (ContainsSensitiveNoteData(query))
            return ToolResult(call, false, "The search was blocked because it may contain personal or credential information.");
        Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(query)) { UseShellExecute = true });
        Plugin.PluginLog.LogInfo($"Opened a user-requested browser search ({query.Length} chars; query hidden)." );
        return ToolResult(call, true, ApiKeyText("已在預設瀏覽器開啟搜尋。", "已在默认浏览器打开搜索。", "既定のブラウザで検索を開いたよ。", "The search was opened in the default browser."));
    }

    private static GeminiToolResult ExecuteSystemStatusTool(GeminiFunctionCallData call, string category)
    {
        var command = category.Trim().ToLowerInvariant() switch
        {
            "battery" => "現在電量多少",
            "memory" => "電腦記憶體使用狀態",
            "storage" => "系統磁碟還有多少空間",
            "network" => "網路連線狀態",
            _ => string.Empty
        };
        return command.Length > 0 && TryReportSystemStatus(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That system status category is not available.");
    }

    private static GeminiToolResult ExecuteKeyboardShortcutTool(GeminiFunctionCallData call, string action)
    {
        var target = GetControllableWindow();
        if (target == IntPtr.Zero || !SetForegroundWindow(target))
            return ToolResult(call, false, "No non-Lilith foreground window was available.");
        switch (action.Trim().ToLowerInvariant())
        {
            case "undo": SendShortcut(0x11, 0x5A); break;
            case "redo": SendShortcut(0x11, 0x59); break;
            case "save": SendShortcut(0x11, 0x53); break;
            case "select_all": SendShortcut(0x11, 0x41); break;
            case "find": SendShortcut(0x11, 0x46); break;
            case "refresh": SendShortcut(0x74); break;
            case "fullscreen": SendShortcut(0x7A); break;
            case "escape": SendShortcut(0x1B); break;
            default: return ToolResult(call, false, "That keyboard shortcut is not allowlisted.");
        }
        Plugin.PluginLog.LogInfo($"Sent allowlisted keyboard shortcut '{action}'.");
        return ToolResult(call, true, $"The allowlisted '{action}' shortcut was sent to the previous foreground app.");
    }

    private static GeminiToolResult ExecuteSetTimerTool(GeminiFunctionCallData call, double minutes, string message)
    {
        if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0.1 || minutes > 1440)
            return ToolResult(call, false, "Timer duration must be between 0.1 and 1440 minutes.");
        if (string.IsNullOrWhiteSpace(message))
            message = ApiKeyText("時間到了。", "时间到了。", "時間だよ。", "Time is up.");
        LocalTimers.Add(new LocalTimer { DueAt = DateTimeOffset.Now.AddMinutes(minutes), Message = message.Trim() });
        Plugin.PluginLog.LogInfo($"Created local Lilith timer for {minutes:0.##} minute(s); message hidden.");
        return ToolResult(call, true, ApiKeyText($"已設定 {minutes:0.##} 分鐘的計時器。", $"已设置 {minutes:0.##} 分钟的计时器。", $"{minutes:0.##}分のタイマーを設定したよ。", $"Set a timer for {minutes:0.##} minute(s)."));
    }

    private static GeminiToolResult ToolResultFromReply(GeminiFunctionCallData call, string reply)
    {
        var failed = Regex.IsMatch(reply, "(沒有成功|没有成功|找不到|沒有找到|没有找到|不能|無法|无法|失敗|失败|できなかった|見つから|couldn't|could not|failed|not available)", RegexOptions.IgnoreCase);
        return ToolResult(call, !failed, reply);
    }

    private static GeminiToolResult ToolResult(GeminiFunctionCallData call, bool success, string message)
        => new() { Name = call.Name, Id = call.Id, Success = success, Message = message };

    private static string GetToolString(JsonElement args, string name, int maximumLength)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return string.Empty;
        var text = value.GetString()?.Trim() ?? string.Empty;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private static double GetToolDouble(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
            return double.NaN;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : double.NaN;
    }

    private static bool ShouldUseGeminiGoogleSearch(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return false;

        return Regex.IsMatch(userText,
            "(?:幫我|帮我)?(?:查查|查一下|查詢|查询|搜尋|搜索|上網查|联网查)|(?:最新|新聞|新闻|即時|即时|現任|现任|今天|今日|今年)|(?:目前|最近|新版|更新後).*(?:消息|資訊|信息|情報|進度|进度|價格|价格|費用|费用|比賽|比赛|天氣|天气|版本|模型|功能|政策|規定|规定|支援|支持|是誰|是谁)|(?:調べて|検索して|ネットで調べて|最新|ニュース|今日)|(?:現在|最近).*(?:ニュース|価格|天気|バージョン|モデル|機能|対応|予定)|(?:look\\s*(?:it|this)?\\s*up|search(?:\\s+the)?\\s+web|google\\s+it|find\\s+online|latest|current|today|recent)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

}
