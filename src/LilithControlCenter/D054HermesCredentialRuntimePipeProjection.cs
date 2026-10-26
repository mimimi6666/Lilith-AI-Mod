namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
{
    public async Task<D054HermesCredentialCatalog> GetCredentialStatusAsync(
        CancellationToken cancellationToken)
    {
        using var timing = ReadDiagnostics.Begin(D046HermesReadOperation.CredentialStatus, cancellationToken);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing.Advance(D046HermesReadPhase.Readiness);
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            timing.Advance(D046HermesReadPhase.Response);
            return timing.Success(await GetCredentialStatusCoreAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Task<D054HermesCredentialCatalog> GetCredentialStatusCoreAsync(
        CancellationToken cancellationToken)
    {
        var protocol = RequireCredentialProtocol();
        var binding = protocol.BindCredentialStatus();
        return SendRequestAsync(
            binding,
            (response, envelope) => protocol.DecodeCredentialStatus(
                response,
                envelope,
                binding),
            cancellationToken,
            isDecodeFailureIsolated: true);
    }

    public async Task<D054HermesCredentialMutationReceipt> ReplaceCredentialAsync(
        string providerId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = RequireCredentialProtocol();
            var binding = protocol.BindCredentialReplace(
                providerId,
                apiKey,
                "cc-" + Guid.NewGuid().ToString("N"));
            return await SendRequestAsync(
                binding,
                (response, envelope) => protocol.DecodeCredentialMutation(
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

    public async Task<D054HermesCredentialMutationReceipt> RemoveCredentialAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = RequireCredentialProtocol();
            var binding = protocol.BindCredentialRemove(
                providerId,
                "cc-" + Guid.NewGuid().ToString("N"));
            return await SendRequestAsync(
                binding,
                (response, envelope) => protocol.DecodeCredentialMutation(
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

    private ID054HermesCredentialRuntimeProtocol RequireCredentialProtocol()
        => _protocol as ID054HermesCredentialRuntimeProtocol
            ?? throw new NotSupportedException(
                "目前無法管理 API Key。");
}
