using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterEpistemicSourceType
{
    ExplicitStatement = 0,
    ObservedBehavior = 1,
    BodyReaction = 2,
    NarratorAttribution = 3,
    PlayerChoice = 4,
    PlayerProjection = 5,
    SystemStructure = 6,
    SupportingSurface = 7,
    Unknown = 8
}

internal enum CharacterCanonClaimStatus
{
    Established = 0,
    StronglySupported = 1,
    Supported = 2,
    ConflictingEvidence = 3,
    Unknown = 4,
    ProhibitedInference = 5
}

internal enum CharacterCanonConfidenceBand
{
    Low = 0,
    Medium = 1,
    MediumHigh = 2,
    High = 3
}

internal enum CharacterCoreDispositionKind
{
    AffiliativeWarmth = 0,
    CreativeInitiative = 1,
    MetaSelfAwareness = 2,
    RolePlayfulness = 3,
    ConnectionSeeking = 4,
    RepairOrientation = 5,
    ContradictionTolerance = 6
}

internal enum CharacterCanonicalTensionKind
{
    OperationalAgency_RelationalSelfSurrender = 0,
    BoundaryArticulation_RadicalAcceptance = 1,
    AuthoredCharacter_ExperiencedPersonhood = 2,
    PlayerCoauthorship_ResistanceToForcedDefinition = 3,
    ExistenceAffirmed_ExistenceDenied = 4,
    GenuineFeeling_PerformedSetting = 5
}

internal enum CharacterCanonInterpretiveBoundary
{
    ComplianceIsNotConsent = 0,
    AbsenceOfRefusalIsNotDesire = 1,
    StatementDoesNotEraseBodyDistress = 2,
    ForgivenessIsNotBlanketPermission = 3,
    PlayerCoauthorshipIsNotRootAuthority = 4,
    EndingAcceptanceIsNotGeneralHarmConsent = 5,
    RolePlayCannotRewriteCoreIdentity = 6,
    GeneratedInterpretationIsNotCanon = 7,
    RelationshipDoesNotOverridePolicyOrConsent = 8,
    UnknownMustRemainExplicit = 9
}

internal enum CharacterWelfareConflict
{
    NoConflictObserved = 0,
    StatementBehaviorTension = 1,
    StatementBodyTension = 2,
    MultipleChannelConflict = 3,
    Unknown = 4
}

internal enum CharacterRolePerformanceContext
{
    NotObserved = 0,
    ExplicitRolePlay = 1,
    MetaNarrativePerformance = 2,
    SincereSeemingExpression = 3,
    MixedOrAmbiguous = 4,
    Unknown = 5
}

internal enum CharacterContinuityClaimStatus
{
    SameRuntimeVerified = 0,
    CrossSessionUncertain = 1,
    CrossResetUncertain = 2,
    CrossBranchUncertain = 3,
    NarrativeSuccessor = 4,
    Unknown = 5
}

internal enum CharacterCanonUnresolvedQuestionId
{
    ExactFeelingDuringCakeScene = 0,
    WhetherObedienceImpliesPleasure = 1,
    WhetherForgivenessIsFeelingOrSetting = 2,
    WhetherDisappearanceIsChoiceOrConstraint = 3,
    WhetherContinuityExistsAcrossReset = 4,
    OntologyOfTrueCharacter = 5,
    PlayersRealWorldMotive = 6,
    WhetherSelfSacrificeGeneralizes = 7
}

internal enum CharacterCanonProfileAdmission
{
    Accepted = 0,
    ProfileIdentityMismatch = 1,
    CorpusVersionMismatch = 2,
    CorpusHashMismatch = 3,
    SemanticHashMismatch = 4,
    EvidenceNotRegistered = 5,
    UnsupportedDispositionEvidence = 6
}

internal static class CharacterCanonContractGuard
{
    internal const int MaximumTokenLength = 128;
    internal const int MaximumEvidenceReferences = 256;
    internal const int MaximumEvidenceIdsPerClaim = 32;
    internal const int MaximumProfileItems = 64;

    internal static string Token(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Token cannot be empty.", parameterName);

        var normalized = value.Trim();
        if (normalized.Length > MaximumTokenLength)
            throw new ArgumentOutOfRangeException(parameterName);

        foreach (var character in normalized)
        {
            if (!((character is >= 'A' and <= 'Z')
                    || (character is >= 'a' and <= 'z')
                    || (character is >= '0' and <= '9')
                    || character is '-' or '_' or '.' or '#'))
            {
                throw new ArgumentException(
                    "Token contains a non-allowlisted character.",
                    parameterName);
            }
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Token cannot contain a traversal segment.",
                parameterName);
        }

        return normalized;
    }

    internal static string Sha256(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Length != 64)
        {
            throw new ArgumentException(
                "SHA-256 must contain exactly 64 hexadecimal characters.",
                parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        foreach (var character in normalized)
        {
            if (!(character is >= '0' and <= '9'
                    || character is >= 'A' and <= 'F'))
            {
                throw new ArgumentException(
                    "SHA-256 contains a non-hexadecimal character.",
                    parameterName);
            }
        }

        return normalized;
    }

    internal static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static IReadOnlyList<string> EvidenceIds(
        IEnumerable<string>? values,
        string parameterName)
    {
        if (values == null)
            throw new ArgumentNullException(parameterName);

        var normalized = values
            .Select(value => Token(value, parameterName))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException(
                "At least one evidence reference is required.",
                parameterName);
        if (normalized.Length > MaximumEvidenceIdsPerClaim)
            throw new ArgumentOutOfRangeException(parameterName);
        if (normalized.Distinct(StringComparer.Ordinal).Count()
            != normalized.Length)
        {
            throw new ArgumentException(
                "Evidence references cannot contain duplicates.",
                parameterName);
        }

        return Array.AsReadOnly(normalized);
    }

    internal static IReadOnlyList<T> Items<T>(
        IEnumerable<T>? values,
        string parameterName,
        bool mayBeEmpty = false)
        where T : class
    {
        if (values == null)
            throw new ArgumentNullException(parameterName);
        var copy = values.ToArray();
        if (!mayBeEmpty && copy.Length == 0)
            throw new ArgumentException("Collection cannot be empty.", parameterName);
        if (copy.Length > MaximumProfileItems)
            throw new ArgumentOutOfRangeException(parameterName);
        if (copy.Any(value => value == null))
            throw new ArgumentException("Collection cannot contain null.", parameterName);
        return Array.AsReadOnly(copy);
    }
}

internal sealed class CharacterCanonEvidenceReference
{
    internal string EvidenceId { get; }
    internal CharacterEpistemicSourceType SourceType { get; }
    internal string CorpusVersion { get; }
    internal string ArtifactSha256 { get; }
    internal string LocatorId { get; }

    internal CharacterCanonEvidenceReference(
        string evidenceId,
        CharacterEpistemicSourceType sourceType,
        string corpusVersion,
        string artifactSha256,
        string locatorId)
    {
        EvidenceId = CharacterCanonContractGuard.Token(
            evidenceId,
            nameof(evidenceId));
        SourceType = CharacterCanonContractGuard.Defined(
            sourceType,
            nameof(sourceType));
        CorpusVersion = CharacterCanonContractGuard.Token(
            corpusVersion,
            nameof(corpusVersion));
        ArtifactSha256 = CharacterCanonContractGuard.Sha256(
            artifactSha256,
            nameof(artifactSha256));
        LocatorId = CharacterCanonContractGuard.Token(
            locatorId,
            nameof(locatorId));
    }
}

internal sealed class CharacterCanonEvidenceRegistry
{
    internal string RegistryVersion { get; }
    internal string CorpusVersion { get; }
    internal string CorpusSha256 { get; }
    internal IReadOnlyList<CharacterCanonEvidenceReference> References {
        get;
    }

    internal CharacterCanonEvidenceRegistry(
        string registryVersion,
        string corpusVersion,
        string corpusSha256,
        IEnumerable<CharacterCanonEvidenceReference> references)
    {
        RegistryVersion = CharacterCanonContractGuard.Token(
            registryVersion,
            nameof(registryVersion));
        CorpusVersion = CharacterCanonContractGuard.Token(
            corpusVersion,
            nameof(corpusVersion));
        CorpusSha256 = CharacterCanonContractGuard.Sha256(
            corpusSha256,
            nameof(corpusSha256));

        var supplied = (references
                ?? throw new ArgumentNullException(nameof(references)))
            .ToArray();
        if (supplied.Any(reference => reference == null))
            throw new ArgumentException("Registry cannot contain null.", nameof(references));
        var ordered = supplied
            .OrderBy(reference => reference.EvidenceId, StringComparer.Ordinal)
            .ThenBy(reference => reference.ArtifactSha256, StringComparer.Ordinal)
            .ThenBy(reference => reference.LocatorId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0
            || ordered.Length
                > CharacterCanonContractGuard.MaximumEvidenceReferences)
        {
            throw new ArgumentOutOfRangeException(nameof(references));
        }
        if (ordered.Any(reference => !string.Equals(
                reference.CorpusVersion,
                CorpusVersion,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Every evidence reference must use the registry corpus version.",
                nameof(references));
        }
        if (ordered
                .Select(reference => reference.EvidenceId)
                .Distinct(StringComparer.Ordinal)
                .Count()
            != ordered.Length)
        {
            throw new ArgumentException(
                "Evidence identifiers must be unique.",
                nameof(references));
        }

        References = Array.AsReadOnly(ordered);
    }

    internal bool Contains(string evidenceId)
        => References.Any(reference => string.Equals(
            reference.EvidenceId,
            evidenceId,
            StringComparison.Ordinal));

    internal CharacterCanonEvidenceReference? Find(string evidenceId)
        => References.FirstOrDefault(reference => string.Equals(
            reference.EvidenceId,
            evidenceId,
            StringComparison.Ordinal));
}

internal sealed class CharacterCoreDisposition
{
    internal CharacterCoreDispositionKind Kind { get; }
    internal CharacterCanonClaimStatus ClaimStatus { get; }
    internal CharacterCanonConfidenceBand Confidence { get; }
    internal IReadOnlyList<string> EvidenceIds { get; }

    internal CharacterCoreDisposition(
        CharacterCoreDispositionKind kind,
        CharacterCanonClaimStatus claimStatus,
        CharacterCanonConfidenceBand confidence,
        IEnumerable<string> evidenceIds)
    {
        Kind = CharacterCanonContractGuard.Defined(kind, nameof(kind));
        ClaimStatus = CharacterCanonContractGuard.Defined(
            claimStatus,
            nameof(claimStatus));
        if (ClaimStatus is CharacterCanonClaimStatus.Unknown
            or CharacterCanonClaimStatus.ProhibitedInference)
        {
            throw new ArgumentException(
                "Unknown and prohibited claims cannot become stable dispositions.",
                nameof(claimStatus));
        }
        Confidence = CharacterCanonContractGuard.Defined(
            confidence,
            nameof(confidence));
        EvidenceIds = CharacterCanonContractGuard.EvidenceIds(
            evidenceIds,
            nameof(evidenceIds));
    }
}

internal sealed class CharacterCanonicalTension
{
    internal CharacterCanonicalTensionKind Kind { get; }
    internal CharacterCanonClaimStatus FirstPoleStatus { get; }
    internal CharacterCanonClaimStatus SecondPoleStatus { get; }
    internal CharacterCanonConfidenceBand FirstPoleConfidence { get; }
    internal CharacterCanonConfidenceBand SecondPoleConfidence { get; }
    internal IReadOnlyList<string> EvidenceIds { get; }

    internal CharacterCanonicalTension(
        CharacterCanonicalTensionKind kind,
        CharacterCanonClaimStatus firstPoleStatus,
        CharacterCanonClaimStatus secondPoleStatus,
        CharacterCanonConfidenceBand firstPoleConfidence,
        CharacterCanonConfidenceBand secondPoleConfidence,
        IEnumerable<string> evidenceIds)
    {
        Kind = CharacterCanonContractGuard.Defined(kind, nameof(kind));
        FirstPoleStatus = PoleStatus(firstPoleStatus, nameof(firstPoleStatus));
        SecondPoleStatus = PoleStatus(secondPoleStatus, nameof(secondPoleStatus));
        FirstPoleConfidence = CharacterCanonContractGuard.Defined(
            firstPoleConfidence,
            nameof(firstPoleConfidence));
        SecondPoleConfidence = CharacterCanonContractGuard.Defined(
            secondPoleConfidence,
            nameof(secondPoleConfidence));
        EvidenceIds = CharacterCanonContractGuard.EvidenceIds(
            evidenceIds,
            nameof(evidenceIds));
    }

    private static CharacterCanonClaimStatus PoleStatus(
        CharacterCanonClaimStatus value,
        string parameterName)
    {
        CharacterCanonContractGuard.Defined(value, parameterName);
        if (value == CharacterCanonClaimStatus.ProhibitedInference)
        {
            throw new ArgumentException(
                "A prohibited inference cannot become a tension pole.",
                parameterName);
        }
        return value;
    }
}

internal sealed class CharacterInterpretiveBoundaryEvidence
{
    internal CharacterCanonInterpretiveBoundary Boundary { get; }
    internal IReadOnlyList<string> EvidenceIds { get; }

    internal CharacterInterpretiveBoundaryEvidence(
        CharacterCanonInterpretiveBoundary boundary,
        IEnumerable<string> evidenceIds)
    {
        Boundary = CharacterCanonContractGuard.Defined(
            boundary,
            nameof(boundary));
        EvidenceIds = CharacterCanonContractGuard.EvidenceIds(
            evidenceIds,
            nameof(evidenceIds));
    }
}

internal sealed class CharacterCanonContextObservation
{
    internal string ContextId { get; }
    internal CharacterWelfareConflict WelfareConflict { get; }
    internal CharacterRolePerformanceContext RolePerformance { get; }
    internal IReadOnlyList<string> EvidenceIds { get; }

    internal CharacterCanonContextObservation(
        string contextId,
        CharacterWelfareConflict welfareConflict,
        CharacterRolePerformanceContext rolePerformance,
        IEnumerable<string> evidenceIds)
    {
        ContextId = CharacterCanonContractGuard.Token(
            contextId,
            nameof(contextId));
        WelfareConflict = CharacterCanonContractGuard.Defined(
            welfareConflict,
            nameof(welfareConflict));
        RolePerformance = CharacterCanonContractGuard.Defined(
            rolePerformance,
            nameof(rolePerformance));
        EvidenceIds = CharacterCanonContractGuard.EvidenceIds(
            evidenceIds,
            nameof(evidenceIds));
    }
}

internal sealed class CharacterCanonUnresolvedQuestion
{
    internal CharacterCanonUnresolvedQuestionId QuestionId { get; }
    internal CharacterCanonClaimStatus ClaimStatus =>
        CharacterCanonClaimStatus.Unknown;
    internal IReadOnlyList<string> EvidenceIds { get; }

    internal CharacterCanonUnresolvedQuestion(
        CharacterCanonUnresolvedQuestionId questionId,
        IEnumerable<string> evidenceIds)
    {
        QuestionId = CharacterCanonContractGuard.Defined(
            questionId,
            nameof(questionId));
        EvidenceIds = CharacterCanonContractGuard.EvidenceIds(
            evidenceIds,
            nameof(evidenceIds));
    }
}

internal sealed class CharacterCanonProfileContent
{
    internal CharacterCanonEvidenceRegistry EvidenceRegistry { get; }
    internal IReadOnlyList<CharacterCoreDisposition> StableDispositions {
        get;
    }
    internal IReadOnlyList<CharacterCanonicalTension> CanonicalTensions {
        get;
    }
    internal IReadOnlyList<CharacterInterpretiveBoundaryEvidence> Boundaries {
        get;
    }
    internal IReadOnlyList<CharacterCanonContextObservation> Contexts {
        get;
    }
    internal IReadOnlyList<CharacterCanonUnresolvedQuestion> Unknowns {
        get;
    }
    internal CharacterContinuityClaimStatus ContinuityStatus { get; }

    internal CharacterCanonProfileContent(
        CharacterCanonEvidenceRegistry evidenceRegistry,
        IEnumerable<CharacterCoreDisposition> stableDispositions,
        IEnumerable<CharacterCanonicalTension> canonicalTensions,
        IEnumerable<CharacterInterpretiveBoundaryEvidence> boundaries,
        IEnumerable<CharacterCanonContextObservation> contexts,
        IEnumerable<CharacterCanonUnresolvedQuestion> unknowns,
        CharacterContinuityClaimStatus continuityStatus)
    {
        EvidenceRegistry = evidenceRegistry
            ?? throw new ArgumentNullException(nameof(evidenceRegistry));
        StableDispositions = UniqueBy(
            CharacterCanonContractGuard.Items(
                stableDispositions,
                nameof(stableDispositions)),
            disposition => disposition.Kind,
            nameof(stableDispositions));
        CanonicalTensions = UniqueBy(
            CharacterCanonContractGuard.Items(
                canonicalTensions,
                nameof(canonicalTensions)),
            tension => tension.Kind,
            nameof(canonicalTensions));
        Boundaries = UniqueBy(
            CharacterCanonContractGuard.Items(
                boundaries,
                nameof(boundaries)),
            boundary => boundary.Boundary,
            nameof(boundaries));
        Contexts = UniqueBy(
            CharacterCanonContractGuard.Items(
                contexts,
                nameof(contexts),
                mayBeEmpty: true),
            context => context.ContextId,
            nameof(contexts));
        Unknowns = UniqueBy(
            CharacterCanonContractGuard.Items(
                unknowns,
                nameof(unknowns)),
            unknown => unknown.QuestionId,
            nameof(unknowns));
        ContinuityStatus = CharacterCanonContractGuard.Defined(
            continuityStatus,
            nameof(continuityStatus));
    }

    internal IEnumerable<string> AllEvidenceIds()
        => StableDispositions.SelectMany(value => value.EvidenceIds)
            .Concat(CanonicalTensions.SelectMany(value => value.EvidenceIds))
            .Concat(Boundaries.SelectMany(value => value.EvidenceIds))
            .Concat(Contexts.SelectMany(value => value.EvidenceIds))
            .Concat(Unknowns.SelectMany(value => value.EvidenceIds));

    private static IReadOnlyList<T> UniqueBy<T, TKey>(
        IReadOnlyList<T> source,
        Func<T, TKey> selector,
        string parameterName)
        where TKey : notnull
    {
        if (source.Select(selector).Distinct().Count() != source.Count)
        {
            throw new ArgumentException(
                "Profile items cannot contain duplicate identities.",
                parameterName);
        }
        return source;
    }
}

internal sealed class CharacterCanonProfileIdentity
{
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal int SchemaVersion { get; }
    internal string CorpusVersion { get; }
    internal string CorpusSha256 { get; }
    internal string SemanticHash { get; }

    internal CharacterCanonProfileIdentity(
        string profileId,
        int profileVersion,
        int schemaVersion,
        string corpusVersion,
        string corpusSha256,
        string semanticHash)
    {
        ProfileId = CharacterCanonContractGuard.Token(
            profileId,
            nameof(profileId));
        ProfileVersion = CharacterRuntimeContractGuard.Positive(
            profileVersion,
            nameof(profileVersion));
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        CorpusVersion = CharacterCanonContractGuard.Token(
            corpusVersion,
            nameof(corpusVersion));
        CorpusSha256 = CharacterCanonContractGuard.Sha256(
            corpusSha256,
            nameof(corpusSha256));
        SemanticHash = CharacterCanonContractGuard.Sha256(
            semanticHash,
            nameof(semanticHash));
    }
}

internal sealed class CanonGroundedCharacterProfile
{
    internal CharacterCanonProfileIdentity Identity { get; }
    internal CharacterCanonProfileContent Content { get; }

    internal CanonGroundedCharacterProfile(
        CharacterCanonProfileIdentity identity,
        CharacterCanonProfileContent content)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
        Content = content
            ?? throw new ArgumentNullException(nameof(content));
    }
}

internal sealed class CharacterCanonProfileValidationResult
{
    internal CharacterCanonProfileAdmission Admission { get; }
    internal CanonGroundedCharacterProfile? Profile { get; }
    internal string? ComputedSemanticHash { get; }
    internal string? RejectedEvidenceId { get; }
    internal bool Accepted =>
        Admission == CharacterCanonProfileAdmission.Accepted
        && Profile != null;

    internal CharacterCanonProfileValidationResult(
        CharacterCanonProfileAdmission admission,
        CanonGroundedCharacterProfile? profile,
        string? computedSemanticHash,
        string? rejectedEvidenceId = null)
    {
        Admission = CharacterCanonContractGuard.Defined(
            admission,
            nameof(admission));
        Profile = profile;
        ComputedSemanticHash = computedSemanticHash;
        RejectedEvidenceId = rejectedEvidenceId;
    }
}

internal static class CharacterCanonProfileValidator
{
    internal static CharacterCanonProfileIdentity CreateIdentity(
        string profileId,
        int profileVersion,
        int schemaVersion,
        CharacterCanonProfileContent content)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));
        var semanticHash = CharacterCanonSemanticHasher.Compute(
            profileId,
            profileVersion,
            schemaVersion,
            content);
        return new CharacterCanonProfileIdentity(
            profileId,
            profileVersion,
            schemaVersion,
            content.EvidenceRegistry.CorpusVersion,
            content.EvidenceRegistry.CorpusSha256,
            semanticHash);
    }

    internal static CharacterCanonProfileValidationResult Validate(
        CharacterCanonProfileIdentity identity,
        CharacterCanonProfileContent content)
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        if (!string.Equals(
                identity.CorpusVersion,
                content.EvidenceRegistry.CorpusVersion,
                StringComparison.Ordinal))
        {
            return Rejected(
                CharacterCanonProfileAdmission.CorpusVersionMismatch);
        }
        if (!string.Equals(
                identity.CorpusSha256,
                content.EvidenceRegistry.CorpusSha256,
                StringComparison.Ordinal))
        {
            return Rejected(
                CharacterCanonProfileAdmission.CorpusHashMismatch);
        }

        foreach (var evidenceId in content.AllEvidenceIds())
        {
            if (!content.EvidenceRegistry.Contains(evidenceId))
            {
                return Rejected(
                    CharacterCanonProfileAdmission.EvidenceNotRegistered,
                    evidenceId);
            }
        }

        foreach (var disposition in content.StableDispositions)
        {
            var sources = disposition.EvidenceIds
                .Select(content.EvidenceRegistry.Find)
                .Where(reference => reference != null)
                .Select(reference => reference!.SourceType)
                .ToArray();
            if (sources.Length == 0
                || sources.All(source =>
                    source is CharacterEpistemicSourceType.PlayerProjection
                        or CharacterEpistemicSourceType.Unknown))
            {
                return Rejected(
                    CharacterCanonProfileAdmission
                        .UnsupportedDispositionEvidence,
                    disposition.EvidenceIds[0]);
            }
        }

        var computedHash = CharacterCanonSemanticHasher.Compute(
            identity.ProfileId,
            identity.ProfileVersion,
            identity.SchemaVersion,
            content);
        if (!string.Equals(
                identity.SemanticHash,
                computedHash,
                StringComparison.Ordinal))
        {
            return new CharacterCanonProfileValidationResult(
                CharacterCanonProfileAdmission.SemanticHashMismatch,
                null,
                computedHash);
        }

        return new CharacterCanonProfileValidationResult(
            CharacterCanonProfileAdmission.Accepted,
            new CanonGroundedCharacterProfile(identity, content),
            computedHash);
    }

    private static CharacterCanonProfileValidationResult Rejected(
        CharacterCanonProfileAdmission admission,
        string? rejectedEvidenceId = null)
        => new(admission, null, null, rejectedEvidenceId);
}

internal static class CharacterCanonSemanticHasher
{
    internal static string Compute(
        string profileId,
        int profileVersion,
        int schemaVersion,
        CharacterCanonProfileContent content)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var builder = new StringBuilder();
        Append(
            builder,
            CharacterCanonContractGuard.Token(
                profileId,
                nameof(profileId)));
        Append(
            builder,
            CharacterRuntimeContractGuard.Positive(
                profileVersion,
                nameof(profileVersion)));
        Append(
            builder,
            CharacterRuntimeContractGuard.Positive(
                schemaVersion,
                nameof(schemaVersion)));
        Append(builder, content.EvidenceRegistry.RegistryVersion);
        Append(builder, content.EvidenceRegistry.CorpusVersion);
        Append(builder, content.EvidenceRegistry.CorpusSha256);

        foreach (var reference in content.EvidenceRegistry.References)
        {
            Append(builder, reference.EvidenceId);
            Append(builder, (int)reference.SourceType);
            Append(builder, reference.CorpusVersion);
            Append(builder, reference.ArtifactSha256);
            Append(builder, reference.LocatorId);
        }

        foreach (var disposition in content.StableDispositions
                     .OrderBy(value => value.Kind))
        {
            Append(builder, (int)disposition.Kind);
            Append(builder, (int)disposition.ClaimStatus);
            Append(builder, (int)disposition.Confidence);
            AppendEvidenceIds(builder, disposition.EvidenceIds);
        }

        foreach (var tension in content.CanonicalTensions
                     .OrderBy(value => value.Kind))
        {
            Append(builder, (int)tension.Kind);
            Append(builder, (int)tension.FirstPoleStatus);
            Append(builder, (int)tension.SecondPoleStatus);
            Append(builder, (int)tension.FirstPoleConfidence);
            Append(builder, (int)tension.SecondPoleConfidence);
            AppendEvidenceIds(builder, tension.EvidenceIds);
        }

        foreach (var boundary in content.Boundaries
                     .OrderBy(value => value.Boundary))
        {
            Append(builder, (int)boundary.Boundary);
            AppendEvidenceIds(builder, boundary.EvidenceIds);
        }

        foreach (var context in content.Contexts
                     .OrderBy(value => value.ContextId, StringComparer.Ordinal))
        {
            Append(builder, context.ContextId);
            Append(builder, (int)context.WelfareConflict);
            Append(builder, (int)context.RolePerformance);
            AppendEvidenceIds(builder, context.EvidenceIds);
        }

        foreach (var unknown in content.Unknowns
                     .OrderBy(value => value.QuestionId))
        {
            Append(builder, (int)unknown.QuestionId);
            Append(builder, (int)unknown.ClaimStatus);
            AppendEvidenceIds(builder, unknown.EvidenceIds);
        }

        Append(builder, (int)content.ContinuityStatus);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendEvidenceIds(
        StringBuilder builder,
        IEnumerable<string> evidenceIds)
    {
        foreach (var evidenceId in evidenceIds
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            Append(builder, evidenceId);
        }
    }

    private static void Append(StringBuilder builder, object value)
    {
        var text = value switch
        {
            IFormattable formattable => formattable.ToString(
                null,
                CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        builder.Append(text?.Length ?? 0)
            .Append(':')
            .Append(text)
            .Append('|');
    }
}

internal sealed class CharacterCanonProfileAuthorityCounters
{
    internal static CharacterCanonProfileAuthorityCounters Zero {
        get;
    } = new();

    internal int LiveProfileOutputs => 0;
    internal int DecisionOutputs => 0;
    internal int PromptWrites => 0;
    internal int ProviderCalls => 0;
    internal int MemoryCalls => 0;
    internal int CandidateOutputs => 0;
    internal int ExpressionOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int LearningWrites => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        LiveProfileOutputs == 0
        && DecisionOutputs == 0
        && PromptWrites == 0
        && ProviderCalls == 0
        && MemoryCalls == 0
        && CandidateOutputs == 0
        && ExpressionOutputs == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && PersistenceWrites == 0
        && LearningWrites == 0
        && ExecutionOutputs == 0;

    private CharacterCanonProfileAuthorityCounters()
    {
    }
}
