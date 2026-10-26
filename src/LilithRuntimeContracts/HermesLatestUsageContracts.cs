using System.Text.Json;

namespace LilithRuntimeContracts;

// Replaces one ephemeral host-owned observation. No session-counter differencing or prices.
public sealed record HermesLatestUsageSnapshot(long Revision, string? TurnId, string State, long RequestCount,
    long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheWriteTokens);

public static class HermesLatestUsageContract
{
    public const string GetMethod = "usage.latest.get";
    public static JsonElement GetRequest() => JsonSerializer.SerializeToElement(new { });
    public static HermesLatestUsageSnapshot Parse(JsonElement value)
    {
        string[] expected = { "schema_version", "revision", "turn_id", "state", "request_count", "input_tokens",
            "output_tokens", "cache_read_tokens", "cache_write_tokens" };
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid latest usage object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!expected.Contains(item.Name, StringComparer.Ordinal) || !fields.TryAdd(item.Name, item.Value))
                throw new InvalidDataException("Invalid latest usage fields.");
        if (fields.Count != expected.Length || Count(fields["schema_version"]) != 1)
            throw new InvalidDataException("Unsupported latest usage schema.");
        var turn = fields["turn_id"];
        var turnId = turn.ValueKind == JsonValueKind.Null ? null : turn.ValueKind == JsonValueKind.String
            ? turn.GetString() : throw new InvalidDataException("Invalid usage turn identity.");
        if (turnId is not null && (string.IsNullOrWhiteSpace(turnId) || turnId.Length > 256 || turnId.Any(char.IsControl)))
            throw new InvalidDataException("Invalid usage turn identity.");
        var state = fields["state"].ValueKind == JsonValueKind.String ? fields["state"].GetString() : null;
        if (state is not ("none" or "pending" or "partial" or "completed"))
            throw new InvalidDataException("Invalid latest usage state.");
        var snapshot = new HermesLatestUsageSnapshot(Count(fields["revision"]), turnId, state,
            Count(fields["request_count"]), Optional(fields["input_tokens"]), Optional(fields["output_tokens"]),
            Optional(fields["cache_read_tokens"]), Optional(fields["cache_write_tokens"]));
        if ((state == "none") != (turnId is null)
            || state == "none" && (snapshot.RequestCount != 0 || snapshot.InputTokens is not null || snapshot.OutputTokens is not null
                || snapshot.CacheReadTokens is not null || snapshot.CacheWriteTokens is not null)
            || snapshot.InputTokens is { } input && (snapshot.CacheReadTokens > input || snapshot.CacheWriteTokens > input
                || snapshot.CacheReadTokens is { } read && snapshot.CacheWriteTokens is { } write && read > input - write))
            throw new InvalidDataException("Contradictory latest usage observation.");
        return snapshot;
    }
    private static long Count(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count)
        && count >= 0 && count <= 9_000_000_000_000_000L ? count : throw new InvalidDataException("Invalid usage count.");
    private static long? Optional(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : Count(value);
}
