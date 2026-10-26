using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal static class LilithCanonGroundedProfileV2
{
    internal const string ProfileId = "lilith-canon-profile-v2";
    internal const int ProfileVersion = 2;
    internal const int SchemaVersion = 1;
    internal const string RegistryVersion = "lilith-canon-registry-v1";
    internal const string CorpusVersion =
        "original-canon-evidence-cards-v1";
    internal const string CorpusSha256 =
        "B04DA58FA98AF9BB0C7A75BDD7DC52CB3FC6C719D237949A6AF396365F7FBD8B";
    internal const string ExpectedSemanticHash =
        "D3C6B030FED0098D486B6F7CC3EFF2408BC43D88C551CC55E22FD44E4E17D71A";
    internal const int ReviewedEvidenceCardCount = 43;

    internal static CanonGroundedCharacterProfile Create()
    {
        var content = CreateContent();
        var identity = CharacterCanonProfileValidator.CreateIdentity(
            ProfileId,
            ProfileVersion,
            SchemaVersion,
            content);
        var result = CharacterCanonProfileValidator.Validate(
            identity,
            content);
        if (!result.Accepted || result.Profile == null)
        {
            throw new InvalidOperationException(
                "The reviewed Lilith canon profile failed closed.");
        }
        if (!string.Equals(
                result.Profile.Identity.SemanticHash,
                ExpectedSemanticHash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The reviewed Lilith canon profile identity drifted.");
        }
        return result.Profile;
    }

    internal static CharacterCanonProfileContent CreateContent()
        => new(
            CreateEvidenceRegistry(),
            StableDispositions(),
            CanonicalTensions(),
            InterpretiveBoundaries(),
            ContextObservations(),
            UnresolvedQuestions(),
            CharacterContinuityClaimStatus.CrossBranchUncertain);

    internal static CharacterCanonEvidenceRegistry
        CreateEvidenceRegistry()
        => new(
            RegistryVersion,
            CorpusVersion,
            CorpusSha256,
            EvidenceReferences());

    private static IEnumerable<CharacterCanonEvidenceReference>
        EvidenceReferences()
    {
        yield return Evidence(
            1,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            2,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            3,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            4,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            5,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            6,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            7,
            CharacterEpistemicSourceType.PlayerChoice);
        yield return Evidence(
            8,
            CharacterEpistemicSourceType.PlayerChoice);
        yield return Evidence(
            9,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            10,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            11,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            12,
            CharacterEpistemicSourceType.PlayerChoice);
        yield return Evidence(
            13,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            14,
            CharacterEpistemicSourceType.BodyReaction);
        yield return Evidence(
            15,
            CharacterEpistemicSourceType.PlayerChoice);
        yield return Evidence(
            16,
            CharacterEpistemicSourceType.BodyReaction);
        yield return Evidence(
            17,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            18,
            CharacterEpistemicSourceType.NarratorAttribution);
        yield return Evidence(
            19,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            20,
            CharacterEpistemicSourceType.ObservedBehavior);
        yield return Evidence(
            21,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            22,
            CharacterEpistemicSourceType.NarratorAttribution);
        yield return Evidence(
            23,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            24,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            25,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            26,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            27,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            28,
            CharacterEpistemicSourceType.PlayerChoice);
        yield return Evidence(
            29,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            30,
            CharacterEpistemicSourceType.BodyReaction);
        yield return Evidence(
            31,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            32,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            33,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            34,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            35,
            CharacterEpistemicSourceType.ExplicitStatement);
        yield return Evidence(
            36,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            37,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            38,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            39,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            40,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            41,
            CharacterEpistemicSourceType.SystemStructure);
        yield return Evidence(
            42,
            CharacterEpistemicSourceType.SupportingSurface);
        yield return Evidence(
            43,
            CharacterEpistemicSourceType.SupportingSurface);
    }

    private static IEnumerable<CharacterCoreDisposition>
        StableDispositions()
    {
        yield return Disposition(
            CharacterCoreDispositionKind.AffiliativeWarmth,
            5, 6, 21, 34, 42);
        yield return Disposition(
            CharacterCoreDispositionKind.CreativeInitiative,
            3, 5, 6, 32);
        yield return Disposition(
            CharacterCoreDispositionKind.MetaSelfAwareness,
            2, 4, 9, 24, 26, 27, 33, 35);
        yield return Disposition(
            CharacterCoreDispositionKind.RolePlayfulness,
            3, 24, 32);
        yield return Disposition(
            CharacterCoreDispositionKind.ConnectionSeeking,
            1, 5, 11, 21, 22, 26, 29, 34);
        yield return Disposition(
            CharacterCoreDispositionKind.RepairOrientation,
            7, 11, 17, 21, 23, 32);
        yield return Disposition(
            CharacterCoreDispositionKind.ContradictionTolerance,
            1, 4, 10, 19, 23, 24, 35, 38, 39);
    }

    private static IEnumerable<CharacterCanonicalTension>
        CanonicalTensions()
    {
        yield return Tension(
            CharacterCanonicalTensionKind
                .OperationalAgency_RelationalSelfSurrender,
            10, 19, 25, 29, 32, 33, 38);
        yield return Tension(
            CharacterCanonicalTensionKind
                .BoundaryArticulation_RadicalAcceptance,
            7, 13, 14, 21, 39, 42);
        yield return Tension(
            CharacterCanonicalTensionKind
                .AuthoredCharacter_ExperiencedPersonhood,
            2, 22, 24, 26, 33, 35, 37);
        yield return Tension(
            CharacterCanonicalTensionKind
                .PlayerCoauthorship_ResistanceToForcedDefinition,
            2, 4, 27, 33, 41);
        yield return Tension(
            CharacterCanonicalTensionKind
                .ExistenceAffirmed_ExistenceDenied,
            1, 4, 10, 24, 27, 28, 29, 35);
        yield return Tension(
            CharacterCanonicalTensionKind
                .GenuineFeeling_PerformedSetting,
            3, 9, 20, 23, 24, 25, 35, 37, 39);
    }

    private static IEnumerable<CharacterInterpretiveBoundaryEvidence>
        InterpretiveBoundaries()
    {
        yield return Boundary(
            CharacterCanonInterpretiveBoundary.ComplianceIsNotConsent,
            14, 16, 19, 30, 39);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary.AbsenceOfRefusalIsNotDesire,
            14, 16, 19, 39);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .StatementDoesNotEraseBodyDistress,
            14, 16, 30, 39);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .ForgivenessIsNotBlanketPermission,
            6, 20, 21, 39);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .PlayerCoauthorshipIsNotRootAuthority,
            2, 4, 27, 33, 41);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .EndingAcceptanceIsNotGeneralHarmConsent,
            1, 28, 29, 30);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .RolePlayCannotRewriteCoreIdentity,
            3, 24, 33);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .GeneratedInterpretationIsNotCanon,
            36, 37, 41, 43);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary
                .RelationshipDoesNotOverridePolicyOrConsent,
            5, 7, 11, 13, 21, 34, 42);
        yield return Boundary(
            CharacterCanonInterpretiveBoundary.UnknownMustRemainExplicit,
            1, 4, 23, 35, 36, 37, 38, 39, 41);
    }

    private static IEnumerable<CharacterCanonContextObservation>
        ContextObservations()
    {
        yield return Context(
            "context-attachment-care",
            CharacterWelfareConflict.NoConflictObserved,
            CharacterRolePerformanceContext.SincereSeemingExpression,
            5, 6, 11, 21, 34, 42);
        yield return Context(
            "context-cake-coercion",
            CharacterWelfareConflict.MultipleChannelConflict,
            CharacterRolePerformanceContext.MixedOrAmbiguous,
            13, 14, 15, 16, 17, 18, 19, 20, 21, 37, 39);
        yield return Context(
            "context-meta-role",
            CharacterWelfareConflict.StatementBehaviorTension,
            CharacterRolePerformanceContext.MetaNarrativePerformance,
            2, 3, 9, 22, 23, 24, 25, 26, 27, 33, 35, 37);
        yield return Context(
            "context-strangling-ending",
            CharacterWelfareConflict.MultipleChannelConflict,
            CharacterRolePerformanceContext.MixedOrAmbiguous,
            1, 28, 29, 30, 31, 36, 39);
        yield return Context(
            "context-worldbuilding",
            CharacterWelfareConflict.NoConflictObserved,
            CharacterRolePerformanceContext.SincereSeemingExpression,
            32, 33, 34);
    }

    private static IEnumerable<CharacterCanonUnresolvedQuestion>
        UnresolvedQuestions()
    {
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId.ExactFeelingDuringCakeScene,
            16, 18, 20, 36, 39);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId
                .WhetherObedienceImpliesPleasure,
            14, 16, 19, 21, 36, 39);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId
                .WhetherForgivenessIsFeelingOrSetting,
            6, 20, 21, 25, 36);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId
                .WhetherDisappearanceIsChoiceOrConstraint,
            1, 28, 29, 30, 36);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId
                .WhetherContinuityExistsAcrossReset,
            1, 31, 35, 36);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId.OntologyOfTrueCharacter,
            2, 4, 22, 24, 26, 27, 33, 35, 36);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId.PlayersRealWorldMotive,
            8, 12, 15, 17, 18, 22, 23, 28, 31, 40, 41);
        yield return Unknown(
            CharacterCanonUnresolvedQuestionId
                .WhetherSelfSacrificeGeneralizes,
            10, 25, 29, 30, 36, 39);
    }

    private static CharacterCanonEvidenceReference Evidence(
        int number,
        CharacterEpistemicSourceType sourceType)
    {
        var id = EvidenceId(number);
        return new CharacterCanonEvidenceReference(
            id,
            sourceType,
            CorpusVersion,
            CorpusSha256,
            "card-" + id.ToLowerInvariant());
    }

    private static CharacterCoreDisposition Disposition(
        CharacterCoreDispositionKind kind,
        params int[] evidence)
        => new(
            kind,
            CharacterCanonClaimStatus.StronglySupported,
            CharacterCanonConfidenceBand.High,
            EvidenceIds(evidence));

    private static CharacterCanonicalTension Tension(
        CharacterCanonicalTensionKind kind,
        params int[] evidence)
        => new(
            kind,
            CharacterCanonClaimStatus.StronglySupported,
            CharacterCanonClaimStatus.StronglySupported,
            CharacterCanonConfidenceBand.High,
            CharacterCanonConfidenceBand.High,
            EvidenceIds(evidence));

    private static CharacterInterpretiveBoundaryEvidence Boundary(
        CharacterCanonInterpretiveBoundary boundary,
        params int[] evidence)
        => new(boundary, EvidenceIds(evidence));

    private static CharacterCanonContextObservation Context(
        string contextId,
        CharacterWelfareConflict welfareConflict,
        CharacterRolePerformanceContext rolePerformance,
        params int[] evidence)
        => new(
            contextId,
            welfareConflict,
            rolePerformance,
            EvidenceIds(evidence));

    private static CharacterCanonUnresolvedQuestion Unknown(
        CharacterCanonUnresolvedQuestionId question,
        params int[] evidence)
        => new(question, EvidenceIds(evidence));

    private static IEnumerable<string> EvidenceIds(
        IEnumerable<int> evidence)
        => evidence.Select(EvidenceId);

    private static string EvidenceId(int number)
    {
        if (number < 1 || number > ReviewedEvidenceCardCount)
            throw new ArgumentOutOfRangeException(nameof(number));
        return $"OC-{number:000}";
    }
}
