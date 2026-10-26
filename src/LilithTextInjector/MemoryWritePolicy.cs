using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

// Shared write data contracts only. The retired legacy write policy and
// coordinator are intentionally absent; this file grants no write authority.
internal enum MemoryProposalSource
{
    LocalRuntime = 0,
    BuiltInModel = 1,
    RemoteAgent = 2,
    UserExplicit = 3
}

internal sealed class MemoryWriteProposal
{
    internal Guid Id { get; }
    internal string ProducerId { get; }
    internal MemoryProposalSource Source { get; }
    internal string OwnerId { get; }
    internal string SessionId { get; }
    internal MemoryEventKind Kind { get; }
    internal string Content { get; }
    internal string Role { get; }
    internal string Summary { get; }
    internal IReadOnlyList<string> Tags { get; }
    internal double ProposedImportance { get; }
    internal double ProposedConfidence { get; }
    internal MemorySensitivity Sensitivity { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal Guid? SupersedesEventId { get; }

    internal MemoryWriteProposal(
        Guid id,
        string producerId,
        MemoryProposalSource source,
        string ownerId,
        string sessionId,
        MemoryEventKind kind,
        string content,
        DateTimeOffset occurredAt,
        DateTimeOffset requestedAt,
        string? role = null,
        string? summary = null,
        IEnumerable<string>? tags = null,
        double proposedImportance = 0.5,
        double proposedConfidence = 0.7,
        MemorySensitivity sensitivity = MemorySensitivity.Personal,
        Guid? supersedesEventId = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Memory proposal ID cannot be empty.", nameof(id));
        if (!Enum.IsDefined(typeof(MemoryProposalSource), source))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(typeof(MemoryEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(MemorySensitivity), sensitivity))
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (proposedImportance < 0 || proposedImportance > 1 || double.IsNaN(proposedImportance))
            throw new ArgumentOutOfRangeException(nameof(proposedImportance));
        if (proposedConfidence < 0 || proposedConfidence > 1 || double.IsNaN(proposedConfidence))
            throw new ArgumentOutOfRangeException(nameof(proposedConfidence));
        Id = id;
        ProducerId = MemoryIdentity.Normalize(producerId, nameof(producerId), 128);
        Source = source;
        OwnerId = MemoryIdentity.Normalize(ownerId, nameof(ownerId), 128);
        SessionId = MemoryIdentity.Normalize(sessionId, nameof(sessionId), 128);
        Kind = kind;
        Content = (content ?? string.Empty).Trim();
        Role = MemoryIdentity.NormalizeOptional(role, nameof(role), 32);
        Summary = (summary ?? string.Empty).Trim();
        Tags = (tags ?? Array.Empty<string>()).ToArray();
        ProposedImportance = proposedImportance;
        ProposedConfidence = proposedConfidence;
        Sensitivity = sensitivity;
        OccurredAt = occurredAt.ToUniversalTime();
        RequestedAt = requestedAt.ToUniversalTime();
        SupersedesEventId = supersedesEventId;
    }
}

internal enum MemoryWriteRejectionReason
{
    None = 0,
    OwnerMismatch,
    Expired,
    FutureTimestamp,
    KindNotAllowed,
    ExplicitConsentRequired,
    SensitiveStorageDisabled,
    CredentialLikeContent,
    InvalidEvent,
    Duplicate,
    StoreConflict
}

internal sealed class MemoryWriteOutcome
{
    internal bool Stored { get; }
    internal Guid ProposalId { get; }
    internal Guid? EventId { get; }
    internal MemoryWriteRejectionReason RejectionReason { get; }

    internal MemoryWriteOutcome(
        bool stored,
        Guid proposalId,
        Guid? eventId,
        MemoryWriteRejectionReason rejectionReason)
    {
        Stored = stored;
        ProposalId = proposalId;
        EventId = eventId;
        RejectionReason = rejectionReason;
    }
}
