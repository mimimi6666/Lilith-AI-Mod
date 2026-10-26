using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace LilithTextInjector;

// Owner-approved MOD dialogue only. It never impersonates an official node,
// starts a model/TTS request, or contributes dialogue to conversation history.
internal static partial class DialogueManagerUpdatePatch
{
    private static readonly MiniGameDialoguePolicy MiniGameCuePolicy = new();
    private static readonly MiniGameDialogueVoiceOwnership MiniGameVoiceOwnership = new();
    private static string _miniGameDialogueKind = string.Empty;
    private static Guid _miniGameDialogueMatch;
    private static MiniGameCueTicket? _miniGamePendingCue;
    private static MiniGameCueTicket? _miniGamePlayingCue;
    private static MiniGameCueTicket? _miniGamePublishingCue;
    private static long _miniGamePublicationGeneration;
    private static MiniGameCueTicket? _miniGameResultSelection;
    private static string? _miniGameResultSelectionId;
    private static AudioClip? _miniGameOwnedVoiceClip;
    private static AudioSource? _miniGameOwnedVoiceSource;
    private static DialogueNode? _miniGameOwnedSourceNode;
    private static DialogueNode? _miniGameOwnedDialogueNode;
    private static DialogueNode? _miniGameCapturedDialogueNode;
    private static double _miniGameVoiceDeadline;
    private static double _miniGameVoiceStartedAt;
    private static double _miniGameVoiceExpectedSeconds;
    private static bool _publishingMiniGameDialogue;
    private static bool _miniGameDialogueAudioFailureLogged;
    private const double MiniGameCueLifetimeSeconds = 3;

    private enum MiniGameVoiceEndReason
    {
        Cancelled, UserStopped, NativeVoiceTakeover, SyntheticDialogueTakeover,
        SessionChanged, LocaleChanged, GameFinished, VoiceDisabled, SnackInactive,
        ForegroundLost, InputBlocked, ControlHeld, ChatOrSpeechBusy,
        NativeSourceChanged, NativeSourceUnavailable, NativeSourceMuted,
        PlaybackStoppedEarly, Completed, DeadlineExpired, OwnershipExpired, PresentationFailed,
    }

    private sealed class MiniGameCueTicket
    {
        internal readonly string Kind;
        internal readonly Guid Match;
        internal readonly MiniGameDialogueCue Cue;
        internal readonly int Revision;
        internal readonly string DisplayLocale;
        internal readonly bool Japanese;
        internal readonly double ExpiresAt;

        internal MiniGameCueTicket(string kind, Guid match, MiniGameDialogueCue cue,
            int revision, string displayLocale, bool japanese, double now)
        {
            Kind = kind; Match = match; Cue = cue; Revision = revision;
            DisplayLocale = displayLocale; Japanese = japanese;
            ExpiresAt = now + MiniGameCueLifetimeSeconds;
        }
    }

    private static double MiniGameDialogueNow =>
        (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    internal static bool MiniGameDialogueVoiceActive => _miniGameOwnedVoiceClip != null;

    private static string MiniGameVoiceLocale(MiniGameCueTicket ticket) =>
        ticket.DisplayLocale + (ticket.Japanese ? ":ja" : ":zh");

    internal static void ObserveMiniGameForceSayInvocation(DialogueManager manager)
    {
        // Our authored node uses BeginDialogue, never ForceSay. Every native
        // ForceSay entry, including a nested one, belongs to another speaker.
        CancelMiniGameDialogueForSyntheticTakeover();
    }

    // PlayNodeVoice is a native static callback. Resolve its current manager
    // explicitly and bind both native identities; never exempt all game speech
    // or use matching text as proof of ownership.
    internal static bool IsCurrentMiniGameDialogueNode(DialogueNode? node)
    {
        try
        {
            var ticket = _publishingMiniGameDialogue ? _miniGamePublishingCue : _miniGamePlayingCue;
            var manager = DialogueManager.instance;
            if (ticket == null || manager == null || node == null
                || !MiniGameCueIdentityCurrent(ticket)) return false;
            if (!_publishingMiniGameDialogue && (_miniGameOwnedDialogueNode == null
                || node.Pointer != _miniGameOwnedDialogueNode.Pointer)) return false;
            var current = manager.CurrentNode;
            if (current == null) return false;
            var locale = MiniGameVoiceLocale(ticket);
            var now = MiniGameDialogueNow;
            if (!_publishingMiniGameDialogue)
                return MiniGameVoiceOwnership.OwnsNode(manager.GetInstanceID(), node.Pointer,
                    current.Pointer, ticket.Match, locale, now);
            var ownsClone = MiniGameVoiceOwnership.TryOwnSynchronousNode(_miniGamePublicationGeneration,
                manager.GetInstanceID(), node.Pointer, current.Pointer, ticket.Match, locale, now);
            if (ownsClone) _miniGameCapturedDialogueNode = node;
            return ownsClone;
        }
        catch (Exception) { return false; }
    }

    internal static void CancelMiniGameDialogueForNativeVoiceTakeover()
    {
        _miniGamePendingCue = null;
        StopMiniGameOwnedVoice(MiniGameVoiceEndReason.NativeVoiceTakeover);
    }

    private static void CancelMiniGameDialogueForSyntheticTakeover()
    {
        _miniGamePendingCue = null;
        StopMiniGameOwnedVoice(MiniGameVoiceEndReason.SyntheticDialogueTakeover);
    }

    internal static void BeginMiniGameDialogue(string gameKind, Guid match)
    {
        CancelMiniGameDialogue();
        _miniGameDialogueKind = string.Empty;
        _miniGameDialogueMatch = Guid.Empty;
        _miniGameResultSelection = null;
        _miniGameResultSelectionId = null;
        MiniGameCuePolicy.Reset();
        _miniGameDialogueAudioFailureLogged = false;
        if (match == Guid.Empty || (gameKind != "old-maid" && gameKind != "chess")) return;
        _miniGameDialogueKind = gameKind;
        _miniGameDialogueMatch = match;
    }

    internal static void EndMiniGameDialogue(string gameKind)
    {
        if (!string.Equals(_miniGameDialogueKind, gameKind, StringComparison.Ordinal)) return;
        CancelMiniGameDialogue();
        _miniGameDialogueKind = string.Empty;
        _miniGameDialogueMatch = Guid.Empty;
        _miniGameResultSelection = null;
        _miniGameResultSelectionId = null;
        MiniGameCuePolicy.Reset();
    }

    internal static void OfferMiniGameDialogue(string gameKind, MiniGameDialogueCue cue, int revision)
    {
        try
        {
            if (!MiniGameDialogueMatches(gameKind) || !CanAdmitMiniGameDialogue()) return;
            var ticket = NewMiniGameCueTicket(gameKind, cue, revision);
            if (!MiniGameCueCurrent(ticket)) return;
            // There is only one short-lived candidate, never a speech backlog.
            if (_miniGamePendingCue != null && MiniGameCueCurrent(_miniGamePendingCue)
                && MiniGameDialogueNow <= _miniGamePendingCue.ExpiresAt
                && MiniGameDialoguePolicy.Priority(_miniGamePendingCue.Cue)
                    >= MiniGameDialoguePolicy.Priority(cue)) return;
            _miniGamePendingCue = ticket;
        }
        catch (Exception) { _miniGamePendingCue = null; }
    }

    internal static bool TryPresentMiniGameDialogue(string gameKind, MiniGameDialogueCue cue, int revision)
    {
        try
        {
            if (!MiniGameDialogueMatches(gameKind)) return false;
            var ticket = NewMiniGameCueTicket(gameKind, cue, revision);
            if (!MiniGameCueCurrent(ticket) || !MiniGameDialogueStable(gameKind)
                || !CanAdmitMiniGameDialogue()) return false;
            return PresentMiniGameCue(ticket);
        }
        catch (Exception error)
        {
            CancelMiniGameDialogue();
            Plugin.PluginLog.LogDebug("Mini-game dialogue unavailable: " + error.GetType().Name);
            return false;
        }
    }

    internal static void UpdateMiniGameDialogue()
    {
        try
        {
            UpdateMiniGameOwnedVoice();
            var pending = _miniGamePendingCue;
            if (pending == null) return;
            if (!MiniGameCueCurrent(pending) || MiniGameDialogueNow > pending.ExpiresAt
                || !CanAdmitMiniGameDialogue())
            { _miniGamePendingCue = null; return; }
            if (!MiniGameDialogueStable(pending.Kind)) return;
            _miniGamePendingCue = null;
            PresentMiniGameCue(pending);
        }
        catch (Exception error)
        {
            CancelMiniGameDialogue();
            Plugin.PluginLog.LogDebug("Mini-game dialogue update unavailable: " + error.GetType().Name);
        }
    }

    private static MiniGameCueTicket NewMiniGameCueTicket(string kind, MiniGameDialogueCue cue, int revision)
        => new(kind, _miniGameDialogueMatch, cue, revision, MiniGameDisplayLocale(),
            IsJapaneseVoiceMode(), MiniGameDialogueNow);

    private static string MiniGameDisplayLocale() =>
        MiniGameDisplayLanguage.Resolve(GameSetting.Language ?? string.Empty);

    private static bool MiniGameDialogueMatches(string gameKind) =>
        _miniGameDialogueMatch != Guid.Empty
        && string.Equals(gameKind, _miniGameDialogueKind, StringComparison.Ordinal)
        && (gameKind == "old-maid" ? OldMaidPresentation.IsOpen
            : gameKind == "chess" && ChessPresentation.IsOpen);

    private static bool MiniGameDialogueStable(string kind) => kind == "old-maid"
        ? OldMaidPresentation.DialogueStable : kind == "chess" && ChessPresentation.DialogueStable;

    private static bool MiniGameCueIdentityCurrent(MiniGameCueTicket ticket) =>
        ticket.Match == _miniGameDialogueMatch && MiniGameDialogueMatches(ticket.Kind)
        && ticket.DisplayLocale == MiniGameDisplayLocale() && ticket.Japanese == IsJapaneseVoiceMode();

    private static bool MiniGameCueCurrent(MiniGameCueTicket ticket) =>
        MiniGameCueIdentityCurrent(ticket)
        && ticket.Revision == (ticket.Kind == "old-maid" ? OldMaidPresentation.DialogueRevision
            : ChessPresentation.DialogueRevision);

    private static bool MiniGameHigherPriorityBusy() =>
        !OldMaidNativeBridge.IsSnackActive || !ReadOldMaidForeground() || ReadOldMaidModal()
        || OldMaidControlKeyHeld() || _requestInFlight || _transcriptionInFlight
        || _microphoneRecording || _activeGameHermesVoiceCapture != null || _pendingVoiceSubmitText != null
        || _aiPagesAwaitingAdvance || !PendingTranscripts.IsEmpty || HasActivePublishedPlayerReplySpeech()
        || !PendingReminderSpeech.IsEmpty || !PendingVoiceAudio.IsEmpty
        || Volatile.Read(ref _activeReminderSpeechCount) > 0
        || Volatile.Read(ref _reminderSpeechPipelineBusy) != 0
        || Volatile.Read(ref _pendingGameHermesNotificationCount) > 0;

    private static bool CanAdmitMiniGameDialogue()
    {
        if (MiniGameHigherPriorityBusy() || MiniGameDialogueVoiceActive) return false;
        var manager = DialogueManager.instance;
        if (manager == null || manager.IsBusy || AudioManager.instance?.source_Voice?.isPlaying == true)
            return false;
        var choices = UnityEngine.Object.FindObjectOfType<PlayerLineController>();
        return choices == null || !choices.HasActiveOrPendingOptions;
    }

    private static bool PresentMiniGameCue(MiniGameCueTicket ticket)
    {
        // One result selection can survive a failed final admission, but it
        // never schedules playback itself: the same-match result caller retries.
        // This avoids consuming the only result if F7 opens during local IO.
        // Reusing the ID is not reusing old audio: a fresh ticket selects and
        // validates the current subtitle/voice language again below.
        var reuseResult = MiniGameDialoguePolicy.IsResult(ticket.Cue)
            && _miniGameResultSelection != null && _miniGameResultSelection.Match == ticket.Match
            && _miniGameResultSelection.Kind == ticket.Kind && _miniGameResultSelection.Revision == ticket.Revision
            && _miniGameResultSelection.Cue == ticket.Cue;
        var id = reuseResult ? _miniGameResultSelectionId
            : MiniGameCuePolicy.Choose(ticket.Cue, ticket.Revision, MiniGameDialogueNow);
        if (id == null) return true; // Deliberately skipped/previously consumed, not a retry loop.
        var line = MiniGameDialogueCatalog.Get(id);
        if (line == null) return false;
        if (MiniGameDialoguePolicy.IsResult(ticket.Cue))
        { _miniGameResultSelection = ticket; _miniGameResultSelectionId = id; }
        var text = MiniGameDialogueSubtitles.Text(line, ticket.DisplayLocale);
        var audio = line.Audio(ticket.Japanese);
        var duration = (float)Math.Clamp(audio.DurationSeconds + 0.2, 3, 15);
        AudioClip? prepared = null;
        try
        {
            var native = AudioManager.instance?.source_Voice;
            if (Plugin.VoiceEnabled.Value && native != null && native.isActiveAndEnabled
                && !native.mute && native.volume > 0 && audio.DurationSeconds <= 14.8)
            {
                try
                {
                    var pack = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "MiniGameVoice");
                    var samples = audio.ReadPcm16(pack);
                    try
                    {
                        prepared = AudioClip.Create("LilithMiniGameCue", samples.Length / audio.Channels,
                            audio.Channels, audio.SampleRate, false);
                        if (prepared == null || !prepared.SetData(samples, 0))
                            throw new InvalidOperationException("Mini-game voice samples rejected.");
                    }
                    finally { Array.Clear(samples, 0, samples.Length); }
                }
                catch (Exception error)
                {
                    DestroyMiniGameClip(prepared); prepared = null;
                    if (!_miniGameDialogueAudioFailureLogged)
                        Plugin.PluginLog.LogWarning("Mini-game voice unavailable; subtitle only: " + error.GetType().Name);
                    _miniGameDialogueAudioFailureLogged = true;
                }
            }
            // Recheck after local IO. No background task reads Unity objects.
            if (!MiniGameCueCurrent(ticket) || !CanAdmitMiniGameDialogue()) return false;
            var manager = DialogueManager.instance;
            if (manager == null) return false;
            _miniGamePendingCue = null;
            var ownsPublishedNode = PublishMiniGameOwnedNode(manager, ticket, text, duration,
                out var publicationNow, out var capturedNode);
            if (!ownsPublishedNode) return false;
            if (MiniGameDialoguePolicy.IsResult(ticket.Cue))
            { _miniGameResultSelection = null; _miniGameResultSelectionId = null; }
            if (prepared != null && ownsPublishedNode && MiniGameCueCurrent(ticket) && !MiniGameHigherPriorityBusy())
            {
                native = AudioManager.instance?.source_Voice;
                if (Plugin.VoiceEnabled.Value && native != null && native.isActiveAndEnabled
                    && !native.isPlaying && !native.mute && native.volume > 0)
                {
                    _miniGameOwnedVoiceClip = prepared;
                    _miniGameOwnedVoiceSource = native;
                    // Retain the exact native wrapper for the duration of the
                    // lease; do not allow a collected node address to be reused.
                    _miniGameOwnedDialogueNode = capturedNode;
                    _miniGamePlayingCue = ticket;
                    _miniGameVoiceStartedAt = MiniGameDialogueNow;
                    _miniGameVoiceExpectedSeconds = audio.DurationSeconds;
                    _miniGameVoiceDeadline = publicationNow + duration + 1;
                    prepared = null;
                    LogMiniGameVoice("start", ticket, "Accepted", 0, audio.DurationSeconds);
                    AudioManager.PlayVoice(_miniGameOwnedVoiceClip, false, true);
                    if (!OwnsMiniGameVoice()) StopMiniGameOwnedVoice(MiniGameVoiceEndReason.NativeSourceChanged);
                }
            }
            if (_miniGameOwnedVoiceClip == null)
            {
                MiniGameVoiceOwnership.Cancel();
                _miniGameOwnedSourceNode = null;
                _miniGameOwnedDialogueNode = null;
            }
            return true;
        }
        catch (Exception error)
        {
            StopMiniGameOwnedVoice(MiniGameVoiceEndReason.PresentationFailed);
            Plugin.PluginLog.LogDebug("Mini-game cue presentation unavailable: " + error.GetType().Name);
            return false;
        }
        finally { DestroyMiniGameClip(prepared); }
    }

    private static bool PublishMiniGameOwnedNode(DialogueManager manager, MiniGameCueTicket ticket,
        string text, float duration, out double publicationNow, out DialogueNode? capturedNode)
    {
        publicationNow = MiniGameDialogueNow;
        capturedNode = null;
        if (!MiniGameCueCurrent(ticket) || !CanAdmitMiniGameDialogue()
            || manager.IsBusy || manager._isNodeTransitioning || manager._isPostStateRunning
            || manager._nodeTransitionCoroutine != null || manager._postStateCoroutine != null)
            return false;
        // Current native ForceSay's neutral fields, without its reminder
        // preemption or asynchronous pre-fade. The constructor owns its lists.
        var source = new DialogueNode
        {
            id = -1, lineId = 0, text = text, textIsFinal = false,
            emotion = string.Empty, duration = duration, nextId = -1,
        };
        var previousNode = manager.CurrentNode;
        var generation = MiniGameVoiceOwnership.BeginPublication(manager.GetInstanceID(),
            previousNode == null ? 0 : previousNode.Pointer, source.Pointer,
            ticket.Match, MiniGameVoiceLocale(ticket), publicationNow, publicationNow + duration + 1);
        _miniGameOwnedSourceNode = source;
        _miniGamePublishingCue = ticket;
        _miniGamePublicationGeneration = generation;
        _miniGameCapturedDialogueNode = null;
        var returned = false;
        _publishingMiniGameDialogue = true;
        try
        {
            if (!MiniGameVoiceOwnership.TryEnterPublication(generation, manager.GetInstanceID(),
                    ticket.Match, MiniGameVoiceLocale(ticket), MiniGameDialogueNow))
            {
                StopMiniGameOwnedVoice(MiniGameVoiceEndReason.PresentationFailed);
                return false;
            }
            // Match ForceSay's empty origin with a real empty native value
            // wrapper. This is a normal field setter, never a Nullable detour.
            manager._activeTrigger = new Il2CppSystem.Nullable<DialogueTriggerType>();
            // Verified current native order: clone -> CurrentNode -> existing
            // PlayNodeVoice callback -> OnDialogueStart, synchronously.
            // Once native publication is attempted, never repeatedly replay a
            // result sentence merely because a callback could not be proved.
            if (MiniGameDialoguePolicy.IsResult(ticket.Cue))
            { _miniGameResultSelection = null; _miniGameResultSelectionId = null; }
            manager.BeginDialogue(source);
            returned = true;
        }
        finally
        {
            capturedNode = _miniGameCapturedDialogueNode;
            _miniGameCapturedDialogueNode = null;
            _publishingMiniGameDialogue = false;
            _miniGamePublishingCue = null;
            _miniGamePublicationGeneration = 0;
        }
        var current = manager.CurrentNode;
        var accepted = MiniGameVoiceOwnership.CompletePublication(generation, returned,
            manager.GetInstanceID(), current == null ? 0 : current.Pointer,
            ticket.Match, MiniGameVoiceLocale(ticket), MiniGameDialogueNow);
        if (!accepted)
        {
            _miniGameOwnedSourceNode = null;
            capturedNode = null;
            Plugin.PluginLog.LogInfo("Mini-game voice: stage=not-started, reason=UnprovenPublishedNode, cue="
                + ticket.Cue + ".");
        }
        return accepted;
    }

    private static bool OwnsMiniGameVoice()
    {
        var source = AudioManager.instance?.source_Voice;
        return source != null && _miniGameOwnedVoiceSource != null && _miniGameOwnedVoiceClip != null
            && source.Pointer == _miniGameOwnedVoiceSource.Pointer && source.clip != null
            && source.clip.Pointer == _miniGameOwnedVoiceClip.Pointer;
    }

    private static void UpdateMiniGameOwnedVoice()
    {
        if (_miniGameOwnedVoiceClip == null) return;
        var source = AudioManager.instance?.source_Voice;
        // A normally completed next move does not cut off a sentence already
        // spoken about the prior move. Pending candidates remain revision-bound.
        var ticket = _miniGamePlayingCue;
        MiniGameVoiceEndReason? reason = null;
        if (ticket == null || ticket.Match != _miniGameDialogueMatch || !MiniGameDialogueMatches(ticket.Kind))
            reason = MiniGameVoiceEndReason.SessionChanged;
        else if (ticket.DisplayLocale != MiniGameDisplayLocale() || ticket.Japanese != IsJapaneseVoiceMode())
            reason = MiniGameVoiceEndReason.LocaleChanged;
        else if ((ticket.Kind == "old-maid" ? OldMaidPresentation.DialogueFinished : ChessPresentation.DialogueFinished)
            && !MiniGameDialoguePolicy.IsResult(ticket.Cue)) reason = MiniGameVoiceEndReason.GameFinished;
        else if (!Plugin.VoiceEnabled.Value) reason = MiniGameVoiceEndReason.VoiceDisabled;
        else if (!OldMaidNativeBridge.IsSnackActive) reason = MiniGameVoiceEndReason.SnackInactive;
        else if (!ReadOldMaidForeground()) reason = MiniGameVoiceEndReason.ForegroundLost;
        else if (ReadOldMaidModal()) reason = MiniGameVoiceEndReason.InputBlocked;
        else if (OldMaidControlKeyHeld()) reason = MiniGameVoiceEndReason.ControlHeld;
        else if (MiniGameHigherPriorityBusy()) reason = MiniGameVoiceEndReason.ChatOrSpeechBusy;
        else if (source == null || !source.isActiveAndEnabled) reason = MiniGameVoiceEndReason.NativeSourceUnavailable;
        else if (!OwnsMiniGameVoice()) reason = MiniGameVoiceEndReason.NativeSourceChanged;
        else if (source.mute || source.volume <= 0) reason = MiniGameVoiceEndReason.NativeSourceMuted;
        else if (!source.isPlaying) reason = MiniGameDialogueNow - _miniGameVoiceStartedAt + 0.15 >= _miniGameVoiceExpectedSeconds
            ? MiniGameVoiceEndReason.Completed : MiniGameVoiceEndReason.PlaybackStoppedEarly;
        else if (MiniGameDialogueNow > _miniGameVoiceDeadline) reason = MiniGameVoiceEndReason.DeadlineExpired;
        else if (!MiniGameVoiceOwnership.IsCurrent(ticket.Match, MiniGameVoiceLocale(ticket), MiniGameDialogueNow))
            reason = MiniGameVoiceEndReason.OwnershipExpired;
        if (reason.HasValue) StopMiniGameOwnedVoice(reason.Value);
    }

    // All takeover hooks run on Unity's main thread. Do not stop another clip
    // merely because it shares AudioManager's channel with this one.
    internal static void CancelMiniGameDialogue(bool cancelUnpublishedResult = false)
    {
        _miniGamePendingCue = null;
        if (cancelUnpublishedResult)
        {
            // F8 is an explicit stop, not a chat-priority deferral. The policy
            // already consumed this selected event, so it cannot reappear later.
            _miniGameResultSelection = null;
            _miniGameResultSelectionId = null;
        }
        StopMiniGameOwnedVoice(cancelUnpublishedResult ? MiniGameVoiceEndReason.UserStopped : MiniGameVoiceEndReason.Cancelled);
    }

    private static void StopMiniGameOwnedVoice(MiniGameVoiceEndReason reason)
    {
        var clip = _miniGameOwnedVoiceClip;
        var ticket = _miniGamePlayingCue;
        var elapsed = Math.Max(0, MiniGameDialogueNow - _miniGameVoiceStartedAt);
        try
        {
            if (OwnsMiniGameVoice())
            {
                _miniGameOwnedVoiceSource!.Stop();
                _miniGameOwnedVoiceSource.clip = null;
            }
        }
        catch (Exception) { }
        finally
        {
            _miniGameOwnedVoiceClip = null;
            _miniGameOwnedVoiceSource = null;
            _miniGameOwnedSourceNode = null;
            _miniGameOwnedDialogueNode = null;
            _miniGameCapturedDialogueNode = null;
            _miniGamePlayingCue = null;
            _miniGamePublishingCue = null;
            _miniGamePublicationGeneration = 0;
            _miniGameVoiceDeadline = 0;
            MiniGameVoiceOwnership.Cancel();
            if (clip != null && ticket != null)
                LogMiniGameVoice("end", ticket, reason.ToString(), elapsed, _miniGameVoiceExpectedSeconds);
            _miniGameVoiceStartedAt = _miniGameVoiceExpectedSeconds = 0;
            DestroyMiniGameClip(clip);
        }
    }

    private static void LogMiniGameVoice(string stage, MiniGameCueTicket ticket,
        string reason, double elapsed, double expected)
    {
        // Exactly one start/end pair per attempted playback, no text, card
        // values, board positions or per-frame output.
        try
        {
            Plugin.PluginLog.LogInfo(FormattableString.Invariant(
                $"Mini-game voice: stage={stage}, reason={reason}, cue={ticket.Cue}, voice={(ticket.Japanese ? "ja" : "zh")}, elapsed={elapsed:F3}, expected={expected:F3}."));
        }
        catch (Exception) { }
    }

    private static void DestroyMiniGameClip(AudioClip? clip)
    { try { if (clip != null) UnityEngine.Object.Destroy(clip); } catch (Exception) { } }
}
