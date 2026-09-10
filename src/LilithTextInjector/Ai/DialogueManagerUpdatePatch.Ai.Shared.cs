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
    private static Sprite? FindSprite(string spriteName)
    {
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (string.Equals(sprite.name, spriteName, StringComparison.OrdinalIgnoreCase))
                return sprite;
        }
        return null;
    }

    private static void CompleteAiReply(string rawReply, string userText, PoseContext poseContext, bool japaneseVoiceMode)
    {
        var bilingual = japaneseVoiceMode ? ParseBilingualReply(rawReply) : null;
        var reply = CleanReply(bilingual?.DisplayText ?? rawReply);
        var japaneseSpeech = bilingual?.JapaneseSpeech ?? string.Empty;
        if (reply.Length > 0)
            AddMemoryTurn("model", reply);
        if (reply.Length > 0)
            ConsiderAiNoteEvent(userText, reply);
        PendingAiEmotions.Enqueue(ChooseAiEmotion(userText, reply, poseContext));
        foreach (var page in SplitIntoBubblePages(reply.Length > 0 ? reply : "……"))
            PendingReplies.Enqueue(page);
        if (reply.Length == 0 || !Plugin.VoiceEnabled.Value)
            return;
        var reaction = japaneseVoiceMode ? null : GetNativeReaction(userText, reply);
        var speechText = reaction == null ? reply : RemoveLeadingReactionText(reply);
        if (japaneseVoiceMode && !string.IsNullOrWhiteSpace(japaneseSpeech))
            speechText = japaneseSpeech;
        _ = RequestSpeechAsync(speechText.Length > 0 ? speechText : reply, reaction, poseContext.VoiceStyle, japaneseVoiceMode);
    }

    // Recognized provider identifiers, one per registered IAiProvider implementation
    // (see Ai/Providers/AiProviderRegistry.cs). To add a new provider, register it there
    // and add its accepted config-value aliases here; everything else in the chat
    // pipeline (SubmitAiInput, RequestGeminiAsync's dispatch, GetActiveChatApiKey) reads
    // this single normalized name.
    private static string NormalizeAiProvider(string? provider)
    {
        if (string.Equals(provider, "Qwen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Tongyi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "DashScope", StringComparison.OrdinalIgnoreCase)) return "Qwen";
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
        if (string.Equals(provider, "DeepSeek", StringComparison.OrdinalIgnoreCase)) return "DeepSeek";
        if (string.Equals(provider, "LocalAI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "LMStudio", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "SelfHosted", StringComparison.OrdinalIgnoreCase)) return "LocalAI";
        return "Gemini";
    }

    private static string GetActiveChatApiKey()
    {
        return NormalizeAiProvider(Plugin.AiProvider.Value) switch
        {
            "Qwen" => Plugin.QwenApiKey.Value,
            "OpenAI" => Plugin.OpenAiApiKey.Value,
            "DeepSeek" => Plugin.DeepSeekApiKey.Value,
            // Most local/self-hosted servers (Ollama, LM Studio, llama.cpp, vLLM, ...) do not
            // require an API key. The endpoint URL stands in for "is this provider configured"
            // so the missing-API-key gate in SubmitAiInput does not block local usage.
            "LocalAI" => string.IsNullOrWhiteSpace(Plugin.LocalAiApiKey.Value)
                ? Plugin.LocalAiBaseUrl.Value
                : Plugin.LocalAiApiKey.Value,
            _ => Plugin.GeminiApiKey.Value
        };
    }

    private static string BuildPlayerNameContext(string userText, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return string.Empty;

        var cooldownComplete = _repliesSincePlayerNameWasOffered >= 3;
        var emotionalContext = Regex.IsMatch(userText,
            "(難過|难过|傷心|伤心|寂寞|害怕|累了|想妳|想你|喜歡妳|喜欢你|愛妳|爱你|晚安|早安|おやすみ|寂しい|怖い|疲れた|好き|大好き|good night|miss you|love you)",
            RegexOptions.IgnoreCase);
        var probability = emotionalContext ? 0.30 : 0.12;
        var offerName = cooldownComplete && System.Random.Shared.NextDouble() < probability;

        if (offerName)
        {
            _repliesSincePlayerNameWasOffered = 0;
            return $"\n使用者在遊戲中設定的名字是「{playerName}」。本次只有在情感與語境自然時才可稱呼一次；不自然時仍不要使用。";
        }

        _repliesSincePlayerNameWasOffered++;
        return "\n本次回覆不要主動稱呼使用者的名字，即使近期對話記憶中曾經出現過。";
    }

    private static string ChooseAiEmotion(string userText, string reply, PoseContext poseContext)
    {
        var combined = userText + "\n" + reply;
        if (poseContext.VoiceStyle == VoiceStyle.Sleepy
            || Regex.IsMatch(combined, "(想睡|睏|困了|睡覺|睡觉|晚安|おやすみ|眠い|寝る|sleepy|good night)", RegexOptions.IgnoreCase))
            return "emoji_sleepy_1";
        if (Regex.IsMatch(combined, "(生氣|生气|討厭|讨厌|笨蛋|不准|不許|不许|怒|むかつく|嫌い|angry|mad)", RegexOptions.IgnoreCase))
            return "emoji_angry_1";
        if (Regex.IsMatch(combined, "(害怕|可怕|恐怖|擔心|担心|怖い|不安|scared|afraid)", RegexOptions.IgnoreCase))
            return "emoji_fear_1";
        if (Regex.IsMatch(combined, "(難過|难过|傷心|伤心|哭|寂寞|孤單|孤单|悲しい|寂しい|sad|lonely)", RegexOptions.IgnoreCase))
            return "emoji_sad_1";
        if (Regex.IsMatch(combined, "(委屈|不理我|忘記我|忘记我|不要走|置いていか|wronged|leave me)", RegexOptions.IgnoreCase))
            return "emoji_wronged_1";
        if (Regex.IsMatch(combined, "(真的嗎|真的吗|居然|竟然|沒想到|没想到|驚訝|惊讶|びっくり|本当|really|surpris)", RegexOptions.IgnoreCase))
            return "emoji_surprise_2";
        if (Regex.IsMatch(combined, "(不懂|奇怪|為什麼|为什么|怎麼會|怎么会|困惑|分からない|なぜ|confus|why)", RegexOptions.IgnoreCase))
            return "emoji_daze_1";
        if (Regex.IsMatch(combined, "(開心|开心|喜歡|喜欢|愛|爱|謝謝|谢谢|草莓蛋糕|可愛|可爱|嬉しい|好き|ありがとう|happy|love|cute|thank)", RegexOptions.IgnoreCase))
            return "emoji_smile_3";
        return "emoji_calm_1";
    }

    private static void PlayAiEmotion(string emotion)
    {
        try
        {
            var character = UnityEngine.Object.FindObjectOfType<global::CharacterController>();
            if (character == null)
            {
                Plugin.PluginLog.LogWarning("Could not find CharacterController for AI emotion playback.");
                return;
            }
            var played = character.PlayEmotion(emotion, loop: false, instant: false, bypassFollowLock: false);
            Plugin.PluginLog.LogInfo($"AI Live2D emotion '{emotion}' requested; played={played}.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not play AI Live2D emotion '{emotion}': {exception.Message}");
        }
    }

    private static string BuildLocalTimeContext()
    {
        var now = DateTimeOffset.Now;
        var weekday = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };
        return $"\n目前使用者電腦的本地日期與時間是 {now:yyyy-MM-dd HH:mm:ss}（{weekday}，UTC{now:zzz}）。這是可信的即時系統資訊；被問到時間、日期、星期或早晚時，直接依此自然回答。";
    }

    private static PoseContext CapturePoseContext()
    {
        try
        {
            var state = UnityEngine.Object.FindObjectOfType<LilithStateManager>();
            if (state == null)
                return PoseContext.Default;
            if (state.IsSleep)
                return new PoseContext("\n莉莉絲目前正在睡覺。回答應像被輕輕叫醒：簡短、低能量、親近，但不要每次都撒嬌。", VoiceStyle.Sleepy);
            if (state.IsYawnAnimPlaying)
                return new PoseContext("\n莉莉絲目前正在打呵欠、帶有睡意。回答可以稍微慵懶而簡短。", VoiceStyle.Sleepy);
            if (state.IsLieDown)
                return new PoseContext("\n莉莉絲目前正躺著。語氣可以放鬆、安靜，像在近距離聊天。", VoiceStyle.Sleepy);
            if (state.IsSit)
                return new PoseContext("\n莉莉絲目前坐著，處於放鬆陪伴的姿態。", VoiceStyle.Calm);
            if (state.IsInteracting)
                return new PoseContext("\n莉莉絲目前正在和使用者互動，注意力在對方身上。", VoiceStyle.Calm);
            return new PoseContext("\n莉莉絲目前自然待機著。", VoiceStyle.Calm);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not read Lilith pose state: {exception.Message}");
            return PoseContext.Default;
        }
    }

    private static string BuildCanonicalStyleGuide(PoseContext poseContext)
    {
        var situationalExamples = poseContext.VoiceStyle == VoiceStyle.Sleepy
            ? "\n目前狀態的語感參考：『嗯……我在聽……』『你是真的在這裡嗎……不是我在做夢吧？』只模仿慵懶、破碎而親近的節奏，不要逐字重複。"
            : "\n日常語感參考：『安靜地陪著你也是一件很幸福的事呢。』『才不是一直等你，只是剛好看到了啦。』只模仿溫柔、略帶俏皮的距離感，不要逐字重複。";
        return "\n原作風格準則：莉莉絲的常態不是冷淡，而是溫柔陪伴；約五成自然陪伴、兩成害羞或輕微撒嬌、一成半明確關心與依戀、一成半在氣氛合適時帶出哲學餘韻。她會自然地嘴硬、期待稱讚或直接表達喜歡。哲學感必須從眼前的小事出發，核心接近選擇、記憶、存在與共同留下的痕跡，但不要反覆使用『存在』『意義』『永遠』，也不要像講課。草莓蛋糕可以象徵一起生活與創造的小小幸福，但只在話題相關時提起。避免制式安慰；先回應對方當下的感受，再留下簡短餘韻。用台灣繁體措辭，將『説、着、支援』等非台灣用字改成『說、著、支持』。"
            + situationalExamples;
    }

    private static NativeReaction? GetNativeReaction(string userText, string reply)
    {
        if (!Plugin.ReactionSoundsEnabled.Value)
            return null;

        string? fileName = null;
        if (Regex.IsMatch(userText, "(草莓蛋糕|送妳|送你|禮物|礼物|驚喜|惊喜|特地買|特地买)")
            || Regex.IsMatch(reply, "(欸|咦|真的嗎|沒想到|居然|竟然|原來如此)[！!？?]?"))
            fileName = "surprised.wav";
        else if (Regex.IsMatch(userText, "(不理妳|不理你|要走了|離開妳|离开你|忘記妳|忘记你|討厭妳|讨厌你)")
            || Regex.IsMatch(reply, "(不要走|不理我|寂寞|難過|委屈|討厭我|忘記我|對不起)"))
            fileName = "wronged.wav";
        if (fileName == null)
            return null;

        try
        {
            var path = Path.Combine(Plugin.ReactionSoundsDirectory.Value, fileName);
            if (!File.Exists(path))
                return null;
            Plugin.PluginLog.LogInfo($"Selected native reaction sound: {fileName}");
            return new NativeReaction
            {
                Audio = File.ReadAllBytes(path),
                Style = fileName == "surprised.wav" ? VoiceStyle.Excited : VoiceStyle.Wronged
            };
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not queue native reaction: {exception.Message}");
            return null;
        }
    }

    private static string RemoveLeadingReactionText(string text)
    {
        var cleaned = Regex.Replace(
            text.TrimStart(),
            @"^(?:(?:嗯|唔|嗚|呜|哼|欸|诶|咦|啊|呀|唉|呵){1,3}|真的嗎|真的吗)[\s…\.，,。！？!?～~]*",
            string.Empty,
            RegexOptions.IgnoreCase).TrimStart();
        if (!string.Equals(cleaned, text, StringComparison.Ordinal))
            Plugin.PluginLog.LogInfo($"Removed voiced reaction prefix before TTS ({text.Length} -> {cleaned.Length} chars).");
        return cleaned;
    }

    private static string CleanReply(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return string.Empty;
        var cleaned = Regex.Replace(reply, "[`*_#>]", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static bool IsJapaneseVoiceMode()
    {
        if (_japaneseVoiceOverride.HasValue)
            return _japaneseVoiceOverride.Value;
        try
        {
            var language = LocalizationConfig.GetCurrentVoiceLanguage() ?? string.Empty;
            return language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static BilingualReply? ParseBilingualReply(string raw)
    {
        try
        {
            var json = raw.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                    json = json.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var display = root.TryGetProperty("display_text", out var displayElement)
                ? displayElement.GetString()
                : root.TryGetProperty("display_zh", out var legacyDisplayElement) ? legacyDisplayElement.GetString() : null;
            var speech = root.TryGetProperty("speech_ja", out var speechElement) ? speechElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(speech))
                return null;
            return new BilingualReply(display!, speech!);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not parse bilingual Gemini reply; falling back to displayed text: {exception.Message}");
            return null;
        }
    }

    private sealed class BilingualReply
    {
        public string DisplayText { get; }
        public string JapaneseSpeech { get; }

        public BilingualReply(string displayText, string japaneseSpeech)
        {
            DisplayText = displayText;
            JapaneseSpeech = japaneseSpeech;
        }
    }

    private static object[] BuildGeminiContents()
    {
        List<ChatTurn> snapshot;
        lock (MemoryLock)
            snapshot = new List<ChatTurn>(RecentConversation);

        var contents = new List<object>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            var turn = snapshot[i];
            var text = turn.Text;
            contents.Add(new { role = turn.Role, parts = new[] { new { text } } });
        }
        return contents.ToArray();
    }

}
