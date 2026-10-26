using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LilithProviderControlPlane;

internal enum UnifiedProviderConnectedAddressAdmission
{
    Invalid = 0,
    Accepted = 1,
    Rejected = 2
}

internal enum UnifiedProviderConnectedAddressReason
{
    Invalid = 0,
    Accepted = 1,
    MissingTransportContext = 2,
    InvalidEndpoint = 3,
    ContextExpired = 4,
    ResolutionFailed = 5,
    ResolutionEmpty = 6,
    UnsafeAddress = 7,
    MixedAddressScopes = 8,
    RouteScopeMismatch = 9,
    DuplicateUse = 10,
    ConnectionEndpointMismatch = 11,
    ConnectedAddressMismatch = 12,
    Cancelled = 13
}

internal enum UnifiedProviderConnectedAddressRouteScope
{
    Invalid = 0,
    LoopbackOnly = 1,
    PrivateLanOnly = 2,
    PublicOnly = 3
}

internal enum UnifiedProviderConnectedAddressScope
{
    Invalid = 0,
    Loopback = 1,
    PrivateNetwork = 2,
    Public = 3,
    LinkLocal = 4,
    Metadata = 5,
    Unspecified = 6,
    Multicast = 7,
    Broadcast = 8,
    Reserved = 9,
    IPv4MappedToIPv6 = 10
}

/// <summary>
/// Immutable identity and deadline for exactly one Provider HTTP attempt.
/// The configured endpoint remains the request URI so HTTP Host and TLS SNI
/// retain the configured hostname; only the socket destination is pinned.
/// </summary>
internal sealed class UnifiedProviderConnectedAddressContext
{
    internal string InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal string EndpointFingerprint { get; }
    internal string AttemptId { get; }
    internal Uri Endpoint { get; }
    internal UnifiedProviderConnectedAddressRouteScope RouteScope { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset Deadline { get; }
    internal string Fingerprint { get; }

    internal UnifiedProviderConnectedAddressContext(
        string instanceId,
        long configurationGeneration,
        string configurationFingerprint,
        string endpointFingerprint,
        string attemptId,
        Uri endpoint,
        UnifiedProviderConnectedAddressRouteScope routeScope,
        DateTimeOffset requestedAt,
        DateTimeOffset deadline)
    {
        InstanceId = Bounded(instanceId, 96, nameof(instanceId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = Sha256(
            configurationFingerprint,
            nameof(configurationFingerprint));
        EndpointFingerprint = Sha256(endpointFingerprint, nameof(endpointFingerprint));
        AttemptId = Bounded(attemptId, 128, nameof(attemptId));
        Endpoint = NormalizeEndpoint(endpoint);
        if (!Enum.IsDefined(typeof(UnifiedProviderConnectedAddressRouteScope), routeScope)
            || routeScope == UnifiedProviderConnectedAddressRouteScope.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(routeScope));
        }
        RouteScope = routeScope;
        if (requestedAt == default || deadline <= requestedAt)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        RequestedAt = requestedAt;
        Deadline = deadline;
        Fingerprint = Hash(
            "D033/CONNECTED-ADDRESS-CONTEXT/1",
            InstanceId,
            ConfigurationGeneration.ToString(CultureInfo.InvariantCulture),
            ConfigurationFingerprint,
            EndpointFingerprint,
            AttemptId,
            Endpoint.AbsoluteUri,
            ((int)RouteScope).ToString(CultureInfo.InvariantCulture),
            RequestedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Deadline.UtcTicks.ToString(CultureInfo.InvariantCulture));
    }

    internal static string FingerprintEndpoint(
        Uri endpoint,
        UnifiedProviderConnectedAddressRouteScope scope)
        => Hash(
            "D033/CONNECTED-ENDPOINT/1",
            NormalizeEndpoint(endpoint).AbsoluteUri,
            ((int)scope).ToString(CultureInfo.InvariantCulture));

    internal static string FingerprintConfiguration(params string[] fields)
        => Hash(
            "D033/CONNECTED-CONFIGURATION/1",
            fields ?? Array.Empty<string>());

    internal static UnifiedProviderConnectedAddressRouteScope FromRouteIntent(
        ConfiguredEndpointRouteIntent intent)
    {
        if (intent == null)
            throw new ArgumentNullException(nameof(intent));
        return intent.Kind switch
        {
            ConfiguredEndpointRouteKind.LoopbackOnly =>
                UnifiedProviderConnectedAddressRouteScope.LoopbackOnly,
            ConfiguredEndpointRouteKind.ExplicitLan =>
                UnifiedProviderConnectedAddressRouteScope.PrivateLanOnly,
            ConfiguredEndpointRouteKind.ManagedCloudHttps
                or ConfiguredEndpointRouteKind.ExplicitRemoteHttps =>
                UnifiedProviderConnectedAddressRouteScope.PublicOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(intent))
        };
    }

    private static Uri NormalizeEndpoint(Uri endpoint)
    {
        if (endpoint == null || !endpoint.IsAbsoluteUri)
            throw new ArgumentException("An absolute endpoint is required.", nameof(endpoint));
        if (endpoint.Scheme != Uri.UriSchemeHttp
            && endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0
            || endpoint.HostNameType == UriHostNameType.Unknown)
        {
            throw new ArgumentException("The endpoint is not transport-safe.", nameof(endpoint));
        }
        return new UriBuilder(endpoint)
        {
            Host = endpoint.IdnHost.ToLowerInvariant()
        }.Uri;
    }

    private static string Bounded(string? value, int maximum, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximum
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded identifier is required.", parameterName);
        }
        return value.Trim();
    }

    private static string Sha256(string? value, string parameterName)
    {
        if (value?.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 fingerprint is required.", parameterName);
        return value.ToUpperInvariant();
    }

    internal static string Hash(string domain, params string[] values)
    {
        var payload = string.Join("\n", new[] { domain }.Concat(values));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

internal sealed class UnifiedProviderConnectedAddressPlan
{
    private int _consumed;

    internal UnifiedProviderConnectedAddressContext Context { get; }
    internal IPAddress SelectedAddress { get; }
    internal IReadOnlyList<IPAddress> ResolvedAddresses { get; }
    internal DateTimeOffset ResolvedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }
    internal bool Consumed => Volatile.Read(ref _consumed) != 0;

    internal UnifiedProviderConnectedAddressPlan(
        UnifiedProviderConnectedAddressContext context,
        IPAddress selectedAddress,
        IEnumerable<IPAddress> resolvedAddresses,
        DateTimeOffset resolvedAt,
        DateTimeOffset expiresAt)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        SelectedAddress = selectedAddress
            ?? throw new ArgumentNullException(nameof(selectedAddress));
        var frozen = (resolvedAddresses
                ?? throw new ArgumentNullException(nameof(resolvedAddresses)))
            .Select(value => value ?? throw new ArgumentException("Null address."))
            .Distinct()
            .OrderBy(CanonicalAddress, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Length == 0 || !frozen.Contains(selectedAddress))
            throw new ArgumentException("The selected address must be admitted.");
        if (resolvedAt == default
            || expiresAt <= resolvedAt
            || expiresAt > context.Deadline)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }
        ResolvedAddresses = new ReadOnlyCollection<IPAddress>(frozen);
        ResolvedAt = resolvedAt;
        ExpiresAt = expiresAt;
        Fingerprint = UnifiedProviderConnectedAddressContext.Hash(
            "D033/CONNECTED-ADDRESS-PLAN/1",
            Context.Fingerprint,
            CanonicalAddress(SelectedAddress),
            string.Join(",", frozen.Select(CanonicalAddress)),
            ResolvedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            ExpiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
    }

    internal bool TryConsume(DateTimeOffset now)
        => now >= ResolvedAt
           && now < ExpiresAt
           && Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;

    internal static string CanonicalAddress(IPAddress address)
        => string.Concat(
            ((int)address.AddressFamily).ToString("D2", CultureInfo.InvariantCulture),
            ":",
            Convert.ToHexString(address.GetAddressBytes()));
}

internal sealed class UnifiedProviderConnectedAddressResult
{
    internal UnifiedProviderConnectedAddressAdmission Admission { get; }
    internal UnifiedProviderConnectedAddressReason Reason { get; }
    internal UnifiedProviderConnectedAddressPlan? Plan { get; }
    internal int ResolutionCalls { get; }
    internal bool Accepted =>
        Admission == UnifiedProviderConnectedAddressAdmission.Accepted;

    internal UnifiedProviderConnectedAddressResult(
        UnifiedProviderConnectedAddressAdmission admission,
        UnifiedProviderConnectedAddressReason reason,
        UnifiedProviderConnectedAddressPlan? plan,
        int resolutionCalls)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderConnectedAddressAdmission), admission)
            || admission == UnifiedProviderConnectedAddressAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (!Enum.IsDefined(typeof(UnifiedProviderConnectedAddressReason), reason)
            || reason == UnifiedProviderConnectedAddressReason.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if (resolutionCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(resolutionCalls));
        if ((admission == UnifiedProviderConnectedAddressAdmission.Accepted)
            != (plan != null))
        {
            throw new ArgumentException("Only accepted results carry a plan.");
        }
        Admission = admission;
        Reason = reason;
        Plan = plan;
        ResolutionCalls = resolutionCalls;
    }
}

internal interface IUnifiedProviderAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveOnceAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class UnifiedProviderSystemAddressResolver
    : IUnifiedProviderAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveOnceAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return new[] { literal };
        var addresses = await Dns.GetHostAddressesAsync(host)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return addresses;
    }
}

internal sealed class UnifiedProviderConnectedStream : IDisposable
{
    internal Stream Stream { get; }
    internal IPAddress RemoteAddress { get; }

    internal UnifiedProviderConnectedStream(Stream stream, IPAddress remoteAddress)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        RemoteAddress = remoteAddress
            ?? throw new ArgumentNullException(nameof(remoteAddress));
    }

    public void Dispose() => Stream.Dispose();
}

internal interface IUnifiedProviderPinnedConnector
{
    ValueTask<UnifiedProviderConnectedStream> ConnectOnceAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

internal sealed class UnifiedProviderSocketPinnedConnector
    : IUnifiedProviderPinnedConnector
{
    public async ValueTask<UnifiedProviderConnectedStream> ConnectOnceAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(
                    new IPEndPoint(address, port),
                    cancellationToken)
                .ConfigureAwait(false);
            if (socket.RemoteEndPoint is not IPEndPoint remote)
                throw new IOException("The connected remote endpoint is unavailable.");
            return new UnifiedProviderConnectedStream(
                new NetworkStream(socket, ownsSocket: true),
                remote.Address);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal interface IUnifiedProviderConnectedAddressBoundary
{
    Task<UnifiedProviderConnectedAddressResult> PrepareAsync(
        UnifiedProviderConnectedAddressContext context,
        CancellationToken cancellationToken);

    void Bind(
        HttpRequestMessage request,
        UnifiedProviderConnectedAddressPlan plan);
}

internal sealed class UnifiedProviderConnectedAddressBoundary
    : IUnifiedProviderConnectedAddressBoundary
{
    internal static TimeSpan MaximumResolutionLifetime { get; } =
        TimeSpan.FromSeconds(15);

    private static readonly HttpRequestOptionsKey<UnifiedProviderConnectedAddressPlan>
        RequestPlanKey = new("Lilith.D033.ConnectedAddressPlan.v1");

    private readonly IUnifiedProviderAddressResolver _resolver;
    private readonly IUnifiedProviderPinnedConnector _connector;
    private readonly Func<DateTimeOffset> _clock;

    internal UnifiedProviderConnectedAddressBoundary(
        IUnifiedProviderAddressResolver? resolver = null,
        IUnifiedProviderPinnedConnector? connector = null,
        Func<DateTimeOffset>? clock = null)
    {
        _resolver = resolver ?? new UnifiedProviderSystemAddressResolver();
        _connector = connector ?? new UnifiedProviderSocketPinnedConnector();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<UnifiedProviderConnectedAddressResult> PrepareAsync(
        UnifiedProviderConnectedAddressContext context,
        CancellationToken cancellationToken)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        var now = _clock();
        if (now < context.RequestedAt || now >= context.Deadline)
            return Reject(UnifiedProviderConnectedAddressReason.ContextExpired, 0);

        IReadOnlyList<IPAddress> resolved;
        try
        {
            resolved = await _resolver.ResolveOnceAsync(
                    context.Endpoint.IdnHost,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Reject(UnifiedProviderConnectedAddressReason.Cancelled, 1);
        }
        catch (Exception exception) when (
            exception is SocketException
                or ArgumentException
                or InvalidOperationException)
        {
            return Reject(UnifiedProviderConnectedAddressReason.ResolutionFailed, 1);
        }

        var addresses = (resolved ?? Array.Empty<IPAddress>())
            .Where(value => value != null)
            .Distinct()
            .OrderBy(UnifiedProviderConnectedAddressPlan.CanonicalAddress, StringComparer.Ordinal)
            .ToArray();
        if (addresses.Length == 0)
            return Reject(UnifiedProviderConnectedAddressReason.ResolutionEmpty, 1);

        var scopes = addresses.Select(Classify).Distinct().ToArray();
        if (scopes.Any(scope => !SafeScope(scope)))
            return Reject(UnifiedProviderConnectedAddressReason.UnsafeAddress, 1);
        if (scopes.Length != 1)
            return Reject(UnifiedProviderConnectedAddressReason.MixedAddressScopes, 1);
        if (!MatchesRoute(context.RouteScope, scopes[0]))
            return Reject(UnifiedProviderConnectedAddressReason.RouteScopeMismatch, 1);

        var resolvedAt = _clock();
        if (resolvedAt < context.RequestedAt || resolvedAt >= context.Deadline)
            return Reject(UnifiedProviderConnectedAddressReason.ContextExpired, 1);
        var expiresAt = Min(
            context.Deadline,
            resolvedAt + MaximumResolutionLifetime);
        var plan = new UnifiedProviderConnectedAddressPlan(
            context,
            addresses[0],
            addresses,
            resolvedAt,
            expiresAt);
        return new UnifiedProviderConnectedAddressResult(
            UnifiedProviderConnectedAddressAdmission.Accepted,
            UnifiedProviderConnectedAddressReason.Accepted,
            plan,
            1);
    }

    public void Bind(
        HttpRequestMessage request,
        UnifiedProviderConnectedAddressPlan plan)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (request.RequestUri == null || !SameAuthority(request.RequestUri, plan.Context.Endpoint))
            throw new InvalidOperationException("The request authority changed after admission.");
        request.Options.Set(RequestPlanKey, plan);
    }

    internal async ValueTask<Stream> ConnectForHttpAsync(
        SocketsHttpConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (connection == null
            || !connection.InitialRequestMessage.Options.TryGetValue(
                RequestPlanKey,
                out var plan)
            || plan == null)
        {
            throw new HttpRequestException(
                UnifiedProviderConnectedAddressReason.MissingTransportContext.ToString());
        }
        var expectedPort = plan.Context.Endpoint.IsDefaultPort
            ? plan.Context.Endpoint.Scheme == Uri.UriSchemeHttps ? 443 : 80
            : plan.Context.Endpoint.Port;
        if (!string.Equals(
                connection.DnsEndPoint.Host,
                plan.Context.Endpoint.IdnHost,
                StringComparison.OrdinalIgnoreCase)
            || connection.DnsEndPoint.Port != expectedPort)
        {
            throw new HttpRequestException(
                UnifiedProviderConnectedAddressReason.ConnectionEndpointMismatch.ToString());
        }
        return await ConnectPinnedForTestAsync(
                plan,
                connection.DnsEndPoint.Host,
                connection.DnsEndPoint.Port,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async ValueTask<Stream> ConnectPinnedForTestAsync(
        UnifiedProviderConnectedAddressPlan plan,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        var expectedPort = plan.Context.Endpoint.IsDefaultPort
            ? plan.Context.Endpoint.Scheme == Uri.UriSchemeHttps ? 443 : 80
            : plan.Context.Endpoint.Port;
        if (!string.Equals(host, plan.Context.Endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)
            || port != expectedPort)
        {
            throw new HttpRequestException(
                UnifiedProviderConnectedAddressReason.ConnectionEndpointMismatch.ToString());
        }
        var now = _clock();
        if (!plan.TryConsume(now))
        {
            throw new HttpRequestException(
                (now >= plan.ExpiresAt
                    ? UnifiedProviderConnectedAddressReason.ContextExpired
                    : UnifiedProviderConnectedAddressReason.DuplicateUse).ToString());
        }

        UnifiedProviderConnectedStream? connected = null;
        try
        {
            connected = await _connector.ConnectOnceAsync(
                    plan.SelectedAddress,
                    port,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!connected.RemoteAddress.Equals(plan.SelectedAddress)
                || !plan.ResolvedAddresses.Contains(connected.RemoteAddress)
                || !MatchesRoute(plan.Context.RouteScope, Classify(connected.RemoteAddress)))
            {
                throw new HttpRequestException(
                    UnifiedProviderConnectedAddressReason.ConnectedAddressMismatch.ToString());
            }
            var stream = connected.Stream;
            connected = null;
            return stream;
        }
        finally
        {
            connected?.Dispose();
        }
    }

    internal static SocketsHttpHandler CreateHandler(
        UnifiedProviderConnectedAddressBoundary boundary,
        int maximumConnectionsPerServer)
    {
        if (boundary == null)
            throw new ArgumentNullException(nameof(boundary));
        if (maximumConnectionsPerServer is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maximumConnectionsPerServer));
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            MaxConnectionsPerServer = maximumConnectionsPerServer,
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            ConnectCallback = boundary.ConnectForHttpAsync,
            SslOptions =
            {
                CertificateRevocationCheckMode = X509RevocationMode.Online,
                EnabledSslProtocols = SslProtocols.None
            }
        };
    }

    internal static UnifiedProviderConnectedAddressScope Classify(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            return UnifiedProviderConnectedAddressScope.IPv4MappedToIPv6;
        if (IPAddress.IsLoopback(address))
            return UnifiedProviderConnectedAddressScope.Loopback;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return UnifiedProviderConnectedAddressScope.Unspecified;
        if (address.IsIPv6Multicast)
            return UnifiedProviderConnectedAddressScope.Multicast;
        if (address.IsIPv6LinkLocal)
            return UnifiedProviderConnectedAddressScope.LinkLocal;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes.SequenceEqual(new byte[] { 255, 255, 255, 255 }))
                return UnifiedProviderConnectedAddressScope.Broadcast;
            if ((bytes[0] & 0xF0) == 0xE0)
                return UnifiedProviderConnectedAddressScope.Multicast;
            if ((bytes[0] == 169 && bytes[1] == 254
                    && ((bytes[2] == 169 && bytes[3] == 254)
                        || (bytes[2] == 170 && bytes[3] == 2)))
                || bytes.SequenceEqual(new byte[] { 100, 100, 100, 200 }))
            {
                return UnifiedProviderConnectedAddressScope.Metadata;
            }
            if (bytes[0] == 169 && bytes[1] == 254)
                return UnifiedProviderConnectedAddressScope.LinkLocal;
            if (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168))
            {
                return UnifiedProviderConnectedAddressScope.PrivateNetwork;
            }
            if (bytes[0] == 0
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 0x40)
                || (bytes[0] == 192 && bytes[1] == 0)
                || (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                || (bytes[0] == 198 && bytes[1] is 18 or 19)
                || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                || bytes[0] >= 240)
            {
                return UnifiedProviderConnectedAddressScope.Reserved;
            }
            return UnifiedProviderConnectedAddressScope.Public;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // AWS IMDS IPv6 endpoint.  It must be classified before the
            // broader ULA branch or a LAN declaration could admit metadata.
            if (address.Equals(IPAddress.Parse("fd00:ec2::254")))
                return UnifiedProviderConnectedAddressScope.Metadata;
            if (bytes[0] is 0xFC or 0xFD || address.IsIPv6SiteLocal)
                return UnifiedProviderConnectedAddressScope.PrivateNetwork;
            if (bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0D
                && bytes[3] == 0xB8)
            {
                return UnifiedProviderConnectedAddressScope.Reserved;
            }
            return UnifiedProviderConnectedAddressScope.Public;
        }
        return UnifiedProviderConnectedAddressScope.Reserved;
    }

    private static bool SafeScope(UnifiedProviderConnectedAddressScope scope)
        => scope is UnifiedProviderConnectedAddressScope.Loopback
            or UnifiedProviderConnectedAddressScope.PrivateNetwork
            or UnifiedProviderConnectedAddressScope.Public;

    private static bool MatchesRoute(
        UnifiedProviderConnectedAddressRouteScope route,
        UnifiedProviderConnectedAddressScope scope)
        => route switch
        {
            UnifiedProviderConnectedAddressRouteScope.LoopbackOnly =>
                scope == UnifiedProviderConnectedAddressScope.Loopback,
            UnifiedProviderConnectedAddressRouteScope.PrivateLanOnly =>
                scope == UnifiedProviderConnectedAddressScope.PrivateNetwork,
            UnifiedProviderConnectedAddressRouteScope.PublicOnly =>
                scope == UnifiedProviderConnectedAddressScope.Public,
            _ => false
        };

    private static bool SameAuthority(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;

    private static UnifiedProviderConnectedAddressResult Reject(
        UnifiedProviderConnectedAddressReason reason,
        int resolutionCalls)
        => new(
            UnifiedProviderConnectedAddressAdmission.Rejected,
            reason,
            null,
            resolutionCalls);
}

internal sealed class UnifiedProviderFailClosedConnectedAddressBoundary
    : IUnifiedProviderConnectedAddressBoundary
{
    internal static UnifiedProviderFailClosedConnectedAddressBoundary Instance { get; }
        = new();

    private UnifiedProviderFailClosedConnectedAddressBoundary()
    {
    }

    public Task<UnifiedProviderConnectedAddressResult> PrepareAsync(
        UnifiedProviderConnectedAddressContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new UnifiedProviderConnectedAddressResult(
            UnifiedProviderConnectedAddressAdmission.Rejected,
            UnifiedProviderConnectedAddressReason.MissingTransportContext,
            null,
            0));

    public void Bind(HttpRequestMessage request, UnifiedProviderConnectedAddressPlan plan)
        => throw new InvalidOperationException(
            UnifiedProviderConnectedAddressReason.MissingTransportContext.ToString());
}

internal sealed class UnifiedProviderConnectedAddressHttpClient : HttpClient
{
    internal IUnifiedProviderConnectedAddressBoundary ConnectedAddressBoundary { get; }

    internal UnifiedProviderConnectedAddressHttpClient(
        SocketsHttpHandler handler,
        IUnifiedProviderConnectedAddressBoundary boundary)
        : base(handler ?? throw new ArgumentNullException(nameof(handler)), disposeHandler: true)
    {
        ConnectedAddressBoundary = boundary
            ?? throw new ArgumentNullException(nameof(boundary));
        Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}

internal static class UnifiedProviderConnectedAddressHttp
{
    internal static HttpClient CreateClient(int maximumConnectionsPerServer)
    {
        var boundary = new UnifiedProviderConnectedAddressBoundary();
        var handler = UnifiedProviderConnectedAddressBoundary.CreateHandler(
            boundary,
            maximumConnectionsPerServer);
        return new UnifiedProviderConnectedAddressHttpClient(handler, boundary);
    }

    internal static IUnifiedProviderConnectedAddressBoundary BoundaryFor(
        HttpClient client,
        IUnifiedProviderConnectedAddressBoundary? explicitBoundary = null)
        => explicitBoundary
           ?? (client as UnifiedProviderConnectedAddressHttpClient)
               ?.ConnectedAddressBoundary
           ?? UnifiedProviderFailClosedConnectedAddressBoundary.Instance;
}
