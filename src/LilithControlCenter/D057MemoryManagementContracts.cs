using System.Collections.ObjectModel;

namespace LilithControlCenter;

public enum D057MemoryCategory
{
    RelationshipCore = 1,
    LongTerm = 2,
    Candidate = 3,
    Temporary = 4,
    RelationshipEpisode = 5
}

public enum D057MemoryState
{
    Active = 1,
    Hidden = 2,
    Rejected = 3,
    Expired = 4,
    Superseded = 5
}

public enum D057MemorySource
{
    ExplicitUser = 1,
    Inferred = 2,
    Operator = 3,
    RelationshipReview = 4
}

public sealed record D057MemoryEntry(
    string EntryId,
    long EntryVersion,
    D057MemoryCategory Category,
    D057MemoryState State,
    string Content,
    D057MemorySource Source,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool Pinned,
    string? EvidenceExcerpt);

public sealed class D057MemoryCatalog
{
    private readonly IReadOnlyList<D057MemoryEntry> _entries;

    public long CatalogRevision { get; }
    public bool ExportSupported { get; }
    public IReadOnlyList<D057MemoryEntry> Entries => _entries;
    internal int? NextOffset { get; }

    public D057MemoryCatalog(
        long catalogRevision,
        bool exportSupported,
        IEnumerable<D057MemoryEntry> entries,
        int? nextOffset = null)
    {
        if (catalogRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(catalogRevision));
        ArgumentNullException.ThrowIfNull(entries);
        var frozen = entries.ToArray();
        if (frozen.Length > D057MemoryContractLimits.MaximumManagedEntries
            || frozen.Any(static value => value is null)
            || frozen.Select(static value => value.EntryId)
                .Distinct(StringComparer.Ordinal)
                .Count() != frozen.Length
            || nextOffset is <= 0
            || nextOffset > D057MemoryContractLimits.MaximumManagedEntries)
        {
            throw new ArgumentException(
                "Hermes memory catalog was rejected.",
                nameof(entries));
        }

        CatalogRevision = catalogRevision;
        ExportSupported = exportSupported;
        _entries = new ReadOnlyCollection<D057MemoryEntry>(frozen);
        NextOffset = nextOffset;
    }
}

public sealed record D057MemoryPreferences(
    long PreferencesRevision,
    bool AutoMemoryEnabled);

public sealed record D057MemoryAddRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    D057MemoryCategory Category,
    string Content,
    bool Pinned);

public sealed record D057MemoryEntryMutationRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion);

public sealed record D057MemoryPinRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion,
    bool Pinned);

public sealed record D057MemoryPromoteRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion,
    D057MemoryCategory TargetCategory);

public sealed record D057MemoryCorrectRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion,
    string ReplacementContent);

public sealed record D057MemoryVisibilityRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion,
    bool Hidden);

public sealed record D057MemoryPermanentDeleteRequest(
    string OperationId,
    long ExpectedCatalogRevision,
    string EntryId,
    long ExpectedEntryVersion);

public sealed record D057MemoryPreferencesSetRequest(
    string OperationId,
    long ExpectedPreferencesRevision,
    bool AutoMemoryEnabled);

public sealed record D057MemoryExportRequest(
    string OperationId,
    long ExpectedCatalogRevision);

public sealed record D057MemoryMutationReceipt(
    string OperationId,
    long CatalogRevision,
    D057MemoryEntry Entry);

public sealed record D057MemoryPermanentDeleteReceipt(
    string OperationId,
    long CatalogRevision,
    string EntryId,
    bool Deleted);

public sealed record D057MemoryExportReceipt(
    string OperationId,
    string Path,
    string Sha256,
    int EntryCount,
    DateTimeOffset CreatedAtUtc);

public interface ID057MemoryManagementProjection
{
    Task<D057MemoryCatalog> GetMemoryCatalogAsync(
        CancellationToken cancellationToken);

    Task<D057MemoryPreferences> GetMemoryPreferencesAsync(
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> AddMemoryAsync(
        D057MemoryAddRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> SetMemoryPinAsync(
        D057MemoryPinRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> PromoteMemoryAsync(
        D057MemoryPromoteRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> CorrectMemoryAsync(
        D057MemoryCorrectRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> SetMemoryVisibilityAsync(
        D057MemoryVisibilityRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryMutationReceipt> RejectMemoryAsync(
        D057MemoryEntryMutationRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryPermanentDeleteReceipt> PermanentlyDeleteMemoryAsync(
        D057MemoryPermanentDeleteRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryPreferences> SetMemoryPreferencesAsync(
        D057MemoryPreferencesSetRequest request,
        CancellationToken cancellationToken);

    Task<D057MemoryExportReceipt> ExportMemoryAsync(
        D057MemoryExportRequest request,
        CancellationToken cancellationToken);
}

internal interface ID057MemoryManagementRuntimeProtocol
{
    D046HermesRuntimeRequestBinding BindMemoryCatalog(int offset);
    D046HermesRuntimeRequestBinding BindMemoryPreferences();
    D046HermesRuntimeRequestBinding BindMemoryAdd(D057MemoryAddRequest request);
    D046HermesRuntimeRequestBinding BindMemoryPin(D057MemoryPinRequest request);
    D046HermesRuntimeRequestBinding BindMemoryPromote(
        D057MemoryPromoteRequest request);
    D046HermesRuntimeRequestBinding BindMemoryCorrect(
        D057MemoryCorrectRequest request);
    D046HermesRuntimeRequestBinding BindMemoryVisibility(
        D057MemoryVisibilityRequest request);
    D046HermesRuntimeRequestBinding BindMemoryReject(
        D057MemoryEntryMutationRequest request);
    D046HermesRuntimeRequestBinding BindMemoryPermanentDelete(
        D057MemoryPermanentDeleteRequest request);
    D046HermesRuntimeRequestBinding BindMemoryPreferencesSet(
        D057MemoryPreferencesSetRequest request);
    D046HermesRuntimeRequestBinding BindMemoryExport(
        D057MemoryExportRequest request);

    D057MemoryCatalog DecodeMemoryCatalog(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D057MemoryPreferences DecodeMemoryPreferences(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D057MemoryMutationReceipt DecodeMemoryMutation(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D057MemoryPermanentDeleteReceipt DecodeMemoryPermanentDelete(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D057MemoryExportReceipt DecodeMemoryExport(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);
}

public sealed class D057MemoryManagementUnavailableException : Exception
{
    public string Code { get; }

    internal D057MemoryManagementUnavailableException(
        string code,
        string message)
        : base(message)
        => Code = string.IsNullOrWhiteSpace(code)
            ? "memory.unavailable"
            : code;
}

public sealed class D057MemoryManagementOperationException : Exception
{
    public string Code { get; }

    internal D057MemoryManagementOperationException(
        string code,
        string message)
        : base(message)
        => Code = string.IsNullOrWhiteSpace(code)
            ? "memory.operation_failed"
            : code;
}

internal static class D057MemoryContractLimits
{
    internal const int MaximumCatalogEntries = 512;
    internal const int MaximumManagedEntries = 1_000_000;
    internal const int MaximumExportEntries = 1_000_000;
    internal const int MaximumContentUtf8Bytes = 16 * 1024;
    internal const int MaximumEvidenceUtf8Bytes = 4 * 1024;
    internal const int MaximumEntryIdCharacters = 256;
    internal const int MaximumOperationIdCharacters = 128;
}
