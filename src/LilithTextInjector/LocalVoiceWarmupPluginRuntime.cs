using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static readonly LocalVoiceWarmup LocalVoicePreparation = new();
    private static readonly HttpClient LocalVoicePreparationHttp = new(
        new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(90) };
    private static float _nextLocalVoicePreparationCheck;
    private static long _silentPlaybackGeneration;
    private static SilentPreparationPlayback? _pendingSilentPlayback;
    private static SilentPreparationPlayback? _activeSilentPlayback;
    private static SilentVoiceWarmupPolicy? _silentPlaybackPolicy;
    private static UnityEngine.AudioClip? _silentPlaybackClip;

    private sealed class SilentPreparationPlayback
    {
        private byte[]? _audio;
        internal readonly long Generation;
        internal readonly bool Japanese;
        internal readonly CancellationToken Cancellation;
        internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal SilentPreparationPlayback(byte[] audio, long generation, bool japanese, CancellationToken cancellation)
        {
            _audio = audio; Generation = generation; Japanese = japanese; Cancellation = cancellation;
        }
        internal byte[]? TakeAudio() => Interlocked.Exchange(ref _audio, null);
        internal void Discard()
        {
            var audio = TakeAudio();
            if (audio != null) Array.Clear(audio, 0, audio.Length);
            Completion.TrySetResult(false);
        }
    }

    // Main-thread only. Cancellation never stops a clip installed by another owner.
    private static void CancelSilentVoicePlayback()
    {
        Interlocked.Increment(ref _silentPlaybackGeneration);
        Interlocked.Exchange(ref _pendingSilentPlayback, null)?.Discard();
        FinishSilentVoicePlayback(false);
    }

    private static void FinishSilentVoicePlayback(bool completed)
    {
        var entry = _activeSilentPlayback;
        var clip = _silentPlaybackClip;
        _activeSilentPlayback = null;
        _silentPlaybackClip = null;
        _silentPlaybackPolicy = null;
        try
        {
            var source = AudioManager.instance?.source_Voice;
            if (clip != null && source != null && ReferenceEquals(source.clip, clip))
            {
                AudioManager.StopVoice();
                source.clip = null;
            }
        }
        catch (Exception exception)
        {
            completed = false;
            Plugin.PluginLog.LogWarning("Local TTS silent playback cleanup failed; fault=" + exception.GetType().Name);
        }
        finally
        {
            try { if (clip != null) UnityEngine.Object.Destroy(clip); }
            catch (Exception) { completed = false; }
            entry?.Completion.TrySetResult(completed);
            entry?.Discard();
        }
    }

    private static void UpdateSilentVoicePlayback()
    {
        try
        {
            var now = (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
            if (_activeSilentPlayback == null)
            {
                _activeSilentPlayback = Interlocked.Exchange(ref _pendingSilentPlayback, null);
                if (_activeSilentPlayback == null) return;
                _silentPlaybackPolicy = new SilentVoiceWarmupPolicy(now);
            }
            var entry = _activeSilentPlayback;
            var source = AudioManager.instance?.source_Voice;
            var cancelled = entry.Cancellation.IsCancellationRequested || entry.Completion.Task.IsCompleted
                || entry.Generation != Interlocked.Read(ref _silentPlaybackGeneration)
                || entry.Japanese != IsJapaneseVoiceMode();
            // A foreground request waits for preparation: release it rather than waiting for it here.
            var demand = _requestInFlight || HasActivePublishedPlayerReplySpeech() || !PendingVoiceAudio.IsEmpty;
            var decision = _silentPlaybackPolicy!.Tick(now, cancelled, demand,
                source != null, source != null && source.isPlaying,
                source != null && ReferenceEquals(source.clip, _silentPlaybackClip));
            if (decision == SilentVoiceWarmupDecision.Start)
            {
                if (source == null || !float.IsFinite(source.pitch) || source.pitch < 0.5f || source.pitch > 2f
                    || source.loop)
                {
                    FinishSilentVoicePlayback(false);
                    return;
                }
                var audio = entry.TakeAudio();
                if (audio == null || entry.Cancellation.IsCancellationRequested)
                {
                    if (audio != null) Array.Clear(audio, 0, audio.Length);
                    FinishSilentVoicePlayback(false);
                    return;
                }
                try
                {
                    _silentPlaybackClip = PlayWav(audio, "silent startup warmup");
                    _silentPlaybackPolicy.Started(now, (long)Math.Ceiling(_silentPlaybackClip.length * 1000 / source.pitch));
                    Plugin.PluginLog.LogInfo("Local TTS silent playback started; zero_samples=true.");
                }
                finally { Array.Clear(audio, 0, audio.Length); }
            }
            else if (decision != SilentVoiceWarmupDecision.Wait)
                FinishSilentVoicePlayback(decision == SilentVoiceWarmupDecision.Complete);
        }
        catch (Exception exception)
        {
            FinishSilentVoicePlayback(false);
            Plugin.PluginLog.LogWarning("Local TTS silent playback aborted; fault=" + exception.GetType().Name);
        }
    }

    // Called on the Unity thread to freeze configuration before background work.
    private static void UpdateLocalVoicePreparation()
    {
        if (!Plugin.VoiceEnabled.Value || !Plugin.VoiceAutoStartLocalService.Value
            || _voiceHostProcess == null)
        {
            LocalVoicePreparation.Reset();
            CancelSilentVoicePlayback();
            return;
        }
        UpdateSilentVoicePlayback();
        if (UnityEngine.Time.unscaledTime < _nextLocalVoicePreparationCheck) return;
        _nextLocalVoicePreparationCheck = UnityEngine.Time.unscaledTime + 1f;
        try
        {
            if (_voiceHostProcess.HasExited) { LocalVoicePreparation.Reset(); CancelSilentVoicePlayback(); return; }
            var japanese = IsJapaneseVoiceMode();
            if (_voiceHostJapaneseMode != japanese
                || !TryBuildD043PlayerReplyVoiceConfiguration(VoiceStyle.Calm,
                    japanese ? LilithGameBridgeSpeechLocale.Japanese : LilithGameBridgeSpeechLocale.Chinese,
                    requireReferenceFiles: true, out var configuration)
                || configuration == null
                || !LocalVoiceWarmup.IsBundledEndpoint(configuration.Endpoint, japanese))
            {
                LocalVoicePreparation.Reset();
                CancelSilentVoicePlayback();
                return;
            }
            var epoch = _voiceHostProcess.Id + ":" + _voiceHostProcess.StartTime.ToUniversalTime().Ticks;
            var generation = Interlocked.Read(ref _silentPlaybackGeneration);
            _ = LocalVoicePreparation.Start(epoch, configuration.Endpoint,
                token => PrepareLocalVoiceAsync(configuration, generation, token), TimeSpan.FromSeconds(185));
        }
        catch (Exception)
        {
            LocalVoicePreparation.Reset();
            CancelSilentVoicePlayback();
        }
    }

    private static async Task PrepareLocalVoiceAsync(
        D043PlayerReplyVoiceConfiguration configuration, long generation, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        long portReadyMs = 0;
        byte[]? wav = null;
        Plugin.PluginLog.LogInfo("Local TTS preparation started; fixed probe with zero-sample playback.");
        try
        {
            await WaitForD043LocalVoiceReadinessAsync(configuration.Endpoint, cancellationToken)
                .ConfigureAwait(false);
            portReadyMs = timer.ElapsedMilliseconds;
            var payload = new
            {
                text = configuration.UseJapanese ? "こんにちは。" : "你好。",
                text_lang = configuration.UseJapanese ? "ja" : "zh",
                ref_audio_path = configuration.ReferencePath,
                aux_ref_audio_paths = configuration.AuxiliaryReferencePaths,
                prompt_lang = configuration.UseJapanese ? "ja" : "zh",
                prompt_text = configuration.PromptText,
                text_split_method = "cut0", batch_size = 1,
                media_type = "wav", streaming_mode = false, seed = 42
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.Endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await LocalVoicePreparationHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            wav = await ReadBoundedD043WavBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (D043BoundedWavAdmission.TryAdmit(wav, D043WavAdmissionCaps.V1).Descriptor == null)
                throw new InvalidDataException("Preparation returned an invalid WAV.");
            cancellationToken.ThrowIfCancellationRequested();
            var synthesisMs = timer.ElapsedMilliseconds - portReadyMs;
            var entry = new SilentPreparationPlayback(SilentVoiceWarmupPolicy.CreateSilentWav(wav),
                generation, configuration.UseJapanese, cancellationToken);
            Interlocked.Exchange(ref _pendingSilentPlayback, entry)?.Discard();
            using var registration = cancellationToken.Register(entry.Discard);
            bool played;
            try { played = await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            finally
            {
                Interlocked.CompareExchange(ref _pendingSilentPlayback, null, entry);
                entry.Discard();
            }
            if (!played) throw new OperationCanceledException("Silent playback skipped or interrupted.");
            Plugin.PluginLog.LogInfo("Local TTS preparation probe passed; port_wait_ms=" + portReadyMs
                + "; synthesis_ms=" + synthesisMs + "; total_ms=" + timer.ElapsedMilliseconds
                + "; wav_bytes=" + wav.Length + "; playback=true; zero_samples=true.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning("Local TTS preparation did not complete; fault="
                + exception.GetType().Name + "; elapsed_ms=" + timer.ElapsedMilliseconds
                + "; no automatic probe retry; normal speech remains available.");
            throw;
        }
        finally
        {
            if (wav != null) Array.Clear(wav, 0, wav.Length);
        }
    }
}
