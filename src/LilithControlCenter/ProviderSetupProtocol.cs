using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public interface IProviderSetupProjection
{
    Task<HermesProviderSetupCatalog> ReadProviderSetupAsync(CancellationToken cancellationToken);
    Task<HermesProviderSetupModels> ReadProviderModelsAsync(string providerId, string configSha256, CancellationToken cancellationToken);
    Task<HermesProviderSetupMutation> MutateProviderSetupAsync(string method, JsonElement request, CancellationToken cancellationToken);
}

public sealed class ProviderSetupOperationException : Exception
{
    public string Code { get; }
    public bool? MutationDispatched { get; }
    public ProviderSetupOperationException(string code, bool? mutationDispatched = null) : base("Provider setup operation was not confirmed.")
    { Code = code; MutationDispatched = mutationDispatched; }
}

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
{
    internal D046HermesRuntimeRequestBinding BindProviderSetup(string method, JsonElement request)
        => new(method, HermesProviderSetupContract.CanonicalizeRequest(method, request));

    private JsonElement RequireProviderSetupSuccess(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            if (!HermesProviderSetupContract.IsMethod(binding.Method) || response.Method != binding.Method)
                throw new InvalidDataException("Provider setup response mismatch.");
            if (!response.Succeeded)
            {
                HermesProviderSetupRuntimeError failure;
                try { failure = HermesProviderSetupContract.ParseRuntimeError(response.HermesPayload); }
                catch (InvalidDataException) { throw new ProviderSetupOperationException("setup.result_uncertain"); }
                throw new ProviderSetupOperationException(failure.Code, failure.MutationDispatched);
            }
            return response.HermesPayload;
        }
    }

    internal HermesProviderSetupCatalog DecodeProviderSetupCatalog(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
        => binding.Method == HermesProviderSetupContract.CatalogMethod
            ? HermesProviderSetupContract.ParseCatalog(RequireProviderSetupSuccess(response, envelope, binding))
            : throw new InvalidDataException("Provider setup method mismatch.");

    internal HermesProviderSetupModels DecodeProviderSetupModels(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        if (binding.Method != HermesProviderSetupContract.ModelsMethod) throw new InvalidDataException("Provider setup method mismatch.");
        var result = HermesProviderSetupContract.ParseModels(RequireProviderSetupSuccess(response, envelope, binding));
        if (result.ProviderId != binding.HermesPayload.GetProperty("provider_id").GetString())
            throw new InvalidDataException("Provider model catalog identity mismatch.");
        return result;
    }

    internal HermesProviderSetupMutation DecodeProviderSetupMutation(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
        => HermesProviderSetupContract.ParseMutation(RequireProviderSetupSuccess(response, envelope, binding),
            binding.Method, binding.HermesPayload);
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : IProviderSetupProjection
{
    private Guid _providerSetupGeneration;
    private void CaptureProviderSetupHello(LilithRuntimeHello hello)
        => _providerSetupGeneration = new[] { HermesProviderSetupContract.CatalogMethod, HermesProviderSetupContract.ModelsMethod,
            HermesProviderSetupContract.SaveMethod, HermesProviderSetupContract.RemoveMethod }
            .All(method => hello.Capabilities.Contains(method, StringComparer.Ordinal)) ? _runtimeGeneration : Guid.Empty;

    private Guid RequireProviderSetupGeneration(Guid? expected = null)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (_providerSetupGeneration == Guid.Empty || _providerSetupGeneration != _runtimeGeneration)
                throw new ProviderSetupOperationException("setup.unsupported");
            if (expected.HasValue && expected != _runtimeGeneration)
                throw new ProviderSetupOperationException("setup.result_uncertain");
            return _runtimeGeneration;
        }
    }

    public async Task<HermesProviderSetupCatalog> ReadProviderSetupAsync(CancellationToken cancellationToken)
    {
        // Setup has native config authority. It is not part of the independent
        // parallel catalog lane; serialize it with the ordinary request gate.
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var generation = RequireProviderSetupGeneration();
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = protocol.BindProviderSetup(HermesProviderSetupContract.CatalogMethod, JsonSerializer.SerializeToElement(new { }));
            return await SendRequestAsync(binding, (r, e) =>
            {
                UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
                return protocol.DecodeProviderSetupCatalog(r, e, binding);
            }, cancellationToken, isDecodeFailureIsolated: true,
                validateStateLocked: () => RequireProviderSetupGeneration(generation)).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<HermesProviderSetupModels> ReadProviderModelsAsync(string providerId, string configSha256,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var generation = RequireProviderSetupGeneration();
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = protocol.BindProviderSetup(HermesProviderSetupContract.ModelsMethod,
                JsonSerializer.SerializeToElement(new { provider_id = providerId, expected_config_sha256 = configSha256 }));
            return await SendRequestAsync(binding, (r, e) =>
            {
                UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
                return protocol.DecodeProviderSetupModels(r, e, binding);
            }, cancellationToken, isDecodeFailureIsolated: true,
                validateStateLocked: () => RequireProviderSetupGeneration(generation)).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<HermesProviderSetupMutation> MutateProviderSetupAsync(string method, JsonElement request,
        CancellationToken cancellationToken)
    {
        if (!HermesProviderSetupContract.IsMutation(method)) throw new ArgumentException("Provider setup method rejected.", nameof(method));
        // Validate locally before obtaining or sending any write. Never retry a mutation.
        var canonical = HermesProviderSetupContract.CanonicalizeRequest(method, request);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var generation = RequireProviderSetupGeneration();
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = protocol.BindProviderSetup(method, canonical);
            return await SendRequestAsync(binding, (r, e) =>
            {
                UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
                return protocol.DecodeProviderSetupMutation(r, e, binding);
            }, cancellationToken, responseTimeout: TimeSpan.FromSeconds(90), isDecodeFailureIsolated: true,
                validateStateLocked: () => RequireProviderSetupGeneration(generation)).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }
}
