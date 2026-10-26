using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum D022SliceEAssistStatus
{
    Off = 0,
    ReadyToArm = 1,
    Armed = 2,
    Executing = 3,
    Fault = 4,
    Capacity = 5
}

internal enum D022SliceEOpportunityStatus
{
    LegacyPassThrough = 0,
    NonAllowlistedMotionUnchanged = 1,
    AssistNotArmed = 2,
    AssistOwnedAndAuthorized = 3,
    AssistOwnedButDecisionDenied = 4,
    AssistOwnedButGateRejected = 5,
    ProcessCandidateLimitReached = 6,
    Faulted = 7
}

internal enum D022SliceEArmStatus
{
    Armed = 0,
    AssistDisabled = 1,
    SourceDisabled = 2,
    AlreadyArmed = 3,
    CandidateAlreadyConsumed = 4,
    Faulted = 5
}

internal sealed class D022SliceEConfiguration
{
    internal bool AssistEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool SessionOpen { get; }
    internal bool EpochActive { get; }
    internal long ConfigurationGeneration { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string EpochId { get; }
    internal bool ContinuousIntegration { get; }

    internal D022SliceEConfiguration(
        bool assistEnabled,
        bool sourceEnabled,
        bool sessionOpen,
        bool epochActive,
        long configurationGeneration,
        CharacterRuntimeIdentity runtimeIdentity,
        string epochId,
        bool continuousIntegration = false)
    {
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }

        AssistEnabled = assistEnabled;
        SourceEnabled = sourceEnabled;
        SessionOpen = sessionOpen;
        EpochActive = epochActive;
        ConfigurationGeneration = configurationGeneration;
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        EpochId = D022ContractGuard.Identifier(
            epochId,
            nameof(epochId));
        ContinuousIntegration = continuousIntegration;
    }

    internal bool Matches(D022SliceEConfiguration other)
        => other != null
            && AssistEnabled == other.AssistEnabled
            && SourceEnabled == other.SourceEnabled
            && SessionOpen == other.SessionOpen
            && EpochActive == other.EpochActive
            && ConfigurationGeneration
                == other.ConfigurationGeneration
            && RuntimeIdentity.Matches(other.RuntimeIdentity)
            && ContinuousIntegration == other.ContinuousIntegration
            && string.Equals(
                EpochId,
                other.EpochId,
                StringComparison.Ordinal);
}

internal sealed class D022SliceELiveSituation
{
    internal long Revision { get; }
    internal DateTimeOffset EvaluatedAt { get; }
    internal bool IsUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }
    internal bool CapabilityAvailable { get; }
    internal bool CharacterAvailable { get; }
    internal bool IsGrounded { get; }
    internal bool IsNativeIdle { get; }
    internal bool IsPajamas { get; }

    internal D022SliceELiveSituation(
        long revision,
        DateTimeOffset evaluatedAt,
        bool isUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        bool capabilityAvailable,
        bool characterAvailable,
        bool isGrounded,
        bool isNativeIdle,
        bool isPajamas)
    {
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
        EvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        IsUnderDirectPlayerControl = isUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
        CapabilityAvailable = capabilityAvailable;
        CharacterAvailable = characterAvailable;
        IsGrounded = isGrounded;
        IsNativeIdle = isNativeIdle;
        IsPajamas = isPajamas;
    }

    internal bool AllowsBodyBinding =>
        !IsUnderDirectPlayerControl
        && !IsConversationBusy
        && !HasPendingExecution
        && !IsQuietPeriod
        && CapabilityAvailable
        && CharacterAvailable
        && IsGrounded
        && IsNativeIdle
        && !IsPajamas;
}

internal sealed class D022SliceEAuthorization
{
    internal D022SliceEOpportunityStatus Status { get; }
    internal D022SliceBGateResult Ownership { get; }
    internal MotionCandidateBinding? Binding { get; }
    internal NoEvidenceBaselineOutcome? Baseline { get; }
    internal AssistAuthorizationTicket? Ticket { get; }
    internal bool AllowsLegacyDirectSubmission =>
        Ownership.AllowsLegacyDirectSubmission;
    internal bool Authorized =>
        Status == D022SliceEOpportunityStatus
            .AssistOwnedAndAuthorized;
    internal bool UsesEvidenceLineage => false;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;

    internal D022SliceEAuthorization(
        D022SliceEOpportunityStatus status,
        D022SliceBGateResult ownership,
        MotionCandidateBinding? binding,
        NoEvidenceBaselineOutcome? baseline,
        AssistAuthorizationTicket? ticket)
    {
        Status = status;
        Ownership = ownership
            ?? throw new ArgumentNullException(nameof(ownership));
        Binding = binding;
        Baseline = baseline;
        Ticket = ticket;
    }
}

internal sealed class D022SliceEQueuePreparation
{
    internal D022SliceCIngressStatus Status { get; }
    internal D022SliceCRestrictedProposalEnvelope? Proposal { get; }
    internal D022SliceCOutcomeExpectation? Expectation { get; }
    internal bool Prepared =>
        Status == D022SliceCIngressStatus.PreparedRecordOnly;

    internal D022SliceEQueuePreparation(
        D022SliceCIngressStatus status,
        D022SliceCRestrictedProposalEnvelope? proposal,
        D022SliceCOutcomeExpectation? expectation)
    {
        Status = status;
        Proposal = proposal;
        Expectation = expectation;
    }
}

internal sealed class D022SliceEAssistSnapshot
{
    internal D022SliceEAssistStatus Status { get; }
    internal bool AssistEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool Armed { get; }
    internal bool CandidateConsumed { get; }
    internal long ConfigurationGeneration { get; }
    internal long SituationRevision { get; }
    internal int CandidateCount { get; }
    internal int QueueSubmissionCount { get; }
    internal int StartedCount { get; }
    internal int CompletedCount { get; }
    internal int PlayerInterruptedCount { get; }
    internal int FailedCount { get; }
    internal int RejectedSignalCount { get; }
    internal int SuccessfulAssistCount { get; }
    internal DateTimeOffset? CooldownUntil { get; }
    internal string LatestOpportunityId { get; }
    internal string LatestDecisionId { get; }
    internal string LatestProposalId { get; }
    internal string LatestTerminal { get; }
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;

    internal D022SliceEAssistSnapshot(
        D022SliceEAssistStatus status,
        bool assistEnabled,
        bool sourceEnabled,
        bool armed,
        bool candidateConsumed,
        long configurationGeneration,
        long situationRevision,
        int candidateCount,
        int queueSubmissionCount,
        int startedCount,
        int completedCount,
        int playerInterruptedCount,
        int failedCount,
        int rejectedSignalCount,
        int successfulAssistCount,
        DateTimeOffset? cooldownUntil,
        string latestOpportunityId,
        string latestDecisionId,
        string latestProposalId,
        string latestTerminal)
    {
        Status = status;
        AssistEnabled = assistEnabled;
        SourceEnabled = sourceEnabled;
        Armed = armed;
        CandidateConsumed = candidateConsumed;
        ConfigurationGeneration = configurationGeneration;
        SituationRevision = situationRevision;
        CandidateCount = candidateCount;
        QueueSubmissionCount = queueSubmissionCount;
        StartedCount = startedCount;
        CompletedCount = completedCount;
        PlayerInterruptedCount = playerInterruptedCount;
        FailedCount = failedCount;
        RejectedSignalCount = rejectedSignalCount;
        SuccessfulAssistCount = successfulAssistCount;
        CooldownUntil = cooldownUntil;
        LatestOpportunityId = latestOpportunityId;
        LatestDecisionId = latestDecisionId;
        LatestProposalId = latestProposalId;
        LatestTerminal = latestTerminal;
    }
}

internal sealed class D022SliceEAssistRuntimeHost
{
    internal const string ProducerId =
        "builtin.local-decision.baseline-assist.v1";

    private D022SliceEConfiguration? _configuration;
    private D022SliceEAssistStatus _status =
        D022SliceEAssistStatus.Off;
    private D022PlayOneShotOwnershipGate _ownershipGate = new();
    private D022SliceCRestrictedQueueIngress _restrictedIngress = new();
    private D022SliceEAuthorization? _authorization;
    private D022SliceCOutcomeExpectation? _expectation;
    private D022SliceCOutcomeLedger? _ledger;
    private bool _armed;
    private bool _candidateConsumed;
    private long _sourceSequence;
    private int _candidateCount;
    private int _queueSubmissionCount;
    private int _startedCount;
    private int _completedCount;
    private int _playerInterruptedCount;
    private int _failedCount;
    private int _rejectedSignalCount;
    private int _successfulAssistCount;
    private DateTimeOffset? _cooldownUntil;
    private readonly Queue<DateTimeOffset> _successfulAssistTimes = new();
    private string _latestOpportunityId = string.Empty;
    private string _latestDecisionId = string.Empty;
    private string _latestProposalId = string.Empty;
    private string _latestTerminal = string.Empty;

    internal D022SliceEAssistStatus Status => _status;
    internal bool HasActiveExpectation =>
        _expectation != null && _ledger != null && !_ledger.IsTerminal;
    internal D022SliceCOutcomeExpectation? ActiveExpectation =>
        _expectation;
    internal long ActiveSituationRevision =>
        _authorization?.Ticket?.SituationRevision ?? 0;

    internal void Reconcile(D022SliceEConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (!configuration.AssistEnabled)
        {
            ResetToOff(configuration);
            return;
        }

        if (_configuration == null
            || !_configuration.RuntimeIdentity.Matches(
                configuration.RuntimeIdentity)
            || _configuration.ConfigurationGeneration
                != configuration.ConfigurationGeneration
            || !string.Equals(
                _configuration.EpochId,
                configuration.EpochId,
                StringComparison.Ordinal)
            || _configuration.SessionOpen != configuration.SessionOpen
            || _configuration.EpochActive != configuration.EpochActive)
        {
            ResetProcessState(configuration);
        }
        else
        {
            _configuration = configuration;
        }

        if (_status == D022SliceEAssistStatus.Fault)
            return;
        if (!configuration.SourceEnabled
            || !configuration.SessionOpen
            || !configuration.EpochActive)
        {
            _armed = false;
            _status = D022SliceEAssistStatus.ReadyToArm;
            return;
        }
        if (HasActiveExpectation)
        {
            _status = D022SliceEAssistStatus.Executing;
            return;
        }
        if (_candidateConsumed)
        {
            if (!TryPrepareNextContinuousCandidate(DateTimeOffset.UtcNow))
            {
                _armed = false;
                _status = D022SliceEAssistStatus.Capacity;
                return;
            }
        }
        if (configuration.ContinuousIntegration)
            _armed = true;
        _status = _armed
            ? D022SliceEAssistStatus.Armed
            : D022SliceEAssistStatus.ReadyToArm;
    }

    internal D022SliceEArmStatus Arm()
    {
        if (_status == D022SliceEAssistStatus.Fault)
            return D022SliceEArmStatus.Faulted;
        if (_configuration == null
            || !_configuration.AssistEnabled)
        {
            return D022SliceEArmStatus.AssistDisabled;
        }
        if (!_configuration.SourceEnabled
            || !_configuration.SessionOpen
            || !_configuration.EpochActive)
        {
            return D022SliceEArmStatus.SourceDisabled;
        }
        if (_candidateConsumed)
        {
            return D022SliceEArmStatus.CandidateAlreadyConsumed;
        }
        if (_armed)
            return D022SliceEArmStatus.AlreadyArmed;

        _armed = true;
        _status = D022SliceEAssistStatus.Armed;
        return D022SliceEArmStatus.Armed;
    }

    internal D022SliceEAuthorization EvaluateOpportunity(
        MotionDirectorProposal proposal,
        D022SliceELiveSituation situation,
        string bodyBindingVersion)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        if (situation == null)
            throw new ArgumentNullException(nameof(situation));
        bodyBindingVersion = D022ContractGuard.Identifier(
            bodyBindingVersion,
            nameof(bodyBindingVersion));

        var defaultOwnership =
            D022PlayOneShotOwnershipGate.EvaluateDefaultOff(proposal);
        if (!D022SliceBGateResult.IsAllowlistedOccurrence(proposal))
        {
            return new D022SliceEAuthorization(
                D022SliceEOpportunityStatus
                    .NonAllowlistedMotionUnchanged,
                defaultOwnership,
                null,
                null,
                null);
        }
        if (_status == D022SliceEAssistStatus.Fault)
        {
            return new D022SliceEAuthorization(
                D022SliceEOpportunityStatus.Faulted,
                defaultOwnership,
                null,
                null,
                null);
        }
        if (_configuration?.ContinuousIntegration == true)
            TryPrepareNextContinuousCandidate(situation.EvaluatedAt);
        if (_configuration == null
            || !_configuration.AssistEnabled
            || !_armed)
        {
            return new D022SliceEAuthorization(
                _configuration?.AssistEnabled == true
                    ? D022SliceEOpportunityStatus.AssistNotArmed
                    : D022SliceEOpportunityStatus.LegacyPassThrough,
                defaultOwnership,
                null,
                null,
                null);
        }
        if (_candidateConsumed)
        {
            _armed = false;
            _status = D022SliceEAssistStatus.Capacity;
            return new D022SliceEAuthorization(
                D022SliceEOpportunityStatus
                    .ProcessCandidateLimitReached,
                defaultOwnership,
                null,
                null,
                null);
        }

        _candidateConsumed = true;
        _armed = false;
        _candidateCount++;
        var sequence = ++_sourceSequence;
        var occurrenceId = "d022-opportunity-" + sequence;
        var context = new D022SliceBOpportunityContext(
            occurrenceId,
            sequence,
            situation.EvaluatedAt,
            situation.EvaluatedAt,
            situation.EvaluatedAt.Add(
                D022SliceACeilings.CandidateLifetime),
            _configuration.RuntimeIdentity.OrchestratorSessionId,
            _configuration.EpochId,
            _configuration.RuntimeIdentity);
        var ownership = _ownershipGate.ClaimForDeterministicBoundary(
            proposal,
            context,
            _configuration.RuntimeIdentity,
            requestAssistOwnership: true);
        _latestOpportunityId = occurrenceId;
        if (ownership.Ownership
            != D022OpportunityOwnership.AssistOwned
            || ownership.Opportunity == null)
        {
            _status = D022SliceEAssistStatus.Capacity;
            return new D022SliceEAuthorization(
                D022SliceEOpportunityStatus
                    .AssistOwnedButGateRejected,
                ownership,
                null,
                null,
                null);
        }

        var candidate = CreateCandidate(ownership.Opportunity);
        var goal = CreateGoal(candidate);
        var intent = new CharacterNeutralMotionIntent(
            "d022-intent-" + sequence);
        var safety = new CharacterBodyBindingSafetyEnvelope(
            bodyBindingVersion,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4));
        var binding = new MotionCandidateBinding(
            "d022-binding-" + sequence,
            ownership.Opportunity,
            candidate,
            "d022-fingerprint-play-one-shot",
            goal,
            intent,
            safety);
        var snapshot = CreateSituationSnapshot(goal, situation);
        var evaluation = LocalDecisionCore.Evaluate(
            snapshot,
            situation.EvaluatedAt);
        if (evaluation.Outcome.Disposition
                != DecisionDisposition.Act
            || evaluation.Outcome.Reason
                != DecisionReason.GoalSelected
            || evaluation.Outcome.Invocation != null
            || !situation.AllowsBodyBinding)
        {
            _status = D022SliceEAssistStatus.Capacity;
            _authorization = new D022SliceEAuthorization(
                D022SliceEOpportunityStatus
                    .AssistOwnedButDecisionDenied,
                ownership,
                binding,
                null,
                null);
            return _authorization;
        }

        var baseline = D022NoEvidenceBaselineProjector.Create(
            binding,
            evaluation,
            situation.Revision,
            situation.EvaluatedAt);
        var ticket = new AssistAuthorizationTicket(
            "d022-ticket-" + sequence,
            "d022-nonce-" + sequence,
            binding,
            baseline,
            _configuration.EpochId,
            _configuration.ConfigurationGeneration,
            D022TicketTransactionBoundary.SameMainThreadTransaction,
            situation.EvaluatedAt);
        _latestDecisionId =
            baseline.DecisionId.ToString("N");
        _authorization = new D022SliceEAuthorization(
            D022SliceEOpportunityStatus.AssistOwnedAndAuthorized,
            ownership,
            binding,
            baseline,
            ticket);
        _status = D022SliceEAssistStatus.Armed;
        return _authorization;
    }

    internal D022SliceEQueuePreparation PrepareRestrictedQueue(
        MotionProposal prospectiveProposal,
        D022SliceELiveSituation situation)
    {
        if (prospectiveProposal == null)
            throw new ArgumentNullException(nameof(prospectiveProposal));
        if (situation == null)
            throw new ArgumentNullException(nameof(situation));
        if (_configuration == null
            || _authorization == null
            || !_authorization.Authorized
            || _authorization.Binding == null
            || _authorization.Baseline == null
            || _authorization.Ticket == null)
        {
            return new D022SliceEQueuePreparation(
                D022SliceCIngressStatus.ProposalIdentityInvalid,
                null,
                null);
        }

        var gate = CreateGateSnapshot(situation);
        var ticket = _authorization.Ticket;
        var consumption = new D022TicketConsumptionContext(
            ticket.TicketId,
            ticket.OpportunityId,
            ticket.CandidateId,
            ticket.GoalId,
            ticket.IntentId,
            ticket.SafetyEnvelope,
            ticket.DecisionId,
            ticket.SnapshotId,
            ticket.SituationRevision,
            ticket.SessionId,
            ticket.EpochId,
            ticket.RuntimeIdentity,
            ticket.AssistConfigurationGeneration,
            isSameMainThreadTransaction: true,
            situation.EvaluatedAt);
        var request = new D022SliceCRestrictedIngressRequest(
            ticket,
            _authorization.Binding,
            _authorization.Baseline,
            gate,
            consumption,
            prospectiveProposal.Id,
            prospectiveProposal.Sequence,
            "d022-expectation-" + _sourceSequence,
            opportunityIsAssistOwned: true);
        var prepared = _restrictedIngress.Prepare(request);
        if (!prepared.Prepared
            || prepared.Proposal == null
            || prepared.Expectation == null)
        {
            return new D022SliceEQueuePreparation(
                prepared.Status,
                null,
                null);
        }
        var revalidated =
            _restrictedIngress.RevalidateBeforeFutureQueueSubmission(
                prepared.Proposal,
                ticket,
                _authorization.Binding,
                gate);
        if (revalidated
            != D022SliceCIngressStatus.PreparedRecordOnly)
        {
            return new D022SliceEQueuePreparation(
                revalidated,
                null,
                null);
        }

        _expectation = prepared.Expectation;
        _ledger = new D022SliceCOutcomeLedger(_expectation);
        _latestProposalId =
            prospectiveProposal.Id.ToString("N");
        return new D022SliceEQueuePreparation(
            D022SliceCIngressStatus.PreparedRecordOnly,
            prepared.Proposal,
            prepared.Expectation);
    }

    internal void RecordQueueSubmission(bool accepted)
    {
        if (_expectation == null || _ledger == null)
            return;
        if (accepted)
        {
            _queueSubmissionCount++;
            _status = D022SliceEAssistStatus.Executing;
            return;
        }

        RecordLifecycle(
            D022SliceCLifecycleKind.QueueRejected,
            MotionIntentReason.ExecutionFailed,
            _expectation.RegisteredAt);
    }

    internal D022SliceCLifecycleAdmission? RecordCompletion(
        MotionProposalCompletion completion)
    {
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        if (_expectation == null
            || _ledger == null
            || completion.ProposalId != _expectation.ProposalId
            || completion.Sequence != _expectation.ProposalSequence
            || !string.Equals(
                completion.ProducerId,
                ProducerId,
                StringComparison.Ordinal)
            || completion.Source != MotionIntentSource.Initiative
            || completion.Kind != MotionIntentKind.PlayOneShot)
        {
            _rejectedSignalCount++;
            return null;
        }

        var kind = completion.Status switch
        {
            MotionIntentStatus.Completed =>
                D022SliceCLifecycleKind.Completed,
            MotionIntentStatus.Interrupted
                when completion.Reason
                    == MotionIntentReason.PlayerInterrupted =>
                D022SliceCLifecycleKind.PlayerInterrupted,
            MotionIntentStatus.Rejected =>
                D022SliceCLifecycleKind.PolicyRejected,
            MotionIntentStatus.Failed
                when completion.Reason
                    == MotionIntentReason.ExecutionTimedOut =>
                D022SliceCLifecycleKind.TimedOut,
            _ => D022SliceCLifecycleKind.Failed
        };
        return RecordLifecycle(
            kind,
            completion.Reason,
            completion.RecordedAt);
    }

    internal D022SliceCLifecycleAdmission? RecordLifecycle(
        D022SliceCLifecycleKind kind,
        MotionIntentReason reason,
        DateTimeOffset recordedAt)
    {
        if (_expectation == null || _ledger == null)
            return null;
        var signal = new D022SliceCLifecycleSignal(
            "d022-signal-" + kind + "-" + (
                _startedCount
                + _completedCount
                + _playerInterruptedCount
                + _failedCount
                + _rejectedSignalCount
                + 1),
            _expectation,
            kind,
            reason,
            recordedAt);
        var admission = _ledger.Observe(signal);
        if (!admission.Accepted)
        {
            _rejectedSignalCount++;
            return admission;
        }

        switch (kind)
        {
            case D022SliceCLifecycleKind.Started:
                _startedCount++;
                _status = D022SliceEAssistStatus.Executing;
                break;
            case D022SliceCLifecycleKind.Completed:
                _completedCount++;
                _successfulAssistCount++;
                _cooldownUntil = recordedAt.Add(
                    D022SliceACeilings.SuccessfulAssistCooldown);
                _successfulAssistTimes.Enqueue(recordedAt);
                _latestTerminal = kind.ToString();
                _status = D022SliceEAssistStatus.Capacity;
                break;
            case D022SliceCLifecycleKind.PlayerInterrupted:
                _playerInterruptedCount++;
                _latestTerminal = kind.ToString();
                _status = D022SliceEAssistStatus.Capacity;
                break;
            case D022SliceCLifecycleKind.QueueRejected:
            case D022SliceCLifecycleKind.PolicyRejected:
            case D022SliceCLifecycleKind.Failed:
            case D022SliceCLifecycleKind.TimedOut:
                _failedCount++;
                _latestTerminal = kind.ToString();
                _status = D022SliceEAssistStatus.Capacity;
                break;
        }
        return admission;
    }

    internal bool MatchesActiveProposal(MotionProposal proposal)
        => proposal != null
            && _expectation != null
            && proposal.Id == _expectation.ProposalId
            && proposal.Sequence == _expectation.ProposalSequence
            && string.Equals(
                proposal.ProducerId,
                ProducerId,
                StringComparison.Ordinal)
            && proposal.Kind == MotionIntentKind.PlayOneShot
            && proposal.Source == MotionIntentSource.Initiative;

    internal bool RevalidateBeforeExecution(
        MotionProposal proposal,
        D022SliceELiveSituation situation)
    {
        if (!MatchesActiveProposal(proposal)
            || _configuration == null
            || !_configuration.AssistEnabled
            || !_configuration.SourceEnabled
            || !_configuration.SessionOpen
            || !_configuration.EpochActive
            || !situation.AllowsBodyBinding
            || _authorization?.Ticket == null)
        {
            return false;
        }
        var ticket = _authorization.Ticket;
        return ticket.RuntimeIdentity.Matches(
                _configuration.RuntimeIdentity)
            && ticket.AssistConfigurationGeneration
                == _configuration.ConfigurationGeneration
            && string.Equals(
                ticket.EpochId,
                _configuration.EpochId,
                StringComparison.Ordinal)
            && situation.Revision == ticket.SituationRevision
            && situation.EvaluatedAt
                < _expectation!.QueueAdmissionDeadline;
    }

    internal void StopByFault()
    {
        _armed = false;
        _status = D022SliceEAssistStatus.Fault;
    }

    internal D022SliceEAssistSnapshot GetSnapshot()
        => new(
            _status,
            _configuration?.AssistEnabled == true,
            _configuration?.SourceEnabled == true,
            _armed,
            _candidateConsumed,
            _configuration?.ConfigurationGeneration ?? 0,
            _authorization?.Ticket?.SituationRevision ?? 0,
            _candidateCount,
            _queueSubmissionCount,
            _startedCount,
            _completedCount,
            _playerInterruptedCount,
            _failedCount,
            _rejectedSignalCount,
            _successfulAssistCount,
            _cooldownUntil,
            _latestOpportunityId,
            _latestDecisionId,
            _latestProposalId,
            _latestTerminal);

    private D022SliceCSituationGateSnapshot CreateGateSnapshot(
        D022SliceELiveSituation situation)
        => new(
            _configuration!.RuntimeIdentity.OrchestratorSessionId,
            _configuration.EpochId,
            situation.Revision,
            _configuration.RuntimeIdentity,
            _configuration.ConfigurationGeneration,
            situation.EvaluatedAt,
            masterEnabled: true,
            assistEnabled: _configuration.AssistEnabled,
            sourceEnabled: _configuration.SourceEnabled,
            sessionOpen: _configuration.SessionOpen,
            epochActive: _configuration.EpochActive,
            situation.IsUnderDirectPlayerControl,
            situation.IsConversationBusy,
            situation.HasPendingExecution,
            situation.IsQuietPeriod,
            situation.CapabilityAvailable,
            situation.CharacterAvailable,
            situation.IsGrounded,
            situation.IsNativeIdle);

    private static InitiativeCandidate CreateCandidate(
        MotionOpportunityIdentity opportunity)
        => new(
            "d022-candidate-" + opportunity.SourceSequence,
            opportunity.SessionId,
            "d022-correlation-" + opportunity.SourceSequence,
            opportunity.SourceId,
            "d022-play-one-shot",
            InitiativeCandidateKind.MotionOpportunity,
            InitiativeReasonCode.MotionDecisionOpportunity,
            InitiativeSensitivity.LocalGameMetadata,
            InitiativeCapabilityClass.Motion,
            SituationTriggerKind.Timer,
            DecisionDisposition.Act,
            DecisionEvidence.None,
            urgency: 0.5,
            attentionCost: 0.2,
            interruptionCost: 0.1,
            requiresConsent: false,
            opportunity.OccurredAt,
            opportunity.ObservedAt,
            opportunity.ExpiresAt);

    private static GoalState CreateGoal(
        InitiativeCandidate candidate)
        => new(
            candidate.Id,
            candidate.SessionId,
            GoalSource.Initiative,
            GoalPriorityClass.Initiative,
            GoalStatus.Candidate,
            GoalPersistenceScope.Session,
            candidate.DesiredDisposition,
            candidate.RequiredEvidence,
            candidate.Urgency,
            candidate.InterruptionCost,
            candidate.RequiresConsent,
            candidate.ObservedAt,
            deadline: null,
            candidate.ExpiresAt,
            evidenceCandidateId: candidate.Id);

    private static SituationSnapshot CreateSituationSnapshot(
        GoalState goal,
        D022SliceELiveSituation situation)
        => new(
            StableGuid(situation.Revision),
            situation.Revision,
            goal.SessionId,
            situation.EvaluatedAt,
            situation.EvaluatedAt.AddSeconds(2),
            new SituationTrigger(
                "d022-trigger-" + situation.Revision,
                SituationTriggerKind.Timer,
                situation.EvaluatedAt),
            SituationConflictLevel.None,
            situation.IsUnderDirectPlayerControl,
            situation.IsConversationBusy,
            situation.HasPendingExecution,
            situation.IsQuietPeriod,
            isInitiativeCooldownActive: false,
            DecisionEvidence.None,
            DecisionBudgets.ShadowDefault,
            new AttentionState(
                AttentionFocusKind.Initiative,
                goal.Id,
                situation.EvaluatedAt),
            new[] { goal });

    private static Guid StableGuid(long value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        BitConverter.GetBytes(~value).CopyTo(bytes, 8);
        return new Guid(bytes);
    }

    private void ResetToOff(D022SliceEConfiguration configuration)
    {
        _configuration = configuration;
        _armed = false;
        _authorization = null;
        _expectation = null;
        _ledger = null;
        _status = D022SliceEAssistStatus.Off;
    }

    private bool TryPrepareNextContinuousCandidate(DateTimeOffset now)
    {
        if (_configuration?.ContinuousIntegration != true
            || !_candidateConsumed
            || HasActiveExpectation
            || _status == D022SliceEAssistStatus.Fault)
        {
            return false;
        }
        while (_successfulAssistTimes.Count > 0
               && now - _successfulAssistTimes.Peek()
                   >= D022SliceACeilings.SuccessfulAssistRollingWindow)
        {
            _successfulAssistTimes.Dequeue();
        }
        if (_successfulAssistCount
                >= D022SliceACeilings.MaximumSuccessfulAssistsPerProcessLifetime
            || _successfulAssistTimes.Count
                >= D022SliceACeilings.MaximumSuccessfulAssistsPerRollingWindow
            || (_cooldownUntil.HasValue && now < _cooldownUntil.Value))
        {
            return false;
        }
        _authorization = null;
        _expectation = null;
        _ledger = null;
        _candidateConsumed = false;
        _restrictedIngress = new D022SliceCRestrictedQueueIngress();
        _armed = true;
        _status = D022SliceEAssistStatus.Armed;
        return true;
    }

    private void ResetProcessState(
        D022SliceEConfiguration configuration)
    {
        var newRuntime = _configuration == null
            || !_configuration.RuntimeIdentity.Matches(
                configuration.RuntimeIdentity);
        _configuration = configuration;
        _armed = false;
        _authorization = null;
        _expectation = null;
        _ledger = null;
        if (newRuntime)
        {
            _candidateConsumed = false;
            _ownershipGate = new D022PlayOneShotOwnershipGate();
            _restrictedIngress =
                new D022SliceCRestrictedQueueIngress();
            _sourceSequence = 0;
            _candidateCount = 0;
            _queueSubmissionCount = 0;
            _startedCount = 0;
            _completedCount = 0;
            _playerInterruptedCount = 0;
            _failedCount = 0;
            _rejectedSignalCount = 0;
            _successfulAssistCount = 0;
            _cooldownUntil = null;
            _successfulAssistTimes.Clear();
            _latestOpportunityId = string.Empty;
            _latestDecisionId = string.Empty;
            _latestProposalId = string.Empty;
            _latestTerminal = string.Empty;
        }
        _status = D022SliceEAssistStatus.ReadyToArm;
    }
}
