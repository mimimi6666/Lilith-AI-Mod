using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D031ProviderRuntimeStageEStatus
{
    Invalid = 0,
    Off = 1,
    Observe = 2,
    Fault = 3,
    Capacity = 4
}

internal enum D031ProviderRuntimeStageEAdmission
{
    Invalid = 0,
    Observed = 1,
    Off = 2,
    Faulted = 3,
    CapacityReached = 4,
    RuntimeIdentityMismatch = 5,
    ConfigurationMismatch = 6,
    OpportunityNotYetValid = 7,
    OpportunityExpired = 8,
    DuplicateOpportunity = 9,
    ProjectionRejected = 10,
    ProjectedRouteMissing = 11,
    OwnershipConflict = 12
}

internal enum D031ProviderRouteMetadataComparison
{
    Invalid = 0,
    Equivalent = 1,
    DifferentInstance = 2,
    DifferentModel = 3,
    DifferentEndpoint = 4,
    DifferentProtocol = 5,
    DifferentConfigurationGeneration = 6
}

internal enum D031ProviderLegacyDisposition
{
    Invalid = 0,
    ContinueLegacy = 1,
    SuppressLegacyNoFallback = 2
}

internal sealed class D031ProviderRuntimeStageEConfiguration
{
    internal bool Enabled { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }

    private D031ProviderRuntimeStageEConfiguration(
        bool enabled,
        CharacterRuntimeIdentity? runtimeIdentity,
        long configurationGeneration,
        string configurationFingerprint)
    {
        Enabled = enabled;
        RuntimeIdentity = runtimeIdentity == null
            ? null
            : CopyRuntime(runtimeIdentity);
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = configurationFingerprint;
    }

    internal static D031ProviderRuntimeStageEConfiguration Off()
        => new(false, null, 0, string.Empty);

    internal static D031ProviderRuntimeStageEConfiguration Observe(
        CharacterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        string configurationFingerprint)
    {
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        return new D031ProviderRuntimeStageEConfiguration(
            true,
            runtimeIdentity,
            configurationGeneration,
            ProviderRegistryContractGuard.Sha256(
                configurationFingerprint,
                nameof(configurationFingerprint)));
    }

    internal bool Matches(D031ProviderRuntimeStageEConfiguration? other)
        => other != null
            && Enabled == other.Enabled
            && ConfigurationGeneration == other.ConfigurationGeneration
            && string.Equals(
                ConfigurationFingerprint,
                other.ConfigurationFingerprint,
                StringComparison.Ordinal)
            && ((RuntimeIdentity == null && other.RuntimeIdentity == null)
                || RuntimeIdentity?.Matches(other.RuntimeIdentity) == true);

    private static CharacterRuntimeIdentity CopyRuntime(
        CharacterRuntimeIdentity runtime)
        => new(
            runtime.RuntimeProfileId,
            runtime.RuntimeInstanceId,
            runtime.Generation,
            runtime.OrchestratorSessionId,
            runtime.OwnerIdentity);
}

internal sealed class D031ProviderLegacyRouteMetadata
{
    internal string ProviderToken { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal string EndpointFingerprint { get; }
    internal string ModelCorrelationHash { get; }
    internal long ConfigurationGeneration { get; }
    internal string Fingerprint { get; }

    internal D031ProviderLegacyRouteMetadata(
        string providerToken,
        string endpoint,
        string model,
        ProviderProtocolKind protocol,
        long configurationGeneration)
    {
        ProviderToken = ProviderRegistryContractGuard.Identifier(
            providerToken,
            nameof(providerToken)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(model)
            || model.Length > ProviderRegistryContractGuard.MaximumIdentifierLength)
        {
            throw new ArgumentException(
                "A bounded model identifier is required.",
                nameof(model));
        }
        Protocol = ProviderRegistryContractGuard.Defined(
            protocol,
            nameof(protocol));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        var endpointResult = ProviderEndpointUri.TryCreate(endpoint);
        if (!endpointResult.Accepted || endpointResult.Endpoint == null)
        {
            throw new ArgumentException(
                "The legacy endpoint is not admitted.",
                nameof(endpoint));
        }
        ConfigurationGeneration = configurationGeneration;
        InstanceId = new ProviderEndpointInstanceId(
            "legacy-" + ProviderToken);
        ModelInstanceId = new ProviderModelInstanceId(
            "legacy-model-" + ProviderToken);
        EndpointFingerprint = endpointResult.Endpoint.Fingerprint;
        ModelCorrelationHash = ProviderRegistryCanonicalHash.Of(
            "D027/LEGACY-MODEL-CORRELATION/1",
            ProviderToken,
            model.Trim());
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-E-LEGACY-ROUTE/1",
            ProviderToken,
            InstanceId.Value,
            ModelInstanceId.Value,
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            EndpointFingerprint,
            ModelCorrelationHash,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration));
    }
}

internal sealed class D031ProviderRuntimeStageEOpportunity
{
    internal string OpportunityId { get; }
    internal string SourceId { get; }
    internal string RequestFingerprint { get; }
    internal string PromptFingerprint { get; }
    internal D031ProviderLegacyRouteMetadata LegacyRoute { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string RuntimeFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string OpportunityFingerprint { get; }

    internal D031ProviderRuntimeStageEOpportunity(
        string opportunityId,
        string sourceId,
        string requestFingerprint,
        string promptFingerprint,
        D031ProviderLegacyRouteMetadata legacyRoute,
        CharacterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        string configurationFingerprint,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        OpportunityId = ProviderRegistryContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
        SourceId = ProviderRegistryContractGuard.Identifier(
            sourceId,
            nameof(sourceId));
        RequestFingerprint = ProviderRegistryContractGuard.Sha256(
            requestFingerprint,
            nameof(requestFingerprint));
        PromptFingerprint = ProviderRegistryContractGuard.Sha256(
            promptFingerprint,
            nameof(promptFingerprint));
        LegacyRoute = legacyRoute
            ?? throw new ArgumentNullException(nameof(legacyRoute));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        RuntimeIdentity = new CharacterRuntimeIdentity(
            runtimeIdentity.RuntimeProfileId,
            runtimeIdentity.RuntimeInstanceId,
            runtimeIdentity.Generation,
            runtimeIdentity.OrchestratorSessionId,
            runtimeIdentity.OwnerIdentity);
        RuntimeFingerprint = ProviderExecutionBinding.RuntimeHash(
            RuntimeIdentity);
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = ProviderRegistryContractGuard.Sha256(
            configurationFingerprint,
            nameof(configurationFingerprint));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= ObservedAt)
        {
            throw new ArgumentException(
                "The opportunity must have a positive lifetime.",
                nameof(expiresAt));
        }
        OpportunityFingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-E-OPPORTUNITY/1",
            OpportunityId,
            SourceId,
            RequestFingerprint,
            PromptFingerprint,
            LegacyRoute.Fingerprint,
            RuntimeFingerprint,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration),
            ConfigurationFingerprint,
            ProviderRegistryCanonicalHash.Timestamp(ObservedAt),
            ProviderRegistryCanonicalHash.Timestamp(ExpiresAt));
    }
}

internal sealed class D031ProviderOpportunityOwnershipClaim
{
    internal string OpportunityFingerprint { get; }
    internal ProviderExecutionOwnershipState State { get; }
    internal DateTimeOffset ClaimedAt { get; }
    internal string ClaimId { get; }
    internal D031ProviderLegacyDisposition LegacyDisposition =>
        D031ProviderCoexistencePolicy.For(State);
    internal bool AllowsAuthorizationTicket =>
        State == ProviderExecutionOwnershipState.RegistryOwned;
    internal bool AllowsProviderCallInStageE => false;

    internal D031ProviderOpportunityOwnershipClaim(
        D031ProviderRuntimeStageEOpportunity opportunity,
        ProviderExecutionOwnershipState state,
        DateTimeOffset claimedAt)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (!Enum.IsDefined(typeof(ProviderExecutionOwnershipState), state)
            || state == ProviderExecutionOwnershipState.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        OpportunityFingerprint = opportunity.OpportunityFingerprint;
        State = state;
        ClaimedAt = DecisionCoreContractGuard.Timestamp(
            claimedAt,
            nameof(claimedAt));
        ClaimId = "d031-stage-e-owner-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/STAGE-E-OWNER/1",
                OpportunityFingerprint,
                ProviderRegistryCanonicalHash.Number((int)State),
                ProviderRegistryCanonicalHash.Timestamp(ClaimedAt))[..32];
    }
}

internal enum D031ProviderOpportunityOwnershipAdmission
{
    Invalid = 0,
    Assigned = 1,
    Duplicate = 2,
    Conflict = 3
}

internal sealed class D031ProviderOpportunityOwnershipResult
{
    internal D031ProviderOpportunityOwnershipAdmission Admission { get; }
    internal D031ProviderOpportunityOwnershipClaim? Claim { get; }
    internal bool Accepted =>
        Admission == D031ProviderOpportunityOwnershipAdmission.Assigned;

    internal D031ProviderOpportunityOwnershipResult(
        D031ProviderOpportunityOwnershipAdmission admission,
        D031ProviderOpportunityOwnershipClaim? claim)
    {
        if (!Enum.IsDefined(
                typeof(D031ProviderOpportunityOwnershipAdmission),
                admission)
            || admission
                == D031ProviderOpportunityOwnershipAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == D031ProviderOpportunityOwnershipAdmission.Assigned)
            != (claim != null))
        {
            throw new ArgumentException(
                "Only assigned ownership carries a claim.",
                nameof(claim));
        }
        Admission = admission;
        Claim = claim;
    }
}

internal sealed class D031ProviderOpportunityOwnershipLedger
{
    private readonly Dictionary<string, D031ProviderOpportunityOwnershipClaim>
        _claims = new(StringComparer.Ordinal);

    internal D031ProviderOpportunityOwnershipResult TryAssign(
        D031ProviderRuntimeStageEOpportunity opportunity,
        ProviderExecutionOwnershipState state,
        DateTimeOffset claimedAt)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (_claims.TryGetValue(
                opportunity.OpportunityFingerprint,
                out var existing))
        {
            return new D031ProviderOpportunityOwnershipResult(
                existing.State == state
                    ? D031ProviderOpportunityOwnershipAdmission.Duplicate
                    : D031ProviderOpportunityOwnershipAdmission.Conflict,
                null);
        }
        var claim = new D031ProviderOpportunityOwnershipClaim(
            opportunity,
            state,
            claimedAt);
        _claims.Add(opportunity.OpportunityFingerprint, claim);
        return new D031ProviderOpportunityOwnershipResult(
            D031ProviderOpportunityOwnershipAdmission.Assigned,
            claim);
    }
}

internal static class D031ProviderCoexistencePolicy
{
    internal static D031ProviderLegacyDisposition For(
        ProviderExecutionOwnershipState state)
        => state switch
        {
            ProviderExecutionOwnershipState.LegacyOwned =>
                D031ProviderLegacyDisposition.ContinueLegacy,
            ProviderExecutionOwnershipState.RegistryObserve =>
                D031ProviderLegacyDisposition.ContinueLegacy,
            ProviderExecutionOwnershipState.RegistryOwned =>
                D031ProviderLegacyDisposition.SuppressLegacyNoFallback,
            ProviderExecutionOwnershipState.Fault =>
                D031ProviderLegacyDisposition.SuppressLegacyNoFallback,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
}

internal sealed class D031ProviderRuntimeStageEObservation
{
    internal string ObservationId { get; }
    internal string OpportunityFingerprint { get; }
    internal D031ProviderRouteMetadataComparison Comparison { get; }
    internal ProviderExecutionOwnershipState Ownership { get; }
    internal D031ProviderLegacyDisposition LegacyDisposition { get; }
    internal string LegacyRouteFingerprint { get; }
    internal string RegistryRouteFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal bool AllowsProviderCall => false;
    internal bool AllowsCompletionDelivery => false;

    internal D031ProviderRuntimeStageEObservation(
        D031ProviderRuntimeStageEOpportunity opportunity,
        D031ProviderRouteMetadataComparison comparison,
        D031ProviderOpportunityOwnershipClaim claim,
        string registryRouteFingerprint,
        long registryGeneration,
        DateTimeOffset observedAt)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (claim == null)
            throw new ArgumentNullException(nameof(claim));
        Comparison = ProviderRegistryContractGuard.Defined(
            comparison,
            nameof(comparison));
        OpportunityFingerprint = opportunity.OpportunityFingerprint;
        Ownership = claim.State;
        LegacyDisposition = claim.LegacyDisposition;
        LegacyRouteFingerprint = opportunity.LegacyRoute.Fingerprint;
        RegistryRouteFingerprint = ProviderRegistryContractGuard.Sha256(
            registryRouteFingerprint,
            nameof(registryRouteFingerprint));
        if (registryGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(registryGeneration));
        RegistryGeneration = registryGeneration;
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ObservationId = "d031-stage-e-observation-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/STAGE-E-OBSERVATION/1",
                OpportunityFingerprint,
                ProviderRegistryCanonicalHash.Number((int)Comparison),
                claim.ClaimId,
                RegistryRouteFingerprint,
                ProviderRegistryCanonicalHash.Number(RegistryGeneration),
                ProviderRegistryCanonicalHash.Timestamp(ObservedAt))[..24];
    }
}

internal sealed class D031ProviderRuntimeStageEResult
{
    internal D031ProviderRuntimeStageEAdmission Admission { get; }
    internal D031ProviderRuntimeStageEObservation? Observation { get; }
    internal LegacyProviderRegistryAdmission? ProjectionAdmission { get; }
    internal bool Accepted =>
        Admission == D031ProviderRuntimeStageEAdmission.Observed;
    internal bool ContinueLegacy =>
        Observation?.LegacyDisposition
            == D031ProviderLegacyDisposition.ContinueLegacy;
    internal D031ProviderRuntimeStageEAuthorityCounters AuthorityCounters { get; }
        = D031ProviderRuntimeStageEAuthorityCounters.Zero;

    private D031ProviderRuntimeStageEResult(
        D031ProviderRuntimeStageEAdmission admission,
        D031ProviderRuntimeStageEObservation? observation,
        LegacyProviderRegistryAdmission? projectionAdmission)
    {
        if (!Enum.IsDefined(
                typeof(D031ProviderRuntimeStageEAdmission),
                admission)
            || admission == D031ProviderRuntimeStageEAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == D031ProviderRuntimeStageEAdmission.Observed)
            != (observation != null))
        {
            throw new ArgumentException(
                "Only an observed result carries metadata.",
                nameof(observation));
        }
        Admission = admission;
        Observation = observation;
        ProjectionAdmission = projectionAdmission;
    }

    internal static D031ProviderRuntimeStageEResult Accept(
        D031ProviderRuntimeStageEObservation observation)
        => new(
            D031ProviderRuntimeStageEAdmission.Observed,
            observation ?? throw new ArgumentNullException(nameof(observation)),
            LegacyProviderRegistryAdmission.Projected);

    internal static D031ProviderRuntimeStageEResult Reject(
        D031ProviderRuntimeStageEAdmission admission,
        LegacyProviderRegistryAdmission? projectionAdmission = null)
        => new(admission, null, projectionAdmission);
}

internal sealed class D031ProviderRuntimeStageESnapshot
{
    internal D031ProviderRuntimeStageEStatus Status { get; }
    internal int Observed { get; }
    internal int Rejected { get; }
    internal int Capacity { get; }
    internal D031ProviderRuntimeStageEObservation? Latest { get; }
    internal D031ProviderRuntimeStageEAuthorityCounters AuthorityCounters { get; }
        = D031ProviderRuntimeStageEAuthorityCounters.Zero;

    internal D031ProviderRuntimeStageESnapshot(
        D031ProviderRuntimeStageEStatus status,
        int observed,
        int rejected,
        int capacity,
        D031ProviderRuntimeStageEObservation? latest)
    {
        Status = ProviderRegistryContractGuard.Defined(status, nameof(status));
        Observed = observed;
        Rejected = rejected;
        Capacity = capacity;
        Latest = latest;
    }
}

internal sealed class D031ProviderRuntimeStageEHost
{
    internal const int MaximumObservations = 128;

    private readonly object _gate = new();
    private readonly List<D031ProviderRuntimeStageEObservation> _observations =
        new(MaximumObservations);
    private D031ProviderOpportunityOwnershipLedger _ownership = new();
    private D031ProviderRuntimeStageEConfiguration _configuration =
        D031ProviderRuntimeStageEConfiguration.Off();
    private D031ProviderRuntimeStageEStatus _status =
        D031ProviderRuntimeStageEStatus.Off;
    private int _rejected;

    internal void Reconcile(
        D031ProviderRuntimeStageEConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (!configuration.Enabled)
            {
                Clear();
                _configuration = configuration;
                _status = D031ProviderRuntimeStageEStatus.Off;
                return;
            }
            if (!_configuration.Matches(configuration))
                Clear();
            _configuration = configuration;
            _status = D031ProviderRuntimeStageEStatus.Observe;
        }
    }

    internal D031ProviderRuntimeStageEResult Observe(
        D031ProviderRuntimeStageEOpportunity opportunity,
        LegacyProviderRegistryInput registryInput,
        DateTimeOffset now)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (registryInput == null)
            throw new ArgumentNullException(nameof(registryInput));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (_status == D031ProviderRuntimeStageEStatus.Off)
                return Reject(D031ProviderRuntimeStageEAdmission.Off);
            if (_status == D031ProviderRuntimeStageEStatus.Fault)
                return Reject(D031ProviderRuntimeStageEAdmission.Faulted);
            if (_status == D031ProviderRuntimeStageEStatus.Capacity
                || _observations.Count >= MaximumObservations)
            {
                _status = D031ProviderRuntimeStageEStatus.Capacity;
                return Reject(
                    D031ProviderRuntimeStageEAdmission.CapacityReached);
            }
            if (_configuration.RuntimeIdentity?.Matches(
                    opportunity.RuntimeIdentity) != true)
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.RuntimeIdentityMismatch);
            }
            if (_configuration.ConfigurationGeneration
                    != opportunity.ConfigurationGeneration
                || registryInput.ConfigurationGeneration
                    != opportunity.ConfigurationGeneration
                || !string.Equals(
                    _configuration.ConfigurationFingerprint,
                    opportunity.ConfigurationFingerprint,
                    StringComparison.Ordinal))
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.ConfigurationMismatch);
            }
            if (now < opportunity.ObservedAt)
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.OpportunityNotYetValid);
            }
            if (now >= opportunity.ExpiresAt)
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.OpportunityExpired);
            }
            if (_observations.Exists(
                    item => string.Equals(
                        item.OpportunityFingerprint,
                        opportunity.OpportunityFingerprint,
                        StringComparison.Ordinal)))
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.DuplicateOpportunity);
            }

            var projection = LegacyProviderRegistryProjector.Project(
                registryInput,
                now);
            if (projection.Admission
                    != LegacyProviderRegistryAdmission.Projected
                || projection.Projection == null)
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.ProjectionRejected,
                    projection.Admission);
            }
            if (!projection.Projection.Snapshot.TryGetInstance(
                    projection.Projection.ActiveInstanceId,
                    out var projectedInstance)
                || projectedInstance == null
                || !projection.Projection.Snapshot.TryGetModel(
                    projection.Projection.ActiveInstanceId,
                    projection.Projection.ActiveModelId,
                    out var projectedModel)
                || projectedModel == null)
            {
                return Reject(
                    D031ProviderRuntimeStageEAdmission.ProjectedRouteMissing,
                    projection.Admission);
            }

            var ownership = _ownership.TryAssign(
                opportunity,
                ProviderExecutionOwnershipState.RegistryObserve,
                now);
            if (!ownership.Accepted || ownership.Claim == null)
            {
                return Reject(
                    ownership.Admission
                        == D031ProviderOpportunityOwnershipAdmission.Duplicate
                        ? D031ProviderRuntimeStageEAdmission.DuplicateOpportunity
                        : D031ProviderRuntimeStageEAdmission.OwnershipConflict);
            }
            var comparison = Compare(
                opportunity.LegacyRoute,
                projectedInstance,
                projectedModel);
            var observation = new D031ProviderRuntimeStageEObservation(
                opportunity,
                comparison,
                ownership.Claim,
                ProviderRegistryCanonicalHash.Of(
                    "D031/STAGE-E-PROJECTED-ROUTE/1",
                    projectedInstance.InstanceId.Value,
                    projectedModel.ModelInstanceId.Value,
                    projectedInstance.EndpointFingerprint,
                    projectedModel.CorrelationHash,
                    ProviderRegistryCanonicalHash.Number(
                        (int)projectedInstance.Protocol),
                    ProviderRegistryCanonicalHash.Number(
                        projectedInstance.ConfigurationGeneration)),
                projection.Projection.Snapshot.RegistryGeneration,
                now);
            _observations.Add(observation);
            return D031ProviderRuntimeStageEResult.Accept(observation);
        }
    }

    internal void StopByFault()
    {
        lock (_gate)
        {
            Clear();
            _status = D031ProviderRuntimeStageEStatus.Fault;
        }
    }

    internal D031ProviderRuntimeStageESnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new D031ProviderRuntimeStageESnapshot(
                _status,
                _observations.Count,
                _rejected,
                MaximumObservations,
                _observations.Count == 0
                    ? null
                    : _observations[^1]);
        }
    }

    private D031ProviderRuntimeStageEResult Reject(
        D031ProviderRuntimeStageEAdmission admission,
        LegacyProviderRegistryAdmission? projectionAdmission = null)
    {
        _rejected++;
        return D031ProviderRuntimeStageEResult.Reject(
            admission,
            projectionAdmission);
    }

    private static D031ProviderRouteMetadataComparison Compare(
        D031ProviderLegacyRouteMetadata legacy,
        ProviderEndpointInstance projectedInstance,
        ProviderModelDescriptor projectedModel)
    {
        if (!legacy.InstanceId.Equals(projectedInstance.InstanceId))
            return D031ProviderRouteMetadataComparison.DifferentInstance;
        if (!legacy.ModelInstanceId.Equals(projectedModel.ModelInstanceId)
            || !string.Equals(
                legacy.ModelCorrelationHash,
                projectedModel.CorrelationHash,
                StringComparison.Ordinal))
        {
            return D031ProviderRouteMetadataComparison.DifferentModel;
        }
        if (!string.Equals(
                legacy.EndpointFingerprint,
                projectedInstance.EndpointFingerprint,
                StringComparison.Ordinal))
        {
            return D031ProviderRouteMetadataComparison.DifferentEndpoint;
        }
        if (legacy.Protocol != projectedInstance.Protocol)
            return D031ProviderRouteMetadataComparison.DifferentProtocol;
        if (legacy.ConfigurationGeneration
            != projectedInstance.ConfigurationGeneration)
        {
            return D031ProviderRouteMetadataComparison
                .DifferentConfigurationGeneration;
        }
        return D031ProviderRouteMetadataComparison.Equivalent;
    }

    private void Clear()
    {
        _observations.Clear();
        _ownership = new D031ProviderOpportunityOwnershipLedger();
        _rejected = 0;
    }
}

internal sealed class D031ProviderRuntimeStageEAuthorityCounters
{
    internal static D031ProviderRuntimeStageEAuthorityCounters Zero { get; } =
        new();

    internal int AuthorizationTickets => 0;
    internal int CredentialReads => 0;
    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int DecisionWrites => 0;
    internal int CompletionDeliveries => 0;
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int PersistenceWrites => 0;
    internal bool IsZero => true;

    private D031ProviderRuntimeStageEAuthorityCounters()
    {
    }
}

internal static class D031ProviderRuntimeStageEStatusFormatter
{
    internal static string Format(
        D031ProviderRuntimeStageESnapshot snapshot,
        OrchestratorObserveStatusLanguage language)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var title = language switch
        {
            OrchestratorObserveStatusLanguage.TraditionalChinese =>
                "D-031 Provider 路由觀察",
            OrchestratorObserveStatusLanguage.SimplifiedChinese =>
                "D-031 Provider 路由观察",
            OrchestratorObserveStatusLanguage.Japanese =>
                "D-031 Provider ルート観察",
            _ => "D-031 Provider Route Observe"
        };
        var latest = snapshot.Latest;
        return title
            + "\nstatus=" + snapshot.Status
            + " observed=" + snapshot.Observed
            + " rejected=" + snapshot.Rejected
            + " capacity=" + snapshot.Observed + "/" + snapshot.Capacity
            + "\nlatest=" + (latest?.Comparison.ToString() ?? "-")
            + " owner=" + (latest?.Ownership.ToString() ?? "-")
            + " legacy=" + (latest?.LegacyDisposition.ToString() ?? "-")
            + "\nticket=0 provider=0 delivery=0 retry=0 fallback=0";
    }
}
