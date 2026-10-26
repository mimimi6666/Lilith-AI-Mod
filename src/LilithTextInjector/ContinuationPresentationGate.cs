using System;
using System.Collections.Generic;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal sealed record ContinuationPresentationTicket(long Epoch, LilithGameBridgeContinuationContext Context);

// One in-process presentation lease, separate from durable history. Restart
// subscribes at the notification tail; history can never mint these leases.
internal sealed class ContinuationPresentationGate
{
    private readonly object _gate = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private long _epoch;
    private long _notBefore;
    private Guid _runtimeGeneration;

    internal void ObserveGeneration(Guid generation, long now)
    {
        lock (_gate)
        {
            if (generation == _runtimeGeneration) return;
            if (_runtimeGeneration != Guid.Empty) Invalidate(now);
            _runtimeGeneration = generation;
        }
    }

    internal void Invalidate(long now)
    {
        lock (_gate) { _epoch = checked(_epoch + 1); _notBefore = Math.Max(_notBefore, now); }
    }

    internal bool TryAdmit(LilithGameBridgeContinuationContext context, long now,
        out ContinuationPresentationTicket? ticket)
    {
        lock (_gate)
        {
            ticket = null;
            var key = context.EventId + ":" + context.MessageId;
            if (!context.IsCurrent(now) || context.CompletedAtUnixMilliseconds <= _notBefore || !_seen.Add(key)) return false;
            _order.Enqueue(key);
            while (_order.Count > 512) _seen.Remove(_order.Dequeue());
            ticket = new(_epoch, context);
            return true;
        }
    }

    internal bool IsCurrent(ContinuationPresentationTicket ticket, long now)
    {
        lock (_gate) return ticket.Epoch == _epoch && ticket.Context.IsCurrent(now);
    }
}
