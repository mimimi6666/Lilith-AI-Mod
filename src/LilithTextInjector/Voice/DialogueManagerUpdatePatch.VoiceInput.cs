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
    private static void HandleVoiceInput(DialogueManager manager)
    {
        var voiceInputKey = Plugin.VoiceInputKey.Value;
        var voiceInputKeyDown = IsKeyCurrentlyDown(voiceInputKey);
        var voiceInputKeyPressed = voiceInputKeyDown && !_voiceInputKeyWasDown;
        var voiceInputKeyReleased = !voiceInputKeyDown && _voiceInputKeyWasDown;
        _voiceInputKeyWasDown = voiceInputKeyDown;

        if (!Plugin.VoiceInputEnabled.Value || _keyBindingTarget != 0)
            return;
        if (voiceInputKeyPressed)
        {
            if (_microphoneRecording || _transcriptionInFlight || _requestInFlight)
            {
                manager.ForceSay(ApiKeyText("先等我一下……", "先等我一下……", "少し待って……", "Wait for me a moment…"), string.Empty, 4f);
                return;
            }
            var activeVoiceProvider = NormalizeAiProvider(Plugin.AiProvider.Value);
            var voiceApiKey = string.Equals(activeVoiceProvider, "Qwen", StringComparison.Ordinal)
                ? Plugin.QwenApiKey.Value
                : Plugin.GeminiApiKey.Value;
            if (string.IsNullOrWhiteSpace(voiceApiKey))
            {
                var providerName = string.Equals(activeVoiceProvider, "Qwen", StringComparison.Ordinal) ? "Qwen" : "Gemini";
                manager.ForceSay(ApiKeyText($"還沒有設定 {providerName} API Key。", $"还没有设置 {providerName} API Key。", $"{providerName} APIキーがまだ設定されていないよ。", $"The {providerName} API key has not been set yet."), string.Empty, 6f);
                return;
            }
            try
            {
                var maxSeconds = Math.Clamp(Plugin.VoiceInputMaxSeconds.Value, 5, 90);
                string deviceName;
                using (var enumerator = new MMDeviceEnumerator())
                    deviceName = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).FriendlyName;
                _wasapiCapture = new WasapiCapture();
                _wasapiStream = new MemoryStream();
                _wasapiWriter = new WaveFileWriter(_wasapiStream, _wasapiCapture.WaveFormat);
                _voiceCaptureFormat = _wasapiCapture.WaveFormat;
                if (string.Equals(activeVoiceProvider, "Qwen", StringComparison.Ordinal) && UseParaformerRealtime())
                    StartParaformerRealtimeSession();
                _wasapiCapture.DataAvailable += OnWasapiDataAvailable;
                _wasapiCapture.StartRecording();
                _microphoneRecording = true;
                _microphoneStartedAt = Time.unscaledTime;
                manager.ForceSay(ApiKeyText("正在聽……", "正在听……", "聞いているよ……", "Listening…"), string.Empty, maxSeconds + 2f);
                Plugin.PluginLog.LogInfo($"F6 WASAPI recording started with Windows default input '{deviceName}' ({_wasapiCapture.WaveFormat}).");
            }
            catch (Exception exception)
            {
                _microphoneRecording = false;
                CleanupWasapiCapture();
                Plugin.PluginLog.LogWarning($"Could not start microphone recording: {exception.Message}");
                manager.ForceSay(ApiKeyText("沒有收到麥克風的聲音。", "没有收到麦克风的声音。", "マイクの音が届いていないみたい。", "I didn't receive any microphone audio."), string.Empty, 6f);
            }
        }

        var reachedLimit = _microphoneRecording
            && Time.unscaledTime - _microphoneStartedAt >= Math.Clamp(Plugin.VoiceInputMaxSeconds.Value, 5, 90) - 0.1f;
        if (_microphoneRecording && (voiceInputKeyReleased || reachedLimit))
            StopVoiceInputRecording(reachedLimit);
    }

    private static void StopVoiceInputRecording(bool reachedLimit)
    {
        _microphoneRecording = false;
        try
        {
            if (_wasapiCapture == null || _wasapiStream == null || _wasapiWriter == null)
                return;
            _wasapiCapture.StopRecording();
            _wasapiCapture.DataAvailable -= OnWasapiDataAvailable;
            byte[] wav;
            lock (WasapiLock)
            {
                _wasapiWriter.Flush();
                _wasapiWriter.Dispose();
                _wasapiWriter = null;
                wav = _wasapiStream.ToArray();
            }
            _wasapiCapture.Dispose();
            _wasapiCapture = null;
            _wasapiStream.Dispose();
            _wasapiStream = null;
            if (wav.Length < 2048)
            {
                PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有收到聲音，再試一次吧。", "刚才没有收到声音，再试一次吧。", "今の声は届かなかったみたい。もう一度試してみて。", "I didn't receive that audio. Please try again."));
                return;
            }
            var elapsed = Math.Max(0f, Time.unscaledTime - _microphoneStartedAt);
            var qwenVoiceInput = string.Equals(NormalizeAiProvider(Plugin.AiProvider.Value), "Qwen", StringComparison.Ordinal);
            var prepared = NormalizeVoiceWav(wav, qwenVoiceInput ? 16000 : 24000);
            if (elapsed < 0.3f || (prepared.Measured && prepared.Peak < 0.0005f && prepared.Rms < 0.00005d))
            {
                PendingTranscriptionErrors.Enqueue(ApiKeyText(
                    "剛才沒有收到聲音，再試一次吧。",
                    "刚才没有收到声音，再试一次吧。",
                    "今の録音には声が入っていなかったみたい。もう一度試してみて。",
                    "That recording did not contain audible speech. Please try again."));
                Plugin.PluginLog.LogInfo($"Skipped silent voice transcription locally (elapsed={elapsed:F1}s, RMS={prepared.Rms:F6}, peak={prepared.Peak:F6}).");
                CancelParaformerRealtimeSession();
                return;
            }
            _transcriptionInFlight = true;
            if (qwenVoiceInput && _paraformerSessionTask != null)
            {
                _paraformerFinishRequested = true;
                _ = CompleteParaformerOrFallbackAsync(_paraformerSessionTask, prepared.Wav, GetVoiceInputLanguageInstruction());
            }
            else
            {
                _ = RequestTranscriptionAsync(prepared.Wav, GetVoiceInputLanguageInstruction());
            }
            Plugin.PluginLog.LogInfo($"F6 WASAPI recording stopped after {elapsed:F1}s{(reachedLimit ? " (time limit reached)" : string.Empty)}; normalized {wav.Length} to {prepared.Wav.Length} WAV bytes for transcription.");
        }
        catch (Exception exception)
        {
            CleanupWasapiCapture();
            Plugin.PluginLog.LogWarning($"Could not finish microphone recording: {exception.Message}");
            PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有聽清楚，再試一次吧。", "刚才没有听清楚，再试一次吧。", "今の声はうまく聞き取れなかった。もう一度試してみて。", "I couldn't understand that recording. Please try again."));
        }
    }

    private static void OnWasapiDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (WasapiLock)
        {
            _wasapiWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }
        if (_paraformerSessionTask != null && _voiceCaptureFormat != null && args.BytesRecorded > 0)
        {
            var pcm = ConvertCaptureChunkToMonoPcm16(args.Buffer, args.BytesRecorded, _voiceCaptureFormat);
            if (pcm.Length > 0)
                ParaformerAudioChunks.Enqueue(pcm);
        }
    }

    private static void CleanupWasapiCapture()
    {
        try { _wasapiCapture?.StopRecording(); } catch { }
        try { _wasapiCapture?.Dispose(); } catch { }
        lock (WasapiLock)
        {
            try { _wasapiWriter?.Dispose(); } catch { }
            try { _wasapiStream?.Dispose(); } catch { }
            _wasapiWriter = null;
            _wasapiStream = null;
        }
        _wasapiCapture = null;
        _voiceCaptureFormat = null;
        CancelParaformerRealtimeSession();
    }

    private static void StartParaformerRealtimeSession()
    {
        CancelParaformerRealtimeSession();
        while (ParaformerAudioChunks.TryDequeue(out _)) { }
        _paraformerFinishRequested = false;
        _paraformerResampleAccumulator = 0d;
        _paraformerCancellation = new CancellationTokenSource();
        _paraformerSessionTask = RunParaformerRealtimeSessionAsync(_paraformerCancellation.Token);
        Plugin.PluginLog.LogInfo($"Starting {_paraformerSessionTask.GetType().Name} for {Plugin.QwenRealtimeAsrModel.Value.Trim()} while push-to-talk is held.");
    }

    private static bool UseParaformerRealtime() =>
        Plugin.QwenRealtimeAsrModel.Value.Trim().StartsWith("paraformer-realtime", StringComparison.OrdinalIgnoreCase);

    private static void CancelParaformerRealtimeSession()
    {
        try { _paraformerCancellation?.Cancel(); } catch { }
        try { _paraformerCancellation?.Dispose(); } catch { }
        _paraformerCancellation = null;
        _paraformerSessionTask = null;
        _paraformerFinishRequested = false;
        while (ParaformerAudioChunks.TryDequeue(out _)) { }
    }

    private static async Task<string> RunParaformerRealtimeSessionAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + Plugin.QwenApiKey.Value.Trim());
        socket.Options.SetRequestHeader("User-Agent", "Lilith-AI-Mod/0.2.1");
        var endpoint = BuildParaformerWebSocketUri();
        await socket.ConnectAsync(endpoint, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

        var taskId = Guid.NewGuid().ToString();
        var language = GetVoiceInputLanguageCode();
        var parameters = new Dictionary<string, object>
        {
            ["format"] = "pcm",
            ["sample_rate"] = 16000,
            ["disfluency_removal_enabled"] = false,
            ["punctuation_prediction_enabled"] = true,
            ["inverse_text_normalization_enabled"] = true,
            ["heartbeat"] = true
        };
        if (!string.IsNullOrWhiteSpace(language))
            parameters["language_hints"] = new[] { language };
        var runTask = new
        {
            header = new { action = "run-task", task_id = taskId, streaming = "duplex" },
            payload = new
            {
                task_group = "audio",
                task = "asr",
                function = "recognition",
                model = Plugin.QwenRealtimeAsrModel.Value.Trim(),
                parameters,
                input = new { }
            }
        };
        await SendWebSocketTextAsync(socket, JsonSerializer.Serialize(runTask), cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var startedMessage = await ReceiveWebSocketTextAsync(socket, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            using var startedDocument = JsonDocument.Parse(startedMessage);
            var header = startedDocument.RootElement.GetProperty("header");
            var eventName = header.GetProperty("event").GetString() ?? string.Empty;
            if (eventName == "task-started")
                break;
            if (eventName == "task-failed")
                throw new InvalidOperationException(GetParaformerFailure(header));
        }

        Plugin.PluginLog.LogInfo($"Paraformer real-time task started (task={taskId}); streaming microphone PCM.");
        var receiveTask = ReceiveParaformerTranscriptAsync(socket, cancellationToken);
        while (!_paraformerFinishRequested || !ParaformerAudioChunks.IsEmpty)
        {
            if (ParaformerAudioChunks.TryDequeue(out var audio))
            {
                for (var offset = 0; offset < audio.Length; offset += 8192)
                {
                    var count = Math.Min(8192, audio.Length - offset);
                    await socket.SendAsync(new ArraySegment<byte>(audio, offset, count), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await Task.Delay(8, cancellationToken).ConfigureAwait(false);
            }
        }

        var finishTask = new
        {
            header = new { action = "finish-task", task_id = taskId, streaming = "duplex" },
            payload = new { input = new { } }
        };
        await SendWebSocketTextAsync(socket, JsonSerializer.Serialize(finishTask), cancellationToken).ConfigureAwait(false);
        var transcript = await receiveTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", CancellationToken.None).ConfigureAwait(false);
        return transcript;
    }

    private static async Task<string> ReceiveParaformerTranscriptAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var finalSentences = new List<string>();
        var latestInterim = string.Empty;
        while (true)
        {
            var message = await ReceiveWebSocketTextAsync(socket, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(message);
            var header = document.RootElement.GetProperty("header");
            var eventName = header.GetProperty("event").GetString() ?? string.Empty;
            if (eventName == "task-failed")
                throw new InvalidOperationException(GetParaformerFailure(header));
            if (eventName == "task-finished")
                return CleanReply(finalSentences.Count > 0 ? string.Concat(finalSentences) : latestInterim);
            if (eventName != "result-generated"
                || !document.RootElement.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("output", out var output)
                || !output.TryGetProperty("sentence", out var sentence))
                continue;
            var text = sentence.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
            var sentenceEnd = sentence.TryGetProperty("sentence_end", out var endElement) && endElement.GetBoolean();
            if (sentenceEnd)
            {
                if (!string.IsNullOrWhiteSpace(text))
                    finalSentences.Add(text);
                latestInterim = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                latestInterim = text;
            }
        }
    }

    private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException($"Paraformer closed the connection ({socket.CloseStatus}: {socket.CloseStatusDescription}).");
            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static Task SendWebSocketTextAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(text);
        return socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static Uri BuildParaformerWebSocketUri()
    {
        var baseUri = new Uri(NormalizeQwenBaseUrl(Plugin.QwenBaseUrl.Value));
        var builder = new UriBuilder(baseUri)
        {
            Scheme = "wss",
            Port = -1,
            Path = "/api-ws/v1/inference",
            Query = string.Empty
        };
        return builder.Uri;
    }

    private static string GetParaformerFailure(JsonElement header)
    {
        var code = header.TryGetProperty("error_code", out var codeElement) ? codeElement.GetString() : null;
        var message = header.TryGetProperty("error_message", out var messageElement) ? messageElement.GetString() : null;
        return $"Paraformer task failed: {code ?? "unknown"}: {message ?? "no details"}";
    }

    private static async Task CompleteParaformerOrFallbackAsync(Task<string> sessionTask, byte[] fallbackWav, string languageInstruction)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var transcript = CleanReply(await sessionTask.ConfigureAwait(false));
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("Paraformer returned an empty transcript.");
            PendingTranscripts.Enqueue(transcript);
            Plugin.PluginLog.LogInfo($"Paraformer real-time transcription completed in {timer.Elapsed.TotalSeconds:F2}s after key release ({transcript.Length} chars; content hidden from log).");
            _transcriptionInFlight = false;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Paraformer real-time transcription failed; falling back to qwen3-asr-flash: {exception.Message}");
            await RequestQwenTranscriptionAsync(fallbackWav, languageInstruction).ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_paraformerSessionTask, sessionTask))
            {
                try { _paraformerCancellation?.Dispose(); } catch { }
                _paraformerCancellation = null;
                _paraformerSessionTask = null;
                _paraformerFinishRequested = false;
                _voiceCaptureFormat = null;
                while (ParaformerAudioChunks.TryDequeue(out _)) { }
            }
        }
    }

    private static byte[] ConvertCaptureChunkToMonoPcm16(byte[] buffer, int count, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frameSize = Math.Max(1, format.BlockAlign);
        var frames = count / frameSize;
        using var output = new MemoryStream(Math.Max(256, frames * 2 * 16000 / Math.Max(8000, format.SampleRate)));
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        for (var frame = 0; frame < frames; frame++)
        {
            double mixed = 0d;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = frame * frameSize + channel * bytesPerSample;
                float sample;
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                    sample = BitConverter.ToSingle(buffer, offset);
                else if (bytesPerSample == 2)
                    sample = BitConverter.ToInt16(buffer, offset) / 32768f;
                else if (bytesPerSample == 3)
                {
                    var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                    if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                    sample = value / 8388608f;
                }
                else if (bytesPerSample == 4)
                    sample = BitConverter.ToInt32(buffer, offset) / 2147483648f;
                else
                    sample = 0f;
                mixed += sample;
            }
            var mono = (float)(mixed / channels);
            _paraformerResampleAccumulator += 16000d;
            if (_paraformerResampleAccumulator < format.SampleRate)
                continue;
            _paraformerResampleAccumulator -= format.SampleRate;
            writer.Write((short)Math.Round(Math.Clamp(mono, -1f, 1f) * short.MaxValue));
        }
        writer.Flush();
        return output.ToArray();
    }

    private static byte[] EncodePcm16Wav(float[] samples, int channels, int sampleRate)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }

    private static (byte[] Wav, double Rms, float Peak, bool Measured) NormalizeVoiceWav(byte[] wav, int preferredSampleRate)
    {
        try
        {
            using var input = new MemoryStream(wav, false);
            using var reader = new WaveFileReader(input);
            var provider = reader.ToSampleProvider();
            var channels = Math.Max(1, provider.WaveFormat.Channels);
            var sourceRate = Math.Max(8000, provider.WaveFormat.SampleRate);
            var estimatedSamples = (int)Math.Min(int.MaxValue, Math.Max(4096L,
                reader.Length / Math.Max(1, reader.WaveFormat.BlockAlign) * channels));
            var samples = new float[estimatedSamples];
            var count = 0;
            while (count < samples.Length)
            {
                var read = provider.Read(samples, count, samples.Length - count);
                if (read <= 0)
                    break;
                count += read;
            }
            if (count < channels)
                return (wav, 0d, 0f, false);

            var frames = count / channels;
            var mono = new float[frames];
            double sumSquares = 0;
            float peak = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += samples[frame * channels + channel];
                var value = (float)(sum / channels);
                mono[frame] = value;
                sumSquares += value * value;
                peak = Math.Max(peak, Math.Abs(value));
            }

            var rms = Math.Sqrt(sumSquares / Math.Max(1, frames));
            var gain = peak > 0.001f ? Math.Min(3f, 0.88f / peak) : 1f;
            if (gain > 1.05f)
                for (var i = 0; i < mono.Length; i++)
                    mono[i] = Math.Clamp(mono[i] * gain, -1f, 1f);

            var targetRate = Math.Min(Math.Clamp(preferredSampleRate, 8000, 48000), sourceRate);
            float[] output;
            if (targetRate == sourceRate)
            {
                output = mono;
            }
            else
            {
                var outputFrames = Math.Max(1, (int)Math.Round(frames * (double)targetRate / sourceRate));
                output = new float[outputFrames];
                var ratio = sourceRate / (double)targetRate;
                for (var i = 0; i < outputFrames; i++)
                {
                    var position = i * ratio;
                    var left = Math.Min(frames - 1, (int)position);
                    var right = Math.Min(frames - 1, left + 1);
                    var fraction = (float)(position - left);
                    output[i] = mono[left] + (mono[right] - mono[left]) * fraction;
                }
            }

            Plugin.PluginLog.LogInfo($"Voice audio prepared as mono PCM16 {targetRate}Hz (source {sourceRate}Hz/{channels}ch, RMS {rms:F4}, peak {peak:F4}, gain {gain:F2}x).");
            return (EncodePcm16Wav(output, 1, targetRate), rms, peak, true);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Voice audio normalization failed; sending original recording: {exception.Message}");
            return (wav, 0d, 0f, false);
        }
    }

    private static string GetVoiceInputLanguageInstruction()
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Japanese. Transcribe as natural Japanese using Japanese script.";
            if (language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Simplified Chinese. Transcribe the speech in Simplified Chinese characters.";
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Traditional Chinese. Transcribe the speech in Traditional Chinese characters.";
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is English. Transcribe the speech in English.";
        }
        catch
        {
        }
        return "Detect whether the speech is Traditional Chinese, Japanese, or English, and transcribe it in the spoken language.";
    }

    private static async Task RequestTranscriptionAsync(byte[] wav, string languageInstruction)
    {
        if (string.Equals(NormalizeAiProvider(Plugin.AiProvider.Value), "Qwen", StringComparison.Ordinal))
        {
            await RequestQwenTranscriptionAsync(wav, languageInstruction).ConfigureAwait(false);
            return;
        }
        try
        {
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = $"Transcribe every spoken word in this entire recording verbatim from beginning to end. Do not summarize, shorten, answer, or omit later sentences, even when the speaker pauses or changes topic. Preserve all clauses and add natural punctuation. {languageInstruction} Return only the complete transcript, with no explanation, labels, quotation marks, or Markdown." },
                            new { inline_data = new { mime_type = "audio/wav", data = Convert.ToBase64String(wav) } }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0,
                    maxOutputTokens = 1024,
                    thinkingConfig = new { thinkingLevel = "MINIMAL" }
                }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini transcription HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var textPart))
                    builder.Append(textPart.GetString());
            var transcript = CleanReply(builder.ToString());
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("Gemini returned an empty transcript.");
            PendingTranscripts.Enqueue(transcript);
            Plugin.PluginLog.LogInfo($"Voice transcription completed ({transcript.Length} chars; content hidden from log).");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Voice transcription failed: {exception}");
            PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有聽清楚，再試一次吧。", "刚才没有听清楚，再试一次吧。", "今の声はうまく聞き取れなかった。もう一度試してみて。", "I couldn't understand that recording. Please try again."));
        }
        finally
        {
            _transcriptionInFlight = false;
        }
    }

    private static async Task RequestQwenTranscriptionAsync(byte[] wav, string languageInstruction)
    {
        try
        {
            var baseUrl = NormalizeQwenBaseUrl(Plugin.QwenBaseUrl.Value);
            var url = baseUrl + "/chat/completions";
            var language = GetVoiceInputLanguageCode();
            var asrOptions = new Dictionary<string, object> { ["enable_itn"] = false };
            if (!string.IsNullOrWhiteSpace(language))
                asrOptions["language"] = language;
            var payload = new Dictionary<string, object>
            {
                ["model"] = UseParaformerRealtime() ? "qwen3-asr-flash" : Plugin.QwenAsrModel.Value.Trim(),
                ["messages"] = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "input_audio",
                                input_audio = new { data = "data:audio/wav;base64," + Convert.ToBase64String(wav) }
                            }
                        }
                    }
                },
                ["stream"] = false,
                ["asr_options"] = asrOptions
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Plugin.QwenApiKey.Value.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Qwen transcription HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var transcript = CleanReply(document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("Qwen returned an empty transcript.");
            PendingTranscripts.Enqueue(transcript);
            var emotion = "unknown";
            var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
            if (message.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Array)
            {
                foreach (var annotation in annotations.EnumerateArray())
                    if (annotation.TryGetProperty("emotion", out var emotionElement))
                        emotion = emotionElement.GetString() ?? emotion;
            }
            Plugin.PluginLog.LogInfo($"Qwen voice transcription completed ({transcript.Length} chars; detectedEmotion={emotion}; content hidden from log).");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Qwen voice transcription failed: {exception}");
            PendingTranscriptionErrors.Enqueue(IsQwenAccountUnavailable(exception)
                ? QwenAccountUnavailableReply()
                : ApiKeyText("剛才沒有聽清楚，再試一次吧。", "刚才没有听清楚，再试一次吧。", "今の声はうまく聞き取れなかった。もう一度試してみて。", "I couldn't understand that recording. Please try again."));
        }
        finally
        {
            _transcriptionInFlight = false;
        }
    }

    private static string GetVoiceInputLanguageCode()
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        }
        catch
        {
        }
        return string.Empty;
    }

}
