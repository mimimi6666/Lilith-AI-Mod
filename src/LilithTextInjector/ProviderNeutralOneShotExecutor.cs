using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LilithProviderControlPlane;

namespace LilithTextInjector;

internal enum ProviderControlRuntimeProfileAdmission
{
    Accepted = 0,
    UnknownControlKind = 1,
    CredentialRequired = 2,
    CredentialNotAllowed = 3
}

internal enum ProviderRuntimeCompatibilitySeam
{
    None = 0,
    D026GeminiGenerateContent = 1,
    D026OpenAiChatCompletions = 2
}

internal sealed class ProviderControlRuntimeProfile
{
    internal ProviderControlKind ControlKind { get; }
    internal string ControlProviderId { get; }
    internal ProviderControlProtocol ControlPlaneProtocol { get; }
    internal ProviderCredentialRequirement CredentialRequirement { get; }
    internal bool IsLocalDefault { get; }
    internal ProviderKind RuntimeProvider { get; }
    internal ProviderProtocolKind RuntimeProtocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal PreparedProviderRoute PreparedRoute { get; }
    internal PreparedProviderProtocol PreparedProtocol { get; }
    internal string RouteToken { get; }
    internal string CodecProfileVersion { get; }
    internal ProviderRuntimeCompatibilitySeam CompatibilitySeam { get; }
    internal string ProfileFingerprint { get; }
    internal bool TextGenerationOnly => true;
    internal int AutomaticRetryCount => 0;
    internal int ProviderFallbackCount => 0;
    internal int ModelFallbackCount => 0;

    internal ProviderControlRuntimeProfile(
        ProviderControlKind controlKind,
        ProviderKind runtimeProvider,
        ProviderProtocolKind runtimeProtocol,
        ProviderAuthenticationScheme authenticationScheme,
        PreparedProviderRoute preparedRoute,
        PreparedProviderProtocol preparedProtocol,
        string routeToken,
        string codecProfileVersion,
        ProviderRuntimeCompatibilitySeam compatibilitySeam)
    {
        var control = UnifiedProviderProfileRegistry.Get(controlKind);
        ControlKind = controlKind;
        ControlProviderId = control.ProviderId;
        ControlPlaneProtocol = control.Protocol;
        CredentialRequirement = control.CredentialRequirement;
        IsLocalDefault = control.IsLocalDefault;
        RuntimeProvider = ProviderRegistryContractGuard.Defined(
            runtimeProvider,
            nameof(runtimeProvider));
        RuntimeProtocol = ProviderRegistryContractGuard.Defined(
            runtimeProtocol,
            nameof(runtimeProtocol));
        ProtocolProfileVersion = 1;
        AuthenticationScheme = ProviderRegistryContractGuard.Defined(
            authenticationScheme,
            nameof(authenticationScheme));
        if (!Enum.IsDefined(typeof(PreparedProviderRoute), preparedRoute))
            throw new ArgumentOutOfRangeException(nameof(preparedRoute));
        if (!Enum.IsDefined(typeof(PreparedProviderProtocol), preparedProtocol))
            throw new ArgumentOutOfRangeException(nameof(preparedProtocol));
        if (!Enum.IsDefined(typeof(ProviderRuntimeCompatibilitySeam), compatibilitySeam))
            throw new ArgumentOutOfRangeException(nameof(compatibilitySeam));
        PreparedRoute = preparedRoute;
        PreparedProtocol = preparedProtocol;
        RouteToken = ProviderRegistryContractGuard.Identifier(
            routeToken,
            nameof(routeToken));
        CodecProfileVersion = ProviderRegistryContractGuard.Identifier(
            codecProfileVersion,
            nameof(codecProfileVersion));
        CompatibilitySeam = compatibilitySeam;
        ProfileFingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/PROVIDER-CONTROL-RUNTIME-PROFILE/1",
            ProviderRegistryCanonicalHash.Number((int)ControlKind),
            ControlProviderId,
            ProviderRegistryCanonicalHash.Number((int)ControlPlaneProtocol),
            ProviderRegistryCanonicalHash.Number((int)CredentialRequirement),
            IsLocalDefault ? "local-default" : "remote-default",
            ProviderRegistryCanonicalHash.Number((int)RuntimeProvider),
            ProviderRegistryCanonicalHash.Number((int)RuntimeProtocol),
            ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
            ProviderRegistryCanonicalHash.Number((int)AuthenticationScheme),
            ProviderRegistryCanonicalHash.Number((int)PreparedRoute),
            ProviderRegistryCanonicalHash.Number((int)PreparedProtocol),
            RouteToken,
            CodecProfileVersion,
            ProviderRegistryCanonicalHash.Number((int)CompatibilitySeam));
    }

    internal bool AllowsCapability(ProviderCapability capability)
        => capability == ProviderCapability.TextGeneration;
}

internal static class ProviderControlRuntimeProfileResolver
{
    internal static ProviderControlRuntimeProfileAdmission TryResolve(
        ProviderControlKind controlKind,
        bool credentialConfigured,
        out ProviderControlRuntimeProfile? profile)
    {
        profile = null;
        if (!UnifiedProviderProfileRegistry.TryGet(controlKind, out var control)
            || control == null)
        {
            return ProviderControlRuntimeProfileAdmission.UnknownControlKind;
        }
        if (control.CredentialRequirement
                == ProviderCredentialRequirement.Required
            && !credentialConfigured)
        {
            return ProviderControlRuntimeProfileAdmission.CredentialRequired;
        }
        if (control.CredentialRequirement
                == ProviderCredentialRequirement.NotRequired
            && credentialConfigured)
        {
            return ProviderControlRuntimeProfileAdmission.CredentialNotAllowed;
        }

        profile = controlKind switch
        {
            ProviderControlKind.DeepSeek => New(
                controlKind,
                ProviderKind.DeepSeek,
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                PreparedProviderRoute.DeepSeek,
                PreparedProviderProtocol.OpenAiChatCompletions,
                "DeepSeek",
                "deepseek-v4-2026-04-24"),
            ProviderControlKind.GoogleGemini => New(
                controlKind,
                ProviderKind.Gemini,
                ProviderProtocolKind.GeminiGenerateContent,
                ProviderAuthenticationScheme.GoogleApiKey,
                PreparedProviderRoute.Gemini,
                PreparedProviderProtocol.GeminiGenerateContent,
                "Gemini",
                "d026-v1",
                ProviderRuntimeCompatibilitySeam.D026GeminiGenerateContent),
            ProviderControlKind.OpenAI => New(
                controlKind,
                ProviderKind.OpenAI,
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                PreparedProviderRoute.OpenAI,
                PreparedProviderProtocol.OpenAiChatCompletions,
                "OpenAI",
                "d026-v1",
                ProviderRuntimeCompatibilitySeam.D026OpenAiChatCompletions),
            ProviderControlKind.QwenDashScope => New(
                controlKind,
                ProviderKind.QwenDashScope,
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                PreparedProviderRoute.Qwen,
                PreparedProviderProtocol.OpenAiChatCompletions,
                "QwenChat",
                "d033-qwen-compatible-chat-v1"),
            ProviderControlKind.AnthropicClaude => New(
                controlKind,
                ProviderKind.AnthropicClaude,
                ProviderProtocolKind.AnthropicMessages,
                ProviderAuthenticationScheme.AnthropicApiKey,
                PreparedProviderRoute.AnthropicClaude,
                PreparedProviderProtocol.AnthropicMessages,
                "Claude",
                "anthropic-messages-2023-06-01"),
            ProviderControlKind.OpenRouter => New(
                controlKind,
                ProviderKind.OpenRouter,
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                PreparedProviderRoute.OpenRouter,
                PreparedProviderProtocol.OpenAiChatCompletions,
                "OpenRouter",
                "d033-openrouter-chat-v1"),
            ProviderControlKind.OllamaCloud => New(
                controlKind,
                ProviderKind.OllamaCloud,
                ProviderProtocolKind.OllamaNativeChat,
                ProviderAuthenticationScheme.Bearer,
                PreparedProviderRoute.OllamaCloud,
                PreparedProviderProtocol.OllamaNativeChat,
                "OllamaCloud",
                "d033-ollama-cloud-chat-v1"),
            ProviderControlKind.OllamaLocal => New(
                controlKind,
                ProviderKind.OllamaLocal,
                ProviderProtocolKind.OllamaNativeChat,
                ProviderAuthenticationScheme.None,
                PreparedProviderRoute.OllamaLocal,
                PreparedProviderProtocol.OllamaNativeChat,
                "OllamaLocal",
                "d033-ollama-local-chat-v1"),
            ProviderControlKind.LMStudio => Compatible(
                controlKind,
                ProviderKind.LMStudio,
                credentialConfigured,
                "LMStudio",
                "d033-lm-studio-chat-v1"),
            ProviderControlKind.LlamaCpp => Compatible(
                controlKind,
                ProviderKind.LlamaCpp,
                credentialConfigured,
                "LlamaCpp",
                "d033-llama-cpp-chat-v1"),
            ProviderControlKind.Vllm => Compatible(
                controlKind,
                ProviderKind.Vllm,
                credentialConfigured,
                "vLLM",
                "d033-vllm-chat-v1"),
            ProviderControlKind.LocalAI => Compatible(
                controlKind,
                ProviderKind.LocalAI,
                credentialConfigured,
                "LocalAI",
                "d033-local-ai-chat-v1"),
            ProviderControlKind.CustomOpenAICompatible => Compatible(
                controlKind,
                ProviderKind.CustomOpenAICompatible,
                credentialConfigured,
                "OpenAICompatible",
                "d033-custom-openai-chat-v1"),
            _ => null
        };
        return profile == null
            ? ProviderControlRuntimeProfileAdmission.UnknownControlKind
            : ProviderControlRuntimeProfileAdmission.Accepted;
    }

    private static ProviderControlRuntimeProfile Compatible(
        ProviderControlKind controlKind,
        ProviderKind runtimeProvider,
        bool credentialConfigured,
        string routeToken,
        string profilePrefix)
        => New(
            controlKind,
            runtimeProvider,
            ProviderProtocolKind.OpenAiChatCompletions,
            credentialConfigured
                ? ProviderAuthenticationScheme.Bearer
                : ProviderAuthenticationScheme.None,
            PreparedProviderRoute.OpenAICompatible,
            PreparedProviderProtocol.OpenAiChatCompletions,
            routeToken,
            profilePrefix + (credentialConfigured ? "-bearer" : "-none"));

    private static ProviderControlRuntimeProfile New(
        ProviderControlKind controlKind,
        ProviderKind runtimeProvider,
        ProviderProtocolKind runtimeProtocol,
        ProviderAuthenticationScheme authenticationScheme,
        PreparedProviderRoute preparedRoute,
        PreparedProviderProtocol preparedProtocol,
        string routeToken,
        string codecProfileVersion,
        ProviderRuntimeCompatibilitySeam compatibilitySeam =
            ProviderRuntimeCompatibilitySeam.None)
        => new(
            controlKind,
            runtimeProvider,
            runtimeProtocol,
            authenticationScheme,
            preparedRoute,
            preparedProtocol,
            routeToken,
            codecProfileVersion,
            compatibilitySeam);
}

internal enum ProviderNeutralOneShotProfileAdmission
{
    Invalid = 0,
    NotEvaluated = 1,
    Accepted = 2,
    UnsupportedProvider = 3,
    ProtocolMismatch = 4,
    ProtocolProfileVersionMismatch = 5,
    AuthenticationMismatch = 6,
    LocalEndpointRequired = 7,
    PreparedRouteMismatch = 8
}

internal enum ProviderNeutralOneShotExecutionAdmission
{
    Invalid = 0,
    CompletedForSourceVerification = 1,
    DuplicateExecutorUse = 2,
    ProfileRejected = 3,
    RegistryIdentityMismatch = 4,
    RouteIdentityMismatch = 5,
    PreparedRequestMismatch = 6,
    TicketRejected = 7,
    ModelTransportRejected = 8,
    CredentialRejected = 9,
    CodecRejected = 10,
    FixtureCodecRejected = 11,
    RequestExpired = 12,
    HttpRejected = 13,
    ResponseTooLarge = 14,
    ResponseRejected = 15,
    CompletionRejected = 16,
    NetworkFailure = 17,
    Cancelled = 18,
    Timeout = 19,
    RedirectRejected = 20,
    ConnectedAddressRejected = 21
}

internal sealed class ProviderNeutralOneShotProfile
{
    internal ProviderKind Provider { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal PreparedProviderRoute PreparedRoute { get; }
    internal PreparedProviderProtocol PreparedProtocol { get; }
    internal string CodecProfileVersion { get; }
    internal string ProfileFingerprint { get; }

    private ProviderNeutralOneShotProfile(
        ProviderKind provider,
        ProviderProtocolKind protocol,
        int protocolProfileVersion,
        ProviderAuthenticationScheme authenticationScheme,
        PreparedProviderRoute preparedRoute,
        PreparedProviderProtocol preparedProtocol,
        string codecProfileVersion)
    {
        Provider = ProviderRegistryContractGuard.Defined(
            provider,
            nameof(provider));
        Protocol = ProviderRegistryContractGuard.Defined(
            protocol,
            nameof(protocol));
        if (protocolProfileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolProfileVersion));
        }
        ProtocolProfileVersion = protocolProfileVersion;
        AuthenticationScheme = ProviderRegistryContractGuard.Defined(
            authenticationScheme,
            nameof(authenticationScheme));
        if (!Enum.IsDefined(typeof(PreparedProviderRoute), preparedRoute))
            throw new ArgumentOutOfRangeException(nameof(preparedRoute));
        if (!Enum.IsDefined(typeof(PreparedProviderProtocol), preparedProtocol))
            throw new ArgumentOutOfRangeException(nameof(preparedProtocol));
        PreparedRoute = preparedRoute;
        PreparedProtocol = preparedProtocol;
        CodecProfileVersion = ProviderRegistryContractGuard.Identifier(
            codecProfileVersion,
            nameof(codecProfileVersion));
        ProfileFingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/PROVIDER-NEUTRAL-ONE-SHOT-PROFILE/1",
            ProviderRegistryCanonicalHash.Number((int)Provider),
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
            ProviderRegistryCanonicalHash.Number((int)AuthenticationScheme),
            ProviderRegistryCanonicalHash.Number((int)PreparedRoute),
            ProviderRegistryCanonicalHash.Number((int)PreparedProtocol),
            CodecProfileVersion);
    }

    internal static ProviderNeutralOneShotProfileAdmission TryResolve(
        ProviderEndpointInstance instance,
        PreparedProviderExecutionRequest request,
        out ProviderNeutralOneShotProfile? profile)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        profile = null;
        if (!IsKnownProvider(instance.Kind))
        {
            return ProviderNeutralOneShotProfileAdmission
                .UnsupportedProvider;
        }
        if (instance.ProtocolProfileVersion != 1)
        {
            return ProviderNeutralOneShotProfileAdmission
                .ProtocolProfileVersionMismatch;
        }
        if (!IsKnownProtocol(instance.Kind, instance.Protocol))
        {
            return ProviderNeutralOneShotProfileAdmission
                .ProtocolMismatch;
        }
        if (!IsKnownAuthentication(
                instance.Kind,
                instance.AuthenticationScheme))
        {
            return ProviderNeutralOneShotProfileAdmission
                .AuthenticationMismatch;
        }
        profile = ResolveExact(instance);
        if (profile == null)
        {
            return ProviderNeutralOneShotProfileAdmission
                .ProtocolMismatch;
        }
        if (profile.AuthenticationScheme == ProviderAuthenticationScheme.None
            && !instance.BaseUri.Value.IsLoopback)
        {
            profile = null;
            return ProviderNeutralOneShotProfileAdmission
                .LocalEndpointRequired;
        }
        if (profile.PreparedRoute != request.Route
            || profile.PreparedProtocol != request.Protocol
            || !MatchesRouteToken(instance.Kind, request.ProviderRouteId))
        {
            profile = null;
            return ProviderNeutralOneShotProfileAdmission
                .PreparedRouteMismatch;
        }
        return ProviderNeutralOneShotProfileAdmission.Accepted;
    }

    private static ProviderNeutralOneShotProfile? ResolveExact(
        ProviderEndpointInstance instance)
    {
        if (instance.ProtocolProfileVersion != 1)
            return null;
        return (instance.Kind, instance.Protocol, instance.AuthenticationScheme)
            switch
            {
                (ProviderKind.Gemini,
                    ProviderProtocolKind.GeminiGenerateContent,
                    ProviderAuthenticationScheme.GoogleApiKey) =>
                    New(
                        instance,
                        PreparedProviderRoute.Gemini,
                        PreparedProviderProtocol.GeminiGenerateContent,
                        "d026-v1"),
                (ProviderKind.OpenAI,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.OpenAI,
                        PreparedProviderProtocol.OpenAiChatCompletions,
                        "d026-v1"),
                (ProviderKind.DeepSeek,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.DeepSeek,
                        PreparedProviderProtocol.OpenAiChatCompletions,
                        "deepseek-v4-2026-04-24"),
                (ProviderKind.QwenDashScope,
                    ProviderProtocolKind.OpenAiResponses,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.Qwen,
                        PreparedProviderProtocol.OpenAiResponses,
                        "d026-v1"),
                (ProviderKind.QwenDashScope,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.Qwen,
                        PreparedProviderProtocol.OpenAiChatCompletions,
                        "d033-qwen-compatible-chat-v1"),
                (ProviderKind.AnthropicClaude,
                    ProviderProtocolKind.AnthropicMessages,
                    ProviderAuthenticationScheme.AnthropicApiKey) =>
                    New(
                        instance,
                        PreparedProviderRoute.AnthropicClaude,
                        PreparedProviderProtocol.AnthropicMessages,
                        "anthropic-messages-2023-06-01"),
                (ProviderKind.Ollama,
                    ProviderProtocolKind.OllamaNativeChat,
                    ProviderAuthenticationScheme.None) =>
                    New(
                        instance,
                        PreparedProviderRoute.Ollama,
                        PreparedProviderProtocol.OllamaNativeChat,
                        "ollama-chat-v1"),
                (ProviderKind.OpenRouter,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.OpenRouter,
                        PreparedProviderProtocol.OpenAiChatCompletions,
                        "d033-openrouter-chat-v1"),
                (ProviderKind.OllamaCloud,
                    ProviderProtocolKind.OllamaNativeChat,
                    ProviderAuthenticationScheme.Bearer) =>
                    New(
                        instance,
                        PreparedProviderRoute.OllamaCloud,
                        PreparedProviderProtocol.OllamaNativeChat,
                        "d033-ollama-cloud-chat-v1"),
                (ProviderKind.OllamaLocal,
                    ProviderProtocolKind.OllamaNativeChat,
                    ProviderAuthenticationScheme.None) =>
                    New(
                        instance,
                        PreparedProviderRoute.OllamaLocal,
                        PreparedProviderProtocol.OllamaNativeChat,
                        "d033-ollama-local-chat-v1"),
                (ProviderKind.CustomOpenAICompatible,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: true,
                        "d033-custom-openai-chat-v1"),
                (ProviderKind.CustomOpenAICompatible,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.None) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: false,
                        "d033-custom-openai-chat-v1"),
                (ProviderKind.LlamaCpp,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.None) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: false,
                        "d033-llama-cpp-chat-v1"),
                (ProviderKind.LlamaCpp,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: true,
                        "d033-llama-cpp-chat-v1"),
                (ProviderKind.LMStudio,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.None) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: false,
                        "d033-lm-studio-chat-v1"),
                (ProviderKind.LMStudio,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: true,
                        "d033-lm-studio-chat-v1"),
                (ProviderKind.Vllm,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.None) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: false,
                        "d033-vllm-chat-v1"),
                (ProviderKind.Vllm,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: true,
                        "d033-vllm-chat-v1"),
                (ProviderKind.LocalAI,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.None) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: false,
                        "d033-local-ai-chat-v1"),
                (ProviderKind.LocalAI,
                    ProviderProtocolKind.OpenAiChatCompletions,
                    ProviderAuthenticationScheme.Bearer) =>
                    OpenAiCompatible(
                        instance,
                        authenticated: true,
                        "d033-local-ai-chat-v1"),
                _ => null
            };
    }

    private static ProviderNeutralOneShotProfile New(
        ProviderEndpointInstance instance,
        PreparedProviderRoute route,
        PreparedProviderProtocol protocol,
        string codecProfileVersion)
        => new(
            instance.Kind,
            instance.Protocol,
            instance.ProtocolProfileVersion,
            instance.AuthenticationScheme,
            route,
            protocol,
            codecProfileVersion);

    private static ProviderNeutralOneShotProfile OpenAiCompatible(
        ProviderEndpointInstance instance,
        bool authenticated,
        string profilePrefix = "openai-compatible-chat-v1")
        => New(
            instance,
            PreparedProviderRoute.OpenAICompatible,
            PreparedProviderProtocol.OpenAiChatCompletions,
            profilePrefix + (authenticated ? "-bearer" : "-none"));

    private static bool IsKnownProvider(ProviderKind provider)
        => provider is ProviderKind.Gemini
            or ProviderKind.OpenAI
            or ProviderKind.DeepSeek
            or ProviderKind.QwenDashScope
            or ProviderKind.AnthropicClaude
            or ProviderKind.Ollama
            or ProviderKind.OpenRouter
            or ProviderKind.OllamaCloud
            or ProviderKind.OllamaLocal
            or ProviderKind.CustomOpenAICompatible
            or ProviderKind.LlamaCpp
            or ProviderKind.LMStudio
            or ProviderKind.Vllm
            or ProviderKind.LocalAI;

    private static bool IsKnownProtocol(
        ProviderKind provider,
        ProviderProtocolKind protocol)
        => provider switch
        {
            ProviderKind.Gemini =>
                protocol == ProviderProtocolKind.GeminiGenerateContent,
            ProviderKind.QwenDashScope =>
                protocol is ProviderProtocolKind.OpenAiResponses
                    or ProviderProtocolKind.OpenAiChatCompletions,
            ProviderKind.AnthropicClaude =>
                protocol == ProviderProtocolKind.AnthropicMessages,
            ProviderKind.Ollama =>
                protocol == ProviderProtocolKind.OllamaNativeChat,
            ProviderKind.OllamaCloud
                or ProviderKind.OllamaLocal =>
                protocol == ProviderProtocolKind.OllamaNativeChat,
            ProviderKind.OpenAI
                or ProviderKind.DeepSeek
                or ProviderKind.OpenRouter
                or ProviderKind.CustomOpenAICompatible
                or ProviderKind.LlamaCpp
                or ProviderKind.LMStudio
                or ProviderKind.Vllm
                or ProviderKind.LocalAI =>
                protocol == ProviderProtocolKind.OpenAiChatCompletions,
            _ => false
        };

    private static bool IsKnownAuthentication(
        ProviderKind provider,
        ProviderAuthenticationScheme scheme)
        => provider switch
        {
            ProviderKind.Gemini =>
                scheme == ProviderAuthenticationScheme.GoogleApiKey,
            ProviderKind.AnthropicClaude =>
                scheme == ProviderAuthenticationScheme.AnthropicApiKey,
            ProviderKind.OpenAI
                or ProviderKind.DeepSeek
                or ProviderKind.QwenDashScope
                or ProviderKind.OpenRouter
                or ProviderKind.OllamaCloud =>
                scheme == ProviderAuthenticationScheme.Bearer,
            ProviderKind.CustomOpenAICompatible =>
                scheme is ProviderAuthenticationScheme.Bearer
                    or ProviderAuthenticationScheme.None,
            ProviderKind.Ollama
                or ProviderKind.OllamaLocal =>
                scheme == ProviderAuthenticationScheme.None,
            ProviderKind.LlamaCpp
                or ProviderKind.LMStudio
                or ProviderKind.Vllm
                or ProviderKind.LocalAI =>
                scheme is ProviderAuthenticationScheme.None
                    or ProviderAuthenticationScheme.Bearer,
            _ => false
        };

    private static bool MatchesRouteToken(
        ProviderKind provider,
        string token)
        => provider switch
        {
            ProviderKind.Gemini => token == "Gemini",
            ProviderKind.OpenAI => token == "OpenAI",
            ProviderKind.DeepSeek => token == "DeepSeek",
            ProviderKind.QwenDashScope =>
                token is "Qwen" or "QwenChat",
            ProviderKind.AnthropicClaude => token == "Claude",
            ProviderKind.Ollama => token == "Ollama",
            ProviderKind.OpenRouter => token == "OpenRouter",
            ProviderKind.OllamaCloud => token == "OllamaCloud",
            ProviderKind.OllamaLocal => token == "OllamaLocal",
            ProviderKind.CustomOpenAICompatible =>
                token == "OpenAICompatible",
            ProviderKind.LlamaCpp => token == "LlamaCpp",
            ProviderKind.LMStudio => token == "LMStudio",
            ProviderKind.Vllm => token == "vLLM",
            ProviderKind.LocalAI => token == "LocalAI",
            _ => false
        };
}

internal interface IProviderOneShotModelTransportResolver
{
    ProviderModelTransportLeaseResult Resolve(
        ProviderModelDescriptor model,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt);
}

internal sealed class ContractReplayProviderOneShotModelTransportResolver
    : IProviderOneShotModelTransportResolver
{
    private readonly IReadOnlyDictionary<string, string> _valuesByReference;

    internal ContractReplayProviderOneShotModelTransportResolver(
        IReadOnlyDictionary<string, string> valuesByReference)
    {
        _valuesByReference = valuesByReference
            ?? throw new ArgumentNullException(nameof(valuesByReference));
    }

    public ProviderModelTransportLeaseResult Resolve(
        ProviderModelDescriptor model,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (!_valuesByReference.TryGetValue(
                model.TransportReference.Value,
                out var value))
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission.EmptyTransportValue);
        }
        return ModelTransportLease.TryCreateForContractReplay(
            model.InstanceId,
            model.ModelInstanceId,
            model.TransportReference,
            model.CorrelationHash,
            value,
            issuedAt,
            expiresAt);
    }
}

internal sealed class ProviderNeutralOneShotExecutionResult
{
    internal ProviderNeutralOneShotExecutionAdmission Admission { get; }
    internal ProviderNeutralOneShotProfileAdmission ProfileAdmission { get; }
    internal ProviderExecutionTicketConsumptionAdmission? TicketAdmission
        { get; }
    internal ProviderExecutionCompletionAdmission? CompletionAdmission
        { get; }
    internal ProviderProtocolNormalizedCompletion? Completion { get; }
    internal ProviderExecutionCompletionIdentity? CompletionIdentity { get; }
    internal string? ProfileFingerprint { get; }
    internal int NetworkCalls { get; }
    internal bool Accepted =>
        Admission
            == ProviderNeutralOneShotExecutionAdmission
                .CompletedForSourceVerification;
    internal bool AllowsDeliveryInStageD => false;
    internal int AutomaticRetries => 0;
    internal int ProviderFallbacks => 0;

    internal ProviderNeutralOneShotExecutionResult(
        ProviderNeutralOneShotExecutionAdmission admission,
        ProviderNeutralOneShotProfileAdmission profileAdmission,
        ProviderExecutionTicketConsumptionAdmission? ticketAdmission,
        ProviderExecutionCompletionAdmission? completionAdmission,
        ProviderProtocolNormalizedCompletion? completion,
        ProviderExecutionCompletionIdentity? completionIdentity,
        string? profileFingerprint,
        int networkCalls)
    {
        if (!Enum.IsDefined(
                typeof(ProviderNeutralOneShotExecutionAdmission),
                admission)
            || admission == ProviderNeutralOneShotExecutionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (!Enum.IsDefined(
                typeof(ProviderNeutralOneShotProfileAdmission),
                profileAdmission)
            || profileAdmission
                == ProviderNeutralOneShotProfileAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(profileAdmission));
        }
        if (networkCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        if (admission
                == ProviderNeutralOneShotExecutionAdmission
                    .CompletedForSourceVerification
            && (completion == null
                || completionIdentity == null
                || completionAdmission
                    != ProviderExecutionCompletionAdmission
                        .AcceptedForContractReplay))
        {
            throw new ArgumentException(
                "Accepted execution requires one admitted completion.");
        }
        if (admission
                != ProviderNeutralOneShotExecutionAdmission
                    .CompletedForSourceVerification
            && (completion != null || completionIdentity != null))
        {
            throw new ArgumentException(
                "Rejected execution cannot carry completion content.");
        }
        Admission = admission;
        ProfileAdmission = profileAdmission;
        TicketAdmission = ticketAdmission;
        CompletionAdmission = completionAdmission;
        Completion = completion;
        CompletionIdentity = completionIdentity;
        ProfileFingerprint = profileFingerprint;
        NetworkCalls = networkCalls;
    }
}

internal static class ProviderNeutralOneShotHttpClientFactory
{
    internal static SocketsHttpHandler CreateHandler()
        => UnifiedProviderConnectedAddressBoundary.CreateHandler(
            new UnifiedProviderConnectedAddressBoundary(),
            maximumConnectionsPerServer: 1);

    internal static HttpClient CreateClient()
        => UnifiedProviderConnectedAddressHttp.CreateClient(
            maximumConnectionsPerServer: 1);
}

internal sealed class ProviderNeutralOneShotExecutor
{
    internal const int MaximumResponseBytes = 256 * 1024;
    internal const int AutomaticRetryCount = 0;
    internal const int ProviderFallbackCount = 0;

    private readonly HttpClient _http;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderOneShotModelTransportResolver _models;
    private readonly IUnifiedProviderConnectedAddressBoundary _connectedAddresses;
    private readonly IPreparedProviderExecutionLifecycle? _lifecycle;
    private readonly ProviderExecutionCompletionLedger _completions = new();
    private readonly Func<DateTimeOffset> _clock;
    private int _attempted;

    internal ProviderNeutralOneShotExecutor(
        HttpClient http,
        IProviderCredentialResolver credentials,
        IProviderOneShotModelTransportResolver models,
        Func<DateTimeOffset>? clock = null,
        IPreparedProviderExecutionLifecycle? lifecycle = null)
        : this(http, credentials, models, null, clock, lifecycle)
    {
    }

    internal ProviderNeutralOneShotExecutor(
        HttpClient http,
        IProviderCredentialResolver credentials,
        IProviderOneShotModelTransportResolver models,
        IUnifiedProviderConnectedAddressBoundary? connectedAddresses,
        Func<DateTimeOffset>? clock = null,
        IPreparedProviderExecutionLifecycle? lifecycle = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentials = credentials
            ?? throw new ArgumentNullException(nameof(credentials));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _connectedAddresses = UnifiedProviderConnectedAddressHttp.BoundaryFor(
            http,
            connectedAddresses);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lifecycle = lifecycle;
    }

    internal async Task<ProviderNeutralOneShotExecutionResult> ExecuteOnceAsync(
        ProviderExecutionAuthorizationTicket ticket,
        ProviderExecutionBinding binding,
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        CharacterRuntimeIdentity activeRuntime,
        CancellationToken cancellationToken)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (Interlocked.Exchange(ref _attempted, 1) != 0)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.DuplicateExecutorUse);
        }

        if (!registry.TryGetInstance(route.InstanceId, out var instance)
            || instance == null
            || !registry.TryGetModel(
                route.InstanceId,
                route.ModelInstanceId,
                out var model)
            || model == null)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission
                    .RegistryIdentityMismatch);
        }

        var profileAdmission = ProviderNeutralOneShotProfile.TryResolve(
            instance,
            request,
            out var profile);
        if (profileAdmission
                != ProviderNeutralOneShotProfileAdmission.Accepted
            || profile == null)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.ProfileRejected,
                profileAdmission);
        }
        if (!MatchesRoute(
                binding,
                registry,
                route,
                instance,
                model))
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.RouteIdentityMismatch,
                profileAdmission,
                profile.ProfileFingerprint);
        }
        if (!MatchesPrepared(binding, request))
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.PreparedRequestMismatch,
                profileAdmission,
                profile.ProfileFingerprint);
        }

        var now = _clock();
        if (now < request.RequestedAt
            || now >= binding.EffectiveDeadline)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.RequestExpired,
                profileAdmission,
                profile.ProfileFingerprint);
        }
        var ticketAdmission = ticket.TryConsume(
            binding,
            activeRuntime,
            registry.RegistryGeneration,
            instance.ConfigurationGeneration,
            now);
        if (ticketAdmission
            != ProviderExecutionTicketConsumptionAdmission
                .ConsumedForContractReplay)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.TicketRejected,
                profileAdmission,
                profile.ProfileFingerprint,
                ticketAdmission);
        }

        var routeScope = instance.ConnectionRouteLocality switch
        {
            ConnectionRouteLocality.Loopback =>
                UnifiedProviderConnectedAddressRouteScope.LoopbackOnly,
            ConnectionRouteLocality.Lan =>
                UnifiedProviderConnectedAddressRouteScope.PrivateLanOnly,
            ConnectionRouteLocality.PublicRemote =>
                UnifiedProviderConnectedAddressRouteScope.PublicOnly,
            _ => UnifiedProviderConnectedAddressRouteScope.Invalid
        };
        if (routeScope == UnifiedProviderConnectedAddressRouteScope.Invalid)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission
                    .ConnectedAddressRejected,
                profileAdmission,
                profile.ProfileFingerprint,
                ticketAdmission);
        }
        var connectedContext = new UnifiedProviderConnectedAddressContext(
            instance.InstanceId.Value,
            instance.ConfigurationGeneration,
            instance.ConfigurationFingerprint,
            instance.EndpointFingerprint,
            ticket.TicketId,
            instance.BaseUri.Value,
            routeScope,
            now,
            binding.EffectiveDeadline);
        var connectedResult = await _connectedAddresses.PrepareAsync(
                connectedContext,
                cancellationToken)
            .ConfigureAwait(false);
        if (!connectedResult.Accepted || connectedResult.Plan == null)
        {
            var callerCancelled = cancellationToken.IsCancellationRequested;
            var expired = connectedResult.Reason
                == UnifiedProviderConnectedAddressReason.ContextExpired;
            return Reject(
                callerCancelled
                    ? ProviderNeutralOneShotExecutionAdmission.Cancelled
                    : expired
                        ? ProviderNeutralOneShotExecutionAdmission.Timeout
                        : ProviderNeutralOneShotExecutionAdmission
                            .ConnectedAddressRejected,
                profileAdmission,
                profile.ProfileFingerprint,
                ticketAdmission);
        }

        var leaseDeadline = Min(
            binding.EffectiveDeadline,
            now + ModelTransportLease.MaximumLifetime);
        var modelResult = _models.Resolve(model, now, leaseDeadline);
        using var modelLease = modelResult.Lease;
        if (!modelResult.Accepted || modelLease == null)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission
                    .ModelTransportRejected,
                profileAdmission,
                profile.ProfileFingerprint,
                ticketAdmission);
        }

        CredentialLease? credentialLease = null;
        var credentialNow = _clock();
        if (credentialNow < now || credentialNow >= binding.EffectiveDeadline)
        {
            return Reject(
                ProviderNeutralOneShotExecutionAdmission.Timeout,
                profileAdmission,
                profile.ProfileFingerprint,
                ticketAdmission);
        }
        if (instance.AuthenticationScheme != ProviderAuthenticationScheme.None)
        {
            var credentialResult = _credentials.Resolve(
                new ProviderCredentialResolutionRequest(
                    instance,
                    credentialNow,
                    Min(
                        binding.EffectiveDeadline,
                        credentialNow + CredentialLease.MaximumLifetime)),
                instance);
            credentialLease = credentialResult.Lease;
            if (!credentialResult.Accepted || credentialLease == null)
            {
                credentialLease?.Dispose();
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission
                        .CredentialRejected,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }
        }

        using (credentialLease)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var modelTransportName = modelLease.CopyTransportValue(credentialNow);
            var codecRequest = ProviderProtocolCodecRequest.FromPrepared(
                instance.Kind,
                instance.Protocol,
                profile.CodecProfileVersion,
                instance.InstanceId,
                modelTransportName,
                request);
            if (!ProviderProtocolCodecResolver.TryResolve(
                    instance.Kind,
                    instance.Protocol,
                    profile.CodecProfileVersion,
                    out var codec)
                || codec == null)
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission.CodecRejected,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }
            var codecAdmission = codec.TryEncode(
                codecRequest,
                now,
                out var envelope);
            if (codecAdmission != ProviderProtocolCodecAdmission.Encoded
                || envelope == null)
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission.CodecRejected,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }
            if (envelope.FixtureOnly)
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission
                        .FixtureCodecRejected,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }
            if (envelope.Authentication.Scheme
                != instance.AuthenticationScheme)
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission.ProfileRejected,
                    ProviderNeutralOneShotProfileAdmission
                        .AuthenticationMismatch,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }

            var destination = BuildDestination(
                instance.BaseUri,
                envelope.RelativePath);
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                destination);
            _connectedAddresses.Bind(httpRequest, connectedResult.Plan);
            foreach (var header in envelope.Authentication.PublicStaticHeaders)
            {
                if (!httpRequest.Headers.TryAddWithoutValidation(
                        header.Key,
                        header.Value))
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission.ProfileRejected,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission);
                }
            }
            if (!TryApplyCredential(
                    instance,
                    envelope.Authentication,
                    credentialLease,
                    httpRequest,
                    credentialNow))
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission
                        .CredentialRejected,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission);
            }
            httpRequest.Content = new StringContent(
                envelope.Json,
                Encoding.UTF8,
                "application/json");

            var networkStarted = false;
            try
            {
                var sendNow = _clock();
                if (sendNow < credentialNow
                    || sendNow >= binding.EffectiveDeadline)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission.Timeout,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission);
                }
                using var timeout = new CancellationTokenSource(
                    binding.EffectiveDeadline - sendNow);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);
                linked.Token.ThrowIfCancellationRequested();
                networkStarted = true;
                _lifecycle?.OnProviderSendStarting(request);
                using var response = await _http.SendAsync(
                        httpRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token)
                    .ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri != null
                    && !SameAbsoluteUri(
                        response.RequestMessage.RequestUri,
                        destination))
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission
                            .RedirectRejected,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission
                            .RedirectRejected,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }
                if (!response.IsSuccessStatusCode)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission.HttpRejected,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }
                if (response.Content.Headers.ContentLength.HasValue
                    && response.Content.Headers.ContentLength.Value
                        > MaximumResponseBytes)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission
                            .ResponseTooLarge,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }

                byte[]? responseBytes = null;
                try
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(linked.Token)
                        .ConfigureAwait(false);
                    responseBytes = await ReadBoundedAsync(
                            stream,
                            MaximumResponseBytes,
                            linked.Token)
                        .ConfigureAwait(false);
                    var responseJson = new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false,
                            throwOnInvalidBytes: true)
                        .GetString(responseBytes);
                    var responseAdmission = codec.TryParse(
                        codecRequest,
                        responseJson,
                        out var completion);
                    if (responseAdmission
                            != ProviderProtocolCodecAdmission.Parsed
                        || completion == null)
                    {
                        return Reject(
                            ProviderNeutralOneShotExecutionAdmission
                                .ResponseRejected,
                            profileAdmission,
                            profile.ProfileFingerprint,
                            ticketAdmission,
                            networkCalls: 1);
                    }

                    var completedAt = _clock();
                    var completionIdentity =
                        new ProviderExecutionCompletionIdentity(
                            ticket,
                            activeRuntime,
                            registry.RegistryGeneration,
                            instance.ConfigurationGeneration,
                            completion.TextHash,
                            completedAt);
                    var completionResult = _completions.TryAdmit(
                        ticket,
                        binding,
                        completionIdentity,
                        completedAt);
                    if (!completionResult.Accepted)
                    {
                        return Reject(
                            ProviderNeutralOneShotExecutionAdmission
                                .CompletionRejected,
                            profileAdmission,
                            profile.ProfileFingerprint,
                            ticketAdmission,
                            completionResult.Admission,
                            networkCalls: 1);
                    }
                    return new ProviderNeutralOneShotExecutionResult(
                        ProviderNeutralOneShotExecutionAdmission
                            .CompletedForSourceVerification,
                        profileAdmission,
                        ticketAdmission,
                        completionResult.Admission,
                        completion,
                        completionIdentity,
                        profile.ProfileFingerprint,
                        networkCalls: 1);
                }
                catch (InvalidDataException)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission
                            .ResponseTooLarge,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }
                catch (DecoderFallbackException)
                {
                    return Reject(
                        ProviderNeutralOneShotExecutionAdmission
                            .ResponseRejected,
                        profileAdmission,
                        profile.ProfileFingerprint,
                        ticketAdmission,
                        networkCalls: 1);
                }
                finally
                {
                    if (responseBytes != null)
                        Array.Clear(responseBytes, 0, responseBytes.Length);
                }
            }
            catch (OperationCanceledException)
            {
                return Reject(
                    cancellationToken.IsCancellationRequested
                        ? ProviderNeutralOneShotExecutionAdmission.Cancelled
                        : ProviderNeutralOneShotExecutionAdmission.Timeout,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission,
                    networkCalls: networkStarted ? 1 : 0);
            }
            catch (HttpRequestException)
            {
                return Reject(
                    ProviderNeutralOneShotExecutionAdmission.NetworkFailure,
                    profileAdmission,
                    profile.ProfileFingerprint,
                    ticketAdmission,
                    networkCalls: networkStarted ? 1 : 0);
            }
        }
    }

    private static bool MatchesRoute(
        ProviderExecutionBinding binding,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        ProviderEndpointInstance instance,
        ProviderModelDescriptor model)
        => binding.RegistryGeneration == registry.RegistryGeneration
            && string.Equals(
                binding.RegistryFingerprint,
                registry.Fingerprint,
                StringComparison.Ordinal)
            && route.RegistryGeneration == registry.RegistryGeneration
            && string.Equals(
                route.RegistryFingerprint,
                registry.Fingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                binding.RouteFingerprint,
                route.RouteFingerprint,
                StringComparison.Ordinal)
            && binding.InstanceId.Equals(instance.InstanceId)
            && binding.ModelInstanceId.Equals(model.ModelInstanceId)
            && binding.Protocol == instance.Protocol
            && binding.ProtocolProfileVersion
                == instance.ProtocolProfileVersion
            && binding.ConfigurationGeneration
                == instance.ConfigurationGeneration
            && string.Equals(
                binding.EndpointFingerprint,
                instance.EndpointFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                binding.ModelDescriptorFingerprint,
                model.Fingerprint,
                StringComparison.Ordinal)
            && route.Kind == instance.Kind
            && route.InstanceId.Equals(instance.InstanceId)
            && route.ModelInstanceId.Equals(model.ModelInstanceId)
            && string.Equals(
                route.ModelDescriptorFingerprint,
                model.Fingerprint,
                StringComparison.Ordinal);

    private static bool MatchesPrepared(
        ProviderExecutionBinding binding,
        PreparedProviderExecutionRequest request)
        => string.Equals(
                binding.SourceRequestFingerprint,
                request.SourceRequestFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                binding.ExecutionFingerprint,
                request.ExecutionFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                binding.PromptFingerprint,
                request.SystemInstructionHash,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                binding.SourceRouteCorrelation,
                request.ProviderRouteId,
                StringComparison.Ordinal)
            && binding.Destination == request.Destination;

    private static bool TryApplyCredential(
        ProviderEndpointInstance instance,
        ProviderProtocolAuthenticationProfile authentication,
        CredentialLease? lease,
        HttpRequestMessage request,
        DateTimeOffset now)
    {
        if (instance.AuthenticationScheme == ProviderAuthenticationScheme.None)
        {
            return lease == null
                && authentication.SecretHeaderNames.Count == 0;
        }
        if (lease == null
            || instance.CredentialReference == null
            || !lease.InstanceId.Equals(instance.InstanceId)
            || !lease.ReferenceId.Equals(
                instance.CredentialReference.ReferenceId)
            || lease.AuthenticationScheme != instance.AuthenticationScheme
            || !string.Equals(
                lease.EndpointFingerprint,
                instance.EndpointFingerprint,
                StringComparison.Ordinal)
            || lease.ConfigurationGeneration
                != instance.ConfigurationGeneration)
        {
            return false;
        }

        return lease.TryConsumeOnce(
            now,
            value =>
            {
                var secret = new string(value);
                switch (instance.AuthenticationScheme)
                {
                    case ProviderAuthenticationScheme.GoogleApiKey:
                        return request.Headers.TryAddWithoutValidation(
                            "x-goog-api-key",
                            secret);
                    case ProviderAuthenticationScheme.Bearer:
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", secret);
                        return true;
                    case ProviderAuthenticationScheme.AnthropicApiKey:
                        return request.Headers.TryAddWithoutValidation(
                            "x-api-key",
                            secret);
                    default:
                        return false;
                }
            });
    }

    private static Uri BuildDestination(
        ProviderEndpointUri endpoint,
        string relativePath)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));
        var path = relativePath.StartsWith("/", StringComparison.Ordinal)
            ? relativePath
            : "/" + relativePath;
        return new Uri(endpoint.Origin + path, UriKind.Absolute);
    }

    private static bool SameAbsoluteUri(Uri left, Uri right)
        => string.Equals(
            left.AbsoluteUri,
            right.AbsoluteUri,
            StringComparison.Ordinal);

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                    break;
                if (output.Length + count > maximumBytes)
                    throw new InvalidDataException("Provider response exceeded the hard byte limit.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }

    private static DateTimeOffset Min(
        DateTimeOffset first,
        DateTimeOffset second)
        => first <= second ? first : second;

    private static ProviderNeutralOneShotExecutionResult Reject(
        ProviderNeutralOneShotExecutionAdmission admission,
        ProviderNeutralOneShotProfileAdmission profileAdmission =
            ProviderNeutralOneShotProfileAdmission.NotEvaluated,
        string? profileFingerprint = null,
        ProviderExecutionTicketConsumptionAdmission? ticketAdmission = null,
        ProviderExecutionCompletionAdmission? completionAdmission = null,
        int networkCalls = 0)
        => new(
            admission,
            profileAdmission,
            ticketAdmission,
            completionAdmission,
            null,
            null,
            profileFingerprint,
            networkCalls);
}

internal sealed class ProviderNeutralOneShotAuthorityCounters
{
    internal int PromptConstructions => 0;
    internal int MemoryReads => 0;
    internal int CharacterReads => 0;
    internal int CharacterWrites => 0;
    internal int RelationshipWrites => 0;
    internal int DecisionWrites => 0;
    internal int ToolCalls => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int PersistenceWrites => 0;
    internal int CompletionDeliveries => 0;
    internal int AutomaticRetries => 0;
    internal int ProviderFallbacks => 0;
    internal bool IsZero => true;
}
