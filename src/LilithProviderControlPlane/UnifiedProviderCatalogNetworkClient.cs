using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LilithProviderControlPlane;

internal enum ProviderCatalogNetworkAdmission
{
    Invalid = 0,
    Accepted = 1,
    NotConfigured = 2,
    ConfigurationRejected = 3,
    CredentialRequired = 4,
    CredentialMismatch = 5,
    AuthenticationRejected = 6,
    RateLimited = 7,
    ServiceUnavailable = 8,
    CatalogObservationUnsupported = 9,
    RedirectRejected = 10,
    ResponseRejected = 11,
    ResourceLimitExceeded = 12,
    NetworkFailure = 13,
    Timeout = 14,
    Cancelled = 15,
    ConnectedAddressRejected = 16
}

internal enum ProviderCatalogContinuationKind
{
    Invalid = 0,
    None = 1,
    GeminiNextPageToken = 2,
    AnthropicAfterId = 3
}

internal enum ProviderCustomAuthenticationKind
{
    Invalid = 0,
    ProfileDefault = 1,
    None = 2,
    Bearer = 3
}

internal sealed class ProviderCatalogContinuation
{
    internal static ProviderCatalogContinuation None { get; } = new(
        ProviderCatalogContinuationKind.None,
        string.Empty);

    internal ProviderCatalogContinuationKind Kind { get; }
    internal string Value { get; }
    internal bool HasContinuation => Kind != ProviderCatalogContinuationKind.None;

    internal ProviderCatalogContinuation(
        ProviderCatalogContinuationKind kind,
        string? value)
    {
        if (!Enum.IsDefined(typeof(ProviderCatalogContinuationKind), kind)
            || kind == ProviderCatalogContinuationKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        var bounded = value ?? string.Empty;
        if (bounded.Length > UnifiedProviderCatalogAdapters.MaximumContinuationCharacters
            || bounded.Any(char.IsControl))
        {
            throw new ArgumentException("Continuation metadata is invalid.", nameof(value));
        }
        if ((kind == ProviderCatalogContinuationKind.None) != (bounded.Length == 0))
        {
            throw new ArgumentException(
                "Continuation identity and value must agree.",
                nameof(value));
        }
        Kind = kind;
        Value = bounded;
    }
}

internal sealed class ProviderCatalogNetworkTarget
{
    internal ProviderControlKind Provider { get; }
    internal string ProfileId { get; }
    internal string InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal Uri BaseUri { get; }
    internal string EndpointProfileId { get; }
    internal string WorkspaceId { get; }
    internal bool CredentialConfigured { get; }
    internal ProviderCustomAuthenticationKind CustomAuthentication { get; }
    internal ConfiguredEndpointRouteIntent RouteIntent { get; }
    internal string ConfigurationFingerprint { get; }
    internal string EndpointFingerprint { get; }
    internal string AttemptId { get; }

    internal ProviderCatalogNetworkTarget(
        ProviderControlKind provider,
        string profileId,
        string instanceId,
        long configurationGeneration,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        bool credentialConfigured,
        ProviderCustomAuthenticationKind customAuthentication =
            ProviderCustomAuthenticationKind.ProfileDefault)
        : this(
            provider,
            profileId,
            instanceId,
            configurationGeneration,
            baseUri,
            endpointProfileId,
            workspaceId,
            credentialConfigured,
            customAuthentication,
            ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                provider,
                baseUri),
            null,
            null)
    {
    }

    internal ProviderCatalogNetworkTarget(
        ProviderControlKind provider,
        string profileId,
        string instanceId,
        long configurationGeneration,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        bool credentialConfigured,
        ProviderCustomAuthenticationKind customAuthentication,
        ConfiguredEndpointRouteIntent routeIntent,
        string? configurationFingerprint,
        string? attemptId)
    {
        if (!Enum.IsDefined(typeof(ProviderControlKind), provider)
            || provider == ProviderControlKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        if (!Enum.IsDefined(typeof(ProviderCustomAuthenticationKind), customAuthentication)
            || customAuthentication == ProviderCustomAuthenticationKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(customAuthentication));
        }
        ProfileId = UnifiedProviderConfiguredInstance.Identifier(
            profileId,
            64,
            nameof(profileId));
        InstanceId = UnifiedProviderConfiguredInstance.Identifier(
            instanceId,
            96,
            nameof(instanceId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        Provider = provider;
        ConfigurationGeneration = configurationGeneration;
        BaseUri = UnifiedProviderConfiguredInstance.NormalizeBaseUri(
            baseUri,
            nameof(baseUri));
        EndpointProfileId = UnifiedProviderConfiguredInstance.OptionalIdentifier(
            endpointProfileId,
            96,
            nameof(endpointProfileId));
        WorkspaceId = UnifiedProviderConfiguredInstance.OptionalIdentifier(
            workspaceId,
            128,
            nameof(workspaceId));
        CredentialConfigured = credentialConfigured;
        CustomAuthentication = customAuthentication;
        RouteIntent = routeIntent
            ?? throw new ArgumentNullException(nameof(routeIntent));
        var scope = UnifiedProviderConnectedAddressContext.FromRouteIntent(
            RouteIntent);
        ConfigurationFingerprint = string.IsNullOrWhiteSpace(configurationFingerprint)
            ? UnifiedProviderConnectedAddressContext.FingerprintConfiguration(
                InstanceId,
                ConfigurationGeneration.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ProfileId,
                BaseUri.AbsoluteUri,
                EndpointProfileId,
                WorkspaceId,
                RouteIntent.Fingerprint)
            : RequireSha256(configurationFingerprint, nameof(configurationFingerprint));
        EndpointFingerprint =
            UnifiedProviderConnectedAddressContext.FingerprintEndpoint(
                BaseUri,
                scope);
        AttemptId = string.IsNullOrWhiteSpace(attemptId)
            ? "catalog-" + Guid.NewGuid().ToString("N")
            : UnifiedProviderConfiguredInstance.Identifier(
                attemptId,
                128,
                nameof(attemptId));
    }

    internal static ProviderCatalogNetworkTarget FromConfiguredInstance(
        UnifiedProviderConfiguredInstance instance,
        ProviderCustomAuthenticationKind customAuthentication =
            ProviderCustomAuthenticationKind.ProfileDefault)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        return new ProviderCatalogNetworkTarget(
            instance.ProviderKind,
            instance.ProfileId,
            instance.InstanceId,
            instance.ConfigurationGeneration,
            instance.BaseUri,
            instance.EndpointProfileId,
            instance.WorkspaceId,
            instance.CredentialConfigured,
            customAuthentication,
            instance.RouteIntent,
            instance.ConfigurationFingerprint,
            null);
    }

    private static string RequireSha256(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 is required.", parameterName);
        return value.ToUpperInvariant();
    }
}

internal sealed class ProviderCatalogNetworkResult
{
    internal ProviderCatalogNetworkAdmission Admission { get; }
    internal ProviderControlKind Provider { get; }
    internal string InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderCatalogParseResult? Catalog { get; }
    internal ProviderCatalogContinuation Continuation { get; }
    internal ProviderHealthAssessment Health { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal int NetworkCalls { get; }
    internal bool Accepted => Admission == ProviderCatalogNetworkAdmission.Accepted;
    internal bool RetainsRawBody => false;
    internal bool InfersGeographicBlocking => false;

    internal ProviderCatalogNetworkResult(
        ProviderCatalogNetworkAdmission admission,
        ProviderControlKind provider,
        string? instanceId,
        long configurationGeneration,
        ProviderCatalogParseResult? catalog,
        ProviderCatalogContinuation continuation,
        ProviderHealthAssessment health,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls)
    {
        if (!Enum.IsDefined(typeof(ProviderCatalogNetworkAdmission), admission)
            || admission == ProviderCatalogNetworkAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        if (networkCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        if (admission == ProviderCatalogNetworkAdmission.NotConfigured)
        {
            if (provider != ProviderControlKind.Invalid
                || !string.IsNullOrEmpty(instanceId)
                || configurationGeneration != 0
                || catalog != null
                || networkCalls != 0)
            {
                throw new ArgumentException("An unconfigured result cannot carry route state.");
            }
        }
        else
        {
            if (!Enum.IsDefined(typeof(ProviderControlKind), provider)
                || provider == ProviderControlKind.Invalid)
            {
                throw new ArgumentOutOfRangeException(nameof(provider));
            }
            _ = UnifiedProviderConfiguredInstance.Identifier(
                instanceId,
                96,
                nameof(instanceId));
            if (configurationGeneration <= 0)
                throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        }
        if (catalog != null && catalog.Provider != provider)
            throw new ArgumentException("Catalog provider identity changed.", nameof(catalog));
        if (admission == ProviderCatalogNetworkAdmission.Accepted
            && (catalog == null || !catalog.Parsed || catalog.Models.Count == 0))
        {
            throw new ArgumentException("Accepted network results require a parsed catalog.");
        }
        Admission = admission;
        Provider = provider;
        InstanceId = instanceId ?? string.Empty;
        ConfigurationGeneration = configurationGeneration;
        Catalog = catalog;
        Continuation = continuation ?? throw new ArgumentNullException(nameof(continuation));
        Health = health ?? throw new ArgumentNullException(nameof(health));
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        NetworkCalls = networkCalls;
    }
}

internal static class UnifiedProviderCatalogNetworkHttp
{
    internal static HttpClient CreateClient()
        => UnifiedProviderConnectedAddressHttp.CreateClient(
            maximumConnectionsPerServer: 2);
}

internal sealed class UnifiedProviderCatalogNetworkClient
{
    internal const int MaximumResponseBytes = 256 * 1024;
    internal const int MaximumCredentialCharacters = 16 * 1024;
    internal const int MaximumPagesPerAttempt = 1;
    internal static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(8);
    internal static TimeSpan ObservationFreshness { get; } = TimeSpan.FromMinutes(5);

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly HttpClient _http;
    private readonly IUnifiedProviderConnectedAddressBoundary _connectedAddresses;
    private readonly Func<DateTimeOffset> _clock;

    internal UnifiedProviderCatalogNetworkClient(
        HttpClient http,
        Func<DateTimeOffset>? clock = null)
        : this(http, null, clock)
    {
    }

    internal UnifiedProviderCatalogNetworkClient(
        HttpClient http,
        IUnifiedProviderConnectedAddressBoundary? connectedAddresses,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _connectedAddresses = UnifiedProviderConnectedAddressHttp.BoundaryFor(
            http,
            connectedAddresses);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<ProviderCatalogNetworkResult> DiscoverConfiguredOnceAsync(
        ProviderCatalogNetworkTarget? target,
        UnifiedProviderCredentialLease? credentialLease,
        CancellationToken cancellationToken)
    {
        var observedAt = _clock();
        var expiresAt = observedAt + ObservationFreshness;
        if (target == null)
        {
            credentialLease?.Dispose();
            return new ProviderCatalogNetworkResult(
                ProviderCatalogNetworkAdmission.NotConfigured,
                ProviderControlKind.Invalid,
                string.Empty,
                0,
                null,
                ProviderCatalogContinuation.None,
                new ProviderHealthAssessment(
                    ProviderConnectionHealth.NotConfigured,
                    ProviderHealthColor.Orange,
                    ProviderHealthReason.ObservationUnsupported),
                observedAt,
                expiresAt,
                0);
        }

        var profile = ValidateTarget(target);
        if (profile == null)
        {
            credentialLease?.Dispose();
            return Rejected(
                target,
                ProviderCatalogNetworkAdmission.ConfigurationRejected,
                ProviderCatalogHealthMapper.FromFailure(
                    ProviderTransportFailure.ProtocolFailure),
                observedAt,
                expiresAt,
                0);
        }

        var endpointAdmission = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            target.Provider,
            target.BaseUri,
            target.RouteIntent,
            target.EndpointProfileId,
            target.WorkspaceId);
        if (endpointAdmission.Admission == UnifiedProviderEndpointAdmission.Rejected)
        {
            credentialLease?.Dispose();
            return Rejected(
                target,
                ProviderCatalogNetworkAdmission.ConfigurationRejected,
                ProviderCatalogHealthMapper.FromFailure(
                    ProviderTransportFailure.ProtocolFailure),
                observedAt,
                expiresAt,
                0);
        }

        var requestDeadline = observedAt + RequestTimeout;
        var connectedContext = new UnifiedProviderConnectedAddressContext(
            target.InstanceId,
            target.ConfigurationGeneration,
            target.ConfigurationFingerprint,
            target.EndpointFingerprint,
            target.AttemptId,
            target.BaseUri,
            UnifiedProviderConnectedAddressContext.FromRouteIntent(
                target.RouteIntent),
            observedAt,
            requestDeadline);
        UnifiedProviderConnectedAddressResult connectedResult;
        using (var addressTimeout = new CancellationTokenSource(RequestTimeout))
        using (var addressLinked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            addressTimeout.Token))
        {
            connectedResult = await _connectedAddresses.PrepareAsync(
                    connectedContext,
                    addressLinked.Token)
                .ConfigureAwait(false);
        }
        if (!connectedResult.Accepted || connectedResult.Plan == null)
        {
            credentialLease?.Dispose();
            var callerCancelled = cancellationToken.IsCancellationRequested;
            var timedOut = connectedResult.Reason
                == UnifiedProviderConnectedAddressReason.Cancelled
                && !callerCancelled;
            return Rejected(
                target,
                callerCancelled
                    ? ProviderCatalogNetworkAdmission.Cancelled
                    : timedOut
                        ? ProviderCatalogNetworkAdmission.Timeout
                        : ProviderCatalogNetworkAdmission.ConnectedAddressRejected,
                new ProviderHealthAssessment(
                    callerCancelled || timedOut
                        ? ProviderConnectionHealth.Degraded
                        : ProviderConnectionHealth.EndpointRejected,
                    callerCancelled || timedOut
                        ? ProviderHealthColor.Orange
                        : ProviderHealthColor.Red,
                    callerCancelled
                        ? ProviderHealthReason.Cancelled
                        : timedOut
                            ? ProviderHealthReason.Timeout
                            : ProviderHealthReason.EndpointRejected),
                observedAt,
                expiresAt,
                0);
        }

        var requestUri = new Uri(target.BaseUri, profile.CatalogPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        _connectedAddresses.Bind(request, connectedResult.Plan);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        char[]? secret = null;
        try
        {
            if (credentialLease != null)
            {
                secret = credentialLease.Take();
            }
            var credentialAdmission = ValidateCredentialBoundary(
                target,
                profile,
                secret);
            if (credentialAdmission != ProviderCatalogNetworkAdmission.Accepted)
            {
                return Rejected(
                    target,
                    credentialAdmission,
                    new ProviderHealthAssessment(
                        credentialAdmission == ProviderCatalogNetworkAdmission.CredentialRequired
                            ? ProviderConnectionHealth.NotConfigured
                            : ProviderConnectionHealth.EndpointRejected,
                        credentialAdmission == ProviderCatalogNetworkAdmission.CredentialRequired
                            ? ProviderHealthColor.Orange
                            : ProviderHealthColor.Red,
                        credentialAdmission == ProviderCatalogNetworkAdmission.CredentialRequired
                            ? ProviderHealthReason.ObservationUnsupported
                            : ProviderHealthReason.EndpointRejected),
                    observedAt,
                    expiresAt,
                    0);
            }
            AssembleHeaders(request, target, profile, secret);

            var remaining = requestDeadline - _clock();
            if (remaining <= TimeSpan.Zero)
            {
                return Rejected(
                    target,
                    ProviderCatalogNetworkAdmission.Timeout,
                    ProviderCatalogHealthMapper.FromFailure(
                        ProviderTransportFailure.Timeout),
                    observedAt,
                    expiresAt,
                    0);
            }
            using var timeout = new CancellationTokenSource(remaining);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token)
                    .ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri == null
                    || !string.Equals(
                        response.RequestMessage.RequestUri.AbsoluteUri,
                        requestUri.AbsoluteUri,
                        StringComparison.Ordinal)
                    || (int)response.StatusCode is >= 300 and < 400)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.RedirectRejected,
                        ProviderCatalogHealthMapper.FromFailure(
                            ProviderTransportFailure.RedirectRejected),
                        observedAt,
                        expiresAt,
                        1);
                }

                var statusCode = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.AuthenticationRejected,
                        ProviderCatalogHealthMapper.FromHttp(
                            statusCode,
                            ProviderCatalogParseStatus.Malformed),
                        observedAt,
                        expiresAt,
                        1);
                }
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.ResponseRejected,
                        ProviderCatalogHealthMapper.FromHttp(
                            statusCode,
                            ProviderCatalogParseStatus.Malformed),
                        observedAt,
                        expiresAt,
                        1);
                }
                if (statusCode == 429)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.RateLimited,
                        ProviderCatalogHealthMapper.FromHttp(
                            statusCode,
                            ProviderCatalogParseStatus.Malformed),
                        observedAt,
                        expiresAt,
                        1);
                }
                if ((target.Provider is ProviderControlKind.QwenDashScope
                        or ProviderControlKind.CustomOpenAICompatible)
                    && statusCode is 404 or 405)
                {
                    return Rejected(
                        target,
                        target.Provider == ProviderControlKind.QwenDashScope
                            ? ProviderCatalogNetworkAdmission.CatalogObservationUnsupported
                            : ProviderCatalogNetworkAdmission.CatalogObservationUnsupported,
                        new ProviderHealthAssessment(
                            ProviderConnectionHealth.ReadyToValidate,
                            ProviderHealthColor.Orange,
                            target.Provider == ProviderControlKind.QwenDashScope
                                ? ProviderHealthReason.ManualSelectionRequired
                                : ProviderHealthReason.ObservationUnsupported),
                        observedAt,
                        expiresAt,
                        1);
                }
                if (!response.IsSuccessStatusCode)
                {
                    var health = ProviderCatalogHealthMapper.FromHttp(
                        statusCode,
                        ProviderCatalogParseStatus.Malformed);
                    return Rejected(
                        target,
                        statusCode == 408 || statusCode is >= 500 and <= 599
                            ? ProviderCatalogNetworkAdmission.ServiceUnavailable
                            : ProviderCatalogNetworkAdmission.ResponseRejected,
                        health,
                        observedAt,
                        expiresAt,
                        1);
                }
                if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.ResourceLimitExceeded,
                        ProviderCatalogHealthMapper.FromFailure(
                            ProviderTransportFailure.ProtocolFailure),
                        observedAt,
                        expiresAt,
                        1);
                }

                byte[]? payload = null;
                try
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(linked.Token)
                        .ConfigureAwait(false);
                    payload = await ReadBoundedAsync(stream, linked.Token)
                        .ConfigureAwait(false);
                    var body = StrictUtf8.GetString(payload);
                    var parsed = Parse(profile, body);
                    parsed = RemoveHardRejectedModels(profile, parsed);
                    var continuation = ContinuationFor(parsed);
                    var health = continuation.HasContinuation
                        ? new ProviderHealthAssessment(
                            ProviderConnectionHealth.Degraded,
                            ProviderHealthColor.Orange,
                            ProviderHealthReason.CatalogDegraded)
                        : ProviderCatalogHealthMapper.FromHttp(
                            statusCode,
                            parsed.Status);
                    if (!parsed.Parsed || parsed.Models.Count == 0)
                    {
                        return Rejected(
                            target,
                            ProviderCatalogNetworkAdmission.ResponseRejected,
                            health,
                            observedAt,
                            expiresAt,
                            1,
                            parsed,
                            continuation);
                    }
                    return new ProviderCatalogNetworkResult(
                        ProviderCatalogNetworkAdmission.Accepted,
                        target.Provider,
                        target.InstanceId,
                        target.ConfigurationGeneration,
                        parsed,
                        continuation,
                        health,
                        observedAt,
                        expiresAt,
                        1);
                }
                catch (ProviderCatalogNetworkResourceLimitException)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.ResourceLimitExceeded,
                        ProviderCatalogHealthMapper.FromFailure(
                            ProviderTransportFailure.ProtocolFailure),
                        observedAt,
                        expiresAt,
                        1);
                }
                catch (DecoderFallbackException)
                {
                    return Rejected(
                        target,
                        ProviderCatalogNetworkAdmission.ResponseRejected,
                        ProviderCatalogHealthMapper.FromFailure(
                            ProviderTransportFailure.ProtocolFailure),
                        observedAt,
                        expiresAt,
                        1);
                }
                finally
                {
                    if (payload != null)
                        CryptographicOperations.ZeroMemory(payload);
                }
            }
            catch (OperationCanceledException)
            {
                var callerCancelled = cancellationToken.IsCancellationRequested;
                return Rejected(
                    target,
                    callerCancelled
                        ? ProviderCatalogNetworkAdmission.Cancelled
                        : ProviderCatalogNetworkAdmission.Timeout,
                    ProviderCatalogHealthMapper.FromFailure(
                        callerCancelled
                            ? ProviderTransportFailure.Cancelled
                            : ProviderTransportFailure.Timeout),
                    observedAt,
                    expiresAt,
                    1);
            }
            catch (HttpRequestException)
            {
                return Rejected(
                    target,
                    ProviderCatalogNetworkAdmission.NetworkFailure,
                    ProviderCatalogHealthMapper.FromFailure(
                        ProviderTransportFailure.NetworkFailure),
                    observedAt,
                    expiresAt,
                    1);
            }
            catch (IOException)
            {
                return Rejected(
                    target,
                    ProviderCatalogNetworkAdmission.NetworkFailure,
                    ProviderCatalogHealthMapper.FromFailure(
                        ProviderTransportFailure.NetworkFailure),
                    observedAt,
                    expiresAt,
                    1);
            }
        }
        finally
        {
            credentialLease?.Dispose();
            if (secret != null)
                Array.Clear(secret, 0, secret.Length);
        }
    }

    private static ProviderControlProfile? ValidateTarget(
        ProviderCatalogNetworkTarget target)
    {
        if (!UnifiedProviderProfileRegistry.TryGet(target.Provider, out var profile)
            || profile == null
            || !string.Equals(
                profile.ProviderId,
                target.ProfileId,
                StringComparison.Ordinal))
        {
            return null;
        }
        if (target.BaseUri.Query.Length != 0 || target.BaseUri.Fragment.Length != 0)
            return null;
        if (profile.RequiresExplicitEndpointProfile)
        {
            var endpoint = profile.EndpointProfiles.SingleOrDefault(value =>
                string.Equals(
                    value.ProfileId,
                    target.EndpointProfileId,
                    StringComparison.Ordinal));
            if (endpoint == null)
                return null;
            if (endpoint.RequiresWorkspaceId != (target.WorkspaceId.Length > 0))
                return null;
            var expectedAddress = endpoint.BaseAddressTemplate.Replace(
                ProviderEndpointProfile.WorkspacePlaceholder,
                target.WorkspaceId,
                StringComparison.Ordinal);
            var expected = UnifiedProviderConfiguredInstance.NormalizeBaseUri(
                new Uri(expectedAddress, UriKind.Absolute),
                nameof(target.BaseUri));
            return string.Equals(
                expected.AbsoluteUri,
                target.BaseUri.AbsoluteUri,
                StringComparison.Ordinal)
                ? profile
                : null;
        }
        if (target.EndpointProfileId.Length != 0 || target.WorkspaceId.Length != 0)
            return null;
        if (!profile.PlayerMayEditEndpoint
            && (profile.DefaultBaseUri == null
                || !string.Equals(
                    profile.DefaultBaseUri.AbsoluteUri,
                    target.BaseUri.AbsoluteUri,
                    StringComparison.Ordinal)))
        {
            return null;
        }
        if (profile.Kind == ProviderControlKind.CustomOpenAICompatible)
        {
            return target.CustomAuthentication is
                ProviderCustomAuthenticationKind.None
                or ProviderCustomAuthenticationKind.Bearer
                ? profile
                : null;
        }
        return target.CustomAuthentication ==
            ProviderCustomAuthenticationKind.ProfileDefault
            ? profile
            : null;
    }

    private static ProviderCatalogNetworkAdmission ValidateCredentialBoundary(
        ProviderCatalogNetworkTarget target,
        ProviderControlProfile profile,
        char[]? secret)
    {
        var required = profile.CredentialRequirement ==
            ProviderCredentialRequirement.Required
            || (profile.Kind == ProviderControlKind.CustomOpenAICompatible
                && target.CustomAuthentication ==
                ProviderCustomAuthenticationKind.Bearer);
        var hasSecret = secret is { Length: > 0 };
        if (required && !hasSecret)
            return ProviderCatalogNetworkAdmission.CredentialRequired;
        if (target.CredentialConfigured != hasSecret)
            return ProviderCatalogNetworkAdmission.CredentialMismatch;
        if (hasSecret && !CredentialIsSafe(secret!))
            return ProviderCatalogNetworkAdmission.CredentialMismatch;
        if (profile.CredentialRequirement == ProviderCredentialRequirement.NotRequired
            && hasSecret)
        {
            return ProviderCatalogNetworkAdmission.CredentialMismatch;
        }
        if (profile.Kind == ProviderControlKind.CustomOpenAICompatible
            && target.CustomAuthentication == ProviderCustomAuthenticationKind.None
            && hasSecret)
        {
            return ProviderCatalogNetworkAdmission.CredentialMismatch;
        }
        return ProviderCatalogNetworkAdmission.Accepted;
    }

    private static bool CredentialIsSafe(char[] value)
        => value.Length is > 0 and <= MaximumCredentialCharacters
           && value.All(character => !char.IsControl(character)
               && !char.IsWhiteSpace(character));

    private static void AssembleHeaders(
        HttpRequestMessage request,
        ProviderCatalogNetworkTarget target,
        ProviderControlProfile profile,
        char[]? secret)
    {
        var auth = profile.Kind == ProviderControlKind.CustomOpenAICompatible
            ? target.CustomAuthentication == ProviderCustomAuthenticationKind.Bearer
                ? ProviderControlAuth.Bearer
                : ProviderControlAuth.NoneByDefault
            : profile.Auth;
        if (secret is { Length: > 0 })
        {
            var headerValue = new string(secret);
            switch (auth)
            {
                case ProviderControlAuth.Bearer:
                case ProviderControlAuth.OptionalBearer:
                case ProviderControlAuth.LocalAiOptional:
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        headerValue);
                    break;
                case ProviderControlAuth.GoogleApiKeyHeader:
                    request.Headers.Add("x-goog-api-key", headerValue);
                    break;
                case ProviderControlAuth.AnthropicApiKeyHeader:
                    request.Headers.Add("x-api-key", headerValue);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The selected profile cannot transmit a credential.");
            }
        }
        if (profile.Auth == ProviderControlAuth.AnthropicApiKeyHeader)
            request.Headers.Add("anthropic-version", "2023-06-01");
        if (profile.Kind == ProviderControlKind.OpenRouter)
        {
            request.Headers.TryAddWithoutValidation(
                "HTTP-Referer",
                "https://github.com/minimi6666/Lilith-AI-Mod");
            request.Headers.TryAddWithoutValidation("X-Title", "Lilith AI MOD");
        }
    }

    private static ProviderCatalogParseResult Parse(
        ProviderControlProfile profile,
        string body)
        => profile.CatalogKind switch
        {
            ProviderCatalogKind.GeminiModelsPaginated =>
                UnifiedProviderCatalogAdapters.ParseGemini(body),
            ProviderCatalogKind.AnthropicModelsCursorPaginated =>
                UnifiedProviderCatalogAdapters.ParseAnthropic(body),
            ProviderCatalogKind.OpenRouterModels =>
                UnifiedProviderCatalogAdapters.ParseOpenRouter(body),
            ProviderCatalogKind.OllamaTagsAndShow =>
                UnifiedProviderCatalogAdapters.ParseOllama(profile.Kind, body),
            ProviderCatalogKind.LmStudioNativeModels =>
                UnifiedProviderCatalogAdapters.ParseLmStudio(body),
            ProviderCatalogKind.LocalAiCapabilities =>
                UnifiedProviderCatalogAdapters.ParseLocalAi(body),
            ProviderCatalogKind.OpenAiModels
                or ProviderCatalogKind.QwenOptionalOpenAiModels
                or ProviderCatalogKind.OptionalOpenAiModels =>
                UnifiedProviderCatalogAdapters.ParseOpenAiShaped(
                    profile.Kind,
                    body),
            _ => throw new ArgumentOutOfRangeException(nameof(profile.CatalogKind))
        };

    private static ProviderCatalogParseResult RemoveHardRejectedModels(
        ProviderControlProfile profile,
        ProviderCatalogParseResult parsed)
    {
        if (!parsed.Parsed || profile.HardRejectedModelIds.Count == 0)
            return parsed;
        var admitted = parsed.Models
            .Where(model => !profile.IsHardRejectedModel(model.ModelId))
            .ToArray();
        var removed = parsed.Models.Count - admitted.Length;
        if (removed == 0)
            return parsed;
        if (admitted.Length == 0)
        {
            return new ProviderCatalogParseResult(
                profile.Kind,
                ProviderCatalogParseStatus.Empty);
        }
        var status = parsed.Status == ProviderCatalogParseStatus.Truncated
            ? ProviderCatalogParseStatus.Truncated
            : ProviderCatalogParseStatus.Partial;
        return new ProviderCatalogParseResult(
            profile.Kind,
            status,
            admitted,
            parsed.NextPageToken,
            parsed.FirstId,
            parsed.LastId,
            parsed.HasMore,
            parsed.RejectedEntries + removed,
            parsed.TruncatedEntries);
    }

    private static ProviderCatalogContinuation ContinuationFor(
        ProviderCatalogParseResult parsed)
    {
        if (parsed.Provider == ProviderControlKind.GoogleGemini
            && parsed.NextPageToken.Length > 0)
        {
            return new ProviderCatalogContinuation(
                ProviderCatalogContinuationKind.GeminiNextPageToken,
                parsed.NextPageToken);
        }
        if (parsed.Provider == ProviderControlKind.AnthropicClaude
            && parsed.HasMore == true
            && parsed.LastId.Length > 0)
        {
            return new ProviderCatalogContinuation(
                ProviderCatalogContinuationKind.AnthropicAfterId,
                parsed.LastId);
        }
        return ProviderCatalogContinuation.None;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
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
                    return output.ToArray();
                if (output.Length + count > MaximumResponseBytes)
                    throw new ProviderCatalogNetworkResourceLimitException();
                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static ProviderCatalogNetworkResult Rejected(
        ProviderCatalogNetworkTarget target,
        ProviderCatalogNetworkAdmission admission,
        ProviderHealthAssessment health,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls,
        ProviderCatalogParseResult? catalog = null,
        ProviderCatalogContinuation? continuation = null)
        => new(
            admission,
            target.Provider,
            target.InstanceId,
            target.ConfigurationGeneration,
            catalog,
            continuation ?? ProviderCatalogContinuation.None,
            health,
            observedAt,
            expiresAt,
            networkCalls);
}

internal sealed class ProviderCatalogNetworkResourceLimitException : Exception
{
}
