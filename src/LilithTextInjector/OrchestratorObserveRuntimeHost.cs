using System;
using System.Collections.Generic;
using System.Threading;

namespace LilithTextInjector;

internal readonly struct OrchestratorObserveRuntimeConfiguration
    : IEquatable<OrchestratorObserveRuntimeConfiguration>
{
    internal static OrchestratorObserveRuntimeConfiguration Off { get; } =
        new(
            enabled: false,
            playerTurnLifecycle: false,
            avatarInteractionGates: false,
            existingExecutionLifecycle: false);

    internal bool Enabled { get; }
    internal bool PlayerTurnLifecycle { get; }
    internal bool AvatarInteractionGates { get; }
    internal bool ExistingExecutionLifecycle { get; }

    internal OrchestratorObserveRuntimeConfiguration(
        bool enabled,
        bool playerTurnLifecycle,
        bool avatarInteractionGates,
        bool existingExecutionLifecycle)
    {
        Enabled = enabled;
        PlayerTurnLifecycle = playerTurnLifecycle;
        AvatarInteractionGates = avatarInteractionGates;
        ExistingExecutionLifecycle = existingExecutionLifecycle;
    }

    internal bool IsSourceEnabled(OrchestratorObserveSourceKind source)
        => Enabled
            && source switch
            {
                OrchestratorObserveSourceKind.PlayerTurnLifecycle =>
                    PlayerTurnLifecycle,
                OrchestratorObserveSourceKind.AvatarInteractionGates =>
                    AvatarInteractionGates,
                OrchestratorObserveSourceKind.ExistingExecutionLifecycle =>
                    ExistingExecutionLifecycle,
                _ => false
            };

    internal OrchestratorObserveSourcePermissions ToPermissions()
        => new(
            PlayerTurnLifecycle,
            AvatarInteractionGates,
            ExistingExecutionLifecycle);

    public bool Equals(OrchestratorObserveRuntimeConfiguration other)
        => Enabled == other.Enabled
            && PlayerTurnLifecycle == other.PlayerTurnLifecycle
            && AvatarInteractionGates == other.AvatarInteractionGates
            && ExistingExecutionLifecycle
                == other.ExistingExecutionLifecycle;

    public override bool Equals(object? obj)
        => obj is OrchestratorObserveRuntimeConfiguration other
            && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            Enabled,
            PlayerTurnLifecycle,
            AvatarInteractionGates,
            ExistingExecutionLifecycle);

    public static bool operator ==(
        OrchestratorObserveRuntimeConfiguration left,
        OrchestratorObserveRuntimeConfiguration right)
        => left.Equals(right);

    public static bool operator !=(
        OrchestratorObserveRuntimeConfiguration left,
        OrchestratorObserveRuntimeConfiguration right)
        => !left.Equals(right);
}

internal sealed class OrchestratorObserveRuntimeSourceSnapshot
{
    internal OrchestratorObserveSourceKind Source { get; }
    internal OrchestratorObserveSourceStatus Status { get; }
    internal long Accepted { get; }
    internal long Rejected { get; }
    internal long Coalesced { get; }
    internal long Dropped { get; }
    internal long Faulted { get; }

    internal OrchestratorObserveRuntimeSourceSnapshot(
        OrchestratorObserveSourceKind source,
        OrchestratorObserveSourceStatus status,
        long accepted,
        long rejected,
        long coalesced,
        long dropped,
        long faulted)
    {
        if (!Enum.IsDefined(typeof(OrchestratorObserveSourceKind), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(typeof(OrchestratorObserveSourceStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (accepted < 0
            || rejected < 0
            || coalesced < 0
            || dropped < 0
            || faulted < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted));
        }

        Source = source;
        Status = status;
        Accepted = accepted;
        Rejected = rejected;
        Coalesced = coalesced;
        Dropped = dropped;
        Faulted = faulted;
    }
}

internal sealed class OrchestratorObserveRuntimeSnapshot
{
    internal OrchestratorObserveHostStatus Status { get; }
    internal IReadOnlyList<OrchestratorObserveRuntimeSourceSnapshot> Sources {
        get;
    }
    internal OrchestratorObserveDiagnosticBuffer Diagnostics { get; }
    internal int RetainedSessionEventCount { get; }
    internal int PendingInvocationCount { get; }
    internal int ObserveCausedProviderCallCount => 0;
    internal int ObserveCausedStateWriteCount => 0;
    internal int ObserveCausedBehaviorCallCount => 0;

    internal OrchestratorObserveRuntimeSnapshot(
        OrchestratorObserveHostStatus status,
        IReadOnlyList<OrchestratorObserveRuntimeSourceSnapshot> sources,
        OrchestratorObserveDiagnosticBuffer diagnostics,
        int retainedSessionEventCount,
        int pendingInvocationCount)
    {
        if (!Enum.IsDefined(typeof(OrchestratorObserveHostStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));
        if (sources.Count != 3)
            throw new ArgumentOutOfRangeException(nameof(sources));
        if (retainedSessionEventCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(retainedSessionEventCount));
        if (pendingInvocationCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(pendingInvocationCount));

        var copy =
            new OrchestratorObserveRuntimeSourceSnapshot[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            copy[index] = sources[index]
                ?? throw new ArgumentException(
                    "Source snapshots cannot contain null.",
                    nameof(sources));
        }

        Status = status;
        Sources = Array.AsReadOnly(copy);
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        RetainedSessionEventCount = retainedSessionEventCount;
        PendingInvocationCount = pendingInvocationCount;
    }
}

internal sealed class OrchestratorObserveRuntimeHost
{
    private readonly OrchestratorSessionOwner _owner =
        new("local-owner", "local-game");
    private readonly MutableSourceCounters[] _counters =
    {
        new(),
        new(),
        new()
    };

    private OrchestratorObserveRuntimeConfiguration _configuration =
        OrchestratorObserveRuntimeConfiguration.Off;
    private OrchestratorObserveSourceCoordinator? _coordinator;
    private OrchestratorControlPlaneState _state =
        OrchestratorControlPlaneState.Empty;
    private OrchestratorObserveDiagnosticBuffer _diagnostics =
        OrchestratorObserveDiagnosticBuffer.Empty;
    private OrchestratorObserveHostStatus _status =
        OrchestratorObserveHostStatus.Off;
    private int _mainThreadId;
    private long _sessionGeneration;
    private long _turnGeneration;
    private long _avatarGeneration;
    private string? _activeTurnId;
    private PlayerTurnInputSurface _activeTurnSurface;
    private bool _hasAvatarSample;
    private bool _lastDirectControl;
    private bool _lastConversationBusy;
    private bool _lastPendingExecution;

    internal OrchestratorObserveHostStatus Status => _status;

    internal void Reconcile(
        OrchestratorObserveRuntimeConfiguration configuration,
        DateTimeOffset occurredAt)
    {
        RequireMainThread();
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (configuration == _configuration)
            return;

        if (_coordinator != null)
        {
            StopActive(
                normalizedAt,
                configuration.Enabled
                    ? OrchestratorCancellationReason.SessionSuperseded
                    : OrchestratorCancellationReason.PlayerRequested);
        }

        _configuration = configuration;
        ResetSourceProjectionState();
        if (!configuration.Enabled)
        {
            ClearEphemeral();
            _status = OrchestratorObserveHostStatus.Off;
            return;
        }

        StartSession(normalizedAt);
    }

    internal void BeginPlayerTurn(
        PlayerTurnInputSurface surface,
        DateTimeOffset occurredAt)
    {
        RequireMainThread();
        if (!CanObserve(OrchestratorObserveSourceKind.PlayerTurnLifecycle))
            return;
        if (!Enum.IsDefined(typeof(PlayerTurnInputSurface), surface))
            throw new ArgumentOutOfRangeException(nameof(surface));

        var turnId = "turn-" + (++_turnGeneration).ToString("x");
        var observation = new PlayerTurnLifecycleObservation(
            turnId,
            surface,
            PlayerTurnLifecycleStatus.Accepted,
            occurredAt);
        if (HandleEmission(
                _coordinator!.Observe(observation),
                observation.OccurredAt))
        {
            _activeTurnId = turnId;
            _activeTurnSurface = surface;
        }
    }

    internal void EndPlayerTurn(
        PlayerTurnLifecycleStatus status,
        DateTimeOffset occurredAt)
    {
        RequireMainThread();
        if (!CanObserve(OrchestratorObserveSourceKind.PlayerTurnLifecycle)
            || _activeTurnId == null)
        {
            return;
        }
        if (status == PlayerTurnLifecycleStatus.Accepted
            || !Enum.IsDefined(typeof(PlayerTurnLifecycleStatus), status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var observation = new PlayerTurnLifecycleObservation(
            _activeTurnId,
            _activeTurnSurface,
            status,
            occurredAt);
        if (HandleEmission(
                _coordinator!.Observe(observation),
                observation.OccurredAt))
            _activeTurnId = null;
    }

    internal void ObserveAvatarInteractionGates(
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        DateTimeOffset occurredAt)
    {
        RequireMainThread();
        if (!CanObserve(
                OrchestratorObserveSourceKind.AvatarInteractionGates))
        {
            return;
        }

        if (!_hasAvatarSample)
        {
            _hasAvatarSample = true;
            _lastDirectControl = isAvatarUnderDirectPlayerControl;
            _lastConversationBusy = isConversationBusy;
            _lastPendingExecution = hasPendingExecution;
            if (!isAvatarUnderDirectPlayerControl
                && !isConversationBusy
                && !hasPendingExecution)
            {
                return;
            }
        }
        else if (_lastDirectControl
            == isAvatarUnderDirectPlayerControl
            && _lastConversationBusy == isConversationBusy
            && _lastPendingExecution == hasPendingExecution)
        {
            return;
        }

        var observation = new AvatarInteractionGatesObservation(
            "avatar-" + (++_avatarGeneration).ToString("x"),
            isAvatarUnderDirectPlayerControl,
            isConversationBusy,
            hasPendingExecution,
            occurredAt);
        if (!HandleEmission(
                _coordinator!.Observe(observation),
                observation.OccurredAt))
            return;

        _lastDirectControl = isAvatarUnderDirectPlayerControl;
        _lastConversationBusy = isConversationBusy;
        _lastPendingExecution = hasPendingExecution;
    }

    internal void ObserveExistingExecution(
        Guid executionId,
        ExistingExecutionLifecycleStatus status,
        bool hasDirectCorrelation,
        bool isConfounded,
        DateTimeOffset occurredAt)
    {
        RequireMainThread();
        if (!CanObserve(
                OrchestratorObserveSourceKind.ExistingExecutionLifecycle))
        {
            return;
        }
        if (executionId == Guid.Empty)
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));

        var observation = new ExistingExecutionLifecycleObservation(
            executionId.ToString("N"),
            status,
            occurredAt,
            ExistingExecutionCategory.AvatarMotion,
            hasDirectCorrelation,
            isConfounded);
        HandleEmission(
            _coordinator!.Observe(observation),
            observation.OccurredAt);
    }

    internal void StopByFault(DateTimeOffset occurredAt)
    {
        RequireMainThread();
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        StopActive(
            normalizedAt,
            OrchestratorCancellationReason.SafetyStop);
        _coordinator = null;
        _activeTurnId = null;
        _status = OrchestratorObserveHostStatus.StoppedByFault;
    }

    internal void Shutdown(DateTimeOffset occurredAt)
    {
        RequireMainThread();
        var normalizedAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        StopActive(
            normalizedAt,
            OrchestratorCancellationReason.Shutdown);
        _configuration = OrchestratorObserveRuntimeConfiguration.Off;
        ClearEphemeral();
        _status = OrchestratorObserveHostStatus.Off;
    }

    internal OrchestratorObserveRuntimeSnapshot GetSnapshot()
    {
        var sources =
            new OrchestratorObserveRuntimeSourceSnapshot[_counters.Length];
        for (var index = 0; index < sources.Length; index++)
        {
            var source = (OrchestratorObserveSourceKind)index;
            var counters = _counters[index];
            sources[index] = new OrchestratorObserveRuntimeSourceSnapshot(
                source,
                ResolveSourceStatus(source),
                counters.Accepted,
                counters.Rejected,
                counters.Coalesced,
                counters.Dropped,
                counters.Faulted);
        }

        var retainedEvents = 0;
        var pendingInvocations = 0;
        foreach (var session in _state.Sessions)
        {
            retainedEvents += session.ProcessedEventIds.Count;
            foreach (var invocation in session.Invocations)
            {
                if (invocation.Status == OrchestratorInvocationStatus.Pending)
                    pendingInvocations++;
            }
        }

        return new OrchestratorObserveRuntimeSnapshot(
            _status,
            Array.AsReadOnly(sources),
            _diagnostics,
            retainedEvents,
            pendingInvocations);
    }

    private void StartSession(DateTimeOffset occurredAt)
    {
        _state = OrchestratorControlPlaneState.Empty;
        _coordinator = new OrchestratorObserveSourceCoordinator(
            "runtime-observe-" + (++_sessionGeneration).ToString("x"),
            _owner,
            occurredAt,
            _configuration.ToPermissions());
        var opened = UnifiedOrchestratorShadow.Apply(
            _state,
            _coordinator.SessionOpenedStep);
        if (!opened.Result.Accepted)
        {
            _coordinator = null;
            _status = OrchestratorObserveHostStatus.StoppedByFault;
            return;
        }

        _state = opened.State;
        _status = OrchestratorObserveHostStatus.Observe;
    }

    private bool HandleEmission(
        OrchestratorObserveSourceEmission emission,
        DateTimeOffset occurredAt)
    {
        var counters = _counters[(int)emission.Source];
        switch (emission.Disposition)
        {
            case OrchestratorObserveEmissionDisposition.Coalesced:
                counters.Coalesced++;
                AppendDiagnostic(
                    emission.Source,
                    OrchestratorObserveDiagnosticKind.EventCoalesced,
                    emission.Reason,
                    occurredAt,
                    step: null,
                    result: null);
                return true;
            case OrchestratorObserveEmissionDisposition.Rejected:
                counters.Rejected++;
                AppendDiagnostic(
                    emission.Source,
                    OrchestratorObserveDiagnosticKind.EventRejected,
                    emission.Reason,
                    occurredAt,
                    step: null,
                    result: null);
                return false;
            case OrchestratorObserveEmissionDisposition.SourceFaulted:
                counters.Faulted++;
                AppendDiagnostic(
                    emission.Source,
                    OrchestratorObserveDiagnosticKind.SourceFaulted,
                    emission.Reason,
                    occurredAt,
                    step: null,
                    result: null);
                return false;
            case OrchestratorObserveEmissionDisposition.Emitted:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(emission));
        }

        var transition = UnifiedOrchestratorShadow.Apply(
            _state,
            emission.Step!);
        if (!transition.Result.Accepted)
        {
            counters.Rejected++;
            AppendDiagnostic(
                emission.Source,
                OrchestratorObserveDiagnosticKind.EventRejected,
                emission.Reason,
                occurredAt,
                emission.Step,
                transition.Result);
            _coordinator = null;
            _activeTurnId = null;
            _status = transition.Result.Reason
                    == OrchestratorProcessReason.EventLimitReached
                ? OrchestratorObserveHostStatus.CapacityReached
                : OrchestratorObserveHostStatus.StoppedByFault;
            return false;
        }
        if (transition.Result.Invocation != null)
        {
            counters.Dropped++;
            AppendDiagnostic(
                emission.Source,
                OrchestratorObserveDiagnosticKind.InvocationSuppressed,
                emission.Reason,
                occurredAt,
                emission.Step,
                transition.Result);
            _coordinator = null;
            _activeTurnId = null;
            _status = OrchestratorObserveHostStatus.StoppedByFault;
            return false;
        }

        _state = transition.State;
        counters.Accepted++;
        AppendDiagnostic(
            emission.Source,
            OrchestratorObserveDiagnosticKind.EventAccepted,
            emission.Reason,
            occurredAt,
            emission.Step,
            transition.Result);
        return true;
    }

    private void AppendDiagnostic(
        OrchestratorObserveSourceKind source,
        OrchestratorObserveDiagnosticKind kind,
        OrchestratorObserveEmissionReason emissionReason,
        DateTimeOffset occurredAt,
        OrchestratorShadowStep? step,
        OrchestratorProcessResult? result)
    {
        var correlation = step?.Envelope.CorrelationId
            ?? "source-"
                + ((int)source).ToString()
                + "-"
                + ((int)emissionReason).ToString();
        var suffix = correlation.Length <= 16
            ? correlation
            : correlation.Substring(correlation.Length - 16);
        _diagnostics = _diagnostics.Append(
            new OrchestratorObserveDiagnosticEntry(
                step?.Envelope.OccurredAt ?? occurredAt,
                source,
                kind,
                step?.Envelope.Kind,
                result?.Disposition,
                result?.Reason,
                result?.Evaluation?.Outcome.Disposition,
                result?.Evaluation?.Outcome.Reason,
                suffix));
    }

    private bool CanObserve(OrchestratorObserveSourceKind source)
        => _status == OrchestratorObserveHostStatus.Observe
            && _coordinator != null
            && _configuration.IsSourceEnabled(source)
            && _coordinator.GetSourceStatus(source)
                == OrchestratorObserveSourceStatus.Enabled;

    private OrchestratorObserveSourceStatus ResolveSourceStatus(
        OrchestratorObserveSourceKind source)
    {
        if (!_configuration.IsSourceEnabled(source))
            return OrchestratorObserveSourceStatus.Disabled;
        if (_coordinator != null
            && _status == OrchestratorObserveHostStatus.Observe)
        {
            return _coordinator.GetSourceStatus(source);
        }
        return OrchestratorObserveSourceStatus.Faulted;
    }

    private void StopActive(
        DateTimeOffset occurredAt,
        OrchestratorCancellationReason reason)
    {
        var coordinator = _coordinator;
        if (coordinator == null)
            return;
        try
        {
            var stopped = UnifiedOrchestratorShadow.Apply(
                _state,
                coordinator.Stop(occurredAt, reason));
            if (stopped.Result.Accepted)
                _state = stopped.State;
        }
        catch
        {
        }
        finally
        {
            _coordinator = null;
            _activeTurnId = null;
        }
    }

    private void ClearEphemeral()
    {
        _coordinator = null;
        _state = OrchestratorControlPlaneState.Empty;
        _diagnostics = OrchestratorObserveDiagnosticBuffer.Empty;
        foreach (var counters in _counters)
            counters.Clear();
        _sessionGeneration = 0;
        _turnGeneration = 0;
        _avatarGeneration = 0;
        ResetSourceProjectionState();
    }

    private void ResetSourceProjectionState()
    {
        _activeTurnId = null;
        _activeTurnSurface = PlayerTurnInputSurface.TextEntry;
        _hasAvatarSample = false;
        _lastDirectControl = false;
        _lastConversationBusy = false;
        _lastPendingExecution = false;
    }

    private void RequireMainThread()
    {
        var current = Environment.CurrentManagedThreadId;
        if (_mainThreadId == 0)
        {
            Interlocked.CompareExchange(
                ref _mainThreadId,
                current,
                comparand: 0);
        }
        if (_mainThreadId != current)
        {
            throw new InvalidOperationException(
                "Observe runtime host is confined to its Unity main thread.");
        }
    }

    private sealed class MutableSourceCounters
    {
        internal long Accepted;
        internal long Rejected;
        internal long Coalesced;
        internal long Dropped;
        internal long Faulted;

        internal void Clear()
        {
            Accepted = 0;
            Rejected = 0;
            Coalesced = 0;
            Dropped = 0;
            Faulted = 0;
        }
    }
}
