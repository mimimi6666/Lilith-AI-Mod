using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithProviderControlPlane;

internal enum ProviderControlPlaneCredentialState
{
    Invalid = 0,
    NotRequired = 1,
    Missing = 2,
    ReferenceConfigured = 3,
    AuthenticationRejected = 4,
    Expired = 5,
    Revoked = 6
}

internal enum ProviderControlPlaneCatalogState
{
    Invalid = 0,
    NotObserved = 1,
    Current = 2,
    Empty = 3,
    Partial = 4,
    Stale = 5,
    ObservationUnsupported = 6,
    Rejected = 7,
    Cancelled = 8
}

internal enum ProviderControlPlaneCapability
{
    Invalid = 0,
    TextGeneration = 1,
    SpeechRecognition = 2,
    Vision = 3,
    ToolUse = 4,
    ComputerAction = 5,
    StructuredOutput = 6,
    Streaming = 7
}

internal enum ProviderControlPlaneFunction
{
    Invalid = 0,
    TextGeneration = 1,
    SpeechRecognition = 2,
    Vision = 3,
    ComputerAction = 4
}

internal enum ProviderRuntimeHealthState
{
    Invalid = 0,
    NotObserved = 1,
    Scheduled = 2,
    InFlight = 3,
    ReachableAndAdmitted = 4,
    ObservationUnsupported = 5,
    Cancelled = 6,
    Timeout = 7,
    TemporaryNetworkFailure = 8,
    RateLimited = 9,
    EmptyOrPartialCatalog = 10,
    ConfigurationChanged = 11,
    InstanceRemoved = 12,
    Expired = 13,
    StaleResultRejected = 14,
    EndpointPolicyRejected = 15,
    AuthenticationRejected = 16,
    ProtocolRejected = 17
}

internal enum ProviderHealthPresentationColor
{
    Invalid = 0,
    Green = 1,
    Orange = 2,
    Red = 3
}

internal enum ProviderHealthPresentationMeaning
{
    Invalid = 0,
    ReachableAndAdmitted = 1,
    IndeterminateOrTransient = 2,
    ConfigurationOrAuthenticationRejected = 3
}

internal enum ProviderHealthObservationProvenance
{
    Invalid = 0,
    BoundedStartupHealth = 1,
    BoundedStartupCatalog = 2
}

internal enum ProviderModelSelectionAdmission
{
    Invalid = 0,
    Accepted = 1,
    ProviderMismatch = 2,
    InstanceMismatch = 3,
    ConfigurationGenerationMismatch = 4,
    CatalogNotCurrent = 5,
    CatalogExpired = 6,
    ModelNotCatalogued = 7,
    ModelRejectedByProfile = 8
}

internal enum ProviderFunctionAssignmentAdmission
{
    Invalid = 0,
    Accepted = 1,
    UnsupportedFunction = 2,
    CapabilityMismatch = 3,
    CapabilityUnknown = 4,
    CapabilityOnlyAdvertised = 5,
    CapabilityNotVerified = 6,
    EvidenceExpired = 7,
    ProviderMismatch = 8,
    InstanceMismatch = 9,
    ConfigurationGenerationMismatch = 10,
    CatalogGenerationMismatch = 11,
    ModelSelectionMismatch = 12
}

internal enum ProviderCredentialLeaseConsumptionAdmission
{
    Invalid = 0,
    Consumed = 1,
    Duplicate = 2,
    NotYetValid = 3,
    Expired = 4,
    ProviderMismatch = 5,
    InstanceMismatch = 6,
    ConfigurationGenerationMismatch = 7,
    RuntimeMismatch = 8,
    CredentialReferenceMismatch = 9
}

internal static class ProviderControlPlaneCoreGuard
{
    internal const int MaximumIdentifierLength = 128;
    internal const int MaximumCatalogModels = 512;

    internal static string Identifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumIdentifierLength
            || value.Any(character =>
                !(char.IsLetterOrDigit(character)
                  || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "A bounded ordinal identifier is required.",
                parameterName);
        }
        return value;
    }

    internal static string Sha256(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'A' and <= 'F')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A SHA-256 hexadecimal identity is required.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    internal static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)
            || Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    internal static long Positive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static int NonNegative(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static long NonNegative(long value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static void Window(
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        string parameterName)
    {
        Timestamp(startsAt, nameof(startsAt));
        Timestamp(expiresAt, parameterName);
        if (expiresAt <= startsAt)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

internal static class ProviderControlPlaneCoreHash
{
    internal static string Of(string domain, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("A hash domain is required.", nameof(domain));
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var material = new StringBuilder();
        Append(material, domain);
        foreach (var value in values)
            Append(material, value ?? string.Empty);
        using var sha = SHA256.Create();
        return Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(material.ToString())));
    }

    internal static string Number(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    internal static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);

    private static void Append(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
    }
}

internal abstract class ProviderControlPlaneOrdinalIdentity
{
    internal string Value { get; }

    protected ProviderControlPlaneOrdinalIdentity(
        string value,
        string parameterName)
    {
        Value = ProviderControlPlaneCoreGuard.Identifier(
            value,
            parameterName);
    }

    public sealed override string ToString() => Value;

    public sealed override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public sealed override bool Equals(object? obj)
        => obj != null
            && obj.GetType() == GetType()
            && string.Equals(
                Value,
                ((ProviderControlPlaneOrdinalIdentity)obj).Value,
                StringComparison.Ordinal);
}

internal sealed class ProviderConfiguredInstanceId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderConfiguredInstanceId>
{
    internal ProviderConfiguredInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderConfiguredInstanceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderControlPlaneTransactionId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderControlPlaneTransactionId>
{
    internal ProviderControlPlaneTransactionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderControlPlaneTransactionId? other)
        => Equals((object?)other);
}

internal sealed class ProviderCatalogObservationId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderCatalogObservationId>
{
    internal ProviderCatalogObservationId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderCatalogObservationId? other)
        => Equals((object?)other);
}

internal sealed class ProviderControlPlaneModelId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderControlPlaneModelId>
{
    internal ProviderControlPlaneModelId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderControlPlaneModelId? other)
        => Equals((object?)other);
}

internal sealed class ProviderModelSelectionId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderModelSelectionId>
{
    internal ProviderModelSelectionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderModelSelectionId? other)
        => Equals((object?)other);
}

internal sealed class ProviderFunctionAssignmentId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderFunctionAssignmentId>
{
    internal ProviderFunctionAssignmentId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderFunctionAssignmentId? other)
        => Equals((object?)other);
}

internal sealed class ProviderCapabilityEvidenceId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderCapabilityEvidenceId>
{
    internal ProviderCapabilityEvidenceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderCapabilityEvidenceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderCredentialReferenceId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderCredentialReferenceId>
{
    internal ProviderCredentialReferenceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderCredentialReferenceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderCredentialLeaseId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderCredentialLeaseId>
{
    internal ProviderCredentialLeaseId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderCredentialLeaseId? other)
        => Equals((object?)other);
}

internal sealed class ProviderTextExecutionProfileId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderTextExecutionProfileId>
{
    internal ProviderTextExecutionProfileId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderTextExecutionProfileId? other)
        => Equals((object?)other);
}

internal sealed class ProviderCatalogProfileId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderCatalogProfileId>
{
    internal ProviderCatalogProfileId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderCatalogProfileId? other)
        => Equals((object?)other);
}

internal sealed class ProviderRuntimeInstanceId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderRuntimeInstanceId>
{
    internal ProviderRuntimeInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderRuntimeInstanceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderRuntimeSessionId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderRuntimeSessionId>
{
    internal ProviderRuntimeSessionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderRuntimeSessionId? other)
        => Equals((object?)other);
}

internal sealed class ProviderStartupHealthRunId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderStartupHealthRunId>
{
    internal ProviderStartupHealthRunId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderStartupHealthRunId? other)
        => Equals((object?)other);
}

internal sealed class ProviderStartupHealthAttemptId :
    ProviderControlPlaneOrdinalIdentity,
    IEquatable<ProviderStartupHealthAttemptId>
{
    internal ProviderStartupHealthAttemptId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderStartupHealthAttemptId? other)
        => Equals((object?)other);
}

internal sealed class ProviderControlPlaneProviderIdentity :
    IEquatable<ProviderControlPlaneProviderIdentity>
{
    internal ProviderControlKind Kind { get; }
    internal string ProviderId { get; }
    internal string Fingerprint { get; }

    internal ProviderControlPlaneProviderIdentity(
        ProviderControlKind kind,
        string providerId)
    {
        Kind = ProviderControlPlaneCoreGuard.Defined(kind, nameof(kind));
        ProviderId = ProviderControlPlaneCoreGuard.Identifier(
            providerId,
            nameof(providerId));
        var profile = UnifiedProviderProfileRegistry.Get(Kind);
        if (!string.Equals(profile.ProviderId, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Provider kind and stable provider identity must match.",
                nameof(providerId));
        }
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/PROVIDER-IDENTITY/1",
            ProviderControlPlaneCoreHash.Number((int)Kind),
            ProviderId);
    }

    internal static ProviderControlPlaneProviderIdentity FromProfile(
        ProviderControlProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        return new ProviderControlPlaneProviderIdentity(
            profile.Kind,
            profile.ProviderId);
    }

    public bool Equals(ProviderControlPlaneProviderIdentity? other)
        => other != null
            && Kind == other.Kind
            && string.Equals(ProviderId, other.ProviderId, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as ProviderControlPlaneProviderIdentity);

    public override int GetHashCode()
        => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(ProviderId));

    public override string ToString() => ProviderId;
}

internal sealed class ProviderControlPlaneDescriptor
{
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderControlProtocol Protocol { get; }
    internal ProviderControlAuth Authentication { get; }
    internal ProviderCredentialRequirement CredentialRequirement { get; }
    internal ProviderCatalogKind CatalogKind { get; }
    internal ProviderCatalogProfileId CatalogProfileId { get; }
    internal ProviderTextExecutionProfileId TextExecutionProfileId { get; }
    internal bool IsLocalDefault { get; }
    internal bool PlayerMayEditEndpoint { get; }
    internal string Fingerprint { get; }

    private ProviderControlPlaneDescriptor(ProviderControlProfile profile)
    {
        Provider = ProviderControlPlaneProviderIdentity.FromProfile(profile);
        Protocol = ProviderControlPlaneCoreGuard.Defined(
            profile.Protocol,
            nameof(profile.Protocol));
        Authentication = ProviderControlPlaneCoreGuard.Defined(
            profile.Auth,
            nameof(profile.Auth));
        CredentialRequirement = ProviderControlPlaneCoreGuard.Defined(
            profile.CredentialRequirement,
            nameof(profile.CredentialRequirement));
        CatalogKind = ProviderControlPlaneCoreGuard.Defined(
            profile.CatalogKind,
            nameof(profile.CatalogKind));
        CatalogProfileId = new ProviderCatalogProfileId(
            profile.ProviderId + ".catalog.v1");
        TextExecutionProfileId = new ProviderTextExecutionProfileId(
            profile.ProviderId + ".text.v1");
        IsLocalDefault = profile.IsLocalDefault;
        PlayerMayEditEndpoint = profile.PlayerMayEditEndpoint;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CONTROL-PLANE-DESCRIPTOR/1",
            Provider.Fingerprint,
            ProviderControlPlaneCoreHash.Number((int)Protocol),
            ProviderControlPlaneCoreHash.Number((int)Authentication),
            ProviderControlPlaneCoreHash.Number((int)CredentialRequirement),
            ProviderControlPlaneCoreHash.Number((int)CatalogKind),
            CatalogProfileId.Value,
            TextExecutionProfileId.Value,
            IsLocalDefault ? "1" : "0",
            PlayerMayEditEndpoint ? "1" : "0");
    }

    internal static ProviderControlPlaneDescriptor FromRegistry(
        ProviderControlKind kind)
        => new(UnifiedProviderProfileRegistry.Get(kind));
}

internal sealed class ProviderControlPlaneRuntimeIdentity :
    IEquatable<ProviderControlPlaneRuntimeIdentity>
{
    internal ProviderRuntimeInstanceId InstanceId { get; }
    internal ProviderRuntimeSessionId SessionId { get; }
    internal long Generation { get; }
    internal string Fingerprint { get; }

    internal ProviderControlPlaneRuntimeIdentity(
        ProviderRuntimeInstanceId instanceId,
        ProviderRuntimeSessionId sessionId,
        long generation)
    {
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        Generation = ProviderControlPlaneCoreGuard.Positive(
            generation,
            nameof(generation));
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/RUNTIME-IDENTITY/1",
            InstanceId.Value,
            SessionId.Value,
            ProviderControlPlaneCoreHash.Number(Generation));
    }

    public bool Equals(ProviderControlPlaneRuntimeIdentity? other)
        => other != null
            && Generation == other.Generation
            && InstanceId.Equals(other.InstanceId)
            && SessionId.Equals(other.SessionId);

    public override bool Equals(object? obj)
        => Equals(obj as ProviderControlPlaneRuntimeIdentity);

    public override int GetHashCode()
        => HashCode.Combine(InstanceId, SessionId, Generation);
}

internal sealed class ProviderControlPlaneCredentialReference
{
    internal ProviderCredentialReferenceId ReferenceId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderControlAuth Authentication { get; }
    internal string Fingerprint { get; }

    internal ProviderControlPlaneCredentialReference(
        ProviderCredentialReferenceId referenceId,
        ProviderControlPlaneProviderIdentity provider,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration,
        ProviderControlAuth authentication)
    {
        ReferenceId = referenceId
            ?? throw new ArgumentNullException(nameof(referenceId));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ConfigurationGeneration = ProviderControlPlaneCoreGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        Authentication = ProviderControlPlaneCoreGuard.Defined(
            authentication,
            nameof(authentication));
        if (Authentication == ProviderControlAuth.NoneByDefault)
        {
            throw new ArgumentException(
                "A no-credential profile cannot carry a credential reference.",
                nameof(authentication));
        }
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CREDENTIAL-REFERENCE/1",
            ReferenceId.Value,
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Number((int)Authentication));
    }
}

internal sealed class ProviderConfiguredInstance
{
    internal ProviderControlPlaneDescriptor Descriptor { get; }
    internal ProviderControlPlaneProviderIdentity Provider => Descriptor.Provider;
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal string EndpointIdentityFingerprint { get; }
    internal ProviderControlPlaneCredentialState CredentialState { get; }
    internal ProviderControlPlaneCredentialReference? CredentialReference { get; }
    internal DateTimeOffset ConfiguredAt { get; }
    internal string Fingerprint { get; }

    internal ProviderConfiguredInstance(
        ProviderControlPlaneDescriptor descriptor,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration,
        string endpointIdentityFingerprint,
        ProviderControlPlaneCredentialState credentialState,
        ProviderControlPlaneCredentialReference? credentialReference,
        DateTimeOffset configuredAt)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ConfigurationGeneration = ProviderControlPlaneCoreGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        EndpointIdentityFingerprint = ProviderControlPlaneCoreGuard.Sha256(
            endpointIdentityFingerprint,
            nameof(endpointIdentityFingerprint));
        CredentialState = ProviderControlPlaneCoreGuard.Defined(
            credentialState,
            nameof(credentialState));
        ConfiguredAt = ProviderControlPlaneCoreGuard.Timestamp(
            configuredAt,
            nameof(configuredAt));
        ValidateCredentialState(credentialReference);
        CredentialReference = credentialReference;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CONFIGURED-INSTANCE/1",
            Descriptor.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            EndpointIdentityFingerprint,
            ProviderControlPlaneCoreHash.Number((int)CredentialState),
            CredentialReference?.Fingerprint ?? string.Empty,
            ProviderControlPlaneCoreHash.Timestamp(ConfiguredAt));
    }

    internal bool Matches(
        ProviderControlPlaneProviderIdentity provider,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration)
        => provider != null
            && instanceId != null
            && Provider.Equals(provider)
            && InstanceId.Equals(instanceId)
            && ConfigurationGeneration == configurationGeneration;

    private void ValidateCredentialState(
        ProviderControlPlaneCredentialReference? credentialReference)
    {
        if (Descriptor.CredentialRequirement
                == ProviderCredentialRequirement.NotRequired)
        {
            if (CredentialState != ProviderControlPlaneCredentialState.NotRequired
                || credentialReference != null)
            {
                throw new ArgumentException(
                    "A no-credential profile must remain reference-free.",
                    nameof(credentialReference));
            }
            return;
        }

        if (Descriptor.CredentialRequirement == ProviderCredentialRequirement.Required
            && CredentialState == ProviderControlPlaneCredentialState.NotRequired)
        {
            throw new ArgumentException(
                "A required credential cannot be marked not required.",
                nameof(CredentialState));
        }

        if (CredentialState == ProviderControlPlaneCredentialState.ReferenceConfigured)
        {
            if (credentialReference == null
                || !credentialReference.Provider.Equals(Provider)
                || !credentialReference.InstanceId.Equals(InstanceId)
                || credentialReference.ConfigurationGeneration
                    != ConfigurationGeneration
                || credentialReference.Authentication
                    != Descriptor.Authentication)
            {
                throw new ArgumentException(
                    "A configured credential reference must match the exact instance generation.",
                    nameof(credentialReference));
            }
        }
        else if (credentialReference != null
                 && (!credentialReference.Provider.Equals(Provider)
                     || !credentialReference.InstanceId.Equals(InstanceId)
                     || credentialReference.ConfigurationGeneration
                         != ConfigurationGeneration))
        {
            throw new ArgumentException(
                "Credential state and reference identity do not match.",
                nameof(credentialReference));
        }
        else if (CredentialState == ProviderControlPlaneCredentialState.Missing
                 && credentialReference != null)
        {
            throw new ArgumentException(
                "A missing credential cannot carry a reference.",
                nameof(credentialReference));
        }
    }
}

internal sealed class ProviderControlPlaneTransactionIdentity
{
    internal ProviderControlPlaneTransactionId TransactionId { get; }
    internal long ExpectedStoreRevision { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderControlPlaneTransactionIdentity(
        ProviderControlPlaneTransactionId transactionId,
        long expectedStoreRevision,
        ProviderControlPlaneProviderIdentity provider,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        TransactionId = transactionId
            ?? throw new ArgumentNullException(nameof(transactionId));
        ExpectedStoreRevision = ProviderControlPlaneCoreGuard.NonNegative(
            expectedStoreRevision,
            nameof(expectedStoreRevision));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ConfigurationGeneration = ProviderControlPlaneCoreGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        ProviderControlPlaneCoreGuard.Window(
            createdAt,
            expiresAt,
            nameof(expiresAt));
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CONTROL-PLANE-TRANSACTION/1",
            TransactionId.Value,
            ProviderControlPlaneCoreHash.Number(ExpectedStoreRevision),
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Timestamp(CreatedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }

    internal bool IsCurrent(DateTimeOffset now)
        => now >= CreatedAt && now < ExpiresAt;
}

internal sealed class ProviderCatalogObservation
{
    internal ProviderCatalogObservationId ObservationId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal long CatalogGeneration { get; }
    internal ProviderCatalogProfileId CatalogProfileId { get; }
    internal ProviderControlPlaneCatalogState State { get; }
    internal IReadOnlyList<ProviderControlPlaneModelId> Models { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderCatalogObservation(
        ProviderCatalogObservationId observationId,
        ProviderControlPlaneProviderIdentity provider,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration,
        long catalogGeneration,
        ProviderCatalogProfileId catalogProfileId,
        ProviderControlPlaneCatalogState state,
        IEnumerable<ProviderControlPlaneModelId>? models,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        ObservationId = observationId
            ?? throw new ArgumentNullException(nameof(observationId));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ConfigurationGeneration = ProviderControlPlaneCoreGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        CatalogGeneration = ProviderControlPlaneCoreGuard.Positive(
            catalogGeneration,
            nameof(catalogGeneration));
        CatalogProfileId = catalogProfileId
            ?? throw new ArgumentNullException(nameof(catalogProfileId));
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        ProviderControlPlaneCoreGuard.Window(
            observedAt,
            expiresAt,
            nameof(expiresAt));
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;

        var frozen = (models ?? Array.Empty<ProviderControlPlaneModelId>())
            .Select(value => value
                ?? throw new ArgumentException(
                    "Catalog model identities cannot be null.",
                    nameof(models)))
            .OrderBy(value => value.Value, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Length > ProviderControlPlaneCoreGuard.MaximumCatalogModels
            || frozen.Select(value => value.Value)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        {
            throw new ArgumentException(
                "Catalog models must be bounded and unique.",
                nameof(models));
        }
        if (State == ProviderControlPlaneCatalogState.Current
            && frozen.Length == 0)
        {
            throw new ArgumentException(
                "A current catalog requires at least one model.",
                nameof(models));
        }
        if (State == ProviderControlPlaneCatalogState.Empty
            && frozen.Length != 0)
        {
            throw new ArgumentException(
                "An empty catalog cannot contain models.",
                nameof(models));
        }
        Models = new ReadOnlyCollection<ProviderControlPlaneModelId>(frozen);
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CATALOG-OBSERVATION/1",
            ObservationId.Value,
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Number(CatalogGeneration),
            CatalogProfileId.Value,
            ProviderControlPlaneCoreHash.Number((int)State),
            string.Join(",", frozen.Select(value => value.Value)),
            ProviderControlPlaneCoreHash.Timestamp(ObservedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }

    internal bool IsSelectableAt(DateTimeOffset now)
        => State == ProviderControlPlaneCatalogState.Current
            && now >= ObservedAt
            && now < ExpiresAt;
}

internal sealed class ProviderModelSelection
{
    internal ProviderModelSelectionId SelectionId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderCatalogObservationId CatalogObservationId { get; }
    internal long CatalogGeneration { get; }
    internal ProviderControlPlaneModelId ModelId { get; }
    internal ProviderTextExecutionProfileId TextExecutionProfileId { get; }
    internal DateTimeOffset SelectedAt { get; }
    internal string Fingerprint { get; }

    private ProviderModelSelection(
        ProviderModelSelectionId selectionId,
        ProviderConfiguredInstance instance,
        ProviderCatalogObservation catalog,
        ProviderControlPlaneModelId modelId,
        DateTimeOffset selectedAt)
    {
        SelectionId = selectionId;
        Provider = instance.Provider;
        InstanceId = instance.InstanceId;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        CatalogObservationId = catalog.ObservationId;
        CatalogGeneration = catalog.CatalogGeneration;
        ModelId = modelId;
        TextExecutionProfileId = instance.Descriptor.TextExecutionProfileId;
        SelectedAt = selectedAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/MODEL-SELECTION/1",
            SelectionId.Value,
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            CatalogObservationId.Value,
            ProviderControlPlaneCoreHash.Number(CatalogGeneration),
            ModelId.Value,
            TextExecutionProfileId.Value,
            ProviderControlPlaneCoreHash.Timestamp(SelectedAt));
    }

    internal static ProviderModelSelectionResult TryCreate(
        ProviderModelSelectionId selectionId,
        ProviderConfiguredInstance instance,
        ProviderCatalogObservation catalog,
        ProviderControlPlaneModelId modelId,
        DateTimeOffset selectedAt)
    {
        if (selectionId == null)
            throw new ArgumentNullException(nameof(selectionId));
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
        if (modelId == null)
            throw new ArgumentNullException(nameof(modelId));
        ProviderControlPlaneCoreGuard.Timestamp(selectedAt, nameof(selectedAt));

        if (!instance.Provider.Equals(catalog.Provider))
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.ProviderMismatch);
        if (!instance.InstanceId.Equals(catalog.InstanceId))
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.InstanceMismatch);
        if (instance.ConfigurationGeneration != catalog.ConfigurationGeneration)
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.ConfigurationGenerationMismatch);
        if (catalog.State != ProviderControlPlaneCatalogState.Current)
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.CatalogNotCurrent);
        if (selectedAt < catalog.ObservedAt || selectedAt >= catalog.ExpiresAt)
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.CatalogExpired);
        if (!catalog.Models.Contains(modelId))
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.ModelNotCatalogued);
        if (UnifiedProviderProfileRegistry.IsHardRejectedModel(
                instance.Provider.Kind,
                modelId.Value))
        {
            return ProviderModelSelectionResult.Reject(
                ProviderModelSelectionAdmission.ModelRejectedByProfile);
        }
        return ProviderModelSelectionResult.Accept(
            new ProviderModelSelection(
                selectionId,
                instance,
                catalog,
                modelId,
                selectedAt));
    }
}

internal sealed class ProviderModelSelectionResult
{
    internal ProviderModelSelectionAdmission Admission { get; }
    internal ProviderModelSelection? Selection { get; }
    internal bool Accepted => Admission == ProviderModelSelectionAdmission.Accepted;

    private ProviderModelSelectionResult(
        ProviderModelSelectionAdmission admission,
        ProviderModelSelection? selection)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(admission, nameof(admission));
        if (Accepted != (selection != null))
        {
            throw new ArgumentException(
                "Only accepted selection results carry a selection.",
                nameof(selection));
        }
        Selection = selection;
    }

    internal static ProviderModelSelectionResult Accept(
        ProviderModelSelection selection)
        => new(
            ProviderModelSelectionAdmission.Accepted,
            selection ?? throw new ArgumentNullException(nameof(selection)));

    internal static ProviderModelSelectionResult Reject(
        ProviderModelSelectionAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderCapabilityEvidence
{
    internal ProviderCapabilityEvidenceId EvidenceId { get; }
    internal ProviderControlPlaneCapability Capability { get; }
    internal ProviderCapabilityState State { get; }
    internal ProviderCapabilityProvenance Provenance { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal long CatalogGeneration { get; }
    internal ProviderModelSelectionId ModelSelectionId { get; }
    internal ProviderControlPlaneModelId ModelId { get; }
    internal ProviderTextExecutionProfileId TextExecutionProfileId { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderCapabilityEvidence(
        ProviderCapabilityEvidenceId evidenceId,
        ProviderControlPlaneCapability capability,
        ProviderCapabilityState state,
        ProviderCapabilityProvenance provenance,
        ProviderModelSelection selection,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        EvidenceId = evidenceId ?? throw new ArgumentNullException(nameof(evidenceId));
        Capability = ProviderControlPlaneCoreGuard.Defined(
            capability,
            nameof(capability));
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        Provenance = ProviderControlPlaneCoreGuard.Defined(
            provenance,
            nameof(provenance));
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        ProviderControlPlaneCoreGuard.Window(
            observedAt,
            expiresAt,
            nameof(expiresAt));
        ValidateStateAndProvenance();

        Provider = selection.Provider;
        InstanceId = selection.InstanceId;
        ConfigurationGeneration = selection.ConfigurationGeneration;
        CatalogGeneration = selection.CatalogGeneration;
        ModelSelectionId = selection.SelectionId;
        ModelId = selection.ModelId;
        TextExecutionProfileId = selection.TextExecutionProfileId;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CAPABILITY-EVIDENCE/1",
            EvidenceId.Value,
            ProviderControlPlaneCoreHash.Number((int)Capability),
            ProviderControlPlaneCoreHash.Number((int)State),
            ProviderControlPlaneCoreHash.Number((int)Provenance),
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Number(CatalogGeneration),
            ModelSelectionId.Value,
            ModelId.Value,
            TextExecutionProfileId.Value,
            ProviderControlPlaneCoreHash.Timestamp(ObservedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }

    internal bool CanAuthorize(
        ProviderControlPlaneFunction function,
        DateTimeOffset now)
        => function == ProviderControlPlaneFunction.TextGeneration
            && Capability == ProviderControlPlaneCapability.TextGeneration
            && State == ProviderCapabilityState.ProbeVerified
            && Provenance == ProviderCapabilityProvenance.ProbeVerified
            && now >= ObservedAt
            && now < ExpiresAt;

    private void ValidateStateAndProvenance()
    {
        var valid = State switch
        {
            ProviderCapabilityState.Unknown =>
                Provenance == ProviderCapabilityProvenance.None,
            ProviderCapabilityState.Advertised =>
                Provenance is ProviderCapabilityProvenance.OfficialModelCatalog
                    or ProviderCapabilityProvenance.OfficialModelMetadataEndpoint,
            ProviderCapabilityState.ProbeVerified =>
                Provenance == ProviderCapabilityProvenance.ProbeVerified,
            ProviderCapabilityState.UserDeclared =>
                Provenance == ProviderCapabilityProvenance.UserDeclared,
            ProviderCapabilityState.Unsupported =>
                Provenance != ProviderCapabilityProvenance.None,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Capability state and provenance must form a closed valid pair.");
        }
    }
}

internal sealed class ProviderFunctionAssignment
{
    internal ProviderFunctionAssignmentId AssignmentId { get; }
    internal ProviderControlPlaneFunction Function { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal long CatalogGeneration { get; }
    internal ProviderModelSelectionId ModelSelectionId { get; }
    internal ProviderControlPlaneModelId ModelId { get; }
    internal ProviderTextExecutionProfileId TextExecutionProfileId { get; }
    internal ProviderCapabilityEvidenceId CapabilityEvidenceId { get; }
    internal DateTimeOffset AssignedAt { get; }
    internal string Fingerprint { get; }

    private ProviderFunctionAssignment(
        ProviderFunctionAssignmentId assignmentId,
        ProviderControlPlaneFunction function,
        ProviderModelSelection selection,
        ProviderCapabilityEvidence evidence,
        DateTimeOffset assignedAt)
    {
        AssignmentId = assignmentId;
        Function = function;
        Provider = selection.Provider;
        InstanceId = selection.InstanceId;
        ConfigurationGeneration = selection.ConfigurationGeneration;
        CatalogGeneration = selection.CatalogGeneration;
        ModelSelectionId = selection.SelectionId;
        ModelId = selection.ModelId;
        TextExecutionProfileId = selection.TextExecutionProfileId;
        CapabilityEvidenceId = evidence.EvidenceId;
        AssignedAt = assignedAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/FUNCTION-ASSIGNMENT/1",
            AssignmentId.Value,
            ProviderControlPlaneCoreHash.Number((int)Function),
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Number(CatalogGeneration),
            ModelSelectionId.Value,
            ModelId.Value,
            TextExecutionProfileId.Value,
            CapabilityEvidenceId.Value,
            ProviderControlPlaneCoreHash.Timestamp(AssignedAt));
    }

    internal static ProviderFunctionAssignmentResult TryCreate(
        ProviderFunctionAssignmentId assignmentId,
        ProviderControlPlaneFunction function,
        ProviderModelSelection selection,
        ProviderCapabilityEvidence evidence,
        DateTimeOffset assignedAt)
    {
        if (assignmentId == null)
            throw new ArgumentNullException(nameof(assignmentId));
        ProviderControlPlaneCoreGuard.Defined(function, nameof(function));
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        ProviderControlPlaneCoreGuard.Timestamp(assignedAt, nameof(assignedAt));

        if (function != ProviderControlPlaneFunction.TextGeneration)
            return Reject(ProviderFunctionAssignmentAdmission.UnsupportedFunction);
        if (evidence.Capability != ProviderControlPlaneCapability.TextGeneration)
            return Reject(ProviderFunctionAssignmentAdmission.CapabilityMismatch);
        if (evidence.State == ProviderCapabilityState.Unknown)
            return Reject(ProviderFunctionAssignmentAdmission.CapabilityUnknown);
        if (evidence.State == ProviderCapabilityState.Advertised)
            return Reject(ProviderFunctionAssignmentAdmission.CapabilityOnlyAdvertised);
        if (assignedAt < evidence.ObservedAt || assignedAt >= evidence.ExpiresAt)
            return Reject(ProviderFunctionAssignmentAdmission.EvidenceExpired);
        if (!selection.Provider.Equals(evidence.Provider))
            return Reject(ProviderFunctionAssignmentAdmission.ProviderMismatch);
        if (!selection.InstanceId.Equals(evidence.InstanceId))
            return Reject(ProviderFunctionAssignmentAdmission.InstanceMismatch);
        if (selection.ConfigurationGeneration != evidence.ConfigurationGeneration)
        {
            return Reject(
                ProviderFunctionAssignmentAdmission.ConfigurationGenerationMismatch);
        }
        if (selection.CatalogGeneration != evidence.CatalogGeneration)
            return Reject(ProviderFunctionAssignmentAdmission.CatalogGenerationMismatch);
        if (!selection.SelectionId.Equals(evidence.ModelSelectionId)
            || !selection.ModelId.Equals(evidence.ModelId)
            || !selection.TextExecutionProfileId.Equals(
                evidence.TextExecutionProfileId))
        {
            return Reject(ProviderFunctionAssignmentAdmission.ModelSelectionMismatch);
        }
        if (!evidence.CanAuthorize(function, assignedAt))
            return Reject(ProviderFunctionAssignmentAdmission.CapabilityNotVerified);
        return ProviderFunctionAssignmentResult.Accept(
            new ProviderFunctionAssignment(
                assignmentId,
                function,
                selection,
                evidence,
                assignedAt));
    }

    private static ProviderFunctionAssignmentResult Reject(
        ProviderFunctionAssignmentAdmission admission)
        => ProviderFunctionAssignmentResult.Reject(admission);
}

internal sealed class ProviderFunctionAssignmentResult
{
    internal ProviderFunctionAssignmentAdmission Admission { get; }
    internal ProviderFunctionAssignment? Assignment { get; }
    internal bool Accepted => Admission == ProviderFunctionAssignmentAdmission.Accepted;

    private ProviderFunctionAssignmentResult(
        ProviderFunctionAssignmentAdmission admission,
        ProviderFunctionAssignment? assignment)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(admission, nameof(admission));
        if (Accepted != (assignment != null))
        {
            throw new ArgumentException(
                "Only accepted assignment results carry an assignment.",
                nameof(assignment));
        }
        Assignment = assignment;
    }

    internal static ProviderFunctionAssignmentResult Accept(
        ProviderFunctionAssignment assignment)
        => new(
            ProviderFunctionAssignmentAdmission.Accepted,
            assignment ?? throw new ArgumentNullException(nameof(assignment)));

    internal static ProviderFunctionAssignmentResult Reject(
        ProviderFunctionAssignmentAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderCredentialLeaseReference
{
    internal ProviderCredentialLeaseId LeaseId { get; }
    internal ProviderControlPlaneCredentialReference CredentialReference { get; }
    internal ProviderControlPlaneRuntimeIdentity RuntimeIdentity { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderCredentialLeaseReference(
        ProviderCredentialLeaseId leaseId,
        ProviderControlPlaneCredentialReference credentialReference,
        ProviderControlPlaneRuntimeIdentity runtimeIdentity,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        LeaseId = leaseId ?? throw new ArgumentNullException(nameof(leaseId));
        CredentialReference = credentialReference
            ?? throw new ArgumentNullException(nameof(credentialReference));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProviderControlPlaneCoreGuard.Window(
            issuedAt,
            expiresAt,
            nameof(expiresAt));
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/CREDENTIAL-LEASE-REFERENCE/1",
            LeaseId.Value,
            CredentialReference.Fingerprint,
            RuntimeIdentity.Fingerprint,
            ProviderControlPlaneCoreHash.Timestamp(IssuedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }
}

internal sealed class ProviderCredentialLeaseLedger
{
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    internal ProviderCredentialLeaseConsumptionAdmission TryConsume(
        ProviderCredentialLeaseReference lease,
        ProviderConfiguredInstance activeInstance,
        ProviderControlPlaneRuntimeIdentity activeRuntime,
        DateTimeOffset now)
    {
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));
        if (activeInstance == null)
            throw new ArgumentNullException(nameof(activeInstance));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));

        var reference = lease.CredentialReference;
        if (_consumed.Contains(lease.Fingerprint))
            return ProviderCredentialLeaseConsumptionAdmission.Duplicate;
        if (now < lease.IssuedAt)
            return ProviderCredentialLeaseConsumptionAdmission.NotYetValid;
        if (now >= lease.ExpiresAt)
            return ProviderCredentialLeaseConsumptionAdmission.Expired;
        if (!reference.Provider.Equals(activeInstance.Provider))
            return ProviderCredentialLeaseConsumptionAdmission.ProviderMismatch;
        if (!reference.InstanceId.Equals(activeInstance.InstanceId))
            return ProviderCredentialLeaseConsumptionAdmission.InstanceMismatch;
        if (reference.ConfigurationGeneration
            != activeInstance.ConfigurationGeneration)
        {
            return ProviderCredentialLeaseConsumptionAdmission
                .ConfigurationGenerationMismatch;
        }
        if (!lease.RuntimeIdentity.Equals(activeRuntime))
            return ProviderCredentialLeaseConsumptionAdmission.RuntimeMismatch;
        if (activeInstance.CredentialReference == null
            || !activeInstance.CredentialReference.ReferenceId.Equals(
                reference.ReferenceId))
        {
            return ProviderCredentialLeaseConsumptionAdmission
                .CredentialReferenceMismatch;
        }
        _consumed.Add(lease.Fingerprint);
        return ProviderCredentialLeaseConsumptionAdmission.Consumed;
    }
}

internal sealed class ProviderHealthPresentation
{
    internal ProviderRuntimeHealthState State { get; }
    internal ProviderHealthPresentationColor Color { get; }
    internal ProviderHealthPresentationMeaning Meaning { get; }
    internal bool ClaimsGeographicBlocking => false;
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;

    internal ProviderHealthPresentation(
        ProviderRuntimeHealthState state,
        ProviderHealthPresentationColor color,
        ProviderHealthPresentationMeaning meaning)
    {
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        Color = ProviderControlPlaneCoreGuard.Defined(color, nameof(color));
        Meaning = ProviderControlPlaneCoreGuard.Defined(meaning, nameof(meaning));
    }
}

internal static class ProviderHealthPresentationPolicy
{
    internal static ProviderHealthPresentation Map(
        ProviderRuntimeHealthState state)
    {
        ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        return state switch
        {
            ProviderRuntimeHealthState.ReachableAndAdmitted => new(
                state,
                ProviderHealthPresentationColor.Green,
                ProviderHealthPresentationMeaning.ReachableAndAdmitted),
            ProviderRuntimeHealthState.EndpointPolicyRejected
                or ProviderRuntimeHealthState.AuthenticationRejected
                or ProviderRuntimeHealthState.ProtocolRejected => new(
                    state,
                    ProviderHealthPresentationColor.Red,
                    ProviderHealthPresentationMeaning
                        .ConfigurationOrAuthenticationRejected),
            ProviderRuntimeHealthState.NotObserved
                or ProviderRuntimeHealthState.Scheduled
                or ProviderRuntimeHealthState.InFlight
                or ProviderRuntimeHealthState.ObservationUnsupported
                or ProviderRuntimeHealthState.Cancelled
                or ProviderRuntimeHealthState.Timeout
                or ProviderRuntimeHealthState.TemporaryNetworkFailure
                or ProviderRuntimeHealthState.RateLimited
                or ProviderRuntimeHealthState.EmptyOrPartialCatalog
                or ProviderRuntimeHealthState.ConfigurationChanged
                or ProviderRuntimeHealthState.InstanceRemoved
                or ProviderRuntimeHealthState.Expired
                or ProviderRuntimeHealthState.StaleResultRejected => new(
                    state,
                    ProviderHealthPresentationColor.Orange,
                    ProviderHealthPresentationMeaning.IndeterminateOrTransient),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }
}

internal sealed class ProviderHealthObservation
{
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderRuntimeHealthState State { get; }
    internal ProviderHealthObservationProvenance Provenance { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal ProviderHealthPresentation Presentation { get; }
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;
    internal bool SelectsModel => false;
    internal string Fingerprint { get; }

    internal ProviderHealthObservation(
        ProviderStartupHealthAttemptId attemptId,
        ProviderControlPlaneProviderIdentity provider,
        ProviderConfiguredInstanceId instanceId,
        long configurationGeneration,
        ProviderRuntimeHealthState state,
        ProviderHealthObservationProvenance provenance,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        AttemptId = attemptId ?? throw new ArgumentNullException(nameof(attemptId));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ConfigurationGeneration = ProviderControlPlaneCoreGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        Provenance = ProviderControlPlaneCoreGuard.Defined(
            provenance,
            nameof(provenance));
        ProviderControlPlaneCoreGuard.Window(
            observedAt,
            expiresAt,
            nameof(expiresAt));
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        Presentation = ProviderHealthPresentationPolicy.Map(State);
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/HEALTH-OBSERVATION/1",
            AttemptId.Value,
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            ProviderControlPlaneCoreHash.Number((int)State),
            ProviderControlPlaneCoreHash.Number((int)Provenance),
            ProviderControlPlaneCoreHash.Timestamp(ObservedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }

    internal bool IsCurrent(DateTimeOffset now)
        => now >= ObservedAt && now < ExpiresAt;
}

internal sealed class ProviderControlPlaneAuthorityCounters
{
    internal static ProviderControlPlaneAuthorityCounters Zero { get; } = new();

    internal int NetworkCalls => 0;
    internal int PersistentStoreWrites => 0;
    internal int UiWrites => 0;
    internal int PluginCalls => 0;
    internal int PromptWrites => 0;
    internal int ProviderGenerationCalls => 0;
    internal int CapabilityGrants => 0;
    internal int ExecutionGrants => 0;
    internal int BehaviorOutputs => 0;
    internal int GameplayRandomReads => 0;
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal int RetainedPayloads => 0;
    internal bool IsZero => true;
}
