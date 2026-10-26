using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace LilithProviderControlPlane;

internal enum ProviderCatalogParseStatus
{
    Invalid = 0,
    Parsed = 1,
    Empty = 2,
    Malformed = 3,
    Oversize = 4,
    Truncated = 5,
    Partial = 6,
    ManualSelectionRequired = 7
}

internal enum ProviderGenerationMethodKind
{
    Invalid = 0,
    GenerateContent = 1,
    StreamGenerateContent = 2,
    EmbedContent = 3,
    BatchEmbedContents = 4,
    CountTokens = 5,
    CreateCachedContent = 6
}

internal enum ProviderModelCapabilityKind
{
    Invalid = 0,
    TextInput = 1,
    ImageInput = 2,
    AudioInput = 3,
    VideoInput = 4,
    FileInput = 5,
    TextOutput = 6,
    ImageOutput = 7,
    AudioOutput = 8,
    ToolCalling = 9,
    StructuredOutput = 10,
    Streaming = 11,
    TextGeneration = 12,
    Embeddings = 13
}

internal enum ProviderHealthColor
{
    Invalid = 0,
    Green = 1,
    Orange = 2,
    Red = 3
}

internal enum ProviderTransportFailure
{
    Invalid = 0,
    NetworkFailure = 1,
    Timeout = 2,
    Cancelled = 3,
    ProtocolFailure = 4,
    RedirectRejected = 5
}

internal enum ProviderHealthReason
{
    Invalid = 0,
    CatalogHealthy = 1,
    CatalogDegraded = 2,
    ManualSelectionRequired = 3,
    AuthenticationRejected = 4,
    RateLimited = 5,
    EndpointRejected = 6,
    ServiceUnavailable = 7,
    NetworkFailure = 8,
    Timeout = 9,
    Cancelled = 10,
    ProtocolRejected = 11,
    ObservationUnsupported = 12
}

internal sealed class ProviderModelCapabilityAdvertisement
{
    internal ProviderModelCapabilityKind Kind { get; }
    internal ProviderCapabilityState State { get; }
    internal ProviderCapabilityProvenance Provenance { get; }

    internal ProviderModelCapabilityAdvertisement(
        ProviderModelCapabilityKind kind,
        ProviderCapabilityState state,
        ProviderCapabilityProvenance provenance)
    {
        Kind = Defined(kind, nameof(kind));
        State = Defined(state, nameof(state));
        Provenance = Defined(provenance, nameof(provenance));
        if (State != ProviderCapabilityState.Advertised)
        {
            throw new ArgumentException(
                "Catalog adapters may only retain advertised capability state.",
                nameof(state));
        }
        if (Provenance is not (
            ProviderCapabilityProvenance.OfficialModelCatalog
            or ProviderCapabilityProvenance.OfficialModelMetadataEndpoint))
        {
            throw new ArgumentException(
                "Catalog adapters require official catalog provenance.",
                nameof(provenance));
        }
    }

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal sealed class ProviderCatalogModel
{
    internal string ModelId { get; }
    internal IReadOnlyList<ProviderGenerationMethodKind> GenerationMethods { get; }
    internal IReadOnlyList<ProviderModelCapabilityAdvertisement> Capabilities { get; }

    internal ProviderCatalogModel(
        string modelId,
        IEnumerable<ProviderGenerationMethodKind>? generationMethods = null,
        IEnumerable<ProviderModelCapabilityAdvertisement>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(modelId)
            || modelId.Length > UnifiedProviderCatalogAdapters.MaximumModelIdCharacters)
        {
            throw new ArgumentException(
                "A bounded model identifier is required.",
                nameof(modelId));
        }
        ModelId = modelId.Trim();
        var methods = (generationMethods ?? Array.Empty<ProviderGenerationMethodKind>())
            .Select(DefinedMethod)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        GenerationMethods = new ReadOnlyCollection<ProviderGenerationMethodKind>(methods);
        var advertised = (capabilities
                ?? Array.Empty<ProviderModelCapabilityAdvertisement>())
            .GroupBy(value => value.Kind)
            .Select(group => group.First())
            .OrderBy(value => value.Kind)
            .ToArray();
        Capabilities = new ReadOnlyCollection<ProviderModelCapabilityAdvertisement>(
            advertised);
    }

    private static ProviderGenerationMethodKind DefinedMethod(
        ProviderGenerationMethodKind value)
    {
        if (!Enum.IsDefined(typeof(ProviderGenerationMethodKind), value)
            || value == ProviderGenerationMethodKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        return value;
    }
}

internal sealed class ProviderCatalogParseResult
{
    internal ProviderControlKind Provider { get; }
    internal ProviderCatalogParseStatus Status { get; }
    internal IReadOnlyList<ProviderCatalogModel> Models { get; }
    internal string NextPageToken { get; }
    internal string FirstId { get; }
    internal string LastId { get; }
    internal bool? HasMore { get; }
    internal int RejectedEntries { get; }
    internal int TruncatedEntries { get; }

    internal bool Parsed => Status is ProviderCatalogParseStatus.Parsed
        or ProviderCatalogParseStatus.Partial
        or ProviderCatalogParseStatus.Truncated;

    internal ProviderCatalogParseResult(
        ProviderControlKind provider,
        ProviderCatalogParseStatus status,
        IEnumerable<ProviderCatalogModel>? models = null,
        string? nextPageToken = null,
        string? firstId = null,
        string? lastId = null,
        bool? hasMore = null,
        int rejectedEntries = 0,
        int truncatedEntries = 0)
    {
        Provider = Defined(provider, nameof(provider));
        Status = Defined(status, nameof(status));
        var frozen = (models ?? Array.Empty<ProviderCatalogModel>()).ToArray();
        if (frozen.Any(value => value == null)
            || frozen.Length > UnifiedProviderCatalogAdapters.MaximumModels)
        {
            throw new ArgumentException("The model list is invalid.", nameof(models));
        }
        Models = new ReadOnlyCollection<ProviderCatalogModel>(frozen);
        NextPageToken = BoundedToken(nextPageToken, nameof(nextPageToken));
        FirstId = BoundedToken(firstId, nameof(firstId));
        LastId = BoundedToken(lastId, nameof(lastId));
        HasMore = hasMore;
        if (rejectedEntries < 0 || truncatedEntries < 0)
            throw new ArgumentOutOfRangeException(nameof(rejectedEntries));
        RejectedEntries = rejectedEntries;
        TruncatedEntries = truncatedEntries;
        if (!Parsed && Models.Count != 0)
            throw new ArgumentException("Rejected results cannot retain models.", nameof(models));
        if (Status == ProviderCatalogParseStatus.Partial && RejectedEntries == 0)
            throw new ArgumentException("Partial results require rejected entries.", nameof(rejectedEntries));
        if (Status == ProviderCatalogParseStatus.Truncated && TruncatedEntries == 0)
            throw new ArgumentException("Truncated results require truncated entries.", nameof(truncatedEntries));
        if (Status == ProviderCatalogParseStatus.ManualSelectionRequired
            && Provider != ProviderControlKind.QwenDashScope)
        {
            throw new ArgumentException(
                "Manual catalog selection is currently reserved for Qwen profiles.",
                nameof(status));
        }
    }

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    private static string BoundedToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Length > UnifiedProviderCatalogAdapters.MaximumContinuationCharacters)
            throw new ArgumentException("Continuation metadata is too large.", parameterName);
        return value;
    }
}

internal sealed class ProviderHealthAssessment
{
    internal ProviderConnectionHealth Health { get; }
    internal ProviderHealthColor Color { get; }
    internal ProviderHealthReason Reason { get; }

    internal ProviderHealthAssessment(
        ProviderConnectionHealth health,
        ProviderHealthColor color,
        ProviderHealthReason reason)
    {
        Health = Defined(health, nameof(health));
        Color = Defined(color, nameof(color));
        Reason = Defined(reason, nameof(reason));
    }

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal static class UnifiedProviderCatalogAdapters
{
    internal const int MaximumBodyCharacters = 256 * 1024;
    internal const int MaximumModels = 256;
    internal const int MaximumModelIdCharacters = 256;
    internal const int MaximumContinuationCharacters = 2048;
    internal const int MaximumCapabilitiesPerModel = 32;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32
    };

    internal static ProviderCatalogParseResult ParseGemini(string? body)
        => Parse(
            ProviderControlKind.GoogleGemini,
            body,
            root => ParseGeminiRoot(root));

    internal static ProviderCatalogParseResult ParseOpenAiShaped(
        ProviderControlKind provider,
        string? body)
    {
        if (provider is not (
            ProviderControlKind.OpenAI
            or ProviderControlKind.DeepSeek
            or ProviderControlKind.QwenDashScope
            or ProviderControlKind.LlamaCpp
            or ProviderControlKind.Vllm
            or ProviderControlKind.CustomOpenAICompatible))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        return Parse(provider, body, root => ParseIdArrayRoot(provider, root, "data"));
    }

    internal static ProviderCatalogParseResult ParseAnthropic(string? body)
        => Parse(
            ProviderControlKind.AnthropicClaude,
            body,
            root => ParseAnthropicRoot(root));

    internal static ProviderCatalogParseResult ParseOpenRouter(string? body)
        => Parse(
            ProviderControlKind.OpenRouter,
            body,
            root => ParseOpenRouterRoot(root));

    internal static ProviderCatalogParseResult ParseOllama(
        ProviderControlKind provider,
        string? body)
    {
        if (provider is not (
            ProviderControlKind.OllamaCloud
            or ProviderControlKind.OllamaLocal))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        return Parse(provider, body, root => ParseOllamaRoot(provider, root));
    }

    internal static ProviderCatalogParseResult ParseLmStudio(string? body)
        => Parse(
            ProviderControlKind.LMStudio,
            body,
            root => ParseLmStudioRoot(root));

    internal static ProviderCatalogParseResult ParseLocalAi(string? body)
        => Parse(
            ProviderControlKind.LocalAI,
            body,
            root => ParseLocalAiRoot(root));

    internal static ProviderCatalogParseResult QwenManualSelectionRequired()
        => new(
            ProviderControlKind.QwenDashScope,
            ProviderCatalogParseStatus.ManualSelectionRequired);

    private static ProviderCatalogParseResult Parse(
        ProviderControlKind provider,
        string? body,
        Func<JsonElement, ProviderCatalogParseResult> parser)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Reject(provider, ProviderCatalogParseStatus.Empty);
        if (body.Length > MaximumBodyCharacters)
            return Reject(provider, ProviderCatalogParseStatus.Oversize);
        try
        {
            using var document = JsonDocument.Parse(body, DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Reject(provider, ProviderCatalogParseStatus.Malformed);
            return parser(document.RootElement);
        }
        catch (JsonException)
        {
            return Reject(provider, ProviderCatalogParseStatus.Malformed);
        }
    }

    private static ProviderCatalogParseResult ParseGeminiRoot(JsonElement root)
    {
        if (!TryArray(root, "models", out var entries))
            return Reject(ProviderControlKind.GoogleGemini, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "name", out var modelId))
            {
                accumulator.Reject();
                continue;
            }
            var methods = new List<ProviderGenerationMethodKind>();
            var capabilities = new List<ProviderModelCapabilityAdvertisement>();
            if (entry.TryGetProperty("supportedGenerationMethods", out var methodsElement))
            {
                if (methodsElement.ValueKind != JsonValueKind.Array)
                {
                    accumulator.RejectMetadata();
                }
                else
                {
                    foreach (var method in methodsElement.EnumerateArray())
                    {
                        if (method.ValueKind != JsonValueKind.String)
                        {
                            accumulator.RejectMetadata();
                            continue;
                        }
                        var mapped = MapGeminiMethod(method.GetString());
                        if (mapped == ProviderGenerationMethodKind.Invalid)
                            continue;
                        methods.Add(mapped);
                        if (mapped == ProviderGenerationMethodKind.GenerateContent)
                        {
                            AddCapability(
                                capabilities,
                                ProviderModelCapabilityKind.TextGeneration,
                                ProviderCapabilityProvenance.OfficialModelCatalog);
                        }
                        else if (mapped == ProviderGenerationMethodKind.StreamGenerateContent)
                        {
                            AddCapability(
                                capabilities,
                                ProviderModelCapabilityKind.Streaming,
                                ProviderCapabilityProvenance.OfficialModelCatalog);
                        }
                        else if (mapped is ProviderGenerationMethodKind.EmbedContent
                                 or ProviderGenerationMethodKind.BatchEmbedContents)
                        {
                            AddCapability(
                                capabilities,
                                ProviderModelCapabilityKind.Embeddings,
                                ProviderCapabilityProvenance.OfficialModelCatalog);
                        }
                    }
                }
            }
            if (IsSupportedGeminiNativeImageModel(modelId))
            {
                AddCapability(
                    capabilities,
                    ProviderModelCapabilityKind.TextInput,
                    ProviderCapabilityProvenance.OfficialModelCatalog);
                AddCapability(
                    capabilities,
                    ProviderModelCapabilityKind.ImageInput,
                    ProviderCapabilityProvenance.OfficialModelCatalog);
                AddCapability(
                    capabilities,
                    ProviderModelCapabilityKind.TextOutput,
                    ProviderCapabilityProvenance.OfficialModelCatalog);
                AddCapability(
                    capabilities,
                    ProviderModelCapabilityKind.ImageOutput,
                    ProviderCapabilityProvenance.OfficialModelCatalog);
            }
            accumulator.Add(modelId, methods, capabilities);
        }
        var token = OptionalBoundedString(root, "nextPageToken", out var tokenRejected);
        if (tokenRejected)
            accumulator.RejectMetadata();
        return accumulator.Result(
            ProviderControlKind.GoogleGemini,
            nextPageToken: token);
    }

    private static ProviderCatalogParseResult ParseIdArrayRoot(
        ProviderControlKind provider,
        JsonElement root,
        string propertyName)
    {
        if (!TryArray(root, propertyName, out var entries))
            return Reject(provider, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "id", out var modelId))
            {
                accumulator.Reject();
                continue;
            }
            accumulator.Add(modelId);
        }
        return accumulator.Result(provider);
    }

    private static ProviderCatalogParseResult ParseAnthropicRoot(JsonElement root)
    {
        if (!TryArray(root, "data", out var entries))
            return Reject(ProviderControlKind.AnthropicClaude, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "id", out var modelId))
            {
                accumulator.Reject();
                continue;
            }
            accumulator.Add(modelId);
        }
        var first = OptionalBoundedString(root, "first_id", out var firstRejected);
        var last = OptionalBoundedString(root, "last_id", out var lastRejected);
        var hasMore = OptionalBoolean(root, "has_more", out var hasMoreRejected);
        if (firstRejected || lastRejected || hasMoreRejected)
            accumulator.RejectMetadata();
        return accumulator.Result(
            ProviderControlKind.AnthropicClaude,
            firstId: first,
            lastId: last,
            hasMore: hasMore);
    }

    private static ProviderCatalogParseResult ParseOpenRouterRoot(JsonElement root)
    {
        if (!TryArray(root, "data", out var entries))
            return Reject(ProviderControlKind.OpenRouter, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "id", out var modelId))
            {
                accumulator.Reject();
                continue;
            }
            var capabilities = new List<ProviderModelCapabilityAdvertisement>();
            if (entry.TryGetProperty("architecture", out var architecture))
            {
                if (architecture.ValueKind != JsonValueKind.Object)
                {
                    accumulator.RejectMetadata();
                }
                else
                {
                    ReadModalities(
                        architecture,
                        "input_modalities",
                        true,
                        capabilities,
                        ProviderCapabilityProvenance.OfficialModelCatalog,
                        accumulator);
                    ReadModalities(
                        architecture,
                        "output_modalities",
                        false,
                        capabilities,
                        ProviderCapabilityProvenance.OfficialModelCatalog,
                        accumulator);
                }
            }
            ReadSupportedParameters(
                entry,
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            accumulator.Add(modelId, capabilities: capabilities);
        }
        return accumulator.Result(ProviderControlKind.OpenRouter);
    }

    private static ProviderCatalogParseResult ParseOllamaRoot(
        ProviderControlKind provider,
        JsonElement root)
    {
        if (!TryArray(root, "models", out var entries))
            return Reject(provider, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "name", out var modelId))
            {
                accumulator.Reject();
                continue;
            }
            var capabilities = new List<ProviderModelCapabilityAdvertisement>();
            ReadNamedCapabilities(
                entry,
                "capabilities",
                capabilities,
                ProviderCapabilityProvenance.OfficialModelMetadataEndpoint,
                accumulator);
            accumulator.Add(modelId, capabilities: capabilities);
        }
        return accumulator.Result(provider);
    }

    private static ProviderCatalogParseResult ParseLmStudioRoot(JsonElement root)
    {
        if (!TryArray(root, "models", out var entries))
            return Reject(ProviderControlKind.LMStudio, ProviderCatalogParseStatus.Malformed);
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "key", out var modelId)
                && !TryString(entry, "id", out modelId))
            {
                accumulator.Reject();
                continue;
            }
            var capabilities = new List<ProviderModelCapabilityAdvertisement>();
            ReadCapabilityObject(
                entry,
                "capabilities",
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            accumulator.Add(modelId, capabilities: capabilities);
        }
        return accumulator.Result(ProviderControlKind.LMStudio);
    }

    private static ProviderCatalogParseResult ParseLocalAiRoot(JsonElement root)
    {
        JsonElement entries;
        if (!TryArray(root, "data", out entries)
            && !TryArray(root, "models", out entries))
        {
            return Reject(ProviderControlKind.LocalAI, ProviderCatalogParseStatus.Malformed);
        }
        var accumulator = new CatalogAccumulator();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "id", out var modelId)
                && !TryString(entry, "name", out modelId))
            {
                accumulator.Reject();
                continue;
            }
            var capabilities = new List<ProviderModelCapabilityAdvertisement>();
            ReadNamedCapabilities(
                entry,
                "capabilities",
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            ReadCapabilityObject(
                entry,
                "capabilities",
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            ReadModalities(
                entry,
                "input_modalities",
                true,
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            ReadModalities(
                entry,
                "output_modalities",
                false,
                capabilities,
                ProviderCapabilityProvenance.OfficialModelCatalog,
                accumulator);
            accumulator.Add(modelId, capabilities: capabilities);
        }
        return accumulator.Result(ProviderControlKind.LocalAI);
    }

    private static void ReadModalities(
        JsonElement owner,
        string propertyName,
        bool input,
        ICollection<ProviderModelCapabilityAdvertisement> destination,
        ProviderCapabilityProvenance provenance,
        CatalogAccumulator accumulator)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
            return;
        if (value.ValueKind != JsonValueKind.Array)
        {
            accumulator.RejectMetadata();
            return;
        }
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                accumulator.RejectMetadata();
                continue;
            }
            var kind = MapModality(item.GetString(), input);
            if (kind != ProviderModelCapabilityKind.Invalid)
                AddCapability(destination, kind, provenance);
        }
    }

    private static void ReadSupportedParameters(
        JsonElement owner,
        ICollection<ProviderModelCapabilityAdvertisement> destination,
        ProviderCapabilityProvenance provenance,
        CatalogAccumulator accumulator)
    {
        if (!owner.TryGetProperty("supported_parameters", out var value))
            return;
        if (value.ValueKind != JsonValueKind.Array)
        {
            accumulator.RejectMetadata();
            return;
        }
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                accumulator.RejectMetadata();
                continue;
            }
            var kind = MapNamedCapability(item.GetString());
            if (kind != ProviderModelCapabilityKind.Invalid)
                AddCapability(destination, kind, provenance);
        }
    }

    private static void ReadNamedCapabilities(
        JsonElement owner,
        string propertyName,
        ICollection<ProviderModelCapabilityAdvertisement> destination,
        ProviderCapabilityProvenance provenance,
        CatalogAccumulator accumulator)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Object)
        {
            return;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            accumulator.RejectMetadata();
            return;
        }
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                accumulator.RejectMetadata();
                continue;
            }
            var kind = MapNamedCapability(item.GetString());
            if (kind != ProviderModelCapabilityKind.Invalid)
                AddCapability(destination, kind, provenance);
        }
    }

    private static void ReadCapabilityObject(
        JsonElement owner,
        string propertyName,
        ICollection<ProviderModelCapabilityAdvertisement> destination,
        ProviderCapabilityProvenance provenance,
        CatalogAccumulator accumulator)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Array)
        {
            return;
        }
        if (value.ValueKind != JsonValueKind.Object)
        {
            accumulator.RejectMetadata();
            return;
        }
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
            {
                accumulator.RejectMetadata();
                continue;
            }
            if (!property.Value.GetBoolean())
                continue;
            var kind = MapNamedCapability(property.Name);
            if (kind != ProviderModelCapabilityKind.Invalid)
                AddCapability(destination, kind, provenance);
        }
    }

    private static ProviderGenerationMethodKind MapGeminiMethod(string? value)
        => value switch
        {
            "generateContent" => ProviderGenerationMethodKind.GenerateContent,
            "streamGenerateContent" =>
                ProviderGenerationMethodKind.StreamGenerateContent,
            "embedContent" => ProviderGenerationMethodKind.EmbedContent,
            "batchEmbedContents" =>
                ProviderGenerationMethodKind.BatchEmbedContents,
            "countTokens" => ProviderGenerationMethodKind.CountTokens,
            "createCachedContent" =>
                ProviderGenerationMethodKind.CreateCachedContent,
            _ => ProviderGenerationMethodKind.Invalid
        };

    internal static bool IsSupportedGeminiNativeImageModel(string? modelId)
        => modelId is "models/gemini-2.5-flash-image"
            or "models/gemini-3.1-flash-image"
            or "models/gemini-3.1-flash-lite-image"
            or "models/gemini-3-pro-image";

    private static ProviderModelCapabilityKind MapModality(
        string? value,
        bool input)
        => (value?.ToLowerInvariant(), input) switch
        {
            ("text", true) => ProviderModelCapabilityKind.TextInput,
            ("image", true) => ProviderModelCapabilityKind.ImageInput,
            ("audio", true) => ProviderModelCapabilityKind.AudioInput,
            ("video", true) => ProviderModelCapabilityKind.VideoInput,
            ("file", true) => ProviderModelCapabilityKind.FileInput,
            ("text", false) => ProviderModelCapabilityKind.TextOutput,
            ("image", false) => ProviderModelCapabilityKind.ImageOutput,
            ("audio", false) => ProviderModelCapabilityKind.AudioOutput,
            _ => ProviderModelCapabilityKind.Invalid
        };

    private static ProviderModelCapabilityKind MapNamedCapability(string? value)
        => value?.ToLowerInvariant() switch
        {
            "completion" or "completions" or "chat" or "text_generation" =>
                ProviderModelCapabilityKind.TextGeneration,
            "vision" or "image" or "image_input" =>
                ProviderModelCapabilityKind.ImageInput,
            "tools" or "tool_use" or "tool_choice" or "function_calling"
                or "trained_for_tool_use" =>
                ProviderModelCapabilityKind.ToolCalling,
            "structured_outputs" or "structured_output" or "response_format" =>
                ProviderModelCapabilityKind.StructuredOutput,
            "stream" or "streaming" => ProviderModelCapabilityKind.Streaming,
            "embedding" or "embeddings" => ProviderModelCapabilityKind.Embeddings,
            _ => ProviderModelCapabilityKind.Invalid
        };

    private static void AddCapability(
        ICollection<ProviderModelCapabilityAdvertisement> destination,
        ProviderModelCapabilityKind kind,
        ProviderCapabilityProvenance provenance)
    {
        if (destination.Any(value => value.Kind == kind)
            || destination.Count >= MaximumCapabilitiesPerModel)
        {
            return;
        }
        destination.Add(new ProviderModelCapabilityAdvertisement(
            kind,
            ProviderCapabilityState.Advertised,
            provenance));
    }

    private static bool TryArray(
        JsonElement owner,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        return owner.TryGetProperty(propertyName, out value)
               && value.ValueKind == JsonValueKind.Array;
    }

    private static bool TryString(
        JsonElement owner,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (owner.ValueKind != JsonValueKind.Object
            || !owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Length > MaximumModelIdCharacters)
        {
            return false;
        }
        value = candidate.Trim();
        return true;
    }

    private static string OptionalBoundedString(
        JsonElement owner,
        string propertyName,
        out bool rejected)
    {
        rejected = false;
        if (!owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            rejected = true;
            return string.Empty;
        }
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Length > MaximumContinuationCharacters)
        {
            rejected = true;
            return string.Empty;
        }
        return value;
    }

    private static bool? OptionalBoolean(
        JsonElement owner,
        string propertyName,
        out bool rejected)
    {
        rejected = false;
        if (!owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            rejected = true;
            return null;
        }
        return element.GetBoolean();
    }

    private static ProviderCatalogParseResult Reject(
        ProviderControlKind provider,
        ProviderCatalogParseStatus status)
        => new(provider, status);

    private sealed class CatalogAccumulator
    {
        private readonly List<ProviderCatalogModel> _models = new();
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        private int _rejected;
        private int _truncated;

        internal void Add(
            string modelId,
            IEnumerable<ProviderGenerationMethodKind>? generationMethods = null,
            IEnumerable<ProviderModelCapabilityAdvertisement>? capabilities = null)
        {
            if (!_ids.Add(modelId))
                return;
            if (_models.Count >= MaximumModels)
            {
                _truncated++;
                return;
            }
            _models.Add(new ProviderCatalogModel(
                modelId,
                generationMethods,
                capabilities));
        }

        internal void Reject() => _rejected++;

        internal void RejectMetadata() => _rejected++;

        internal ProviderCatalogParseResult Result(
            ProviderControlKind provider,
            string? nextPageToken = null,
            string? firstId = null,
            string? lastId = null,
            bool? hasMore = null)
        {
            if (_models.Count == 0)
            {
                return _rejected == 0 && _truncated == 0
                    ? UnifiedProviderCatalogAdapters.Reject(
                        provider,
                        ProviderCatalogParseStatus.Empty)
                    : UnifiedProviderCatalogAdapters.Reject(
                        provider,
                        ProviderCatalogParseStatus.Malformed);
            }
            var status = _truncated > 0
                ? ProviderCatalogParseStatus.Truncated
                : _rejected > 0
                    ? ProviderCatalogParseStatus.Partial
                    : ProviderCatalogParseStatus.Parsed;
            return new ProviderCatalogParseResult(
                provider,
                status,
                _models,
                nextPageToken,
                firstId,
                lastId,
                hasMore,
                _rejected,
                _truncated);
        }
    }
}

internal static class ProviderCatalogHealthMapper
{
    internal static ProviderHealthAssessment FromHttp(
        int statusCode,
        ProviderCatalogParseStatus parseStatus)
    {
        if (statusCode is >= 200 and <= 299)
        {
            return parseStatus switch
            {
                ProviderCatalogParseStatus.Parsed => Assessment(
                    ProviderConnectionHealth.CatalogAvailable,
                    ProviderHealthColor.Green,
                    ProviderHealthReason.CatalogHealthy),
                ProviderCatalogParseStatus.Partial
                    or ProviderCatalogParseStatus.Truncated
                    or ProviderCatalogParseStatus.Empty => Assessment(
                        ProviderConnectionHealth.Degraded,
                        ProviderHealthColor.Orange,
                        ProviderHealthReason.CatalogDegraded),
                ProviderCatalogParseStatus.ManualSelectionRequired => Assessment(
                    ProviderConnectionHealth.ReadyToValidate,
                    ProviderHealthColor.Orange,
                    ProviderHealthReason.ManualSelectionRequired),
                _ => Assessment(
                    ProviderConnectionHealth.EndpointRejected,
                    ProviderHealthColor.Red,
                    ProviderHealthReason.ProtocolRejected)
            };
        }
        if (statusCode == 401)
        {
            return Assessment(
                ProviderConnectionHealth.AuthenticationRejected,
                ProviderHealthColor.Red,
                ProviderHealthReason.AuthenticationRejected);
        }
        if (statusCode == 403)
        {
            // A generic 403 can be produced by account policy, WAF, network
            // policy or a provider-specific restriction.  Without an audited
            // provider response contract it is not proof that the credential
            // itself was rejected, so it remains indeterminate/Orange.
            return Assessment(
                ProviderConnectionHealth.Degraded,
                ProviderHealthColor.Orange,
                ProviderHealthReason.ServiceUnavailable);
        }
        if (statusCode == 429)
        {
            return Assessment(
                ProviderConnectionHealth.RateLimited,
                ProviderHealthColor.Orange,
                ProviderHealthReason.RateLimited);
        }
        if (statusCode == 408 || statusCode is >= 500 and <= 599)
        {
            return Assessment(
                ProviderConnectionHealth.Degraded,
                ProviderHealthColor.Orange,
                ProviderHealthReason.ServiceUnavailable);
        }
        return Assessment(
            ProviderConnectionHealth.EndpointRejected,
            ProviderHealthColor.Red,
            ProviderHealthReason.EndpointRejected);
    }

    internal static ProviderHealthAssessment FromFailure(
        ProviderTransportFailure failure)
        => failure switch
        {
            ProviderTransportFailure.NetworkFailure => Assessment(
                ProviderConnectionHealth.NetworkFailure,
                ProviderHealthColor.Orange,
                ProviderHealthReason.NetworkFailure),
            ProviderTransportFailure.Timeout => Assessment(
                ProviderConnectionHealth.Degraded,
                ProviderHealthColor.Orange,
                ProviderHealthReason.Timeout),
            ProviderTransportFailure.Cancelled => Assessment(
                ProviderConnectionHealth.Degraded,
                ProviderHealthColor.Orange,
                ProviderHealthReason.Cancelled),
            ProviderTransportFailure.ProtocolFailure
                or ProviderTransportFailure.RedirectRejected => Assessment(
                    ProviderConnectionHealth.EndpointRejected,
                    ProviderHealthColor.Red,
                    ProviderHealthReason.ProtocolRejected),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

    private static ProviderHealthAssessment Assessment(
        ProviderConnectionHealth health,
        ProviderHealthColor color,
        ProviderHealthReason reason)
        => new(health, color, reason);
}
