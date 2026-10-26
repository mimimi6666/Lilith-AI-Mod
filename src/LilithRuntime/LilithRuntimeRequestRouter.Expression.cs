using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private const string ExpressionBaseIdentity =
        LilithIdentityPackageV9Loader.IdentityFingerprintSha256;
    private const int ExpressionMaximumRevisions = 256;
    private const int ExpressionMaximumContentBytes = 32768;
    private const int ExpressionMaximumReasonBytes = 1024;

    private static bool IsExpressionManagementRpc(string method) => method is
        "expression.get" or "expression.version.get" or "expression.save" or "expression.revert";

    private static void ValidateExpressionRequest(string method, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (values.Count != AllowedProperties(method).Length)
            throw new InvalidDataException("Expression request fields were rejected.");
        if (method == "expression.version.get")
            RequiredInt64(values, "revision", 0, ExpressionMaximumRevisions);
        if (method is not ("expression.save" or "expression.revert")) return;
        ValidateMemoryOperationId(RequiredTrimmedText(values, "operation_id", 128));
        RequiredInt64(values, "expected_revision", 0, ExpressionMaximumRevisions);
        ExpressionText(values, "reason", ExpressionMaximumReasonBytes);
        if (method == "expression.save")
            ExpressionContent(values);
        else
            RequiredInt64(values, "target_revision", 0, ExpressionMaximumRevisions);
    }

    private async Task<LilithRuntimeResponse> RouteExpressionManagementAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        var mutation = method is "expression.save" or "expression.revert";
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // This owner-management channel is not registered as a model tool.
            // The provider commits an independent CAS revision, never a session,
            // identity root, memory entry, tool grant, or model configuration.
            var result = await _requestAsync("memory.provider.manage",
                JsonSerializer.SerializeToElement(new { action = method, request = parameters }),
                cancellationToken).ConfigureAwait(false);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("code", out _))
            {
                var failure = ExpressionObject(result, "schema_version", "code", "message");
                RequiredInt64(failure, "schema_version", 1, 1);
                var code = RequiredTrimmedText(failure, "code", 64);
                RequiredText(failure, "message", 1024);
                var message = code switch
                {
                    "expression.invalid_request" => "Expression request was rejected.",
                    "expression.revision_conflict" => "The expression version changed. Refresh before saving.",
                    "expression.version_not_found" => "The requested expression version does not exist.",
                    "expression.secret_rejected" => "Remove sensitive credentials before saving expression text.",
                    "expression.capacity_reached" => "The retained expression version limit was reached.",
                    "expression.operation_failed" => "The expression operation could not be verified.",
                    _ => throw new InvalidDataException("Unknown expression failure.")
                };
                // A persistence failure may occur after commit. Never call it
                // a proven rejection or expose native diagnostics/private paths.
                return Failure(method, mutation && code == "expression.operation_failed"
                    ? "expression.result_uncertain" : code, message);
            }
            ValidateExpressionResult(method, parameters, result);
            return Success(method, result);
        }
        catch (HermesRpcException error) when (error.Code == 5035)
        {
            // Preserve the existing native-config quarantine boundary.
            throw;
        }
        catch (HermesRequestNotSentException)
        {
            return Failure(method, "expression.request_not_sent", "The expression request was not sent.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A cancelled waiter or malformed receipt is not proof of rollback.
            // The UI retains the draft and asks for an explicit catalog refresh.
            return Failure(method, mutation ? "expression.result_uncertain" : "expression.operation_failed",
                mutation ? "The expression save outcome is unknown. Refresh the retained versions before retrying."
                    : "The expression data could not be verified.");
        }
    }

    private static void ValidateExpressionResult(string method, JsonElement request, JsonElement result)
    {
        var isVersion = method == "expression.version.get";
        var values = isVersion
            ? ExpressionObject(result, "schema_version", "revision", "content", "content_sha256", "base_identity_fingerprint")
            : ExpressionObject(result, "schema_version", "revision", "content", "content_sha256", "base_identity_fingerprint",
                "history", "automatic_updates_enabled");
        RequiredInt64(values, "schema_version", 1, 1);
        var revision = (int)RequiredInt64(values, "revision", 0, ExpressionMaximumRevisions);
        var content = ExpressionContent(values);
        var digest = ExpressionDigest(values, "content_sha256");
        if (RequiredText(values, "base_identity_fingerprint", 64) != ExpressionBaseIdentity
            || digest != ExpressionHash(content) || revision == 0 && content.Length != 0)
            throw new InvalidDataException("Expression version identity or content was rejected.");
        if (isVersion)
        {
            if (revision != request.GetProperty("revision").GetInt32())
                throw new InvalidDataException("Expression version did not match its request.");
            return;
        }
        if (RequiredBoolean(values, "automatic_updates_enabled")
            || values["history"].ValueKind != JsonValueKind.Array
            || values["history"].GetArrayLength() != revision)
            throw new InvalidDataException("Expression catalog scope was rejected.");
        var historyHashes = new List<string> { ExpressionHash("") };
        string? lastReason = null;
        int? lastReverted = null;
        var index = 0;
        foreach (var row in values["history"].EnumerateArray())
        {
            index++;
            var entry = ExpressionObject(row, "revision", "created_at_utc", "reason", "content_sha256", "reverted_from");
            RequiredInt64(entry, "revision", index, index);
            var date = RequiredText(entry, "created_at_utc", 64);
            if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                || instant.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Expression history timestamp was rejected.");
            lastReason = ExpressionText(entry, "reason", ExpressionMaximumReasonBytes);
            lastReverted = entry["reverted_from"].ValueKind == JsonValueKind.Null ? null
                : (int)RequiredInt64(entry, "reverted_from", 0, index - 1);
            var hash = ExpressionDigest(entry, "content_sha256");
            if (lastReverted.HasValue && hash != historyHashes[lastReverted.Value])
                throw new InvalidDataException("Expression restored history digest was rejected.");
            historyHashes.Add(hash);
        }
        if (historyHashes[revision] != digest)
            throw new InvalidDataException("Expression history does not match its current version.");
        if (method is "expression.save" or "expression.revert")
        {
            if (revision != request.GetProperty("expected_revision").GetInt32() + 1
                || lastReason != request.GetProperty("reason").GetString())
                throw new InvalidDataException("Expression commit receipt does not match its request.");
            if (method == "expression.save"
                && (content != request.GetProperty("content").GetString() || lastReverted.HasValue))
                throw new InvalidDataException("Expression save receipt was rejected.");
            if (method == "expression.revert"
                && lastReverted != request.GetProperty("target_revision").GetInt32())
                throw new InvalidDataException("Expression revert receipt was rejected.");
        }
    }

    private static Dictionary<string, JsonElement> ExpressionObject(JsonElement value, params string[] names)
    {
        var result = ReadExactObject(value, names);
        if (result.Count != names.Length) throw new InvalidDataException("Expression fields were rejected.");
        return result;
    }

    private static string ExpressionContent(IReadOnlyDictionary<string, JsonElement> values)
        => ExpressionText(values, "content", ExpressionMaximumContentBytes);

    private static string ExpressionText(IReadOnlyDictionary<string, JsonElement> values, string name, int limit)
    {
        if (!values.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Expression text is required.");
        var text = value.GetString()!;
        if (StrictUtf8.GetByteCount(text) > limit
            || text.EnumerateRunes().Any(r => System.Text.Rune.GetUnicodeCategory(r) is
                UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                && r.Value is not (9 or 10 or 13)))
            throw new InvalidDataException("Expression content was rejected.");
        return text;
    }

    private static string ExpressionDigest(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var hash = RequiredText(values, name, 64);
        if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Expression digest was rejected.");
        return hash;
    }

    private static string ExpressionHash(string content) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(content))).ToLowerInvariant();
}
