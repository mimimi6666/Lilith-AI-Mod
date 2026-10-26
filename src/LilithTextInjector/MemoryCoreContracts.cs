using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum MemoryEventKind
{
    ConversationTurn = 1,
    Episode = 2,
    Fact = 3,
    Preference = 4,
    Relationship = 5,
    Commitment = 6,
    Summary = 7,
    Tombstone = 8,
    MigrationMarker = 9
}

internal enum MemorySensitivity
{
    Normal = 0,
    Personal = 1,
    Sensitive = 2
}

/// <summary>
/// Policy-facing identity for an existing schema-v1 source string. This is a
/// compatible projection: it does not rewrite stored events or make display
/// strings authoritative.
/// </summary>
internal enum MemoryProvenanceKind
{
    Unknown = 0,
    LocalConversation = 1,
    PlayerExplicit = 2,
    BuiltInModel = 3,
    RemoteAgent = 4,
    Import = 5,
    Consolidation = 6,
    Migration = 7
}

internal sealed class MemoryProvenance
{
    private const string ProposalPrefix = "proposal:";
    internal const string LocalConversationProducerId =
        "local.runtime.chat";

    internal MemoryProvenanceKind Kind { get; }
    internal string ProducerId { get; }
    internal string StoredSource { get; }

    private MemoryProvenance(
        MemoryProvenanceKind kind,
        string producerId,
        string storedSource)
    {
        Kind = kind;
        ProducerId = producerId;
        StoredSource = storedSource;
    }

    internal static MemoryProvenance FromProposal(
        MemoryProposalSource source,
        string producerId)
    {
        var normalizedProducer = MemoryIdentity.Normalize(
            producerId,
            nameof(producerId),
            128);
        return new MemoryProvenance(
            KindFromProposal(source),
            normalizedProducer,
            ProposalPrefix + source + ":" + normalizedProducer);
    }

    internal static MemoryProvenance FromStoredSource(string? source)
    {
        var normalized = (source ?? string.Empty).Trim();
        if (string.Equals(
                normalized,
                LocalConversationProducerId,
                StringComparison.Ordinal))
        {
            return new MemoryProvenance(
                MemoryProvenanceKind.LocalConversation,
                LocalConversationProducerId,
                normalized);
        }

        if (normalized.StartsWith(ProposalPrefix, StringComparison.Ordinal))
        {
            var kindStart = ProposalPrefix.Length;
            var producerSeparator = normalized.IndexOf(':', kindStart);
            if (producerSeparator > kindStart
                && producerSeparator < normalized.Length - 1)
            {
                var proposalKind = normalized.Substring(
                    kindStart,
                    producerSeparator - kindStart);
                var producerId = normalized.Substring(
                    producerSeparator + 1);
                return new MemoryProvenance(
                    KindFromProposalName(proposalKind),
                    producerId,
                    normalized);
            }
        }

        var compatibilityKind = normalized switch
        {
            "legacy.memory.json" => MemoryProvenanceKind.Import,
            "memory.consolidation" => MemoryProvenanceKind.Consolidation,
            "memory.migration" => MemoryProvenanceKind.Migration,
            _ => MemoryProvenanceKind.Unknown
        };
        return new MemoryProvenance(
            compatibilityKind,
            normalized,
            normalized);
    }

    internal bool Matches(
        MemoryProvenanceKind kind,
        string producerId)
        => Kind == kind
            && string.Equals(
                ProducerId,
                producerId,
                StringComparison.Ordinal);

    private static MemoryProvenanceKind KindFromProposal(
        MemoryProposalSource source)
        => source switch
        {
            MemoryProposalSource.LocalRuntime =>
                MemoryProvenanceKind.LocalConversation,
            MemoryProposalSource.UserExplicit =>
                MemoryProvenanceKind.PlayerExplicit,
            MemoryProposalSource.BuiltInModel =>
                MemoryProvenanceKind.BuiltInModel,
            MemoryProposalSource.RemoteAgent =>
                MemoryProvenanceKind.RemoteAgent,
            _ => MemoryProvenanceKind.Unknown
        };

    private static MemoryProvenanceKind KindFromProposalName(string source)
        => source switch
        {
            nameof(MemoryProposalSource.LocalRuntime) =>
                MemoryProvenanceKind.LocalConversation,
            nameof(MemoryProposalSource.UserExplicit) =>
                MemoryProvenanceKind.PlayerExplicit,
            nameof(MemoryProposalSource.BuiltInModel) =>
                MemoryProvenanceKind.BuiltInModel,
            nameof(MemoryProposalSource.RemoteAgent) =>
                MemoryProvenanceKind.RemoteAgent,
            _ => MemoryProvenanceKind.Unknown
        };
}

internal sealed class MemoryEvent
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumContentLength = 32_768;
    internal const int MaximumSummaryLength = 2_048;
    internal const int MaximumTagCount = 16;

    internal Guid Id { get; }
    internal int SchemaVersion { get; }
    internal string OwnerId { get; }
    internal string SessionId { get; }
    internal MemoryEventKind Kind { get; }
    internal string Content { get; }
    internal string Summary { get; }
    internal string Source { get; }
    internal string Role { get; }
    internal IReadOnlyList<string> Tags { get; }
    internal double Importance { get; }
    internal double Confidence { get; }
    internal MemorySensitivity Sensitivity { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset RecordedAt { get; }
    internal Guid? SupersedesEventId { get; }

    internal MemoryEvent(
        Guid id,
        string ownerId,
        string sessionId,
        MemoryEventKind kind,
        string content,
        string source,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string? role = null,
        string? summary = null,
        IEnumerable<string>? tags = null,
        double importance = 0.5,
        double confidence = 1.0,
        MemorySensitivity sensitivity = MemorySensitivity.Personal,
        Guid? supersedesEventId = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A memory event ID cannot be empty.", nameof(id));
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported memory event schema version.");
        if (!Enum.IsDefined(typeof(MemoryEventKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(MemorySensitivity), sensitivity))
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (importance < 0 || importance > 1 || double.IsNaN(importance))
            throw new ArgumentOutOfRangeException(nameof(importance), "Importance must be between zero and one.");
        if (confidence < 0 || confidence > 1 || double.IsNaN(confidence))
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");
        if (supersedesEventId == id)
            throw new ArgumentException("A memory event cannot supersede itself.", nameof(supersedesEventId));

        Id = id;
        SchemaVersion = schemaVersion;
        OwnerId = MemoryIdentity.Normalize(ownerId, nameof(ownerId), 128);
        SessionId = MemoryIdentity.Normalize(sessionId, nameof(sessionId), 128);
        Kind = kind;
        Content = NormalizeText(content, nameof(content), MaximumContentLength, allowEmpty: false);
        Summary = NormalizeText(summary, nameof(summary), MaximumSummaryLength, allowEmpty: true);
        Source = MemoryIdentity.Normalize(source, nameof(source), 128);
        Role = MemoryIdentity.NormalizeOptional(role, nameof(role), 32);
        Tags = NormalizeTags(tags);
        Importance = importance;
        Confidence = confidence;
        Sensitivity = sensitivity;
        OccurredAt = NormalizeTimestamp(occurredAt, nameof(occurredAt));
        RecordedAt = NormalizeTimestamp(recordedAt, nameof(recordedAt));
        SupersedesEventId = supersedesEventId;
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value, string parameterName)
    {
        if (value == default || value.Year < 2000 || value.Year > 9998)
            throw new ArgumentOutOfRangeException(parameterName, "Memory timestamps must be explicit and valid.");
        return value.ToUniversalTime();
    }

    private static string NormalizeText(
        string? value,
        string parameterName,
        int maximumLength,
        bool allowEmpty)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (!allowEmpty && normalized.Length == 0)
            throw new ArgumentException("Memory text cannot be empty.", parameterName);
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"Memory text exceeds {maximumLength} characters.", parameterName);
        if (normalized.IndexOf('\0') >= 0)
            throw new ArgumentException("Memory text cannot contain null characters.", parameterName);
        return normalized;
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags == null)
            return Array.Empty<string>();
        var normalized = tags
            .Select(tag => MemoryIdentity.Normalize(tag, nameof(tags), 64))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length > MaximumTagCount)
            throw new ArgumentException($"A memory event may have at most {MaximumTagCount} tags.", nameof(tags));
        return new ReadOnlyCollection<string>(normalized);
    }
}

internal static class MemoryIdentity
{
    internal static string Normalize(string? value, string parameterName, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"Identifier exceeds {maximumLength} characters.", parameterName);
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Identifier cannot contain control characters.", parameterName);
        return normalized;
    }

    internal static string NormalizeOptional(string? value, string parameterName, int maximumLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Normalize(value, parameterName, maximumLength);
}

internal sealed class MemoryQuery
{
    internal string OwnerId { get; }
    internal string SessionId { get; }
    internal string Text { get; }
    internal IReadOnlyCollection<MemoryEventKind> Kinds { get; }
    internal int MaximumResults { get; }
    internal bool IncludeInactive { get; }
    internal bool IncludeTombstones { get; }

    internal MemoryQuery(
        string ownerId,
        string? sessionId = null,
        string? text = null,
        IEnumerable<MemoryEventKind>? kinds = null,
        int maximumResults = 20,
        bool includeInactive = false,
        bool includeTombstones = false)
    {
        OwnerId = MemoryIdentity.Normalize(ownerId, nameof(ownerId), 128);
        SessionId = MemoryIdentity.NormalizeOptional(sessionId, nameof(sessionId), 128);
        Text = (text ?? string.Empty).Trim();
        if (Text.Length > 1_024 || Text.IndexOf('\0') >= 0)
            throw new ArgumentException("Memory query text is invalid.", nameof(text));
        if (maximumResults < 1 || maximumResults > 200)
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "Maximum results must be between 1 and 200.");
        var normalizedKinds = kinds?.Distinct().ToArray() ?? Array.Empty<MemoryEventKind>();
        if (normalizedKinds.Any(kind => !Enum.IsDefined(typeof(MemoryEventKind), kind)))
            throw new ArgumentOutOfRangeException(nameof(kinds));
        Kinds = new ReadOnlyCollection<MemoryEventKind>(normalizedKinds);
        MaximumResults = maximumResults;
        IncludeInactive = includeInactive;
        IncludeTombstones = includeTombstones;
    }
}

internal enum MemoryAppendStatus
{
    Added = 0,
    Duplicate = 1,
    Conflict = 2
}

internal sealed class MemoryAppendResult
{
    internal Guid EventId { get; }
    internal MemoryAppendStatus Status { get; }

    internal MemoryAppendResult(Guid eventId, MemoryAppendStatus status)
    {
        EventId = eventId;
        Status = status;
    }
}

internal sealed class MemoryStoreHealth
{
    internal int ValidEventCount { get; }
    internal int CorruptEventCount { get; }
    internal int OrphanTemporaryFileCount { get; }
    internal IReadOnlyList<string> CorruptFileNames { get; }

    internal bool IsHealthy => CorruptEventCount == 0 && OrphanTemporaryFileCount == 0;

    internal MemoryStoreHealth(
        int validEventCount,
        int corruptEventCount,
        int orphanTemporaryFileCount,
        IEnumerable<string>? corruptFileNames = null)
    {
        ValidEventCount = Math.Max(0, validEventCount);
        CorruptEventCount = Math.Max(0, corruptEventCount);
        OrphanTemporaryFileCount = Math.Max(0, orphanTemporaryFileCount);
        CorruptFileNames = new ReadOnlyCollection<string>(
            (corruptFileNames ?? Array.Empty<string>()).ToArray());
    }
}

internal interface IMemoryEventStore
{
    ValueTask<MemoryAppendResult> AppendAsync(
        MemoryEvent memoryEvent,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MemoryEvent>> QueryAsync(
        MemoryQuery query,
        CancellationToken cancellationToken);

    ValueTask<MemoryStoreHealth> InspectAsync(CancellationToken cancellationToken);
}
