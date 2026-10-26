using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum ProviderCredentialResolverMode
{
    Invalid = 0,
    Off = 1,
    ContractReplay = 2
}

internal enum ProviderLegacyCredentialCompatibilityState
{
    Invalid = 0,
    NotRequired = 1,
    MissingReference = 2,
    OpaqueReferenceOnlyResolverUnavailable = 3,
    SecretMaterialUnavailable = 4,
    EligibleForFinalBoundaryLease = 5
}

internal enum ProviderCredentialLeaseAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    ResolverOff = 2,
    CredentialNotRequired = 3,
    ReferenceMissing = 4,
    ReferenceMismatch = 5,
    AuthenticationSchemeMismatch = 6,
    EndpointFingerprintMismatch = 7,
    ConfigurationGenerationMismatch = 8,
    SecretMaterialUnavailable = 9,
    EmptySecret = 10,
    SecretTooLong = 11,
    ControlCharacterRejected = 12,
    InvalidUnicodeRejected = 13,
    InvalidLifetime = 14,
    LifetimeExceeded = 15
}

internal sealed class ProviderCredentialResolutionRequest
{
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal CredentialReference Reference { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal string EndpointFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderCredentialResolutionRequest(
        ProviderEndpointInstance instance,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (instance.AuthenticationScheme
                == ProviderAuthenticationScheme.None
            || instance.CredentialReference == null)
        {
            throw new ArgumentException(
                "Credential resolution requires an authenticated instance.",
                nameof(instance));
        }

        issuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        expiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        InstanceId = instance.InstanceId;
        Reference = instance.CredentialReference;
        AuthenticationScheme = instance.AuthenticationScheme;
        EndpointFingerprint = instance.EndpointFingerprint;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CREDENTIAL-RESOLUTION-REQUEST/1",
            InstanceId.Value,
            Reference.Fingerprint,
            ProviderRegistryCanonicalHash.Number(
                (int)AuthenticationScheme),
            EndpointFingerprint,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration),
            ProviderRegistryCanonicalHash.Timestamp(IssuedAt),
            ProviderRegistryCanonicalHash.Timestamp(ExpiresAt));
    }
}

internal sealed class ProviderCredentialLeaseResult
{
    internal ProviderCredentialLeaseAdmission Admission { get; }
    internal CredentialLease? Lease { get; }
    internal bool Accepted =>
        Admission
            == ProviderCredentialLeaseAdmission
                .AcceptedForContractReplay;

    private ProviderCredentialLeaseResult(
        ProviderCredentialLeaseAdmission admission,
        CredentialLease? lease)
    {
        if (!Enum.IsDefined(
                typeof(ProviderCredentialLeaseAdmission),
                admission)
            || admission == ProviderCredentialLeaseAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderCredentialLeaseAdmission
                    .AcceptedForContractReplay)
            != (lease != null))
        {
            throw new ArgumentException(
                "Only an accepted result may carry a credential lease.");
        }
        Admission = admission;
        Lease = lease;
    }

    internal static ProviderCredentialLeaseResult Accept(
        CredentialLease lease)
        => new(
            ProviderCredentialLeaseAdmission.AcceptedForContractReplay,
            lease ?? throw new ArgumentNullException(nameof(lease)));

    internal static ProviderCredentialLeaseResult Reject(
        ProviderCredentialLeaseAdmission admission)
        => new(admission, null);
}

internal delegate bool CredentialLeaseConsumer(
    ReadOnlySpan<char> credential);

internal sealed class CredentialLease : IDisposable
{
    internal const int MaximumSecretLength = 8192;
    internal static TimeSpan MaximumLifetime { get; } =
        TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private char[] _secret;
    private bool _consumed;
    private bool _disposed;

    internal ProviderEndpointInstanceId InstanceId { get; }
    internal CredentialReferenceId ReferenceId { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal string EndpointFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string RequestFingerprint { get; }
    internal bool IsConsumed
    {
        get
        {
            lock (_gate)
                return _consumed;
        }
    }
    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
                return _disposed;
        }
    }

    private CredentialLease(
        ProviderCredentialResolutionRequest request,
        char[] secret)
    {
        InstanceId = request.InstanceId;
        ReferenceId = request.Reference.ReferenceId;
        AuthenticationScheme = request.AuthenticationScheme;
        EndpointFingerprint = request.EndpointFingerprint;
        ConfigurationGeneration = request.ConfigurationGeneration;
        IssuedAt = request.IssuedAt;
        ExpiresAt = request.ExpiresAt;
        RequestFingerprint = request.Fingerprint;
        _secret = secret;
    }

    internal static ProviderCredentialLeaseResult
        TryCreateForContractReplay(
            ProviderCredentialResolutionRequest request,
            ProviderEndpointInstance currentInstance,
            char[]? secretMaterial)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (currentInstance == null)
            throw new ArgumentNullException(nameof(currentInstance));

        if (!request.InstanceId.Equals(currentInstance.InstanceId)
            || currentInstance.CredentialReference == null
            || !request.Reference.ReferenceId.Equals(
                currentInstance.CredentialReference.ReferenceId))
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.ReferenceMismatch);
        }
        if (request.AuthenticationScheme
                != currentInstance.AuthenticationScheme
            || request.Reference.AuthenticationScheme
                != currentInstance.AuthenticationScheme)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .AuthenticationSchemeMismatch);
        }
        if (!string.Equals(
                request.EndpointFingerprint,
                currentInstance.EndpointFingerprint,
                StringComparison.Ordinal))
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .EndpointFingerprintMismatch);
        }
        if (request.ConfigurationGeneration
            != currentInstance.ConfigurationGeneration)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (secretMaterial == null)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .SecretMaterialUnavailable);
        }
        if (secretMaterial.Length == 0)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.EmptySecret);
        }
        if (secretMaterial.Length > MaximumSecretLength)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.SecretTooLong);
        }
        var invalidUnicode = false;
        var control = false;
        for (var index = 0; index < secretMaterial.Length; index++)
        {
            var value = secretMaterial[index];
            if (char.IsControl(value))
                control = true;
            if (char.IsHighSurrogate(value))
            {
                if (index + 1 >= secretMaterial.Length
                    || !char.IsLowSurrogate(secretMaterial[index + 1]))
                {
                    invalidUnicode = true;
                }
                else
                {
                    index++;
                }
            }
            else if (char.IsLowSurrogate(value))
            {
                invalidUnicode = true;
            }
        }
        if (control)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .ControlCharacterRejected);
        }
        if (invalidUnicode)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .InvalidUnicodeRejected);
        }
        if (request.ExpiresAt <= request.IssuedAt)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.InvalidLifetime);
        }
        if (request.ExpiresAt - request.IssuedAt > MaximumLifetime)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.LifetimeExceeded);
        }

        return ProviderCredentialLeaseResult.Accept(
            new CredentialLease(
                request,
                (char[])secretMaterial.Clone()));
    }

    internal bool TryConsumeOnce(
        DateTimeOffset now,
        CredentialLeaseConsumer consumer)
    {
        if (consumer == null)
            throw new ArgumentNullException(nameof(consumer));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_disposed
                || _consumed
                || now < IssuedAt
                || now >= ExpiresAt)
            {
                return false;
            }

            _consumed = true;
            try
            {
                return consumer(_secret.AsSpan());
            }
            finally
            {
                ClearSecret();
                _disposed = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            ClearSecret();
            _disposed = true;
        }
    }

    private void ClearSecret()
    {
        Array.Clear(_secret, 0, _secret.Length);
        _secret = Array.Empty<char>();
    }

    public override string ToString()
        => "CredentialLease([REDACTED])";
}

internal interface IProviderCredentialResolver
{
    ProviderCredentialLeaseResult Resolve(
        ProviderCredentialResolutionRequest request,
        ProviderEndpointInstance currentInstance);
}

internal sealed class ContractReplayProviderCredentialResolver :
    IProviderCredentialResolver
{
    private readonly ProviderCredentialResolverMode _mode;
    private readonly IReadOnlyDictionary<string, char[]> _materialByReference;

    internal ContractReplayProviderCredentialResolver(
        ProviderCredentialResolverMode mode,
        IReadOnlyDictionary<string, char[]> materialByReference)
    {
        if (!Enum.IsDefined(typeof(ProviderCredentialResolverMode), mode)
            || mode == ProviderCredentialResolverMode.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        _mode = mode;
        _materialByReference = materialByReference
            ?? throw new ArgumentNullException(nameof(materialByReference));
    }

    public ProviderCredentialLeaseResult Resolve(
        ProviderCredentialResolutionRequest request,
        ProviderEndpointInstance currentInstance)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (currentInstance == null)
            throw new ArgumentNullException(nameof(currentInstance));
        if (_mode == ProviderCredentialResolverMode.Off)
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission.ResolverOff);
        }
        if (!_materialByReference.TryGetValue(
                request.Reference.ReferenceId.Value,
                out var material))
        {
            return ProviderCredentialLeaseResult.Reject(
                ProviderCredentialLeaseAdmission
                    .SecretMaterialUnavailable);
        }
        return CredentialLease.TryCreateForContractReplay(
            request,
            currentInstance,
            material);
    }
}

internal static class ProviderLegacyCredentialCompatibility
{
    internal static ProviderLegacyCredentialCompatibilityState Evaluate(
        ProviderEndpointInstance instance,
        bool legacySecretMaterialPresent,
        bool finalBoundaryResolverAvailable)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (instance.AuthenticationScheme
            == ProviderAuthenticationScheme.None)
        {
            return ProviderLegacyCredentialCompatibilityState.NotRequired;
        }
        if (instance.CredentialReference == null)
        {
            return ProviderLegacyCredentialCompatibilityState
                .MissingReference;
        }
        if (!legacySecretMaterialPresent)
        {
            return ProviderLegacyCredentialCompatibilityState
                .SecretMaterialUnavailable;
        }
        if (!finalBoundaryResolverAvailable)
        {
            return ProviderLegacyCredentialCompatibilityState
                .OpaqueReferenceOnlyResolverUnavailable;
        }
        return ProviderLegacyCredentialCompatibilityState
            .EligibleForFinalBoundaryLease;
    }
}

internal sealed class ProviderModelTransportResolutionRequest
{
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal OpaqueTransportModelReference Reference { get; }
    internal string ExpectedCorrelationHash { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal ProviderModelTransportResolutionRequest(
        ProviderModelDescriptor model,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (model.TransportIdentityClass
            != ProviderTransportIdentityClass.SensitiveLocalIdentifier)
        {
            throw new ArgumentException(
                "Only sensitive local identifiers require a transport lease.",
                nameof(model));
        }
        InstanceId = model.InstanceId;
        ModelInstanceId = model.ModelInstanceId;
        Reference = model.TransportReference;
        ExpectedCorrelationHash = model.CorrelationHash;
        IssuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
    }
}

internal interface IProviderModelTransportResolver
{
    ProviderModelTransportLeaseResult Resolve(
        ProviderModelTransportResolutionRequest request);
}

internal sealed class ContractReplayProviderModelTransportResolver :
    IProviderModelTransportResolver
{
    private readonly ProviderCredentialResolverMode _mode;
    private readonly IReadOnlyDictionary<string, string> _valuesByReference;

    internal ContractReplayProviderModelTransportResolver(
        ProviderCredentialResolverMode mode,
        IReadOnlyDictionary<string, string> valuesByReference)
    {
        if (!Enum.IsDefined(typeof(ProviderCredentialResolverMode), mode)
            || mode == ProviderCredentialResolverMode.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        _mode = mode;
        _valuesByReference = valuesByReference
            ?? throw new ArgumentNullException(nameof(valuesByReference));
    }

    public ProviderModelTransportLeaseResult Resolve(
        ProviderModelTransportResolutionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (_mode == ProviderCredentialResolverMode.Off
            || !_valuesByReference.TryGetValue(
                request.Reference.Value,
                out var value))
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .EmptyTransportValue);
        }
        return ModelTransportLease.TryCreateForContractReplay(
            request.InstanceId,
            request.ModelInstanceId,
            request.Reference,
            request.ExpectedCorrelationHash,
            value,
            request.IssuedAt,
            request.ExpiresAt);
    }
}

internal sealed class ProviderCredentialBoundaryAuthorityCounters
{
    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int DecisionWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int PersistenceWrites => 0;
    internal int SecretMigrations => 0;
    internal int SecretDeletions => 0;
}
