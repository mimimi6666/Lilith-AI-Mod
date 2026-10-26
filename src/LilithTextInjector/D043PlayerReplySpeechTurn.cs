using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D043PlayerReplySpeechTurnCreationAdmission
{
    Created = 1,
    SpeechProjectionMismatch = 2,
    VoiceProfileLanguageMismatch = 3,
    EstimatorSnapshotRejected = 4,
    SegmentationRejected = 5,
    SegmentDrainRejected = 6,
    NoSegments = 7
}

/// <summary>
/// Exact publication metadata admitted by the ordinary player-reply owner.
/// It contains identities only; reply text remains owned by the ephemeral turn.
/// </summary>
internal sealed class D043PlayerReplySpeechCommittedAuthority
{
    internal D043AdaptiveTtsPublicationAuthority Publication { get; }
    internal string SourceFingerprint { get; }
    internal string OperationFingerprint { get; }
    internal long LanguageGeneration { get; }
    internal bool HasFirstSegmentPrelude { get; }

    internal D043PlayerReplySpeechCommittedAuthority(
        D043AdaptiveTtsPublicationAuthority publication,
        string sourceFingerprint,
        string operationFingerprint,
        long languageGeneration,
        bool hasFirstSegmentPrelude = false)
    {
        Publication = publication
            ?? throw new ArgumentNullException(nameof(publication));
        if (!Publication.PublicationCommitted)
        {
            throw new ArgumentException(
                "Player-reply speech requires a committed publication.",
                nameof(publication));
        }
        SourceFingerprint = D043AdaptiveTtsFingerprint.Require(
            sourceFingerprint,
            nameof(sourceFingerprint));
        OperationFingerprint = D043AdaptiveTtsFingerprint.Require(
            operationFingerprint,
            nameof(operationFingerprint));
        if (languageGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(languageGeneration));
        LanguageGeneration = languageGeneration;
        HasFirstSegmentPrelude = hasFirstSegmentPrelude;
    }

    public override string ToString()
        => "D043PlayerReplySpeechCommittedAuthority(sequence="
            + Publication.PublicationSequence
            + ", content-free)";
}

internal sealed class D043PlayerReplySpeechTurnCreation
{
    internal D043PlayerReplySpeechTurnCreationAdmission Admission { get; }
    internal D043AdaptiveTtsSegmentationAdmission? SegmentationAdmission { get; }
    internal D043AdaptiveTtsSegmentTakeAdmission? SegmentTakeAdmission { get; }
    internal D043PlayerReplySpeechTurn? Turn { get; }
    internal bool Created =>
        Admission == D043PlayerReplySpeechTurnCreationAdmission.Created;

    private D043PlayerReplySpeechTurnCreation(
        D043PlayerReplySpeechTurnCreationAdmission admission,
        D043AdaptiveTtsSegmentationAdmission? segmentationAdmission,
        D043AdaptiveTtsSegmentTakeAdmission? segmentTakeAdmission,
        D043PlayerReplySpeechTurn? turn)
    {
        if (!Enum.IsDefined(
                typeof(D043PlayerReplySpeechTurnCreationAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == D043PlayerReplySpeechTurnCreationAdmission.Created)
            != (turn != null))
        {
            throw new ArgumentException(
                "Creation admission and speech turn presence must agree.",
                nameof(turn));
        }
        Admission = admission;
        SegmentationAdmission = segmentationAdmission;
        SegmentTakeAdmission = segmentTakeAdmission;
        Turn = turn;
    }

    internal static D043PlayerReplySpeechTurnCreation Accept(
        D043PlayerReplySpeechTurn turn)
        => new(
            D043PlayerReplySpeechTurnCreationAdmission.Created,
            D043AdaptiveTtsSegmentationAdmission.Created,
            D043AdaptiveTtsSegmentTakeAdmission.Completed,
            turn ?? throw new ArgumentNullException(nameof(turn)));

    internal static D043PlayerReplySpeechTurnCreation Reject(
        D043PlayerReplySpeechTurnCreationAdmission admission,
        D043AdaptiveTtsSegmentationAdmission? segmentationAdmission = null,
        D043AdaptiveTtsSegmentTakeAdmission? segmentTakeAdmission = null)
    {
        if (admission == D043PlayerReplySpeechTurnCreationAdmission.Created)
            throw new ArgumentOutOfRangeException(nameof(admission));
        return new D043PlayerReplySpeechTurnCreation(
            admission,
            segmentationAdmission,
            segmentTakeAdmission,
            null);
    }
}

internal sealed class D043PlayerReplySpeechSynthesisTransition
{
    internal D043SpeechSchedulerTransition SchedulerTransition { get; }
    internal D043WavAdmissionResult? WavAdmission { get; }
    internal D043AdaptiveTtsLatencyObservationResult? EstimatorObservation { get; }
    internal bool WavWasInspected => WavAdmission != null;
    internal bool RetainedForPlayback { get; }

    internal D043PlayerReplySpeechSynthesisTransition(
        D043SpeechSchedulerTransition schedulerTransition,
        D043WavAdmissionResult? wavAdmission,
        D043AdaptiveTtsLatencyObservationResult? estimatorObservation,
        bool retainedForPlayback)
    {
        SchedulerTransition = schedulerTransition
            ?? throw new ArgumentNullException(nameof(schedulerTransition));
        WavAdmission = wavAdmission;
        EstimatorObservation = estimatorObservation;
        RetainedForPlayback = retainedForPlayback;
    }
}

/// <summary>
/// Pure, publication-owned adapter over the D-043 Stage-A components.
/// It owns only ephemeral segment text and admitted encoded WAV bytes. It does
/// not perform HTTP, decoding, Unity allocation, playback, logging or storage.
/// </summary>
internal sealed class D043PlayerReplySpeechTurn
{
    private sealed class SegmentState
    {
        internal int Index { get; }
        internal string SegmentFingerprint { get; }
        internal int TextElementCount { get; }
        internal int Utf8ByteCount { get; }
        internal string? Text { get; set; }
        internal JapaneseSpeechDelivery Delivery { get; set; } = JapaneseSpeechDelivery.None;
        internal byte[]? EncodedWav { get; set; }
        internal D043AdmittedWavDescriptor? WavDescriptor { get; set; }

        internal SegmentState(D043AdaptiveTtsSegment segment)
        {
            if (segment == null)
                throw new ArgumentNullException(nameof(segment));
            Index = segment.Index;
            SegmentFingerprint = segment.Fingerprint;
            TextElementCount = segment.TextElementCount;
            Utf8ByteCount = segment.Utf8ByteCount;
            Text = segment.Text;
        }

        internal void ClearText()
            => Text = null;

        internal void ClearWav()
        {
            if (EncodedWav != null)
                Array.Clear(EncodedWav, 0, EncodedWav.Length);
            EncodedWav = null;
            WavDescriptor = null;
        }

        internal void Clear()
        {
            ClearText();
            ClearWav();
        }
    }

    private readonly D043PlayerReplySpeechCommittedAuthority _committedAuthority;
    private readonly D043AdaptiveTtsVoiceProfileIdentity _voiceProfile;
    private readonly ID043AdaptiveTtsLatencyEstimator _estimator;
    private readonly D043AdaptiveTtsLatencySnapshot _estimatorSnapshotAtCreation;
    private readonly D043SpeechPublicationAuthority _schedulerAuthority;
    private readonly IReadOnlyList<D043SpeechSegmentPlan> _plans;
    private readonly SegmentState[] _segments;
    private readonly Dictionary<int, string> _synthesisCommands = new();
    private readonly Dictionary<int, string> _playbackCommands = new();
    private D043AdaptiveTtsEffectScheduler? _scheduler;
    private bool _startAttempted;
    private bool _retired;
    private long _deadlineMicroseconds;

    internal string TurnId => _schedulerAuthority.UtteranceFingerprint;
    internal string AuthorityFingerprint =>
        _schedulerAuthority.AuthorityFingerprint;
    internal int SegmentCount => _segments.Length;
    internal bool IsStarted => _scheduler != null;
    internal bool IsRetired => _retired;
    internal D043AdaptiveTtsLatencySnapshot EstimatorSnapshotAtCreation =>
        _estimatorSnapshotAtCreation;
    internal D043SpeechSchedulerSnapshot? Snapshot => _scheduler?.Snapshot;
    internal int RetainedTextSegmentCount => CountRetained(text: true);
    internal int RetainedWavSegmentCount => CountRetained(text: false);

    private D043PlayerReplySpeechTurn(
        D043PlayerReplySpeechCommittedAuthority committedAuthority,
        D043AdaptiveTtsVoiceProfileIdentity voiceProfile,
        ID043AdaptiveTtsLatencyEstimator estimator,
        D043AdaptiveTtsLatencySnapshot estimatorSnapshotAtCreation,
        D043SpeechPublicationAuthority schedulerAuthority,
        IReadOnlyList<D043SpeechSegmentPlan> plans,
        SegmentState[] segments)
    {
        _committedAuthority = committedAuthority;
        _voiceProfile = voiceProfile;
        _estimator = estimator;
        _estimatorSnapshotAtCreation = estimatorSnapshotAtCreation;
        _schedulerAuthority = schedulerAuthority;
        _plans = plans;
        _segments = segments;
    }

    internal static D043PlayerReplySpeechTurnCreation TryCreate(
        D043PlayerReplySpeechCommittedAuthority committedAuthority,
        string speechText,
        D043AdaptiveTtsVoiceProfileIdentity voiceProfile,
        ID043AdaptiveTtsLatencyEstimator estimator,
        string japaneseSpeechEmotion = "")
    {
        if (committedAuthority == null)
            throw new ArgumentNullException(nameof(committedAuthority));
        if (speechText == null)
            throw new ArgumentNullException(nameof(speechText));
        if (voiceProfile == null)
            throw new ArgumentNullException(nameof(voiceProfile));
        if (estimator == null)
            throw new ArgumentNullException(nameof(estimator));

        var speechTextFingerprint = D043AdaptiveTtsFingerprint.Text(speechText);
        if (!string.Equals(
                speechTextFingerprint,
                committedAuthority.Publication.SpeechProjectionSha256,
                StringComparison.Ordinal))
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission
                    .SpeechProjectionMismatch);
        }
        if (voiceProfile.Language != committedAuthority.Publication.Language)
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission
                    .VoiceProfileLanguageMismatch);
        }

        var estimatorSnapshot = estimator.Snapshot(voiceProfile);
        if (estimatorSnapshot == null
            || !string.Equals(
                estimatorSnapshot.ProfileFingerprint,
                voiceProfile.Fingerprint,
                StringComparison.Ordinal))
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission
                    .EstimatorSnapshotRejected);
        }

        var request = new D043AdaptiveTtsTurnRequest(
            committedAuthority.Publication,
            speechText);
        var japanese = voiceProfile.Language == D043AdaptiveTtsLanguage.Japanese;
        var segmentation = D043AdaptiveTtsLazySegmenter.TryCreate(request,
            preserveJapaneseCueContinuation: japanese);
        if (!segmentation.Created || segmentation.Segmenter == null)
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission
                    .SegmentationRejected,
                segmentation.Admission);
        }

        var segmenter = segmentation.Segmenter;
        var drained = new List<D043AdaptiveTtsSegment>();
        D043AdaptiveTtsSegmentTakeAdmission? drainFailure = null;
        try
        {
            while (true)
            {
                var next = segmenter.TryTakeNext(
                    estimatorSnapshot.SegmentationTargets);
                if (next.Admission
                    == D043AdaptiveTtsSegmentTakeAdmission.Completed)
                {
                    break;
                }
                if (!next.Created || next.Segment == null)
                {
                    drainFailure = next.Admission;
                    break;
                }
                drained.Add(next.Segment);
                if (drained.Count
                    > D043AdaptiveTtsSegmentationCaps.V1.MaximumSegments)
                {
                    drainFailure = D043AdaptiveTtsSegmentTakeAdmission
                        .SegmentLimitExceeded;
                    break;
                }
            }
        }
        finally
        {
            segmenter.Retire();
        }

        if (drainFailure.HasValue)
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission
                    .SegmentDrainRejected,
                D043AdaptiveTtsSegmentationAdmission.Created,
                drainFailure);
        }
        if (drained.Count == 0)
        {
            return D043PlayerReplySpeechTurnCreation.Reject(
                D043PlayerReplySpeechTurnCreationAdmission.NoSegments,
                D043AdaptiveTtsSegmentationAdmission.Created,
                D043AdaptiveTtsSegmentTakeAdmission.Completed);
        }

        var states = new SegmentState[drained.Count];
        var plans = new D043SpeechSegmentPlan[drained.Count];
        var delivery = japanese ? JapaneseSpeechDeliveryPolicy.Plan(drained, japaneseSpeechEmotion) : null;
        for (var index = 0; index < drained.Count; index++)
        {
            var segment = drained[index];
            var state = new SegmentState(segment);
            if (delivery != null) state.Delivery = delivery[index];
            states[index] = state;
            plans[index] = new D043SpeechSegmentPlan(
                state.Index,
                state.SegmentFingerprint,
                state.TextElementCount,
                state.Utf8ByteCount,
                committedAuthority.HasFirstSegmentPrelude && index == 0);
        }

        var schedulerAuthority = new D043SpeechPublicationAuthority(
            request.TurnId,
            committedAuthority.SourceFingerprint,
            committedAuthority.OperationFingerprint,
            committedAuthority.Publication.PublicationSequence,
            committedAuthority.Publication.RuntimeGeneration,
            committedAuthority.LanguageGeneration,
            voiceProfile.Fingerprint);
        return D043PlayerReplySpeechTurnCreation.Accept(
            new D043PlayerReplySpeechTurn(
                committedAuthority,
                voiceProfile,
                estimator,
                estimatorSnapshot,
                schedulerAuthority,
                Array.AsReadOnly(plans),
                states));
    }

    internal D043SpeechSchedulerTransition Start(
        long startedAtMicroseconds,
        long lifetimeMicroseconds)
    {
        if (_startAttempted)
            return RejectedCurrent(D043SpeechEventDisposition.InvalidEventRejected);
        _startAttempted = true;
        if (_retired)
            return RejectedCurrent(D043SpeechEventDisposition.TerminalAlreadySealed);

        var transition = D043AdaptiveTtsEffectScheduler.Start(
            _schedulerAuthority,
            _plans,
            startedAtMicroseconds,
            lifetimeMicroseconds,
            D043SpeechSchedulerCaps.V1,
            out var scheduler);
        _scheduler = scheduler;
        if (scheduler == null)
        {
            RetireAndClear();
            return transition;
        }
        _deadlineMicroseconds = checked(
            startedAtMicroseconds + lifetimeMicroseconds);
        ApplyEffects(transition);
        return transition;
    }

    internal bool TryGetSynthesisText(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        out string text)
    {
        text = string.Empty;
        if (!HasExactActiveCommand(
                authorityFingerprint,
                segmentIndex,
                commandFingerprint,
                synthesis: true)
            || !TrySegment(segmentIndex, out var state)
            || state.Text == null)
        {
            return false;
        }
        text = state.Text;
        return true;
    }

    internal bool TryGetSynthesisDelivery(
        string authorityFingerprint, int segmentIndex, string commandFingerprint,
        out JapaneseSpeechDelivery delivery)
    {
        delivery = JapaneseSpeechDelivery.None;
        if (!HasExactActiveCommand(authorityFingerprint, segmentIndex, commandFingerprint, synthesis: true)
            || !TrySegment(segmentIndex, out var state) || state.Text == null) return false;
        delivery = state.Delivery;
        return true;
    }

    internal bool TryCopyPlaybackWav(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        out byte[] encodedWav)
    {
        encodedWav = Array.Empty<byte>();
        if (!HasExactActiveCommand(
                authorityFingerprint,
                segmentIndex,
                commandFingerprint,
                synthesis: false)
            || !TrySegment(segmentIndex, out var state)
            || state.EncodedWav == null
            || state.WavDescriptor == null)
        {
            return false;
        }
        encodedWav = (byte[])state.EncodedWav.Clone();
        return true;
    }

    internal D043PlayerReplySpeechSynthesisTransition SynthesisSucceeded(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        ReadOnlyMemory<byte> encodedWav,
        long startedAtMicroseconds,
        long completedAtMicroseconds,
        bool monotonicClockWasContinuous = true,
        long? schedulerEventAtMicroseconds = null)
    {
        if (startedAtMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startedAtMicroseconds));
        var schedulerEventAt = schedulerEventAtMicroseconds
            ?? completedAtMicroseconds;
        if (schedulerEventAt < completedAtMicroseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedulerEventAtMicroseconds));
        }
        var scheduler = RequireScheduler();
        var preflight = PreflightSynthesis(
            authorityFingerprint,
            segmentIndex,
            commandFingerprint);
        if (preflight != null)
        {
            return new D043PlayerReplySpeechSynthesisTransition(
                preflight,
                null,
                null,
                retainedForPlayback: false);
        }

        byte[]? ownedWav = null;
        D043WavAdmissionResult wavAdmission;
        var wavCaps = D043WavAdmissionCaps.V1;
        if (wavCaps.IsWithinV1Policy
            && encodedWav.Length >= wavCaps.MinimumEncodedBytes
            && encodedWav.Length <= wavCaps.MaximumEncodedBytes)
        {
            ownedWav = encodedWav.ToArray();
            wavAdmission = D043BoundedWavAdmission.TryAdmit(ownedWav, wavCaps);
        }
        else
        {
            // The exact parser rejects the envelope without an unbounded copy.
            wavAdmission = D043BoundedWavAdmission.TryAdmit(encodedWav, wavCaps);
        }

        var state = _segments[segmentIndex];
        var schedulerSnapshot = scheduler.Snapshot;
        var observationContinuous = monotonicClockWasContinuous
            && completedAtMicroseconds > startedAtMicroseconds
            && completedAtMicroseconds >= schedulerSnapshot.LastMonotonicMicroseconds;
        var activeAtCompletion = observationContinuous
            && completedAtMicroseconds <= _deadlineMicroseconds;
        var descriptor = wavAdmission.Descriptor;
        var observation = new D043AdaptiveTtsLatencyObservation(
            _committedAuthority.Publication.ProcessEpochId,
            _voiceProfile.Fingerprint,
            TurnId,
            segmentIndex,
            attempt: 1,
            state.TextElementCount,
            startedAtMicroseconds,
            completedAtMicroseconds,
            descriptor?.DurationMicroseconds ?? 0,
            turnWasActiveAtCompletion: activeAtCompletion,
            wavWasAdmitted: wavAdmission.Admitted,
            monotonicClockWasContinuous: observationContinuous);
        var estimatorObservation = _estimator.TryObserve(
            _voiceProfile,
            observation);

        D043SpeechSchedulerTransition transition;
        if (!wavAdmission.Admitted || descriptor == null || ownedWav == null)
        {
            transition = scheduler.SynthesisFailed(
                authorityFingerprint,
                segmentIndex,
                commandFingerprint,
                schedulerEventAt);
        }
        else
        {
            state.EncodedWav = ownedWav;
            state.WavDescriptor = descriptor;
            transition = scheduler.SynthesisSucceeded(
                authorityFingerprint,
                segmentIndex,
                commandFingerprint,
                descriptor,
                schedulerEventAt);
        }

        var applied = transition.Disposition
            == D043SpeechEventDisposition.Applied;
        if (applied)
            state.ClearText();
        if (!applied || transition.Snapshot.IsTerminal)
            state.ClearWav();
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return new D043PlayerReplySpeechSynthesisTransition(
            transition,
            wavAdmission,
            estimatorObservation,
            retainedForPlayback: applied
                && !transition.Snapshot.IsTerminal
                && state.EncodedWav != null);
    }

    internal D043SpeechSchedulerTransition SynthesisFailed(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        var transition = RequireScheduler().SynthesisFailed(
            authorityFingerprint,
            segmentIndex,
            commandFingerprint,
            monotonicMicroseconds);
        if (transition.Disposition == D043SpeechEventDisposition.Applied
            && TrySegment(segmentIndex, out var state))
        {
            state.ClearText();
        }
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return transition;
    }

    internal D043SpeechSchedulerTransition PlaybackCompleted(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        var transition = RequireScheduler().PlaybackCompleted(
            authorityFingerprint,
            segmentIndex,
            commandFingerprint,
            monotonicMicroseconds);
        if (transition.Disposition == D043SpeechEventDisposition.Applied
            && TrySegment(segmentIndex, out var state))
        {
            state.ClearWav();
        }
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return transition;
    }

    internal D043SpeechSchedulerTransition PlaybackFailed(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        long monotonicMicroseconds)
    {
        var transition = RequireScheduler().PlaybackFailed(
            authorityFingerprint,
            segmentIndex,
            commandFingerprint,
            monotonicMicroseconds);
        if (transition.Disposition == D043SpeechEventDisposition.Applied
            && TrySegment(segmentIndex, out var state))
        {
            state.ClearWav();
        }
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return transition;
    }

    internal D043SpeechSchedulerTransition Cancel(
        string authorityFingerprint,
        D043SpeechCancellationReason reason,
        long monotonicMicroseconds)
    {
        var transition = RequireScheduler().Cancel(
            authorityFingerprint,
            reason,
            monotonicMicroseconds);
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return transition;
    }

    internal D043SpeechSchedulerTransition Advance(
        string authorityFingerprint,
        long monotonicMicroseconds)
    {
        var transition = RequireScheduler().Advance(
            authorityFingerprint,
            monotonicMicroseconds);
        ApplyEffects(transition);
        RetireIfTerminal(transition);
        return transition;
    }

    private D043SpeechSchedulerTransition? PreflightSynthesis(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint)
    {
        var scheduler = RequireScheduler();
        if (!string.Equals(
                authorityFingerprint,
                AuthorityFingerprint,
                StringComparison.Ordinal))
        {
            return Dropped(D043SpeechEventDisposition.ForeignIdentityDropped);
        }
        if (_retired || scheduler.Snapshot.IsTerminal)
            return Dropped(D043SpeechEventDisposition.LateOrDuplicateDropped);
        var activeIndex = scheduler.Snapshot.SynthesisSegmentIndex;
        if (!activeIndex.HasValue)
            return Dropped(D043SpeechEventDisposition.LateOrDuplicateDropped);
        if (activeIndex.Value != segmentIndex)
            return Dropped(D043SpeechEventDisposition.OutOfOrderDropped);
        if (!_synthesisCommands.TryGetValue(segmentIndex, out var activeCommand)
            || !string.Equals(
                activeCommand,
                commandFingerprint,
                StringComparison.Ordinal))
        {
            return Dropped(D043SpeechEventDisposition.ForeignCommandDropped);
        }
        return null;
    }

    private bool HasExactActiveCommand(
        string authorityFingerprint,
        int segmentIndex,
        string commandFingerprint,
        bool synthesis)
    {
        if (_retired
            || _scheduler == null
            || _scheduler.Snapshot.IsTerminal
            || !string.Equals(
                authorityFingerprint,
                AuthorityFingerprint,
                StringComparison.Ordinal))
        {
            return false;
        }
        var activeIndex = synthesis
            ? _scheduler.Snapshot.SynthesisSegmentIndex
            : _scheduler.Snapshot.PlaybackSegmentIndex;
        var commands = synthesis ? _synthesisCommands : _playbackCommands;
        return activeIndex == segmentIndex
            && commands.TryGetValue(segmentIndex, out var activeCommand)
            && string.Equals(
                activeCommand,
                commandFingerprint,
                StringComparison.Ordinal);
    }

    private void ApplyEffects(D043SpeechSchedulerTransition transition)
    {
        foreach (var effect in transition.Effects)
        {
            switch (effect.Kind)
            {
                case D043SpeechEffectKind.DispatchSynthesis:
                    _synthesisCommands[effect.SegmentIndex] =
                        effect.CommandFingerprint;
                    break;
                case D043SpeechEffectKind.CancelSynthesis:
                    _synthesisCommands.Remove(effect.SegmentIndex);
                    break;
                case D043SpeechEffectKind.StartPlayback:
                    _playbackCommands[effect.SegmentIndex] =
                        effect.CommandFingerprint;
                    break;
                case D043SpeechEffectKind.StopPlayback:
                    _playbackCommands.Remove(effect.SegmentIndex);
                    break;
            }
        }

        var synthesisIndex = transition.Snapshot.SynthesisSegmentIndex;
        RemoveInactiveCommands(_synthesisCommands, synthesisIndex);
        var playbackIndex = transition.Snapshot.PlaybackSegmentIndex;
        RemoveInactiveCommands(_playbackCommands, playbackIndex);
    }

    private static void RemoveInactiveCommands(
        Dictionary<int, string> commands,
        int? activeIndex)
    {
        if (!activeIndex.HasValue)
        {
            commands.Clear();
            return;
        }
        if (commands.Count == 1 && commands.ContainsKey(activeIndex.Value))
            return;
        var activeCommand = commands.TryGetValue(
            activeIndex.Value,
            out var command)
            ? command
            : null;
        commands.Clear();
        if (activeCommand != null)
            commands.Add(activeIndex.Value, activeCommand);
    }

    private void RetireIfTerminal(D043SpeechSchedulerTransition transition)
    {
        if (transition.Snapshot.IsTerminal)
            RetireAndClear();
    }

    private void RetireAndClear()
    {
        if (_retired)
            return;
        foreach (var segment in _segments)
            segment.Clear();
        _synthesisCommands.Clear();
        _playbackCommands.Clear();
        _retired = true;
    }

    private D043AdaptiveTtsEffectScheduler RequireScheduler()
        => _scheduler
            ?? throw new InvalidOperationException(
                "The player-reply speech turn has not started.");

    private D043SpeechSchedulerTransition Dropped(
        D043SpeechEventDisposition disposition)
        => new(
            disposition,
            Array.Empty<D043SpeechEffect>(),
            RequireScheduler().Snapshot);

    private D043SpeechSchedulerTransition RejectedCurrent(
        D043SpeechEventDisposition disposition)
    {
        if (_scheduler != null)
            return Dropped(disposition);
        return new D043SpeechSchedulerTransition(
            disposition,
            Array.Empty<D043SpeechEffect>(),
            new D043SpeechSchedulerSnapshot(
                _retired,
                _retired
                    ? D043SpeechTerminalKind.Canceled
                    : D043SpeechTerminalKind.None,
                _retired
                    ? D043SpeechCancellationReason.PublicationRetired
                    : D043SpeechCancellationReason.None,
                _segments.Length,
                0,
                null,
                null,
                0,
                0,
                0,
                0));
    }

    private bool TrySegment(int segmentIndex, out SegmentState state)
    {
        if (segmentIndex < 0 || segmentIndex >= _segments.Length)
        {
            state = null!;
            return false;
        }
        state = _segments[segmentIndex];
        return true;
    }

    private int CountRetained(bool text)
    {
        var count = 0;
        foreach (var segment in _segments)
        {
            if (text ? segment.Text != null : segment.EncodedWav != null)
                count++;
        }
        return count;
    }
}
