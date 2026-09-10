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
    private static async Task RequestSpeechAsync(string text, NativeReaction? reaction = null, VoiceStyle poseStyle = VoiceStyle.Calm, bool? japaneseVoiceMode = null)
    {
        var generationTimer = Stopwatch.StartNew();
        try
        {
            var useJapanese = japaneseVoiceMode ?? IsJapaneseVoiceMode();
            var speechText = PrepareTextForSpeech(text);
            if (speechText.Length == 0)
                return;
            var referencePath = useJapanese ? Plugin.JapaneseVoiceReferencePath.Value.Trim() : Plugin.VoiceReferencePath.Value.Trim();
            var promptText = useJapanese
                ? "これは儀式でもあるの。君に私の存在を感じてもらうための儀式ね。"
                : "你的選擇創造了我，所以我的存在本身就是你的善意。";
            var effectiveStyle = reaction?.Style ?? poseStyle;
            var auxiliaryReferences = useJapanese ? effectiveStyle switch
            {
                VoiceStyle.Excited => new[] { Plugin.JapaneseExcitedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Wronged => new[] { Plugin.JapaneseWrongedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Sleepy => new[] { Plugin.JapaneseSleepyVoiceReferencePath.Value.Trim() },
                _ => new[] { Plugin.JapaneseCalmAuxVoiceReferencePath.Value.Trim() }
            } : effectiveStyle switch
            {
                VoiceStyle.Excited => new[] { Plugin.ExcitedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Wronged => new[] { Plugin.WrongedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Sleepy => new[] { Plugin.SleepyVoiceReferencePath.Value.Trim() },
                _ => Array.Empty<string>()
            };
            auxiliaryReferences = Array.FindAll(auxiliaryReferences, File.Exists);
            if (!File.Exists(referencePath))
            {
                Plugin.PluginLog.LogWarning($"Voice reference was not found: {referencePath}");
                return;
            }

            var payload = new
            {
                text = speechText,
                text_lang = useJapanese ? "ja" : "zh",
                ref_audio_path = referencePath,
                aux_ref_audio_paths = auxiliaryReferences,
                prompt_lang = useJapanese ? "ja" : "zh",
                prompt_text = promptText,
                text_split_method = "cut0",
                batch_size = 1,
                media_type = "wav",
                streaming_mode = false,
                seed = 42
            };
            var endpoint = useJapanese ? Plugin.JapaneseVoiceEndpoint.Value.Trim() : Plugin.VoiceEndpoint.Value.Trim();
            var payloadJson = JsonSerializer.Serialize(payload);
            var localEndpoint = IsLocalVoiceEndpoint(endpoint);
            var maximumAttempts = localEndpoint && Plugin.VoiceAutoStartLocalService.Value ? 7 : 1;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                    using var response = await Http.SendAsync(request).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException($"TTS HTTP {(int)response.StatusCode}: {error}");
                    }
                    var speech = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    PendingVoiceAudio.Enqueue(new VoiceSequence { Reaction = reaction?.Audio, Speech = speech });
                    Plugin.PluginLog.LogInfo($"Local voice generation completed in {generationTimer.Elapsed.TotalSeconds:F2}s ({speech.Length} bytes)." );
                    return;
                }
                catch (HttpRequestException exception) when (attempt < maximumAttempts)
                {
                    Plugin.PluginLog.LogInfo($"Local voice service is still starting; retrying in 5 seconds ({attempt}/{maximumAttempts}): {exception.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Voice generation failed; text chat continues: {exception.Message}");
        }
    }

    private static bool IsLocalVoiceEndpoint(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    private static string PrepareTextForSpeech(string text)
    {
        var cleaned = Regex.Replace(text, @"\[([^\]]+)\]\(https?://[^\s\)]+\)", "$1", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"https?://\S+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(?:來源|资料来源|資料來源|出典|Sources?)\s*[:：]\s*$", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n").Trim();
        if (!string.Equals(cleaned, text, StringComparison.Ordinal))
            Plugin.PluginLog.LogInfo($"Removed web citation markup before TTS ({text.Length} -> {cleaned.Length} chars).");
        return cleaned;
    }

    private static void SetVoicePitch(float pitch)
    {
        var manager = AudioManager.instance;
        if (manager != null && manager.source_Voice != null)
            manager.source_Voice.pitch = pitch;
    }

    private static AudioClip PlayWav(byte[] wav, string label)
    {
        var clip = CreateAudioClipFromWav(wav);
        AudioManager.PlayVoice(clip, false, true);
        Plugin.PluginLog.LogInfo($"Playing {label} ({wav.Length} bytes, {clip.length:0.00}s).");
        return clip;
    }

    private static AudioClip CreateAudioClipFromWav(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("TTS response is not a WAV file.");

        var offset = 12;
        ushort format = 0;
        ushort channels = 0;
        var sampleRate = 0;
        ushort bits = 0;
        var dataOffset = -1;
        var dataLength = 0;
        while (offset + 8 <= wav.Length)
        {
            var chunk = Encoding.ASCII.GetString(wav, offset, 4);
            var length = BitConverter.ToInt32(wav, offset + 4);
            var body = offset + 8;
            if (length < 0 || body + length > wav.Length)
                throw new InvalidDataException("Invalid WAV chunk length.");
            if (chunk == "fmt " && length >= 16)
            {
                format = BitConverter.ToUInt16(wav, body);
                channels = BitConverter.ToUInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToUInt16(wav, body + 14);
            }
            else if (chunk == "data")
            {
                dataOffset = body;
                dataLength = length;
                break;
            }
            offset = body + length + (length & 1);
        }
        if (dataOffset < 0 || channels == 0 || sampleRate <= 0)
            throw new InvalidDataException("WAV format or data chunk is missing.");

        float[] samples;
        if (format == 1 && bits == 16)
        {
            samples = new float[dataLength / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;
        }
        else if (format == 3 && bits == 32)
        {
            samples = new float[dataLength / 4];
            Buffer.BlockCopy(wav, dataOffset, samples, 0, samples.Length * 4);
        }
        else
        {
            throw new InvalidDataException($"Unsupported WAV format={format}, bits={bits}.");
        }

        var frameCount = samples.Length / channels;
        var clip = AudioClip.Create("LilithAiVoice", frameCount, channels, sampleRate, false);
        if (!clip.SetData(samples, 0))
            throw new InvalidOperationException("Unity rejected generated audio samples.");
        return clip;
    }

}
