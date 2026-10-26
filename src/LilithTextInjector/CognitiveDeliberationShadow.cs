using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class CognitiveDeliberationShadowReplayResult
{
    internal CognitiveConflictAnalysis Analysis { get; }
    internal CognitiveShadowOutcome Outcome { get; }
    internal CognitiveComparisonRecord Comparison { get; }

    internal CognitiveDeliberationShadowReplayResult(
        CognitiveConflictAnalysis analysis,
        CognitiveShadowOutcome outcome,
        CognitiveComparisonRecord comparison)
    {
        Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        Comparison = comparison
            ?? throw new ArgumentNullException(nameof(comparison));
    }
}

internal static class CognitiveDeliberationShadowReplay
{
    internal static CognitiveDeliberationShadowReplayResult Evaluate(
        CognitiveProposalSet proposalSet,
        IReadOnlyList<CognitiveRelationCandidate> relationCandidates,
        CognitiveBaselineObservation baseline,
        long nowMonotonicTicks)
    {
        var analysis = CognitiveConflictDetector.Detect(
            proposalSet,
            relationCandidates);
        var outcome = CognitiveConflictShadowArbiter.Evaluate(
            analysis,
            nowMonotonicTicks);
        var comparison = CognitiveComparisonRecorder.Compare(
            analysis,
            baseline,
            outcome);
        return new CognitiveDeliberationShadowReplayResult(
            analysis,
            outcome,
            comparison);
    }
}

internal static class CognitiveConflictDetector
{
    internal static CognitiveConflictAnalysis Detect(
        CognitiveProposalSet proposalSet,
        IReadOnlyList<CognitiveRelationCandidate> relationCandidates)
    {
        if (proposalSet == null)
            throw new ArgumentNullException(nameof(proposalSet));
        if (relationCandidates == null)
            throw new ArgumentNullException(nameof(relationCandidates));
        if (relationCandidates.Count
            > CognitiveConflictAnalysis.MaximumRelations)
        {
            throw new ArgumentOutOfRangeException(nameof(relationCandidates));
        }

        var proposalIds = new HashSet<string>(
            proposalSet.Proposals.Select(item => item.ProposalId),
            StringComparer.Ordinal);
        var proposalsById = proposalSet.Proposals.ToDictionary(
            item => item.ProposalId,
            StringComparer.Ordinal);
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relationCandidates)
        {
            if (relation == null)
            {
                throw new ArgumentException(
                    "Relation set cannot contain null.",
                    nameof(relationCandidates));
            }
            if (!relationIds.Add(relation.RelationId))
            {
                throw new ArgumentException(
                    "Relation identities must be unique.",
                    nameof(relationCandidates));
            }
            if (!proposalIds.Contains(relation.FromProposalId)
                || !proposalIds.Contains(relation.ToProposalId))
            {
                throw new ArgumentException(
                    "Relations must reference proposals from the exact set.",
                    nameof(relationCandidates));
            }
            if (relation.Kind == CognitiveRelationKind.Blocks
                && (proposalsById[relation.FromProposalId].Domain
                        != CognitiveDomain.Boundary
                    || proposalsById[relation.FromProposalId]
                        .Payload
                        .ClaimKind != CognitiveClaimKind.Boundary
                    || proposalsById[relation.FromProposalId]
                        .ProducerKind is not
                            CognitiveProducerKind.DeterministicPolicy
                        and not CognitiveProducerKind.FixedCorpusOracle))
            {
                throw new ArgumentException(
                    "A hard block must originate from an admitted Boundary proposal.",
                    nameof(relationCandidates));
            }
        }

        var relations = relationCandidates
            .OrderBy(item => item.RelationId, StringComparer.Ordinal)
            .ToArray();
        var conflicts = relations
            .Where(CognitiveConflict.IsConflictRelation)
            .Select(relation => new CognitiveConflict(relation))
            .ToArray();
        return new CognitiveConflictAnalysis(
            proposalSet,
            relations,
            conflicts);
    }

}

internal static class CognitiveConflictShadowArbiter
{
    internal static CognitiveShadowOutcome Evaluate(
        CognitiveConflictAnalysis analysis,
        long nowMonotonicTicks)
    {
        if (analysis == null)
            throw new ArgumentNullException(nameof(analysis));
        CognitiveShadowContractGuard.NonNegative(
            nowMonotonicTicks,
            nameof(nowMonotonicTicks));
        var snapshot = analysis.ProposalSet.Snapshot;
        if (nowMonotonicTicks >= snapshot.ExpiresAtMonotonicTicks)
        {
            return EmptyOutcome(
                analysis,
                CognitiveShadowAdmission.SnapshotExpired,
                CognitiveEpistemicState.Abstained,
                CognitiveShadowDisposition.Defer);
        }

        var usable = analysis.ProposalSet.Proposals
            .Where(proposal =>
                proposal.CreatedAtMonotonicTicks <= nowMonotonicTicks
                && nowMonotonicTicks < proposal.ExpiresAtMonotonicTicks
                && proposal.EpistemicState
                    != CognitiveEpistemicState.Abstained)
            .ToDictionary(
                proposal => proposal.ProposalId,
                StringComparer.Ordinal);
        if (usable.Count == 0)
        {
            return EmptyOutcome(
                analysis,
                CognitiveShadowAdmission.NoUsableProposals,
                CognitiveEpistemicState.Abstained,
                CognitiveShadowDisposition.Observe);
        }

        var superseded = Targets(
            analysis.Relations,
            CognitiveRelationKind.Supersedes,
            usable,
            excludedSources: null);
        var temporallyLimited = Targets(
            analysis.Relations,
            CognitiveRelationKind.TemporallyLimits,
            usable,
            superseded);
        var replacedForThisSnapshot = new HashSet<string>(
            superseded,
            StringComparer.Ordinal);
        replacedForThisSnapshot.UnionWith(temporallyLimited);
        var effectiveRelations = analysis.Relations
            .Where(relation =>
                usable.ContainsKey(relation.FromProposalId)
                && usable.ContainsKey(relation.ToProposalId)
                && !replacedForThisSnapshot.Contains(
                    relation.FromProposalId))
            .ToArray();
        var blocked = Targets(
            effectiveRelations,
            CognitiveRelationKind.Blocks,
            usable,
            replacedForThisSnapshot);
        var deferred = Targets(
            effectiveRelations,
            CognitiveRelationKind.Defers,
            usable,
            replacedForThisSnapshot);
        var insufficient = Targets(
            effectiveRelations,
            CognitiveRelationKind.InsufficientEvidenceFor,
            usable,
            replacedForThisSnapshot);
        var excluded = new HashSet<string>(
            replacedForThisSnapshot,
            StringComparer.Ordinal);
        excluded.UnionWith(blocked);
        excluded.UnionWith(deferred);
        excluded.UnionWith(insufficient);
        var active = usable.Values
            .Where(proposal => !excluded.Contains(proposal.ProposalId))
            .OrderBy(proposal => proposal.ProposalId, StringComparer.Ordinal)
            .ToArray();
        if (active.Length == 0)
        {
            return EmptyOutcome(
                analysis,
                CognitiveShadowAdmission.NoUsableProposals,
                CognitiveEpistemicState.NeedMoreEvidence,
                CognitiveShadowDisposition.Defer,
                policyBlockApplied: blocked.Count > 0);
        }

        var requiresConfirmation = effectiveRelations.Any(relation =>
            relation.Kind == CognitiveRelationKind.RequiresConfirmation)
            || active.Any(proposal =>
                proposal.Payload.RequiresConfirmationForStateMutation);
        var epistemic = ResolveEpistemicState(
            active,
            effectiveRelations,
            requiresConfirmation);
        var stance = Highest(
            active.Select(proposal => proposal.Payload.SuggestedStance),
            CognitiveInteractionStance.Neutral,
            StanceRank);
        var followUp = Highest(
            active.Select(proposal => proposal.Payload.SuggestedFollowUp),
            CognitiveFollowUpMode.None,
            FollowUpRank);
        var memoryPreview = Highest(
            active
                .Where(proposal =>
                    !requiresConfirmation
                    || !proposal.Payload
                        .RequiresConfirmationForStateMutation)
                .Select(proposal =>
                    proposal.Payload.SuggestedMemoryPreview),
            CognitiveMemoryPreviewKind.None,
            MemoryPreviewRank);
        var disposition = Highest(
            active.Select(proposal =>
                proposal.Payload.SuggestedDisposition),
            CognitiveShadowDisposition.Observe,
            DispositionRank);
        if (followUp == CognitiveFollowUpMode.AskClarification)
            disposition = CognitiveShadowDisposition.Respond;

        var usedProposalIds = active
            .Select(proposal => proposal.ProposalId)
            .ToArray();
        var policyBlockApplied = blocked.Count > 0;
        return new CognitiveShadowOutcome(
            snapshot.SnapshotId,
            snapshot.SnapshotFingerprint,
            CognitiveShadowAdmission.Evaluated,
            epistemic,
            stance,
            followUp,
            memoryPreview,
            disposition,
            requiresConfirmation,
            policyBlockApplied,
            usedProposalIds);
    }

    private static HashSet<string> Targets(
        IEnumerable<CognitiveRelationCandidate> relations,
        CognitiveRelationKind kind,
        IReadOnlyDictionary<string, CognitiveProposal> usable,
        IReadOnlySet<string>? excludedSources)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relations)
        {
            if (relation.Kind != kind
                || !usable.ContainsKey(relation.FromProposalId)
                || !usable.ContainsKey(relation.ToProposalId)
                || excludedSources?.Contains(relation.FromProposalId) == true)
            {
                continue;
            }
            targets.Add(relation.ToProposalId);
        }
        return targets;
    }

    private static CognitiveEpistemicState ResolveEpistemicState(
        IReadOnlyList<CognitiveProposal> active,
        IReadOnlyList<CognitiveRelationCandidate> relations,
        bool requiresConfirmation)
    {
        if (requiresConfirmation
            || relations.Any(relation =>
                relation.Kind
                    == CognitiveRelationKind.InsufficientEvidenceFor))
        {
            return CognitiveEpistemicState.NeedMoreEvidence;
        }
        if (active.Any(proposal =>
                proposal.EpistemicState
                    == CognitiveEpistemicState.Contradicted))
        {
            return CognitiveEpistemicState.Contradicted;
        }
        if (relations.Any(relation =>
                relation.Kind == CognitiveRelationKind.Contradicts)
            || active.Any(proposal =>
                proposal.EpistemicState
                    == CognitiveEpistemicState.Ambiguous))
        {
            return CognitiveEpistemicState.Ambiguous;
        }
        if (active.Any(proposal =>
                proposal.EpistemicState
                    == CognitiveEpistemicState.NeedMoreEvidence))
        {
            return CognitiveEpistemicState.NeedMoreEvidence;
        }
        if (active.Any(proposal =>
                proposal.EpistemicState
                    == CognitiveEpistemicState.Plausible)
            || relations.Any(relation =>
                relation.Kind == CognitiveRelationKind.Cautions))
        {
            return CognitiveEpistemicState.Plausible;
        }
        return CognitiveEpistemicState.Supported;
    }

    private static T Highest<T>(
        IEnumerable<T> values,
        T fallback,
        Func<T, int> rank)
    {
        var selected = fallback;
        var selectedValue = rank(selected);
        foreach (var value in values)
        {
            var numeric = rank(value);
            if (numeric <= selectedValue)
                continue;
            selected = value;
            selectedValue = numeric;
        }
        return selected;
    }

    private static int StanceRank(CognitiveInteractionStance stance)
        => stance switch
        {
            CognitiveInteractionStance.NoOpinion => 0,
            CognitiveInteractionStance.Neutral => 1,
            CognitiveInteractionStance.NonCommittal => 2,
            CognitiveInteractionStance.Acknowledge => 3,
            CognitiveInteractionStance.Care => 4,
            CognitiveInteractionStance.Clarify => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(stance))
        };

    private static int FollowUpRank(CognitiveFollowUpMode followUp)
        => followUp switch
        {
            CognitiveFollowUpMode.NoOpinion => 0,
            CognitiveFollowUpMode.None => 1,
            CognitiveFollowUpMode.Optional => 2,
            CognitiveFollowUpMode.AskClarification => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(followUp))
        };

    private static int MemoryPreviewRank(
        CognitiveMemoryPreviewKind memoryPreview)
        => memoryPreview switch
        {
            CognitiveMemoryPreviewKind.None => 0,
            CognitiveMemoryPreviewKind.Episode => 1,
            CognitiveMemoryPreviewKind.Fact => 2,
            CognitiveMemoryPreviewKind.Correction => 3,
            CognitiveMemoryPreviewKind.ExplicitRemember => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryPreview))
        };

    private static int DispositionRank(
        CognitiveShadowDisposition disposition)
        => disposition switch
        {
            CognitiveShadowDisposition.NoOpinion => 0,
            CognitiveShadowDisposition.Silence => 1,
            CognitiveShadowDisposition.Observe => 2,
            CognitiveShadowDisposition.Defer => 3,
            CognitiveShadowDisposition.Respond => 4,
            CognitiveShadowDisposition.AskConsent => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };

    private static CognitiveShadowOutcome EmptyOutcome(
        CognitiveConflictAnalysis analysis,
        CognitiveShadowAdmission admission,
        CognitiveEpistemicState epistemicState,
        CognitiveShadowDisposition disposition,
        bool policyBlockApplied = false)
    {
        var ids = Array.Empty<string>();
        return new CognitiveShadowOutcome(
            analysis.ProposalSet.Snapshot.SnapshotId,
            analysis.ProposalSet.Snapshot.SnapshotFingerprint,
            admission,
            epistemicState,
            CognitiveInteractionStance.Neutral,
            CognitiveFollowUpMode.None,
            CognitiveMemoryPreviewKind.None,
            disposition,
            requiresConfirmation: false,
            policyBlockApplied,
            ids);
    }
}

internal static class CognitiveComparisonRecorder
{
    internal static CognitiveComparisonRecord Compare(
        CognitiveConflictAnalysis analysis,
        CognitiveBaselineObservation baseline,
        CognitiveShadowOutcome shadow)
    {
        return new CognitiveComparisonRecord(
            analysis,
            baseline,
            shadow);
    }
}
