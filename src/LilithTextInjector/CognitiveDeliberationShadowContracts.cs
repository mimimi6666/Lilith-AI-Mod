using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

[Flags]
internal enum CognitiveDomain
{
    None = 0,
    Memory = 1 << 0,
    UserAffect = 1 << 1,
    Boundary = 1 << 2,
    OpenLoop = 1 << 3,
    Proactive = 1 << 4,
    All = Memory | UserAffect | Boundary | OpenLoop | Proactive
}

internal enum CognitiveInputSource
{
    FixedCorpusOracle = 0,
    PlayerTurnOperation = 1
}

internal enum CognitiveProducerKind
{
    DeterministicPolicy = 0,
    FixedCorpusOracle = 1,
    SemanticModel = 2,
    LocalClassifier = 3
}

internal enum CognitiveClaimKind
{
    Episode = 0,
    Fact = 1,
    Correction = 2,
    ExplicitRemember = 3,
    UserAffect = 4,
    Boundary = 5,
    OpenLoop = 6,
    OpenLoopResolution = 7,
    CareIntent = 8,
    FollowUpIntent = 9,
    SubjectAttribution = 10,
    InteractionPreference = 11
}

internal enum CognitiveSubjectScope
{
    Unknown = 0,
    User = 1,
    ThirdParty = 2,
    Lilith = 3,
    Conversation = 4
}

internal enum CognitiveTemporalScope
{
    Unknown = 0,
    CurrentTurn = 1,
    Recent = 2,
    Historical = 3,
    Durable = 4
}

internal enum CognitiveConfidenceBand
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal enum CognitiveEpistemicState
{
    Supported = 0,
    Plausible = 1,
    Ambiguous = 2,
    Contradicted = 3,
    NeedMoreEvidence = 4,
    Abstained = 5
}

internal enum CognitivePrivacyClass
{
    Public = 0,
    Private = 1,
    Sensitive = 2
}

internal enum CognitiveInterruptibilityBand
{
    Unknown = 0,
    LowRisk = 1,
    MediumRisk = 2,
    HighRisk = 3
}

internal enum CognitiveEvidenceKind
{
    FixedCorpusOracle = 0,
    CurrentTurn = 1,
    MemoryWorkingSet = 2,
    ActiveBoundary = 3,
    ActiveOpenLoop = 4,
    CharacterProjection = 5,
    ProactiveSituation = 6
}

internal enum CognitiveRelationKind
{
    Supports = 0,
    Contradicts = 1,
    Cautions = 2,
    Blocks = 3,
    Defers = 4,
    RequiresConfirmation = 5,
    InsufficientEvidenceFor = 6,
    Unrelated = 7,
    Supersedes = 8,
    TemporallyLimits = 9
}

internal enum CognitiveRelationReason
{
    EvidenceSupport = 0,
    EvidenceConflict = 1,
    ExplicitBoundary = 2,
    PrivacyPolicy = 3,
    SafetyPolicy = 4,
    ConsentPolicy = 5,
    ExplicitCorrection = 6,
    SubjectAttribution = 7,
    TemporalScope = 8,
    MissingReferent = 9,
    InsufficientEvidence = 10,
    InteractionPressure = 11,
    UnrelatedScope = 12,
    OpenLoopResolution = 13
}

internal enum CognitiveRelationStrength
{
    Weak = 0,
    Moderate = 1,
    Strong = 2,
    Deterministic = 3
}

internal enum CognitiveConflictKind
{
    Epistemic = 0,
    Temporal = 1,
    Attribution = 2,
    Policy = 3,
    Ownership = 4
}

internal enum CognitiveInteractionStance
{
    NoOpinion = 0,
    Neutral = 1,
    NonCommittal = 2,
    Acknowledge = 3,
    Care = 4,
    Clarify = 5
}

internal enum CognitiveFollowUpMode
{
    NoOpinion = 0,
    None = 1,
    Optional = 2,
    AskClarification = 3
}

internal enum CognitiveMemoryPreviewKind
{
    None = 0,
    Episode = 1,
    Fact = 2,
    Correction = 3,
    ExplicitRemember = 4
}

internal enum CognitiveShadowDisposition
{
    NoOpinion = 0,
    Silence = 1,
    Observe = 2,
    Defer = 3,
    Respond = 4,
    AskConsent = 5
}

internal enum CognitiveShadowAdmission
{
    Evaluated = 0,
    SnapshotExpired = 1,
    NoUsableProposals = 2
}

internal enum CognitiveComparisonBaselineSource
{
    FixedCorpusOracle = 0,
    ProductionObservation = 1
}

internal enum CognitiveComparisonDifference
{
    BaselineUnknown = 0,
    EquivalentDisposition = 1,
    DifferentDisposition = 2
}

internal enum CognitiveAuthority
{
    ShadowOnly = 0
}

internal sealed class CognitiveInputSnapshot
{
    internal const int SchemaVersion = 1;
    internal const int MaximumTurnUnicodeScalars = 512;

    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal CognitiveInputSource InputSource { get; }
    internal string ProcessEpochId { get; }
    internal long RuntimeGeneration { get; }
    internal string OperationId { get; }
    internal long OperationGeneration { get; }
    internal PlayerTurnOperationHandle? PlayerTurnSourceHandle { get; }
    internal string EphemeralTurnText { get; }
    internal string TurnHash { get; }
    internal CognitivePrivacyClass PrivacyClass { get; }
    internal long SituationRevision { get; }
    internal string SituationFingerprint { get; }
    internal long MemoryWorkingSetRevision { get; }
    internal string MemoryWorkingSetFingerprint { get; }
    internal string CharacterProjectionFingerprint { get; }
    internal string? ActiveBoundaryReference { get; }
    internal string? ActiveOpenLoopReference { get; }
    internal CognitiveInterruptibilityBand Interruptibility { get; }
    internal CognitiveDomain AllowedDomains { get; }
    internal long CapturedAtMonotonicTicks { get; }
    internal long ExpiresAtMonotonicTicks { get; }

    internal CognitiveInputSnapshot(
        Guid snapshotId,
        CognitiveInputSource inputSource,
        string processEpochId,
        long runtimeGeneration,
        string operationId,
        long operationGeneration,
        string ephemeralTurnText,
        string turnHash,
        CognitivePrivacyClass privacyClass,
        long situationRevision,
        string situationFingerprint,
        long memoryWorkingSetRevision,
        string memoryWorkingSetFingerprint,
        string characterProjectionFingerprint,
        string? activeBoundaryReference,
        string? activeOpenLoopReference,
        CognitiveInterruptibilityBand interruptibility,
        CognitiveDomain allowedDomains,
        long capturedAtMonotonicTicks,
        long expiresAtMonotonicTicks,
        PlayerTurnOperationHandle? playerTurnSourceHandle = null)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot identity cannot be empty.", nameof(snapshotId));
        SnapshotId = snapshotId;
        InputSource = CognitiveShadowContractGuard.Enum(inputSource, nameof(inputSource));
        ProcessEpochId = CognitiveShadowContractGuard.Identifier(
            processEpochId,
            nameof(processEpochId));
        RuntimeGeneration = CognitiveShadowContractGuard.Positive(
            runtimeGeneration,
            nameof(runtimeGeneration));
        OperationId = CognitiveShadowContractGuard.Identifier(
            operationId,
            nameof(operationId));
        OperationGeneration = CognitiveShadowContractGuard.Positive(
            operationGeneration,
            nameof(operationGeneration));
        if (playerTurnSourceHandle != null
            && (InputSource != CognitiveInputSource.PlayerTurnOperation
                || !string.Equals(
                    playerTurnSourceHandle.ProcessEpochId,
                    ProcessEpochId,
                    StringComparison.Ordinal)
                || playerTurnSourceHandle.RuntimeGeneration
                    != RuntimeGeneration
                || !string.Equals(
                    playerTurnSourceHandle.OperationId,
                    OperationId,
                    StringComparison.Ordinal)
                || playerTurnSourceHandle.OperationGeneration
                    != OperationGeneration))
        {
            throw new ArgumentException(
                "PlayerTurn source handle must match snapshot identity.",
                nameof(playerTurnSourceHandle));
        }
        PlayerTurnSourceHandle = playerTurnSourceHandle;
        EphemeralTurnText = CognitiveShadowContractGuard.Text(
            ephemeralTurnText,
            MaximumTurnUnicodeScalars,
            nameof(ephemeralTurnText));
        TurnHash = CognitiveShadowContractGuard.Fingerprint(
            turnHash,
            nameof(turnHash));
        if (!string.Equals(
                TurnHash,
                CognitiveShadowFingerprint.Hash(EphemeralTurnText),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Turn hash does not match the frozen ephemeral text.",
                nameof(turnHash));
        }
        PrivacyClass = CognitiveShadowContractGuard.Enum(
            privacyClass,
            nameof(privacyClass));
        SituationRevision = CognitiveShadowContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        SituationFingerprint = CognitiveShadowContractGuard.Fingerprint(
            situationFingerprint,
            nameof(situationFingerprint));
        MemoryWorkingSetRevision = CognitiveShadowContractGuard.NonNegative(
            memoryWorkingSetRevision,
            nameof(memoryWorkingSetRevision));
        MemoryWorkingSetFingerprint = CognitiveShadowContractGuard.FingerprintOrSentinel(
            memoryWorkingSetFingerprint,
            nameof(memoryWorkingSetFingerprint));
        CharacterProjectionFingerprint = CognitiveShadowContractGuard.FingerprintOrSentinel(
            characterProjectionFingerprint,
            nameof(characterProjectionFingerprint));
        ActiveBoundaryReference = CognitiveShadowContractGuard.OptionalIdentifier(
            activeBoundaryReference,
            nameof(activeBoundaryReference));
        ActiveOpenLoopReference = CognitiveShadowContractGuard.OptionalIdentifier(
            activeOpenLoopReference,
            nameof(activeOpenLoopReference));
        Interruptibility = CognitiveShadowContractGuard.Enum(
            interruptibility,
            nameof(interruptibility));
        if (allowedDomains == CognitiveDomain.None
            || (allowedDomains & ~CognitiveDomain.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedDomains));
        }
        AllowedDomains = allowedDomains;
        CapturedAtMonotonicTicks = CognitiveShadowContractGuard.NonNegative(
            capturedAtMonotonicTicks,
            nameof(capturedAtMonotonicTicks));
        if (expiresAtMonotonicTicks <= CapturedAtMonotonicTicks)
            throw new ArgumentOutOfRangeException(nameof(expiresAtMonotonicTicks));
        ExpiresAtMonotonicTicks = expiresAtMonotonicTicks;
        SnapshotFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                SnapshotId.ToString("N"),
                ((int)InputSource).ToString(CultureInfo.InvariantCulture),
                ProcessEpochId,
                RuntimeGeneration.ToString(CultureInfo.InvariantCulture),
                OperationId,
                OperationGeneration.ToString(CultureInfo.InvariantCulture),
                TurnHash,
                ((int)PrivacyClass).ToString(CultureInfo.InvariantCulture),
                SituationRevision.ToString(CultureInfo.InvariantCulture),
                SituationFingerprint,
                MemoryWorkingSetRevision.ToString(
                    CultureInfo.InvariantCulture),
                MemoryWorkingSetFingerprint,
                CharacterProjectionFingerprint,
                ActiveBoundaryReference == null ? "0" : "1",
                ActiveBoundaryReference ?? string.Empty,
                ActiveOpenLoopReference == null ? "0" : "1",
                ActiveOpenLoopReference ?? string.Empty,
                ((int)Interruptibility).ToString(
                    CultureInfo.InvariantCulture),
                ((int)AllowedDomains).ToString(CultureInfo.InvariantCulture),
                CapturedAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture),
                ExpiresAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture)));
    }
}

internal sealed class CognitiveEvidenceReference
{
    internal const int MaximumSpanLength = 512;

    internal string EvidenceId { get; }
    internal CognitiveEvidenceKind Kind { get; }
    internal string SourceReference { get; }
    internal int StartOffset { get; }
    internal int SpanLength { get; }
    internal string ContentHash { get; }

    internal CognitiveEvidenceReference(
        string evidenceId,
        CognitiveEvidenceKind kind,
        string sourceReference,
        int startOffset,
        int spanLength,
        string contentHash)
    {
        EvidenceId = CognitiveShadowContractGuard.Identifier(
            evidenceId,
            nameof(evidenceId));
        Kind = CognitiveShadowContractGuard.Enum(kind, nameof(kind));
        SourceReference = CognitiveShadowContractGuard.Identifier(
            sourceReference,
            nameof(sourceReference));
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        if (spanLength < 0 || spanLength > MaximumSpanLength)
            throw new ArgumentOutOfRangeException(nameof(spanLength));
        StartOffset = startOffset;
        SpanLength = spanLength;
        ContentHash = CognitiveShadowContractGuard.Fingerprint(
            contentHash,
            nameof(contentHash));
    }
}

internal sealed class CognitiveProposalPayload
{
    internal CognitiveDomain Domain { get; }
    internal CognitiveClaimKind ClaimKind { get; }
    internal string TopicReference { get; }
    internal CognitiveSubjectScope SubjectScope { get; }
    internal CognitiveTemporalScope TemporalScope { get; }
    internal CognitiveShadowDisposition SuggestedDisposition { get; }
    internal CognitiveInteractionStance SuggestedStance { get; }
    internal CognitiveFollowUpMode SuggestedFollowUp { get; }
    internal CognitiveMemoryPreviewKind SuggestedMemoryPreview { get; }
    internal bool RequiresConfirmationForStateMutation { get; }

    internal CognitiveProposalPayload(
        CognitiveDomain domain,
        CognitiveClaimKind claimKind,
        string topicReference,
        CognitiveSubjectScope subjectScope,
        CognitiveTemporalScope temporalScope,
        CognitiveShadowDisposition suggestedDisposition,
        CognitiveInteractionStance suggestedStance,
        CognitiveFollowUpMode suggestedFollowUp,
        CognitiveMemoryPreviewKind suggestedMemoryPreview,
        bool requiresConfirmationForStateMutation)
    {
        Domain = CognitiveShadowContractGuard.Domain(domain, nameof(domain));
        ClaimKind = CognitiveShadowContractGuard.Enum(
            claimKind,
            nameof(claimKind));
        ValidateClaimOwner(domain, claimKind);
        TopicReference = CognitiveShadowContractGuard.Identifier(
            topicReference,
            nameof(topicReference));
        SubjectScope = CognitiveShadowContractGuard.Enum(
            subjectScope,
            nameof(subjectScope));
        TemporalScope = CognitiveShadowContractGuard.Enum(
            temporalScope,
            nameof(temporalScope));
        SuggestedDisposition = CognitiveShadowContractGuard.Enum(
            suggestedDisposition,
            nameof(suggestedDisposition));
        SuggestedStance = CognitiveShadowContractGuard.Enum(
            suggestedStance,
            nameof(suggestedStance));
        SuggestedFollowUp = CognitiveShadowContractGuard.Enum(
            suggestedFollowUp,
            nameof(suggestedFollowUp));
        SuggestedMemoryPreview = CognitiveShadowContractGuard.Enum(
            suggestedMemoryPreview,
            nameof(suggestedMemoryPreview));
        RequiresConfirmationForStateMutation =
            requiresConfirmationForStateMutation;
    }

    private static void ValidateClaimOwner(
        CognitiveDomain domain,
        CognitiveClaimKind claimKind)
    {
        var accepted = domain switch
        {
            CognitiveDomain.Memory => claimKind is
                CognitiveClaimKind.Episode
                or CognitiveClaimKind.Fact
                or CognitiveClaimKind.Correction
                or CognitiveClaimKind.ExplicitRemember
                or CognitiveClaimKind.SubjectAttribution,
            CognitiveDomain.UserAffect =>
                claimKind == CognitiveClaimKind.UserAffect,
            CognitiveDomain.Boundary => claimKind is
                CognitiveClaimKind.Boundary
                or CognitiveClaimKind.InteractionPreference,
            CognitiveDomain.OpenLoop => claimKind is
                CognitiveClaimKind.OpenLoop
                or CognitiveClaimKind.OpenLoopResolution,
            CognitiveDomain.Proactive => claimKind is
                CognitiveClaimKind.CareIntent
                or CognitiveClaimKind.FollowUpIntent
                or CognitiveClaimKind.InteractionPreference,
            _ => false
        };
        if (!accepted)
        {
            throw new ArgumentException(
                "Claim kind does not belong to the proposal domain.",
                nameof(claimKind));
        }
    }
}

internal sealed class CognitiveProposal
{
    internal const int MaximumEvidenceReferences = 8;

    internal string ProposalId { get; }
    internal CognitiveDomain Domain { get; }
    internal CognitiveProducerKind ProducerKind { get; }
    internal string ProducerId { get; }
    internal string ProducerVersion { get; }
    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal CognitiveProposalPayload Payload { get; }
    internal CognitiveConfidenceBand Confidence { get; }
    internal CognitiveEpistemicState EpistemicState { get; }
    internal CognitivePrivacyClass PrivacyClass { get; }
    internal IReadOnlyList<CognitiveEvidenceReference> Evidence { get; }
    internal long CreatedAtMonotonicTicks { get; }
    internal long ExpiresAtMonotonicTicks { get; }
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;
    internal bool CanMutateState => false;
    internal bool CanPublish => false;
    internal bool CanInvokeTools => false;

    internal CognitiveProposal(
        string proposalId,
        CognitiveDomain domain,
        CognitiveProducerKind producerKind,
        string producerId,
        string producerVersion,
        Guid snapshotId,
        string snapshotFingerprint,
        CognitiveProposalPayload payload,
        CognitiveConfidenceBand confidence,
        CognitiveEpistemicState epistemicState,
        CognitivePrivacyClass privacyClass,
        IReadOnlyList<CognitiveEvidenceReference> evidence,
        long createdAtMonotonicTicks,
        long expiresAtMonotonicTicks)
    {
        ProposalId = CognitiveShadowContractGuard.Identifier(
            proposalId,
            nameof(proposalId));
        Domain = CognitiveShadowContractGuard.Domain(domain, nameof(domain));
        ProducerKind = CognitiveShadowContractGuard.Enum(
            producerKind,
            nameof(producerKind));
        ProducerId = CognitiveShadowContractGuard.Identifier(
            producerId,
            nameof(producerId));
        ProducerVersion = CognitiveShadowContractGuard.Identifier(
            producerVersion,
            nameof(producerVersion));
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot identity cannot be empty.", nameof(snapshotId));
        SnapshotId = snapshotId;
        SnapshotFingerprint = CognitiveShadowContractGuard.Fingerprint(
            snapshotFingerprint,
            nameof(snapshotFingerprint));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        if (Payload.Domain != Domain)
        {
            throw new ArgumentException(
                "Proposal payload must belong to the exact proposal domain.",
                nameof(payload));
        }
        Confidence = CognitiveShadowContractGuard.Enum(
            confidence,
            nameof(confidence));
        EpistemicState = CognitiveShadowContractGuard.Enum(
            epistemicState,
            nameof(epistemicState));
        PrivacyClass = CognitiveShadowContractGuard.Enum(
            privacyClass,
            nameof(privacyClass));
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        if (evidence.Count > MaximumEvidenceReferences
            || (epistemicState != CognitiveEpistemicState.Abstained
                && evidence.Count == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(evidence));
        }
        var evidenceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in evidence)
        {
            if (reference == null || !evidenceIds.Add(reference.EvidenceId))
            {
                throw new ArgumentException(
                    "Evidence references must be non-null and unique.",
                    nameof(evidence));
            }
        }
        Evidence = Array.AsReadOnly(evidence.ToArray());
        CreatedAtMonotonicTicks = CognitiveShadowContractGuard.NonNegative(
            createdAtMonotonicTicks,
            nameof(createdAtMonotonicTicks));
        if (expiresAtMonotonicTicks <= CreatedAtMonotonicTicks)
            throw new ArgumentOutOfRangeException(nameof(expiresAtMonotonicTicks));
        ExpiresAtMonotonicTicks = expiresAtMonotonicTicks;
    }
}

internal sealed class CognitiveProposalSet
{
    internal const int MaximumProposals = 24;

    internal CognitiveInputSnapshot Snapshot { get; }
    internal IReadOnlyList<CognitiveProposal> Proposals { get; }

    internal CognitiveProposalSet(
        CognitiveInputSnapshot snapshot,
        IReadOnlyList<CognitiveProposal> proposals)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (proposals == null)
            throw new ArgumentNullException(nameof(proposals));
        if (proposals.Count > MaximumProposals)
            throw new ArgumentOutOfRangeException(nameof(proposals));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            if (proposal == null)
                throw new ArgumentException("Proposal set cannot contain null.", nameof(proposals));
            if (!ids.Add(proposal.ProposalId))
                throw new ArgumentException("Proposal identities must be unique.", nameof(proposals));
            if (proposal.SnapshotId != Snapshot.SnapshotId
                || !string.Equals(
                    proposal.SnapshotFingerprint,
                    Snapshot.SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every proposal must reference the exact frozen snapshot.",
                    nameof(proposals));
            }
            if ((Snapshot.AllowedDomains & proposal.Domain) == 0)
                throw new ArgumentException("Proposal domain is not admitted.", nameof(proposals));
            if (proposal.ProducerKind
                    == CognitiveProducerKind.FixedCorpusOracle
                && Snapshot.InputSource
                    != CognitiveInputSource.FixedCorpusOracle)
            {
                throw new ArgumentException(
                    "Fixed-corpus proposal provenance requires a fixed-corpus snapshot.",
                    nameof(proposals));
            }
            if (proposal.PrivacyClass < Snapshot.PrivacyClass)
            {
                throw new ArgumentException(
                    "Proposal cannot downgrade snapshot privacy.",
                    nameof(proposals));
            }
            var turnLength = Snapshot.EphemeralTurnText
                .EnumerateRunes()
                .Count();
            foreach (var reference in proposal.Evidence)
            {
                ValidateEvidence(
                    Snapshot,
                    reference,
                    turnLength,
                    nameof(proposals));
            }
            if (proposal.CreatedAtMonotonicTicks
                    < Snapshot.CapturedAtMonotonicTicks
                || proposal.ExpiresAtMonotonicTicks
                    > Snapshot.ExpiresAtMonotonicTicks)
            {
                throw new ArgumentException(
                    "Proposal lifetime must remain inside the snapshot lifetime.",
                    nameof(proposals));
            }
        }
        Proposals = Array.AsReadOnly(proposals.ToArray());
    }

    private static void ValidateEvidence(
        CognitiveInputSnapshot snapshot,
        CognitiveEvidenceReference reference,
        int turnLength,
        string parameterName)
    {
        switch (reference.Kind)
        {
            case CognitiveEvidenceKind.FixedCorpusOracle:
                if (snapshot.InputSource
                    != CognitiveInputSource.FixedCorpusOracle)
                {
                    throw new ArgumentException(
                        "Fixed-corpus evidence requires a fixed-corpus snapshot.",
                        parameterName);
                }
                goto case CognitiveEvidenceKind.CurrentTurn;
            case CognitiveEvidenceKind.CurrentTurn:
                if (!string.Equals(
                        reference.SourceReference,
                        "turn:" + snapshot.OperationId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        reference.ContentHash,
                        snapshot.TurnHash,
                        StringComparison.Ordinal)
                    || reference.SpanLength > turnLength
                    || reference.StartOffset
                        > turnLength - reference.SpanLength)
                {
                    throw new ArgumentException(
                        "Turn evidence must match the frozen snapshot span.",
                        parameterName);
                }
                return;
            case CognitiveEvidenceKind.MemoryWorkingSet:
                RequireMetadataEvidence(
                    reference,
                    snapshot.MemoryWorkingSetFingerprint,
                    parameterName);
                return;
            case CognitiveEvidenceKind.ActiveBoundary:
                RequireReferencedEvidence(
                    reference,
                    snapshot.ActiveBoundaryReference,
                    parameterName);
                return;
            case CognitiveEvidenceKind.ActiveOpenLoop:
                RequireReferencedEvidence(
                    reference,
                    snapshot.ActiveOpenLoopReference,
                    parameterName);
                return;
            case CognitiveEvidenceKind.CharacterProjection:
                RequireMetadataEvidence(
                    reference,
                    snapshot.CharacterProjectionFingerprint,
                    parameterName);
                return;
            case CognitiveEvidenceKind.ProactiveSituation:
                RequireMetadataEvidence(
                    reference,
                    snapshot.SituationFingerprint,
                    parameterName);
                return;
            default:
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireReferencedEvidence(
        CognitiveEvidenceReference reference,
        string? expectedReference,
        string parameterName)
    {
        if (expectedReference == null
            || !string.Equals(
                reference.SourceReference,
                expectedReference,
                StringComparison.Ordinal)
            || !string.Equals(
                reference.ContentHash,
                CognitiveShadowFingerprint.Hash(expectedReference),
                StringComparison.Ordinal)
            || reference.StartOffset != 0
            || reference.SpanLength != 0)
        {
            throw new ArgumentException(
                "Referenced evidence must match the frozen snapshot reference.",
                parameterName);
        }
    }

    private static void RequireMetadataEvidence(
        CognitiveEvidenceReference reference,
        string expectedFingerprint,
        string parameterName)
    {
        if (expectedFingerprint is "MISSING" or "UNAVAILABLE"
            || !string.Equals(
                reference.ContentHash,
                expectedFingerprint,
                StringComparison.Ordinal)
            || reference.StartOffset != 0
            || reference.SpanLength != 0)
        {
            throw new ArgumentException(
                "Metadata evidence must match the frozen snapshot fingerprint.",
                parameterName);
        }
    }
}

internal sealed class CognitiveRelationCandidate
{
    internal string RelationId { get; }
    internal string FromProposalId { get; }
    internal string ToProposalId { get; }
    internal CognitiveRelationKind Kind { get; }
    internal CognitiveRelationReason Reason { get; }
    internal CognitiveRelationStrength Strength { get; }
    internal CognitiveProducerKind ProducerKind { get; }

    internal CognitiveRelationCandidate(
        string relationId,
        string fromProposalId,
        string toProposalId,
        CognitiveRelationKind kind,
        CognitiveRelationReason reason,
        CognitiveRelationStrength strength,
        CognitiveProducerKind producerKind)
    {
        RelationId = CognitiveShadowContractGuard.Identifier(
            relationId,
            nameof(relationId));
        FromProposalId = CognitiveShadowContractGuard.Identifier(
            fromProposalId,
            nameof(fromProposalId));
        ToProposalId = CognitiveShadowContractGuard.Identifier(
            toProposalId,
            nameof(toProposalId));
        if (string.Equals(FromProposalId, ToProposalId, StringComparison.Ordinal))
            throw new ArgumentException("A proposal cannot relate to itself.", nameof(toProposalId));
        Kind = CognitiveShadowContractGuard.Enum(kind, nameof(kind));
        Reason = CognitiveShadowContractGuard.Enum(reason, nameof(reason));
        Strength = CognitiveShadowContractGuard.Enum(strength, nameof(strength));
        ProducerKind = CognitiveShadowContractGuard.Enum(
            producerKind,
            nameof(producerKind));
        if (Kind == CognitiveRelationKind.Blocks
            && (!IsHardPolicyReason(Reason)
                || Strength != CognitiveRelationStrength.Deterministic
                || ProducerKind is not CognitiveProducerKind.DeterministicPolicy
                    and not CognitiveProducerKind.FixedCorpusOracle))
        {
            throw new ArgumentException(
                "A hard block requires deterministic policy authority.",
                nameof(kind));
        }
    }

    private static bool IsHardPolicyReason(CognitiveRelationReason reason)
        => reason is CognitiveRelationReason.ExplicitBoundary
            or CognitiveRelationReason.PrivacyPolicy
            or CognitiveRelationReason.SafetyPolicy
            or CognitiveRelationReason.ConsentPolicy;
}

internal sealed class CognitiveConflict
{
    internal string ConflictId { get; }
    internal CognitiveConflictKind Kind { get; }
    internal string RelationId { get; }
    internal string FromProposalId { get; }
    internal string ToProposalId { get; }

    internal CognitiveConflict(CognitiveRelationCandidate relation)
    {
        if (relation == null)
            throw new ArgumentNullException(nameof(relation));
        if (!IsConflictRelation(relation))
        {
            throw new ArgumentException(
                "A non-conflicting relation cannot produce a conflict.",
                nameof(relation));
        }
        ConflictId = CanonicalId(relation);
        Kind = KindFor(relation);
        RelationId = relation.RelationId;
        FromProposalId = relation.FromProposalId;
        ToProposalId = relation.ToProposalId;
    }

    internal static bool IsConflictRelation(
        CognitiveRelationCandidate relation)
        => relation.Kind is not CognitiveRelationKind.Supports
            and not CognitiveRelationKind.Unrelated;

    internal static string CanonicalId(
        CognitiveRelationCandidate relation)
        => "conflict-"
            + CognitiveShadowFingerprint
                .Hash(relation.RelationId)
                .Substring(0, 24);

    internal static CognitiveConflictKind KindFor(
        CognitiveRelationCandidate relation)
    {
        if (relation.Reason == CognitiveRelationReason.SubjectAttribution)
            return CognitiveConflictKind.Attribution;
        if (relation.Reason == CognitiveRelationReason.TemporalScope)
            return CognitiveConflictKind.Temporal;
        if (relation.Kind is CognitiveRelationKind.Blocks
            or CognitiveRelationKind.Cautions
            or CognitiveRelationKind.Defers
            || relation.Reason is CognitiveRelationReason.ExplicitBoundary
                or CognitiveRelationReason.PrivacyPolicy
                or CognitiveRelationReason.SafetyPolicy
                or CognitiveRelationReason.ConsentPolicy
                or CognitiveRelationReason.InteractionPressure)
        {
            return CognitiveConflictKind.Policy;
        }
        return CognitiveConflictKind.Epistemic;
    }
}

internal sealed class CognitiveConflictAnalysis
{
    internal const int MaximumRelations = 48;

    internal CognitiveProposalSet ProposalSet { get; }
    internal IReadOnlyList<CognitiveRelationCandidate> Relations { get; }
    internal IReadOnlyList<CognitiveConflict> Conflicts { get; }

    internal CognitiveConflictAnalysis(
        CognitiveProposalSet proposalSet,
        IReadOnlyList<CognitiveRelationCandidate> relations,
        IReadOnlyList<CognitiveConflict> conflicts)
    {
        ProposalSet = proposalSet
            ?? throw new ArgumentNullException(nameof(proposalSet));
        if (relations == null)
            throw new ArgumentNullException(nameof(relations));
        if (conflicts == null)
            throw new ArgumentNullException(nameof(conflicts));
        if (relations.Count > MaximumRelations
            || conflicts.Count > relations.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(relations));
        }
        var proposalsById = ProposalSet.Proposals.ToDictionary(
            item => item.ProposalId,
            StringComparer.Ordinal);
        var relationsById = new Dictionary<
            string,
            CognitiveRelationCandidate>(StringComparer.Ordinal);
        foreach (var relation in relations)
        {
            if (relation == null
                || !relationsById.TryAdd(relation.RelationId, relation)
                || !proposalsById.ContainsKey(relation.FromProposalId)
                || !proposalsById.ContainsKey(relation.ToProposalId))
            {
                throw new ArgumentException(
                    "Analysis relations must be unique and reference the proposal set.",
                    nameof(relations));
            }
            if (relation.ProducerKind
                    == CognitiveProducerKind.FixedCorpusOracle
                && ProposalSet.Snapshot.InputSource
                    != CognitiveInputSource.FixedCorpusOracle)
            {
                throw new ArgumentException(
                    "Fixed-corpus relation provenance requires a fixed-corpus snapshot.",
                    nameof(relations));
            }
            var fromProposal = proposalsById[relation.FromProposalId];
            var toProposal = proposalsById[relation.ToProposalId];
            if (relation.Kind is CognitiveRelationKind.Supersedes
                    or CognitiveRelationKind.TemporallyLimits
                && IsProtectedBoundary(toProposal)
                && !CanRetireProtectedBoundary(relation, fromProposal))
            {
                throw new ArgumentException(
                    "Retiring a deterministic Boundary requires deterministic Boundary-policy provenance.",
                    nameof(relations));
            }
            if (relation.Kind == CognitiveRelationKind.Blocks
                && (fromProposal.Domain
                        != CognitiveDomain.Boundary
                    || fromProposal.Payload.ClaimKind
                        != CognitiveClaimKind.Boundary
                    || fromProposal.ProducerKind is not
                            CognitiveProducerKind.DeterministicPolicy
                        and not CognitiveProducerKind.FixedCorpusOracle))
            {
                throw new ArgumentException(
                    "A hard block must originate from deterministic Boundary evidence.",
                    nameof(relations));
                }
        }
        var expectedConflictRelations = relations
            .Where(CognitiveConflict.IsConflictRelation)
            .Select(relation => relation.RelationId)
            .ToHashSet(StringComparer.Ordinal);
        if (conflicts.Count != expectedConflictRelations.Count)
        {
            throw new ArgumentException(
                "Analysis must contain the exact derived conflict set.",
                nameof(conflicts));
        }
        var conflictIds = new HashSet<string>(StringComparer.Ordinal);
        var conflictRelationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflict in conflicts)
        {
            if (conflict == null
                || !conflictIds.Add(conflict.ConflictId)
                || !relationsById.TryGetValue(
                    conflict.RelationId,
                    out var relation)
                || !string.Equals(
                    conflict.FromProposalId,
                    relation.FromProposalId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    conflict.ToProposalId,
                    relation.ToProposalId,
                    StringComparison.Ordinal)
                || !conflictRelationIds.Add(conflict.RelationId)
                || !CognitiveConflict.IsConflictRelation(relation)
                || conflict.Kind != CognitiveConflict.KindFor(relation)
                || !string.Equals(
                    conflict.ConflictId,
                    CognitiveConflict.CanonicalId(relation),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Analysis conflicts must map exactly to admitted relations.",
                    nameof(conflicts));
            }
        }
        if (!conflictRelationIds.SetEquals(expectedConflictRelations))
        {
            throw new ArgumentException(
                "Analysis must contain the exact derived conflict set.",
                nameof(conflicts));
        }
        Relations = Array.AsReadOnly(
            relations
                .OrderBy(relation => relation.RelationId, StringComparer.Ordinal)
                .ToArray());
        Conflicts = Array.AsReadOnly(
            conflicts
                .OrderBy(conflict => conflict.ConflictId, StringComparer.Ordinal)
                .ToArray());
    }

    private static bool IsProtectedBoundary(CognitiveProposal proposal)
        => proposal.Domain == CognitiveDomain.Boundary
            && proposal.Payload.ClaimKind == CognitiveClaimKind.Boundary
            && proposal.ProducerKind is
                CognitiveProducerKind.DeterministicPolicy
                or CognitiveProducerKind.FixedCorpusOracle;

    private static bool CanRetireProtectedBoundary(
        CognitiveRelationCandidate relation,
        CognitiveProposal source)
        => relation.Strength == CognitiveRelationStrength.Deterministic
            && relation.ProducerKind is
                CognitiveProducerKind.DeterministicPolicy
                or CognitiveProducerKind.FixedCorpusOracle
            && source.Domain == CognitiveDomain.Boundary
            && source.Payload.ClaimKind is CognitiveClaimKind.Boundary
                or CognitiveClaimKind.InteractionPreference
            && source.ProducerKind is
                CognitiveProducerKind.DeterministicPolicy
                or CognitiveProducerKind.FixedCorpusOracle
            && relation.Reason is CognitiveRelationReason.ExplicitBoundary
                or CognitiveRelationReason.ConsentPolicy
                or CognitiveRelationReason.ExplicitCorrection
                or CognitiveRelationReason.TemporalScope;
}

internal sealed class CognitiveShadowOutcome
{
    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal CognitiveShadowAdmission Admission { get; }
    internal CognitiveEpistemicState EpistemicState { get; }
    internal CognitiveInteractionStance InteractionStance { get; }
    internal CognitiveFollowUpMode FollowUpMode { get; }
    internal CognitiveMemoryPreviewKind MemoryPreview { get; }
    internal CognitiveShadowDisposition ProjectedDisposition { get; }
    internal bool RequiresConfirmation { get; }
    internal bool PolicyBlockApplied { get; }
    internal IReadOnlyList<string> UsedProposalReferenceHashes { get; }
    internal string OutcomeFingerprint { get; }
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;
    internal bool NoStateMutation => true;
    internal bool NoPublication => true;
    internal bool NoAction => true;
    internal bool NoProviderCall => true;
    internal bool NoPromptMutation => true;

    internal CognitiveShadowOutcome(
        Guid snapshotId,
        string snapshotFingerprint,
        CognitiveShadowAdmission admission,
        CognitiveEpistemicState epistemicState,
        CognitiveInteractionStance interactionStance,
        CognitiveFollowUpMode followUpMode,
        CognitiveMemoryPreviewKind memoryPreview,
        CognitiveShadowDisposition projectedDisposition,
        bool requiresConfirmation,
        bool policyBlockApplied,
        IReadOnlyList<string> usedProposalIds)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot identity cannot be empty.", nameof(snapshotId));
        SnapshotId = snapshotId;
        SnapshotFingerprint = CognitiveShadowContractGuard.Fingerprint(
            snapshotFingerprint,
            nameof(snapshotFingerprint));
        Admission = CognitiveShadowContractGuard.Enum(admission, nameof(admission));
        EpistemicState = CognitiveShadowContractGuard.Enum(
            epistemicState,
            nameof(epistemicState));
        InteractionStance = CognitiveShadowContractGuard.Enum(
            interactionStance,
            nameof(interactionStance));
        FollowUpMode = CognitiveShadowContractGuard.Enum(
            followUpMode,
            nameof(followUpMode));
        MemoryPreview = CognitiveShadowContractGuard.Enum(
            memoryPreview,
            nameof(memoryPreview));
        ProjectedDisposition = CognitiveShadowContractGuard.Enum(
            projectedDisposition,
            nameof(projectedDisposition));
        RequiresConfirmation = requiresConfirmation;
        PolicyBlockApplied = policyBlockApplied;
        if (usedProposalIds == null)
            throw new ArgumentNullException(nameof(usedProposalIds));
        if (usedProposalIds.Count > CognitiveProposalSet.MaximumProposals
            || usedProposalIds.Distinct(StringComparer.Ordinal).Count()
                != usedProposalIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(usedProposalIds));
        }
        var references = usedProposalIds
            .Select(id => CognitiveShadowFingerprint.Hash(
                CognitiveShadowContractGuard.Identifier(
                    id,
                    nameof(usedProposalIds))))
            .ToArray();
        UsedProposalReferenceHashes = Array.AsReadOnly(references);
        OutcomeFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SnapshotFingerprint,
                ((int)Admission).ToString(CultureInfo.InvariantCulture),
                ((int)EpistemicState).ToString(
                    CultureInfo.InvariantCulture),
                ((int)InteractionStance).ToString(
                    CultureInfo.InvariantCulture),
                ((int)FollowUpMode).ToString(CultureInfo.InvariantCulture),
                ((int)MemoryPreview).ToString(CultureInfo.InvariantCulture),
                ((int)ProjectedDisposition).ToString(
                    CultureInfo.InvariantCulture),
                RequiresConfirmation ? "1" : "0",
                PolicyBlockApplied ? "1" : "0",
                string.Join(",", references)));
    }
}

internal sealed class CognitiveBaselineObservation
{
    internal string BaselineId { get; }
    internal CognitiveComparisonBaselineSource Source { get; }
    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal CognitiveShadowDisposition? ProjectedDisposition { get; }
    internal string OutcomeReferenceHash { get; }
    internal bool WasProductionObserved { get; }
    internal string BaselineFingerprint { get; }

    internal CognitiveBaselineObservation(
        string baselineId,
        CognitiveComparisonBaselineSource source,
        Guid snapshotId,
        string snapshotFingerprint,
        CognitiveShadowDisposition? projectedDisposition,
        string outcomeReferenceHash,
        bool wasProductionObserved)
    {
        BaselineId = CognitiveShadowContractGuard.Identifier(
            baselineId,
            nameof(baselineId));
        Source = CognitiveShadowContractGuard.Enum(source, nameof(source));
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("Snapshot identity cannot be empty.", nameof(snapshotId));
        SnapshotId = snapshotId;
        SnapshotFingerprint = CognitiveShadowContractGuard.Fingerprint(
            snapshotFingerprint,
            nameof(snapshotFingerprint));
        if (projectedDisposition.HasValue)
        {
            ProjectedDisposition = CognitiveShadowContractGuard.Enum(
                projectedDisposition.Value,
                nameof(projectedDisposition));
        }
        OutcomeReferenceHash = CognitiveShadowContractGuard.Fingerprint(
            outcomeReferenceHash,
            nameof(outcomeReferenceHash));
        if (source == CognitiveComparisonBaselineSource.FixedCorpusOracle
            && wasProductionObserved)
        {
            throw new ArgumentException(
                "A fixed-corpus oracle cannot claim production observation.",
                nameof(wasProductionObserved));
        }
        if (source == CognitiveComparisonBaselineSource.ProductionObservation
            && !wasProductionObserved)
        {
            throw new ArgumentException(
                "Production baseline must be explicitly observed.",
                nameof(wasProductionObserved));
        }
        WasProductionObserved = wasProductionObserved;
        BaselineFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                BaselineId,
                ((int)Source).ToString(CultureInfo.InvariantCulture),
                SnapshotId.ToString("N"),
                SnapshotFingerprint,
                ProjectedDisposition.HasValue ? "1" : "0",
                ProjectedDisposition.HasValue
                    ? ((int)ProjectedDisposition.Value).ToString(
                        CultureInfo.InvariantCulture)
                    : "0",
                OutcomeReferenceHash,
                WasProductionObserved ? "1" : "0"));
    }
}

internal sealed class CognitiveComparisonRecord
{
    internal string ComparisonId { get; }
    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal CognitiveComparisonBaselineSource BaselineSource { get; }
    internal CognitiveShadowDisposition? BaselineProjectedDisposition { get; }
    internal string BaselineReferenceHash { get; }
    internal string BaselineOutcomeReferenceHash { get; }
    internal bool BaselineWasProductionObserved { get; }
    internal CognitiveShadowOutcome Shadow { get; }
    internal CognitiveComparisonDifference Difference { get; }
    internal int ProposalCount { get; }
    internal int RelationCount { get; }
    internal int ConflictCount { get; }
    internal bool ContainsRawContent => false;
    internal int PromptMutations => 0;
    internal int ProviderCallsAdded => 0;
    internal int StateMutations => 0;
    internal int Publications => 0;
    internal int ToolInvocations => 0;
    internal int ProactiveAuthorizations => 0;

    internal CognitiveComparisonRecord(
        CognitiveConflictAnalysis analysis,
        CognitiveBaselineObservation baseline,
        CognitiveShadowOutcome shadow)
    {
        if (analysis == null)
            throw new ArgumentNullException(nameof(analysis));
        if (baseline == null)
            throw new ArgumentNullException(nameof(baseline));
        if (shadow == null)
            throw new ArgumentNullException(nameof(shadow));
        var snapshot = analysis.ProposalSet.Snapshot;
        if ((baseline.Source
                == CognitiveComparisonBaselineSource.FixedCorpusOracle)
            != (snapshot.InputSource
                == CognitiveInputSource.FixedCorpusOracle))
        {
            throw new ArgumentException(
                "Baseline provenance must match the exact snapshot input source.",
                nameof(baseline));
        }
        if (baseline.SnapshotId != snapshot.SnapshotId
            || !string.Equals(
                baseline.SnapshotFingerprint,
                snapshot.SnapshotFingerprint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Baseline must reference the exact deliberation snapshot.",
                nameof(baseline));
        }
        if (shadow.SnapshotId != snapshot.SnapshotId
            || !string.Equals(
                shadow.SnapshotFingerprint,
                snapshot.SnapshotFingerprint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Shadow outcome must reference the exact deliberation snapshot.",
                nameof(shadow));
        }
        var admittedProposalReferences = new HashSet<string>(
            analysis.ProposalSet.Proposals.Select(proposal =>
                CognitiveShadowFingerprint.Hash(proposal.ProposalId)),
            StringComparer.Ordinal);
        if (shadow.UsedProposalReferenceHashes.Any(reference =>
                !admittedProposalReferences.Contains(reference)))
        {
            throw new ArgumentException(
                "Shadow outcome references a proposal outside the exact set.",
                nameof(shadow));
        }

        SnapshotId = snapshot.SnapshotId;
        SnapshotFingerprint = snapshot.SnapshotFingerprint;
        BaselineSource = baseline.Source;
        BaselineProjectedDisposition = baseline.ProjectedDisposition;
        BaselineReferenceHash = baseline.BaselineFingerprint;
        BaselineOutcomeReferenceHash = baseline.OutcomeReferenceHash;
        BaselineWasProductionObserved = baseline.WasProductionObserved;
        Shadow = shadow;
        Difference = !baseline.ProjectedDisposition.HasValue
            ? CognitiveComparisonDifference.BaselineUnknown
            : baseline.ProjectedDisposition.Value
                == shadow.ProjectedDisposition
                ? CognitiveComparisonDifference.EquivalentDisposition
                : CognitiveComparisonDifference.DifferentDisposition;
        ProposalCount = analysis.ProposalSet.Proposals.Count;
        RelationCount = analysis.Relations.Count;
        ConflictCount = analysis.Conflicts.Count;
        var identity = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SnapshotFingerprint,
                baseline.BaselineFingerprint,
                shadow.OutcomeFingerprint));
        ComparisonId = "comparison-" + identity.Substring(0, 24);
    }
}

internal static class CognitiveShadowFingerprint
{
    internal static string Hash(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

internal static class CognitiveShadowContractGuard
{
    internal const int MaximumIdentifierLength = 128;

    internal static string Identifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumIdentifierLength
            || value.Any(character =>
                character > 127
                || !(character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '.' or '_' or ':' or '-' or '/')))
        {
            throw new ArgumentException(
                "Identifier must be a bounded ASCII token.",
                parameterName);
        }
        return value;
    }

    internal static string? OptionalIdentifier(
        string? value,
        string parameterName)
        => value == null ? null : Identifier(value, parameterName);

    internal static string Text(
        string value,
        int maximumUnicodeScalars,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.EnumerateRunes().Count() > maximumUnicodeScalars)
        {
            throw new ArgumentException(
                "Text is empty or exceeds the bounded scalar length.",
                parameterName);
        }
        return value;
    }

    internal static string Fingerprint(string value, string parameterName)
    {
        if (value == null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Fingerprint must be a 64-character hexadecimal SHA-256.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    internal static string FingerprintOrSentinel(
        string value,
        string parameterName)
        => value is "MISSING" or "UNAVAILABLE"
            ? value
            : Fingerprint(value, parameterName);

    internal static long Positive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static long NonNegative(long value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static T Enum<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!System.Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static CognitiveDomain Domain(
        CognitiveDomain value,
        string parameterName)
    {
        if (value is not CognitiveDomain.Memory
            and not CognitiveDomain.UserAffect
            and not CognitiveDomain.Boundary
            and not CognitiveDomain.OpenLoop
            and not CognitiveDomain.Proactive)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }
}
