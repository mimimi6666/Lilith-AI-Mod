using System;
using System.Threading;

namespace LilithTextInjector;

internal enum ProviderExecutionBindingAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    SourceRequestIdentityMismatch = 2,
    PromptIdentityMismatch = 3,
    CompletionDestinationMismatch = 4,
    RuntimeIdentityMismatch = 5,
    RegistryGenerationMismatch = 6,
    RegistryFingerprintMismatch = 7,
    InstanceIdentityMismatch = 8,
    ModelIdentityMismatch = 9,
    RequestNotYetValid = 10,
    RequestExpired = 11,
    RouteNotYetValid = 12,
    RouteExpired = 13
}

internal enum ProviderExecutionOwnershipState
{
    Invalid = 0,
    LegacyOwned = 1,
    RegistryObserve = 2,
    RegistryOwned = 3,
    Fault = 4
}

internal enum ProviderExecutionOwnershipAdmission
{
    Invalid = 0,
    AssignedForContractReplay = 1,
    DuplicateOwnership = 2,
    OwnershipConflict = 3,
    BindingExpired = 4
}

internal enum ProviderExecutionAuthorizationAdmission
{
    Invalid = 0,
    AuthorizedForContractReplay = 1,
    OwnershipMissing = 2,
    OwnershipNotRegistryOwned = 3,
    OwnershipIdentityMismatch = 4,
    DuplicateAuthorization = 5,
    RequestExpired = 6
}

internal enum ProviderExecutionTicketConsumptionAdmission
{
    Invalid = 0,
    ConsumedForContractReplay = 1,
    TicketExpired = 2,
    TicketAlreadyConsumed = 3,
    BindingIdentityMismatch = 4,
    RuntimeIdentityMismatch = 5,
    RegistryGenerationMismatch = 6,
    ConfigurationGenerationMismatch = 7
}

internal enum ProviderExecutionCompletionAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    TicketNotConsumed = 2,
    TicketIdentityMismatch = 3,
    BindingIdentityMismatch = 4,
    RuntimeIdentityMismatch = 5,
    RegistryGenerationMismatch = 6,
    ConfigurationGenerationMismatch = 7,
    CompletionBeforeAuthorization = 8,
    CompletionExpired = 9,
    DuplicateCompletion = 10,
    DuplicateTicketCompletion = 11
}

internal sealed class ProviderExecutionBindingResult
{
    internal ProviderExecutionBindingAdmission Admission { get; }
    internal ProviderExecutionBinding? Binding { get; }
    internal bool Accepted =>
        Admission
            == ProviderExecutionBindingAdmission
                .AcceptedForContractReplay;

    private ProviderExecutionBindingResult(
        ProviderExecutionBindingAdmission admission,
        ProviderExecutionBinding? binding)
    {
        if (!Enum.IsDefined(typeof(ProviderExecutionBindingAdmission), admission)
            || admission == ProviderExecutionBindingAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderExecutionBindingAdmission
                    .AcceptedForContractReplay)
            != (binding != null))
        {
            throw new ArgumentException(
                "Only an accepted binding result can carry a binding.",
                nameof(binding));
        }
        Admission = admission;
        Binding = binding;
    }

    internal static ProviderExecutionBindingResult Accept(
        ProviderExecutionBinding binding)
        => new(
            ProviderExecutionBindingAdmission.AcceptedForContractReplay,
            binding ?? throw new ArgumentNullException(nameof(binding)));

    internal static ProviderExecutionBindingResult Reject(
        ProviderExecutionBindingAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderExecutionBinding
{
    internal string SourceRequestFingerprint { get; }
    internal string ExecutionFingerprint { get; }
    internal string PromptFingerprint { get; }
    internal string SourceRouteCorrelation { get; }
    internal LivingPreparedCompletionDestination Destination { get; }
    internal string RequestCorrelationId { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderDefinitionId DefinitionId { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal string EndpointFingerprint { get; }
    internal string ModelDescriptorFingerprint { get; }
    internal string RouteFingerprint { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string RuntimeFingerprint { get; }
    internal DateTimeOffset RequestCreatedAt { get; }
    internal DateTimeOffset RequestDeadline { get; }
    internal DateTimeOffset RouteSelectedAt { get; }
    internal DateTimeOffset RouteDeadline { get; }
    internal DateTimeOffset EffectiveDeadline { get; }
    internal string BindingFingerprint { get; }
    internal bool ConstructsPrompt => false;
    internal bool ReadsMemory => false;
    internal bool WritesDecision => false;
    internal bool AllowsRetry => false;
    internal bool AllowsProviderFallback => false;
    internal bool AllowsStageAProviderCall => false;

    private ProviderExecutionBinding(
        LivingPreparedProviderRequest source,
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route)
    {
        SourceRequestFingerprint = request.SourceRequestFingerprint;
        ExecutionFingerprint = request.ExecutionFingerprint;
        PromptFingerprint = request.SystemInstructionHash;
        SourceRouteCorrelation = source.ProviderRouteId;
        Destination = request.Destination;
        RequestCorrelationId = request.CorrelationId;
        RegistryGeneration = registry.RegistryGeneration;
        RegistryFingerprint = registry.Fingerprint;
        ConfigurationGeneration = route.ConfigurationGeneration;
        DefinitionId = route.DefinitionId;
        InstanceId = route.InstanceId;
        ModelInstanceId = route.ModelInstanceId;
        Protocol = route.Protocol;
        ProtocolProfileVersion = route.ProtocolProfileVersion;
        EndpointFingerprint = route.EndpointFingerprint;
        ModelDescriptorFingerprint = route.ModelDescriptorFingerprint;
        RouteFingerprint = route.RouteFingerprint;
        RuntimeIdentity = CopyRuntime(source.RuntimeIdentity);
        RuntimeFingerprint = RuntimeHash(RuntimeIdentity);
        RequestCreatedAt = request.RequestedAt;
        RequestDeadline = request.Deadline;
        RouteSelectedAt = route.SelectedAt;
        RouteDeadline = route.ExpiresAt;
        EffectiveDeadline = RequestDeadline <= RouteDeadline
            ? RequestDeadline
            : RouteDeadline;
        BindingFingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/PROVIDER-EXECUTION-BINDING/1",
            SourceRequestFingerprint,
            ExecutionFingerprint,
            PromptFingerprint,
            SourceRouteCorrelation,
            ProviderRegistryCanonicalHash.Number((int)Destination),
            RequestCorrelationId,
            ProviderRegistryCanonicalHash.Number(RegistryGeneration),
            RegistryFingerprint,
            ProviderRegistryCanonicalHash.Number(ConfigurationGeneration),
            DefinitionId.Value,
            InstanceId.Value,
            ModelInstanceId.Value,
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
            EndpointFingerprint,
            ModelDescriptorFingerprint,
            RouteFingerprint,
            RuntimeFingerprint,
            ProviderRegistryCanonicalHash.Timestamp(RequestCreatedAt),
            ProviderRegistryCanonicalHash.Timestamp(RequestDeadline),
            ProviderRegistryCanonicalHash.Timestamp(RouteSelectedAt),
            ProviderRegistryCanonicalHash.Timestamp(RouteDeadline));
    }

    internal static ProviderExecutionBindingResult TryCreate(
        LivingPreparedProviderRequest source,
        PreparedProviderExecutionRequest request,
        ProviderRegistrySnapshot registry,
        ProviderRouteSelection route,
        CharacterRuntimeIdentity activeRuntime,
        DateTimeOffset observedAt)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        observedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));

        if (!string.Equals(source.RequestId, request.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(
                source.RequestFingerprint,
                request.SourceRequestFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                source.ProviderRouteId,
                request.ProviderRouteId,
                StringComparison.Ordinal))
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.SourceRequestIdentityMismatch);
        }
        if (!string.Equals(
                source.SystemPromptHash,
                request.SystemInstructionHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.PromptIdentityMismatch);
        }
        if (source.Destination != request.Destination)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.CompletionDestinationMismatch);
        }
        if (!source.RuntimeIdentity.Matches(activeRuntime))
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RuntimeIdentityMismatch);
        }
        if (route.RegistryGeneration != registry.RegistryGeneration)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RegistryGenerationMismatch);
        }
        if (!string.Equals(
                route.RegistryFingerprint,
                registry.Fingerprint,
                StringComparison.Ordinal))
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RegistryFingerprintMismatch);
        }
        if (!registry.TryGetInstance(route.InstanceId, out var instance)
            || instance == null
            || instance.ConfigurationGeneration != route.ConfigurationGeneration
            || !string.Equals(
                instance.ConfigurationFingerprint,
                route.InstanceConfigurationFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                instance.Fingerprint,
                route.InstanceFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                instance.EndpointFingerprint,
                route.EndpointFingerprint,
                StringComparison.Ordinal)
            || instance.Protocol != route.Protocol
            || instance.ProtocolProfileVersion != route.ProtocolProfileVersion)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.InstanceIdentityMismatch);
        }
        if (!registry.TryGetModel(
                route.InstanceId,
                route.ModelInstanceId,
                out var model)
            || model == null
            || !string.Equals(
                model.Fingerprint,
                route.ModelDescriptorFingerprint,
                StringComparison.Ordinal))
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.ModelIdentityMismatch);
        }
        if (observedAt < request.RequestedAt)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RequestNotYetValid);
        }
        if (observedAt >= request.Deadline)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RequestExpired);
        }
        if (observedAt < route.SelectedAt)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RouteNotYetValid);
        }
        if (observedAt >= route.ExpiresAt)
        {
            return ProviderExecutionBindingResult.Reject(
                ProviderExecutionBindingAdmission.RouteExpired);
        }
        return ProviderExecutionBindingResult.Accept(
            new ProviderExecutionBinding(source, request, registry, route));
    }

    internal bool Matches(ProviderExecutionBinding? other)
        => other != null
            && string.Equals(
                BindingFingerprint,
                other.BindingFingerprint,
                StringComparison.Ordinal);

    internal static string RuntimeHash(CharacterRuntimeIdentity runtime)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        return ProviderRegistryCanonicalHash.Of(
            "D031/CHARACTER-RUNTIME-IDENTITY/1",
            runtime.RuntimeProfileId,
            runtime.RuntimeInstanceId,
            ProviderRegistryCanonicalHash.Number(runtime.Generation),
            runtime.OrchestratorSessionId,
            runtime.OwnerIdentity);
    }

    private static CharacterRuntimeIdentity CopyRuntime(
        CharacterRuntimeIdentity runtime)
        => new(
            runtime.RuntimeProfileId,
            runtime.RuntimeInstanceId,
            runtime.Generation,
            runtime.OrchestratorSessionId,
            runtime.OwnerIdentity);
}

internal sealed class ProviderExecutionOwnershipClaim
{
    internal ProviderExecutionOwnershipState State { get; }
    internal string BindingFingerprint { get; }
    internal string RequestFingerprint { get; }
    internal string RuntimeFingerprint { get; }
    internal DateTimeOffset ClaimedAt { get; }
    internal string ClaimId { get; }
    internal bool CanIssueRegistryTicket =>
        State == ProviderExecutionOwnershipState.RegistryOwned;
    internal bool AllowsLegacyExecution =>
        State == ProviderExecutionOwnershipState.LegacyOwned;
    internal bool AllowsLegacyFallbackAfterRegistryOwnership => false;
    internal bool AllowsStageAProviderCall => false;

    internal ProviderExecutionOwnershipClaim(
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipState state,
        DateTimeOffset claimedAt)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (!Enum.IsDefined(typeof(ProviderExecutionOwnershipState), state)
            || state == ProviderExecutionOwnershipState.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        State = state;
        BindingFingerprint = binding.BindingFingerprint;
        RequestFingerprint = binding.ExecutionFingerprint;
        RuntimeFingerprint = binding.RuntimeFingerprint;
        ClaimedAt = DecisionCoreContractGuard.Timestamp(
            claimedAt,
            nameof(claimedAt));
        ClaimId = "d031-owner-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/PROVIDER-OWNERSHIP-CLAIM/1",
                BindingFingerprint,
                ProviderRegistryCanonicalHash.Number((int)State),
                RuntimeFingerprint,
                ProviderRegistryCanonicalHash.Timestamp(ClaimedAt))
                .Substring(0, 32);
    }
}

internal sealed class ProviderExecutionOwnershipResult
{
    internal ProviderExecutionOwnershipAdmission Admission { get; }
    internal ProviderExecutionOwnershipClaim? Claim { get; }
    internal bool Accepted =>
        Admission
            == ProviderExecutionOwnershipAdmission
                .AssignedForContractReplay;

    private ProviderExecutionOwnershipResult(
        ProviderExecutionOwnershipAdmission admission,
        ProviderExecutionOwnershipClaim? claim)
    {
        if (!Enum.IsDefined(typeof(ProviderExecutionOwnershipAdmission), admission)
            || admission == ProviderExecutionOwnershipAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderExecutionOwnershipAdmission
                    .AssignedForContractReplay)
            != (claim != null))
        {
            throw new ArgumentException(
                "Only accepted ownership can carry a claim.",
                nameof(claim));
        }
        Admission = admission;
        Claim = claim;
    }

    internal static ProviderExecutionOwnershipResult Accept(
        ProviderExecutionOwnershipClaim claim)
        => new(
            ProviderExecutionOwnershipAdmission.AssignedForContractReplay,
            claim ?? throw new ArgumentNullException(nameof(claim)));

    internal static ProviderExecutionOwnershipResult Reject(
        ProviderExecutionOwnershipAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderExecutionAuthorizationTicket
{
    internal static readonly TimeSpan MaximumLifetime =
        TimeSpan.FromSeconds(1);

    private int _consumed;

    internal string TicketId { get; }
    internal string ClaimId { get; }
    internal string BindingFingerprint { get; }
    internal string SourceRequestFingerprint { get; }
    internal string ExecutionFingerprint { get; }
    internal string PromptFingerprint { get; }
    internal LivingPreparedCompletionDestination Destination { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal string RouteFingerprint { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string RuntimeFingerprint { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal DateTimeOffset RequestDeadline { get; }
    internal bool IsConsumed => Volatile.Read(ref _consumed) != 0;
    internal bool AllowsRetry => false;
    internal bool AllowsProviderFallback => false;
    internal bool AllowsStageAProviderCall => false;

    internal ProviderExecutionAuthorizationTicket(
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipClaim claim,
        DateTimeOffset issuedAt)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (claim == null)
            throw new ArgumentNullException(nameof(claim));
        BindingFingerprint = binding.BindingFingerprint;
        SourceRequestFingerprint = binding.SourceRequestFingerprint;
        ExecutionFingerprint = binding.ExecutionFingerprint;
        PromptFingerprint = binding.PromptFingerprint;
        Destination = binding.Destination;
        RegistryGeneration = binding.RegistryGeneration;
        RegistryFingerprint = binding.RegistryFingerprint;
        ConfigurationGeneration = binding.ConfigurationGeneration;
        InstanceId = binding.InstanceId;
        ModelInstanceId = binding.ModelInstanceId;
        Protocol = binding.Protocol;
        ProtocolProfileVersion = binding.ProtocolProfileVersion;
        RouteFingerprint = binding.RouteFingerprint;
        RuntimeIdentity = new CharacterRuntimeIdentity(
            binding.RuntimeIdentity.RuntimeProfileId,
            binding.RuntimeIdentity.RuntimeInstanceId,
            binding.RuntimeIdentity.Generation,
            binding.RuntimeIdentity.OrchestratorSessionId,
            binding.RuntimeIdentity.OwnerIdentity);
        RuntimeFingerprint = binding.RuntimeFingerprint;
        ClaimId = claim.ClaimId;
        IssuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = IssuedAt + MaximumLifetime;
        RequestDeadline = binding.EffectiveDeadline;
        TicketId = "d031-ticket-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/PROVIDER-EXECUTION-TICKET/1",
                ClaimId,
                BindingFingerprint,
                SourceRequestFingerprint,
                ExecutionFingerprint,
                PromptFingerprint,
                ProviderRegistryCanonicalHash.Number((int)Destination),
                ProviderRegistryCanonicalHash.Number(RegistryGeneration),
                RegistryFingerprint,
                ProviderRegistryCanonicalHash.Number(ConfigurationGeneration),
                InstanceId.Value,
                ModelInstanceId.Value,
                ProviderRegistryCanonicalHash.Number((int)Protocol),
                ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
                RouteFingerprint,
                RuntimeFingerprint,
                ProviderRegistryCanonicalHash.Timestamp(IssuedAt),
                ProviderRegistryCanonicalHash.Timestamp(RequestDeadline))
                .Substring(0, 32);
    }

    internal ProviderExecutionTicketConsumptionAdmission TryConsume(
        ProviderExecutionBinding binding,
        CharacterRuntimeIdentity activeRuntime,
        long activeRegistryGeneration,
        long activeConfigurationGeneration,
        DateTimeOffset now)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (activeRegistryGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeRegistryGeneration));
        if (activeConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeConfigurationGeneration));
        }
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        if (now < IssuedAt || now >= ExpiresAt || now >= RequestDeadline)
        {
            return ProviderExecutionTicketConsumptionAdmission.TicketExpired;
        }
        if (!RuntimeIdentity.Matches(activeRuntime))
        {
            return ProviderExecutionTicketConsumptionAdmission
                .RuntimeIdentityMismatch;
        }
        if (RegistryGeneration != activeRegistryGeneration)
        {
            return ProviderExecutionTicketConsumptionAdmission
                .RegistryGenerationMismatch;
        }
        if (ConfigurationGeneration != activeConfigurationGeneration)
        {
            return ProviderExecutionTicketConsumptionAdmission
                .ConfigurationGenerationMismatch;
        }
        if (!string.Equals(
                BindingFingerprint,
                binding.BindingFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                ExecutionFingerprint,
                binding.ExecutionFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                RouteFingerprint,
                binding.RouteFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                PromptFingerprint,
                binding.PromptFingerprint,
                StringComparison.OrdinalIgnoreCase)
            || Destination != binding.Destination)
        {
            return ProviderExecutionTicketConsumptionAdmission
                .BindingIdentityMismatch;
        }
        return Interlocked.Exchange(ref _consumed, 1) == 0
            ? ProviderExecutionTicketConsumptionAdmission
                .ConsumedForContractReplay
            : ProviderExecutionTicketConsumptionAdmission
                .TicketAlreadyConsumed;
    }
}

internal sealed class ProviderExecutionAuthorizationResult
{
    internal ProviderExecutionAuthorizationAdmission Admission { get; }
    internal ProviderExecutionAuthorizationTicket? Ticket { get; }
    internal bool Accepted =>
        Admission
            == ProviderExecutionAuthorizationAdmission
                .AuthorizedForContractReplay;

    private ProviderExecutionAuthorizationResult(
        ProviderExecutionAuthorizationAdmission admission,
        ProviderExecutionAuthorizationTicket? ticket)
    {
        if (!Enum.IsDefined(typeof(ProviderExecutionAuthorizationAdmission), admission)
            || admission == ProviderExecutionAuthorizationAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderExecutionAuthorizationAdmission
                    .AuthorizedForContractReplay)
            != (ticket != null))
        {
            throw new ArgumentException(
                "Only accepted authorization can carry a ticket.",
                nameof(ticket));
        }
        Admission = admission;
        Ticket = ticket;
    }

    internal static ProviderExecutionAuthorizationResult Accept(
        ProviderExecutionAuthorizationTicket ticket)
        => new(
            ProviderExecutionAuthorizationAdmission
                .AuthorizedForContractReplay,
            ticket ?? throw new ArgumentNullException(nameof(ticket)));

    internal static ProviderExecutionAuthorizationResult Reject(
        ProviderExecutionAuthorizationAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderExecutionCompletionIdentity
{
    internal string CompletionId { get; }
    internal string TicketId { get; }
    internal string BindingFingerprint { get; }
    internal string ExecutionFingerprint { get; }
    internal string RouteFingerprint { get; }
    internal string PromptFingerprint { get; }
    internal LivingPreparedCompletionDestination Destination { get; }
    internal long RegistryGeneration { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string RuntimeFingerprint { get; }
    internal string NormalizedCompletionFingerprint { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal bool AllowsDeliveryInStageA => false;

    internal ProviderExecutionCompletionIdentity(
        ProviderExecutionAuthorizationTicket ticket,
        CharacterRuntimeIdentity runtimeIdentity,
        long registryGeneration,
        long configurationGeneration,
        string normalizedCompletionFingerprint,
        DateTimeOffset completedAt)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        if (registryGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(registryGeneration));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        }
        TicketId = ticket.TicketId;
        BindingFingerprint = ticket.BindingFingerprint;
        ExecutionFingerprint = ticket.ExecutionFingerprint;
        RouteFingerprint = ticket.RouteFingerprint;
        PromptFingerprint = ticket.PromptFingerprint;
        Destination = ticket.Destination;
        RegistryGeneration = registryGeneration;
        ConfigurationGeneration = configurationGeneration;
        InstanceId = ticket.InstanceId;
        ModelInstanceId = ticket.ModelInstanceId;
        Protocol = ticket.Protocol;
        ProtocolProfileVersion = ticket.ProtocolProfileVersion;
        RuntimeIdentity = new CharacterRuntimeIdentity(
            runtimeIdentity.RuntimeProfileId,
            runtimeIdentity.RuntimeInstanceId,
            runtimeIdentity.Generation,
            runtimeIdentity.OrchestratorSessionId,
            runtimeIdentity.OwnerIdentity);
        RuntimeFingerprint = ProviderExecutionBinding.RuntimeHash(RuntimeIdentity);
        NormalizedCompletionFingerprint =
            ProviderRegistryContractGuard.Sha256(
                normalizedCompletionFingerprint,
                nameof(normalizedCompletionFingerprint));
        CompletedAt = DecisionCoreContractGuard.Timestamp(
            completedAt,
            nameof(completedAt));
        CompletionId = "d031-completion-"
            + ProviderRegistryCanonicalHash.Of(
                "D031/PROVIDER-COMPLETION-IDENTITY/1",
                TicketId,
                BindingFingerprint,
                ExecutionFingerprint,
                RouteFingerprint,
                PromptFingerprint,
                ProviderRegistryCanonicalHash.Number((int)Destination),
                ProviderRegistryCanonicalHash.Number(RegistryGeneration),
                ProviderRegistryCanonicalHash.Number(ConfigurationGeneration),
                InstanceId.Value,
                ModelInstanceId.Value,
                ProviderRegistryCanonicalHash.Number((int)Protocol),
                ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
                RuntimeFingerprint,
                NormalizedCompletionFingerprint,
                ProviderRegistryCanonicalHash.Timestamp(CompletedAt))
                .Substring(0, 32);
    }
}

internal sealed class ProviderExecutionCompletionResult
{
    internal ProviderExecutionCompletionAdmission Admission { get; }
    internal string? CompletionId { get; }
    internal bool Accepted =>
        Admission
            == ProviderExecutionCompletionAdmission
                .AcceptedForContractReplay;
    internal bool AllowsDelivery => false;

    internal ProviderExecutionCompletionResult(
        ProviderExecutionCompletionAdmission admission,
        string? completionId)
    {
        if (!Enum.IsDefined(typeof(ProviderExecutionCompletionAdmission), admission)
            || admission == ProviderExecutionCompletionAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == ProviderExecutionCompletionAdmission
                    .AcceptedForContractReplay)
            != !string.IsNullOrWhiteSpace(completionId))
        {
            throw new ArgumentException(
                "Only accepted completion admission carries an identity.",
                nameof(completionId));
        }
        Admission = admission;
        CompletionId = completionId;
    }
}

internal sealed class ProviderRuntimeStageAAuthorityCounters
{
    internal int PluginCalls => 0;
    internal int ConfigurationReads => 0;
    internal int CredentialReads => 0;
    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int PromptConstructions => 0;
    internal int MemoryReads => 0;
    internal int DecisionWrites => 0;
    internal int CapabilityOutputs => 0;
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal int CompletionDeliveries => 0;
    internal bool IsZero => true;
}
