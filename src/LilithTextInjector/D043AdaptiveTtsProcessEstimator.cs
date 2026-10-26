using System;
using System.Collections.Generic;
using System.Globalization;

namespace LilithTextInjector;

internal enum D043AdaptiveTtsLatencyObservationAdmission
{
    Accepted = 1,
    ProcessEpochMismatch = 2,
    CanceledOrStale = 3,
    WavNotAdmitted = 4,
    RetryExcluded = 5,
    InvalidMonotonicInterval = 6,
    TextElementCountLimitExceeded = 7,
    SynthesisLatencyLimitExceeded = 8,
    AudioDurationLimitExceeded = 9,
    ProfileMismatch = 10
}

internal sealed class D043AdaptiveTtsVoiceProfileIdentity
{
    internal string ProfileId { get; }
    internal D043AdaptiveTtsLanguage Language { get; }
    internal string EndpointServiceFingerprint { get; }
    internal string PrimaryReferenceFingerprint { get; }
    internal string AuxiliaryReferenceSetFingerprint { get; }
    internal string PromptFingerprint { get; }
    internal string StyleFingerprint { get; }
    internal int Seed { get; }
    internal int PitchPermille { get; }
    internal string Fingerprint { get; }
    internal bool ContainsEndpointOrReferenceContent => false;

    internal D043AdaptiveTtsVoiceProfileIdentity(
        string profileId,
        D043AdaptiveTtsLanguage language,
        string endpointServiceFingerprint,
        string primaryReferenceFingerprint,
        string auxiliaryReferenceSetFingerprint,
        string promptFingerprint,
        string styleFingerprint,
        int seed,
        int pitchPermille)
    {
        ProfileId = D043AdaptiveTtsFingerprint.Metadata(
            profileId,
            128,
            nameof(profileId));
        if (!Enum.IsDefined(typeof(D043AdaptiveTtsLanguage), language))
            throw new ArgumentOutOfRangeException(nameof(language));
        Language = language;
        EndpointServiceFingerprint = D043AdaptiveTtsFingerprint.Require(
            endpointServiceFingerprint,
            nameof(endpointServiceFingerprint));
        PrimaryReferenceFingerprint = D043AdaptiveTtsFingerprint.Require(
            primaryReferenceFingerprint,
            nameof(primaryReferenceFingerprint));
        AuxiliaryReferenceSetFingerprint = D043AdaptiveTtsFingerprint.Require(
            auxiliaryReferenceSetFingerprint,
            nameof(auxiliaryReferenceSetFingerprint));
        PromptFingerprint = D043AdaptiveTtsFingerprint.Require(
            promptFingerprint,
            nameof(promptFingerprint));
        StyleFingerprint = D043AdaptiveTtsFingerprint.Require(
            styleFingerprint,
            nameof(styleFingerprint));
        Seed = seed;
        if (pitchPermille < 800 || pitchPermille > 1_200)
            throw new ArgumentOutOfRangeException(nameof(pitchPermille));
        PitchPermille = pitchPermille;
        Fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.VoiceProfile.V1",
            ProfileId,
            Language,
            EndpointServiceFingerprint,
            PrimaryReferenceFingerprint,
            AuxiliaryReferenceSetFingerprint,
            PromptFingerprint,
            StyleFingerprint,
            Seed,
            PitchPermille);
    }

    public override string ToString()
        => "D043AdaptiveTtsVoiceProfileIdentity(content-free)";
}

internal sealed class D043AdaptiveTtsLatencyObservation
{
    internal string ProcessEpochId { get; }
    internal string ProfileFingerprint { get; }
    internal string TurnId { get; }
    internal int SegmentIndex { get; }
    internal int Attempt { get; }
    internal int TextElementCount { get; }
    internal long StartedAtMicroseconds { get; }
    internal long CompletedAtMicroseconds { get; }
    internal long AudioDurationMicroseconds { get; }
    internal bool TurnWasActiveAtCompletion { get; }
    internal bool WavWasAdmitted { get; }
    internal bool MonotonicClockWasContinuous { get; }
    internal string Fingerprint { get; }

    internal D043AdaptiveTtsLatencyObservation(
        string processEpochId,
        string profileFingerprint,
        string turnId,
        int segmentIndex,
        int attempt,
        int textElementCount,
        long startedAtMicroseconds,
        long completedAtMicroseconds,
        long audioDurationMicroseconds,
        bool turnWasActiveAtCompletion,
        bool wavWasAdmitted,
        bool monotonicClockWasContinuous)
    {
        ProcessEpochId = D043AdaptiveTtsFingerprint.Metadata(
            processEpochId,
            128,
            nameof(processEpochId));
        ProfileFingerprint = D043AdaptiveTtsFingerprint.Require(
            profileFingerprint,
            nameof(profileFingerprint));
        TurnId = D043AdaptiveTtsFingerprint.Require(turnId, nameof(turnId));
        if (segmentIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        SegmentIndex = segmentIndex;
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        Attempt = attempt;
        if (textElementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(textElementCount));
        TextElementCount = textElementCount;
        if (startedAtMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startedAtMicroseconds));
        StartedAtMicroseconds = startedAtMicroseconds;
        CompletedAtMicroseconds = completedAtMicroseconds;
        AudioDurationMicroseconds = audioDurationMicroseconds;
        TurnWasActiveAtCompletion = turnWasActiveAtCompletion;
        WavWasAdmitted = wavWasAdmitted;
        MonotonicClockWasContinuous = monotonicClockWasContinuous;
        Fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.LatencyObservation.V1",
            ProcessEpochId,
            ProfileFingerprint,
            TurnId,
            SegmentIndex,
            Attempt,
            TextElementCount,
            StartedAtMicroseconds,
            CompletedAtMicroseconds,
            AudioDurationMicroseconds,
            TurnWasActiveAtCompletion,
            WavWasAdmitted,
            MonotonicClockWasContinuous);
    }

    public override string ToString()
        => "D043AdaptiveTtsLatencyObservation(content-free)";
}

internal sealed class D043AdaptiveTtsLatencySnapshot
{
    internal const long FirstLatencyBudgetMicrosecondsV1 = 1_000_000;
    internal const long MinimumSteadyBufferMicrosecondsV1 = 3_000_000;
    internal const long NominalSteadyBufferMicrosecondsV1 = 4_500_000;
    internal const long MaximumSteadyBufferMicrosecondsV1 = 6_000_000;

    internal string ProfileFingerprint { get; }
    internal int AcceptedSampleCount { get; }
    internal long EstimatedSynthesisMicrosecondsPerUnit { get; }
    internal long EstimatedAudioMicrosecondsPerUnit { get; }
    internal int EstimatedRealtimeFactorPermille { get; }
    internal long SelectedSteadyBufferMicroseconds { get; }
    internal D043AdaptiveTtsSegmentationTargets SegmentationTargets { get; }
    internal string Fingerprint { get; }

    internal D043AdaptiveTtsLatencySnapshot(
        string profileFingerprint,
        int acceptedSampleCount,
        long estimatedSynthesisMicrosecondsPerUnit,
        long estimatedAudioMicrosecondsPerUnit)
    {
        ProfileFingerprint = D043AdaptiveTtsFingerprint.Require(
            profileFingerprint,
            nameof(profileFingerprint));
        if (acceptedSampleCount < 0
            || acceptedSampleCount > D043AdaptiveTtsProcessEstimator
                .MaximumAcceptedSamplesPerProfile)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedSampleCount));
        }
        AcceptedSampleCount = acceptedSampleCount;
        if (estimatedSynthesisMicrosecondsPerUnit
                < D043AdaptiveTtsProcessEstimator.MinimumSynthesisMicrosecondsPerUnit
            || estimatedSynthesisMicrosecondsPerUnit
                > D043AdaptiveTtsProcessEstimator.MaximumSynthesisMicrosecondsPerUnit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedSynthesisMicrosecondsPerUnit));
        }
        if (estimatedAudioMicrosecondsPerUnit
                < D043AdaptiveTtsProcessEstimator.MinimumAudioMicrosecondsPerUnit
            || estimatedAudioMicrosecondsPerUnit
                > D043AdaptiveTtsProcessEstimator.MaximumAudioMicrosecondsPerUnit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedAudioMicrosecondsPerUnit));
        }
        EstimatedSynthesisMicrosecondsPerUnit =
            estimatedSynthesisMicrosecondsPerUnit;
        EstimatedAudioMicrosecondsPerUnit = estimatedAudioMicrosecondsPerUnit;
        EstimatedRealtimeFactorPermille = checked((int)Math.Clamp(
            EstimatedSynthesisMicrosecondsPerUnit * 1_000
                / EstimatedAudioMicrosecondsPerUnit,
            0,
            int.MaxValue));
        SelectedSteadyBufferMicroseconds =
            EstimatedRealtimeFactorPermille >= 900
                ? MaximumSteadyBufferMicrosecondsV1
                : EstimatedRealtimeFactorPermille <= 400
                    ? MinimumSteadyBufferMicrosecondsV1
                    : NominalSteadyBufferMicrosecondsV1;
        var firstTarget = checked((int)Math.Clamp(
            FirstLatencyBudgetMicrosecondsV1
                / EstimatedSynthesisMicrosecondsPerUnit,
            8,
            14));
        var steadyTarget = checked((int)Math.Clamp(
            SelectedSteadyBufferMicroseconds
                / EstimatedAudioMicrosecondsPerUnit,
            16,
            80));
        SegmentationTargets = new D043AdaptiveTtsSegmentationTargets(
            minimumSegmentTextElements: 4,
            firstTarget,
            steadyTarget,
            firstSoftMaximumUnits: 18,
            firstForcedHardMaximumUnits: 120,
            steadyForcedHardMaximumUnits: 120);
        Fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.LatencySnapshot.V1",
            ProfileFingerprint,
            AcceptedSampleCount,
            EstimatedSynthesisMicrosecondsPerUnit,
            EstimatedAudioMicrosecondsPerUnit,
            EstimatedRealtimeFactorPermille,
            SelectedSteadyBufferMicroseconds,
            SegmentationTargets.FirstTargetUnits,
            SegmentationTargets.SteadyTargetUnits,
            SegmentationTargets.FirstSoftMaximumUnits,
            SegmentationTargets.FirstForcedHardMaximumUnits,
            SegmentationTargets.SteadyForcedHardMaximumUnits);
    }

    public override string ToString()
        => "D043AdaptiveTtsLatencySnapshot(samples="
            + AcceptedSampleCount.ToString(CultureInfo.InvariantCulture)
            + ", content-free)";
}

internal sealed class D043AdaptiveTtsLatencyObservationResult
{
    internal D043AdaptiveTtsLatencyObservationAdmission Admission { get; }
    internal D043AdaptiveTtsLatencySnapshot Snapshot { get; }
    internal bool Accepted =>
        Admission == D043AdaptiveTtsLatencyObservationAdmission.Accepted;

    internal D043AdaptiveTtsLatencyObservationResult(
        D043AdaptiveTtsLatencyObservationAdmission admission,
        D043AdaptiveTtsLatencySnapshot snapshot)
    {
        if (!Enum.IsDefined(
                typeof(D043AdaptiveTtsLatencyObservationAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        Admission = admission;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }
}

internal interface ID043AdaptiveTtsLatencyEstimator
{
    D043AdaptiveTtsLatencySnapshot Snapshot(
        D043AdaptiveTtsVoiceProfileIdentity profile);

    D043AdaptiveTtsLatencyObservationResult TryObserve(
        D043AdaptiveTtsVoiceProfileIdentity profile,
        D043AdaptiveTtsLatencyObservation observation);
}

internal sealed class D043AdaptiveTtsProcessEstimator
    : ID043AdaptiveTtsLatencyEstimator
{
    internal const int MaximumProfileCount = 8;
    internal const int MaximumAcceptedSamplesPerProfile = 32;
    internal const int FirstSegmentMaximumTextElements = 120;
    internal const int SteadySegmentMaximumTextElements = 120;
    internal const long ColdSynthesisMicrosecondsPerUnit = 100_000;
    internal const long ColdAudioMicrosecondsPerUnit = 180_000;
    internal const long MinimumSynthesisMicrosecondsPerUnit = 5_000;
    internal const long MaximumSynthesisMicrosecondsPerUnit = 2_000_000;
    internal const long MinimumAudioMicrosecondsPerUnit = 20_000;
    internal const long MaximumAudioMicrosecondsPerUnit = 1_000_000;
    internal const long MaximumSynthesisLatencyMicroseconds = 60_000_000;
    internal const long MinimumAudioDurationMicroseconds = 50_000;
    internal const long MaximumAudioDurationMicroseconds = 20_000_000;

    private readonly object _gate = new();
    private readonly string _processEpochId;
    private readonly Dictionary<string, ProfileState> _profiles = new(
        StringComparer.Ordinal);
    private long _accessSequence;

    internal int ProfileCount
    {
        get
        {
            lock (_gate)
                return _profiles.Count;
        }
    }

    internal D043AdaptiveTtsProcessEstimator(string processEpochId)
    {
        _processEpochId = D043AdaptiveTtsFingerprint.Metadata(
            processEpochId,
            128,
            nameof(processEpochId));
    }

    public D043AdaptiveTtsLatencySnapshot Snapshot(
        D043AdaptiveTtsVoiceProfileIdentity profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        lock (_gate)
        {
            return _profiles.TryGetValue(profile.Fingerprint, out var state)
                ? state.Snapshot(profile.Fingerprint)
                : ColdSnapshot(profile.Fingerprint);
        }
    }

    public D043AdaptiveTtsLatencyObservationResult TryObserve(
        D043AdaptiveTtsVoiceProfileIdentity profile,
        D043AdaptiveTtsLatencyObservation observation)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        lock (_gate)
        {
            var current = _profiles.TryGetValue(profile.Fingerprint, out var found)
                ? found.Snapshot(profile.Fingerprint)
                : ColdSnapshot(profile.Fingerprint);
            var rejection = Validate(profile, observation);
            if (rejection.HasValue)
            {
                return new D043AdaptiveTtsLatencyObservationResult(
                    rejection.Value,
                    current);
            }

            var synthesisLatency = checked(
                observation.CompletedAtMicroseconds
                - observation.StartedAtMicroseconds);
            var synthesisPerUnit = Math.Clamp(
                synthesisLatency / observation.TextElementCount,
                MinimumSynthesisMicrosecondsPerUnit,
                MaximumSynthesisMicrosecondsPerUnit);
            var audioPerUnit = Math.Clamp(
                observation.AudioDurationMicroseconds
                    / observation.TextElementCount,
                MinimumAudioMicrosecondsPerUnit,
                MaximumAudioMicrosecondsPerUnit);

            if (found == null)
            {
                EnsureProfileCapacity();
                found = new ProfileState(
                    ColdSynthesisMicrosecondsPerUnit,
                    ColdAudioMicrosecondsPerUnit,
                    NextAccessSequence());
                _profiles.Add(profile.Fingerprint, found);
            }
            found.SynthesisMicrosecondsPerUnit = Smooth(
                found.SynthesisMicrosecondsPerUnit,
                synthesisPerUnit);
            found.AudioMicrosecondsPerUnit = Smooth(
                found.AudioMicrosecondsPerUnit,
                audioPerUnit);
            found.AcceptedSampleCount = Math.Min(
                MaximumAcceptedSamplesPerProfile,
                found.AcceptedSampleCount + 1);
            found.LastAccessSequence = NextAccessSequence();
            return new D043AdaptiveTtsLatencyObservationResult(
                D043AdaptiveTtsLatencyObservationAdmission.Accepted,
                found.Snapshot(profile.Fingerprint));
        }
    }

    public override string ToString()
        => "D043AdaptiveTtsProcessEstimator(process-local, content-free)";

    private D043AdaptiveTtsLatencyObservationAdmission? Validate(
        D043AdaptiveTtsVoiceProfileIdentity profile,
        D043AdaptiveTtsLatencyObservation observation)
    {
        if (!string.Equals(
                observation.ProcessEpochId,
                _processEpochId,
                StringComparison.Ordinal))
        {
            return D043AdaptiveTtsLatencyObservationAdmission
                .ProcessEpochMismatch;
        }
        if (!string.Equals(
                observation.ProfileFingerprint,
                profile.Fingerprint,
                StringComparison.Ordinal))
        {
            return D043AdaptiveTtsLatencyObservationAdmission.ProfileMismatch;
        }
        if (!observation.TurnWasActiveAtCompletion)
        {
            return D043AdaptiveTtsLatencyObservationAdmission.CanceledOrStale;
        }
        if (!observation.WavWasAdmitted)
        {
            return D043AdaptiveTtsLatencyObservationAdmission.WavNotAdmitted;
        }
        if (observation.Attempt != 1)
        {
            return D043AdaptiveTtsLatencyObservationAdmission.RetryExcluded;
        }
        if (!observation.MonotonicClockWasContinuous
            || observation.CompletedAtMicroseconds
                <= observation.StartedAtMicroseconds)
        {
            return D043AdaptiveTtsLatencyObservationAdmission
                .InvalidMonotonicInterval;
        }
        var maximumTextElements = observation.SegmentIndex == 0
            ? FirstSegmentMaximumTextElements
            : SteadySegmentMaximumTextElements;
        if (observation.TextElementCount <= 0
            || observation.TextElementCount > maximumTextElements)
        {
            return D043AdaptiveTtsLatencyObservationAdmission
                .TextElementCountLimitExceeded;
        }
        var synthesisLatency = checked(
            observation.CompletedAtMicroseconds
            - observation.StartedAtMicroseconds);
        if (synthesisLatency > MaximumSynthesisLatencyMicroseconds)
        {
            return D043AdaptiveTtsLatencyObservationAdmission
                .SynthesisLatencyLimitExceeded;
        }
        if (observation.AudioDurationMicroseconds
                < MinimumAudioDurationMicroseconds
            || observation.AudioDurationMicroseconds
                > MaximumAudioDurationMicroseconds)
        {
            return D043AdaptiveTtsLatencyObservationAdmission
                .AudioDurationLimitExceeded;
        }
        return null;
    }

    private void EnsureProfileCapacity()
    {
        if (_profiles.Count < MaximumProfileCount)
            return;
        string? oldestKey = null;
        var oldestSequence = long.MaxValue;
        foreach (var pair in _profiles)
        {
            if (pair.Value.LastAccessSequence >= oldestSequence)
                continue;
            oldestKey = pair.Key;
            oldestSequence = pair.Value.LastAccessSequence;
        }
        if (oldestKey == null || !_profiles.Remove(oldestKey))
            throw new InvalidOperationException("Estimator profile eviction failed.");
    }

    private long NextAccessSequence()
    {
        if (_accessSequence == long.MaxValue)
            throw new InvalidOperationException("Estimator sequence exhausted.");
        return ++_accessSequence;
    }

    private static long Smooth(long previous, long sample)
    {
        var lower = Math.Max(1, previous / 2);
        var upper = checked(previous * 2);
        var winsorized = Math.Clamp(sample, lower, upper);
        return checked((previous * 7 + winsorized + 4) / 8);
    }

    private static D043AdaptiveTtsLatencySnapshot ColdSnapshot(
        string profileFingerprint)
        => new(
            profileFingerprint,
            acceptedSampleCount: 0,
            estimatedSynthesisMicrosecondsPerUnit:
                ColdSynthesisMicrosecondsPerUnit,
            estimatedAudioMicrosecondsPerUnit:
                ColdAudioMicrosecondsPerUnit);

    private sealed class ProfileState
    {
        internal int AcceptedSampleCount { get; set; }
        internal long SynthesisMicrosecondsPerUnit { get; set; }
        internal long AudioMicrosecondsPerUnit { get; set; }
        internal long LastAccessSequence { get; set; }

        internal ProfileState(
            long synthesisMicrosecondsPerUnit,
            long audioMicrosecondsPerUnit,
            long lastAccessSequence)
        {
            SynthesisMicrosecondsPerUnit = synthesisMicrosecondsPerUnit;
            AudioMicrosecondsPerUnit = audioMicrosecondsPerUnit;
            LastAccessSequence = lastAccessSequence;
        }

        internal D043AdaptiveTtsLatencySnapshot Snapshot(
            string profileFingerprint)
            => new(
                profileFingerprint,
                AcceptedSampleCount,
                SynthesisMicrosecondsPerUnit,
                AudioMicrosecondsPerUnit);
    }
}
