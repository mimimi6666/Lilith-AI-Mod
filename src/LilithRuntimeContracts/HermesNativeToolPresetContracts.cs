using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

// A native-authored editor preset, never an additional runtime permission.
public sealed class HermesNativeToolPreset
{
    [JsonPropertyName("id")]
    public string Id { get; }
    [JsonPropertyName("toolset_ids")]
    public IReadOnlyList<string> ToolsetIds { get; }
    [JsonPropertyName("managed_toolset_ids")]
    public IReadOnlyList<string> ManagedToolsetIds { get; }

    public HermesNativeToolPreset(string id, IEnumerable<string> toolsetIds, IEnumerable<string> managedToolsetIds)
    {
        if (id is not ("daily" or "work")) throw new InvalidDataException("Unknown native tool preset.");
        Id = id;
        ToolsetIds = HermesNativeToolsContract.UniqueTokens(toolsetIds, HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes, nameof(toolsetIds));
        ManagedToolsetIds = HermesNativeToolsContract.UniqueTokens(managedToolsetIds, HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes, nameof(managedToolsetIds));
        if (!ToolsetIds.ToHashSet(StringComparer.Ordinal).IsSubsetOf(ManagedToolsetIds))
            throw new InvalidDataException("Native preset selected outside its managed toolsets.");
    }
}

public static partial class HermesNativeToolsContract
{
    public static bool? DecodeToolDiscoveryEnabled(JsonElement container)
    {
        if (!container.TryGetProperty("tool_discovery_enabled", out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true, JsonValueKind.False => false,
            _ => throw new InvalidDataException("Invalid native tool discovery setting.")
        };
    }

    public static IReadOnlyList<HermesNativeToolPreset>? DecodePresets(JsonElement container, IEnumerable<string> catalogNames)
    {
        if (!container.TryGetProperty("presets", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 2)
            throw new InvalidDataException("Invalid native tool presets.");
        var presets = value.EnumerateArray().Select(item =>
        {
            var row = ExactObject(item, "id", "toolset_ids", "managed_toolset_ids");
            return new HermesNativeToolPreset(RequiredString(row, "id"),
                StringArray(row["toolset_ids"], MaximumToolsets, MaximumToolsetNameUtf8Bytes, "toolset_ids"),
                StringArray(row["managed_toolset_ids"], MaximumToolsets, MaximumToolsetNameUtf8Bytes, "managed_toolset_ids"));
        }).ToArray();
        return ValidatePresets(presets, catalogNames);
    }

    internal static IReadOnlyList<HermesNativeToolPreset>? ValidatePresets(
        IReadOnlyList<HermesNativeToolPreset>? presets, IEnumerable<string> catalogNames)
    {
        if (presets is null) return null;
        var rows = presets.ToArray();
        var names = catalogNames.ToHashSet(StringComparer.Ordinal);
        if (rows.Length > 2 || rows.Any(row => row is null)
            || rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != rows.Length
            || rows.Any(row => !row.ManagedToolsetIds.ToHashSet(StringComparer.Ordinal).IsSubsetOf(names)))
            throw new InvalidDataException("Native presets must reference unique, present catalog toolsets.");
        return Array.AsReadOnly(rows);
    }
}
