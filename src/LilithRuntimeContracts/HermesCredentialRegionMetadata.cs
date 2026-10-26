using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

// Display metadata supplied by Hermes. This never resolves an endpoint, selects
// a model, stores a credential or grants permission to send one to another region.
public sealed record HermesCredentialRegionMetadata(
    string FamilyId, string FamilyName, string VariantId, string BaseUrl)
{
    private static readonly string[] LegacyKeys =
        { "slug", "name", "configured", "legacy_sources_present" };
    private static readonly string[] RegionKeys =
        { "slug", "name", "configured", "legacy_sources_present",
          "family_id", "family_name", "variant_id", "base_url" };

    public static string[] RowKeys(JsonElement row)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty("family_id", out _)
            ? RegionKeys.ToArray() : LegacyKeys.ToArray();

    public static HermesCredentialRegionMetadata? Parse(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) throw Rejected();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in row.EnumerateObject())
            if (!fields.TryAdd(field.Name, field.Value)) throw Rejected();
        var keys = RowKeys(row);
        if (fields.Count != keys.Length || keys.Any(key => !fields.ContainsKey(key))) throw Rejected();
        if (keys.Length == LegacyKeys.Length) return null;
        var family = Text(fields["family_id"], 128);
        if (family.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            || family[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9')) throw Rejected();
        var label = Text(fields["family_name"], 256);
        var variant = Text(fields["variant_id"], 64);
        if (variant is not ("default" or "international" or "china" or "singapore" or "us" or "china-legacy" or "legacy"))
            throw Rejected();
        var url = Text(fields["base_url"], 2048, allowEmpty: true);
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || url.Contains('\\') || url.Any(char.IsWhiteSpace))) throw Rejected();
        return new(family, label, variant, url);
    }

    private static string Text(JsonElement value, int maximumBytes, bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text
            || (!allowEmpty && string.IsNullOrWhiteSpace(text)) || text != text.Trim()
            || text.Any(char.IsControl) || Encoding.UTF8.GetByteCount(text) > maximumBytes) throw Rejected();
        return text;
    }

    private static InvalidDataException Rejected() => new("Hermes credential region metadata was rejected.");
}
