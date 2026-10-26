using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

// Economic preferences are not permissions, session settings, or model defaults.
public sealed record HermesUsageBudgetPreference(long Revision, int Level);
public sealed record HermesUsageBudgetUpdate(string OperationId, long ExpectedRevision, int Level);
public sealed record HermesUsageBudgetReceipt(string OperationId, long ExpectedRevision,
    HermesUsageBudgetPreference Committed, string PreferenceDigest);
public sealed record HermesUsageBudgetCommit(HermesUsageBudgetPreference Preference,
    HermesUsageBudgetReceipt Receipt, bool Replay);

/// <summary>Closed native economic-policy packets; no caller-supplied paths or numeric budgets.</summary>
public static class HermesUsageBudgetContract
{
    public const string GetMethod = "budget.get";
    public const string SetMethod = "budget.set";
    public const string AppliesFrom = "next_origin_turn";
    public const int MaximumPacketBytes = 2048;

    public static JsonElement GetRequest() => JsonSerializer.SerializeToElement(new { });

    public static void ValidateGetRequest(JsonElement value) => Exact(value);

    public static JsonElement SetRequest(string operationId, long expectedRevision, int level)
    {
        var value = JsonSerializer.SerializeToElement(new
        { schema_version = 1, operation_id = operationId, expected_revision = expectedRevision, level });
        ParseUpdate(value);
        return value;
    }

    public static HermesUsageBudgetUpdate ParseUpdate(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "operation_id", "expected_revision", "level");
        Integer(fields["schema_version"], 1, 1);
        return new(Operation(fields["operation_id"]), Integer(fields["expected_revision"], 0, long.MaxValue - 1),
            (int)Integer(fields["level"], 1, 6));
    }

    public static HermesUsageBudgetPreference ParsePreference(JsonElement value)
    {
        var fields = Exact(value, "schema_version", "revision", "level");
        Integer(fields["schema_version"], 1, 1);
        var revision = Integer(fields["revision"], 0, long.MaxValue);
        var level = (int)Integer(fields["level"], 1, 6);
        if (revision == 0 && level != 5) throw Invalid();
        return new(revision, level);
    }

    public static HermesUsageBudgetCommit ParseCommit(JsonElement value, HermesUsageBudgetUpdate request)
    {
        // Validate externally constructed typed requests too.
        SetRequest(request.OperationId, request.ExpectedRevision, request.Level);
        var fields = Exact(value, "preference", "receipt", "replay");
        var preference = ParsePreference(fields["preference"]);
        var receiptFields = Exact(fields["receipt"], "schema_version", "operation_id", "expected_revision",
            "revision", "level", "preference_digest", "applies_from");
        Integer(receiptFields["schema_version"], 1, 1);
        var operation = Operation(receiptFields["operation_id"]);
        var expected = Integer(receiptFields["expected_revision"], 0, long.MaxValue - 1);
        var committed = new HermesUsageBudgetPreference(Integer(receiptFields["revision"], 1, long.MaxValue),
            (int)Integer(receiptFields["level"], 1, 6));
        var digest = Text(receiptFields["preference_digest"]);
        if (operation != request.OperationId || expected != request.ExpectedRevision || committed.Level != request.Level
            || committed.Revision != expected + 1 || digest != Digest(committed)
            || Text(receiptFields["applies_from"]) != AppliesFrom) throw Invalid();
        if (fields["replay"].ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
        var replay = fields["replay"].GetBoolean();
        // A replay may refer to a retained older receipt. It does not roll back
        // or imply that the old level remains the current preference.
        if (preference.Revision < committed.Revision
            || preference.Revision == committed.Revision && preference != committed
            || !replay && preference != committed) throw Invalid();
        return new(preference, new(operation, expected, committed, digest), replay);
    }

    public static string Digest(HermesUsageBudgetPreference value)
    {
        if (value.Revision < 0 || value.Level is < 1 or > 6 || value.Revision == 0 && value.Level != 5)
            throw Invalid();
        // Matches native sorted, compact, ASCII JSON exactly; independent of locale.
        var canonical = "{\"level\":" + value.Level.ToString(CultureInfo.InvariantCulture)
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

    private static string Text(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();

    private static string Operation(JsonElement value)
    {
        var text = Text(value);
        if (text.Length is < 1 or > 96 || text.Any(c => c is not (>= 'a' and <= 'z')
            and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_' and not '-')) throw Invalid();
        return text;
    }

    private static InvalidDataException Invalid() => new("Economic preference packet was rejected.");
}
