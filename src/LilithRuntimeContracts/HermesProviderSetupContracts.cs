using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

// These are non-secret projections of the native configuration, not another store.
public sealed record HermesProviderSetupPreset(string Id, string Label, string Kind, string BaseUrl, string AuthMode);
public sealed record HermesProviderSetupEntry(string ProviderId, string Label, string PresetId,
    string Kind, string BaseUrl, string ModelId, string AuthMode, bool KeyPresent, bool IsDefault);
public sealed record HermesProviderSetupCatalog(string ConfigSha256,
    IReadOnlyList<HermesProviderSetupPreset> Presets, IReadOnlyList<HermesProviderSetupEntry> Entries,
    string DefaultProvider, string DefaultModel);
public sealed record HermesProviderSetupModels(string ProviderId, string Status, IReadOnlyList<string> Models);
public sealed record HermesProviderSetupMutation(string OperationId, string ProviderId, string Action,
    bool Changed, string PreviousConfigSha256, string ConfigSha256);
public sealed record HermesProviderSetupRuntimeError(string Code, bool MutationDispatched);

public static class HermesProviderSetupContract
{
    public const string CatalogMethod = "provider.setup.catalog";
    public const string ModelsMethod = "provider.setup.models";
    public const string SaveMethod = "provider.setup.save";
    public const string RemoveMethod = "provider.setup.remove";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] ModelStatuses = { "ready", "empty", "auth_required", "request_rejected",
        "service_failed", "timeout", "unreachable", "invalid_response" };
    private static readonly string[] ErrorCodes = { "setup.invalid_request", "setup.unauthorized", "setup.stale_config",
        "setup.busy", "setup.not_found", "setup.limit_reached", "setup.entry_in_use", "setup.key_required",
        "setup.endpoint_changed_requires_key", "setup.storage_failed", "setup.rollback_unverified",
        "setup.receipt_required", "setup.receipt_invalid", "setup.catalog_unavailable", "setup.cancelled" };

    public static bool IsMethod(string method) => method is CatalogMethod or ModelsMethod or SaveMethod or RemoveMethod;
    public static bool IsMutation(string method) => method is SaveMethod or RemoveMethod;
    public static IReadOnlyList<string> RequestKeys(string method) => Array.AsReadOnly(method switch
    {
        CatalogMethod => Array.Empty<string>(),
        ModelsMethod => new[] { "provider_id", "expected_config_sha256" },
        RemoveMethod => new[] { "expected_config_sha256", "operation_id", "provider_id" },
        SaveMethod => new[] { "expected_config_sha256", "operation_id", "provider_id", "label", "preset_id",
            "kind", "base_url", "model_id", "auth_mode", "key_action", "api_key" },
        _ => throw Rejected()
    });

    // Keep secrets in a transient JSON payload only: no secret-bearing record,
    // diagnostics, exception details, ToString override or persistence here.
    public static JsonElement CanonicalizeRequest(string method, JsonElement value)
    {
        var fields = Exact(value, RequestKeys(method).ToArray());
        if (method != CatalogMethod)
        {
            ProviderId(String(fields, "provider_id"));
            Sha256(String(fields, "expected_config_sha256"));
        }
        if (IsMutation(method)) OperationId(String(fields, "operation_id"));
        if (method == SaveMethod)
        {
            Label(String(fields, "label"));
            PresetId(String(fields, "preset_id"));
            var kind = Kind(String(fields, "kind"));
            Endpoint(String(fields, "base_url"), kind);
            ModelId(String(fields, "model_id"));
            var auth = Auth(String(fields, "auth_mode"), kind);
            var action = String(fields, "key_action");
            var key = String(fields, "api_key");
            if (action is not ("keep" or "replace" or "clear")
                || auth == "none" && action != "clear"
                || auth == "api_key" && action == "clear"
                || action != "replace" && key.Length != 0
                || action == "replace" && (key.Length is < 1 or > 16384
                    || key.Any(c => c is < '!' or > '~')))
                throw Rejected();
        }
        return JsonSerializer.SerializeToElement(fields);
    }

    public static HermesProviderSetupCatalog ParseCatalog(JsonElement value)
    {
        var root = Exact(value, "schema_version", "config_sha256", "presets", "entries", "default_provider", "default_model");
        Version(root);
        var digest = Sha256(String(root, "config_sha256"));
        var defaultProvider = String(root, "default_provider");
        var defaultModel = String(root, "default_model");
        if (defaultProvider.Length != 0) ModelProviderId(defaultProvider);
        if (defaultModel.Length != 0) ModelId(defaultModel);
        var presets = ArrayValue(root, "presets", 32).Select(item =>
        {
            var row = Exact(item, "id", "label", "kind", "base_url", "auth_mode");
            var kind = Kind(String(row, "kind"));
            return new HermesProviderSetupPreset(PresetId(String(row, "id")), Label(String(row, "label")),
                kind, Endpoint(String(row, "base_url"), kind, allowEmpty: true), Auth(String(row, "auth_mode"), kind));
        }).ToArray();
        Unique(presets.Select(p => p.Id));
        var entries = ArrayValue(root, "entries", 128).Select(item =>
        {
            var row = Exact(item, "provider_id", "label", "preset_id", "kind", "base_url", "model_id",
                "auth_mode", "key_present", "is_default");
            var kind = Kind(String(row, "kind"));
            var auth = Auth(String(row, "auth_mode"), kind);
            var key = Boolean(row, "key_present");
            if (auth == "none" && key) throw Rejected();
            var preset = PresetId(String(row, "preset_id"));
            if (!presets.Any(p => p.Id == preset && p.Kind == kind)) throw Rejected();
            return new HermesProviderSetupEntry(ProviderId(String(row, "provider_id")), Label(String(row, "label")),
                preset, kind, Endpoint(String(row, "base_url"), kind), ModelId(String(row, "model_id")),
                auth, key, Boolean(row, "is_default"));
        }).ToArray();
        Unique(entries.Select(e => e.ProviderId));
        if (entries.Count(e => e.IsDefault) > 1) throw Rejected();
        return new(digest, Array.AsReadOnly(presets), Array.AsReadOnly(entries), defaultProvider, defaultModel);
    }

    public static HermesProviderSetupModels ParseModels(JsonElement value)
    {
        var root = Exact(value, "schema_version", "provider_id", "status", "models", "catalog_only");
        Version(root);
        var provider = ProviderId(String(root, "provider_id"));
        var status = String(root, "status");
        if (!ModelStatuses.Contains(status, StringComparer.Ordinal) || !Boolean(root, "catalog_only")) throw Rejected();
        var models = ArrayValue(root, "models", 256).Select(m => m.ValueKind == JsonValueKind.String
            ? ModelId(m.GetString()!) : throw Rejected()).ToArray();
        Unique(models);
        if (status == "ready" ? models.Length == 0 : models.Length != 0) throw Rejected();
        return new(provider, status, Array.AsReadOnly(models));
    }

    public static HermesProviderSetupMutation ParseMutation(JsonElement value, string method, JsonElement request)
    {
        if (!IsMutation(method)) throw Rejected();
        var expected = CanonicalizeRequest(method, request);
        var root = Exact(value, "schema_version", "operation_id", "provider_id", "action", "changed",
            "previous_config_sha256", "config_sha256");
        Version(root);
        var operation = OperationId(String(root, "operation_id"));
        var provider = ProviderId(String(root, "provider_id"));
        var action = String(root, "action");
        var changed = Boolean(root, "changed");
        var previous = Sha256(String(root, "previous_config_sha256"));
        var digest = Sha256(String(root, "config_sha256"));
        if (operation != expected.GetProperty("operation_id").GetString()
            || provider != expected.GetProperty("provider_id").GetString()
            || previous != expected.GetProperty("expected_config_sha256").GetString()
            || action != (method == SaveMethod ? "save" : "remove")
            || changed != (previous != digest)) throw Rejected();
        return new(operation, provider, action, changed, previous, digest);
    }

    public static string ParseError(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "code");
        Version(fields);
        var code = String(fields, "code");
        return ErrorCodes.Contains(code, StringComparer.Ordinal) ? code : throw Rejected();
    }

    public static HermesProviderSetupRuntimeError ParseRuntimeError(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "code", "mutation_dispatched");
        Version(fields);
        var code = String(fields, "code");
        if (!ErrorCodes.Contains(code, StringComparer.Ordinal) && code != "setup.request_not_sent") throw Rejected();
        var dispatched = Boolean(fields, "mutation_dispatched");
        // This bit marks the Runtime retirement / dispatch-attempt boundary,
        // not proof of native delivery. Even an exact not-sent result may need G2.
        return new(code, dispatched);
    }

    public static void RequirePostcondition(HermesProviderSetupCatalog catalog, HermesProviderSetupMutation receipt,
        string method, JsonElement request, HermesProviderSetupCatalog? previousCatalog = null)
    {
        var expected = CanonicalizeRequest(method, request);
        if (catalog.ConfigSha256 != receipt.ConfigSha256) throw Rejected();
        if (previousCatalog is not null && (catalog.DefaultProvider != previousCatalog.DefaultProvider
            || catalog.DefaultModel != previousCatalog.DefaultModel)) throw Rejected();
        var row = catalog.Entries.SingleOrDefault(e => e.ProviderId == receipt.ProviderId);
        if (method == RemoveMethod) { if (row is not null) throw Rejected(); return; }
        if (method != SaveMethod || row is null
            || row.Label != expected.GetProperty("label").GetString()
            || row.PresetId != expected.GetProperty("preset_id").GetString()
            || row.Kind != expected.GetProperty("kind").GetString()
            || row.BaseUrl != expected.GetProperty("base_url").GetString()
            || row.ModelId != expected.GetProperty("model_id").GetString()
            || row.AuthMode != expected.GetProperty("auth_mode").GetString()
            || row.KeyPresent != (row.AuthMode == "api_key")) throw Rejected();
    }

    public static string Endpoint(string value, string kind, bool allowEmpty = false)
    {
        Kind(kind);
        if (allowEmpty && value.Length == 0) return value;
        Text(value, 2048, 8192);
        if (value.Any(c => char.IsWhiteSpace(c) || c == '\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.Host.Length == 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || kind == "cloud" && uri.Scheme != "https"
            || kind == "local" && !ExactLoopbackHost(value))
            throw Rejected();
        return value;
    }

    private static string Kind(string value) => value is "cloud" or "local" ? value : throw Rejected();
    private static string Auth(string value, string kind)
        => value == "api_key" || value == "none" && kind == "local" ? value : throw Rejected();
    private static string Label(string value) => Text(value, 128, 512);
    private static string ModelId(string value)
    {
        Text(value, 256, 1024);
        if (value[0] == '-' || value.Any(c => !AsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '/' or ':' or '@' or '+')))
            throw Rejected();
        return value;
    }
    private static string ProviderId(string value)
        => value.Length == 39 && value.StartsWith("custom:", StringComparison.Ordinal)
            && value.AsSpan(7).ToArray().All(LowerHex) ? value : throw Rejected();
    private static string ModelProviderId(string value)
    {
        Text(value, 128, 512);
        var token = value.StartsWith("custom:", StringComparison.Ordinal) ? value[7..] : value;
        if (token.Length == 0 || token[0] == '-'
            || token.Any(c => !AsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw Rejected();
        return value;
    }
    private static string Sha256(string value) => value.Length == 64 && value.All(LowerHex) ? value : throw Rejected();
    private static bool LowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static string PresetId(string value)
        => value.Length is >= 1 and <= 48 && value[0] is >= 'a' and <= 'z'
            && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-') ? value : throw Rejected();
    private static string OperationId(string value)
        => value.Length is >= 1 and <= 128 && value.All(c => AsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? value : throw Rejected();
    private static bool AsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    private static bool ExactLoopbackHost(string value)
    {
        // Uri normalizes numeric aliases such as 127.1; native admission only
        // permits the explicitly spelled loopback hosts, not those aliases.
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0) return false;
        var authority = value[(separator + 3)..].Split('/')[0];
        var end = authority.StartsWith("[", StringComparison.Ordinal) ? authority.IndexOf(']') + 1 : authority.IndexOf(':');
        var host = (end < 0 ? authority : authority[..end]).ToLowerInvariant();
        return host is "localhost" or "127.0.0.1" or "[::1]";
    }
    private static string Text(string value, int characters, int bytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > characters
            || value.Any(char.IsControl)) throw Rejected();
        try { if (StrictUtf8.GetByteCount(value) > bytes) throw Rejected(); }
        catch (EncoderFallbackException) { throw Rejected(); }
        return value;
    }
    private static string String(IReadOnlyDictionary<string, JsonElement> fields, string name)
        => fields[name].ValueKind == JsonValueKind.String ? fields[name].GetString()! : throw Rejected();
    private static bool Boolean(IReadOnlyDictionary<string, JsonElement> fields, string name)
        => fields[name].ValueKind is JsonValueKind.True or JsonValueKind.False ? fields[name].GetBoolean() : throw Rejected();
    private static void Version(IReadOnlyDictionary<string, JsonElement> fields)
    {
        if (fields["schema_version"].ValueKind != JsonValueKind.Number
            || !fields["schema_version"].TryGetInt32(out var version) || version != 1) throw Rejected();
    }
    private static IEnumerable<JsonElement> ArrayValue(IReadOnlyDictionary<string, JsonElement> fields, string name, int max)
    {
        var value = fields[name];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > max) throw Rejected();
        return value.EnumerateArray();
    }
    private static void Unique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) if (!seen.Add(value)) throw Rejected();
    }
    private static Dictionary<string, JsonElement> Exact(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Rejected();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!keys.Contains(item.Name, StringComparer.Ordinal) || !fields.TryAdd(item.Name, item.Value.Clone())) throw Rejected();
        if (fields.Count != keys.Length) throw Rejected();
        return fields;
    }
    private static InvalidDataException Rejected() => new("Native provider setup data was rejected.");
}
