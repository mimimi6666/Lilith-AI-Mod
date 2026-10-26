using System;

namespace LilithTextInjector;

internal enum D022OpportunityOwnership
{
    LegacyOwned = 0,
    AssistOwned = 1,
    Retired = 2
}

internal enum D022TicketTransactionBoundary
{
    SameMainThreadTransaction = 0,
    CrossesFrameSchedulerOrAsyncBoundary = 1
}

internal enum D022TicketConsumptionStatus
{
    AcceptedForContractReplay = 0,
    AlreadyConsumed = 1,
    NotYetValid = 2,
    Expired = 3,
    TicketMismatch = 4,
    OpportunityMismatch = 5,
    CandidateMismatch = 6,
    GoalMismatch = 7,
    IntentMismatch = 8,
    DecisionMismatch = 9,
    SnapshotMismatch = 10,
    SituationRevisionMismatch = 11,
    SessionMismatch = 12,
    EpochMismatch = 13,
    RuntimeIdentityMismatch = 14,
    RuntimeGenerationMismatch = 15,
    AssistGenerationMismatch = 16,
    BodyBindingMismatch = 17,
    TransactionBoundaryMismatch = 18
}

internal enum D022SliceAExecutionPermission
{
    DeniedByContractOnlyBoundary = 0
}

internal enum D022MotionCancellationStrategy
{
    TypedActiveCancellationRequired = 0
}

internal static class D022SliceACeilings
{
    internal const int MaximumCandidateSources = 1;
    internal const int MaximumCapabilityClasses = 1;
    internal const int MaximumSemanticMotionKinds = 1;
    internal const int MaximumActiveCandidates = 1;
    internal const int MaximumOutstandingTickets = 1;
    internal const int MaximumOutstandingProposals = 1;
    internal const int MaximumAutomaticRetries = 0;
    internal const int MaximumLegacyFallbacks = 0;
    internal const int MaximumSuccessfulAssistsPerRollingWindow = 3;
    internal const int MaximumSuccessfulAssistsPerProcessLifetime = 12;
    internal const int MaximumFirstLiveBindingOneShots = 1;

    internal static readonly TimeSpan CandidateLifetime =
        TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan ProposalLifetime =
        TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MinimumEvaluationInterval =
        TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan SuccessfulAssistCooldown =
        TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan SuccessfulAssistRollingWindow =
        TimeSpan.FromMinutes(10);
}

internal static class D022ContractGuard
{
    internal const int MaximumIdentifierLength = 128;

    internal static string Identifier(
        string? value,
        string parameterName)
        => DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);
}

internal sealed class CharacterRuntimeIdentity
{
    internal string RuntimeProfileId { get; }
    internal string RuntimeInstanceId { get; }
    internal long Generation { get; }
    internal string OrchestratorSessionId { get; }
    internal string OwnerIdentity { get; }

    internal CharacterRuntimeIdentity(
        string runtimeProfileId,
        string runtimeInstanceId,
        long generation,
        string orchestratorSessionId,
        string ownerIdentity)
    {
        RuntimeProfileId = D022ContractGuard.Identifier(
            runtimeProfileId,
            nameof(runtimeProfileId));
        RuntimeInstanceId = D022ContractGuard.Identifier(
            runtimeInstanceId,
            nameof(runtimeInstanceId));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        OrchestratorSessionId = D022ContractGuard.Identifier(
            orchestratorSessionId,
            nameof(orchestratorSessionId));
        OwnerIdentity = D022ContractGuard.Identifier(
            ownerIdentity,
            nameof(ownerIdentity));
        Generation = generation;
    }

    internal bool Matches(CharacterRuntimeIdentity? other)
        => other != null
            && string.Equals(
                RuntimeProfileId,
                other.RuntimeProfileId,
                StringComparison.Ordinal)
            && string.Equals(
                RuntimeInstanceId,
                other.RuntimeInstanceId,
                StringComparison.Ordinal)
            && Generation == other.Generation
            && string.Equals(
                OrchestratorSessionId,
                other.OrchestratorSessionId,
                StringComparison.Ordinal)
            && string.Equals(
                OwnerIdentity,
                other.OwnerIdentity,
                StringComparison.Ordinal);
}

internal sealed class CharacterNeutralMotionIntent
{
    internal string IntentId { get; }
    internal InitiativeCapabilityClass CapabilityClass =>
        InitiativeCapabilityClass.Motion;
    internal MotionIntentKind SemanticKind => MotionIntentKind.PlayOneShot;
    internal MotionIntentSource Source => MotionIntentSource.Initiative;

    internal CharacterNeutralMotionIntent(string intentId)
    {
        IntentId = D022ContractGuard.Identifier(
            intentId,
            nameof(intentId));
    }
}

internal sealed class CharacterBodyBindingSafetyEnvelope
{
    internal string BindingVersion { get; }
    internal bool RequiresCharacterAvailable => true;
    internal bool RequiresGrounded => true;
    internal bool RequiresNativeIdle => true;
    internal bool AllowsSleeping => false;
    internal bool AllowsUnsupportedClothing => false;
    internal D022MotionCancellationStrategy CancellationStrategy =>
        D022MotionCancellationStrategy.TypedActiveCancellationRequired;
    internal TimeSpan ExpectedDuration { get; }
    internal TimeSpan Timeout { get; }

    internal CharacterBodyBindingSafetyEnvelope(
        string bindingVersion,
        TimeSpan expectedDuration,
        TimeSpan timeout)
    {
        BindingVersion = D022ContractGuard.Identifier(
            bindingVersion,
            nameof(bindingVersion));
        if (expectedDuration <= TimeSpan.Zero
            || expectedDuration > D022SliceACeilings.CandidateLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedDuration));
        }
        if (timeout < expectedDuration
            || timeout > D022SliceACeilings.CandidateLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ExpectedDuration = expectedDuration;
        Timeout = timeout;
    }

    internal bool Matches(CharacterBodyBindingSafetyEnvelope? other)
        => other != null
            && string.Equals(
                BindingVersion,
                other.BindingVersion,
                StringComparison.Ordinal)
            && RequiresCharacterAvailable
                == other.RequiresCharacterAvailable
            && RequiresGrounded == other.RequiresGrounded
            && RequiresNativeIdle == other.RequiresNativeIdle
            && AllowsSleeping == other.AllowsSleeping
            && AllowsUnsupportedClothing
                == other.AllowsUnsupportedClothing
            && CancellationStrategy == other.CancellationStrategy
            && ExpectedDuration == other.ExpectedDuration
            && Timeout == other.Timeout;
}

internal sealed class MotionOpportunityIdentity
{
    internal static readonly TimeSpan MaximumCandidateLifetime =
        D022SliceACeilings.CandidateLifetime;

    internal string OpportunityId { get; }
    internal string SourceId { get; }
    internal long SourceSequence { get; }
    internal MotionIntentKind SemanticKind { get; }
    internal MotionDirectorReason Reason { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }

    internal MotionOpportunityIdentity(
        string opportunityId,
        string sourceId,
        long sourceSequence,
        MotionIntentKind semanticKind,
        MotionDirectorReason reason,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        string sessionId,
        string epochId,
        CharacterRuntimeIdentity runtimeIdentity)
    {
        OpportunityId = D022ContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
        SourceId = D022ContractGuard.Identifier(
            sourceId,
            nameof(sourceId));
        if (sourceSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSequence));
        if (semanticKind != MotionIntentKind.PlayOneShot)
            throw new ArgumentOutOfRangeException(nameof(semanticKind));
        if (reason != MotionDirectorReason.React)
            throw new ArgumentOutOfRangeException(nameof(reason));

        OccurredAt = DecisionCoreContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ExpiresAt = DecisionCoreContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ObservedAt < OccurredAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (ExpiresAt <= ObservedAt
            || ExpiresAt - ObservedAt > MaximumCandidateLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        SessionId = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                RuntimeIdentity.OrchestratorSessionId,
                SessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Runtime identity and opportunity must use the same session.",
                nameof(runtimeIdentity));
        }

        SourceSequence = sourceSequence;
        SemanticKind = semanticKind;
        Reason = reason;
    }
}

internal sealed class MotionCandidateBinding
{
    internal string BindingId { get; }
    internal MotionOpportunityIdentity Opportunity { get; }
    internal string CandidateId { get; }
    internal string CandidateFingerprint { get; }
    internal string GoalId { get; }
    internal CharacterNeutralMotionIntent Intent { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal MotionCandidateBinding(
        string bindingId,
        MotionOpportunityIdentity opportunity,
        InitiativeCandidate candidate,
        string candidateFingerprint,
        GoalState goal,
        CharacterNeutralMotionIntent intent,
        CharacterBodyBindingSafetyEnvelope safetyEnvelope)
    {
        BindingId = D022ContractGuard.Identifier(
            bindingId,
            nameof(bindingId));
        Opportunity = opportunity
            ?? throw new ArgumentNullException(nameof(opportunity));
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));
        if (goal == null)
            throw new ArgumentNullException(nameof(goal));
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        SafetyEnvelope = safetyEnvelope
            ?? throw new ArgumentNullException(nameof(safetyEnvelope));

        CandidateFingerprint = D022ContractGuard.Identifier(
            candidateFingerprint,
            nameof(candidateFingerprint));
        ValidateCandidate(candidate, opportunity);
        ValidateGoal(candidate, goal);
        if (Intent.SemanticKind != opportunity.SemanticKind)
        {
            throw new ArgumentException(
                "Intent semantic kind must match the opportunity.",
                nameof(intent));
        }
        if (candidate.ExpiresAt != opportunity.ExpiresAt)
        {
            throw new ArgumentException(
                "Candidate expiry must preserve opportunity expiry.",
                nameof(candidate));
        }

        CandidateId = candidate.Id;
        GoalId = goal.Id;
        RuntimeIdentity = opportunity.RuntimeIdentity;
        ExpiresAt = candidate.ExpiresAt;
    }

    private static void ValidateCandidate(
        InitiativeCandidate candidate,
        MotionOpportunityIdentity opportunity)
    {
        if (!string.Equals(
                candidate.SessionId,
                opportunity.SessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.SourceId,
                opportunity.SourceId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Candidate must preserve opportunity session and source.",
                nameof(candidate));
        }
        if (candidate.Kind != InitiativeCandidateKind.MotionOpportunity
            || candidate.Reason
                != InitiativeReasonCode.MotionDecisionOpportunity
            || candidate.CapabilityClass
                != InitiativeCapabilityClass.Motion
            || candidate.DesiredDisposition != DecisionDisposition.Act
            || candidate.RequiredEvidence != DecisionEvidence.None
            || candidate.RequiresConsent)
        {
            throw new ArgumentException(
                "D-022 candidate must be a no-evidence Motion Act.",
                nameof(candidate));
        }
    }

    private static void ValidateGoal(
        InitiativeCandidate candidate,
        GoalState goal)
    {
        if (!string.Equals(
                candidate.Id,
                goal.Id,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.SessionId,
                goal.SessionId,
                StringComparison.Ordinal)
            || goal.Source != GoalSource.Initiative
            || goal.PriorityClass != GoalPriorityClass.Initiative
            || goal.Status is not GoalStatus.Candidate
                and not GoalStatus.Active
            || goal.DesiredDisposition != DecisionDisposition.Act
            || goal.RequiredEvidence != DecisionEvidence.None
            || goal.RequiresConsent)
        {
            throw new ArgumentException(
                "Goal must preserve the no-evidence Motion Candidate identity.",
                nameof(goal));
        }
    }
}

internal static class D022NoEvidenceBaselineProjector
{
    internal static NoEvidenceBaselineOutcome Create(
        MotionCandidateBinding binding,
        DecisionEvaluation evaluation,
        long situationRevision,
        DateTimeOffset evaluatedAt)
    {
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        if (evaluation.Trace.SnapshotSequence != situationRevision)
        {
            throw new ArgumentException(
                "Situation revision must match the Decision snapshot.",
                nameof(situationRevision));
        }
        if (evaluation.Outcome.Disposition != DecisionDisposition.Act
            || evaluation.Outcome.Reason != DecisionReason.GoalSelected
            || evaluation.Outcome.Invocation != null
            || !string.Equals(
                evaluation.Outcome.SelectedGoalId,
                binding.GoalId,
                StringComparison.Ordinal)
            || !string.Equals(
                evaluation.Trace.SelectedGoalId,
                binding.GoalId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "D-022 Baseline requires an exact no-invocation Act decision.",
                nameof(evaluation));
        }

        return new NoEvidenceBaselineOutcome(
            "d022-baseline-" + evaluation.Trace.DecisionId.ToString("N"),
            binding.CandidateId,
            binding.CandidateFingerprint,
            evaluation.Trace.SnapshotId,
            situationRevision,
            evaluation.Trace.DecisionId,
            evaluation.Outcome.Disposition,
            evaluation.Outcome.Reason,
            evaluation.Outcome.SelectedGoalId,
            evaluatedAt);
    }
}

internal sealed class AssistAuthorizationTicket
{
    internal static readonly TimeSpan SameTransactionLifetime =
        TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ScheduledBoundaryLifetime =
        TimeSpan.FromSeconds(1);

    internal string TicketId { get; }
    internal string Nonce { get; }
    internal string BindingId { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal string BodyBindingVersion { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal D022TicketTransactionBoundary TransactionBoundary { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal D022SliceAExecutionPermission ExecutionPermission =>
        D022SliceAExecutionPermission.DeniedByContractOnlyBoundary;

    internal AssistAuthorizationTicket(
        string ticketId,
        string nonce,
        MotionCandidateBinding binding,
        NoEvidenceBaselineOutcome baseline,
        string epochId,
        long assistConfigurationGeneration,
        D022TicketTransactionBoundary transactionBoundary,
        DateTimeOffset issuedAt)
    {
        TicketId = D022ContractGuard.Identifier(
            ticketId,
            nameof(ticketId));
        Nonce = D022ContractGuard.Identifier(nonce, nameof(nonce));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (baseline == null)
            throw new ArgumentNullException(nameof(baseline));
        if (!Enum.IsDefined(
                typeof(D022TicketTransactionBoundary),
                transactionBoundary))
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionBoundary));
        }
        if (assistConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assistConfigurationGeneration));
        }
        if (!string.Equals(
                binding.CandidateId,
                baseline.CandidateId,
                StringComparison.Ordinal)
            || !string.Equals(
                binding.CandidateFingerprint,
                baseline.CandidateFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                binding.GoalId,
                baseline.SelectedGoalId,
                StringComparison.Ordinal)
            || baseline.Disposition != DecisionDisposition.Act
            || baseline.Reason != DecisionReason.GoalSelected)
        {
            throw new ArgumentException(
                "Ticket must preserve the exact Candidate, Goal and Baseline.",
                nameof(baseline));
        }

        var normalizedIssuedAt = DecisionCoreContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        if (normalizedIssuedAt < baseline.EvaluatedAt
            || normalizedIssuedAt >= binding.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(issuedAt));
        }

        BindingId = binding.BindingId;
        OpportunityId = binding.Opportunity.OpportunityId;
        CandidateId = binding.CandidateId;
        GoalId = binding.GoalId;
        IntentId = binding.Intent.IntentId;
        BodyBindingVersion = binding.SafetyEnvelope.BindingVersion;
        SafetyEnvelope = binding.SafetyEnvelope;
        DecisionId = baseline.DecisionId;
        SnapshotId = baseline.SnapshotId;
        SituationRevision = baseline.SituationRevision;
        SessionId = binding.Opportunity.SessionId;
        EpochId = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        if (!string.Equals(
                EpochId,
                binding.Opportunity.EpochId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Ticket and opportunity must use the same Epoch.",
                nameof(epochId));
        }
        RuntimeIdentity = binding.RuntimeIdentity;
        AssistConfigurationGeneration = assistConfigurationGeneration;
        TransactionBoundary = transactionBoundary;
        IssuedAt = normalizedIssuedAt;
        var nominalExpiry = IssuedAt.Add(LifetimeFor(transactionBoundary));
        ExpiresAt = nominalExpiry <= binding.ExpiresAt
            ? nominalExpiry
            : binding.ExpiresAt;
    }

    internal static TimeSpan LifetimeFor(
        D022TicketTransactionBoundary transactionBoundary)
        => transactionBoundary switch
        {
            D022TicketTransactionBoundary.SameMainThreadTransaction =>
                SameTransactionLifetime,
            D022TicketTransactionBoundary
                .CrossesFrameSchedulerOrAsyncBoundary =>
                ScheduledBoundaryLifetime,
            _ => throw new ArgumentOutOfRangeException(
                nameof(transactionBoundary))
        };
}

internal sealed class D022TicketConsumptionContext
{
    internal string TicketId { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal bool IsSameMainThreadTransaction { get; }
    internal DateTimeOffset ConsumedAt { get; }

    internal D022TicketConsumptionContext(
        string ticketId,
        string opportunityId,
        string candidateId,
        string goalId,
        string intentId,
        CharacterBodyBindingSafetyEnvelope safetyEnvelope,
        Guid decisionId,
        Guid snapshotId,
        long situationRevision,
        string sessionId,
        string epochId,
        CharacterRuntimeIdentity runtimeIdentity,
        long assistConfigurationGeneration,
        bool isSameMainThreadTransaction,
        DateTimeOffset consumedAt)
    {
        TicketId = D022ContractGuard.Identifier(
            ticketId,
            nameof(ticketId));
        OpportunityId = D022ContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
        CandidateId = D022ContractGuard.Identifier(
            candidateId,
            nameof(candidateId));
        GoalId = D022ContractGuard.Identifier(goalId, nameof(goalId));
        IntentId = D022ContractGuard.Identifier(
            intentId,
            nameof(intentId));
        SafetyEnvelope = safetyEnvelope
            ?? throw new ArgumentNullException(nameof(safetyEnvelope));
        if (decisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.", nameof(decisionId));
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot ID cannot be empty.", nameof(snapshotId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        SessionId = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(epochId, nameof(epochId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (assistConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assistConfigurationGeneration));
        }

        DecisionId = decisionId;
        SnapshotId = snapshotId;
        SituationRevision = situationRevision;
        AssistConfigurationGeneration = assistConfigurationGeneration;
        IsSameMainThreadTransaction = isSameMainThreadTransaction;
        ConsumedAt = DecisionCoreContractGuard.Timestamp(
            consumedAt,
            nameof(consumedAt));
    }
}

internal static class D022TicketValidator
{
    internal static D022TicketConsumptionStatus Validate(
        AssistAuthorizationTicket ticket,
        D022TicketConsumptionContext context,
        bool alreadyConsumed)
    {
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (alreadyConsumed)
            return D022TicketConsumptionStatus.AlreadyConsumed;
        if (context.ConsumedAt < ticket.IssuedAt)
            return D022TicketConsumptionStatus.NotYetValid;
        if (context.ConsumedAt >= ticket.ExpiresAt)
            return D022TicketConsumptionStatus.Expired;
        if (!Same(ticket.TicketId, context.TicketId))
            return D022TicketConsumptionStatus.TicketMismatch;
        if (!Same(ticket.OpportunityId, context.OpportunityId))
            return D022TicketConsumptionStatus.OpportunityMismatch;
        if (!Same(ticket.CandidateId, context.CandidateId))
            return D022TicketConsumptionStatus.CandidateMismatch;
        if (!Same(ticket.GoalId, context.GoalId))
            return D022TicketConsumptionStatus.GoalMismatch;
        if (!Same(ticket.IntentId, context.IntentId))
            return D022TicketConsumptionStatus.IntentMismatch;
        if (ticket.DecisionId != context.DecisionId)
            return D022TicketConsumptionStatus.DecisionMismatch;
        if (ticket.SnapshotId != context.SnapshotId)
            return D022TicketConsumptionStatus.SnapshotMismatch;
        if (ticket.SituationRevision != context.SituationRevision)
        {
            return D022TicketConsumptionStatus
                .SituationRevisionMismatch;
        }
        if (!Same(ticket.SessionId, context.SessionId))
            return D022TicketConsumptionStatus.SessionMismatch;
        if (!Same(ticket.EpochId, context.EpochId))
            return D022TicketConsumptionStatus.EpochMismatch;
        if (!Same(
                ticket.RuntimeIdentity.RuntimeProfileId,
                context.RuntimeIdentity.RuntimeProfileId)
            || !Same(
                ticket.RuntimeIdentity.RuntimeInstanceId,
                context.RuntimeIdentity.RuntimeInstanceId)
            || !Same(
                ticket.RuntimeIdentity.OrchestratorSessionId,
                context.RuntimeIdentity.OrchestratorSessionId)
            || !Same(
                ticket.RuntimeIdentity.OwnerIdentity,
                context.RuntimeIdentity.OwnerIdentity))
        {
            return D022TicketConsumptionStatus.RuntimeIdentityMismatch;
        }
        if (ticket.RuntimeIdentity.Generation
            != context.RuntimeIdentity.Generation)
        {
            return D022TicketConsumptionStatus.RuntimeGenerationMismatch;
        }
        if (ticket.AssistConfigurationGeneration
            != context.AssistConfigurationGeneration)
        {
            return D022TicketConsumptionStatus
                .AssistGenerationMismatch;
        }
        if (!ticket.SafetyEnvelope.Matches(context.SafetyEnvelope))
            return D022TicketConsumptionStatus.BodyBindingMismatch;
        if (ticket.TransactionBoundary
                == D022TicketTransactionBoundary.SameMainThreadTransaction
            && !context.IsSameMainThreadTransaction)
        {
            return D022TicketConsumptionStatus
                .TransactionBoundaryMismatch;
        }

        return D022TicketConsumptionStatus.AcceptedForContractReplay;
    }

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);
}

internal sealed class D022LegacyContinuityStamp
{
    internal long RngCursor { get; }
    internal string RngOrderFingerprint { get; }
    internal long TimerSequence { get; }
    internal long SourceSequence { get; }
    internal int OneShotSelectionOrdinal { get; }
    internal long QueueOrdinal { get; }
    internal string CompletionCorrelationId { get; }

    internal D022LegacyContinuityStamp(
        long rngCursor,
        string rngOrderFingerprint,
        long timerSequence,
        long sourceSequence,
        int oneShotSelectionOrdinal,
        long queueOrdinal,
        string completionCorrelationId)
    {
        if (rngCursor < 0)
            throw new ArgumentOutOfRangeException(nameof(rngCursor));
        if (timerSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(timerSequence));
        if (sourceSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceSequence));
        if (oneShotSelectionOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(oneShotSelectionOrdinal));
        }
        if (queueOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(queueOrdinal));

        RngOrderFingerprint = D022ContractGuard.Identifier(
            rngOrderFingerprint,
            nameof(rngOrderFingerprint));
        CompletionCorrelationId = D022ContractGuard.Identifier(
            completionCorrelationId,
            nameof(completionCorrelationId));
        RngCursor = rngCursor;
        TimerSequence = timerSequence;
        SourceSequence = sourceSequence;
        OneShotSelectionOrdinal = oneShotSelectionOrdinal;
        QueueOrdinal = queueOrdinal;
    }

    internal bool IsExactlySame(D022LegacyContinuityStamp? other)
        => other != null
            && RngCursor == other.RngCursor
            && string.Equals(
                RngOrderFingerprint,
                other.RngOrderFingerprint,
                StringComparison.Ordinal)
            && TimerSequence == other.TimerSequence
            && SourceSequence == other.SourceSequence
            && OneShotSelectionOrdinal == other.OneShotSelectionOrdinal
            && QueueOrdinal == other.QueueOrdinal
            && string.Equals(
                CompletionCorrelationId,
                other.CompletionCorrelationId,
                StringComparison.Ordinal);
}

internal sealed class D022SliceASideEffectCounters
{
    internal static D022SliceASideEffectCounters Zero { get; } = new();

    internal int AdditionalRngReads => 0;
    internal int TimerAdvances => 0;
    internal int SourceSequenceAdvances => 0;
    internal int OneShotSelectionChanges => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int Executions => 0;
    internal int AuthorityOutputs => 0;
    internal int EvidenceCalls => 0;
    internal int MemoryCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;
    internal bool IsZero => true;

    private D022SliceASideEffectCounters()
    {
    }
}
