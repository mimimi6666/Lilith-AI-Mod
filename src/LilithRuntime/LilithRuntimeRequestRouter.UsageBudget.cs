using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private async Task<LilithRuntimeResponse> RouteUsageBudgetAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (_sessionSurface != LilithRuntimeSessionSurface.Workbench)
            return Failure(method, "runtime.workbench_surface_required",
                "Economic preferences require the authenticated Workbench control surface.");
        cancellationToken.ThrowIfCancellationRequested();
        var mutation = method == HermesUsageBudgetContract.SetMethod;
        try
        {
            // Sessionless human-management RPC: intentionally permits saving
            // during an active turn. Hermes freezes it only at the next origin.
            var native = await _requestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            var failure = UsageBudgetFailure(method, native, mutation);
            if (failure is not null) return failure;
            if (!mutation)
            {
                if (method == HermesLatestUsageContract.GetMethod)
                    HermesLatestUsageContract.Parse(native);
                else if (method == HermesUsageBudgetPreviewContract.PreviewMethod)
                    HermesUsageBudgetPreviewContract.Parse(native);
                else HermesUsageBudgetContract.ParsePreference(native);
                return Success(method, native);
            }
            var request = HermesUsageBudgetContract.ParseUpdate(parameters);
            var commit = HermesUsageBudgetContract.ParseCommit(native, request);
            // A receipt alone is not readback proof. Do not retry the setter,
            // roll back, or rotate native config/tool/session authority here.
            var readback = await _requestAsync(HermesUsageBudgetContract.GetMethod,
                HermesUsageBudgetContract.GetRequest(), cancellationToken).ConfigureAwait(false);
            if (HermesUsageBudgetContract.ParsePreference(readback) != commit.Preference)
                return Failure(method, "budget.result_uncertain",
                    "The saved preference changed before readback. Refresh before deciding whether to retry.");
            return Success(method, native);
        }
        catch (HermesRpcException error) when (error.Code == 5035) { throw; }
        catch (HermesRequestNotSentException)
        {
            // A readback transport failure can happen after the setter commit.
            // Conservatively mark every mutation uncertain, never claim rollback.
            return Failure(method, mutation ? "budget.result_uncertain" : "budget.request_not_sent",
                "The economic preference result could not be verified. Refresh before retrying.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Failure(method, mutation ? "budget.result_uncertain" : "budget.operation_failed",
                "The economic preference result could not be verified. Refresh before retrying.");
        }
    }

    private static LilithRuntimeResponse? UsageBudgetFailure(string method, JsonElement value, bool mutation)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("code", out _)) return null;
        var fields = ReadExactObject(value, new[] { "schema_version", "code" });
        if (fields.Count != 2) throw new InvalidDataException("Economic preference error was rejected.");
        RequiredInt64(fields, "schema_version", 1, 1);
        var code = RequiredText(fields, "code", 64);
        var safeCode = code switch
        {
            "budget.invalid_request" or "budget.revision_conflict" or "budget.operation_conflict"
                or "budget.revision_exhausted" or "budget.store_capacity_reached" or "budget.store_busy"
                or "budget.host_required" => code,
            "budget.store_unavailable" or "budget.store_malformed" or "budget.unsafe_store_path"
                or "budget.operation_failed" => mutation ? "budget.result_uncertain" : "budget.operation_failed",
            _ => throw new InvalidDataException("Economic preference error was rejected.")
        };
        return Failure(method, safeCode, "The economic preference could not be accepted. Refresh before retrying.");
    }
}
