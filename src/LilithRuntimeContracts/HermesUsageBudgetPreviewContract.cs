using System.Text;
using System.Text.Json;

namespace LilithRuntimeContracts;

public sealed record HermesUsageBudgetLevelPreview(int Level, long? RequestInputTokens,
    long? RequestOutputTokens, long? RequestThinkingTokens, long? LogicalIterations,
    long? TurnTokenAllowance, long? TurnActualCallAllowance, HermesInformationStrategy? Strategy = null);

public sealed record HermesInformationStrategy(long? HistoryTurns, long RelationshipEvery, long SummaryEvery,
    long? SummaryTargetTokens, long? OldToolChars, long BackgroundEvery);

// A preview is not a preference, receipt, provider capacity or applied-turn snapshot.
public sealed record HermesUsageBudgetPreview(string Scope, string ReferenceVersion,
    string InputCountMode, string MemoryPolicy, IReadOnlyList<HermesUsageBudgetLevelPreview> Levels);

public static class HermesUsageBudgetPreviewContract
{
    public const string PreviewMethod = "budget.preview";
    public const string ReferenceVersion = "lilith-economy-20260926-v2";
    public const string LegacyStrategyVersion = "lilith-strategies-20260927-v1";
    public const string PriorStrategyVersion = "lilith-strategies-20260927-v2";
    public const string ExpandedStrategyVersion = "lilith-strategies-20261006-v3";
    public const string StrategyVersion = "lilith-strategies-20261007-v4";
    public const long MaximumValue = 9007199254740991;
    private static readonly Lazy<HermesUsageBudgetPreview> Reference = new(LoadReference);
    public static HermesUsageBudgetPreview ReferencePreview => Reference.Value;
    private static readonly Lazy<HermesUsageBudgetPreview> StrategyReference = new(() => LoadReference("LilithRuntimeContracts.InformationStrategyReference.json"));
    public static HermesUsageBudgetPreview InformationReferencePreview => StrategyReference.Value;

    public static HermesUsageBudgetPreview Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > 8192)
            throw Invalid();
        var fields = Exact(value, "schema_version", "scope", "reference_version", "input_count_mode", "memory_policy", "levels");
        var version = Number(fields["schema_version"]);
        if (version is not (1 or 2)) throw Invalid();
        var strategy = version == 2;
        var reference = Text(fields["reference_version"]);
        if (strategy ? reference is not (StrategyVersion or ExpandedStrategyVersion or PriorStrategyVersion or LegacyStrategyVersion) : reference != ReferenceVersion)
            throw Invalid();
        var countMode = strategy ? "native" : "estimated";
        var memoryPolicy = strategy ? "batch_relationship_keep_explicit_profile" : "unchanged_no_per_level_count";
        var scope = Text(fields["scope"]);
        if (scope is not ("configured_preview" or "reference_preview")
            || Text(fields["input_count_mode"]) != countMode
            || Text(fields["memory_policy"]) != memoryPolicy) throw Invalid();
        var rows = fields["levels"];
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 6) throw Invalid();
        var levels = new List<HermesUsageBudgetLevelPreview>(6);
        foreach (var item in rows.EnumerateArray())
        {
            var names = new[] { "level", "request_input_tokens", "request_output_tokens", "request_thinking_tokens",
                "logical_iterations", "turn_token_allowance", "turn_actual_call_allowance" };
            var row = Exact(item, strategy ? names.Append("strategy").ToArray() : names);
            var level = Number(row["level"]);
            if (level != levels.Count + 1) throw Invalid();
            var parsed = new HermesUsageBudgetLevelPreview((int)level, Optional(row["request_input_tokens"]),
                Optional(row["request_output_tokens"]), Optional(row["request_thinking_tokens"]),
                Optional(row["logical_iterations"]), Optional(row["turn_token_allowance"]), Optional(row["turn_actual_call_allowance"]),
                strategy ? ParseStrategy(row["strategy"], level, reference) : null);
            if (!strategy && level <= 4 && (parsed.RequestInputTokens is null || parsed.RequestOutputTokens is null
                || parsed.TurnTokenAllowance is null || parsed.TurnActualCallAllowance is null)) throw Invalid();
            if ((strategy || level >= 5) && (parsed.TurnTokenAllowance is not null || parsed.TurnActualCallAllowance is not null)) throw Invalid();
            if (level == 6 && (parsed.RequestInputTokens is not null || parsed.RequestOutputTokens is not null
                || parsed.RequestThinkingTokens is not null || parsed.LogicalIterations is not null)) throw Invalid();
            levels.Add(parsed);
        }
        return new(scope, reference, countMode, memoryPolicy, levels.AsReadOnly());
    }

    private static HermesInformationStrategy ParseStrategy(JsonElement value, long level, string reference)
    {
        var row = Exact(value, "history_turns", "relationship_every", "summary_every", "summary_target_tokens", "old_tool_chars", "background_every");
        var result = new HermesInformationStrategy(Optional(row["history_turns"]), Number(row["relationship_every"]),
            Number(row["summary_every"]), Optional(row["summary_target_tokens"]), Optional(row["old_tool_chars"]), Number(row["background_every"]));
        if (result.RelationshipEvery > 8 || result.SummaryEvery > 64 || result.BackgroundEvery > 64
            || result.HistoryTurns > 128 || result.SummaryTargetTokens > 16384 || result.OldToolChars > 65536) throw Invalid();
        // Information targets and numeric usage ceilings are independent. V4
        // requires six structured rows while level six's numeric caps stay null.
        var structured = level <= 4 || reference == StrategyVersion
            || level == 5 && reference == ExpandedStrategyVersion;
        if (structured && (result.HistoryTurns is null || result.SummaryTargetTokens is null || result.OldToolChars is null)) throw Invalid();
        if (!structured && (result.HistoryTurns is not null || result.SummaryTargetTokens is not null || result.OldToolChars is not null
            || result.RelationshipEvery != 1 || result.SummaryEvery != 1 || result.BackgroundEvery != 1)) throw Invalid();
        return result;
    }

    private static HermesUsageBudgetPreview LoadReference() => LoadReference("LilithRuntimeContracts.UsageBudgetReference.json");
    private static HermesUsageBudgetPreview LoadReference(string resource)
    {
        using var stream = typeof(HermesUsageBudgetPreviewContract).Assembly.GetManifestResourceStream(
            resource) ?? throw Invalid();
        using var document = JsonDocument.Parse(stream);
        var result = Parse(document.RootElement);
        if (result.Scope != "reference_preview") throw Invalid();
        return result;
    }

    private static Dictionary<string, JsonElement> Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Contains(field.Name, StringComparer.Ordinal) || !fields.TryAdd(field.Name, field.Value)) throw Invalid();
        if (fields.Count != names.Length) throw Invalid();
        return fields;
    }
    private static long Number(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) && result is > 0 and <= MaximumValue
            ? result : throw Invalid();
    private static long? Optional(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : Number(value);
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();
    private static InvalidDataException Invalid() => new("Economic preview packet was rejected.");
}
