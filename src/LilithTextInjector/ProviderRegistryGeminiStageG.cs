using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum GeminiRegistryStageGMode
{
    Invalid = 0,
    Off = 1,
    Armed = 2,
    Fault = 3,
    Capacity = 4
}

internal enum GeminiRegistryStageGAdmission
{
    Invalid = 0,
    Authorized = 1,
    Off = 2,
    NotGemini = 3,
    ProtocolMismatch = 4,
    RouteIdentityMismatch = 5,
    RequestExpired = 6,
    ConcurrentCapacityReached = 7,
    ProcessCapacityReached = 8,
    DuplicateCorrelation = 9,
    TicketExpired = 10,
    TicketConsumed = 11
}

internal sealed class GeminiRegistryStageGAuthorizationTicket
{
    internal static TimeSpan MaximumLifetime { get; } =
        TimeSpan.FromSeconds(1);

    private int _consumed;

    internal string HostId { get; }
    internal string TicketId { get; }
    internal string CorrelationId { get; }
    internal string ExecutionFingerprint { get; }
    internal string RouteFingerprint { get; }
    internal string RegistryFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal bool AllowsRetry => false;
    internal bool AllowsFallback => false;
    internal bool IsConsumed =>
        Volatile.Read(ref _consumed) != 0;

    internal GeminiRegistryStageGAuthorizationTicket(
        string hostId,
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        DateTimeOffset issuedAt)
    {
        HostId = LivingContinuityContractGuard.Identifier(
            hostId,
            nameof(hostId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        CorrelationId = request.CorrelationId;
        ExecutionFingerprint = request.ExecutionFingerprint;
        RouteFingerprint = route.RouteFingerprint;
        RegistryFingerprint = registry.Fingerprint;
        RegistryGeneration = registry.RegistryGeneration;
        InstanceId = route.InstanceId;
        ModelInstanceId = route.ModelInstanceId;
        IssuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = IssuedAt + MaximumLifetime;
        TicketId = "d027-gemini-g-"
            + ProviderRegistryCanonicalHash.Of(
                "D027/GEMINI-STAGE-G-TICKET/1",
                HostId,
                CorrelationId,
                ExecutionFingerprint,
                RouteFingerprint,
                RegistryFingerprint,
                ProviderRegistryCanonicalHash.Timestamp(IssuedAt))
                .Substring(0, 32);
    }

    internal GeminiRegistryStageGAdmission TryConsume(
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        if (now < IssuedAt || now >= ExpiresAt)
            return GeminiRegistryStageGAdmission.TicketExpired;
        if (!string.Equals(
                CorrelationId,
                request.CorrelationId,
                StringComparison.Ordinal)
            || !string.Equals(
                ExecutionFingerprint,
                request.ExecutionFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                RouteFingerprint,
                route.RouteFingerprint,
                StringComparison.Ordinal)
            || RegistryGeneration != registry.RegistryGeneration
            || !string.Equals(
                RegistryFingerprint,
                registry.Fingerprint,
                StringComparison.Ordinal)
            || !InstanceId.Equals(route.InstanceId)
            || !ModelInstanceId.Equals(route.ModelInstanceId))
        {
            return GeminiRegistryStageGAdmission
                .RouteIdentityMismatch;
        }
        return Interlocked.Exchange(ref _consumed, 1) == 0
            ? GeminiRegistryStageGAdmission.Authorized
            : GeminiRegistryStageGAdmission.TicketConsumed;
    }
}

internal sealed class GeminiRegistryStageGAuthorizationResult
{
    internal GeminiRegistryStageGAdmission Admission { get; }
    internal GeminiRegistryStageGAuthorizationTicket? Ticket { get; }
    internal bool Accepted =>
        Admission == GeminiRegistryStageGAdmission.Authorized;

    private GeminiRegistryStageGAuthorizationResult(
        GeminiRegistryStageGAdmission admission,
        GeminiRegistryStageGAuthorizationTicket? ticket)
    {
        if (!Enum.IsDefined(
                typeof(GeminiRegistryStageGAdmission),
                admission)
            || admission == GeminiRegistryStageGAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == GeminiRegistryStageGAdmission.Authorized)
            != (ticket != null))
        {
            throw new ArgumentException(
                "Only accepted authorization carries a ticket.");
        }
        Admission = admission;
        Ticket = ticket;
    }

    internal static GeminiRegistryStageGAuthorizationResult Accept(
        GeminiRegistryStageGAuthorizationTicket ticket)
        => new(
            GeminiRegistryStageGAdmission.Authorized,
            ticket ?? throw new ArgumentNullException(nameof(ticket)));

    internal static GeminiRegistryStageGAuthorizationResult Reject(
        GeminiRegistryStageGAdmission admission)
        => new(admission, null);
}

internal sealed class GeminiRegistryStageGHost
{
    internal const int MaximumProviderCallsPerProcess = 4;
    internal const int MaximumConcurrentProviderCalls = 1;
    internal const int AutomaticRetryCount = 0;

    private readonly object _gate = new();
    private readonly HashSet<string>
        _correlations = new(StringComparer.Ordinal);
    private readonly HashSet<string>
        _activeTickets = new(StringComparer.Ordinal);
    private readonly string _hostId;
    private GeminiRegistryStageGMode _mode =
        GeminiRegistryStageGMode.Off;
    private int _callsAuthorized;
    private int _active;

    internal GeminiRegistryStageGHost(string? hostId = null)
    {
        _hostId = LivingContinuityContractGuard.Identifier(
            hostId ?? "gemini-stage-g-" + Guid.NewGuid().ToString("N"),
            nameof(hostId));
    }

    internal GeminiRegistryStageGMode Mode
    {
        get
        {
            lock (_gate)
                return _mode;
        }
    }
    internal int CallsAuthorized
    {
        get
        {
            lock (_gate)
                return _callsAuthorized;
        }
    }
    internal int Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    internal void ArmForProcess()
    {
        lock (_gate)
        {
            if (_mode == GeminiRegistryStageGMode.Off)
                _mode = GeminiRegistryStageGMode.Armed;
        }
    }

    internal void Disable()
    {
        lock (_gate)
        {
            _mode = GeminiRegistryStageGMode.Off;
        }
    }

    internal GeminiRegistryStageGAuthorizationResult TryAuthorize(
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_mode != GeminiRegistryStageGMode.Armed)
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission.Off);
            }
            if (request.Route != PreparedProviderRoute.Gemini
                || route.Kind != ProviderKind.Gemini)
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission.NotGemini);
            }
            if (request.Protocol
                    != PreparedProviderProtocol.GeminiGenerateContent
                || route.Protocol
                    != ProviderProtocolKind.GeminiGenerateContent)
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission.ProtocolMismatch);
            }
            if (route.RegistryGeneration != registry.RegistryGeneration
                || !string.Equals(
                    route.RegistryFingerprint,
                    registry.Fingerprint,
                    StringComparison.Ordinal)
                || now < route.SelectedAt
                || now >= route.ExpiresAt
                || now < request.RequestedAt
                || now >= request.Deadline)
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission
                        .RouteIdentityMismatch);
            }
            if (_active >= MaximumConcurrentProviderCalls)
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission
                        .ConcurrentCapacityReached);
            }
            if (_callsAuthorized >= MaximumProviderCallsPerProcess)
            {
                _mode = GeminiRegistryStageGMode.Capacity;
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission
                        .ProcessCapacityReached);
            }
            if (!_correlations.Add(request.CorrelationId))
            {
                return GeminiRegistryStageGAuthorizationResult.Reject(
                    GeminiRegistryStageGAdmission
                        .DuplicateCorrelation);
            }

            var ticket = new GeminiRegistryStageGAuthorizationTicket(
                    _hostId,
                    request,
                    registry,
                    route,
                    now);
            if (!_activeTickets.Add(ticket.TicketId))
            {
                throw new InvalidOperationException(
                    "Stage G ticket identity collided.");
            }
            _callsAuthorized++;
            _active++;
            return GeminiRegistryStageGAuthorizationResult.Accept(
                ticket);
        }
    }

    internal bool Owns(
        GeminiRegistryStageGAuthorizationTicket ticket)
        => ticket != null
            && string.Equals(
                ticket.HostId,
                _hostId,
                StringComparison.Ordinal);

    internal bool TryComplete(
        GeminiRegistryStageGAuthorizationTicket ticket)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        lock (_gate)
        {
            if (!Owns(ticket)
                || !_activeTickets.Remove(ticket.TicketId))
            {
                return false;
            }
            if (_active <= 0)
            {
                throw new InvalidOperationException(
                    "Stage G active reservation count is inconsistent.");
            }
            _active--;
            return true;
        }
    }

    internal bool TryCancel(
        GeminiRegistryStageGAuthorizationTicket ticket)
        => TryComplete(ticket);
}

internal sealed class GeminiRegistryStageGExecutor
{
    private static readonly Uri GeminiBaseUri =
        new(
            "https://generativelanguage.googleapis.com/",
            UriKind.Absolute);

    private readonly GeminiRegistryStageGHost _host;
    private readonly System.Net.Http.HttpClient _http;
    private readonly IProviderCredentialResolver _credentials;
    private readonly Func<string> _modelTransportResolver;
    private readonly Func<DateTimeOffset> _clock;

    internal GeminiRegistryStageGExecutor(
        GeminiRegistryStageGHost host,
        System.Net.Http.HttpClient http,
        IProviderCredentialResolver credentials,
        Func<string> modelTransportResolver,
        Func<DateTimeOffset>? clock = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentials = credentials
            ?? throw new ArgumentNullException(nameof(credentials));
        _modelTransportResolver = modelTransportResolver
            ?? throw new ArgumentNullException(
                nameof(modelTransportResolver));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<PreparedProviderNormalizedCompletion>
        ExecuteOnceAsync(
            GeminiRegistryStageGAuthorizationTicket ticket,
            PreparedProviderExecutionRequest request,
            ProviderRegistrySnapshot registry,
            ProviderRouteSelection route,
            CancellationToken cancellationToken)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (!_host.Owns(ticket))
        {
            throw new InvalidOperationException(
                "Gemini Stage G ticket belongs to another host.");
        }
        try
        {
            var now = _clock();
            var admission = ticket.TryConsume(
                request,
                registry,
                route,
                now);
            if (admission != GeminiRegistryStageGAdmission.Authorized)
            {
                throw new InvalidOperationException(
                    "Gemini Stage G ticket rejected: "
                    + admission
                    + ".");
            }
            if (!registry.TryGetInstance(
                    route.InstanceId,
                    out var instance)
                || instance == null
                || !registry.TryGetModel(
                    route.InstanceId,
                    route.ModelInstanceId,
                    out var model)
                || model == null)
            {
                throw new InvalidOperationException(
                    "Gemini Stage G route identity is unavailable.");
            }
            var leaseResult = _credentials.Resolve(
                new ProviderCredentialResolutionRequest(
                    instance,
                    now,
                    Min(
                        now + CredentialLease.MaximumLifetime,
                        request.Deadline,
                        route.ExpiresAt)),
                instance);
            using var lease = leaseResult.Lease
                ?? throw new InvalidOperationException(
                    "Gemini Stage G credential lease rejected: "
                    + leaseResult.Admission
                    + ".");
            var modelId = LivingContinuityContractGuard.Identifier(
                _modelTransportResolver(),
                "modelTransportValue");
            var expectedModelCorrelation =
                ProviderRegistryCanonicalHash.Of(
                    "D027/LEGACY-MODEL-CORRELATION/1",
                    "gemini",
                    modelId);
            if (!string.Equals(
                    expectedModelCorrelation,
                    model.CorrelationHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Gemini Stage G model transport correlation changed.");
            }
            var binding =
                PreparedProviderRuntimeBinding
                    .GeminiWithCredentialLease(
                        GeminiBaseUri,
                        modelId,
                        lease);
            var executor = new PreparedProviderHttpExecutor(
                _http,
                new PreparedProviderSingleBindingResolver(binding),
                lifecycle: null,
                _clock);
            var completion = await executor.ExecuteAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    completion.CorrelationId,
                    ticket.CorrelationId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    completion.ProviderRouteId,
                    "Gemini",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Gemini Stage G completion identity changed.");
            }
            return completion;
        }
        finally
        {
            _host.TryComplete(ticket);
        }
    }

    private static DateTimeOffset Min(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third)
        => first <= second && first <= third
            ? first
            : second <= third
                ? second
                : third;
}

internal sealed class GeminiRegistryStageGAuthorityCounters
{
    internal int AutomaticRetries => 0;
    internal int CrossProviderFallbacks => 0;
    internal int ToolCalls => 0;
    internal int MemoryWrites => 0;
    internal int CharacterWrites => 0;
    internal int RelationshipWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
}
