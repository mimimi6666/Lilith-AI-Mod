using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum InitiativeProducerSourceKind
{
    MotionDirector = 0,
    AiNoteScheduler = 1,
    OriginalGameScheduler = 2
}

internal enum InitiativeProducerEmissionDisposition
{
    CandidateEmitted = 0,
    WithdrawalEmitted = 1,
    Coalesced = 2,
    Rejected = 3,
    SourceFaulted = 4
}

internal enum InitiativeProducerEmissionReason
{
    CandidateCreated = 0,
    CandidateWithdrawn = 1,
    NormalizedStateUnchanged = 2,
    SourceDisabled = 3,
    CoordinatorStopped = 4,
    TimestampReversal = 5,
    RateLimitExceeded = 6,
    SourceAlreadyFaulted = 7,
    CandidateExpired = 8,
    TransitionMismatch = 9,
    SourceCapacityReached = 10,
    OpportunityNotEligible = 11,
    StableTypedSeamUnavailable = 12
}

internal enum AiNoteLifecycleStatus
{
    Due = 0,
    Cancelled = 1,
    Completed = 2
}

internal static class InitiativeProducerSourceIds
{
    internal const string MotionDirector = "initiative.motion-director";
    internal const string AiNoteScheduler = "initiative.ai-note-scheduler";
    internal const string OriginalGameScheduler =
        "initiative.original-game-scheduler";

    internal static string For(InitiativeProducerSourceKind source)
        => source switch
        {
            InitiativeProducerSourceKind.MotionDirector =>
                MotionDirector,
            InitiativeProducerSourceKind.AiNoteScheduler =>
                AiNoteScheduler,
            InitiativeProducerSourceKind.OriginalGameScheduler =>
                OriginalGameScheduler,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}

internal sealed class InitiativeProducerPermissions
{
    internal static InitiativeProducerPermissions AllDisabled { get; } =
        new(
            motionDirector: false,
            aiNoteScheduler: false,
            originalGameScheduler: false);

    internal bool MotionDirector { get; }
    internal bool AiNoteScheduler { get; }
    internal bool OriginalGameScheduler { get; }

    internal InitiativeProducerPermissions(
        bool motionDirector,
        bool aiNoteScheduler,
        bool originalGameScheduler)
    {
        MotionDirector = motionDirector;
        AiNoteScheduler = aiNoteScheduler;
        OriginalGameScheduler = originalGameScheduler;
    }

    internal bool IsEnabled(InitiativeProducerSourceKind source)
        => source switch
        {
            InitiativeProducerSourceKind.MotionDirector =>
                MotionDirector,
            InitiativeProducerSourceKind.AiNoteScheduler =>
                AiNoteScheduler,
            InitiativeProducerSourceKind.OriginalGameScheduler =>
                OriginalGameScheduler,
            _ => false
        };
}

internal sealed class MotionDirectorOpportunityObservation
{
    internal string ObservationId { get; }
    internal string SessionId { get; }
    internal string CorrelationId { get; }
    internal MotionDirectorDisposition Disposition { get; }
    internal MotionIntentKind MotionKind { get; }
    internal MotionDirectorReason MotionReason { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal MotionDirectorOpportunityObservation(
        string observationId,
        string sessionId,
        string correlationId,
        MotionDirectorDisposition disposition,
        MotionIntentKind motionKind,
        MotionDirectorReason motionReason,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        ObservationId = InitiativeProducerContractGuard.OpaqueId(
            observationId,
            nameof(observationId));
        SessionId = InitiativeProducerContractGuard.OpaqueId(
            sessionId,
            nameof(sessionId));
        CorrelationId = InitiativeProducerContractGuard.OpaqueId(
            correlationId,
            nameof(correlationId));
        if (!Enum.IsDefined(
                typeof(MotionDirectorDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (!Enum.IsDefined(typeof(MotionIntentKind), motionKind))
            throw new ArgumentOutOfRangeException(nameof(motionKind));
        if (!Enum.IsDefined(
                typeof(MotionDirectorReason),
                motionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(motionReason));
        }

        Disposition = disposition;
        MotionKind = motionKind;
        MotionReason = motionReason;
        (OccurredAt, ObservedAt, ExpiresAt) =
            InitiativeProducerContractGuard.Lifetime(
                occurredAt,
                observedAt,
                expiresAt);
    }
}

internal sealed class AiNoteLifecycleObservation
{
    internal string ObservationId { get; }
    internal string SessionId { get; }
    internal string EventId { get; }
    internal AiNoteLifecycleStatus Status { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset? ExpiresAt { get; }

    internal AiNoteLifecycleObservation(
        string observationId,
        string sessionId,
        string eventId,
        AiNoteLifecycleStatus status,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset? expiresAt = null)
    {
        ObservationId = InitiativeProducerContractGuard.OpaqueId(
            observationId,
            nameof(observationId));
        SessionId = InitiativeProducerContractGuard.OpaqueId(
            sessionId,
            nameof(sessionId));
        EventId = InitiativeProducerContractGuard.OpaqueId(
            eventId,
            nameof(eventId));
        if (!Enum.IsDefined(typeof(AiNoteLifecycleStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (ObservedAt < OccurredAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));

        if (status == AiNoteLifecycleStatus.Due)
        {
            if (!expiresAt.HasValue)
                throw new ArgumentNullException(nameof(expiresAt));
            var normalizedExpiry = DecisionCoreContractGuard.Timestamp(
                expiresAt.Value,
                nameof(expiresAt));
            if (normalizedExpiry <= ObservedAt
                || normalizedExpiry - OccurredAt
                    > InitiativeCandidate.MaximumLifetime)
            {
                throw new ArgumentOutOfRangeException(nameof(expiresAt));
            }
            ExpiresAt = normalizedExpiry;
        }
        else
        {
            if (expiresAt.HasValue)
            {
                throw new ArgumentException(
                    "Terminal AI Note observations cannot carry expiry.",
                    nameof(expiresAt));
            }
            ExpiresAt = null;
        }
    }
}

internal sealed class InitiativeProducerEmission
{
    internal InitiativeProducerSourceKind Source { get; }
    internal InitiativeProducerEmissionDisposition Disposition { get; }
    internal InitiativeProducerEmissionReason Reason { get; }
    internal InitiativeCandidate? Candidate { get; }
    internal InitiativeCandidateWithdrawal? Withdrawal { get; }
    internal InitiativeSideEffectCounters SideEffects { get; }

    internal InitiativeProducerEmission(
        InitiativeProducerSourceKind source,
        InitiativeProducerEmissionDisposition disposition,
        InitiativeProducerEmissionReason reason,
        InitiativeCandidate? candidate = null,
        InitiativeCandidateWithdrawal? withdrawal = null)
    {
        if (!Enum.IsDefined(
                typeof(InitiativeProducerSourceKind),
                source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        if (!Enum.IsDefined(
                typeof(InitiativeProducerEmissionDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (!Enum.IsDefined(
                typeof(InitiativeProducerEmissionReason),
                reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var candidateExpected =
            disposition
                == InitiativeProducerEmissionDisposition.CandidateEmitted;
        var withdrawalExpected =
            disposition
                == InitiativeProducerEmissionDisposition.WithdrawalEmitted;
        if (candidateExpected != (candidate != null)
            || withdrawalExpected != (withdrawal != null)
            || (candidate != null && withdrawal != null))
        {
            throw new ArgumentException(
                "Producer emission payload does not match its disposition.");
        }

        Source = source;
        Disposition = disposition;
        Reason = reason;
        Candidate = candidate;
        Withdrawal = withdrawal;
        SideEffects = InitiativeSideEffectCounters.Zero;
    }
}

internal sealed class InitiativeProducerSourceSnapshot
{
    internal InitiativeProducerSourceKind Source { get; }
    internal bool Enabled { get; }
    internal bool Faulted { get; }
    internal int AcceptedObservationCount { get; }
    internal int CandidateCount { get; }
    internal int WithdrawalCount { get; }
    internal int CoalescedCount { get; }
    internal int RejectedCount { get; }

    internal InitiativeProducerSourceSnapshot(
        InitiativeProducerSourceKind source,
        bool enabled,
        bool faulted,
        int acceptedObservationCount,
        int candidateCount,
        int withdrawalCount,
        int coalescedCount,
        int rejectedCount)
    {
        Source = source;
        Enabled = enabled;
        Faulted = faulted;
        AcceptedObservationCount = acceptedObservationCount;
        CandidateCount = candidateCount;
        WithdrawalCount = withdrawalCount;
        CoalescedCount = coalescedCount;
        RejectedCount = rejectedCount;
    }
}

internal sealed class InitiativeProducerCoordinator
{
    internal const int MaximumTrackedAiNoteEvents = 16;
    internal const int MaximumObservationsPerSourceWindow = 8;
    internal static readonly TimeSpan ObservationWindow =
        TimeSpan.FromSeconds(1);

    private readonly string _sessionId;
    private readonly InitiativeProducerPermissions _permissions;
    private readonly SourceRuntimeState[] _sources;
    private readonly Dictionary<string, AiNoteLifecycleStatus>
        _aiNoteStates = new(StringComparer.Ordinal);

    private bool _stopped;
    private string? _lastMotionFingerprint;
    private DateTimeOffset? _lastMotionExpiry;

    internal InitiativeProducerCoordinator(
        string sessionId,
        DateTimeOffset openedAt,
        InitiativeProducerPermissions? permissions = null)
    {
        _sessionId = InitiativeProducerContractGuard.OpaqueId(
            sessionId,
            nameof(sessionId));
        var normalizedOpenedAt = DecisionCoreContractGuard.Timestamp(
            openedAt,
            nameof(openedAt));
        _permissions =
            permissions ?? InitiativeProducerPermissions.AllDisabled;
        _sources = new[]
        {
            new SourceRuntimeState(
                InitiativeProducerSourceKind.MotionDirector,
                _permissions.MotionDirector,
                normalizedOpenedAt),
            new SourceRuntimeState(
                InitiativeProducerSourceKind.AiNoteScheduler,
                _permissions.AiNoteScheduler,
                normalizedOpenedAt),
            new SourceRuntimeState(
                InitiativeProducerSourceKind.OriginalGameScheduler,
                _permissions.OriginalGameScheduler,
                normalizedOpenedAt)
        };
    }

    internal InitiativeProducerEmission Observe(
        MotionDirectorOpportunityObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        ValidateSession(observation.SessionId);
        var gated = Gate(
            InitiativeProducerSourceKind.MotionDirector,
            observation.ObservedAt);
        if (gated != null)
            return gated;

        var state = State(InitiativeProducerSourceKind.MotionDirector);
        if (observation.ExpiresAt <= observation.ObservedAt)
        {
            return Reject(
                state,
                InitiativeProducerEmissionReason.CandidateExpired);
        }
        if (observation.Disposition
                == MotionDirectorDisposition.Silence
            || observation.MotionKind == MotionIntentKind.None
            || observation.MotionReason == MotionDirectorReason.None)
        {
            return Reject(
                state,
                InitiativeProducerEmissionReason.OpportunityNotEligible);
        }

        var fingerprint =
            $"{(int)observation.MotionKind}:"
            + $"{(int)observation.MotionReason}";
        if (string.Equals(
                fingerprint,
                _lastMotionFingerprint,
                StringComparison.Ordinal)
            && _lastMotionExpiry.HasValue
            && observation.ObservedAt < _lastMotionExpiry.Value)
        {
            state.CoalescedCount++;
            return new InitiativeProducerEmission(
                state.Source,
                InitiativeProducerEmissionDisposition.Coalesced,
                InitiativeProducerEmissionReason
                    .NormalizedStateUnchanged);
        }

        _lastMotionFingerprint = fingerprint;
        _lastMotionExpiry = observation.ExpiresAt;
        state.CandidateCount++;
        var candidate = new InitiativeCandidate(
            CandidateId(state.Source, observation.ObservationId),
            observation.SessionId,
            observation.CorrelationId,
            InitiativeProducerSourceIds.MotionDirector,
            "initiative.motion-slot",
            InitiativeCandidateKind.MotionOpportunity,
            InitiativeReasonCode.MotionDecisionOpportunity,
            InitiativeSensitivity.LocalGameMetadata,
            InitiativeCapabilityClass.Motion,
            SituationTriggerKind.AvatarStateChanged,
            DecisionDisposition.Act,
            DecisionEvidence.CapabilityState,
            MotionUrgency(observation.MotionReason),
            attentionCost: 0.25d,
            interruptionCost: 0.25d,
            requiresConsent: false,
            observation.OccurredAt,
            observation.ObservedAt,
            observation.ExpiresAt);
        return new InitiativeProducerEmission(
            state.Source,
            InitiativeProducerEmissionDisposition.CandidateEmitted,
            InitiativeProducerEmissionReason.CandidateCreated,
            candidate);
    }

    internal InitiativeProducerEmission Observe(
        AiNoteLifecycleObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        ValidateSession(observation.SessionId);
        var gated = Gate(
            InitiativeProducerSourceKind.AiNoteScheduler,
            observation.ObservedAt);
        if (gated != null)
            return gated;

        var state = State(InitiativeProducerSourceKind.AiNoteScheduler);
        if (_aiNoteStates.TryGetValue(
                observation.EventId,
                out var current))
        {
            if (current == observation.Status)
            {
                state.CoalescedCount++;
                return new InitiativeProducerEmission(
                    state.Source,
                    InitiativeProducerEmissionDisposition.Coalesced,
                    InitiativeProducerEmissionReason
                        .NormalizedStateUnchanged);
            }
            if (current != AiNoteLifecycleStatus.Due)
            {
                return Reject(
                    state,
                    InitiativeProducerEmissionReason.TransitionMismatch);
            }
        }
        else
        {
            if (observation.Status != AiNoteLifecycleStatus.Due)
            {
                return Reject(
                    state,
                    InitiativeProducerEmissionReason.TransitionMismatch);
            }
            if (_aiNoteStates.Count >= MaximumTrackedAiNoteEvents)
            {
                return Reject(
                    state,
                    InitiativeProducerEmissionReason
                        .SourceCapacityReached);
            }
        }

        if (observation.Status == AiNoteLifecycleStatus.Due)
        {
            if (!observation.ExpiresAt.HasValue
                || observation.ExpiresAt.Value
                    <= observation.ObservedAt)
            {
                return Reject(
                    state,
                    InitiativeProducerEmissionReason.CandidateExpired);
            }

            _aiNoteStates[observation.EventId] =
                AiNoteLifecycleStatus.Due;
            state.CandidateCount++;
            var candidate = new InitiativeCandidate(
                CandidateId(state.Source, observation.EventId),
                observation.SessionId,
                CorrelationId(state.Source, observation.EventId),
                InitiativeProducerSourceIds.AiNoteScheduler,
                $"initiative.ai-note:{observation.EventId}",
                InitiativeCandidateKind.AiNoteDue,
                InitiativeReasonCode.ScheduledNoteDue,
                InitiativeSensitivity.LocalGameMetadata,
                InitiativeCapabilityClass.Response,
                SituationTriggerKind.Timer,
                DecisionDisposition.Respond,
                DecisionEvidence.Reasoning,
                urgency: 0.4d,
                attentionCost: 0.35d,
                interruptionCost: 0.3d,
                requiresConsent: false,
                observation.OccurredAt,
                observation.ObservedAt,
                observation.ExpiresAt.Value);
            return new InitiativeProducerEmission(
                state.Source,
                InitiativeProducerEmissionDisposition.CandidateEmitted,
                InitiativeProducerEmissionReason.CandidateCreated,
                candidate);
        }

        _aiNoteStates[observation.EventId] = observation.Status;
        state.WithdrawalCount++;
        var withdrawal = new InitiativeCandidateWithdrawal(
            CandidateId(state.Source, observation.EventId),
            observation.SessionId,
            InitiativeProducerSourceIds.AiNoteScheduler,
            CorrelationId(state.Source, observation.EventId),
            observation.Status == AiNoteLifecycleStatus.Cancelled
                ? InitiativeCandidateWithdrawalReason.SourceCancelled
                : InitiativeCandidateWithdrawalReason.SourceCompleted,
            observation.OccurredAt,
            observation.ObservedAt);
        return new InitiativeProducerEmission(
            state.Source,
            InitiativeProducerEmissionDisposition.WithdrawalEmitted,
            InitiativeProducerEmissionReason.CandidateWithdrawn,
            withdrawal: withdrawal);
    }

    internal InitiativeProducerEmission ObserveOriginalSchedulerProbe(
        DateTimeOffset observedAt)
    {
        var normalized = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        var gated = Gate(
            InitiativeProducerSourceKind.OriginalGameScheduler,
            normalized);
        if (gated != null)
            return gated;

        return Reject(
            State(InitiativeProducerSourceKind.OriginalGameScheduler),
            InitiativeProducerEmissionReason
                .StableTypedSeamUnavailable);
    }

    internal void Stop()
    {
        _stopped = true;
        _aiNoteStates.Clear();
        _lastMotionFingerprint = null;
        _lastMotionExpiry = null;
    }

    internal InitiativeProducerSourceSnapshot Snapshot(
        InitiativeProducerSourceKind source)
    {
        var state = State(source);
        return new InitiativeProducerSourceSnapshot(
            state.Source,
            state.Enabled,
            state.Faulted,
            state.AcceptedObservationCount,
            state.CandidateCount,
            state.WithdrawalCount,
            state.CoalescedCount,
            state.RejectedCount);
    }

    private InitiativeProducerEmission? Gate(
        InitiativeProducerSourceKind source,
        DateTimeOffset observedAt)
    {
        var state = State(source);
        if (_stopped)
        {
            return Reject(
                state,
                InitiativeProducerEmissionReason.CoordinatorStopped);
        }
        if (!state.Enabled)
        {
            return Reject(
                state,
                InitiativeProducerEmissionReason.SourceDisabled);
        }
        if (state.Faulted)
        {
            return Reject(
                state,
                InitiativeProducerEmissionReason.SourceAlreadyFaulted);
        }
        if (observedAt < state.LastObservedAt)
        {
            return Fault(
                state,
                InitiativeProducerEmissionReason.TimestampReversal);
        }

        if (observedAt - state.WindowStartedAt
            >= ObservationWindow)
        {
            state.WindowStartedAt = observedAt;
            state.WindowObservationCount = 0;
        }
        state.WindowObservationCount++;
        if (state.WindowObservationCount
            > MaximumObservationsPerSourceWindow)
        {
            return Fault(
                state,
                InitiativeProducerEmissionReason.RateLimitExceeded);
        }

        state.LastObservedAt = observedAt;
        state.AcceptedObservationCount++;
        return null;
    }

    private static InitiativeProducerEmission Reject(
        SourceRuntimeState state,
        InitiativeProducerEmissionReason reason)
    {
        state.RejectedCount++;
        return new InitiativeProducerEmission(
            state.Source,
            InitiativeProducerEmissionDisposition.Rejected,
            reason);
    }

    private static InitiativeProducerEmission Fault(
        SourceRuntimeState state,
        InitiativeProducerEmissionReason reason)
    {
        state.Faulted = true;
        state.RejectedCount++;
        return new InitiativeProducerEmission(
            state.Source,
            InitiativeProducerEmissionDisposition.SourceFaulted,
            reason);
    }

    private SourceRuntimeState State(
        InitiativeProducerSourceKind source)
    {
        if (!Enum.IsDefined(
                typeof(InitiativeProducerSourceKind),
                source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        return _sources[(int)source];
    }

    private void ValidateSession(string sessionId)
    {
        if (!string.Equals(
                _sessionId,
                sessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Observation belongs to another Initiative session.",
                nameof(sessionId));
        }
    }

    private static string CandidateId(
        InitiativeProducerSourceKind source,
        string opaqueId)
        => $"initiative:{(int)source}:{opaqueId}";

    private static string CorrelationId(
        InitiativeProducerSourceKind source,
        string opaqueId)
        => $"initiative-correlation:{(int)source}:{opaqueId}";

    private static double MotionUrgency(MotionDirectorReason reason)
        => reason switch
        {
            MotionDirectorReason.SeekWindow => 0.55d,
            MotionDirectorReason.Depart => 0.45d,
            MotionDirectorReason.ResumeFromSit => 0.4d,
            _ => 0.3d
        };

    private sealed class SourceRuntimeState
    {
        internal InitiativeProducerSourceKind Source { get; }
        internal bool Enabled { get; }
        internal bool Faulted { get; set; }
        internal DateTimeOffset LastObservedAt { get; set; }
        internal DateTimeOffset WindowStartedAt { get; set; }
        internal int WindowObservationCount { get; set; }
        internal int AcceptedObservationCount { get; set; }
        internal int CandidateCount { get; set; }
        internal int WithdrawalCount { get; set; }
        internal int CoalescedCount { get; set; }
        internal int RejectedCount { get; set; }

        internal SourceRuntimeState(
            InitiativeProducerSourceKind source,
            bool enabled,
            DateTimeOffset openedAt)
        {
            Source = source;
            Enabled = enabled;
            LastObservedAt = openedAt;
            WindowStartedAt = openedAt;
        }
    }
}

internal static class InitiativeProducerContractGuard
{
    internal static string OpaqueId(
        string? value,
        string parameterName)
    {
        var normalized = DecisionCoreContractGuard.Identifier(
            value,
            48,
            parameterName);
        foreach (var character in normalized)
        {
            var allowed =
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_' or '.' or ':';
            if (!allowed)
            {
                throw new ArgumentException(
                    "Initiative producer identifiers must be opaque "
                    + "ASCII tokens.",
                    parameterName);
            }
        }
        return normalized;
    }

    internal static (
        DateTimeOffset OccurredAt,
        DateTimeOffset ObservedAt,
        DateTimeOffset ExpiresAt) Lifetime(
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        var occurred = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        var observed = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        var expires = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (observed < occurred)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (expires <= observed
            || expires - occurred > InitiativeCandidate.MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }
        return (occurred, observed, expires);
    }
}
