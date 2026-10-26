using System;
using System.Collections.Generic;
using System.Globalization;

namespace LilithTextInjector;

internal enum D022SliceAOperationKind
{
    ClaimOwnership = 0,
    RegisterTicket = 1,
    ConsumeTicket = 2,
    RetireOwnership = 3
}

internal enum D022SliceAReplayDisposition
{
    LegacyOwned = 0,
    AssistOwned = 1,
    OwnershipImmutable = 2,
    OwnershipRetired = 3,
    TicketRegistered = 4,
    TicketAcceptedForContractReplay = 5,
    TicketRejected = 6,
    CapacityRejected = 7,
    IdentityRejected = 8,
    AlreadyRetired = 9
}

internal abstract class D022SliceAReplayOperation
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal D022SliceAOperationKind Kind { get; }

    protected D022SliceAReplayOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        D022SliceAOperationKind kind)
    {
        OperationId = D022ContractGuard.Identifier(
            operationId,
            nameof(operationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (!Enum.IsDefined(typeof(D022SliceAOperationKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Sequence = sequence;
        Kind = kind;
    }
}

internal sealed class D022ClaimOwnershipOperation
    : D022SliceAReplayOperation
{
    internal bool AssistEnabled { get; }
    internal MotionOpportunityIdentity Opportunity { get; }
    internal MotionCandidateBinding? Binding { get; }

    internal D022ClaimOwnershipOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        bool assistEnabled,
        MotionOpportunityIdentity opportunity,
        MotionCandidateBinding? binding)
        : base(
            operationId,
            sequence,
            occurredAt,
            D022SliceAOperationKind.ClaimOwnership)
    {
        Opportunity = opportunity
            ?? throw new ArgumentNullException(nameof(opportunity));
        if (OccurredAt < opportunity.ObservedAt
            || OccurredAt >= opportunity.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt));
        }
        if (assistEnabled)
        {
            Binding = binding
                ?? throw new ArgumentNullException(nameof(binding));
            if (!string.Equals(
                    Binding.Opportunity.OpportunityId,
                    opportunity.OpportunityId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Binding must belong to the claimed opportunity.",
                    nameof(binding));
            }
        }
        else if (binding != null)
        {
            throw new ArgumentException(
                "Off-equivalence claim cannot create a binding.",
                nameof(binding));
        }

        AssistEnabled = assistEnabled;
    }
}

internal sealed class D022RegisterTicketOperation
    : D022SliceAReplayOperation
{
    internal AssistAuthorizationTicket Ticket { get; }

    internal D022RegisterTicketOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        AssistAuthorizationTicket ticket)
        : base(
            operationId,
            sequence,
            occurredAt,
            D022SliceAOperationKind.RegisterTicket)
    {
        Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
        if (OccurredAt < ticket.IssuedAt
            || OccurredAt >= ticket.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(occurredAt));
        }
    }
}

internal sealed class D022ConsumeTicketOperation
    : D022SliceAReplayOperation
{
    internal D022TicketConsumptionContext Context { get; }

    internal D022ConsumeTicketOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        D022TicketConsumptionContext context)
        : base(
            operationId,
            sequence,
            occurredAt,
            D022SliceAOperationKind.ConsumeTicket)
    {
        Context = context
            ?? throw new ArgumentNullException(nameof(context));
        if (OccurredAt != context.ConsumedAt)
        {
            throw new ArgumentException(
                "Operation and ticket context must use the same time.",
                nameof(context));
        }
    }
}

internal sealed class D022RetireOwnershipOperation
    : D022SliceAReplayOperation
{
    internal string OpportunityId { get; }

    internal D022RetireOwnershipOperation(
        string operationId,
        long sequence,
        DateTimeOffset occurredAt,
        string opportunityId)
        : base(
            operationId,
            sequence,
            occurredAt,
            D022SliceAOperationKind.RetireOwnership)
    {
        OpportunityId = D022ContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
    }
}

internal sealed class D022OwnershipRecord
{
    internal string OpportunityId { get; }
    internal D022OpportunityOwnership Ownership { get; }
    internal MotionCandidateBinding? Binding { get; }
    internal DateTimeOffset RecordedAt { get; }

    internal D022OwnershipRecord(
        string opportunityId,
        D022OpportunityOwnership ownership,
        MotionCandidateBinding? binding,
        DateTimeOffset recordedAt)
    {
        OpportunityId = D022ContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
        if (!Enum.IsDefined(typeof(D022OpportunityOwnership), ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));
        if (ownership == D022OpportunityOwnership.AssistOwned
            && binding == null)
        {
            throw new ArgumentNullException(nameof(binding));
        }
        if (ownership == D022OpportunityOwnership.LegacyOwned
            && binding != null)
        {
            throw new ArgumentException(
                "Legacy ownership cannot retain an Assist binding.",
                nameof(binding));
        }

        Ownership = ownership;
        Binding = binding;
        RecordedAt = DecisionCoreContractGuard.Timestamp(
            recordedAt,
            nameof(recordedAt));
    }
}

internal sealed class D022SliceAReplayStep
{
    internal string OperationId { get; }
    internal long Sequence { get; }
    internal D022SliceAReplayDisposition Disposition { get; }
    internal D022OpportunityOwnership? Ownership { get; }
    internal D022TicketConsumptionStatus? TicketStatus { get; }
    internal D022LegacyContinuityStamp LegacyContinuity { get; }
    internal D022SliceASideEffectCounters SideEffects =>
        D022SliceASideEffectCounters.Zero;
    internal D022SliceAExecutionPermission ExecutionPermission =>
        D022SliceAExecutionPermission.DeniedByContractOnlyBoundary;

    internal D022SliceAReplayStep(
        D022SliceAReplayOperation operation,
        D022SliceAReplayDisposition disposition,
        D022OpportunityOwnership? ownership,
        D022TicketConsumptionStatus? ticketStatus,
        D022LegacyContinuityStamp legacyContinuity)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (!Enum.IsDefined(
                typeof(D022SliceAReplayDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (ownership.HasValue
            && !Enum.IsDefined(
                typeof(D022OpportunityOwnership),
                ownership.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }
        if (ticketStatus.HasValue
            && !Enum.IsDefined(
                typeof(D022TicketConsumptionStatus),
                ticketStatus.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(ticketStatus));
        }

        OperationId = operation.OperationId;
        Sequence = operation.Sequence;
        Disposition = disposition;
        Ownership = ownership;
        TicketStatus = ticketStatus;
        LegacyContinuity = legacyContinuity
            ?? throw new ArgumentNullException(nameof(legacyContinuity));
    }

    internal string DeterministicFingerprint
        => OperationId
            + ":"
            + Sequence.ToString(CultureInfo.InvariantCulture)
            + ":"
            + ((int)Disposition).ToString(CultureInfo.InvariantCulture)
            + ":"
            + (Ownership.HasValue
                ? ((int)Ownership.Value).ToString(
                    CultureInfo.InvariantCulture)
                : "-")
            + ":"
            + (TicketStatus.HasValue
                ? ((int)TicketStatus.Value).ToString(
                    CultureInfo.InvariantCulture)
                : "-");
}

internal sealed class D022SliceAReplayResult
{
    internal IReadOnlyList<D022SliceAReplayStep> Steps { get; }
    internal IReadOnlyList<D022OwnershipRecord> OwnershipRecords { get; }
    internal bool TicketRegistered { get; }
    internal bool TicketConsumed { get; }
    internal D022LegacyContinuityStamp LegacyContinuity { get; }
    internal D022SliceASideEffectCounters SideEffects =>
        D022SliceASideEffectCounters.Zero;
    internal D022SliceAExecutionPermission ExecutionPermission =>
        D022SliceAExecutionPermission.DeniedByContractOnlyBoundary;
    internal string DeterministicFingerprint { get; }

    internal D022SliceAReplayResult(
        IReadOnlyList<D022SliceAReplayStep> steps,
        IReadOnlyList<D022OwnershipRecord> ownershipRecords,
        bool ticketRegistered,
        bool ticketConsumed,
        D022LegacyContinuityStamp legacyContinuity)
    {
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));
        if (ownershipRecords == null)
            throw new ArgumentNullException(nameof(ownershipRecords));
        var copiedSteps = new D022SliceAReplayStep[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            copiedSteps[index] = steps[index]
                ?? throw new ArgumentException(
                    "Replay steps cannot contain null.",
                    nameof(steps));
        }
        var copiedRecords =
            new D022OwnershipRecord[ownershipRecords.Count];
        for (var index = 0; index < ownershipRecords.Count; index++)
        {
            copiedRecords[index] = ownershipRecords[index]
                ?? throw new ArgumentException(
                    "Ownership records cannot contain null.",
                    nameof(ownershipRecords));
        }

        Steps = Array.AsReadOnly(copiedSteps);
        OwnershipRecords = Array.AsReadOnly(copiedRecords);
        TicketRegistered = ticketRegistered;
        TicketConsumed = ticketConsumed;
        LegacyContinuity = legacyContinuity
            ?? throw new ArgumentNullException(nameof(legacyContinuity));

        var fingerprints = new string[copiedSteps.Length];
        for (var index = 0; index < copiedSteps.Length; index++)
            fingerprints[index] = copiedSteps[index].DeterministicFingerprint;
        DeterministicFingerprint = string.Join("|", fingerprints);
    }
}

internal static class D022SliceAReplay
{
    internal static D022SliceAReplayResult Run(
        D022LegacyContinuityStamp legacyContinuity,
        IReadOnlyList<D022SliceAReplayOperation> operations)
    {
        if (legacyContinuity == null)
            throw new ArgumentNullException(nameof(legacyContinuity));
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));

        var steps = new List<D022SliceAReplayStep>(operations.Count);
        var records = new Dictionary<string, D022OwnershipRecord>(
            StringComparer.Ordinal);
        string? activeAssistOpportunityId = null;
        AssistAuthorizationTicket? ticket = null;
        var ticketConsumed = false;
        long lastSequence = 0;
        DateTimeOffset? lastOccurredAt = null;

        foreach (var operation in operations)
        {
            if (operation == null)
            {
                throw new ArgumentException(
                    "Replay operations cannot contain null.",
                    nameof(operations));
            }
            if (operation.Sequence <= lastSequence)
            {
                throw new ArgumentException(
                    "Replay operation sequence must increase.",
                    nameof(operations));
            }
            if (lastOccurredAt.HasValue
                && operation.OccurredAt < lastOccurredAt.Value)
            {
                throw new ArgumentException(
                    "Replay time cannot move backwards.",
                    nameof(operations));
            }

            D022SliceAReplayStep step;
            switch (operation)
            {
                case D022ClaimOwnershipOperation claim:
                    step = Claim(
                        claim,
                        legacyContinuity,
                        records,
                        ref activeAssistOpportunityId);
                    break;
                case D022RegisterTicketOperation register:
                    step = RegisterTicket(
                        register,
                        legacyContinuity,
                        records,
                        activeAssistOpportunityId,
                        ref ticket);
                    break;
                case D022ConsumeTicketOperation consume:
                    step = ConsumeTicket(
                        consume,
                        legacyContinuity,
                        ticket,
                        ref ticketConsumed);
                    break;
                case D022RetireOwnershipOperation retire:
                    step = Retire(
                        retire,
                        legacyContinuity,
                        records,
                        ref activeAssistOpportunityId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operations),
                        "Unknown replay operation.");
            }

            steps.Add(step);
            lastSequence = operation.Sequence;
            lastOccurredAt = operation.OccurredAt;
        }

        var orderedRecords = new List<D022OwnershipRecord>(records.Values);
        orderedRecords.Sort((left, right) => string.CompareOrdinal(
            left.OpportunityId,
            right.OpportunityId));
        return new D022SliceAReplayResult(
            steps,
            orderedRecords,
            ticket != null,
            ticketConsumed,
            legacyContinuity);
    }

    private static D022SliceAReplayStep Claim(
        D022ClaimOwnershipOperation operation,
        D022LegacyContinuityStamp legacyContinuity,
        IDictionary<string, D022OwnershipRecord> records,
        ref string? activeAssistOpportunityId)
    {
        if (records.TryGetValue(
                operation.Opportunity.OpportunityId,
                out var existing))
        {
            var disposition = existing.Ownership
                == D022OpportunityOwnership.Retired
                ? D022SliceAReplayDisposition.AlreadyRetired
                : D022SliceAReplayDisposition.OwnershipImmutable;
            return Step(
                operation,
                disposition,
                existing.Ownership,
                null,
                legacyContinuity);
        }

        if (!operation.AssistEnabled)
        {
            records.Add(
                operation.Opportunity.OpportunityId,
                new D022OwnershipRecord(
                    operation.Opportunity.OpportunityId,
                    D022OpportunityOwnership.LegacyOwned,
                    binding: null,
                    operation.OccurredAt));
            return Step(
                operation,
                D022SliceAReplayDisposition.LegacyOwned,
                D022OpportunityOwnership.LegacyOwned,
                null,
                legacyContinuity);
        }

        if (activeAssistOpportunityId != null)
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.CapacityRejected,
                null,
                null,
                legacyContinuity);
        }

        var binding = operation.Binding
            ?? throw new InvalidOperationException(
                "Assist ownership requires a binding.");
        activeAssistOpportunityId = operation.Opportunity.OpportunityId;
        records.Add(
            operation.Opportunity.OpportunityId,
            new D022OwnershipRecord(
                operation.Opportunity.OpportunityId,
                D022OpportunityOwnership.AssistOwned,
                binding,
                operation.OccurredAt));
        return Step(
            operation,
            D022SliceAReplayDisposition.AssistOwned,
            D022OpportunityOwnership.AssistOwned,
            null,
            legacyContinuity);
    }

    private static D022SliceAReplayStep RegisterTicket(
        D022RegisterTicketOperation operation,
        D022LegacyContinuityStamp legacyContinuity,
        IReadOnlyDictionary<string, D022OwnershipRecord> records,
        string? activeAssistOpportunityId,
        ref AssistAuthorizationTicket? ticket)
    {
        if (ticket != null)
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.CapacityRejected,
                null,
                null,
                legacyContinuity);
        }
        if (activeAssistOpportunityId == null
            || !string.Equals(
                activeAssistOpportunityId,
                operation.Ticket.OpportunityId,
                StringComparison.Ordinal)
            || !records.TryGetValue(
                activeAssistOpportunityId,
                out var ownership)
            || ownership.Ownership != D022OpportunityOwnership.AssistOwned
            || ownership.Binding == null
            || !string.Equals(
                ownership.Binding.BindingId,
                operation.Ticket.BindingId,
                StringComparison.Ordinal))
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.IdentityRejected,
                null,
                null,
                legacyContinuity);
        }

        ticket = operation.Ticket;
        return Step(
            operation,
            D022SliceAReplayDisposition.TicketRegistered,
            D022OpportunityOwnership.AssistOwned,
            null,
            legacyContinuity);
    }

    private static D022SliceAReplayStep ConsumeTicket(
        D022ConsumeTicketOperation operation,
        D022LegacyContinuityStamp legacyContinuity,
        AssistAuthorizationTicket? ticket,
        ref bool ticketConsumed)
    {
        if (ticket == null)
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.TicketRejected,
                null,
                D022TicketConsumptionStatus.TicketMismatch,
                legacyContinuity);
        }

        var status = D022TicketValidator.Validate(
            ticket,
            operation.Context,
            ticketConsumed);
        if (status
            == D022TicketConsumptionStatus.AcceptedForContractReplay)
        {
            ticketConsumed = true;
        }
        return Step(
            operation,
            status == D022TicketConsumptionStatus.AcceptedForContractReplay
                ? D022SliceAReplayDisposition
                    .TicketAcceptedForContractReplay
                : D022SliceAReplayDisposition.TicketRejected,
            D022OpportunityOwnership.AssistOwned,
            status,
            legacyContinuity);
    }

    private static D022SliceAReplayStep Retire(
        D022RetireOwnershipOperation operation,
        D022LegacyContinuityStamp legacyContinuity,
        IDictionary<string, D022OwnershipRecord> records,
        ref string? activeAssistOpportunityId)
    {
        if (!records.TryGetValue(operation.OpportunityId, out var existing))
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.IdentityRejected,
                null,
                null,
                legacyContinuity);
        }
        if (existing.Ownership == D022OpportunityOwnership.Retired)
        {
            return Step(
                operation,
                D022SliceAReplayDisposition.AlreadyRetired,
                D022OpportunityOwnership.Retired,
                null,
                legacyContinuity);
        }

        records[operation.OpportunityId] = new D022OwnershipRecord(
            operation.OpportunityId,
            D022OpportunityOwnership.Retired,
            existing.Binding,
            operation.OccurredAt);
        if (string.Equals(
                activeAssistOpportunityId,
                operation.OpportunityId,
                StringComparison.Ordinal))
        {
            activeAssistOpportunityId = null;
        }
        return Step(
            operation,
            D022SliceAReplayDisposition.OwnershipRetired,
            D022OpportunityOwnership.Retired,
            null,
            legacyContinuity);
    }

    private static D022SliceAReplayStep Step(
        D022SliceAReplayOperation operation,
        D022SliceAReplayDisposition disposition,
        D022OpportunityOwnership? ownership,
        D022TicketConsumptionStatus? ticketStatus,
        D022LegacyContinuityStamp legacyContinuity)
        => new(
            operation,
            disposition,
            ownership,
            ticketStatus,
            legacyContinuity);
}
