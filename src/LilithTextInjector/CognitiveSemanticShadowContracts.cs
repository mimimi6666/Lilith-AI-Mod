using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal enum CognitiveSemanticProducerOrigin
{
    SyntheticFixture = 0,
    ObservedSameCall = 1
}

[Flags]
internal enum CognitiveSemanticComparisonDifference
{
    None = 0,
    Admission = 1 << 0,
    EpistemicState = 1 << 1,
    InteractionStance = 1 << 2,
    FollowUpMode = 1 << 3,
    MemoryPreview = 1 << 4,
    ProjectedDisposition = 1 << 5,
    RequiresConfirmation = 1 << 6,
    PolicyBlockApplied = 1 << 7
}

internal sealed class CognitiveSemanticProducerRequest
{
    internal const int SchemaVersion = 1;

    internal CognitiveInputSnapshot Snapshot { get; }
    internal CognitiveProducerKind ProducerKind { get; }
    internal string ProducerId { get; }
    internal string ProducerVersion { get; }
    internal CognitiveSemanticProducerOrigin Origin { get; }
    internal long EvaluateAtMonotonicTicks { get; }
    internal string ScenarioFingerprint { get; }
    internal string RequestFingerprint { get; }
    internal CognitiveSameCallProviderAttestation? ProviderAttestation {
        get;
    }
    internal bool WasProviderObserved => ProviderAttestation != null;
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;

    internal CognitiveSemanticProducerRequest(
        CognitiveInputSnapshot snapshot,
        CognitiveProducerKind producerKind,
        string producerId,
        string producerVersion,
        CognitiveSemanticProducerOrigin origin,
        long evaluateAtMonotonicTicks)
        : this(
            snapshot,
            producerKind,
            producerId,
            producerVersion,
            origin,
            evaluateAtMonotonicTicks,
            providerAttestation: null)
    {
    }

    private CognitiveSemanticProducerRequest(
        CognitiveInputSnapshot snapshot,
        CognitiveProducerKind producerKind,
        string producerId,
        string producerVersion,
        CognitiveSemanticProducerOrigin origin,
        long evaluateAtMonotonicTicks,
        CognitiveSameCallProviderAttestation? providerAttestation)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (Snapshot.InputSource != CognitiveInputSource.PlayerTurnOperation)
        {
            throw new ArgumentException(
                "A semantic request requires a PlayerTurnOperation-shaped snapshot.",
                nameof(snapshot));
        }
        if (producerKind is not CognitiveProducerKind.SemanticModel
            and not CognitiveProducerKind.LocalClassifier)
        {
            throw new ArgumentOutOfRangeException(nameof(producerKind));
        }
        ProducerKind = producerKind;
        ProducerId = CognitiveShadowContractGuard.Identifier(
            producerId,
            nameof(producerId));
        ProducerVersion = CognitiveShadowContractGuard.Identifier(
            producerVersion,
            nameof(producerVersion));
        Origin = CognitiveShadowContractGuard.Enum(origin, nameof(origin));
        if ((Origin == CognitiveSemanticProducerOrigin.SyntheticFixture)
                != (providerAttestation == null)
            || (Origin == CognitiveSemanticProducerOrigin.ObservedSameCall
                && (ProducerKind != CognitiveProducerKind.SemanticModel
                    || !providerAttestation!.Matches(Snapshot))))
        {
            throw new ArgumentException(
                "Semantic producer origin must preserve exact Provider attestation provenance.",
                nameof(providerAttestation));
        }
        ProviderAttestation = providerAttestation;
        EvaluateAtMonotonicTicks = CognitiveShadowContractGuard.NonNegative(
            evaluateAtMonotonicTicks,
            nameof(evaluateAtMonotonicTicks));
        if (EvaluateAtMonotonicTicks < Snapshot.CapturedAtMonotonicTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evaluateAtMonotonicTicks));
        }
        ScenarioFingerprint = CognitiveSemanticScenarioFingerprint.From(
            Snapshot);
        RequestFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                Snapshot.SnapshotId.ToString("N"),
                Snapshot.SnapshotFingerprint,
                ((int)ProducerKind).ToString(CultureInfo.InvariantCulture),
                ProducerId,
                ProducerVersion,
                ((int)Origin).ToString(CultureInfo.InvariantCulture),
                EvaluateAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture),
                ScenarioFingerprint,
                WasProviderObserved
                    ? "provider-observed:1:"
                        + ProviderAttestation!.AttestationFingerprint
                    : "provider-observed:0"));
    }

    internal static CognitiveSemanticProducerRequest
        CreateObservedSameCall(
            CognitiveInputSnapshot snapshot,
            CognitiveSameCallProviderAttestation attestation)
    {
        if (attestation == null)
            throw new ArgumentNullException(nameof(attestation));
        var version = "v2-"
            + attestation.ProtocolInstructionFingerprint.Substring(0, 12)
            + "-"
            + attestation.EffectiveModelFingerprint.Substring(0, 12);
        return new CognitiveSemanticProducerRequest(
            snapshot,
            CognitiveProducerKind.SemanticModel,
            "d041-same-call-semantic-model",
            version,
            CognitiveSemanticProducerOrigin.ObservedSameCall,
            attestation.ObservedAtMonotonicTicks,
            attestation);
    }
}

internal static class CognitiveSemanticScenarioFingerprint
{
    internal const int SchemaVersion = 1;

    internal static string From(CognitiveInputSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        return CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                snapshot.TurnHash,
                ((int)snapshot.PrivacyClass).ToString(
                    CultureInfo.InvariantCulture),
                snapshot.SituationRevision.ToString(
                    CultureInfo.InvariantCulture),
                snapshot.SituationFingerprint,
                snapshot.MemoryWorkingSetRevision.ToString(
                    CultureInfo.InvariantCulture),
                snapshot.MemoryWorkingSetFingerprint,
                snapshot.CharacterProjectionFingerprint,
                ReferenceFingerprint(snapshot.ActiveBoundaryReference),
                ReferenceFingerprint(snapshot.ActiveOpenLoopReference),
                ((int)snapshot.Interruptibility).ToString(
                    CultureInfo.InvariantCulture),
                ((int)snapshot.AllowedDomains).ToString(
                    CultureInfo.InvariantCulture)));
    }

    private static string ReferenceFingerprint(string? reference)
        => reference == null
            ? "0"
            : "1:" + CognitiveShadowFingerprint.Hash(reference);
}

internal sealed class CognitiveSemanticRelationEnvelope
{
    internal CognitiveSemanticProducerRequest Request { get; }
    internal CognitiveRelationCandidate Candidate { get; }
    internal string EnvelopeFingerprint { get; }

    internal CognitiveSemanticRelationEnvelope(
        CognitiveSemanticProducerRequest request,
        CognitiveRelationCandidate candidate)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        if (Candidate.ProducerKind != Request.ProducerKind
            || Candidate.Kind == CognitiveRelationKind.Blocks
            || Candidate.Strength
                == CognitiveRelationStrength.Deterministic)
        {
            throw new ArgumentException(
                "A semantic relation must preserve request provenance and cannot claim deterministic policy authority.",
                nameof(candidate));
        }

        EnvelopeFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                Request.RequestFingerprint,
                Request.ProducerId,
                Request.ProducerVersion,
                Candidate.RelationId,
                Candidate.FromProposalId,
                Candidate.ToProposalId,
                ((int)Candidate.Kind).ToString(
                    CultureInfo.InvariantCulture),
                ((int)Candidate.Reason).ToString(
                    CultureInfo.InvariantCulture),
                ((int)Candidate.Strength).ToString(
                    CultureInfo.InvariantCulture),
                ((int)Candidate.ProducerKind).ToString(
                    CultureInfo.InvariantCulture)));
    }
}

internal sealed class CognitiveSemanticBatch
{
    internal const int MaximumProposals = 8;
    internal const int MaximumRelations = 16;

    internal CognitiveSemanticProducerRequest Request { get; }
    internal CognitiveProposalSet ProposalSet { get; }
    internal IReadOnlyList<CognitiveSemanticRelationEnvelope>
        RelationEnvelopes { get; }
    internal IReadOnlyList<CognitiveRelationCandidate> Relations { get; }
    internal string ScenarioFingerprint => Request.ScenarioFingerprint;
    internal string BatchFingerprint { get; }
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;
    internal bool CanMutateState => false;
    internal bool CanPublish => false;
    internal bool CanInvokeTools => false;
    internal bool CanAuthorizeProactive => false;

    internal CognitiveSemanticBatch(
        CognitiveSemanticProducerRequest request,
        CognitiveProposalSet proposalSet,
        IReadOnlyList<CognitiveSemanticRelationEnvelope> relations)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        if (proposalSet == null)
            throw new ArgumentNullException(nameof(proposalSet));
        if (relations == null)
            throw new ArgumentNullException(nameof(relations));
        if (!ReferenceEquals(Request.Snapshot, proposalSet.Snapshot))
        {
            throw new ArgumentException(
                "A semantic batch must use the exact request snapshot instance.",
                nameof(proposalSet));
        }
        if (proposalSet.Proposals.Count > MaximumProposals)
            throw new ArgumentOutOfRangeException(nameof(proposalSet));
        if (relations.Count > MaximumRelations)
            throw new ArgumentOutOfRangeException(nameof(relations));
        if (relations.Any(relation => relation == null))
        {
            throw new ArgumentException(
                "A semantic batch cannot contain a null relation.",
                nameof(relations));
        }

        var orderedProposals = proposalSet.Proposals
            .OrderBy(proposal => proposal.ProposalId, StringComparer.Ordinal)
            .ToArray();
        foreach (var proposal in orderedProposals)
        {
            if (proposal.ProducerKind != Request.ProducerKind
                || !string.Equals(
                    proposal.ProducerId,
                    Request.ProducerId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    proposal.ProducerVersion,
                    Request.ProducerVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every semantic proposal must preserve exact request provenance.",
                    nameof(proposalSet));
            }
        }

        var orderedRelations = relations
            .OrderBy(
                relation => relation.Candidate.RelationId,
                StringComparer.Ordinal)
            .ToArray();
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in orderedRelations)
        {
            if (relation == null
                || !ReferenceEquals(relation.Request, Request)
                || !relationIds.Add(relation.Candidate.RelationId))
            {
                throw new ArgumentException(
                    "Semantic relations must be unique and preserve the exact request instance.",
                    nameof(relations));
            }
        }

        ProposalSet = new CognitiveProposalSet(
            Request.Snapshot,
            orderedProposals);
        RelationEnvelopes = new ReadOnlyCollection<
            CognitiveSemanticRelationEnvelope>(orderedRelations);
        Relations = new ReadOnlyCollection<CognitiveRelationCandidate>(
            orderedRelations.Select(relation => relation.Candidate).ToArray());
        _ = CognitiveConflictDetector.Detect(ProposalSet, Relations);
        BatchFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                Request.RequestFingerprint,
                string.Join(
                    ",",
                    ProposalSet.Proposals.Select(ProposalFingerprint)),
                string.Join(
                    ",",
                    RelationEnvelopes.Select(
                        relation => relation.EnvelopeFingerprint))));
    }

    private static string ProposalFingerprint(CognitiveProposal proposal)
        => CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                proposal.ProposalId,
                ((int)proposal.Domain).ToString(CultureInfo.InvariantCulture),
                ((int)proposal.ProducerKind).ToString(
                    CultureInfo.InvariantCulture),
                proposal.ProducerId,
                proposal.ProducerVersion,
                proposal.SnapshotId.ToString("N"),
                proposal.SnapshotFingerprint,
                ((int)proposal.Payload.ClaimKind).ToString(
                    CultureInfo.InvariantCulture),
                proposal.Payload.TopicReference,
                ((int)proposal.Payload.SubjectScope).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.Payload.TemporalScope).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.Payload.SuggestedDisposition).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.Payload.SuggestedStance).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.Payload.SuggestedFollowUp).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.Payload.SuggestedMemoryPreview).ToString(
                    CultureInfo.InvariantCulture),
                proposal.Payload.RequiresConfirmationForStateMutation
                    ? "1"
                    : "0",
                ((int)proposal.Confidence).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.EpistemicState).ToString(
                    CultureInfo.InvariantCulture),
                ((int)proposal.PrivacyClass).ToString(
                    CultureInfo.InvariantCulture),
                proposal.CreatedAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture),
                proposal.ExpiresAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture),
                string.Join(
                    ",",
                    proposal.Evidence
                        .OrderBy(
                            evidence => evidence.EvidenceId,
                            StringComparer.Ordinal)
                        .Select(EvidenceFingerprint))));

    private static string EvidenceFingerprint(CognitiveEvidenceReference evidence)
        => CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                evidence.EvidenceId,
                ((int)evidence.Kind).ToString(CultureInfo.InvariantCulture),
                evidence.SourceReference,
                evidence.StartOffset.ToString(CultureInfo.InvariantCulture),
                evidence.SpanLength.ToString(CultureInfo.InvariantCulture),
                evidence.ContentHash));

}

internal sealed class CognitiveDesignOracleProjection
{
    internal string ScenarioFingerprint { get; }
    internal string SnapshotIdentityHash { get; }
    internal string SnapshotReferenceHash { get; }
    internal string OutcomeReferenceHash { get; }
    internal CognitiveShadowAdmission Admission { get; }
    internal CognitiveEpistemicState EpistemicState { get; }
    internal CognitiveInteractionStance InteractionStance { get; }
    internal CognitiveFollowUpMode FollowUpMode { get; }
    internal CognitiveMemoryPreviewKind MemoryPreview { get; }
    internal CognitiveShadowDisposition ProjectedDisposition { get; }
    internal bool RequiresConfirmation { get; }
    internal bool PolicyBlockApplied { get; }
    internal int ProposalCount { get; }
    internal int RelationCount { get; }
    internal int ConflictCount { get; }
    internal string ProjectionFingerprint { get; }
    internal bool WasProductionObserved => false;
    internal bool ContainsRawContent => false;
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;

    internal CognitiveDesignOracleProjection(
        CognitiveDeliberationShadowReplayResult oracle,
        long evaluateAtMonotonicTicks)
    {
        if (oracle == null)
            throw new ArgumentNullException(nameof(oracle));
        var snapshot = oracle.Analysis.ProposalSet.Snapshot;
        CognitiveShadowContractGuard.NonNegative(
            evaluateAtMonotonicTicks,
            nameof(evaluateAtMonotonicTicks));
        var canonicalOutcome = CognitiveConflictShadowArbiter.Evaluate(
            oracle.Analysis,
            evaluateAtMonotonicTicks);
        if (snapshot.InputSource != CognitiveInputSource.FixedCorpusOracle
            || evaluateAtMonotonicTicks
                < snapshot.CapturedAtMonotonicTicks
            || evaluateAtMonotonicTicks
                >= snapshot.ExpiresAtMonotonicTicks
            || oracle.Analysis.ProposalSet.Proposals.Any(proposal =>
                proposal.ProducerKind
                    != CognitiveProducerKind.FixedCorpusOracle
                || proposal.Evidence.Any(evidence =>
                    evidence.Kind != CognitiveEvidenceKind.FixedCorpusOracle))
            || oracle.Analysis.Relations.Any(relation =>
                relation.ProducerKind
                    != CognitiveProducerKind.FixedCorpusOracle)
            || oracle.Outcome.SnapshotId != snapshot.SnapshotId
            || !string.Equals(
                oracle.Outcome.SnapshotFingerprint,
                snapshot.SnapshotFingerprint,
                StringComparison.Ordinal)
            || oracle.Comparison.SnapshotId != snapshot.SnapshotId
            || !string.Equals(
                oracle.Comparison.SnapshotFingerprint,
                snapshot.SnapshotFingerprint,
                StringComparison.Ordinal)
            || oracle.Comparison.BaselineSource
                != CognitiveComparisonBaselineSource.FixedCorpusOracle
            || oracle.Comparison.BaselineWasProductionObserved
            || !string.Equals(
                oracle.Comparison.Shadow.OutcomeFingerprint,
                oracle.Outcome.OutcomeFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                canonicalOutcome.OutcomeFingerprint,
                oracle.Outcome.OutcomeFingerprint,
                StringComparison.Ordinal)
            || oracle.Comparison.ProposalCount
                != oracle.Analysis.ProposalSet.Proposals.Count
            || oracle.Comparison.RelationCount
                != oracle.Analysis.Relations.Count
            || oracle.Comparison.ConflictCount
                != oracle.Analysis.Conflicts.Count)
        {
            throw new ArgumentException(
                "A design-oracle projection requires an exact non-production fixed-corpus replay.",
                nameof(oracle));
        }

        ScenarioFingerprint = CognitiveSemanticScenarioFingerprint.From(
            snapshot);
        SnapshotIdentityHash = CognitiveShadowFingerprint.Hash(
            snapshot.SnapshotId.ToString("N"));
        SnapshotReferenceHash = CognitiveShadowFingerprint.Hash(
            snapshot.SnapshotId.ToString("N")
                + "|"
                + snapshot.SnapshotFingerprint);
        OutcomeReferenceHash = oracle.Outcome.OutcomeFingerprint;
        Admission = oracle.Outcome.Admission;
        EpistemicState = oracle.Outcome.EpistemicState;
        InteractionStance = oracle.Outcome.InteractionStance;
        FollowUpMode = oracle.Outcome.FollowUpMode;
        MemoryPreview = oracle.Outcome.MemoryPreview;
        ProjectedDisposition = oracle.Outcome.ProjectedDisposition;
        RequiresConfirmation = oracle.Outcome.RequiresConfirmation;
        PolicyBlockApplied = oracle.Outcome.PolicyBlockApplied;
        ProposalCount = oracle.Analysis.ProposalSet.Proposals.Count;
        RelationCount = oracle.Analysis.Relations.Count;
        ConflictCount = oracle.Analysis.Conflicts.Count;
        ProjectionFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                ScenarioFingerprint,
                SnapshotIdentityHash,
                SnapshotReferenceHash,
                OutcomeReferenceHash,
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
                ProposalCount.ToString(CultureInfo.InvariantCulture),
                RelationCount.ToString(CultureInfo.InvariantCulture),
                ConflictCount.ToString(CultureInfo.InvariantCulture)));
    }
}

internal sealed class CognitiveSemanticShadowReplayResult
{
    internal CognitiveSemanticBatch Batch { get; }
    internal CognitiveConflictAnalysis Analysis { get; }
    internal CognitiveShadowOutcome Outcome { get; }
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;
    internal bool NoStateMutation => true;
    internal bool NoPublication => true;
    internal bool NoAction => true;
    internal bool NoProviderCall => true;

    private CognitiveSemanticShadowReplayResult(
        CognitiveSemanticBatch batch,
        CognitiveConflictAnalysis analysis,
        CognitiveShadowOutcome outcome)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
    }

    internal static CognitiveSemanticShadowReplayResult EvaluateExact(
        CognitiveSemanticBatch batch)
    {
        if (batch == null)
            throw new ArgumentNullException(nameof(batch));
        var analysis = CognitiveConflictDetector.Detect(
            batch.ProposalSet,
            batch.Relations);
        var outcome = CognitiveConflictShadowArbiter.Evaluate(
            analysis,
            batch.Request.EvaluateAtMonotonicTicks);
        return new CognitiveSemanticShadowReplayResult(
            batch,
            analysis,
            outcome);
    }
}

internal sealed class CognitiveSemanticPairedComparisonRecord
{
    internal string ComparisonId { get; }
    internal string ScenarioFingerprint { get; }
    internal string OracleProjectionReferenceHash { get; }
    internal string SemanticBatchReferenceHash { get; }
    internal string SemanticOutcomeReferenceHash { get; }
    internal CognitiveSemanticComparisonDifference Difference { get; }
    internal bool AllClosedDimensionsMatch =>
        Difference == CognitiveSemanticComparisonDifference.None;
    internal int OracleProposalCount { get; }
    internal int OracleRelationCount { get; }
    internal int OracleConflictCount { get; }
    internal int SemanticProposalCount { get; }
    internal int SemanticRelationCount { get; }
    internal int SemanticConflictCount { get; }
    internal bool WasProviderObserved => false;
    internal bool ContainsRawContent => false;
    internal int PromptMutations => 0;
    internal int ProviderCallsAdded => 0;
    internal int StateMutations => 0;
    internal int Publications => 0;
    internal int ToolInvocations => 0;
    internal int ProactiveAuthorizations => 0;
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;

    internal CognitiveSemanticPairedComparisonRecord(
        CognitiveDesignOracleProjection oracle,
        CognitiveSemanticShadowReplayResult semantic)
    {
        if (oracle == null)
            throw new ArgumentNullException(nameof(oracle));
        if (semantic == null)
            throw new ArgumentNullException(nameof(semantic));
        var semanticSnapshot = semantic.Analysis.ProposalSet.Snapshot;
        if (semanticSnapshot.InputSource
                != CognitiveInputSource.PlayerTurnOperation
            || semantic.Batch.Request.WasProviderObserved
            || semantic.Batch.Request.Origin
                != CognitiveSemanticProducerOrigin.SyntheticFixture
            || !ReferenceEquals(
                semantic.Batch.ProposalSet,
                semantic.Analysis.ProposalSet)
            || semantic.Outcome.SnapshotId != semanticSnapshot.SnapshotId
            || !string.Equals(
                semantic.Outcome.SnapshotFingerprint,
                semanticSnapshot.SnapshotFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                oracle.ScenarioFingerprint,
                semantic.Batch.ScenarioFingerprint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Paired comparison requires one exact synthetic semantic replay of the same scenario.",
                nameof(semantic));
        }

        var semanticSnapshotReference = CognitiveShadowFingerprint.Hash(
            semanticSnapshot.SnapshotId.ToString("N")
                + "|"
                + semanticSnapshot.SnapshotFingerprint);
        var semanticSnapshotIdentity = CognitiveShadowFingerprint.Hash(
            semanticSnapshot.SnapshotId.ToString("N"));
        if (string.Equals(
                oracle.SnapshotReferenceHash,
                semanticSnapshotReference,
                StringComparison.Ordinal)
            || string.Equals(
                oracle.SnapshotIdentityHash,
                semanticSnapshotIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Oracle and semantic replay must use distinct source snapshots.",
                nameof(semantic));
        }

        ScenarioFingerprint = oracle.ScenarioFingerprint;
        OracleProjectionReferenceHash = oracle.ProjectionFingerprint;
        SemanticBatchReferenceHash = semantic.Batch.BatchFingerprint;
        SemanticOutcomeReferenceHash = semantic.Outcome.OutcomeFingerprint;
        Difference = DeriveDifference(oracle, semantic.Outcome);
        OracleProposalCount = oracle.ProposalCount;
        OracleRelationCount = oracle.RelationCount;
        OracleConflictCount = oracle.ConflictCount;
        SemanticProposalCount = semantic.Analysis.ProposalSet.Proposals.Count;
        SemanticRelationCount = semantic.Analysis.Relations.Count;
        SemanticConflictCount = semantic.Analysis.Conflicts.Count;
        var identity = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                ScenarioFingerprint,
                OracleProjectionReferenceHash,
                SemanticBatchReferenceHash,
                SemanticOutcomeReferenceHash,
                ((int)Difference).ToString(CultureInfo.InvariantCulture)));
        ComparisonId = "semantic-comparison-" + identity.Substring(0, 24);
    }

    private static CognitiveSemanticComparisonDifference DeriveDifference(
        CognitiveDesignOracleProjection oracle,
        CognitiveShadowOutcome outcome)
    {
        var difference = CognitiveSemanticComparisonDifference.None;
        if (oracle.Admission != outcome.Admission)
            difference |= CognitiveSemanticComparisonDifference.Admission;
        if (oracle.EpistemicState != outcome.EpistemicState)
            difference |= CognitiveSemanticComparisonDifference.EpistemicState;
        if (oracle.InteractionStance != outcome.InteractionStance)
        {
            difference |= CognitiveSemanticComparisonDifference
                .InteractionStance;
        }
        if (oracle.FollowUpMode != outcome.FollowUpMode)
            difference |= CognitiveSemanticComparisonDifference.FollowUpMode;
        if (oracle.MemoryPreview != outcome.MemoryPreview)
            difference |= CognitiveSemanticComparisonDifference.MemoryPreview;
        if (oracle.ProjectedDisposition != outcome.ProjectedDisposition)
        {
            difference |= CognitiveSemanticComparisonDifference
                .ProjectedDisposition;
        }
        if (oracle.RequiresConfirmation != outcome.RequiresConfirmation)
        {
            difference |= CognitiveSemanticComparisonDifference
                .RequiresConfirmation;
        }
        if (oracle.PolicyBlockApplied != outcome.PolicyBlockApplied)
        {
            difference |= CognitiveSemanticComparisonDifference
                .PolicyBlockApplied;
        }
        return difference;
    }
}
