using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private async Task<LilithRuntimeResponse> RouteNativeSettingsAsync(string method, JsonElement parameters,
        LilithRuntimeModelAuthorityLease authority, CancellationToken cancellationToken)
    {
        if (_sessionSurface != LilithRuntimeSessionSurface.Workbench || _profile is null)
            return Failure(method, "runtime.workbench_surface_required", "Native settings require the authenticated Workbench.");
        var rejected = NativeModelAuthorityMutationFailure(method, authority.Snapshot);
        if (rejected is not null) return rejected;
        if (method == "tools.settings.get")
            return await ReadNativeSettingsAsync(cancellationToken).ConfigureAwait(false);
        lock (_sessionLock)
        {
            if (VoiceBlocksConversationMutation() || CheckpointRestoreBlocksConversationMutation()
                || _sessionLifecycleInFlight
                || _sessionTurns.Values.Any(turn => turn.AwaitingReceipt || turn.AwaitingQueuedTurn || turn.ActiveAssistantMessageIds.Count != 0))
                return Failure(method, "runtime.settings_busy", "Wait for the current operation before saving settings.");
        }
        var expected = parameters.GetProperty("expected_config_sha256").GetString()!;
        var result = await RouteNativeToolsSetAsync(parameters, cancellationToken, authority, expected).ConfigureAwait(false);
        return new LilithRuntimeResponse(method, result.Succeeded, result.HermesPayload);
    }

    private async Task<LilithRuntimeResponse> ReadNativeSettingsAsync(CancellationToken cancellationToken)
    {
        const string method = "tools.settings.get";
        if (_profile is null)
            return Failure(method, "runtime.workbench_surface_required", "Native settings require the authenticated profile.");
        var before = await LilithNativeConfigMutationReceiptStore.CaptureCurrentAsync(_hermesHomePath,
            _profile.CanonicalLearningConfigSha256, _profile.IdentityFingerprintSha256,
            _profile.SoulSha256, _profile.HermesPersonalitySha256, cancellationToken).ConfigureAwait(false);
        var catalog = await _requestAsync("tools.catalog", Json("{}"), cancellationToken).ConfigureAwait(false);
        _ = HermesNativeToolsContract.DecodeCatalog(catalog);
        var after = await LilithNativeConfigMutationReceiptStore.ReadConfigSha256Async(_hermesHomePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.ConfigSha256, after, StringComparison.Ordinal))
            return Failure(method, "runtime.settings_changed", "Native settings changed while reading; refresh required.");
        return Success(method, JsonSerializer.SerializeToElement(new { config_sha256 = after, catalog }));
    }
}
