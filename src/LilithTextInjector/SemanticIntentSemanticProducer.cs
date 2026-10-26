using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum SemanticIntentSemanticProducerAdmission
{
    Produced = 1,
    Abstained = 2,
    InputExpired = 3,
    InferenceUnavailable = 4,
    InferenceCanceled = 5,
    InferenceFaulted = 6,
    ResponseBindingRejected = 7,
    ResponseExpired = 8,
    ResponseRejected = 9,
    RequestReplayRejected = 10
}

internal sealed class SemanticIntentSemanticProducerResult
{
    internal SemanticIntentSemanticProducerAdmission Admission { get; }
    internal string RequestFingerprint { get; }
    internal string InputFingerprint { get; }
    internal string RegistryFingerprint { get; }
    internal string ProfileFingerprint { get; }
    internal string? ResponseFingerprint { get; }
    internal SemanticIntentProposalBatch? Batch { get; }
    internal long InferencePortInvocations { get; }
    internal string Fingerprint { get; }
    internal SemanticIntentAuthority Authority =>
        SemanticIntentAuthority.ShadowOnly;
    internal bool ContainsRawContent => false;
    internal bool CanRoute => false;
    internal bool CanAdmit => false;
    internal bool CanAffectDecision => false;
    internal bool CanPublish => false;
    internal bool CanInvokeTool => false;
    internal bool CanMutateState => false;
    internal bool InferencePortEffectsObserved => false;
    internal long ProducerDirectProviderCalls => 0;
    internal long ProducerDirectToolCalls => 0;
    internal long ProducerDirectStateWrites => 0;
    internal long ProducerDirectPolicyAdmissions => 0;
    internal long ProducerDirectCapabilityTickets => 0;
    internal long ProducerDirectPublications => 0;

    private SemanticIntentSemanticProducerResult(
        SemanticIntentSemanticProducerAdmission admission,
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse? response,
        SemanticIntentProposalBatch? batch,
        long inferencePortInvocations)
    {
        Admission = SemanticIntentContractGuard.Enum(
            admission,
            nameof(admission));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        RequestFingerprint = request.Fingerprint;
        InputFingerprint = request.Input.Fingerprint;
        RegistryFingerprint = request.Registry.Fingerprint;
        ProfileFingerprint = request.Profile.Fingerprint;
        ResponseFingerprint = response?.Fingerprint;
        if (inferencePortInvocations is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inferencePortInvocations));
        }
        InferencePortInvocations = inferencePortInvocations;

        var exactResponse = response != null
            && ReferenceEquals(
                response.RequestIdentityWitness,
                request.IdentityWitness)
            && response.RequestFingerprint == request.Fingerprint
            && response.InputFingerprint == request.Input.Fingerprint
            && response.RegistryGeneration == request.Registry.Generation
            && response.RegistryFingerprint == request.Registry.Fingerprint
            && response.ProducerId == request.Profile.ProducerId
            && response.ProducerVersion == request.Profile.ProducerVersion
            && response.ProfileFingerprint == request.Profile.Fingerprint;
        var exactBatch = batch != null
            && batch.ProducerOrigin ==
                SemanticIntentProducerOrigin.SemanticInference
            && batch.ProducerId == request.Profile.ProducerId
            && batch.ProducerVersion == request.Profile.ProducerVersion
            && batch.ProducerProfileFingerprint == request.Profile.Fingerprint
            && batch.InputFingerprint == request.Input.Fingerprint
            && batch.RegistryGeneration == request.Registry.Generation
            && batch.RegistryFingerprint == request.Registry.Fingerprint;
        var validShape = Admission switch
        {
            SemanticIntentSemanticProducerAdmission.Produced =>
                inferencePortInvocations == 1
                && exactResponse
                && response!.Disposition ==
                    SemanticIntentInferenceDisposition.Proposed
                && response.Candidates.Count > 0
                && exactBatch
                && batch!.Proposals.Count > 0,
            SemanticIntentSemanticProducerAdmission.Abstained =>
                inferencePortInvocations == 1
                && exactResponse
                && response!.Disposition ==
                    SemanticIntentInferenceDisposition.Abstained
                && response.Candidates.Count == 0
                && exactBatch
                && batch!.Proposals.Count == 0,
            SemanticIntentSemanticProducerAdmission.InputExpired
                or SemanticIntentSemanticProducerAdmission
                    .RequestReplayRejected =>
                inferencePortInvocations == 0
                && response == null
                && batch == null,
            SemanticIntentSemanticProducerAdmission.InferenceUnavailable =>
                inferencePortInvocations == 1
                && exactResponse
                && response!.Disposition ==
                    SemanticIntentInferenceDisposition.Unavailable
                && response.Candidates.Count == 0
                && batch == null,
            SemanticIntentSemanticProducerAdmission.InferenceCanceled
                or SemanticIntentSemanticProducerAdmission.InferenceFaulted =>
                inferencePortInvocations is 0 or 1
                && response == null
                && batch == null,
            SemanticIntentSemanticProducerAdmission.ResponseBindingRejected =>
                inferencePortInvocations == 1
                && response != null
                && batch == null,
            SemanticIntentSemanticProducerAdmission.ResponseExpired =>
                inferencePortInvocations == 1
                && response != null
                && batch == null,
            SemanticIntentSemanticProducerAdmission.ResponseRejected =>
                inferencePortInvocations == 1
                && batch == null,
            _ => false
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "Producer receipt shape did not match its terminal admission.",
                nameof(batch));
        }
        Batch = batch;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Admission,
            RequestFingerprint,
            InputFingerprint,
            RegistryFingerprint,
            ProfileFingerprint,
            ResponseFingerprint,
            Batch?.Fingerprint,
            InferencePortInvocations,
            Authority);
    }

    internal static SemanticIntentSemanticProducerResult Create(
        SemanticIntentSemanticProducer issuer,
        object issuerCapability,
        SemanticIntentSemanticProducerAdmission admission,
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse? response,
        SemanticIntentProposalBatch? batch,
        long inferencePortInvocations)
    {
        if (issuer == null || !issuer.IsResultIssuer(issuerCapability))
            throw new InvalidOperationException(
                "Only the exact Semantic Producer instance can issue its receipt.");
        return new SemanticIntentSemanticProducerResult(
            admission,
            request,
            response,
            batch,
            inferencePortInvocations);
    }
}

internal sealed class SemanticIntentSemanticProducer
{
    internal const int MaximumInputLifetimeSeconds = 60;

    private readonly ISemanticIntentInferencePort _inferencePort;
    private readonly ISemanticIntentInferenceMonotonicClock _monotonicClock;
    private readonly long _maximumInputLifetimeTicks;
    private readonly object _resultIssuerCapability = new();

    internal SemanticIntentSemanticProducer(
        ISemanticIntentInferencePort inferencePort,
        ISemanticIntentInferenceMonotonicClock monotonicClock)
    {
        _inferencePort = inferencePort
            ?? throw new ArgumentNullException(nameof(inferencePort));
        _monotonicClock = monotonicClock
            ?? throw new ArgumentNullException(nameof(monotonicClock));
        var frequency = _monotonicClock.Frequency;
        if (frequency <= 0
            || frequency > long.MaxValue / MaximumInputLifetimeSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicClock));
        }
        _maximumInputLifetimeTicks =
            frequency * MaximumInputLifetimeSeconds;
    }

    internal bool IsResultIssuer(object issuerCapability)
        => ReferenceEquals(_resultIssuerCapability, issuerCapability);

    internal async ValueTask<SemanticIntentSemanticProducerResult> ProduceAsync(
        SemanticIntentInferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (!request.TryClaimInferenceInvocation())
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.RequestReplayRejected,
                request,
                null,
                null,
                0);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceCanceled,
                request,
                null,
                null,
                0);
        }

        long invocationObservedAt;
        try
        {
            invocationObservedAt = _monotonicClock.ObserveTicks();
        }
        catch (Exception)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceFaulted,
                request,
                null,
                null,
                0);
        }
        if (request.RequestedAtMonotonicTicks
                < request.Input.CapturedAtMonotonicTicks
            || request.RequestedAtMonotonicTicks
                >= request.Input.ExpiresAtMonotonicTicks
            || request.Input.ExpiresAtMonotonicTicks
                    - request.Input.CapturedAtMonotonicTicks
                > _maximumInputLifetimeTicks
            || invocationObservedAt < request.RequestedAtMonotonicTicks
            || invocationObservedAt >= request.Input.ExpiresAtMonotonicTicks)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InputExpired,
                request,
                null,
                null,
                0);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceCanceled,
                request,
                null,
                null,
                0);
        }

        SemanticIntentInferenceResponse? response;
        try
        {
            response = await _inferencePort.InferAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceCanceled,
                request,
                null,
                null,
                1);
        }
        catch (Exception)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceFaulted,
                request,
                null,
                null,
                1);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceCanceled,
                request,
                null,
                null,
                1);
        }
        if (response == null)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.ResponseRejected,
                request,
                null,
                null,
                1);
        }
        long completionObservedAt;
        try
        {
            completionObservedAt = _monotonicClock.ObserveTicks();
        }
        catch (Exception)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.InferenceFaulted,
                request,
                null,
                null,
                1);
        }
        if (completionObservedAt < invocationObservedAt
            || completionObservedAt >= request.Input.ExpiresAtMonotonicTicks)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.ResponseExpired,
                request,
                response,
                null,
                1);
        }
        if (response.CompletedAtMonotonicTicks
                < invocationObservedAt
            || response.CompletedAtMonotonicTicks > completionObservedAt)
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.ResponseBindingRejected,
                request,
                response,
                null,
                1);
        }
        if (!HasExactBindings(request, response))
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.ResponseBindingRejected,
                request,
                response,
                null,
                1);
        }
        if (response.Disposition ==
            SemanticIntentInferenceDisposition.Unavailable)
        {
            return response.Candidates.Count == 0
                ? Result(
                    SemanticIntentSemanticProducerAdmission.InferenceUnavailable,
                    request,
                    response,
                    null,
                    1)
                : Result(
                    SemanticIntentSemanticProducerAdmission.ResponseRejected,
                    request,
                    response,
                    null,
                    1);
        }

        if (response.Disposition == SemanticIntentInferenceDisposition.Abstained)
        {
            if (response.Candidates.Count != 0)
            {
                return Result(
                    SemanticIntentSemanticProducerAdmission.ResponseRejected,
                    request,
                    response,
                    null,
                    1);
            }
            var emptyBatch = CreateBatch(
                request,
                Array.Empty<SemanticIntentProposal>());
            return Result(
                SemanticIntentSemanticProducerAdmission.Abstained,
                request,
                response,
                emptyBatch,
                1);
        }

        if (response.Disposition != SemanticIntentInferenceDisposition.Proposed
            || response.Candidates.Count == 0
            || !TryMapProposals(request, response, out var proposals))
        {
            return Result(
                SemanticIntentSemanticProducerAdmission.ResponseRejected,
                request,
                response,
                null,
                1);
        }

        var batch = CreateBatch(request, proposals);
        return Result(
            SemanticIntentSemanticProducerAdmission.Produced,
            request,
            response,
            batch,
            1);
    }

    private static bool HasExactBindings(
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse response)
        => ReferenceEquals(
                response.RequestIdentityWitness,
                request.IdentityWitness)
            && response.RequestFingerprint == request.Fingerprint
            && response.InputFingerprint == request.Input.Fingerprint
            && response.RegistryGeneration == request.Registry.Generation
            && response.RegistryFingerprint == request.Registry.Fingerprint
            && response.ProducerId == request.Profile.ProducerId
            && response.ProducerVersion == request.Profile.ProducerVersion
            && response.ProfileFingerprint == request.Profile.Fingerprint;

    private static bool TryMapProposals(
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse response,
        out IReadOnlyList<SemanticIntentProposal> proposals)
    {
        proposals = Array.Empty<SemanticIntentProposal>();
        var candidates = response.Candidates.ToArray();
        if (!HasContiguousRanks(candidates.Select(item => item.Rank))
            || candidates.Select(item => item.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != candidates.Length)
        {
            return false;
        }

        var mapped = new List<SemanticIntentProposal>(candidates.Length);
        foreach (var candidate in candidates.OrderBy(item => item.Rank))
        {
            if (!TryMapProposal(request, response, candidate, out var proposal))
                return false;
            mapped.Add(proposal);
        }
        proposals = new ReadOnlyCollection<SemanticIntentProposal>(mapped);
        return true;
    }

    private static bool TryMapProposal(
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse response,
        SemanticIntentInferenceCandidate candidate,
        out SemanticIntentProposal proposal)
    {
        proposal = null!;
        var descriptorCandidates = candidate.Descriptors.ToArray();
        if (!HasContiguousRanks(descriptorCandidates.Select(item => item.Rank))
            || descriptorCandidates.Select(item => item.DescriptorId)
                .Distinct(StringComparer.Ordinal)
                .Count() != descriptorCandidates.Length)
        {
            return false;
        }

        var descriptors = new List<SemanticDescriptorReference>(
            descriptorCandidates.Length);
        var descriptorDefinitions = new List<SemanticRegistryDescriptor>(
            descriptorCandidates.Length);
        foreach (var candidateDescriptor in descriptorCandidates
            .OrderBy(item => item.Rank))
        {
            var descriptor = request.Registry.Find(
                candidateDescriptor.DescriptorId,
                candidateDescriptor.DescriptorVersion);
            var definition = request.Profile.FindDefinition(
                candidateDescriptor.DescriptorId,
                candidateDescriptor.DescriptorVersion);
            if (descriptor == null
                || definition == null
                || definition.DescriptorFingerprint != descriptor.Fingerprint)
            {
                return false;
            }
            descriptorDefinitions.Add(descriptor);
            descriptors.Add(new SemanticDescriptorReference(
                candidateDescriptor.DescriptorId,
                candidateDescriptor.DescriptorVersion,
                candidateDescriptor.Rank));
        }
        var primary = descriptorDefinitions[0];
        if (descriptorDefinitions.Any(descriptor =>
                descriptor.Purpose != primary.Purpose
                || descriptor.Freshness != primary.Freshness
                || descriptor.Effect != primary.Effect
                || descriptor.Target != primary.Target))
        {
            return false;
        }

        var contextIds = candidate.ContextReferenceIds.ToArray();
        if (contextIds.Distinct(StringComparer.Ordinal).Count()
                != contextIds.Length)
        {
            return false;
        }
        var contexts = new List<SemanticContextReference>(contextIds.Length);
        foreach (var contextId in contextIds)
        {
            var context = request.Input.FindContext(contextId);
            var material = request.FindContextMaterial(contextId);
            if (context == null
                || material == null
                || !ReferenceEquals(material.Reference, context))
            {
                return false;
            }
            contexts.Add(context);
        }
        if (descriptorDefinitions
            .SelectMany(descriptor => descriptor.RequiredContexts)
            .Distinct()
            .Any(required =>
                contexts.All(context => context.Kind != required)))
        {
            return false;
        }

        var evidenceCandidates = candidate.Evidence.ToArray();
        if (evidenceCandidates.Select(item => item.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != evidenceCandidates.Length)
        {
            return false;
        }
        var evidence = new List<SemanticEvidenceSpan>(
            evidenceCandidates.Length);
        foreach (var item in evidenceCandidates)
        {
            var turn = request.Input.FindTurn(item.TurnId);
            if (turn == null
                || item.ScalarLength > turn.UnicodeScalarCount
                || item.ScalarStart
                    > turn.UnicodeScalarCount - item.ScalarLength)
            {
                return false;
            }
            evidence.Add(new SemanticEvidenceSpan(
                item.TurnId,
                item.ScalarStart,
                item.ScalarLength,
                item.Role));
        }
        if (!evidence.Any(item =>
            item.Role == SemanticEvidenceRole.IntentCue
            && item.TurnId == request.Input.CurrentTurnId))
            return false;

        SemanticTargetReference targetReference;
        var requestsEffect = primary.Effect
            is SemanticEffectAxis.ActionRequested
            or SemanticEffectAxis.ChangeRequested;
        if (requestsEffect)
        {
            if (candidate.Ambiguity
                    != SemanticAmbiguityKind.UnresolvedDescriptor
                || primary.Purpose is not SemanticPurposeAxis.Act
                    and not SemanticPurposeAxis.Manage
                || primary.Target is SemanticTargetAxis.None
                    or SemanticTargetAxis.PublicInformation
                    or SemanticTargetAxis.Unknown
                || candidate.TargetCueEvidenceFingerprint == null)
            {
                return false;
            }
            var targetEvidence = evidence.SingleOrDefault(item =>
                item.Fingerprint == candidate.TargetCueEvidenceFingerprint
                && item.Role == SemanticEvidenceRole.TargetCue);
            if (targetEvidence == null)
                return false;
            targetReference = SemanticTargetReference.OpaqueUnresolved(
                targetEvidence);
        }
        else
        {
            if (candidate.TargetCueEvidenceFingerprint != null
                || candidate.Ambiguity
                    == SemanticAmbiguityKind.UnresolvedDescriptor)
            {
                return false;
            }
            targetReference = SemanticTargetReference.None();
        }

        if (primary.Purpose == SemanticPurposeAxis.Ambiguous
            && candidate.Ambiguity == SemanticAmbiguityKind.None)
        {
            return false;
        }

        var proposalId = "semantic-proposal:"
            + SemanticIntentFingerprint.HashCanonical(
                request.Fingerprint,
                response.Fingerprint,
                candidate.Fingerprint,
                candidate.Rank);
        proposal = new SemanticIntentProposal(
            proposalId,
            candidate.Rank,
            request.Input.Fingerprint,
            request.Registry.Generation,
            request.Registry.Fingerprint,
            primary.Purpose,
            primary.Freshness,
            primary.Effect,
            primary.Target,
            candidate.Ambiguity,
            candidate.Confidence,
            targetReference,
            descriptors,
            contextIds,
            evidence);
        return true;
    }

    private static bool HasContiguousRanks(IEnumerable<int> ranks)
    {
        var items = ranks.OrderBy(value => value).ToArray();
        return items.Length > 0
            && items.Select((value, index) => value == index + 1)
                .All(matches => matches);
    }

    private static SemanticIntentProposalBatch CreateBatch(
        SemanticIntentInferenceRequest request,
        IEnumerable<SemanticIntentProposal> proposals)
        => new(
            request.Profile.ProducerId,
            request.Profile.ProducerVersion,
            SemanticIntentProducerOrigin.SemanticInference,
            request.Profile.Fingerprint,
            request.Input.Fingerprint,
            request.Registry.Generation,
            request.Registry.Fingerprint,
            proposals);

    private SemanticIntentSemanticProducerResult Result(
        SemanticIntentSemanticProducerAdmission admission,
        SemanticIntentInferenceRequest request,
        SemanticIntentInferenceResponse? response,
        SemanticIntentProposalBatch? batch,
        long inferencePortInvocations)
        => SemanticIntentSemanticProducerResult.Create(
            this,
            _resultIssuerCapability,
            admission,
            request,
            response,
            batch,
            inferencePortInvocations);
}
