using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class MotionProposalCompletion
{
    internal Guid ProposalId { get; }
    internal long Sequence { get; }
    internal string ProducerId { get; }
    internal MotionIntentSource Source { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentStatus Status { get; }
    internal MotionIntentReason Reason { get; }
    internal DateTimeOffset RecordedAt { get; }

    internal MotionProposalCompletion(
        MotionProposal proposal,
        MotionIntentResult result)
    {
        ProposalId = proposal.Id;
        Sequence = proposal.Sequence;
        ProducerId = proposal.ProducerId;
        Source = proposal.Source;
        Kind = proposal.Kind;
        Status = result.Status;
        Reason = result.Reason;
        RecordedAt = result.RecordedAt;
    }
}

internal sealed class MotionCompletionSubscription : IDisposable
{
    private readonly object _gate = new();
    private readonly MotionProposalCompletionHub _owner;
    private readonly Queue<MotionProposalCompletion> _buffer = new();
    private readonly int _capacity;
    private bool _disposed;
    private long _droppedCount;

    internal Guid Id { get; }
    internal string ProducerId { get; }

    internal long DroppedCount
    {
        get
        {
            lock (_gate)
                return _droppedCount;
        }
    }

    internal int BufferedCount
    {
        get
        {
            lock (_gate)
                return _buffer.Count;
        }
    }

    internal MotionCompletionSubscription(
        MotionProposalCompletionHub owner,
        Guid id,
        string producerId,
        int capacity)
    {
        _owner = owner;
        Id = id;
        ProducerId = producerId;
        _capacity = capacity;
    }

    internal bool TryRead(out MotionProposalCompletion? completion)
    {
        lock (_gate)
        {
            if (_disposed || _buffer.Count == 0)
            {
                completion = null;
                return false;
            }
            completion = _buffer.Dequeue();
            return true;
        }
    }

    internal void Enqueue(MotionProposalCompletion completion)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            while (_buffer.Count >= _capacity)
            {
                _buffer.Dequeue();
                _droppedCount++;
            }
            _buffer.Enqueue(completion);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _buffer.Clear();
        }
        _owner.Unsubscribe(Id);
    }
}

internal sealed class MotionProposalCompletionHub
{
    internal const int DefaultSubscriberCapacity = 64;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, MotionCompletionSubscription> _subscriptions = new();

    internal MotionCompletionSubscription Subscribe(
        string producerId,
        int capacity = DefaultSubscriberCapacity)
    {
        if (!MotionProducerIdentity.TryNormalize(producerId, out var normalizedProducer))
            throw new ArgumentException("Producer ID is invalid.", nameof(producerId));
        if (capacity < 1 || capacity > 1024)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        var subscription = new MotionCompletionSubscription(
            this,
            Guid.NewGuid(),
            normalizedProducer,
            capacity);
        lock (_gate)
            _subscriptions.Add(subscription.Id, subscription);
        return subscription;
    }

    internal bool Publish(MotionProposal proposal, MotionIntentResult result)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (proposal.Id != result.RequestId
            || proposal.Kind != result.Kind)
        {
            return false;
        }
        if (result.Status is MotionIntentStatus.Accepted or MotionIntentStatus.Started)
            return false;

        var completion = new MotionProposalCompletion(proposal, result);
        MotionCompletionSubscription[] targets;
        lock (_gate)
        {
            var matches = new List<MotionCompletionSubscription>();
            foreach (var subscription in _subscriptions.Values)
            {
                if (string.Equals(
                    subscription.ProducerId,
                    proposal.ProducerId,
                    StringComparison.Ordinal))
                {
                    matches.Add(subscription);
                }
            }
            targets = matches.ToArray();
        }

        foreach (var target in targets)
            target.Enqueue(completion);
        return true;
    }

    internal void Unsubscribe(Guid subscriptionId)
    {
        lock (_gate)
            _subscriptions.Remove(subscriptionId);
    }

    internal int SubscriberCount
    {
        get
        {
            lock (_gate)
                return _subscriptions.Count;
        }
    }
}

internal static class MotionProposalCompletions
{
    private static readonly MotionProposalCompletionHub Shared = new();

    internal static MotionCompletionSubscription Subscribe(
        string producerId,
        int capacity = MotionProposalCompletionHub.DefaultSubscriberCapacity)
        => Shared.Subscribe(producerId, capacity);

    internal static bool Publish(MotionProposal proposal, MotionIntentResult result)
        => Shared.Publish(proposal, result);

    internal static int SubscriberCount => Shared.SubscriberCount;
}
