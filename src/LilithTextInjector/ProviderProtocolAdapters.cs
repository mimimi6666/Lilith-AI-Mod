using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LilithTextInjector;

internal enum ProviderProtocolCodecAdmission
{
    Encoded = 0,
    Parsed = 1,
    ProviderProtocolMismatch = 2,
    UnsupportedProfile = 3,
    Expired = 4,
    MalformedResponse = 5,
    ToolBearingResponse = 6,
    EmptyResponse = 7,
    TerminalStatusRejected = 8,
    UnsupportedField = 9
}

internal enum ProviderNormalizedFinishReason
{
    Invalid = 0,
    Stop = 1,
    Length = 2
}

internal enum ProviderFixtureRole
{
    Invalid = 0,
    User = 1,
    Assistant = 2
}

internal sealed class ProviderProtocolAuthenticationProfile
{
    internal ProviderAuthenticationScheme Scheme { get; }
    internal IReadOnlyList<string> SecretHeaderNames { get; }
    internal IReadOnlyDictionary<string, string> PublicStaticHeaders { get; }

    internal ProviderProtocolAuthenticationProfile(
        ProviderAuthenticationScheme scheme,
        IEnumerable<string>? secretHeaderNames = null,
        IReadOnlyDictionary<string, string>? publicStaticHeaders = null)
    {
        Scheme = ProviderRegistryContractGuard.Defined(
            scheme,
            nameof(scheme));
        SecretHeaderNames = new ReadOnlyCollection<string>(
            (secretHeaderNames ?? Array.Empty<string>())
            .Select(
                name => DecisionCoreContractGuard.Identifier(
                    name,
                    64,
                    nameof(secretHeaderNames)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
        var staticHeaders = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (publicStaticHeaders != null)
        {
            foreach (var entry in publicStaticHeaders)
                staticHeaders.Add(entry.Key, entry.Value);
        }
        PublicStaticHeaders = new ReadOnlyDictionary<string, string>(
            staticHeaders);
        if (Scheme == ProviderAuthenticationScheme.None
            && SecretHeaderNames.Count != 0)
        {
            throw new ArgumentException(
                "Unauthenticated profiles cannot require secret headers.",
                nameof(secretHeaderNames));
        }
    }

    internal static ProviderProtocolAuthenticationProfile Gemini()
        => new(
            ProviderAuthenticationScheme.GoogleApiKey,
            new[] { "x-goog-api-key" });

    internal static ProviderProtocolAuthenticationProfile Bearer()
        => new(
            ProviderAuthenticationScheme.Bearer,
            new[] { "Authorization" });

    internal static ProviderProtocolAuthenticationProfile Anthropic()
        => new(
            ProviderAuthenticationScheme.AnthropicApiKey,
            new[] { "x-api-key" },
            new Dictionary<string, string>
            {
                ["anthropic-version"] = "2023-06-01"
            });

    internal static ProviderProtocolAuthenticationProfile None()
        => new(ProviderAuthenticationScheme.None);
}

internal sealed class ProviderFixtureTurn
{
    internal ProviderFixtureRole Role { get; }
    internal string Text { get; }
    internal string TextHash { get; }

    internal ProviderFixtureTurn(
        ProviderFixtureRole role,
        string text)
    {
        Role = ProviderRegistryContractGuard.Defined(role, nameof(role));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        TextHash = LivingPromptHash.Of(Text);
    }
}

internal sealed class ProviderProtocolCodecRequest
{
    internal ProviderKind Provider { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal string ProfileVersion { get; }
    internal ProviderEndpointInstanceId EndpointInstanceId { get; }
    internal string ModelTransportName { get; }
    internal string SystemInstruction { get; }
    internal IReadOnlyList<ProviderFixtureTurn> Turns { get; }
    internal int MaximumOutputTokens { get; }
    internal decimal Temperature { get; }
    internal bool ThinkingEnabled { get; }
    internal string KeepAlive { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset Deadline { get; }
    internal PreparedProviderExecutionRequest? CompatibilityRequest { get; }
    internal string RequestFingerprint { get; }
    internal bool AllowsTools => false;
    internal bool AllowsRetry => false;
    internal bool AllowsFallback => false;
    internal bool CreatesConversationState => false;

    private ProviderProtocolCodecRequest(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        string profileVersion,
        ProviderEndpointInstanceId endpointInstanceId,
        string modelTransportName,
        string systemInstruction,
        IReadOnlyList<ProviderFixtureTurn> turns,
        int maximumOutputTokens,
        decimal temperature,
        bool thinkingEnabled,
        string keepAlive,
        DateTimeOffset requestedAt,
        DateTimeOffset deadline,
        PreparedProviderExecutionRequest? compatibilityRequest)
    {
        Provider = ProviderRegistryContractGuard.Defined(
            provider,
            nameof(provider));
        Protocol = ProviderRegistryContractGuard.Defined(
            protocol,
            nameof(protocol));
        ProfileVersion = ProviderRegistryContractGuard.Identifier(
            profileVersion,
            nameof(profileVersion));
        EndpointInstanceId = endpointInstanceId
            ?? throw new ArgumentNullException(nameof(endpointInstanceId));
        ModelTransportName = ProviderRegistryContractGuard.SafeDisplayAlias(
            modelTransportName,
            nameof(modelTransportName));
        SystemInstruction = systemInstruction
            ?? throw new ArgumentNullException(nameof(systemInstruction));
        Turns = new ReadOnlyCollection<ProviderFixtureTurn>(
            (turns ?? throw new ArgumentNullException(nameof(turns)))
            .ToArray());
        if (Turns.Count == 0)
            throw new ArgumentException("At least one turn is required.", nameof(turns));
        if (maximumOutputTokens < 1 || maximumOutputTokens > 4096)
            throw new ArgumentOutOfRangeException(nameof(maximumOutputTokens));
        if (temperature < 0m || temperature > 2m)
            throw new ArgumentOutOfRangeException(nameof(temperature));
        MaximumOutputTokens = maximumOutputTokens;
        Temperature = temperature;
        ThinkingEnabled = thinkingEnabled;
        KeepAlive = DecisionCoreContractGuard.Identifier(
            keepAlive,
            32,
            nameof(keepAlive));
        RequestedAt = LivingContinuityContractGuard.Timestamp(
            requestedAt,
            nameof(requestedAt));
        Deadline = LivingContinuityContractGuard.Timestamp(
            deadline,
            nameof(deadline));
        if (Deadline <= RequestedAt)
            throw new ArgumentException("Deadline must follow request time.", nameof(deadline));
        CompatibilityRequest = compatibilityRequest;
        RequestFingerprint = LivingPromptHash.Of(
            string.Join(
                "|",
                ((int)Provider).ToString(CultureInfo.InvariantCulture),
                ((int)Protocol).ToString(CultureInfo.InvariantCulture),
                ProfileVersion,
                EndpointInstanceId.Value,
                LivingPromptHash.Of(ModelTransportName),
                LivingPromptHash.Of(SystemInstruction),
                string.Join(",", Turns.Select(turn => turn.TextHash)),
                MaximumOutputTokens.ToString(CultureInfo.InvariantCulture),
                Temperature.ToString(CultureInfo.InvariantCulture),
                ThinkingEnabled ? "1" : "0",
                KeepAlive,
                RequestedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                Deadline.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture)));
    }

    internal static ProviderProtocolCodecRequest FromPrepared(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        string profileVersion,
        ProviderEndpointInstanceId endpointInstanceId,
        string modelTransportName,
        PreparedProviderExecutionRequest prepared)
    {
        if (prepared == null)
            throw new ArgumentNullException(nameof(prepared));
        return new ProviderProtocolCodecRequest(
            provider,
            protocol,
            profileVersion,
            endpointInstanceId,
            modelTransportName,
            prepared.SystemInstruction,
            new ReadOnlyCollection<ProviderFixtureTurn>(
                prepared.Turns
                    .Select(
                        turn => new ProviderFixtureTurn(
                            turn.Role == PreparedProviderRole.Assistant
                                ? ProviderFixtureRole.Assistant
                                : ProviderFixtureRole.User,
                            turn.Text))
                    .ToArray()),
            1024,
            0.8m,
            false,
            "0",
            prepared.RequestedAt,
            prepared.Deadline,
            prepared);
    }

    internal static ProviderProtocolCodecRequest Fixture(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        string profileVersion,
        string modelTransportName,
        string systemInstruction,
        IEnumerable<ProviderFixtureTurn> turns,
        DateTimeOffset requestedAt,
        DateTimeOffset deadline,
        int maximumOutputTokens = 1024,
        decimal temperature = 0.8m,
        bool thinkingEnabled = false,
        string keepAlive = "0")
        => new(
            provider,
            protocol,
            profileVersion,
            new ProviderEndpointInstanceId(
                "fixture-" + provider.ToString().ToLowerInvariant()),
            modelTransportName,
            systemInstruction,
            new ReadOnlyCollection<ProviderFixtureTurn>(turns.ToArray()),
            maximumOutputTokens,
            temperature,
            thinkingEnabled,
            keepAlive,
            requestedAt,
            deadline,
            null);
}

internal sealed class ProviderProtocolWireEnvelope
{
    internal string RelativePath { get; }
    internal string Json { get; }
    internal string PayloadHash { get; }
    internal ProviderProtocolAuthenticationProfile Authentication { get; }
    internal bool Streaming { get; }
    internal bool FixtureOnly { get; }

    internal ProviderProtocolWireEnvelope(
        string relativePath,
        string json,
        ProviderProtocolAuthenticationProfile authentication,
        bool streaming,
        bool fixtureOnly)
    {
        RelativePath = DecisionCoreContractGuard.Identifier(
            relativePath,
            192,
            nameof(relativePath));
        Json = json ?? throw new ArgumentNullException(nameof(json));
        Authentication = authentication
            ?? throw new ArgumentNullException(nameof(authentication));
        Streaming = streaming;
        FixtureOnly = fixtureOnly;
        PayloadHash = LivingPromptHash.Of(Json);
    }
}

internal sealed class ProviderProtocolNormalizedCompletion
{
    internal string RequestFingerprint { get; }
    internal string Text { get; }
    internal string TextHash { get; }
    internal ProviderNormalizedFinishReason FinishReason { get; }
    internal int? InputTokens { get; }
    internal int? OutputTokens { get; }
    internal bool ReasoningObserved { get; }

    internal ProviderProtocolNormalizedCompletion(
        ProviderProtocolCodecRequest request,
        string text,
        ProviderNormalizedFinishReason finishReason,
        int? inputTokens,
        int? outputTokens,
        bool reasoningObserved)
    {
        RequestFingerprint = request?.RequestFingerprint
            ?? throw new ArgumentNullException(nameof(request));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        if (string.IsNullOrWhiteSpace(Text))
            throw new ArgumentException("Completion text cannot be empty.", nameof(text));
        FinishReason = ProviderRegistryContractGuard.Defined(
            finishReason,
            nameof(finishReason));
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        ReasoningObserved = reasoningObserved;
        TextHash = LivingPromptHash.Of(Text);
    }
}

internal interface IProviderProtocolCodec
{
    ProviderKind Provider { get; }
    ProviderProtocolKind Protocol { get; }
    string ProfileVersion { get; }

    ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope);

    ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion);
}

internal static class ProviderProtocolCodecResolver
{
    internal static bool TryResolve(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        string profileVersion,
        out IProviderProtocolCodec? codec)
    {
        codec = (provider, protocol, profileVersion) switch
        {
            (ProviderKind.Gemini,
                ProviderProtocolKind.GeminiGenerateContent,
                "d026-v1") =>
                new PreparedCompatibilityProtocolCodec(
                    provider,
                    protocol,
                    profileVersion),
            (ProviderKind.QwenDashScope,
                ProviderProtocolKind.OpenAiResponses,
                "d026-v1") =>
                new PreparedCompatibilityProtocolCodec(
                    provider,
                    protocol,
                    profileVersion),
            (ProviderKind.QwenDashScope,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-qwen-compatible-chat-v1") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer(),
                    "/compatible-mode/v1/chat/completions"),
            (ProviderKind.OpenAI,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d026-v1") =>
                new PreparedCompatibilityProtocolCodec(
                    provider,
                    protocol,
                    profileVersion),
            (ProviderKind.DeepSeek,
                ProviderProtocolKind.OpenAiChatCompletions,
                "deepseek-v4-2026-04-24") =>
                new DeepSeekV4ProtocolCodec(),
            (ProviderKind.AnthropicClaude,
                ProviderProtocolKind.AnthropicMessages,
                "anthropic-messages-2023-06-01") =>
                new AnthropicMessagesProtocolCodec(),
            (ProviderKind.Ollama,
                ProviderProtocolKind.OllamaNativeChat,
                "ollama-chat-v1") =>
                new OllamaNativeChatProtocolCodec(),
            (ProviderKind.OpenRouter,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-openrouter-chat-v1") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer(),
                    "/api/v1/chat/completions"),
            (ProviderKind.OllamaCloud,
                ProviderProtocolKind.OllamaNativeChat,
                "d033-ollama-cloud-chat-v1") =>
                new OllamaNativeChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.OllamaLocal,
                ProviderProtocolKind.OllamaNativeChat,
                "d033-ollama-local-chat-v1") =>
                new OllamaNativeChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.CustomOpenAICompatible,
                ProviderProtocolKind.OpenAiChatCompletions,
                "custom-openai-chat-fixture-v1") =>
                new GenericOpenAiChatFixtureCodec(),
            (ProviderKind.CustomOpenAICompatible,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-custom-openai-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.CustomOpenAICompatible,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-custom-openai-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.CustomOpenAICompatible,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.CustomOpenAICompatible,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LlamaCpp,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-llama-cpp-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LlamaCpp,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-llama-cpp-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.LlamaCpp,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LMStudio,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-lm-studio-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LMStudio,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-lm-studio-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.LMStudio,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.Vllm,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-vllm-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.Vllm,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-vllm-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.Vllm,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LocalAI,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-local-ai-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            (ProviderKind.LocalAI,
                ProviderProtocolKind.OpenAiChatCompletions,
                "d033-local-ai-chat-v1-bearer") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.Bearer()),
            (ProviderKind.LocalAI,
                ProviderProtocolKind.OpenAiChatCompletions,
                "openai-compatible-chat-v1-none") =>
                new OpenAiCompatibleChatProtocolCodec(
                    provider,
                    profileVersion,
                    ProviderProtocolAuthenticationProfile.None()),
            _ => null
        };
        return codec != null;
    }
}

internal abstract class ProviderProtocolCodecBase : IProviderProtocolCodec
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public abstract ProviderKind Provider { get; }
    public abstract ProviderProtocolKind Protocol { get; }
    public abstract string ProfileVersion { get; }

    public abstract ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope);

    public abstract ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion);

    protected ProviderProtocolCodecAdmission Validate(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now)
    {
        if (request.Provider != Provider || request.Protocol != Protocol)
            return ProviderProtocolCodecAdmission.ProviderProtocolMismatch;
        if (!string.Equals(
                request.ProfileVersion,
                ProfileVersion,
                StringComparison.Ordinal))
        {
            return ProviderProtocolCodecAdmission.UnsupportedProfile;
        }
        if (now < request.RequestedAt || now >= request.Deadline)
            return ProviderProtocolCodecAdmission.Expired;
        return ProviderProtocolCodecAdmission.Encoded;
    }

    protected static ProviderNormalizedFinishReason? Finish(
        string? value,
        params string[] stopValues)
    {
        if (stopValues.Contains(value, StringComparer.Ordinal))
            return ProviderNormalizedFinishReason.Stop;
        if (string.Equals(value, "length", StringComparison.Ordinal)
            || string.Equals(value, "max_tokens", StringComparison.Ordinal)
            || string.Equals(value, "MAX_TOKENS", StringComparison.Ordinal))
        {
            return ProviderNormalizedFinishReason.Length;
        }
        return null;
    }

    protected static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    protected static int? Int32(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var result)
            ? result
            : null;

    protected static IReadOnlyList<object> Messages(
        ProviderProtocolCodecRequest request)
    {
        var messages = new List<object>
        {
            new { role = "system", content = request.SystemInstruction }
        };
        messages.AddRange(
            request.Turns.Select(
                turn => (object)new
                {
                    role = turn.Role == ProviderFixtureRole.Assistant
                        ? "assistant"
                        : "user",
                    content = turn.Text
                }));
        return messages;
    }

    protected static ProviderProtocolCodecAdmission ParseJson(
        string responseJson,
        Func<JsonElement, ProviderProtocolCodecAdmission> parse)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            return parse(document.RootElement);
        }
        catch (JsonException)
        {
            return ProviderProtocolCodecAdmission.MalformedResponse;
        }
        catch (InvalidOperationException)
        {
            return ProviderProtocolCodecAdmission.MalformedResponse;
        }
    }
}

internal sealed class PreparedCompatibilityProtocolCodec
    : ProviderProtocolCodecBase
{
    private readonly ProviderKind _provider;
    private readonly ProviderProtocolKind _protocol;
    private readonly string _profileVersion;

    internal PreparedCompatibilityProtocolCodec(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        string profileVersion)
    {
        _provider = provider;
        _protocol = protocol;
        _profileVersion = profileVersion;
    }

    public override ProviderKind Provider => _provider;
    public override ProviderProtocolKind Protocol => _protocol;
    public override string ProfileVersion => _profileVersion;

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        if (request.CompatibilityRequest == null)
            return ProviderProtocolCodecAdmission.UnsupportedField;

        var portable = PreparedProviderTextCodec.Encode(
            request.CompatibilityRequest);
        var json = Protocol == ProviderProtocolKind.GeminiGenerateContent
            ? portable.Json
            : BindModel(portable.Json, request.ModelTransportName);
        var path = Protocol switch
        {
            ProviderProtocolKind.GeminiGenerateContent =>
                "/v1beta/models/"
                + Uri.EscapeDataString(request.ModelTransportName)
                + ":generateContent",
            ProviderProtocolKind.OpenAiResponses => "/v1/responses",
            ProviderProtocolKind.OpenAiChatCompletions =>
                "/v1/chat/completions",
            _ => throw new InvalidOperationException()
        };
        var auth = Provider == ProviderKind.Gemini
            ? ProviderProtocolAuthenticationProfile.Gemini()
            : ProviderProtocolAuthenticationProfile.Bearer();
        envelope = new ProviderProtocolWireEnvelope(
            path,
            json,
            auth,
            streaming: false,
            fixtureOnly: false);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        if (request.Provider != Provider
            || request.Protocol != Protocol
            || request.CompatibilityRequest == null)
        {
            return ProviderProtocolCodecAdmission.ProviderProtocolMismatch;
        }
        var admission = PreparedProviderTextCodec.TryParse(
            request.CompatibilityRequest,
            responseJson,
            out var parsed);
        if (admission != PreparedProviderCodecAdmission.Parsed
            || parsed == null)
        {
            return admission switch
            {
                PreparedProviderCodecAdmission.ToolBearingResponse =>
                    ProviderProtocolCodecAdmission.ToolBearingResponse,
                PreparedProviderCodecAdmission.EmptyResponse =>
                    ProviderProtocolCodecAdmission.EmptyResponse,
                PreparedProviderCodecAdmission.TerminalStatusRejected =>
                    ProviderProtocolCodecAdmission.TerminalStatusRejected,
                _ => ProviderProtocolCodecAdmission.MalformedResponse
            };
        }
        var finish = Finish(
            parsed.TerminalStatus,
            "STOP",
            "completed",
            "stop");
        if (!finish.HasValue)
            return ProviderProtocolCodecAdmission.TerminalStatusRejected;
        completion = new ProviderProtocolNormalizedCompletion(
            request,
            parsed.Text,
            finish.Value,
            parsed.InputTokens,
            parsed.OutputTokens,
            reasoningObserved: false);
        return ProviderProtocolCodecAdmission.Parsed;
    }

    private static string BindModel(string json, string model)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            foreach (var property in document.RootElement.EnumerateObject())
                property.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

internal sealed class DeepSeekV4ProtocolCodec
    : ProviderProtocolCodecBase
{
    public override ProviderKind Provider => ProviderKind.DeepSeek;
    public override ProviderProtocolKind Protocol =>
        ProviderProtocolKind.OpenAiChatCompletions;
    public override string ProfileVersion => "deepseek-v4-2026-04-24";

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        if (request.ModelTransportName != "deepseek-v4-pro"
            && request.ModelTransportName != "deepseek-v4-flash")
        {
            return ProviderProtocolCodecAdmission.UnsupportedField;
        }
        var json = JsonSerializer.Serialize(
            new
            {
                model = request.ModelTransportName,
                messages = Messages(request),
                thinking = new
                {
                    type = request.ThinkingEnabled ? "enabled" : "disabled"
                },
                max_tokens = request.MaximumOutputTokens,
                temperature = request.Temperature,
                stream = false
            },
            JsonOptions);
        envelope = new ProviderProtocolWireEnvelope(
            "/chat/completions",
            json,
            ProviderProtocolAuthenticationProfile.Bearer(),
            streaming: false,
            fixtureOnly: false);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        ProviderProtocolNormalizedCompletion? parsedCompletion = null;
        var admission = ParseJson(
            responseJson,
            root =>
            {
                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() != 1)
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                var choice = choices[0];
                if (!choice.TryGetProperty("message", out var message)
                    || message.TryGetProperty("tool_calls", out _)
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                {
                    return ProviderProtocolCodecAdmission.ToolBearingResponse;
                }
                var text = content.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return ProviderProtocolCodecAdmission.EmptyResponse;
                var finish = Finish(String(choice, "finish_reason"), "stop");
                if (!finish.HasValue)
                    return ProviderProtocolCodecAdmission.TerminalStatusRejected;
                int? input = null;
                int? output = null;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = Int32(usage, "prompt_tokens");
                    output = Int32(usage, "completion_tokens");
                }
                var reasoningObserved =
                    message.TryGetProperty("reasoning_content", out var reasoning)
                    && reasoning.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(reasoning.GetString());
                parsedCompletion = new ProviderProtocolNormalizedCompletion(
                    request,
                    text,
                    finish.Value,
                    input,
                    output,
                    reasoningObserved);
                return ProviderProtocolCodecAdmission.Parsed;
            });
        completion = parsedCompletion;
        return admission;
    }
}

internal sealed class AnthropicMessagesProtocolCodec
    : ProviderProtocolCodecBase
{
    public override ProviderKind Provider => ProviderKind.AnthropicClaude;
    public override ProviderProtocolKind Protocol =>
        ProviderProtocolKind.AnthropicMessages;
    public override string ProfileVersion => "anthropic-messages-2023-06-01";

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        var json = JsonSerializer.Serialize(
            new
            {
                model = request.ModelTransportName,
                max_tokens = request.MaximumOutputTokens,
                system = request.SystemInstruction,
                messages = request.Turns.Select(
                    turn => new
                    {
                        role = turn.Role == ProviderFixtureRole.Assistant
                            ? "assistant"
                            : "user",
                        content = turn.Text
                    }),
                stream = false
            },
            JsonOptions);
        envelope = new ProviderProtocolWireEnvelope(
            "/v1/messages",
            json,
            ProviderProtocolAuthenticationProfile.Anthropic(),
            streaming: false,
            fixtureOnly: false);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        ProviderProtocolNormalizedCompletion? parsedCompletion = null;
        var admission = ParseJson(
            responseJson,
            root =>
            {
                if (!root.TryGetProperty("content", out var items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                var text = new List<string>();
                foreach (var item in items.EnumerateArray())
                {
                    if (!string.Equals(
                            String(item, "type"),
                            "text",
                            StringComparison.Ordinal)
                        || !item.TryGetProperty("text", out var part)
                        || part.ValueKind != JsonValueKind.String)
                    {
                        return ProviderProtocolCodecAdmission.ToolBearingResponse;
                    }
                    text.Add(part.GetString() ?? string.Empty);
                }
                var combined = string.Concat(text);
                if (string.IsNullOrWhiteSpace(combined))
                    return ProviderProtocolCodecAdmission.EmptyResponse;
                var finish = Finish(
                    String(root, "stop_reason"),
                    "end_turn",
                    "stop_sequence");
                if (!finish.HasValue)
                    return ProviderProtocolCodecAdmission.TerminalStatusRejected;
                int? input = null;
                int? output = null;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = Int32(usage, "input_tokens");
                    output = Int32(usage, "output_tokens");
                }
                parsedCompletion = new ProviderProtocolNormalizedCompletion(
                    request,
                    combined,
                    finish.Value,
                    input,
                    output,
                    reasoningObserved: false);
                return ProviderProtocolCodecAdmission.Parsed;
            });
        completion = parsedCompletion;
        return admission;
    }
}

internal sealed class OllamaNativeChatProtocolCodec
    : ProviderProtocolCodecBase
{
    private readonly ProviderKind _provider;
    private readonly string _profileVersion;
    private readonly ProviderProtocolAuthenticationProfile _authentication;

    internal OllamaNativeChatProtocolCodec()
        : this(
            ProviderKind.Ollama,
            "ollama-chat-v1",
            ProviderProtocolAuthenticationProfile.None())
    {
    }

    internal OllamaNativeChatProtocolCodec(
        ProviderKind provider,
        string profileVersion,
        ProviderProtocolAuthenticationProfile authentication)
    {
        if (provider is not (
                ProviderKind.Ollama
                or ProviderKind.OllamaCloud
                or ProviderKind.OllamaLocal))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        _provider = provider;
        _profileVersion = ProviderRegistryContractGuard.Identifier(
            profileVersion,
            nameof(profileVersion));
        _authentication = authentication
            ?? throw new ArgumentNullException(nameof(authentication));
        if (provider == ProviderKind.OllamaCloud
            && _authentication.Scheme != ProviderAuthenticationScheme.Bearer)
        {
            throw new ArgumentException(
                "Ollama Cloud requires its authenticated cloud profile.",
                nameof(authentication));
        }
        if (provider is ProviderKind.Ollama or ProviderKind.OllamaLocal
            && _authentication.Scheme != ProviderAuthenticationScheme.None)
        {
            throw new ArgumentException(
                "The accepted Ollama Local profile is unauthenticated.",
                nameof(authentication));
        }
    }

    public override ProviderKind Provider => _provider;
    public override ProviderProtocolKind Protocol =>
        ProviderProtocolKind.OllamaNativeChat;
    public override string ProfileVersion => _profileVersion;

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        var json = JsonSerializer.Serialize(
            new
            {
                model = request.ModelTransportName,
                messages = Messages(request),
                stream = false,
                keep_alive = request.KeepAlive,
                options = new
                {
                    temperature = request.Temperature,
                    num_predict = request.MaximumOutputTokens
                }
            },
            JsonOptions);
        envelope = new ProviderProtocolWireEnvelope(
            "/api/chat",
            json,
            _authentication,
            streaming: false,
            fixtureOnly: false);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        ProviderProtocolNormalizedCompletion? parsedCompletion = null;
        var admission = ParseJson(
            responseJson,
            root =>
            {
                if (!root.TryGetProperty("done", out var done)
                    || done.ValueKind != JsonValueKind.True
                    || !root.TryGetProperty("message", out var message))
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                if (message.TryGetProperty("tool_calls", out _))
                    return ProviderProtocolCodecAdmission.ToolBearingResponse;
                if (!message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                var text = content.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return ProviderProtocolCodecAdmission.EmptyResponse;
                var finish = Finish(String(root, "done_reason"), "stop");
                if (!finish.HasValue)
                    return ProviderProtocolCodecAdmission.TerminalStatusRejected;
                parsedCompletion = new ProviderProtocolNormalizedCompletion(
                    request,
                    text,
                    finish.Value,
                    Int32(root, "prompt_eval_count"),
                    Int32(root, "eval_count"),
                    reasoningObserved:
                        root.TryGetProperty("thinking", out var thinking)
                        && thinking.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(thinking.GetString()));
                return ProviderProtocolCodecAdmission.Parsed;
            });
        completion = parsedCompletion;
        return admission;
    }
}

internal sealed class OpenAiCompatibleChatProtocolCodec
    : ProviderProtocolCodecBase
{
    private readonly ProviderKind _provider;
    private readonly string _profileVersion;
    private readonly ProviderProtocolAuthenticationProfile _authentication;
    private readonly string _relativePath;

    internal OpenAiCompatibleChatProtocolCodec(
        ProviderKind provider,
        string profileVersion,
        ProviderProtocolAuthenticationProfile authentication,
        string relativePath = "/v1/chat/completions")
    {
        if (provider is not (
                ProviderKind.CustomOpenAICompatible
                or ProviderKind.OpenRouter
                or ProviderKind.QwenDashScope
                or ProviderKind.LlamaCpp
                or ProviderKind.LMStudio
                or ProviderKind.Vllm
                or ProviderKind.LocalAI))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        _provider = provider;
        _profileVersion = ProviderRegistryContractGuard.Identifier(
            profileVersion,
            nameof(profileVersion));
        _authentication = authentication
            ?? throw new ArgumentNullException(nameof(authentication));
        _relativePath = DecisionCoreContractGuard.Identifier(
            relativePath,
            192,
            nameof(relativePath));
    }

    public override ProviderKind Provider => _provider;
    public override ProviderProtocolKind Protocol =>
        ProviderProtocolKind.OpenAiChatCompletions;
    public override string ProfileVersion => _profileVersion;

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        envelope = new ProviderProtocolWireEnvelope(
            _relativePath,
            JsonSerializer.Serialize(
                new
                {
                    model = request.ModelTransportName,
                    messages = Messages(request),
                    max_tokens = request.MaximumOutputTokens,
                    temperature = request.Temperature,
                    stream = false
                },
                JsonOptions),
            _authentication,
            streaming: false,
            fixtureOnly: false);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        ProviderProtocolNormalizedCompletion? parsedCompletion = null;
        var admission = ParseJson(
            responseJson,
            root =>
            {
                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() != 1)
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                var choice = choices[0];
                if (!choice.TryGetProperty("message", out var message)
                    || message.TryGetProperty("tool_calls", out _)
                    || message.TryGetProperty("function_call", out _)
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                {
                    return ProviderProtocolCodecAdmission.ToolBearingResponse;
                }
                var text = content.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return ProviderProtocolCodecAdmission.EmptyResponse;
                var finish = Finish(String(choice, "finish_reason"), "stop");
                if (!finish.HasValue)
                {
                    return ProviderProtocolCodecAdmission
                        .TerminalStatusRejected;
                }
                int? input = null;
                int? output = null;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = Int32(usage, "prompt_tokens");
                    output = Int32(usage, "completion_tokens");
                }
                parsedCompletion = new ProviderProtocolNormalizedCompletion(
                    request,
                    text,
                    finish.Value,
                    input,
                    output,
                    reasoningObserved: false);
                return ProviderProtocolCodecAdmission.Parsed;
            });
        completion = parsedCompletion;
        return admission;
    }
}

internal sealed class GenericOpenAiChatFixtureCodec
    : ProviderProtocolCodecBase
{
    public override ProviderKind Provider =>
        ProviderKind.CustomOpenAICompatible;
    public override ProviderProtocolKind Protocol =>
        ProviderProtocolKind.OpenAiChatCompletions;
    public override string ProfileVersion =>
        "custom-openai-chat-fixture-v1";

    public override ProviderProtocolCodecAdmission TryEncode(
        ProviderProtocolCodecRequest request,
        DateTimeOffset now,
        out ProviderProtocolWireEnvelope? envelope)
    {
        envelope = null;
        var admission = Validate(request, now);
        if (admission != ProviderProtocolCodecAdmission.Encoded)
            return admission;
        envelope = new ProviderProtocolWireEnvelope(
            "/v1/chat/completions",
            JsonSerializer.Serialize(
                new
                {
                    model = request.ModelTransportName,
                    messages = Messages(request),
                    max_tokens = request.MaximumOutputTokens,
                    temperature = request.Temperature,
                    stream = false
                },
                JsonOptions),
            ProviderProtocolAuthenticationProfile.Bearer(),
            streaming: false,
            fixtureOnly: true);
        return ProviderProtocolCodecAdmission.Encoded;
    }

    public override ProviderProtocolCodecAdmission TryParse(
        ProviderProtocolCodecRequest request,
        string responseJson,
        out ProviderProtocolNormalizedCompletion? completion)
    {
        completion = null;
        ProviderProtocolNormalizedCompletion? parsedCompletion = null;
        var admission = ParseJson(
            responseJson,
            root =>
            {
                if (!root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() != 1)
                {
                    return ProviderProtocolCodecAdmission.MalformedResponse;
                }
                var choice = choices[0];
                if (!choice.TryGetProperty("message", out var message)
                    || message.TryGetProperty("tool_calls", out _)
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.String)
                {
                    return ProviderProtocolCodecAdmission.ToolBearingResponse;
                }
                var text = content.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return ProviderProtocolCodecAdmission.EmptyResponse;
                var finish = Finish(String(choice, "finish_reason"), "stop");
                if (!finish.HasValue)
                    return ProviderProtocolCodecAdmission.TerminalStatusRejected;
                int? input = null;
                int? output = null;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = Int32(usage, "prompt_tokens");
                    output = Int32(usage, "completion_tokens");
                }
                parsedCompletion = new ProviderProtocolNormalizedCompletion(
                    request,
                    text,
                    finish.Value,
                    input,
                    output,
                    reasoningObserved: false);
                return ProviderProtocolCodecAdmission.Parsed;
            });
        completion = parsedCompletion;
        return admission;
    }
}
