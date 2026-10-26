using System.Security.Cryptography;
using System.Text.Json;

namespace LilithRuntimeContracts;

/// <summary>
/// A Runtime observation of a retained FIFO item after a proved no-mutation
/// failure. This is not consent and never releases or retries an action.
/// </summary>
public static class RuntimeApprovalRetryProof
{
    public const string FailureCode = "runtime.approval_no_mutation";
    public const string Source = "lilith-runtime.approval-no-mutation.v1";

    public static JsonElement Create(
        Guid generation, string sessionId, string choice,
        string patternKey, IReadOnlyList<string> choices)
        => JsonSerializer.SerializeToElement(new
        {
            schema_version = 1,
            source = Source,
            runtime_generation = generation.ToString("N"),
            session_id = sessionId,
            choice,
            approval_fingerprint = Fingerprint(patternKey, choices),
            no_mutation = true,
            pending_retained = true
        });

    public static bool Matches(
        JsonElement value, string generation, string sessionId, string choice,
        string patternKey, IReadOnlyList<string> choices)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var expected = new HashSet<string>(new[]
        {
            "schema_version", "source", "runtime_generation", "session_id",
            "choice", "approval_fingerprint", "no_mutation", "pending_retained"
        }, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!expected.Remove(property.Name)) return false;
        if (expected.Count != 0) return false;
        return value.GetProperty("schema_version").ValueKind == JsonValueKind.Number
            && value.GetProperty("schema_version").TryGetInt32(out var version)
            && version == 1
            && TextEquals(value, "source", Source)
            && TextEquals(value, "runtime_generation", generation)
            && TextEquals(value, "session_id", sessionId)
            && TextEquals(value, "choice", choice)
            && TextEquals(value, "approval_fingerprint", Fingerprint(patternKey, choices))
            && value.GetProperty("no_mutation").ValueKind == JsonValueKind.True
            && value.GetProperty("pending_retained").ValueKind == JsonValueKind.True;
    }

    private static bool TextEquals(JsonElement value, string name, string expected)
        => value.GetProperty(name).ValueKind == JsonValueKind.String
            && string.Equals(value.GetProperty(name).GetString(), expected, StringComparison.Ordinal);

    private static string Fingerprint(string patternKey, IReadOnlyList<string> choices)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { pattern_key = patternKey, choices }))).ToLowerInvariant();
}
