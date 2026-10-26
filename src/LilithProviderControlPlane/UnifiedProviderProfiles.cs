using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithProviderControlPlane;

internal enum ProviderControlKind
{
    Invalid = 0,
    DeepSeek = 1,
    GoogleGemini = 2,
    OpenAI = 3,
    QwenDashScope = 4,
    AnthropicClaude = 5,
    OpenRouter = 6,
    OllamaCloud = 7,
    OllamaLocal = 8,
    LMStudio = 9,
    LlamaCpp = 10,
    Vllm = 11,
    LocalAI = 12,
    CustomOpenAICompatible = 13
}

internal enum ProviderControlProtocol
{
    Invalid = 0,
    OpenAiResponses = 1,
    OpenAiChatCompletions = 2,
    GeminiGenerateContent = 3,
    AnthropicMessages = 4,
    OllamaNativeChat = 5,
    OpenAiCompatibleChat = 7
}

internal enum ProviderControlAuth
{
    Invalid = 0,
    Bearer = 1,
    GoogleApiKeyHeader = 2,
    AnthropicApiKeyHeader = 3,
    NoneByDefault = 4,
    OptionalBearer = 5,
    LocalAiOptional = 6,
    UserDefined = 7
}

internal enum ProviderCredentialRequirement
{
    Invalid = 0,
    Required = 1,
    Optional = 2,
    NotRequired = 3
}

internal enum ProviderCatalogKind
{
    Invalid = 0,
    OpenAiModels = 1,
    GeminiModelsPaginated = 2,
    AnthropicModelsCursorPaginated = 3,
    OpenRouterModels = 4,
    QwenOptionalOpenAiModels = 5,
    OllamaTagsAndShow = 6,
    LmStudioNativeModels = 7,
    LocalAiCapabilities = 8,
    OptionalOpenAiModels = 9
}

internal enum ProviderCapabilityProvenance
{
    Invalid = 0,
    None = 1,
    OfficialModelCatalog = 2,
    OfficialModelMetadataEndpoint = 3,
    OfficialStaticProfile = 4,
    ProbeVerified = 5,
    UserDeclared = 6
}

internal enum ProviderCapabilityState
{
    Invalid = 0,
    Unknown = 1,
    Advertised = 2,
    ProbeVerified = 3,
    UserDeclared = 4,
    Unsupported = 5
}

internal enum ProviderConnectionHealth
{
    Invalid = 0,
    Unknown = 1,
    NotConfigured = 2,
    ReadyToValidate = 3,
    CatalogAvailable = 4,
    GenerationVerified = 5,
    AuthenticationRejected = 6,
    EndpointRejected = 7,
    RateLimited = 8,
    NetworkFailure = 9,
    Degraded = 10,
    Disabled = 11
}

internal sealed class ProviderEndpointProfile
{
    internal const string WorkspacePlaceholder = "{workspaceId}";

    internal string ProfileId { get; }
    internal string Region { get; }
    internal string BaseAddressTemplate { get; }
    internal bool RequiresWorkspaceId { get; }
    internal bool IsSharedEndpoint { get; }

    internal ProviderEndpointProfile(
        string profileId,
        string region,
        string baseAddressTemplate,
        bool requiresWorkspaceId,
        bool isSharedEndpoint)
    {
        ProfileId = Identifier(profileId, nameof(profileId));
        Region = Identifier(region, nameof(region));
        BaseAddressTemplate = AbsoluteAddressTemplate(
            baseAddressTemplate,
            requiresWorkspaceId,
            nameof(baseAddressTemplate));
        RequiresWorkspaceId = requiresWorkspaceId;
        IsSharedEndpoint = isSharedEndpoint;
        if (requiresWorkspaceId == isSharedEndpoint)
        {
            throw new ArgumentException(
                "A workspace endpoint cannot also be a shared endpoint.",
                nameof(isSharedEndpoint));
        }
    }

    private static string Identifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || value.Any(character =>
                !(char.IsLetterOrDigit(character)
                  || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "A bounded identifier is required.",
                parameterName);
        }
        return value;
    }

    private static string AbsoluteAddressTemplate(
        string? value,
        bool requiresWorkspaceId,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            throw new ArgumentException("A bounded endpoint is required.", parameterName);
        var placeholderCount = value.Split(
            new[] { WorkspacePlaceholder },
            StringSplitOptions.None).Length - 1;
        if (requiresWorkspaceId != (placeholderCount == 1))
        {
            throw new ArgumentException(
                "Workspace endpoint templates require exactly one workspace placeholder.",
                parameterName);
        }
        var probe = value.Replace(
            WorkspacePlaceholder,
            "workspace",
            StringComparison.Ordinal);
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("An absolute HTTP endpoint is required.", parameterName);
        }
        return value;
    }
}

internal sealed class ProviderControlProfile
{
    internal const int CurrentSchemaVersion = 1;

    internal int SchemaVersion { get; }
    internal ProviderControlKind Kind { get; }
    internal string ProviderId { get; }
    internal string DisplayName { get; }
    internal ProviderControlProtocol Protocol { get; }
    internal ProviderControlAuth Auth { get; }
    internal ProviderCredentialRequirement CredentialRequirement { get; }
    internal ProviderCatalogKind CatalogKind { get; }
    internal ProviderCapabilityState CatalogCapabilityState { get; }
    internal ProviderCapabilityProvenance CatalogCapabilityProvenance { get; }
    internal Uri? DefaultBaseUri { get; }
    internal string CatalogPath { get; }
    internal string GenerationPath { get; }
    internal bool IsLocalDefault { get; }
    internal bool PlayerMayEditEndpoint { get; }
    internal bool RequiresExplicitEndpointProfile { get; }
    internal IReadOnlyList<ProviderEndpointProfile> EndpointProfiles { get; }
    internal IReadOnlyList<string> HardRejectedModelIds { get; }

    internal ProviderControlProfile(
        ProviderControlKind kind,
        string providerId,
        string displayName,
        ProviderControlProtocol protocol,
        ProviderControlAuth auth,
        ProviderCredentialRequirement credentialRequirement,
        ProviderCatalogKind catalogKind,
        ProviderCapabilityState catalogCapabilityState,
        ProviderCapabilityProvenance catalogCapabilityProvenance,
        string? defaultBaseAddress,
        string catalogPath,
        string generationPath,
        bool isLocalDefault,
        bool playerMayEditEndpoint,
        bool requiresExplicitEndpointProfile = false,
        IEnumerable<ProviderEndpointProfile>? endpointProfiles = null,
        IEnumerable<string>? hardRejectedModelIds = null)
    {
        SchemaVersion = CurrentSchemaVersion;
        Kind = Defined(kind, nameof(kind));
        ProviderId = Identifier(providerId, 64, nameof(providerId));
        DisplayName = Text(displayName, 128, nameof(displayName));
        Protocol = Defined(protocol, nameof(protocol));
        Auth = Defined(auth, nameof(auth));
        CredentialRequirement = Defined(
            credentialRequirement,
            nameof(credentialRequirement));
        CatalogKind = Defined(catalogKind, nameof(catalogKind));
        CatalogCapabilityState = Defined(
            catalogCapabilityState,
            nameof(catalogCapabilityState));
        CatalogCapabilityProvenance = Defined(
            catalogCapabilityProvenance,
            nameof(catalogCapabilityProvenance));
        DefaultBaseUri = OptionalAbsoluteUri(
            defaultBaseAddress,
            nameof(defaultBaseAddress));
        CatalogPath = RelativePath(catalogPath, nameof(catalogPath));
        GenerationPath = RelativePath(generationPath, nameof(generationPath));
        IsLocalDefault = isLocalDefault;
        PlayerMayEditEndpoint = playerMayEditEndpoint;
        RequiresExplicitEndpointProfile = requiresExplicitEndpointProfile;

        var endpoints = (endpointProfiles ?? Array.Empty<ProviderEndpointProfile>())
            .ToArray();
        if (endpoints.Any(value => value == null)
            || endpoints.Select(value => value.ProfileId)
                .Distinct(StringComparer.Ordinal).Count() != endpoints.Length)
        {
            throw new ArgumentException(
                "Endpoint profiles must be non-null and uniquely identified.",
                nameof(endpointProfiles));
        }
        EndpointProfiles = new ReadOnlyCollection<ProviderEndpointProfile>(endpoints);

        var rejected = (hardRejectedModelIds ?? Array.Empty<string>())
            .Select(value => Identifier(value, 128, nameof(hardRejectedModelIds)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HardRejectedModelIds = new ReadOnlyCollection<string>(rejected);

        if (DefaultBaseUri == null
            && Kind != ProviderControlKind.CustomOpenAICompatible)
        {
            throw new ArgumentException(
                "Only the custom OpenAI-compatible profile may omit a default endpoint.",
                nameof(defaultBaseAddress));
        }
        if (DefaultBaseUri != null
            && IsLocalDefault != IsLoopback(DefaultBaseUri))
        {
            throw new ArgumentException(
                "Local-default metadata must match the default endpoint.",
                nameof(isLocalDefault));
        }
        if (RequiresExplicitEndpointProfile && EndpointProfiles.Count == 0)
        {
            throw new ArgumentException(
                "An explicit endpoint policy requires endpoint profiles.",
                nameof(endpointProfiles));
        }
        if (CredentialRequirement == ProviderCredentialRequirement.Required
            && Auth is ProviderControlAuth.NoneByDefault
                or ProviderControlAuth.OptionalBearer
                or ProviderControlAuth.LocalAiOptional
                or ProviderControlAuth.UserDefined)
        {
            throw new ArgumentException(
                "Required credentials need a required authentication scheme.",
                nameof(credentialRequirement));
        }
        if (CatalogCapabilityState == ProviderCapabilityState.Unknown
            && CatalogCapabilityProvenance != ProviderCapabilityProvenance.None)
        {
            throw new ArgumentException(
                "Unknown catalog capability must have no provenance.",
                nameof(catalogCapabilityProvenance));
        }
        if (CatalogCapabilityState == ProviderCapabilityState.Advertised
            && CatalogCapabilityProvenance is not (
                ProviderCapabilityProvenance.OfficialModelCatalog
                or ProviderCapabilityProvenance.OfficialModelMetadataEndpoint))
        {
            throw new ArgumentException(
                "Advertised catalog capability requires official catalog provenance.",
                nameof(catalogCapabilityProvenance));
        }
    }

    internal bool IsHardRejectedModel(string? modelId)
        => !string.IsNullOrWhiteSpace(modelId)
           && HardRejectedModelIds.Contains(
               modelId.Trim(),
               StringComparer.OrdinalIgnoreCase);

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)
            || Convert.ToInt32(value) == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    private static string Identifier(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(character =>
                !(char.IsLetterOrDigit(character)
                  || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "A bounded identifier is required.",
                parameterName);
        }
        return value;
    }

    private static string Text(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException("Bounded text is required.", parameterName);
        return value.Trim();
    }

    private static Uri? OptionalAbsoluteUri(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("An absolute HTTP endpoint is required.", parameterName);
        }
        return uri;
    }

    private static string RelativePath(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 256
            || value.StartsWith("/", StringComparison.Ordinal)
            || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A relative endpoint path is required.", parameterName);
        }
        return value;
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback
           || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}

internal static class UnifiedProviderProfileRegistry
{
    internal const int ContractVersion = 1;

    private static readonly IReadOnlyList<ProviderControlProfile> Profiles =
        new ReadOnlyCollection<ProviderControlProfile>(new[]
        {
            Profile(
                ProviderControlKind.DeepSeek,
                "deepseek",
                "DeepSeek",
                ProviderControlProtocol.OpenAiChatCompletions,
                ProviderControlAuth.Bearer,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.OpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "https://api.deepseek.com/",
                "models",
                "chat/completions",
                false,
                false,
                hardRejectedModelIds: new[]
                {
                    "deepseek-chat",
                    "deepseek-reasoner"
                }),
            Profile(
                ProviderControlKind.GoogleGemini,
                "gemini",
                "Google Gemini",
                ProviderControlProtocol.GeminiGenerateContent,
                ProviderControlAuth.GoogleApiKeyHeader,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.GeminiModelsPaginated,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "https://generativelanguage.googleapis.com/",
                "v1beta/models",
                "v1beta/models/{model}:generateContent",
                false,
                false),
            Profile(
                ProviderControlKind.OpenAI,
                "openai",
                "OpenAI",
                ProviderControlProtocol.OpenAiChatCompletions,
                ProviderControlAuth.Bearer,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.OpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "https://api.openai.com/",
                "v1/models",
                "v1/chat/completions",
                false,
                false),
            Profile(
                ProviderControlKind.QwenDashScope,
                "qwen-dashscope",
                "Qwen / DashScope",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.Bearer,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.QwenOptionalOpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "https://dashscope.aliyuncs.com/compatible-mode/v1/",
                "models",
                "chat/completions",
                false,
                true,
                requiresExplicitEndpointProfile: true,
                endpointProfiles: QwenEndpointProfiles()),
            Profile(
                ProviderControlKind.AnthropicClaude,
                "anthropic-claude",
                "Anthropic Claude",
                ProviderControlProtocol.AnthropicMessages,
                ProviderControlAuth.AnthropicApiKeyHeader,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.AnthropicModelsCursorPaginated,
                AdvertisedCapabilityState,
                CatalogCapabilityProvenance,
                "https://api.anthropic.com/",
                "v1/models",
                "v1/messages",
                false,
                false),
            Profile(
                ProviderControlKind.OpenRouter,
                "openrouter",
                "OpenRouter",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.Bearer,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.OpenRouterModels,
                AdvertisedCapabilityState,
                CatalogCapabilityProvenance,
                "https://openrouter.ai/api/v1/",
                "models",
                "chat/completions",
                false,
                false),
            Profile(
                ProviderControlKind.OllamaCloud,
                "ollama-cloud",
                "Ollama Cloud",
                ProviderControlProtocol.OllamaNativeChat,
                ProviderControlAuth.Bearer,
                ProviderCredentialRequirement.Required,
                ProviderCatalogKind.OllamaTagsAndShow,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "https://ollama.com/api/",
                "tags",
                "chat",
                false,
                false),
            Profile(
                ProviderControlKind.OllamaLocal,
                "ollama-local",
                "Ollama Local",
                ProviderControlProtocol.OllamaNativeChat,
                ProviderControlAuth.NoneByDefault,
                ProviderCredentialRequirement.NotRequired,
                ProviderCatalogKind.OllamaTagsAndShow,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "http://127.0.0.1:11434/api/",
                "tags",
                "chat",
                true,
                true),
            Profile(
                ProviderControlKind.LMStudio,
                "lm-studio",
                "LM Studio",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.OptionalBearer,
                ProviderCredentialRequirement.Optional,
                ProviderCatalogKind.LmStudioNativeModels,
                AdvertisedCapabilityState,
                CatalogCapabilityProvenance,
                "http://127.0.0.1:1234/",
                "api/v1/models",
                "v1/chat/completions",
                true,
                true),
            Profile(
                ProviderControlKind.LlamaCpp,
                "llama-cpp",
                "llama.cpp",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.OptionalBearer,
                ProviderCredentialRequirement.Optional,
                ProviderCatalogKind.OpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "http://127.0.0.1:8080/v1/",
                "models",
                "chat/completions",
                true,
                true),
            Profile(
                ProviderControlKind.Vllm,
                "vllm",
                "vLLM",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.OptionalBearer,
                ProviderCredentialRequirement.Optional,
                ProviderCatalogKind.OpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                "http://127.0.0.1:8000/v1/",
                "models",
                "chat/completions",
                true,
                true),
            Profile(
                ProviderControlKind.LocalAI,
                "local-ai",
                "LocalAI",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.LocalAiOptional,
                ProviderCredentialRequirement.Optional,
                ProviderCatalogKind.LocalAiCapabilities,
                AdvertisedCapabilityState,
                CatalogCapabilityProvenance,
                "http://127.0.0.1:8080/v1/",
                "models/capabilities",
                "chat/completions",
                true,
                true),
            Profile(
                ProviderControlKind.CustomOpenAICompatible,
                "custom-openai-compatible",
                "Custom OpenAI-compatible",
                ProviderControlProtocol.OpenAiCompatibleChat,
                ProviderControlAuth.UserDefined,
                ProviderCredentialRequirement.Optional,
                ProviderCatalogKind.OptionalOpenAiModels,
                UnknownCapabilityState,
                NoCapabilityProvenance,
                null,
                "models",
                "chat/completions",
                false,
                true)
        });

    private static readonly IReadOnlyDictionary<ProviderControlKind, ProviderControlProfile>
        ProfilesByKind = new ReadOnlyDictionary<ProviderControlKind, ProviderControlProfile>(
            Profiles.ToDictionary(profile => profile.Kind));

    private const ProviderCapabilityState UnknownCapabilityState =
        ProviderCapabilityState.Unknown;
    private const ProviderCapabilityState AdvertisedCapabilityState =
        ProviderCapabilityState.Advertised;
    private const ProviderCapabilityProvenance NoCapabilityProvenance =
        ProviderCapabilityProvenance.None;
    private const ProviderCapabilityProvenance CatalogCapabilityProvenance =
        ProviderCapabilityProvenance.OfficialModelCatalog;
    private const ProviderCapabilityProvenance MetadataCapabilityProvenance =
        ProviderCapabilityProvenance.OfficialModelMetadataEndpoint;

    internal static IReadOnlyList<ProviderControlProfile> All => Profiles;

    internal static ProviderControlProfile Get(ProviderControlKind kind)
    {
        if (!ProfilesByKind.TryGetValue(kind, out var profile))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return profile;
    }

    internal static bool TryGet(
        ProviderControlKind kind,
        out ProviderControlProfile? profile)
        => ProfilesByKind.TryGetValue(kind, out profile);

    internal static bool IsHardRejectedModel(
        ProviderControlKind kind,
        string? modelId)
        => Get(kind).IsHardRejectedModel(modelId);

    private static ProviderControlProfile Profile(
        ProviderControlKind kind,
        string providerId,
        string displayName,
        ProviderControlProtocol protocol,
        ProviderControlAuth auth,
        ProviderCredentialRequirement credentialRequirement,
        ProviderCatalogKind catalogKind,
        ProviderCapabilityState capabilityState,
        ProviderCapabilityProvenance capabilityProvenance,
        string? defaultBaseAddress,
        string catalogPath,
        string generationPath,
        bool isLocalDefault,
        bool playerMayEditEndpoint,
        bool requiresExplicitEndpointProfile = false,
        IEnumerable<ProviderEndpointProfile>? endpointProfiles = null,
        IEnumerable<string>? hardRejectedModelIds = null)
        => new(
            kind,
            providerId,
            displayName,
            protocol,
            auth,
            credentialRequirement,
            catalogKind,
            capabilityState,
            capabilityProvenance,
            defaultBaseAddress,
            catalogPath,
            generationPath,
            isLocalDefault,
            playerMayEditEndpoint,
            requiresExplicitEndpointProfile,
            endpointProfiles,
            hardRejectedModelIds);

    private static IEnumerable<ProviderEndpointProfile> QwenEndpointProfiles()
    {
        yield return new ProviderEndpointProfile(
            "cn-beijing-shared",
            "cn-beijing",
            "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            false,
            true);
        yield return new ProviderEndpointProfile(
            "ap-southeast-1-shared",
            "ap-southeast-1",
            "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/",
            false,
            true);
        yield return new ProviderEndpointProfile(
            "us-shared",
            "us",
            "https://dashscope-us.aliyuncs.com/compatible-mode/v1/",
            false,
            true);
        yield return new ProviderEndpointProfile(
            "cn-beijing-workspace",
            "cn-beijing",
            "https://{workspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/",
            true,
            false);
        yield return new ProviderEndpointProfile(
            "ap-southeast-1-workspace",
            "ap-southeast-1",
            "https://{workspaceId}.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1/",
            true,
            false);
        yield return new ProviderEndpointProfile(
            "ap-northeast-1-workspace",
            "ap-northeast-1",
            "https://{workspaceId}.ap-northeast-1.maas.aliyuncs.com/compatible-mode/v1/",
            true,
            false);
        yield return new ProviderEndpointProfile(
            "eu-central-1-workspace",
            "eu-central-1",
            "https://{workspaceId}.eu-central-1.maas.aliyuncs.com/compatible-mode/v1/",
            true,
            false);
    }
}
