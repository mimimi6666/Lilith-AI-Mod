namespace LilithControlCenter;

/// <summary>
/// One view model's display-only snapshot and single-flight read state.
/// The owning UI serializes access and validates native/schema identity before
/// completion. This class grants no authority and owns no fetch, timer, storage,
/// cancellation or transport. An old snapshot may remain visible after a failure
/// or invalidation, but it is not reusable as a successful current read.
/// </summary>
internal sealed class D046HermesCapabilityCatalogReadCache<T> where T : class
{
    private long _version;
    private long _nextTicket;
    private long _activeTicket;
    private long _activeReadVersion;

    public T? Snapshot { get; private set; }
    public DateTimeOffset? CompletedUtc { get; private set; }
    public bool IsReading { get; private set; }
    public bool IsInvalidated { get; private set; } = true;
    public bool LastReadFailed { get; private set; }
    public bool IsStopped { get; private set; }
    public bool CanReuse => Snapshot is not null
        && !IsReading && !IsInvalidated && !LastReadFailed && !IsStopped;

    public bool TryBeginRead(out long ticket)
    {
        ticket = 0;
        if (IsStopped || IsReading)
            return false;

        _activeTicket = checked(++_nextTicket);
        _activeReadVersion = _version;
        IsReading = true;
        ticket = _activeTicket;
        return true;
    }

    public bool TryComplete(long ticket, T snapshot, DateTimeOffset completedUtc)
    {
        if (!IsReading || ticket != _activeTicket)
            return false;

        try
        {
            if (IsStopped || _activeReadVersion != _version)
                return false;
            if (snapshot is null)
            {
                LastReadFailed = true;
                return false;
            }

            Snapshot = snapshot;
            CompletedUtc = completedUtc.ToUniversalTime();
            IsInvalidated = false;
            LastReadFailed = false;
            return true;
        }
        finally
        {
            IsReading = false;
        }
    }

    public bool Fail(long ticket)
    {
        if (!IsReading || ticket != _activeTicket)
            return false;

        LastReadFailed = true;
        IsReading = false;
        return true;
    }

    public void Invalidate()
    {
        _version = checked(_version + 1);
        IsInvalidated = true;
    }

    public void Reset()
    {
        Invalidate();
        Snapshot = null;
        CompletedUtc = null;
        LastReadFailed = false;
        // A reset retires adoption, not the owner of the still-running read.
        // Its completion/failure must release the slot before another can begin.
    }

    public void Stop()
    {
        Reset();
        IsStopped = true;
    }
}
