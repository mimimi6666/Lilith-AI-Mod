using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal static class LilithRuntimeCatalogReadPolicy
{
    internal static bool Contains(string method) => method is HermesCapabilityCatalogContract.Method
        or HermesCommandCatalogContract.Method or "tools.catalog" or "tools.settings.get"
        or HermesLatestUsageContract.GetMethod;
}

internal sealed partial class LilithRuntimeRequestRouter
{
    // Inventory reads grant no execution authority. Bind a before/after snapshot
    // under the real authority gate, but do not hold that gate during native IO.
    // Concurrent mutations retain their original gate and stale reads fail.
    private async Task<LilithRuntimeResponse> RouteIndependentCatalogReadAsync(
        LilithRuntimeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = Canonicalize(request.Method, request.HermesPayload);
            if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
                return Failure(request.Method, "runtime.workbench_surface_required", "Catalog reads require the Workbench.");
            LilithRuntimeModelAuthoritySnapshot before;
            object? sessionInfo;
            string? active;
            bool pending;
            using (var lease = await _modelAuthority.AcquireAsync(_generationId, cancellationToken).ConfigureAwait(false))
            {
                before = lease.Snapshot;
                var rejected = NativeModelAuthorityMutationFailure(request.Method, before);
                if (rejected is not null) return rejected;
                ObserveSharedNativeConfigRevision(before);
                lock (_sessionLock)
                {
                    if (_sessionActivationQuarantined || _nativeConfigQuarantined || _nativeConfigMutationInFlightSessionId is not null)
                        return Failure(request.Method, "runtime.native_config_quarantined", "Catalog authority is unavailable.");
                    active = _activeSessionId;
                    sessionInfo = _lastAllowedSessionInfo;
                    pending = _nativeToolsNewSessionRequired;
                }
                if (request.Method == "tools.catalog"
                    && (OptionalSessionId(parameters) is not { } session || !IsActiveNativeToolsCatalogSession(session)))
                    return Failure(request.Method, "runtime.active_session_required", "Tool catalog requires its active session.");
            }

            LilithRuntimeResponse response;
            switch (request.Method)
            {
                case HermesCommandCatalogContract.Method:
                    response = Success(request.Method, JsonSerializer.SerializeToElement(
                        HermesCapabilityCatalogAssembler.AssembleCommands(
                            await _requestAsync("commands.catalog", parameters, cancellationToken).ConfigureAwait(false))));
                    break;
                case HermesCapabilityCatalogContract.Method:
                    response = await RouteCapabilityCatalogAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "tools.catalog":
                    response = await RouteNativeToolsCatalogAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "tools.settings.get":
                    response = await ReadNativeSettingsAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case HermesLatestUsageContract.GetMethod:
                    response = await RouteUsageBudgetAsync(request.Method, parameters, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidDataException("Unknown independent catalog read.");
            }
            using (var lease = await _modelAuthority.AcquireAsync(_generationId, cancellationToken).ConfigureAwait(false))
            {
                var rejected = NativeModelAuthorityMutationFailure(request.Method, lease.Snapshot);
                if (rejected is not null) return rejected;
                if (lease.Snapshot != before)
                    return Failure(request.Method, "runtime.settings_changed", "Catalog authority changed; refresh required.");
                lock (_sessionLock)
                {
                    if (_sessionActivationQuarantined || _nativeConfigQuarantined
                        || _nativeConfigMutationInFlightSessionId is not null
                        || active != _activeSessionId || !ReferenceEquals(sessionInfo, _lastAllowedSessionInfo)
                        || pending != _nativeToolsNewSessionRequired || _sessionLifecycleInFlight)
                        return Failure(request.Method, "runtime.settings_changed", "Catalog session changed; refresh required.");
                }
            }
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException)
        { return Failure(request.Method, "runtime.payload_rejected", "Catalog payload or native response was rejected."); }
        catch (ArgumentException)
        { return Failure(request.Method, "runtime.payload_rejected", "Catalog parameters were rejected."); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return Failure(request.Method, "runtime.catalog_unavailable", "Native catalog could not be read."); }
    }
}
