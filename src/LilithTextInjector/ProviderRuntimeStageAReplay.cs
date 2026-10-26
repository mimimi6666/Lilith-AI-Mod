using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class ProviderExecutionOwnershipLedger
{
    private readonly Dictionary<string, ProviderExecutionOwnershipClaim>
        _claims = new(StringComparer.Ordinal);

    internal ProviderExecutionOwnershipResult TryAssign(
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipState state,
        DateTimeOffset claimedAt)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        claimedAt = DecisionCoreContractGuard.Timestamp(
            claimedAt,
            nameof(claimedAt));
        if (claimedAt < binding.RequestCreatedAt
            || claimedAt < binding.RouteSelectedAt
            || claimedAt >= binding.EffectiveDeadline)
        {
            return ProviderExecutionOwnershipResult.Reject(
                ProviderExecutionOwnershipAdmission.BindingExpired);
        }
        if (_claims.TryGetValue(binding.ExecutionFingerprint, out var existing))
        {
            return ProviderExecutionOwnershipResult.Reject(
                existing.State == state
                    && string.Equals(
                        existing.BindingFingerprint,
                        binding.BindingFingerprint,
                        StringComparison.Ordinal)
                    ? ProviderExecutionOwnershipAdmission.DuplicateOwnership
                    : ProviderExecutionOwnershipAdmission.OwnershipConflict);
        }
        var claim = new ProviderExecutionOwnershipClaim(
            binding,
            state,
            claimedAt);
        _claims.Add(binding.ExecutionFingerprint, claim);
        return ProviderExecutionOwnershipResult.Accept(claim);
    }
}

internal sealed class ProviderExecutionAuthorizationLedger
{
    private readonly HashSet<string> _authorizedBindings =
        new(StringComparer.Ordinal);

    internal ProviderExecutionAuthorizationResult TryIssue(
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipClaim? claim,
        DateTimeOffset issuedAt)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        issuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        if (claim == null)
        {
            return ProviderExecutionAuthorizationResult.Reject(
                ProviderExecutionAuthorizationAdmission.OwnershipMissing);
        }
        if (claim.State != ProviderExecutionOwnershipState.RegistryOwned)
        {
            return ProviderExecutionAuthorizationResult.Reject(
                ProviderExecutionAuthorizationAdmission
                    .OwnershipNotRegistryOwned);
        }
        if (!string.Equals(
                claim.BindingFingerprint,
                binding.BindingFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                claim.RuntimeFingerprint,
                binding.RuntimeFingerprint,
                StringComparison.Ordinal))
        {
            return ProviderExecutionAuthorizationResult.Reject(
                ProviderExecutionAuthorizationAdmission
                    .OwnershipIdentityMismatch);
        }
        if (issuedAt < claim.ClaimedAt
            || issuedAt < binding.RequestCreatedAt
            || issuedAt < binding.RouteSelectedAt
            || issuedAt >= binding.EffectiveDeadline)
        {
            return ProviderExecutionAuthorizationResult.Reject(
                ProviderExecutionAuthorizationAdmission.RequestExpired);
        }
        if (!_authorizedBindings.Add(binding.BindingFingerprint))
        {
            return ProviderExecutionAuthorizationResult.Reject(
                ProviderExecutionAuthorizationAdmission
                    .DuplicateAuthorization);
        }
        return ProviderExecutionAuthorizationResult.Accept(
            new ProviderExecutionAuthorizationTicket(
                binding,
                claim,
                issuedAt));
    }
}

internal sealed class ProviderExecutionCompletionLedger
{
    private readonly HashSet<string> _completionIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedTickets =
        new(StringComparer.Ordinal);

    internal ProviderExecutionCompletionResult TryAdmit(
        ProviderExecutionAuthorizationTicket ticket,
        ProviderExecutionBinding binding,
        ProviderExecutionCompletionIdentity completion,
        DateTimeOffset observedAt)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        observedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (!ticket.IsConsumed)
        {
            return Reject(
                ProviderExecutionCompletionAdmission.TicketNotConsumed);
        }
        if (!string.Equals(
                completion.TicketId,
                ticket.TicketId,
                StringComparison.Ordinal))
        {
            return Reject(
                ProviderExecutionCompletionAdmission.TicketIdentityMismatch);
        }
        if (!string.Equals(
                completion.BindingFingerprint,
                binding.BindingFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.ExecutionFingerprint,
                binding.ExecutionFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.RouteFingerprint,
                binding.RouteFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.PromptFingerprint,
                binding.PromptFingerprint,
                StringComparison.OrdinalIgnoreCase)
            || completion.Destination != binding.Destination
            || !completion.InstanceId.Equals(binding.InstanceId)
            || !completion.ModelInstanceId.Equals(binding.ModelInstanceId)
            || completion.Protocol != binding.Protocol
            || completion.ProtocolProfileVersion
                != binding.ProtocolProfileVersion)
        {
            return Reject(
                ProviderExecutionCompletionAdmission.BindingIdentityMismatch);
        }
        if (!completion.RuntimeIdentity.Matches(binding.RuntimeIdentity))
        {
            return Reject(
                ProviderExecutionCompletionAdmission.RuntimeIdentityMismatch);
        }
        if (completion.RegistryGeneration != binding.RegistryGeneration)
        {
            return Reject(
                ProviderExecutionCompletionAdmission
                    .RegistryGenerationMismatch);
        }
        if (completion.ConfigurationGeneration
            != binding.ConfigurationGeneration)
        {
            return Reject(
                ProviderExecutionCompletionAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (completion.CompletedAt < ticket.IssuedAt
            || observedAt < completion.CompletedAt)
        {
            return Reject(
                ProviderExecutionCompletionAdmission
                    .CompletionBeforeAuthorization);
        }
        if (completion.CompletedAt >= binding.EffectiveDeadline
            || observedAt >= binding.EffectiveDeadline)
        {
            return Reject(
                ProviderExecutionCompletionAdmission.CompletionExpired);
        }
        if (_completionIds.Contains(completion.CompletionId))
        {
            return Reject(
                ProviderExecutionCompletionAdmission.DuplicateCompletion);
        }
        if (_completedTickets.Contains(ticket.TicketId))
        {
            return Reject(
                ProviderExecutionCompletionAdmission
                    .DuplicateTicketCompletion);
        }
        _completionIds.Add(completion.CompletionId);
        _completedTickets.Add(ticket.TicketId);
        return new ProviderExecutionCompletionResult(
            ProviderExecutionCompletionAdmission
                .AcceptedForContractReplay,
            completion.CompletionId);
    }

    private static ProviderExecutionCompletionResult Reject(
        ProviderExecutionCompletionAdmission admission)
        => new(admission, null);
}

internal sealed class ProviderExecutionStageAReplayInput
{
    internal ProviderExecutionBinding Binding { get; }
    internal ProviderExecutionOwnershipState OwnershipState { get; }
    internal CharacterRuntimeIdentity ActiveRuntime { get; }
    internal long ActiveRegistryGeneration { get; }
    internal long ActiveConfigurationGeneration { get; }
    internal DateTimeOffset OwnershipAt { get; }
    internal DateTimeOffset AuthorizationAt { get; }
    internal DateTimeOffset ConsumptionAt { get; }
    internal DateTimeOffset CompletionAt { get; }
    internal DateTimeOffset CompletionObservedAt { get; }
    internal string NormalizedCompletionFingerprint { get; }

    internal ProviderExecutionStageAReplayInput(
        ProviderExecutionBinding binding,
        ProviderExecutionOwnershipState ownershipState,
        CharacterRuntimeIdentity activeRuntime,
        long activeRegistryGeneration,
        long activeConfigurationGeneration,
        DateTimeOffset ownershipAt,
        DateTimeOffset authorizationAt,
        DateTimeOffset consumptionAt,
        DateTimeOffset completionAt,
        DateTimeOffset completionObservedAt,
        string normalizedCompletionFingerprint)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        if (!Enum.IsDefined(typeof(ProviderExecutionOwnershipState), ownershipState)
            || ownershipState == ProviderExecutionOwnershipState.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(ownershipState));
        }
        OwnershipState = ownershipState;
        ActiveRuntime = activeRuntime
            ?? throw new ArgumentNullException(nameof(activeRuntime));
        if (activeRegistryGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeRegistryGeneration));
        if (activeConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeConfigurationGeneration));
        }
        ActiveRegistryGeneration = activeRegistryGeneration;
        ActiveConfigurationGeneration = activeConfigurationGeneration;
        OwnershipAt = DecisionCoreContractGuard.Timestamp(
            ownershipAt,
            nameof(ownershipAt));
        AuthorizationAt = DecisionCoreContractGuard.Timestamp(
            authorizationAt,
            nameof(authorizationAt));
        ConsumptionAt = DecisionCoreContractGuard.Timestamp(
            consumptionAt,
            nameof(consumptionAt));
        CompletionAt = DecisionCoreContractGuard.Timestamp(
            completionAt,
            nameof(completionAt));
        CompletionObservedAt = DecisionCoreContractGuard.Timestamp(
            completionObservedAt,
            nameof(completionObservedAt));
        NormalizedCompletionFingerprint =
            ProviderRegistryContractGuard.Sha256(
                normalizedCompletionFingerprint,
                nameof(normalizedCompletionFingerprint));
    }
}

internal sealed class ProviderExecutionStageAReplayResult
{
    internal ProviderExecutionOwnershipAdmission OwnershipAdmission { get; }
    internal ProviderExecutionAuthorizationAdmission AuthorizationAdmission { get; }
    internal ProviderExecutionTicketConsumptionAdmission? ConsumptionAdmission { get; }
    internal ProviderExecutionCompletionAdmission? CompletionAdmission { get; }
    internal string? ClaimId { get; }
    internal string? TicketId { get; }
    internal string? CompletionId { get; }
    internal string DeterministicFingerprint { get; }
    internal ProviderRuntimeStageAAuthorityCounters AuthorityCounters { get; } =
        new();

    internal ProviderExecutionStageAReplayResult(
        ProviderExecutionOwnershipAdmission ownershipAdmission,
        ProviderExecutionAuthorizationAdmission authorizationAdmission,
        ProviderExecutionTicketConsumptionAdmission? consumptionAdmission,
        ProviderExecutionCompletionAdmission? completionAdmission,
        string? claimId,
        string? ticketId,
        string? completionId)
    {
        OwnershipAdmission = ownershipAdmission;
        AuthorizationAdmission = authorizationAdmission;
        ConsumptionAdmission = consumptionAdmission;
        CompletionAdmission = completionAdmission;
        ClaimId = claimId;
        TicketId = ticketId;
        CompletionId = completionId;
        DeterministicFingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/PROVIDER-STAGE-A-REPLAY/1",
            ProviderRegistryCanonicalHash.Number((int)OwnershipAdmission),
            ProviderRegistryCanonicalHash.Number((int)AuthorizationAdmission),
            ConsumptionAdmission.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)ConsumptionAdmission.Value)
                : string.Empty,
            CompletionAdmission.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)CompletionAdmission.Value)
                : string.Empty,
            ClaimId ?? string.Empty,
            TicketId ?? string.Empty,
            CompletionId ?? string.Empty);
    }
}

internal static class ProviderExecutionStageAReplay
{
    internal static ProviderExecutionStageAReplayResult Run(
        ProviderExecutionStageAReplayInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        var ownership = new ProviderExecutionOwnershipLedger().TryAssign(
            input.Binding,
            input.OwnershipState,
            input.OwnershipAt);
        var authorization = new ProviderExecutionAuthorizationLedger().TryIssue(
            input.Binding,
            ownership.Claim,
            input.AuthorizationAt);
        if (!authorization.Accepted || authorization.Ticket == null)
        {
            return new ProviderExecutionStageAReplayResult(
                ownership.Admission,
                authorization.Admission,
                null,
                null,
                ownership.Claim?.ClaimId,
                null,
                null);
        }

        var ticket = authorization.Ticket;
        var consumption = ticket.TryConsume(
            input.Binding,
            input.ActiveRuntime,
            input.ActiveRegistryGeneration,
            input.ActiveConfigurationGeneration,
            input.ConsumptionAt);
        if (consumption
            != ProviderExecutionTicketConsumptionAdmission
                .ConsumedForContractReplay)
        {
            return new ProviderExecutionStageAReplayResult(
                ownership.Admission,
                authorization.Admission,
                consumption,
                null,
                ownership.Claim?.ClaimId,
                ticket.TicketId,
                null);
        }

        var completion = new ProviderExecutionCompletionIdentity(
            ticket,
            input.ActiveRuntime,
            input.ActiveRegistryGeneration,
            input.ActiveConfigurationGeneration,
            input.NormalizedCompletionFingerprint,
            input.CompletionAt);
        var completionResult = new ProviderExecutionCompletionLedger()
            .TryAdmit(
                ticket,
                input.Binding,
                completion,
                input.CompletionObservedAt);
        return new ProviderExecutionStageAReplayResult(
            ownership.Admission,
            authorization.Admission,
            consumption,
            completionResult.Admission,
            ownership.Claim?.ClaimId,
            ticket.TicketId,
            completionResult.CompletionId);
    }
}
