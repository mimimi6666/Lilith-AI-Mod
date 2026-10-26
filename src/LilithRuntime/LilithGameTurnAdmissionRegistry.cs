namespace LilithRuntime;

internal enum LilithGameTurnAdmissionDisposition
{
    Admitted = 0,
    CancelledBeforeAdmission = 1,
    Duplicate = 2,
    RecoveryRequired = 3
}

internal enum LilithGameTurnInterruptDisposition
{
    CancelledBeforeAdmission = 0,
    AdmissionCancelled = 1,
    AlreadyCancelled = 2,
    RecoveryRequired = 3
}

/// <summary>
/// Linearizes an exact game client-turn identity before it can wait for the
/// single Hermes turn gate. This closes the window in which Stop could arrive
/// before prompt.submit returned a Hermes turn id.
/// </summary>
internal sealed class LilithGameTurnAdmissionRegistry : IDisposable
{
    internal const int MaximumCancellationTombstonesPerGeneration = 4_096;

    private readonly object _sync = new();
    private readonly Guid _generationId;
    private readonly string _sessionScope;
    private readonly Dictionary<TurnKey, Admission> _admissions = new();
    private readonly HashSet<TurnKey> _cancellationTombstones = new();
    private TaskCompletionSource<bool> _empty = CompletedEmptySignal();
    private bool _saturated;
    private bool _accepting = true;
    private bool _disposed;

    internal LilithGameTurnAdmissionRegistry(
        Guid generationId,
        string sessionScope)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("A Runtime generation is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        _generationId = generationId;
        _sessionScope = sessionScope;
    }

    internal LilithGameTurnAdmissionDisposition Admit(
        Guid clientTurnId,
        out CancellationToken interruptRequested)
    {
        var key = Key(clientTurnId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_accepting)
            {
                interruptRequested = new CancellationToken(canceled: true);
                return LilithGameTurnAdmissionDisposition.RecoveryRequired;
            }
            if (_admissions.ContainsKey(key))
            {
                interruptRequested = CancellationToken.None;
                return LilithGameTurnAdmissionDisposition.Duplicate;
            }
            if (_cancellationTombstones.Contains(key))
            {
                // An accepted Stop remains authoritative for this exact turn
                // identity until the Runtime generation retires. A delayed or
                // retransmitted Submit must never resurrect it.
                interruptRequested = new CancellationToken(canceled: true);
                return LilithGameTurnAdmissionDisposition.CancelledBeforeAdmission;
            }
            if (_saturated)
            {
                interruptRequested = new CancellationToken(canceled: true);
                return LilithGameTurnAdmissionDisposition.RecoveryRequired;
            }

            var admission = new Admission();
            if (_admissions.Count == 0)
            {
                _empty = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _admissions.Add(key, admission);
            interruptRequested = admission.InterruptRequested.Token;
            return LilithGameTurnAdmissionDisposition.Admitted;
        }
    }

    internal LilithGameTurnInterruptDisposition RecordInterrupt(
        Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        CancellationTokenSource? cancel = null;
        LilithGameTurnInterruptDisposition disposition;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_admissions.TryGetValue(key, out var admission))
            {
                if (admission.Cancelled)
                {
                    return LilithGameTurnInterruptDisposition.AlreadyCancelled;
                }
                admission.Cancelled = true;
                PreserveCancellationAuthorityUnsafe(key);
                cancel = admission.InterruptRequested;
                disposition = LilithGameTurnInterruptDisposition.AdmissionCancelled;
            }
            else if (_cancellationTombstones.Contains(key))
            {
                return LilithGameTurnInterruptDisposition.CancelledBeforeAdmission;
            }
            else if (_saturated)
            {
                return LilithGameTurnInterruptDisposition.RecoveryRequired;
            }
            else if (_cancellationTombstones.Count
                >= MaximumCancellationTombstonesPerGeneration)
            {
                // Never evict an older Stop tombstone: doing so could allow
                // its delayed Submit to resurrect. A new unmatched Stop is
                // rejected without affecting any other turn or session.
                _saturated = true;
                return LilithGameTurnInterruptDisposition.RecoveryRequired;
            }
            else
            {
                _cancellationTombstones.Add(key);
                return LilithGameTurnInterruptDisposition.CancelledBeforeAdmission;
            }
        }

        // Cancellation callbacks are deliberately invoked outside the
        // registry lock. A callback may unwind prompt dispatch synchronously.
        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Retire won the post-lock race. The admission is already
            // terminal and its generation-long tombstone was preserved.
        }
        return disposition;
    }

    internal bool TryBeginPromptDispatch(Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = RequiredAdmission(key);
            if (admission.Cancelled)
                return false;
            if (admission.PromptDispatchStarted)
            {
                throw new InvalidOperationException(
                    "The client turn prompt was already dispatched.");
            }
            admission.PromptDispatchStarted = true;
            admission.RemoteTurnMayBeActive = true;
            return true;
        }
    }

    internal void CompletePromptDispatch(
        Guid clientTurnId,
        bool remoteTurnMayBeActive)
    {
        var key = Key(clientTurnId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = RequiredAdmission(key);
            if (!admission.PromptDispatchStarted
                || admission.PromptDispatchCompleted)
            {
                throw new InvalidOperationException(
                    "The client turn prompt dispatch was not active.");
            }
            admission.PromptDispatchCompleted = true;
            admission.RemoteTurnMayBeActive = remoteTurnMayBeActive;
        }
    }

    internal void CompleteUntrackedPromptFailure(Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        CancellationTokenSource? cancel = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = RequiredAdmission(key);
            if (!admission.PromptDispatchStarted
                || admission.PromptDispatchCompleted)
            {
                throw new InvalidOperationException(
                    "The failed client prompt dispatch was not active.");
            }
            admission.PromptDispatchCompleted = true;
            admission.RemoteTurnMayBeActive = true;
            if (!admission.Cancelled)
            {
                admission.Cancelled = true;
                PreserveCancellationAuthorityUnsafe(key);
                cancel = admission.InterruptRequested;
            }
        }
        cancel?.Cancel();
    }

    internal bool TryClaimRemoteInterrupt(Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = RequiredAdmission(key);
            if (!admission.Cancelled
                || !admission.PromptDispatchCompleted
                || !admission.RemoteTurnMayBeActive
                || admission.RemoteInterruptClaimed)
            {
                return false;
            }
            admission.RemoteInterruptClaimed = true;
            return true;
        }
    }

    internal void ObserveRemoteTerminal(Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = RequiredAdmission(key);
            admission.RemoteTurnMayBeActive = false;
        }
    }

    internal void Retire(Guid clientTurnId)
    {
        var key = Key(clientTurnId);
        Admission? admission;
        TaskCompletionSource<bool>? empty = null;
        lock (_sync)
        {
            if (_disposed)
                return;
            _admissions.Remove(key, out admission);
            if (admission is not null && _admissions.Count == 0)
                empty = _empty;
        }
        admission?.InterruptRequested.Dispose();
        empty?.TrySetResult(true);
    }

    internal void SealAndCancelAllAdmissions()
    {
        CancellationTokenSource[] cancellations;
        lock (_sync)
        {
            if (_disposed)
                return;
            // Admission shutdown is linearized with Admit under this lock.
            // Once sealed, a Submit that passed a Host pre-check but has not
            // yet registered cannot appear after the drain snapshot.
            _accepting = false;
            var pending = new List<CancellationTokenSource>();
            foreach (var pair in _admissions)
            {
                if (pair.Value.Cancelled)
                    continue;
                pair.Value.Cancelled = true;
                PreserveCancellationAuthorityUnsafe(pair.Key);
                pending.Add(pair.Value.InterruptRequested);
            }
            cancellations = pending.ToArray();
        }
        foreach (var cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Retire completed between the sealed snapshot and delivery.
            }
        }
    }

    internal async Task<bool> WaitForEmptyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Task empty;
        lock (_sync)
        {
            if (_disposed || _admissions.Count == 0)
                return true;
            empty = _empty.Task;
        }
        try
        {
            await empty.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Admission[] admissions;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            admissions = _admissions.Values.ToArray();
            _admissions.Clear();
            _cancellationTombstones.Clear();
            _empty.TrySetResult(true);
        }
        foreach (var admission in admissions)
        {
            try
            {
                admission.InterruptRequested.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            admission.InterruptRequested.Dispose();
        }
    }

    private Admission RequiredAdmission(TurnKey key)
        => _admissions.TryGetValue(key, out var admission)
            ? admission
            : throw new InvalidOperationException(
                "The client turn admission is no longer active.");

    private void PreserveCancellationAuthorityUnsafe(TurnKey key)
    {
        if (_saturated || _cancellationTombstones.Contains(key))
            return;
        if (_cancellationTombstones.Count
            >= MaximumCancellationTombstonesPerGeneration)
        {
            // Existing tombstones are never evicted. Saturation rejects every
            // later admission in this generation, including retransmission of
            // this active turn, while the active turn is still cancelled now.
            _saturated = true;
            return;
        }
        _cancellationTombstones.Add(key);
    }

    private TurnKey Key(Guid clientTurnId)
    {
        if (clientTurnId == Guid.Empty)
            throw new ArgumentException("A client turn identity is required.");
        return new TurnKey(_generationId, _sessionScope, clientTurnId);
    }

    private sealed record TurnKey(
        Guid GenerationId,
        string SessionScope,
        Guid ClientTurnId);

    private sealed class Admission
    {
        internal CancellationTokenSource InterruptRequested { get; } = new();

        internal bool Cancelled { get; set; }

        internal bool PromptDispatchStarted { get; set; }

        internal bool PromptDispatchCompleted { get; set; }

        internal bool RemoteTurnMayBeActive { get; set; }

        internal bool RemoteInterruptClaimed { get; set; }
    }

    private static TaskCompletionSource<bool> CompletedEmptySignal()
    {
        var signal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult(true);
        return signal;
    }
}
