using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum ProactiveRespondMode
{
    Off = 0,
    Observe = 1,
    AssistPrepared = 2,
    Formal = 3
}

internal enum ProactiveRespondPurpose
{
    FollowUpUpcomingEvent = 0,
    CelebrateAchievement = 1,
    OfferComfort = 2,
    RevisitCommitment = 3,
    AcknowledgeBond = 4
}

internal enum ProactiveRespondAdmission
{
    Disabled = 0,
    ObserveOnly = 1,
    ArmRequired = 2,
    SourceDisabled = 3,
    MasterDisabled = 4,
    RuntimeMismatch = 5,
    SituationMismatch = 6,
    Expired = 7,
    OwnershipConflict = 8,
    LegacyAlreadyCompleted = 9,
    BrainOwnedNoFallback = 10,
    DecisionDidNotRespond = 11,
    EvaluationRateLimited = 12,
    CooldownActive = 13,
    RollingCapacityReached = 14,
    ProcessCapacityReached = 15,
    ActiveCapacityReached = 16,
    AuthorizationPrepared = 17,
    DuplicateTerminal = 18,
    OutcomeRecorded = 19,
    OutcomeRejected = 20,
    EpochMismatch = 21
}

internal sealed class ProactiveRespondPolicy
{
    internal static readonly TimeSpan MinimumEvaluationInterval =
        TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan SuccessfulCooldown =
        TimeSpan.FromHours(24);
    internal static readonly TimeSpan RollingWindow =
        TimeSpan.FromDays(7);
    internal const int MaximumSuccessfulPerRollingWindow = 3;
    internal const int MaximumAuthorizationsPerProcess = 3;
    internal const int MaximumActive = 1;

    internal static ProactiveRespondPolicy Off { get; } = new(
        ProactiveRespondMode.Off,
        explicitlyArmed: false);

    internal ProactiveRespondMode Mode { get; }
    internal bool ExplicitlyArmed { get; }
    internal bool Enabled => Mode != ProactiveRespondMode.Off;
    internal bool RequiresExplicitArm =>
        Mode == ProactiveRespondMode.AssistPrepared;
    internal bool IsFormal => Mode == ProactiveRespondMode.Formal;

    internal ProactiveRespondPolicy(
        ProactiveRespondMode mode,
        bool explicitlyArmed)
    {
        if (!Enum.IsDefined(typeof(ProactiveRespondMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode != ProactiveRespondMode.AssistPrepared
            && explicitlyArmed)
        {
            throw new ArgumentException(
                "Only AssistPrepared may be explicitly armed.",
                nameof(explicitlyArmed));
        }
        Mode = mode;
        ExplicitlyArmed = explicitlyArmed;
    }
}

internal sealed class ProactiveRespondOpportunity
{
    internal static readonly TimeSpan MaximumLifetime =
        TimeSpan.FromMinutes(30);

    internal Guid OccurrenceId { get; }
    internal string SessionId { get; }
    internal string EpochId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal ProactiveRespondPurpose Purpose { get; }
    internal string SourceContentHash { get; }
    internal DateTimeOffset OccurredAt { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string OccurrenceToken => OccurrenceId.ToString("N");

    internal ProactiveRespondOpportunity(
        Guid occurrenceId,
        string sessionId,
        CharacterRuntimeIdentity runtimeIdentity,
        Guid situationId,
        long situationRevision,
        ProactiveRespondPurpose purpose,
        string sourceContentHash,
        DateTimeOffset occurredAt,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        string? epochId = null)
    {
        if (occurrenceId == Guid.Empty)
            throw new ArgumentException(
                "Proactive occurrence ID cannot be empty.",
                nameof(occurrenceId));
        SessionId = LivingContinuityContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        EpochId = LivingContinuityContractGuard.Identifier(
            epochId ?? "process-epoch-" + SessionId,
            nameof(epochId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                sessionId,
                runtimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Opportunity and runtime must share a session.",
                nameof(sessionId));
        }
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        if (!Enum.IsDefined(typeof(ProactiveRespondPurpose), purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        var occurred = LivingContinuityContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        var observed = LivingContinuityContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        var expiry = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (observed < occurred
            || expiry <= observed
            || expiry - occurred > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        OccurrenceId = occurrenceId;
        SituationId = situationId;
        SituationRevision = situationRevision;
        Purpose = purpose;
        SourceContentHash = LivingContinuityContractGuard.Sha256(
            sourceContentHash,
            nameof(sourceContentHash));
        OccurredAt = occurred;
        ObservedAt = observed;
        ExpiresAt = expiry;
    }

    internal InitiativeCandidate ToBaselineCandidate()
        => new(
            "d025-note-" + OccurrenceToken,
            SessionId,
            "d025-note-correlation-" + OccurrenceToken,
            InitiativeProducerSourceIds.AiNoteScheduler,
            "d025.ai-note:" + OccurrenceToken,
            InitiativeCandidateKind.AiNoteDue,
            InitiativeReasonCode.ScheduledNoteDue,
            InitiativeSensitivity.LocalGameMetadata,
            InitiativeCapabilityClass.Response,
            SituationTriggerKind.Timer,
            DecisionDisposition.Respond,
            DecisionEvidence.None,
            urgency: 0.4,
            attentionCost: 0.35,
            interruptionCost: 0.3,
            requiresConsent: false,
            OccurredAt,
            ObservedAt,
            ExpiresAt);
}

internal sealed class ProactiveRespondSituation
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string EpochId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal bool MasterEnabled { get; }
    internal bool SourceEnabled { get; }
    internal bool IsAvatarUnderDirectPlayerControl { get; }
    internal bool IsConversationBusy { get; }
    internal bool HasPendingExecution { get; }
    internal bool IsQuietPeriod { get; }

    internal ProactiveRespondSituation(
        CharacterRuntimeIdentity runtimeIdentity,
        Guid situationId,
        long situationRevision,
        bool masterEnabled,
        bool sourceEnabled,
        bool isAvatarUnderDirectPlayerControl,
        bool isConversationBusy,
        bool hasPendingExecution,
        bool isQuietPeriod,
        string? epochId = null)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        EpochId = LivingContinuityContractGuard.Identifier(
            epochId
                ?? "process-epoch-"
                + runtimeIdentity.OrchestratorSessionId,
            nameof(epochId));
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        SituationId = situationId;
        SituationRevision = situationRevision;
        MasterEnabled = masterEnabled;
        SourceEnabled = sourceEnabled;
        IsAvatarUnderDirectPlayerControl =
            isAvatarUnderDirectPlayerControl;
        IsConversationBusy = isConversationBusy;
        HasPendingExecution = hasPendingExecution;
        IsQuietPeriod = isQuietPeriod;
    }
}

internal sealed class ProactiveRespondAuthorization
{
    internal string AuthorizationId { get; }
    internal Guid OccurrenceId { get; }
    internal string CandidateId { get; }
    internal Guid DecisionId { get; }
    internal Guid SnapshotId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string EpochId { get; }
    internal ProactiveRespondPurpose Purpose { get; }
    internal string SourceContentHash { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal DecisionDisposition Disposition => DecisionDisposition.Respond;
    internal InitiativeCapabilityClass Capability =>
        InitiativeCapabilityClass.Response;

    internal ProactiveRespondAuthorization(
        string authorizationId,
        ProactiveRespondOpportunity opportunity,
        InitiativeCandidate candidate,
        DecisionEvaluation evaluation,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        AuthorizationId = LivingContinuityContractGuard.Identifier(
            authorizationId,
            nameof(authorizationId));
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));
        if (evaluation == null)
            throw new ArgumentNullException(nameof(evaluation));
        if (evaluation.Outcome.Disposition != DecisionDisposition.Respond
            || evaluation.Outcome.Reason != DecisionReason.GoalSelected
            || !string.Equals(
                evaluation.Outcome.SelectedGoalId,
                candidate.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Authorization requires the exact accepted Respond decision.",
                nameof(evaluation));
        }
        if (evaluation.Trace.SnapshotId != opportunity.SituationId
            || evaluation.Trace.SnapshotSequence
                != opportunity.SituationRevision)
        {
            throw new ArgumentException(
                "Decision and opportunity Situation must match.",
                nameof(evaluation));
        }
        var issued = LivingContinuityContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        var expiry = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (expiry <= issued || expiry - issued > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(expiresAt));

        OccurrenceId = opportunity.OccurrenceId;
        CandidateId = candidate.Id;
        DecisionId = evaluation.Trace.DecisionId;
        SnapshotId = evaluation.Trace.SnapshotId;
        SituationRevision = evaluation.Trace.SnapshotSequence;
        RuntimeIdentity = opportunity.RuntimeIdentity;
        EpochId = opportunity.EpochId;
        Purpose = opportunity.Purpose;
        SourceContentHash = opportunity.SourceContentHash;
        IssuedAt = issued;
        ExpiresAt = expiry;
    }

    internal string CorrelationFingerprint()
    {
        var value = string.Join(
            "|",
            AuthorizationId,
            OccurrenceId.ToString("N"),
            CandidateId,
            DecisionId.ToString("N"),
            SnapshotId.ToString("N"),
            SituationRevision.ToString(CultureInfo.InvariantCulture),
            RuntimeIdentity.RuntimeInstanceId,
            RuntimeIdentity.Generation.ToString(CultureInfo.InvariantCulture),
            EpochId,
            ((int)Purpose).ToString(CultureInfo.InvariantCulture),
            SourceContentHash);
        using var sha = SHA256.Create();
        return Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}

internal sealed class ProactiveRespondResult
{
    internal ProactiveRespondAdmission Admission { get; }
    internal InitiativeCandidate? Candidate { get; }
    internal DecisionEvaluation? Evaluation { get; }
    internal ProactiveRespondAuthorization? Authorization { get; }
    internal ContinuityOwnershipRecord? Ownership { get; }
    internal bool SuppressLegacy { get; }
    internal bool LegacyMayExecute => !SuppressLegacy;
    internal LivingContinuityAuthorityCounters AuthorityCounters { get; }

    internal ProactiveRespondResult(
        ProactiveRespondAdmission admission,
        InitiativeCandidate? candidate,
        DecisionEvaluation? evaluation,
        ProactiveRespondAuthorization? authorization,
        ContinuityOwnershipRecord? ownership,
        bool suppressLegacy)
    {
        if (!Enum.IsDefined(typeof(ProactiveRespondAdmission), admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        if (authorization != null
            && (candidate == null || evaluation == null))
        {
            throw new ArgumentException(
                "Authorization requires Candidate and Decision.");
        }
        Admission = admission;
        Candidate = candidate;
        Evaluation = evaluation;
        Authorization = authorization;
        Ownership = ownership;
        SuppressLegacy = suppressLegacy;
        AuthorityCounters = LivingContinuityAuthorityCounters.Zero;
    }
}
