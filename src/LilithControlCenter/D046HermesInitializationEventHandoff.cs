using System.IO;

namespace LilithControlCenter;

internal enum D046HermesInitializationEventKind
{
    Projection,
    Voice
}

internal enum D046HermesInitializationEnqueueDisposition
{
    Buffered,
    ScheduleDrain,
    Closed
}

internal sealed record D046HermesSnapshotHandoffEpoch(
    long Epoch,
    bool HadBaseline);

internal sealed record D046HermesSnapshotHandoffCallbacks(
    Func<D046HermesSnapshotHandoffEpoch> Begin,
    Action<D046HermesSnapshotHandoffEpoch> Commit,
    Action<D046HermesSnapshotHandoffEpoch, Exception> Abort,
    Action<D046HermesSnapshotHandoffEpoch, Exception> Fail);

internal sealed record D046HermesInitializationEvent(
    long ArrivalOrdinal,
    D046HermesInitializationEventKind Kind,
    D046HermesProjectionEvent? Projection,
    D046HermesVoiceEvent? Voice)
{
    internal static D046HermesInitializationEvent FromProjection(
        long arrivalOrdinal,
        D046HermesProjectionEvent value)
        => new(
            arrivalOrdinal,
            D046HermesInitializationEventKind.Projection,
            value,
            Voice: null);

    internal static D046HermesInitializationEvent FromVoice(
        long arrivalOrdinal,
        D046HermesVoiceEvent value)
        => new(
            arrivalOrdinal,
            D046HermesInitializationEventKind.Voice,
            Projection: null,
            value);
}

internal static class D046HermesSnapshotCompanionRefreshPolicy
{
    internal sealed record AuthorityState(
        bool IsConnected,
        bool NativeSurfaceAttested,
        bool ActiveSessionConfigStale,
        bool SequenceGapDetected,
        bool ProjectionFault,
        bool RollbackQuarantined,
        bool DelegationQuarantined,
        D046HermesVoiceCaptureState VoiceCaptureState,
        bool HasPendingApproval,
        D046HermesTaskState? SelectedTaskState,
        bool CredentialRecoveryAvailable = false);

    internal sealed record Operations(
        Func<Task> RefreshDelegation,
        Func<Task> LoadModelOptions,
        Func<Task> RefreshNativeTools,
        Func<Task> SynchronizeVoice);

    internal static bool IsAuthorized(AuthorityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.IsConnected
            && state.NativeSurfaceAttested
            && !state.ActiveSessionConfigStale
            && !state.SequenceGapDetected
            && !state.ProjectionFault
            && !state.RollbackQuarantined
            && !state.DelegationQuarantined
            && state.VoiceCaptureState != D046HermesVoiceCaptureState.Quarantined
            && !state.HasPendingApproval
            && state.SelectedTaskState.HasValue
            && state.SelectedTaskState != D046HermesTaskState.WaitingForApproval;
    }

    internal static async Task<bool> RunFullAsync(
        AuthorityState state,
        Operations operations,
        bool refreshNativeTools = true)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ValidateOperations(operations);
        if (IsModelRecoveryAuthorized(state))
        {
            await operations.LoadModelOptions();
            return true;
        }
        if (!IsAuthorized(state))
            return false;

        await operations.RefreshDelegation();
        await operations.LoadModelOptions();
        if (refreshNativeTools)
            await operations.RefreshNativeTools();
        await operations.SynchronizeVoice();
        return true;
    }

    internal static bool IsModelRecoveryAuthorized(AuthorityState state)
        => state.CredentialRecoveryAvailable
            && !state.NativeSurfaceAttested
            && state.SelectedTaskState == D046HermesTaskState.Failed
            && state.VoiceCaptureState is D046HermesVoiceCaptureState.Off
                or D046HermesVoiceCaptureState.Idle
            && IsAuthorized(state with { NativeSurfaceAttested = true });

    internal static async Task<bool> RunCompressionAsync(
        AuthorityState state,
        Operations operations,
        bool refreshNativeTools = true)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ValidateOperations(operations);
        if (!IsAuthorized(state))
            return false;

        await operations.LoadModelOptions();
        if (refreshNativeTools)
            await operations.RefreshNativeTools();
        return true;
    }

    private static void ValidateOperations(Operations operations)
    {
        ArgumentNullException.ThrowIfNull(operations.RefreshDelegation);
        ArgumentNullException.ThrowIfNull(operations.LoadModelOptions);
        ArgumentNullException.ThrowIfNull(operations.RefreshNativeTools);
        ArgumentNullException.ThrowIfNull(operations.SynchronizeVoice);
    }
}

internal sealed class D046HermesInitializationEventHandoff
{
    internal const int MaximumPendingEvents = 256;

    private readonly object _gate = new();
    private readonly Queue<D046HermesInitializationEvent> _pending = new();
    private D046HermesInitializationHandoffState _state =
        D046HermesInitializationHandoffState.Live;
    private Exception? _fault;
    private long _nextArrivalOrdinal;
    private long _nextEpoch;
    private long _activeEpoch;
    private bool _baselineAvailable;
    private bool _drainScheduled;

    internal int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    internal bool IsFaulted
    {
        get
        {
            lock (_gate)
                return _state == D046HermesInitializationHandoffState.Faulted;
        }
    }

    internal bool IsClosed
    {
        get
        {
            lock (_gate)
                return _state == D046HermesInitializationHandoffState.Closed;
        }
    }

    internal D046HermesInitializationEnqueueDisposition EnqueueProjection(
        D046HermesProjectionEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Enqueue((ordinal) =>
            D046HermesInitializationEvent.FromProjection(ordinal, value));
    }

    internal D046HermesInitializationEnqueueDisposition EnqueueVoice(
        D046HermesVoiceEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Enqueue((ordinal) =>
            D046HermesInitializationEvent.FromVoice(ordinal, value));
    }

    internal bool TryBeginSnapshotEpoch(out D046HermesSnapshotHandoffEpoch epoch)
    {
        lock (_gate)
        {
            ThrowIfFaultedLocked();
            if (_state == D046HermesInitializationHandoffState.Closed)
                throw new ObjectDisposedException(GetType().FullName);
            if (_state != D046HermesInitializationHandoffState.Live)
            {
                var exception = new InvalidOperationException(
                    "A nested Hermes snapshot handoff epoch was rejected.");
                FaultLocked(exception);
                throw exception;
            }
            if (_pending.Count != 0 || _drainScheduled)
            {
                _drainScheduled = _pending.Count != 0;
                epoch = null!;
                return false;
            }

            try
            {
                _activeEpoch = checked(++_nextEpoch);
            }
            catch (OverflowException exception)
            {
                FaultLocked(exception);
                throw;
            }
            _state = D046HermesInitializationHandoffState.Capturing;
            _drainScheduled = false;
            epoch = new D046HermesSnapshotHandoffEpoch(
                _activeEpoch,
                _baselineAvailable);
            return true;
        }
    }

    internal bool CommitSnapshotEpoch(D046HermesSnapshotHandoffEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        lock (_gate)
        {
            ValidateActiveEpochLocked(epoch);
            _baselineAvailable = true;
            _activeEpoch = 0;
            _state = D046HermesInitializationHandoffState.Live;
            _drainScheduled = _pending.Count != 0;
            return _drainScheduled;
        }
    }

    internal bool AbortSnapshotEpoch(
        D046HermesSnapshotHandoffEpoch epoch,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            ValidateActiveEpochLocked(epoch);
            if (!epoch.HadBaseline)
            {
                FaultLocked(exception);
                return false;
            }

            _activeEpoch = 0;
            _state = D046HermesInitializationHandoffState.Live;
            _drainScheduled = _pending.Count != 0;
            return _drainScheduled;
        }
    }

    internal void FailSnapshotEpoch(
        D046HermesSnapshotHandoffEpoch epoch,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            ValidateActiveEpochLocked(epoch);
            FaultLocked(exception);
        }
    }

    internal void EnsureBaselineEstablished()
    {
        lock (_gate)
        {
            ThrowIfFaultedLocked();
            if (_state == D046HermesInitializationHandoffState.Closed)
                throw new ObjectDisposedException(GetType().FullName);
            if (!_baselineAvailable
                || _state != D046HermesInitializationHandoffState.Live)
            {
                throw new InvalidOperationException(
                    "The Hermes snapshot handoff baseline was not established.");
            }
        }
    }

    internal bool TryTakeNext(out D046HermesInitializationEvent value)
    {
        lock (_gate)
        {
            if (_state is D046HermesInitializationHandoffState.Closed
                or D046HermesInitializationHandoffState.Faulted)
            {
                value = null!;
                return false;
            }
            if (_state == D046HermesInitializationHandoffState.Capturing
                || !_drainScheduled)
            {
                value = null!;
                return false;
            }
            if (_pending.Count != 0)
            {
                value = _pending.Dequeue();
                return true;
            }

            _drainScheduled = false;
            value = null!;
            return false;
        }
    }

    internal void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            if (_state == D046HermesInitializationHandoffState.Closed)
                return;
            FaultLocked(exception);
        }
    }

    internal bool Close()
    {
        lock (_gate)
        {
            if (_state == D046HermesInitializationHandoffState.Closed)
                return false;
            _state = D046HermesInitializationHandoffState.Closed;
            _fault = null;
            _pending.Clear();
            _drainScheduled = false;
            return true;
        }
    }

    private D046HermesInitializationEnqueueDisposition Enqueue(
        Func<long, D046HermesInitializationEvent> create)
    {
        lock (_gate)
        {
            if (_state is D046HermesInitializationHandoffState.Closed
                or D046HermesInitializationHandoffState.Faulted)
                return D046HermesInitializationEnqueueDisposition.Closed;
            if (_pending.Count >= MaximumPendingEvents)
            {
                var exception = new InvalidDataException(
                    "The Hermes initialization event handoff exceeded its bound.");
                FaultLocked(exception);
                throw exception;
            }

            long ordinal;
            try
            {
                ordinal = checked(++_nextArrivalOrdinal);
            }
            catch (OverflowException exception)
            {
                FaultLocked(exception);
                throw;
            }
            _pending.Enqueue(create(ordinal));

            if (_state == D046HermesInitializationHandoffState.Capturing
                || _drainScheduled)
            {
                return D046HermesInitializationEnqueueDisposition.Buffered;
            }

            _drainScheduled = true;
            return D046HermesInitializationEnqueueDisposition.ScheduleDrain;
        }
    }

    private void ThrowIfFaultedLocked()
    {
        if (_state != D046HermesInitializationHandoffState.Faulted)
            return;
        throw new InvalidOperationException(
            "The Hermes initialization event handoff is faulted closed.",
            _fault);
    }

    private void ValidateActiveEpochLocked(D046HermesSnapshotHandoffEpoch epoch)
    {
        ThrowIfFaultedLocked();
        if (_state == D046HermesInitializationHandoffState.Closed)
            throw new ObjectDisposedException(GetType().FullName);
        if (_state == D046HermesInitializationHandoffState.Capturing
            && epoch.Epoch == _activeEpoch
            && epoch.HadBaseline == _baselineAvailable)
        {
            return;
        }

        var exception = new InvalidOperationException(
            "The Hermes snapshot handoff epoch token was stale.");
        FaultLocked(exception);
        throw exception;
    }

    private void FaultLocked(Exception exception)
    {
        _state = D046HermesInitializationHandoffState.Faulted;
        _fault = exception;
        _pending.Clear();
        _drainScheduled = false;
    }

    private enum D046HermesInitializationHandoffState
    {
        Live,
        Capturing,
        Faulted,
        Closed
    }
}
