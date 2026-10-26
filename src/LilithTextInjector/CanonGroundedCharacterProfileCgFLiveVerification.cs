using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterCanonProfileCgFLiveStatus
{
    Off = 0,
    ReadyToArm = 1,
    Armed = 2,
    AwaitingCompletion = 3,
    Completed = 4,
    Capacity = 5,
    Fault = 6,
    Cancelled = 7
}

internal enum CharacterCanonProfileCgFLiveAdmission
{
    Disabled = 0,
    ReadyToArm = 1,
    ArmAccepted = 2,
    AlreadyArmed = 3,
    ProcessArmAlreadyConsumed = 4,
    LegacyAssistConflict = 5,
    ObservationIneligible = 6,
    AuthorizationExpired = 7,
    RuntimeMismatch = 8,
    SessionMismatch = 9,
    SituationMismatch = 10,
    ProjectionUnavailable = 11,
    ProjectionExpired = 12,
    ProfileMismatch = 13,
    ProviderRouteMismatch = 14,
    PromptBaselineMismatch = 15,
    PromptOwnershipRejected = 16,
    LegacyOwnerRetained = 17,
    CandidatePrepared = 18,
    ApplicationFailedClosedAfterOwnership = 19,
    CompletionNotExpected = 20,
    CompletionIdentityMismatch = 21,
    CompletionExpired = 22,
    CompletionRecorded = 23,
    RestartCleared = 24,
    Faulted = 25,
    SessionExpired = 26,
    SessionCapacityReached = 27,
    ApplicationAlreadyActive = 28,
    ProbeCancelled = 29,
    ConfigurationConflict = 30
}

internal sealed class CharacterCanonProfileCgFLiveConfiguration
{
    internal const int MultiTurnMaximumReplies = 8;
    internal static readonly TimeSpan MultiTurnSessionLifetime =
        TimeSpan.FromMinutes(20);

    internal static CharacterCanonProfileCgFLiveConfiguration Off {
        get;
    } = new(enabled: false);

    internal bool Enabled { get; }
    internal int MaximumReplies { get; }
    internal TimeSpan SessionLifetime { get; }
    internal bool IsMultiTurn => MaximumReplies > 1;

    internal CharacterCanonProfileCgFLiveConfiguration(bool enabled)
        : this(enabled, maximumReplies: 1, TimeSpan.Zero)
    {
    }

    internal CharacterCanonProfileCgFLiveConfiguration(
        bool enabled,
        int maximumReplies,
        TimeSpan sessionLifetime)
    {
        if (maximumReplies <= 0
            || maximumReplies
                > MultiTurnMaximumReplies)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReplies));
        }
        if (maximumReplies == 1
            && sessionLifetime != TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionLifetime));
        }
        if (maximumReplies > 1
            && sessionLifetime
                != MultiTurnSessionLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionLifetime));
        }
        Enabled = enabled;
        MaximumReplies = maximumReplies;
        SessionLifetime = sessionLifetime;
    }
}

internal sealed class CharacterCanonProfileCgFLiveApplication
{
    internal string ApplicationId { get; }
    internal CharacterCanonProfileCgFApplicationCandidate Candidate {
        get;
    }
    internal CharacterCanonRuntimeProfileOwner Owner =>
        CharacterCanonRuntimeProfileOwner.CanonGroundedProfileV2;
    internal string PromptForExistingProviderCall =>
        Candidate.CandidatePrompt;
    internal DateTimeOffset AppliedAt { get; }
    internal DateTimeOffset CompletionDeadline { get; }
    internal int AdditionalProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal bool LegacyFallbackAllowed => false;

    internal CharacterCanonProfileCgFLiveApplication(
        string applicationId,
        CharacterCanonProfileCgFApplicationCandidate candidate,
        DateTimeOffset appliedAt,
        DateTimeOffset completionDeadline)
    {
        ApplicationId = CharacterRuntimeContractGuard.Identifier(
            applicationId,
            nameof(applicationId));
        Candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        AppliedAt = CharacterRuntimeContractGuard.Timestamp(
            appliedAt,
            nameof(appliedAt));
        CompletionDeadline = CharacterRuntimeContractGuard.Timestamp(
            completionDeadline,
            nameof(completionDeadline));
        if (CompletionDeadline <= AppliedAt)
            throw new ArgumentOutOfRangeException(
                nameof(completionDeadline));
    }
}

internal sealed class CharacterCanonProfileCgFLiveCompletion
{
    internal string CompletionId { get; }
    internal string ApplicationId { get; }
    internal string CandidateId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterCanonRuntimeProfileOwner Owner { get; }
    internal string CandidateProfileId { get; }
    internal int CandidateProfileVersion { get; }
    internal string StyleId { get; }
    internal string BaselinePromptSha256 { get; }
    internal string CandidatePromptSha256 { get; }
    internal int ReplyCharacters { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal int AdditionalProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal bool ContainsPromptText => false;
    internal bool ContainsReplyText => false;
    internal bool MayLearn => false;
    internal bool MayPersist => false;

    internal CharacterCanonProfileCgFLiveCompletion(
        string completionId,
        CharacterCanonProfileCgFLiveApplication application,
        int replyCharacters,
        DateTimeOffset completedAt)
    {
        if (application == null)
            throw new ArgumentNullException(nameof(application));
        if (replyCharacters < 0)
            throw new ArgumentOutOfRangeException(
                nameof(replyCharacters));
        CompletionId = CharacterRuntimeContractGuard.Identifier(
            completionId,
            nameof(completionId));
        ApplicationId = application.ApplicationId;
        CandidateId = application.Candidate.CandidateId;
        AuthorizationId = application.Candidate.AuthorizationId;
        TurnId = application.Candidate.TurnId;
        RuntimeIdentity = application.Candidate.RuntimeIdentity;
        Owner = application.Owner;
        CandidateProfileId =
            application.Candidate.CandidateProfileId;
        CandidateProfileVersion =
            application.Candidate.CandidateProfileVersion;
        StyleId = application.Candidate.StyleId;
        BaselinePromptSha256 =
            application.Candidate.BaselinePromptSha256;
        CandidatePromptSha256 =
            application.Candidate.CandidatePromptSha256;
        ReplyCharacters = replyCharacters;
        CompletedAt = CharacterRuntimeContractGuard.Timestamp(
            completedAt,
            nameof(completedAt));
    }
}

internal sealed class CharacterCanonProfileCgFLiveSnapshot
{
    internal static CharacterCanonProfileCgFLiveSnapshot Empty {
        get;
    } = new(
        CharacterCanonProfileCgFLiveStatus.Off,
        enabled: false,
        armed: false,
        processArmConsumed: false,
        runtimeIdentity: null,
        appliedCount: 0,
        completedCount: 0,
        rejectedCount: 0,
        retiredCount: 0,
        CharacterCanonProfileCgFLiveAdmission.RestartCleared,
        candidateOwnershipStarted: false,
        maximumReplies: 1,
        sessionStartedAt: null,
        sessionDeadline: null,
        active: null,
        latestCompletion: null);

    internal CharacterCanonProfileCgFLiveStatus Status { get; }
    internal bool Enabled { get; }
    internal bool Armed { get; }
    internal bool ProcessArmConsumed { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal int AppliedCount { get; }
    internal int CompletedCount { get; }
    internal int RejectedCount { get; }
    internal int RetiredCount { get; }
    internal CharacterCanonProfileCgFLiveAdmission LastAdmission {
        get;
    }
    internal bool CandidateOwnershipStarted { get; }
    internal int MaximumReplies { get; }
    internal DateTimeOffset? SessionStartedAt { get; }
    internal DateTimeOffset? SessionDeadline { get; }
    internal int RemainingReplies =>
        Math.Max(0, MaximumReplies - CompletedCount);
    internal CharacterCanonProfileCgFLiveApplication? Active { get; }
    internal CharacterCanonProfileCgFLiveCompletion? LatestCompletion {
        get;
    }
    internal int AdditionalProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal int LegacyFallbacks => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;

    internal CharacterCanonProfileCgFLiveSnapshot(
        CharacterCanonProfileCgFLiveStatus status,
        bool enabled,
        bool armed,
        bool processArmConsumed,
        CharacterRuntimeIdentity? runtimeIdentity,
        int appliedCount,
        int completedCount,
        int rejectedCount,
        int retiredCount,
        CharacterCanonProfileCgFLiveAdmission lastAdmission,
        bool candidateOwnershipStarted,
        int maximumReplies,
        DateTimeOffset? sessionStartedAt,
        DateTimeOffset? sessionDeadline,
        CharacterCanonProfileCgFLiveApplication? active,
        CharacterCanonProfileCgFLiveCompletion? latestCompletion)
    {
        Status = status;
        Enabled = enabled;
        Armed = armed;
        ProcessArmConsumed = processArmConsumed;
        RuntimeIdentity = runtimeIdentity;
        AppliedCount = appliedCount;
        CompletedCount = completedCount;
        RejectedCount = rejectedCount;
        RetiredCount = retiredCount;
        LastAdmission = lastAdmission;
        CandidateOwnershipStarted = candidateOwnershipStarted;
        MaximumReplies = maximumReplies;
        SessionStartedAt = sessionStartedAt;
        SessionDeadline = sessionDeadline;
        Active = active;
        LatestCompletion = latestCompletion;
    }
}

internal sealed class CharacterCanonProfileCgFLiveVerificationHost
{
    internal static readonly TimeSpan CompletionLifetime =
        TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private readonly CharacterPromptInfluenceOwnershipLedger
        _promptOwnership = new();
    private CharacterCanonProfileCgFApplicationHost
        _applicationSource = NewApplicationSource();
    private CharacterCanonProfileCgFLiveStatus _status =
        CharacterCanonProfileCgFLiveStatus.Off;
    private CharacterCanonProfileCgFLiveAdmission _lastAdmission =
        CharacterCanonProfileCgFLiveAdmission.RestartCleared;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private bool _enabled;
    private bool _armed;
    private bool _processArmConsumed;
    private bool _candidateOwnershipStarted;
    private int _appliedCount;
    private int _completedCount;
    private int _rejectedCount;
    private int _retiredCount;
    private int _maximumReplies = 1;
    private TimeSpan _sessionLifetime = TimeSpan.Zero;
    private DateTimeOffset? _sessionStartedAt;
    private DateTimeOffset? _sessionDeadline;
    private CharacterCanonProfileCgFLiveApplication? _active;
    private CharacterCanonProfileCgFLiveCompletion? _latestCompletion;

    internal bool IsArmed
    {
        get
        {
            lock (_gate)
                return _enabled && _armed && _active == null;
        }
    }

    internal bool HasActiveApplication
    {
        get
        {
            lock (_gate)
                return _active != null;
        }
    }

    internal void Reconcile(
        CharacterCanonProfileCgFLiveConfiguration configuration,
        bool sessionOpen,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        lock (_gate)
        {
            if (!configuration.Enabled || !sessionOpen)
            {
                RetireActive();
                _enabled = false;
                _armed = false;
                _runtimeIdentity = null;
                _status = CharacterCanonProfileCgFLiveStatus.Off;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission.Disabled;
                _candidateOwnershipStarted = false;
                _appliedCount = 0;
                _completedCount = 0;
                _latestCompletion = null;
                _maximumReplies = 1;
                _sessionLifetime = TimeSpan.Zero;
                _sessionStartedAt = null;
                _sessionDeadline = null;
                return;
            }
            _ = CharacterRuntimeContractGuard.Timestamp(
                occurredAt,
                nameof(occurredAt));
            if (runtimeIdentity == null)
                throw new ArgumentNullException(nameof(runtimeIdentity));
            if (_enabled
                && (_maximumReplies != configuration.MaximumReplies
                    || _sessionLifetime
                        != configuration.SessionLifetime))
            {
                RetireActive();
                _enabled = false;
                _armed = false;
                _status = CharacterCanonProfileCgFLiveStatus.Fault;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission
                        .ConfigurationConflict;
                return;
            }
            if (_enabled
                && _runtimeIdentity != null
                && !_runtimeIdentity.Matches(runtimeIdentity))
            {
                RetireActive();
                _armed = false;
                _candidateOwnershipStarted = false;
                _appliedCount = 0;
                _completedCount = 0;
                _latestCompletion = null;
                _applicationSource = NewApplicationSource();
                _sessionStartedAt = null;
                _sessionDeadline = null;
            }
            _maximumReplies = configuration.MaximumReplies;
            _sessionLifetime = configuration.SessionLifetime;
            if (_sessionDeadline.HasValue
                && occurredAt >= _sessionDeadline.Value)
            {
                RetireActive();
                _armed = false;
                _processArmConsumed = true;
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission
                        .SessionExpired;
            }
            if (_active != null
                && occurredAt > _active.CompletionDeadline)
            {
                RetireActive();
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission
                        .CompletionExpired;
            }
            _enabled = true;
            _runtimeIdentity = runtimeIdentity;
            if (_active != null)
            {
                _status =
                    CharacterCanonProfileCgFLiveStatus
                        .AwaitingCompletion;
            }
            else if (_maximumReplies == 1
                && _latestCompletion != null)
            {
                _status = CharacterCanonProfileCgFLiveStatus.Completed;
            }
            else if (_maximumReplies > 1
                && _completedCount >= _maximumReplies)
            {
                _armed = false;
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission
                        .SessionCapacityReached;
            }
            else if (_armed)
            {
                _status = CharacterCanonProfileCgFLiveStatus.Armed;
            }
            else
            {
                _status = _processArmConsumed
                    ? CharacterCanonProfileCgFLiveStatus.Capacity
                    : CharacterCanonProfileCgFLiveStatus.ReadyToArm;
            }
            if (_lastAdmission
                is CharacterCanonProfileCgFLiveAdmission.RestartCleared
                    or CharacterCanonProfileCgFLiveAdmission.Disabled)
            {
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission.ReadyToArm;
            }
        }
    }

    internal CharacterCanonProfileCgFLiveAdmission Arm(
        bool legacyStyleAssistArmedOrActive,
        DateTimeOffset? armedAt = null)
    {
        lock (_gate)
        {
            if (!_enabled)
                return SetAdmission(
                    CharacterCanonProfileCgFLiveAdmission.Disabled);
            if (legacyStyleAssistArmedOrActive)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .LegacyAssistConflict);
            }
            if (_processArmConsumed)
            {
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .ProcessArmAlreadyConsumed);
            }
            if (_armed)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission.AlreadyArmed);
            }
            _armed = true;
            if (_maximumReplies > 1)
            {
                _processArmConsumed = true;
                _sessionStartedAt =
                    CharacterRuntimeContractGuard.Timestamp(
                        armedAt ?? DateTimeOffset.UtcNow,
                        nameof(armedAt));
                _sessionDeadline =
                    _sessionStartedAt.Value + _sessionLifetime;
            }
            _status = CharacterCanonProfileCgFLiveStatus.Armed;
            return SetAdmission(
                CharacterCanonProfileCgFLiveAdmission.ArmAccepted);
        }
    }

    internal void BeginTurnAttempt()
    {
        lock (_gate)
        {
            if (_enabled && _armed && _active == null)
                _candidateOwnershipStarted = false;
        }
    }

    internal CharacterCanonProfileCgFLiveAdmission TryApply(
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterLanguageStyleStageFBRequestObservation observation,
        CharacterSituationProjection projection,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string baselinePrompt,
        CoreIdentityProfile legacyProfile,
        Func<CanonGroundedCharacterProfile> candidateFactory,
        DateTimeOffset appliedAt,
        out CharacterCanonProfileCgFLiveApplication? application)
    {
        application = null;
        lock (_gate)
        {
            if (!_enabled || !_armed)
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission.Disabled);
            if (_active != null)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .ApplicationAlreadyActive);
            }
            if (correlation == null)
                throw new ArgumentNullException(nameof(correlation));
            if (observation == null)
                throw new ArgumentNullException(nameof(observation));
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            if (activeRuntimeIdentity == null)
            {
                throw new ArgumentNullException(
                    nameof(activeRuntimeIdentity));
            }
            if (baselinePrompt == null)
                throw new ArgumentNullException(nameof(baselinePrompt));
            if (legacyProfile == null)
                throw new ArgumentNullException(nameof(legacyProfile));
            if (candidateFactory == null)
                throw new ArgumentNullException(nameof(candidateFactory));

            var now = CharacterRuntimeContractGuard.Timestamp(
                appliedAt,
                nameof(appliedAt));
            if (_sessionDeadline.HasValue
                && now >= _sessionDeadline.Value)
            {
                _armed = false;
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .SessionExpired);
            }
            if (_completedCount >= _maximumReplies)
            {
                _armed = false;
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .SessionCapacityReached);
            }
            if (observation.Admission
                    != CharacterLanguageStyleStageFBAdmission
                        .RequestEligible
                || observation.Exclusions
                    != CharacterLanguageStyleStageFBExclusion.None)
            {
                return Reject(
                    observation.Admission
                            == CharacterLanguageStyleStageFBAdmission
                                .LegacyAssistConflict
                        ? CharacterCanonProfileCgFLiveAdmission
                            .LegacyOwnerRetained
                        : CharacterCanonProfileCgFLiveAdmission
                            .ObservationIneligible);
            }
            if (correlation.ValidUntil <= now)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .AuthorizationExpired);
            }
            if (!_runtimeIdentity!.Matches(activeRuntimeIdentity)
                || !correlation.Authorization.RuntimeIdentity.Matches(
                    activeRuntimeIdentity)
                || !projection.RuntimeIdentity.Matches(
                    activeRuntimeIdentity))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .RuntimeMismatch);
            }
            if (!string.Equals(
                    correlation.Authorization.SessionId,
                    activeSessionId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .SessionMismatch);
            }
            if (correlation.Authorization.SituationId
                    != activeSituationId
                || correlation.Authorization.SituationRevision
                    != activeSituationRevision
                || projection.SituationRevision
                    != activeSituationRevision)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .SituationMismatch);
            }
            if (projection.ExpiresAt <= now)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .ProjectionExpired);
            }
            if (string.IsNullOrWhiteSpace(
                    projection.SemanticProjectionFingerprint))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .ProjectionUnavailable);
            }
            if (!string.Equals(
                    projection.Profile.Identity.ProfileId,
                    activeRuntimeIdentity.RuntimeProfileId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    legacyProfile.Identity.ProfileId,
                    activeRuntimeIdentity.RuntimeProfileId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission.ProfileMismatch);
            }
            if (!string.Equals(
                    observation.ProviderRouteId,
                    providerRouteId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .ProviderRouteMismatch);
            }
            var baselineHash =
                CharacterLanguageStyleStageFContractGuard.Sha256(
                    baselinePrompt);
            if (!string.Equals(
                    observation.PromptBaselineSha256,
                    baselineHash,
                    StringComparison.Ordinal)
                || observation.PromptBaselineCharacters
                    != baselinePrompt.Length)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .PromptBaselineMismatch);
            }

            var authorization = correlation.Authorization;
            var equivalent = new EquivalentExpressionSet(
                CharacterLanguageStyleStageFCPreview
                    .EquivalentExpressionSetId,
                authorization.Capability,
                authorization.Target,
                authorization.SemanticCommitmentHash,
                authorization.SafetyEnvelope.ConsentState,
                authorization.SafetyEnvelope.BehaviorIntensity,
                CharacterLanguageStyleIds.All.Select(
                    CharacterLanguageStyleIds.ToStableId));
            var expression =
                PlayerTriggeredResponseExpressionBinding
                    .FromExplicitPlayerRequest(
                        "cg-f-live-expression-"
                        + authorization.AuthorizationId.ToString("N"),
                        authorization,
                        equivalent,
                        authorization.ValidUntil);
            var promptLeaseId = DeterministicGuid(
                "cg-f-live-prompt-lease",
                correlation.CorrelationId,
                authorization.AuthorizationId);
            var validUntil = Minimum(
                authorization.ValidUntil,
                projection.ExpiresAt,
                now.AddSeconds(1));
            var promptIssue = _promptOwnership.TryIssue(
                promptLeaseId,
                authorization,
                LanguageStylePromptOwner.CharacterRuntimeOwned,
                observation.ExistingMode,
                activeRuntimeIdentity,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                providerRouteId,
                baselineHash,
                now,
                validUntil);
            if (promptIssue.Lease == null)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .PromptOwnershipRejected);
            }
            if (promptIssue.Lease.Owner
                != LanguageStylePromptOwner.CharacterRuntimeOwned)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .LegacyOwnerRetained);
            }

            _processArmConsumed = true;
            _candidateOwnershipStarted = true;
            var legacyStyle =
                CharacterLanguageStyleStageFCStyleSelector.Select(
                    projection);
            var legacyVariantId =
                CharacterLanguageStyleIds.ToStableId(legacyStyle);
            var prepare = _applicationSource.TryPrepare(
                DeterministicGuid(
                    "cg-f-live-profile-lease",
                    correlation.CorrelationId,
                    authorization.AuthorizationId),
                promptIssue.Lease,
                authorization,
                correlation.CorrelationId,
                expression,
                legacyProfile,
                candidateFactory,
                activeRuntimeIdentity,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                providerRouteId,
                baselinePrompt,
                legacyVariantId,
                now);
            if (prepare.Candidate == null)
            {
                _armed = false;
                _status = CharacterCanonProfileCgFLiveStatus.Fault;
                _lastAdmission =
                    CharacterCanonProfileCgFLiveAdmission
                        .ApplicationFailedClosedAfterOwnership;
                _rejectedCount++;
                return _lastAdmission;
            }

            application = new CharacterCanonProfileCgFLiveApplication(
                "cg-f-live-application-"
                + authorization.AuthorizationId.ToString("N"),
                prepare.Candidate,
                now,
                _sessionDeadline.HasValue
                    ? Minimum(
                        now + CompletionLifetime,
                        _sessionDeadline.Value)
                    : now + CompletionLifetime);
            _active = application;
            _armed = false;
            _appliedCount++;
            _status =
                CharacterCanonProfileCgFLiveStatus.AwaitingCompletion;
            return SetAdmission(
                CharacterCanonProfileCgFLiveAdmission
                    .CandidatePrepared);
        }
    }

    internal CharacterCanonProfileCgFLiveAdmission TryComplete(
        string applicationId,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        int replyCharacters,
        DateTimeOffset completedAt,
        out CharacterCanonProfileCgFLiveCompletion? completion)
    {
        completion = null;
        lock (_gate)
        {
            if (_active == null)
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .CompletionNotExpected);
            }
            if (!string.Equals(
                    _active.ApplicationId,
                    applicationId,
                    StringComparison.Ordinal)
                || !_active.Candidate.RuntimeIdentity.Matches(
                    activeRuntimeIdentity))
            {
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .CompletionIdentityMismatch);
            }
            var now = CharacterRuntimeContractGuard.Timestamp(
                completedAt,
                nameof(completedAt));
            if (now > _active.CompletionDeadline)
            {
                RetireActive();
                _status = CharacterCanonProfileCgFLiveStatus.Capacity;
                return Reject(
                    CharacterCanonProfileCgFLiveAdmission
                        .CompletionExpired);
            }
            completion = new CharacterCanonProfileCgFLiveCompletion(
                "cg-f-live-completion-"
                + _active.Candidate.AuthorizationId.ToString("N"),
                _active,
                replyCharacters,
                now);
            _latestCompletion = completion;
            _active = null;
            _completedCount++;
            _applicationSource = NewApplicationSource();
            if (_maximumReplies > 1
                && _completedCount < _maximumReplies
                && (!_sessionDeadline.HasValue
                    || now < _sessionDeadline.Value))
            {
                _armed = true;
                _status = CharacterCanonProfileCgFLiveStatus.Armed;
            }
            else
            {
                _armed = false;
                _status = _maximumReplies > 1
                    && _completedCount >= _maximumReplies
                        ? CharacterCanonProfileCgFLiveStatus.Capacity
                        : CharacterCanonProfileCgFLiveStatus.Completed;
            }
            return SetAdmission(
                CharacterCanonProfileCgFLiveAdmission
                    .CompletionRecorded);
        }
    }

    internal CharacterCanonProfileCgFLiveSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_enabled
                && _status == CharacterCanonProfileCgFLiveStatus.Off
                && !_processArmConsumed
                && _retiredCount == 0)
            {
                return CharacterCanonProfileCgFLiveSnapshot.Empty;
            }
            return new CharacterCanonProfileCgFLiveSnapshot(
                _status,
                _enabled,
                _armed,
                _processArmConsumed,
                _runtimeIdentity,
                _appliedCount,
                _completedCount,
                _rejectedCount,
                _retiredCount,
                _lastAdmission,
                _candidateOwnershipStarted,
                _maximumReplies,
                _sessionStartedAt,
                _sessionDeadline,
                _active,
                _latestCompletion);
        }
    }

    internal void Restart()
    {
        lock (_gate)
        {
            _promptOwnership.Reset();
            _applicationSource.Restart();
            _enabled = false;
            _armed = false;
            _processArmConsumed = false;
            _candidateOwnershipStarted = false;
            _runtimeIdentity = null;
            _status = CharacterCanonProfileCgFLiveStatus.Off;
            _lastAdmission =
                CharacterCanonProfileCgFLiveAdmission.RestartCleared;
            _appliedCount = 0;
            _completedCount = 0;
            _rejectedCount = 0;
            _retiredCount = 0;
            _active = null;
            _latestCompletion = null;
            _maximumReplies = 1;
            _sessionLifetime = TimeSpan.Zero;
            _sessionStartedAt = null;
            _sessionDeadline = null;
        }
    }

    internal CharacterCanonProfileCgFLiveAdmission Cancel()
    {
        lock (_gate)
        {
            if (!_enabled)
                return SetAdmission(
                    CharacterCanonProfileCgFLiveAdmission.Disabled);
            RetireActive();
            _armed = false;
            _processArmConsumed = true;
            _status = CharacterCanonProfileCgFLiveStatus.Cancelled;
            return SetAdmission(
                CharacterCanonProfileCgFLiveAdmission.ProbeCancelled);
        }
    }

    internal void StopByFault()
    {
        lock (_gate)
        {
            RetireActive();
            _enabled = false;
            _armed = false;
            _runtimeIdentity = null;
            _status = CharacterCanonProfileCgFLiveStatus.Fault;
            _lastAdmission =
                CharacterCanonProfileCgFLiveAdmission.Faulted;
        }
    }

    private static CharacterCanonProfileCgFApplicationHost
        NewApplicationSource()
        => new(
            new CharacterCanonProfileCgFConfiguration(
                enabled: true,
                CharacterCanonProfileCgFConfiguration
                    .DefaultMaximumFinalCharacters));

    private void RetireActive()
    {
        if (_active != null || _latestCompletion != null)
            _retiredCount++;
        _active = null;
    }

    private CharacterCanonProfileCgFLiveAdmission Reject(
        CharacterCanonProfileCgFLiveAdmission admission)
    {
        _rejectedCount++;
        return SetAdmission(admission);
    }

    private CharacterCanonProfileCgFLiveAdmission SetAdmission(
        CharacterCanonProfileCgFLiveAdmission admission)
    {
        _lastAdmission = admission;
        return admission;
    }

    private static DateTimeOffset Minimum(
        params DateTimeOffset[] values)
        => values.Min();

    private static Guid DeterministicGuid(
        string purpose,
        string correlationId,
        Guid authorizationId)
    {
        var material = string.Join(
            "|",
            purpose,
            correlationId,
            authorizationId.ToString("N"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}

internal static class CharacterCanonProfileCgFLiveStatusFormatter
{
    internal static string Format(
        CharacterCanonProfileCgFLiveSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var active = snapshot.Active;
        var completed = snapshot.LatestCompletion;
        var owner = active?.Owner.ToString()
            ?? completed?.Owner.ToString()
            ?? "-";
        var profile = active == null
            ? completed == null
                ? "-"
                : completed.CandidateProfileId
                  + " v"
                  + completed.CandidateProfileVersion
            : active.Candidate.CandidateProfileId
              + " v"
              + active.Candidate.CandidateProfileVersion;
        var style = active?.Candidate.StyleId
            ?? completed?.StyleId
            ?? "-";
        return string.Join(
            "\n",
            "D-024 CG-F 實機所有權切換 "
            + snapshot.Status,
            "每次啟動需 Ctrl+Shift+F12 明確 Arm",
            $"enabled={snapshot.Enabled} armed={snapshot.Armed} "
            + $"consumed={snapshot.ProcessArmConsumed}",
            "session="
            + (snapshot.SessionStartedAt.HasValue
                ? snapshot.SessionStartedAt.Value
                    .ToLocalTime()
                    .ToString("HH:mm:ss")
                : "-")
            + " deadline="
            + (snapshot.SessionDeadline.HasValue
                ? snapshot.SessionDeadline.Value
                    .ToLocalTime()
                    .ToString("HH:mm:ss")
                : "-"),
            $"owner={owner} ownership-started="
            + snapshot.CandidateOwnershipStarted,
            $"profile={profile} style={style}",
            $"applied={snapshot.AppliedCount} "
            + $"completed={snapshot.CompletedCount} "
            + $"remaining={snapshot.RemainingReplies}/"
            + snapshot.MaximumReplies
            + " "
            + $"rejected={snapshot.RejectedCount} "
            + $"retired={snapshot.RetiredCount}",
            "last=" + snapshot.LastAdmission,
            "additional-provider/retry/fallback="
            + $"{snapshot.AdditionalProviderCalls}/"
            + $"{snapshot.ProviderRetries}/"
            + $"{snapshot.LegacyFallbacks}",
            "Memory/Evidence/Reasoning="
            + $"{snapshot.MemoryCalls}/"
            + $"{snapshot.EvidenceCalls}/"
            + $"{snapshot.ReasoningCalls}");
    }
}
