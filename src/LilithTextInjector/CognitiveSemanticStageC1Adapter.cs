using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal enum CognitiveSemanticStageC1Admission
{
    Mapped = 0,
    MappedEmpty = 1,
    AttestationMismatch = 2,
    EvidenceRejected = 3,
    MetadataUnavailable = 4,
    DuplicateProposal = 5,
    SemanticRejected = 6,
    DuplicateReplay = 7,
    CapacityExceeded = 8,
    ResponseMismatch = 9,
    EnvelopeRejected = 10,
    ClockRejected = 11
}

internal sealed class CognitiveSemanticStageC1Record
{
    internal string ObservationId { get; }
    internal string AttestationReferenceHash { get; }
    internal string SemanticRequestReferenceHash { get; }
    internal string SemanticBatchReferenceHash { get; }
    internal string ShadowOutcomeReferenceHash { get; }
    internal CognitiveShadowAdmission ShadowAdmission { get; }
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
    internal bool WasProviderObserved => true;
    internal bool ContainsRawContent => false;
    internal int PromptMutations => 0;
    internal int ProviderCallsAdded => 0;
    internal int StateMutations => 0;
    internal int Publications => 0;
    internal int ToolInvocations => 0;
    internal int ProactiveAuthorizations => 0;
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;

    internal CognitiveSemanticStageC1Record(
        CognitiveSemanticShadowReplayResult replay)
    {
        if (replay == null)
            throw new ArgumentNullException(nameof(replay));
        var request = replay.Batch.Request;
        if (!request.WasProviderObserved
            || request.Origin
                != CognitiveSemanticProducerOrigin.ObservedSameCall
            || request.ProviderAttestation == null)
        {
            throw new ArgumentException(
                "Stage C1 record requires exact observed same-call provenance.",
                nameof(replay));
        }
        AttestationReferenceHash = CognitiveShadowFingerprint.Hash(
            request.ProviderAttestation.AttestationFingerprint);
        SemanticRequestReferenceHash = CognitiveShadowFingerprint.Hash(
            request.RequestFingerprint);
        SemanticBatchReferenceHash = CognitiveShadowFingerprint.Hash(
            replay.Batch.BatchFingerprint);
        ShadowOutcomeReferenceHash = CognitiveShadowFingerprint.Hash(
            replay.Outcome.OutcomeFingerprint);
        ShadowAdmission = replay.Outcome.Admission;
        EpistemicState = replay.Outcome.EpistemicState;
        InteractionStance = replay.Outcome.InteractionStance;
        FollowUpMode = replay.Outcome.FollowUpMode;
        MemoryPreview = replay.Outcome.MemoryPreview;
        ProjectedDisposition = replay.Outcome.ProjectedDisposition;
        RequiresConfirmation = replay.Outcome.RequiresConfirmation;
        PolicyBlockApplied = replay.Outcome.PolicyBlockApplied;
        ProposalCount = replay.Analysis.ProposalSet.Proposals.Count;
        RelationCount = replay.Analysis.Relations.Count;
        ConflictCount = replay.Analysis.Conflicts.Count;
        var fingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                AttestationReferenceHash,
                SemanticRequestReferenceHash,
                SemanticBatchReferenceHash,
                ShadowOutcomeReferenceHash,
                ((int)ShadowAdmission).ToString(
                    CultureInfo.InvariantCulture),
                ProposalCount.ToString(CultureInfo.InvariantCulture),
                RelationCount.ToString(CultureInfo.InvariantCulture),
                ConflictCount.ToString(CultureInfo.InvariantCulture)));
        ObservationId = "d041-c1-observation-" + fingerprint.Substring(0, 24);
    }
}

internal sealed class CognitiveSemanticStageC1MappingResult
{
    internal CognitiveSemanticStageC1Admission Admission { get; }
    internal CognitiveSemanticShadowReplayResult? Replay { get; }
    internal CognitiveSemanticStageC1Record? Record { get; }
    internal bool Mapped => Admission is CognitiveSemanticStageC1Admission.Mapped
        or CognitiveSemanticStageC1Admission.MappedEmpty;
    internal bool ContainsEphemeralSnapshot => Replay != null;

    internal CognitiveSemanticStageC1MappingResult(
        CognitiveSemanticStageC1Admission admission,
        CognitiveSemanticShadowReplayResult? replay,
        CognitiveSemanticStageC1Record? record)
    {
        Admission = CognitiveShadowContractGuard.Enum(
            admission,
            nameof(admission));
        if (Mapped != (replay != null && record != null))
        {
            throw new ArgumentException(
                "Only a mapped result may carry replay and record output.");
        }
        Replay = replay;
        Record = record;
    }
}

internal sealed class CognitiveSemanticStageC1AdmissionSnapshot
{
    internal int ConsumedAttestations { get; }
    internal long Attempts { get; }
    internal long Mapped { get; }
    internal long Rejected { get; }
    internal long DuplicateReplays { get; }
    internal long CapacityRejections { get; }
    internal bool ContainsRawContent => false;
    internal int StateMutations => 0;
    internal int Publications => 0;
    internal int ToolInvocations => 0;
    internal CognitiveAuthority Authority => CognitiveAuthority.ShadowOnly;

    internal CognitiveSemanticStageC1AdmissionSnapshot(
        int consumedAttestations,
        long attempts,
        long mapped,
        long rejected,
        long duplicateReplays,
        long capacityRejections)
    {
        ConsumedAttestations = consumedAttestations;
        Attempts = attempts;
        Mapped = mapped;
        Rejected = rejected;
        DuplicateReplays = duplicateReplays;
        CapacityRejections = capacityRejections;
    }
}

internal sealed class CognitiveSemanticStageC1AdmissionHost
{
    internal const int MaximumConsumedAttestations = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _consumedAttestations = new(
        StringComparer.Ordinal);
    private readonly Func<long> _timestampProvider;
    private long? _lastObservedTimestamp;
    private long _attempts;
    private long _mapped;
    private long _rejected;
    private long _duplicateReplays;
    private long _capacityRejections;

    internal CognitiveSemanticStageC1AdmissionHost(
        Func<long> timestampProvider)
    {
        _timestampProvider = timestampProvider
            ?? throw new ArgumentNullException(nameof(timestampProvider));
    }

    internal CognitiveSemanticStageC1MappingResult TryAdmit(
        CognitiveInputSnapshot snapshot,
        CognitiveSameCallProviderAttestation attestation,
        string providerResponse)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (attestation == null)
            throw new ArgumentNullException(nameof(attestation));
        if (providerResponse == null)
            throw new ArgumentNullException(nameof(providerResponse));

        var responseFingerprint = CognitiveShadowFingerprint.Hash(
            providerResponse);
        lock (_gate)
        {
            _attempts = Increment(_attempts);
            if (!string.Equals(
                    responseFingerprint,
                    attestation.ResponseFingerprint,
                    StringComparison.Ordinal))
            {
                _rejected = Increment(_rejected);
                return Reject(
                    CognitiveSemanticStageC1Admission.ResponseMismatch);
            }
            if (!TryNow(out var now))
            {
                _rejected = Increment(_rejected);
                return Reject(CognitiveSemanticStageC1Admission.ClockRejected);
            }
            RemoveExpired(now);
            if (!attestation.Matches(snapshot)
                || attestation.ObservedAtMonotonicTicks
                    < snapshot.CapturedAtMonotonicTicks
                || now < attestation.ObservedAtMonotonicTicks
                || now < snapshot.CapturedAtMonotonicTicks
                || now >= snapshot.ExpiresAtMonotonicTicks)
            {
                _rejected = Increment(_rejected);
                return Reject(
                    CognitiveSemanticStageC1Admission.AttestationMismatch);
            }
            if (_consumedAttestations.ContainsKey(
                    attestation.ReplayIdentityFingerprint))
            {
                _duplicateReplays = Increment(_duplicateReplays);
                _rejected = Increment(_rejected);
                return Reject(
                    CognitiveSemanticStageC1Admission.DuplicateReplay);
            }
            if (_consumedAttestations.Count
                >= MaximumConsumedAttestations)
            {
                _capacityRejections = Increment(_capacityRejections);
                _rejected = Increment(_rejected);
                return Reject(
                    CognitiveSemanticStageC1Admission.CapacityExceeded);
            }
            _consumedAttestations.Add(
                attestation.ReplayIdentityFingerprint,
                snapshot.ExpiresAtMonotonicTicks);
        }

        var observation = CognitiveSemanticEnvelopeV2Parser.Parse(
            providerResponse);
        var result = observation.Admission
                    == CognitiveSemanticEnvelopeV2Admission.EnvelopeObserved
                && observation.Envelope != null
            ? MapOnce(snapshot, attestation, observation.Envelope)
            : Reject(CognitiveSemanticStageC1Admission.EnvelopeRejected);
        lock (_gate)
        {
            if (result.Mapped)
                _mapped = Increment(_mapped);
            else
                _rejected = Increment(_rejected);
        }
        return result;
    }

    internal CognitiveSemanticStageC1AdmissionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new CognitiveSemanticStageC1AdmissionSnapshot(
                _consumedAttestations.Count,
                _attempts,
                _mapped,
                _rejected,
                _duplicateReplays,
                _capacityRejections);
        }
    }

    private static CognitiveSemanticStageC1MappingResult MapOnce(
        CognitiveInputSnapshot snapshot,
        CognitiveSameCallProviderAttestation attestation,
        CognitiveSemanticEnvelopeV2 envelope)
    {
        try
        {
            var request = CognitiveSemanticProducerRequest
                .CreateObservedSameCall(snapshot, attestation);
            var proposals = new List<CognitiveProposal>(
                envelope.Proposals.Count);
            var proposalIds = new List<string>(envelope.Proposals.Count);
            var proposalKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < envelope.Proposals.Count; index++)
            {
                var source = envelope.Proposals[index];
                if (!TryResolveTurnEvidence(
                        snapshot,
                        source.EvidenceQuote,
                        out var startOffset,
                        out var spanLength))
                {
                    return Reject(
                        CognitiveSemanticStageC1Admission.EvidenceRejected);
                }
                var proposalKey = ProposalKey(
                    source,
                    startOffset,
                    spanLength);
                if (!proposalKeys.Add(proposalKey))
                {
                    return Reject(
                        CognitiveSemanticStageC1Admission.DuplicateProposal);
                }
                var proposalId = "d041-v2-p-"
                    + attestation.AttestationFingerprint.Substring(0, 16)
                    + "-"
                    + index.ToString(CultureInfo.InvariantCulture);
                proposalIds.Add(proposalId);
                var evidence = new List<CognitiveEvidenceReference>(
                    1 + source.MetadataEvidence.Count)
                {
                    new(
                        proposalId + "-e-turn",
                        CognitiveEvidenceKind.CurrentTurn,
                        "turn:" + snapshot.OperationId,
                        startOffset,
                        spanLength,
                        snapshot.TurnHash)
                };
                foreach (var evidenceKind in source.MetadataEvidence)
                {
                    if (!TryCreateMetadataEvidence(
                            snapshot,
                            proposalId,
                            evidenceKind,
                            out var metadataEvidence)
                        || metadataEvidence == null)
                    {
                        return Reject(
                            CognitiveSemanticStageC1Admission
                                .MetadataUnavailable);
                    }
                    evidence.Add(metadataEvidence);
                }
                var topicFingerprint = CognitiveShadowFingerprint.Hash(
                    proposalKey
                    + "|"
                    + snapshot.TurnHash
                    + "|"
                    + attestation.AttestationFingerprint);
                var payload = new CognitiveProposalPayload(
                    source.Domain,
                    source.ClaimKind,
                    "topic-" + topicFingerprint.Substring(0, 32),
                    source.SubjectScope,
                    source.TemporalScope,
                    source.SuggestedDisposition,
                    source.SuggestedStance,
                    source.SuggestedFollowUp,
                    source.SuggestedMemoryPreview,
                    source.RequiresConfirmation);
                proposals.Add(
                    new CognitiveProposal(
                        proposalId,
                        source.Domain,
                        request.ProducerKind,
                        request.ProducerId,
                        request.ProducerVersion,
                        snapshot.SnapshotId,
                        snapshot.SnapshotFingerprint,
                        payload,
                        source.Confidence,
                        source.EpistemicState,
                        snapshot.PrivacyClass,
                        new ReadOnlyCollection<CognitiveEvidenceReference>(
                            evidence),
                        snapshot.CapturedAtMonotonicTicks,
                        snapshot.ExpiresAtMonotonicTicks));
            }

            var proposalSet = new CognitiveProposalSet(
                snapshot,
                new ReadOnlyCollection<CognitiveProposal>(proposals));
            var relations = new List<CognitiveSemanticRelationEnvelope>(
                envelope.Relations.Count);
            for (var index = 0; index < envelope.Relations.Count; index++)
            {
                var source = envelope.Relations[index];
                var relationId = "d041-v2-r-"
                    + attestation.AttestationFingerprint.Substring(0, 16)
                    + "-"
                    + index.ToString(CultureInfo.InvariantCulture);
                var candidate = new CognitiveRelationCandidate(
                    relationId,
                    proposalIds[source.FromProposalIndex],
                    proposalIds[source.ToProposalIndex],
                    source.Kind,
                    source.Reason,
                    source.Strength,
                    request.ProducerKind);
                relations.Add(
                    new CognitiveSemanticRelationEnvelope(
                        request,
                        candidate));
            }
            var batch = new CognitiveSemanticBatch(
                request,
                proposalSet,
                new ReadOnlyCollection<
                    CognitiveSemanticRelationEnvelope>(relations));
            var replay = CognitiveSemanticShadowReplay.Evaluate(batch);
            return new CognitiveSemanticStageC1MappingResult(
                proposals.Count == 0
                    ? CognitiveSemanticStageC1Admission.MappedEmpty
                    : CognitiveSemanticStageC1Admission.Mapped,
                replay,
                new CognitiveSemanticStageC1Record(replay));
        }
        catch (ArgumentException)
        {
            return Reject(CognitiveSemanticStageC1Admission.SemanticRejected);
        }
        catch (InvalidOperationException)
        {
            return Reject(CognitiveSemanticStageC1Admission.SemanticRejected);
        }
    }

    private static long Increment(long value)
        => value == long.MaxValue ? value : value + 1;

    private bool TryNow(out long now)
    {
        now = 0;
        try
        {
            now = _timestampProvider();
        }
        catch
        {
            return false;
        }
        if (now < 0
            || (_lastObservedTimestamp.HasValue
                && now < _lastObservedTimestamp.Value))
        {
            return false;
        }
        _lastObservedTimestamp = now;
        return true;
    }

    private void RemoveExpired(long now)
    {
        if (_consumedAttestations.Count == 0)
            return;
        var expired = _consumedAttestations
            .Where(item => item.Value <= now)
            .Select(item => item.Key)
            .ToArray();
        foreach (var identity in expired)
            _consumedAttestations.Remove(identity);
    }

    private static CognitiveSemanticStageC1MappingResult Reject(
        CognitiveSemanticStageC1Admission admission)
        => new(admission, null, null);

    private static bool TryResolveTurnEvidence(
        CognitiveInputSnapshot snapshot,
        string quote,
        out int startOffset,
        out int spanLength)
    {
        startOffset = 0;
        spanLength = 0;
        var turn = snapshot.EphemeralTurnText;
        if (!CognitiveSemanticEnvelopeV2Parser.TryCountUnicodeScalars(
                turn,
                out var turnLength))
        {
            return false;
        }
        if (quote.Length == 0)
        {
            spanLength = turnLength;
            return true;
        }
        var first = turn.IndexOf(quote, StringComparison.Ordinal);
        if (first < 0
            || turn.IndexOf(
                quote,
                first + 1,
                StringComparison.Ordinal) >= 0
            || !CognitiveSemanticEnvelopeV2Parser.TryCountUnicodeScalars(
                turn.Substring(0, first),
                out startOffset)
            || !CognitiveSemanticEnvelopeV2Parser.TryCountUnicodeScalars(
                quote,
                out spanLength)
            || spanLength == 0)
        {
            startOffset = 0;
            spanLength = 0;
            return false;
        }
        return startOffset <= turnLength - spanLength;
    }

    private static bool TryCreateMetadataEvidence(
        CognitiveInputSnapshot snapshot,
        string proposalId,
        CognitiveEvidenceKind kind,
        out CognitiveEvidenceReference? evidence)
    {
        evidence = null;
        string sourceReference;
        string contentHash;
        switch (kind)
        {
            case CognitiveEvidenceKind.MemoryWorkingSet:
                if (snapshot.MemoryWorkingSetRevision <= 0
                    || snapshot.MemoryWorkingSetFingerprint
                        is "MISSING" or "UNAVAILABLE")
                {
                    return false;
                }
                sourceReference = "memory-working-set";
                contentHash = snapshot.MemoryWorkingSetFingerprint;
                break;
            case CognitiveEvidenceKind.ActiveBoundary:
                if (snapshot.ActiveBoundaryReference == null)
                    return false;
                sourceReference = snapshot.ActiveBoundaryReference;
                contentHash = CognitiveShadowFingerprint.Hash(sourceReference);
                break;
            case CognitiveEvidenceKind.ActiveOpenLoop:
                if (snapshot.ActiveOpenLoopReference == null)
                    return false;
                sourceReference = snapshot.ActiveOpenLoopReference;
                contentHash = CognitiveShadowFingerprint.Hash(sourceReference);
                break;
            case CognitiveEvidenceKind.CharacterProjection:
                if (snapshot.CharacterProjectionFingerprint
                    is "MISSING" or "UNAVAILABLE")
                {
                    return false;
                }
                sourceReference = "character-projection";
                contentHash = snapshot.CharacterProjectionFingerprint;
                break;
            case CognitiveEvidenceKind.ProactiveSituation:
                sourceReference = "proactive-situation";
                contentHash = snapshot.SituationFingerprint;
                break;
            default:
                return false;
        }
        evidence = new CognitiveEvidenceReference(
            proposalId
                + "-e-"
                + ((int)kind).ToString(CultureInfo.InvariantCulture),
            kind,
            sourceReference,
            startOffset: 0,
            spanLength: 0,
            contentHash);
        return true;
    }

    private static string ProposalKey(
        CognitiveSemanticEnvelopeV2Proposal proposal,
        int startOffset,
        int spanLength)
        => string.Join(
            "|",
            ((int)proposal.Domain).ToString(CultureInfo.InvariantCulture),
            ((int)proposal.ClaimKind).ToString(CultureInfo.InvariantCulture),
            ((int)proposal.SubjectScope).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.TemporalScope).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.EpistemicState).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.Confidence).ToString(CultureInfo.InvariantCulture),
            ((int)proposal.SuggestedDisposition).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.SuggestedStance).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.SuggestedFollowUp).ToString(
                CultureInfo.InvariantCulture),
            ((int)proposal.SuggestedMemoryPreview).ToString(
                CultureInfo.InvariantCulture),
            proposal.RequiresConfirmation ? "1" : "0",
            startOffset.ToString(CultureInfo.InvariantCulture),
            spanLength.ToString(CultureInfo.InvariantCulture),
            string.Join(
                ",",
                proposal.MetadataEvidence
                    .OrderBy(item => item)
                    .Select(item => ((int)item).ToString(
                        CultureInfo.InvariantCulture))));
}
