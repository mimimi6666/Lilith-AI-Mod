using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterCanonFidelityPreviewAdmission
{
    PreviewCreated = 0,
    BaselineExpired = 1,
    RuntimeIdentityMismatch = 2,
    SituationRevisionMismatch = 3,
    CandidateIdentityMismatch = 4,
    CandidateRejected = 5,
    BaselineVariantMismatch = 6
}

internal enum CharacterCanonFidelitySelectionReason
{
    NoApplicableCanonConstraint = 0,
    PreserveNonIntervention = 1,
    PreserveConsentClarity = 2,
    PreserveTypedUnknown = 3
}

internal sealed class CharacterCanonFidelityExpressionPreview
{
    internal string PreviewId { get; }
    internal CharacterExpressionPreviewRecord BaselinePreview { get; }
    internal string LegacyProfileId { get; }
    internal string LegacyProfileSemanticHash { get; }
    internal string CandidateProfileId { get; }
    internal string CandidateProfileSemanticHash { get; }
    internal string BaselineVariantId { get; }
    internal string CanonVariantId { get; }
    internal CharacterCanonFidelitySelectionReason SelectionReason { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal bool MetadataOnly => true;
    internal bool Applied => false;
    internal bool Executable => false;
    internal bool MayChangeDecision => false;
    internal bool MayChangeSemanticCommitment => false;
    internal bool MayChangeConsent => false;
    internal bool MayChangeSafetyEnvelope => false;
    internal bool MayChangeBehaviorIntensity => false;
    internal bool MayWritePrompt => false;
    internal bool MayProduceReply => false;
    internal bool MayCallProvider => false;
    internal bool MayWriteState => false;
    internal bool MayExecuteCapability => false;

    internal CharacterCanonFidelityExpressionPreview(
        string previewId,
        CharacterExpressionPreviewRecord baselinePreview,
        CharacterCanonDualReadProjection dualRead,
        CanonGroundedCharacterProfile candidateProfile,
        string canonVariantId,
        CharacterCanonFidelitySelectionReason selectionReason,
        DateTimeOffset createdAt)
    {
        PreviewId = CharacterRuntimeContractGuard.Identifier(
            previewId,
            nameof(previewId));
        BaselinePreview = baselinePreview
            ?? throw new ArgumentNullException(nameof(baselinePreview));
        if (dualRead == null)
            throw new ArgumentNullException(nameof(dualRead));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));
        if (!Enum.IsDefined(
                typeof(CharacterCanonFidelitySelectionReason),
                selectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(selectionReason));
        }

        LegacyProfileId = dualRead.LegacyProfileId;
        LegacyProfileSemanticHash = dualRead.LegacySemanticHash;
        CandidateProfileId = candidateProfile.Identity.ProfileId;
        CandidateProfileSemanticHash =
            candidateProfile.Identity.SemanticHash;
        BaselineVariantId = baselinePreview.SelectedVariantId;
        CanonVariantId = CharacterRuntimeContractGuard.Identifier(
            canonVariantId,
            nameof(canonVariantId));
        SelectionReason = selectionReason;
        CreatedAt = CharacterRuntimeContractGuard.Timestamp(
            createdAt,
            nameof(createdAt));

        var intent = baselinePreview.Intent;
        if (!intent.EquivalentExpressions.SemanticVariantIds.Contains(
                BaselineVariantId,
                StringComparer.Ordinal)
            || !intent.EquivalentExpressions.SemanticVariantIds.Contains(
                CanonVariantId,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Both CG-D styles must remain inside the exact accepted "
                + "EquivalentExpressionSet.");
        }
        if (!intent.RuntimeIdentity.Matches(dualRead.RuntimeIdentity)
            || intent.SituationRevision != dualRead.SituationRevision
            || !string.Equals(
                CandidateProfileId,
                dualRead.CandidateProfileId,
                StringComparison.Ordinal)
            || !string.Equals(
                CandidateProfileSemanticHash,
                dualRead.CandidateSemanticHash,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "CG-D must retain the exact runtime, Situation and "
                + "candidate-profile identity.");
        }
    }
}

internal sealed class CharacterCanonFidelityPreviewResult
{
    internal CharacterCanonFidelityPreviewAdmission Admission { get; }
    internal CharacterCanonFidelityExpressionPreview? Preview { get; }
    internal bool Accepted =>
        Admission == CharacterCanonFidelityPreviewAdmission.PreviewCreated;

    internal CharacterCanonFidelityPreviewResult(
        CharacterCanonFidelityPreviewAdmission admission,
        CharacterCanonFidelityExpressionPreview? preview)
    {
        if (!Enum.IsDefined(
                typeof(CharacterCanonFidelityPreviewAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission
                == CharacterCanonFidelityPreviewAdmission.PreviewCreated)
            != (preview != null))
        {
            throw new ArgumentException(
                "Only an accepted CG-D result may carry a Preview.",
                nameof(preview));
        }

        Admission = admission;
        Preview = preview;
    }
}

internal static class CharacterCanonFidelityPreviewProjector
{
    private const string Balanced = "style-balanced";
    private const string Direct = "style-direct";
    private const string Restrained = "style-restrained";

    internal static CharacterCanonFidelityPreviewResult Project(
        CharacterExpressionPreviewRecord baselinePreview,
        CharacterCanonDualReadProjection dualRead,
        CanonGroundedCharacterProfile candidateProfile,
        DateTimeOffset observedAt)
    {
        if (baselinePreview == null)
            throw new ArgumentNullException(nameof(baselinePreview));
        if (dualRead == null)
            throw new ArgumentNullException(nameof(dualRead));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        var intent = baselinePreview.Intent;

        if (intent.ValidUntil <= timestamp)
            return Rejected(CharacterCanonFidelityPreviewAdmission.BaselineExpired);
        if (!intent.RuntimeIdentity.Matches(dualRead.RuntimeIdentity))
        {
            return Rejected(
                CharacterCanonFidelityPreviewAdmission
                    .RuntimeIdentityMismatch);
        }
        if (intent.SituationRevision != dualRead.SituationRevision)
        {
            return Rejected(
                CharacterCanonFidelityPreviewAdmission
                    .SituationRevisionMismatch);
        }
        if (!MatchesCandidate(dualRead, candidateProfile))
        {
            return Rejected(
                CharacterCanonFidelityPreviewAdmission
                    .CandidateIdentityMismatch);
        }
        if (!CharacterCanonProfileValidator.Validate(
                candidateProfile.Identity,
                candidateProfile.Content).Accepted)
        {
            return Rejected(
                CharacterCanonFidelityPreviewAdmission.CandidateRejected);
        }
        if (!intent.EquivalentExpressions.SemanticVariantIds.Contains(
                baselinePreview.SelectedVariantId,
                StringComparer.Ordinal))
        {
            return Rejected(
                CharacterCanonFidelityPreviewAdmission
                    .BaselineVariantMismatch);
        }

        var selection = Select(
            intent,
            candidateProfile,
            baselinePreview.SelectedVariantId);
        var previewId = "canon-expression-" + Sha256(
            string.Join(
                "|",
                baselinePreview.PreviewId,
                intent.IntentId,
                dualRead.SemanticComparisonFingerprint,
                candidateProfile.Identity.SemanticHash,
                selection.VariantId,
                (int)selection.Reason))[..20];
        return new CharacterCanonFidelityPreviewResult(
            CharacterCanonFidelityPreviewAdmission.PreviewCreated,
            new CharacterCanonFidelityExpressionPreview(
                previewId,
                baselinePreview,
                dualRead,
                candidateProfile,
                selection.VariantId,
                selection.Reason,
                timestamp));
    }

    private static (string VariantId,
        CharacterCanonFidelitySelectionReason Reason) Select(
        ExpressionIntent intent,
        CanonGroundedCharacterProfile profile,
        string baselineVariantId)
    {
        var variants = intent.EquivalentExpressions.SemanticVariantIds;
        if (intent.Capability == CharacterExpressionCapability.NonIntervention)
        {
            return (
                Available(variants, Restrained, baselineVariantId),
                CharacterCanonFidelitySelectionReason
                    .PreserveNonIntervention);
        }
        if (intent.SafetyEnvelope.ConsentState
            == CharacterConsentState.RequiredAndUnsatisfied)
        {
            return (
                Available(variants, Direct, baselineVariantId),
                CharacterCanonFidelitySelectionReason
                    .PreserveConsentClarity);
        }
        var preservesUnknown =
            profile.Content.Unknowns.Count > 0
            && profile.Content.Boundaries.Any(
                value => value.Boundary
                    == CharacterCanonInterpretiveBoundary
                        .UnknownMustRemainExplicit);
        if (intent.Capability
                == CharacterExpressionCapability.PlayerTriggeredResponse
            && preservesUnknown)
        {
            return (
                Available(variants, Restrained, baselineVariantId),
                CharacterCanonFidelitySelectionReason
                    .PreserveTypedUnknown);
        }

        return (
            Available(variants, Balanced, baselineVariantId),
            CharacterCanonFidelitySelectionReason
                .NoApplicableCanonConstraint);
    }

    private static string Available(
        System.Collections.Generic.IReadOnlyList<string> variants,
        string preferred,
        string fallback)
        => variants.Contains(preferred, StringComparer.Ordinal)
            ? preferred
            : fallback;

    private static bool MatchesCandidate(
        CharacterCanonDualReadProjection dualRead,
        CanonGroundedCharacterProfile candidate)
        => string.Equals(
                dualRead.CandidateProfileId,
                candidate.Identity.ProfileId,
                StringComparison.Ordinal)
            && dualRead.CandidateProfileVersion
                == candidate.Identity.ProfileVersion
            && dualRead.CandidateSchemaVersion
                == candidate.Identity.SchemaVersion
            && string.Equals(
                dualRead.CandidateCorpusVersion,
                candidate.Identity.CorpusVersion,
                StringComparison.Ordinal)
            && string.Equals(
                dualRead.CandidateCorpusHash,
                candidate.Identity.CorpusSha256,
                StringComparison.Ordinal)
            && string.Equals(
                dualRead.CandidateSemanticHash,
                candidate.Identity.SemanticHash,
                StringComparison.Ordinal);

    private static CharacterCanonFidelityPreviewResult Rejected(
        CharacterCanonFidelityPreviewAdmission admission)
        => new(admission, preview: null);

    private static string Sha256(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
