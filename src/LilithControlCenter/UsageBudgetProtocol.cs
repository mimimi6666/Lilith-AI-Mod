using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal static class UsageBudgetProtocolGuard
{
    internal static bool Published(IEnumerable<string> methods)
        => methods.Contains(HermesUsageBudgetContract.GetMethod, StringComparer.Ordinal)
            && methods.Contains(HermesUsageBudgetContract.SetMethod, StringComparer.Ordinal);
    internal static void RequireGeneration(Guid expected, Guid actual)
    {
        if (expected == Guid.Empty || expected != actual)
            throw new UsageBudgetOperationException("budget.result_uncertain");
    }
}

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
{
    internal D046HermesRuntimeRequestBinding BindUsageBudgetGet()
        => new(HermesUsageBudgetContract.GetMethod, HermesUsageBudgetContract.GetRequest());
    internal D046HermesRuntimeRequestBinding BindUsageBudgetPreview()
        => new(HermesUsageBudgetPreviewContract.PreviewMethod, HermesUsageBudgetContract.GetRequest());
    internal D046HermesRuntimeRequestBinding BindUsageBudgetSet(HermesUsageBudgetUpdate request)
        => new(HermesUsageBudgetContract.SetMethod, HermesUsageBudgetContract.SetRequest(
            request.OperationId, request.ExpectedRevision, request.Level));

    private JsonElement RequireUsageBudgetSuccess(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            if (response.Method != binding.Method || binding.Method is not
                (HermesUsageBudgetContract.GetMethod or HermesUsageBudgetContract.SetMethod or HermesUsageBudgetPreviewContract.PreviewMethod))
                throw new InvalidDataException("Economic preference response mismatch.");
            if (!response.Succeeded)
            {
                var code = response.HermesPayload.ValueKind == JsonValueKind.Object
                    && response.HermesPayload.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
                throw new UsageBudgetOperationException(code switch
                {
                    "budget.revision_conflict" or "budget.operation_conflict" => "budget.revision_conflict",
                    "budget.host_required" => "budget.host_required",
                    _ => "budget.result_uncertain"
                });
            }
            return response.HermesPayload;
        }
    }

    internal HermesUsageBudgetPreference DecodeUsageBudgetGet(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
        => HermesUsageBudgetContract.ParsePreference(RequireUsageBudgetSuccess(response, envelope, binding));

    internal HermesUsageBudgetPreview DecodeUsageBudgetPreview(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
        => HermesUsageBudgetPreviewContract.Parse(RequireUsageBudgetSuccess(response, envelope, binding));

    internal HermesUsageBudgetCommit DecodeUsageBudgetSet(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
        => HermesUsageBudgetContract.ParseCommit(RequireUsageBudgetSuccess(response, envelope, binding),
            HermesUsageBudgetContract.ParseUpdate(binding.HermesPayload));
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : IUsageBudgetProjection
{
    private Guid _usageBudgetCapabilityGeneration;
    private bool _usageBudgetPreviewPublished;
    private TimeSpan UsageBudgetReadTimeout => _responseTimeout < TimeSpan.FromSeconds(5)
        ? _responseTimeout : TimeSpan.FromSeconds(5);

    private void CaptureUsageBudgetHello(LilithRuntimeHello hello)
    {
        // Called under the state gate, after canonical identity/readiness validation.
        // Runtime publishes this closed human-management surface in its authenticated
        // hello. Reading one preference must not scan models, tools or plug-ins.
        _usageBudgetCapabilityGeneration = UsageBudgetProtocolGuard.Published(hello.Capabilities)
            ? _runtimeGeneration : Guid.Empty;
        _usageBudgetPreviewPublished = hello.Capabilities.Contains(HermesUsageBudgetPreviewContract.PreviewMethod,
            StringComparer.Ordinal);
    }

    public async Task<UsageBudgetSnapshot> ReadUsageBudgetAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Guid generation;
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (_usageBudgetCapabilityGeneration == Guid.Empty) return new(false, null);
            generation = RequireUsageBudgetCapability();
        }
        return new(true, await ReadUsageBudgetPreferenceAsync(generation, cancellationToken).ConfigureAwait(false));
    }

    public async Task<HermesUsageBudgetPreview?> ReadUsageBudgetPreviewAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Guid generation;
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (!_usageBudgetPreviewPublished || _usageBudgetCapabilityGeneration == Guid.Empty) return null;
            generation = RequireUsageBudgetCapability();
        }
        var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
        var binding = protocol.BindUsageBudgetPreview();
        return await SendCatalogReadAsync(binding, (r, e) =>
        {
            UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
            return protocol.DecodeUsageBudgetPreview(r, e, binding);
        }, cancellationToken, responseTimeout: UsageBudgetReadTimeout,
            validateStateLocked: () => RequireUsageBudgetCapability(generation)).ConfigureAwait(false);
    }

    private Guid RequireUsageBudgetCapability(Guid? expected = null)
    {
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (_usageBudgetCapabilityGeneration == Guid.Empty || _usageBudgetCapabilityGeneration != _runtimeGeneration)
                throw new UsageBudgetOperationException("budget.unsupported");
            if (expected is { } generation) UsageBudgetProtocolGuard.RequireGeneration(generation, _runtimeGeneration);
            return _runtimeGeneration;
        }
    }

    private async Task<HermesUsageBudgetPreference> ReadUsageBudgetPreferenceAsync(Guid generation, CancellationToken cancellationToken)
    {
        RequireUsageBudgetCapability(generation);
        var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
        var binding = protocol.BindUsageBudgetGet();
        return await SendCatalogReadAsync(binding, (r, e) =>
        {
            UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
            return protocol.DecodeUsageBudgetGet(r, e, binding);
        }, cancellationToken, responseTimeout: UsageBudgetReadTimeout,
            validateStateLocked: () => RequireUsageBudgetCapability(generation)).ConfigureAwait(false);
    }

    public async Task<HermesUsageBudgetCommit> SaveUsageBudgetAsync(HermesUsageBudgetUpdate request,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var generation = RequireUsageBudgetCapability();
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = protocol.BindUsageBudgetSet(request);
            var commit = await SendRequestAsync(binding, (r, e) =>
            {
                UsageBudgetProtocolGuard.RequireGeneration(generation, e.GenerationId);
                return protocol.DecodeUsageBudgetSet(r, e, binding);
            }, cancellationToken, isDecodeFailureIsolated: true,
                validateStateLocked: () => RequireUsageBudgetCapability(generation)).ConfigureAwait(false);
            if (await ReadUsageBudgetPreferenceAsync(generation, cancellationToken).ConfigureAwait(false) != commit.Preference)
                throw new UsageBudgetOperationException("budget.result_uncertain");
            // No session/config/model/tool invalidation or mutation retry.
            return commit;
        }
        finally { _operationGate.Release(); }
    }
}
