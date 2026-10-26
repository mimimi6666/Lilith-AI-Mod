using System;
using System.Threading;

namespace LilithTextInjector;

internal enum D045WorkbenchTurnCancellationKind
{
    None = 0,
    ClientDisconnected = 1,
    TurnTimedOut = 2,
    ServerShutdown = 3
}

internal sealed class D045WorkbenchTurnLiveness
{
    private enum TurnState
    {
        Live = 0,
        Completed = 1,
        Cancelled = 2
    }

    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private TurnState _state;
    private D045WorkbenchTurnCancellationKind _cancellationKind;
    private bool _cancellationConsumed;

    internal CancellationToken Token => _cancellation.Token;

    internal D045WorkbenchTurnCancellationKind CancellationKind
    {
        get
        {
            lock (_gate)
            {
                return _state == TurnState.Cancelled
                    ? _cancellationKind
                    : D045WorkbenchTurnCancellationKind.None;
            }
        }
    }

    internal bool IsLive
    {
        get
        {
            lock (_gate)
                return _state == TurnState.Live;
        }
    }

    internal bool TryCancel(D045WorkbenchTurnCancellationKind kind)
    {
        if (!Enum.IsDefined(typeof(D045WorkbenchTurnCancellationKind), kind)
            || kind == D045WorkbenchTurnCancellationKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            if (_state != TurnState.Live)
                return false;
            _cancellationKind = kind;
            _state = TurnState.Cancelled;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A consumer callback cannot undo the already committed cancellation.
        }
        return true;
    }

    internal bool TryConsumeCancellation(
        out D045WorkbenchTurnCancellationKind kind)
    {
        lock (_gate)
        {
            if (_state != TurnState.Cancelled || _cancellationConsumed)
            {
                kind = D045WorkbenchTurnCancellationKind.None;
                return false;
            }
            _cancellationConsumed = true;
            kind = _cancellationKind;
            return true;
        }
    }

    internal bool TryComplete()
    {
        lock (_gate)
        {
            if (_state != TurnState.Live)
                return false;
            _state = TurnState.Completed;
            return true;
        }
    }

    internal bool TryAdmitEffectDispatch(Func<bool> hostAdmission)
    {
        if (hostAdmission is null)
            throw new ArgumentNullException(nameof(hostAdmission));
        lock (_gate)
        {
            return _state == TurnState.Live && hostAdmission();
        }
    }
}
