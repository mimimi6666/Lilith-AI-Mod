using System.Text.Json;

namespace LilithRuntimeContracts;

// Native terminal disclosure, never a budget, tool permission or model authority.
public sealed record HermesUsageBudgetConstraint(
    string WireFormat, string Parameter, long? RetainedValue, string Reason);

public sealed record HermesUsageBudgetTerminalError(string Code);

/// <summary>Shared strict wire decoder for optional native terminal metadata.</summary>
public static class HermesUsageBudgetTerminalMetadata
{
    public static IReadOnlyList<HermesUsageBudgetConstraint> DecodeConstraints(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw Invalid();
        if (!payload.TryGetProperty("budget_constraints", out var value))
            return Array.Empty<HermesUsageBudgetConstraint>();
        if (payload.EnumerateObject().Count(p => p.Name == "budget_constraints") != 1
            || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 32)
            throw Invalid();
        var result = new List<HermesUsageBudgetConstraint>();
        foreach (var item in value.EnumerateArray())
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Any(p => !names.Add(p.Name))
                || !names.SetEquals(new[] { "schema_version", "wire_format", "parameter", "retained_value", "reason" })
                || item.GetProperty("schema_version").ValueKind != JsonValueKind.Number
                || !item.GetProperty("schema_version").TryGetInt32(out var version) || version != 1)
                throw Invalid();
            var wire = Text(item, "wire_format");
            var parameter = Text(item, "parameter");
            var reason = Text(item, "reason");
            var permitted = (wire, parameter, reason) switch
            {
                ("anthropic_messages", "max_tokens" or "thinking.budget_tokens", "api_required_parameter") => true,
                ("bedrock_converse", "thinking.budget_tokens", "api_required_parameter") => true,
                ("chat_completions" or "chat_max_tokens" or "chat_max_completion_tokens",
                    "thinking_budget" or "reasoning.max_tokens", "unreviewed_optional_parameter") => true,
                ("native_context", "context_length" or "threshold_tokens_cap" or "threshold_basis_points",
                    "existing_context_setting") => true,
                _ => false
            };
            var numeric = item.GetProperty("retained_value");
            long? retained = null;
            if (numeric.ValueKind != JsonValueKind.Null)
            {
                if (numeric.ValueKind != JsonValueKind.Number || !numeric.TryGetInt64(out var number) || number < 0)
                    throw Invalid();
                retained = number;
            }
            if (!permitted) throw Invalid();
            var constraint = new HermesUsageBudgetConstraint(wire, parameter, retained, reason);
            if (result.Contains(constraint)) throw Invalid();
            result.Add(constraint);
        }
        return result.AsReadOnly();
    }

    public static HermesUsageBudgetTerminalError? DecodeError(JsonElement payload, bool isError)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!payload.TryGetProperty("budget_error", out var value)) return null;
        if (!isError || value.ValueKind != JsonValueKind.String
            || payload.EnumerateObject().Count(p => p.Name == "budget_error") != 1)
            throw Invalid();
        return ParseErrorCode(value.GetString()!);
    }

    public static HermesUsageBudgetTerminalError ParseErrorCode(string code)
    {
        if (code is null || code.Length is < 1 or > 96 || code[0] is < 'a' or > 'z'
            || code.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))
            throw Invalid();
        return new(code);
    }

    private static string Text(JsonElement item, string name) =>
        item.GetProperty(name).ValueKind == JsonValueKind.String
            ? item.GetProperty(name).GetString()!
            : throw Invalid();

    private static InvalidDataException Invalid() => new("Native economic terminal metadata was malformed.");
}
