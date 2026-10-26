using System;

namespace LilithTextInjector;

internal enum ProviderRouteSelectorMode
{
    Invalid = 0,
    Off = 1,
    Observe = 2
}

internal enum ProviderEndpointHealthState
{
    Invalid = 0,
    Healthy = 1,
    Unhealthy = 2,
    Unknown = 3
}

internal enum ProviderRouteObserveAdmission
{
    Invalid = 0,
    SelectedForObserve = 1,
    SelectorOff = 2,
    RegistryGenerationMismatch = 3,
    InstanceUnknown = 4,
    InstanceDisabled = 5,
    ModelUnknown = 6,
    HealthIdentityMismatch = 7,
    HealthUnhealthy = 8,
    HealthUnknown = 9,
    HealthNotYetValid = 10,
    HealthExpired = 11,
    RouteRequirementRejected = 12
}

internal sealed class ProviderEndpointHealthObservation
{
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal string EndpointFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderEndpointHealthState State { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal string Fingerprint { get; }

    internal ProviderEndpointHealthObservation(
        ProviderEndpointInstanceId instanceId,
        string endpointFingerprint,
        long configurationGeneration,
        ProviderEndpointHealthState state,
        DateTimeOffset observedAt,
        DateTimeOffset validUntil)
    {
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        EndpointFingerprint = ProviderRegistryContractGuard.Sha256(
            endpointFingerprint,
            nameof(endpointFingerprint));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        ConfigurationGeneration = configurationGeneration;
        State = ProviderRegistryContractGuard.Defined(
            state,
            nameof(state));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ValidUntil = DecisionCoreContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (ValidUntil <= ObservedAt)
        {
            throw new ArgumentException(
                "Health observation must have a positive validity window.",
                nameof(validUntil));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/ENDPOINT-HEALTH-OBSERVATION/1",
            InstanceId.Value,
            EndpointFingerprint,
            ProviderRegistryCanonicalHash.Number(
                ConfigurationGeneration),
            ProviderRegistryCanonicalHash.Number((int)State),
            ProviderRegistryCanonicalHash.Timestamp(ObservedAt),
            ProviderRegistryCanonicalHash.Timestamp(ValidUntil));
    }
}

internal sealed class ProviderRouteObserveRequest
{
    internal long ExpectedRegistryGeneration { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal ProviderCapabilityRequirementSet RequiredCapabilities { get; }
    internal ProcessingLocalityRequirement LocalityRequirement { get; }
    internal string ProcessingPolicyFingerprint { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal DateTimeOffset RouteExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderRouteObserveRequest(
        long expectedRegistryGeneration,
        ProviderEndpointInstanceId instanceId,
        ProviderModelInstanceId modelInstanceId,
        ProviderCapabilityRequirementSet requiredCapabilities,
        ProcessingLocalityRequirement localityRequirement,
        string processingPolicyFingerprint,
        DateTimeOffset evaluatedAt,
        DateTimeOffset routeExpiresAt)
    {
        if (expectedRegistryGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedRegistryGeneration));
        }
        ExpectedRegistryGeneration = expectedRegistryGeneration;
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        ModelInstanceId = modelInstanceId
            ?? throw new ArgumentNullException(nameof(modelInstanceId));
        RequiredCapabilities = requiredCapabilities
            ?? throw new ArgumentNullException(
                nameof(requiredCapabilities));
        LocalityRequirement = ProviderRegistryContractGuard.Defined(
            localityRequirement,
            nameof(localityRequirement));
        ProcessingPolicyFingerprint =
            ProviderRegistryContractGuard.Sha256(
                processingPolicyFingerprint,
                nameof(processingPolicyFingerprint));
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        RouteExpiresAt = DecisionCoreContractGuard.Timestamp(
            routeExpiresAt,
            nameof(routeExpiresAt));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/ROUTE-OBSERVE-REQUEST/1",
            ProviderRegistryCanonicalHash.Number(
                ExpectedRegistryGeneration),
            InstanceId.Value,
            ModelInstanceId.Value,
            RequiredCapabilities.Fingerprint,
            ProviderRegistryCanonicalHash.Number(
                (int)LocalityRequirement),
            ProcessingPolicyFingerprint,
            ProviderRegistryCanonicalHash.Timestamp(EvaluatedAt),
            ProviderRegistryCanonicalHash.Timestamp(RouteExpiresAt));
    }
}

internal sealed class ProviderRouteObserveResult
{
    internal ProviderRouteObserveAdmission Admission { get; }
    internal ProviderRouteSelectionAdmission?
        RouteSelectionAdmission { get; }
    internal ProviderRouteSelection? Selection { get; }
    internal string? HealthFingerprint { get; }
    internal bool Accepted =>
        Admission == ProviderRouteObserveAdmission.SelectedForObserve;
    internal bool AllowsProviderCall => false;
    internal bool GrantsRouteOwnership => false;

    private ProviderRouteObserveResult(
        ProviderRouteObserveAdmission admission,
        ProviderRouteSelectionAdmission? routeSelectionAdmission,
        ProviderRouteSelection? selection,
        string? healthFingerprint)
    {
        if (!Enum.IsDefined(
                typeof(ProviderRouteObserveAdmission),
                admission)
            || admission == ProviderRouteObserveAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderRouteObserveAdmission.SelectedForObserve)
            != (selection != null))
        {
            throw new ArgumentException(
                "Only accepted Observe results may carry a selection.");
        }
        if (admission
                == ProviderRouteObserveAdmission.RouteRequirementRejected
            != routeSelectionAdmission.HasValue)
        {
            throw new ArgumentException(
                "Only route requirement rejection carries its typed reason.");
        }
        Admission = admission;
        RouteSelectionAdmission = routeSelectionAdmission;
        Selection = selection;
        HealthFingerprint = healthFingerprint;
    }

    internal static ProviderRouteObserveResult Accept(
        ProviderRouteSelection selection,
        string healthFingerprint)
        => new(
            ProviderRouteObserveAdmission.SelectedForObserve,
            null,
            selection ?? throw new ArgumentNullException(nameof(selection)),
            ProviderRegistryContractGuard.Sha256(
                healthFingerprint,
                nameof(healthFingerprint)));

    internal static ProviderRouteObserveResult Reject(
        ProviderRouteObserveAdmission admission)
        => new(admission, null, null, null);

    internal static ProviderRouteObserveResult RejectRoute(
        ProviderRouteSelectionAdmission routeAdmission,
        string healthFingerprint)
        => new(
            ProviderRouteObserveAdmission.RouteRequirementRejected,
            routeAdmission,
            null,
            ProviderRegistryContractGuard.Sha256(
                healthFingerprint,
                nameof(healthFingerprint)));
}

internal interface IProviderRouteSelector
{
    ProviderRouteObserveResult Select(
        ProviderRegistrySnapshot registry,
        ProviderRouteObserveRequest request,
        ProviderEndpointHealthObservation health);
}

internal sealed class DeterministicProviderRouteObserveSelector :
    IProviderRouteSelector
{
    private readonly ProviderRouteSelectorMode _mode;

    internal DeterministicProviderRouteObserveSelector(
        ProviderRouteSelectorMode mode)
    {
        if (!Enum.IsDefined(typeof(ProviderRouteSelectorMode), mode)
            || mode == ProviderRouteSelectorMode.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        _mode = mode;
    }

    public ProviderRouteObserveResult Select(
        ProviderRegistrySnapshot registry,
        ProviderRouteObserveRequest request,
        ProviderEndpointHealthObservation health)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (health == null)
            throw new ArgumentNullException(nameof(health));
        if (_mode == ProviderRouteSelectorMode.Off)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.SelectorOff);
        }
        if (registry.RegistryGeneration
            != request.ExpectedRegistryGeneration)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission
                    .RegistryGenerationMismatch);
        }
        if (!registry.TryGetInstance(
                request.InstanceId,
                out var instance)
            || instance == null)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.InstanceUnknown);
        }
        if (!instance.Enabled)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.InstanceDisabled);
        }
        if (!registry.TryGetModel(
                request.InstanceId,
                request.ModelInstanceId,
                out var model)
            || model == null)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.ModelUnknown);
        }
        if (!health.InstanceId.Equals(instance.InstanceId)
            || health.ConfigurationGeneration
                != instance.ConfigurationGeneration
            || !string.Equals(
                health.EndpointFingerprint,
                instance.EndpointFingerprint,
                StringComparison.Ordinal))
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.HealthIdentityMismatch);
        }
        if (request.EvaluatedAt < health.ObservedAt)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.HealthNotYetValid);
        }
        if (request.EvaluatedAt >= health.ValidUntil)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.HealthExpired);
        }
        if (health.State == ProviderEndpointHealthState.Unknown)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.HealthUnknown);
        }
        if (health.State == ProviderEndpointHealthState.Unhealthy)
        {
            return ProviderRouteObserveResult.Reject(
                ProviderRouteObserveAdmission.HealthUnhealthy);
        }

        var route = ProviderRouteSelection.CreateForContractReplay(
            registry,
            request.InstanceId,
            request.ModelInstanceId,
            request.RequiredCapabilities,
            request.LocalityRequirement,
            request.ProcessingPolicyFingerprint,
            request.EvaluatedAt,
            request.RouteExpiresAt);
        if (!route.Accepted || route.Selection == null)
        {
            return ProviderRouteObserveResult.RejectRoute(
                route.Admission,
                health.Fingerprint);
        }
        return ProviderRouteObserveResult.Accept(
            route.Selection,
            health.Fingerprint);
    }
}

internal enum ProviderRouteComparison
{
    Invalid = 0,
    Equivalent = 1,
    DifferentInstance = 2,
    DifferentModel = 3,
    DifferentProtocol = 4,
    LegacyMissing = 5
}

internal sealed class LegacyProviderRouteMetadata
{
    internal bool Present { get; }
    internal ProviderEndpointInstanceId? InstanceId { get; }
    internal ProviderModelInstanceId? ModelInstanceId { get; }
    internal ProviderProtocolKind? Protocol { get; }

    internal LegacyProviderRouteMetadata(
        ProviderEndpointInstanceId instanceId,
        ProviderModelInstanceId modelInstanceId,
        ProviderProtocolKind protocol)
    {
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        ModelInstanceId = modelInstanceId
            ?? throw new ArgumentNullException(nameof(modelInstanceId));
        Protocol = ProviderRegistryContractGuard.Defined(
            protocol,
            nameof(protocol));
        Present = true;
    }

    private LegacyProviderRouteMetadata()
    {
        Present = false;
    }

    internal static LegacyProviderRouteMetadata Missing()
        => new();
}

internal static class ProviderRouteObserveComparison
{
    internal static ProviderRouteComparison Compare(
        LegacyProviderRouteMetadata legacy,
        ProviderRouteSelection registrySelection)
    {
        if (legacy == null)
            throw new ArgumentNullException(nameof(legacy));
        if (registrySelection == null)
        {
            throw new ArgumentNullException(
                nameof(registrySelection));
        }
        if (!legacy.Present)
            return ProviderRouteComparison.LegacyMissing;
        if (!legacy.InstanceId!.Equals(registrySelection.InstanceId))
            return ProviderRouteComparison.DifferentInstance;
        if (!legacy.ModelInstanceId!.Equals(
                registrySelection.ModelInstanceId))
        {
            return ProviderRouteComparison.DifferentModel;
        }
        if (legacy.Protocol != registrySelection.Protocol)
            return ProviderRouteComparison.DifferentProtocol;
        return ProviderRouteComparison.Equivalent;
    }
}

internal sealed class ProviderRouteObserveAuthorityCounters
{
    internal int ProviderCalls => 0;
    internal int NetworkCalls => 0;
    internal int CredentialResolutions => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int DecisionWrites => 0;
    internal int RouteOwnershipWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionWrites => 0;
    internal int PersistenceWrites => 0;
}
