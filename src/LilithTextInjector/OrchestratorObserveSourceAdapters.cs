using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum PlayerTurnInputSurface
{
    TextEntry = 0,
    PushToTalkTranscript = 1
}

internal enum PlayerTurnLifecycleStatus
{
    Accepted = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3
}

internal enum ExistingExecutionLifecycleStatus
{
    Queued = 0,
    Started = 1,
    Completed = 2,
    Rejected = 3,
    Interrupted = 4,
    Failed = 5,
    TimedOut = 6
}

internal enum ExistingExecutionCategory
{
    AvatarMotion = 0
}

internal enum OrchestratorObserveEmissionDisposition
{
    Emitted = 0,
    Coalesced = 1,
    Rejected = 2,
    SourceFaulted = 3
}

internal enum OrchestratorObserveEmissionReason
{
    NormalizedTransition = 0,
    NormalizedStateUnchanged = 1,
    SourceDisabled = 2,
    CoordinatorStopped = 3,
    TimestampReversal = 4,
    TransitionMismatch = 5,
    SourceCapacityReached = 6,
    RateLimitExceeded = 7,
    SourceAlreadyFaulted = 8
}

internal sealed class PlayerTurnLifecycleObservation
{
    internal string TurnId { get; }
    internal PlayerTurnInputSurface Surface { get; }
    internal PlayerTurnLifecycleStatus Status { get; }
    internal DateTimeOffset OccurredAt { get; }

    internal PlayerTurnLifecycleObservation(
        string turnId,
        PlayerTurnInputSurface surface,
        PlayerTurnLifecycleStatus status,
        DateTimeOffset occurredAt)
    {
        TurnId = OrchestratorObserveSourceContractGuard.OpaqueId(
            turnId,
            nameof(turnId));
        if (!Enum.IsDefined(typeof(PlayerTurnInputSurface), surface))
            throw new ArgumentOutOfRangeException(nameof(surface));
        if (!Enum.IsDefined(typeof(PlayerTurnLifecycleStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Surface = surface;
        Status = status;
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
    }
}

internal sealed class AvatarInteractionGatesObservation
{
    internal string ObservationId { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal DateTimeOffset OccurredAt { get; }

    internal AvatarInteractionGatesObservation(
        string observationId,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        DateTimeOffset occurredAt)
    {
        ObservationId = OrchestratorObserveSourceContractGuard.OpaqueId(
            observationId,
            nameof(observationId));
        IsAvatarUnderDirectPlayerControl =
            isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
    }
}

internal sealed class ExistingExecutionLifecycleObservation
{
    internal string ExecutionId { get; }
    internal ExistingExecutionCategory Category { get; }
    internal ExistingExecutionLifecycleStatus Status { get; }
    internal bool HasDirectCorrelation { get; }
    internal bool IsConfounded { get; }
    internal DateTimeOffset OccurredAt { get; }

    internal ExistingExecutionLifecycleObservation(
        string executionId,
        ExistingExecutionLifecycleStatus status,
        DateTimeOffset occurredAt,
        ExistingExecutionCategory category =
            ExistingExecutionCategory.AvatarMotion,
        bool hasDirectCorrelation = false,
        bool isConfounded = true)
    {
        ExecutionId = OrchestratorObserveSourceContractGuard.OpaqueId(
            executionId,
            nameof(executionId));
        if (!Enum.IsDefined(typeof(ExistingExecutionCategory), category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(
                typeof(ExistingExecutionLifecycleStatus),
                status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        Category = category;
        Status = status;
        HasDirectCorrelation = hasDirectCorrelation;
        IsConfounded = isConfounded;
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
    }
}

internal sealed class OrchestratorObserveSourcePermissions
{
    internal static OrchestratorObserveSourcePermissions AllDisabled { get; } =
        new(
            playerTurnLifecycle: false,
            avatarInteractionGates: false,
            existingExecutionLifecycle: false);

    internal bool PlayerTurnLifecycle { get; }
    internal bool AvatarInteractionGates { get; }
    internal bool ExistingExecutionLifecycle { get; }

    internal OrchestratorObserveSourcePermissions(
        bool playerTurnLifecycle,
        bool avatarInteractionGates,
        bool existingExecutionLifecycle)
    {
        PlayerTurnLifecycle = playerTurnLifecycle;
        AvatarInteractionGates = avatarInteractionGates;
        ExistingExecutionLifecycle = existingExecutionLifecycle;
    }

    internal bool IsEnabled(OrchestratorObserveSourceKind source)
        => source switch
        {
            OrchestratorObserveSourceKind.PlayerTurnLifecycle =>
                PlayerTurnLifecycle,
            OrchestratorObserveSourceKind.AvatarInteractionGates =>
                AvatarInteractionGates,
            OrchestratorObserveSourceKind.ExistingExecutionLifecycle =>
                ExistingExecutionLifecycle,
            _ => false
        };
}

internal sealed class OrchestratorObserveSourceEmission
{
    internal OrchestratorObserveSourceKind Source { get; }
    internal OrchestratorObserveEmissionDisposition Disposition { get; }
    internal OrchestratorObserveEmissionReason Reason { get; }
    internal OrchestratorShadowStep? Step { get; }
    internal bool Emitted =>
        Disposition == OrchestratorObserveEmissionDisposition.Emitted;

    internal OrchestratorObserveSourceEmission(
        OrchestratorObserveSourceKind source,
        OrchestratorObserveEmissionDisposition disposition,
        OrchestratorObserveEmissionReason reason,
        OrchestratorShadowStep? step)
    {
        if (!Enum.IsDefined(typeof(OrchestratorObserveSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(
                typeof(OrchestratorObserveEmissionDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (!Enum.IsDefined(
                typeof(OrchestratorObserveEmissionReason),
                reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if ((disposition == OrchestratorObserveEmissionDisposition.Emitted)
            != (step != null))
        {
            throw new ArgumentException(
                "Only emitted observations can carry an Orchestrator step.",
                nameof(step));
        }

        Source = source;
        Disposition = disposition;
        Reason = reason;
        Step = step;
    }
}

internal sealed class OrchestratorObserveSourceCoordinator
{
    internal const int MaximumTrackedExecutions = 16;
    internal const int MaximumEmissionsPerSourceWindow = 8;
    internal static readonly TimeSpan EmissionWindow =
        TimeSpan.FromSeconds(1);

    private readonly string _sessionId;
    private readonly string _sessionToken;
    private readonly OrchestratorSessionOwner _owner;
    private readonly OrchestratorObserveSourcePermissions _permissions;
    private readonly SourceRuntimeState[] _sources;
    private readonly HashSet<string> _activeExecutionIds =
        new(StringComparer.Ordinal);

    private long _nextEnvelopeSequence = 2;
    private long _nextSituationSequence = 1;
    private DateTimeOffset _lastObservationAt;
    private string? _activeTurnId;
    private bool _avatarDirectControl;
    private bool _avatarConversationBusy;
    private bool _avatarPendingExecution;
    private bool _effectiveDirectControl;
    private bool _effectiveConversationBusy;
    private bool _effectivePendingExecution;
    private bool _stopped;

    internal OrchestratorShadowStep SessionOpenedStep { get; }

    internal OrchestratorObserveSourceCoordinator(
        string sessionId,
        OrchestratorSessionOwner owner,
        DateTimeOffset openedAt,
        OrchestratorObserveSourcePermissions? permissions = null)
    {
        _sessionId = OrchestratorObserveSourceContractGuard.OpaqueId(
            sessionId,
            nameof(sessionId));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _permissions =
            permissions ?? OrchestratorObserveSourcePermissions.AllDisabled;
        _sessionToken =
            OrchestratorObserveSourceContractGuard.StableToken(_sessionId);
        _lastObservationAt = DecisionCoreContractGuard.Timestamp(
            openedAt,
            nameof(openedAt));
        _sources = new[]
        {
            new SourceRuntimeState(
                _permissions.PlayerTurnLifecycle),
            new SourceRuntimeState(
                _permissions.AvatarInteractionGates),
            new SourceRuntimeState(
                _permissions.ExistingExecutionLifecycle)
        };

        var open = new OrchestratorSessionOpenedEnvelope(
            OrchestratorProtocol.CurrentVersion,
            "observe-" + _sessionToken + "-1-open",
            "observe-" + _sessionToken + "-open",
            _sessionId,
            _lastObservationAt,
            OrchestratorSourceKind.LocalGameSurface,
            _owner);
        SessionOpenedStep = new OrchestratorShadowStep(open);
    }

    internal OrchestratorObserveSourceStatus GetSourceStatus(
        OrchestratorObserveSourceKind source)
        => SourceState(source).Status;

    internal OrchestratorObserveSourceEmission Observe(
        PlayerTurnLifecycleObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        const OrchestratorObserveSourceKind source =
            OrchestratorObserveSourceKind.PlayerTurnLifecycle;
        var blocked = Gate(source, observation.OccurredAt);
        if (blocked != null)
            return blocked;

        string? nextTurnId;
        switch (observation.Status)
        {
            case PlayerTurnLifecycleStatus.Accepted:
                if (_activeTurnId != null)
                {
                    return string.Equals(
                            _activeTurnId,
                            observation.TurnId,
                            StringComparison.Ordinal)
                        ? Coalesce(source, observation.OccurredAt)
                        : Reject(
                            source,
                            OrchestratorObserveEmissionReason
                                .TransitionMismatch);
                }
                nextTurnId = observation.TurnId;
                break;
            case PlayerTurnLifecycleStatus.Completed:
            case PlayerTurnLifecycleStatus.Failed:
            case PlayerTurnLifecycleStatus.Cancelled:
                if (!string.Equals(
                        _activeTurnId,
                        observation.TurnId,
                        StringComparison.Ordinal))
                {
                    return Reject(
                        source,
                        OrchestratorObserveEmissionReason
                            .TransitionMismatch);
                }
                nextTurnId = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(observation));
        }

        return EmitInteraction(
            source,
            observation.TurnId,
            observation.OccurredAt,
            observation.Status == PlayerTurnLifecycleStatus.Accepted
                ? SituationTriggerKind.UserMessage
                : SituationTriggerKind.InternalStateChanged,
            _avatarDirectControl,
            _avatarConversationBusy || nextTurnId != null,
            _avatarPendingExecution || _activeExecutionIds.Count > 0,
            () => _activeTurnId = nextTurnId);
    }

    internal OrchestratorObserveSourceEmission Observe(
        AvatarInteractionGatesObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        const OrchestratorObserveSourceKind source =
            OrchestratorObserveSourceKind.AvatarInteractionGates;
        var blocked = Gate(source, observation.OccurredAt);
        if (blocked != null)
            return blocked;

        return EmitInteraction(
            source,
            observation.ObservationId,
            observation.OccurredAt,
            SituationTriggerKind.AvatarStateChanged,
            observation.IsAvatarUnderDirectPlayerControl,
            observation.IsConversationBusy || _activeTurnId != null,
            observation.HasPendingExecution
                || _activeExecutionIds.Count > 0,
            () =>
            {
                _avatarDirectControl =
                    observation.IsAvatarUnderDirectPlayerControl;
                _avatarConversationBusy =
                    observation.IsConversationBusy;
                _avatarPendingExecution =
                    observation.HasPendingExecution;
            });
    }

    internal OrchestratorObserveSourceEmission Observe(
        ExistingExecutionLifecycleObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        const OrchestratorObserveSourceKind source =
            OrchestratorObserveSourceKind.ExistingExecutionLifecycle;
        var blocked = Gate(source, observation.OccurredAt);
        if (blocked != null)
            return blocked;

        var isPending = observation.Status
            is ExistingExecutionLifecycleStatus.Queued
                or ExistingExecutionLifecycleStatus.Started;
        var alreadyTracked =
            _activeExecutionIds.Contains(observation.ExecutionId);
        if (isPending && alreadyTracked)
            return Coalesce(source, observation.OccurredAt);
        if (!isPending && !alreadyTracked)
        {
            return Reject(
                source,
                OrchestratorObserveEmissionReason.TransitionMismatch);
        }
        if (isPending
            && _activeExecutionIds.Count >= MaximumTrackedExecutions)
        {
            return Reject(
                source,
                OrchestratorObserveEmissionReason.SourceCapacityReached);
        }

        var nextExecutionCount =
            _activeExecutionIds.Count + (isPending ? 1 : -1);
        return EmitInteraction(
            source,
            observation.ExecutionId,
            observation.OccurredAt,
            SituationTriggerKind.ExecutionOutcome,
            _avatarDirectControl,
            _avatarConversationBusy || _activeTurnId != null,
            _avatarPendingExecution || nextExecutionCount > 0,
            () =>
            {
                if (isPending)
                    _activeExecutionIds.Add(observation.ExecutionId);
                else
                    _activeExecutionIds.Remove(observation.ExecutionId);
            });
    }

    internal OrchestratorShadowStep Stop(
        DateTimeOffset occurredAt,
        OrchestratorCancellationReason reason)
    {
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (!Enum.IsDefined(typeof(OrchestratorCancellationReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (_stopped)
            throw new InvalidOperationException("Observe coordinator is already stopped.");
        if (normalizedAt < _lastObservationAt)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));

        var sequence = _nextEnvelopeSequence++;
        _lastObservationAt = normalizedAt;
        _stopped = true;
        var envelope = new OrchestratorCancellationEnvelope(
            OrchestratorProtocol.CurrentVersion,
            EventId(sequence, "stop"),
            "observe-" + _sessionToken + "-stop",
            sequence,
            _sessionId,
            normalizedAt,
            OrchestratorSourceKind.LocalGameSurface,
            _owner,
            reason);
        return new OrchestratorShadowStep(envelope);
    }

    private OrchestratorObserveSourceEmission EmitInteraction(
        OrchestratorObserveSourceKind source,
        string observationId,
        DateTimeOffset occurredAt,
        SituationTriggerKind triggerKind,
        bool directControl,
        bool conversationBusy,
        bool pendingExecution,
        Action commitSourceState)
    {
        _lastObservationAt = occurredAt;
        if (directControl == _effectiveDirectControl
            && conversationBusy == _effectiveConversationBusy
            && pendingExecution == _effectivePendingExecution)
        {
            commitSourceState();
            return Coalesced(source);
        }

        var sourceState = SourceState(source);
        if (!sourceState.TryAdmit(occurredAt))
        {
            return new OrchestratorObserveSourceEmission(
                source,
                OrchestratorObserveEmissionDisposition.SourceFaulted,
                OrchestratorObserveEmissionReason.RateLimitExceeded,
                step: null);
        }

        commitSourceState();
        _effectiveDirectControl = directControl;
        _effectiveConversationBusy = conversationBusy;
        _effectivePendingExecution = pendingExecution;

        var envelopeSequence = _nextEnvelopeSequence++;
        var eventId = EventId(
            envelopeSequence,
            SourceToken(source));
        var situation = new InteractionStateChangedSituationEvent(
            eventId,
            _nextSituationSequence++,
            _sessionId,
            occurredAt,
            triggerKind,
            directControl,
            conversationBusy,
            pendingExecution);
        var envelope = new OrchestratorSituationEnvelope(
            OrchestratorProtocol.CurrentVersion,
            CorrelationId(source, observationId),
            envelopeSequence,
            OrchestratorSourceKind.LocalGameSurface,
            _owner,
            situation);
        var stamp = new OrchestratorEvaluationStamp(
            SnapshotId(envelopeSequence, source),
            TimeSpan.FromSeconds(5),
            occurredAt,
            new DecisionBudgets(
                maximumMemoryItems: 0,
                maximumContextCharacters: 0,
                maximumReasoningCalls: 0));
        return new OrchestratorObserveSourceEmission(
            source,
            OrchestratorObserveEmissionDisposition.Emitted,
            OrchestratorObserveEmissionReason.NormalizedTransition,
            new OrchestratorShadowStep(envelope, stamp));
    }

    private OrchestratorObserveSourceEmission? Gate(
        OrchestratorObserveSourceKind source,
        DateTimeOffset occurredAt)
    {
        var sourceState = SourceState(source);
        if (_stopped)
        {
            return Reject(
                source,
                OrchestratorObserveEmissionReason.CoordinatorStopped);
        }
        if (sourceState.Status
            == OrchestratorObserveSourceStatus.Disabled)
        {
            return Reject(
                source,
                OrchestratorObserveEmissionReason.SourceDisabled);
        }
        if (sourceState.Status
            == OrchestratorObserveSourceStatus.Faulted)
        {
            return new OrchestratorObserveSourceEmission(
                source,
                OrchestratorObserveEmissionDisposition.SourceFaulted,
                OrchestratorObserveEmissionReason.SourceAlreadyFaulted,
                step: null);
        }
        if (occurredAt < _lastObservationAt)
        {
            return Reject(
                source,
                OrchestratorObserveEmissionReason.TimestampReversal);
        }
        return null;
    }

    private OrchestratorObserveSourceEmission Coalesce(
        OrchestratorObserveSourceKind source,
        DateTimeOffset occurredAt)
    {
        _lastObservationAt = occurredAt;
        return Coalesced(source);
    }

    private static OrchestratorObserveSourceEmission Coalesced(
        OrchestratorObserveSourceKind source)
        => new(
            source,
            OrchestratorObserveEmissionDisposition.Coalesced,
            OrchestratorObserveEmissionReason.NormalizedStateUnchanged,
            step: null);

    private static OrchestratorObserveSourceEmission Reject(
        OrchestratorObserveSourceKind source,
        OrchestratorObserveEmissionReason reason)
        => new(
            source,
            OrchestratorObserveEmissionDisposition.Rejected,
            reason,
            step: null);

    private SourceRuntimeState SourceState(
        OrchestratorObserveSourceKind source)
    {
        if (!Enum.IsDefined(typeof(OrchestratorObserveSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        return _sources[(int)source];
    }

    private string EventId(long sequence, string suffix)
        => "observe-"
            + _sessionToken
            + "-"
            + sequence
            + "-"
            + suffix;

    private string CorrelationId(
        OrchestratorObserveSourceKind source,
        string observationId)
        => "observe-"
            + _sessionToken
            + "-"
            + SourceToken(source)
            + "-"
            + observationId;

    private Guid SnapshotId(
        long envelopeSequence,
        OrchestratorObserveSourceKind source)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(envelopeSequence).CopyTo(bytes, 0);
        BitConverter.GetBytes(
                OrchestratorObserveSourceContractGuard
                    .StableHash(_sessionId))
            .CopyTo(bytes, 8);
        bytes[15] ^= (byte)source;
        return new Guid(bytes);
    }

    private static string SourceToken(
        OrchestratorObserveSourceKind source)
        => source switch
        {
            OrchestratorObserveSourceKind.PlayerTurnLifecycle => "turn",
            OrchestratorObserveSourceKind.AvatarInteractionGates => "avatar",
            OrchestratorObserveSourceKind.ExistingExecutionLifecycle => "execution",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    private sealed class SourceRuntimeState
    {
        private readonly Queue<DateTimeOffset> _emissions = new();

        internal OrchestratorObserveSourceStatus Status { get; private set; }

        internal SourceRuntimeState(bool enabled)
        {
            Status = enabled
                ? OrchestratorObserveSourceStatus.Enabled
                : OrchestratorObserveSourceStatus.Disabled;
        }

        internal bool TryAdmit(DateTimeOffset occurredAt)
        {
            var earliest = occurredAt - EmissionWindow;
            while (_emissions.Count > 0
                   && _emissions.Peek() <= earliest)
            {
                _emissions.Dequeue();
            }
            if (_emissions.Count >= MaximumEmissionsPerSourceWindow)
            {
                Status = OrchestratorObserveSourceStatus.Faulted;
                return false;
            }
            _emissions.Enqueue(occurredAt);
            return true;
        }
    }
}

internal static class OrchestratorObserveSourceContractGuard
{
    internal static string OpaqueId(string? value, string parameterName)
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
                    "Observe identifiers must be opaque ASCII tokens.",
                    parameterName);
            }
        }
        return normalized;
    }

    internal static string StableToken(string value)
        => StableHash(value).ToString("x8");

    internal static uint StableHash(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return hash;
    }
}
