using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal sealed class NativeDialogueHermesCommit
{
    internal Guid EventId { get; }
    internal string Text { get; }
    internal long OccurredAtUnixTimeMilliseconds { get; }
    internal int NodeId { get; }
    internal int LineId { get; }
    internal string Trigger { get; }
    internal string Locale { get; }
    internal bool ProactiveDialogueEnabled { get; }
    // Deliberately not written to the durable outbox: restarted/stale events
    // retain their official speech, but cannot replay a screen observation.
    internal LilithGameBridgeNativeApplicationObservation? Application { get; }

    internal NativeDialogueHermesCommit(
        Guid eventId,
        string text,
        long occurredAtUnixTimeMilliseconds,
        int nodeId,
        int lineId,
        string trigger,
        string locale,
        bool proactiveDialogueEnabled = false,
        LilithGameBridgeNativeApplicationObservation? application = null)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A native-dialogue event identity is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(text)
            || text.Length > NativeDialogueHermesCaptureCoordinator.MaximumTextLength)
            throw new ArgumentOutOfRangeException(nameof(text));
        if (occurredAtUnixTimeMilliseconds is <= 0 or > 253402300799999)
            throw new ArgumentOutOfRangeException(nameof(occurredAtUnixTimeMilliseconds));
        if ((trigger ?? string.Empty).Length > NativeDialogueHermesCaptureCoordinator.MaximumTriggerLength)
            throw new ArgumentOutOfRangeException(nameof(trigger));
        if (string.IsNullOrWhiteSpace(locale)
            || locale.Length > NativeDialogueHermesCaptureCoordinator.MaximumLocaleLength)
            throw new ArgumentOutOfRangeException(nameof(locale));
        EventId = eventId;
        Text = text;
        OccurredAtUnixTimeMilliseconds = occurredAtUnixTimeMilliseconds;
        NodeId = nodeId;
        LineId = lineId;
        Trigger = trigger ?? string.Empty;
        Locale = locale;
        ProactiveDialogueEnabled = proactiveDialogueEnabled;
        // A long original line or clock correction must not invalidate the
        // original event. Only its ephemeral screen observation expires.
        Application = application is not null
            && LilithGameBridgeNativeApplicationObservation.IsApplicationTrigger(Trigger)
            && application.ObservedAtUnixMilliseconds <= occurredAtUnixTimeMilliseconds
            && occurredAtUnixTimeMilliseconds - application.ObservedAtUnixMilliseconds <= 30_000
                ? application : null;
    }

    internal NativeDialogueHermesCommit WithProactiveDialogueEnabled(
        bool enabled)
        => new(
            EventId,
            Text,
            OccurredAtUnixTimeMilliseconds,
            NodeId,
            LineId,
            Trigger,
            Locale,
            enabled,
            Application);
}

internal readonly record struct NativeDialogueHermesCompletionToken(
    long Generation,
    int NodeId,
    int LineId);

internal readonly record struct NativeDialogueForceSayInvocationToken(
    long InvocationId,
    int ManagerInstanceId);

/// <summary>
/// Carries ForceSay provenance across its return boundary without relying on
/// text or elapsed time. A successful invocation which did not synchronously
/// enter ShowNode leaves one manager-scoped deferred exclusion. When possible
/// that exclusion is bound to the exact native DialogueNode identity.
/// </summary>
internal sealed class NativeDialogueForceSayProvenance
{
    private sealed class Invocation
    {
        internal long InvocationId { get; }
        internal int ManagerInstanceId { get; }
        internal nint NodeIdentityBefore { get; }
        internal bool SawSynchronousShowNode { get; set; }

        internal Invocation(
            long invocationId,
            int managerInstanceId,
            nint nodeIdentityBefore)
        {
            InvocationId = invocationId;
            ManagerInstanceId = managerInstanceId;
            NodeIdentityBefore = nodeIdentityBefore;
        }
    }

    private sealed record DeferredExclusion(
        long InvocationId,
        int ManagerInstanceId,
        nint NodeIdentity);

    private readonly object _gate = new();
    private readonly Dictionary<long, Invocation> _active = new();
    private readonly List<DeferredExclusion> _deferred = new();
    private long _nextInvocationId;

    internal NativeDialogueForceSayInvocationToken Begin(
        int managerInstanceId,
        nint nodeIdentityBefore)
    {
        if (managerInstanceId == 0)
            throw new ArgumentOutOfRangeException(nameof(managerInstanceId));
        lock (_gate)
        {
            var invocationId = checked(++_nextInvocationId);
            _active.Add(
                invocationId,
                new Invocation(
                    invocationId,
                    managerInstanceId,
                    nodeIdentityBefore));
            return new NativeDialogueForceSayInvocationToken(
                invocationId,
                managerInstanceId);
        }
    }

    internal bool ExcludesShowNode(
        int managerInstanceId,
        nint nodeIdentity,
        int bubbleInstanceId)
    {
        if (managerInstanceId == 0 || bubbleInstanceId == 0)
            return true;
        lock (_gate)
        {
            var synchronous = _active.Values
                .Where(value => value.ManagerInstanceId == managerInstanceId)
                .ToArray();
            if (synchronous.Length > 0)
            {
                foreach (var invocation in synchronous)
                    invocation.SawSynchronousShowNode = true;
                return true;
            }

            var sameManager = _deferred
                .Where(value => value.ManagerInstanceId == managerInstanceId)
                .ToArray();
            if (sameManager.Length == 0)
                return false;

            var exact = sameManager.FirstOrDefault(
                value => value.NodeIdentity != 0
                    && value.NodeIdentity == nodeIdentity);
            if (exact != null)
            {
                _deferred.Remove(exact);
                return true;
            }

            var unresolved = sameManager.FirstOrDefault(
                value => value.NodeIdentity == 0);
            if (unresolved != null)
            {
                _deferred.Remove(unresolved);
                return true;
            }

            // The next node is provably not any resolved deferred ForceSay.
            // Retire those stale tokens so a real official node is not
            // permanently contaminated.
            _deferred.RemoveAll(value =>
                value.ManagerInstanceId == managerInstanceId);
            return false;
        }
    }

    internal void Complete(
        NativeDialogueForceSayInvocationToken token,
        nint nodeIdentity)
    {
        lock (_gate)
        {
            if (!_active.Remove(token.InvocationId, out var invocation)
                || invocation.ManagerInstanceId != token.ManagerInstanceId)
                return;
            if (invocation.SawSynchronousShowNode)
                return;
            var provenNodeIdentity = nodeIdentity != 0
                    && nodeIdentity != invocation.NodeIdentityBefore
                ? nodeIdentity
                : 0;
            _deferred.Add(new DeferredExclusion(
                invocation.InvocationId,
                invocation.ManagerInstanceId,
                provenNodeIdentity));
        }
    }

    internal void Fail(NativeDialogueForceSayInvocationToken token)
    {
        lock (_gate)
        {
            _active.Remove(token.InvocationId);
            _deferred.RemoveAll(value =>
                value.InvocationId == token.InvocationId);
        }
    }

    internal int DeferredCount
    {
        get
        {
            lock (_gate)
                return _deferred.Count;
        }
    }
}

internal readonly record struct NativeDialogueOfficialAppSpeechToken(
    long CandidateId,
    long SpeechId,
    int ManagedThreadId,
    bool IsOwner);

/// <summary>
/// Carries only exact official direct-perception Say calls across their return
/// boundary: the eight AppAware fallback lines and the four game-companion
/// opening lines. Current IL2CPP builds preserve the exact official line id on
/// DialogueManager.Say. Ordinary native dialogue, reminders, ForceSay and
/// MOD-owned text are never admitted here. A nested API call and a ShowNode
/// accepted by the normal capture remain single-writer.
/// </summary>
internal sealed class NativeDialogueOfficialAppProvenance
{
    private sealed class Speech
    {
        internal long CandidateId { get; }
        internal long SpeechId { get; }
        internal int ManagedThreadId { get; }
        internal string Text { get; }
        internal int LineId { get; }
        internal string Trigger { get; }
        internal LilithGameBridgeNativeApplicationObservation? Application { get; }
        internal bool NestedSucceeded { get; set; }
        internal bool AcceptedShowNode { get; set; }

        internal Speech(
            long candidateId,
            long speechId,
            int managedThreadId,
            string text,
            int lineId,
            string trigger,
            LilithGameBridgeNativeApplicationObservation? application)
        {
            CandidateId = candidateId;
            SpeechId = speechId;
            ManagedThreadId = managedThreadId;
            Text = text;
            LineId = lineId;
            Trigger = trigger;
            Application = application;
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, Speech> _active = new();
    private long _nextCandidateId;
    private long _nextSpeechId;

    internal static bool IsKnownAppAwareLine(int lineId)
        => TryResolveKnownAppAwareTrigger(lineId, out _);

    internal static bool TryResolveKnownAppAwareTrigger(
        int lineId,
        out string trigger)
    {
        trigger = lineId switch
        {
            2051297 => "AppAwareBrowsing",
            2051298 => "AppAwareDesign",
            2051299 => "AppAwareChat",
            2051300 => "AppAwareCodingLate",
            2051362 => "AppAwareCodingMusic",
            2051364 => "AppAwareMusicOnly",
            2051363 => "AppAwareOffice",
            2051361 => "AppAwareCrowded",
            _ => string.Empty
        };
        return trigger.Length > 0;
    }

    /// <summary>
    /// Resolves only official direct-Say perception lines. Keep this separate
    /// from <see cref="IsKnownAppAwareLine"/> because the latter is also an
    /// admission gate for database-backed ShowNode capture.
    /// </summary>
    internal static bool TryResolveKnownDirectSpeechTrigger(
        int lineId,
        out string trigger)
    {
        if (TryResolveKnownAppAwareTrigger(lineId, out trigger))
            return true;
        trigger = lineId switch
        {
            2051253 => "GamePlayEnter",
            2051254 => "GamePlayEnter",
            2051255 => "GamePlayEnter",
            2051256 => "GamePlayEnter",
            _ => string.Empty
        };
        return trigger.Length > 0;
    }

    internal NativeDialogueOfficialAppSpeechToken BeginSpeech(
        string text,
        int lineId,
        LilithGameBridgeNativeApplicationObservation? application = null)
    {
        if (!TryResolveKnownDirectSpeechTrigger(lineId, out var trigger)
            || string.IsNullOrWhiteSpace(text)
            || text.Length
                > NativeDialogueHermesCaptureCoordinator.MaximumTextLength)
            return default;
        lock (_gate)
        {
            var threadId = Environment.CurrentManagedThreadId;
            if (_active.TryGetValue(threadId, out var active))
            {
                if (!string.Equals(active.Text, text, StringComparison.Ordinal)
                    || active.LineId != lineId)
                    return default;
                return new NativeDialogueOfficialAppSpeechToken(
                    active.CandidateId,
                    active.SpeechId,
                    active.ManagedThreadId,
                    IsOwner: false);
            }

            var speech = new Speech(
                checked(++_nextCandidateId),
                checked(++_nextSpeechId),
                threadId,
                text,
                lineId,
                trigger,
                LilithGameBridgeNativeApplicationObservation.IsApplicationTrigger(trigger) ? application : null);
            _active.Add(threadId, speech);
            return new NativeDialogueOfficialAppSpeechToken(
                speech.CandidateId,
                speech.SpeechId,
                speech.ManagedThreadId,
                IsOwner: true);
        }
    }

    internal void ObserveAcceptedShowNode()
    {
        lock (_gate)
        {
            if (_active.TryGetValue(
                    Environment.CurrentManagedThreadId,
                    out var speech))
                speech.AcceptedShowNode = true;
        }
    }

    internal NativeDialogueHermesCommit? CompleteSpeech(
        NativeDialogueOfficialAppSpeechToken token,
        bool succeeded,
        int nodeId,
        int lineId,
        string locale,
        DateTimeOffset occurredAt)
    {
        if (token.CandidateId == 0 || token.SpeechId == 0)
            return null;
        lock (_gate)
        {
            var speech = FindSpeech(token);
            if (speech == null)
                return null;
            if (!token.IsOwner)
            {
                speech.NestedSucceeded |= succeeded;
                return null;
            }

            _active.Remove(speech.ManagedThreadId);
            if ((!succeeded && !speech.NestedSucceeded)
                || speech.AcceptedShowNode
                || string.IsNullOrWhiteSpace(locale)
                || locale.Length
                    > NativeDialogueHermesCaptureCoordinator.MaximumLocaleLength)
                return null;
            var unixMilliseconds = occurredAt.ToUnixTimeMilliseconds();
            if (unixMilliseconds is <= 0 or > 253402300799999)
                return null;
            return new NativeDialogueHermesCommit(
                Guid.NewGuid(),
                speech.Text,
                unixMilliseconds,
                Math.Max(0, nodeId),
                speech.LineId,
                speech.Trigger,
                locale,
                application: speech.Application);
        }
    }

    internal void FailSpeech(NativeDialogueOfficialAppSpeechToken token)
    {
        if (!token.IsOwner || token.CandidateId == 0 || token.SpeechId == 0)
            return;
        lock (_gate)
        {
            var speech = FindSpeech(token);
            if (speech != null)
                _active.Remove(speech.ManagedThreadId);
        }
    }

    private Speech? FindSpeech(
        NativeDialogueOfficialAppSpeechToken token)
        => token.ManagedThreadId == Environment.CurrentManagedThreadId
            && _active.TryGetValue(
                token.ManagedThreadId,
                out var speech)
            && speech.CandidateId == token.CandidateId
            && speech.SpeechId == token.SpeechId
                ? speech
            : null;
}

/// <summary>
/// Exact low-frequency official dialogue nodes whose meaning is useful to the
/// durable Lilith conversation.  The list includes terminal official
/// transitions and the three database-backed AppAware nodes used before the
/// fallback Say path.  Ordinary idle, touch, drag, sleep, hourly and alarm
/// chatter is intentionally outside this list.  Never replace these triples
/// with ranges.
/// </summary>
internal static class NativeDialogueMeaningfulOfficialProvenance
{
    internal static bool IsAdmittedShowNode(
        int nodeId,
        int lineId,
        string trigger)
        => (nodeId, lineId, trigger ?? string.Empty) switch
        {
            (700324, 2200001, "AppAwareCoding") => true,
            (700325, 2200002, "AppAwareCoding") => true,
            (700326, 2200003, "AppAwareVideo") => true,
            (139001, 1000351, "Weekend") => true,
            (800003, 950703, "EnterGame") => true,
            (710700, 2051013, "WakeUp") => true,
            (820010, 2051096, "PlayerDisappearComeback") => true,
            (820011, 2051097, "PlayerDisappearComeback") => true,
            (820012, 2051098, "PlayerDisappearComeback") => true,
            (820013, 2051099, "PlayerDisappearComeback") => true,
            (820014, 2051100, "PlayerDisappearComeback") => true,
            (847001, 2050015, "GamePlayEnter") => true,
            (847002, 2050016, "GamePlayPause") => true,
            (847003, 2050017, "GamePlayRest") => true,
            (847004, 2050018, "GamePlayReturn") => true,
            _ => false
        };

    /// <summary>
    /// Exact perception transitions that may ask Hermes for a continuation.
    /// Startup and calendar chatter still enter the durable conversation so
    /// Lilith remembers saying it, but they must not build a speech backlog
    /// every time the game opens.
    /// </summary>
    internal static bool RequestsProactiveExtension(string trigger)
        => trigger switch
        {
            "AppAwareBrowsing" => true,
            "AppAwareDesign" => true,
            "AppAwareChat" => true,
            "AppAwareCoding" => true,
            "AppAwareCodingLate" => true,
            "AppAwareCodingMusic" => true,
            "AppAwareMusicOnly" => true,
            "AppAwareOffice" => true,
            "AppAwareCrowded" => true,
            "AppAwareVideo" => true,
            "WakeUp" => true,
            "PlayerDisappearComeback" => true,
            "GamePlayEnter" => true,
            "GamePlayPause" => true,
            "GamePlayRest" => true,
            "GamePlayReturn" => true,
            _ => false
        };
}

internal readonly record struct NativeDialogueHermesCaptureTrace(
    string Stage,
    string Reason,
    long Generation,
    int ManagerInstanceId,
    int BubbleInstanceId,
    int TypewriterInstanceId,
    int NodeId,
    int LineId);

internal readonly record struct NativeDialogueTypewriterStopToken(
    long Generation,
    int TypewriterInstanceId);

internal readonly record struct NativeDialogueTypewriterStopPatchState(
    NativeDialogueTypewriterStopToken Token,
    bool NestedInsideSamePlay);

/// <summary>
/// Tracks one proven native DialogueManager node. Typewriter binding may occur
/// after ShowNode returns, but only for the exact manager, native node identity,
/// bubble and TypewriterEffect ancestry selected by the caller.
/// </summary>
internal sealed class NativeDialogueHermesCaptureCoordinator
{
    internal const int MaximumTextLength = 8_000;
    internal const int MaximumTriggerLength = 256;
    internal const int MaximumLocaleLength = 32;

    private sealed class Candidate
    {
        internal Guid EventId { get; }
        internal long Generation { get; }
        internal int ManagerInstanceId { get; }
        internal int BubbleInstanceId { get; }
        internal int ExpectedTypewriterInstanceId { get; }
        internal nint NodeIdentity { get; }
        internal int NodeId { get; }
        internal int LineId { get; }
        internal string Trigger { get; }
        internal string Locale { get; }
        internal LilithGameBridgeNativeApplicationObservation? Application { get; }
        internal string Text { get; set; } = string.Empty;
        internal int TypewriterInstanceId { get; set; }
        internal bool ShowNodeActive { get; set; } = true;
        internal bool ShowNodeSucceeded { get; set; }
        internal bool SawTypingStart { get; set; }
        internal bool TypewriterFinished { get; set; }
        internal bool SuccessfulPlayReturnedWithText { get; set; }

        internal Candidate(
            Guid eventId,
            long generation,
            int managerInstanceId,
            int bubbleInstanceId,
            int expectedTypewriterInstanceId,
            nint nodeIdentity,
            int nodeId,
            int lineId,
            string trigger,
            string locale,
            LilithGameBridgeNativeApplicationObservation? application)
        {
            EventId = eventId;
            Generation = generation;
            ManagerInstanceId = managerInstanceId;
            BubbleInstanceId = bubbleInstanceId;
            ExpectedTypewriterInstanceId = expectedTypewriterInstanceId;
            NodeIdentity = nodeIdentity;
            NodeId = nodeId;
            LineId = lineId;
            Trigger = trigger;
            Locale = locale;
            Application = application;
        }
    }

    private readonly object _gate = new();
    private readonly Action<NativeDialogueHermesCaptureTrace>? _trace;
    private Candidate? _candidate;
    private long _nextGeneration;

    internal NativeDialogueHermesCaptureCoordinator(
        Action<NativeDialogueHermesCaptureTrace>? trace = null)
    {
        _trace = trace;
    }

    internal long BeginShowNode(
        int managerInstanceId,
        int bubbleInstanceId,
        int expectedTypewriterInstanceId,
        nint nodeIdentity,
        int nodeId,
        int lineId,
        string trigger,
        string locale,
        bool provenOfficialLifecycle)
        => BeginShowNode(
            managerInstanceId,
            bubbleInstanceId,
            expectedTypewriterInstanceId,
            nodeIdentity,
            nodeId,
            lineId,
            trigger,
            locale,
            provenOfficialLifecycle,
            out _);

    internal long BeginShowNode(
        int managerInstanceId,
        int bubbleInstanceId,
        int expectedTypewriterInstanceId,
        nint nodeIdentity,
        int nodeId,
        int lineId,
        string trigger,
        string locale,
        bool provenOfficialLifecycle,
        out bool admitted,
        LilithGameBridgeNativeApplicationObservation? application = null)
    {
        admitted = false;
        lock (_gate)
        {
            if (_candidate != null)
                Drop("replaced-by-next-show-node");
            _candidate = null;
            var generation = checked(++_nextGeneration);
            if (!provenOfficialLifecycle
                || managerInstanceId == 0
                || bubbleInstanceId == 0
                || expectedTypewriterInstanceId == 0
                || nodeIdentity == 0
                || nodeId <= 0
                || lineId <= 0)
                return generation;
            var boundedLocale = Bound(locale, MaximumLocaleLength);
            if (string.IsNullOrWhiteSpace(boundedLocale))
                return generation;
            _candidate = new Candidate(
                Guid.NewGuid(),
                generation,
                managerInstanceId,
                bubbleInstanceId,
                expectedTypewriterInstanceId,
                nodeIdentity,
                nodeId,
                lineId,
                Bound(trigger, MaximumTriggerLength),
                boundedLocale,
                LilithGameBridgeNativeApplicationObservation.IsApplicationTrigger(trigger) ? application : null);
            admitted = true;
            Emit(_candidate, "armed", "official-show-node");
            return generation;
        }
    }

    internal long BindTypewriter(
        int managerInstanceId,
        nint nodeIdentity,
        int typewriterInstanceId,
        bool isTypingAtBind)
    {
        lock (_gate)
        {
            if (_candidate == null
                || _candidate.ManagerInstanceId != managerInstanceId
                || _candidate.NodeIdentity == 0
                || _candidate.NodeIdentity != nodeIdentity
                || _candidate.ExpectedTypewriterInstanceId
                    != typewriterInstanceId
                || typewriterInstanceId == 0)
                return 0;
            if (!_candidate.ShowNodeActive
                && !_candidate.ShowNodeSucceeded)
                return 0;
            if (_candidate.TypewriterInstanceId != 0
                && _candidate.TypewriterInstanceId != typewriterInstanceId)
            {
                Drop("different-typewriter-instance");
                return 0;
            }
            _candidate.TypewriterInstanceId = typewriterInstanceId;
            if (isTypingAtBind)
            {
                _candidate.SawTypingStart = true;
                _candidate.TypewriterFinished = false;
            }
            Emit(
                _candidate,
                "typewriter-bound",
                _candidate.ShowNodeActive
                    ? "inside-show-node"
                    : "after-show-node");
            if (isTypingAtBind)
                Emit(_candidate, "typing-started", "already-typing-at-bind");
            return _candidate.Generation;
        }
    }

    internal void EndShowNode(
        long generation,
        int bubbleInstanceId,
        bool succeeded)
    {
        lock (_gate)
        {
            if (_candidate == null || _candidate.Generation != generation)
                return;
            if (_candidate.BubbleInstanceId != bubbleInstanceId)
            {
                Drop("show-node-bubble-mismatch");
                return;
            }
            _candidate.ShowNodeActive = false;
            _candidate.ShowNodeSucceeded = succeeded;
            if (!succeeded)
            {
                Drop("show-node-threw");
                return;
            }
            Emit(
                _candidate,
                "show-node-ended",
                _candidate.TypewriterInstanceId == 0
                    ? "awaiting-exact-typewriter"
                    : "typewriter-already-bound");
        }
    }

    internal void ObserveDisplayedText(
        long generation,
        int typewriterInstanceId,
        string text)
    {
        lock (_gate)
        {
            if (!MatchesTypewriter(_candidate, generation, typewriterInstanceId))
                return;
            var exact = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exact)
                || exact.Length > MaximumTextLength)
            {
                Drop("display-text-invalid");
                return;
            }
            _candidate!.Text = exact;
            _candidate.SuccessfulPlayReturnedWithText = true;
            Emit(_candidate, "display-text-observed", "identity-only");
        }
    }

    internal void ObserveTypingState(int typewriterInstanceId, bool isTyping)
    {
        lock (_gate)
        {
            if (_candidate == null
                || _candidate.TypewriterInstanceId != typewriterInstanceId)
                return;
            if (isTyping)
            {
                _candidate.SawTypingStart = true;
                _candidate.TypewriterFinished = false;
                Emit(_candidate, "typing-started", "same-typewriter");
                return;
            }
            if (_candidate.SawTypingStart)
            {
                _candidate.TypewriterFinished = true;
                Emit(_candidate, "typing-finished", "same-typewriter");
            }
            else
            {
                Emit(_candidate, "typing-false-ignored", "missing-start");
            }
        }
    }

    internal NativeDialogueTypewriterStopToken BeginTypewriterStop(
        int typewriterInstanceId)
    {
        lock (_gate)
        {
            return _candidate != null
                    && _candidate.TypewriterInstanceId == typewriterInstanceId
                ? new NativeDialogueTypewriterStopToken(
                    _candidate.Generation,
                    typewriterInstanceId)
                : default;
        }
    }

    internal void CompleteTypewriterStop(
        NativeDialogueTypewriterStopToken token,
        bool originalRan,
        bool nestedInsideSamePlay)
    {
        lock (_gate)
        {
            if (!MatchesTypewriter(
                    _candidate,
                    token.Generation,
                    token.TypewriterInstanceId))
                return;
            if (_candidate!.TypewriterFinished)
            {
                Emit(_candidate, "typewriter-stop", "finished-during-stop");
                return;
            }
            if (nestedInsideSamePlay)
            {
                Emit(
                    _candidate,
                    "typewriter-stop",
                    originalRan
                        ? "play-reset-ignored"
                        : "skipped-play-reset-ignored");
                return;
            }
            Drop(originalRan
                ? "stop-before-typing-finished"
                : "typewriter-stop-skipped");
        }
    }

    internal void FailTypewriterStop(
        NativeDialogueTypewriterStopToken token)
    {
        lock (_gate)
        {
            if (MatchesTypewriter(
                    _candidate,
                    token.Generation,
                    token.TypewriterInstanceId))
                Drop("typewriter-stop-threw");
        }
    }

    internal void InterruptTypewriter(int typewriterInstanceId, string reason)
    {
        lock (_gate)
        {
            if (_candidate != null
                && _candidate.TypewriterInstanceId == typewriterInstanceId)
                Drop(reason);
        }
    }

    internal NativeDialogueHermesCompletionToken? BeginVerifiedCompletion(
        int managerInstanceId,
        int nodeId,
        int lineId)
    {
        lock (_gate)
        {
            if (_candidate == null)
                return null;
            if (!MatchesNode(_candidate, nodeId, lineId)
                || _candidate.ManagerInstanceId != managerInstanceId)
            {
                Drop("completion-manager-or-node-mismatch");
                return null;
            }
            if (_candidate.ShowNodeActive)
            {
                Drop("completion-before-show-node-returned");
                return null;
            }
            if (_candidate.TypewriterInstanceId == 0)
            {
                Drop("completion-without-typewriter-binding");
                return null;
            }
            if (!_candidate.SawTypingStart)
            {
                // Current official game builds can complete Typewriter.Play
                // synchronously without calling SetIsTyping.  Admit that exact
                // lifecycle only after the same Play returned successfully
                // with bounded text and the same manager/node reached its
                // verified natural completion.  Stop/Skip and mismatched
                // lifecycle paths have already retired the candidate.
                if (!_candidate.SuccessfulPlayReturnedWithText
                    || string.IsNullOrWhiteSpace(_candidate.Text))
                {
                    Drop("completion-without-typing-start");
                    return null;
                }
                _candidate.TypewriterFinished = true;
                Emit(
                    _candidate,
                    "typing-finished",
                    "successful-play-return-fallback");
            }
            if (!_candidate.TypewriterFinished)
            {
                Drop("completion-before-typing-finished");
                return null;
            }
            if (string.IsNullOrWhiteSpace(_candidate.Text))
            {
                Drop("completion-without-display-text");
                return null;
            }
            Emit(_candidate, "completion-ready", "verified-prefix");
            return new NativeDialogueHermesCompletionToken(
                _candidate.Generation,
                nodeId,
                lineId);
        }
    }

    internal NativeDialogueHermesCommit? CompleteVerified(
        NativeDialogueHermesCompletionToken? token,
        bool originalRan,
        DateTimeOffset occurredAt)
    {
        lock (_gate)
        {
            if (token == null
                || !originalRan
                || !MatchesNode(_candidate, token.Value.NodeId, token.Value.LineId)
                || _candidate!.Generation != token.Value.Generation
                || !_candidate.TypewriterFinished)
            {
                Drop(token == null
                    ? "completion-postfix-without-token"
                    : !originalRan
                        ? "completion-original-skipped"
                        : "completion-postfix-identity-mismatch");
                return null;
            }
            return Take(occurredAt);
        }
    }

    internal void Interrupt(string reason = "external-interrupt")
    {
        lock (_gate)
        {
            if (_candidate != null)
                Drop(reason);
        }
    }

    private NativeDialogueHermesCommit Take(DateTimeOffset occurredAt)
    {
        var candidate = _candidate
            ?? throw new InvalidOperationException("A native-dialogue candidate was not active.");
        Emit(candidate, "commit-created", "verified-postfix");
        _candidate = null;
        return new NativeDialogueHermesCommit(
            candidate.EventId,
            candidate.Text,
            occurredAt.ToUnixTimeMilliseconds(),
            candidate.NodeId,
            candidate.LineId,
            candidate.Trigger,
            candidate.Locale,
            application: candidate.Application);
    }

    private void Drop(string reason)
    {
        var candidate = _candidate;
        if (candidate == null)
            return;
        Emit(candidate, "dropped", reason);
        _candidate = null;
    }

    private void Emit(Candidate? candidate, string stage, string reason)
    {
        if (candidate == null || _trace == null)
            return;
        try
        {
            _trace(new NativeDialogueHermesCaptureTrace(
                stage,
                reason,
                candidate.Generation,
                candidate.ManagerInstanceId,
                candidate.BubbleInstanceId,
                candidate.TypewriterInstanceId,
                candidate.NodeId,
                candidate.LineId));
        }
        catch
        {
            // Diagnostics must never alter the verified capture lifecycle.
        }
    }

    private static bool MatchesNode(Candidate? candidate, int nodeId, int lineId)
        => candidate != null
            && candidate.NodeId == nodeId
            && candidate.LineId == lineId;

    private static bool MatchesTypewriter(
        Candidate? candidate,
        long generation,
        int typewriterInstanceId)
        => candidate != null
            && candidate.Generation == generation
            && candidate.TypewriterInstanceId == typewriterInstanceId;

    private static string Bound(string value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}

/// <summary>
/// A bounded write-new outbox. Each official line is one atomic file. An
/// accepted (including duplicate) Hermes receipt is the only operation that
/// removes it, so a process crash before deletion safely resends the same ID.
/// </summary>
internal sealed class NativeDialogueHermesDurableOutbox
{
    internal const int DefaultCapacity = 512;
    private const int SchemaVersion = 1;
    private const string EventFileExtension = ".native-dialogue-event.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed class StoredEvent
    {
        public int SchemaVersion { get; set; }
        public Guid EventId { get; set; }
        public string Text { get; set; } = string.Empty;
        public long OccurredAtUnixTimeMilliseconds { get; set; }
        public int NodeId { get; set; }
        public int LineId { get; set; }
        public string Trigger { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
        public bool ProactiveDialogueEnabled { get; set; }
    }

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _capacity;
    private readonly List<NativeDialogueHermesCommit> _pending;

    internal NativeDialogueHermesDurableOutbox(
        string directory,
        int capacity = DefaultCapacity)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The native-dialogue outbox directory is required.", nameof(directory));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _directory = Path.GetFullPath(directory);
        _capacity = capacity;
        Directory.CreateDirectory(_directory);
        _pending = Directory.EnumerateFiles(
                _directory,
                "*" + EventFileExtension,
                SearchOption.TopDirectoryOnly)
            .Select(ReadEvent)
            .OrderBy(value => value.OccurredAtUnixTimeMilliseconds)
            .ThenBy(value => value.EventId)
            .ToList();
        if (_pending.Count > _capacity
            || _pending.Select(value => value.EventId).Distinct().Count()
                != _pending.Count)
            throw new InvalidDataException("The native-dialogue outbox exceeded its bounded identity contract.");
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    internal bool TryEnqueue(NativeDialogueHermesCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_gate)
        {
            var duplicate = _pending.FirstOrDefault(value => value.EventId == commit.EventId);
            if (duplicate != null)
            {
                if (!SameEvent(duplicate, commit))
                    throw new InvalidDataException("A native-dialogue outbox identity carried conflicting content.");
                return true;
            }
            if (_pending.Count >= _capacity)
                return false;

            var destination = EventPath(commit.EventId);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var encoded = JsonSerializer.SerializeToUtf8Bytes(ToStored(commit), JsonOptions);
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(encoded);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination);
                _pending.Add(commit);
                _pending.Sort(CompareEvents);
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                }
            }
        }
    }

    internal bool TryPeek(out NativeDialogueHermesCommit? commit)
    {
        lock (_gate)
        {
            commit = _pending.Count == 0 ? null : _pending[0];
            return commit != null;
        }
    }

    internal bool Acknowledge(Guid eventId)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A native-dialogue event identity is required.", nameof(eventId));
        lock (_gate)
        {
            var index = _pending.FindIndex(value => value.EventId == eventId);
            if (index < 0)
                return false;
            File.Delete(EventPath(eventId));
            _pending.RemoveAt(index);
            return true;
        }
    }

    internal IReadOnlyList<NativeDialogueHermesCommit> Snapshot()
    {
        lock (_gate)
            return _pending.ToArray();
    }

    private NativeDialogueHermesCommit ReadEvent(string path)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredEvent>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException("A native-dialogue outbox event was empty.");
            if (stored.SchemaVersion != SchemaVersion
                || stored.EventId == Guid.Empty
                || !string.Equals(
                    Path.GetFileName(path),
                    stored.EventId.ToString("N") + EventFileExtension,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A native-dialogue outbox event identity was rejected.");
            return new NativeDialogueHermesCommit(
                stored.EventId,
                stored.Text,
                stored.OccurredAtUnixTimeMilliseconds,
                stored.NodeId,
                stored.LineId,
                stored.Trigger,
                stored.Locale,
                stored.ProactiveDialogueEnabled);
        }
        catch (Exception exception) when (exception is
            JsonException or ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                "A native-dialogue outbox event was rejected: " + Path.GetFileName(path),
                exception);
        }
    }

    private string EventPath(Guid eventId)
        => Path.Combine(_directory, eventId.ToString("N") + EventFileExtension);

    private static StoredEvent ToStored(NativeDialogueHermesCommit value)
        => new()
        {
            SchemaVersion = SchemaVersion,
            EventId = value.EventId,
            Text = value.Text,
            OccurredAtUnixTimeMilliseconds = value.OccurredAtUnixTimeMilliseconds,
            NodeId = value.NodeId,
            LineId = value.LineId,
            Trigger = value.Trigger,
            Locale = value.Locale,
            ProactiveDialogueEnabled = value.ProactiveDialogueEnabled
        };

    private static int CompareEvents(
        NativeDialogueHermesCommit left,
        NativeDialogueHermesCommit right)
    {
        var occurred = left.OccurredAtUnixTimeMilliseconds.CompareTo(
            right.OccurredAtUnixTimeMilliseconds);
        return occurred != 0 ? occurred : left.EventId.CompareTo(right.EventId);
    }

    private static bool SameEvent(
        NativeDialogueHermesCommit left,
        NativeDialogueHermesCommit right)
        => left.EventId == right.EventId
            && left.OccurredAtUnixTimeMilliseconds == right.OccurredAtUnixTimeMilliseconds
            && left.NodeId == right.NodeId
            && left.LineId == right.LineId
            && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            && string.Equals(left.Trigger, right.Trigger, StringComparison.Ordinal)
            && string.Equals(left.Locale, right.Locale, StringComparison.Ordinal)
            && left.ProactiveDialogueEnabled == right.ProactiveDialogueEnabled;
}
