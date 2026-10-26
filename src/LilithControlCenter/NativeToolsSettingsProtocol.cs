using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed record NativeToolsSettingsSnapshot(string ConfigSha256, HermesNativeToolsCatalog Catalog);
public interface INativeToolsSettingsProjection
{
    Task<NativeToolsSettingsSnapshot> GetNativeSettingsAsync(CancellationToken cancellationToken);
    Task<HermesNativeToolsSetResult> SetNativeSettingsAsync(string expectedConfigSha256,
        HermesNativeToolsSetRequest request, CancellationToken cancellationToken);
}
public sealed class NativeSettingsOperationException(string code, bool uncertain) : Exception(code)
{
    public string Code { get; } = code;
    public bool Uncertain { get; } = uncertain;
}

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
{
    internal D046HermesRuntimeRequestBinding BindNativeSettingsGet() => Bind("tools.settings.get", new { });
    internal D046HermesRuntimeRequestBinding BindNativeSettingsSet(string expected, HermesNativeToolsSetRequest request)
    {
        if (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Invalid native config digest.");
        var values = HermesNativeToolsContract.EncodeSetRequest(request).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        values.Add("expected_config_sha256", JsonSerializer.SerializeToElement(expected));
        return Bind("tools.settings.set", values);
    }
    private JsonElement RequireNativeSettingsSuccess(LilithRuntimeResponse response, D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            if (response.Method != binding.Method || binding.Method is not ("tools.settings.get" or "tools.settings.set"))
                throw new InvalidDataException("Native settings response mismatch.");
            if (!response.Succeeded)
            {
                var code = response.HermesPayload.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "runtime.settings_failed" : "runtime.settings_failed";
                throw new NativeSettingsOperationException(code, code is not ("runtime.settings_changed" or "runtime.settings_busy"
                    or "runtime.workbench_surface_required" or "hermes.request_rejected" or "hermes.busy"));
            }
            return response.HermesPayload;
        }
    }
    internal NativeToolsSettingsSnapshot DecodeNativeSettingsGet(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireNativeSettingsSuccess(response, envelope, binding);
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 2)
            throw new InvalidDataException("Native settings catalog shape mismatch.");
        var hash = payload.GetProperty("config_sha256").GetString() ?? "";
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid native config digest.");
        return new(hash, HermesNativeToolsContract.DecodeCatalog(payload.GetProperty("catalog")));
    }
    internal HermesNativeToolsSetResult DecodeNativeSettingsSet(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding, HermesNativeToolsSetRequest request)
    {
        var result = HermesNativeToolsContract.DecodeSetResult(RequireNativeSettingsSuccess(response, envelope, binding));
        if (result.ApprovalMode != request.ApprovalMode || result.SetupSelection != request.SetupSelection || result.McpSetup != request.McpSetup
            || request.ToolDiscoveryEnabled.HasValue && result.ToolDiscoveryEnabled != request.ToolDiscoveryEnabled
            || !result.EnabledToolsets.ToHashSet(StringComparer.Ordinal).SetEquals(request.EnabledToolsets))
            throw new InvalidDataException("Native settings receipt mismatch.");
        lock (_gate)
        {
            foreach (var task in _byTaskId.Values)
            {
                task.NativeToolsConfigStale = true;
                task.NativeToolsConfigStaleTurnActive = false;
                task.NativeApprovalOnlyReactivation = false;
                task.NoEffectAttestation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            if (_selectedTaskId is not null && _byTaskId.TryGetValue(_selectedTaskId, out var selected))
                _runtimeInvalidatedNativeToolsSessionId = selected.RuntimeSessionId;
        }
        return result;
    }
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : INativeToolsSettingsProjection
{
    public async Task<NativeToolsSettingsSnapshot> GetNativeSettingsAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
        var binding = protocol.BindNativeSettingsGet();
        return await SendCatalogReadAsync(binding, (r, e) => protocol.DecodeNativeSettingsGet(r, e, binding),
            cancellationToken).ConfigureAwait(false);
    }
    public async Task<HermesNativeToolsSetResult> SetNativeSettingsAsync(string expectedConfigSha256,
        HermesNativeToolsSetRequest request, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = protocol.BindNativeSettingsSet(expectedConfigSha256, request);
            return await SendRequestAsync(binding, (r, e) => protocol.DecodeNativeSettingsSet(r, e, binding, request), cancellationToken,
                isDecodeFailureIsolated: true).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }
}
