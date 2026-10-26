using System.Text.Json;

namespace LilithControlCenter;

internal static class D052HermesClarificationResultDisplay
{
    // Read-only projection of the native clarify tool's durable JSON result.
    // Never infer an answer from assistant prose, tool summaries or an error.
    internal static D046HermesClarificationAnswer? TryRead(string toolName, JsonElement result)
    {
        if (toolName != "clarify") return null;
        JsonDocument? parsed = null;
        try
        {
            if (result.ValueKind == JsonValueKind.String)
            {
                var json = result.GetString();
                if (string.IsNullOrWhiteSpace(json)) return null;
                parsed = JsonDocument.Parse(json);
                result = parsed.RootElement;
            }
            if (result.ValueKind != JsonValueKind.Object
                || result.TryGetProperty("error", out _)
                || !result.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.String
                || !result.TryGetProperty("user_response", out var response)) return null;
            string answer;
            if (response.ValueKind == JsonValueKind.String) answer = response.GetString() ?? string.Empty;
            else if (response.ValueKind == JsonValueKind.Array
                && response.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String))
                answer = string.Join("\n", response.EnumerateArray().Select(value => value.GetString()));
            else return null;
            return string.IsNullOrWhiteSpace(answer) ? null : new(question.GetString() ?? string.Empty, answer);
        }
        catch (JsonException) { return null; }
        finally { parsed?.Dispose(); }
    }
}
