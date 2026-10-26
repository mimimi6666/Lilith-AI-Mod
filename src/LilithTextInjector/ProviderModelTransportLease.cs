using System;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum ProviderModelTransportLeaseAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    EmptyTransportValue = 2,
    TransportValueTooLong = 3,
    ControlCharacterRejected = 4,
    InvalidLifetime = 5,
    LifetimeExceeded = 6,
    CorrelationMismatch = 7,
    InvalidUnicodeRejected = 8
}

internal sealed class ProviderModelTransportLeaseResult
{
    internal ProviderModelTransportLeaseAdmission Admission { get; }
    internal ModelTransportLease? Lease { get; }
    internal bool Accepted =>
        Admission
            == ProviderModelTransportLeaseAdmission
                .AcceptedForContractReplay;

    private ProviderModelTransportLeaseResult(
        ProviderModelTransportLeaseAdmission admission,
        ModelTransportLease? lease)
    {
        if (!Enum.IsDefined(
                typeof(ProviderModelTransportLeaseAdmission),
                admission)
            || admission
                == ProviderModelTransportLeaseAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission
                == ProviderModelTransportLeaseAdmission
                    .AcceptedForContractReplay
            && lease == null)
        {
            throw new ArgumentNullException(nameof(lease));
        }
        if (admission
                != ProviderModelTransportLeaseAdmission
                    .AcceptedForContractReplay
            && lease != null)
        {
            throw new ArgumentException(
                "Rejected transport lease cannot carry a lease.",
                nameof(lease));
        }

        Admission = admission;
        Lease = lease;
    }

    internal static ProviderModelTransportLeaseResult Accept(
        ModelTransportLease lease)
        => new(
            ProviderModelTransportLeaseAdmission
                .AcceptedForContractReplay,
            lease ?? throw new ArgumentNullException(nameof(lease)));

    internal static ProviderModelTransportLeaseResult Reject(
        ProviderModelTransportLeaseAdmission admission)
        => new(admission, null);
}

internal sealed class ModelTransportLease : IDisposable
{
    internal const int MaximumTransportValueLength = 1024;
    internal static TimeSpan MaximumLifetime { get; } =
        TimeSpan.FromSeconds(30);

    private char[] _transportValue;
    private readonly object _stateGate = new();
    private bool _disposed;

    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal OpaqueTransportModelReference Reference { get; }
    internal string CorrelationHash { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal bool IsDisposed
    {
        get
        {
            lock (_stateGate)
                return _disposed;
        }
    }

    private ModelTransportLease(
        ProviderEndpointInstanceId instanceId,
        ProviderModelInstanceId modelInstanceId,
        OpaqueTransportModelReference reference,
        string correlationHash,
        string transportValue,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        InstanceId = instanceId;
        ModelInstanceId = modelInstanceId;
        Reference = reference;
        CorrelationHash = correlationHash;
        _transportValue = transportValue.ToCharArray();
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    internal static ProviderModelTransportLeaseResult
        TryCreateForContractReplay(
            ProviderEndpointInstanceId instanceId,
            ProviderModelInstanceId modelInstanceId,
            OpaqueTransportModelReference reference,
            string expectedCorrelationHash,
            string? transportValue,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt)
    {
        if (instanceId == null)
            throw new ArgumentNullException(nameof(instanceId));
        if (modelInstanceId == null)
        {
            throw new ArgumentNullException(
                nameof(modelInstanceId));
        }
        if (reference == null)
            throw new ArgumentNullException(nameof(reference));
        expectedCorrelationHash =
            ProviderRegistryContractGuard.Sha256(
                expectedCorrelationHash,
                nameof(expectedCorrelationHash));
        issuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        expiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));

        if (string.IsNullOrWhiteSpace(transportValue))
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .EmptyTransportValue);
        }
        if (transportValue.Length > MaximumTransportValueLength)
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .TransportValueTooLong);
        }
        var containsControlCharacter = false;
        var containsInvalidUnicode = false;
        for (var index = 0; index < transportValue.Length; index++)
        {
            var value = transportValue[index];
            if (char.IsControl(value))
                containsControlCharacter = true;
            if (char.IsHighSurrogate(value))
            {
                if (index + 1 >= transportValue.Length
                    || !char.IsLowSurrogate(
                        transportValue[index + 1]))
                {
                    containsInvalidUnicode = true;
                }
                else
                {
                    index++;
                }
            }
            else if (char.IsLowSurrogate(value))
            {
                containsInvalidUnicode = true;
            }
        }
        if (containsControlCharacter)
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .ControlCharacterRejected);
        }
        if (containsInvalidUnicode)
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .InvalidUnicodeRejected);
        }
        if (expiresAt <= issuedAt)
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission.InvalidLifetime);
        }
        if (expiresAt - issuedAt > MaximumLifetime)
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission.LifetimeExceeded);
        }

        var actualCorrelationHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(transportValue)));
        if (!string.Equals(
                expectedCorrelationHash,
                actualCorrelationHash,
                StringComparison.Ordinal))
        {
            return ProviderModelTransportLeaseResult.Reject(
                ProviderModelTransportLeaseAdmission
                    .CorrelationMismatch);
        }

        return ProviderModelTransportLeaseResult.Accept(
            new ModelTransportLease(
                instanceId,
                modelInstanceId,
                reference,
                expectedCorrelationHash,
                transportValue,
                issuedAt,
                expiresAt));
    }

    internal bool IsUsableAt(DateTimeOffset now)
    {
        now = DecisionCoreContractGuard.Timestamp(
            now,
            nameof(now));
        lock (_stateGate)
        {
            return !_disposed
                && now >= IssuedAt
                && now < ExpiresAt;
        }
    }

    internal string CopyTransportValue(DateTimeOffset now)
    {
        now = DecisionCoreContractGuard.Timestamp(
            now,
            nameof(now));
        lock (_stateGate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ModelTransportLease));
            }
            if (now < IssuedAt || now >= ExpiresAt)
            {
                throw new InvalidOperationException(
                    "Model transport lease is outside its validity window.");
            }
            return new string(_transportValue);
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;
            Array.Clear(
                _transportValue,
                0,
                _transportValue.Length);
            _transportValue = Array.Empty<char>();
            _disposed = true;
        }
    }

    public override string ToString()
        => "ModelTransportLease([REDACTED])";
}
