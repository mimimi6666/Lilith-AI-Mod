using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum PreparedProviderRoute
{
    Gemini = 0,
    Qwen = 1,
    OpenAI = 2,
    DeepSeek = 3,
    AnthropicClaude = 4,
    Ollama = 5,
    OpenAICompatible = 6,
    OpenRouter = 7,
    OllamaCloud = 8,
    OllamaLocal = 9
}

internal enum PreparedProviderProtocol
{
    GeminiGenerateContent = 0,
    OpenAiResponses = 1,
    OpenAiChatCompletions = 2,
    AnthropicMessages = 3,
    OllamaNativeChat = 4
}

internal enum PreparedProviderRole
{
    User = 0,
    Assistant = 1
}

internal enum PreparedProviderCapability
{
    TextOnly = 0
}

internal enum PreparedProviderCodecAdmission
{
    Encoded = 0,
    Parsed = 1,
    UnknownRoute = 2,
    RouteMismatch = 3,
    DuplicateCurrentTurn = 4,
    Expired = 5,
    MalformedResponse = 6,
    ToolBearingResponse = 7,
    EmptyResponse = 8,
    TerminalStatusRejected = 9
}

internal enum PreparedProviderRuntimeSeam
{
    PlayerReplyF2 = 0,
    ProactiveNoteF3 = 1
}

internal enum PreparedProviderRuntimeAdmission
{
    LegacyUnchanged = 0,
    UnifiedAuthorized = 1,
    SourceRouteRejected = 2
}

internal static class PreparedProviderRuntimeRoutePolicy
{
    internal static PreparedProviderRuntimeAdmission Evaluate(
        bool unifiedEnabled,
        PreparedProviderRuntimeSeam seam,
        LivingPreparedProviderRequest request)
    {
        if (!Enum.IsDefined(typeof(PreparedProviderRuntimeSeam), seam))
            throw new ArgumentOutOfRangeException(nameof(seam));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (!unifiedEnabled)
            return PreparedProviderRuntimeAdmission.LegacyUnchanged;

        var accepted = seam switch
        {
            PreparedProviderRuntimeSeam.PlayerReplyF2 =>
                request.SourceKind
                    == LivingPromptSourceKind.PlayerTriggered
                && request.Destination
                    == LivingPreparedCompletionDestination.PlayerReply
                && (string.Equals(
                        request.ProviderRouteId,
                        "OpenAI",
                        StringComparison.Ordinal)
                    || string.Equals(
                        request.ProviderRouteId,
                        "DeepSeek",
                        StringComparison.Ordinal)),
            PreparedProviderRuntimeSeam.ProactiveNoteF3 =>
                request.SourceKind
                    == LivingPromptSourceKind.BrainAuthorizedProactive
                && request.Destination
                    == LivingPreparedCompletionDestination.ProactiveNote
                && string.Equals(
                    request.ProviderRouteId,
                    "Gemini",
                    StringComparison.Ordinal),
            _ => false
        };
        return accepted
            ? PreparedProviderRuntimeAdmission.UnifiedAuthorized
            : PreparedProviderRuntimeAdmission.SourceRouteRejected;
    }
}

internal sealed class PreparedProviderTurn
{
    internal string TurnId { get; }
    internal PreparedProviderRole Role { get; }
    internal string ContentHash { get; }
    internal string Text { get; }

    internal PreparedProviderTurn(
        string turnId,
        PreparedProviderRole role,
        string contentHash,
        string text)
    {
        TurnId = LivingContinuityContractGuard.Identifier(
            turnId,
            nameof(turnId));
        if (!Enum.IsDefined(typeof(PreparedProviderRole), role))
            throw new ArgumentOutOfRangeException(nameof(role));
        Role = role;
        ContentHash = LivingContinuityContractGuard.Sha256(
            contentHash,
            nameof(contentHash));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        if (!string.Equals(
                LivingPromptHash.Of(Text),
                ContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Prepared turn content does not match its hash.",
                nameof(contentHash));
        }
    }
}

internal sealed class PreparedProviderExecutionRequest
{
    internal string CorrelationId { get; }
    internal string SourceRequestFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal PreparedProviderRoute Route { get; }
    internal PreparedProviderProtocol Protocol { get; }
    internal PreparedProviderCapability Capability { get; }
    internal LivingPreparedCompletionDestination Destination { get; }
    internal string SystemInstruction { get; }
    internal string SystemInstructionHash { get; }
    internal IReadOnlyList<PreparedProviderTurn> Turns { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset Deadline { get; }
    internal string ExecutionFingerprint { get; }
    internal bool AllowsTools => false;
    internal bool AllowsSearch => false;
    internal bool AllowsRetry => false;
    internal bool AllowsProviderFallback => false;
    internal bool ReadsMemory => false;
    internal bool ConstructsPrompt => false;

    private PreparedProviderExecutionRequest(
        LivingPreparedProviderRequest source,
        PreparedProviderRoute route,
        PreparedProviderProtocol protocol,
        IReadOnlyList<PreparedProviderTurn> turns)
    {
        CorrelationId = source.RequestId;
        SourceRequestFingerprint = source.RequestFingerprint;
        ProviderRouteId = source.ProviderRouteId;
        Route = route;
        Protocol = protocol;
        Capability = PreparedProviderCapability.TextOnly;
        Destination = source.Destination;
        SystemInstruction = source.SystemPrompt;
        SystemInstructionHash = source.SystemPromptHash;
        Turns = turns;
        RequestedAt = source.AttemptedAt;
        Deadline = source.ExpiresAt;
        ExecutionFingerprint = LivingPromptHash.Of(
            string.Join(
                "|",
                CorrelationId,
                SourceRequestFingerprint,
                ProviderRouteId,
                ((int)Route).ToString(CultureInfo.InvariantCulture),
                ((int)Protocol).ToString(CultureInfo.InvariantCulture),
                ((int)Capability).ToString(CultureInfo.InvariantCulture),
                ((int)Destination).ToString(
                    CultureInfo.InvariantCulture),
                SystemInstructionHash,
                string.Join(
                    ",",
                    Turns.Select(
                        item => LivingPromptHash.Of(
                            string.Join(
                                "|",
                                item.TurnId,
                                ((int)item.Role).ToString(
                                    CultureInfo.InvariantCulture),
                                item.ContentHash)))),
                RequestedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                Deadline.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture)));
    }

    internal static PreparedProviderCodecAdmission TryCreate(
        LivingPreparedProviderRequest source,
        DateTimeOffset now,
        out PreparedProviderExecutionRequest? request)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        request = null;
        if (now < source.AttemptedAt
            || now >= source.ExpiresAt)
        {
            return PreparedProviderCodecAdmission.Expired;
        }
        if (!PreparedProviderRouteMap.TryParse(
                source.ProviderRouteId,
                out var route,
                out var protocol))
        {
            return PreparedProviderCodecAdmission.UnknownRoute;
        }
        if (source.WorkingContext.Any(
                item => string.Equals(
                        item.TurnId,
                        source.CurrentRequest.TurnId,
                        StringComparison.Ordinal)
                    || string.Equals(
                        item.ContentHash,
                        source.CurrentRequest.ContentHash,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return PreparedProviderCodecAdmission.DuplicateCurrentTurn;
        }

        var turns = source.WorkingContext
            .Select(Copy)
            .Append(Copy(source.CurrentRequest))
            .ToArray();
        request = new PreparedProviderExecutionRequest(
            source,
            route,
            protocol,
            new ReadOnlyCollection<PreparedProviderTurn>(turns));
        return PreparedProviderCodecAdmission.Encoded;
    }

    private static PreparedProviderTurn Copy(
        FrozenLivingPromptTurn source)
        => new(
            source.TurnId,
            source.Role switch
            {
                LivingPromptTurnRole.User =>
                    PreparedProviderRole.User,
                LivingPromptTurnRole.Assistant =>
                    PreparedProviderRole.Assistant,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(source),
                    "Unsupported prepared Prompt role.")
            },
            source.ContentHash,
            source.Text);
}

internal static class PreparedProviderRouteMap
{
    internal static bool TryParse(
        string providerRouteId,
        out PreparedProviderRoute route,
        out PreparedProviderProtocol protocol)
    {
        switch (providerRouteId)
        {
            case "Gemini":
                route = PreparedProviderRoute.Gemini;
                protocol =
                    PreparedProviderProtocol.GeminiGenerateContent;
                return true;
            case "Qwen":
                route = PreparedProviderRoute.Qwen;
                protocol = PreparedProviderProtocol.OpenAiResponses;
                return true;
            case "QwenChat":
                route = PreparedProviderRoute.Qwen;
                protocol = PreparedProviderProtocol.OpenAiChatCompletions;
                return true;
            case "OpenAI":
                route = PreparedProviderRoute.OpenAI;
                protocol =
                    PreparedProviderProtocol.OpenAiChatCompletions;
                return true;
            case "DeepSeek":
                route = PreparedProviderRoute.DeepSeek;
                protocol =
                    PreparedProviderProtocol.OpenAiChatCompletions;
                return true;
            case "Claude":
                route = PreparedProviderRoute.AnthropicClaude;
                protocol = PreparedProviderProtocol.AnthropicMessages;
                return true;
            case "Ollama":
                route = PreparedProviderRoute.Ollama;
                protocol = PreparedProviderProtocol.OllamaNativeChat;
                return true;
            case "OpenRouter":
                route = PreparedProviderRoute.OpenRouter;
                protocol =
                    PreparedProviderProtocol.OpenAiChatCompletions;
                return true;
            case "OllamaCloud":
                route = PreparedProviderRoute.OllamaCloud;
                protocol = PreparedProviderProtocol.OllamaNativeChat;
                return true;
            case "OllamaLocal":
                route = PreparedProviderRoute.OllamaLocal;
                protocol = PreparedProviderProtocol.OllamaNativeChat;
                return true;
            case "OpenAICompatible":
            case "LlamaCpp":
            case "LMStudio":
            case "vLLM":
            case "LocalAI":
                route = PreparedProviderRoute.OpenAICompatible;
                protocol =
                    PreparedProviderProtocol.OpenAiChatCompletions;
                return true;
            default:
                route = default;
                protocol = default;
                return false;
        }
    }
}

internal sealed class PreparedProviderWirePayload
{
    internal string CorrelationId { get; }
    internal string ProviderRouteId { get; }
    internal PreparedProviderProtocol Protocol { get; }
    internal string Json { get; }
    internal string PayloadHash { get; }

    internal PreparedProviderWirePayload(
        PreparedProviderExecutionRequest request,
        string json)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        CorrelationId = request.CorrelationId;
        ProviderRouteId = request.ProviderRouteId;
        Protocol = request.Protocol;
        Json = json ?? throw new ArgumentNullException(nameof(json));
        PayloadHash = LivingPromptHash.Of(Json);
    }
}

internal sealed class PreparedProviderNormalizedCompletion
{
    internal string CorrelationId { get; }
    internal string ProviderRouteId { get; }
    internal string Text { get; }
    internal string TextHash { get; }
    internal string TerminalStatus { get; }
    internal int? InputTokens { get; }
    internal int? OutputTokens { get; }

    internal PreparedProviderNormalizedCompletion(
        PreparedProviderExecutionRequest request,
        string text,
        string terminalStatus,
        int? inputTokens,
        int? outputTokens)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException(
                "Normalized provider text cannot be empty.",
                nameof(text));
        CorrelationId = request.CorrelationId;
        ProviderRouteId = request.ProviderRouteId;
        Text = text;
        TextHash = LivingPromptHash.Of(text);
        TerminalStatus = terminalStatus ?? string.Empty;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }
}

internal interface IPreparedProviderExecutor
{
    Task<PreparedProviderNormalizedCompletion> ExecuteAsync(
        PreparedProviderExecutionRequest request,
        CancellationToken cancellationToken);
}

internal static class PreparedProviderTextCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    internal static PreparedProviderWirePayload Encode(
        PreparedProviderExecutionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        var payload = request.Protocol switch
        {
            PreparedProviderProtocol.GeminiGenerateContent =>
                EncodeGemini(request),
            PreparedProviderProtocol.OpenAiResponses =>
                EncodeResponses(request),
            PreparedProviderProtocol.OpenAiChatCompletions =>
                EncodeChat(request),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                "Unsupported prepared provider protocol.")
        };
        return new PreparedProviderWirePayload(request, payload);
    }

    internal static PreparedProviderCodecAdmission TryParse(
        PreparedProviderExecutionRequest request,
        string responseJson,
        out PreparedProviderNormalizedCompletion? completion)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (responseJson == null)
            throw new ArgumentNullException(nameof(responseJson));
        completion = null;
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            return request.Protocol switch
            {
                PreparedProviderProtocol.GeminiGenerateContent =>
                    ParseGemini(request, document.RootElement, out completion),
                PreparedProviderProtocol.OpenAiResponses =>
                    ParseResponses(
                        request,
                        document.RootElement,
                        out completion),
                PreparedProviderProtocol.OpenAiChatCompletions =>
                    ParseChat(request, document.RootElement, out completion),
                _ => PreparedProviderCodecAdmission.RouteMismatch
            };
        }
        catch (JsonException)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
        catch (InvalidOperationException)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
    }

    private static string EncodeGemini(
        PreparedProviderExecutionRequest request)
    {
        var contents = request.Turns.Select(
            item => new
            {
                role = item.Role == PreparedProviderRole.Assistant
                    ? "model"
                    : "user",
                parts = new[] { new { text = item.Text } }
            });
        var maxTokens =
            request.Destination
                is LivingPreparedCompletionDestination.ProactiveNote
                    or LivingPreparedCompletionDestination.ProactiveChat
                ? 320
                : 1024;
        return JsonSerializer.Serialize(
            new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new { text = request.SystemInstruction }
                    }
                },
                contents,
                generationConfig = new
                {
                    maxOutputTokens = maxTokens,
                    thinkingConfig = new { thinkingLevel = "MINIMAL" }
                }
            },
            JsonOptions);
    }

    private static string EncodeResponses(
        PreparedProviderExecutionRequest request)
        => JsonSerializer.Serialize(
            new
            {
                instructions = request.SystemInstruction,
                input = request.Turns.Select(
                    item => new
                    {
                        role = item.Role
                            == PreparedProviderRole.Assistant
                            ? "assistant"
                            : "user",
                        content = item.Text
                    }),
                max_output_tokens = 1536,
                store = false,
                reasoning = new { effort = "none" }
            },
            JsonOptions);

    private static string EncodeChat(
        PreparedProviderExecutionRequest request)
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = request.SystemInstruction
            }
        };
        messages.AddRange(
            request.Turns.Select(
                item => (object)new
                {
                    role = item.Role
                        == PreparedProviderRole.Assistant
                        ? "assistant"
                        : "user",
                    content = item.Text
                }));
        return JsonSerializer.Serialize(
            new
            {
                messages,
                max_tokens = 1024,
                temperature = 0.8
            },
            JsonOptions);
    }

    private static PreparedProviderCodecAdmission ParseGemini(
        PreparedProviderExecutionRequest request,
        JsonElement root,
        out PreparedProviderNormalizedCompletion? completion)
    {
        completion = null;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
        var text = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out _)
                || !part.TryGetProperty("text", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return PreparedProviderCodecAdmission
                    .ToolBearingResponse;
            }
            text.Add(value.GetString() ?? string.Empty);
        }
        var combined = string.Concat(text);
        if (string.IsNullOrWhiteSpace(combined))
            return PreparedProviderCodecAdmission.EmptyResponse;

        var status = TryString(candidate, "finishReason") ?? string.Empty;
        if (!string.Equals(
                status,
                "STOP",
                StringComparison.Ordinal)
            && !string.Equals(
                status,
                "MAX_TOKENS",
                StringComparison.Ordinal))
        {
            return PreparedProviderCodecAdmission
                .TerminalStatusRejected;
        }
        int? input = null;
        int? output = null;
        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            input = TryInt32(usage, "promptTokenCount");
            output = TryInt32(usage, "candidatesTokenCount");
        }
        completion = new PreparedProviderNormalizedCompletion(
            request,
            combined,
            status,
            input,
            output);
        return PreparedProviderCodecAdmission.Parsed;
    }

    private static PreparedProviderCodecAdmission ParseResponses(
        PreparedProviderExecutionRequest request,
        JsonElement root,
        out PreparedProviderNormalizedCompletion? completion)
    {
        completion = null;
        if (!root.TryGetProperty("output", out var outputItems)
            || outputItems.ValueKind != JsonValueKind.Array)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
        var text = new List<string>();
        foreach (var outputItem in outputItems.EnumerateArray())
        {
            var outputType = TryString(outputItem, "type");
            if (!string.Equals(
                    outputType,
                    "message",
                    StringComparison.Ordinal)
                || !outputItem.TryGetProperty(
                    "content",
                    out var contents)
                || contents.ValueKind != JsonValueKind.Array)
            {
                return PreparedProviderCodecAdmission
                    .ToolBearingResponse;
            }
            foreach (var content in contents.EnumerateArray())
            {
                if (!string.Equals(
                        TryString(content, "type"),
                        "output_text",
                        StringComparison.Ordinal)
                    || !content.TryGetProperty("text", out var value)
                    || value.ValueKind != JsonValueKind.String)
                {
                    return PreparedProviderCodecAdmission
                        .ToolBearingResponse;
                }
                text.Add(value.GetString() ?? string.Empty);
            }
        }
        var combined = string.Concat(text);
        if (string.IsNullOrWhiteSpace(combined))
            return PreparedProviderCodecAdmission.EmptyResponse;
        var status = TryString(root, "status") ?? string.Empty;
        if (!string.Equals(
                status,
                "completed",
                StringComparison.Ordinal))
        {
            return PreparedProviderCodecAdmission
                .TerminalStatusRejected;
        }

        int? input = null;
        int? output = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            input = TryInt32(usage, "input_tokens");
            output = TryInt32(usage, "output_tokens");
        }
        completion = new PreparedProviderNormalizedCompletion(
            request,
            combined,
            status,
            input,
            output);
        return PreparedProviderCodecAdmission.Parsed;
    }

    private static PreparedProviderCodecAdmission ParseChat(
        PreparedProviderExecutionRequest request,
        JsonElement root,
        out PreparedProviderNormalizedCompletion? completion)
    {
        completion = null;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return PreparedProviderCodecAdmission.MalformedResponse;
        }
        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var message)
            || message.TryGetProperty("tool_calls", out _)
            || message.TryGetProperty("function_call", out _)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return PreparedProviderCodecAdmission.ToolBearingResponse;
        }
        var text = content.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return PreparedProviderCodecAdmission.EmptyResponse;
        var status = TryString(choice, "finish_reason") ?? string.Empty;
        if (!string.Equals(
                status,
                "stop",
                StringComparison.Ordinal)
            && !string.Equals(
                status,
                "length",
                StringComparison.Ordinal))
        {
            return PreparedProviderCodecAdmission
                .TerminalStatusRejected;
        }

        int? input = null;
        int? output = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            input = TryInt32(usage, "prompt_tokens");
            output = TryInt32(usage, "completion_tokens");
        }
        completion = new PreparedProviderNormalizedCompletion(
            request,
            text,
            status,
            input,
            output);
        return PreparedProviderCodecAdmission.Parsed;
    }

    private static string? TryString(
        JsonElement element,
        string propertyName)
        => element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryInt32(
        JsonElement element,
        string propertyName)
        => element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
            ? result
            : null;
}
