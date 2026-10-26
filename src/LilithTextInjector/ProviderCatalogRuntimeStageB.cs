using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum ProviderCatalogRuntimeMode
{
    Invalid = 0,
    Off = 1,
    Armed = 2,
    Fault = 3,
    Capacity = 4
}

internal enum ProviderCatalogRuntimeBindingAdmission
{
    Invalid = 0,
    Accepted = 1,
    InstanceUnavailable = 2,
    InstanceDisabled = 3,
    ProviderIdentityMismatch = 4,
    IdentityEvidenceInsufficient = 5,
    EndpointMismatch = 6,
    EndpointAdmissionRejected = 7,
    EndpointEvidenceMismatch = 8,
    EndpointEvidenceExpired = 9,
    CatalogPathRejected = 10,
    TrustScopeRejected = 11
}

internal enum ProviderCatalogRuntimeAuthorizationAdmission
{
    Invalid = 0,
    Authorized = 1,
    Off = 2,
    Fault = 3,
    ConcurrentCapacityReached = 4,
    ProcessCapacityReached = 5,
    DuplicateRefresh = 6,
    BindingExpired = 7
}

internal enum ProviderCatalogRuntimeTicketAdmission
{
    Invalid = 0,
    Consumed = 1,
    Expired = 2,
    AlreadyConsumed = 3,
    BindingMismatch = 4
}

internal enum ProviderCatalogRuntimeExecutionAdmission
{
    Invalid = 0,
    NormalizedObservation = 1,
    HostReservationMissing = 2,
    TicketRejected = 3,
    CredentialMissing = 4,
    CredentialMismatch = 5,
    AuthenticationSchemeUnsupported = 6,
    CredentialRejected = 7,
    RedirectRejected = 8,
    AuthenticationRejected = 9,
    HttpStatusRejected = 10,
    UnsupportedContentEncoding = 11,
    ResourceLimitExceeded = 12,
    Timeout = 13,
    Cancelled = 14,
    NetworkFailure = 15
}

internal sealed class ProviderCatalogRuntimeBindingResult
{
    internal ProviderCatalogRuntimeBindingAdmission Admission { get; }
    internal ProviderCatalogRuntimeBinding? Binding { get; }
    internal bool Accepted =>
        Admission == ProviderCatalogRuntimeBindingAdmission.Accepted;

    private ProviderCatalogRuntimeBindingResult(
        ProviderCatalogRuntimeBindingAdmission admission,
        ProviderCatalogRuntimeBinding? binding)
    {
        if (!Enum.IsDefined(
                typeof(ProviderCatalogRuntimeBindingAdmission),
                admission)
            || admission == ProviderCatalogRuntimeBindingAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == ProviderCatalogRuntimeBindingAdmission.Accepted)
            != (binding != null))
        {
            throw new ArgumentException(
                "Only an accepted result may carry a binding.",
                nameof(binding));
        }
        Admission = admission;
        Binding = binding;
    }

    internal static ProviderCatalogRuntimeBindingResult Accept(
        ProviderCatalogRuntimeBinding binding)
        => new(
            ProviderCatalogRuntimeBindingAdmission.Accepted,
            binding ?? throw new ArgumentNullException(nameof(binding)));

    internal static ProviderCatalogRuntimeBindingResult Reject(
        ProviderCatalogRuntimeBindingAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderCatalogRuntimeBinding
{
    internal static TimeSpan MaximumFreshnessLifetime { get; } =
        TimeSpan.FromMinutes(10);

    internal ProviderEndpointInstance Instance { get; }
    internal ProviderDiscoveryProbePlan Probe { get; }
    internal ProviderDiscoveryRuntimeBudget Budget { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal string EndpointAdmissionFingerprint { get; }
    internal Uri CatalogUri { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset Deadline { get; }
    internal DateTimeOffset ObservationExpiresAt { get; }
    internal string BindingFingerprint { get; }
    internal bool RequiresCredential =>
        Instance.AuthenticationScheme != ProviderAuthenticationScheme.None;
    internal bool IsReadOnly => true;
    internal int MaximumPages => 1;
    internal int MaximumConcurrentConnections => 1;
    internal bool AllowsRedirect => false;
    internal bool AllowsRetry => false;
    internal bool AllowsFallback => false;
    internal bool AllowsGeneration => false;
    internal bool AllowsModelDownload => false;
    internal bool AllowsModelLoad => false;
    internal bool AllowsModelStart => false;

    private ProviderCatalogRuntimeBinding(
        ProviderRegistrySnapshot registry,
        ProviderEndpointInstance instance,
        ProviderDiscoveryProbePlan probe,
        ProviderEndpointAdmissionResult endpointAdmission,
        ProviderDiscoveryRuntimeBudget budget,
        Uri catalogUri,
        DateTimeOffset requestedAt,
        TimeSpan freshnessLifetime)
    {
        Instance = instance;
        Probe = probe;
        Budget = budget;
        RegistryGeneration = registry.RegistryGeneration;
        RegistryFingerprint = registry.Fingerprint;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        EndpointAdmissionFingerprint = endpointAdmission.Evidence!.Fingerprint;
        CatalogUri = catalogUri;
        RequestedAt = requestedAt;
        Deadline = requestedAt + budget.Timeout;
        ObservationExpiresAt = requestedAt + freshnessLifetime;
        BindingFingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/PROVIDER-CATALOG-RUNTIME-BINDING/1",
            ProviderRegistryCanonicalHash.Number(RegistryGeneration),
            RegistryFingerprint,
            instance.InstanceId.Value,
            ProviderRegistryCanonicalHash.Number(ConfigurationGeneration),
            instance.Fingerprint,
            probe.Fingerprint,
            EndpointAdmissionFingerprint,
            CatalogUri.AbsoluteUri,
            ProviderRegistryCanonicalHash.Number(budget.MaximumWireBytes),
            ProviderRegistryCanonicalHash.Number(
                budget.MaximumDecompressedBytes),
            ProviderRegistryCanonicalHash.Number(budget.MaximumJsonDepth),
            ProviderRegistryCanonicalHash.Number(budget.MaximumModelCount),
            ProviderRegistryCanonicalHash.Number(MaximumPages),
            ProviderRegistryCanonicalHash.Number(
                MaximumConcurrentConnections),
            ProviderRegistryCanonicalHash.Number(budget.MaximumStringLength),
            budget.Timeout.Ticks.ToString(CultureInfo.InvariantCulture),
            ProviderRegistryCanonicalHash.Timestamp(RequestedAt),
            ProviderRegistryCanonicalHash.Timestamp(Deadline),
            ProviderRegistryCanonicalHash.Timestamp(ObservationExpiresAt));
    }

    internal static ProviderCatalogRuntimeBindingResult TryCreate(
        ProviderRegistrySnapshot registry,
        ProviderEndpointInstanceId instanceId,
        ProviderDiscoveryProbePlan probe,
        ProviderEndpointAdmissionResult endpointAdmission,
        ProviderDiscoveryRuntimeBudget budget,
        DateTimeOffset requestedAt,
        TimeSpan? freshnessLifetime = null)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (instanceId == null)
            throw new ArgumentNullException(nameof(instanceId));
        if (probe == null)
            throw new ArgumentNullException(nameof(probe));
        if (endpointAdmission == null)
            throw new ArgumentNullException(nameof(endpointAdmission));
        if (budget == null)
            throw new ArgumentNullException(nameof(budget));
        requestedAt = DecisionCoreContractGuard.Timestamp(
            requestedAt,
            nameof(requestedAt));
        var freshness = freshnessLifetime ?? TimeSpan.FromMinutes(5);
        if (freshness <= TimeSpan.Zero
            || freshness > MaximumFreshnessLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(freshnessLifetime));
        }
        if (!registry.TryGetInstance(instanceId, out var instance)
            || instance == null)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.InstanceUnavailable);
        }
        if (!instance.Enabled)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.InstanceDisabled);
        }
        if (!probe.ClaimedProvider.HasValue
            || probe.ClaimedProvider.Value != instance.Kind)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.ProviderIdentityMismatch);
        }
        if (probe.IdentityAdmission
            == ProviderIdentityAdmission
                .InsufficientGenericCompatibilityEvidence)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission
                    .IdentityEvidenceInsufficient);
        }
        if (!string.Equals(
                probe.Endpoint.Fingerprint,
                instance.BaseUri.Fingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.EndpointMismatch);
        }
        if (!endpointAdmission.Accepted
            || endpointAdmission.Evidence == null)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission
                    .EndpointAdmissionRejected);
        }
        var evidence = endpointAdmission.Evidence;
        if (!evidence.Resolution.InstanceId.Equals(instance.InstanceId)
            || evidence.Resolution.ConfigurationGeneration
                != instance.ConfigurationGeneration
            || !string.Equals(
                evidence.Resolution.EndpointFingerprint,
                instance.EndpointFingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission
                    .EndpointEvidenceMismatch);
        }
        if (requestedAt < evidence.Connection.ConnectedAt
            || requestedAt >= evidence.Resolution.ValidUntil)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission
                    .EndpointEvidenceExpired);
        }
        if (ProviderDiscoverySameOriginPolicy.Validate(
                instance.BaseUri,
                probe.RelativeCatalogPath,
                out var catalogUri)
            != ProviderDiscoveryAdmission.PlannedForObserve
            || catalogUri == null)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.CatalogPathRejected);
        }
        var official =
            instance.TrustClass == ProviderTrustClass.OfficialCloudHttps
            && instance.ConnectionRouteLocality
                == ConnectionRouteLocality.PublicRemote
            && probe.Kind
                == ProviderDiscoveryProbeKind.OfficialConfiguredEndpoint;
        var loopback =
            instance.TrustClass == ProviderTrustClass.LoopbackRouteAdmitted
            && instance.ConnectionRouteLocality
                == ConnectionRouteLocality.Loopback
            && probe.Kind
                != ProviderDiscoveryProbeKind.OfficialConfiguredEndpoint;
        if (!official && !loopback)
        {
            return Reject(
                ProviderCatalogRuntimeBindingAdmission.TrustScopeRejected);
        }
        return ProviderCatalogRuntimeBindingResult.Accept(
            new ProviderCatalogRuntimeBinding(
                registry,
                instance,
                probe,
                endpointAdmission,
                budget,
                catalogUri,
                requestedAt,
                freshness));
    }

    private static ProviderCatalogRuntimeBindingResult Reject(
        ProviderCatalogRuntimeBindingAdmission admission)
        => ProviderCatalogRuntimeBindingResult.Reject(admission);
}

internal sealed class ProviderCatalogRuntimeTicket
{
    internal static TimeSpan MaximumLifetime { get; } =
        TimeSpan.FromSeconds(1);

    private int _consumed;

    internal string HostId { get; }
    internal string TicketId { get; }
    internal string BindingFingerprint { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal long RegistryGeneration { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal bool IsConsumed => Volatile.Read(ref _consumed) != 0;

    internal ProviderCatalogRuntimeTicket(
        string hostId,
        ProviderCatalogRuntimeBinding binding,
        DateTimeOffset issuedAt)
    {
        HostId = ProviderRegistryContractGuard.Identifier(
            hostId,
            nameof(hostId));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        BindingFingerprint = binding.BindingFingerprint;
        InstanceId = binding.Instance.InstanceId;
        RegistryGeneration = binding.RegistryGeneration;
        ConfigurationGeneration = binding.ConfigurationGeneration;
        IssuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        var ticketDeadline = IssuedAt + MaximumLifetime;
        ExpiresAt = ticketDeadline <= binding.Deadline
            ? ticketDeadline
            : binding.Deadline;
        TicketId = "d031-catalog-b-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/PROVIDER-CATALOG-RUNTIME-TICKET/1",
                HostId,
                BindingFingerprint,
                InstanceId.Value,
                ProviderRegistryCanonicalHash.Number(RegistryGeneration),
                ProviderRegistryCanonicalHash.Number(
                    ConfigurationGeneration),
                ProviderRegistryCanonicalHash.Timestamp(IssuedAt),
                ProviderRegistryCanonicalHash.Timestamp(ExpiresAt))
                .Substring(0, 32);
    }

    internal ProviderCatalogRuntimeTicketAdmission TryConsume(
        ProviderCatalogRuntimeBinding binding,
        DateTimeOffset now)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        if (now < IssuedAt || now >= ExpiresAt)
            return ProviderCatalogRuntimeTicketAdmission.Expired;
        if (!string.Equals(
                BindingFingerprint,
                binding.BindingFingerprint,
                StringComparison.Ordinal)
            || !InstanceId.Equals(binding.Instance.InstanceId)
            || RegistryGeneration != binding.RegistryGeneration
            || ConfigurationGeneration != binding.ConfigurationGeneration)
        {
            return ProviderCatalogRuntimeTicketAdmission.BindingMismatch;
        }
        return Interlocked.Exchange(ref _consumed, 1) == 0
            ? ProviderCatalogRuntimeTicketAdmission.Consumed
            : ProviderCatalogRuntimeTicketAdmission.AlreadyConsumed;
    }
}

internal sealed class ProviderCatalogRuntimeAuthorizationResult
{
    internal ProviderCatalogRuntimeAuthorizationAdmission Admission { get; }
    internal ProviderCatalogRuntimeTicket? Ticket { get; }
    internal bool Accepted =>
        Admission == ProviderCatalogRuntimeAuthorizationAdmission.Authorized;

    private ProviderCatalogRuntimeAuthorizationResult(
        ProviderCatalogRuntimeAuthorizationAdmission admission,
        ProviderCatalogRuntimeTicket? ticket)
    {
        if (!Enum.IsDefined(
                typeof(ProviderCatalogRuntimeAuthorizationAdmission),
                admission)
            || admission == ProviderCatalogRuntimeAuthorizationAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderCatalogRuntimeAuthorizationAdmission.Authorized)
            != (ticket != null))
        {
            throw new ArgumentException(
                "Only an accepted authorization carries a ticket.");
        }
        Admission = admission;
        Ticket = ticket;
    }

    internal static ProviderCatalogRuntimeAuthorizationResult Accept(
        ProviderCatalogRuntimeTicket ticket)
        => new(
            ProviderCatalogRuntimeAuthorizationAdmission.Authorized,
            ticket ?? throw new ArgumentNullException(nameof(ticket)));

    internal static ProviderCatalogRuntimeAuthorizationResult Reject(
        ProviderCatalogRuntimeAuthorizationAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderCatalogRuntimeHost
{
    internal const int MaximumConcurrentRefreshes = 1;
    internal const int MaximumRefreshesPerProcess = 4;
    internal const int AutomaticRetryCount = 0;

    private readonly object _gate = new();
    private readonly string _hostId;
    private readonly HashSet<string> _seenBindings =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _active =
        new(StringComparer.Ordinal);
    private ProviderCatalogRuntimeMode _mode = ProviderCatalogRuntimeMode.Off;
    private int _refreshesAuthorized;

    internal ProviderCatalogRuntimeHost(string? hostId = null)
    {
        _hostId = ProviderRegistryContractGuard.Identifier(
            hostId ?? "provider-catalog-" + Guid.NewGuid().ToString("N"),
            nameof(hostId));
    }

    internal ProviderCatalogRuntimeMode Mode
    {
        get
        {
            lock (_gate)
                return _mode;
        }
    }
    internal int Active
    {
        get
        {
            lock (_gate)
                return _active.Count;
        }
    }
    internal int RefreshesAuthorized
    {
        get
        {
            lock (_gate)
                return _refreshesAuthorized;
        }
    }

    internal void ArmForProcess()
    {
        lock (_gate)
        {
            if (_mode == ProviderCatalogRuntimeMode.Off)
                _mode = ProviderCatalogRuntimeMode.Armed;
        }
    }

    internal void Disable()
    {
        lock (_gate)
        {
            _mode = ProviderCatalogRuntimeMode.Off;
            foreach (var cancellation in _active.Values)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
            _active.Clear();
        }
    }

    internal ProviderCatalogRuntimeAuthorizationResult TryAuthorize(
        ProviderCatalogRuntimeBinding binding,
        DateTimeOffset now)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_mode == ProviderCatalogRuntimeMode.Fault)
            {
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission.Fault);
            }
            if (_mode != ProviderCatalogRuntimeMode.Armed)
            {
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission.Off);
            }
            if (now < binding.RequestedAt || now >= binding.Deadline)
            {
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission.BindingExpired);
            }
            if (_active.Count >= MaximumConcurrentRefreshes)
            {
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission
                        .ConcurrentCapacityReached);
            }
            if (_refreshesAuthorized >= MaximumRefreshesPerProcess)
            {
                _mode = ProviderCatalogRuntimeMode.Capacity;
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission
                        .ProcessCapacityReached);
            }
            if (!_seenBindings.Add(binding.BindingFingerprint))
            {
                return Reject(
                    ProviderCatalogRuntimeAuthorizationAdmission
                        .DuplicateRefresh);
            }
            var ticket = new ProviderCatalogRuntimeTicket(
                _hostId,
                binding,
                now);
            _active.Add(ticket.TicketId, new CancellationTokenSource());
            _refreshesAuthorized++;
            return ProviderCatalogRuntimeAuthorizationResult.Accept(ticket);
        }
    }

    internal bool TryGetExecutionToken(
        ProviderCatalogRuntimeTicket ticket,
        out CancellationToken token)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        lock (_gate)
        {
            if (!string.Equals(ticket.HostId, _hostId, StringComparison.Ordinal)
                || !_active.TryGetValue(ticket.TicketId, out var source))
            {
                token = new CancellationToken(canceled: true);
                return false;
            }
            token = source.Token;
            return true;
        }
    }

    internal bool TryComplete(ProviderCatalogRuntimeTicket ticket)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        lock (_gate)
        {
            if (!string.Equals(ticket.HostId, _hostId, StringComparison.Ordinal)
                || !_active.Remove(ticket.TicketId, out var source))
            {
                return false;
            }
            source.Dispose();
            return true;
        }
    }

    internal bool TryCancel(ProviderCatalogRuntimeTicket ticket)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        lock (_gate)
        {
            if (!string.Equals(ticket.HostId, _hostId, StringComparison.Ordinal)
                || !_active.Remove(ticket.TicketId, out var source))
            {
                return false;
            }
            source.Cancel();
            source.Dispose();
            return true;
        }
    }

    private static ProviderCatalogRuntimeAuthorizationResult Reject(
        ProviderCatalogRuntimeAuthorizationAdmission admission)
        => ProviderCatalogRuntimeAuthorizationResult.Reject(admission);
}

internal sealed class ProviderCatalogRuntimeExecutionResult
{
    internal ProviderCatalogRuntimeExecutionAdmission Admission { get; }
    internal ProviderCatalogRuntimeTicketAdmission? TicketAdmission { get; }
    internal ProviderCatalogObservation? Observation { get; }
    internal int NetworkCalls { get; }
    internal int PagesRead { get; }
    internal bool AdditionalPageAvailable { get; }
    internal bool Accepted =>
        Admission
            == ProviderCatalogRuntimeExecutionAdmission.NormalizedObservation;
    internal int ProviderGenerationCalls => 0;
    internal int AutomaticRetries => 0;
    internal int ProviderFallbacks => 0;
    internal int ModelDownloads => 0;
    internal int ModelLoads => 0;
    internal int ModelStarts => 0;
    internal int PromptWrites => 0;
    internal int MemoryWrites => 0;
    internal int CharacterWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal string? RawPayload => null;

    internal ProviderCatalogRuntimeExecutionResult(
        ProviderCatalogRuntimeExecutionAdmission admission,
        ProviderCatalogRuntimeTicketAdmission? ticketAdmission,
        ProviderCatalogObservation? observation,
        int networkCalls,
        int pagesRead,
        bool additionalPageAvailable)
    {
        if (!Enum.IsDefined(
                typeof(ProviderCatalogRuntimeExecutionAdmission),
                admission)
            || admission == ProviderCatalogRuntimeExecutionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (networkCalls < 0 || networkCalls > 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        if (pagesRead < 0 || pagesRead > 1)
            throw new ArgumentOutOfRangeException(nameof(pagesRead));
        if ((admission
                == ProviderCatalogRuntimeExecutionAdmission
                    .NormalizedObservation)
            != (observation != null))
        {
            throw new ArgumentException(
                "Only normalized execution carries an observation.",
                nameof(observation));
        }
        Admission = admission;
        TicketAdmission = ticketAdmission;
        Observation = observation;
        NetworkCalls = networkCalls;
        PagesRead = pagesRead;
        AdditionalPageAvailable = additionalPageAvailable;
    }

    internal static ProviderCatalogRuntimeExecutionResult Reject(
        ProviderCatalogRuntimeExecutionAdmission admission,
        ProviderCatalogRuntimeTicketAdmission? ticketAdmission = null,
        int networkCalls = 0)
        => new(admission, ticketAdmission, null, networkCalls, 0, false);

    internal static ProviderCatalogRuntimeExecutionResult Accept(
        ProviderCatalogRuntimeTicketAdmission ticketAdmission,
        ProviderCatalogObservation observation,
        bool additionalPageAvailable)
        => new(
            ProviderCatalogRuntimeExecutionAdmission.NormalizedObservation,
            ticketAdmission,
            observation,
            1,
            1,
            additionalPageAvailable);
}

internal static class ProviderCatalogHttpClientFactory
{
    internal static HttpClientHandler CreateHandler()
        => new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            MaxConnectionsPerServer = 1,
            CheckCertificateRevocationList = true
        };

    internal static HttpClient CreateClient()
        => new(CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
}

internal sealed class ProviderCatalogRuntimeExecutor
{
    private readonly ProviderCatalogRuntimeHost _host;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;

    internal ProviderCatalogRuntimeExecutor(
        ProviderCatalogRuntimeHost host,
        HttpClient http,
        Func<DateTimeOffset>? clock = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<ProviderCatalogRuntimeExecutionResult> ExecuteOnceAsync(
        ProviderCatalogRuntimeTicket ticket,
        ProviderCatalogRuntimeBinding binding,
        CredentialLease? credentialLease,
        CancellationToken cancellationToken)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (!_host.TryGetExecutionToken(ticket, out var hostToken))
        {
            credentialLease?.Dispose();
            return ProviderCatalogRuntimeExecutionResult.Reject(
                ProviderCatalogRuntimeExecutionAdmission
                    .HostReservationMissing);
        }

        var ticketAdmission = ticket.TryConsume(binding, _clock());
        if (ticketAdmission != ProviderCatalogRuntimeTicketAdmission.Consumed)
        {
            credentialLease?.Dispose();
            _host.TryComplete(ticket);
            return ProviderCatalogRuntimeExecutionResult.Reject(
                ProviderCatalogRuntimeExecutionAdmission.TicketRejected,
                ticketAdmission);
        }

        using (credentialLease)
        using (var request = new HttpRequestMessage(
                   HttpMethod.Get,
                   binding.CatalogUri))
        using (var timeout = new CancellationTokenSource())
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   hostToken,
                   timeout.Token))
        {
            var remaining = binding.Deadline - _clock();
            if (remaining <= TimeSpan.Zero)
            {
                _host.TryComplete(ticket);
                return ProviderCatalogRuntimeExecutionResult.Reject(
                    ProviderCatalogRuntimeExecutionAdmission.Timeout,
                    ticketAdmission);
            }
            timeout.CancelAfter(
                remaining <= binding.Budget.Timeout
                    ? remaining
                    : binding.Budget.Timeout);

            var credentialAdmission = ApplyCredential(
                binding,
                credentialLease,
                request,
                _clock());
            if (credentialAdmission.HasValue)
            {
                _host.TryComplete(ticket);
                return ProviderCatalogRuntimeExecutionResult.Reject(
                    credentialAdmission.Value,
                    ticketAdmission);
            }
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var networkStarted = false;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                networkStarted = true;
                using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token)
                    .ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri != null
                    && !SameAbsoluteUri(
                        response.RequestMessage.RequestUri,
                        binding.CatalogUri))
                {
                    return RejectNetwork(
                        ProviderCatalogRuntimeExecutionAdmission
                            .RedirectRejected,
                        ticketAdmission);
                }
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    return RejectNetwork(
                        ProviderCatalogRuntimeExecutionAdmission
                            .RedirectRejected,
                        ticketAdmission);
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    var observation = AuthenticationRejected(
                        binding,
                        _clock());
                    return ProviderCatalogRuntimeExecutionResult.Accept(
                        ticketAdmission,
                        observation,
                        additionalPageAvailable: false);
                }
                if (!response.IsSuccessStatusCode)
                {
                    return RejectNetwork(
                        ProviderCatalogRuntimeExecutionAdmission
                            .HttpStatusRejected,
                        ticketAdmission);
                }
                if (response.Content.Headers.ContentLength.HasValue
                    && response.Content.Headers.ContentLength.Value
                        > binding.Budget.MaximumWireBytes)
                {
                    return RejectNetwork(
                        ProviderCatalogRuntimeExecutionAdmission
                            .ResourceLimitExceeded,
                        ticketAdmission);
                }

                byte[]? wire = null;
                byte[]? decompressed = null;
                try
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(linked.Token)
                        .ConfigureAwait(false);
                    wire = await ReadBoundedAsync(
                            stream,
                            binding.Budget.MaximumWireBytes,
                            linked.Token)
                        .ConfigureAwait(false);
                    var encoding = ContentEncoding(response);
                    if (encoding == null)
                    {
                        return RejectNetwork(
                            ProviderCatalogRuntimeExecutionAdmission
                                .UnsupportedContentEncoding,
                            ticketAdmission);
                    }
                    decompressed = await DecompressAsync(
                            wire,
                            encoding,
                            binding.Budget.MaximumDecompressedBytes,
                            linked.Token)
                        .ConfigureAwait(false);
                    var adapter = ProviderModelCatalogAdapterResolver.Resolve(
                        binding.Probe.AdapterKind);
                    var observedAt = _clock();
                    var observation = adapter.Parse(
                        wire,
                        decompressed,
                        1,
                        binding.Budget,
                        observedAt,
                        binding.ObservationExpiresAt);
                    observation = WithAuthentication(
                        observation,
                        binding.RequiresCredential);
                    var additional = HasAdditionalPage(
                        binding.Probe.AdapterKind,
                        decompressed,
                        binding.Budget.MaximumJsonDepth);
                    return ProviderCatalogRuntimeExecutionResult.Accept(
                        ticketAdmission,
                        observation,
                        additional);
                }
                catch (ProviderCatalogResourceLimitException)
                {
                    return RejectNetwork(
                        ProviderCatalogRuntimeExecutionAdmission
                            .ResourceLimitExceeded,
                        ticketAdmission);
                }
                finally
                {
                    if (decompressed != null
                        && !ReferenceEquals(decompressed, wire))
                    {
                        Array.Clear(decompressed, 0, decompressed.Length);
                    }
                    if (wire != null)
                        Array.Clear(wire, 0, wire.Length);
                }
            }
            catch (OperationCanceledException)
            {
                return ProviderCatalogRuntimeExecutionResult.Reject(
                    cancellationToken.IsCancellationRequested
                        || hostToken.IsCancellationRequested
                        ? ProviderCatalogRuntimeExecutionAdmission.Cancelled
                        : ProviderCatalogRuntimeExecutionAdmission.Timeout,
                    ticketAdmission,
                    networkStarted ? 1 : 0);
            }
            catch (HttpRequestException)
            {
                return ProviderCatalogRuntimeExecutionResult.Reject(
                    ProviderCatalogRuntimeExecutionAdmission.NetworkFailure,
                    ticketAdmission,
                    networkStarted ? 1 : 0);
            }
            finally
            {
                _host.TryComplete(ticket);
            }
        }
    }

    private static ProviderCatalogRuntimeExecutionAdmission? ApplyCredential(
        ProviderCatalogRuntimeBinding binding,
        CredentialLease? lease,
        HttpRequestMessage request,
        DateTimeOffset now)
    {
        if (!binding.RequiresCredential)
        {
            return lease == null
                ? null
                : ProviderCatalogRuntimeExecutionAdmission
                    .CredentialMismatch;
        }
        if (lease == null)
        {
            return ProviderCatalogRuntimeExecutionAdmission
                .CredentialMissing;
        }
        var instance = binding.Instance;
        if (instance.CredentialReference == null
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
            return ProviderCatalogRuntimeExecutionAdmission
                .CredentialMismatch;
        }
        if (instance.AuthenticationScheme
            == ProviderAuthenticationScheme.CustomHeaderReference)
        {
            return ProviderCatalogRuntimeExecutionAdmission
                .AuthenticationSchemeUnsupported;
        }
        var applied = lease.TryConsumeOnce(
            now,
            secret =>
            {
                var value = new string(secret);
                switch (instance.AuthenticationScheme)
                {
                    case ProviderAuthenticationScheme.GoogleApiKey:
                        return request.Headers.TryAddWithoutValidation(
                            "x-goog-api-key",
                            value);
                    case ProviderAuthenticationScheme.Bearer:
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", value);
                        return true;
                    case ProviderAuthenticationScheme.AnthropicApiKey:
                        return request.Headers.TryAddWithoutValidation(
                                "x-api-key",
                                value)
                            && request.Headers.TryAddWithoutValidation(
                                "anthropic-version",
                                "2023-06-01");
                    default:
                        return false;
                }
            });
        return applied
            ? null
            : ProviderCatalogRuntimeExecutionAdmission.CredentialRejected;
    }

    private static string? ContentEncoding(HttpResponseMessage response)
    {
        var encodings = response.Content.Headers.ContentEncoding
            .Where(value => !string.Equals(
                value,
                "identity",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (encodings.Length == 0)
            return string.Empty;
        if (encodings.Length != 1)
            return null;
        return encodings[0].Equals("gzip", StringComparison.OrdinalIgnoreCase)
                || encodings[0].Equals(
                    "deflate",
                    StringComparison.OrdinalIgnoreCase)
            ? encodings[0].ToLowerInvariant()
            : null;
    }

    private static async Task<byte[]> DecompressAsync(
        byte[] wire,
        string encoding,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (encoding.Length == 0)
        {
            if (wire.Length > maximumBytes)
                throw new ProviderCatalogResourceLimitException();
            return wire;
        }
        await using var input = new MemoryStream(wire, writable: false);
        await using Stream decompressor = encoding == "gzip"
            ? new GZipStream(input, CompressionMode.Decompress, leaveOpen: false)
            : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);
        return await ReadBoundedAsync(
                decompressor,
                maximumBytes,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(
            Math.Min(maximumBytes, 16 * 1024));
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
                    throw new ProviderCatalogResourceLimitException();
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
        }
    }

    private static ProviderCatalogObservation WithAuthentication(
        ProviderCatalogObservation observation,
        bool authenticated)
    {
        if (!authenticated)
            return observation;
        return new ProviderCatalogObservation(
            observation.Admission,
            observation.Reachability,
            ProviderAuthenticationEvidence.Authenticated,
            observation.CatalogAvailability,
            observation.Models,
            observation.ObservedAt,
            observation.ExpiresAt);
    }

    private static ProviderCatalogObservation AuthenticationRejected(
        ProviderCatalogRuntimeBinding binding,
        DateTimeOffset observedAt)
        => new(
            ProviderDiscoveryAdmission.AuthenticationRequired,
            ProviderReachabilityEvidence.Reachable,
            binding.RequiresCredential
                ? ProviderAuthenticationEvidence.Rejected
                : ProviderAuthenticationEvidence.AuthenticationRequired,
            ProviderCatalogAvailability.AuthenticationRequired,
            null,
            observedAt,
            binding.ObservationExpiresAt);

    private static bool HasAdditionalPage(
        ProviderCatalogAdapterKind adapterKind,
        byte[] payload,
        int maximumDepth)
    {
        try
        {
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions { MaxDepth = maximumDepth });
            var root = document.RootElement;
            if (adapterKind == ProviderCatalogAdapterKind.GeminiModels
                && root.TryGetProperty("nextPageToken", out var token)
                && token.ValueKind == JsonValueKind.String)
            {
                return !string.IsNullOrWhiteSpace(token.GetString());
            }
            return adapterKind == ProviderCatalogAdapterKind.OpenAiModels
                && root.TryGetProperty("has_more", out var hasMore)
                && hasMore.ValueKind is JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SameAbsoluteUri(Uri first, Uri second)
        => string.Equals(
            first.AbsoluteUri,
            second.AbsoluteUri,
            StringComparison.Ordinal);

    private static ProviderCatalogRuntimeExecutionResult RejectNetwork(
        ProviderCatalogRuntimeExecutionAdmission admission,
        ProviderCatalogRuntimeTicketAdmission ticketAdmission)
        => ProviderCatalogRuntimeExecutionResult.Reject(
            admission,
            ticketAdmission,
            networkCalls: 1);
}

internal sealed class ProviderCatalogResourceLimitException : Exception
{
}
