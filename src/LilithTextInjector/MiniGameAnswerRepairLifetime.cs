using System;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Main-thread owned, pure identity gate. No timers, captures, model calls or
// cancellation callbacks are owned here. A newer match can never revive a ticket.
internal sealed class MiniGameMatchIdentity
{
    internal Guid Epoch { get; }
    internal Guid Match { get; }
    internal string Kind { get; }
    internal MiniGameMatchIdentity(Guid epoch, Guid match, string kind)
    { Epoch = epoch; Match = match; Kind = kind; }
    internal bool Matches(MiniGameMatchIdentity? other) => other != null
        && Epoch == other.Epoch && Match == other.Match && string.Equals(Kind, other.Kind, StringComparison.Ordinal);
}

internal sealed class MiniGameAnswerRepairLifetime
{
    private PlayerTurnOperationHandle? operation;
    private MiniGameMatchIdentity? identity;
    internal bool HasOwner => operation != null;

    internal void Attach(PlayerTurnOperationHandle owner, LilithMiniGamePublicContext context,
        LilithMiniGameAnswerRepair requirements)
    {
        if (!requirements.IsSupportedBy(context)) throw new ArgumentException("Unsupported repair lifetime.");
        if (operation != null) throw new InvalidOperationException("A repair lifetime is already owned.");
        operation = owner; identity = new(context.GameEpoch, context.MatchId, context.GameKind);
    }

    internal PlayerTurnOperationHandle? RetireIfChanged(MiniGameMatchIdentity? current,
        Func<PlayerTurnOperationHandle, bool> isActive)
    {
        if (operation == null) return null;
        if (!isActive(operation)) { operation = null; identity = null; return null; }
        if (identity!.Matches(current)) return null;
        var retired = operation;
        operation = null; identity = null;
        return retired;
    }
}
