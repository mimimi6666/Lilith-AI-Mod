using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace LilithProviderControlPlane;

internal enum ConfiguredEndpointRouteKind
{
    Invalid = 0,
    ManagedCloudHttps = 1,
    LoopbackOnly = 2,
    ExplicitLan = 3,
    ExplicitRemoteHttps = 4
}

internal enum ConfiguredEndpointExposure
{
    Invalid = 0,
    ManagedCloud = 1,
    LoopbackOnly = 2,
    LanExposedDeclared = 3,
    PublicExposedDeclared = 4
}

internal enum ConfiguredEndpointTrust
{
    Invalid = 0,
    OfficialCloudHttps = 1,
    LoopbackRouteAdmitted = 2,
    ExplicitLan = 3,
    ExplicitRemoteCustom = 4
}

internal enum ConfiguredEndpointTransportProtection
{
    Invalid = 0,
    HttpsRequired = 1,
    LoopbackHttpPermitted = 2
}

/// <summary>
/// Immutable player-selected route policy.  This is configuration intent,
/// never proof that the current DNS resolution or connected address is safe.
/// LAN and remote routes therefore remain pending until a transport supplies
/// fresh connected-address evidence for the same configuration generation.
/// </summary>
internal sealed class ConfiguredEndpointRouteIntent
{
    internal ConfiguredEndpointRouteKind Kind { get; }
    internal ConfiguredEndpointExposure Exposure { get; }
    internal ConfiguredEndpointTrust Trust { get; }
    internal ConfiguredEndpointTransportProtection TransportProtection { get; }
    internal string Fingerprint { get; }

    internal bool RequiresSendTimeConnectedAddressEvidence =>
        Kind is ConfiguredEndpointRouteKind.ManagedCloudHttps
            or ConfiguredEndpointRouteKind.ExplicitLan
            or ConfiguredEndpointRouteKind.ExplicitRemoteHttps;

    private ConfiguredEndpointRouteIntent(
        ConfiguredEndpointRouteKind kind,
        ConfiguredEndpointExposure exposure,
        ConfiguredEndpointTrust trust,
        ConfiguredEndpointTransportProtection transportProtection)
    {
        Kind = Defined(kind, nameof(kind));
        Exposure = Defined(exposure, nameof(exposure));
        Trust = Defined(trust, nameof(trust));
        TransportProtection = Defined(
            transportProtection,
            nameof(transportProtection));
        if (!CanonicalCombination(kind, exposure, trust, transportProtection))
        {
            throw new ArgumentException(
                "Endpoint route intent fields must form one canonical policy.",
                nameof(kind));
        }
        Fingerprint = UnifiedProviderConfiguredInstance.Hash(
            "D033/CONFIGURED-ENDPOINT-ROUTE-INTENT/1",
            ((int)Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)Exposure).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)Trust).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)TransportProtection).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static ConfiguredEndpointRouteIntent ManagedCloudHttps()
        => new(
            ConfiguredEndpointRouteKind.ManagedCloudHttps,
            ConfiguredEndpointExposure.ManagedCloud,
            ConfiguredEndpointTrust.OfficialCloudHttps,
            ConfiguredEndpointTransportProtection.HttpsRequired);

    internal static ConfiguredEndpointRouteIntent LoopbackOnly()
        => new(
            ConfiguredEndpointRouteKind.LoopbackOnly,
            ConfiguredEndpointExposure.LoopbackOnly,
            ConfiguredEndpointTrust.LoopbackRouteAdmitted,
            ConfiguredEndpointTransportProtection.LoopbackHttpPermitted);

    internal static ConfiguredEndpointRouteIntent ExplicitLan()
        => new(
            ConfiguredEndpointRouteKind.ExplicitLan,
            ConfiguredEndpointExposure.LanExposedDeclared,
            ConfiguredEndpointTrust.ExplicitLan,
            ConfiguredEndpointTransportProtection.HttpsRequired);

    internal static ConfiguredEndpointRouteIntent ExplicitRemoteHttps()
        => new(
            ConfiguredEndpointRouteKind.ExplicitRemoteHttps,
            ConfiguredEndpointExposure.PublicExposedDeclared,
            ConfiguredEndpointTrust.ExplicitRemoteCustom,
            ConfiguredEndpointTransportProtection.HttpsRequired);

    internal static ConfiguredEndpointRouteIntent FromPersisted(
        int kind,
        int exposure,
        int trust,
        int transportProtection)
        => new(
            (ConfiguredEndpointRouteKind)kind,
            (ConfiguredEndpointExposure)exposure,
            (ConfiguredEndpointTrust)trust,
            (ConfiguredEndpointTransportProtection)transportProtection);

    internal static ConfiguredEndpointRouteIntent SafeCompatibilityDefault(
        ProviderControlKind provider,
        Uri baseUri)
    {
        if (baseUri == null)
            throw new ArgumentNullException(nameof(baseUri));
        if (provider is ProviderControlKind.DeepSeek
            or ProviderControlKind.GoogleGemini
            or ProviderControlKind.OpenAI
            or ProviderControlKind.QwenDashScope
            or ProviderControlKind.AnthropicClaude
            or ProviderControlKind.OpenRouter
            or ProviderControlKind.OllamaCloud)
        {
            return ManagedCloudHttps();
        }
        if (provider == ProviderControlKind.CustomOpenAICompatible
            && !baseUri.IsLoopback
            && baseUri.Scheme == Uri.UriSchemeHttps)
        {
            // Compatibility for the pre-route Custom setup surface: entering
            // a custom HTTPS base address was already an explicit remote
            // declaration. Named self-hosted providers do not inherit this.
            return ExplicitRemoteHttps();
        }
        return LoopbackOnly();
    }

    private static bool CanonicalCombination(
        ConfiguredEndpointRouteKind kind,
        ConfiguredEndpointExposure exposure,
        ConfiguredEndpointTrust trust,
        ConfiguredEndpointTransportProtection transportProtection)
        => kind switch
        {
            ConfiguredEndpointRouteKind.ManagedCloudHttps =>
                exposure == ConfiguredEndpointExposure.ManagedCloud
                && trust == ConfiguredEndpointTrust.OfficialCloudHttps
                && transportProtection
                    == ConfiguredEndpointTransportProtection.HttpsRequired,
            ConfiguredEndpointRouteKind.LoopbackOnly =>
                exposure == ConfiguredEndpointExposure.LoopbackOnly
                && trust == ConfiguredEndpointTrust.LoopbackRouteAdmitted
                && transportProtection
                    == ConfiguredEndpointTransportProtection.LoopbackHttpPermitted,
            ConfiguredEndpointRouteKind.ExplicitLan =>
                exposure == ConfiguredEndpointExposure.LanExposedDeclared
                && trust == ConfiguredEndpointTrust.ExplicitLan
                && transportProtection
                    == ConfiguredEndpointTransportProtection.HttpsRequired,
            ConfiguredEndpointRouteKind.ExplicitRemoteHttps =>
                exposure == ConfiguredEndpointExposure.PublicExposedDeclared
                && trust == ConfiguredEndpointTrust.ExplicitRemoteCustom
                && transportProtection
                    == ConfiguredEndpointTransportProtection.HttpsRequired,
            _ => false
        };

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal enum UnifiedProviderEndpointAdmission
{
    Invalid = 0,
    Rejected = 1,
    AcceptedForNetwork = 2,
    RequiresConnectedAddressEvidence = 3
}

internal enum UnifiedProviderEndpointAdmissionReason
{
    Invalid = 0,
    InvalidProvider = 1,
    MissingOrInvalidUri = 2,
    UnsupportedScheme = 3,
    UserInfoRejected = 4,
    QueryRejected = 5,
    FragmentRejected = 6,
    InvalidPort = 7,
    InvalidHost = 8,
    UnexpectedEndpointProfile = 9,
    UnexpectedWorkspace = 10,
    CanonicalEndpointMismatch = 11,
    EndpointProfileRequired = 12,
    EndpointProfileUnknown = 13,
    WorkspaceRequired = 14,
    WorkspaceForbidden = 15,
    WorkspaceInvalid = 16,
    LocalEndpointMustBeLoopback = 17,
    HttpEndpointMustBeLoopback = 18,
    UnsafeAddressScope = 19,
    LoopbackAccepted = 20,
    CanonicalCloudEndpointRequiresConnectedAddressEvidence = 21,
    QwenEndpointRequiresConnectedAddressEvidence = 22,
    CustomHttpsDnsRequiresConnectedAddressEvidence = 23,
    CustomHttpsPublicAddressRequiresConnectedAddressEvidence = 24,
    RouteIntentMismatch = 25,
    LanHttpsRequiresConnectedAddressEvidence = 26,
    RemoteHttpsRequiresConnectedAddressEvidence = 27
}

internal enum UnifiedProviderEndpointAddressFamily
{
    Invalid = 0,
    Unknown = 1,
    DnsName = 2,
    IPv4 = 3,
    IPv6 = 4,
    IPv4MappedToIPv6 = 5
}

internal enum UnifiedProviderEndpointAddressScope
{
    Invalid = 0,
    Unknown = 1,
    UnresolvedDns = 2,
    Loopback = 3,
    PrivateNetwork = 4,
    Public = 5,
    LinkLocal = 6,
    Metadata = 7,
    Unspecified = 8,
    Multicast = 9,
    Broadcast = 10,
    Reserved = 11
}

internal sealed class UnifiedProviderEndpointAdmissionResult
{
    internal UnifiedProviderEndpointAdmission Admission { get; }
    internal UnifiedProviderEndpointAdmissionReason Reason { get; }
    internal UnifiedProviderEndpointAddressFamily AddressFamily { get; }
    internal UnifiedProviderEndpointAddressScope AddressScope { get; }
    internal Uri? NormalizedEndpoint { get; }

    internal bool Accepted =>
        Admission == UnifiedProviderEndpointAdmission.AcceptedForNetwork;

    internal bool RequiresSendTimeConnectedAddressEvidence =>
        Admission
        == UnifiedProviderEndpointAdmission.RequiresConnectedAddressEvidence;

    private UnifiedProviderEndpointAdmissionResult(
        UnifiedProviderEndpointAdmission admission,
        UnifiedProviderEndpointAdmissionReason reason,
        UnifiedProviderEndpointAddressFamily addressFamily,
        UnifiedProviderEndpointAddressScope addressScope,
        Uri? normalizedEndpoint)
    {
        Admission = Defined(admission, nameof(admission));
        Reason = Defined(reason, nameof(reason));
        AddressFamily = Defined(addressFamily, nameof(addressFamily));
        AddressScope = Defined(addressScope, nameof(addressScope));
        NormalizedEndpoint = normalizedEndpoint;

        if (admission == UnifiedProviderEndpointAdmission.AcceptedForNetwork
            && (addressScope != UnifiedProviderEndpointAddressScope.Loopback
                || normalizedEndpoint == null))
        {
            throw new ArgumentException(
                "Pure endpoint policy may directly accept only loopback.",
                nameof(admission));
        }

        if (admission
                == UnifiedProviderEndpointAdmission
                    .RequiresConnectedAddressEvidence
            && (normalizedEndpoint == null
                || addressScope is not (
                    UnifiedProviderEndpointAddressScope.Public
                    or UnifiedProviderEndpointAddressScope.UnresolvedDns
                    or UnifiedProviderEndpointAddressScope.PrivateNetwork)))
        {
            throw new ArgumentException(
                "Connected-address admission requires a bounded remote endpoint.",
                nameof(admission));
        }

        if (admission == UnifiedProviderEndpointAdmission.Rejected
            && normalizedEndpoint != null)
        {
            throw new ArgumentException(
                "Rejected endpoints must not be retained.",
                nameof(normalizedEndpoint));
        }
    }

    internal static UnifiedProviderEndpointAdmissionResult Reject(
        UnifiedProviderEndpointAdmissionReason reason,
        UnifiedProviderEndpointAddressFamily family,
        UnifiedProviderEndpointAddressScope scope)
        => new(
            UnifiedProviderEndpointAdmission.Rejected,
            reason,
            family,
            scope,
            null);

    internal static UnifiedProviderEndpointAdmissionResult AcceptLoopback(
        Uri endpoint,
        UnifiedProviderEndpointAddressFamily family)
        => new(
            UnifiedProviderEndpointAdmission.AcceptedForNetwork,
            UnifiedProviderEndpointAdmissionReason.LoopbackAccepted,
            family,
            UnifiedProviderEndpointAddressScope.Loopback,
            endpoint);

    internal static UnifiedProviderEndpointAdmissionResult RequireEvidence(
        UnifiedProviderEndpointAdmissionReason reason,
        Uri endpoint,
        UnifiedProviderEndpointAddressFamily family,
        UnifiedProviderEndpointAddressScope scope)
        => new(
            UnifiedProviderEndpointAdmission.RequiresConnectedAddressEvidence,
            reason,
            family,
            scope,
            endpoint);

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

/// <summary>
/// Pure, source-only admission for configured Provider base endpoints.
/// This policy performs no DNS lookup and cannot prove that a remote DNS name
/// still resolves to an admitted address when a request is sent. A caller must
/// independently bind and verify the connected address at send time whenever
/// <see cref="UnifiedProviderEndpointAdmission.RequiresConnectedAddressEvidence" />
/// is returned.
/// </summary>
internal static class UnifiedProviderEndpointAdmissionPolicy
{
    private static readonly ProviderControlKind[] FixedCloudProviders =
    {
        ProviderControlKind.DeepSeek,
        ProviderControlKind.GoogleGemini,
        ProviderControlKind.OpenAI,
        ProviderControlKind.AnthropicClaude,
        ProviderControlKind.OpenRouter,
        ProviderControlKind.OllamaCloud
    };

    private static readonly ProviderControlKind[] LocalProviders =
    {
        ProviderControlKind.OllamaLocal,
        ProviderControlKind.LMStudio,
        ProviderControlKind.LlamaCpp,
        ProviderControlKind.Vllm,
        ProviderControlKind.LocalAI
    };

    internal static UnifiedProviderEndpointAdmissionResult Evaluate(
        ProviderControlKind providerKind,
        Uri? candidateBaseUri,
        string? endpointProfileId = null,
        string? workspaceId = null)
    {
        if (!UnifiedProviderProfileRegistry.TryGet(providerKind, out var profile)
            || profile == null)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.InvalidProvider,
                UnifiedProviderEndpointAddressFamily.Unknown,
                UnifiedProviderEndpointAddressScope.Unknown);
        }

        var common = ValidateAndNormalize(candidateBaseUri);
        if (common.Result != null)
            return common.Result;

        var endpoint = common.Endpoint!;
        var classification = common.Classification;
        var normalizedProfileId = endpointProfileId?.Trim() ?? string.Empty;
        var normalizedWorkspace = workspaceId?.Trim() ?? string.Empty;

        if (FixedCloudProviders.Contains(providerKind))
        {
            if (normalizedProfileId.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason
                        .UnexpectedEndpointProfile,
                    classification.Family,
                    classification.Scope);
            }
            if (normalizedWorkspace.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason.UnexpectedWorkspace,
                    classification.Family,
                    classification.Scope);
            }
            if (profile.DefaultBaseUri == null
                || !EquivalentBaseEndpoint(endpoint, profile.DefaultBaseUri))
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason
                        .CanonicalEndpointMismatch,
                    classification.Family,
                    classification.Scope);
            }
            return RequireRemoteEvidence(
                UnifiedProviderEndpointAdmissionReason
                    .CanonicalCloudEndpointRequiresConnectedAddressEvidence,
                endpoint,
                classification);
        }

        if (providerKind == ProviderControlKind.QwenDashScope)
        {
            return EvaluateQwen(
                profile,
                endpoint,
                normalizedProfileId,
                normalizedWorkspace,
                classification);
        }

        if (LocalProviders.Contains(providerKind))
        {
            if (normalizedProfileId.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason
                        .UnexpectedEndpointProfile,
                    classification.Family,
                    classification.Scope);
            }
            if (normalizedWorkspace.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason.UnexpectedWorkspace,
                    classification.Family,
                    classification.Scope);
            }
            return classification.Scope
                == UnifiedProviderEndpointAddressScope.Loopback
                    ? UnifiedProviderEndpointAdmissionResult.AcceptLoopback(
                        endpoint,
                        classification.Family)
                    : Reject(
                        UnifiedProviderEndpointAdmissionReason
                            .LocalEndpointMustBeLoopback,
                        classification.Family,
                        classification.Scope);
        }

        if (providerKind == ProviderControlKind.CustomOpenAICompatible)
        {
            if (normalizedProfileId.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason
                        .UnexpectedEndpointProfile,
                    classification.Family,
                    classification.Scope);
            }
            if (normalizedWorkspace.Length != 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason.UnexpectedWorkspace,
                    classification.Family,
                    classification.Scope);
            }
            return EvaluateCustom(endpoint, classification);
        }

        return Reject(
            UnifiedProviderEndpointAdmissionReason.InvalidProvider,
            classification.Family,
            classification.Scope);
    }

    /// <summary>
    /// Evaluates an endpoint against an explicit configured route.  The route
    /// is player-authored policy, not connected-address evidence.  LAN and
    /// remote HTTPS therefore remain pending and fail closed at this source
    /// boundary until the send-time transport can bind fresh evidence.
    /// </summary>
    internal static UnifiedProviderEndpointAdmissionResult Evaluate(
        ProviderControlKind providerKind,
        Uri? candidateBaseUri,
        ConfiguredEndpointRouteIntent routeIntent,
        string? endpointProfileId = null,
        string? workspaceId = null)
    {
        if (routeIntent == null)
            throw new ArgumentNullException(nameof(routeIntent));

        if (!UnifiedProviderProfileRegistry.TryGet(providerKind, out var profile)
            || profile == null)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.InvalidProvider,
                UnifiedProviderEndpointAddressFamily.Unknown,
                UnifiedProviderEndpointAddressScope.Unknown);
        }

        var managed = FixedCloudProviders.Contains(providerKind)
            || providerKind == ProviderControlKind.QwenDashScope;
        var selfHosted = LocalProviders.Contains(providerKind)
            || providerKind == ProviderControlKind.CustomOpenAICompatible;
        if ((routeIntent.Kind == ConfiguredEndpointRouteKind.ManagedCloudHttps
                && !managed)
            || (routeIntent.Kind != ConfiguredEndpointRouteKind.ManagedCloudHttps
                && !selfHosted))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.RouteIntentMismatch,
                UnifiedProviderEndpointAddressFamily.Unknown,
                UnifiedProviderEndpointAddressScope.Unknown);
        }

        if (routeIntent.Kind == ConfiguredEndpointRouteKind.ManagedCloudHttps)
        {
            return Evaluate(
                providerKind,
                candidateBaseUri,
                endpointProfileId,
                workspaceId);
        }

        var common = ValidateAndNormalize(candidateBaseUri);
        if (common.Result != null)
            return common.Result;
        var endpoint = common.Endpoint!;
        var classification = common.Classification;
        if (!string.IsNullOrWhiteSpace(endpointProfileId))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.UnexpectedEndpointProfile,
                classification.Family,
                classification.Scope);
        }
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.UnexpectedWorkspace,
                classification.Family,
                classification.Scope);
        }

        return routeIntent.Kind switch
        {
            ConfiguredEndpointRouteKind.LoopbackOnly =>
                classification.Scope == UnifiedProviderEndpointAddressScope.Loopback
                    ? UnifiedProviderEndpointAdmissionResult.AcceptLoopback(
                        endpoint,
                        classification.Family)
                    : Reject(
                        UnifiedProviderEndpointAdmissionReason
                            .LocalEndpointMustBeLoopback,
                        classification.Family,
                        classification.Scope),
            ConfiguredEndpointRouteKind.ExplicitLan => EvaluateExplicitLan(
                endpoint,
                classification),
            ConfiguredEndpointRouteKind.ExplicitRemoteHttps =>
                EvaluateExplicitRemote(endpoint, classification),
            _ => Reject(
                UnifiedProviderEndpointAdmissionReason.RouteIntentMismatch,
                classification.Family,
                classification.Scope)
        };
    }

    private static UnifiedProviderEndpointAdmissionResult EvaluateExplicitLan(
        Uri endpoint,
        EndpointClassification classification)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.HttpEndpointMustBeLoopback,
                classification.Family,
                classification.Scope);
        }
        if (classification.Scope is not (
                UnifiedProviderEndpointAddressScope.PrivateNetwork
                or UnifiedProviderEndpointAddressScope.UnresolvedDns))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.UnsafeAddressScope,
                classification.Family,
                classification.Scope);
        }
        return UnifiedProviderEndpointAdmissionResult.RequireEvidence(
            UnifiedProviderEndpointAdmissionReason
                .LanHttpsRequiresConnectedAddressEvidence,
            endpoint,
            classification.Family,
            classification.Scope);
    }

    private static UnifiedProviderEndpointAdmissionResult EvaluateExplicitRemote(
        Uri endpoint,
        EndpointClassification classification)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.HttpEndpointMustBeLoopback,
                classification.Family,
                classification.Scope);
        }
        if (classification.Scope is not (
                UnifiedProviderEndpointAddressScope.Public
                or UnifiedProviderEndpointAddressScope.UnresolvedDns))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.UnsafeAddressScope,
                classification.Family,
                classification.Scope);
        }
        return UnifiedProviderEndpointAdmissionResult.RequireEvidence(
            UnifiedProviderEndpointAdmissionReason
                .RemoteHttpsRequiresConnectedAddressEvidence,
            endpoint,
            classification.Family,
            classification.Scope);
    }

    private static UnifiedProviderEndpointAdmissionResult EvaluateQwen(
        ProviderControlProfile profile,
        Uri endpoint,
        string endpointProfileId,
        string workspaceId,
        EndpointClassification classification)
    {
        if (endpointProfileId.Length == 0)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.EndpointProfileRequired,
                classification.Family,
                classification.Scope);
        }

        var endpointProfile = profile.EndpointProfiles.SingleOrDefault(value =>
            string.Equals(
                value.ProfileId,
                endpointProfileId,
                StringComparison.Ordinal));
        if (endpointProfile == null)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.EndpointProfileUnknown,
                classification.Family,
                classification.Scope);
        }

        if (endpointProfile.RequiresWorkspaceId)
        {
            if (workspaceId.Length == 0)
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason.WorkspaceRequired,
                    classification.Family,
                    classification.Scope);
            }
            if (!IsDnsLabel(workspaceId))
            {
                return Reject(
                    UnifiedProviderEndpointAdmissionReason.WorkspaceInvalid,
                    classification.Family,
                    classification.Scope);
            }
        }
        else if (workspaceId.Length != 0)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.WorkspaceForbidden,
                classification.Family,
                classification.Scope);
        }

        var expectedText = endpointProfile.BaseAddressTemplate.Replace(
            ProviderEndpointProfile.WorkspacePlaceholder,
            workspaceId,
            StringComparison.Ordinal);
        var expected = new Uri(expectedText, UriKind.Absolute);
        if (!EquivalentBaseEndpoint(endpoint, expected))
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.CanonicalEndpointMismatch,
                classification.Family,
                classification.Scope);
        }

        return RequireRemoteEvidence(
            UnifiedProviderEndpointAdmissionReason
                .QwenEndpointRequiresConnectedAddressEvidence,
            endpoint,
            classification);
    }

    private static UnifiedProviderEndpointAdmissionResult EvaluateCustom(
        Uri endpoint,
        EndpointClassification classification)
    {
        if (classification.Scope
            == UnifiedProviderEndpointAddressScope.Loopback)
        {
            return UnifiedProviderEndpointAdmissionResult.AcceptLoopback(
                endpoint,
                classification.Family);
        }

        if (endpoint.Scheme == Uri.UriSchemeHttp)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason
                    .HttpEndpointMustBeLoopback,
                classification.Family,
                classification.Scope);
        }

        if (classification.Family
                == UnifiedProviderEndpointAddressFamily.DnsName
            && classification.Scope
                == UnifiedProviderEndpointAddressScope.UnresolvedDns)
        {
            return UnifiedProviderEndpointAdmissionResult.RequireEvidence(
                UnifiedProviderEndpointAdmissionReason
                    .CustomHttpsDnsRequiresConnectedAddressEvidence,
                endpoint,
                classification.Family,
                classification.Scope);
        }

        if (classification.Scope == UnifiedProviderEndpointAddressScope.Public)
        {
            return UnifiedProviderEndpointAdmissionResult.RequireEvidence(
                UnifiedProviderEndpointAdmissionReason
                    .CustomHttpsPublicAddressRequiresConnectedAddressEvidence,
                endpoint,
                classification.Family,
                classification.Scope);
        }

        return Reject(
            UnifiedProviderEndpointAdmissionReason.UnsafeAddressScope,
            classification.Family,
            classification.Scope);
    }

    private static UnifiedProviderEndpointAdmissionResult RequireRemoteEvidence(
        UnifiedProviderEndpointAdmissionReason reason,
        Uri endpoint,
        EndpointClassification classification)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps
            || classification.Family
                != UnifiedProviderEndpointAddressFamily.DnsName
            || classification.Scope
                != UnifiedProviderEndpointAddressScope.UnresolvedDns)
        {
            return Reject(
                UnifiedProviderEndpointAdmissionReason.UnsafeAddressScope,
                classification.Family,
                classification.Scope);
        }
        return UnifiedProviderEndpointAdmissionResult.RequireEvidence(
            reason,
            endpoint,
            classification.Family,
            classification.Scope);
    }

    private static (
        Uri? Endpoint,
        EndpointClassification Classification,
        UnifiedProviderEndpointAdmissionResult? Result) ValidateAndNormalize(
        Uri? candidate)
    {
        if (candidate == null
            || !candidate.IsAbsoluteUri
            || candidate.AbsoluteUri.Length > 1024
            || string.IsNullOrWhiteSpace(candidate.Host))
        {
            return InvalidUri(
                UnifiedProviderEndpointAdmissionReason.MissingOrInvalidUri);
        }

        if (candidate.Scheme is not ("http" or "https"))
            return InvalidUri(UnifiedProviderEndpointAdmissionReason.UnsupportedScheme);
        if (!string.IsNullOrEmpty(candidate.UserInfo))
            return InvalidUri(UnifiedProviderEndpointAdmissionReason.UserInfoRejected);
        if (candidate.OriginalString.Contains('?')
            || !string.IsNullOrEmpty(candidate.Query))
        {
            return InvalidUri(UnifiedProviderEndpointAdmissionReason.QueryRejected);
        }
        if (candidate.OriginalString.Contains('#')
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return InvalidUri(UnifiedProviderEndpointAdmissionReason.FragmentRejected);
        }
        if (candidate.Port <= 0 || candidate.Port > 65535)
            return InvalidUri(UnifiedProviderEndpointAdmissionReason.InvalidPort);

        var normalized = new Uri(
            candidate.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/",
            UriKind.Absolute);
        var classification = Classify(normalized);
        if (classification.Family
                == UnifiedProviderEndpointAddressFamily.Invalid
            || classification.Scope
                == UnifiedProviderEndpointAddressScope.Invalid)
        {
            return (
                null,
                classification,
                Reject(
                    UnifiedProviderEndpointAdmissionReason.InvalidHost,
                    UnifiedProviderEndpointAddressFamily.Unknown,
                    UnifiedProviderEndpointAddressScope.Unknown));
        }
        return (normalized, classification, null);
    }

    private static (
        Uri? Endpoint,
        EndpointClassification Classification,
        UnifiedProviderEndpointAdmissionResult Result) InvalidUri(
        UnifiedProviderEndpointAdmissionReason reason)
    {
        var classification = new EndpointClassification(
            UnifiedProviderEndpointAddressFamily.Unknown,
            UnifiedProviderEndpointAddressScope.Unknown);
        return (
            null,
            classification,
            Reject(reason, classification.Family, classification.Scope));
    }

    private static EndpointClassification Classify(Uri endpoint)
    {
        var host = endpoint.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return new EndpointClassification(
                UnifiedProviderEndpointAddressFamily.DnsName,
                UnifiedProviderEndpointAddressScope.Loopback);
        }
        if (IsKnownMetadataHost(host))
        {
            return new EndpointClassification(
                UnifiedProviderEndpointAddressFamily.DnsName,
                UnifiedProviderEndpointAddressScope.Metadata);
        }
        if (!IPAddress.TryParse(host, out var address))
        {
            return Uri.CheckHostName(host) == UriHostNameType.Dns
                ? new EndpointClassification(
                    UnifiedProviderEndpointAddressFamily.DnsName,
                    UnifiedProviderEndpointAddressScope.UnresolvedDns)
                : new EndpointClassification(
                    UnifiedProviderEndpointAddressFamily.Invalid,
                    UnifiedProviderEndpointAddressScope.Invalid);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return new EndpointClassification(
                UnifiedProviderEndpointAddressFamily.IPv4MappedToIPv6,
                ClassifyIPv4(address.MapToIPv4()));
        }
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return new EndpointClassification(
                UnifiedProviderEndpointAddressFamily.IPv4,
                ClassifyIPv4(address));
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return new EndpointClassification(
                UnifiedProviderEndpointAddressFamily.IPv6,
                ClassifyIPv6(address));
        }
        return new EndpointClassification(
            UnifiedProviderEndpointAddressFamily.Invalid,
            UnifiedProviderEndpointAddressScope.Invalid);
    }

    private static UnifiedProviderEndpointAddressScope ClassifyIPv4(
        IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.SequenceEqual(new byte[] { 0, 0, 0, 0 }))
            return UnifiedProviderEndpointAddressScope.Unspecified;
        if (bytes.SequenceEqual(new byte[] { 255, 255, 255, 255 }))
            return UnifiedProviderEndpointAddressScope.Broadcast;
        if (IPAddress.IsLoopback(address))
            return UnifiedProviderEndpointAddressScope.Loopback;
        if (bytes.SequenceEqual(new byte[] { 169, 254, 169, 254 })
            || bytes.SequenceEqual(new byte[] { 169, 254, 170, 2 })
            || bytes.SequenceEqual(new byte[] { 100, 100, 100, 200 }))
        {
            return UnifiedProviderEndpointAddressScope.Metadata;
        }
        if (bytes[0] == 169 && bytes[1] == 254)
            return UnifiedProviderEndpointAddressScope.LinkLocal;
        if (bytes[0] is >= 224 and <= 239)
            return UnifiedProviderEndpointAddressScope.Multicast;
        if (bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127))
        {
            return UnifiedProviderEndpointAddressScope.PrivateNetwork;
        }
        if (bytes[0] == 0
            || bytes[0] >= 240
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            || (bytes[0] == 198 && bytes[1] is 18 or 19)
            || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113))
        {
            return UnifiedProviderEndpointAddressScope.Reserved;
        }
        return UnifiedProviderEndpointAddressScope.Public;
    }

    private static UnifiedProviderEndpointAddressScope ClassifyIPv6(
        IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6None))
            return UnifiedProviderEndpointAddressScope.Unspecified;
        if (address.Equals(IPAddress.IPv6Loopback))
            return UnifiedProviderEndpointAddressScope.Loopback;
        if (address.Equals(IPAddress.Parse("fd00:ec2::254")))
            return UnifiedProviderEndpointAddressScope.Metadata;
        if (address.IsIPv6LinkLocal)
            return UnifiedProviderEndpointAddressScope.LinkLocal;
        if (address.IsIPv6Multicast)
            return UnifiedProviderEndpointAddressScope.Multicast;
        if (address.IsIPv6SiteLocal
            || (address.GetAddressBytes()[0] & 0xFE) == 0xFC)
        {
            return UnifiedProviderEndpointAddressScope.PrivateNetwork;
        }
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0x20 && bytes[1] == 0x01
            && bytes[2] == 0x0D && bytes[3] == 0xB8)
        {
            return UnifiedProviderEndpointAddressScope.Reserved;
        }
        return UnifiedProviderEndpointAddressScope.Public;
    }

    private static bool EquivalentBaseEndpoint(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port
           && string.Equals(
               NormalizePath(left.AbsolutePath),
               NormalizePath(right.AbsolutePath),
               StringComparison.Ordinal);

    private static string NormalizePath(string path)
        => path.TrimEnd('/') + "/";

    private static bool IsDnsLabel(string value)
        => value.Length is >= 1 and <= 63
           && char.IsLetterOrDigit(value[0])
           && char.IsLetterOrDigit(value[^1])
           && value.All(character => char.IsLetterOrDigit(character)
               || character == '-');

    private static bool IsKnownMetadataHost(string host)
        => string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "metadata.goog", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "instance-data.ec2.internal", StringComparison.OrdinalIgnoreCase);

    private static UnifiedProviderEndpointAdmissionResult Reject(
        UnifiedProviderEndpointAdmissionReason reason,
        UnifiedProviderEndpointAddressFamily family,
        UnifiedProviderEndpointAddressScope scope)
        => UnifiedProviderEndpointAdmissionResult.Reject(reason, family, scope);

    private readonly struct EndpointClassification
    {
        internal UnifiedProviderEndpointAddressFamily Family { get; }
        internal UnifiedProviderEndpointAddressScope Scope { get; }

        internal EndpointClassification(
            UnifiedProviderEndpointAddressFamily family,
            UnifiedProviderEndpointAddressScope scope)
        {
            Family = family;
            Scope = scope;
        }
    }
}
