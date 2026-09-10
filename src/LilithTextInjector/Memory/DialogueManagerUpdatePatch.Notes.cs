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
    private static void TryCreateOneTestNote()
    {
        if (_testNoteAttempted || !Plugin.TestNoteOnce.Value || Time.unscaledTime < 8f)
            return;
        _testNoteAttempted = true;
        Plugin.TestNoteOnce.Value = false;
        try
        {
            var text = ApiKeyText(
                "剛才聊到，要把那些原本藏在日常裡的小巧思延續下去。若你看見這封信，就代表莉莉絲真的能把我們的談話留在這裡了。——莉莉絲",
                "刚才聊到，要把那些原本藏在日常里的小巧思延续下去。若你看见这封信，就代表莉莉丝真的能把我们的谈话留在这里了。——莉莉丝",
                "さっき、日常に隠れている小さな仕掛けを、これからも大切にしようって話したね。この手紙が届いたなら、私たちの会話をここに残せたということ。——リリス",
                "We talked about carrying those little details hidden in everyday life forward. If this letter reached you, Lilith can truly leave a trace of our conversation here. —Lilith");
            var path = NoteImageSaver.SaveNote(text, false);
            NoteInbox.NotifySaved();
            Plugin.PluginLog.LogInfo($"Created one native-format test note: {path}");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not create the one-time test note: {exception.Message}");
        }
    }

    internal static void LoadAiNoteState()
    {
        try
        {
            Directory.CreateDirectory(MemoryDirectory);
            if (File.Exists(AiNoteStatePath))
                _aiNoteState = JsonSerializer.Deserialize<AiNoteState>(File.ReadAllText(AiNoteStatePath)) ?? new AiNoteState();
            foreach (var item in _aiNoteState.Pending)
                if (item.Status == "generating") item.Status = "pending";
            _aiNoteState.DeliveredAt.RemoveAll(value => value < DateTimeOffset.Now.AddDays(-30));
            SaveAiNoteState();
            Plugin.PluginLog.LogInfo($"Loaded AI note scheduler: pending={_aiNoteState.Pending.Count}, recentDeliveries={_aiNoteState.DeliveredAt.Count}.");
        }
        catch (Exception exception)
        {
            _aiNoteState = new AiNoteState();
            Plugin.PluginLog.LogWarning($"Could not load AI note state: {exception.Message}");
        }
    }

    private static void SaveAiNoteState()
    {
        lock (AiNoteLock)
        {
            Directory.CreateDirectory(MemoryDirectory);
            File.WriteAllText(AiNoteStatePath, JsonSerializer.Serialize(_aiNoteState,
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
        }
    }

    private static void ConsiderAiNoteEvent(string userText, string reply)
    {
        if (!Plugin.AiNotesEnabled.Value || ContainsSensitiveNoteData(userText))
            return;
        if (Regex.IsMatch(userText, "(取消|算了|不用了|別再提|别再提|忘了吧|キャンセル|やめて|forget it|cancel)", RegexOptions.IgnoreCase))
            return;

        string category;
        string emotion;
        if (Regex.IsMatch(userText, "(明天|後天|下週|下周|等等要|待會要|考試|面試|報告|手術|約會|旅行|出發|deadline|tomorrow|exam|interview|明日|試験|面接)", RegexOptions.IgnoreCase))
        {
            category = "upcoming";
            emotion = Regex.IsMatch(userText, "(緊張|害怕|擔心|焦慮|不安|怖い|心配|nervous|worried)", RegexOptions.IgnoreCase) ? "緊張，需要溫柔支持" : "期待中的重要安排";
        }
        else if (Regex.IsMatch(userText, "(完成了|成功了|做到了|通過了|过了|終於|终于|畢業|毕业|錄取|得獎|できた|合格|finished|succeeded|passed)", RegexOptions.IgnoreCase))
        {
            category = "achievement";
            emotion = "值得一起慶祝與記住";
        }
        else if (Regex.IsMatch(userText, "(難過|伤心|傷心|哭了|寂寞|孤單|失戀|分手|失敗|失败|被罵|壓力|压力|悲しい|寂しい|つらい|sad|lonely|heartbroken)", RegexOptions.IgnoreCase))
        {
            category = "comfort";
            emotion = "低落，需要安靜陪伴而非說教";
        }
        else if (Regex.IsMatch(userText, "(約定|答應我|記得|不要忘記|我們說好|说好|約束|覚えて|promise|remember this)", RegexOptions.IgnoreCase))
        {
            category = "promise";
            emotion = "兩人之間值得珍惜的約定";
        }
        else if (Regex.IsMatch(userText, "(喜歡妳|喜欢你|愛妳|爱你|謝謝妳|谢谢你|有妳真好|陪著我|大好き|愛してる|ありがとう|love you|thank you)", RegexOptions.IgnoreCase))
        {
            category = "bond";
            emotion = "親近、感謝與依戀";
        }
        else
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var minimum = Math.Clamp(Plugin.AiNoteMinimumDelayMinutes.Value, 1, 1440);
        var maximum = Math.Clamp(Plugin.AiNoteMaximumDelayMinutes.Value, minimum, 2880);
        var delay = System.Random.Shared.Next(minimum, maximum + 1);
        var compactUser = CompactNoteContext(userText, 360);
        var compactReply = CompactNoteContext(reply, 360);
        lock (AiNoteLock)
        {
            var existing = _aiNoteState.Pending.LastOrDefault(item => item.Status == "pending" && item.Category == category && item.CreatedAt > now.AddHours(-6));
            if (existing != null)
            {
                existing.Topic = compactUser;
                existing.LatestContext = compactReply;
                existing.Emotion = emotion;
                SaveAiNoteState();
                Plugin.PluginLog.LogInfo($"Updated pending AI note event '{category}'.");
                return;
            }
            _aiNoteState.Pending.Add(new AiNoteEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Category = category,
                Topic = compactUser,
                LatestContext = compactReply,
                Emotion = emotion,
                CreatedAt = now,
                DeliverAfter = now.AddMinutes(delay),
                Status = "pending"
            });
            while (_aiNoteState.Pending.Count(item => item.Status == "pending") > 5)
                _aiNoteState.Pending.Remove(_aiNoteState.Pending.First(item => item.Status == "pending"));
            SaveAiNoteState();
        }
        Plugin.PluginLog.LogInfo($"Scheduled AI note event '{category}' after {delay} minutes (content hidden from log).");
    }

    private static void UpdatePendingAiNoteEvents(string userText)
    {
        if (!Plugin.AiNotesEnabled.Value)
            return;
        var cancel = Regex.IsMatch(userText, "(取消了|取消吧|算了|不用了|不去了|別再提|别再提|忘了吧|キャンセル|中止|やめて|cancel|forget it)", RegexOptions.IgnoreCase);
        var completed = Regex.IsMatch(userText, "(完成了|結束了|结束了|考完了|做完了|成功了|通過了|过了|終於好了|終わった|できた|finished|done|passed)", RegexOptions.IgnoreCase);
        if (!cancel && !completed)
            return;
        lock (AiNoteLock)
        {
            var item = _aiNoteState.Pending.LastOrDefault(value => value.Status == "pending");
            if (item == null) return;
            if (cancel)
            {
                item.Status = "cancelled";
                Plugin.PluginLog.LogInfo($"Cancelled pending AI note event '{item.Category}' from follow-up context.");
            }
            else
            {
                item.Category = "achievement";
                item.Emotion = "事情已經完成，適合祝賀並關心結果";
                item.LatestContext = CompactNoteContext(userText, 360);
                Plugin.PluginLog.LogInfo("Updated pending AI note event after the user reported completion.");
            }
            SaveAiNoteState();
        }
    }

    private static bool ContainsSensitiveNoteData(string text)
    {
        return Regex.IsMatch(text, "(api[ _-]?key|密碼|密码|驗證碼|验证码|信用卡|身分證|身份证|護照|passport|password|token|secret)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\b(?:\d[ -]*?){12,19}\b")
            || Regex.IsMatch(text, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase);
    }

    private static string CompactNoteContext(string text, int maximum)
    {
        var compact = Regex.Replace(text, @"\s+", " ").Trim();
        return compact.Length <= maximum ? compact : compact[..maximum] + "…";
    }

    private static void ProcessAiNoteScheduler()
    {
        while (PendingAiNotes.TryDequeue(out var generated))
        {
            try
            {
                var path = NoteImageSaver.SaveNote(generated.Text, false);
                NoteInbox.NotifySaved();
                lock (AiNoteLock)
                {
                    var item = _aiNoteState.Pending.FirstOrDefault(value => value.Id == generated.EventId);
                    if (item != null) item.Status = "delivered";
                    _aiNoteState.DeliveredAt.Add(DateTimeOffset.Now);
                    _aiNoteState.DeliveredPaths.Add(path);
                    _aiNoteState.DeliveredAt.RemoveAll(value => value < DateTimeOffset.Now.AddDays(-30));
                    SaveAiNoteState();
                }
                Plugin.PluginLog.LogInfo($"Delivered scheduled AI note through the native inbox: {path}");
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not save generated AI note: {exception.Message}");
            }
            _aiNoteGenerationInFlight = false;
        }

        if (!Plugin.AiNotesEnabled.Value || _aiNoteGenerationInFlight || Time.unscaledTime < _nextAiNoteCheckAt)
            return;
        _nextAiNoteCheckAt = Time.unscaledTime + 30f;
        var now = DateTimeOffset.Now;
        AiNoteEvent? due;
        lock (AiNoteLock)
            due = _aiNoteState.Pending.FirstOrDefault(item => item.Status == "pending" && item.DeliverAfter <= now);
        if (due == null)
            return;
        if (GetWindowsIdleSeconds() < Math.Clamp(Plugin.AiNoteRequiredAwayMinutes.Value, 0, 1440) * 60)
            return;
        var cooldown = TimeSpan.FromHours(Math.Clamp(Plugin.AiNoteCooldownHours.Value, 1, 720));
        if (_aiNoteState.DeliveredAt.Count > 0 && now - _aiNoteState.DeliveredAt.Max() < cooldown)
            return;
        if (_aiNoteState.DeliveredAt.Count(value => value >= now.AddDays(-7)) >= Math.Clamp(Plugin.AiNoteWeeklyLimit.Value, 1, 20))
            return;
        if (OfficialNoteWasJustDelivered(now))
            return;
        if (string.IsNullOrWhiteSpace(Plugin.GeminiApiKey.Value))
            return;

        due.Status = "generating";
        SaveAiNoteState();
        _aiNoteGenerationInFlight = true;
        var language = GetAiInterfaceLanguage().Name;
        _ = GenerateAiNoteAsync(due, language);
    }

    private static bool OfficialNoteWasJustDelivered(DateTimeOffset now)
    {
        try
        {
            var directory = NoteInbox.NotesDirectory;
            if (!Directory.Exists(directory)) return false;
            var newest = Directory.GetFiles(directory, "note_*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (newest == null || now - File.GetLastWriteTime(newest) > TimeSpan.FromMinutes(30)) return false;
            return !_aiNoteState.DeliveredPaths.Any(path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(newest), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static async Task GenerateAiNoteAsync(AiNoteEvent item, string language)
    {
        try
        {
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var prompt = $"Write one private desktop-pet letter in {language} as Lilith. Event category: {item.Category}. User topic: {item.Topic}. Earlier Lilith reply: {item.LatestContext}. Emotional direction: {item.Emotion}. Write 45-110 natural words or equivalent characters. Be warm, slightly playful and intimate, with subtle themes of memory, choice, and shared existence only when natural. Do not mention AI, scheduling, stored memory, APIs, or monitoring. Do not give medical or professional claims. Do not repeat sensitive details. End with an em dash and Lilith's localized name. Output only the letter text.";
            var payload = new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                generationConfig = new { maxOutputTokens = 320, thinkingConfig = new { thinkingLevel = "MINIMAL" } }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini note HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var text = string.Concat(parts.EnumerateArray().Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString())).Trim();
            text = CleanReply(text);
            if (text.Length == 0) throw new InvalidOperationException("Gemini returned an empty note.");
            PendingAiNotes.Enqueue(new GeneratedAiNote { EventId = item.Id, Text = text });
        }
        catch (Exception exception)
        {
            lock (AiNoteLock)
            {
                item.Status = "pending";
                item.DeliverAfter = DateTimeOffset.Now.AddHours(1);
                SaveAiNoteState();
            }
            _aiNoteGenerationInFlight = false;
            Plugin.PluginLog.LogWarning($"AI note generation failed and was deferred: {exception.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class AiNoteState
    {
        public List<AiNoteEvent> Pending { get; set; } = new();
        public List<DateTimeOffset> DeliveredAt { get; set; } = new();
        public List<string> DeliveredPaths { get; set; } = new();
    }

    private sealed class AiNoteEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string LatestContext { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset DeliverAfter { get; set; }
        public string Status { get; set; } = "pending";
    }

    private sealed class GeneratedAiNote
    {
        public string EventId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

}
