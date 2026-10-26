using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using LilithProviderControlPlane;

namespace LilithControlCenter;

internal enum ProviderControlCenterNetworkOperation
{
    StartupHealth = 1,
    ManualHealth = 2,
    ValidateAndSave = 3
}

internal enum ProviderControlCenterNetworkBoundaryAdmission
{
    LiveAuthorizationRequired = 1,
    Accepted = 2,
    Cancelled = 3,
    Faulted = 4
}

internal enum ProviderControlCenterNetworkBoundaryReason
{
    Invalid = 0,
    SourcePredeploymentDisabled = 1,
    Completed = 2,
    CallerCancelled = 3,
    CapacityUnavailable = 4,
    EndpointRejected = 5,
    AuthenticationRejected = 6,
    RateLimited = 7,
    NetworkUnavailable = 8,
    TimedOut = 9,
    ProtocolRejected = 10,
    StaleResult = 11,
    IdentityMismatch = 12,
    Faulted = 13
}

internal enum ProviderControlCenterHealthReason
{
    Invalid = 0,
    CatalogReachable = 1,
    GenerationVerified = 2,
    AuthenticationRejected = 3,
    EndpointPolicyRejected = 4,
    RateLimited = 5,
    NetworkUnavailable = 6,
    TimedOut = 7,
    ProtocolRejected = 8,
    ObservationUnsupported = 9,
    Cancelled = 10
}

internal enum ProviderControlCenterObservationFreshness
{
    Invalid = 0,
    Fresh = 1,
    Stale = 2,
    Expired = 3
}

internal enum ProviderControlCenterSetupObservationState
{
    Invalid = 0,
    ValidatedAndSaved = 1
}

internal enum ProviderControlCenterResultAdmission
{
    Accepted = 1,
    BoundaryNotAccepted = 2,
    RequestIdentityMismatch = 3,
    OperationMismatch = 4,
    ObservationShapeMismatch = 5,
    InstanceIdentityMismatch = 6,
    ConfigurationGenerationMismatch = 7,
    ConfigurationFingerprintMismatch = 8,
    CurrentInstanceMissing = 9,
    FreshnessRejected = 10,
    HealthReasonMismatch = 11,
    StoreGenerationMismatch = 12,
    ModelMismatch = 13,
    EndpointMismatch = 14,
    AuthenticationMismatch = 15,
    RouteIntentMismatch = 16
}

internal sealed class ProviderControlCenterHealthObservation
{
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string ProfileId { get; }
    internal string InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal ProviderConnectionHealth Health { get; }
    internal ProviderControlCenterHealthReason Reason { get; }
    internal ProviderControlCenterObservationFreshness Freshness { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal ProviderControlCenterHealthObservation(
        ProviderControlKind provider,
        string providerId,
        string profileId,
        string instanceId,
        long configurationGeneration,
        string configurationFingerprint,
        ProviderConnectionHealth health,
        ProviderControlCenterHealthReason reason,
        ProviderControlCenterObservationFreshness freshness,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        if (!Enum.IsDefined(typeof(ProviderControlKind), provider)
            || provider == ProviderControlKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        Provider = provider;
        ProviderId = ProviderControlCenterNetworkRequest.Identifier(
            providerId,
            96,
            nameof(providerId));
        ProfileId = ProviderControlCenterNetworkRequest.Identifier(
            profileId,
            96,
            nameof(profileId));
        InstanceId = ProviderControlCenterNetworkRequest.Identifier(
            instanceId,
            96,
            nameof(instanceId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = ExactFingerprint(
            configurationFingerprint,
            nameof(configurationFingerprint));
        if (!IsTerminalHealth(health))
            throw new ArgumentOutOfRangeException(nameof(health));
        if (!Enum.IsDefined(typeof(ProviderControlCenterHealthReason), reason)
            || reason == ProviderControlCenterHealthReason.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if (!HealthReasonMatches(health, reason))
        {
            throw new ArgumentException(
                "The typed health and reason do not describe the same result.",
                nameof(reason));
        }
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterObservationFreshness),
                freshness)
            || freshness == ProviderControlCenterObservationFreshness.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }
        if (expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Health = health;
        Reason = reason;
        Freshness = freshness;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
    }

    internal bool IsFreshAt(DateTimeOffset now)
        => Freshness == ProviderControlCenterObservationFreshness.Fresh
           && now >= ObservedAt
           && now < ExpiresAt;

    internal static bool HealthReasonMatches(
        ProviderConnectionHealth health,
        ProviderControlCenterHealthReason reason)
        => health switch
        {
            ProviderConnectionHealth.CatalogAvailable =>
                reason == ProviderControlCenterHealthReason.CatalogReachable,
            ProviderConnectionHealth.GenerationVerified =>
                reason == ProviderControlCenterHealthReason.GenerationVerified,
            ProviderConnectionHealth.AuthenticationRejected =>
                reason == ProviderControlCenterHealthReason.AuthenticationRejected,
            ProviderConnectionHealth.EndpointRejected =>
                reason == ProviderControlCenterHealthReason.EndpointPolicyRejected,
            ProviderConnectionHealth.RateLimited =>
                reason == ProviderControlCenterHealthReason.RateLimited,
            ProviderConnectionHealth.NetworkFailure =>
                reason is ProviderControlCenterHealthReason.NetworkUnavailable
                    or ProviderControlCenterHealthReason.TimedOut,
            ProviderConnectionHealth.Degraded =>
                reason is ProviderControlCenterHealthReason.ProtocolRejected
                    or ProviderControlCenterHealthReason.ObservationUnsupported
                    or ProviderControlCenterHealthReason.Cancelled,
            _ => false
        };

    internal static string ExactFingerprint(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "An exact SHA-256 fingerprint is required.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    private static bool IsTerminalHealth(ProviderConnectionHealth health)
        => health is ProviderConnectionHealth.CatalogAvailable
            or ProviderConnectionHealth.GenerationVerified
            or ProviderConnectionHealth.AuthenticationRejected
            or ProviderConnectionHealth.EndpointRejected
            or ProviderConnectionHealth.RateLimited
            or ProviderConnectionHealth.NetworkFailure
            or ProviderConnectionHealth.Degraded;
}

internal sealed class ProviderControlCenterSetupObservation
{
    internal ProviderControlCenterSetupObservationState State { get; }
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string ProfileId { get; }
    internal string InstanceId { get; }
    internal long PreviousStoreGeneration { get; }
    internal long StoreGeneration { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal string SelectedModel { get; }
    internal ProviderConnectionHealth Health { get; }
    internal ProviderControlCenterHealthReason Reason { get; }
    internal ProviderControlCenterObservationFreshness Freshness { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal ProviderControlCenterSetupObservation(
        ProviderControlCenterSetupObservationState state,
        ProviderControlKind provider,
        string providerId,
        string profileId,
        string instanceId,
        long previousStoreGeneration,
        long storeGeneration,
        long configurationGeneration,
        string configurationFingerprint,
        string selectedModel,
        ProviderConnectionHealth health,
        ProviderControlCenterHealthReason reason,
        ProviderControlCenterObservationFreshness freshness,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        if (state != ProviderControlCenterSetupObservationState.ValidatedAndSaved)
            throw new ArgumentOutOfRangeException(nameof(state));
        State = state;
        if (!Enum.IsDefined(typeof(ProviderControlKind), provider)
            || provider == ProviderControlKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        Provider = provider;
        ProviderId = ProviderControlCenterNetworkRequest.Identifier(
            providerId,
            96,
            nameof(providerId));
        ProfileId = ProviderControlCenterNetworkRequest.Identifier(
            profileId,
            96,
            nameof(profileId));
        InstanceId = ProviderControlCenterNetworkRequest.Identifier(
            instanceId,
            96,
            nameof(instanceId));
        if (previousStoreGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(previousStoreGeneration));
        if (storeGeneration <= previousStoreGeneration)
            throw new ArgumentOutOfRangeException(nameof(storeGeneration));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        PreviousStoreGeneration = previousStoreGeneration;
        StoreGeneration = storeGeneration;
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint =
            ProviderControlCenterHealthObservation.ExactFingerprint(
                configurationFingerprint,
                nameof(configurationFingerprint));
        SelectedModel = UnifiedProviderCatalogCommit.ModelId(
            selectedModel,
            nameof(selectedModel));
        if (!ProviderControlCenterHealthObservation.HealthReasonMatches(
                health,
                reason))
        {
            throw new ArgumentException(
                "The typed health and reason do not describe the same result.",
                nameof(reason));
        }
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterObservationFreshness),
                freshness)
            || freshness == ProviderControlCenterObservationFreshness.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }
        if (expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Health = health;
        Reason = reason;
        Freshness = freshness;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
    }

    internal bool IsFreshAt(DateTimeOffset now)
        => Freshness == ProviderControlCenterObservationFreshness.Fresh
           && now >= ObservedAt
           && now < ExpiresAt;
}

internal sealed class ProviderControlCenterCachedHealthObservation
{
    private readonly string _instanceId;
    private readonly long _configurationGeneration;
    private readonly string _configurationFingerprint;
    private readonly ProviderControlCenterObservationFreshness _freshness;
    private readonly DateTimeOffset _observedAt;
    private readonly DateTimeOffset _expiresAt;

    internal ProviderConnectionHealth Health { get; }

    internal ProviderControlCenterCachedHealthObservation(
        ProviderControlCenterHealthObservation observation)
        : this(
            observation?.InstanceId,
            observation?.ConfigurationGeneration ?? 0,
            observation?.ConfigurationFingerprint,
            observation?.Health ?? ProviderConnectionHealth.Invalid,
            observation?.Freshness ?? ProviderControlCenterObservationFreshness.Invalid,
            observation?.ObservedAt ?? default,
            observation?.ExpiresAt ?? default)
    {
    }

    internal ProviderControlCenterCachedHealthObservation(
        ProviderControlCenterSetupObservation observation)
        : this(
            observation?.InstanceId,
            observation?.ConfigurationGeneration ?? 0,
            observation?.ConfigurationFingerprint,
            observation?.Health ?? ProviderConnectionHealth.Invalid,
            observation?.Freshness ?? ProviderControlCenterObservationFreshness.Invalid,
            observation?.ObservedAt ?? default,
            observation?.ExpiresAt ?? default)
    {
    }

    private ProviderControlCenterCachedHealthObservation(
        string? instanceId,
        long configurationGeneration,
        string? configurationFingerprint,
        ProviderConnectionHealth health,
        ProviderControlCenterObservationFreshness freshness,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        _instanceId = ProviderControlCenterNetworkRequest.Identifier(
            instanceId,
            96,
            nameof(instanceId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        _configurationGeneration = configurationGeneration;
        _configurationFingerprint =
            ProviderControlCenterHealthObservation.ExactFingerprint(
                configurationFingerprint ?? string.Empty,
                nameof(configurationFingerprint));
        if (!Enum.IsDefined(typeof(ProviderConnectionHealth), health)
            || health == ProviderConnectionHealth.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(health));
        }
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterObservationFreshness),
                freshness)
            || freshness == ProviderControlCenterObservationFreshness.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(freshness));
        }
        if (expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Health = health;
        _freshness = freshness;
        _observedAt = observedAt;
        _expiresAt = expiresAt;
    }

    internal bool IsCurrentFor(
        UnifiedProviderConfiguredInstance instance,
        DateTimeOffset now)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        return string.Equals(_instanceId, instance.InstanceId, StringComparison.Ordinal)
            && _configurationGeneration == instance.ConfigurationGeneration
            && string.Equals(
                _configurationFingerprint,
                instance.ConfigurationFingerprint,
                StringComparison.OrdinalIgnoreCase)
            && _freshness == ProviderControlCenterObservationFreshness.Fresh
            && now >= _observedAt
            && now < _expiresAt;
    }
}

internal sealed class ProviderControlCenterRouteChoice
{
    internal ConfiguredEndpointRouteKind Kind { get; }
    public string DisplayName { get; }

    internal ProviderControlCenterRouteChoice(
        ConfiguredEndpointRouteKind kind,
        string displayName)
    {
        if (kind is not (
                ConfiguredEndpointRouteKind.LoopbackOnly
                or ConfiguredEndpointRouteKind.ExplicitLan
                or ConfiguredEndpointRouteKind.ExplicitRemoteHttps))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Kind = kind;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException(
                "A route choice display name is required.",
                nameof(displayName))
            : displayName;
    }

    internal ConfiguredEndpointRouteIntent CreateIntent()
        => Kind switch
        {
            ConfiguredEndpointRouteKind.LoopbackOnly =>
                ConfiguredEndpointRouteIntent.LoopbackOnly(),
            ConfiguredEndpointRouteKind.ExplicitLan =>
                ConfiguredEndpointRouteIntent.ExplicitLan(),
            ConfiguredEndpointRouteKind.ExplicitRemoteHttps =>
                ConfiguredEndpointRouteIntent.ExplicitRemoteHttps(),
            _ => throw new InvalidOperationException("Unsupported route choice.")
        };
}

internal static class ProviderControlCenterRouteChoices
{
    private static readonly IReadOnlyList<ProviderControlCenterRouteChoice>
        SelfHostedChoices = Array.AsReadOnly(new[]
        {
            new ProviderControlCenterRouteChoice(
                ConfiguredEndpointRouteKind.LoopbackOnly,
                "本機（Loopback）"),
            new ProviderControlCenterRouteChoice(
                ConfiguredEndpointRouteKind.ExplicitLan,
                "指定區網（HTTPS）"),
            new ProviderControlCenterRouteChoice(
                ConfiguredEndpointRouteKind.ExplicitRemoteHttps,
                "指定遠端（HTTPS）")
        });

    internal static IReadOnlyList<ProviderControlCenterRouteChoice> For(
        ProviderControlKind provider)
        => provider is ProviderControlKind.OllamaLocal
            or ProviderControlKind.LMStudio
            or ProviderControlKind.LlamaCpp
            or ProviderControlKind.Vllm
            or ProviderControlKind.LocalAI
            or ProviderControlKind.CustomOpenAICompatible
                ? SelfHostedChoices
                : Array.Empty<ProviderControlCenterRouteChoice>();
}

internal sealed class ProviderControlCenterNetworkBoundaryResult
{
    internal string RequestId { get; }
    internal ProviderControlCenterNetworkOperation Operation { get; }
    internal ProviderControlCenterNetworkBoundaryAdmission Admission { get; }
    internal ProviderControlCenterNetworkBoundaryReason Reason { get; }
    internal IReadOnlyList<ProviderControlCenterHealthObservation>
        HealthObservations { get; }
    internal ProviderControlCenterSetupObservation? SetupObservation { get; }

    internal bool Accepted =>
        Admission == ProviderControlCenterNetworkBoundaryAdmission.Accepted;

    internal ProviderControlCenterNetworkBoundaryResult(
        string requestId,
        ProviderControlCenterNetworkOperation operation,
        ProviderControlCenterNetworkBoundaryAdmission admission,
        ProviderControlCenterNetworkBoundaryReason reason,
        IEnumerable<ProviderControlCenterHealthObservation>? healthObservations = null,
        ProviderControlCenterSetupObservation? setupObservation = null)
    {
        RequestId = ProviderControlCenterNetworkRequest.Identifier(
            requestId,
            96,
            nameof(requestId));
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterNetworkOperation),
                operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
        Operation = operation;
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterNetworkBoundaryAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        Admission = admission;
        if (!Enum.IsDefined(
                typeof(ProviderControlCenterNetworkBoundaryReason),
                reason)
            || reason == ProviderControlCenterNetworkBoundaryReason.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        Reason = reason;
        var health = (healthObservations
                ?? Array.Empty<ProviderControlCenterHealthObservation>())
            .ToArray();
        if (health.Any(value => value == null))
            throw new ArgumentException("Health observations cannot be null.", nameof(healthObservations));
        if (admission != ProviderControlCenterNetworkBoundaryAdmission.Accepted
            && (health.Length != 0 || setupObservation != null))
        {
            throw new ArgumentException(
                "A rejected boundary result cannot carry observations.",
                nameof(healthObservations));
        }
        if (admission == ProviderControlCenterNetworkBoundaryAdmission.Accepted)
        {
            var healthOperation = operation is (
                ProviderControlCenterNetworkOperation.StartupHealth
                or ProviderControlCenterNetworkOperation.ManualHealth);
            if ((healthOperation && (health.Length == 0 || setupObservation != null))
                || (!healthOperation && (health.Length != 0 || setupObservation == null)))
            {
                throw new ArgumentException(
                    "Accepted results must carry only the observation shape for their operation.",
                    nameof(healthObservations));
            }
        }
        HealthObservations = Array.AsReadOnly(health);
        SetupObservation = setupObservation;
    }
}

internal abstract class ProviderControlCenterNetworkRequest
{
    internal string RequestId { get; }
    internal ProviderControlCenterNetworkOperation Operation { get; }

    protected ProviderControlCenterNetworkRequest(
        string requestId,
        ProviderControlCenterNetworkOperation operation)
    {
        RequestId = Identifier(requestId, 96, nameof(requestId));
        if (!Enum.IsDefined(typeof(ProviderControlCenterNetworkOperation), operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        Operation = operation;
    }

    internal static string Identifier(
        string? value,
        int maximum,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximum
            || value.Any(character => !(char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "A bounded request identifier is required.",
                parameterName);
        }
        return value.Trim();
    }
}

internal sealed class ProviderControlCenterConfiguredHealthTarget
{
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string ProfileId { get; }
    internal string InstanceId { get; }
    internal Uri BaseUri { get; }
    internal string EndpointProfileId { get; }
    internal string WorkspaceId { get; }
    internal string SelectedModel { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }

    internal ProviderControlCenterConfiguredHealthTarget(
        UnifiedProviderConfiguredInstance configured)
    {
        if (configured == null)
            throw new ArgumentNullException(nameof(configured));
        var profile = UnifiedProviderProfileRegistry.Get(configured.ProviderKind);
        if (!string.Equals(
                profile.ProviderId,
                configured.ProfileId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The configured instance does not match its Provider profile.",
                nameof(configured));
        }
        Provider = configured.ProviderKind;
        ProviderId = profile.ProviderId;
        ProfileId = configured.ProfileId;
        InstanceId = configured.InstanceId;
        BaseUri = configured.BaseUri;
        EndpointProfileId = configured.EndpointProfileId;
        WorkspaceId = configured.WorkspaceId;
        SelectedModel = configured.SelectedModel;
        ConfigurationGeneration = configured.ConfigurationGeneration;
        ConfigurationFingerprint = configured.ConfigurationFingerprint;
    }
}

internal sealed class ProviderControlCenterHealthRequest :
    ProviderControlCenterNetworkRequest
{
    internal long ExpectedStoreGeneration { get; }
    internal IReadOnlyList<ProviderControlCenterConfiguredHealthTarget>
        ConfiguredInstances { get; }

    internal ProviderControlCenterHealthRequest(
        string requestId,
        ProviderControlCenterNetworkOperation operation,
        UnifiedProviderControlPlaneSnapshot snapshot)
        : base(requestId, operation)
    {
        if (operation is not (
                ProviderControlCenterNetworkOperation.StartupHealth
                or ProviderControlCenterNetworkOperation.ManualHealth))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var instances = snapshot.Instances
            .Select(value => new ProviderControlCenterConfiguredHealthTarget(value))
            .OrderBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (instances.Length == 0)
        {
            throw new ArgumentException(
                "Health checks require an already configured instance.",
                nameof(snapshot));
        }
        if (instances.Select(value => value.InstanceId)
            .Distinct(StringComparer.Ordinal).Count() != instances.Length)
        {
            throw new ArgumentException(
                "Configured health targets must be unique.",
                nameof(snapshot));
        }
        ExpectedStoreGeneration = snapshot.StoreGeneration;
        ConfiguredInstances = Array.AsReadOnly(instances);
    }
}

/// <summary>
/// A PasswordBox credential stays encrypted until an admitted boundary asks
/// for it exactly once. Materialization creates no managed string; the
/// resulting character buffer is owned and cleared by the setup host.
/// </summary>
internal sealed class ProviderControlCenterSecureCredentialSource :
    IUnifiedProviderSetupCredentialSource,
    IDisposable
{
    private SecureString? _secureCredential;
    private readonly bool _configured;
    private int _materialized;

    internal bool CredentialConfigured => _configured;
    internal int MaterializationCount => Volatile.Read(ref _materialized);
    bool IUnifiedProviderSetupCredentialSource.CredentialConfigured =>
        _configured;

    internal ProviderControlCenterSecureCredentialSource(
        SecureString secureCredential)
    {
        if (secureCredential == null)
            throw new ArgumentNullException(nameof(secureCredential));
        if (secureCredential.Length
            > UnifiedProviderControlPlaneStore.MaximumCredentialCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(secureCredential));
        }
        _configured = secureCredential.Length > 0;
        _secureCredential = secureCredential.Copy();
    }

    UnifiedProviderSetupCredentialMaterial
        IUnifiedProviderSetupCredentialSource.Materialize()
    {
        if (Interlocked.Exchange(ref _materialized, 1) != 0)
        {
            throw new InvalidOperationException(
                "The UI credential source is single-use.");
        }
        var secure = Interlocked.Exchange(ref _secureCredential, null)
            ?? throw new ObjectDisposedException(
                nameof(ProviderControlCenterSecureCredentialSource));
        var characters = new char[secure.Length];
        IntPtr pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = (char)Marshal.ReadInt16(
                    pointer,
                    index * sizeof(char));
            }
            return new UnifiedProviderSetupCredentialMaterial(characters);
        }
        catch
        {
            Array.Clear(characters, 0, characters.Length);
            throw;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            secure.Dispose();
        }
    }

    public void Dispose()
    {
        var secure = Interlocked.Exchange(ref _secureCredential, null);
        secure?.Dispose();
    }
}

internal sealed class ProviderControlCenterValidateAndSaveRequest :
    ProviderControlCenterNetworkRequest,
    IDisposable
{
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string ProfileId { get; }
    internal int ProfileSchemaVersion { get; }
    internal string InstanceId { get; }
    internal Uri BaseUri { get; }
    internal string EndpointProfileId { get; }
    internal string WorkspaceId { get; }
    internal ProviderCustomAuthenticationKind CustomAuthentication { get; }
    internal ConfiguredEndpointRouteIntent RouteIntent { get; }
    internal string DesiredModel { get; }
    internal long ExpectedStoreGeneration { get; }
    internal IUnifiedProviderSetupCredentialSource CredentialSource { get; }

    internal ProviderControlCenterValidateAndSaveRequest(
        string requestId,
        ProviderControlProfile profile,
        string instanceId,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        ProviderCustomAuthenticationKind customAuthentication,
        string? desiredModel,
        long expectedStoreGeneration,
        IUnifiedProviderSetupCredentialSource credentialSource,
        ConfiguredEndpointRouteIntent? routeIntent = null)
        : base(
            requestId,
            ProviderControlCenterNetworkOperation.ValidateAndSave)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        Provider = profile.Kind;
        ProviderId = profile.ProviderId;
        ProfileId = profile.ProviderId;
        ProfileSchemaVersion = profile.SchemaVersion;
        InstanceId = UnifiedProviderConfiguredInstance.Identifier(
            instanceId,
            96,
            nameof(instanceId));
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
        RouteIntent = routeIntent
            ?? ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                profile.Kind,
                BaseUri);
        if (!Enum.IsDefined(
                typeof(ProviderCustomAuthenticationKind),
                customAuthentication)
            || customAuthentication == ProviderCustomAuthenticationKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(customAuthentication));
        }
        if ((profile.Kind == ProviderControlKind.CustomOpenAICompatible
                && customAuthentication is not (
                    ProviderCustomAuthenticationKind.None
                    or ProviderCustomAuthenticationKind.Bearer))
            || (profile.Kind != ProviderControlKind.CustomOpenAICompatible
                && customAuthentication
                    != ProviderCustomAuthenticationKind.ProfileDefault))
        {
            throw new ArgumentException(
                "Authentication must match the exact Provider profile.",
                nameof(customAuthentication));
        }
        CustomAuthentication = customAuthentication;
        var routeAdmission = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            profile.Kind,
            BaseUri,
            RouteIntent,
            EndpointProfileId,
            WorkspaceId);
        if (routeAdmission.Admission == UnifiedProviderEndpointAdmission.Rejected)
        {
            throw new ArgumentException(
                "The endpoint does not match the exact typed route intent.",
                nameof(routeIntent));
        }
        DesiredModel = string.IsNullOrWhiteSpace(desiredModel)
            ? string.Empty
            : UnifiedProviderCatalogCommit.ModelId(
                desiredModel,
                nameof(desiredModel));
        if (expectedStoreGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedStoreGeneration));
        ExpectedStoreGeneration = expectedStoreGeneration;
        CredentialSource = credentialSource
            ?? throw new ArgumentNullException(nameof(credentialSource));
    }

    public void Dispose()
    {
        if (CredentialSource is IDisposable disposable)
            disposable.Dispose();
    }
}

internal static class ProviderControlCenterResultAdmissionPolicy
{
    internal static ProviderControlCenterResultAdmission TryAdmitHealth(
        ProviderControlCenterHealthRequest request,
        ProviderControlCenterNetworkBoundaryResult result,
        UnifiedProviderControlPlaneSnapshot currentSnapshot,
        DateTimeOffset now,
        out IReadOnlyList<ProviderControlCenterHealthObservation> observations)
    {
        observations = Array.Empty<ProviderControlCenterHealthObservation>();
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (currentSnapshot == null)
            throw new ArgumentNullException(nameof(currentSnapshot));
        var common = CommonAdmission(request, result);
        if (common != ProviderControlCenterResultAdmission.Accepted)
            return common;
        if (currentSnapshot.StoreGeneration != request.ExpectedStoreGeneration)
            return ProviderControlCenterResultAdmission.StoreGenerationMismatch;
        if (result.SetupObservation != null
            || result.HealthObservations.Count != request.ConfiguredInstances.Count)
        {
            return ProviderControlCenterResultAdmission.ObservationShapeMismatch;
        }
        var requestTargets = request.ConfiguredInstances.ToDictionary(
            value => value.InstanceId,
            StringComparer.Ordinal);
        if (result.HealthObservations.Select(value => value.InstanceId)
            .Distinct(StringComparer.Ordinal).Count()
            != result.HealthObservations.Count)
        {
            return ProviderControlCenterResultAdmission.ObservationShapeMismatch;
        }
        var accepted = new List<ProviderControlCenterHealthObservation>(
            result.HealthObservations.Count);
        foreach (var observation in result.HealthObservations)
        {
            if (!requestTargets.TryGetValue(
                    observation.InstanceId,
                    out var requested)
                || requested.Provider != observation.Provider
                || !string.Equals(
                    requested.ProviderId,
                    observation.ProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    requested.ProfileId,
                    observation.ProfileId,
                    StringComparison.Ordinal))
            {
                return ProviderControlCenterResultAdmission.InstanceIdentityMismatch;
            }
            if (requested.ConfigurationGeneration
                != observation.ConfigurationGeneration)
            {
                return ProviderControlCenterResultAdmission.ConfigurationGenerationMismatch;
            }
            if (!string.Equals(
                    requested.ConfigurationFingerprint,
                    observation.ConfigurationFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ProviderControlCenterResultAdmission.ConfigurationFingerprintMismatch;
            }
            var current = currentSnapshot.Instances.SingleOrDefault(value =>
                string.Equals(
                    value.InstanceId,
                    observation.InstanceId,
                    StringComparison.Ordinal));
            if (current == null)
                return ProviderControlCenterResultAdmission.CurrentInstanceMissing;
            if (current.ProviderKind != observation.Provider
                || !string.Equals(
                    current.ProfileId,
                    observation.ProfileId,
                    StringComparison.Ordinal))
            {
                return ProviderControlCenterResultAdmission.InstanceIdentityMismatch;
            }
            if (current.ConfigurationGeneration
                != observation.ConfigurationGeneration)
            {
                return ProviderControlCenterResultAdmission.ConfigurationGenerationMismatch;
            }
            if (!string.Equals(
                    current.ConfigurationFingerprint,
                    observation.ConfigurationFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ProviderControlCenterResultAdmission.ConfigurationFingerprintMismatch;
            }
            if (!observation.IsFreshAt(now))
                return ProviderControlCenterResultAdmission.FreshnessRejected;
            if (!ProviderControlCenterHealthObservation.HealthReasonMatches(
                    observation.Health,
                    observation.Reason))
            {
                return ProviderControlCenterResultAdmission.HealthReasonMismatch;
            }
            accepted.Add(observation);
        }
        observations = Array.AsReadOnly(accepted.ToArray());
        return ProviderControlCenterResultAdmission.Accepted;
    }

    internal static ProviderControlCenterResultAdmission TryAdmitSetup(
        ProviderControlCenterValidateAndSaveRequest request,
        ProviderControlCenterNetworkBoundaryResult result,
        UnifiedProviderControlPlaneSnapshot currentSnapshot,
        DateTimeOffset now,
        out ProviderControlCenterSetupObservation? observation)
    {
        observation = null;
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (currentSnapshot == null)
            throw new ArgumentNullException(nameof(currentSnapshot));
        var common = CommonAdmission(request, result);
        if (common != ProviderControlCenterResultAdmission.Accepted)
            return common;
        var candidate = result.SetupObservation;
        if (candidate == null || result.HealthObservations.Count != 0)
            return ProviderControlCenterResultAdmission.ObservationShapeMismatch;
        if (candidate.Provider != request.Provider
            || !string.Equals(
                candidate.ProviderId,
                request.ProviderId,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.ProfileId,
                request.ProfileId,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.InstanceId,
                request.InstanceId,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.InstanceIdentityMismatch;
        }
        if (candidate.PreviousStoreGeneration != request.ExpectedStoreGeneration
            || candidate.StoreGeneration != currentSnapshot.StoreGeneration)
        {
            return ProviderControlCenterResultAdmission.StoreGenerationMismatch;
        }
        if (!string.IsNullOrEmpty(request.DesiredModel)
            && !string.Equals(
                request.DesiredModel,
                candidate.SelectedModel,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.ModelMismatch;
        }
        var current = currentSnapshot.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, candidate.InstanceId, StringComparison.Ordinal));
        if (current == null)
            return ProviderControlCenterResultAdmission.CurrentInstanceMissing;
        if (current.ProviderKind != candidate.Provider
            || !string.Equals(current.ProfileId, candidate.ProfileId, StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.InstanceIdentityMismatch;
        }
        if (!Uri.Equals(current.BaseUri, request.BaseUri)
            || !string.Equals(
                current.EndpointProfileId,
                request.EndpointProfileId,
                StringComparison.Ordinal)
            || !string.Equals(
                current.WorkspaceId,
                request.WorkspaceId,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.EndpointMismatch;
        }
        if (!string.Equals(
                current.RouteIntent.Fingerprint,
                request.RouteIntent.Fingerprint,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.RouteIntentMismatch;
        }
        if (!AuthenticationMatches(request, current))
            return ProviderControlCenterResultAdmission.AuthenticationMismatch;
        if (current.ConfigurationGeneration != candidate.ConfigurationGeneration)
            return ProviderControlCenterResultAdmission.ConfigurationGenerationMismatch;
        if (!string.Equals(
                current.ConfigurationFingerprint,
                candidate.ConfigurationFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return ProviderControlCenterResultAdmission.ConfigurationFingerprintMismatch;
        }
        if (!string.Equals(
                current.SelectedModel,
                candidate.SelectedModel,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.ModelMismatch;
        }
        if (!candidate.IsFreshAt(now))
            return ProviderControlCenterResultAdmission.FreshnessRejected;
        if (!ProviderControlCenterHealthObservation.HealthReasonMatches(
                candidate.Health,
                candidate.Reason))
        {
            return ProviderControlCenterResultAdmission.HealthReasonMismatch;
        }
        observation = candidate;
        return ProviderControlCenterResultAdmission.Accepted;
    }

    private static bool AuthenticationMatches(
        ProviderControlCenterValidateAndSaveRequest request,
        UnifiedProviderConfiguredInstance current)
    {
        if (request.Provider != ProviderControlKind.CustomOpenAICompatible)
        {
            return request.CustomAuthentication
                == ProviderCustomAuthenticationKind.ProfileDefault
                && request.CredentialSource.CredentialConfigured
                    == current.CredentialConfigured;
        }
        return request.CustomAuthentication switch
        {
            ProviderCustomAuthenticationKind.None =>
                !current.CredentialConfigured
                && !request.CredentialSource.CredentialConfigured,
            ProviderCustomAuthenticationKind.Bearer =>
                current.CredentialConfigured
                && request.CredentialSource.CredentialConfigured,
            _ => false
        };
    }

    private static ProviderControlCenterResultAdmission CommonAdmission(
        ProviderControlCenterNetworkRequest request,
        ProviderControlCenterNetworkBoundaryResult result)
    {
        if (result.Admission
            != ProviderControlCenterNetworkBoundaryAdmission.Accepted
            || result.Reason != ProviderControlCenterNetworkBoundaryReason.Completed)
        {
            return ProviderControlCenterResultAdmission.BoundaryNotAccepted;
        }
        if (!string.Equals(
                result.RequestId,
                request.RequestId,
                StringComparison.Ordinal))
        {
            return ProviderControlCenterResultAdmission.RequestIdentityMismatch;
        }
        return result.Operation == request.Operation
            ? ProviderControlCenterResultAdmission.Accepted
            : ProviderControlCenterResultAdmission.OperationMismatch;
    }
}

internal interface IProviderControlCenterNetworkBoundary
{
    Task<ProviderControlCenterNetworkBoundaryResult> ExecuteAsync(
        ProviderControlCenterNetworkRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SourcePredeploymentDisabledNetworkBoundary :
    IProviderControlCenterNetworkBoundary
{
    internal static SourcePredeploymentDisabledNetworkBoundary Instance { get; } =
        new();

    private SourcePredeploymentDisabledNetworkBoundary()
    {
    }

    public Task<ProviderControlCenterNetworkBoundaryResult> ExecuteAsync(
        ProviderControlCenterNetworkRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderControlCenterNetworkBoundaryResult(
            request.RequestId,
            request.Operation,
            ProviderControlCenterNetworkBoundaryAdmission.LiveAuthorizationRequired,
            ProviderControlCenterNetworkBoundaryReason.SourcePredeploymentDisabled));
    }
}

internal static class ProviderControlCenterStartupPolicy
{
    internal const string LiveProviderArgument = "--provider-live";

    internal static bool EnablesLiveProviderNetwork(IEnumerable<string>? arguments)
        => arguments != null
           && arguments.Any(value => string.Equals(
               value,
               LiveProviderArgument,
               StringComparison.Ordinal));
}

/// <summary>
/// Provider-neutral bounded setup boundary. Every supported Provider uses the
/// same one-request, no-retry catalog transaction and current-user protected
/// credential store. Vendor differences remain inside typed profiles/adapters.
/// </summary>
internal sealed class ProviderControlCenterLiveNetworkBoundary :
    IProviderControlCenterNetworkBoundary,
    IDisposable
{
    private static readonly UnifiedProviderSetupOptions LiveSetupOptions = new(
        enabled: true,
        transactionLifetime: TimeSpan.FromSeconds(10));

    private readonly HttpClient _http;
    private readonly UnifiedProviderControlPlaneStore _store;
    private readonly UnifiedProviderSetupTransactionHost _setupHost;
    private readonly Func<DateTimeOffset> _clock;
    private int _disposed;

    private ProviderControlCenterLiveNetworkBoundary(
        HttpClient http,
        UnifiedProviderControlPlaneStore store,
        UnifiedProviderSetupTransactionHost setupHost,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _setupHost = setupHost ?? throw new ArgumentNullException(nameof(setupHost));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal static ProviderControlCenterLiveNetworkBoundary ForCurrentUser()
    {
        var http = UnifiedProviderCatalogNetworkHttp.CreateClient();
        try
        {
            var store = UnifiedProviderControlPlaneStore.ForCurrentUser();
            var client = new UnifiedProviderCatalogNetworkClient(http);
            var setupHost = new UnifiedProviderSetupTransactionHost(
                new UnifiedProviderSetupConnectedTransportEndpointAdmission(),
                new UnifiedProviderSetupCatalogProbe(client),
                new UnifiedProviderControlPlaneSetupStore(store));
            return new ProviderControlCenterLiveNetworkBoundary(
                http,
                store,
                setupHost);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    public async Task<ProviderControlCenterNetworkBoundaryResult> ExecuteAsync(
        ProviderControlCenterNetworkRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (Volatile.Read(ref _disposed) != 0)
        {
            return Rejected(
                request,
                ProviderControlCenterNetworkBoundaryReason.Faulted);
        }
        if (request is not ProviderControlCenterValidateAndSaveRequest setup)
        {
            // Startup/manual health remains an independent bounded runtime path.
            // Enabling direct setup must never silently add recurring network calls.
            return Rejected(
                request,
                ProviderControlCenterNetworkBoundaryReason.ProtocolRejected);
        }

        UnifiedProviderSetupResult result;
        try
        {
            var setupRequest = new UnifiedProviderSetupRequest(
                setup.RequestId,
                setup.Provider,
                setup.ProviderId,
                setup.InstanceId,
                setup.BaseUri,
                setup.EndpointProfileId,
                setup.WorkspaceId,
                setup.RouteIntent,
                setup.CustomAuthentication,
                setup.DesiredModel,
                setup.ExpectedStoreGeneration,
                setup.CredentialSource);
            result = await _setupHost.RunOnceAsync(
                    setupRequest,
                    LiveSetupOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ProviderControlCenterNetworkBoundaryResult(
                request.RequestId,
                request.Operation,
                ProviderControlCenterNetworkBoundaryAdmission.Cancelled,
                ProviderControlCenterNetworkBoundaryReason.CallerCancelled);
        }
        catch
        {
            return Rejected(
                request,
                ProviderControlCenterNetworkBoundaryReason.Faulted);
        }

        if (!result.Accepted)
        {
            var reason = MapFailure(result);
            return new ProviderControlCenterNetworkBoundaryResult(
                request.RequestId,
                request.Operation,
                reason == ProviderControlCenterNetworkBoundaryReason.CallerCancelled
                    ? ProviderControlCenterNetworkBoundaryAdmission.Cancelled
                    : ProviderControlCenterNetworkBoundaryAdmission.Faulted,
                reason);
        }

        var loaded = _store.Load();
        var instance = loaded.Accepted && loaded.Snapshot != null
            ? loaded.Snapshot.Instances.SingleOrDefault(value =>
                value.ProviderKind == setup.Provider
                && string.Equals(
                    value.InstanceId,
                    setup.InstanceId,
                    StringComparison.Ordinal))
            : null;
        var now = _clock();
        if (instance == null
            || loaded.Snapshot == null
            || loaded.Snapshot.StoreGeneration <= setup.ExpectedStoreGeneration
            || !instance.CatalogIsCurrent(now))
        {
            return Rejected(
                request,
                ProviderControlCenterNetworkBoundaryReason.StaleResult);
        }

        var observation = new ProviderControlCenterSetupObservation(
            ProviderControlCenterSetupObservationState.ValidatedAndSaved,
            setup.Provider,
            setup.ProviderId,
            setup.ProfileId,
            setup.InstanceId,
            setup.ExpectedStoreGeneration,
            loaded.Snapshot.StoreGeneration,
            instance.ConfigurationGeneration,
            instance.ConfigurationFingerprint,
            instance.SelectedModel,
            ProviderConnectionHealth.CatalogAvailable,
            ProviderControlCenterHealthReason.CatalogReachable,
            ProviderControlCenterObservationFreshness.Fresh,
            instance.CatalogObservedAt,
            instance.CatalogExpiresAt);
        return new ProviderControlCenterNetworkBoundaryResult(
            request.RequestId,
            request.Operation,
            ProviderControlCenterNetworkBoundaryAdmission.Accepted,
            ProviderControlCenterNetworkBoundaryReason.Completed,
            setupObservation: observation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _http.Dispose();
    }

    private static ProviderControlCenterNetworkBoundaryResult Rejected(
        ProviderControlCenterNetworkRequest request,
        ProviderControlCenterNetworkBoundaryReason reason)
        => new(
            request.RequestId,
            request.Operation,
            ProviderControlCenterNetworkBoundaryAdmission.Faulted,
            reason);

    private static ProviderControlCenterNetworkBoundaryReason MapFailure(
        UnifiedProviderSetupResult result)
    {
        if (result.CatalogNetworkAdmission.HasValue)
        {
            return result.CatalogNetworkAdmission.Value switch
            {
                ProviderCatalogNetworkAdmission.AuthenticationRejected
                    or ProviderCatalogNetworkAdmission.CredentialRequired
                    or ProviderCatalogNetworkAdmission.CredentialMismatch =>
                        ProviderControlCenterNetworkBoundaryReason.AuthenticationRejected,
                ProviderCatalogNetworkAdmission.RateLimited =>
                    ProviderControlCenterNetworkBoundaryReason.RateLimited,
                ProviderCatalogNetworkAdmission.NetworkFailure
                    or ProviderCatalogNetworkAdmission.ServiceUnavailable =>
                        ProviderControlCenterNetworkBoundaryReason.NetworkUnavailable,
                ProviderCatalogNetworkAdmission.Timeout =>
                    ProviderControlCenterNetworkBoundaryReason.TimedOut,
                ProviderCatalogNetworkAdmission.Cancelled =>
                    ProviderControlCenterNetworkBoundaryReason.CallerCancelled,
                ProviderCatalogNetworkAdmission.ConfigurationRejected
                    or ProviderCatalogNetworkAdmission.ConnectedAddressRejected
                    or ProviderCatalogNetworkAdmission.RedirectRejected =>
                        ProviderControlCenterNetworkBoundaryReason.EndpointRejected,
                _ => ProviderControlCenterNetworkBoundaryReason.ProtocolRejected
            };
        }

        return result.Admission switch
        {
            UnifiedProviderSetupAdmission.Cancelled =>
                ProviderControlCenterNetworkBoundaryReason.CallerCancelled,
            UnifiedProviderSetupAdmission.TimedOut =>
                ProviderControlCenterNetworkBoundaryReason.TimedOut,
            UnifiedProviderSetupAdmission.DuplicateTransaction
                or UnifiedProviderSetupAdmission.TransactionCapacityReached
                or UnifiedProviderSetupAdmission.ConcurrentTransactionRejected =>
                    ProviderControlCenterNetworkBoundaryReason.CapacityUnavailable,
            UnifiedProviderSetupAdmission.EndpointRejected
                or UnifiedProviderSetupAdmission.ConnectedAddressEvidenceRequired =>
                    ProviderControlCenterNetworkBoundaryReason.EndpointRejected,
            UnifiedProviderSetupAdmission.CredentialRejected =>
                ProviderControlCenterNetworkBoundaryReason.AuthenticationRejected,
            UnifiedProviderSetupAdmission.StaleConfiguration =>
                ProviderControlCenterNetworkBoundaryReason.StaleResult,
            UnifiedProviderSetupAdmission.ProviderIdentityRejected
                or UnifiedProviderSetupAdmission.CatalogIdentityRejected =>
                    ProviderControlCenterNetworkBoundaryReason.IdentityMismatch,
            UnifiedProviderSetupAdmission.Faulted =>
                ProviderControlCenterNetworkBoundaryReason.Faulted,
            _ => ProviderControlCenterNetworkBoundaryReason.ProtocolRejected
        };
    }
}

internal enum AiFunctionId
{
    Invalid = 0,
    TextConversation = 1,
    SpeechRecognition = 2,
    ComputerActionPlanning = 3,
    HigherReasoning = 4,
    LocalSpeechSynthesis = 5
}

internal enum AiFunctionAssignmentState
{
    Invalid = 0,
    Missing = 1,
    ConfiguredDesired = 2,
    LocalService = 3
}

internal enum AiProviderSetupState
{
    Invalid = 0,
    SetupSupported = 1,
    RuntimePresentControlCenterPending = 2,
    Planned = 3
}

internal enum AiProviderUiGroup
{
    Invalid = 0,
    Cloud = 1,
    LocalAndSelfHosted = 2
}

internal enum AiProviderAuthRequirement
{
    Invalid = 0,
    ApiKeyRequired = 1,
    EndpointDefined = 2,
    NoCredentialLocal = 3
}

internal enum AiModelExecutionCompatibility
{
    Invalid = 0,
    Unknown = 1,
    UserSelectedTextRoute = 2,
    VerifiedTextGeneration = 3
}

internal enum AiProviderHealthLevel
{
    Invalid = 0,
    Unconfigured = 1,
    Checking = 2,
    Healthy = 3,
    Attention = 4,
    Unavailable = 5
}

internal sealed class AiModelCapabilityBadgeVm
{
    public string Label { get; }
    public string EvidenceLabel { get; }
    public string Color { get; }
    internal ProviderModelCapabilityKind? CapabilityKind { get; }
    internal AiModelExecutionCompatibility? ExecutionCompatibility { get; }
    internal bool IsCurrent { get; }

    internal AiModelCapabilityBadgeVm(
        string label,
        string evidenceLabel,
        string color,
        bool isCurrent,
        ProviderModelCapabilityKind? capabilityKind = null,
        AiModelExecutionCompatibility? executionCompatibility = null)
    {
        Label = Text(label, 96, nameof(label));
        EvidenceLabel = Text(evidenceLabel, 128, nameof(evidenceLabel));
        Color = Text(color, 16, nameof(color));
        IsCurrent = isCurrent;
        if (capabilityKind.HasValue
            && (!Enum.IsDefined(typeof(ProviderModelCapabilityKind), capabilityKind.Value)
                || capabilityKind.Value == ProviderModelCapabilityKind.Invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityKind));
        }
        if (executionCompatibility.HasValue
            && (!Enum.IsDefined(
                    typeof(AiModelExecutionCompatibility),
                    executionCompatibility.Value)
                || executionCompatibility.Value
                    == AiModelExecutionCompatibility.Invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(executionCompatibility));
        }
        CapabilityKind = capabilityKind;
        ExecutionCompatibility = executionCompatibility;
    }

    private static string Text(string? value, int maximum, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException("A bounded badge value is required.", parameterName);
        return value.Trim();
    }
}

internal sealed class AiConfiguredModelChoiceVm
{
    internal string InstanceId { get; }
    internal string ProviderId { get; }
    internal string ProviderDisplayName { get; }
    internal string ModelId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderControlKind ProviderKind { get; }
    internal bool AcceptsAudio { get; }
    internal bool AcceptsImages { get; }
    internal bool SupportsTools { get; }
    internal bool ProducesImages { get; }
    internal D045AgentToolModelCompatibility AgentToolCompatibility { get; }
    public string DisplayText => ProviderDisplayName
        + " · "
        + ModelId
        + " · "
        + InstanceId
        + AgentToolCompatibilitySuffix(AgentToolCompatibility);

    internal AiConfiguredModelChoiceVm(
        string instanceId,
        string providerId,
        string providerDisplayName,
        string modelId,
        long configurationGeneration,
        ProviderControlKind providerKind,
        bool acceptsAudio,
        bool acceptsImages,
        bool supportsTools,
        bool producesImages = false,
        D045AgentToolModelCompatibility agentToolCompatibility =
            D045AgentToolModelCompatibility.NotApplicable)
    {
        InstanceId = Text(instanceId, 128, nameof(instanceId));
        ProviderId = Text(providerId, 64, nameof(providerId));
        ProviderDisplayName = Text(
            providerDisplayName,
            128,
            nameof(providerDisplayName));
        ModelId = Text(modelId, 256, nameof(modelId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        ProviderKind = providerKind;
        AcceptsAudio = acceptsAudio;
        AcceptsImages = acceptsImages;
        SupportsTools = supportsTools;
        ProducesImages = producesImages;
        if (!Enum.IsDefined(
                typeof(D045AgentToolModelCompatibility),
                agentToolCompatibility)
            || agentToolCompatibility
                == D045AgentToolModelCompatibility.Invalid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(agentToolCompatibility));
        }
        AgentToolCompatibility = agentToolCompatibility;
    }

    public override string ToString() => DisplayText;

    private static string AgentToolCompatibilitySuffix(
        D045AgentToolModelCompatibility compatibility)
        => compatibility switch
        {
            D045AgentToolModelCompatibility.CatalogAdvertised =>
                " · Agent 工具：模型目錄已宣告",
            D045AgentToolModelCompatibility
                .RuntimeAdapterCatalogUnverified =>
                " · Agent 工具：未由模型目錄驗證",
            D045AgentToolModelCompatibility.Ineligible =>
                " · Agent 工具：不相容",
            _ => string.Empty
        };

    private static string Text(string? value, int maximum, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException("A bounded display value is required.", parameterName);
        return value.Trim();
    }
}

internal sealed class AiFunctionCardVm
{
    internal AiFunctionId FunctionId { get; }
    internal string DisplayName { get; }
    internal string Assignment { get; }
    internal string Detail { get; }
    internal AiFunctionAssignmentState State { get; }
    internal bool CanChooseConfiguredModel { get; }
    internal IReadOnlyList<string> CompatibleConfiguredModels { get; }
    internal IReadOnlyList<AiConfiguredModelChoiceVm> CompatibleConfiguredModelChoices { get; }

    internal AiFunctionCardVm(
        AiFunctionId functionId,
        string displayName,
        string assignment,
        string detail,
        AiFunctionAssignmentState state,
        bool canChooseConfiguredModel,
        IEnumerable<AiConfiguredModelChoiceVm>? compatibleConfiguredModels = null)
    {
        FunctionId = Defined(functionId, nameof(functionId));
        State = Defined(state, nameof(state));
        DisplayName = Text(displayName, 256, nameof(displayName));
        Assignment = Text(assignment, 512, nameof(assignment));
        Detail = Text(detail, 1024, nameof(detail));
        var models = (compatibleConfiguredModels
                ?? Array.Empty<AiConfiguredModelChoiceVm>())
            .OrderBy(value => value.ProviderDisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.ModelId, StringComparer.Ordinal)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (canChooseConfiguredModel != (models.Length > 0))
        {
            throw new ArgumentException(
                "A selectable function must have configured models.",
                nameof(canChooseConfiguredModel));
        }
        CanChooseConfiguredModel = canChooseConfiguredModel;
        CompatibleConfiguredModelChoices = Array.AsReadOnly(models);
        CompatibleConfiguredModels = Array.AsReadOnly(
            models.Select(value => value.ModelId).ToArray());
    }

    private static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    private static string Text(string? value, int maximum, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException("A bounded display value is required.", parameterName);
        return value.Trim();
    }
}

internal sealed class AiProviderModelVm
{
    public string ModelId { get; }
    public bool IsConfiguredSelection { get; }
    public string CapabilityEvidence { get; }
    public IReadOnlyList<AiModelCapabilityBadgeVm> CapabilityBadges { get; }
    public AiModelExecutionCompatibility ExecutionCompatibility { get; }
    public bool CanSelectForText { get; }

    internal bool HasCurrentCapability(ProviderModelCapabilityKind kind)
        => CapabilityBadges.Any(value => value.IsCurrent
            && value.CapabilityKind == kind);

    internal AiProviderModelVm(
        string modelId,
        bool isConfiguredSelection,
        AiModelExecutionCompatibility executionCompatibility,
        ProviderCapabilityState advertisedState)
        : this(
            modelId,
            isConfiguredSelection,
            executionCompatibility,
            Array.Empty<UnifiedProviderCatalogCapabilityEvidence>(),
            catalogIsCurrent: true)
    {
        _ = advertisedState;
    }

    internal AiProviderModelVm(
        string modelId,
        bool isConfiguredSelection,
        AiModelExecutionCompatibility executionCompatibility,
        IEnumerable<UnifiedProviderCatalogCapabilityEvidence>?
            catalogCapabilityEvidence,
        bool catalogIsCurrent)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 256)
            throw new ArgumentException("A bounded model identifier is required.", nameof(modelId));
        ModelId = modelId.Trim();
        IsConfiguredSelection = isConfiguredSelection;
        ExecutionCompatibility = executionCompatibility;
        var evidence = (catalogCapabilityEvidence
                ?? Array.Empty<UnifiedProviderCatalogCapabilityEvidence>())
            .Where(value => string.Equals(
                value.ModelId,
                ModelId,
                StringComparison.Ordinal))
            .OrderBy(value => value.Kind)
            .ToArray();
        var badges = new List<AiModelCapabilityBadgeVm>();
        if (executionCompatibility
            == AiModelExecutionCompatibility.VerifiedTextGeneration)
        {
            badges.Add(new AiModelCapabilityBadgeVm(
                "文字執行已驗證",
                "實機執行證據",
                "#1D795A",
                isCurrent: true,
                executionCompatibility: executionCompatibility));
        }
        else if (executionCompatibility
                 == AiModelExecutionCompatibility.UserSelectedTextRoute)
        {
            badges.Add(new AiModelCapabilityBadgeVm(
                "已選文字路由",
                "Desired 設定；尚非執行驗證",
                "#B93343",
                isCurrent: true,
                executionCompatibility: executionCompatibility));
        }
        foreach (var item in evidence)
        {
            badges.Add(new AiModelCapabilityBadgeVm(
                CapabilityLabel(item.Kind),
                catalogIsCurrent
                    ? ProvenanceLabel(item.Provenance)
                    : "宣告已過期",
                catalogIsCurrent ? "#936110" : "#52627A",
                catalogIsCurrent,
                capabilityKind: item.Kind));
        }
        if (evidence.Length == 0)
        {
            badges.Add(new AiModelCapabilityBadgeVm(
                "能力未知",
                "沒有逐模型官方能力證據",
                "#52627A",
                catalogIsCurrent));
        }
        CapabilityBadges = Array.AsReadOnly(badges.ToArray());
        CanSelectForText = executionCompatibility is
                AiModelExecutionCompatibility.UserSelectedTextRoute
                or AiModelExecutionCompatibility.VerifiedTextGeneration
            || (catalogIsCurrent
                && UnifiedProviderCatalogCapabilityRules
                    .AdvertisesTextConversation(evidence, ModelId));
        CapabilityEvidence = executionCompatibility switch
        {
            AiModelExecutionCompatibility.VerifiedTextGeneration =>
                evidence.Length > 0
                ? "文字執行已驗證；其他徽章只代表官方宣告"
                : "文字執行已驗證；其他能力仍為未知",
            AiModelExecutionCompatibility.UserSelectedTextRoute =>
                evidence.Length > 0
                ? "已選為文字路由；能力徽章不等於執行驗證"
                : "已選為文字路由；模型能力仍待實機驗證",
            _ when evidence.Length > 0 && catalogIsCurrent =>
                "只顯示官方逐模型宣告；不代表已完成執行驗證",
            _ when evidence.Length > 0 =>
                "逐模型宣告已過期；需重新取得目錄",
            _ => "目錄只證明模型可見；不從模型名稱推測能力"
        };
    }

    private static string CapabilityLabel(ProviderModelCapabilityKind kind)
        => kind switch
        {
            ProviderModelCapabilityKind.TextInput => "文字輸入",
            ProviderModelCapabilityKind.ImageInput => "圖片輸入",
            ProviderModelCapabilityKind.AudioInput => "音訊輸入",
            ProviderModelCapabilityKind.VideoInput => "影片輸入",
            ProviderModelCapabilityKind.FileInput => "檔案輸入",
            ProviderModelCapabilityKind.TextOutput => "文字輸出",
            ProviderModelCapabilityKind.ImageOutput => "圖片輸出",
            ProviderModelCapabilityKind.AudioOutput => "音訊輸出",
            ProviderModelCapabilityKind.ToolCalling => "工具呼叫",
            ProviderModelCapabilityKind.StructuredOutput => "結構化輸出",
            ProviderModelCapabilityKind.Streaming => "串流輸出",
            ProviderModelCapabilityKind.TextGeneration => "文字生成",
            ProviderModelCapabilityKind.Embeddings => "向量嵌入",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string ProvenanceLabel(ProviderCapabilityProvenance provenance)
        => provenance switch
        {
            ProviderCapabilityProvenance.OfficialModelCatalog => "官方目錄宣告",
            ProviderCapabilityProvenance.OfficialModelMetadataEndpoint =>
                "官方 Metadata 宣告",
            _ => throw new ArgumentOutOfRangeException(nameof(provenance))
        };
}

internal sealed class AiConnectedProviderVm
{
    public string InstanceId { get; }
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string CredentialState { get; }
    public string SelectedModel { get; }
    public long ConfigurationGeneration { get; }
    public DateTimeOffset CatalogObservedAt { get; }
    public DateTimeOffset CatalogExpiresAt { get; }
    public IReadOnlyList<AiProviderModelVm> Models { get; }
    public bool CatalogIsCurrent { get; }
    public string CatalogSummary { get; }
    public string CredentialHealthSummary { get; }
    public string CatalogHealthSummary { get; }
    public string RuntimeHealthSummary { get; }
    public string OverallHealthSummary { get; }
    public string HealthColor { get; }
    internal ProviderControlKind ProviderKind { get; }
    internal AiProviderHealthLevel HealthLevel { get; }

    internal AiConnectedProviderVm(
        UnifiedProviderConfiguredInstance source,
        ProviderConnectionHealth health,
        UnifiedProviderTextAssignment? textAssignment,
        DateTimeOffset now)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        var profile = UnifiedProviderProfileRegistry.Get(source.ProviderKind);
        InstanceId = source.InstanceId;
        ProviderKind = source.ProviderKind;
        ProviderId = profile.ProviderId;
        DisplayName = profile.DisplayName;
        CredentialState = profile.CredentialRequirement
            == ProviderCredentialRequirement.NotRequired
                ? "此服務不需要 API Key"
                : "請在上方查看 API Key 狀態";
        SelectedModel = source.SelectedModel;
        ConfigurationGeneration = source.ConfigurationGeneration;
        CatalogObservedAt = source.CatalogObservedAt;
        CatalogExpiresAt = source.CatalogExpiresAt;
        CatalogIsCurrent = source.CatalogIsCurrent(now);
        var selectedIsTextRoute = textAssignment?.Matches(source) == true;
        Models = Array.AsReadOnly(source.CatalogModels
            .Select(value => new AiProviderModelVm(
                value,
                string.Equals(value, source.SelectedModel, StringComparison.Ordinal),
                selectedIsTextRoute
                    && string.Equals(value, source.SelectedModel, StringComparison.Ordinal)
                    ? health == ProviderConnectionHealth.GenerationVerified
                        ? AiModelExecutionCompatibility.VerifiedTextGeneration
                        : AiModelExecutionCompatibility.UserSelectedTextRoute
                    : AiModelExecutionCompatibility.Unknown,
                source.CatalogCapabilityEvidence,
                CatalogIsCurrent))
            .ToArray());
        CatalogSummary = $"{Models.Count} 個模型 · "
            + CatalogObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        CredentialHealthSummary = profile.CredentialRequirement
            == ProviderCredentialRequirement.NotRequired
                ? "API Key：此服務不需要"
                : "API Key：請查看上方設定";
        CatalogHealthSummary = CatalogIsCurrent
            ? "模型清單：仍在有效期限內"
            : "模型清單：已過期";
        RuntimeHealthSummary = HealthText(health);
        (HealthLevel, OverallHealthSummary, HealthColor) = HealthPresentation(
            health,
            configured: true,
            CatalogIsCurrent);
    }

    internal AiConnectedProviderVm(
        AiFunctionsDeepSeekInput source,
        DateTimeOffset now)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        InstanceId = "legacy-deepseek-v1";
        ProviderKind = ProviderControlKind.DeepSeek;
        ProviderId = "deepseek";
        DisplayName = "DeepSeek";
        CredentialState = "請在上方查看 API Key 狀態";
        SelectedModel = source.SelectedModel;
        ConfigurationGeneration = source.ConfigurationGeneration;
        CatalogObservedAt = source.CatalogObservedAt;
        CatalogExpiresAt = source.CatalogExpiresAt;
        CatalogIsCurrent = now >= CatalogObservedAt && now < CatalogExpiresAt;
        Models = Array.AsReadOnly(source.CatalogModels
            .Select(value => new AiProviderModelVm(
                value,
                string.Equals(value, SelectedModel, StringComparison.Ordinal),
                AiFunctionsPageProjection.IsVerifiedLegacyDeepSeekTextModel(value)
                    ? AiModelExecutionCompatibility.VerifiedTextGeneration
                    : AiModelExecutionCompatibility.Unknown,
                ProviderCapabilityState.Unknown))
            .ToArray());
        CatalogSummary = $"{Models.Count} 個模型 · 舊版設定";
        CredentialHealthSummary = "API Key：請查看上方設定";
        CatalogHealthSummary = CatalogIsCurrent
            ? "模型清單：仍在有效期限內"
            : "模型清單：已過期";
        RuntimeHealthSummary = "舊版設定僅供查看，未進行連線檢查";
        HealthLevel = AiProviderHealthLevel.Unconfigured;
        OverallHealthSummary = "舊版設定，僅供查看";
        HealthColor = "#52627A";
    }

    private static string HealthText(ProviderConnectionHealth health)
        => health switch
        {
            ProviderConnectionHealth.CatalogAvailable => "連線：可讀取模型清單",
            ProviderConnectionHealth.GenerationVerified => "連線：已確認可生成文字",
            ProviderConnectionHealth.AuthenticationRejected => "連線：驗證未通過",
            ProviderConnectionHealth.EndpointRejected => "連線：服務位址未通過安全檢查",
            ProviderConnectionHealth.RateLimited => "連線：請求過於頻繁，請稍後再試",
            ProviderConnectionHealth.NetworkFailure => "連線：目前無法連線",
            ProviderConnectionHealth.Degraded => "連線：暫時無法確認",
            ProviderConnectionHealth.ReadyToValidate => "連線：尚未檢查",
            ProviderConnectionHealth.Disabled => "連線：檢查已停用",
            _ => "連線：尚未檢查"
        };

    internal static (AiProviderHealthLevel Level, string Text, string Color)
        HealthPresentation(
            ProviderConnectionHealth health,
            bool configured,
            bool catalogCurrent)
    {
        if (!configured)
            return (AiProviderHealthLevel.Unconfigured, "未設定", "#52627A");
        return health switch
        {
            ProviderConnectionHealth.CatalogAvailable =>
                (AiProviderHealthLevel.Healthy, "模型目錄可達", "#1D795A"),
            ProviderConnectionHealth.GenerationVerified =>
                (AiProviderHealthLevel.Healthy, "文字生成已驗證", "#1D795A"),
            ProviderConnectionHealth.AuthenticationRejected
                or ProviderConnectionHealth.EndpointRejected =>
                (AiProviderHealthLevel.Unavailable, "連線失敗", "#BD354B"),
            ProviderConnectionHealth.RateLimited
                or ProviderConnectionHealth.NetworkFailure
                or ProviderConnectionHealth.Degraded =>
                (AiProviderHealthLevel.Attention, "需要注意", "#936110"),
            ProviderConnectionHealth.ReadyToValidate =>
                (AiProviderHealthLevel.Checking, "尚未檢查", "#52627A"),
            ProviderConnectionHealth.Disabled =>
                (AiProviderHealthLevel.Unconfigured, "檢查已停用", "#52627A"),
            _ when !catalogCurrent =>
                (AiProviderHealthLevel.Attention, "等待重新確認", "#936110"),
            _ => (AiProviderHealthLevel.Checking, "尚未檢查", "#52627A")
        };
    }
}

internal sealed class AiProviderSetupDescriptor
{
    internal ProviderControlKind ProviderKind { get; }
    internal string ProviderId { get; }
    internal string DisplayName { get; }
    internal AiProviderUiGroup UiGroup { get; }
    internal AiProviderSetupState State { get; }
    internal AiProviderAuthRequirement AuthRequirement { get; }
    internal string EndpointSummary { get; }
    internal string CatalogSummary { get; }
    internal string DisabledReason { get; }

    internal AiProviderSetupDescriptor(
        ProviderControlKind providerKind,
        string providerId,
        string displayName,
        AiProviderUiGroup uiGroup,
        AiProviderSetupState state,
        AiProviderAuthRequirement authRequirement,
        string endpointSummary,
        string catalogSummary,
        string? disabledReason)
    {
        ProviderKind = providerKind;
        ProviderId = providerId;
        DisplayName = displayName;
        UiGroup = uiGroup;
        State = state;
        AuthRequirement = authRequirement;
        EndpointSummary = endpointSummary;
        CatalogSummary = catalogSummary;
        DisabledReason = disabledReason?.Trim() ?? string.Empty;
        if (state == AiProviderSetupState.SetupSupported
            && DisabledReason.Length != 0)
        {
            throw new ArgumentException(
                "An available setup option cannot have a disabled reason.",
                nameof(disabledReason));
        }
        if (state != AiProviderSetupState.SetupSupported
            && DisabledReason.Length == 0)
        {
            throw new ArgumentException(
                "An unavailable setup option must explain why it is disabled.",
                nameof(disabledReason));
        }
    }

    // D-030 compatibility constructor for deterministic descriptor tests.
    internal AiProviderSetupDescriptor(
        string providerId,
        string displayName,
        AiProviderUiGroup uiGroup,
        AiProviderSetupState state,
        AiProviderAuthRequirement authRequirement,
        string endpointSummary,
        string catalogSummary,
        string? disabledReason)
        : this(
            ProviderControlKind.DeepSeek,
            providerId,
            displayName,
            uiGroup,
            state,
            authRequirement,
            endpointSummary,
            catalogSummary,
            disabledReason)
    {
    }
}

internal sealed class AiProviderSetupOptionVm
{
    public string ProviderId { get; }
    public string DisplayName { get; }
    public string StatusSummary { get; }
    public string AuthSummary { get; }
    public string EndpointSummary { get; }
    public string CatalogSummary { get; }
    public string DisabledReason { get; }
    public bool CanOpenSetup { get; }
    public string HealthSummary { get; }
    public string HealthColor { get; }
    internal ProviderControlKind ProviderKind { get; }
    internal AiProviderUiGroup UiGroup { get; }
    internal AiProviderSetupState State { get; }
    internal AiProviderAuthRequirement AuthRequirement { get; }
    internal AiProviderHealthLevel HealthLevel { get; }
    internal string Detail { get; }

    internal AiProviderSetupOptionVm(
        AiProviderSetupDescriptor descriptor,
        IReadOnlyList<AiConnectedProviderVm> connected)
    {
        ProviderKind = descriptor.ProviderKind;
        ProviderId = descriptor.ProviderId;
        DisplayName = descriptor.DisplayName;
        UiGroup = descriptor.UiGroup;
        State = descriptor.State;
        AuthRequirement = descriptor.AuthRequirement;
        StatusSummary = State == AiProviderSetupState.SetupSupported
            ? "可設定（尚未探測）"
            : State == AiProviderSetupState.RuntimePresentControlCenterPending
                ? "Control Center 尚未接入"
                : "規劃中";
        AuthSummary = AuthRequirement switch
        {
            AiProviderAuthRequirement.ApiKeyRequired => "需要 API Key",
            AiProviderAuthRequirement.EndpointDefined => "依端點設定",
            AiProviderAuthRequirement.NoCredentialLocal => "本機預設免 API Key",
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
        };
        EndpointSummary = descriptor.EndpointSummary;
        CatalogSummary = descriptor.CatalogSummary;
        DisabledReason = descriptor.DisabledReason;
        CanOpenSetup = State == AiProviderSetupState.SetupSupported;
        var matching = connected
            .Where(value => value.ProviderKind == ProviderKind)
            .ToArray();
        var health = matching.OrderBy(value => HealthRank(value.HealthLevel))
            .FirstOrDefault();
        if (health == null)
        {
            HealthLevel = AiProviderHealthLevel.Unconfigured;
            HealthSummary = "未設定";
            HealthColor = "#52627A";
        }
        else
        {
            HealthLevel = health.HealthLevel;
            HealthSummary = health.OverallHealthSummary;
            HealthColor = health.HealthColor;
        }
        Detail = string.Join(
            "；",
            new[] { EndpointSummary, CatalogSummary, DisabledReason }
                .Where(value => value.Length != 0));
    }

    private static int HealthRank(AiProviderHealthLevel value)
        => value switch
        {
            AiProviderHealthLevel.Unavailable => 0,
            AiProviderHealthLevel.Attention => 1,
            AiProviderHealthLevel.Checking => 2,
            AiProviderHealthLevel.Healthy => 3,
            _ => 4
        };
}

internal sealed class AiFunctionsDeepSeekInput
{
    internal long ConfigurationGeneration { get; }
    internal string SelectedModel { get; }
    internal IReadOnlyList<string> CatalogModels { get; }
    internal DateTimeOffset CatalogObservedAt { get; }
    internal DateTimeOffset CatalogExpiresAt { get; }
    internal bool CredentialConfigured { get; }

    internal AiFunctionsDeepSeekInput(
        long configurationGeneration,
        string selectedModel,
        IEnumerable<string> catalogModels,
        DateTimeOffset catalogObservedAt,
        DateTimeOffset catalogExpiresAt,
        bool credentialConfigured)
    {
        ConfigurationGeneration = configurationGeneration;
        SelectedModel = selectedModel ?? string.Empty;
        CatalogModels = new ReadOnlyCollection<string>(
            (catalogModels ?? Array.Empty<string>()).ToArray());
        CatalogObservedAt = catalogObservedAt;
        CatalogExpiresAt = catalogExpiresAt;
        CredentialConfigured = credentialConfigured;
    }
}

internal sealed class AiFunctionsControlPlaneInput
{
    internal UnifiedProviderControlPlaneSnapshot? Snapshot { get; }
    internal IReadOnlyDictionary<string, ProviderConnectionHealth> HealthByInstance { get; }
    internal AiFunctionsDeepSeekInput? LegacyDeepSeek { get; }
    internal D045AiFunctionRoutingSnapshot? FunctionRouting { get; }

    internal AiFunctionsControlPlaneInput(
        UnifiedProviderControlPlaneSnapshot? snapshot,
        IReadOnlyDictionary<string, ProviderConnectionHealth>? healthByInstance,
        AiFunctionsDeepSeekInput? legacyDeepSeek,
        D045AiFunctionRoutingSnapshot? functionRouting = null)
    {
        Snapshot = snapshot;
        HealthByInstance = new ReadOnlyDictionary<string, ProviderConnectionHealth>(
            new Dictionary<string, ProviderConnectionHealth>(
                healthByInstance ?? new Dictionary<string, ProviderConnectionHealth>(),
                StringComparer.Ordinal));
        LegacyDeepSeek = legacyDeepSeek;
        FunctionRouting = functionRouting;
    }
}

internal sealed class AiFunctionsPageProjection
{
    private static readonly string[] VerifiedLegacyDeepSeekTextModels =
    {
        "deepseek-v4-flash",
        "deepseek-v4-pro"
    };

    internal IReadOnlyList<AiFunctionCardVm> Functions { get; }
    internal IReadOnlyList<AiConnectedProviderVm> ConnectedProviders { get; }
    internal IReadOnlyList<AiProviderSetupOptionVm> SetupOptions { get; }
    internal IReadOnlyList<AiConfiguredModelChoiceVm>
        HigherReasoningImageGenerationChoices { get; }

    private AiFunctionsPageProjection(
        IReadOnlyList<AiFunctionCardVm> functions,
        IReadOnlyList<AiConnectedProviderVm> connectedProviders,
        IReadOnlyList<AiProviderSetupOptionVm> setupOptions,
        IReadOnlyList<AiConfiguredModelChoiceVm> imageGenerationChoices)
    {
        Functions = functions;
        ConnectedProviders = connectedProviders;
        SetupOptions = setupOptions;
        HigherReasoningImageGenerationChoices = imageGenerationChoices;
    }

    internal static AiFunctionsPageProjection Build(
        AiFunctionsDeepSeekInput? deepSeek,
        DateTimeOffset now)
        => BuildControlPlane(
            new AiFunctionsControlPlaneInput(null, null, deepSeek),
            now);

    internal static AiFunctionsPageProjection BuildControlPlane(
        AiFunctionsControlPlaneInput input,
        DateTimeOffset now)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        var connected = new List<AiConnectedProviderVm>();
        if (input.Snapshot != null)
        {
            connected.AddRange(input.Snapshot.Instances.Select(instance =>
                new AiConnectedProviderVm(
                    instance,
                    input.HealthByInstance.TryGetValue(
                        instance.InstanceId,
                        out var health)
                        ? health
                        : ProviderConnectionHealth.Unknown,
                    input.Snapshot.TextAssignment,
                    now)));
        }
        connected = connected
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToList();

        var configuredInstances = input.Snapshot?.Instances.ToDictionary(
            value => value.InstanceId,
            StringComparer.Ordinal)
            ?? new Dictionary<string, UnifiedProviderConfiguredInstance>(
                StringComparer.Ordinal);
        var choices = connected
            .SelectMany(provider => provider.Models
                .Where(model => configuredInstances.TryGetValue(
                        provider.InstanceId,
                        out var configured)
                    ? D045WorkbenchModelCatalogRules
                        .IsSelectableLiveTextModel(configured, model.ModelId)
                    : model.CanSelectForText)
                .Select(model => new AiConfiguredModelChoiceVm(
                    provider.InstanceId,
                    provider.ProviderId,
                    provider.DisplayName,
                    model.ModelId,
                    provider.ConfigurationGeneration,
                    provider.ProviderKind,
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.AudioInput),
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.ImageInput),
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.ToolCalling),
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.ImageOutput))))
            .ToArray();
        var workbenchChoices = choices;
        var reasoningChoices = choices
            .Where(value => configuredInstances.TryGetValue(
                    value.InstanceId,
                    out var configured)
                && D045WorkbenchModelCatalogRules
                    .IsSelectableHigherReasoningModel(
                        configured,
                        value.ModelId,
                        now))
            .Select(value =>
            {
                var configured = configuredInstances[value.InstanceId];
                return new AiConfiguredModelChoiceVm(
                    value.InstanceId,
                    value.ProviderId,
                    value.ProviderDisplayName,
                    value.ModelId,
                    value.ConfigurationGeneration,
                    value.ProviderKind,
                    value.AcceptsAudio,
                    value.AcceptsImages,
                    value.SupportsTools,
                    value.ProducesImages,
                    D045WorkbenchModelCatalogRules
                        .GetAgentToolModelCompatibility(
                            configured,
                            value.ModelId,
                            now));
            })
            .ToArray();
        var planningChoices = reasoningChoices;
        var imageGenerationChoices = connected
            .Where(provider => provider.ProviderKind
                == ProviderControlKind.GoogleGemini)
            .SelectMany(provider => provider.Models
                .Where(model =>
                    UnifiedProviderCatalogAdapters.IsSupportedGeminiNativeImageModel(
                        model.ModelId))
                .Select(model => new AiConfiguredModelChoiceVm(
                    provider.InstanceId,
                    provider.ProviderId,
                    provider.DisplayName,
                    model.ModelId,
                    provider.ConfigurationGeneration,
                    provider.ProviderKind,
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.AudioInput),
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.ImageInput),
                    model.HasCurrentCapability(
                        ProviderModelCapabilityKind.ToolCalling),
                    producesImages: true)))
            .ToArray();
        var assigned = ResolveAssigned(
            D045AiFunctionKind.TextConversation,
            input.Snapshot,
            input.FunctionRouting,
            connected,
            workbenchChoices);
        var textFunction = assigned == null
            ? new AiFunctionCardVm(
                AiFunctionId.TextConversation,
                "文字對話",
                "尚未設定模型",
                workbenchChoices.Length == 0
                    ? "先連接一個可用的文字生成模型。"
                    : "選擇這項功能要使用的模型。",
                AiFunctionAssignmentState.Missing,
                workbenchChoices.Length > 0,
                workbenchChoices)
            : new AiFunctionCardVm(
                AiFunctionId.TextConversation,
                "文字對話",
                assigned.DisplayText,
                "這是文字對話卡片的獨立模型；不會覆寫 F7 的日常對話模型。",
                AiFunctionAssignmentState.ConfiguredDesired,
                workbenchChoices.Length > 0,
                workbenchChoices);
        var functions = new[]
        {
            textFunction,
            new AiFunctionCardVm(
                AiFunctionId.SpeechRecognition,
                "語音辨識",
                "本機語音辨識",
                "F6 的語音會先在本機轉成文字，再由目前的預設模型回覆。",
                AiFunctionAssignmentState.LocalService,
                false),
            RoutedFunction(
                AiFunctionId.ComputerActionPlanning,
                D045AiFunctionKind.ComputerActionPlanning,
                "電腦操作規劃",
                "模型負責規劃；每項操作前會檢查權限，完成後確認結果。",
                planningChoices,
                input.Snapshot,
                input.FunctionRouting),
            RoutedFunction(
                AiFunctionId.HigherReasoning,
                D045AiFunctionKind.HigherReasoning,
                "上位思考",
                "文字、圖片、相關莉莉絲記憶與 F7 電腦任務規劃會交給這個模型整合處理。",
                reasoningChoices,
                input.Snapshot,
                input.FunctionRouting),
            new AiFunctionCardVm(
                AiFunctionId.LocalSpeechSynthesis,
                "語音輸出",
                "本機語音合成",
                "不使用大型語言模型；服務健康由本機語音系統另行回報。",
                AiFunctionAssignmentState.LocalService,
                false)
        };
        var descriptors = UnifiedProviderProfileRegistry.All
            .Select(Descriptor)
            .Select(value => new AiProviderSetupOptionVm(value, connected))
            .ToArray();
        return new AiFunctionsPageProjection(
            Array.AsReadOnly(functions),
            Array.AsReadOnly(connected.ToArray()),
            Array.AsReadOnly(descriptors),
            Array.AsReadOnly(imageGenerationChoices));
    }

    private static AiFunctionCardVm RoutedFunction(
        AiFunctionId functionId,
        D045AiFunctionKind functionKind,
        string displayName,
        string configuredDetail,
        IReadOnlyList<AiConfiguredModelChoiceVm> choices,
        UnifiedProviderControlPlaneSnapshot? providers,
        D045AiFunctionRoutingSnapshot? routing)
    {
        var assigned = ResolveAssigned(
            functionKind,
            providers,
            routing,
            Array.Empty<AiConnectedProviderVm>(),
            choices);
        return assigned == null
            ? new AiFunctionCardVm(
                functionId,
                displayName,
                "尚未指派",
                choices.Count == 0
                    ? "目前沒有已連接且具備這項功能證據的模型。"
                    : "選擇一個已連接模型作為這項功能的獨立路由。",
                AiFunctionAssignmentState.Missing,
                choices.Count > 0,
                choices)
            : new AiFunctionCardVm(
                functionId,
                displayName,
                assigned.DisplayText,
                configuredDetail,
                AiFunctionAssignmentState.ConfiguredDesired,
                choices.Count > 0,
                choices);
    }

    private static AiConfiguredModelChoiceVm? ResolveAssigned(
        D045AiFunctionKind functionKind,
        UnifiedProviderControlPlaneSnapshot? snapshot,
        D045AiFunctionRoutingSnapshot? routing,
        IReadOnlyList<AiConnectedProviderVm> connected,
        IReadOnlyList<AiConfiguredModelChoiceVm> choices)
    {
        var route = routing?.Find(functionKind);
        if (route != null)
        {
            return choices.SingleOrDefault(value =>
                string.Equals(value.InstanceId, route.InstanceId, StringComparison.Ordinal)
                && string.Equals(value.ModelId, route.ModelId, StringComparison.Ordinal)
                && value.ConfigurationGeneration == route.ConfigurationGeneration);
        }
        if (functionKind == D045AiFunctionKind.TextConversation
            && snapshot?.TextAssignment != null)
        {
            return choices.SingleOrDefault(value =>
                string.Equals(
                    value.InstanceId,
                    snapshot.TextAssignment.InstanceId,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.ModelId,
                    snapshot.TextAssignment.ModelId,
                    StringComparison.Ordinal)
                && value.ConfigurationGeneration
                    == snapshot.TextAssignment.ConfigurationGeneration);
        }
        return null;
    }

    private static AiProviderSetupDescriptor Descriptor(ProviderControlProfile profile)
    {
        var cloud = profile.Kind is ProviderControlKind.DeepSeek
            or ProviderControlKind.GoogleGemini
            or ProviderControlKind.OpenAI
            or ProviderControlKind.QwenDashScope
            or ProviderControlKind.AnthropicClaude
            or ProviderControlKind.OpenRouter
            or ProviderControlKind.OllamaCloud;
        var auth = profile.CredentialRequirement switch
        {
            ProviderCredentialRequirement.Required =>
                AiProviderAuthRequirement.ApiKeyRequired,
            ProviderCredentialRequirement.NotRequired =>
                AiProviderAuthRequirement.NoCredentialLocal,
            _ => AiProviderAuthRequirement.EndpointDefined
        };
        var endpoint = profile.DefaultBaseUri?.GetLeftPart(UriPartial.Authority)
            ?? "玩家明確指定的 Base URL";
        if (profile.RequiresExplicitEndpointProfile)
            endpoint = "區域／Workspace 專用端點；Key 不跨區嘗試";
        else if (profile.IsLocalDefault)
            endpoint += "；本機預設免驗證或依端點選用憑證";
        var catalog = profile.CatalogKind switch
        {
            ProviderCatalogKind.GeminiModelsPaginated => "官方分頁模型目錄",
            ProviderCatalogKind.AnthropicModelsCursorPaginated => "官方 cursor 分頁模型目錄",
            ProviderCatalogKind.OpenRouterModels => "官方目錄與 modality 宣告",
            ProviderCatalogKind.OllamaTagsAndShow => "Tags 目錄；能力需 Show metadata",
            ProviderCatalogKind.QwenOptionalOpenAiModels => "可用時讀取目錄；否則明確手動選型",
            ProviderCatalogKind.LocalAiCapabilities => "LocalAI 模型／能力 metadata",
            ProviderCatalogKind.LmStudioNativeModels => "LM Studio 原生模型目錄",
            ProviderCatalogKind.OptionalOpenAiModels
                when profile.Kind == ProviderControlKind.CustomOpenAICompatible =>
                "只查詢明確 Base URL；不廣播、不掃描區網",
            _ => "模型目錄只證明 ID 可見"
        };
        return new AiProviderSetupDescriptor(
            profile.Kind,
            profile.ProviderId,
            profile.DisplayName,
            cloud ? AiProviderUiGroup.Cloud : AiProviderUiGroup.LocalAndSelfHosted,
            AiProviderSetupState.SetupSupported,
            auth,
            endpoint,
            catalog,
            null);
    }

    private static AiFunctionCardVm Missing(
        AiFunctionId id,
        string name,
        string detail)
        => new(
            id,
            name,
            "尚未指派",
            detail,
            AiFunctionAssignmentState.Missing,
            false);

    internal static bool IsVerifiedLegacyDeepSeekTextModel(string? modelId)
        => !string.IsNullOrWhiteSpace(modelId)
           && VerifiedLegacyDeepSeekTextModels.Contains(
               modelId,
               StringComparer.Ordinal);

    internal static bool IsVerifiedDeepSeekTextModel(string? modelId)
        => IsVerifiedLegacyDeepSeekTextModel(modelId);
}
