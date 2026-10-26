using System.IO;

namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
    : ID057MemoryManagementProjection
{
    public async Task<D057MemoryCatalog> GetMemoryCatalogAsync(
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = RequireMemoryManagementProtocol();
            var entries = new List<D057MemoryEntry>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            long? catalogRevision = null;
            bool? exportSupported = null;
            var offset = 0;

            while (true)
            {
                var binding = protocol.BindMemoryCatalog(offset);
                var page = await SendRequestAsync(
                    binding,
                    (response, envelope) => protocol.DecodeMemoryCatalog(
                        response,
                        envelope,
                        binding),
                    cancellationToken,
                    isDecodeFailureIsolated: true).ConfigureAwait(false);

                catalogRevision ??= page.CatalogRevision;
                exportSupported ??= page.ExportSupported;
                if (page.CatalogRevision != catalogRevision
                    || page.ExportSupported != exportSupported)
                {
                    throw new InvalidDataException(
                        "讀取記憶清單時資料已變更，請重新整理。");
                }
                foreach (var entry in page.Entries)
                {
                    if (!entryIds.Add(entry.EntryId))
                    {
                        throw new InvalidDataException(
                            "Hermes 回傳了跨頁重複的記憶識別。");
                    }
                    entries.Add(entry);
                }
                if (entries.Count > D057MemoryContractLimits.MaximumManagedEntries)
                {
                    throw new InvalidDataException("記憶清單超過安全上限。");
                }
                if (page.NextOffset is not { } nextOffset)
                {
                    return new D057MemoryCatalog(
                        catalogRevision.Value,
                        exportSupported.Value,
                        entries);
                }
                if (nextOffset <= offset)
                    throw new InvalidDataException("Hermes 記憶分頁沒有前進。");
                offset = nextOffset;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<D057MemoryPreferences> GetMemoryPreferencesAsync(
        CancellationToken cancellationToken)
        => ExecuteMemoryRequestAsync(
            static protocol => protocol.BindMemoryPreferences(),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryPreferences(response, envelope, binding),
            cancellationToken);

    public Task<D057MemoryMutationReceipt> AddMemoryAsync(
        D057MemoryAddRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryAdd(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryMutationReceipt> SetMemoryPinAsync(
        D057MemoryPinRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryPin(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryMutationReceipt> PromoteMemoryAsync(
        D057MemoryPromoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryPromote(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryMutationReceipt> CorrectMemoryAsync(
        D057MemoryCorrectRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryCorrect(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryMutationReceipt> SetMemoryVisibilityAsync(
        D057MemoryVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryVisibility(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryMutationReceipt> RejectMemoryAsync(
        D057MemoryEntryMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryReject(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryMutation(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryPermanentDeleteReceipt> PermanentlyDeleteMemoryAsync(
        D057MemoryPermanentDeleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryPermanentDelete(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryPermanentDelete(
                    response,
                    envelope,
                    binding),
            cancellationToken);
    }

    public Task<D057MemoryPreferences> SetMemoryPreferencesAsync(
        D057MemoryPreferencesSetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryPreferencesSet(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryPreferences(response, envelope, binding),
            cancellationToken);
    }

    public Task<D057MemoryExportReceipt> ExportMemoryAsync(
        D057MemoryExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteMemoryRequestAsync(
            protocol => protocol.BindMemoryExport(request),
            static (protocol, response, envelope, binding) =>
                protocol.DecodeMemoryExport(response, envelope, binding),
            cancellationToken);
    }

    private async Task<T> ExecuteMemoryRequestAsync<T>(
        Func<ID057MemoryManagementRuntimeProtocol,
            D046HermesRuntimeRequestBinding> bind,
        Func<
            ID057MemoryManagementRuntimeProtocol,
            LilithRuntimeContracts.LilithRuntimeResponse,
            D046HermesRuntimeEnvelopeContext,
            D046HermesRuntimeRequestBinding,
            T> decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        ArgumentNullException.ThrowIfNull(decode);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = RequireMemoryManagementProtocol();
            var binding = bind(protocol);
            return await SendRequestAsync(
                binding,
                (response, envelope) => decode(
                    protocol,
                    response,
                    envelope,
                    binding),
                cancellationToken,
                isDecodeFailureIsolated: true).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private ID057MemoryManagementRuntimeProtocol
        RequireMemoryManagementProtocol()
        => _protocol as ID057MemoryManagementRuntimeProtocol
            ?? throw new D057MemoryManagementUnavailableException(
                "memory.protocol_unavailable",
                "記憶管理目前不可用。");
}
