using System.Globalization;
using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
{
    internal D046HermesRuntimeRequestBinding BindExpressionGet() => Bind("expression.get", new { });
    internal D046HermesRuntimeRequestBinding BindExpressionVersion(int revision)
    { D048ExpressionLimits.Revision(revision); return Bind("expression.version.get", new { revision }); }
    internal D046HermesRuntimeRequestBinding BindExpressionSave(string operationId, int expectedRevision, string content, string reason)
    {
        D048ExpressionLimits.Operation(operationId); D048ExpressionLimits.Revision(expectedRevision);
        D048ExpressionLimits.Text(content, D048ExpressionLimits.MaximumContentBytes);
        D048ExpressionLimits.Text(reason, D048ExpressionLimits.MaximumReasonBytes, reason: true);
        return Bind("expression.save", new { operation_id = operationId, expected_revision = expectedRevision, content, reason });
    }
    internal D046HermesRuntimeRequestBinding BindExpressionRevert(string operationId, int expectedRevision, int targetRevision, string reason)
    {
        D048ExpressionLimits.Operation(operationId); D048ExpressionLimits.Revision(expectedRevision);
        D048ExpressionLimits.Revision(targetRevision); D048ExpressionLimits.Text(reason, D048ExpressionLimits.MaximumReasonBytes, reason: true);
        if (targetRevision > expectedRevision) throw new ArgumentException("Unknown expression revision.");
        return Bind("expression.revert", new { operation_id = operationId, expected_revision = expectedRevision, target_revision = targetRevision, reason });
    }
    private JsonElement RequireExpressionSuccess(LilithRuntimeResponse response, D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        lock (_gate)
        {
            if (envelope.GenerationId == Guid.Empty) throw new InvalidDataException("Expression response has no Runtime generation.");
            ObserveGenerationLocked(envelope);
            if (response.Method != binding.Method || binding.Method is not ("expression.get" or "expression.version.get" or "expression.save" or "expression.revert"))
                throw new InvalidDataException("Expression response method mismatch.");
            if (!response.Succeeded)
            {
                var code = response.HermesPayload.ValueKind == JsonValueKind.Object
                    && response.HermesPayload.TryGetProperty("code", out var property) && property.ValueKind == JsonValueKind.String
                    ? property.GetString() : null;
                if (code is "expression.invalid_request" or "expression.revision_conflict" or "expression.version_not_found"
                    or "expression.secret_rejected" or "expression.capacity_reached" or "expression.operation_failed" or "expression.result_uncertain")
                    throw new D048ExpressionOperationException(code);
                throw new InvalidDataException("Expression operation result is unconfirmed.");
            }
            return response.HermesPayload;
        }
    }
    internal D048ExpressionCatalog DecodeExpressionCatalog(LilithRuntimeResponse response, D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        if (binding.Method == "expression.version.get") throw new InvalidDataException("Expected expression catalog.");
        var values = ExpressionObject(RequireExpressionSuccess(response, envelope, binding), "schema_version", "revision", "content",
            "content_sha256", "base_identity_fingerprint", "history", "automatic_updates_enabled");
        var version = ReadExpressionVersion(values);
        if (values["automatic_updates_enabled"].ValueKind != JsonValueKind.False || values["history"].ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid expression catalog state.");
        var history = new List<D048ExpressionHistory>();
        foreach (var row in values["history"].EnumerateArray())
        {
            if (history.Count >= D048ExpressionLimits.MaximumRevisions) throw new InvalidDataException("Expression history limit exceeded.");
            var item = ExpressionObject(row, "revision", "created_at_utc", "reason", "content_sha256", "reverted_from");
            var revision = ExpressionRevision(item["revision"]);
            var date = ExpressionString(item["created_at_utc"]);
            var reason = ExpressionString(item["reason"]);
            if (revision == 0 || date.Length > 128 || !(date.EndsWith('Z') || date.EndsWith("+00:00", StringComparison.Ordinal))
                || !DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                || instant.Offset != TimeSpan.Zero) throw new InvalidDataException("Invalid expression history revision or date.");
            try { D048ExpressionLimits.Text(reason, D048ExpressionLimits.MaximumReasonBytes, reason: true); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid expression reason.", e); }
            int? reverted = item["reverted_from"].ValueKind == JsonValueKind.Null ? null : ExpressionRevision(item["reverted_from"]);
            if (reverted >= revision) throw new InvalidDataException("Invalid restored expression version.");
            history.Add(new(revision, date, reason, ExpressionHash(item["content_sha256"]), reverted));
        }
        if (history.Count != version.Revision || !history.Select(h => h.Revision).SequenceEqual(Enumerable.Range(1, version.Revision))
            || history.Count != 0 && history[^1].ContentSha256 != version.ContentSha256)
            throw new InvalidDataException("Expression history does not match its active version.");
        foreach (var row in history)
            if (row.RevertedFrom is { } source && row.ContentSha256 != (source == 0
                ? D048ExpressionLimits.Hash("") : history[source - 1].ContentSha256))
                throw new InvalidDataException("Expression restoration history digest mismatch.");
        if (binding.Method is "expression.save" or "expression.revert")
        {
            var request = binding.HermesPayload;
            if (version.Revision != request.GetProperty("expected_revision").GetInt32() + 1
                || history[^1].Reason != request.GetProperty("reason").GetString()
                || binding.Method == "expression.save" && (version.Content != request.GetProperty("content").GetString() || history[^1].RevertedFrom is not null)
                || binding.Method == "expression.revert" && history[^1].RevertedFrom != request.GetProperty("target_revision").GetInt32())
                throw new InvalidDataException("Expression receipt does not match its request.");
            if (binding.Method == "expression.revert")
            {
                var target = request.GetProperty("target_revision").GetInt32();
                var targetHash = target == 0 ? D048ExpressionLimits.Hash("") : history.Single(h => h.Revision == target).ContentSha256;
                if (version.ContentSha256 != targetHash) throw new InvalidDataException("Restored content does not match its retained version.");
            }
        }
        return new(version.Revision, version.Content, version.ContentSha256, version.BaseIdentityFingerprint, history.AsReadOnly());
    }
    internal D048ExpressionVersion DecodeExpressionVersion(LilithRuntimeResponse response, D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        if (binding.Method != "expression.version.get") throw new InvalidDataException("Expected expression version.");
        var result = ReadExpressionVersion(ExpressionObject(RequireExpressionSuccess(response, envelope, binding),
            "schema_version", "revision", "content", "content_sha256", "base_identity_fingerprint"));
        if (result.Revision != binding.HermesPayload.GetProperty("revision").GetInt32()) throw new InvalidDataException("Expression version mismatch.");
        return result;
    }
    private static D048ExpressionVersion ReadExpressionVersion(Dictionary<string, JsonElement> values)
    {
        if (values["schema_version"].ValueKind != JsonValueKind.Number || !values["schema_version"].TryGetInt32(out var schema) || schema != 1)
            throw new InvalidDataException("Unsupported expression schema.");
        var revision = ExpressionRevision(values["revision"]);
        var content = ExpressionString(values["content"]);
        try { D048ExpressionLimits.Text(content, D048ExpressionLimits.MaximumContentBytes); }
        catch (ArgumentException e) { throw new InvalidDataException("Invalid expression content.", e); }
        var hash = ExpressionHash(values["content_sha256"]);
        var identity = ExpressionString(values["base_identity_fingerprint"]);
        if (identity != D048ExpressionLimits.BaseIdentity || hash != D048ExpressionLimits.Hash(content) || revision == 0 && content != "")
            throw new InvalidDataException("Expression content or base identity mismatch.");
        return new(revision, content, hash, identity);
    }
    private static Dictionary<string, JsonElement> ExpressionObject(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected expression object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !result.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("Unknown or duplicate expression field.");
        if (result.Count != names.Length) throw new InvalidDataException("Missing expression field.");
        return result;
    }
    private static string ExpressionString(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidDataException("Expected expression string.");
    private static int ExpressionRevision(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var revision) || revision is < 0 or > D048ExpressionLimits.MaximumRevisions)
            throw new InvalidDataException("Invalid expression revision.");
        return revision;
    }
    private static string ExpressionHash(JsonElement value)
    {
        var hash = ExpressionString(value);
        if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid expression digest.");
        return hash;
    }
}
