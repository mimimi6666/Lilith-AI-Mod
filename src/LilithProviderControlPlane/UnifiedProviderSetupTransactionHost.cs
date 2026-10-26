using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LilithProviderControlPlane;

internal enum UnifiedProviderSetupAdmission
{
    Invalid = 0,
    Accepted = 1,
    Disabled = 2,
    DuplicateTransaction = 3,
    TransactionCapacityReached = 4,
    ConcurrentTransactionRejected = 5,
    Cancelled = 6,
    TimedOut = 7,
    ProviderIdentityRejected = 8,
    EndpointRejected = 9,
    ConnectedAddressEvidenceRequired = 10,
    CredentialRejected = 11,
    CatalogRejected = 12,
    CatalogIdentityRejected = 13,
    ModelRejected = 14,
    StaleConfiguration = 15,
    StoreRejected = 16,
    Faulted = 17
}

internal enum UnifiedProviderSetupCatalogAdmission
{
    Invalid = 0,
    None = 1,
    ProviderCatalogAccepted = 2,
    ManualSelectionAccepted = 3,
    Rejected = 4
}

internal enum UnifiedProviderSetupEndpointDecision
{
    Invalid = 0,
    Accepted = 1,
    Rejected = 2,
    ConnectedAddressEvidenceRequired = 3
}

internal enum UnifiedProviderSetupStoreReadAdmission
{
    Invalid = 0,
    Accepted = 1,
    Unavailable = 2
}

internal sealed class UnifiedProviderSetupEndpointResult
{
    internal UnifiedProviderSetupEndpointDecision Decision { get; }
    internal UnifiedProviderEndpointAdmissionReason Reason { get; }
    internal Uri? NormalizedEndpoint { get; }

    private UnifiedProviderSetupEndpointResult(
        UnifiedProviderSetupEndpointDecision decision,
        UnifiedProviderEndpointAdmissionReason reason,
        Uri? normalizedEndpoint)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderSetupEndpointDecision), decision)
            || decision == UnifiedProviderSetupEndpointDecision.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }
        if (!Enum.IsDefined(typeof(UnifiedProviderEndpointAdmissionReason), reason)
            || reason == UnifiedProviderEndpointAdmissionReason.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if ((decision == UnifiedProviderSetupEndpointDecision.Rejected)
            != (normalizedEndpoint == null))
        {
            throw new ArgumentException(
                "Only an admitted endpoint may be retained.",
                nameof(normalizedEndpoint));
        }
        Decision = decision;
        Reason = reason;
        NormalizedEndpoint = normalizedEndpoint;
    }

    internal static UnifiedProviderSetupEndpointResult Accept(Uri endpoint)
        => new(
            UnifiedProviderSetupEndpointDecision.Accepted,
            UnifiedProviderEndpointAdmissionReason.LoopbackAccepted,
            Normalize(endpoint));

    internal static UnifiedProviderSetupEndpointResult Accept(
        Uri endpoint,
        UnifiedProviderEndpointAdmissionReason reason)
        => new(
            UnifiedProviderSetupEndpointDecision.Accepted,
            reason,
            Normalize(endpoint));

    internal static UnifiedProviderSetupEndpointResult Reject(
        UnifiedProviderEndpointAdmissionReason reason)
        => new(
            UnifiedProviderSetupEndpointDecision.Rejected,
            reason,
            null);

    internal static UnifiedProviderSetupEndpointResult RequireConnectedAddressEvidence(
        Uri endpoint,
        UnifiedProviderEndpointAdmissionReason reason)
        => new(
            UnifiedProviderSetupEndpointDecision.ConnectedAddressEvidenceRequired,
            reason,
            Normalize(endpoint));

    private static Uri Normalize(Uri? endpoint)
        => UnifiedProviderConfiguredInstance.NormalizeBaseUri(
            endpoint,
            nameof(endpoint));
}

internal interface IUnifiedProviderSetupEndpointAdmission
{
    UnifiedProviderSetupEndpointResult Evaluate(
        ProviderControlKind provider,
        Uri baseUri,
        ConfiguredEndpointRouteIntent routeIntent,
        string endpointProfileId,
        string workspaceId);
}

/// <summary>
/// Pure endpoint admission for a setup transaction. Remote endpoints remain
/// non-runnable until a later transport supplies connected-address evidence.
/// </summary>
internal sealed class UnifiedProviderSetupPureEndpointAdmission :
    IUnifiedProviderSetupEndpointAdmission
{
    public UnifiedProviderSetupEndpointResult Evaluate(
        ProviderControlKind provider,
        Uri baseUri,
        ConfiguredEndpointRouteIntent routeIntent,
        string endpointProfileId,
        string workspaceId)
    {
        var result = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            provider,
            baseUri,
            routeIntent,
            endpointProfileId,
            workspaceId);
        if (result.Accepted)
            return UnifiedProviderSetupEndpointResult.Accept(result.NormalizedEndpoint!);
        if (result.RequiresSendTimeConnectedAddressEvidence)
        {
            return UnifiedProviderSetupEndpointResult.RequireConnectedAddressEvidence(
                result.NormalizedEndpoint!,
                result.Reason);
        }
        return UnifiedProviderSetupEndpointResult.Reject(result.Reason);
    }
}

/// <summary>
/// Setup admission used only with <see cref="UnifiedProviderCatalogNetworkClient"/>.
/// Remote endpoints may reach that client because its connected-address transport
/// resolves once, pins the socket address, and validates the connected address at
/// send time. This admission must never be paired with an ordinary HTTP transport.
/// </summary>
internal sealed class UnifiedProviderSetupConnectedTransportEndpointAdmission :
    IUnifiedProviderSetupEndpointAdmission
{
    public UnifiedProviderSetupEndpointResult Evaluate(
        ProviderControlKind provider,
        Uri baseUri,
        ConfiguredEndpointRouteIntent routeIntent,
        string endpointProfileId,
        string workspaceId)
    {
        var result = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            provider,
            baseUri,
            routeIntent,
            endpointProfileId,
            workspaceId);
        if (result.Accepted)
            return UnifiedProviderSetupEndpointResult.Accept(result.NormalizedEndpoint!);
        if (result.RequiresSendTimeConnectedAddressEvidence)
        {
            return UnifiedProviderSetupEndpointResult.Accept(
                result.NormalizedEndpoint!,
                result.Reason);
        }
        return UnifiedProviderSetupEndpointResult.Reject(result.Reason);
    }
}

internal interface IUnifiedProviderSetupCatalogProbe
{
    Task<ProviderCatalogNetworkResult> DiscoverOnceAsync(
        ProviderCatalogNetworkTarget target,
        UnifiedProviderCredentialLease? credentialLease,
        CancellationToken cancellationToken);
}

internal sealed class UnifiedProviderSetupCatalogProbe :
    IUnifiedProviderSetupCatalogProbe
{
    private readonly UnifiedProviderCatalogNetworkClient _client;

    internal UnifiedProviderSetupCatalogProbe(
        UnifiedProviderCatalogNetworkClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<ProviderCatalogNetworkResult> DiscoverOnceAsync(
        ProviderCatalogNetworkTarget target,
        UnifiedProviderCredentialLease? credentialLease,
        CancellationToken cancellationToken)
        => _client.DiscoverConfiguredOnceAsync(
            target,
            credentialLease,
            cancellationToken);
}

internal sealed class UnifiedProviderSetupStoreReadResult
{
    internal UnifiedProviderSetupStoreReadAdmission Admission { get; }
    internal long StoreGeneration { get; }

    internal UnifiedProviderSetupStoreReadResult(
        UnifiedProviderSetupStoreReadAdmission admission,
        long storeGeneration)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderSetupStoreReadAdmission), admission)
            || admission == UnifiedProviderSetupStoreReadAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == UnifiedProviderSetupStoreReadAdmission.Accepted)
            != (storeGeneration > 0))
        {
            throw new ArgumentException(
                "Only an accepted read may carry a store generation.",
                nameof(storeGeneration));
        }
        Admission = admission;
        StoreGeneration = storeGeneration;
    }
}

/// <summary>
/// The store must compare expectedStoreGeneration and persist atomically.
/// Implementations take ownership of credentialLease for the duration of the
/// call and must consume or dispose it before returning.
/// </summary>
internal interface IUnifiedProviderSetupStore
{
    UnifiedProviderSetupStoreReadResult ReadCurrentGeneration();

    UnifiedProviderStoreResult ApplyValidated(
        ProviderControlProfile profile,
        string instanceId,
        Uri baseUri,
        string endpointProfileId,
        string workspaceId,
        ConfiguredEndpointRouteIntent routeIntent,
        ProviderCustomAuthenticationKind customAuthentication,
        UnifiedProviderCredentialLease credentialLease,
        UnifiedProviderCatalogCommit catalog,
        string selectedModel,
        long expectedStoreGeneration,
        DateTimeOffset now,
        bool assignTextRoute = true);
}

/// <summary>
/// Production source adapter for the protected current-user Provider store.
/// The underlying store owns the final credential lease and performs the
/// expected-generation comparison in the same mutation critical section as
/// persistence.
/// </summary>
internal sealed class UnifiedProviderControlPlaneSetupStore :
    IUnifiedProviderSetupStore
{
    private readonly UnifiedProviderControlPlaneStore _store;

    internal UnifiedProviderControlPlaneSetupStore(
        UnifiedProviderControlPlaneStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    internal static UnifiedProviderControlPlaneSetupStore ForCurrentUser()
        => new(UnifiedProviderControlPlaneStore.ForCurrentUser());

    public UnifiedProviderSetupStoreReadResult ReadCurrentGeneration()
    {
        var loaded = _store.Load();
        return loaded.Accepted && loaded.Snapshot != null
            ? new UnifiedProviderSetupStoreReadResult(
                UnifiedProviderSetupStoreReadAdmission.Accepted,
                loaded.Snapshot.StoreGeneration)
            : new UnifiedProviderSetupStoreReadResult(
                UnifiedProviderSetupStoreReadAdmission.Unavailable,
                0);
    }

    public UnifiedProviderStoreResult ApplyValidated(
        ProviderControlProfile profile,
        string instanceId,
        Uri baseUri,
        string endpointProfileId,
        string workspaceId,
        ConfiguredEndpointRouteIntent routeIntent,
        ProviderCustomAuthenticationKind customAuthentication,
        UnifiedProviderCredentialLease credentialLease,
        UnifiedProviderCatalogCommit catalog,
        string selectedModel,
        long expectedStoreGeneration,
        DateTimeOffset now,
        bool assignTextRoute = true)
        => _store.ApplyValidated(
            profile,
            instanceId,
            baseUri,
            endpointProfileId,
            workspaceId,
            routeIntent,
            customAuthentication,
            credentialLease,
            catalog,
            selectedModel,
            expectedStoreGeneration,
            now,
            assignTextRoute);
}

internal interface IUnifiedProviderSetupCredentialSource
{
    bool CredentialConfigured { get; }

    UnifiedProviderSetupCredentialMaterial Materialize();
}

/// <summary>
/// One materialization is retained in a bounded scope. The catalog probe gets
/// one disposable copy; the original is transferred exactly once to the
/// atomic store boundary. Every remaining buffer is cleared on disposal.
/// </summary>
internal sealed class UnifiedProviderSetupCredentialMaterial : IDisposable
{
    private char[]? _secret;
    private int _probeLeaseIssued;
    private int _storeLeaseIssued;

    internal bool CredentialConfigured => _secret?.Length > 0;

    internal UnifiedProviderSetupCredentialMaterial(char[] secret)
    {
        if (secret == null)
            throw new ArgumentNullException(nameof(secret));
        if (secret.Length > UnifiedProviderControlPlaneStore.MaximumCredentialCharacters)
            throw new ArgumentOutOfRangeException(nameof(secret));
        _secret = secret;
    }

    internal UnifiedProviderCredentialLease CreateProbeLease()
    {
        if (Interlocked.Exchange(ref _probeLeaseIssued, 1) != 0)
            throw new InvalidOperationException("The probe credential is single-use.");
        var secret = Volatile.Read(ref _secret)
            ?? throw new ObjectDisposedException(nameof(UnifiedProviderSetupCredentialMaterial));
        return new UnifiedProviderCredentialLease((char[])secret.Clone());
    }

    internal UnifiedProviderCredentialLease TransferStoreLease()
    {
        if (Interlocked.Exchange(ref _storeLeaseIssued, 1) != 0)
            throw new InvalidOperationException("The store credential is single-use.");
        var secret = Interlocked.Exchange(ref _secret, null)
            ?? throw new ObjectDisposedException(nameof(UnifiedProviderSetupCredentialMaterial));
        return new UnifiedProviderCredentialLease(secret);
    }

    public void Dispose()
    {
        var secret = Interlocked.Exchange(ref _secret, null);
        if (secret != null)
            Array.Clear(secret, 0, secret.Length);
    }
}

internal sealed class UnifiedProviderSetupRequest
{
    internal string TransactionId { get; }
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string InstanceId { get; }
    internal Uri BaseUri { get; }
    internal string EndpointProfileId { get; }
    internal string WorkspaceId { get; }
    internal ConfiguredEndpointRouteIntent RouteIntent { get; }
    internal ProviderCustomAuthenticationKind CustomAuthentication { get; }
    internal string DesiredModel { get; }
    internal long ExpectedStoreGeneration { get; }
    internal IUnifiedProviderSetupCredentialSource? CredentialSource { get; }

    internal UnifiedProviderSetupRequest(
        string transactionId,
        ProviderControlKind provider,
        string providerId,
        string instanceId,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        ProviderCustomAuthenticationKind customAuthentication,
        string? desiredModel,
        long expectedStoreGeneration,
        IUnifiedProviderSetupCredentialSource? credentialSource)
        : this(
            transactionId,
            provider,
            providerId,
            instanceId,
            baseUri,
            endpointProfileId,
            workspaceId,
            ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                provider,
                baseUri),
            customAuthentication,
            desiredModel,
            expectedStoreGeneration,
            credentialSource)
    {
    }

    internal UnifiedProviderSetupRequest(
        string transactionId,
        ProviderControlKind provider,
        string providerId,
        string instanceId,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        ConfiguredEndpointRouteIntent routeIntent,
        ProviderCustomAuthenticationKind customAuthentication,
        string? desiredModel,
        long expectedStoreGeneration,
        IUnifiedProviderSetupCredentialSource? credentialSource)
    {
        TransactionId = UnifiedProviderConfiguredInstance.Identifier(
            transactionId,
            96,
            nameof(transactionId));
        if (!Enum.IsDefined(typeof(ProviderControlKind), provider)
            || provider == ProviderControlKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        Provider = provider;
        ProviderId = UnifiedProviderConfiguredInstance.Identifier(
            providerId,
            64,
            nameof(providerId));
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
            ?? throw new ArgumentNullException(nameof(routeIntent));
        if (!Enum.IsDefined(typeof(ProviderCustomAuthenticationKind), customAuthentication)
            || customAuthentication == ProviderCustomAuthenticationKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(customAuthentication));
        }
        CustomAuthentication = customAuthentication;
        DesiredModel = string.IsNullOrWhiteSpace(desiredModel)
            ? string.Empty
            : UnifiedProviderCatalogCommit.ModelId(desiredModel, nameof(desiredModel));
        if (expectedStoreGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedStoreGeneration));
        ExpectedStoreGeneration = expectedStoreGeneration;
        CredentialSource = credentialSource;
    }
}

internal sealed class UnifiedProviderSetupOptions
{
    internal static UnifiedProviderSetupOptions DefaultOff { get; } = new(
        enabled: false,
        transactionLifetime: TimeSpan.FromSeconds(10));

    internal bool Enabled { get; }
    internal TimeSpan TransactionLifetime { get; }

    internal UnifiedProviderSetupOptions(
        bool enabled,
        TimeSpan transactionLifetime)
    {
        if (transactionLifetime <= TimeSpan.Zero
            || transactionLifetime > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(transactionLifetime));
        }
        Enabled = enabled;
        TransactionLifetime = transactionLifetime;
    }
}

internal sealed class UnifiedProviderSetupResult
{
    internal UnifiedProviderSetupAdmission Admission { get; }
    internal UnifiedProviderSetupCatalogAdmission CatalogAdmission { get; }
    internal ProviderControlKind Provider { get; }
    internal string ProviderId { get; }
    internal string InstanceId { get; }
    internal long ExpectedStoreGeneration { get; }
    internal UnifiedProviderEndpointAdmissionReason? EndpointReason { get; }
    internal ProviderCatalogNetworkAdmission? CatalogNetworkAdmission { get; }
    internal UnifiedProviderStoreAdmission? StoreAdmission { get; }
    internal string SelectedModelFingerprint { get; }
    internal int EndpointEvaluations { get; }
    internal int StoreReads { get; }
    internal int CredentialMaterializations { get; }
    internal int NetworkCalls { get; }
    internal int StoreWrites { get; }
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal bool RetainsRawCredential => false;
    internal bool RetainsRawCatalogBody => false;
    internal bool Accepted => Admission == UnifiedProviderSetupAdmission.Accepted;

    internal UnifiedProviderSetupResult(
        UnifiedProviderSetupAdmission admission,
        UnifiedProviderSetupCatalogAdmission catalogAdmission,
        UnifiedProviderSetupRequest request,
        UnifiedProviderEndpointAdmissionReason? endpointReason,
        ProviderCatalogNetworkAdmission? catalogNetworkAdmission,
        UnifiedProviderStoreAdmission? storeAdmission,
        string? selectedModelFingerprint,
        int endpointEvaluations,
        int storeReads,
        int credentialMaterializations,
        int networkCalls,
        int storeWrites)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderSetupAdmission), admission)
            || admission == UnifiedProviderSetupAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (!Enum.IsDefined(typeof(UnifiedProviderSetupCatalogAdmission), catalogAdmission)
            || catalogAdmission == UnifiedProviderSetupCatalogAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(catalogAdmission));
        }
        if (endpointEvaluations is < 0 or > 1
            || storeReads is < 0 or > 3
            || credentialMaterializations is < 0 or > 1
            || networkCalls is < 0 or > 1
            || storeWrites is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        }
        Admission = admission;
        CatalogAdmission = catalogAdmission;
        Provider = request.Provider;
        ProviderId = request.ProviderId;
        InstanceId = request.InstanceId;
        ExpectedStoreGeneration = request.ExpectedStoreGeneration;
        EndpointReason = endpointReason;
        CatalogNetworkAdmission = catalogNetworkAdmission;
        StoreAdmission = storeAdmission;
        SelectedModelFingerprint = string.IsNullOrEmpty(selectedModelFingerprint)
            ? string.Empty
            : selectedModelFingerprint.Length == 64
                && selectedModelFingerprint.All(IsHex)
                    ? selectedModelFingerprint.ToUpperInvariant()
                    : throw new ArgumentException(
                        "A model fingerprint must be SHA-256.",
                        nameof(selectedModelFingerprint));
        EndpointEvaluations = endpointEvaluations;
        StoreReads = storeReads;
        CredentialMaterializations = credentialMaterializations;
        NetworkCalls = networkCalls;
        StoreWrites = storeWrites;
    }

    private static bool IsHex(char value)
        => value is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f';
}

/// <summary>
/// Source-only generic validate/catalog/save transaction. The host does not
/// own a network implementation or persistence implementation; both are
/// injected, bounded, and independently Default Off.
/// </summary>
internal sealed class UnifiedProviderSetupTransactionHost
{
    internal const int MaximumRememberedTransactions = 128;

    private readonly IUnifiedProviderSetupEndpointAdmission _endpointAdmission;
    private readonly IUnifiedProviderSetupCatalogProbe _catalogProbe;
    private readonly IUnifiedProviderSetupStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly object _identityGate = new();
    private readonly HashSet<string> _consumedTransactions = new(StringComparer.Ordinal);

    internal UnifiedProviderSetupTransactionHost(
        IUnifiedProviderSetupEndpointAdmission endpointAdmission,
        IUnifiedProviderSetupCatalogProbe catalogProbe,
        IUnifiedProviderSetupStore store,
        Func<DateTimeOffset>? clock = null)
    {
        _endpointAdmission = endpointAdmission
            ?? throw new ArgumentNullException(nameof(endpointAdmission));
        _catalogProbe = catalogProbe ?? throw new ArgumentNullException(nameof(catalogProbe));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<UnifiedProviderSetupResult> RunOnceAsync(
        UnifiedProviderSetupRequest request,
        UnifiedProviderSetupOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        options ??= UnifiedProviderSetupOptions.DefaultOff;
        if (!options.Enabled)
            return Result(UnifiedProviderSetupAdmission.Disabled, request);

        var profile = UnifiedProviderProfileRegistry.Get(request.Provider);
        if (!string.Equals(profile.ProviderId, request.ProviderId, StringComparison.Ordinal)
            || !AuthenticationIsAdmitted(profile, request))
        {
            return Result(
                UnifiedProviderSetupAdmission.ProviderIdentityRejected,
                request);
        }

        var identityAdmission = ConsumeTransactionIdentity(request.TransactionId);
        if (identityAdmission != UnifiedProviderSetupAdmission.Accepted)
            return Result(identityAdmission, request);

        if (cancellationToken.IsCancellationRequested)
            return Result(UnifiedProviderSetupAdmission.Cancelled, request);

        UnifiedProviderSetupEndpointResult endpoint;
        try
        {
            endpoint = _endpointAdmission.Evaluate(
                request.Provider,
                request.BaseUri,
                request.RouteIntent,
                request.EndpointProfileId,
                request.WorkspaceId);
        }
        catch
        {
            return Result(
                UnifiedProviderSetupAdmission.EndpointRejected,
                request,
                endpointEvaluations: 1);
        }

        if (endpoint.Decision == UnifiedProviderSetupEndpointDecision.Rejected
            || endpoint.NormalizedEndpoint == null
            || !SameEndpoint(endpoint.NormalizedEndpoint, request.BaseUri))
        {
            return Result(
                UnifiedProviderSetupAdmission.EndpointRejected,
                request,
                endpointReason: endpoint.Reason,
                endpointEvaluations: 1);
        }
        if (endpoint.Decision
            == UnifiedProviderSetupEndpointDecision.ConnectedAddressEvidenceRequired)
        {
            return Result(
                UnifiedProviderSetupAdmission.ConnectedAddressEvidenceRequired,
                request,
                endpointReason: endpoint.Reason,
                endpointEvaluations: 1);
        }

        using var timeout = new CancellationTokenSource(options.TransactionLifetime);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        bool entered;
        try
        {
            entered = await _slot.WaitAsync(0, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result(
                cancellationToken.IsCancellationRequested
                    ? UnifiedProviderSetupAdmission.Cancelled
                    : UnifiedProviderSetupAdmission.TimedOut,
                request,
                endpointReason: endpoint.Reason,
                endpointEvaluations: 1);
        }
        if (!entered)
        {
            return Result(
                UnifiedProviderSetupAdmission.ConcurrentTransactionRejected,
                request,
                endpointReason: endpoint.Reason,
                endpointEvaluations: 1);
        }

        try
        {
            return await RunInsideSlotAsync(
                    request,
                    profile,
                    endpoint,
                    cancellationToken,
                    timeout,
                    linked.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _slot.Release();
        }
    }

    private async Task<UnifiedProviderSetupResult> RunInsideSlotAsync(
        UnifiedProviderSetupRequest request,
        ProviderControlProfile profile,
        UnifiedProviderSetupEndpointResult endpoint,
        CancellationToken callerCancellation,
        CancellationTokenSource timeout,
        CancellationToken transactionCancellation)
    {
        var storeReads = 0;
        var materializations = 0;
        var networkCalls = 0;
        UnifiedProviderSetupCredentialMaterial? credential = null;
        try
        {
            var before = _store.ReadCurrentGeneration();
            storeReads++;
            if (!StoreGenerationMatches(before, request.ExpectedStoreGeneration))
            {
                return Result(
                    UnifiedProviderSetupAdmission.StaleConfiguration,
                    request,
                    endpointReason: endpoint.Reason,
                    endpointEvaluations: 1,
                    storeReads: storeReads);
            }
            if (transactionCancellation.IsCancellationRequested)
            {
                return CancelledResult(
                    request,
                    endpoint.Reason,
                    storeReads,
                    materializations,
                    networkCalls,
                    callerCancellation,
                    timeout);
            }

            if (request.CredentialSource != null)
            {
                credential = request.CredentialSource.Materialize();
                materializations++;
                if (credential == null
                    || credential.CredentialConfigured
                        != request.CredentialSource.CredentialConfigured)
                {
                    return Result(
                        UnifiedProviderSetupAdmission.CredentialRejected,
                        request,
                        endpointReason: endpoint.Reason,
                        endpointEvaluations: 1,
                        storeReads: storeReads,
                        credentialMaterializations: materializations);
                }
            }

            if (!CredentialIsAdmitted(profile, request, credential))
            {
                return Result(
                    UnifiedProviderSetupAdmission.CredentialRejected,
                    request,
                    endpointReason: endpoint.Reason,
                    endpointEvaluations: 1,
                    storeReads: storeReads,
                    credentialMaterializations: materializations);
            }

            var target = new ProviderCatalogNetworkTarget(
                request.Provider,
                request.ProviderId,
                request.InstanceId,
                request.ExpectedStoreGeneration,
                endpoint.NormalizedEndpoint!,
                request.EndpointProfileId,
                request.WorkspaceId,
                credential?.CredentialConfigured == true,
                request.CustomAuthentication);
            ProviderCatalogNetworkResult catalogResult;
            using (var probeLease = credential?.CreateProbeLease())
            {
                catalogResult = await _catalogProbe.DiscoverOnceAsync(
                        target,
                        probeLease,
                        transactionCancellation)
                    .ConfigureAwait(false);
            }
            networkCalls = catalogResult.NetworkCalls;
            if (networkCalls is < 0 or > 1)
            {
                return Result(
                    UnifiedProviderSetupAdmission.CatalogRejected,
                    request,
                    catalogAdmission: UnifiedProviderSetupCatalogAdmission.Rejected,
                    endpointReason: endpoint.Reason,
                    catalogNetworkAdmission: catalogResult.Admission,
                    endpointEvaluations: 1,
                    storeReads: storeReads,
                    credentialMaterializations: materializations);
            }

            if (!CatalogIdentityMatches(catalogResult, request))
            {
                return Result(
                    UnifiedProviderSetupAdmission.CatalogIdentityRejected,
                    request,
                    catalogAdmission: UnifiedProviderSetupCatalogAdmission.Rejected,
                    endpointReason: endpoint.Reason,
                    catalogNetworkAdmission: catalogResult.Admission,
                    endpointEvaluations: 1,
                    storeReads: storeReads,
                    credentialMaterializations: materializations,
                    networkCalls: networkCalls);
            }

            var catalog = BuildCatalogCommit(
                profile,
                request,
                catalogResult,
                out var catalogAdmission,
                out var selectedModel);
            if (catalog == null)
            {
                return Result(
                    UnifiedProviderSetupAdmission.CatalogRejected,
                    request,
                    catalogAdmission: UnifiedProviderSetupCatalogAdmission.Rejected,
                    endpointReason: endpoint.Reason,
                    catalogNetworkAdmission: catalogResult.Admission,
                    endpointEvaluations: 1,
                    storeReads: storeReads,
                    credentialMaterializations: materializations,
                    networkCalls: networkCalls);
            }
            if (transactionCancellation.IsCancellationRequested)
            {
                return CancelledResult(
                    request,
                    endpoint.Reason,
                    storeReads,
                    materializations,
                    networkCalls,
                    callerCancellation,
                    timeout,
                    catalogAdmission,
                    catalogResult.Admission,
                    selectedModel);
            }

            var finalRead = _store.ReadCurrentGeneration();
            storeReads++;
            if (!StoreGenerationMatches(finalRead, request.ExpectedStoreGeneration))
            {
                return Result(
                    UnifiedProviderSetupAdmission.StaleConfiguration,
                    request,
                    catalogAdmission,
                    endpoint.Reason,
                    catalogResult.Admission,
                    selectedModel,
                    endpointEvaluations: 1,
                    storeReads: storeReads,
                    credentialMaterializations: materializations,
                    networkCalls: networkCalls);
            }

            using var storeLease = credential != null
                ? credential.TransferStoreLease()
                : new UnifiedProviderCredentialLease(Array.Empty<char>());
            var stored = _store.ApplyValidated(
                profile,
                request.InstanceId,
                endpoint.NormalizedEndpoint!,
                request.EndpointProfileId,
                request.WorkspaceId,
                request.RouteIntent,
                request.CustomAuthentication,
                storeLease,
                catalog,
                selectedModel,
                request.ExpectedStoreGeneration,
                _clock(),
                assignTextRoute: request.DesiredModel.Length > 0
                    || catalog.AdvertisesTextCapability(selectedModel));
            var storeWrites = 1;
            return Result(
                stored.Accepted
                    ? UnifiedProviderSetupAdmission.Accepted
                    : stored.Admission == UnifiedProviderStoreAdmission.StaleGeneration
                        ? UnifiedProviderSetupAdmission.StaleConfiguration
                        : UnifiedProviderSetupAdmission.StoreRejected,
                request,
                catalogAdmission,
                endpoint.Reason,
                catalogResult.Admission,
                selectedModel,
                stored.Admission,
                endpointEvaluations: 1,
                storeReads: storeReads,
                credentialMaterializations: materializations,
                networkCalls: networkCalls,
                storeWrites: storeWrites);
        }
        catch (OperationCanceledException)
        {
            return CancelledResult(
                request,
                endpoint.Reason,
                storeReads,
                materializations,
                networkCalls,
                callerCancellation,
                timeout);
        }
        catch
        {
            return Result(
                UnifiedProviderSetupAdmission.Faulted,
                request,
                endpointReason: endpoint.Reason,
                endpointEvaluations: 1,
                storeReads: storeReads,
                credentialMaterializations: materializations,
                networkCalls: networkCalls);
        }
        finally
        {
            credential?.Dispose();
        }
    }

    private UnifiedProviderSetupAdmission ConsumeTransactionIdentity(string identity)
    {
        lock (_identityGate)
        {
            if (_consumedTransactions.Contains(identity))
                return UnifiedProviderSetupAdmission.DuplicateTransaction;
            if (_consumedTransactions.Count >= MaximumRememberedTransactions)
                return UnifiedProviderSetupAdmission.TransactionCapacityReached;
            _consumedTransactions.Add(identity);
            return UnifiedProviderSetupAdmission.Accepted;
        }
    }

    private static bool AuthenticationIsAdmitted(
        ProviderControlProfile profile,
        UnifiedProviderSetupRequest request)
    {
        if (request.Provider != ProviderControlKind.CustomOpenAICompatible)
            return request.CustomAuthentication
                == ProviderCustomAuthenticationKind.ProfileDefault;
        return request.CustomAuthentication is ProviderCustomAuthenticationKind.None
            or ProviderCustomAuthenticationKind.Bearer;
    }

    private static bool CredentialIsAdmitted(
        ProviderControlProfile profile,
        UnifiedProviderSetupRequest request,
        UnifiedProviderSetupCredentialMaterial? credential)
    {
        var configured = credential?.CredentialConfigured == true;
        if (profile.CredentialRequirement == ProviderCredentialRequirement.Required
            && !configured)
        {
            return false;
        }
        if (request.Provider == ProviderControlKind.CustomOpenAICompatible)
        {
            return request.CustomAuthentication switch
            {
                ProviderCustomAuthenticationKind.None => !configured,
                ProviderCustomAuthenticationKind.Bearer => configured,
                _ => false
            };
        }
        return true;
    }

    private static bool CatalogIdentityMatches(
        ProviderCatalogNetworkResult result,
        UnifiedProviderSetupRequest request)
        => result.Provider == request.Provider
           && string.Equals(result.InstanceId, request.InstanceId, StringComparison.Ordinal)
           && result.ConfigurationGeneration == request.ExpectedStoreGeneration;

    private static UnifiedProviderCatalogCommit? BuildCatalogCommit(
        ProviderControlProfile profile,
        UnifiedProviderSetupRequest request,
        ProviderCatalogNetworkResult result,
        out UnifiedProviderSetupCatalogAdmission admission,
        out string selectedModel)
    {
        admission = UnifiedProviderSetupCatalogAdmission.Rejected;
        selectedModel = string.Empty;
        IReadOnlyList<string> models;
        IReadOnlyList<UnifiedProviderCatalogCapabilityEvidence>
            capabilityEvidence;
        string catalogFingerprint;
        if (result.Accepted && result.Catalog != null)
        {
            models = result.Catalog.Models
                .Select(value => value.ModelId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            capabilityEvidence = result.Catalog.Models
                .SelectMany(model => model.Capabilities.Select(capability =>
                    new UnifiedProviderCatalogCapabilityEvidence(
                        model.ModelId,
                        capability.Kind,
                        capability.State,
                        capability.Provenance)))
                .OrderBy(value => value.ModelId, StringComparer.Ordinal)
                .ThenBy(value => value.Kind)
                .ToArray();
            catalogFingerprint = UnifiedProviderConfiguredInstance.Hash(
                "D033/SETUP-CATALOG/2",
                profile.ProviderId,
                request.InstanceId,
                request.ExpectedStoreGeneration.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                string.Join("\u001e", models),
                string.Join(
                    "\u001e",
                    capabilityEvidence.Select(value => string.Join(
                        "\u001d",
                        value.ModelId,
                        ((int)value.Kind).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ((int)value.State).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ((int)value.Provenance).ToString(
                            System.Globalization.CultureInfo.InvariantCulture)))));
            admission = UnifiedProviderSetupCatalogAdmission.ProviderCatalogAccepted;
        }
        else if (profile.CatalogKind is
                     ProviderCatalogKind.QwenOptionalOpenAiModels
                     or ProviderCatalogKind.OptionalOpenAiModels
                 && result.Admission
                    == ProviderCatalogNetworkAdmission.CatalogObservationUnsupported
                 && result.Health.Reason == ProviderHealthReason.ManualSelectionRequired
                 && request.DesiredModel.Length > 0)
        {
            models = new[] { request.DesiredModel };
            capabilityEvidence =
                Array.Empty<UnifiedProviderCatalogCapabilityEvidence>();
            catalogFingerprint = UnifiedProviderConfiguredInstance.Hash(
                "D033/MANUAL-CATALOG/1",
                profile.ProviderId,
                request.InstanceId,
                request.ExpectedStoreGeneration.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                request.DesiredModel);
            admission = UnifiedProviderSetupCatalogAdmission.ManualSelectionAccepted;
        }
        else
        {
            return null;
        }

        try
        {
            selectedModel = request.DesiredModel.Length > 0
                ? request.DesiredModel
                : models.FirstOrDefault(value =>
                    !profile.IsHardRejectedModel(value)
                    && UnifiedProviderCatalogCapabilityRules
                        .AdvertisesTextConversation(capabilityEvidence, value))
                    ?? models.FirstOrDefault(value => !profile.IsHardRejectedModel(value))
                    ?? string.Empty;
            if (selectedModel.Length == 0
                || profile.IsHardRejectedModel(selectedModel)
                || !models.Contains(selectedModel, StringComparer.Ordinal))
            {
                admission = UnifiedProviderSetupCatalogAdmission.Rejected;
                selectedModel = string.Empty;
                return null;
            }
            return new UnifiedProviderCatalogCommit(
                models,
                result.ObservedAt,
                result.ExpiresAt,
                catalogFingerprint,
                capabilityEvidence);
        }
        catch (ArgumentException)
        {
            admission = UnifiedProviderSetupCatalogAdmission.Rejected;
            selectedModel = string.Empty;
            return null;
        }
    }

    private static bool StoreGenerationMatches(
        UnifiedProviderSetupStoreReadResult result,
        long expectedGeneration)
        => result.Admission == UnifiedProviderSetupStoreReadAdmission.Accepted
           && result.StoreGeneration == expectedGeneration;

    private static bool SameEndpoint(Uri left, Uri right)
        => string.Equals(
            UnifiedProviderConfiguredInstance.NormalizeBaseUri(left, nameof(left)).AbsoluteUri,
            UnifiedProviderConfiguredInstance.NormalizeBaseUri(right, nameof(right)).AbsoluteUri,
            StringComparison.Ordinal);

    private static UnifiedProviderSetupResult CancelledResult(
        UnifiedProviderSetupRequest request,
        UnifiedProviderEndpointAdmissionReason endpointReason,
        int storeReads,
        int materializations,
        int networkCalls,
        CancellationToken callerCancellation,
        CancellationTokenSource timeout,
        UnifiedProviderSetupCatalogAdmission catalogAdmission =
            UnifiedProviderSetupCatalogAdmission.None,
        ProviderCatalogNetworkAdmission? catalogNetworkAdmission = null,
        string? selectedModel = null)
        => Result(
            callerCancellation.IsCancellationRequested
                ? UnifiedProviderSetupAdmission.Cancelled
                : timeout.IsCancellationRequested
                    ? UnifiedProviderSetupAdmission.TimedOut
                    : UnifiedProviderSetupAdmission.Cancelled,
            request,
            catalogAdmission,
            endpointReason,
            catalogNetworkAdmission,
            selectedModel,
            endpointEvaluations: 1,
            storeReads: storeReads,
            credentialMaterializations: materializations,
            networkCalls: networkCalls);

    private static UnifiedProviderSetupResult Result(
        UnifiedProviderSetupAdmission admission,
        UnifiedProviderSetupRequest request,
        UnifiedProviderSetupCatalogAdmission catalogAdmission =
            UnifiedProviderSetupCatalogAdmission.None,
        UnifiedProviderEndpointAdmissionReason? endpointReason = null,
        ProviderCatalogNetworkAdmission? catalogNetworkAdmission = null,
        string? selectedModel = null,
        UnifiedProviderStoreAdmission? storeAdmission = null,
        int endpointEvaluations = 0,
        int storeReads = 0,
        int credentialMaterializations = 0,
        int networkCalls = 0,
        int storeWrites = 0)
        => new(
            admission,
            catalogAdmission,
            request,
            endpointReason,
            catalogNetworkAdmission,
            storeAdmission,
            string.IsNullOrEmpty(selectedModel)
                ? string.Empty
                : UnifiedProviderConfiguredInstance.Hash(
                    "D033/SETUP-SELECTED-MODEL/1",
                    selectedModel),
            endpointEvaluations,
            storeReads,
            credentialMaterializations,
            networkCalls,
            storeWrites);
}
