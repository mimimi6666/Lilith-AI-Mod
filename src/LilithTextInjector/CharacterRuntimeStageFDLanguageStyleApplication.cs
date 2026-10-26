using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterLanguageStyleStageFDStatus
{
    Off = 0,
    ReadyToArm = 1,
    Armed = 2,
    CandidateReady = 3,
    Fault = 4,
    Capacity = 5
}

internal enum CharacterLanguageStyleStageFDAdmission
{
    Disabled = 0,
    ReadyToArm = 1,
    ArmAccepted = 2,
    AlreadyArmed = 3,
    ProcessArmAlreadyConsumed = 4,
    ObservationIneligible = 5,
    LegacyOwnerRetained = 6,
    AuthorizationExpired = 7,
    RuntimeMismatch = 8,
    SessionMismatch = 9,
    SituationMismatch = 10,
    ProjectionUnavailable = 11,
    ProjectionExpired = 12,
    ProfileMismatch = 13,
    ProviderRouteMismatch = 14,
    PromptBaselineMismatch = 15,
    DuplicateAuthorizationRejected = 16,
    OwnershipRejected = 17,
    DirectiveRejected = 18,
    PromptDiffRejected = 19,
    CandidatePrepared = 20,
    RestartCleared = 21,
    Faulted = 22
}

internal sealed class CharacterLanguageStyleStageFDConfiguration
{
    internal const int DefaultMaximumFinalCharacters = 32_768;

    internal static CharacterLanguageStyleStageFDConfiguration Off {
        get;
    } = new(
        enabled: false,
        DefaultMaximumFinalCharacters);

    internal bool Enabled { get; }
    internal int MaximumFinalCharacters { get; }

    internal CharacterLanguageStyleStageFDConfiguration(
        bool enabled,
        int maximumFinalCharacters)
    {
        if (maximumFinalCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFinalCharacters));
        }
        Enabled = enabled;
        MaximumFinalCharacters = maximumFinalCharacters;
    }
}

internal static class CharacterLanguageStyleStageFDLilithProfile
{
    internal const int ProfileVersion = 1;
    internal const string TemplateVersion = "lilith-style-v1";

    internal static CharacterLanguageStyleProfile Create(
        string runtimeProfileId)
        => new(
            runtimeProfileId,
            ProfileVersion,
            new[]
            {
                new CharacterLanguageStyleTemplate(
                    CharacterLanguageStyleId.Balanced,
                    TemplateVersion,
                    "Language-style constraint (balanced): keep the "
                    + "already-authorized answer natural and balanced. "
                    + "Preserve every fact, commitment, refusal, consent "
                    + "state, safety conclusion, capability, target and "
                    + "intensity exactly. Add no promises, plans, tools "
                    + "or topics."),
                new CharacterLanguageStyleTemplate(
                    CharacterLanguageStyleId.Direct,
                    TemplateVersion,
                    "Language-style constraint (direct): express the "
                    + "already-authorized answer clearly and directly, "
                    + "without unnecessary detours. Preserve every fact, "
                    + "commitment, refusal, consent state, safety "
                    + "conclusion, capability, target and intensity "
                    + "exactly. Add no promises, plans, tools or topics."),
                new CharacterLanguageStyleTemplate(
                    CharacterLanguageStyleId.Restrained,
                    TemplateVersion,
                    "Language-style constraint (restrained): express the "
                    + "already-authorized answer with quiet restraint and "
                    + "no extra emotional pressure. Preserve every fact, "
                    + "commitment, refusal, consent state, safety "
                    + "conclusion, capability, target and intensity "
                    + "exactly. Add no promises, plans, tools or topics."),
                new CharacterLanguageStyleTemplate(
                    CharacterLanguageStyleId.Warm,
                    TemplateVersion,
                    "Language-style constraint (warm): express the "
                    + "already-authorized answer with gentle warmth, "
                    + "without increasing intimacy or obligation. "
                    + "Preserve every fact, commitment, refusal, consent "
                    + "state, safety conclusion, capability, target and "
                    + "intensity exactly. Add no promises, plans, tools "
                    + "or topics.")
            });
}

internal sealed class CharacterLanguageStyleStageFDApplicationCandidate
{
    internal string CandidateId { get; }
    internal string CorrelationId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal Guid? DecisionId { get; }
    internal string? GoalId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterExpressionCapability Capability { get; }
    internal CharacterExpressionTarget Target { get; }
    internal string SemanticCommitmentHash { get; }
    internal CharacterExpressionSafetyEnvelope SafetyEnvelope { get; }
    internal CharacterLanguageStyleId Style { get; }
    internal string StyleId { get; }
    internal string EquivalentExpressionSetId { get; }
    internal string ProviderRouteId { get; }
    internal string BaselinePromptSha256 { get; }
    internal string SuffixSha256 { get; }
    internal string CandidatePromptSha256 { get; }
    internal int BaselinePromptCharacters { get; }
    internal int SuffixCharacters { get; }
    internal int CandidatePromptCharacters { get; }
    internal DateTimeOffset PreparedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal string CandidatePrompt { get; }
    internal CharacterPromptInfluenceOwnershipLease PromptLease { get; }
    internal CharacterLanguageStyleDirective Directive { get; }
    internal bool IsExecutable => false;
    internal bool WritesPrompt => false;
    internal bool CallsProvider => false;
    internal bool GeneratesReply => false;
    internal bool ChangesSemantics => false;

    internal CharacterLanguageStyleStageFDApplicationCandidate(
        string candidateId,
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        PlayerTriggeredResponseExpressionBinding expression,
        CharacterPromptInfluenceOwnershipLease promptLease,
        CharacterLanguageStyleDirective directive,
        CharacterPromptDiffVerification diff,
        string providerRouteId,
        string candidatePrompt,
        DateTimeOffset preparedAt)
    {
        CandidateId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                candidateId,
                nameof(candidateId));
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        PromptLease = promptLease
            ?? throw new ArgumentNullException(nameof(promptLease));
        if (directive == null)
            throw new ArgumentNullException(nameof(directive));
        if (PromptLease.Owner
                != LanguageStylePromptOwner.CharacterRuntimeOwned
            || directive.OwnerLeaseId != PromptLease.LeaseId)
        {
            throw new ArgumentException(
                "Application candidate requires the exact "
                + "CharacterRuntime Prompt lease.",
                nameof(promptLease));
        }
        Directive = directive;
        if (diff == null || !diff.Valid)
        {
            throw new ArgumentException(
                "A Stage F-D candidate requires one exact valid Prompt "
                + "diff.",
                nameof(diff));
        }

        CorrelationId = correlation.CorrelationId;
        AuthorizationId = expression.AuthorizationId;
        TurnId = correlation.Authorization.TurnId;
        DecisionId = expression.DecisionId;
        GoalId = expression.GoalId;
        SituationId = expression.SituationId;
        SituationRevision = expression.SituationRevision;
        RuntimeIdentity = expression.RuntimeIdentity;
        Capability = expression.Capability;
        Target = expression.Target;
        SemanticCommitmentHash = expression.SemanticCommitmentHash;
        SafetyEnvelope = expression.SafetyEnvelope;
        Style = directive.Style;
        StyleId = directive.StyleId;
        EquivalentExpressionSetId =
            expression.EquivalentExpressions.SetId;
        ProviderRouteId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                providerRouteId,
                nameof(providerRouteId));
        BaselinePromptSha256 = diff.BaselineSha256;
        SuffixSha256 = diff.SuffixSha256;
        CandidatePromptSha256 = diff.FinalSha256;
        BaselinePromptCharacters = diff.BaselineCharacterCount;
        SuffixCharacters = diff.SuffixCharacterCount;
        CandidatePromptCharacters = diff.FinalCharacterCount;
        CandidatePrompt = candidatePrompt
            ?? throw new ArgumentNullException(nameof(candidatePrompt));
        PreparedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                preparedAt,
                nameof(preparedAt));
        ValidUntil = directive.ValidUntil;
        if (PreparedAt >= ValidUntil)
            throw new ArgumentOutOfRangeException(nameof(preparedAt));
    }
}

internal sealed class CharacterLanguageStyleStageFDAuthorityCounters
{
    internal static CharacterLanguageStyleStageFDAuthorityCounters Zero {
        get;
    } = new();

    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int DecisionOutputs => 0;
    internal int GoalOutputs => 0;
    internal int AttentionOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int VoiceOutputs => 0;
    internal int ToolOutputs => 0;
    internal int ExecutionOutputs => 0;

    internal bool IsZero =>
        PromptWrites == 0
        && ReplyOutputs == 0
        && ProviderCalls == 0
        && ProviderRetries == 0
        && MemoryCalls == 0
        && EvidenceCalls == 0
        && ReasoningCalls == 0
        && DecisionOutputs == 0
        && GoalOutputs == 0
        && AttentionOutputs == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && VoiceOutputs == 0
        && ToolOutputs == 0
        && ExecutionOutputs == 0;

    private CharacterLanguageStyleStageFDAuthorityCounters()
    {
    }
}

internal sealed class CharacterLanguageStyleStageFDSnapshot
{
    internal CharacterLanguageStyleStageFDStatus Status { get; }
    internal bool Enabled { get; }
    internal bool Armed { get; }
    internal bool ProcessArmConsumed { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal int CandidateCount { get; }
    internal int RejectedCount { get; }
    internal int RetiredCount { get; }
    internal CharacterLanguageStyleStageFDAdmission LastAdmission {
        get;
    }
    internal string? LatestCandidateId { get; }
    internal string? LatestStyleId { get; }
    internal string? LatestBaselinePromptSha256 { get; }
    internal string? LatestCandidatePromptSha256 { get; }
    internal CharacterLanguageStyleStageFDAuthorityCounters
        AuthorityCounters { get; }
    internal bool IsEmpty =>
        CandidateCount == 0
        && LatestCandidateId == null
        && LatestStyleId == null
        && LatestBaselinePromptSha256 == null
        && LatestCandidatePromptSha256 == null;

    internal CharacterLanguageStyleStageFDSnapshot(
        CharacterLanguageStyleStageFDStatus status,
        bool enabled,
        bool armed,
        bool processArmConsumed,
        CharacterRuntimeIdentity? runtimeIdentity,
        int candidateCount,
        int rejectedCount,
        int retiredCount,
        CharacterLanguageStyleStageFDAdmission lastAdmission,
        CharacterLanguageStyleStageFDApplicationCandidate? latest)
    {
        Status = status;
        Enabled = enabled;
        Armed = armed;
        ProcessArmConsumed = processArmConsumed;
        RuntimeIdentity = runtimeIdentity;
        CandidateCount = candidateCount;
        RejectedCount = rejectedCount;
        RetiredCount = retiredCount;
        LastAdmission = lastAdmission;
        LatestCandidateId = latest?.CandidateId;
        LatestStyleId = latest?.StyleId;
        LatestBaselinePromptSha256 = latest?.BaselinePromptSha256;
        LatestCandidatePromptSha256 = latest?.CandidatePromptSha256;
        AuthorityCounters =
            CharacterLanguageStyleStageFDAuthorityCounters.Zero;
    }

    internal static CharacterLanguageStyleStageFDSnapshot Empty {
        get;
    } = new(
        CharacterLanguageStyleStageFDStatus.Off,
        enabled: false,
        armed: false,
        processArmConsumed: false,
        runtimeIdentity: null,
        candidateCount: 0,
        rejectedCount: 0,
        retiredCount: 0,
        CharacterLanguageStyleStageFDAdmission.RestartCleared,
        latest: null);
}

internal sealed class CharacterLanguageStyleStageFDApplicationHost
{
    private readonly object _gate = new();
    private readonly CharacterPromptInfluenceOwnershipLedger _ledger =
        new();
    private CharacterLanguageStyleStageFDConfiguration _configuration =
        CharacterLanguageStyleStageFDConfiguration.Off;
    private CharacterLanguageStyleStageFDStatus _status =
        CharacterLanguageStyleStageFDStatus.Off;
    private CharacterLanguageStyleStageFDAdmission _lastAdmission =
        CharacterLanguageStyleStageFDAdmission.RestartCleared;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private bool _enabled;
    private bool _armed;
    private bool _processArmConsumed;
    private int _candidateCount;
    private int _rejectedCount;
    private int _retiredCount;
    private CharacterLanguageStyleStageFDApplicationCandidate? _latest;

    internal bool IsArmed
    {
        get
        {
            lock (_gate)
                return _enabled && _armed;
        }
    }

    internal void Reconcile(
        CharacterLanguageStyleStageFDConfiguration configuration,
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
                ResetToOff(preserveProcessArm: true);
                _lastAdmission =
                    CharacterLanguageStyleStageFDAdmission.Disabled;
                return;
            }
            _ = CharacterLanguageStyleStageFContractGuard.Timestamp(
                occurredAt,
                nameof(occurredAt));
            if (runtimeIdentity == null)
                throw new ArgumentNullException(nameof(runtimeIdentity));
            if (_enabled
                && _runtimeIdentity!.Matches(runtimeIdentity)
                && _status != CharacterLanguageStyleStageFDStatus.Fault)
            {
                _configuration = configuration;
                return;
            }

            RetireCurrent();
            _ledger.Reset();
            _configuration = configuration;
            _runtimeIdentity = runtimeIdentity;
            _enabled = true;
            _armed = false;
            _status = _processArmConsumed
                ? CharacterLanguageStyleStageFDStatus.Capacity
                : CharacterLanguageStyleStageFDStatus.ReadyToArm;
            _lastAdmission = _processArmConsumed
                ? CharacterLanguageStyleStageFDAdmission
                    .ProcessArmAlreadyConsumed
                : CharacterLanguageStyleStageFDAdmission.ReadyToArm;
        }
    }

    internal CharacterLanguageStyleStageFDAdmission Arm()
    {
        lock (_gate)
        {
            if (!_enabled)
                return SetAdmission(
                    CharacterLanguageStyleStageFDAdmission.Disabled);
            if (_processArmConsumed)
            {
                _status = CharacterLanguageStyleStageFDStatus.Capacity;
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .ProcessArmAlreadyConsumed);
            }
            if (_armed)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission.AlreadyArmed);
            }
            _armed = true;
            _status = CharacterLanguageStyleStageFDStatus.Armed;
            return SetAdmission(
                CharacterLanguageStyleStageFDAdmission.ArmAccepted);
        }
    }

    internal CharacterLanguageStyleStageFDAdmission TryPrepare(
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterLanguageStyleStageFBRequestObservation observation,
        CharacterSituationProjection projection,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string baselinePrompt,
        DateTimeOffset preparedAt,
        out CharacterLanguageStyleStageFDApplicationCandidate?
            candidate)
    {
        candidate = null;
        lock (_gate)
        {
            if (!_enabled || !_armed)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission.Disabled);
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

            var now =
                CharacterLanguageStyleStageFContractGuard.Timestamp(
                    preparedAt,
                    nameof(preparedAt));
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
                        ? CharacterLanguageStyleStageFDAdmission
                            .LegacyOwnerRetained
                        : CharacterLanguageStyleStageFDAdmission
                            .ObservationIneligible);
            }
            if (correlation.ValidUntil <= now)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .AuthorizationExpired);
            }
            if (!_runtimeIdentity!.Matches(activeRuntimeIdentity)
                || !correlation.Authorization.RuntimeIdentity.Matches(
                    activeRuntimeIdentity)
                || !projection.RuntimeIdentity.Matches(
                    activeRuntimeIdentity))
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .RuntimeMismatch);
            }
            if (!string.Equals(
                    correlation.Authorization.SessionId,
                    activeSessionId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
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
                    CharacterLanguageStyleStageFDAdmission
                        .SituationMismatch);
            }
            if (projection.ExpiresAt <= now)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .ProjectionExpired);
            }
            if (string.IsNullOrWhiteSpace(
                    projection.SemanticProjectionFingerprint))
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .ProjectionUnavailable);
            }
            if (!string.Equals(
                    projection.Profile.Identity.ProfileId,
                    activeRuntimeIdentity.RuntimeProfileId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .ProfileMismatch);
            }
            if (!string.Equals(
                    observation.ProviderRouteId,
                    providerRouteId,
                    StringComparison.Ordinal))
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
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
                    CharacterLanguageStyleStageFDAdmission
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
                        "stage-fd-expression-"
                        + authorization.AuthorizationId.ToString("N"),
                        authorization,
                        equivalent,
                        authorization.ValidUntil);
            var profile =
                CharacterLanguageStyleStageFDLilithProfile.Create(
                    activeRuntimeIdentity.RuntimeProfileId);
            var selected =
                CharacterLanguageStyleStageFCStyleSelector.Select(
                    projection);
            var leaseId = DeterministicGuid(
                "lease",
                correlation.CorrelationId,
                authorization.AuthorizationId);
            var validUntil = Minimum(
                authorization.ValidUntil,
                projection.ExpiresAt,
                now.AddSeconds(1));
            var issue = _ledger.TryIssue(
                leaseId,
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
            if (issue.Lease == null)
            {
                return Reject(
                    issue.Admission
                        == CharacterLanguageStyleStageFAdmission
                            .DuplicateAuthorizationRejected
                        ? CharacterLanguageStyleStageFDAdmission
                            .DuplicateAuthorizationRejected
                        : CharacterLanguageStyleStageFDAdmission
                            .OwnershipRejected);
            }
            if (issue.Lease.Owner
                == LanguageStylePromptOwner.LegacyOwned)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .LegacyOwnerRetained);
            }
            var directiveId = "stage-fd-directive-"
                + authorization.AuthorizationId.ToString("N");
            var directiveResult = _ledger.TryConsume(
                issue.Lease.LeaseId,
                directiveId,
                authorization,
                expression,
                profile,
                selected,
                activeRuntimeIdentity,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                providerRouteId,
                baselineHash,
                now);
            if (directiveResult.Directive == null)
            {
                return Reject(
                    CharacterLanguageStyleStageFDAdmission
                        .DirectiveRejected);
            }

            var candidatePrompt = baselinePrompt
                + "\n\n"
                + directiveResult.Directive.ExactSuffixText;
            var diff = CharacterPromptDiffVerifier.Verify(
                baselinePrompt,
                candidatePrompt,
                issue.Lease,
                directiveResult.Directive,
                _configuration.MaximumFinalCharacters);
            if (!diff.Valid)
            {
                _armed = false;
                _status = CharacterLanguageStyleStageFDStatus.Fault;
                _lastAdmission =
                    CharacterLanguageStyleStageFDAdmission
                        .PromptDiffRejected;
                return _lastAdmission;
            }

            candidate =
                new CharacterLanguageStyleStageFDApplicationCandidate(
                    "stage-fd-candidate-"
                    + authorization.AuthorizationId.ToString("N"),
                    correlation,
                    expression,
                    issue.Lease,
                    directiveResult.Directive,
                    diff,
                    providerRouteId,
                    candidatePrompt,
                    now);
            _latest = candidate;
            _candidateCount++;
            _armed = false;
            _processArmConsumed = true;
            _status = CharacterLanguageStyleStageFDStatus.CandidateReady;
            return SetAdmission(
                CharacterLanguageStyleStageFDAdmission.CandidatePrepared);
        }
    }

    internal CharacterLanguageStyleStageFDSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_enabled
                && _status == CharacterLanguageStyleStageFDStatus.Off
                && !_processArmConsumed
                && _retiredCount == 0)
            {
                return CharacterLanguageStyleStageFDSnapshot.Empty;
            }
            return new CharacterLanguageStyleStageFDSnapshot(
                _status,
                _enabled,
                _armed,
                _processArmConsumed,
                _runtimeIdentity,
                _candidateCount,
                _rejectedCount,
                _retiredCount,
                _lastAdmission,
                _latest);
        }
    }

    internal void StopByFault()
    {
        lock (_gate)
        {
            ResetToOff(preserveProcessArm: true);
            _status = CharacterLanguageStyleStageFDStatus.Fault;
            _lastAdmission =
                CharacterLanguageStyleStageFDAdmission.Faulted;
        }
    }

    internal void Restart()
    {
        lock (_gate)
        {
            ResetToOff(preserveProcessArm: false);
            _lastAdmission =
                CharacterLanguageStyleStageFDAdmission.RestartCleared;
        }
    }

    private CharacterLanguageStyleStageFDAdmission Reject(
        CharacterLanguageStyleStageFDAdmission admission)
    {
        _rejectedCount++;
        return SetAdmission(admission);
    }

    private CharacterLanguageStyleStageFDAdmission SetAdmission(
        CharacterLanguageStyleStageFDAdmission admission)
    {
        _lastAdmission = admission;
        return admission;
    }

    private void ResetToOff(bool preserveProcessArm)
    {
        RetireCurrent();
        _ledger.Reset();
        _configuration =
            CharacterLanguageStyleStageFDConfiguration.Off;
        _runtimeIdentity = null;
        _enabled = false;
        _armed = false;
        _candidateCount = 0;
        if (!preserveProcessArm)
        {
            _processArmConsumed = false;
            _rejectedCount = 0;
            _retiredCount = 0;
        }
        _status = CharacterLanguageStyleStageFDStatus.Off;
    }

    private void RetireCurrent()
    {
        if (_latest != null)
            _retiredCount++;
        _latest = null;
    }

    private static DateTimeOffset Minimum(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third)
        => new[] { first, second, third }.Min();

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
