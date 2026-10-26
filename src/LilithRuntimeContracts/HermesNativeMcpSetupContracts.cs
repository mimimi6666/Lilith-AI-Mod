using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

public sealed record HermesNativeMcpSetupSelection
{
    [JsonPropertyName("catalog_entry")]
    public string CatalogEntry { get; }
    [JsonPropertyName("catalog_sha256")]
    public string CatalogSha256 { get; }
    [JsonPropertyName("enabled")]
    public bool Enabled { get; }

    public HermesNativeMcpSetupSelection(string catalogEntry, string catalogSha256, bool enabled)
    {
        CatalogEntry = HermesNativeToolsContract.McpCatalogName(catalogEntry);
        CatalogSha256 = HermesNativeToolsContract.Sha256(catalogSha256, nameof(catalogSha256));
        Enabled = enabled;
    }
}

public sealed record HermesNativeMcpCatalogEntry
{
    [JsonPropertyName("name")] public string Name { get; }
    [JsonPropertyName("description")] public string Description { get; }
    [JsonPropertyName("transport")] public string Transport { get; }
    [JsonPropertyName("url")] public string Url { get; }
    [JsonPropertyName("auth")] public string Auth { get; }
    [JsonPropertyName("required_fields")] public IReadOnlyList<string> RequiredFields { get; }
    [JsonPropertyName("catalog_sha256")] public string CatalogSha256 { get; }
    [JsonPropertyName("setup_support")] public string SetupSupport { get; }
    [JsonPropertyName("registered")] public bool Registered { get; }
    [JsonPropertyName("enabled")] public bool Enabled { get; }
    [JsonPropertyName("config_matches_catalog")] public bool ConfigMatchesCatalog { get; }

    public HermesNativeMcpCatalogEntry(string name, string description, string transport, string url,
        string auth, string catalogSha256, string setupSupport, bool registered, bool enabled,
        bool configMatchesCatalog, IReadOnlyList<string>? requiredFields = null)
    {
        Name = HermesNativeToolsContract.McpCatalogName(name);
        Description = HermesNativeToolsContract.Text(description, 4096, nameof(description), allowEmpty: true);
        Url = HermesNativeToolsContract.Text(url, 4096, nameof(url), allowEmpty: true);
        CatalogSha256 = HermesNativeToolsContract.Sha256(catalogSha256, nameof(catalogSha256));
        RequiredFields = Array.AsReadOnly((requiredFields ?? Array.Empty<string>()).ToArray());
        if (RequiredFields.Count > 64 || RequiredFields.Any(field => string.IsNullOrEmpty(field)
                || field.Length > 128 || field[0] is not (>= 'A' and <= 'Z' or '_')
                || field.Any(c => c is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            || RequiredFields.Distinct(StringComparer.Ordinal).Count() != RequiredFields.Count)
            throw new InvalidDataException("Native MCP field names were rejected.");
        if (transport is not ("http" or "stdio") || auth is not ("oauth" or "none" or "api_key")
            || setupSupport is not ("catalog_registration" or "external_install_required")
            || setupSupport == "catalog_registration" && RequiredFields.Count != 0
            || !registered && (enabled || configMatchesCatalog)
            || transport == "stdio" && (url.Length != 0 || setupSupport == "catalog_registration")
            || transport == "http" && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
                || uri.Fragment.Length != 0))
            throw new InvalidDataException("Native MCP catalog state was rejected.");
        Transport = transport;
        Auth = auth;
        SetupSupport = setupSupport;
        Registered = registered;
        Enabled = enabled;
        ConfigMatchesCatalog = configMatchesCatalog;
    }
}

public static partial class HermesNativeToolsContract
{
    internal static string McpCatalogName(string name)
    {
        name = Text(name, 128, nameof(name));
        static bool AlphaNumeric(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
        if (!AlphaNumeric(name[0]) || name.Any(c => !AlphaNumeric(c) && c is not ('_' or '-')))
            throw new InvalidDataException("Native MCP catalog name was rejected.");
        return name;
    }

    private static IReadOnlyDictionary<string, JsonElement> ExactSetupMutationObject(
        JsonElement value, params string[] required)
    {
        var provider = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("setup_selection", out _);
        var mcp = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("mcp_setup", out _);
        if (provider && mcp) throw new InvalidDataException("Native setup transactions cannot be combined.");
        var discovery = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("tool_discovery_enabled", out _);
        return ExactObject(value, required.Concat(provider ? new[] { "setup_selection" }
            : mcp ? new[] { "mcp_setup" } : Array.Empty<string>())
            .Concat(discovery ? new[] { "tool_discovery_enabled" } : Array.Empty<string>()).ToArray());
    }

    public static HermesNativeMcpSetupSelection? DecodeMcpSetup(JsonElement container)
    {
        if (!container.TryGetProperty("mcp_setup", out var value)) return null;
        var fields = ExactObject(value, "catalog_entry", "catalog_sha256", "enabled");
        return new HermesNativeMcpSetupSelection(RequiredString(fields, "catalog_entry"),
            RequiredString(fields, "catalog_sha256"), RequiredBoolean(fields, "enabled"));
    }

    public static IReadOnlyList<HermesNativeMcpCatalogEntry>? DecodeMcpCatalog(JsonElement container)
    {
        if (!container.TryGetProperty("mcp_catalog", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 64)
            throw new InvalidDataException("Native MCP catalog is unbounded.");
        var rows = new List<HermesNativeMcpCatalogEntry>();
        foreach (var item in value.EnumerateArray())
        {
            var fields = ExactObject(item, "name", "description", "transport", "url", "auth", "required_fields",
                "catalog_sha256", "setup_support", "registered", "enabled", "config_matches_catalog");
            var requiredFields = StringArray(fields["required_fields"], 64, 128, "required_fields");
            var description = fields["description"];
            var url = fields["url"];
            if (description.ValueKind != JsonValueKind.String || url.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Native MCP catalog text was rejected.");
            rows.Add(new HermesNativeMcpCatalogEntry(RequiredString(fields, "name"), description.GetString()!,
                RequiredString(fields, "transport"), url.GetString()!, RequiredString(fields, "auth"),
                RequiredString(fields, "catalog_sha256"), RequiredString(fields, "setup_support"),
                RequiredBoolean(fields, "registered"), RequiredBoolean(fields, "enabled"),
                RequiredBoolean(fields, "config_matches_catalog"), requiredFields));
        }
        return ValidateMcpCatalog(rows);
    }

    internal static IReadOnlyList<HermesNativeMcpCatalogEntry>? ValidateMcpCatalog(
        IReadOnlyList<HermesNativeMcpCatalogEntry>? entries)
    {
        if (entries is null) return null;
        if (entries.Count > 64 || entries.Any(p => p is null)
            || entries.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("Native MCP catalog entries are duplicated or unbounded.");
        return Array.AsReadOnly(entries.ToArray());
    }

    public static bool McpSetupMatchesCatalog(HermesNativeMcpSetupSelection selection,
        IReadOnlyList<HermesNativeMcpCatalogEntry>? catalog, bool requireRegistered)
        => catalog?.Any(row => row.Name == selection.CatalogEntry
            && row.CatalogSha256 == selection.CatalogSha256 && row.Transport == "http"
            && row.SetupSupport == "catalog_registration"
            && (!row.Registered || row.ConfigMatchesCatalog)
            && (!requireRegistered || row.Registered && row.ConfigMatchesCatalog && row.Enabled == selection.Enabled)) == true;
}
