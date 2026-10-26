using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private const string ProviderSetupAuthority = "lilith-provider-setup-v1";

    private async Task<LilithRuntimeResponse> RouteProviderSetupAsync(string method, JsonElement parameters,
        LilithRuntimeModelAuthorityLease authority, CancellationToken cancellationToken)
    {
        if (_sessionSurface != LilithRuntimeSessionSurface.Workbench || _profile is null)
            return Failure(method, "runtime.workbench_surface_required",
                "Provider setup requires the authenticated management surface.");
        var authorityFailure = NativeModelAuthorityMutationFailure(method, authority.Snapshot);
        if (authorityFailure is not null) return authorityFailure;
        var mutation = HermesProviderSetupContract.IsMutation(method);
        if (mutation)
        {
            lock (_sessionLock)
            {
                if (VoiceBlocksConversationMutation() || CheckpointRestoreBlocksConversationMutation()
                    || _sessionLifecycleInFlight || _sessionActivationQuarantined || _sessionDeleteQuarantined
                    || _sessionImages.Values.Any(image => image.State != SessionImageLifecycleState.Idle)
                    || _sessionTurns.Values.Any(turn => turn.AwaitingReceipt || turn.AwaitingQueuedTurn
                        || turn.ActiveAssistantMessageIds.Count != 0))
                    return ProviderSetupFailure(method, "setup.busy");
            }
        }
        await _nativeConfigGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var dispatched = false;
        var phase = LilithNativeModelMutationPhase.ProviderSetupMutation;
        try
        {
            var previous = await CaptureProviderSetupStateAsync(cancellationToken).ConfigureAwait(false);
            if (method != HermesProviderSetupContract.CatalogMethod
                && parameters.GetProperty("expected_config_sha256").GetString() != previous.ConfigSha256)
                return ProviderSetupFailure(method, "setup.stale_config");

            if (!mutation)
            {
                var value = await _requestAsync(method, ProviderSetupNativeRequest(parameters), cancellationToken)
                    .ConfigureAwait(false);
                await RequireProviderSetupUnchangedAsync(previous, cancellationToken).ConfigureAwait(false);
                var error = ProviderSetupError(value);
                if (error is not null) return ProviderSetupFailure(method, error);
                if (method == HermesProviderSetupContract.CatalogMethod)
                {
                    if (HermesProviderSetupContract.ParseCatalog(value).ConfigSha256 != previous.ConfigSha256)
                        throw new InvalidDataException("Provider catalog did not match the verified config.");
                }
                else if (HermesProviderSetupContract.ParseModels(value).ProviderId
                    != parameters.GetProperty("provider_id").GetString())
                    throw new InvalidDataException("Model discovery returned another provider.");
                return Success(method, value);
            }

            // These are native metadata reads, not an inference or network
            // discovery request. They bind the write to its observed preimage.
            var before = HermesProviderSetupContract.ParseCatalog(await _requestAsync(
                HermesProviderSetupContract.CatalogMethod, ProviderSetupNativeRequest(Json("{}")), cancellationToken)
                .ConfigureAwait(false));
            if (before.ConfigSha256 != previous.ConfigSha256)
                return ProviderSetupFailure(method, "setup.stale_config");
            var selected = parameters.GetProperty("provider_id").GetString()!;
            if (before.Entries.Any(row => row.ProviderId == selected && row.IsDefault)
                || before.DefaultProvider == selected)
                return ProviderSetupFailure(method, "setup.entry_in_use");
            // Sequence zero has no durable policy receipt yet. Its C# placeholder
            // policy is not evidence of the actual native enabled tools. Capture
            // that policy before dispatch; native phase two verifies it again.
            var canonicalPolicy = previous.IsCanonicalBase
                ? HermesNativeToolsContract.DecodeCatalog(await _requestAsync(
                    "tools.catalog", Json("{}"), cancellationToken).ConfigureAwait(false))
                : null;
            await RequireProviderSetupUnchangedAsync(previous, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // A dispatched config/secret mutation retires the whole worker
            // generation, even when the response is lost or reports failure.
            _credentialMutationDispatched(method);
            dispatched = true;
            JsonElement result;
            try
            {
                result = await _requestAsync(method, ProviderSetupNativeRequest(parameters), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HermesRequestNotSentException)
            {
                await RequireProviderSetupUnchangedAsync(previous, CancellationToken.None).ConfigureAwait(false);
                return ProviderSetupFailure(method, "setup.request_not_sent", mutationDispatched: true);
            }
            var nativeError = ProviderSetupError(result);
            if (nativeError is not null)
            {
                if (nativeError is "setup.rollback_unverified" or "setup.receipt_required" or "setup.receipt_invalid")
                    throw new LilithNativeConfigMutationUncertainException();
                await RequireProviderSetupUnchangedAsync(previous, CancellationToken.None).ConfigureAwait(false);
                return ProviderSetupFailure(method, nativeError, mutationDispatched: true);
            }
            phase = LilithNativeModelMutationPhase.ProviderSetupMutationReceipt;
            var receipt = HermesProviderSetupContract.ParseMutation(result, method, parameters);
            phase = LilithNativeModelMutationPhase.ProviderSetupReadBack;
            var after = HermesProviderSetupContract.ParseCatalog(await _requestAsync(
                HermesProviderSetupContract.CatalogMethod, ProviderSetupNativeRequest(Json("{}")), cancellationToken)
                .ConfigureAwait(false));
            HermesProviderSetupContract.RequirePostcondition(after, receipt, method, parameters);
            RequireUnrelatedProviderRowsUnchanged(before, after, selected);
            if (after.Entries.Any(row => row.ProviderId == selected && row.IsDefault)
                || before.DefaultProvider != after.DefaultProvider || before.DefaultModel != after.DefaultModel
                || receipt.ConfigSha256 != await LilithNativeConfigMutationReceiptStore.ReadConfigSha256Async(
                    _hermesHomePath, CancellationToken.None).ConfigureAwait(false))
                throw new InvalidDataException("Provider setup changed the default or failed exact readback.");
            if (!receipt.Changed)
            {
                await RequireProviderSetupUnchangedAsync(previous, CancellationToken.None).ConfigureAwait(false);
                return Success(method, result);
            }

            phase = LilithNativeModelMutationPhase.ProviderSetupWriteReceipt;
            var commit = await LilithNativeConfigMutationReceiptStore.CommitProviderSetupAsync(
                _hermesHomePath, previous, receipt.ConfigSha256, canonicalPolicy,
                CancellationToken.None).ConfigureAwait(false);
            phase = LilithNativeModelMutationPhase.ProviderSetupCommitAcknowledgement;
            var confirmation = await _requestAsync("provider.setup.commit", JsonSerializer.SerializeToElement(new
            {
                authority = ProviderSetupAuthority,
                operation_id = receipt.OperationId,
                previous_config_sha256 = previous.ConfigSha256,
                config_sha256 = receipt.ConfigSha256,
                receipt_sha256 = commit.ReceiptSha256
            }), cancellationToken).ConfigureAwait(false);
            var fields = ReadExactObject(confirmation, new[] { "schema_version", "operation_id", "committed" });
            if (fields.Count != 3 || RequiredInt64(fields, "schema_version", 1, 1) != 1
                || RequiredText(fields, "operation_id", 128) != receipt.OperationId
                || !RequiredBoolean(fields, "committed"))
                throw new InvalidDataException("Provider setup commit was not confirmed.");
            phase = LilithNativeModelMutationPhase.ProviderSetupVerifyDurable;
            var durable = await CaptureProviderSetupStateAsync(CancellationToken.None).ConfigureAwait(false);
            if (!ProviderSetupStatesEqual(commit.State, durable))
                throw new InvalidDataException("Provider setup durable state changed after commit.");
            return Success(method, result);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            if (dispatched)
            {
                _modelAuthority.RecordNativeModelFailure(authority, _generationId,
                    LilithNativeModelMutationDiagnostic.Create(phase, exception));
                QuarantineNativeConfig(authority);
            }
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (dispatched)
            {
                _modelAuthority.RecordNativeModelFailure(authority, _generationId,
                    LilithNativeModelMutationDiagnostic.Create(phase, exception));
                QuarantineNativeConfig(authority);
                throw new LilithNativeConfigMutationUncertainException();
            }
            // No raw provider/transport exception, URL, key or response body
            // may be reflected into the management UI or diagnostics.
            return ProviderSetupFailure(method, "setup.catalog_unavailable");
        }
        finally { _nativeConfigGate.Release(); }
    }

    private static JsonElement ProviderSetupNativeRequest(JsonElement parameters)
    {
        var values = parameters.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        values.Add("authority", JsonSerializer.SerializeToElement(ProviderSetupAuthority));
        return JsonSerializer.SerializeToElement(values);
    }

    private Task<LilithNativeConfigMutationState> CaptureProviderSetupStateAsync(CancellationToken cancellationToken)
        => LilithNativeConfigMutationReceiptStore.CaptureCurrentAsync(_hermesHomePath,
            _profile!.CanonicalLearningConfigSha256, _profile.IdentityFingerprintSha256,
            _profile.SoulSha256, _profile.HermesPersonalitySha256, cancellationToken);

    private async Task RequireProviderSetupUnchangedAsync(LilithNativeConfigMutationState expected, CancellationToken cancellationToken)
    {
        if (!ProviderSetupStatesEqual(expected, await CaptureProviderSetupStateAsync(cancellationToken).ConfigureAwait(false)))
            throw new InvalidDataException("Provider setup config authority changed.");
    }

    private static bool ProviderSetupStatesEqual(LilithNativeConfigMutationState a, LilithNativeConfigMutationState b)
        => a.Sequence == b.Sequence && a.Source == b.Source && a.ConfigSha256 == b.ConfigSha256
            && a.PreviousConfigSha256 == b.PreviousConfigSha256 && a.CanonicalBaseSha256 == b.CanonicalBaseSha256
            && a.IdentityFingerprintSha256 == b.IdentityFingerprintSha256 && a.SoulSha256 == b.SoulSha256
            && a.PersonalitySha256 == b.PersonalitySha256 && a.ApprovalMode == b.ApprovalMode
            && a.OperationNonce == b.OperationNonce && a.EnabledToolsets.SequenceEqual(b.EnabledToolsets);

    private static void RequireUnrelatedProviderRowsUnchanged(HermesProviderSetupCatalog before,
        HermesProviderSetupCatalog after, string selected)
    {
        if (!before.Presets.SequenceEqual(after.Presets)
            || !before.Entries.Where(e => e.ProviderId != selected).OrderBy(e => e.ProviderId, StringComparer.Ordinal)
                .SequenceEqual(after.Entries.Where(e => e.ProviderId != selected).OrderBy(e => e.ProviderId, StringComparer.Ordinal)))
            throw new InvalidDataException("Provider setup modified an unrelated connection.");
    }

    private static string? ProviderSetupError(JsonElement value)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("code", out _)
            ? HermesProviderSetupContract.ParseError(value) : null;

    private static LilithRuntimeResponse ProviderSetupFailure(string method, string code, bool mutationDispatched = false)
        => new(method, succeeded: false, JsonSerializer.SerializeToElement(new
        { schema_version = 1, code, mutation_dispatched = mutationDispatched }));
}
