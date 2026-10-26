using System.IO;
using System.Text.Json;

namespace LilithControlCenter;

// Read-only presentation helpers. A retained catalog is never a new source of
// model-selection authority; MainWindow separately revokes its writable state.
internal static class DefaultModelCatalogPresentation
{
    internal static D046HermesModelOption? ExactSelection(D046HermesModelCatalog? catalog)
        => catalog?.HasExactSelection == true
            ? catalog.Options.SingleOrDefault(option =>
                string.Equals(option.ProviderId, catalog.ExactProviderId, StringComparison.Ordinal)
                && string.Equals(option.ModelId, catalog.ExactModelId, StringComparison.Ordinal))
            : null;

    internal static string SafeReadFailureCode(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.String
                ? SafeReadFailureCode(code.GetString()) : "model.discovery_unavailable";

    internal static string SafeReadFailureCode(string? code)
        => code is "runtime.native_config_quarantined"
            or "runtime.native_config_transition_busy"
            or "runtime.config_verification_failed"
            or "model.discovery_unavailable" or "model.discovery_timeout"
                ? code : "model.discovery_unavailable";

    internal static string FailureCode(Exception exception)
        => exception switch
        {
            D046HermesSessionPreflightException value => SafeReadFailureCode(value.Code),
            TimeoutException => "model.discovery_timeout",
            OperationCanceledException => "model.read_superseded",
            InvalidDataException => "model.catalog_invalid",
            IOException or ObjectDisposedException => "runtime_connection_lost",
            _ => "model.discovery_unavailable"
        };
}
