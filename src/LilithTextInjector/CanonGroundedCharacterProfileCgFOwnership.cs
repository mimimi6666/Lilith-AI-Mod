using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterCanonRuntimeProfileOwner
{
    LegacyProfileV1 = 0,
    CanonGroundedProfileV2 = 1
}

internal enum CharacterCanonProfileCgFAdmission
{
    OffLegacyRetained = 0,
    LegacyRetainedBeforeOwnership = 1,
    CandidateLeaseIssued = 2,
    CandidateLeaseConsumed = 3,
    CandidatePromptPrepared = 4,
    PromptOwnerMismatch = 5,
    AuthorizationMismatch = 6,
    RuntimeMismatch = 7,
    SessionMismatch = 8,
    SituationMismatch = 9,
    ProviderRouteMismatch = 10,
    PromptBaselineMismatch = 11,
    ExpressionMismatch = 12,
    EquivalentExpressionSetMismatch = 13,
    LegacyProfileMismatch = 14,
    CandidateProfileRejected = 15,
    CandidateIdentityMismatch = 16,
    DuplicateAuthorizationRejected = 17,
    DuplicateLeaseRejected = 18,
    LeaseNotFound = 19,
    LeaseExpired = 20,
    LeaseAlreadyTerminal = 21,
    ProfileSelectionRejected = 22,
    PromptDiffRejected = 23,
    CapacityReached = 24,
    RestartCleared = 25
}

internal enum CharacterCanonProfileCgFRetirementReason
{
    Consumed = 0,
    Expired = 1,
    IdentityMismatch = 2,
    PromptMismatch = 3,
    SelectionRejected = 4,
    PromptDiffRejected = 5,
    Restarted = 6
}

internal enum CharacterCanonProfileCgFSelectionReason
{
    PreserveNonIntervention = 0,
    PreserveConsentClarity = 1,
    PreserveTypedUnknown = 2,
    NoApplicableCanonConstraint = 3
}

internal sealed class CharacterCanonProfileCgFConfiguration
{
    internal const int DefaultMaximumFinalCharacters = 32_768;

    internal static CharacterCanonProfileCgFConfiguration Off {
        get;
    } = new(
        enabled: false,
        DefaultMaximumFinalCharacters);

    internal bool Enabled { get; }
    internal int MaximumFinalCharacters { get; }

    internal CharacterCanonProfileCgFConfiguration(
        bool enabled,
        int maximumFinalCharacters)
    {
        if (maximumFinalCharacters <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumFinalCharacters));
        Enabled = enabled;
        MaximumFinalCharacters = maximumFinalCharacters;
    }
}

internal sealed class CharacterCanonProfileOwnershipLease
{
    internal Guid LeaseId { get; }
    internal Guid PromptOwnerLeaseId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal string RequestCorrelationId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal string SessionId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ProviderRouteId { get; }
    internal string PromptBaselineSha256 { get; }
    internal CharacterCanonRuntimeProfileOwner Owner =>
        CharacterCanonRuntimeProfileOwner.CanonGroundedProfileV2;
    internal string LegacyProfileId { get; }
    internal int LegacyProfileVersion { get; }
    internal int LegacySchemaVersion { get; }
    internal string LegacySemanticHash { get; }
    internal string CandidateProfileId { get; }
    internal int CandidateProfileVersion { get; }
    internal int CandidateSchemaVersion { get; }
    internal string CandidateCorpusVersion { get; }
    internal string CandidateCorpusSha256 { get; }
    internal string CandidateSemanticHash { get; }
    internal string EquivalentExpressionSetId { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ValidUntil { get; }

    internal CharacterCanonProfileOwnershipLease(
        Guid leaseId,
        CharacterPromptInfluenceOwnershipLease promptLease,
        PlayerTriggeredRespondAuthorization authorization,
        string requestCorrelationId,
        PlayerTriggeredResponseExpressionBinding expression,
        CoreIdentityProfile legacyProfile,
        CanonGroundedCharacterProfile candidateProfile,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        if (leaseId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(leaseId));
        if (promptLease == null)
            throw new ArgumentNullException(nameof(promptLease));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (legacyProfile == null)
            throw new ArgumentNullException(nameof(legacyProfile));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));
        if (promptLease.Owner
            != LanguageStylePromptOwner.CharacterRuntimeOwned)
        {
            throw new ArgumentException(
                "Candidate profile ownership requires the exact "
                + "CharacterRuntime-owned Prompt lease.",
                nameof(promptLease));
        }

        LeaseId = leaseId;
        PromptOwnerLeaseId = promptLease.LeaseId;
        AuthorizationId = authorization.AuthorizationId;
        TurnId = authorization.TurnId;
        RequestCorrelationId =
            CharacterRuntimeContractGuard.Identifier(
                requestCorrelationId,
                nameof(requestCorrelationId));
        SituationId = authorization.SituationId;
        SituationRevision = authorization.SituationRevision;
        SessionId = authorization.SessionId;
        RuntimeIdentity = authorization.RuntimeIdentity;
        ProviderRouteId = promptLease.ProviderRouteId;
        PromptBaselineSha256 = promptLease.PromptBaselineSha256;
        LegacyProfileId = legacyProfile.Identity.ProfileId;
        LegacyProfileVersion = legacyProfile.Identity.ProfileVersion;
        LegacySchemaVersion = legacyProfile.Identity.SchemaVersion;
        LegacySemanticHash = legacyProfile.Identity.ContentHash;
        CandidateProfileId = candidateProfile.Identity.ProfileId;
        CandidateProfileVersion =
            candidateProfile.Identity.ProfileVersion;
        CandidateSchemaVersion =
            candidateProfile.Identity.SchemaVersion;
        CandidateCorpusVersion =
            candidateProfile.Identity.CorpusVersion;
        CandidateCorpusSha256 =
            candidateProfile.Identity.CorpusSha256;
        CandidateSemanticHash =
            candidateProfile.Identity.SemanticHash;
        EquivalentExpressionSetId =
            expression.EquivalentExpressions.SetId;
        IssuedAt = CharacterRuntimeContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ValidUntil = CharacterRuntimeContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (ValidUntil <= IssuedAt
            || ValidUntil > promptLease.ValidUntil
            || ValidUntil > authorization.ValidUntil
            || ValidUntil > expression.ValidUntil)
        {
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        }
    }
}

internal sealed class CharacterCanonProfileOwnershipResult
{
    internal CharacterCanonProfileCgFAdmission Admission { get; }
    internal CharacterCanonRuntimeProfileOwner Owner { get; }
    internal CharacterCanonProfileOwnershipLease? Lease { get; }
    internal CharacterCanonProfileCgFRetirementReason? RetirementReason {
        get;
    }
    internal bool Accepted => Lease != null;
    internal bool CandidateOwnershipStarted =>
        Owner
            == CharacterCanonRuntimeProfileOwner
                .CanonGroundedProfileV2;

    internal CharacterCanonProfileOwnershipResult(
        CharacterCanonProfileCgFAdmission admission,
        CharacterCanonRuntimeProfileOwner owner,
        CharacterCanonProfileOwnershipLease? lease,
        CharacterCanonProfileCgFRetirementReason? retirementReason =
            null)
    {
        Admission = admission;
        Owner = owner;
        Lease = lease;
        RetirementReason = retirementReason;
    }
}

internal sealed class CharacterCanonProfileOwnershipSnapshot
{
    internal int LeaseCount { get; }
    internal int TerminalCount { get; }
    internal bool IsEmpty => LeaseCount == 0 && TerminalCount == 0;

    internal CharacterCanonProfileOwnershipSnapshot(
        int leaseCount,
        int terminalCount)
    {
        LeaseCount = leaseCount;
        TerminalCount = terminalCount;
    }
}

internal sealed class CharacterCanonProfileOwnershipLedger
{
    private sealed class LeaseState
    {
        internal CharacterCanonProfileOwnershipLease Lease { get; }
        internal CharacterCanonProfileCgFRetirementReason?
            RetirementReason { get; set; }
        internal bool Terminal => RetirementReason.HasValue;

        internal LeaseState(
            CharacterCanonProfileOwnershipLease lease)
        {
            Lease = lease;
        }
    }

    private readonly Dictionary<Guid, LeaseState> _leases = new();
    private readonly Dictionary<Guid, Guid> _authorizationLeases = new();

    internal CharacterCanonProfileOwnershipResult TryIssue(
        Guid leaseId,
        CharacterPromptInfluenceOwnershipLease promptLease,
        PlayerTriggeredRespondAuthorization authorization,
        string requestCorrelationId,
        PlayerTriggeredResponseExpressionBinding expression,
        CoreIdentityProfile legacyProfile,
        CanonGroundedCharacterProfile candidateProfile,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        if (promptLease == null)
            throw new ArgumentNullException(nameof(promptLease));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (legacyProfile == null)
            throw new ArgumentNullException(nameof(legacyProfile));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));
        if (activeRuntimeIdentity == null)
            throw new ArgumentNullException(
                nameof(activeRuntimeIdentity));
        var now = CharacterRuntimeContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));

        if (promptLease.Owner
            != LanguageStylePromptOwner.CharacterRuntimeOwned)
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.PromptOwnerMismatch);
        }
        if (promptLease.AuthorizationId
                != authorization.AuthorizationId
            || promptLease.TurnId != authorization.TurnId)
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .AuthorizationMismatch);
        }
        if (!expression.MatchesAuthorization(authorization)
            || !string.Equals(
                expression.EquivalentExpressions.SetId,
                CharacterLanguageStyleStageFCPreview
                    .EquivalentExpressionSetId,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.ExpressionMismatch);
        }
        if (!authorization.RuntimeIdentity.Matches(
                activeRuntimeIdentity)
            || !promptLease.RuntimeIdentity.Matches(
                activeRuntimeIdentity))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.RuntimeMismatch);
        }
        if (!string.Equals(
                authorization.SessionId,
                activeSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                promptLease.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.SessionMismatch);
        }
        if (authorization.SituationId != activeSituationId
            || promptLease.SituationId != activeSituationId
            || authorization.SituationRevision
                != activeSituationRevision
            || promptLease.SituationRevision
                != activeSituationRevision)
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.SituationMismatch);
        }
        if (!string.Equals(
                promptLease.ProviderRouteId,
                providerRouteId,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .ProviderRouteMismatch);
        }
        var baselineHash =
            CharacterLanguageStyleStageFContractGuard
                .Sha256Identifier(
                    promptBaselineSha256,
                    nameof(promptBaselineSha256));
        if (!string.Equals(
                promptLease.PromptBaselineSha256,
                baselineHash,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .PromptBaselineMismatch);
        }
        if (!string.Equals(
                legacyProfile.Identity.ProfileId,
                activeRuntimeIdentity.RuntimeProfileId,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .LegacyProfileMismatch);
        }
        if (promptLease.ValidUntil <= now
            || authorization.ValidUntil <= now
            || expression.ValidUntil <= now
            || validUntil <= now)
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission.LeaseExpired);
        }
        var candidateValidation =
            CharacterCanonProfileValidator.Validate(
                candidateProfile.Identity,
                candidateProfile.Content);
        if (!candidateValidation.Accepted)
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .CandidateProfileRejected);
        }
        if (string.Equals(
                legacyProfile.Identity.ProfileId,
                candidateProfile.Identity.ProfileId,
                StringComparison.Ordinal))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .CandidateIdentityMismatch);
        }
        if (_authorizationLeases.ContainsKey(
                authorization.AuthorizationId))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .DuplicateAuthorizationRejected);
        }
        if (_leases.ContainsKey(leaseId))
        {
            return Legacy(
                CharacterCanonProfileCgFAdmission
                    .DuplicateLeaseRejected);
        }

        var lease = new CharacterCanonProfileOwnershipLease(
            leaseId,
            promptLease,
            authorization,
            requestCorrelationId,
            expression,
            legacyProfile,
            candidateProfile,
            now,
            validUntil);
        _leases.Add(leaseId, new LeaseState(lease));
        _authorizationLeases.Add(
            authorization.AuthorizationId,
            leaseId);
        return new CharacterCanonProfileOwnershipResult(
            CharacterCanonProfileCgFAdmission.CandidateLeaseIssued,
            CharacterCanonRuntimeProfileOwner.CanonGroundedProfileV2,
            lease);
    }

    internal CharacterCanonProfileOwnershipResult TryConsume(
        Guid leaseId,
        CharacterPromptInfluenceOwnershipLease promptLease,
        PlayerTriggeredRespondAuthorization authorization,
        PlayerTriggeredResponseExpressionBinding expression,
        CanonGroundedCharacterProfile candidateProfile,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        DateTimeOffset consumedAt)
    {
        if (!_leases.TryGetValue(leaseId, out var state))
        {
            return CandidateRejected(
                CharacterCanonProfileCgFAdmission.LeaseNotFound,
                CharacterCanonProfileCgFRetirementReason
                    .IdentityMismatch);
        }
        if (state.Terminal)
        {
            return CandidateRejected(
                CharacterCanonProfileCgFAdmission
                    .LeaseAlreadyTerminal,
                state.RetirementReason);
        }

        var lease = state.Lease;
        var now = CharacterRuntimeContractGuard.Timestamp(
            consumedAt,
            nameof(consumedAt));
        if (lease.ValidUntil <= now
            || promptLease.ValidUntil <= now
            || authorization.ValidUntil <= now
            || expression.ValidUntil <= now)
        {
            state.RetirementReason =
                CharacterCanonProfileCgFRetirementReason.Expired;
            return CandidateRejected(
                CharacterCanonProfileCgFAdmission.LeaseExpired,
                state.RetirementReason);
        }

        var identityMatches =
            lease.PromptOwnerLeaseId == promptLease.LeaseId
            && promptLease.Owner
                == LanguageStylePromptOwner.CharacterRuntimeOwned
            && lease.AuthorizationId == authorization.AuthorizationId
            && lease.TurnId == authorization.TurnId
            && expression.MatchesAuthorization(authorization)
            && lease.RuntimeIdentity.Matches(activeRuntimeIdentity)
            && authorization.RuntimeIdentity.Matches(
                activeRuntimeIdentity)
            && string.Equals(
                lease.SessionId,
                activeSessionId,
                StringComparison.Ordinal)
            && lease.SituationId == activeSituationId
            && lease.SituationRevision == activeSituationRevision
            && string.Equals(
                lease.ProviderRouteId,
                providerRouteId,
                StringComparison.Ordinal)
            && string.Equals(
                lease.PromptBaselineSha256,
                promptBaselineSha256,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                lease.EquivalentExpressionSetId,
                expression.EquivalentExpressions.SetId,
                StringComparison.Ordinal)
            && string.Equals(
                lease.CandidateProfileId,
                candidateProfile.Identity.ProfileId,
                StringComparison.Ordinal)
            && lease.CandidateProfileVersion
                == candidateProfile.Identity.ProfileVersion
            && lease.CandidateSchemaVersion
                == candidateProfile.Identity.SchemaVersion
            && string.Equals(
                lease.CandidateCorpusVersion,
                candidateProfile.Identity.CorpusVersion,
                StringComparison.Ordinal)
            && string.Equals(
                lease.CandidateCorpusSha256,
                candidateProfile.Identity.CorpusSha256,
                StringComparison.Ordinal)
            && string.Equals(
                lease.CandidateSemanticHash,
                candidateProfile.Identity.SemanticHash,
                StringComparison.Ordinal);
        if (!identityMatches)
        {
            state.RetirementReason =
                CharacterCanonProfileCgFRetirementReason
                    .IdentityMismatch;
            return CandidateRejected(
                CharacterCanonProfileCgFAdmission
                    .CandidateIdentityMismatch,
                state.RetirementReason);
        }

        state.RetirementReason =
            CharacterCanonProfileCgFRetirementReason.Consumed;
        return new CharacterCanonProfileOwnershipResult(
            CharacterCanonProfileCgFAdmission.CandidateLeaseConsumed,
            CharacterCanonRuntimeProfileOwner.CanonGroundedProfileV2,
            lease,
            state.RetirementReason);
    }

    internal CharacterCanonProfileOwnershipSnapshot Snapshot()
        => new(
            _leases.Count,
            _leases.Values.Count(value => value.Terminal));

    internal void Reset()
    {
        _leases.Clear();
        _authorizationLeases.Clear();
    }

    private static CharacterCanonProfileOwnershipResult Legacy(
        CharacterCanonProfileCgFAdmission admission)
        => new(
            admission,
            CharacterCanonRuntimeProfileOwner.LegacyProfileV1,
            lease: null);

    private static CharacterCanonProfileOwnershipResult
        CandidateRejected(
            CharacterCanonProfileCgFAdmission admission,
            CharacterCanonProfileCgFRetirementReason?
                retirementReason)
        => new(
            admission,
            CharacterCanonRuntimeProfileOwner
                .CanonGroundedProfileV2,
            lease: null,
            retirementReason);
}

internal sealed class CharacterCanonProfileCgFStyleSelection
{
    internal string VariantId { get; }
    internal CharacterLanguageStyleId Style { get; }
    internal CharacterCanonProfileCgFSelectionReason Reason { get; }

    internal CharacterCanonProfileCgFStyleSelection(
        string variantId,
        CharacterLanguageStyleId style,
        CharacterCanonProfileCgFSelectionReason reason)
    {
        VariantId = CharacterRuntimeContractGuard.Identifier(
            variantId,
            nameof(variantId));
        Style = style;
        Reason = reason;
    }
}

internal static class CharacterCanonProfileCgFStyleSelector
{
    internal static CharacterCanonProfileCgFStyleSelection Select(
        PlayerTriggeredResponseExpressionBinding expression,
        CanonGroundedCharacterProfile candidateProfile,
        string baselineVariantId)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));
        var validation = CharacterCanonProfileValidator.Validate(
            candidateProfile.Identity,
            candidateProfile.Content);
        if (!validation.Accepted)
        {
            throw new ArgumentException(
                "A valid immutable candidate profile is required.",
                nameof(candidateProfile));
        }

        var variants =
            expression.EquivalentExpressions.SemanticVariantIds;
        string preferred;
        CharacterCanonProfileCgFSelectionReason reason;
        if (expression.Capability
            == CharacterExpressionCapability.NonIntervention)
        {
            preferred = "style-restrained";
            reason =
                CharacterCanonProfileCgFSelectionReason
                    .PreserveNonIntervention;
        }
        else if (expression.SafetyEnvelope.ConsentState
            == CharacterConsentState.RequiredAndUnsatisfied)
        {
            preferred = "style-direct";
            reason =
                CharacterCanonProfileCgFSelectionReason
                    .PreserveConsentClarity;
        }
        else
        {
            var preservesUnknown =
                candidateProfile.Content.Unknowns.Count > 0
                && candidateProfile.Content.Boundaries.Any(
                    value => value.Boundary
                        == CharacterCanonInterpretiveBoundary
                            .UnknownMustRemainExplicit);
            if (expression.Capability
                    == CharacterExpressionCapability
                        .PlayerTriggeredResponse
                && preservesUnknown)
            {
                preferred = "style-restrained";
                reason =
                    CharacterCanonProfileCgFSelectionReason
                        .PreserveTypedUnknown;
            }
            else
            {
                preferred = "style-balanced";
                reason =
                    CharacterCanonProfileCgFSelectionReason
                        .NoApplicableCanonConstraint;
            }
        }

        var selected = variants.Contains(
                preferred,
                StringComparer.Ordinal)
            ? preferred
            : baselineVariantId;
        if (!variants.Contains(selected, StringComparer.Ordinal)
            || !TryParseStyle(selected, out var style))
        {
            throw new InvalidOperationException(
                "Selection must remain inside the fixed equivalent "
                + "expression set.");
        }
        return new CharacterCanonProfileCgFStyleSelection(
            selected,
            style,
            reason);
    }

    private static bool TryParseStyle(
        string value,
        out CharacterLanguageStyleId style)
    {
        foreach (var candidate in CharacterLanguageStyleIds.All)
        {
            if (string.Equals(
                    CharacterLanguageStyleIds.ToStableId(candidate),
                    value,
                    StringComparison.Ordinal))
            {
                style = candidate;
                return true;
            }
        }
        style = default;
        return false;
    }
}

internal sealed class CharacterCanonProfileCgFApplicationCandidate
{
    internal string CandidateId { get; }
    internal CharacterCanonRuntimeProfileOwner Owner =>
        CharacterCanonRuntimeProfileOwner.CanonGroundedProfileV2;
    internal Guid ProfileOwnershipLeaseId { get; }
    internal Guid PromptOwnerLeaseId { get; }
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
    internal string EquivalentExpressionSetId { get; }
    internal string CandidateProfileId { get; }
    internal int CandidateProfileVersion { get; }
    internal string CandidateSemanticHash { get; }
    internal CharacterLanguageStyleId Style { get; }
    internal string StyleId { get; }
    internal CharacterCanonProfileCgFSelectionReason SelectionReason {
        get;
    }
    internal string ProviderRouteId { get; }
    internal string BaselinePromptSha256 { get; }
    internal string SuffixSha256 { get; }
    internal string CandidatePromptSha256 { get; }
    internal string CandidatePrompt { get; }
    internal DateTimeOffset PreparedAt { get; }
    internal CharacterPromptInfluenceOwnershipLease PromptLease { get; }
    internal CharacterLanguageStyleDirective Directive { get; }
    internal bool IsExecutable => false;
    internal bool WritesPrompt => false;
    internal bool CallsProvider => false;
    internal bool GeneratesReply => false;
    internal bool ChangesSemantics => false;
    internal bool WritesState => false;
    internal bool WritesPersistence => false;

    internal CharacterCanonProfileCgFApplicationCandidate(
        string candidateId,
        CharacterPromptInfluenceOwnershipLease promptLease,
        CharacterCanonProfileOwnershipLease ownershipLease,
        PlayerTriggeredResponseExpressionBinding expression,
        CharacterCanonProfileCgFStyleSelection selection,
        CharacterLanguageStyleDirective directive,
        CharacterPromptDiffVerification diff,
        string candidatePrompt,
        DateTimeOffset preparedAt)
    {
        if (ownershipLease == null)
            throw new ArgumentNullException(nameof(ownershipLease));
        PromptLease = promptLease
            ?? throw new ArgumentNullException(nameof(promptLease));
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        if (directive == null)
            throw new ArgumentNullException(nameof(directive));
        if (PromptLease.Owner
                != LanguageStylePromptOwner.CharacterRuntimeOwned
            || ownershipLease.PromptOwnerLeaseId
                != PromptLease.LeaseId
            || directive.OwnerLeaseId != PromptLease.LeaseId)
        {
            throw new ArgumentException(
                "CG-F candidate requires the exact "
                + "CharacterRuntime Prompt lease.",
                nameof(promptLease));
        }
        Directive = directive;
        if (diff == null || !diff.Valid)
            throw new ArgumentException(
                "CG-F requires one exact suffix-only Prompt diff.",
                nameof(diff));

        CandidateId = CharacterRuntimeContractGuard.Identifier(
            candidateId,
            nameof(candidateId));
        ProfileOwnershipLeaseId = ownershipLease.LeaseId;
        PromptOwnerLeaseId = ownershipLease.PromptOwnerLeaseId;
        AuthorizationId = ownershipLease.AuthorizationId;
        TurnId = ownershipLease.TurnId;
        DecisionId = expression.DecisionId;
        GoalId = expression.GoalId;
        SituationId = ownershipLease.SituationId;
        SituationRevision = ownershipLease.SituationRevision;
        RuntimeIdentity = ownershipLease.RuntimeIdentity;
        Capability = expression.Capability;
        Target = expression.Target;
        SemanticCommitmentHash =
            expression.SemanticCommitmentHash;
        SafetyEnvelope = expression.SafetyEnvelope;
        EquivalentExpressionSetId =
            expression.EquivalentExpressions.SetId;
        CandidateProfileId = ownershipLease.CandidateProfileId;
        CandidateProfileVersion =
            ownershipLease.CandidateProfileVersion;
        CandidateSemanticHash =
            ownershipLease.CandidateSemanticHash;
        Style = selection.Style;
        StyleId = selection.VariantId;
        SelectionReason = selection.Reason;
        ProviderRouteId = ownershipLease.ProviderRouteId;
        BaselinePromptSha256 = diff.BaselineSha256;
        SuffixSha256 = diff.SuffixSha256;
        CandidatePromptSha256 = diff.FinalSha256;
        CandidatePrompt = candidatePrompt
            ?? throw new ArgumentNullException(nameof(candidatePrompt));
        PreparedAt = CharacterRuntimeContractGuard.Timestamp(
            preparedAt,
            nameof(preparedAt));
    }
}

internal sealed class CharacterCanonProfileCgFPrepareResult
{
    internal CharacterCanonProfileCgFAdmission Admission { get; }
    internal CharacterCanonRuntimeProfileOwner Owner { get; }
    internal CharacterCanonProfileCgFApplicationCandidate? Candidate {
        get;
    }
    internal string LegacyPrompt { get; }
    internal string LegacyVariantId { get; }
    internal bool CandidateOwnershipStarted { get; }
    internal bool MayFallbackToLegacy =>
        !CandidateOwnershipStarted;

    internal CharacterCanonProfileCgFPrepareResult(
        CharacterCanonProfileCgFAdmission admission,
        CharacterCanonRuntimeProfileOwner owner,
        CharacterCanonProfileCgFApplicationCandidate? candidate,
        string legacyPrompt,
        string legacyVariantId,
        bool candidateOwnershipStarted)
    {
        Admission = admission;
        Owner = owner;
        Candidate = candidate;
        LegacyPrompt = legacyPrompt
            ?? throw new ArgumentNullException(nameof(legacyPrompt));
        LegacyVariantId =
            CharacterRuntimeContractGuard.Identifier(
                legacyVariantId,
                nameof(legacyVariantId));
        CandidateOwnershipStarted = candidateOwnershipStarted;
    }
}

internal sealed class CharacterCanonProfileCgFAuthorityCounters
{
    internal int DecisionWrites => 0;
    internal int GoalWrites => 0;
    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int StateWrites => 0;
    internal int PersistenceWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int ExecutionOutputs => 0;
}

internal sealed class CharacterCanonProfileCgFApplicationHost
{
    private readonly CharacterCanonProfileCgFConfiguration
        _configuration;
    private readonly CharacterCanonProfileOwnershipLedger _ledger =
        new();

    internal CharacterCanonProfileCgFApplicationHost(
        CharacterCanonProfileCgFConfiguration configuration)
    {
        _configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));
    }

    internal CharacterCanonProfileCgFPrepareResult TryPrepare(
        Guid profileLeaseId,
        CharacterPromptInfluenceOwnershipLease promptLease,
        PlayerTriggeredRespondAuthorization authorization,
        string requestCorrelationId,
        PlayerTriggeredResponseExpressionBinding expression,
        CoreIdentityProfile legacyProfile,
        Func<CanonGroundedCharacterProfile> candidateFactory,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string baselinePrompt,
        string legacyVariantId,
        DateTimeOffset now)
    {
        if (!_configuration.Enabled)
        {
            return new CharacterCanonProfileCgFPrepareResult(
                CharacterCanonProfileCgFAdmission.OffLegacyRetained,
                CharacterCanonRuntimeProfileOwner.LegacyProfileV1,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: false);
        }
        if (_ledger.Snapshot().LeaseCount > 0)
        {
            return new CharacterCanonProfileCgFPrepareResult(
                CharacterCanonProfileCgFAdmission.CapacityReached,
                CharacterCanonRuntimeProfileOwner.LegacyProfileV1,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: false);
        }

        if (candidateFactory == null)
            throw new ArgumentNullException(nameof(candidateFactory));
        CanonGroundedCharacterProfile? candidateProfile;
        try
        {
            candidateProfile = candidateFactory();
        }
        catch
        {
            candidateProfile = null;
        }
        if (candidateProfile == null)
        {
            return new CharacterCanonProfileCgFPrepareResult(
                CharacterCanonProfileCgFAdmission
                    .CandidateProfileRejected,
                CharacterCanonRuntimeProfileOwner.LegacyProfileV1,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: false);
        }
        var baselineHash =
            CharacterLanguageStyleStageFContractGuard.Sha256(
                baselinePrompt);
        var validUntil = Minimum(
            promptLease.ValidUntil,
            authorization.ValidUntil,
            expression.ValidUntil,
            now.AddSeconds(1));
        var issue = _ledger.TryIssue(
            profileLeaseId,
            promptLease,
            authorization,
            requestCorrelationId,
            expression,
            legacyProfile,
            candidateProfile,
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
            return new CharacterCanonProfileCgFPrepareResult(
                issue.Admission,
                issue.Owner,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted:
                    issue.CandidateOwnershipStarted);
        }

        var consume = _ledger.TryConsume(
            profileLeaseId,
            promptLease,
            authorization,
            expression,
            candidateProfile,
            activeRuntimeIdentity,
            activeSessionId,
            activeSituationId,
            activeSituationRevision,
            providerRouteId,
            baselineHash,
            now);
        if (consume.Lease == null)
        {
            return new CharacterCanonProfileCgFPrepareResult(
                consume.Admission,
                consume.Owner,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: true);
        }

        CharacterCanonProfileCgFStyleSelection selection;
        try
        {
            selection =
                CharacterCanonProfileCgFStyleSelector.Select(
                    expression,
                    candidateProfile,
                    legacyVariantId);
        }
        catch
        {
            return new CharacterCanonProfileCgFPrepareResult(
                CharacterCanonProfileCgFAdmission
                    .ProfileSelectionRejected,
                CharacterCanonRuntimeProfileOwner
                    .CanonGroundedProfileV2,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: true);
        }

        var styleProfile =
            CharacterLanguageStyleStageFDLilithProfile.Create(
                activeRuntimeIdentity.RuntimeProfileId);
        var directive = new CharacterLanguageStyleDirective(
            "cg-f-directive-"
            + authorization.AuthorizationId.ToString("N"),
            promptLease,
            expression,
            styleProfile.Get(selection.Style),
            validUntil);
        var candidatePrompt =
            baselinePrompt + "\n\n" + directive.ExactSuffixText;
        var diff = CharacterPromptDiffVerifier.Verify(
            baselinePrompt,
            candidatePrompt,
            promptLease,
            directive,
            _configuration.MaximumFinalCharacters);
        if (!diff.Valid)
        {
            return new CharacterCanonProfileCgFPrepareResult(
                CharacterCanonProfileCgFAdmission.PromptDiffRejected,
                CharacterCanonRuntimeProfileOwner
                    .CanonGroundedProfileV2,
                candidate: null,
                baselinePrompt,
                legacyVariantId,
                candidateOwnershipStarted: true);
        }

        var application =
            new CharacterCanonProfileCgFApplicationCandidate(
                CandidateId(
                    authorization.AuthorizationId,
                    candidateProfile.Identity.SemanticHash,
                    selection.VariantId),
                promptLease,
                consume.Lease,
                expression,
                selection,
                directive,
                diff,
                candidatePrompt,
                now);
        return new CharacterCanonProfileCgFPrepareResult(
            CharacterCanonProfileCgFAdmission.CandidatePromptPrepared,
            CharacterCanonRuntimeProfileOwner
                .CanonGroundedProfileV2,
            application,
            baselinePrompt,
            legacyVariantId,
            candidateOwnershipStarted: true);
    }

    internal CharacterCanonProfileOwnershipSnapshot Snapshot()
        => _ledger.Snapshot();

    internal void Restart()
        => _ledger.Reset();

    internal CharacterCanonProfileCgFAuthorityCounters
        AuthorityCounters()
        => new();

    private static DateTimeOffset Minimum(
        params DateTimeOffset[] values)
        => values.Min();

    private static string CandidateId(
        Guid authorizationId,
        string semanticHash,
        string styleId)
    {
        var payload = authorizationId.ToString("N")
            + "|"
            + semanticHash
            + "|"
            + styleId;
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        return "cg-f-candidate-" + hash[..20];
    }
}
