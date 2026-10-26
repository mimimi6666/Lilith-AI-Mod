using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace LilithTextInjector;

internal enum ProviderEndpointUriAdmission
{
    Invalid = 0,
    AcceptedForContract = 1,
    Empty = 2,
    TooLong = 3,
    Malformed = 4,
    Relative = 5,
    UnsupportedScheme = 6,
    UserInfoRejected = 7,
    QueryRejected = 8,
    FragmentRejected = 9,
    HostRejected = 10
}

internal sealed class ProviderEndpointUriResult
{
    internal ProviderEndpointUriAdmission Admission { get; }
    internal ProviderEndpointUri? Endpoint { get; }
    internal bool Accepted =>
        Admission == ProviderEndpointUriAdmission.AcceptedForContract;

    private ProviderEndpointUriResult(
        ProviderEndpointUriAdmission admission,
        ProviderEndpointUri? endpoint)
    {
        if (!Enum.IsDefined(
                typeof(ProviderEndpointUriAdmission),
                admission)
            || admission == ProviderEndpointUriAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission
                == ProviderEndpointUriAdmission.AcceptedForContract
            && endpoint == null)
        {
            throw new ArgumentNullException(nameof(endpoint));
        }
        if (admission
                != ProviderEndpointUriAdmission.AcceptedForContract
            && endpoint != null)
        {
            throw new ArgumentException(
                "Rejected endpoint URI cannot carry an endpoint.",
                nameof(endpoint));
        }

        Admission = admission;
        Endpoint = endpoint;
    }

    internal static ProviderEndpointUriResult Accept(
        ProviderEndpointUri endpoint)
        => new(
            ProviderEndpointUriAdmission.AcceptedForContract,
            endpoint
                ?? throw new ArgumentNullException(nameof(endpoint)));

    internal static ProviderEndpointUriResult Reject(
        ProviderEndpointUriAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderEndpointUri
{
    internal const int MaximumCandidateLength = 2048;

    internal Uri Value { get; }
    internal string CanonicalValue { get; }
    internal string Origin { get; }
    internal string Scheme { get; }
    internal string CanonicalHost { get; }
    internal int Port { get; }
    internal string BasePath { get; }
    internal string Fingerprint { get; }

    private ProviderEndpointUri(
        Uri value,
        string canonicalValue,
        string origin,
        string scheme,
        string canonicalHost,
        int port,
        string basePath)
    {
        Value = value;
        CanonicalValue = canonicalValue;
        Origin = origin;
        Scheme = scheme;
        CanonicalHost = canonicalHost;
        Port = port;
        BasePath = basePath;
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            "D027/ENDPOINT-URI/1",
            CanonicalValue);
    }

    internal bool HasSameOrigin(ProviderEndpointUri? other)
        => other != null
            && string.Equals(
                Origin,
                other.Origin,
                StringComparison.Ordinal);

    internal static ProviderEndpointUriResult TryCreate(
        string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.Empty);
        }

        var normalized = candidate.Trim();
        if (normalized.Length > MaximumCandidateLength)
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.TooLong);
        }
        if (!Uri.TryCreate(
                normalized,
                UriKind.RelativeOrAbsolute,
                out var parsed))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.Malformed);
        }
        if (!parsed.IsAbsoluteUri)
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.Relative);
        }

        var scheme = parsed.Scheme.ToLowerInvariant();
        if (scheme != Uri.UriSchemeHttp
            && scheme != Uri.UriSchemeHttps)
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.UnsupportedScheme);
        }
        var authorityStart = scheme.Length + 3;
        if (normalized.Length < authorityStart
            || normalized[scheme.Length] != ':'
            || normalized[scheme.Length + 1] != '/'
            || normalized[scheme.Length + 2] != '/')
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.Malformed);
        }
        if (!string.IsNullOrEmpty(parsed.UserInfo)
            || AuthorityContainsUserInfoSyntax(
                normalized,
                authorityStart))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.UserInfoRejected);
        }
        if (!string.IsNullOrEmpty(parsed.Query))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.QueryRejected);
        }
        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.FragmentRejected);
        }
        if (string.IsNullOrWhiteSpace(parsed.IdnHost)
            || parsed.HostNameType == UriHostNameType.Unknown)
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.HostRejected);
        }

        var canonicalHost = parsed.IdnHost.ToLowerInvariant();
        if (parsed.HostNameType == UriHostNameType.Dns)
            canonicalHost = canonicalHost.TrimEnd('.');
        if (canonicalHost.Length == 0)
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.HostRejected);
        }
        var formattedHost =
            parsed.HostNameType == UriHostNameType.IPv6
                ? "[" + canonicalHost + "]"
                : canonicalHost;
        var port = parsed.IsDefaultPort
            ? scheme == Uri.UriSchemeHttps
                ? 443
                : 80
            : parsed.Port;
        var basePath = parsed.AbsolutePath;
        if (string.IsNullOrEmpty(basePath))
            basePath = "/";
        if (!basePath.EndsWith("/", StringComparison.Ordinal))
            basePath += "/";
        var origin = scheme
            + "://"
            + formattedHost
            + ":"
            + port.ToString(CultureInfo.InvariantCulture);
        var canonicalValue = origin + basePath;
        if (!Uri.TryCreate(
                canonicalValue,
                UriKind.Absolute,
                out var canonicalUri))
        {
            return ProviderEndpointUriResult.Reject(
                ProviderEndpointUriAdmission.Malformed);
        }

        return ProviderEndpointUriResult.Accept(
            new ProviderEndpointUri(
                canonicalUri,
                canonicalValue,
                origin,
                scheme,
                canonicalHost,
                port,
                basePath));
    }

    private static bool AuthorityContainsUserInfoSyntax(
        string candidate,
        int authorityStart)
    {
        for (var index = authorityStart;
             index < candidate.Length;
             index++)
        {
            var value = candidate[index];
            if (value is '/' or '?' or '#')
                return false;
            if (value == '@')
                return true;
        }
        return false;
    }
}

internal enum ProviderResolvedAddressAdmission
{
    Invalid = 0,
    AcceptedForContract = 1,
    Empty = 2,
    Malformed = 3
}

internal enum ProviderAddressClass
{
    Invalid = 0,
    Loopback = 1,
    PrivateLan = 2,
    Public = 3,
    LinkLocal = 4,
    Metadata = 5,
    Unspecified = 6,
    Multicast = 7,
    Broadcast = 8,
    Reserved = 9
}

internal enum ProviderIpAddressFamily
{
    Invalid = 0,
    Ipv4 = 1,
    Ipv6 = 2
}

internal sealed class ProviderResolvedAddressResult
{
    internal ProviderResolvedAddressAdmission Admission { get; }
    internal ProviderResolvedAddress? Address { get; }
    internal bool Accepted =>
        Admission
            == ProviderResolvedAddressAdmission.AcceptedForContract;

    private ProviderResolvedAddressResult(
        ProviderResolvedAddressAdmission admission,
        ProviderResolvedAddress? address)
    {
        if (!Enum.IsDefined(
                typeof(ProviderResolvedAddressAdmission),
                admission)
            || admission == ProviderResolvedAddressAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission
                == ProviderResolvedAddressAdmission.AcceptedForContract
            && address == null)
        {
            throw new ArgumentNullException(nameof(address));
        }
        if (admission
                != ProviderResolvedAddressAdmission.AcceptedForContract
            && address != null)
        {
            throw new ArgumentException(
                "Rejected address cannot carry an address.",
                nameof(address));
        }

        Admission = admission;
        Address = address;
    }

    internal static ProviderResolvedAddressResult Accept(
        ProviderResolvedAddress address)
        => new(
            ProviderResolvedAddressAdmission.AcceptedForContract,
            address
                ?? throw new ArgumentNullException(nameof(address)));

    internal static ProviderResolvedAddressResult Reject(
        ProviderResolvedAddressAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderResolvedAddress
{
    internal string CanonicalAddress { get; }
    internal ProviderIpAddressFamily AddressFamily { get; }
    internal ProviderAddressClass AddressClass { get; }
    internal bool WasIpv4Mapped { get; }
    internal string Fingerprint { get; }

    private ProviderResolvedAddress(
        IPAddress address,
        ProviderAddressClass addressClass,
        bool wasIpv4Mapped)
    {
        CanonicalAddress = address.ToString().ToLowerInvariant();
        AddressFamily = address.GetAddressBytes().Length == 4
            ? ProviderIpAddressFamily.Ipv4
            : ProviderIpAddressFamily.Ipv6;
        AddressClass = addressClass;
        WasIpv4Mapped = wasIpv4Mapped;
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            "D027/RESOLVED-ADDRESS/1",
            CanonicalAddress,
            ((int)AddressClass).ToString(
                CultureInfo.InvariantCulture),
            WasIpv4Mapped ? "1" : "0");
    }

    internal static ProviderResolvedAddressResult TryCreate(
        string? literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
        {
            return ProviderResolvedAddressResult.Reject(
                ProviderResolvedAddressAdmission.Empty);
        }
        if (!IPAddress.TryParse(literal.Trim(), out var parsed))
        {
            return ProviderResolvedAddressResult.Reject(
                ProviderResolvedAddressAdmission.Malformed);
        }

        var wasMapped = parsed.GetAddressBytes().Length == 16
            && parsed.IsIPv4MappedToIPv6;
        var classified = wasMapped ? parsed.MapToIPv4() : parsed;
        var addressClass =
            !wasMapped
                && parsed.GetAddressBytes().Length == 16
                && parsed.ScopeId != 0
                ? ProviderAddressClass.Reserved
                : Classify(classified);
        return ProviderResolvedAddressResult.Accept(
            new ProviderResolvedAddress(
                parsed,
                addressClass,
                wasMapped));
    }

    private static ProviderAddressClass Classify(IPAddress address)
    {
        if (IsMetadata(address))
            return ProviderAddressClass.Metadata;
        if (IPAddress.IsLoopback(address))
            return ProviderAddressClass.Loopback;
        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
        {
            return ProviderAddressClass.Unspecified;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return ClassifyIpv4(bytes);
        if (bytes.Length == 16)
            return ClassifyIpv6(bytes);
        return ProviderAddressClass.Reserved;
    }

    private static ProviderAddressClass ClassifyIpv4(byte[] bytes)
    {
        if (bytes[0] == 169 && bytes[1] == 254)
            return ProviderAddressClass.LinkLocal;
        if (bytes[0] == 10
            || bytes[0] == 172
                && bytes[1] >= 16
                && bytes[1] <= 31
            || bytes[0] == 192 && bytes[1] == 168)
        {
            return ProviderAddressClass.PrivateLan;
        }
        if (bytes[0] >= 224 && bytes[0] <= 239)
            return ProviderAddressClass.Multicast;
        if (bytes[0] == 255
            && bytes[1] == 255
            && bytes[2] == 255
            && bytes[3] == 255)
        {
            return ProviderAddressClass.Broadcast;
        }
        if (IsReservedIpv4(bytes))
            return ProviderAddressClass.Reserved;
        return ProviderAddressClass.Public;
    }

    private static ProviderAddressClass ClassifyIpv6(byte[] bytes)
    {
        if (bytes[0] == 0xff)
            return ProviderAddressClass.Multicast;
        if (bytes[0] == 0xfe
            && (bytes[1] & 0xc0) == 0x80)
        {
            return ProviderAddressClass.LinkLocal;
        }
        if ((bytes[0] & 0xfe) == 0xfc)
            return ProviderAddressClass.PrivateLan;
        if (HasPrefix(
                bytes,
                new byte[]
                {
                    0x00, 0x64, 0xff, 0x9b,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00
                }))
        {
            return ClassifyIpv4(
                new[]
                {
                    bytes[12],
                    bytes[13],
                    bytes[14],
                    bytes[15]
                });
        }
        if (IsReservedIpv6(bytes))
            return ProviderAddressClass.Reserved;
        return ProviderAddressClass.Public;
    }

    private static bool IsReservedIpv6(byte[] bytes)
        => HasPrefix(
                bytes,
                new byte[12])
            || HasPrefix(
                bytes,
                new byte[]
                {
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0xff, 0xff, 0x00, 0x00
                })
            || HasPrefix(
                bytes,
                new byte[]
                {
                    0x00, 0x64, 0xff, 0x9b, 0x00, 0x01
                })
            || HasPrefix(
                bytes,
                new byte[]
                {
                    0x01, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00
                })
            || bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] <= 0x01
            || bytes[0] == 0x20 && bytes[1] == 0x02
            || bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0d
                && bytes[3] == 0xb8
            || bytes[0] == 0x3f
                && bytes[1] == 0xff
                && (bytes[2] & 0xf0) == 0x00
            || bytes[0] == 0x5f && bytes[1] == 0x00
            || bytes[0] == 0xfe
                && (bytes[1] & 0xc0) == 0xc0;

    private static bool HasPrefix(
        byte[] value,
        byte[] prefix)
    {
        if (value.Length < prefix.Length)
            return false;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (value[index] != prefix[index])
                return false;
        }
        return true;
    }

    private static bool IsMetadata(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 169
                && bytes[1] == 254
                && bytes[2] == 169
                && bytes[3] == 254;
        }
        if (bytes.Length != 16)
            return false;
        return bytes.SequenceEqual(
            new byte[]
            {
                0xfd, 0x00, 0x0e, 0xc2,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x02, 0x54
            });
    }

    private static bool IsReservedIpv4(byte[] bytes)
        => bytes[0] == 0
            || bytes[0] == 100
                && bytes[1] >= 64
                && bytes[1] <= 127
            || bytes[0] == 192
                && bytes[1] == 0
                && bytes[2] == 0
            || bytes[0] == 192
                && bytes[1] == 0
                && bytes[2] == 2
            || bytes[0] == 198
                && (bytes[1] == 18 || bytes[1] == 19)
            || bytes[0] == 198
                && bytes[1] == 51
                && bytes[2] == 100
            || bytes[0] == 203
                && bytes[1] == 0
                && bytes[2] == 113
            || bytes[0] >= 240;
}

internal sealed class ProviderAddressResolutionEvidence
{
    internal const int MaximumAddresses = 64;

    internal ProviderEndpointInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal string EndpointFingerprint { get; }
    internal DateTimeOffset ResolvedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal IReadOnlyList<ProviderResolvedAddress> Addresses { get; }
    internal string Fingerprint { get; }

    internal ProviderAddressResolutionEvidence(
        ProviderEndpointInstanceId instanceId,
        long configurationGeneration,
        string endpointFingerprint,
        DateTimeOffset resolvedAt,
        DateTimeOffset validUntil,
        IReadOnlyList<ProviderResolvedAddress> addresses)
    {
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        ConfigurationGeneration = configurationGeneration;
        EndpointFingerprint =
            ProviderRouteAdmissionContractGuard.Sha256(
                endpointFingerprint,
                nameof(endpointFingerprint));
        ResolvedAt = ProviderRouteAdmissionContractGuard.Timestamp(
            resolvedAt,
            nameof(resolvedAt));
        ValidUntil = ProviderRouteAdmissionContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (ValidUntil <= ResolvedAt)
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        if (addresses == null)
            throw new ArgumentNullException(nameof(addresses));
        if (addresses.Count == 0
            || addresses.Count > MaximumAddresses)
        {
            throw new ArgumentOutOfRangeException(nameof(addresses));
        }

        var copy = new ProviderResolvedAddress[addresses.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < addresses.Count; index++)
        {
            var address = addresses[index]
                ?? throw new ArgumentException(
                    "Resolution evidence cannot contain null.",
                    nameof(addresses));
            if (!unique.Add(address.CanonicalAddress))
            {
                throw new ArgumentException(
                    "Resolution addresses must be unique.",
                    nameof(addresses));
            }
            copy[index] = address;
        }
        Array.Sort(
            copy,
            (left, right) => string.Compare(
                left.CanonicalAddress,
                right.CanonicalAddress,
                StringComparison.Ordinal));
        Addresses = Array.AsReadOnly(copy);
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            new[]
            {
                "D027/ADDRESS-RESOLUTION/1",
                InstanceId.Value,
                ConfigurationGeneration.ToString(
                    CultureInfo.InvariantCulture),
                EndpointFingerprint,
                ResolvedAt.UtcTicks.ToString(
                    CultureInfo.InvariantCulture),
                ValidUntil.UtcTicks.ToString(
                    CultureInfo.InvariantCulture)
            }.Concat(
                copy.Select(address => address.Fingerprint))
                .ToArray());
    }
}

internal sealed class ProviderConnectionAddressEvidence
{
    internal DateTimeOffset ConnectedAt { get; }
    internal ProviderResolvedAddress ConnectedAddress { get; }
    internal string Fingerprint { get; }

    internal ProviderConnectionAddressEvidence(
        DateTimeOffset connectedAt,
        ProviderResolvedAddress connectedAddress)
    {
        ConnectedAt = ProviderRouteAdmissionContractGuard.Timestamp(
            connectedAt,
            nameof(connectedAt));
        ConnectedAddress = connectedAddress
            ?? throw new ArgumentNullException(nameof(connectedAddress));
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            "D027/CONNECTION-ADDRESS/1",
            ConnectedAt.UtcTicks.ToString(
                CultureInfo.InvariantCulture),
            ConnectedAddress.Fingerprint);
    }
}

internal enum ProviderRedirectPolicy
{
    Invalid = 0,
    Disabled = 1
}

internal sealed class ProviderRedirectObservation
{
    internal ProviderRedirectPolicy Policy { get; }
    internal ProviderEndpointUri? Target { get; }
    internal bool RedirectObserved => Target != null;
    internal string Fingerprint { get; }

    internal ProviderRedirectObservation(
        ProviderRedirectPolicy policy,
        ProviderEndpointUri? target)
    {
        if (!Enum.IsDefined(typeof(ProviderRedirectPolicy), policy)
            || policy == ProviderRedirectPolicy.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
        Policy = policy;
        Target = target;
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            "D027/REDIRECT-OBSERVATION/1",
            ((int)Policy).ToString(CultureInfo.InvariantCulture),
            Target?.Fingerprint ?? "-");
    }

    internal static ProviderRedirectObservation None(
        ProviderRedirectPolicy policy =
            ProviderRedirectPolicy.Disabled)
        => new(policy, null);
}

internal sealed class ProviderEndpointNetworkEvidence
{
    internal ProviderAddressResolutionEvidence Resolution { get; }
    internal ProviderConnectionAddressEvidence Connection { get; }
    internal ProviderRedirectObservation Redirect { get; }
    internal string Fingerprint { get; }

    internal ProviderEndpointNetworkEvidence(
        ProviderAddressResolutionEvidence resolution,
        ProviderConnectionAddressEvidence connection,
        ProviderRedirectObservation redirect)
    {
        Resolution = resolution
            ?? throw new ArgumentNullException(nameof(resolution));
        Connection = connection
            ?? throw new ArgumentNullException(nameof(connection));
        Redirect = redirect
            ?? throw new ArgumentNullException(nameof(redirect));
        Fingerprint = ProviderRouteAdmissionContractHash.Of(
            "D027/ENDPOINT-NETWORK-EVIDENCE/1",
            Resolution.Fingerprint,
            Connection.Fingerprint,
            Redirect.Fingerprint);
    }
}

internal enum ProviderEndpointAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    EvidenceInstanceMismatch = 2,
    ConfigurationGenerationMismatch = 3,
    EndpointFingerprintMismatch = 4,
    ResolutionMissingOrExpired = 5,
    RedirectRejected = 6,
    Ipv4MappedAddressRejected = 7,
    UnspecifiedAddressRejected = 8,
    LinkLocalOrMetadataAddressRejected = 9,
    MulticastOrBroadcastAddressRejected = 10,
    ReservedAddressRejected = 11,
    MixedAddressClassRejected = 12,
    RouteLocalityMismatch = 13,
    InsecureRemoteTransport = 14,
    TrustClassMismatch = 15,
    DnsRebindingRejected = 16
}

internal sealed class ProviderEndpointAdmissionResult
{
    internal ProviderEndpointAdmission Admission { get; }
    internal ProviderEndpointNetworkEvidence? Evidence { get; }
    internal bool Accepted =>
        Admission
            == ProviderEndpointAdmission.AcceptedForContractReplay;
    internal bool AllowsNetworkCall => false;

    private ProviderEndpointAdmissionResult(
        ProviderEndpointAdmission admission,
        ProviderEndpointNetworkEvidence? evidence)
    {
        if (!Enum.IsDefined(
                typeof(ProviderEndpointAdmission),
                admission)
            || admission == ProviderEndpointAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission
                == ProviderEndpointAdmission.AcceptedForContractReplay
            && evidence == null)
        {
            throw new ArgumentNullException(nameof(evidence));
        }
        if (admission
                != ProviderEndpointAdmission.AcceptedForContractReplay
            && evidence != null)
        {
            throw new ArgumentException(
                "Rejected endpoint cannot carry admitted evidence.",
                nameof(evidence));
        }
        Admission = admission;
        Evidence = evidence;
    }

    internal static ProviderEndpointAdmissionResult Accept(
        ProviderEndpointNetworkEvidence evidence)
        => new(
            ProviderEndpointAdmission.AcceptedForContractReplay,
            evidence
                ?? throw new ArgumentNullException(nameof(evidence)));

    internal static ProviderEndpointAdmissionResult Reject(
        ProviderEndpointAdmission admission)
        => new(admission, null);
}

internal static class ProviderEndpointAdmissionPolicy
{
    internal static ProviderEndpointAdmissionResult Evaluate(
        ProviderEndpointInstance instance,
        ProviderEndpointNetworkEvidence evidence,
        DateTimeOffset evaluatedAt)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        evaluatedAt = ProviderRouteAdmissionContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));

        var resolution = evidence.Resolution;
        if (!resolution.InstanceId.Equals(instance.InstanceId))
        {
            return Reject(
                ProviderEndpointAdmission.EvidenceInstanceMismatch);
        }
        if (resolution.ConfigurationGeneration
            != instance.ConfigurationGeneration)
        {
            return Reject(
                ProviderEndpointAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (!string.Equals(
                resolution.EndpointFingerprint,
                instance.BaseUri.Fingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderEndpointAdmission.EndpointFingerprintMismatch);
        }
        if (resolution.ResolvedAt > evidence.Connection.ConnectedAt
            || evidence.Connection.ConnectedAt
                >= resolution.ValidUntil
            || evaluatedAt < evidence.Connection.ConnectedAt
            || evaluatedAt >= resolution.ValidUntil)
        {
            return Reject(
                ProviderEndpointAdmission.ResolutionMissingOrExpired);
        }
        if (evidence.Redirect.RedirectObserved)
        {
            return Reject(
                ProviderEndpointAdmission.RedirectRejected);
        }
        if (RequiresHttps(instance)
            && !string.Equals(
                instance.BaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderEndpointAdmission.InsecureRemoteTransport);
        }
        if (!TrustMatches(instance))
        {
            return Reject(
                ProviderEndpointAdmission.TrustClassMismatch);
        }

        var addresses = resolution.Addresses
            .Append(evidence.Connection.ConnectedAddress)
            .ToArray();
        if (addresses.Any(address => address.WasIpv4Mapped))
        {
            return Reject(
                ProviderEndpointAdmission
                    .Ipv4MappedAddressRejected);
        }
        if (addresses.Any(
                address =>
                    address.AddressClass
                    == ProviderAddressClass.Unspecified))
        {
            return Reject(
                ProviderEndpointAdmission.UnspecifiedAddressRejected);
        }
        if (addresses.Any(
                address =>
                    address.AddressClass
                        is ProviderAddressClass.LinkLocal
                        or ProviderAddressClass.Metadata))
        {
            return Reject(
                ProviderEndpointAdmission
                    .LinkLocalOrMetadataAddressRejected);
        }
        if (addresses.Any(
                address =>
                    address.AddressClass
                        is ProviderAddressClass.Multicast
                        or ProviderAddressClass.Broadcast))
        {
            return Reject(
                ProviderEndpointAdmission
                    .MulticastOrBroadcastAddressRejected);
        }
        if (addresses.Any(
                address =>
                    address.AddressClass
                    == ProviderAddressClass.Reserved))
        {
            return Reject(
                ProviderEndpointAdmission.ReservedAddressRejected);
        }

        var resolvedClasses = resolution.Addresses
            .Select(address => address.AddressClass)
            .Distinct()
            .ToArray();
        if (resolvedClasses.Length != 1)
        {
            return Reject(
                ProviderEndpointAdmission.MixedAddressClassRejected);
        }
        if (!RouteMatches(
                instance.ConnectionRouteLocality,
                resolvedClasses[0])
            || !RouteMatches(
                instance.ConnectionRouteLocality,
                evidence.Connection.ConnectedAddress.AddressClass))
        {
            return Reject(
                ProviderEndpointAdmission.RouteLocalityMismatch);
        }
        if (!resolution.Addresses.Any(
                address => string.Equals(
                    address.CanonicalAddress,
                    evidence.Connection.ConnectedAddress
                        .CanonicalAddress,
                    StringComparison.Ordinal)))
        {
            return Reject(
                ProviderEndpointAdmission.DnsRebindingRejected);
        }

        return ProviderEndpointAdmissionResult.Accept(evidence);
    }

    private static ProviderEndpointAdmissionResult Reject(
        ProviderEndpointAdmission admission)
        => ProviderEndpointAdmissionResult.Reject(admission);

    private static bool RouteMatches(
        ConnectionRouteLocality route,
        ProviderAddressClass addressClass)
        => route switch
        {
            ConnectionRouteLocality.Loopback =>
                addressClass == ProviderAddressClass.Loopback,
            ConnectionRouteLocality.Lan =>
                addressClass == ProviderAddressClass.PrivateLan,
            ConnectionRouteLocality.PublicRemote =>
                addressClass == ProviderAddressClass.Public,
            _ => false
        };

    private static bool RequiresHttps(
        ProviderEndpointInstance instance)
        => instance.ConnectionRouteLocality
                == ConnectionRouteLocality.PublicRemote
            || instance.TrustClass
                is ProviderTrustClass.OfficialCloudHttps
                    or ProviderTrustClass.ExplicitRemoteCustom;

    private static bool TrustMatches(
        ProviderEndpointInstance instance)
        => instance.TrustClass switch
        {
            ProviderTrustClass.OfficialCloudHttps =>
                instance.DeploymentOwnership
                    == DeploymentOwnership.ManagedCloud
                && instance.ConnectionRouteLocality
                    == ConnectionRouteLocality.PublicRemote
                && ProviderOfficialCloudEndpointPolicy.Matches(
                    instance.Kind,
                    instance.BaseUri),
            ProviderTrustClass.LoopbackRouteAdmitted =>
                instance.ConnectionRouteLocality
                    == ConnectionRouteLocality.Loopback,
            ProviderTrustClass.ExplicitLan =>
                instance.ConnectionRouteLocality
                    == ConnectionRouteLocality.Lan,
            ProviderTrustClass.ExplicitRemoteCustom =>
                instance.ConnectionRouteLocality
                    == ConnectionRouteLocality.PublicRemote
                && string.Equals(
                    instance.BaseUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.Ordinal),
            _ => false
        };
}

internal static class ProviderOfficialCloudEndpointPolicy
{
    internal const int RuleSetVersion = 1;

    internal static bool Matches(
        ProviderKind kind,
        ProviderEndpointUri endpoint)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));
        if (!string.Equals(
                endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.Ordinal)
            || endpoint.Port != 443)
        {
            return false;
        }

        return kind switch
        {
            ProviderKind.Gemini =>
                HostEquals(
                    endpoint,
                    "generativelanguage.googleapis.com"),
            ProviderKind.OpenAI =>
                HostEquals(endpoint, "api.openai.com"),
            ProviderKind.AnthropicClaude =>
                HostEquals(endpoint, "api.anthropic.com"),
            ProviderKind.DeepSeek =>
                HostEquals(endpoint, "api.deepseek.com"),
            ProviderKind.QwenDashScope =>
                HostEquals(endpoint, "dashscope.aliyuncs.com")
                || HostEquals(
                    endpoint,
                    "dashscope-intl.aliyuncs.com")
                || HostEquals(
                    endpoint,
                    "dashscope-us.aliyuncs.com"),
            _ => false
        };
    }

    private static bool HostEquals(
        ProviderEndpointUri endpoint,
        string expected)
        => string.Equals(
            endpoint.CanonicalHost,
            expected,
            StringComparison.Ordinal);
}

internal sealed class ProviderCapabilityRequirementSet
{
    internal IReadOnlyList<ProviderCapability> Requirements { get; }
    internal string Fingerprint { get; }

    internal ProviderCapabilityRequirementSet(
        IReadOnlyList<ProviderCapability> requirements)
    {
        if (requirements == null)
            throw new ArgumentNullException(nameof(requirements));
        if (requirements.Count == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requirements));
        }

        var copy = new ProviderCapability[requirements.Count];
        var unique = new HashSet<ProviderCapability>();
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = ProviderRegistryContractGuard.Defined(
                requirements[index],
                nameof(requirements));
            if (!unique.Add(requirement))
            {
                throw new ArgumentException(
                    "Capability requirements must be unique.",
                    nameof(requirements));
            }
            copy[index] = requirement;
        }
        if (!unique.Contains(ProviderCapability.TextGeneration))
        {
            throw new ArgumentException(
                "Text route requirements must include text generation.",
                nameof(requirements));
        }
        Array.Sort(
            copy,
            (left, right) => ((int)left).CompareTo((int)right));
        Requirements = Array.AsReadOnly(copy);
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CAPABILITY-REQUIREMENTS/1",
            copy.Select(
                    value => ProviderRegistryCanonicalHash.Number(
                        (int)value))
                .ToArray());
    }
}

internal enum ProviderRouteSelectionAdmission
{
    Invalid = 0,
    CreatedForContractReplay = 1,
    UnknownInstance = 2,
    InstanceDisabled = 3,
    UnknownModel = 4,
    RequiredCapabilityNotAuthorized = 5,
    ProcessingLocalityRejected = 6,
    InvalidSelectionWindow = 7
}

internal sealed class ProviderRouteSelectionResult
{
    internal ProviderRouteSelectionAdmission Admission { get; }
    internal ProviderRouteSelection? Selection { get; }
    internal bool Accepted =>
        Admission
            == ProviderRouteSelectionAdmission
                .CreatedForContractReplay;

    private ProviderRouteSelectionResult(
        ProviderRouteSelectionAdmission admission,
        ProviderRouteSelection? selection)
    {
        if (!Enum.IsDefined(
                typeof(ProviderRouteSelectionAdmission),
                admission)
            || admission == ProviderRouteSelectionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission
                == ProviderRouteSelectionAdmission
                    .CreatedForContractReplay
            && selection == null)
        {
            throw new ArgumentNullException(nameof(selection));
        }
        if (admission
                != ProviderRouteSelectionAdmission
                    .CreatedForContractReplay
            && selection != null)
        {
            throw new ArgumentException(
                "Rejected route selection cannot carry a selection.",
                nameof(selection));
        }
        Admission = admission;
        Selection = selection;
    }

    internal static ProviderRouteSelectionResult Accept(
        ProviderRouteSelection selection)
        => new(
            ProviderRouteSelectionAdmission.CreatedForContractReplay,
            selection
                ?? throw new ArgumentNullException(nameof(selection)));

    internal static ProviderRouteSelectionResult Reject(
        ProviderRouteSelectionAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderRouteSelection
{
    internal string SelectionId { get; }
    internal int RegistryVersion { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal ProviderDefinitionId DefinitionId { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderKind Kind { get; }
    internal long ConfigurationGeneration { get; }
    internal string InstanceConfigurationFingerprint { get; }
    internal string InstanceFingerprint { get; }
    internal string EndpointFingerprint { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal string ModelDescriptorFingerprint { get; }
    internal string CorrelationHash { get; }
    internal ExecutionVenue ExecutionVenue { get; }
    internal ProviderExecutionVenueProvenance
        ExecutionVenueProvenance { get; }
    internal string ExecutionVenueEvidenceFingerprint { get; }
    internal ProcessingLocalityRequirement
        ProcessingLocalityRequirement { get; }
    internal ProviderCapabilityRequirementSet
        RequiredCapabilities { get; }
    internal string RequiredCapabilitiesFingerprint { get; }
    internal string CapabilityProfileFingerprint { get; }
    internal string ProcessingPolicyFingerprint { get; }
    internal int OfficialCloudEndpointRuleSetVersion { get; }
    internal DateTimeOffset SelectedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string RouteFingerprint { get; }
    internal bool AllowsProviderCall => false;

    private ProviderRouteSelection(
        ProviderRegistrySnapshot snapshot,
        ProviderEndpointInstance instance,
        ProviderModelDescriptor model,
        ProviderCapabilityRequirementSet requiredCapabilities,
        ProcessingLocalityRequirement processingLocalityRequirement,
        string processingPolicyFingerprint,
        DateTimeOffset selectedAt,
        DateTimeOffset expiresAt)
    {
        RegistryVersion = snapshot.RegistryVersion;
        RegistryGeneration = snapshot.RegistryGeneration;
        RegistryFingerprint = snapshot.Fingerprint;
        DefinitionId = instance.DefinitionId;
        InstanceId = instance.InstanceId;
        Kind = instance.Kind;
        ConfigurationGeneration =
            instance.ConfigurationGeneration;
        InstanceConfigurationFingerprint =
            instance.ConfigurationFingerprint;
        InstanceFingerprint = instance.Fingerprint;
        EndpointFingerprint = instance.EndpointFingerprint;
        Protocol = instance.Protocol;
        ProtocolProfileVersion = instance.ProtocolProfileVersion;
        ModelInstanceId = model.ModelInstanceId;
        ModelDescriptorFingerprint = model.Fingerprint;
        CorrelationHash = model.CorrelationHash;
        ExecutionVenue = model.ExecutionVenue;
        ExecutionVenueProvenance =
            model.ExecutionVenueProvenance;
        ExecutionVenueEvidenceFingerprint =
            model.ExecutionVenueEvidence.Fingerprint;
        ProcessingLocalityRequirement =
            processingLocalityRequirement;
        RequiredCapabilities = requiredCapabilities;
        RequiredCapabilitiesFingerprint =
            requiredCapabilities.Fingerprint;
        CapabilityProfileFingerprint =
            model.CapabilityProfile.Fingerprint;
        ProcessingPolicyFingerprint =
            processingPolicyFingerprint;
        OfficialCloudEndpointRuleSetVersion =
            instance.TrustClass
                == ProviderTrustClass.OfficialCloudHttps
                ? ProviderOfficialCloudEndpointPolicy.RuleSetVersion
                : 0;
        SelectedAt = selectedAt;
        ExpiresAt = expiresAt;
        RouteFingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-ROUTE-SELECTION/1",
            ProviderRegistryCanonicalHash.Number(RegistryVersion),
            ProviderRegistryCanonicalHash.Number(
                RegistryGeneration),
            RegistryFingerprint,
            DefinitionId.Value,
            InstanceId.Value,
            ProviderRegistryCanonicalHash.Number((int)Kind),
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration),
            InstanceConfigurationFingerprint,
            InstanceFingerprint,
            EndpointFingerprint,
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            ProviderRegistryCanonicalHash.Number(
                ProtocolProfileVersion),
            ModelInstanceId.Value,
            ModelDescriptorFingerprint,
            CorrelationHash,
            ProviderRegistryCanonicalHash.Number(
                (int)ExecutionVenue),
            ProviderRegistryCanonicalHash.Number(
                (int)ExecutionVenueProvenance),
            ExecutionVenueEvidenceFingerprint,
            ProviderRegistryCanonicalHash.Number(
                (int)ProcessingLocalityRequirement),
            RequiredCapabilitiesFingerprint,
            CapabilityProfileFingerprint,
            ProcessingPolicyFingerprint,
            ProviderRegistryCanonicalHash.Number(
                OfficialCloudEndpointRuleSetVersion),
            ProviderRegistryCanonicalHash.Timestamp(SelectedAt),
            ProviderRegistryCanonicalHash.Timestamp(ExpiresAt));
        SelectionId = "d027-route-"
            + RouteFingerprint.Substring(0, 32);
    }

    internal static ProviderRouteSelectionResult
        CreateForContractReplay(
            ProviderRegistrySnapshot snapshot,
            ProviderEndpointInstanceId instanceId,
            ProviderModelInstanceId modelId,
            ProviderCapabilityRequirementSet requiredCapabilities,
            ProcessingLocalityRequirement localityRequirement,
            string processingPolicyFingerprint,
            DateTimeOffset selectedAt,
            DateTimeOffset expiresAt)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (instanceId == null)
            throw new ArgumentNullException(nameof(instanceId));
        if (modelId == null)
            throw new ArgumentNullException(nameof(modelId));
        if (requiredCapabilities == null)
        {
            throw new ArgumentNullException(
                nameof(requiredCapabilities));
        }
        localityRequirement = ProviderRegistryContractGuard.Defined(
            localityRequirement,
            nameof(localityRequirement));
        processingPolicyFingerprint =
            ProviderRegistryContractGuard.Sha256(
                processingPolicyFingerprint,
                nameof(processingPolicyFingerprint));
        selectedAt = DecisionCoreContractGuard.Timestamp(
            selectedAt,
            nameof(selectedAt));
        expiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (expiresAt <= selectedAt)
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission
                    .InvalidSelectionWindow);
        }
        if (!snapshot.TryGetInstance(instanceId, out var instance)
            || instance == null)
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission.UnknownInstance);
        }
        if (!instance.Enabled)
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission.InstanceDisabled);
        }
        if (!snapshot.TryGetModel(
                instanceId,
                modelId,
                out var model)
            || model == null)
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission.UnknownModel);
        }
        if (requiredCapabilities.Requirements.Any(
                capability =>
                    !model.CapabilityProfile.IsAuthorized(
                        capability)))
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission
                    .RequiredCapabilityNotAuthorized);
        }
        if (!ProviderProcessingLocalityPolicy.Allows(
                model,
                localityRequirement,
                selectedAt))
        {
            return ProviderRouteSelectionResult.Reject(
                ProviderRouteSelectionAdmission
                    .ProcessingLocalityRejected);
        }

        return ProviderRouteSelectionResult.Accept(
            new ProviderRouteSelection(
                snapshot,
                instance,
                model,
                requiredCapabilities,
                localityRequirement,
                processingPolicyFingerprint,
                selectedAt,
                expiresAt));
    }
}

internal static class ProviderProcessingLocalityPolicy
{
    internal static bool Allows(
        ProviderModelDescriptor model,
        ProcessingLocalityRequirement requirement,
        DateTimeOffset evaluatedAt)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        requirement = ProviderRegistryContractGuard.Defined(
            requirement,
            nameof(requirement));
        evaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (requirement
            == ProcessingLocalityRequirement
                .ExternalProcessingAllowed)
        {
            return true;
        }

        var evidence = model.ExecutionVenueEvidence;
        var verifiedCurrent =
            evidence.Freshness == ProviderEvidenceFreshness.Current
            && evidence.AssessedAt <= evaluatedAt
            && (!evidence.ValidUntil.HasValue
                || evaluatedAt < evidence.ValidUntil.Value)
            && model.ExecutionVenueProvenance
                != ProviderExecutionVenueProvenance.Unknown;
        if (!verifiedCurrent)
            return false;

        return requirement switch
        {
            ProcessingLocalityRequirement.LocalProcessRequired =>
                model.ExecutionVenue == ExecutionVenue.LocalProcess
                && model.ExecutionVenueProvenance
                    == ProviderExecutionVenueProvenance
                        .VerifiedLocalRuntime,
            ProcessingLocalityRequirement.SelfHostedAllowed =>
                model.ExecutionVenue == ExecutionVenue.LocalProcess
                    && model.ExecutionVenueProvenance
                        == ProviderExecutionVenueProvenance
                            .VerifiedLocalRuntime
                || model.ExecutionVenue
                    == ExecutionVenue.SelfHostedRemote,
            _ => false
        };
    }
}

internal enum ProviderRouteAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    SelectionNotYetValid = 2,
    SelectionExpired = 3,
    RegistryVersionMismatch = 4,
    RegistryGenerationMismatch = 5,
    RegistryFingerprintMismatch = 6,
    InstanceNotFound = 7,
    InstanceDisabled = 8,
    DefinitionMismatch = 9,
    ConfigurationGenerationMismatch = 10,
    InstanceFingerprintMismatch = 11,
    EndpointFingerprintMismatch = 12,
    ProtocolMismatch = 13,
    ProtocolProfileVersionMismatch = 14,
    ModelNotFound = 15,
    ModelFingerprintMismatch = 16,
    ModelCorrelationMismatch = 17,
    CapabilityProfileMismatch = 18,
    ProcessingPolicyMismatch = 19,
    ExecutionVenueMismatch = 20,
    ProcessingLocalityRejected = 21,
    EndpointRejected = 22,
    EndpointPolicyVersionMismatch = 23
}

internal sealed class ProviderRouteAdmissionResult
{
    internal ProviderRouteAdmission Admission { get; }
    internal ProviderRouteSelection? AdmittedSelection { get; }
    internal ProviderEndpointAdmission?
        EndpointAdmission { get; }
    internal bool Accepted =>
        Admission == ProviderRouteAdmission.AcceptedForContractReplay;
    internal bool AllowsProviderCall => false;

    private ProviderRouteAdmissionResult(
        ProviderRouteAdmission admission,
        ProviderRouteSelection? admittedSelection,
        ProviderEndpointAdmission? endpointAdmission)
    {
        if (!Enum.IsDefined(
                typeof(ProviderRouteAdmission),
                admission)
            || admission == ProviderRouteAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (endpointAdmission.HasValue
            && (!Enum.IsDefined(
                    typeof(ProviderEndpointAdmission),
                    endpointAdmission.Value)
                || endpointAdmission.Value
                    == ProviderEndpointAdmission.Invalid))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endpointAdmission));
        }
        if (admission
                == ProviderRouteAdmission.AcceptedForContractReplay
            && (admittedSelection == null
                || endpointAdmission
                    != ProviderEndpointAdmission
                        .AcceptedForContractReplay))
        {
            throw new ArgumentException(
                "Accepted route must carry its selection and endpoint admission.");
        }
        if (admission
                == ProviderRouteAdmission.EndpointRejected
            && (!endpointAdmission.HasValue
                || endpointAdmission.Value
                    == ProviderEndpointAdmission
                        .AcceptedForContractReplay))
        {
            throw new ArgumentException(
                "Endpoint rejection must carry its endpoint reason.",
                nameof(endpointAdmission));
        }
        if (admission
                != ProviderRouteAdmission.AcceptedForContractReplay
            && admittedSelection != null)
        {
            throw new ArgumentException(
                "Rejected route cannot carry an admitted selection.",
                nameof(admittedSelection));
        }
        if (admission
                is not ProviderRouteAdmission
                    .AcceptedForContractReplay
                and not ProviderRouteAdmission.EndpointRejected
            && endpointAdmission.HasValue)
        {
            throw new ArgumentException(
                "Pre-endpoint rejection cannot carry endpoint admission.",
                nameof(endpointAdmission));
        }

        Admission = admission;
        AdmittedSelection = admittedSelection;
        EndpointAdmission = endpointAdmission;
    }

    internal static ProviderRouteAdmissionResult Accept(
        ProviderRouteSelection selection)
        => new(
            ProviderRouteAdmission.AcceptedForContractReplay,
            selection
                ?? throw new ArgumentNullException(nameof(selection)),
            ProviderEndpointAdmission.AcceptedForContractReplay);

    internal static ProviderRouteAdmissionResult Reject(
        ProviderRouteAdmission admission)
        => new(admission, null, null);

    internal static ProviderRouteAdmissionResult RejectEndpoint(
        ProviderEndpointAdmission endpointAdmission)
        => new(
            ProviderRouteAdmission.EndpointRejected,
            null,
            endpointAdmission);
}

internal static class ProviderRouteAdmissionPolicy
{
    internal static ProviderRouteAdmissionResult Evaluate(
        ProviderRegistrySnapshot currentSnapshot,
        ProviderRouteSelection selection,
        ProviderEndpointNetworkEvidence endpointEvidence,
        string activeProcessingPolicyFingerprint,
        DateTimeOffset evaluatedAt)
    {
        if (currentSnapshot == null)
        {
            throw new ArgumentNullException(
                nameof(currentSnapshot));
        }
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        if (endpointEvidence == null)
        {
            throw new ArgumentNullException(
                nameof(endpointEvidence));
        }
        activeProcessingPolicyFingerprint =
            ProviderRegistryContractGuard.Sha256(
                activeProcessingPolicyFingerprint,
                nameof(activeProcessingPolicyFingerprint));
        evaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));

        if (evaluatedAt < selection.SelectedAt)
        {
            return Reject(
                ProviderRouteAdmission.SelectionNotYetValid);
        }
        if (evaluatedAt >= selection.ExpiresAt)
        {
            return Reject(
                ProviderRouteAdmission.SelectionExpired);
        }
        if (currentSnapshot.RegistryVersion
            != selection.RegistryVersion)
        {
            return Reject(
                ProviderRouteAdmission.RegistryVersionMismatch);
        }
        if (currentSnapshot.RegistryGeneration
            != selection.RegistryGeneration)
        {
            return Reject(
                ProviderRouteAdmission.RegistryGenerationMismatch);
        }
        if (!string.Equals(
                currentSnapshot.Fingerprint,
                selection.RegistryFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.RegistryFingerprintMismatch);
        }
        if (!currentSnapshot.TryGetInstance(
                selection.InstanceId,
                out var instance)
            || instance == null)
        {
            return Reject(
                ProviderRouteAdmission.InstanceNotFound);
        }
        if (!instance.Enabled)
        {
            return Reject(
                ProviderRouteAdmission.InstanceDisabled);
        }
        if (!instance.DefinitionId.Equals(selection.DefinitionId)
            || instance.Kind != selection.Kind)
        {
            return Reject(
                ProviderRouteAdmission.DefinitionMismatch);
        }
        if (instance.ConfigurationGeneration
            != selection.ConfigurationGeneration)
        {
            return Reject(
                ProviderRouteAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (!string.Equals(
                instance.EndpointFingerprint,
                selection.EndpointFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.EndpointFingerprintMismatch);
        }
        if (instance.Protocol != selection.Protocol)
        {
            return Reject(
                ProviderRouteAdmission.ProtocolMismatch);
        }
        if (instance.ProtocolProfileVersion
            != selection.ProtocolProfileVersion)
        {
            return Reject(
                ProviderRouteAdmission
                    .ProtocolProfileVersionMismatch);
        }
        if (!string.Equals(
                instance.ConfigurationFingerprint,
                selection.InstanceConfigurationFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                instance.Fingerprint,
                selection.InstanceFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.InstanceFingerprintMismatch);
        }
        var officialCloudEndpointRuleSetVersion =
            instance.TrustClass
                == ProviderTrustClass.OfficialCloudHttps
                ? ProviderOfficialCloudEndpointPolicy.RuleSetVersion
                : 0;
        if (selection.OfficialCloudEndpointRuleSetVersion
            != officialCloudEndpointRuleSetVersion)
        {
            return Reject(
                ProviderRouteAdmission
                    .EndpointPolicyVersionMismatch);
        }
        if (!currentSnapshot.TryGetModel(
                selection.InstanceId,
                selection.ModelInstanceId,
                out var model)
            || model == null)
        {
            return Reject(
                ProviderRouteAdmission.ModelNotFound);
        }
        if (!string.Equals(
                model.CorrelationHash,
                selection.CorrelationHash,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.ModelCorrelationMismatch);
        }
        if (!string.Equals(
                model.CapabilityProfile.Fingerprint,
                selection.CapabilityProfileFingerprint,
                StringComparison.Ordinal)
            || selection.RequiredCapabilities.Requirements.Any(
                capability =>
                    !model.CapabilityProfile.IsAuthorized(
                        capability)))
        {
            return Reject(
                ProviderRouteAdmission.CapabilityProfileMismatch);
        }
        if (model.ExecutionVenue != selection.ExecutionVenue
            || model.ExecutionVenueProvenance
                != selection.ExecutionVenueProvenance
            || !string.Equals(
                model.ExecutionVenueEvidence.Fingerprint,
                selection.ExecutionVenueEvidenceFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.ExecutionVenueMismatch);
        }
        if (!string.Equals(
                model.Fingerprint,
                selection.ModelDescriptorFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.ModelFingerprintMismatch);
        }
        if (!string.Equals(
                activeProcessingPolicyFingerprint,
                selection.ProcessingPolicyFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderRouteAdmission.ProcessingPolicyMismatch);
        }
        if (!ProviderProcessingLocalityPolicy.Allows(
                model,
                selection.ProcessingLocalityRequirement,
                evaluatedAt))
        {
            return Reject(
                ProviderRouteAdmission.ProcessingLocalityRejected);
        }

        var endpointAdmission =
            ProviderEndpointAdmissionPolicy.Evaluate(
                instance,
                endpointEvidence,
                evaluatedAt);
        if (!endpointAdmission.Accepted)
        {
            return ProviderRouteAdmissionResult.RejectEndpoint(
                endpointAdmission.Admission);
        }
        return ProviderRouteAdmissionResult.Accept(selection);
    }

    private static ProviderRouteAdmissionResult Reject(
        ProviderRouteAdmission admission)
        => ProviderRouteAdmissionResult.Reject(admission);
}

internal static class ProviderRouteAdmissionContractGuard
{
    internal static string Sha256(
        string? value,
        string parameterName)
        => ProviderRegistryContractGuard.Sha256(
            value,
            parameterName);

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
        => DecisionCoreContractGuard.Timestamp(
            value,
            parameterName);
}

internal static class ProviderRouteAdmissionContractHash
{
    internal static string Of(params string[] fields)
    {
        if (fields == null)
            throw new ArgumentNullException(nameof(fields));
        for (var index = 0; index < fields.Length; index++)
        {
            if (fields[index] == null)
            {
                throw new ArgumentException(
                    "Fingerprint fields cannot contain null.",
                    nameof(fields));
            }
        }
        if (fields.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fields));
        }
        return ProviderRegistryCanonicalHash.Of(
            fields[0],
            fields.Skip(1).ToArray());
    }
}
