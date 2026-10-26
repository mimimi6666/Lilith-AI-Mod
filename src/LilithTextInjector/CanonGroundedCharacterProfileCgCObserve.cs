using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterCanonDualReadObserveStatus
{
    Off = 0,
    ProjectionOnly = 1,
    Fault = 2,
    Capacity = 3
}

internal enum CharacterCanonDualReadObserveAdmission
{
    NotRunning = 0,
    Recorded = 1,
    SemanticallyCoalesced = 2,
    RuntimeIdentityMismatch = 3,
    LegacyOwnerMismatch = 4,
    CandidateRejected = 5,
    CapacityReached = 6,
    Faulted = 7
}

internal enum CharacterCanonProfileOwner
{
    LegacyProfileV1 = 0
}

internal static class CharacterCanonDualReadContractGuard
{
    internal static int NonNegative(
        int value,
        string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal sealed class CharacterCanonDualReadProjection
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal CharacterCanonProfileOwner ActiveOwner =>
        CharacterCanonProfileOwner.LegacyProfileV1;
    internal CharacterInfluenceMode InfluenceMode =>
        CharacterInfluenceMode.ProjectionOnly;
    internal string LegacyProfileId { get; }
    internal int LegacyProfileVersion { get; }
    internal int LegacySchemaVersion { get; }
    internal string LegacySemanticHash { get; }
    internal string CandidateProfileId { get; }
    internal int CandidateProfileVersion { get; }
    internal int CandidateSchemaVersion { get; }
    internal string CandidateCorpusVersion { get; }
    internal string CandidateCorpusHash { get; }
    internal string CandidateSemanticHash { get; }
    internal int StableDispositionCount { get; }
    internal int CanonicalTensionCount { get; }
    internal int InterpretiveBoundaryCount { get; }
    internal int ContextCount { get; }
    internal int TypedUnknownCount { get; }
    internal CharacterContinuityClaimStatus ContinuityStatus { get; }
    internal string SemanticComparisonFingerprint { get; }
    internal bool CandidateIsActiveOwner => false;
    internal bool HasSingleActiveOwner => true;

    internal CharacterCanonDualReadProjection(
        CharacterRuntimeIdentity runtimeIdentity,
        long projectionRevision,
        long situationRevision,
        DateTimeOffset observedAt,
        CoreIdentityProfile legacyProfile,
        CanonGroundedCharacterProfile candidateProfile)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProjectionRevision = CharacterRuntimeContractGuard.Positive(
            projectionRevision,
            nameof(projectionRevision));
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        ObservedAt = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (legacyProfile == null)
            throw new ArgumentNullException(nameof(legacyProfile));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));

        LegacyProfileId = legacyProfile.Identity.ProfileId;
        LegacyProfileVersion = legacyProfile.Identity.ProfileVersion;
        LegacySchemaVersion = legacyProfile.Identity.SchemaVersion;
        LegacySemanticHash = legacyProfile.Identity.ContentHash;
        CandidateProfileId = candidateProfile.Identity.ProfileId;
        CandidateProfileVersion = candidateProfile.Identity.ProfileVersion;
        CandidateSchemaVersion = candidateProfile.Identity.SchemaVersion;
        CandidateCorpusVersion = candidateProfile.Identity.CorpusVersion;
        CandidateCorpusHash = candidateProfile.Identity.CorpusSha256;
        CandidateSemanticHash = candidateProfile.Identity.SemanticHash;
        StableDispositionCount =
            candidateProfile.Content.StableDispositions.Count;
        CanonicalTensionCount =
            candidateProfile.Content.CanonicalTensions.Count;
        InterpretiveBoundaryCount =
            candidateProfile.Content.Boundaries.Count;
        ContextCount = candidateProfile.Content.Contexts.Count;
        TypedUnknownCount = candidateProfile.Content.Unknowns.Count;
        ContinuityStatus = candidateProfile.Content.ContinuityStatus;
        SemanticComparisonFingerprint =
            CharacterCanonDualReadProjectionBuilder
                .SemanticComparisonFingerprint(
                    legacyProfile.Identity,
                    candidateProfile.Identity,
                    StableDispositionCount,
                    CanonicalTensionCount,
                    InterpretiveBoundaryCount,
                    ContextCount,
                    TypedUnknownCount,
                    ContinuityStatus);
    }
}

internal sealed class CharacterCanonDualReadProjectionResult
{
    internal CharacterCanonDualReadObserveAdmission Admission { get; }
    internal CharacterCanonDualReadProjection? Projection { get; }
    internal string ReasonCode { get; }
    internal bool Accepted =>
        Admission is CharacterCanonDualReadObserveAdmission.Recorded
            or CharacterCanonDualReadObserveAdmission
                .SemanticallyCoalesced;

    internal CharacterCanonDualReadProjectionResult(
        CharacterCanonDualReadObserveAdmission admission,
        CharacterCanonDualReadProjection? projection,
        string reasonCode)
    {
        if (!Enum.IsDefined(
                typeof(CharacterCanonDualReadObserveAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        Admission = admission;
        Projection = projection;
        ReasonCode = CharacterRuntimeContractGuard.Identifier(
            reasonCode,
            nameof(reasonCode));
    }
}

internal static class CharacterCanonDualReadProjectionBuilder
{
    internal static CharacterCanonDualReadProjectionResult Create(
        CharacterRuntimeIdentity runtimeIdentity,
        CoreIdentityProfile legacyProfile,
        CanonGroundedCharacterProfile candidateProfile,
        long projectionRevision,
        long situationRevision,
        DateTimeOffset observedAt)
    {
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        if (legacyProfile == null)
            throw new ArgumentNullException(nameof(legacyProfile));
        if (candidateProfile == null)
            throw new ArgumentNullException(nameof(candidateProfile));

        if (!string.Equals(
                runtimeIdentity.RuntimeProfileId,
                legacyProfile.Identity.ProfileId,
                StringComparison.Ordinal))
        {
            return Rejected(
                CharacterCanonDualReadObserveAdmission.LegacyOwnerMismatch,
                "legacy-owner-mismatch");
        }
        if (string.Equals(
                legacyProfile.Identity.ProfileId,
                candidateProfile.Identity.ProfileId,
                StringComparison.Ordinal))
        {
            return Rejected(
                CharacterCanonDualReadObserveAdmission.CandidateRejected,
                "candidate-owner-collision");
        }
        var candidateValidation =
            CharacterCanonProfileValidator.Validate(
                candidateProfile.Identity,
                candidateProfile.Content);
        if (!candidateValidation.Accepted)
        {
            return Rejected(
                CharacterCanonDualReadObserveAdmission.CandidateRejected,
                "candidate-validation-rejected");
        }

        return new CharacterCanonDualReadProjectionResult(
            CharacterCanonDualReadObserveAdmission.Recorded,
            new CharacterCanonDualReadProjection(
                runtimeIdentity,
                projectionRevision,
                situationRevision,
                observedAt,
                legacyProfile,
                candidateProfile),
            "projection-recorded");
    }

    internal static string SemanticComparisonFingerprint(
        CharacterProfileIdentity legacyIdentity,
        CharacterCanonProfileIdentity candidateIdentity,
        int stableDispositionCount,
        int canonicalTensionCount,
        int interpretiveBoundaryCount,
        int contextCount,
        int typedUnknownCount,
        CharacterContinuityClaimStatus continuityStatus)
    {
        if (legacyIdentity == null)
            throw new ArgumentNullException(nameof(legacyIdentity));
        if (candidateIdentity == null)
            throw new ArgumentNullException(nameof(candidateIdentity));
        CharacterCanonDualReadContractGuard.NonNegative(
            stableDispositionCount,
            nameof(stableDispositionCount));
        CharacterCanonDualReadContractGuard.NonNegative(
            canonicalTensionCount,
            nameof(canonicalTensionCount));
        CharacterCanonDualReadContractGuard.NonNegative(
            interpretiveBoundaryCount,
            nameof(interpretiveBoundaryCount));
        CharacterCanonDualReadContractGuard.NonNegative(
            contextCount,
            nameof(contextCount));
        CharacterCanonDualReadContractGuard.NonNegative(
            typedUnknownCount,
            nameof(typedUnknownCount));
        CharacterCanonContractGuard.Defined(
            continuityStatus,
            nameof(continuityStatus));

        var source = string.Join(
            "|",
            "cg-c-v1",
            legacyIdentity.ProfileId,
            legacyIdentity.ProfileVersion,
            legacyIdentity.SchemaVersion,
            legacyIdentity.ContentHash,
            candidateIdentity.ProfileId,
            candidateIdentity.ProfileVersion,
            candidateIdentity.SchemaVersion,
            candidateIdentity.CorpusVersion,
            candidateIdentity.CorpusSha256,
            candidateIdentity.SemanticHash,
            stableDispositionCount,
            canonicalTensionCount,
            interpretiveBoundaryCount,
            contextCount,
            typedUnknownCount,
            (int)continuityStatus);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static CharacterCanonDualReadProjectionResult Rejected(
        CharacterCanonDualReadObserveAdmission admission,
        string reasonCode)
        => new(admission, null, reasonCode);
}

internal sealed class CharacterCanonDualReadObserveRecord
{
    internal long Sequence { get; }
    internal CharacterCanonDualReadProjection Projection { get; }
    internal CharacterCanonDualReadObserveAdmission Admission { get; }
    internal int CoalescedCount { get; private set; }

    internal CharacterCanonDualReadObserveRecord(
        long sequence,
        CharacterCanonDualReadProjection projection)
    {
        Sequence = CharacterRuntimeContractGuard.Positive(
            sequence,
            nameof(sequence));
        Projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        Admission = CharacterCanonDualReadObserveAdmission.Recorded;
    }

    internal void IncrementCoalesced()
        => CoalescedCount = checked(CoalescedCount + 1);
}

internal sealed class CharacterCanonDualReadAuthorityCounters
{
    internal static CharacterCanonDualReadAuthorityCounters Zero { get; } =
        new();

    internal int ProfileOwnerWrites => 0;
    internal int DecisionOutputs => 0;
    internal int GoalOutputs => 0;
    internal int AttentionOutputs => 0;
    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int MemoryCalls => 0;
    internal int ExpressionOutputs => 0;
    internal int CandidateOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int LearningWrites => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        ProfileOwnerWrites == 0
        && DecisionOutputs == 0
        && GoalOutputs == 0
        && AttentionOutputs == 0
        && PromptWrites == 0
        && ReplyOutputs == 0
        && ProviderCalls == 0
        && MemoryCalls == 0
        && ExpressionOutputs == 0
        && CandidateOutputs == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && PersistenceWrites == 0
        && LearningWrites == 0
        && ExecutionOutputs == 0;

    private CharacterCanonDualReadAuthorityCounters()
    {
    }
}

internal sealed class CharacterCanonDualReadObserveSnapshot
{
    internal bool Enabled { get; }
    internal CharacterCanonDualReadObserveStatus Status { get; }
    internal CharacterInfluenceMode InfluenceMode { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal IReadOnlyList<CharacterCanonDualReadObserveRecord> Records {
        get;
    }
    internal CharacterCanonDualReadProjection? Latest =>
        Records.LastOrDefault()?.Projection;
    internal int ObservationCount { get; }
    internal int AcceptedCount { get; }
    internal int CoalescedCount { get; }
    internal int RejectedCount { get; }
    internal int RetiredCount { get; }
    internal CharacterCanonDualReadAuthorityCounters AuthorityCounters {
        get;
    }

    internal CharacterCanonDualReadObserveSnapshot(
        bool enabled,
        CharacterCanonDualReadObserveStatus status,
        CharacterRuntimeIdentity? runtimeIdentity,
        IEnumerable<CharacterCanonDualReadObserveRecord> records,
        int observationCount,
        int acceptedCount,
        int coalescedCount,
        int rejectedCount,
        int retiredCount)
    {
        Enabled = enabled;
        Status = status;
        RuntimeIdentity = runtimeIdentity;
        Records = Array.AsReadOnly((records
                ?? throw new ArgumentNullException(nameof(records)))
            .ToArray());
        ObservationCount = CharacterCanonDualReadContractGuard.NonNegative(
            observationCount,
            nameof(observationCount));
        AcceptedCount = CharacterCanonDualReadContractGuard.NonNegative(
            acceptedCount,
            nameof(acceptedCount));
        CoalescedCount = CharacterCanonDualReadContractGuard.NonNegative(
            coalescedCount,
            nameof(coalescedCount));
        RejectedCount = CharacterCanonDualReadContractGuard.NonNegative(
            rejectedCount,
            nameof(rejectedCount));
        RetiredCount = CharacterCanonDualReadContractGuard.NonNegative(
            retiredCount,
            nameof(retiredCount));
        InfluenceMode = enabled
            && status == CharacterCanonDualReadObserveStatus.ProjectionOnly
                ? CharacterInfluenceMode.ProjectionOnly
                : CharacterInfluenceMode.Off;
        AuthorityCounters = CharacterCanonDualReadAuthorityCounters.Zero;
    }
}

internal sealed class CharacterCanonDualReadObserveHost
{
    internal const int MaximumRecords = 128;

    private readonly List<CharacterCanonDualReadObserveRecord> _records =
        new();
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private bool _enabled;
    private CharacterCanonDualReadObserveStatus _status =
        CharacterCanonDualReadObserveStatus.Off;
    private int _observationCount;
    private int _acceptedCount;
    private int _coalescedCount;
    private int _rejectedCount;
    private int _retiredCount;
    private long _sequence;

    internal void Reconcile(
        bool enabled,
        bool sessionOpen,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        if (!enabled || !sessionOpen)
        {
            Clear();
            return;
        }
        CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));

        if (_enabled
            && _runtimeIdentity != null
            && _runtimeIdentity.Matches(runtimeIdentity)
            && _status != CharacterCanonDualReadObserveStatus.Fault)
        {
            return;
        }

        if (_enabled)
            _retiredCount = checked(_retiredCount + _records.Count);
        if (!_enabled
            || _runtimeIdentity == null
            || !_runtimeIdentity.Matches(runtimeIdentity)
            || _status == CharacterCanonDualReadObserveStatus.Fault)
        {
            _records.Clear();
            _runtimeIdentity = runtimeIdentity;
            _observationCount = 0;
            _acceptedCount = 0;
            _coalescedCount = 0;
            _rejectedCount = 0;
            _sequence = 0;
        }
        _enabled = true;
        _status = CharacterCanonDualReadObserveStatus.ProjectionOnly;
    }

    internal CharacterCanonDualReadObserveAdmission Observe(
        CharacterCanonDualReadProjectionResult result,
        DateTimeOffset occurredAt)
    {
        CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (!_enabled
            || _status != CharacterCanonDualReadObserveStatus.ProjectionOnly
            || _runtimeIdentity == null)
        {
            return CharacterCanonDualReadObserveAdmission.NotRunning;
        }

        _observationCount = checked(_observationCount + 1);
        if (!result.Accepted || result.Projection == null)
        {
            _rejectedCount = checked(_rejectedCount + 1);
            return result.Admission;
        }
        if (!_runtimeIdentity.Matches(
                result.Projection.RuntimeIdentity))
        {
            _rejectedCount = checked(_rejectedCount + 1);
            return CharacterCanonDualReadObserveAdmission
                .RuntimeIdentityMismatch;
        }
        if (_records.Count > 0
            && string.Equals(
                _records[^1].Projection.SemanticComparisonFingerprint,
                result.Projection.SemanticComparisonFingerprint,
                StringComparison.Ordinal))
        {
            _records[^1].IncrementCoalesced();
            _coalescedCount = checked(_coalescedCount + 1);
            return CharacterCanonDualReadObserveAdmission
                .SemanticallyCoalesced;
        }
        if (_records.Count >= MaximumRecords)
        {
            _status = CharacterCanonDualReadObserveStatus.Capacity;
            _rejectedCount = checked(_rejectedCount + 1);
            return CharacterCanonDualReadObserveAdmission.CapacityReached;
        }

        _records.Add(
            new CharacterCanonDualReadObserveRecord(
                ++_sequence,
                result.Projection));
        _acceptedCount = checked(_acceptedCount + 1);
        return CharacterCanonDualReadObserveAdmission.Recorded;
    }

    internal void StopByFault()
    {
        _enabled = false;
        _status = CharacterCanonDualReadObserveStatus.Fault;
        _runtimeIdentity = null;
        _retiredCount = checked(_retiredCount + _records.Count);
        _records.Clear();
    }

    internal CharacterCanonDualReadObserveSnapshot GetSnapshot()
        => new(
            _enabled,
            _status,
            _runtimeIdentity,
            _records,
            _observationCount,
            _acceptedCount,
            _coalescedCount,
            _rejectedCount,
            _retiredCount);

    private void Clear()
    {
        _enabled = false;
        _status = CharacterCanonDualReadObserveStatus.Off;
        _runtimeIdentity = null;
        _records.Clear();
        _observationCount = 0;
        _acceptedCount = 0;
        _coalescedCount = 0;
        _rejectedCount = 0;
        _retiredCount = 0;
        _sequence = 0;
    }
}
