using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

public sealed record HermesNativeSetupSelection
{
    [JsonPropertyName("toolset")]
    public string Toolset { get; }
    [JsonPropertyName("provider")]
    public string Provider { get; }
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; }

    public HermesNativeSetupSelection(string toolset, string provider, string? model = null)
    {
        Toolset = HermesNativeToolsContract.Token(toolset, 256, nameof(toolset));
        Provider = HermesNativeToolsContract.Text(provider, 1024, nameof(provider));
        Model = model is null ? null : HermesNativeToolsContract.Text(model, 1024, nameof(model));
    }
}

public sealed record HermesNativeSetupProvider
{
    [JsonPropertyName("name")]
    public string Name { get; }
    [JsonPropertyName("readiness")]
    public string Readiness { get; }
    [JsonPropertyName("setup_support")]
    public string SetupSupport { get; }
    [JsonPropertyName("selected")]
    public bool Selected { get; }
    [JsonPropertyName("dependency_status")]
    public string DependencyStatus { get; }
    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Models { get; }
    [JsonPropertyName("current_model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentModel { get; }

    public HermesNativeSetupProvider(string name, string readiness, string setupSupport,
        bool selected, string dependencyStatus, IReadOnlyList<string>? models = null, string? currentModel = null)
    {
        Name = HermesNativeToolsContract.Text(name, 1024, nameof(name));
        if (readiness is not ("ready" or "needs_setup" or "needs_auth" or "needs_keys")
            || setupSupport is not ("provider_selection" or "secure_fields" or "spotify_oauth"
                or "native_manager_required" or "no_setup_fields" or "model_selection" or "dependency_setup")
            || dependencyStatus is not ("not_required" or "ready" or "missing" or "unknown"))
            throw new InvalidDataException("Native setup metadata has unsupported states.");
        Readiness = readiness;
        SetupSupport = setupSupport;
        Selected = selected;
        DependencyStatus = dependencyStatus;
        if ((models is null) != (currentModel is null) || models?.Count > 256
            || models?.Distinct(StringComparer.Ordinal).Count() != models?.Count)
            throw new InvalidDataException("Native model choices are ambiguous or unbounded.");
        Models = models is null ? null : Array.AsReadOnly(models.Select(m => HermesNativeToolsContract.Text(m, 1024, nameof(models))).ToArray());
        CurrentModel = currentModel is null ? null : HermesNativeToolsContract.Text(currentModel, 1024, nameof(currentModel), allowEmpty: true);
    }
}

public static partial class HermesNativeToolsContract
{
    private static IReadOnlyDictionary<string, JsonElement> ExactObjectWithOptionalSetup(
        JsonElement value, string optional, params string[] required)
        => ExactObject(value, value.ValueKind == JsonValueKind.Object && value.TryGetProperty(optional, out _)
            ? required.Concat(new[] { optional }).ToArray() : required);

    public static HermesNativeSetupSelection? DecodeSetupSelection(JsonElement container)
    {
        if (!container.TryGetProperty("setup_selection", out var value))
            return null;
        var fields = ExactObjectWithOptionalSetup(value, "model", "toolset", "provider");
        return new HermesNativeSetupSelection(RequiredString(fields, "toolset"), RequiredString(fields, "provider"),
            fields.ContainsKey("model") ? RequiredString(fields, "model") : null);
    }

    public static IReadOnlyList<HermesNativeSetupProvider>? DecodeSetupProviders(JsonElement container)
    {
        if (!container.TryGetProperty("setup_providers", out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 64)
            throw new InvalidDataException("Native setup provider catalog is unbounded.");
        var result = new List<HermesNativeSetupProvider>();
        foreach (var item in value.EnumerateArray())
        {
            var hasModels = item.TryGetProperty("models", out var models);
            var required = new[] { "name", "readiness", "setup_support", "selected", "dependency_status" };
            var fields = ExactObject(item, hasModels ? required.Concat(new[] { "models", "current_model" }).ToArray() : required);
            result.Add(new HermesNativeSetupProvider(RequiredString(fields, "name"), RequiredString(fields, "readiness"),
                RequiredString(fields, "setup_support"), RequiredBoolean(fields, "selected"), RequiredString(fields, "dependency_status"),
                hasModels ? StringArray(models, 256, 1024, "models") : null,
                hasModels ? RequiredString(fields, "current_model", allowEmpty: true) : null));
        }
        return ValidateSetupProviders(result);
    }

    internal static IReadOnlyList<HermesNativeSetupProvider>? ValidateSetupProviders(
        IReadOnlyList<HermesNativeSetupProvider>? providers)
    {
        if (providers is null) return null;
        if (providers.Count > 64 || providers.Any(p => p is null)
            || providers.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != providers.Count)
            throw new InvalidDataException("Native setup providers are duplicated or ambiguous.");
        return Array.AsReadOnly(providers.ToArray());
    }
}
