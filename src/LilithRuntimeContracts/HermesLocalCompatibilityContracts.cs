using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

// Local compatibility preferences are not permissions, session settings, or model defaults.
public sealed record HermesLocalCompatibilityPreference(long Revision, bool Enabled);
public sealed record HermesLocalCompatibilityUpdate(string OperationId, long ExpectedRevision, bool Enabled);
public sealed record HermesLocalCompatibilityReceipt(string OperationId, long ExpectedRevision,
    HermesLocalCompatibilityPreference Committed, string PreferenceDigest);
public sealed record HermesLocalCompatibilityCommit(HermesLocalCompatibilityPreference Preference,
    HermesLocalCompatibilityReceipt Receipt, bool Replay);

/// <summary>Closed native local compatibility packets; no caller-supplied paths or numeric budgets.</summary>
public static class HermesLocalCompatibilityContract
{
    public const string GetMethod = "local_compatibility.get";
    public const string SetMethod = "local_compatibility.set";
    public const string AppliesFrom = "next_origin_turn";
    public const int MaximumPacketBytes = 2048;

    public static JsonElement GetRequest() => JsonSerializer.SerializeToElement(new { });

    public static void ValidateGetRequest(JsonElement value) => Exact(value);

    public static JsonElement SetRequest(string operationId, long expectedRevision, bool enabled)
    {
        var value = JsonSerializer.SerializeToElement(new
        { schema_version = 1, operation_id = operationId, expected_revision = expectedRevision, enabled });
        ParseUpdate(value);
        return value;
    }

    public static HermesLocalCompatibilityUpdate ParseUpdate(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "operation_id", "expected_revision", "enabled");
        Integer(fields["schema_version"], 1, 1);
        return new(Operation(fields["operation_id"]), Integer(fields["expected_revision"], 0, long.MaxValue - 1),
            Boolean(fields["enabled"]));
    }

    public static HermesLocalCompatibilityPreference ParsePreference(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "revision", "enabled");
        Integer(fields["schema_version"], 1, 1);
        var revision = Integer(fields["revision"], 0, long.MaxValue);
        var enabled = Boolean(fields["enabled"]);
        if (revision == 0 && enabled) throw Invalid();
        return new(revision, enabled);
    }

    public static HermesLocalCompatibilityCommit ParseCommit(JsonElement value, HermesLocalCompatibilityUpdate request)
    {
        // Validate externally constructed typed requests too.
        SetRequest(request.OperationId, request.ExpectedRevision, request.Enabled);
        var fields = Exact(value, "preference", "receipt", "replay");
        var preference = ParsePreference(fields["preference"]);
        var receiptFields = Exact(fields["receipt"], "schema_version", "operation_id", "expected_revision",
            "revision", "enabled", "preference_digest", "applies_from");
        Integer(receiptFields["schema_version"], 1, 1);
        var operation = Operation(receiptFields["operation_id"]);
        var expected = Integer(receiptFields["expected_revision"], 0, long.MaxValue - 1);
        var committed = new HermesLocalCompatibilityPreference(Integer(receiptFields["revision"], 1, long.MaxValue),
            Boolean(receiptFields["enabled"]));
        var digest = Text(receiptFields["preference_digest"]);
        if (operation != request.OperationId || expected != request.ExpectedRevision || committed.Enabled != request.Enabled
            || committed.Revision != expected + 1 || digest != Digest(committed)
            || Text(receiptFields["applies_from"]) != AppliesFrom) throw Invalid();
        if (fields["replay"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        var replay = fields["replay"].GetBoolean();
        // A replay may refer to a retained older receipt. It does not roll back
        // or imply that the old enabled remains the current preference.
        if (preference.Revision < committed.Revision
            || preference.Revision == committed.Revision && preference != committed
            || !replay && preference != committed) throw Invalid();
        return new(preference, new(operation, expected, committed, digest), replay);
    }

    public static string Digest(HermesLocalCompatibilityPreference value)
    {
        if (value.Revision < 0 || value.Revision == 0 && value.Enabled)
            throw Invalid();
        // Matches native sorted, compact, ASCII JSON exactly; independent of locale.
        var canonical = "{\"enabled\":" + (value.Enabled ? "true" : "false")
            + ",\"revision\":" + value.Revision.ToString(CultureInfo.InvariantCulture) + ",\"schema_version\":1}";
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))).ToLowerInvariant();
    }

    private static Dictionary<string, JsonElement> Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > MaximumPacketBytes)
            throw Invalid();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Contains(field.Name, StringComparer.Ordinal) || !fields.TryAdd(field.Name, field.Value))
                throw Invalid();
        if (fields.Count != names.Length) throw Invalid();
        return fields;
    }

    private static long Integer(JsonElement value, long minimum, long maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)
            || number < minimum || number > maximum) throw Invalid();
        return number;
    }

    private static bool Boolean(JsonElement value)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid();

    private static string Text(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();

    private static string Operation(JsonElement value)
    {
        var text = Text(value);
        if (text.Length is < 1 or > 96 || text.Any(c => c is not (>= 'a' and <= 'z')
            and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_' and not '-')) throw Invalid();
        return text;
    }

    private static InvalidDataException Invalid() => new("Local compatibility preference packet was rejected.");
}
