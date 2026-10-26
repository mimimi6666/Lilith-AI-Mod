using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;
using UnityEngine;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private const long D043PlayerReplySpeechLifetimeMicroseconds =
        300_000_000;
    private static readonly TimeSpan D043LocalVoiceReadinessTimeout =
        TimeSpan.FromSeconds(100);
    private static readonly string D043PlayerReplySpeechProcessEpochId =
        "d043-player-reply-" + Guid.NewGuid().ToString("N");
    private static readonly D043AdaptiveTtsProcessEstimator
        D043PlayerReplySpeechEstimator = new(
            D043PlayerReplySpeechProcessEpochId);
    private static readonly ConcurrentQueue<
        D043PlayerReplySpeechSynthesisCompletion>
        D043PlayerReplySpeechSynthesisCompletions = new();
    private static ActiveD043PlayerReplySpeech? _activeD043PlayerReplySpeech;
    private static long _d043PlayerReplyPublicationSequence;
    private static long _d043PlayerReplyLanguageGeneration = 1;
    private static bool _d043PlayerReplyLanguageObserved;
    private static bool _d043PlayerReplyObservedJapanese;

    private sealed class D043PlayerReplyVoiceConfiguration
    {
        internal bool UseJapanese { get; }
        internal VoiceStyle Style { get; }
        internal string Endpoint { get; }
        internal string ReferencePath { get; }
        internal string[] AuxiliaryReferencePaths { get; }
        internal string PromptText { get; }
        internal string Fingerprint { get; }
        internal D043AdaptiveTtsVoiceProfileIdentity VoiceProfile { get; }

        internal D043PlayerReplyVoiceConfiguration(
            bool useJapanese,
            VoiceStyle style,
            string endpoint,
            string referencePath,
            string[] auxiliaryReferencePaths,
            string promptText,
            string fingerprint,
            D043AdaptiveTtsVoiceProfileIdentity voiceProfile)
        {
            UseJapanese = useJapanese;
            Style = style;
            Endpoint = endpoint;
            ReferencePath = referencePath;
            AuxiliaryReferencePaths = auxiliaryReferencePaths;
            PromptText = promptText;
            Fingerprint = fingerprint;
            VoiceProfile = voiceProfile;
        }
    }

    private sealed class ActiveD043PlayerReplySpeech
    {
        internal D043PlayerReplySpeechTurn Turn { get; }
        internal D043PlayerReplyVoiceConfiguration Configuration { get; }
        internal long RuntimeGeneration { get; }
        internal CancellationTokenSource? SynthesisCancellation { get; set; }
        internal int SynthesisSegmentIndex { get; set; } = -1;
        internal string SynthesisCommandFingerprint { get; set; } =
            string.Empty;
        internal int PlaybackSegmentIndex { get; set; } = -1;
        internal string PlaybackCommandFingerprint { get; set; } =
            string.Empty;
        internal AudioClip? PlaybackClip { get; set; }
        internal float PlaybackStartedAt { get; set; } = -1f;
        internal float PlaybackExpectedEndAt { get; set; } = -1f;
        internal float PlaybackHardEndAt { get; set; } = -1f;

        internal ActiveD043PlayerReplySpeech(
            D043PlayerReplySpeechTurn turn,
            D043PlayerReplyVoiceConfiguration configuration,
            long runtimeGeneration)
        {
            Turn = turn;
            Configuration = configuration;
            RuntimeGeneration = runtimeGeneration;
        }
    }

    private sealed class D043PlayerReplySpeechSynthesisCompletion
    {
        internal string AuthorityFingerprint { get; }
        internal int SegmentIndex { get; }
        internal string CommandFingerprint { get; }
        internal long StartedAtMicroseconds { get; }
        internal long CompletedAtMicroseconds { get; }
        internal byte[]? EncodedWav { get; }
        internal string FaultType { get; }
        internal CancellationTokenSource Cancellation { get; }

        internal D043PlayerReplySpeechSynthesisCompletion(
            string authorityFingerprint,
            int segmentIndex,
            string commandFingerprint,
            long startedAtMicroseconds,
            long completedAtMicroseconds,
            byte[]? encodedWav,
            string faultType,
            CancellationTokenSource cancellation)
        {
            AuthorityFingerprint = authorityFingerprint;
            SegmentIndex = segmentIndex;
            CommandFingerprint = commandFingerprint;
            StartedAtMicroseconds = startedAtMicroseconds;
            CompletedAtMicroseconds = completedAtMicroseconds;
            EncodedWav = encodedWav;
            FaultType = faultType;
            Cancellation = cancellation;
        }
    }

    private static void StartPublishedPlayerReplySpeech(
        PendingPlayerTurnReply reply,
        long publicationRuntimeGeneration,
        long languagePublicationSequence)
    {
        if (reply.PublishedPlayerReplySpeechText.Length == 0
            || reply.PublicationKind
                != PlayerTurnPublicationKind.AssistantReply
            || !Plugin.VoiceEnabled.Value)
        {
            return;
        }

        try
        {
            var publishedText = reply.PublishedPlayerReplySpeechText;
            var speechText = PrepareTextForSpeech(publishedText);
            if (speechText.Length == 0)
                return;
            var frozenSpeechLocale =
                reply.PublishedPlayerReplySpeechLocale;
            if (!frozenSpeechLocale.HasValue
                || !Enum.IsDefined(frozenSpeechLocale.Value))
            {
                Plugin.PluginLog.LogWarning(
                    "Published F6/F7 reply speech had no valid frozen "
                    + "locale; text chat continues.");
                return;
            }
            var frozenUseJapanese = frozenSpeechLocale.Value
                == LilithGameBridgeSpeechLocale.Japanese;
            if (frozenUseJapanese != IsJapaneseVoiceMode())
            {
                CancelPublishedPlayerReplySpeech(
                    D043SpeechCancellationReason.LanguageGenerationChanged);
                Plugin.PluginLog.LogInfo(
                    "Retired published F6/F7 reply speech because the voice "
                    + "locale changed after turn submission; text chat "
                    + "continues.");
                return;
            }
            if (!TryBuildD043PlayerReplyVoiceConfiguration(
                    reply.PublishedPlayerReplyVoiceStyle,
                    frozenSpeechLocale.Value,
                    requireReferenceFiles: true,
                    out var configuration)
                || configuration == null)
            {
                Plugin.PluginLog.LogWarning(
                    "Published F6/F7 reply could not enter segmented speech "
                    + "because its selected voice references are unavailable; "
                    + "text chat continues.");
                return;
            }

            CancelPublishedPlayerReplySpeech(
                D043SpeechCancellationReason.PublicationRetired);
            var publicationSequence = Interlocked.Increment(
                ref _d043PlayerReplyPublicationSequence);
            if (publicationSequence <= 0)
                throw new InvalidOperationException(
                    "The player-reply speech publication sequence overflowed.");
            ObserveD043PlayerReplyLanguage(configuration.UseJapanese);
            var language = configuration.UseJapanese
                ? D043AdaptiveTtsLanguage.Japanese
                : D043AdaptiveTtsLanguage.Chinese;
            var publication = new D043AdaptiveTtsPublicationAuthority(
                "game-hermes-language-publication-"
                    + languagePublicationSequence.ToString(
                        CultureInfo.InvariantCulture),
                publicationSequence,
                D043PlayerReplySpeechProcessEpochId,
                publicationRuntimeGeneration,
                D043AdaptiveTtsFingerprint.Text(publishedText),
                D043AdaptiveTtsFingerprint.Text(speechText),
                language,
                Stopwatch.GetTimestamp());
            var committedAuthority =
                new D043PlayerReplySpeechCommittedAuthority(
                    publication,
                    D043AdaptiveTtsFingerprint.Canonical(
                        "D043.StageB.GameHermes.OrdinaryPlayerReply.V1",
                        configuration.UseJapanese ? reply.PublishedPlayerReplySpeechEmotion : string.Empty),
                    D043AdaptiveTtsFingerprint.Canonical(
                        reply.Operation.ProcessEpochId,
                        reply.Operation.OperationId,
                        reply.Operation.OperationGeneration,
                        reply.Operation.RuntimeGeneration,
                        reply.Operation.InputSource),
                    _d043PlayerReplyLanguageGeneration);
            var creation = D043PlayerReplySpeechTurn.TryCreate(
                committedAuthority,
                speechText,
                configuration.VoiceProfile,
                D043PlayerReplySpeechEstimator,
                configuration.UseJapanese ? reply.PublishedPlayerReplySpeechEmotion : string.Empty);
            if (!creation.Created || creation.Turn == null)
            {
                Plugin.PluginLog.LogWarning(
                    "Published F6/F7 reply was rejected by segmented speech "
                    + "admission: "
                    + creation.Admission
                    + "; text chat continues.");
                return;
            }

            var active = new ActiveD043PlayerReplySpeech(
                creation.Turn,
                configuration,
                publicationRuntimeGeneration);
            _activeD043PlayerReplySpeech = active;
            var startedAt = D043PlayerReplyMonotonicMicroseconds();
            var transition = active.Turn.Start(
                startedAt,
                D043PlayerReplySpeechLifetimeMicroseconds);
            ApplyD043PlayerReplySpeechEffects(active, transition);
            Plugin.PluginLog.LogInfo(
                "Started publication-owned F6/F7 segmented speech: segments="
                + active.Turn.SegmentCount
                + ", language="
                + (configuration.UseJapanese ? "ja" : "zh")
                + ".");
        }
        catch (Exception exception)
        {
            CancelPublishedPlayerReplySpeech(
                D043SpeechCancellationReason.PublicationRetired);
            Plugin.PluginLog.LogWarning(
                "Published F6/F7 segmented speech could not start; text "
                + "chat continues: "
                + exception.GetType().Name
                + ".");
        }
    }

    private static void UpdatePublishedPlayerReplySpeech()
    {
        try
        {
            var useJapanese = IsJapaneseVoiceMode();
            ObserveD043PlayerReplyLanguage(useJapanese);
            var active = _activeD043PlayerReplySpeech;
            if (active != null)
            {
                if (!Plugin.VoiceEnabled.Value)
                {
                    CancelPublishedPlayerReplySpeech(
                        D043SpeechCancellationReason.VoiceDisabled);
                }
                else if (active.Configuration.UseJapanese != useJapanese)
                {
                    CancelPublishedPlayerReplySpeech(
                        D043SpeechCancellationReason
                            .LanguageGenerationChanged);
                }
                else if (active.RuntimeGeneration
                    != CurrentPlayerTurnRuntimeGeneration())
                {
                    CancelPublishedPlayerReplySpeech(
                        D043SpeechCancellationReason
                            .RuntimeGenerationChanged);
                }
                else if (!TryBuildD043PlayerReplyVoiceConfiguration(
                             active.Configuration.Style,
                             active.Configuration.UseJapanese
                                 ? LilithGameBridgeSpeechLocale.Japanese
                                 : LilithGameBridgeSpeechLocale.Chinese,
                             requireReferenceFiles: false,
                             out var currentConfiguration)
                    || currentConfiguration == null
                    || !string.Equals(
                        active.Configuration.Fingerprint,
                        currentConfiguration.Fingerprint,
                        StringComparison.Ordinal))
                {
                    CancelPublishedPlayerReplySpeech(
                        D043SpeechCancellationReason.ConfigurationChanged);
                }
            }

            ProcessD043PlayerReplySpeechSynthesisCompletions();
            CompleteD043PlayerReplyPlaybackWhenDue();
            active = _activeD043PlayerReplySpeech;
            if (active != null)
            {
                var advanced = active.Turn.Advance(
                    active.Turn.AuthorityFingerprint,
                    D043PlayerReplyMonotonicMicroseconds());
                ApplyD043PlayerReplySpeechEffects(active, advanced);
            }
        }
        catch (Exception exception)
        {
            CancelPublishedPlayerReplySpeech(
                D043SpeechCancellationReason.PublicationRetired);
            Plugin.PluginLog.LogWarning(
                "F6/F7 segmented speech update failed closed; text chat "
                + "continues: "
                + exception.GetType().Name
                + ".");
        }
    }

    internal static void NotifyNativeVoiceTakeover()
    {
        CancelMiniGameDialogue();
        if (HasActivePublishedPlayerReplySpeech())
        {
            Plugin.PluginLog.LogInfo(
                "Ignored lower-priority native voice takeover while F6/F7 "
                + "reply speech owns the shared channel.");
            return;
        }
        CancelPublishedPlayerReplySpeech(
            D043SpeechCancellationReason.NativeVoiceTakeover);
    }

    internal static bool HasActivePublishedPlayerReplySpeech()
        => _activeD043PlayerReplySpeech != null;

    private static void CancelPublishedPlayerReplySpeech(
        D043SpeechCancellationReason reason)
    {
        CancelMiniGameDialogue(cancelUnpublishedResult: reason == D043SpeechCancellationReason.UserRequested);
        if (reason != D043SpeechCancellationReason.NativeVoiceTakeover)
            CancelSilentVoicePlayback();
        var active = _activeD043PlayerReplySpeech;
        if (active == null)
            return;
        try
        {
            var transition = active.Turn.Cancel(
                active.Turn.AuthorityFingerprint,
                reason,
                D043PlayerReplyMonotonicMicroseconds());
            ApplyD043PlayerReplySpeechEffects(active, transition);
        }
        catch (Exception exception)
        {
            try { active.SynthesisCancellation?.Cancel(); } catch { }
            try
            {
                StopOwnedD043PlayerReplyPlaybackBestEffort(active);
            }
            finally
            {
                ClearD043PlayerReplyPlayback(active);
                if (ReferenceEquals(_activeD043PlayerReplySpeech, active))
                    _activeD043PlayerReplySpeech = null;
            }
            Plugin.PluginLog.LogWarning(
                "F6/F7 segmented speech cancellation failed closed: "
                + exception.GetType().Name
                + ".");
        }
    }

    private static void ObserveD043PlayerReplyLanguage(bool useJapanese)
    {
        if (!_d043PlayerReplyLanguageObserved)
        {
            _d043PlayerReplyLanguageObserved = true;
            _d043PlayerReplyObservedJapanese = useJapanese;
            return;
        }
        if (_d043PlayerReplyObservedJapanese == useJapanese)
            return;
        _d043PlayerReplyObservedJapanese = useJapanese;
        if (_d043PlayerReplyLanguageGeneration == long.MaxValue)
            throw new InvalidOperationException(
                "The player-reply speech language generation overflowed.");
        _d043PlayerReplyLanguageGeneration++;
    }

    private static bool TryBuildD043PlayerReplyVoiceConfiguration(
        VoiceStyle style,
        LilithGameBridgeSpeechLocale speechLocale,
        bool requireReferenceFiles,
        out D043PlayerReplyVoiceConfiguration? configuration)
    {
        configuration = null;
        if (!Enum.IsDefined(speechLocale))
            return false;
        var useJapanese = speechLocale
            == LilithGameBridgeSpeechLocale.Japanese;
        var endpoint = (useJapanese
            ? Plugin.JapaneseVoiceEndpoint.Value
            : Plugin.VoiceEndpoint.Value).Trim();
        var referencePath = (useJapanese
            ? Plugin.JapaneseVoiceReferencePath.Value
            : Plugin.VoiceReferencePath.Value).Trim();
        var promptText = useJapanese
            ? "これは儀式でもあるの。君に私の存在を感じてもらうための儀式ね。"
            : "你的選擇創造了我，所以我的存在本身就是你的善意。";
        var configuredAuxiliary = useJapanese
            ? style switch
            {
                VoiceStyle.Excited =>
                    new[] { Plugin.JapaneseExcitedVoiceReferencePath.Value },
                VoiceStyle.Wronged =>
                    new[] { Plugin.JapaneseWrongedVoiceReferencePath.Value },
                VoiceStyle.Sleepy =>
                    new[] { Plugin.JapaneseSleepyVoiceReferencePath.Value },
                _ => new[]
                {
                    Plugin.JapaneseCalmAuxVoiceReferencePath.Value
                }
            }
            : style switch
            {
                VoiceStyle.Excited =>
                    new[] { Plugin.ExcitedVoiceReferencePath.Value },
                VoiceStyle.Wronged =>
                    new[] { Plugin.WrongedVoiceReferencePath.Value },
                VoiceStyle.Sleepy =>
                    new[] { Plugin.SleepyVoiceReferencePath.Value },
                _ => Array.Empty<string>()
            };
        var auxiliary = new List<string>();
        foreach (var path in configuredAuxiliary)
        {
            var trimmed = path.Trim();
            if (trimmed.Length > 0 && File.Exists(trimmed))
                auxiliary.Add(trimmed);
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)
            || referencePath.Length == 0
            || (requireReferenceFiles && !File.Exists(referencePath)))
        {
            return false;
        }

        var endpointFingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.StageB.Endpoint.V1",
            endpoint);
        var referenceFingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.StageB.PrimaryReference.V1",
            referencePath);
        var auxiliaryFingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.StageB.AuxiliaryReferences.V1",
            string.Join("\n", auxiliary));
        var promptFingerprint = D043AdaptiveTtsFingerprint.Text(promptText);
        var styleFingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.StageB.Style.V1",
            style);
        var voiceProfile = new D043AdaptiveTtsVoiceProfileIdentity(
            "game-hermes-ordinary-player-reply-"
                + (useJapanese ? "ja" : "zh"),
            useJapanese
                ? D043AdaptiveTtsLanguage.Japanese
                : D043AdaptiveTtsLanguage.Chinese,
            endpointFingerprint,
            referenceFingerprint,
            auxiliaryFingerprint,
            promptFingerprint,
            styleFingerprint,
            seed: 42,
            pitchPermille: 1_000);
        var fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.StageB.PlayerReplyVoiceConfiguration.V1",
            voiceProfile.Fingerprint,
            Plugin.VoiceAutoStartLocalService.Value,
            useJapanese);
        configuration = new D043PlayerReplyVoiceConfiguration(
            useJapanese,
            style,
            endpoint,
            referencePath,
            auxiliary.ToArray(),
            promptText,
            fingerprint,
            voiceProfile);
        return true;
    }

    private static void ApplyD043PlayerReplySpeechEffects(
        ActiveD043PlayerReplySpeech active,
        D043SpeechSchedulerTransition transition)
    {
        foreach (var effect in transition.Effects)
        {
            switch (effect.Kind)
            {
                case D043SpeechEffectKind.DispatchSynthesis:
                    DispatchD043PlayerReplySynthesis(active, effect);
                    break;
                case D043SpeechEffectKind.CancelSynthesis:
                    if (active.SynthesisSegmentIndex == effect.SegmentIndex
                        && string.Equals(
                            active.SynthesisCommandFingerprint,
                            effect.CommandFingerprint,
                            StringComparison.Ordinal))
                    {
                        active.SynthesisCancellation?.Cancel();
                        active.SynthesisCancellation = null;
                        active.SynthesisSegmentIndex = -1;
                        active.SynthesisCommandFingerprint = string.Empty;
                    }
                    break;
                case D043SpeechEffectKind.StartPlayback:
                    StartD043PlayerReplyPlayback(active, effect);
                    break;
                case D043SpeechEffectKind.StopPlayback:
                    if (active.PlaybackSegmentIndex == effect.SegmentIndex
                        && string.Equals(
                            active.PlaybackCommandFingerprint,
                            effect.CommandFingerprint,
                            StringComparison.Ordinal))
                    {
                        StopOwnedD043PlayerReplyPlaybackBestEffort(active);
                        ClearD043PlayerReplyPlayback(active);
                    }
                    break;
                case D043SpeechEffectKind.EmitTerminalReceipt:
                    Plugin.PluginLog.LogInfo(
                        "F6/F7 segmented speech terminal: kind="
                        + effect.TerminalKind
                        + ", reason="
                        + effect.TerminalReason
                        + ".");
                    break;
            }
        }

        if (transition.Snapshot.IsTerminal
            && ReferenceEquals(_activeD043PlayerReplySpeech, active))
        {
            active.SynthesisCancellation?.Cancel();
            active.SynthesisCancellation = null;
            _activeD043PlayerReplySpeech = null;
        }
    }

    private static void DispatchD043PlayerReplySynthesis(
        ActiveD043PlayerReplySpeech active,
        D043SpeechEffect effect)
    {
        if (!ReferenceEquals(_activeD043PlayerReplySpeech, active)
            || active.SynthesisSegmentIndex >= 0
            || !active.Turn.TryGetSynthesisText(
                active.Turn.AuthorityFingerprint,
                effect.SegmentIndex,
                effect.CommandFingerprint,
                out var segmentText)
            || !active.Turn.TryGetSynthesisDelivery(
                active.Turn.AuthorityFingerprint, effect.SegmentIndex,
                effect.CommandFingerprint, out var delivery))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        active.SynthesisCancellation = cancellation;
        active.SynthesisSegmentIndex = effect.SegmentIndex;
        active.SynthesisCommandFingerprint = effect.CommandFingerprint;
        _ = RunD043PlayerReplySynthesisAsync(
            active.Turn.AuthorityFingerprint,
            effect.SegmentIndex,
            effect.CommandFingerprint,
            segmentText,
            active.Configuration,
            delivery,
            cancellation);
    }

    private static async Task RunD043PlayerReplySynthesisAsync(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        string segmentText,
        D043PlayerReplyVoiceConfiguration configuration,
        JapaneseSpeechDelivery delivery,
        CancellationTokenSource cancellation)
    {
        var startedAt = D043PlayerReplyMonotonicMicroseconds();
        byte[]? encodedWav = null;
        var faultType = string.Empty;
        try
        {
            if (IsLocalVoiceEndpoint(configuration.Endpoint)
                && Plugin.VoiceAutoStartLocalService.Value)
            {
                await WaitForD043LocalVoiceReadinessAsync(
                        configuration.Endpoint,
                        cancellation.Token)
                    .ConfigureAwait(false);
                await LocalVoicePreparation.WaitAsync(configuration.Endpoint, cancellation.Token)
                    .ConfigureAwait(false);
                if (segmentIndex == 0)
                    Plugin.PluginLog.LogInfo("F6/F7 first-segment local preparation wait_ms="
                        + ((D043PlayerReplyMonotonicMicroseconds() - startedAt) / 1_000)
                        + "; synthesis follows separately.");
            }
            cancellation.Token.ThrowIfCancellationRequested();
            startedAt = D043PlayerReplyMonotonicMicroseconds();
            var payload = new Dictionary<string, object>
            {
                ["text"] = segmentText,
                ["text_lang"] = configuration.UseJapanese ? "ja" : "zh",
                ["ref_audio_path"] = configuration.ReferencePath,
                ["aux_ref_audio_paths"] = configuration.AuxiliaryReferencePaths,
                ["prompt_lang"] = configuration.UseJapanese ? "ja" : "zh",
                ["prompt_text"] = configuration.PromptText,
                ["text_split_method"] = "cut0",
                ["batch_size"] = 1,
                ["media_type"] = "wav",
                ["streaming_mode"] = false,
                ["seed"] = 42
            };
            var requestEndpoint = configuration.Endpoint;
            // Only the known owned Japanese service accepts this versioned extension.
            // User-selected external endpoints and Chinese keep the old request shape.
            if (configuration.UseJapanese && Uri.TryCreate(requestEndpoint, UriKind.Absolute, out var uri)
                && uri.Scheme == "http" && uri.IsLoopback && uri.Port == 9881
                && uri.AbsolutePath == "/tts" && uri.Query.Length == 0)
            {
                payload["lilith_emotion"] = delivery.Emotion;
                payload["lilith_reference_cue"] = delivery.ReferenceCue;
                payload["lilith_allow_native_cue"] = delivery.AllowNativeCue;
                requestEndpoint = new UriBuilder(uri) { Path = "/lilith/tts" }.Uri.AbsoluteUri;
            }
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                requestEndpoint);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    "The segmented TTS endpoint rejected the request with "
                    + "HTTP status "
                    + (int)response.StatusCode
                    + ".");
            }
            encodedWav = await ReadBoundedD043WavBodyAsync(
                    response,
                    cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException
            or HttpRequestException
            or IOException
            or SocketException
            or TimeoutException
            or InvalidDataException)
        {
            faultType = exception.GetType().Name;
        }
        catch (Exception exception)
        {
            faultType = exception.GetType().Name;
        }
        finally
        {
            var completedAt = D043PlayerReplyMonotonicMicroseconds();
            D043PlayerReplySpeechSynthesisCompletions.Enqueue(
                new D043PlayerReplySpeechSynthesisCompletion(
                    authorityFingerprint,
                    segmentIndex,
                    commandFingerprint,
                    startedAt,
                    Math.Max(startedAt + 1, completedAt),
                    encodedWav,
                    faultType,
                    cancellation));
        }
    }

    private static async Task WaitForD043LocalVoiceReadinessAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !uri.IsLoopback
            || uri.Port <= 0)
        {
            return;
        }

        var deadline = Stopwatch.GetTimestamp()
            + checked((long)(
                D043LocalVoiceReadinessTimeout.TotalSeconds
                * Stopwatch.Frequency));
        while (Stopwatch.GetTimestamp() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                using var probeTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                probeTimeout.CancelAfter(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync(
                        uri.Host,
                        uri.Port,
                        probeTimeout.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
            }
            await Task.Delay(
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        throw new TimeoutException(
            "The selected local voice endpoint did not become ready.");
    }

    private static async Task<byte[]> ReadBoundedD043WavBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var maximum = D043WavAdmissionCaps.V1.MaximumEncodedBytes;
        if (response.Content.Headers.ContentLength is long declared
            && (declared < D043WavAdmissionCaps.V1.MinimumEncodedBytes
                || declared > maximum))
        {
            throw new InvalidDataException(
                "The segmented TTS response length is outside the WAV "
                + "admission envelope.");
        }
        using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream(
            response.Content.Headers.ContentLength is long length
                ? checked((int)Math.Min(length, maximum))
                : 64 * 1024);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            if (destination.Length > maximum - read)
            {
                throw new InvalidDataException(
                    "The segmented TTS response exceeded the bounded WAV "
                    + "admission envelope.");
            }
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    private static void ProcessD043PlayerReplySpeechSynthesisCompletions()
    {
        while (D043PlayerReplySpeechSynthesisCompletions.TryDequeue(
                   out var completion))
        {
            try
            {
                var active = _activeD043PlayerReplySpeech;
                if (active == null
                    || !string.Equals(
                        active.Turn.AuthorityFingerprint,
                        completion.AuthorityFingerprint,
                        StringComparison.Ordinal)
                    || active.SynthesisSegmentIndex
                        != completion.SegmentIndex
                    || !string.Equals(
                        active.SynthesisCommandFingerprint,
                        completion.CommandFingerprint,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                active.SynthesisCancellation = null;
                active.SynthesisSegmentIndex = -1;
                active.SynthesisCommandFingerprint = string.Empty;
                var schedulerEventAt =
                    D043PlayerReplyMonotonicMicroseconds();
                D043SpeechSchedulerTransition transition;
                if (completion.EncodedWav == null
                    || completion.FaultType.Length > 0)
                {
                    transition = active.Turn.SynthesisFailed(
                        completion.AuthorityFingerprint,
                        completion.SegmentIndex,
                        completion.CommandFingerprint,
                        schedulerEventAt);
                    Plugin.PluginLog.LogWarning(
                        "F6/F7 segmented speech synthesis failed at segment "
                        + completion.SegmentIndex
                        + "; text chat continues; fault="
                        + (completion.FaultType.Length == 0
                            ? "NoAudio"
                            : completion.FaultType)
                        + ".");
                }
                else
                {
                    var result = active.Turn.SynthesisSucceeded(
                        completion.AuthorityFingerprint,
                        completion.SegmentIndex,
                        completion.CommandFingerprint,
                        completion.EncodedWav,
                        completion.StartedAtMicroseconds,
                        completion.CompletedAtMicroseconds,
                        schedulerEventAtMicroseconds: schedulerEventAt);
                    transition = result.SchedulerTransition;
                    if (result.WavAdmission?.Admitted != true)
                    {
                        Plugin.PluginLog.LogWarning(
                            "F6/F7 segmented speech WAV admission rejected "
                            + "segment "
                            + completion.SegmentIndex
                            + ": "
                            + (result.WavAdmission?.Reason.ToString()
                                ?? "NotInspected")
                            + ".");
                    }
                }
                ApplyD043PlayerReplySpeechEffects(active, transition);
            }
            finally
            {
                ClearD043CompletionWav(completion);
                completion.Cancellation.Dispose();
            }
        }
    }

    private static void StartD043PlayerReplyPlayback(
        ActiveD043PlayerReplySpeech active,
        D043SpeechEffect effect)
    {
        if (!ReferenceEquals(_activeD043PlayerReplySpeech, active)
            || active.PlaybackSegmentIndex >= 0
            || !active.Turn.TryCopyPlaybackWav(
                active.Turn.AuthorityFingerprint,
                effect.SegmentIndex,
                effect.CommandFingerprint,
                out var encodedWav))
        {
            return;
        }

        try
        {
            var currentVoiceSource = AudioManager.instance?.source_Voice;
            if (currentVoiceSource != null
                && currentVoiceSource.isPlaying
                && (active.PlaybackClip == null
                    || !ReferenceEquals(
                        currentVoiceSource.clip,
                        active.PlaybackClip)))
            {
                Plugin.PluginLog.LogInfo(
                    "F6/F7 foreground reply speech preempted one "
                    + "lower-priority shared voice clip.");
                AudioManager.StopVoice();
            }

            try
            {
                SetVoicePitch(1f);
                var clip = PlayWav(
                    encodedWav,
                    "F6/F7 segmented generated speech");
                active.PlaybackClip = clip;
                active.PlaybackSegmentIndex = effect.SegmentIndex;
                active.PlaybackCommandFingerprint =
                    effect.CommandFingerprint;
                var duration = Math.Max(
                    clip.length,
                    effect.AudioDurationMicroseconds / 1_000_000f);
                var playbackStartedAt = Time.unscaledTime;
                active.PlaybackStartedAt = playbackStartedAt;
                active.PlaybackExpectedEndAt = playbackStartedAt + Math.Max(
                    0.15f,
                    duration - 0.08f);
                active.PlaybackHardEndAt =
                    playbackStartedAt + duration + 2f;
            }
            catch (Exception exception)
            {
                StopOwnedD043PlayerReplyPlaybackBestEffort(active);
                ClearD043PlayerReplyPlayback(active);
                var failed = active.Turn.PlaybackFailed(
                    active.Turn.AuthorityFingerprint,
                    effect.SegmentIndex,
                    effect.CommandFingerprint,
                    D043PlayerReplyMonotonicMicroseconds());
                Plugin.PluginLog.LogWarning(
                    "F6/F7 segmented speech playback failed; text chat "
                    + "continues: "
                    + exception.GetType().Name
                    + ".");
                ApplyD043PlayerReplySpeechEffects(active, failed);
            }
        }
        finally
        {
            Array.Clear(encodedWav, 0, encodedWav.Length);
        }
    }

    private static void CompleteD043PlayerReplyPlaybackWhenDue()
    {
        var active = _activeD043PlayerReplySpeech;
        if (active == null
            || active.PlaybackSegmentIndex < 0)
        {
            return;
        }

        var voiceSource = AudioManager.instance?.source_Voice;
        if (active.PlaybackClip == null
            || voiceSource == null
            || !ReferenceEquals(voiceSource.clip, active.PlaybackClip))
        {
            FailD043PlayerReplyPlayback(active);
            return;
        }
        if (Time.unscaledTime < active.PlaybackExpectedEndAt)
        {
            if (voiceSource.isPlaying
                || Time.unscaledTime - active.PlaybackStartedAt < 0.15f)
            {
                return;
            }

            FailD043PlayerReplyPlayback(active);
            return;
        }
        if (voiceSource.isPlaying
            && Time.unscaledTime < active.PlaybackHardEndAt)
        {
            return;
        }
        var segmentIndex = active.PlaybackSegmentIndex;
        var command = active.PlaybackCommandFingerprint;
        D043SpeechSchedulerTransition transition;
        if (voiceSource.isPlaying)
        {
            StopOwnedD043PlayerReplyPlaybackBestEffort(active);
            transition = active.Turn.PlaybackFailed(
                active.Turn.AuthorityFingerprint,
                segmentIndex,
                command,
                D043PlayerReplyMonotonicMicroseconds());
        }
        else
        {
            transition = active.Turn.PlaybackCompleted(
                active.Turn.AuthorityFingerprint,
                segmentIndex,
                command,
                D043PlayerReplyMonotonicMicroseconds());
        }
        ClearD043PlayerReplyPlayback(active);
        ApplyD043PlayerReplySpeechEffects(active, transition);
    }

    private static void FailD043PlayerReplyPlayback(
        ActiveD043PlayerReplySpeech active)
    {
        var segmentIndex = active.PlaybackSegmentIndex;
        var command = active.PlaybackCommandFingerprint;
        var transition = active.Turn.PlaybackFailed(
            active.Turn.AuthorityFingerprint,
            segmentIndex,
            command,
            D043PlayerReplyMonotonicMicroseconds());
        ClearD043PlayerReplyPlayback(active);
        ApplyD043PlayerReplySpeechEffects(active, transition);
    }

    private static void ClearD043PlayerReplyPlayback(
        ActiveD043PlayerReplySpeech active)
    {
        var clip = active.PlaybackClip;
        active.PlaybackClip = null;
        if (clip != null)
        {
            try
            {
                var source = AudioManager.instance?.source_Voice;
                if (source != null
                    && ReferenceEquals(source.clip, clip)
                    && !source.isPlaying)
                {
                    source.clip = null;
                }
            }
            catch { }
            try { UnityEngine.Object.Destroy(clip); } catch { }
        }
        active.PlaybackSegmentIndex = -1;
        active.PlaybackCommandFingerprint = string.Empty;
        active.PlaybackStartedAt = -1f;
        active.PlaybackExpectedEndAt = -1f;
        active.PlaybackHardEndAt = -1f;
    }

    private static void StopOwnedD043PlayerReplyPlaybackBestEffort(
        ActiveD043PlayerReplySpeech active)
    {
        var clip = active.PlaybackClip;
        if (clip == null)
            return;
        AudioSource? source;
        try { source = AudioManager.instance?.source_Voice; }
        catch { return; }
        if (source == null || !ReferenceEquals(source.clip, clip))
            return;
        try { AudioManager.StopVoice(); } catch { }
        try
        {
            if (ReferenceEquals(source.clip, clip))
            {
                source.Stop();
                source.clip = null;
            }
        }
        catch { }
    }

    private static void ClearD043CompletionWav(
        D043PlayerReplySpeechSynthesisCompletion completion)
    {
        if (completion.EncodedWav != null)
        {
            Array.Clear(
                completion.EncodedWav,
                0,
                completion.EncodedWav.Length);
        }
    }

    private static long D043PlayerReplyMonotonicMicroseconds()
    {
        var timestamp = Stopwatch.GetTimestamp();
        return checked((long)Math.Floor(
            timestamp
            * 1_000_000d
            / Stopwatch.Frequency));
    }
}
