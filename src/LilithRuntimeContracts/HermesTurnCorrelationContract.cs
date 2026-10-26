using System.IO;
using System.Text.Json;

namespace LilithRuntimeContracts;

public enum HermesGatewayEventTurnScope
{
    Turn = 1,
    Contextual = 2,
    Session = 3
}

public enum HermesPromptSubmitDisposition
{
    Streaming = 1,
    Queued = 2,
    Steered = 3,
    Redirected = 4,
    VoiceStopped = 5
}

public sealed record HermesPromptSubmitResult(
    HermesPromptSubmitDisposition Disposition,
    string TurnId,
    string UserMessageId,
    string AssistantMessageId,
    bool TurnIsolation,
    IReadOnlyList<JsonElement> SurvivorUserRowIds)
{
    public bool CreatesTurn => Disposition == HermesPromptSubmitDisposition.Streaming;
}

public enum HermesNativeAssistantContinuationDisposition
{
    Streaming = 1,
    Completed = 2,
    Silent = 3,
    Abandoned = 4
}

public sealed record HermesNativeAssistantContinuationResult(
    HermesNativeAssistantContinuationDisposition Disposition,
    string EventId,
    string TurnId,
    string AssistantMessageId,
    bool AlreadyProcessed,
    bool Attempted)
{
    public bool CreatesOrRejoinsTurn =>
        Disposition == HermesNativeAssistantContinuationDisposition.Streaming;
}

public static class HermesTurnCorrelationContract
{
    public const int IdentityCharacters = 32;
    public const string CronCompletionEventType = "cron.complete";
    public const string CronCompletionSchema = "lilith.cron.completion.v1";

    public static bool IsIdentity(string? value)
    {
        if (value is null || value.Length != IdentityCharacters)
            return false;
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }
        return true;
    }

    public static string RequireIdentity(string? value, string name)
        => IsIdentity(value)
            ? value!
            : throw new InvalidDataException(
                $"Hermes {name} must be exactly 32 lowercase hexadecimal characters.");

    public static HermesPromptSubmitResult DecodePromptSubmitResult(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes returned a malformed prompt receipt.");

        var properties = ExactProperties(value, "prompt.submit result");
        if (properties.Count == 1
            && properties.TryGetValue("voice_stopped", out var voiceStopped)
            && voiceStopped.ValueKind == JsonValueKind.True)
        {
            return new HermesPromptSubmitResult(
                HermesPromptSubmitDisposition.VoiceStopped,
                string.Empty,
                string.Empty,
                string.Empty,
                TurnIsolation: false,
                Array.Empty<JsonElement>());
        }

        if (!properties.TryGetValue("status", out var statusValue)
            || statusValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Hermes returned a malformed prompt receipt.");
        }
        var status = statusValue.GetString();
        if (status is "queued" or "steered" or "redirected")
        {
            if (properties.Count != 1)
                throw new InvalidDataException("Hermes returned a malformed busy prompt receipt.");
            return new HermesPromptSubmitResult(
                status switch
                {
                    "queued" => HermesPromptSubmitDisposition.Queued,
                    "steered" => HermesPromptSubmitDisposition.Steered,
                    _ => HermesPromptSubmitDisposition.Redirected
                },
                string.Empty,
                string.Empty,
                string.Empty,
                TurnIsolation: false,
                Array.Empty<JsonElement>());
        }
        if (!string.Equals(status, "streaming", StringComparison.Ordinal))
            throw new InvalidDataException("Hermes returned an unknown prompt receipt.");

        foreach (var property in properties.Keys)
        {
            if (property is not ("status" or "turn_id" or "user_message_id"
                or "assistant_message_id" or "turn_isolation"
                or "survivor_user_row_ids"))
            {
                throw new InvalidDataException("Hermes returned a malformed streaming prompt receipt.");
            }
        }
        var turnId = RequirePropertyIdentity(properties, "turn_id");
        var userMessageId = RequirePropertyIdentity(properties, "user_message_id");
        var assistantMessageId = RequirePropertyIdentity(
            properties,
            "assistant_message_id");
        if (new HashSet<string>(StringComparer.Ordinal)
            {
                turnId,
                userMessageId,
                assistantMessageId
            }.Count != 3)
        {
            throw new InvalidDataException(
                "Hermes turn, user-message, and assistant-message identities must be distinct.");
        }

        var turnIsolation = false;
        if (properties.TryGetValue("turn_isolation", out var isolation))
        {
            if (isolation.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Hermes turn isolation receipt was malformed.");
            turnIsolation = true;
        }

        IReadOnlyList<JsonElement> survivors = Array.Empty<JsonElement>();
        if (properties.TryGetValue("survivor_user_row_ids", out var survivorValues))
        {
            if (survivorValues.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Hermes survivor row identities were malformed.");
            var decoded = new List<JsonElement>();
            foreach (var survivor in survivorValues.EnumerateArray())
            {
                if (survivor.ValueKind == JsonValueKind.Null)
                {
                    decoded.Add(survivor.Clone());
                    continue;
                }
                if (survivor.ValueKind == JsonValueKind.Number
                    && survivor.TryGetInt64(out _))
                {
                    decoded.Add(survivor.Clone());
                    continue;
                }
                if (survivor.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(survivor.GetString())
                    && survivor.GetString()!.Length <= 1024
                    && !survivor.GetString()!.Any(char.IsControl))
                {
                    decoded.Add(survivor.Clone());
                    continue;
                }
                throw new InvalidDataException("Hermes survivor row identity was malformed.");
            }
            survivors = decoded;
        }

        if (properties.Count != 4
                + (turnIsolation ? 1 : 0)
                + (properties.ContainsKey("survivor_user_row_ids") ? 1 : 0))
        {
            throw new InvalidDataException("Hermes returned a malformed streaming prompt receipt.");
        }
        return new HermesPromptSubmitResult(
            HermesPromptSubmitDisposition.Streaming,
            turnId,
            userMessageId,
            assistantMessageId,
            turnIsolation,
            survivors);
    }

    public static HermesNativeAssistantContinuationResult
        DecodeNativeAssistantContinuationResult(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Hermes returned a malformed native assistant continuation receipt.");
        }

        var properties = ExactProperties(
            value,
            "native assistant continuation result");
        var hasGameReceipt = properties.TryGetValue("game_event_receipt", out var gameReceipt);
        if (!properties.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(new[]
            {
                "status",
                "event_id",
                "turn_id",
                "assistant_message_id",
                "already_processed",
                "attempted"
            }.Concat(hasGameReceipt ? new[] { "game_event_receipt" } : Array.Empty<string>())))
        {
            throw new InvalidDataException(
                "Hermes native assistant continuation receipt was not exact.");
        }
        if (properties["status"].ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Hermes native assistant continuation status was malformed.");
        }
        var disposition = properties["status"].GetString() switch
        {
            "streaming" => HermesNativeAssistantContinuationDisposition.Streaming,
            "completed" => HermesNativeAssistantContinuationDisposition.Completed,
            "silent" => HermesNativeAssistantContinuationDisposition.Silent,
            "abandoned" => HermesNativeAssistantContinuationDisposition.Abandoned,
            _ => throw new InvalidDataException(
                "Hermes native assistant continuation status was not recognized.")
        };
        var eventId = RequirePropertyIdentity(properties, "event_id");
        if (hasGameReceipt)
        {
            _ = LilithGameBridgeContracts.LilithMiniGameEventReceipt.Read(gameReceipt);
            if (disposition != HermesNativeAssistantContinuationDisposition.Completed)
                throw new InvalidDataException("Only completed native turns carry game event acknowledgement.");
        }
        var turnId = RequirePropertyIdentity(properties, "turn_id");
        var assistantMessageId = RequirePropertyIdentity(
            properties,
            "assistant_message_id");
        if (new HashSet<string>(StringComparer.Ordinal)
            {
                eventId,
                turnId,
                assistantMessageId
            }.Count != 3)
        {
            throw new InvalidDataException(
                "Hermes native event, turn, and assistant identities must be distinct.");
        }
        var alreadyProcessed = RequireBoolean(
            properties,
            "already_processed");
        var attempted = RequireBoolean(properties, "attempted");
        if ((!alreadyProcessed
                && disposition
                    != HermesNativeAssistantContinuationDisposition.Streaming)
            || (!attempted
                && disposition
                    != HermesNativeAssistantContinuationDisposition.Abandoned))
        {
            throw new InvalidDataException(
                "Hermes native assistant continuation state was contradictory.");
        }
        return new HermesNativeAssistantContinuationResult(
            disposition,
            eventId,
            turnId,
            assistantMessageId,
            alreadyProcessed,
            attempted);
    }

    public static HermesGatewayEventTurnScope EventScope(string eventType)
        => eventType switch
        {
            "message.user" or "message.start" or "message.delta"
                or "message.interim" or "message.complete"
                or "thinking.delta" or "reasoning.delta" or "reasoning.available"
                or "tool.generating" or "tool.start" or "tool.progress"
                or "tool.complete" or "tool.output_risk"
                or "moa.reference" or "moa.aggregating" or "moa.progress"
                or "moa.phase"
                or "subagent.start" or "subagent.thinking" or "subagent.tool"
                or "subagent.progress" or "subagent.complete"
                or "approval.request"
                or "clarify.request" or "clarify.expire"
                or "sudo.request" or "sudo.expire"
                or "secret.request" or "secret.expire"
                or "terminal.read.request" or "terminal.read.expire"
                or "preview.read.request" or "preview.read.expire"
                or "window.read.request" or "window.read.expire"
                or "mcp.setup.request" or "mcp.setup.expire"
                or "reaction" or "review.summary" or "preview.open"
                or "game.context_diagnostic"
                => HermesGatewayEventTurnScope.Turn,
            "status.update" or "error" or "notification.show"
                or "notification.clear" or "browser.progress"
                => HermesGatewayEventTurnScope.Contextual,
             "session.info" or "session.title" or "sessions.changed"
                 or CronCompletionEventType
                 or "skin.changed" or "voice.status" or "voice.transcript"
                 or "wake.detected" or "terminal.output" or "terminal.close"
                 or "preview.restart.start" or "preview.restart.complete"
                 or "rollback.restored" or "background.complete"
                 => HermesGatewayEventTurnScope.Session,
            _ => throw new InvalidDataException(
                "The Hermes gateway event turn scope was not recognized.")
        };

    public static string ValidateGatewayEvent(
        string eventType,
        JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes gateway event parameters were malformed.");
        var properties = ExactProperties(parameters, "gateway event");
        foreach (var property in properties.Keys)
        {
            if (property is not ("type" or "session_id" or "payload" or "turn_id"))
                throw new InvalidDataException("Hermes gateway event parameters were not exact.");
        }
        if (!properties.TryGetValue("type", out var typeValue)
            || typeValue.ValueKind != JsonValueKind.String
            || !string.Equals(typeValue.GetString(), eventType, StringComparison.Ordinal)
            || !properties.TryGetValue("session_id", out var sessionValue)
            || sessionValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Hermes gateway event identity was malformed.");
        }
        if (properties.TryGetValue("payload", out var payload)
            && payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Hermes gateway event payload was malformed.");
        }

        var scope = EventScope(eventType);
        var hasTurnId = properties.TryGetValue("turn_id", out var turnValue);
        if (scope == HermesGatewayEventTurnScope.Session)
        {
            if (hasTurnId)
                throw new InvalidDataException("Session-scoped Hermes event carried a turn identity.");
            return string.Empty;
        }
        if (!hasTurnId)
        {
            if (scope == HermesGatewayEventTurnScope.Contextual)
                return string.Empty;
            throw new InvalidDataException("Turn-scoped Hermes event omitted its turn identity.");
        }
        if (turnValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes gateway event turn identity was malformed.");
        return RequireIdentity(turnValue.GetString(), "event turn_id");
    }

    private static string RequirePropertyIdentity(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Hermes {name} was missing.");
        }
        return RequireIdentity(value.GetString(), name);
    }

    private static bool RequireBoolean(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Hermes {name} was malformed.");
        }
        return value.GetBoolean();
    }

    private static Dictionary<string, JsonElement> ExactProperties(
        JsonElement value,
        string description)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
                throw new InvalidDataException($"Hermes {description} contained a duplicate property.");
        }
        return properties;
    }
}
