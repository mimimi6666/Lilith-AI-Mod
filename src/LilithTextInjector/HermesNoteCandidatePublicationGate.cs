using System;

namespace LilithTextInjector;

internal sealed class HermesNoteCandidateContext
{
    internal string UserText { get; }
    internal string Reply { get; }

    internal HermesNoteCandidateContext(string userText, string reply)
    {
        UserText = userText;
        Reply = reply;
    }
}

// One transient slot, not another transcript or permanent memory store.
// Only the caller holding a successful terminal publication receipt may take it.
internal sealed class HermesNoteCandidatePublicationGate
{
    private readonly object _gate = new();
    private PlayerTurnOperationHandle? _operation;
    private PlayerTurnOperationHandle? _consumed;
    private HermesNoteCandidateContext? _context;

    internal void Stage(
        PlayerTurnOperationHandle operation,
        string userText,
        string reply)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if ((_consumed != null && IsSameOrOlder(operation, _consumed))
                || (_operation != null && IsSameOrOlder(operation, _operation)))
                return;
            _operation = null;
            _context = null;
            if (string.IsNullOrWhiteSpace(userText)
                || string.IsNullOrWhiteSpace(reply))
                return;
            _operation = operation;
            _context = new HermesNoteCandidateContext(userText, reply);
        }
    }

    internal bool TryTakePublished(
        PlayerTurnOperationHandle operation,
        PlayerTurnPublicationKind publicationKind,
        out HermesNoteCandidateContext? context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            context = null;
            if (_operation == null || !_operation.Matches(operation))
                return false;
            var pending = _context;
            _consumed = _operation;
            _operation = null;
            _context = null;
            if (publicationKind != PlayerTurnPublicationKind.AssistantReply
                || pending == null)
                return false;
            context = pending;
            return true;
        }
    }

    internal void Retire(PlayerTurnOperationHandle operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if (_operation == null || !_operation.Matches(operation))
                return;
            _consumed = _operation;
            _operation = null;
            _context = null;
        }
    }

    private static bool IsSameOrOlder(
        PlayerTurnOperationHandle candidate,
        PlayerTurnOperationHandle current)
        => string.Equals(candidate.ProcessEpochId, current.ProcessEpochId,
                StringComparison.Ordinal)
            && candidate.OperationGeneration <= current.OperationGeneration;
}
