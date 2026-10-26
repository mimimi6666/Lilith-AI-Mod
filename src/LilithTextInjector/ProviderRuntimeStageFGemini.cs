using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LilithProviderControlPlane;

namespace LilithTextInjector;

internal enum D031ProviderRuntimeStageFGeminiStatus
{
    Off = 0,
    ReadyToArm = 1,
    Armed = 2,
    Executing = 3,
    Completed = 4,
    Capacity = 5,
    Cancelled = 6,
    Fault = 7
}

internal enum D031ProviderRuntimeStageFGeminiAdmission
{
    Invalid = 0,
    Off = 1,
    NotArmed = 2,
    CapacityReached = 3,
    ActiveInvocationExists = 4,
    SourceRejected = 5,
    PreparedRequestRejected = 6,
    RegistryProjectionRejected = 7,
    RouteRejected = 8,
    BindingRejected = 9,
    OwnershipRejected = 10,
    AuthorizationRejected = 11,
    RegistryOwned = 12,
    ProviderRejected = 13,
    CompletionIdentityRejected = 14,
    RuntimeIdentityRejected = 15,
    ConfigurationRejected = 16,
    Cancelled = 17,
    Completed = 18
}

internal sealed class D031ProviderRuntimeStageFGeminiConfiguration
{
    internal bool Enabled { get; }
    internal ProviderKind Provider { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }

    private D031ProviderRuntimeStageFGeminiConfiguration(
        bool enabled,
        ProviderKind provider,
        long configurationGeneration,
        string configurationFingerprint,
        CharacterRuntimeIdentity runtimeIdentity)
    {
        Enabled = enabled;
        if (provider is not ProviderKind.Gemini and not ProviderKind.DeepSeek)
            throw new ArgumentOutOfRangeException(nameof(provider));
        Provider = provider;
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = ProviderRegistryContractGuard.Sha256(
            configurationFingerprint,
            nameof(configurationFingerprint));
        RuntimeIdentity = Copy(runtimeIdentity);
    }

    internal static D031ProviderRuntimeStageFGeminiConfiguration Create(
        long configurationGeneration,
        string configurationFingerprint,
        CharacterRuntimeIdentity runtimeIdentity)
        => new(
            true,
            ProviderKind.Gemini,
            configurationGeneration,
            configurationFingerprint,
            runtimeIdentity ?? throw new ArgumentNullException(nameof(runtimeIdentity)));

    internal static D031ProviderRuntimeStageFGeminiConfiguration CreateDeepSeek(
        long configurationGeneration,
        string configurationFingerprint,
        CharacterRuntimeIdentity runtimeIdentity)
        => new(
            true,
            ProviderKind.DeepSeek,
            configurationGeneration,
            configurationFingerprint,
            runtimeIdentity ?? throw new ArgumentNullException(nameof(runtimeIdentity)));

    internal bool Matches(
        D031ProviderRuntimeStageFGeminiConfiguration? other)
        => other != null
            && Enabled == other.Enabled
            && Provider == other.Provider
            && ConfigurationGeneration == other.ConfigurationGeneration
            && string.Equals(
                ConfigurationFingerprint,
                other.ConfigurationFingerprint,
                StringComparison.Ordinal)
            && RuntimeIdentity.Matches(other.RuntimeIdentity);

    private static CharacterRuntimeIdentity Copy(CharacterRuntimeIdentity source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        return new CharacterRuntimeIdentity(
            source.RuntimeProfileId,
            source.RuntimeInstanceId,
            source.Generation,
            source.OrchestratorSessionId,
            source.OwnerIdentity);
    }
}

internal sealed class D031ProviderRuntimeStageFGeminiInvocation
{
    internal string HostId { get; }
    internal string InvocationId { get; }
    internal LivingPreparedProviderRequest Source { get; }
    internal PreparedProviderExecutionRequest Request { get; }
    internal ProviderRegistrySnapshot Registry { get; }
    internal ProviderRouteSelection Route { get; }
    internal ProviderExecutionBinding Binding { get; }
    internal ProviderExecutionOwnershipClaim Ownership { get; }
    internal ProviderExecutionAuthorizationTicket Ticket { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal CancellationToken CancellationToken { get; }
    internal bool LegacyMayFallback => false;
    internal bool AllowsRetry => false;
    internal bool AllowsCrossProviderFallback => false;

    internal D031ProviderRuntimeStageFGeminiInvocation(
        string hostId,
        LivingPreparedProviderRequest source,
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipClaim ownership,
        ProviderExecutionAuthorizationTicket ticket,
        long configurationGeneration,
        string configurationFingerprint,
        CancellationToken cancellationToken)
    {
        HostId = LivingContinuityContractGuard.Identifier(hostId, nameof(hostId));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Route = route ?? throw new ArgumentNullException(nameof(route));
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = ProviderRegistryContractGuard.Sha256(
            configurationFingerprint,
            nameof(configurationFingerprint));
        RuntimeIdentity = new CharacterRuntimeIdentity(
            source.RuntimeIdentity.RuntimeProfileId,
            source.RuntimeIdentity.RuntimeInstanceId,
            source.RuntimeIdentity.Generation,
            source.RuntimeIdentity.OrchestratorSessionId,
            source.RuntimeIdentity.OwnerIdentity);
        CancellationToken = cancellationToken;
        InvocationId = "d031-stage-f-gemini-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/STAGE-F-GEMINI-INVOCATION/1",
                HostId,
                Source.RequestFingerprint,
                Binding.BindingFingerprint,
                Ownership.ClaimId,
                Ticket.TicketId,
                ConfigurationFingerprint)[..32];
    }
}

internal sealed class D031ProviderRuntimeStageFGeminiPreparationResult
{
    internal D031ProviderRuntimeStageFGeminiAdmission Admission { get; }
    internal D031ProviderRuntimeStageFGeminiInvocation? Invocation { get; }
    internal bool RegistryOwned =>
        Admission == D031ProviderRuntimeStageFGeminiAdmission.RegistryOwned
        && Invocation != null;
    internal bool LegacyMayFallback => !RegistryOwned;

    private D031ProviderRuntimeStageFGeminiPreparationResult(
        D031ProviderRuntimeStageFGeminiAdmission admission,
        D031ProviderRuntimeStageFGeminiInvocation? invocation)
    {
        if (!Enum.IsDefined(typeof(D031ProviderRuntimeStageFGeminiAdmission), admission)
            || admission == D031ProviderRuntimeStageFGeminiAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == D031ProviderRuntimeStageFGeminiAdmission.RegistryOwned)
            != (invocation != null))
        {
            throw new ArgumentException(
                "Only RegistryOwned preparation can carry an invocation.",
                nameof(invocation));
        }
        Admission = admission;
        Invocation = invocation;
    }

    internal static D031ProviderRuntimeStageFGeminiPreparationResult Accept(
        D031ProviderRuntimeStageFGeminiInvocation invocation)
        => new(
            D031ProviderRuntimeStageFGeminiAdmission.RegistryOwned,
            invocation ?? throw new ArgumentNullException(nameof(invocation)));

    internal static D031ProviderRuntimeStageFGeminiPreparationResult Reject(
        D031ProviderRuntimeStageFGeminiAdmission admission)
        => new(admission, null);
}

internal sealed class D031ProviderRuntimeStageFGeminiAcceptedCompletion
{
    internal D031ProviderRuntimeStageFGeminiInvocation Invocation { get; }
    internal ProviderProtocolNormalizedCompletion NormalizedCompletion { get; }
    internal ProviderExecutionCompletionIdentity CompletionIdentity { get; }
    internal string InvocationFingerprint { get; }
    internal int NetworkCalls { get; }
    internal int AutomaticRetries => 0;
    internal int CrossProviderFallbacks => 0;

    private D031ProviderRuntimeStageFGeminiAcceptedCompletion(
        D031ProviderRuntimeStageFGeminiInvocation invocation,
        ProviderProtocolNormalizedCompletion normalizedCompletion,
        ProviderExecutionCompletionIdentity completionIdentity,
        int networkCalls)
    {
        Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        NormalizedCompletion = normalizedCompletion
            ?? throw new ArgumentNullException(nameof(normalizedCompletion));
        CompletionIdentity = completionIdentity
            ?? throw new ArgumentNullException(nameof(completionIdentity));
        if (networkCalls != 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        if (!MatchesInvocation(invocation, completionIdentity)
            || !string.Equals(
                normalizedCompletion.TextHash,
                completionIdentity.NormalizedCompletionFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Accepted completion must retain the exact invocation and ledger-admitted identity.",
                nameof(completionIdentity));
        }

        NetworkCalls = networkCalls;
        InvocationFingerprint = FingerprintOf(Invocation);
    }

    internal static string FingerprintOf(
        D031ProviderRuntimeStageFGeminiInvocation invocation)
    {
        if (invocation == null)
            throw new ArgumentNullException(nameof(invocation));
        return ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-F-GEMINI-ACCEPTED-COMPLETION/1",
            invocation.InvocationId,
            invocation.Ticket.TicketId,
            invocation.Binding.BindingFingerprint,
            ProviderRegistryCanonicalHash.Number(1),
            ProviderRegistryCanonicalHash.Number(0),
            ProviderRegistryCanonicalHash.Number(0));
    }

    internal static D031ProviderRuntimeStageFGeminiAcceptedCompletion Issue(
        object capability,
        D031ProviderRuntimeStageFGeminiInvocation invocation,
        ProviderProtocolNormalizedCompletion normalizedCompletion,
        ProviderExecutionCompletionIdentity completionIdentity,
        int networkCalls)
    {
        if (!D031ProviderRuntimeStageFGeminiExecutor
            .OwnsAcceptedCompletionIssuanceCapability(capability))
        {
            throw new InvalidOperationException(
                "Only the Stage F executor can issue an accepted completion.");
        }
        return new D031ProviderRuntimeStageFGeminiAcceptedCompletion(
            invocation,
            normalizedCompletion,
            completionIdentity,
            networkCalls);
    }

    private static bool MatchesInvocation(
        D031ProviderRuntimeStageFGeminiInvocation invocation,
        ProviderExecutionCompletionIdentity completionIdentity)
        => string.Equals(
                completionIdentity.TicketId,
                invocation.Ticket.TicketId,
                StringComparison.Ordinal)
            && string.Equals(
                completionIdentity.BindingFingerprint,
                invocation.Binding.BindingFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                completionIdentity.ExecutionFingerprint,
                invocation.Binding.ExecutionFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                completionIdentity.RouteFingerprint,
                invocation.Binding.RouteFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                completionIdentity.PromptFingerprint,
                invocation.Binding.PromptFingerprint,
                StringComparison.Ordinal)
            && completionIdentity.Destination == invocation.Binding.Destination
            && completionIdentity.RegistryGeneration
                == invocation.Binding.RegistryGeneration
            && completionIdentity.ConfigurationGeneration
                == invocation.Binding.ConfigurationGeneration
            && completionIdentity.InstanceId.Equals(invocation.Binding.InstanceId)
            && completionIdentity.ModelInstanceId.Equals(
                invocation.Binding.ModelInstanceId)
            && completionIdentity.Protocol == invocation.Binding.Protocol
            && completionIdentity.ProtocolProfileVersion
                == invocation.Binding.ProtocolProfileVersion
            && completionIdentity.RuntimeIdentity.Matches(invocation.RuntimeIdentity)
            && string.Equals(
                completionIdentity.RuntimeFingerprint,
                invocation.Binding.RuntimeFingerprint,
                StringComparison.Ordinal);
}

internal sealed class D031ProviderRuntimeStageFGeminiExecutionResult
{
    private readonly string? _responseText;
    private readonly string? _completionId;

    internal D031ProviderRuntimeStageFGeminiAdmission Admission { get; }
    internal D031ProviderRuntimeStageFGeminiAcceptedCompletion? AcceptedCompletion
        { get; }
    internal string? ResponseText =>
        AcceptedCompletion?.NormalizedCompletion.Text ?? _responseText;
    internal string? CompletionId =>
        AcceptedCompletion?.CompletionIdentity.CompletionId ?? _completionId;
    internal int ProviderCalls { get; }
    internal bool RegistryOwned { get; }
    internal bool AllowsPlayerReplyDelivery =>
        RegistryOwned
        && Admission == D031ProviderRuntimeStageFGeminiAdmission.Completed
        && ResponseText != null
        && CompletionId != null;
    internal bool LegacyMayFallback => !RegistryOwned;
    internal int AutomaticRetries => 0;
    internal int CrossProviderFallbacks => 0;

    internal D031ProviderRuntimeStageFGeminiExecutionResult(
        D031ProviderRuntimeStageFGeminiAdmission admission,
        bool registryOwned,
        D031ProviderRuntimeStageFGeminiAcceptedCompletion? acceptedCompletion,
        string? responseText,
        string? completionId,
        int providerCalls)
    {
        if (!Enum.IsDefined(typeof(D031ProviderRuntimeStageFGeminiAdmission), admission)
            || admission == D031ProviderRuntimeStageFGeminiAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (providerCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(providerCalls));
        var carriesLegacyCompletion = responseText != null && completionId != null;
        var carriesAcceptedCompletion = acceptedCompletion != null;
        if ((responseText == null) != (completionId == null)
            || (carriesLegacyCompletion && carriesAcceptedCompletion)
            || (admission == D031ProviderRuntimeStageFGeminiAdmission.Completed)
                != (carriesLegacyCompletion || carriesAcceptedCompletion))
        {
            throw new ArgumentException(
                "Completed execution requires exactly one compatible or accepted completion.",
                nameof(acceptedCompletion));
        }
        if (acceptedCompletion != null
            && providerCalls != acceptedCompletion.NetworkCalls)
        {
            throw new ArgumentException(
                "Accepted completion call count must match the execution result.",
                nameof(providerCalls));
        }
        Admission = admission;
        RegistryOwned = registryOwned;
        AcceptedCompletion = acceptedCompletion;
        _responseText = responseText;
        _completionId = completionId;
        ProviderCalls = providerCalls;
    }
}

internal sealed class D031ProviderRuntimeStageFGeminiSnapshot
{
    internal D031ProviderRuntimeStageFGeminiStatus Status { get; }
    internal bool Enabled { get; }
    internal bool Armed { get; }
    internal int Authorized { get; }
    internal int Active { get; }
    internal int Completed { get; }
    internal int Failed { get; }
    internal int Cancelled { get; }
    internal int ProviderCalls { get; }
    internal string? LastAdmission { get; }
    internal string? LastInvocationId { get; }

    internal D031ProviderRuntimeStageFGeminiSnapshot(
        D031ProviderRuntimeStageFGeminiStatus status,
        bool enabled,
        bool armed,
        int authorized,
        int active,
        int completed,
        int failed,
        int cancelled,
        int providerCalls,
        string? lastAdmission,
        string? lastInvocationId)
    {
        Status = status;
        Enabled = enabled;
        Armed = armed;
        Authorized = authorized;
        Active = active;
        Completed = completed;
        Failed = failed;
        Cancelled = cancelled;
        ProviderCalls = providerCalls;
        LastAdmission = lastAdmission;
        LastInvocationId = lastInvocationId;
    }
}

internal sealed class D031ProviderRuntimeStageFGeminiHost
{
    internal const int MaximumProviderCallsPerProcess = 1;
    internal const int MaximumActiveInvocations = 1;
    internal const int AutomaticRetryCount = 0;
    internal const int CrossProviderFallbackCount = 0;

    private readonly object _gate = new();
    private readonly string _hostId;
    private readonly ProviderExecutionOwnershipLedger _ownership = new();
    private readonly ProviderExecutionAuthorizationLedger _authorization = new();
    private D031ProviderRuntimeStageFGeminiConfiguration? _configuration;
    private D031ProviderRuntimeStageFGeminiStatus _status =
        D031ProviderRuntimeStageFGeminiStatus.Off;
    private CancellationTokenSource? _activeCancellation;
    private D031ProviderRuntimeStageFGeminiInvocation? _activeInvocation;
    private string? _activeInvocationId;
    private int _authorized;
    private int _active;
    private int _completed;
    private int _failed;
    private int _cancelled;
    private int _providerCalls;
    private string? _lastAdmission;
    private string? _lastInvocationId;
    private bool _continuousIntegration;

    internal D031ProviderRuntimeStageFGeminiHost(string? hostId = null)
    {
        _hostId = LivingContinuityContractGuard.Identifier(
            hostId ?? "d031-stage-f-host-" + Guid.NewGuid().ToString("N"),
            nameof(hostId));
    }

    internal void Reconcile(D031ProviderRuntimeStageFGeminiConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (_configuration?.Matches(configuration) == true)
                return;
            CancelActiveLocked();
            _configuration = configuration;
            _status = NextReadyStatusLocked();
        }
    }

    internal void SetContinuousIntegration(bool enabled)
    {
        lock (_gate)
        {
            if (_continuousIntegration == enabled)
                return;
            _continuousIntegration = enabled;
            if (_configuration == null || _active != 0)
                return;
            _status = NextReadyStatusLocked();
        }
    }

    internal void Disable()
    {
        lock (_gate)
        {
            CancelActiveLocked();
            _configuration = null;
            _status = D031ProviderRuntimeStageFGeminiStatus.Off;
        }
    }

    internal bool ArmForProcess()
    {
        lock (_gate)
        {
            if (_configuration == null
                || !HasAuthorizationCapacityLocked()
                || _active != 0)
            {
                return false;
            }
            _status = D031ProviderRuntimeStageFGeminiStatus.Armed;
            return true;
        }
    }

    internal bool CanOwnGeminiPlayerReply()
        => CanOwnPlayerReply(ProviderKind.Gemini);

    internal bool CanOwnDeepSeekPlayerReply()
        => CanOwnPlayerReply(ProviderKind.DeepSeek);

    private bool CanOwnPlayerReply(ProviderKind provider)
    {
        lock (_gate)
        {
            return _configuration != null
                && _configuration.Provider == provider
                && _status == D031ProviderRuntimeStageFGeminiStatus.Armed
                && HasAuthorizationCapacityLocked()
                && _active == 0;
        }
    }

    internal D031ProviderRuntimeStageFGeminiPreparationResult TryPrepare(
        LivingPreparedProviderRequest source,
        LegacyProviderRegistryInput registryInput,
        CharacterRuntimeIdentity activeRuntime,
        long configurationGeneration,
        string configurationFingerprint,
        DateTimeOffset now)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (registryInput == null)
            throw new ArgumentNullException(nameof(registryInput));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));

        lock (_gate)
        {
            if (_configuration == null)
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.Off);
            if (!HasAuthorizationCapacityLocked())
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.CapacityReached);
            if (_active != 0)
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.ActiveInvocationExists);
            if (_status != D031ProviderRuntimeStageFGeminiStatus.Armed)
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.NotArmed);
            if (configurationGeneration != _configuration.ConfigurationGeneration
                || !string.Equals(
                    configurationFingerprint,
                    _configuration.ConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.ConfigurationRejected);
            }
            if (!_configuration.RuntimeIdentity.Matches(activeRuntime)
                || !source.RuntimeIdentity.Matches(activeRuntime))
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.RuntimeIdentityRejected);
            }
            var provider = _configuration.Provider;
            var providerToken = provider == ProviderKind.Gemini
                ? "Gemini"
                : "DeepSeek";
            var expectedProtocol = provider == ProviderKind.Gemini
                ? ProviderProtocolKind.GeminiGenerateContent
                : ProviderProtocolKind.OpenAiChatCompletions;
            if (source.SourceKind != LivingPromptSourceKind.PlayerTriggered
                || source.Destination != LivingPreparedCompletionDestination.PlayerReply
                || !string.Equals(source.ProviderRouteId, providerToken, StringComparison.Ordinal))
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.SourceRejected);
            }

            var preparedAdmission = PreparedProviderExecutionRequest.TryCreate(
                source,
                now,
                out var prepared);
            if (preparedAdmission != PreparedProviderCodecAdmission.Encoded
                || prepared == null
                || prepared.AllowsTools
                || prepared.AllowsSearch)
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.PreparedRequestRejected);
            }
            var projectionResult = LegacyProviderRegistryProjector.Project(
                registryInput,
                now);
            var projection = projectionResult.Projection;
            if (projectionResult.Admission != LegacyProviderRegistryAdmission.Projected
                || projection == null
                || projection.ActiveModelMigrationState
                    != LegacyModelMigrationState.CurrentOrUnverified)
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.RegistryProjectionRejected);
            }
            var modelTransportValue = provider == ProviderKind.Gemini
                ? registryInput.GeminiModel
                : registryInput.DeepSeekModel;
            var executableRegistry =
                CreateTextOnlyExecutableRegistry(
                    projection,
                    provider,
                    modelTransportValue);
            if (executableRegistry == null)
            {
                return RejectLocked(
                    D031ProviderRuntimeStageFGeminiAdmission
                        .RegistryProjectionRejected);
            }
            var routeResult = ProviderRouteSelection.CreateForContractReplay(
                executableRegistry,
                projection.ActiveInstanceId,
                projection.ActiveModelId,
                new ProviderCapabilityRequirementSet(
                    new[] { ProviderCapability.TextGeneration }),
                ProcessingLocalityRequirement.ExternalProcessingAllowed,
                ProviderRegistryCanonicalHash.Of(
                    "D031/STAGE-F-PROCESSING-POLICY/2",
                    providerToken,
                    "player-triggered",
                    "text-only",
                    "one-call",
                    "no-retry",
                    "no-fallback"),
                now,
                source.ExpiresAt);
            var route = routeResult.Selection;
            if (!routeResult.Accepted
                || route == null
                || route.Kind != provider
                || route.Protocol != expectedProtocol)
            {
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.RouteRejected);
            }
            var bindingResult = ProviderExecutionBinding.TryCreate(
                source,
                prepared,
                executableRegistry,
                route,
                activeRuntime,
                now);
            var binding = bindingResult.Binding;
            if (!bindingResult.Accepted || binding == null)
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.BindingRejected);
            var ownershipResult = _ownership.TryAssign(
                binding,
                ProviderExecutionOwnershipState.RegistryOwned,
                now);
            var ownership = ownershipResult.Claim;
            if (!ownershipResult.Accepted || ownership == null)
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.OwnershipRejected);
            var authorizationResult = _authorization.TryIssue(
                binding,
                ownership,
                now);
            var ticket = authorizationResult.Ticket;
            if (!authorizationResult.Accepted || ticket == null)
            {
                _status = D031ProviderRuntimeStageFGeminiStatus.Fault;
                return RejectLocked(D031ProviderRuntimeStageFGeminiAdmission.AuthorizationRejected);
            }

            _activeCancellation = new CancellationTokenSource();
            var invocation = new D031ProviderRuntimeStageFGeminiInvocation(
                _hostId,
                source,
                prepared,
                executableRegistry,
                route,
                binding,
                ownership,
                ticket,
                configurationGeneration,
                configurationFingerprint,
                _activeCancellation.Token);
            _activeInvocation = invocation;
            _activeInvocationId = invocation.InvocationId;
            _lastInvocationId = invocation.InvocationId;
            _authorized++;
            _active = 1;
            _status = D031ProviderRuntimeStageFGeminiStatus.Executing;
            _lastAdmission = D031ProviderRuntimeStageFGeminiAdmission.RegistryOwned.ToString();
            return D031ProviderRuntimeStageFGeminiPreparationResult.Accept(invocation);
        }
    }

    internal bool OwnsActive(D031ProviderRuntimeStageFGeminiInvocation invocation)
    {
        if (invocation == null)
            throw new ArgumentNullException(nameof(invocation));
        lock (_gate)
        {
            return _configuration != null
                && _active == 1
                && ReferenceEquals(invocation, _activeInvocation)
                && string.Equals(invocation.HostId, _hostId, StringComparison.Ordinal)
                && string.Equals(
                    invocation.InvocationId,
                    _activeInvocationId,
                    StringComparison.Ordinal)
                && invocation.ConfigurationGeneration
                    == _configuration.ConfigurationGeneration
                && string.Equals(
                    invocation.ConfigurationFingerprint,
                    _configuration.ConfigurationFingerprint,
                    StringComparison.Ordinal)
                && invocation.RuntimeIdentity.Matches(_configuration.RuntimeIdentity);
        }
    }

    internal void Finish(
        D031ProviderRuntimeStageFGeminiInvocation invocation,
        D031ProviderRuntimeStageFGeminiAdmission admission,
        int providerCalls)
    {
        if (invocation == null)
            throw new ArgumentNullException(nameof(invocation));
        lock (_gate)
        {
            if (_active == 0
                || !ReferenceEquals(invocation, _activeInvocation)
                || !string.Equals(invocation.HostId, _hostId, StringComparison.Ordinal)
                || !string.Equals(
                    invocation.InvocationId,
                    _activeInvocationId,
                    StringComparison.Ordinal))
            {
                return;
            }
            _providerCalls += providerCalls;
            _lastAdmission = admission.ToString();
            if (admission == D031ProviderRuntimeStageFGeminiAdmission.Completed)
                _completed++;
            else if (admission == D031ProviderRuntimeStageFGeminiAdmission.Cancelled)
                _cancelled++;
            else
                _failed++;
            _active = 0;
            _activeInvocation = null;
            _activeInvocationId = null;
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            _status = _configuration == null
                ? D031ProviderRuntimeStageFGeminiStatus.Off
                : _continuousIntegration
                    ? admission == D031ProviderRuntimeStageFGeminiAdmission.Completed
                        || admission == D031ProviderRuntimeStageFGeminiAdmission.Cancelled
                        ? D031ProviderRuntimeStageFGeminiStatus.Armed
                        : D031ProviderRuntimeStageFGeminiStatus.Fault
                    : _authorized >= MaximumProviderCallsPerProcess
                        ? D031ProviderRuntimeStageFGeminiStatus.Capacity
                        : admission == D031ProviderRuntimeStageFGeminiAdmission.Cancelled
                            ? D031ProviderRuntimeStageFGeminiStatus.Cancelled
                            : admission == D031ProviderRuntimeStageFGeminiAdmission.Completed
                                ? D031ProviderRuntimeStageFGeminiStatus.Completed
                                : D031ProviderRuntimeStageFGeminiStatus.Fault;
        }
    }

    internal D031ProviderRuntimeStageFGeminiSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new D031ProviderRuntimeStageFGeminiSnapshot(
                _status,
                _configuration != null,
                _status == D031ProviderRuntimeStageFGeminiStatus.Armed,
                _authorized,
                _active,
                _completed,
                _failed,
                _cancelled,
                _providerCalls,
                _lastAdmission,
                _lastInvocationId);
        }
    }

    private D031ProviderRuntimeStageFGeminiPreparationResult RejectLocked(
        D031ProviderRuntimeStageFGeminiAdmission admission)
    {
        _lastAdmission = admission.ToString();
        return D031ProviderRuntimeStageFGeminiPreparationResult.Reject(admission);
    }

    private bool HasAuthorizationCapacityLocked()
        => _continuousIntegration
            || _authorized < MaximumProviderCallsPerProcess;

    private D031ProviderRuntimeStageFGeminiStatus NextReadyStatusLocked()
        => _configuration == null
            ? D031ProviderRuntimeStageFGeminiStatus.Off
            : _continuousIntegration
                ? D031ProviderRuntimeStageFGeminiStatus.Armed
                : _authorized >= MaximumProviderCallsPerProcess
                    ? D031ProviderRuntimeStageFGeminiStatus.Capacity
                    : D031ProviderRuntimeStageFGeminiStatus.ReadyToArm;

    private static ProviderRegistrySnapshot?
        CreateTextOnlyExecutableRegistry(
            LegacyProviderRegistryProjection projection,
            ProviderKind provider,
            string modelTransportValue)
    {
        if (!projection.Snapshot.TryGetInstance(
                projection.ActiveInstanceId,
                out var instance)
            || instance == null
            || instance.Kind != provider
            || !projection.Snapshot.TryGetModel(
                projection.ActiveInstanceId,
                projection.ActiveModelId,
                out var model)
            || model == null)
        {
            return null;
        }

        var capabilities = new ProviderCapabilityProfile(
            Enum.GetValues<ProviderCapability>()
                .Where(value => value != ProviderCapability.Invalid)
                .Select(
                    capability => capability
                        == ProviderCapability.TextGeneration
                        ? new ProviderCapabilityStatus(
                            capability,
                            ProviderCapabilityEvidenceState.Supported,
                            ProviderCapabilityEvidenceState.Supported,
                            ProviderCapabilityAuthorizationState.Authorized)
                        : new ProviderCapabilityStatus(
                            capability,
                            ProviderCapabilityEvidenceState.Unknown,
                            ProviderCapabilityEvidenceState.Unknown,
                            ProviderCapabilityAuthorizationState
                                .NotAuthorized))
                .ToArray());
        var executableModel = new ProviderModelDescriptor(
            model.InstanceId,
            model.ModelInstanceId,
            model.SafeDisplayAlias,
            LivingPromptHash.Of(modelTransportValue),
            model.TransportIdentityClass,
            model.TransportReference,
            model.CatalogFacts,
            capabilities,
            model.ExecutionVenue,
            model.ExecutionVenueProvenance,
            model.ExecutionVenueEvidence);
        var models = projection.Snapshot.Models
            .Select(
                current => current.InstanceId.Equals(model.InstanceId)
                    && current.ModelInstanceId.Equals(model.ModelInstanceId)
                        ? executableModel
                        : current)
            .ToArray();
        var result = ProviderRegistrySnapshot.Create(
            projection.Snapshot.RegistryVersion,
            projection.Snapshot.RegistryGeneration,
            projection.Snapshot.Definitions,
            projection.Snapshot.Instances,
            models);
        return result.Accepted ? result.Snapshot : null;
    }

    private void CancelActiveLocked()
    {
        if (_active == 0)
            return;
        _activeCancellation?.Cancel();
        _cancelled++;
        _active = 0;
        _activeInvocation = null;
        _activeInvocationId = null;
        _activeCancellation?.Dispose();
        _activeCancellation = null;
        _lastAdmission = D031ProviderRuntimeStageFGeminiAdmission.Cancelled.ToString();
    }
}

internal sealed class D031ProviderRuntimeStageFGeminiExecutor
{
    private static readonly object AcceptedCompletionIssuer = new();

    internal static bool OwnsAcceptedCompletionIssuanceCapability(
        object? capability)
        => ReferenceEquals(capability, AcceptedCompletionIssuer);

    private readonly D031ProviderRuntimeStageFGeminiHost _host;
    private readonly System.Net.Http.HttpClient _http;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderOneShotModelTransportResolver _models;
    private readonly IUnifiedProviderConnectedAddressBoundary _connectedAddresses;
    private readonly IPreparedProviderExecutionLifecycle? _lifecycle;
    private readonly Func<DateTimeOffset> _clock;

    internal D031ProviderRuntimeStageFGeminiExecutor(
        D031ProviderRuntimeStageFGeminiHost host,
        System.Net.Http.HttpClient http,
        IProviderCredentialResolver credentials,
        IProviderOneShotModelTransportResolver models,
        Func<DateTimeOffset>? clock = null,
        IPreparedProviderExecutionLifecycle? lifecycle = null)
        : this(host, http, credentials, models, null, clock, lifecycle)
    {
    }

    internal D031ProviderRuntimeStageFGeminiExecutor(
        D031ProviderRuntimeStageFGeminiHost host,
        System.Net.Http.HttpClient http,
        IProviderCredentialResolver credentials,
        IProviderOneShotModelTransportResolver models,
        IUnifiedProviderConnectedAddressBoundary? connectedAddresses,
        Func<DateTimeOffset>? clock = null,
        IPreparedProviderExecutionLifecycle? lifecycle = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _connectedAddresses = UnifiedProviderConnectedAddressHttp.BoundaryFor(
            http,
            connectedAddresses);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lifecycle = lifecycle;
    }

    internal async Task<D031ProviderRuntimeStageFGeminiExecutionResult>
        ExecuteOnceAsync(
            D031ProviderRuntimeStageFGeminiInvocation invocation,
            CharacterRuntimeIdentity activeRuntime,
            CancellationToken cancellationToken,
            bool captureAcceptedCompletionArtifact = false)
    {
        if (invocation == null)
            throw new ArgumentNullException(nameof(invocation));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        var providerCalls = 0;
        var terminal = D031ProviderRuntimeStageFGeminiAdmission.ProviderRejected;
        try
        {
            if (!_host.OwnsActive(invocation)
                || !activeRuntime.Matches(invocation.RuntimeIdentity))
            {
                terminal = D031ProviderRuntimeStageFGeminiAdmission.RuntimeIdentityRejected;
                return Reject(terminal, providerCalls);
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                invocation.CancellationToken);
            var executor = new ProviderNeutralOneShotExecutor(
                _http,
                _credentials,
                _models,
                _connectedAddresses,
                _clock,
                _lifecycle);
            var result = await executor.ExecuteOnceAsync(
                    invocation.Ticket,
                    invocation.Binding,
                    invocation.Request,
                    invocation.Registry,
                    invocation.Route,
                    activeRuntime,
                    linked.Token)
                .ConfigureAwait(false);
            providerCalls = result.NetworkCalls;
            if (!result.Accepted
                || result.Completion == null
                || result.CompletionIdentity == null)
            {
                terminal = linked.IsCancellationRequested
                    ? D031ProviderRuntimeStageFGeminiAdmission.Cancelled
                    : D031ProviderRuntimeStageFGeminiAdmission.ProviderRejected;
                return Reject(terminal, providerCalls);
            }
            if (!_host.OwnsActive(invocation)
                || !activeRuntime.Matches(invocation.RuntimeIdentity)
                || !string.Equals(
                    result.CompletionIdentity.TicketId,
                    invocation.Ticket.TicketId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    result.CompletionIdentity.BindingFingerprint,
                    invocation.Binding.BindingFingerprint,
                    StringComparison.Ordinal)
                || result.CompletionIdentity.Destination
                    != LivingPreparedCompletionDestination.PlayerReply)
            {
                terminal = D031ProviderRuntimeStageFGeminiAdmission.CompletionIdentityRejected;
                return Reject(terminal, providerCalls);
            }
            var acceptedCompletion = captureAcceptedCompletionArtifact
                ? D031ProviderRuntimeStageFGeminiAcceptedCompletion.Issue(
                    AcceptedCompletionIssuer,
                    invocation,
                    result.Completion,
                    result.CompletionIdentity,
                    providerCalls)
                : null;
            terminal = D031ProviderRuntimeStageFGeminiAdmission.Completed;
            return new D031ProviderRuntimeStageFGeminiExecutionResult(
                terminal,
                registryOwned: true,
                acceptedCompletion,
                responseText: acceptedCompletion == null
                    ? result.Completion.Text
                    : null,
                completionId: acceptedCompletion == null
                    ? result.CompletionIdentity.CompletionId
                    : null,
                providerCalls);
        }
        catch (OperationCanceledException)
        {
            terminal = D031ProviderRuntimeStageFGeminiAdmission.Cancelled;
            return Reject(terminal, providerCalls);
        }
        finally
        {
            _host.Finish(invocation, terminal, providerCalls);
        }
    }

    private static D031ProviderRuntimeStageFGeminiExecutionResult Reject(
        D031ProviderRuntimeStageFGeminiAdmission admission,
        int providerCalls)
        => new(
            admission,
            registryOwned: true,
            acceptedCompletion: null,
            responseText: null,
            completionId: null,
            providerCalls);
}

internal sealed class D031ProviderRuntimeStageFGeminiAuthorityCounters
{
    internal int AutomaticRetries => 0;
    internal int CrossProviderFallbacks => 0;
    internal int ExtraProviderCalls => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int MemoryWrites => 0;
    internal int CharacterWrites => 0;
    internal int RelationshipWrites => 0;
    internal int DecisionWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int ToolCalls => 0;
    internal bool IsZero => true;
}

internal static class D031ProviderRuntimeStageFGeminiStatusFormatter
{
    internal static string Format(
        D031ProviderRuntimeStageFGeminiSnapshot snapshot,
        OrchestratorObserveStatusLanguage language,
        string provider = "Gemini")
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (provider is not "Gemini" and not "DeepSeek")
            throw new ArgumentOutOfRangeException(nameof(provider));
        var title = language switch
        {
            OrchestratorObserveStatusLanguage.TraditionalChinese =>
                "D-031 " + provider + " Registry bounded migration",
            OrchestratorObserveStatusLanguage.SimplifiedChinese =>
                "D-031 " + provider + " Registry 有界迁移",
            OrchestratorObserveStatusLanguage.Japanese =>
                "D-031 " + provider + " Registry bounded migration",
            _ => "D-031 " + provider + " Registry bounded migration"
        };
        return title
            + "\nstatus=" + snapshot.Status
            + " enabled=" + snapshot.Enabled
            + " armed=" + snapshot.Armed
            + "\nauthorized=" + snapshot.Authorized + "/1"
            + " active=" + snapshot.Active
            + " completed=" + snapshot.Completed
            + " failed=" + snapshot.Failed
            + " cancelled=" + snapshot.Cancelled
            + "\nproviderCalls=" + snapshot.ProviderCalls
            + " retry=0 fallback=0"
            + "\nlast=" + (snapshot.LastAdmission ?? "-")
            + " invocation=" + (snapshot.LastInvocationId ?? "-");
    }
}
