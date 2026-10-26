using System;
using System.Collections.Generic;
using System.Linq;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Ephemeral attachment to an existing official-dialogue event, never an outbox
// migration or a new trigger. Restored outbox entries have no current-game capture.
internal sealed class NativeMiniGameEventDispatches
{
    internal const int MaximumPending = 64;
    internal const long LifetimeMilliseconds = 11 * 60 * 1000;
    private readonly object _gate = new();
    private readonly MiniGameConversationEvents _events;
    private readonly Dictionary<Guid, (LilithMiniGamePublicContext? Context, long Expires)> _pending = new();

    internal NativeMiniGameEventDispatches(MiniGameConversationEvents events) => _events = events;

    internal void Capture(Guid eventId, LilithMiniGamePublicContext? context, long now)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event identity is required.");
        lock (_gate)
        {
            ExpireLocked(now);
            if (_pending.ContainsKey(eventId) || _pending.Count >= MaximumPending) return;
            _pending.Add(eventId, (_events.Reserve(context, eventId), checked(now + LifetimeMilliseconds)));
        }
    }

    internal LilithMiniGamePublicContext? Get(Guid eventId)
    {
        lock (_gate) return _pending.TryGetValue(eventId, out var value) ? value.Context : null;
    }

    internal bool Settle(Guid eventId, LilithMiniGameEventReceipt? receipt)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(eventId, out var value)) return false;
            if (receipt is not null && !receipt.Matches(value.Context)) return false;
            _pending.Remove(eventId);
            if (receipt is not null) _events.Acknowledge(eventId, receipt);
            _events.Release(eventId);
            return true;
        }
    }

    internal void Expire(long now)
    {
        lock (_gate) ExpireLocked(now);
    }

    private void ExpireLocked(long now)
    {
        foreach (var id in _pending.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
        {
            _pending.Remove(id);
            _events.Release(id); // keep the immutable retry batch; never infer an acknowledgement
        }
    }
}
