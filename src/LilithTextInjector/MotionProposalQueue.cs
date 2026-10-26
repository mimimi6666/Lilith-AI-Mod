using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal static class MotionProducerIdentity
{
    internal static bool TryNormalize(string? producerId, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(producerId))
            return false;
        normalized = producerId.Trim();
        if (normalized.Length > 64)
        {
            normalized = string.Empty;
            return false;
        }
        foreach (var character in normalized)
        {
            if (!char.IsControl(character))
                continue;
            normalized = string.Empty;
            return false;
        }
        return true;
    }
}

internal enum MotionProposalEnqueueStatus
{
    Accepted,
    InvalidKind,
    InvalidSource,
    InvalidProducer,
    InvalidTimeToLive,
    ProducerLimitReached,
    QueueFull
}

internal sealed class MotionProposal
{
    internal Guid Id { get; }
    internal long Sequence { get; }
    internal MotionIntentKind Kind { get; }
    internal MotionIntentSource Source { get; }
    internal string ProducerId { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal MotionProposal(
        Guid id,
        long sequence,
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        Sequence = sequence;
        Kind = kind;
        Source = source;
        ProducerId = producerId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }
}

internal readonly struct MotionProposalEnqueueResult
{
    internal MotionProposalEnqueueStatus Status { get; }
    internal MotionProposal? Proposal { get; }
    internal bool Accepted => Status == MotionProposalEnqueueStatus.Accepted;

    private MotionProposalEnqueueResult(
        MotionProposalEnqueueStatus status,
        MotionProposal? proposal)
    {
        Status = status;
        Proposal = proposal;
    }

    internal static MotionProposalEnqueueResult Accept(MotionProposal proposal)
        => new(MotionProposalEnqueueStatus.Accepted, proposal);

    internal static MotionProposalEnqueueResult Reject(MotionProposalEnqueueStatus status)
        => new(status, null);
}

internal readonly struct MotionProposalTakeResult
{
    internal MotionProposal? Proposal { get; }
    internal int ExpiredSkipped { get; }
    internal bool HasProposal => Proposal != null;

    internal MotionProposalTakeResult(MotionProposal? proposal, int expiredSkipped)
    {
        Proposal = proposal;
        ExpiredSkipped = expiredSkipped;
    }
}

internal enum RestrictedMotionProposalEnqueueStatus
{
    Accepted = 0,
    InvalidKind = 1,
    InvalidSource = 2,
    InvalidProducer = 3,
    InvalidTimeToLive = 4,
    AuthorizationRejected = 5,
    ProducerLimitReached = 6,
    QueueFull = 7
}

internal readonly struct RestrictedMotionProposalEnqueueResult
{
    internal RestrictedMotionProposalEnqueueStatus Status { get; }
    internal MotionProposal? Proposal { get; }
    internal bool Accepted =>
        Status == RestrictedMotionProposalEnqueueStatus.Accepted;

    internal RestrictedMotionProposalEnqueueResult(
        RestrictedMotionProposalEnqueueStatus status,
        MotionProposal? proposal)
    {
        Status = status;
        Proposal = proposal;
    }
}

internal readonly struct MotionProposalQueueSnapshot
{
    internal int Capacity { get; }
    internal int MaximumPerProducer { get; }
    internal int Count { get; }
    internal long Accepted { get; }
    internal long Dequeued { get; }
    internal long Canceled { get; }
    internal long Expired { get; }
    internal long Rejected { get; }

    internal MotionProposalQueueSnapshot(
        int capacity,
        int maximumPerProducer,
        int count,
        long accepted,
        long dequeued,
        long canceled,
        long expired,
        long rejected)
    {
        Capacity = capacity;
        MaximumPerProducer = maximumPerProducer;
        Count = count;
        Accepted = accepted;
        Dequeued = dequeued;
        Canceled = canceled;
        Expired = expired;
        Rejected = rejected;
    }
}

internal sealed class MotionProposalQueue
{
    internal static readonly TimeSpan MinimumTimeToLive = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan MaximumTimeToLive = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly Queue<MotionProposal> _queue = new();
    private readonly int _capacity;
    private readonly int _maximumPerProducer;
    private long _nextSequence;
    private long _accepted;
    private long _dequeued;
    private long _canceled;
    private long _expired;
    private long _rejected;

    internal MotionProposalQueue(int capacity, int? maximumPerProducer = null)
    {
        if (capacity < 1 || capacity > 1024)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        var resolvedMaximumPerProducer = maximumPerProducer ?? Math.Min(capacity, 16);
        if (resolvedMaximumPerProducer < 1 || resolvedMaximumPerProducer > capacity)
            throw new ArgumentOutOfRangeException(nameof(maximumPerProducer));
        _capacity = capacity;
        _maximumPerProducer = resolvedMaximumPerProducer;
    }

    internal MotionProposalEnqueueResult Enqueue(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive)
        => Enqueue(kind, source, producerId, timeToLive, DateTimeOffset.UtcNow);

    internal MotionProposalEnqueueResult Enqueue(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive,
        DateTimeOffset now)
    {
        var validation = Validate(kind, source, producerId, timeToLive);
        if (validation != MotionProposalEnqueueStatus.Accepted)
        {
            lock (_gate)
                _rejected++;
            return MotionProposalEnqueueResult.Reject(validation);
        }

        MotionProducerIdentity.TryNormalize(producerId, out var normalizedProducer);
        lock (_gate)
        {
            PruneExpiredLocked(now);
            if (_queue.Count >= _capacity)
            {
                _rejected++;
                return MotionProposalEnqueueResult.Reject(MotionProposalEnqueueStatus.QueueFull);
            }
            if (CountProducerLocked(normalizedProducer) >= _maximumPerProducer)
            {
                _rejected++;
                return MotionProposalEnqueueResult.Reject(
                    MotionProposalEnqueueStatus.ProducerLimitReached);
            }

            var proposal = new MotionProposal(
                Guid.NewGuid(),
                ++_nextSequence,
                kind,
                source,
                normalizedProducer,
                now,
                now + timeToLive);
            _queue.Enqueue(proposal);
            _accepted++;
            return MotionProposalEnqueueResult.Accept(proposal);
        }
    }

    internal RestrictedMotionProposalEnqueueResult EnqueueRestricted(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive,
        DateTimeOffset now,
        Func<MotionProposal, bool> authorizeProspectiveProposal)
    {
        if (authorizeProspectiveProposal == null)
        {
            throw new ArgumentNullException(
                nameof(authorizeProspectiveProposal));
        }

        var validation = Validate(kind, source, producerId, timeToLive);
        if (validation != MotionProposalEnqueueStatus.Accepted)
        {
            lock (_gate)
                _rejected++;
            return new RestrictedMotionProposalEnqueueResult(
                ToRestrictedStatus(validation),
                null);
        }

        MotionProducerIdentity.TryNormalize(
            producerId,
            out var normalizedProducer);
        lock (_gate)
        {
            PruneExpiredLocked(now);
            var prospectiveProposal = new MotionProposal(
                Guid.NewGuid(),
                checked(_nextSequence + 1),
                kind,
                source,
                normalizedProducer,
                now,
                now + timeToLive);
            if (!authorizeProspectiveProposal(prospectiveProposal))
            {
                _rejected++;
                return new RestrictedMotionProposalEnqueueResult(
                    RestrictedMotionProposalEnqueueStatus
                        .AuthorizationRejected,
                    prospectiveProposal);
            }
            if (_queue.Count >= _capacity)
            {
                _rejected++;
                return new RestrictedMotionProposalEnqueueResult(
                    RestrictedMotionProposalEnqueueStatus.QueueFull,
                    prospectiveProposal);
            }
            if (CountProducerLocked(normalizedProducer)
                >= _maximumPerProducer)
            {
                _rejected++;
                return new RestrictedMotionProposalEnqueueResult(
                    RestrictedMotionProposalEnqueueStatus
                        .ProducerLimitReached,
                    prospectiveProposal);
            }

            _nextSequence = prospectiveProposal.Sequence;
            _queue.Enqueue(prospectiveProposal);
            _accepted++;
            return new RestrictedMotionProposalEnqueueResult(
                RestrictedMotionProposalEnqueueStatus.Accepted,
                prospectiveProposal);
        }
    }

    internal MotionProposalTakeResult TakeNext(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expiredBefore = _expired;
            PruneExpiredLocked(now);
            if (_queue.Count == 0)
                return new MotionProposalTakeResult(null, checked((int)(_expired - expiredBefore)));

            var proposal = _queue.Dequeue();
            _dequeued++;
            return new MotionProposalTakeResult(
                proposal,
                checked((int)(_expired - expiredBefore)));
        }
    }

    internal bool Cancel(Guid proposalId)
    {
        if (proposalId == Guid.Empty)
            return false;

        lock (_gate)
        {
            var removed = RemoveWhereLocked(proposal => proposal.Id == proposalId);
            _canceled += removed;
            return removed > 0;
        }
    }

    internal int CancelByProducer(string producerId)
    {
        if (!MotionProducerIdentity.TryNormalize(producerId, out var normalizedProducer))
            return 0;
        lock (_gate)
        {
            var removed = RemoveWhereLocked(proposal => string.Equals(
                proposal.ProducerId,
                normalizedProducer,
                StringComparison.Ordinal));
            _canceled += removed;
            return removed;
        }
    }

    internal MotionProposalQueueSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new MotionProposalQueueSnapshot(
                _capacity,
                _maximumPerProducer,
                _queue.Count,
                _accepted,
                _dequeued,
                _canceled,
                _expired,
                _rejected);
        }
    }

    private MotionProposalEnqueueStatus Validate(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive)
    {
        if (kind == MotionIntentKind.None)
            return MotionProposalEnqueueStatus.InvalidKind;
        if (source is not (MotionIntentSource.Initiative
            or MotionIntentSource.Model
            or MotionIntentSource.Remote))
        {
            return MotionProposalEnqueueStatus.InvalidSource;
        }
        if (!MotionProducerIdentity.TryNormalize(producerId, out _))
            return MotionProposalEnqueueStatus.InvalidProducer;
        if (timeToLive < MinimumTimeToLive || timeToLive > MaximumTimeToLive)
            return MotionProposalEnqueueStatus.InvalidTimeToLive;
        return MotionProposalEnqueueStatus.Accepted;
    }

    private static RestrictedMotionProposalEnqueueStatus
        ToRestrictedStatus(MotionProposalEnqueueStatus status)
        => status switch
        {
            MotionProposalEnqueueStatus.InvalidKind =>
                RestrictedMotionProposalEnqueueStatus.InvalidKind,
            MotionProposalEnqueueStatus.InvalidSource =>
                RestrictedMotionProposalEnqueueStatus.InvalidSource,
            MotionProposalEnqueueStatus.InvalidProducer =>
                RestrictedMotionProposalEnqueueStatus.InvalidProducer,
            MotionProposalEnqueueStatus.InvalidTimeToLive =>
                RestrictedMotionProposalEnqueueStatus.InvalidTimeToLive,
            MotionProposalEnqueueStatus.ProducerLimitReached =>
                RestrictedMotionProposalEnqueueStatus.ProducerLimitReached,
            MotionProposalEnqueueStatus.QueueFull =>
                RestrictedMotionProposalEnqueueStatus.QueueFull,
            _ => RestrictedMotionProposalEnqueueStatus.Accepted
        };

    private void PruneExpiredLocked(DateTimeOffset now)
    {
        var removed = RemoveWhereLocked(proposal => proposal.ExpiresAt <= now);
        _expired += removed;
    }

    private int CountProducerLocked(string producerId)
    {
        var count = 0;
        foreach (var proposal in _queue)
        {
            if (string.Equals(proposal.ProducerId, producerId, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private int RemoveWhereLocked(Func<MotionProposal, bool> predicate)
    {
        var originalCount = _queue.Count;
        if (originalCount == 0)
            return 0;

        var retained = new Queue<MotionProposal>(originalCount);
        var removed = 0;
        while (_queue.Count > 0)
        {
            var proposal = _queue.Dequeue();
            if (predicate(proposal))
                removed++;
            else
                retained.Enqueue(proposal);
        }
        while (retained.Count > 0)
            _queue.Enqueue(retained.Dequeue());
        return removed;
    }
}

internal static class MotionProposalIngress
{
    internal const int DefaultCapacity = 64;
    internal const int DefaultMaximumPerProducer = 16;
    private static readonly MotionProposalQueue Shared = new(
        DefaultCapacity,
        DefaultMaximumPerProducer);

    internal static MotionProposalEnqueueResult Submit(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive)
        => Shared.Enqueue(kind, source, producerId, timeToLive);

    internal static RestrictedMotionProposalEnqueueResult SubmitRestricted(
        MotionIntentKind kind,
        MotionIntentSource source,
        string producerId,
        TimeSpan timeToLive,
        DateTimeOffset now,
        Func<MotionProposal, bool> authorizeProspectiveProposal)
        => Shared.EnqueueRestricted(
            kind,
            source,
            producerId,
            timeToLive,
            now,
            authorizeProspectiveProposal);

    internal static bool Cancel(Guid proposalId)
        => Shared.Cancel(proposalId);

    internal static int CancelByProducer(string producerId)
        => Shared.CancelByProducer(producerId);

    internal static MotionProposalTakeResult TakeNext(DateTimeOffset now)
        => Shared.TakeNext(now);

    internal static MotionProposalQueueSnapshot GetSnapshot()
        => Shared.GetSnapshot();
}
