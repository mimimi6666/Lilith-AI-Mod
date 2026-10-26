using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D022SliceCIngressStatus
{
    PreparedRecordOnly = 0,
    TicketRejected = 1,
    OwnershipMismatch = 2,
    CandidateExpired = 3,
    MasterDisabled = 4,
    AssistDisabled = 5,
    SourceDisabled = 6,
    SessionClosed = 7,
    EpochDraining = 8,
    DirectPlayerControl = 9,
    ConversationBusy = 10,
    PendingExecution = 11,
    QuietPeriod = 12,
    CapabilityUnavailable = 13,
    CharacterUnavailable = 14,
    CharacterNotGrounded = 15,
    NativeStateBusy = 16,
    CapacityReached = 17,
    ProposalIdentityInvalid = 18
}

internal enum D022SliceCLifecycleKind
{
    QueueRejected = 0,
    PolicyRejected = 1,
    Started = 2,
    Completed = 3,
    PlayerInterrupted = 4,
    Failed = 5,
    TimedOut = 6
}

internal enum D022SliceCLifecycleAdmissionStatus
{
    AcceptedRecordOnly = 0,
    DuplicateSignal = 1,
    AlreadyTerminal = 2,
    ExpectationMismatch = 3,
    OpportunityMismatch = 4,
    CandidateMismatch = 5,
    GoalMismatch = 6,
    IntentMismatch = 7,
    DecisionMismatch = 8,
    SnapshotMismatch = 9,
    SituationRevisionMismatch = 10,
    TicketMismatch = 11,
    ProposalMismatch = 12,
    SessionMismatch = 13,
    EpochMismatch = 14,
    RuntimeIdentityMismatch = 15,
    RuntimeGenerationMismatch = 16,
    ConfigurationGenerationMismatch = 17,
    BodyBindingMismatch = 18,
    NotYetValid = 19,
    Late = 20,
    InvalidTransition = 21,
    CapacityReached = 22
}

internal sealed class D022SliceCSituationGateSnapshot
{
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal bool MasterEnabled { get; }
    internal bool AssistEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool SessionOpen { get; }
    internal bool EpochActive { get; }
    internal bool IsUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool CapabilityAvailable { get; }
    internal bool CharacterAvailable { get; }
    internal bool IsGrounded { get; }
    internal bool IsNativeIdle { get; }

    internal D022SliceCSituationGateSnapshot(
        string sessionId,
        string epochId,
        long situationRevision,
        CharacterRuntimeIdentity runtimeIdentity,
        long assistConfigurationGeneration,
        DateTimeOffset evaluatedAt,
        bool masterEnabled,
        bool assistEnabled,
        bool sourceEnabled,
        bool sessionOpen,
        bool epochActive,
        bool isUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool capabilityAvailable,
        bool characterAvailable,
        bool isGrounded,
        bool isNativeIdle)
    {
        SessionId = D022ContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(epochId, nameof(epochId));
        if (situationRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(situationRevision));
        }
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (assistConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assistConfigurationGeneration));
        }

        SituationRevision = situationRevision;
        AssistConfigurationGeneration = assistConfigurationGeneration;
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        MasterEnabled = masterEnabled;
        AssistEnabled = assistEnabled;
        SourceEnabled = sourceEnabled;
        SessionOpen = sessionOpen;
        EpochActive = epochActive;
        IsUnderDirectPlayerControl = isUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        CapabilityAvailable = capabilityAvailable;
        CharacterAvailable = characterAvailable;
        IsGrounded = isGrounded;
        IsNativeIdle = isNativeIdle;
    }
}

internal sealed class D022SliceCRestrictedIngressRequest
{
    internal AssistAuthorizationTicket Ticket { get; }
    internal MotionCandidateBinding Binding { get; }
    internal NoEvidenceBaselineOutcome Baseline { get; }
    internal D022SliceCSituationGateSnapshot Situation { get; }
    internal D022TicketConsumptionContext TicketConsumption { get; }
    internal Guid ProposalId { get; }
    internal long ProposalSequence { get; }
    internal string OutcomeExpectationId { get; }
    internal bool OpportunityIsAssistOwned { get; }

    internal D022SliceCRestrictedIngressRequest(
        AssistAuthorizationTicket ticket,
        MotionCandidateBinding binding,
        NoEvidenceBaselineOutcome baseline,
        D022SliceCSituationGateSnapshot situation,
        D022TicketConsumptionContext ticketConsumption,
        Guid proposalId,
        long proposalSequence,
        string outcomeExpectationId,
        bool opportunityIsAssistOwned)
    {
        Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
        Binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        Baseline = baseline
            ?? throw new ArgumentNullException(nameof(baseline));
        Situation = situation
            ?? throw new ArgumentNullException(nameof(situation));
        TicketConsumption = ticketConsumption
            ?? throw new ArgumentNullException(nameof(ticketConsumption));
        if (proposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Proposal ID cannot be empty.",
                nameof(proposalId));
        }
        if (proposalSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(proposalSequence));
        OutcomeExpectationId = D022ContractGuard.Identifier(
            outcomeExpectationId,
            nameof(outcomeExpectationId));
        ProposalId = proposalId;
        ProposalSequence = proposalSequence;
        OpportunityIsAssistOwned = opportunityIsAssistOwned;
    }
}

internal sealed class D022SliceCRestrictedProposalEnvelope
{
    internal Guid ProposalId { get; }
    internal long ProposalSequence { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string TicketId { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal MotionIntentKind SemanticKind => MotionIntentKind.PlayOneShot;
    internal MotionIntentSource Source => MotionIntentSource.Initiative;
    internal DateTimeOffset PreparedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal DateTimeOffset OutcomeDeadline { get; }
    internal bool MayWriteLiveQueue => false;

    internal D022SliceCRestrictedProposalEnvelope(
        D022SliceCRestrictedIngressRequest request)
    {
        ProposalId = request.ProposalId;
        ProposalSequence = request.ProposalSequence;
        OpportunityId = request.Ticket.OpportunityId;
        CandidateId = request.Ticket.CandidateId;
        GoalId = request.Ticket.GoalId;
        IntentId = request.Ticket.IntentId;
        DecisionId = request.Ticket.DecisionId;
        SnapshotId = request.Ticket.SnapshotId;
        SituationRevision = request.Ticket.SituationRevision;
        TicketId = request.Ticket.TicketId;
        SessionId = request.Ticket.SessionId;
        EpochId = request.Ticket.EpochId;
        RuntimeIdentity = request.Ticket.RuntimeIdentity;
        AssistConfigurationGeneration =
            request.Ticket.AssistConfigurationGeneration;
        SafetyEnvelope = request.Ticket.SafetyEnvelope;
        PreparedAt = request.Situation.EvaluatedAt;

        var proposalExpiry = PreparedAt.Add(
            D022SliceACeilings.ProposalLifetime);
        ExpiresAt = proposalExpiry <= request.Binding.ExpiresAt
            ? proposalExpiry
            : request.Binding.ExpiresAt;
        var outcomeDeadline = PreparedAt.Add(
            request.Binding.SafetyEnvelope.Timeout);
        OutcomeDeadline = outcomeDeadline <= request.Binding.ExpiresAt
            ? outcomeDeadline
            : request.Binding.ExpiresAt;
    }
}

internal sealed class D022SliceCOutcomeExpectation
{
    internal string ExpectationId { get; }
    internal Guid ProposalId { get; }
    internal long ProposalSequence { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string TicketId { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal MotionIntentKind SemanticKind => MotionIntentKind.PlayOneShot;
    internal DateTimeOffset RegisteredAt { get; }
    internal DateTimeOffset QueueAdmissionDeadline { get; }
    internal DateTimeOffset Deadline { get; }

    internal D022SliceCOutcomeExpectation(
        string expectationId,
        D022SliceCRestrictedProposalEnvelope proposal)
    {
        ExpectationId = D022ContractGuard.Identifier(
            expectationId,
            nameof(expectationId));
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));

        ProposalId = proposal.ProposalId;
        ProposalSequence = proposal.ProposalSequence;
        OpportunityId = proposal.OpportunityId;
        CandidateId = proposal.CandidateId;
        GoalId = proposal.GoalId;
        IntentId = proposal.IntentId;
        DecisionId = proposal.DecisionId;
        SnapshotId = proposal.SnapshotId;
        SituationRevision = proposal.SituationRevision;
        TicketId = proposal.TicketId;
        SessionId = proposal.SessionId;
        EpochId = proposal.EpochId;
        RuntimeIdentity = proposal.RuntimeIdentity;
        AssistConfigurationGeneration =
            proposal.AssistConfigurationGeneration;
        SafetyEnvelope = proposal.SafetyEnvelope;
        RegisteredAt = proposal.PreparedAt;
        QueueAdmissionDeadline = proposal.ExpiresAt;
        Deadline = proposal.OutcomeDeadline;
    }
}

internal sealed class D022SliceCIngressResult
{
    internal D022SliceCIngressStatus Status { get; }
    internal D022TicketConsumptionStatus? TicketStatus { get; }
    internal D022SliceCRestrictedProposalEnvelope? Proposal { get; }
    internal D022SliceCOutcomeExpectation? Expectation { get; }
    internal bool AllowsLegacyFallback => false;
    internal D022SliceASideEffectCounters SideEffects =>
        D022SliceASideEffectCounters.Zero;
    internal bool Prepared =>
        Status == D022SliceCIngressStatus.PreparedRecordOnly;

    private D022SliceCIngressResult(
        D022SliceCIngressStatus status,
        D022TicketConsumptionStatus? ticketStatus,
        D022SliceCRestrictedProposalEnvelope? proposal,
        D022SliceCOutcomeExpectation? expectation)
    {
        Status = status;
        TicketStatus = ticketStatus;
        Proposal = proposal;
        Expectation = expectation;
    }

    internal static D022SliceCIngressResult Reject(
        D022SliceCIngressStatus status,
        D022TicketConsumptionStatus? ticketStatus = null)
        => new(status, ticketStatus, null, null);

    internal static D022SliceCIngressResult Accept(
        D022SliceCRestrictedProposalEnvelope proposal,
        D022SliceCOutcomeExpectation expectation)
        => new(
            D022SliceCIngressStatus.PreparedRecordOnly,
            D022TicketConsumptionStatus.AcceptedForContractReplay,
            proposal,
            expectation);
}

internal sealed class D022SliceCRestrictedQueueIngress
{
    private readonly HashSet<string> _consumedTicketIds =
        new(StringComparer.Ordinal);
    private string? _activeProposalKey;

    internal D022SliceCIngressResult Prepare(
        D022SliceCRestrictedIngressRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var alreadyConsumed = _consumedTicketIds.Contains(
            request.Ticket.TicketId);
        var ticketStatus = D022TicketValidator.Validate(
            request.Ticket,
            request.TicketConsumption,
            alreadyConsumed);
        if (ticketStatus
            != D022TicketConsumptionStatus.AcceptedForContractReplay)
        {
            return D022SliceCIngressResult.Reject(
                D022SliceCIngressStatus.TicketRejected,
                ticketStatus);
        }
        _consumedTicketIds.Add(request.Ticket.TicketId);
        if (!request.OpportunityIsAssistOwned)
        {
            return D022SliceCIngressResult.Reject(
                D022SliceCIngressStatus.OwnershipMismatch);
        }

        var identityStatus = ValidateIdentity(request);
        if (identityStatus != D022SliceCIngressStatus.PreparedRecordOnly)
            return D022SliceCIngressResult.Reject(identityStatus);

        var gateStatus = ValidateSituation(
            request.Ticket,
            request.Binding,
            request.Situation);
        if (gateStatus != D022SliceCIngressStatus.PreparedRecordOnly)
            return D022SliceCIngressResult.Reject(gateStatus);
        if (_activeProposalKey != null)
        {
            return D022SliceCIngressResult.Reject(
                D022SliceCIngressStatus.CapacityReached);
        }

        var proposal = new D022SliceCRestrictedProposalEnvelope(request);
        _activeProposalKey = ProposalKey(
            proposal.ProposalId,
            proposal.ProposalSequence);
        var expectation = new D022SliceCOutcomeExpectation(
            request.OutcomeExpectationId,
            proposal);
        return D022SliceCIngressResult.Accept(proposal, expectation);
    }

    internal D022SliceCIngressStatus RevalidateBeforeFutureQueueSubmission(
        D022SliceCRestrictedProposalEnvelope proposal,
        AssistAuthorizationTicket ticket,
        MotionCandidateBinding binding,
        D022SliceCSituationGateSnapshot situation)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (ticket == null)
            throw new ArgumentNullException(nameof(ticket));
        if (binding == null)
            throw new ArgumentNullException(nameof(binding));
        if (situation == null)
            throw new ArgumentNullException(nameof(situation));
        if (!string.Equals(
                _activeProposalKey,
                ProposalKey(
                    proposal.ProposalId,
                    proposal.ProposalSequence),
                StringComparison.Ordinal))
        {
            return D022SliceCIngressStatus.ProposalIdentityInvalid;
        }
        if (!Matches(ticket, proposal)
            || !Matches(binding, ticket)
            || !Matches(situation, ticket))
        {
            return D022SliceCIngressStatus.ProposalIdentityInvalid;
        }
        if (situation.EvaluatedAt >= proposal.ExpiresAt
            || situation.EvaluatedAt >= ticket.ExpiresAt
            || situation.EvaluatedAt >= binding.ExpiresAt)
        {
            return D022SliceCIngressStatus.CandidateExpired;
        }

        return ValidateSituation(ticket, binding, situation);
    }

    private static D022SliceCIngressStatus ValidateIdentity(
        D022SliceCRestrictedIngressRequest request)
    {
        if (!Matches(request.Binding, request.Ticket)
            || !Matches(request.Baseline, request.Ticket)
            || !Matches(request.Situation, request.Ticket))
        {
            return D022SliceCIngressStatus.ProposalIdentityInvalid;
        }
        return D022SliceCIngressStatus.PreparedRecordOnly;
    }

    private static D022SliceCIngressStatus ValidateSituation(
        AssistAuthorizationTicket ticket,
        MotionCandidateBinding binding,
        D022SliceCSituationGateSnapshot situation)
    {
        if (situation.EvaluatedAt >= ticket.ExpiresAt
            || situation.EvaluatedAt >= binding.ExpiresAt)
        {
            return D022SliceCIngressStatus.CandidateExpired;
        }
        if (!situation.MasterEnabled)
            return D022SliceCIngressStatus.MasterDisabled;
        if (!situation.AssistEnabled)
            return D022SliceCIngressStatus.AssistDisabled;
        if (!situation.SourceEnabled)
            return D022SliceCIngressStatus.SourceDisabled;
        if (!situation.SessionOpen)
            return D022SliceCIngressStatus.SessionClosed;
        if (!situation.EpochActive)
            return D022SliceCIngressStatus.EpochDraining;
        if (situation.IsUnderDirectPlayerControl)
            return D022SliceCIngressStatus.DirectPlayerControl;
        if (situation.IsConversationBusy)
            return D022SliceCIngressStatus.ConversationBusy;
        if (situation.HasPendingExecution)
            return D022SliceCIngressStatus.PendingExecution;
        if (situation.IsQuietPeriod)
            return D022SliceCIngressStatus.QuietPeriod;
        if (!situation.CapabilityAvailable)
            return D022SliceCIngressStatus.CapabilityUnavailable;
        if (!situation.CharacterAvailable)
            return D022SliceCIngressStatus.CharacterUnavailable;
        if (ticket.SafetyEnvelope.RequiresGrounded
            && !situation.IsGrounded)
        {
            return D022SliceCIngressStatus.CharacterNotGrounded;
        }
        if (ticket.SafetyEnvelope.RequiresNativeIdle
            && !situation.IsNativeIdle)
        {
            return D022SliceCIngressStatus.NativeStateBusy;
        }
        return D022SliceCIngressStatus.PreparedRecordOnly;
    }

    private static bool Matches(
        MotionCandidateBinding binding,
        AssistAuthorizationTicket ticket)
        => string.Equals(
                binding.BindingId,
                ticket.BindingId,
                StringComparison.Ordinal)
            && string.Equals(
                binding.Opportunity.OpportunityId,
                ticket.OpportunityId,
                StringComparison.Ordinal)
            && string.Equals(
                binding.CandidateId,
                ticket.CandidateId,
                StringComparison.Ordinal)
            && string.Equals(
                binding.GoalId,
                ticket.GoalId,
                StringComparison.Ordinal)
            && string.Equals(
                binding.Intent.IntentId,
                ticket.IntentId,
                StringComparison.Ordinal)
            && binding.Intent.CapabilityClass
                == InitiativeCapabilityClass.Motion
            && binding.Intent.SemanticKind
                == MotionIntentKind.PlayOneShot
            && binding.RuntimeIdentity.Matches(ticket.RuntimeIdentity)
            && binding.SafetyEnvelope.Matches(ticket.SafetyEnvelope);

    private static bool Matches(
        NoEvidenceBaselineOutcome baseline,
        AssistAuthorizationTicket ticket)
        => string.Equals(
                baseline.CandidateId,
                ticket.CandidateId,
                StringComparison.Ordinal)
            && string.Equals(
                baseline.SelectedGoalId,
                ticket.GoalId,
                StringComparison.Ordinal)
            && baseline.DecisionId == ticket.DecisionId
            && baseline.SnapshotId == ticket.SnapshotId
            && baseline.SituationRevision == ticket.SituationRevision
            && baseline.Disposition == DecisionDisposition.Act
            && baseline.Reason == DecisionReason.GoalSelected;

    private static bool Matches(
        D022SliceCSituationGateSnapshot situation,
        AssistAuthorizationTicket ticket)
        => string.Equals(
                situation.SessionId,
                ticket.SessionId,
                StringComparison.Ordinal)
            && string.Equals(
                situation.EpochId,
                ticket.EpochId,
                StringComparison.Ordinal)
            && situation.SituationRevision == ticket.SituationRevision
            && situation.RuntimeIdentity.Matches(ticket.RuntimeIdentity)
            && situation.AssistConfigurationGeneration
                == ticket.AssistConfigurationGeneration;

    private static bool Matches(
        AssistAuthorizationTicket ticket,
        D022SliceCRestrictedProposalEnvelope proposal)
        => string.Equals(
                ticket.OpportunityId,
                proposal.OpportunityId,
                StringComparison.Ordinal)
            && string.Equals(
                ticket.CandidateId,
                proposal.CandidateId,
                StringComparison.Ordinal)
            && string.Equals(
                ticket.GoalId,
                proposal.GoalId,
                StringComparison.Ordinal)
            && string.Equals(
                ticket.IntentId,
                proposal.IntentId,
                StringComparison.Ordinal)
            && ticket.DecisionId == proposal.DecisionId
            && ticket.SnapshotId == proposal.SnapshotId
            && ticket.SituationRevision == proposal.SituationRevision
            && string.Equals(
                ticket.TicketId,
                proposal.TicketId,
                StringComparison.Ordinal)
            && ticket.RuntimeIdentity.Matches(proposal.RuntimeIdentity)
            && ticket.AssistConfigurationGeneration
                == proposal.AssistConfigurationGeneration
            && ticket.SafetyEnvelope.Matches(proposal.SafetyEnvelope);

    private static string ProposalKey(Guid id, long sequence)
        => id.ToString("N") + ":" + sequence;
}

internal sealed class D022SliceCLifecycleSignal
{
    internal string SignalId { get; }
    internal string ExpectationId { get; }
    internal Guid ProposalId { get; }
    internal long ProposalSequence { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string TicketId { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal CharacterBodyBindingSafetyEnvelope SafetyEnvelope { get; }
    internal D022SliceCLifecycleKind Kind { get; }
    internal MotionIntentReason Reason { get; }
    internal DateTimeOffset RecordedAt { get; }

    internal D022SliceCLifecycleSignal(
        string signalId,
        D022SliceCOutcomeExpectation expectation,
        D022SliceCLifecycleKind kind,
        MotionIntentReason reason,
        DateTimeOffset recordedAt,
        string? expectationId = null,
        Guid? proposalId = null,
        long? proposalSequence = null,
        string? opportunityId = null,
        string? candidateId = null,
        string? goalId = null,
        string? intentId = null,
        Guid? decisionId = null,
        Guid? snapshotId = null,
        long? situationRevision = null,
        string? ticketId = null,
        string? sessionId = null,
        string? epochId = null,
        CharacterRuntimeIdentity? runtimeIdentity = null,
        long? assistConfigurationGeneration = null,
        CharacterBodyBindingSafetyEnvelope? safetyEnvelope = null)
    {
        SignalId = D022ContractGuard.Identifier(
            signalId,
            nameof(signalId));
        if (expectation == null)
            throw new ArgumentNullException(nameof(expectation));
        if (!Enum.IsDefined(typeof(D022SliceCLifecycleKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(MotionIntentReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        ExpectationId = D022ContractGuard.Identifier(
            expectationId ?? expectation.ExpectationId,
            nameof(expectationId));
        ProposalId = proposalId ?? expectation.ProposalId;
        if (ProposalId == Guid.Empty)
            throw new ArgumentException("Proposal ID cannot be empty.");
        ProposalSequence = proposalSequence
            ?? expectation.ProposalSequence;
        if (ProposalSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(proposalSequence));
        OpportunityId = D022ContractGuard.Identifier(
            opportunityId ?? expectation.OpportunityId,
            nameof(opportunityId));
        CandidateId = D022ContractGuard.Identifier(
            candidateId ?? expectation.CandidateId,
            nameof(candidateId));
        GoalId = D022ContractGuard.Identifier(
            goalId ?? expectation.GoalId,
            nameof(goalId));
        IntentId = D022ContractGuard.Identifier(
            intentId ?? expectation.IntentId,
            nameof(intentId));
        DecisionId = decisionId ?? expectation.DecisionId;
        if (DecisionId == Guid.Empty)
            throw new ArgumentException("Decision ID cannot be empty.");
        SnapshotId = snapshotId ?? expectation.SnapshotId;
        if (SnapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot ID cannot be empty.");
        SituationRevision = situationRevision
            ?? expectation.SituationRevision;
        if (SituationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        TicketId = D022ContractGuard.Identifier(
            ticketId ?? expectation.TicketId,
            nameof(ticketId));
        SessionId = D022ContractGuard.Identifier(
            sessionId ?? expectation.SessionId,
            nameof(sessionId));
        EpochId = D022ContractGuard.Identifier(
            epochId ?? expectation.EpochId,
            nameof(epochId));
        RuntimeIdentity = runtimeIdentity ?? expectation.RuntimeIdentity;
        AssistConfigurationGeneration = assistConfigurationGeneration
            ?? expectation.AssistConfigurationGeneration;
        if (AssistConfigurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assistConfigurationGeneration));
        }
        SafetyEnvelope = safetyEnvelope ?? expectation.SafetyEnvelope;
        Kind = kind;
        Reason = reason;
        RecordedAt = DecisionCoreContractGuard.Timestamp(
            recordedAt,
            nameof(recordedAt));
    }
}

internal sealed class D022SliceCLifecycleRecord
{
    internal string SignalId { get; }
    internal string ExpectationId { get; }
    internal Guid ProposalId { get; }
    internal long ProposalSequence { get; }
    internal string OpportunityId { get; }
    internal string CandidateId { get; }
    internal string GoalId { get; }
    internal string IntentId { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal string TicketId { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long AssistConfigurationGeneration { get; }
    internal string BodyBindingVersion { get; }
    internal D022SliceCLifecycleKind Kind { get; }
    internal MotionIntentReason Reason { get; }
    internal DateTimeOffset RecordedAt { get; }
    internal bool IsTerminal { get; }
    internal bool VerifiedSuccess { get; }
    internal bool MayStartFixedCooldown => VerifiedSuccess;
    internal bool MayMutateState => false;
    internal D022SliceASideEffectCounters SideEffects =>
        D022SliceASideEffectCounters.Zero;

    internal D022SliceCLifecycleRecord(
        D022SliceCLifecycleSignal signal)
    {
        SignalId = signal.SignalId;
        ExpectationId = signal.ExpectationId;
        ProposalId = signal.ProposalId;
        ProposalSequence = signal.ProposalSequence;
        OpportunityId = signal.OpportunityId;
        CandidateId = signal.CandidateId;
        GoalId = signal.GoalId;
        IntentId = signal.IntentId;
        DecisionId = signal.DecisionId;
        SnapshotId = signal.SnapshotId;
        SituationRevision = signal.SituationRevision;
        TicketId = signal.TicketId;
        SessionId = signal.SessionId;
        EpochId = signal.EpochId;
        RuntimeIdentity = signal.RuntimeIdentity;
        AssistConfigurationGeneration =
            signal.AssistConfigurationGeneration;
        BodyBindingVersion = signal.SafetyEnvelope.BindingVersion;
        Kind = signal.Kind;
        Reason = signal.Reason;
        RecordedAt = signal.RecordedAt;
        IsTerminal = signal.Kind != D022SliceCLifecycleKind.Started;
        VerifiedSuccess =
            signal.Kind == D022SliceCLifecycleKind.Completed;
    }
}

internal sealed class D022SliceCLifecycleAdmission
{
    internal D022SliceCLifecycleAdmissionStatus Status { get; }
    internal D022SliceCLifecycleRecord? Record { get; }
    internal D022SliceASideEffectCounters SideEffects =>
        D022SliceASideEffectCounters.Zero;
    internal bool Accepted =>
        Status == D022SliceCLifecycleAdmissionStatus.AcceptedRecordOnly;

    internal D022SliceCLifecycleAdmission(
        D022SliceCLifecycleAdmissionStatus status,
        D022SliceCLifecycleRecord? record)
    {
        Status = status;
        Record = record;
    }
}

internal sealed class D022SliceCOutcomeLedger
{
    internal const int MaximumRecords = 128;

    private readonly D022SliceCOutcomeExpectation _expectation;
    private readonly List<D022SliceCLifecycleRecord> _records = new();
    private readonly HashSet<string> _signalIds =
        new(StringComparer.Ordinal);
    private bool _started;
    private bool _terminal;

    internal D022SliceCOutcomeLedger(
        D022SliceCOutcomeExpectation expectation)
    {
        _expectation = expectation
            ?? throw new ArgumentNullException(nameof(expectation));
    }

    internal IReadOnlyList<D022SliceCLifecycleRecord> Records =>
        _records.AsReadOnly();
    internal bool IsTerminal => _terminal;

    internal D022SliceCLifecycleAdmission Observe(
        D022SliceCLifecycleSignal signal)
    {
        if (signal == null)
            throw new ArgumentNullException(nameof(signal));
        if (_signalIds.Contains(signal.SignalId))
        {
            return Reject(
                D022SliceCLifecycleAdmissionStatus.DuplicateSignal);
        }
        if (_terminal)
        {
            return Reject(
                D022SliceCLifecycleAdmissionStatus.AlreadyTerminal);
        }

        var identityStatus = ValidateIdentity(signal);
        if (identityStatus
            != D022SliceCLifecycleAdmissionStatus.AcceptedRecordOnly)
        {
            return Reject(identityStatus);
        }
        if (signal.RecordedAt < _expectation.RegisteredAt)
        {
            return Reject(
                D022SliceCLifecycleAdmissionStatus.NotYetValid);
        }
        if (signal.Kind == D022SliceCLifecycleKind.TimedOut)
        {
            if (signal.RecordedAt < _expectation.Deadline)
            {
                return Reject(
                    D022SliceCLifecycleAdmissionStatus.InvalidTransition);
            }
        }
        else if (signal.RecordedAt > _expectation.Deadline)
        {
            return Reject(D022SliceCLifecycleAdmissionStatus.Late);
        }
        if (!TransitionIsValid(signal))
        {
            return Reject(
                D022SliceCLifecycleAdmissionStatus.InvalidTransition);
        }
        if (_records.Count >= MaximumRecords)
        {
            return Reject(
                D022SliceCLifecycleAdmissionStatus.CapacityReached);
        }

        _signalIds.Add(signal.SignalId);
        var record = new D022SliceCLifecycleRecord(signal);
        _records.Add(record);
        if (signal.Kind == D022SliceCLifecycleKind.Started)
            _started = true;
        if (record.IsTerminal)
            _terminal = true;
        return new D022SliceCLifecycleAdmission(
            D022SliceCLifecycleAdmissionStatus.AcceptedRecordOnly,
            record);
    }

    private D022SliceCLifecycleAdmissionStatus ValidateIdentity(
        D022SliceCLifecycleSignal signal)
    {
        if (!Same(_expectation.ExpectationId, signal.ExpectationId))
            return D022SliceCLifecycleAdmissionStatus.ExpectationMismatch;
        if (!Same(_expectation.OpportunityId, signal.OpportunityId))
            return D022SliceCLifecycleAdmissionStatus.OpportunityMismatch;
        if (!Same(_expectation.CandidateId, signal.CandidateId))
            return D022SliceCLifecycleAdmissionStatus.CandidateMismatch;
        if (!Same(_expectation.GoalId, signal.GoalId))
            return D022SliceCLifecycleAdmissionStatus.GoalMismatch;
        if (!Same(_expectation.IntentId, signal.IntentId))
            return D022SliceCLifecycleAdmissionStatus.IntentMismatch;
        if (_expectation.DecisionId != signal.DecisionId)
            return D022SliceCLifecycleAdmissionStatus.DecisionMismatch;
        if (_expectation.SnapshotId != signal.SnapshotId)
            return D022SliceCLifecycleAdmissionStatus.SnapshotMismatch;
        if (_expectation.SituationRevision != signal.SituationRevision)
        {
            return D022SliceCLifecycleAdmissionStatus
                .SituationRevisionMismatch;
        }
        if (!Same(_expectation.TicketId, signal.TicketId))
            return D022SliceCLifecycleAdmissionStatus.TicketMismatch;
        if (_expectation.ProposalId != signal.ProposalId
            || _expectation.ProposalSequence != signal.ProposalSequence)
        {
            return D022SliceCLifecycleAdmissionStatus.ProposalMismatch;
        }
        if (!Same(_expectation.SessionId, signal.SessionId))
            return D022SliceCLifecycleAdmissionStatus.SessionMismatch;
        if (!Same(_expectation.EpochId, signal.EpochId))
            return D022SliceCLifecycleAdmissionStatus.EpochMismatch;
        if (!Same(
                _expectation.RuntimeIdentity.RuntimeProfileId,
                signal.RuntimeIdentity.RuntimeProfileId)
            || !Same(
                _expectation.RuntimeIdentity.RuntimeInstanceId,
                signal.RuntimeIdentity.RuntimeInstanceId)
            || !Same(
                _expectation.RuntimeIdentity.OrchestratorSessionId,
                signal.RuntimeIdentity.OrchestratorSessionId)
            || !Same(
                _expectation.RuntimeIdentity.OwnerIdentity,
                signal.RuntimeIdentity.OwnerIdentity))
        {
            return D022SliceCLifecycleAdmissionStatus
                .RuntimeIdentityMismatch;
        }
        if (_expectation.RuntimeIdentity.Generation
            != signal.RuntimeIdentity.Generation)
        {
            return D022SliceCLifecycleAdmissionStatus
                .RuntimeGenerationMismatch;
        }
        if (_expectation.AssistConfigurationGeneration
            != signal.AssistConfigurationGeneration)
        {
            return D022SliceCLifecycleAdmissionStatus
                .ConfigurationGenerationMismatch;
        }
        if (!_expectation.SafetyEnvelope.Matches(signal.SafetyEnvelope))
        {
            return D022SliceCLifecycleAdmissionStatus.BodyBindingMismatch;
        }

        return D022SliceCLifecycleAdmissionStatus.AcceptedRecordOnly;
    }

    private bool TransitionIsValid(D022SliceCLifecycleSignal signal)
        => signal.Kind switch
        {
            D022SliceCLifecycleKind.QueueRejected =>
                !_started && signal.Reason != MotionIntentReason.None,
            D022SliceCLifecycleKind.PolicyRejected =>
                !_started && signal.Reason != MotionIntentReason.None,
            D022SliceCLifecycleKind.Started =>
                !_started && signal.Reason == MotionIntentReason.None,
            D022SliceCLifecycleKind.Completed =>
                _started && signal.Reason == MotionIntentReason.None,
            D022SliceCLifecycleKind.PlayerInterrupted =>
                _started
                && signal.Reason
                    == MotionIntentReason.PlayerInterrupted,
            D022SliceCLifecycleKind.Failed =>
                _started && signal.Reason != MotionIntentReason.None,
            D022SliceCLifecycleKind.TimedOut =>
                signal.Reason == MotionIntentReason.ExecutionTimedOut,
            _ => false
        };

    private static D022SliceCLifecycleAdmission Reject(
        D022SliceCLifecycleAdmissionStatus status)
        => new(status, null);

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);
}
