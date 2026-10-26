using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace LilithTextInjector;

internal sealed class PlayerTurnOperationRuntimeHost
{
    private sealed class CompletionReceiptIssuanceCapability
    {
        internal CompletionReceiptIssuanceCapability()
        {
        }
    }

    internal const int MaximumRecentTerminals = 32;
    internal const int MaximumProviderCallsPerOperation = 16;

    private static readonly object
        CompletionReceiptCapability =
            new CompletionReceiptIssuanceCapability();

    internal static bool AuthorizesCompletionReceiptIssuance(
        object? capability)
        => ReferenceEquals(capability, CompletionReceiptCapability);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _processEpochId;
    private readonly Queue<PlayerTurnOperationSnapshot> _recentTerminals = new();
    private MutableOperation? _active;
    private DateTimeOffset? _lastObservedAt;
    private long _operationGeneration;
    private long _began;
    private long _completed;
    private long _failed;
    private long _cancelled;
    private long _rejectedTransitions;

    internal PlayerTurnOperationRuntimeHost(
        Func<DateTimeOffset>? clock = null,
        string? processEpochId = null)
    {
        _clock = clock ?? CreateProcessAnchoredClock();
        _processEpochId = PlayerTurnOperationGuard.Metadata(
            processEpochId
                ?? "player-turn-epoch-" + Guid.NewGuid().ToString("N"),
            nameof(processEpochId));
    }

    internal PlayerTurnBeginResult TryBegin(
        PlayerTurnInputSource inputSource,
        long runtimeGeneration)
    {
        if (!Enum.IsDefined(typeof(PlayerTurnInputSource), inputSource)
            || inputSource == PlayerTurnInputSource.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSource));
        }
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));

        lock (_gate)
        {
            if (_active != null)
                return RejectedBegin(PlayerTurnBeginAdmission.ActiveOperation);
            if (_operationGeneration == long.MaxValue)
            {
                return RejectedBegin(
                    PlayerTurnBeginAdmission.GenerationExhausted);
            }
            if (!TryNow(out var now))
                return RejectedBegin(PlayerTurnBeginAdmission.TimestampReversal);
            var generation = ++_operationGeneration;
            var handle = new PlayerTurnOperationHandle(
                "player-turn-" + generation.ToString("x") + "-"
                    + _processEpochId.Substring(
                        Math.Max(0, _processEpochId.Length - 12)),
                generation,
                _processEpochId,
                runtimeGeneration,
                inputSource);
            _active = new MutableOperation(handle, now);
            _began++;
            return new PlayerTurnBeginResult(
                PlayerTurnBeginAdmission.Accepted,
                handle);
        }
    }

    internal bool TryAdmit(
        PlayerTurnOperationHandle handle,
        string routeId)
    {
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Phase != PlayerTurnOperationPhase.Began
                || !TryNow(out var now))
            {
                return Reject();
            }
            active.ProviderRouteId = PlayerTurnOperationGuard.Metadata(
                routeId,
                nameof(routeId));
            active.AdmittedAt = now;
            active.Phase = PlayerTurnOperationPhase.Admitted;
            return true;
        }
    }

    internal bool TryProviderStarted(
        PlayerTurnOperationHandle handle,
        string providerRouteId,
        string providerRequestId)
    {
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Phase is not PlayerTurnOperationPhase.Admitted
                    and not PlayerTurnOperationPhase.ProviderCompleted
                || active.ProviderCallCount
                    >= MaximumProviderCallsPerOperation
                || !TryNow(out var now))
            {
                return Reject();
            }
            var route = PlayerTurnOperationGuard.Metadata(
                providerRouteId,
                nameof(providerRouteId));
            if (!string.Equals(
                    active.ProviderRouteId,
                    route,
                    StringComparison.Ordinal))
            {
                return Reject();
            }
            var requestId = PlayerTurnOperationGuard.Metadata(
                providerRequestId,
                nameof(providerRequestId));
            if (!active.SeenProviderRequestIds.Add(requestId))
                return Reject();
            active.ProviderRequestId = requestId;
            active.ProviderStartedAt = now;
            active.ProviderCallCount++;
            active.Phase = PlayerTurnOperationPhase.ProviderStarted;
            return true;
        }
    }

    internal bool TryProviderCompleted(
        PlayerTurnOperationHandle handle,
        string providerRequestId)
    {
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Phase != PlayerTurnOperationPhase.ProviderStarted
                || !string.Equals(
                    active.ProviderRequestId,
                    PlayerTurnOperationGuard.Metadata(
                        providerRequestId,
                        nameof(providerRequestId)),
                    StringComparison.Ordinal)
                || !TryNow(out var now))
            {
                return Reject();
            }
            active.ProviderCompletedAt = now;
            active.ProviderCompletionCount++;
            active.Phase = PlayerTurnOperationPhase.ProviderCompleted;
            return true;
        }
    }

    internal bool TryProviderCompletedWithReceipt(
        PlayerTurnOperationHandle handle,
        string providerRequestId,
        string responseFingerprint,
        out PlayerTurnProviderCompletionReceipt? receipt)
    {
        receipt = null;
        var normalizedResponseFingerprint =
            PlayerTurnOperationGuard.Fingerprint(
                responseFingerprint,
                nameof(responseFingerprint));
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Phase != PlayerTurnOperationPhase.ProviderStarted
                || !string.Equals(
                    active.ProviderRequestId,
                    PlayerTurnOperationGuard.Metadata(
                        providerRequestId,
                        nameof(providerRequestId)),
                    StringComparison.Ordinal)
                || !TryNow(out var now))
            {
                return Reject();
            }
            active.ProviderCompletedAt = now;
            active.ProviderCompletionCount++;
            active.Phase = PlayerTurnOperationPhase.ProviderCompleted;
            receipt = PlayerTurnProviderCompletionReceipt.Issue(
                CompletionReceiptCapability,
                active.Handle,
                active.ProviderRouteId,
                active.ProviderRequestId,
                normalizedResponseFingerprint,
                active.ProviderCallCount,
                active.ProviderCompletionCount,
                now);
            return true;
        }
    }

    internal bool TryPublish(
        PlayerTurnOperationHandle handle,
        PlayerTurnPublicationKind kind,
        long currentRuntimeGeneration)
    {
        ValidatePublicationInput(kind, currentRuntimeGeneration);
        lock (_gate)
        {
            if (EvaluatePublicationLocked(
                    handle,
                    kind,
                    currentRuntimeGeneration)
                    != PlayerTurnPublicationAdmission.Admitted
                || !TryNow(out var now))
            {
                return Reject();
            }
            var active = _active!;
            active!.PublishedAt = now;
            active.PublicationKind = kind;
            active.Phase = PlayerTurnOperationPhase.Published;
            return true;
        }
    }

    internal bool CanPublish(
        PlayerTurnOperationHandle handle,
        PlayerTurnPublicationKind kind,
        long currentRuntimeGeneration)
        => EvaluatePublication(
                handle,
                kind,
                currentRuntimeGeneration)
            == PlayerTurnPublicationAdmission.Admitted;

    internal PlayerTurnPublicationAdmission EvaluatePublication(
        PlayerTurnOperationHandle handle,
        PlayerTurnPublicationKind kind,
        long currentRuntimeGeneration)
    {
        ValidatePublicationInput(kind, currentRuntimeGeneration);
        lock (_gate)
        {
            return EvaluatePublicationLocked(
                handle,
                kind,
                currentRuntimeGeneration);
        }
    }

    internal bool TryCancelForRuntimeGenerationChange(
        PlayerTurnOperationHandle handle,
        long currentRuntimeGeneration)
    {
        if (currentRuntimeGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRuntimeGeneration));
        }
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Handle.RuntimeGeneration
                    == currentRuntimeGeneration)
            {
                return Reject();
            }
            return Terminal(
                active,
                _lastObservedAt ?? active.BeganAt,
                PlayerTurnTerminalDisposition.Cancelled,
                PlayerTurnFailureReason.None,
                PlayerTurnCancellationReason.RuntimeGenerationChanged);
        }
    }

    internal bool TryComplete(PlayerTurnOperationHandle handle)
    {
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || active!.Phase != PlayerTurnOperationPhase.Published
                || active.PublicationKind
                    != PlayerTurnPublicationKind.AssistantReply
                || (active.ProviderStartedAt.HasValue
                    && !active.ProviderCompletedAt.HasValue)
                || !TryNow(out var now))
            {
                return Reject();
            }
            return Terminal(
                active,
                now,
                PlayerTurnTerminalDisposition.Completed,
                PlayerTurnFailureReason.None,
                PlayerTurnCancellationReason.None);
        }
    }

    internal bool TryFail(
        PlayerTurnOperationHandle handle,
        PlayerTurnFailureReason reason)
    {
        if (!Enum.IsDefined(typeof(PlayerTurnFailureReason), reason)
            || reason == PlayerTurnFailureReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || !TryNow(out var now))
            {
                return Reject();
            }
            return Terminal(
                active!,
                now,
                PlayerTurnTerminalDisposition.Failed,
                reason,
                PlayerTurnCancellationReason.None);
        }
    }

    internal bool TryCancel(
        PlayerTurnOperationHandle handle,
        PlayerTurnCancellationReason reason)
    {
        if (!Enum.IsDefined(typeof(PlayerTurnCancellationReason), reason)
            || reason == PlayerTurnCancellationReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        lock (_gate)
        {
            if (!TryActive(handle, out var active)
                || !TryNow(out var now))
            {
                return Reject();
            }
            return Terminal(
                active!,
                now,
                PlayerTurnTerminalDisposition.Cancelled,
                PlayerTurnFailureReason.None,
                reason);
        }
    }

    internal bool IsActive(PlayerTurnOperationHandle handle)
    {
        lock (_gate)
            return TryActive(handle, out _);
    }

    /// <summary>
    /// Defines the exact host-side dispatch boundary for a local effect.
    /// Cancellation and this admission are serialized by the same gate: if
    /// cancellation wins, the effect must not start; if admission wins, a
    /// later cancellation is truthfully treated as occurring after dispatch.
    /// </summary>
    internal bool TryAdmitEffectDispatch(PlayerTurnOperationHandle handle)
    {
        lock (_gate)
            return TryActive(handle, out _);
    }

    internal CancellationToken GetCancellationToken(
        PlayerTurnOperationHandle handle)
    {
        lock (_gate)
        {
            return TryActive(handle, out var active)
                ? active!.Cancellation.Token
                : new CancellationToken(canceled: true);
        }
    }

    internal PlayerTurnOperationRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new PlayerTurnOperationRuntimeSnapshot(
                _processEpochId,
                _active?.Snapshot(),
                _recentTerminals,
                _began,
                _completed,
                _failed,
                _cancelled,
                _rejectedTransitions);
        }
    }

    private static Func<DateTimeOffset> CreateProcessAnchoredClock()
    {
        var wallClockAnchor = DateTimeOffset.UtcNow;
        var monotonicAnchor = Stopwatch.GetTimestamp();
        return () => wallClockAnchor.AddSeconds(
            (Stopwatch.GetTimestamp() - monotonicAnchor)
                / (double)Stopwatch.Frequency);
    }

    private PlayerTurnBeginResult RejectedBegin(
        PlayerTurnBeginAdmission admission)
    {
        _rejectedTransitions++;
        return new PlayerTurnBeginResult(admission, null);
    }

    private bool TryActive(
        PlayerTurnOperationHandle? handle,
        out MutableOperation? active)
    {
        active = _active;
        return handle != null
            && active != null
            && active.Handle.Matches(handle);
    }

    private static void ValidatePublicationInput(
        PlayerTurnPublicationKind kind,
        long currentRuntimeGeneration)
    {
        if (!Enum.IsDefined(typeof(PlayerTurnPublicationKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (currentRuntimeGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRuntimeGeneration));
        }
    }

    private PlayerTurnPublicationAdmission EvaluatePublicationLocked(
        PlayerTurnOperationHandle? handle,
        PlayerTurnPublicationKind kind,
        long currentRuntimeGeneration)
    {
        if (_active == null)
            return PlayerTurnPublicationAdmission.NoActiveOperation;
        if (!TryActive(handle, out var active))
            return PlayerTurnPublicationAdmission.StaleOperation;
        if (active!.Handle.RuntimeGeneration != currentRuntimeGeneration)
        {
            return PlayerTurnPublicationAdmission
                .RuntimeGenerationChanged;
        }
        return CanPublishPhase(active, kind)
            ? PlayerTurnPublicationAdmission.Admitted
            : PlayerTurnPublicationAdmission.PhaseRejected;
    }

    private bool TryNow(out DateTimeOffset now)
    {
        now = _clock();
        if (now == default
            || (_lastObservedAt.HasValue && now < _lastObservedAt.Value))
        {
            return false;
        }
        _lastObservedAt = now;
        return true;
    }

    private bool Reject()
    {
        _rejectedTransitions++;
        return false;
    }

    private bool Terminal(
        MutableOperation active,
        DateTimeOffset now,
        PlayerTurnTerminalDisposition disposition,
        PlayerTurnFailureReason failureReason,
        PlayerTurnCancellationReason cancellationReason)
    {
        active.TerminalAt = now;
        active.TerminalDisposition = disposition;
        active.FailureReason = failureReason;
        active.CancellationReason = cancellationReason;
        active.Phase = PlayerTurnOperationPhase.Terminal;
        try
        {
            active.Cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // Consumer callbacks cannot undo the committed terminal state.
        }
        var snapshot = active.Snapshot();
        _recentTerminals.Enqueue(snapshot);
        while (_recentTerminals.Count > MaximumRecentTerminals)
            _recentTerminals.Dequeue();
        _active = null;
        switch (disposition)
        {
            case PlayerTurnTerminalDisposition.Completed:
                _completed++;
                break;
            case PlayerTurnTerminalDisposition.Failed:
                _failed++;
                break;
            case PlayerTurnTerminalDisposition.Cancelled:
                _cancelled++;
                break;
        }
        return true;
    }

    private static bool CanPublishPhase(
        MutableOperation active,
        PlayerTurnPublicationKind kind)
    {
        if (kind == PlayerTurnPublicationKind.FailureNotice)
        {
            return active.Phase is PlayerTurnOperationPhase.Began
                or PlayerTurnOperationPhase.Admitted
                or PlayerTurnOperationPhase.ProviderCompleted;
        }
        return active.Phase == PlayerTurnOperationPhase.Admitted
            && !active.ProviderStartedAt.HasValue
            || active.Phase == PlayerTurnOperationPhase.ProviderCompleted;
    }

    private sealed class MutableOperation
    {
        internal PlayerTurnOperationHandle Handle { get; }
        internal PlayerTurnOperationPhase Phase { get; set; }
        internal DateTimeOffset BeganAt { get; }
        internal DateTimeOffset? AdmittedAt { get; set; }
        internal DateTimeOffset? ProviderStartedAt { get; set; }
        internal DateTimeOffset? ProviderCompletedAt { get; set; }
        internal DateTimeOffset? PublishedAt { get; set; }
        internal DateTimeOffset? TerminalAt { get; set; }
        internal string ProviderRouteId { get; set; } = string.Empty;
        internal string ProviderRequestId { get; set; } = string.Empty;
        internal PlayerTurnPublicationKind? PublicationKind { get; set; }
        internal PlayerTurnTerminalDisposition TerminalDisposition { get; set; }
        internal PlayerTurnFailureReason FailureReason { get; set; }
        internal PlayerTurnCancellationReason CancellationReason { get; set; }
        internal int ProviderCallCount { get; set; }
        internal int ProviderCompletionCount { get; set; }
        internal HashSet<string> SeenProviderRequestIds { get; } =
            new(StringComparer.Ordinal);
        internal CancellationTokenSource Cancellation { get; } = new();

        internal MutableOperation(
            PlayerTurnOperationHandle handle,
            DateTimeOffset beganAt)
        {
            Handle = handle;
            BeganAt = beganAt;
            Phase = PlayerTurnOperationPhase.Began;
        }

        internal PlayerTurnOperationSnapshot Snapshot()
            => new(
                Handle,
                Phase,
                BeganAt,
                AdmittedAt,
                ProviderStartedAt,
                ProviderCompletedAt,
                PublishedAt,
                TerminalAt,
                ProviderRouteId,
                ProviderRequestId,
                PublicationKind,
                TerminalDisposition,
                FailureReason,
                CancellationReason,
                ProviderCallCount,
                ProviderCompletionCount);
    }
}
