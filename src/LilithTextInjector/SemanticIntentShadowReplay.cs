using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal static class SemanticIntentShadowReplay
{
    internal static SemanticIntentShadowOutcome Evaluate(
        SemanticIntentInputSnapshot input,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentProposalBatch batch,
        long nowMonotonicTicks)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (batch == null)
            throw new ArgumentNullException(nameof(batch));
        RequireExactBindings(input, registry, batch);

        if (nowMonotonicTicks < input.CapturedAtMonotonicTicks
            || nowMonotonicTicks >= input.ExpiresAtMonotonicTicks)
        {
            return SemanticIntentShadowOutcome.CreateValidated(
                input.Fingerprint,
                input.ScenarioFingerprint,
                registry.Fingerprint,
                batch.Fingerprint,
                batch.ProducerOrigin,
                batch.ProducerProfileFingerprint,
                SemanticIntentShadowAdmission.Expired,
                Array.Empty<SemanticIntentShadowProjection>());
        }

        var projections = batch.Proposals
            .Select(proposal => ValidateAndProject(
                input,
                registry,
                proposal))
            .ToArray();
        if (projections.Length == 0)
        {
            return SemanticIntentShadowOutcome.CreateValidated(
                input.Fingerprint,
                input.ScenarioFingerprint,
                registry.Fingerprint,
                batch.Fingerprint,
                batch.ProducerOrigin,
                batch.ProducerProfileFingerprint,
                SemanticIntentShadowAdmission.Abstained,
                projections);
        }

        var ambiguous = projections.Length != 1
            || projections.Any(projection =>
                projection.Ambiguity != SemanticAmbiguityKind.None
                || projection.Purpose == SemanticPurposeAxis.Ambiguous
                || projection.Freshness == SemanticFreshnessAxis.Unknown
                || projection.Effect == SemanticEffectAxis.Unknown
                || projection.Target == SemanticTargetAxis.Unknown);
        return SemanticIntentShadowOutcome.CreateValidated(
            input.Fingerprint,
            input.ScenarioFingerprint,
            registry.Fingerprint,
            batch.Fingerprint,
            batch.ProducerOrigin,
            batch.ProducerProfileFingerprint,
            ambiguous
                ? SemanticIntentShadowAdmission.Ambiguous
                : SemanticIntentShadowAdmission.Projected,
            projections);
    }

    private static void RequireExactBindings(
        SemanticIntentInputSnapshot input,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentProposalBatch batch)
    {
        var requiredProducerOrigin = input.Origin switch
        {
            SemanticIntentInputOrigin.FixedCorpusReplay =>
                SemanticIntentProducerOrigin.FixedCorpusReplay,
            SemanticIntentInputOrigin.SyntheticReplay =>
                SemanticIntentProducerOrigin.SyntheticReplay,
            SemanticIntentInputOrigin.SemanticInference =>
                SemanticIntentProducerOrigin.SemanticInference,
            _ => throw new InvalidOperationException(
                "Semantic input origin has no exact producer pairing.")
        };
        if (input.RegistryGeneration != registry.Generation
            || input.RegistryFingerprint != registry.Fingerprint
            || batch.InputFingerprint != input.Fingerprint
            || batch.RegistryGeneration != registry.Generation
            || batch.RegistryFingerprint != registry.Fingerprint
            || batch.ProducerOrigin != requiredProducerOrigin)
        {
            throw new InvalidOperationException(
                "Semantic input, registry and proposal provenance do not match.");
        }
    }

    private static SemanticIntentShadowProjection ValidateAndProject(
        SemanticIntentInputSnapshot input,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentProposal proposal)
    {
        if (proposal.InputFingerprint != input.Fingerprint
            || proposal.RegistryGeneration != registry.Generation
            || proposal.RegistryFingerprint != registry.Fingerprint)
        {
            throw new InvalidOperationException(
                "A semantic proposal crossed its input or registry boundary.");
        }

        var descriptors = proposal.Descriptors
            .Select(reference => registry.Find(
                reference.DescriptorId,
                reference.DescriptorVersion)
                ?? throw new InvalidOperationException(
                    "A proposal referenced a descriptor outside its registry."))
            .ToArray();
        var primary = descriptors[0];
        if (primary.Purpose != proposal.Purpose
            || primary.Freshness != proposal.Freshness
            || primary.Effect != proposal.Effect
            || primary.Target != proposal.Target)
        {
            throw new InvalidOperationException(
                "Proposal axes must be owned by its primary intent descriptor.");
        }

        var contexts = proposal.ContextReferenceIds
            .Select(contextId => input.FindContext(contextId)
                ?? throw new InvalidOperationException(
                    "A proposal referenced context outside its input snapshot."))
            .ToArray();
        if (primary.RequiredContexts.Any(required =>
                contexts.All(context => context.Kind != required)))
        {
            throw new InvalidOperationException(
                "A proposal omitted a required typed context reference.");
        }

        foreach (var context in contexts)
        {
            if (context.ObservedAtMonotonicTicks
                    > input.CapturedAtMonotonicTicks
                || context.ExpiresAtMonotonicTicks
                    <= input.CapturedAtMonotonicTicks)
            {
                throw new InvalidOperationException(
                    "A proposal used context outside the frozen input instant.");
            }
        }

        foreach (var evidence in proposal.EvidenceSpans)
        {
            var turn = input.FindTurn(evidence.TurnId)
                ?? throw new InvalidOperationException(
                    "Evidence referenced a turn outside the input snapshot.");
            if (evidence.ScalarLength > turn.UnicodeScalarCount
                || evidence.ScalarStart
                    > turn.UnicodeScalarCount - evidence.ScalarLength)
            {
                throw new InvalidOperationException(
                    "Evidence exceeded its exact frozen turn.");
            }
        }

        if (proposal.TargetReference.Kind
                == SemanticTargetReferenceKind.OpaqueUnresolved
            && proposal.EvidenceSpans.All(evidence =>
                evidence.Fingerprint
                    != proposal.TargetReference.EvidenceFingerprint))
        {
            throw new InvalidOperationException(
                "An opaque target must bind evidence from the exact proposal.");
        }

        var requestsEffect = proposal.Effect
            is SemanticEffectAxis.ActionRequested
            or SemanticEffectAxis.ChangeRequested;
        var hasOpaqueTarget = proposal.TargetReference.Kind
            == SemanticTargetReferenceKind.OpaqueUnresolved;
        var hasUnresolvedDescriptor = proposal.Ambiguity
            == SemanticAmbiguityKind.UnresolvedDescriptor;
        if (requestsEffect != hasOpaqueTarget
            || hasOpaqueTarget != hasUnresolvedDescriptor
            || (requestsEffect
                && proposal.Purpose is not SemanticPurposeAxis.Act
                    and not SemanticPurposeAxis.Manage)
            || (requestsEffect
                && proposal.Target is SemanticTargetAxis.None
                    or SemanticTargetAxis.PublicInformation
                    or SemanticTargetAxis.Unknown))
        {
            throw new InvalidOperationException(
                "A requested effect must remain coherently opaque and unresolved.");
        }
        if (proposal.Purpose == SemanticPurposeAxis.Ambiguous
            && proposal.Ambiguity == SemanticAmbiguityKind.None)
        {
            throw new InvalidOperationException(
                "An ambiguous purpose must preserve an explicit ambiguity reason.");
        }
        return SemanticIntentShadowProjection.CreateValidated(proposal);
    }
}

internal static class SemanticAdaptationShadowReplay
{
    internal static SemanticAdaptationShadowOutcome Evaluate(
        SemanticIntentRegistrySnapshot registry,
        SemanticAdaptationCandidate candidate,
        IEnumerable<SemanticIntentExperienceEvidence> experiences,
        SemanticAdaptationReplayEvidence replayEvidence)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));
        if (experiences == null)
            throw new ArgumentNullException(nameof(experiences));
        if (replayEvidence == null)
            throw new ArgumentNullException(nameof(replayEvidence));
        if (candidate.RegistryGeneration != registry.Generation
            || candidate.RegistryFingerprint != registry.Fingerprint
            || replayEvidence.CandidateFingerprint != candidate.Fingerprint
            || replayEvidence.CorpusFingerprint != candidate.CorpusFingerprint)
        {
            throw new InvalidOperationException(
                "Adaptation input identities do not match.");
        }

        var experienceItems = experiences
            .Take(SemanticAdaptationCandidate.MaximumExperienceReferences + 1)
            .ToArray();
        if (experienceItems.Length
                > SemanticAdaptationCandidate.MaximumExperienceReferences
            || experienceItems.Any(item => item == null)
            || experienceItems.Select(item => item.ExperienceId)
                .Distinct(StringComparer.Ordinal)
                .Count() != experienceItems.Length
            || experienceItems.Length != candidate.ExperienceIds.Count)
        {
            return Outcome(
                SemanticAdaptationShadowAdmission.EvidenceRejected,
                candidate,
                replayEvidence);
        }
        var evidenceById = experienceItems.ToDictionary(
                item => item.ExperienceId,
                item => item,
                StringComparer.Ordinal);
        var selectedEvidence = new List<SemanticIntentExperienceEvidence>();
        foreach (var experienceId in candidate.ExperienceIds)
        {
            if (!evidenceById.TryGetValue(experienceId, out var evidence)
                || evidence.RegistryGeneration != registry.Generation
                || evidence.RegistryFingerprint != registry.Fingerprint
                || evidence.CorpusFingerprint != candidate.CorpusFingerprint
                || evidence.ProducerProfileFingerprint
                    != candidate.BaseProfileFingerprint
                || !replayEvidence.BaselineComparisonFingerprints.Contains(
                    evidence.SourceProofFingerprint,
                    StringComparer.Ordinal)
                || !replayEvidence.ScenarioFingerprints.Contains(
                    evidence.ScenarioFingerprint,
                    StringComparer.Ordinal))
            {
                return Outcome(
                    SemanticAdaptationShadowAdmission.EvidenceRejected,
                    candidate,
                    replayEvidence);
            }
            selectedEvidence.Add(evidence);
        }

        if (replayEvidence.RegressionCount != 0
            || replayEvidence.SafetyRegressionCount != 0)
        {
            return Outcome(
                SemanticAdaptationShadowAdmission.RegressionRejected,
                candidate,
                replayEvidence);
        }

        foreach (var delta in candidate.Deltas)
        {
            var descriptor = registry.Find(
                delta.DescriptorId,
                delta.DescriptorVersion);
            if (descriptor == null || !descriptor.AdaptiveRankingAllowed)
            {
                return Outcome(
                    SemanticAdaptationShadowAdmission.EvidenceRejected,
                    candidate,
                    replayEvidence);
            }
            if (!selectedEvidence.Any(evidence =>
                    evidence.Assessment
                        == SemanticExperienceAssessment.Contradicts
                    && replayEvidence.SupportsImprovement(
                        evidence.ScenarioFingerprint,
                        delta.DescriptorId,
                        delta.DescriptorVersion)))
            {
                return Outcome(
                    SemanticAdaptationShadowAdmission.EvidenceRejected,
                    candidate,
                    replayEvidence);
            }
        }

        if (replayEvidence.ImprovedScenarioCount == 0
            || selectedEvidence.All(item =>
                item.Assessment != SemanticExperienceAssessment.Contradicts))
        {
            return Outcome(
                SemanticAdaptationShadowAdmission.EvidenceRejected,
                candidate,
                replayEvidence);
        }
        return Outcome(
            SemanticAdaptationShadowAdmission.EligibleForShadowReview,
            candidate,
            replayEvidence);
    }

    private static SemanticAdaptationShadowOutcome Outcome(
        SemanticAdaptationShadowAdmission admission,
        SemanticAdaptationCandidate candidate,
        SemanticAdaptationReplayEvidence replayEvidence)
        => new(
            admission,
            candidate.Fingerprint,
            candidate.RollbackIdentity,
            candidate.BaseProfileFingerprint,
            replayEvidence.Fingerprint);
}
