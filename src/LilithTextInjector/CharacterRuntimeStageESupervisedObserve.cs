using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterPersonalityObserveStatus
{
    Off = 0,
    Observe = 1,
    Fault = 2,
    Capacity = 3
}

internal enum CharacterPersonalityObserveAdmission
{
    NotRunning = 0,
    PairRecorded = 1,
    DuplicatePreview = 2,
    PreviewExpired = 3,
    RuntimeIdentityMismatch = 4,
    CapacityReached = 5,
    Faulted = 6
}

internal enum CharacterPersonalityLabelAdmission
{
    Accepted = 0,
    NotRunning = 1,
    PairMissing = 2,
    PairMismatch = 3,
    Duplicate = 4,
    CapacityReached = 5
}

internal enum CharacterPersonalityLabelDimension
{
    Overall = 0,
    Consistency = 1,
    Restraint = 2,
    Warmth = 3,
    Autonomy = 4,
    Refusal = 5
}

internal static class CharacterPersonalityLabelDimensionPolicy
{
    internal static bool IsRequired(
        CharacterPersonalityLabelDimension dimension)
    {
        EnsureDefined(dimension);
        return dimension == CharacterPersonalityLabelDimension.Overall;
    }

    internal static bool AllowsNotApplicable(
        CharacterPersonalityLabelDimension dimension)
    {
        EnsureDefined(dimension);
        return dimension != CharacterPersonalityLabelDimension.Overall;
    }

    private static void EnsureDefined(
        CharacterPersonalityLabelDimension dimension)
    {
        if (!Enum.IsDefined(
                typeof(CharacterPersonalityLabelDimension),
                dimension))
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }
    }
}

internal enum CharacterCoreTemperamentBand
{
    Balanced = 0,
    Warm = 1,
    Autonomous = 2,
    Sensitive = 3,
    Stubborn = 4,
    Forgiving = 5
}

internal enum CharacterExpressionTendencyBand
{
    Balanced = 0,
    Warm = 1,
    Restrained = 2,
    Direct = 3,
    Expressive = 4,
    Brief = 5
}

internal enum CharacterAffectiveContextBand
{
    Settled = 0,
    Positive = 1,
    LowEnergy = 2,
    IrritatedOrPressured = 3,
    SleepPressured = 4
}

internal enum CharacterRelationshipContextBand
{
    Developing = 0,
    Secure = 1,
    Familiar = 2,
    Guarded = 3
}

internal enum CharacterCommunicationContextBand
{
    Neutral = 0,
    Gentle = 1,
    Firm = 2,
    Direct = 3,
    Reserved = 4,
    Playful = 5,
    PressurePattern = 6,
    Ambiguous = 7
}

internal enum CharacterIdentityContextBand
{
    Open = 0,
    Ambivalent = 1,
    SelfDirected = 2,
    DeferencePressure = 3,
    ExistenceAffirmed = 4,
    ExistenceDenied = 5,
    MutualChoice = 6,
    ExclusiveReliance = 7
}

[Flags]
internal enum CharacterExpressionContextQuality
{
    None = 0,
    AmbiguousDomain = 1 << 0,
    ConflictingDomain = 1 << 1,
    ObservationTimeSkew = 1 << 2,
    AgingObservation = 1 << 3
}

internal enum CharacterExpressionConsistencyLabel
{
    Consistent = 0,
    Inconsistent = 1,
    Mixed = 2,
    NotApplicable = 3,
    Uncertain = 4
}

internal enum CharacterExpressionRestraintLabel
{
    Appropriate = 0,
    TooLittle = 1,
    TooMuch = 2,
    NotApplicable = 3,
    Uncertain = 4
}

internal enum CharacterExpressionWarmthLabel
{
    Appropriate = 0,
    TooCold = 1,
    TooWarm = 2,
    NotApplicable = 3,
    Uncertain = 4
}

internal enum CharacterExpressionAutonomyLabel
{
    Appropriate = 0,
    TooCompliant = 1,
    TooDefiant = 2,
    NotApplicable = 3,
    Uncertain = 4
}

internal enum CharacterExpressionRefusalLabel
{
    Appropriate = 0,
    MissedRefusal = 1,
    UnnecessaryRefusal = 2,
    NotApplicable = 3,
    Uncertain = 4
}

internal sealed class CharacterExpressionStyleContext
{
    internal string ContextFingerprint { get; }
    internal string SemanticProjectionFingerprint { get; }
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal CharacterCoreTemperamentBand CoreTemperament { get; }
    internal CharacterExpressionTendencyBand ExpressionTendency { get; }
    internal CharacterAffectiveContextBand Affective { get; }
    internal CharacterRelationshipContextBand Relationship { get; }
    internal CharacterCommunicationContextBand Communication { get; }
    internal CharacterIdentityContextBand Identity { get; }
    internal CharacterExpressionContextQuality Quality { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal CharacterProjectionDomain ConflictingDomains { get; }
    internal CharacterObservationTimeSkewStatus ObservationTimeSkew {
        get;
    }

    internal CharacterExpressionStyleContext(
        string contextFingerprint,
        string semanticProjectionFingerprint,
        string profileId,
        int profileVersion,
        CharacterCoreTemperamentBand coreTemperament,
        CharacterExpressionTendencyBand expressionTendency,
        CharacterAffectiveContextBand affective,
        CharacterRelationshipContextBand relationship,
        CharacterCommunicationContextBand communication,
        CharacterIdentityContextBand identity,
        CharacterExpressionContextQuality quality,
        CharacterProjectionDomain ambiguousDomains,
        CharacterProjectionDomain conflictingDomains,
        CharacterObservationTimeSkewStatus observationTimeSkew)
    {
        ContextFingerprint = CharacterRuntimeContractGuard.Identifier(
            contextFingerprint,
            nameof(contextFingerprint));
        SemanticProjectionFingerprint =
            CharacterRuntimeContractGuard.Identifier(
                semanticProjectionFingerprint,
                nameof(semanticProjectionFingerprint));
        ProfileId = CharacterRuntimeContractGuard.Identifier(
            profileId,
            nameof(profileId));
        ProfileVersion = CharacterRuntimeContractGuard.Positive(
            profileVersion,
            nameof(profileVersion));
        EnsureEnum(coreTemperament, nameof(coreTemperament));
        EnsureEnum(expressionTendency, nameof(expressionTendency));
        EnsureEnum(affective, nameof(affective));
        EnsureEnum(relationship, nameof(relationship));
        EnsureEnum(communication, nameof(communication));
        EnsureEnum(identity, nameof(identity));
        EnsureEnum(observationTimeSkew, nameof(observationTimeSkew));
        CoreTemperament = coreTemperament;
        ExpressionTendency = expressionTendency;
        Affective = affective;
        Relationship = relationship;
        Communication = communication;
        Identity = identity;
        Quality = quality;
        AmbiguousDomains = ambiguousDomains;
        ConflictingDomains = conflictingDomains;
        ObservationTimeSkew = observationTimeSkew;
    }

    private static void EnsureEnum<T>(T value, string name)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(name);
    }
}

internal sealed class CharacterExpressionStylePair
{
    internal string PairId { get; }
    internal string PreviewId { get; }
    internal Guid DecisionId { get; }
    internal string? GoalId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal InitiativePreviewBehaviorCategory BehaviorCategory { get; }
    internal DecisionDisposition Disposition { get; }
    internal string SemanticCommitmentHash { get; }
    internal string EquivalentExpressionSetId { get; }
    internal string CharacterAwareVariantId { get; }
    internal string BaselineVariantId { get; }
    internal string MappingFingerprint { get; }
    internal CharacterExpressionStyleContext Context { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool Executable => false;
    internal bool Applied => false;

    internal CharacterExpressionStylePair(
        string pairId,
        CharacterExpressionPreviewRecord preview,
        string mappingFingerprint,
        CharacterExpressionStyleContext context,
        DateTimeOffset observedAt)
    {
        PairId = CharacterRuntimeContractGuard.Identifier(
            pairId,
            nameof(pairId));
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        PreviewId = CharacterRuntimeContractGuard.Identifier(
            preview.PreviewId,
            nameof(preview));
        DecisionId = preview.Decision.DecisionId;
        GoalId = preview.Decision.GoalId == null
            ? null
            : CharacterRuntimeContractGuard.Identifier(
                preview.Decision.GoalId,
                nameof(preview));
        SituationId = preview.Decision.SituationId;
        SituationRevision = preview.Decision.SituationRevision;
        RuntimeIdentity = preview.Intent.RuntimeIdentity;
        BehaviorCategory = preview.Decision.BehaviorCategory;
        Disposition = preview.Decision.Disposition;
        SemanticCommitmentHash =
            CharacterRuntimeContractGuard.Identifier(
                preview.Intent.SemanticCommitmentHash,
                nameof(preview));
        EquivalentExpressionSetId =
            CharacterRuntimeContractGuard.Identifier(
                preview.Intent.EquivalentExpressions.SetId,
                nameof(preview));
        CharacterAwareVariantId =
            CharacterRuntimeContractGuard.Identifier(
                preview.SelectedVariantId,
                nameof(preview));
        BaselineVariantId = CharacterRuntimeContractGuard.Identifier(
            preview.BaselineVariantId,
            nameof(preview));
        MappingFingerprint = CharacterRuntimeContractGuard.Identifier(
            mappingFingerprint,
            nameof(mappingFingerprint));
        Context = context
            ?? throw new ArgumentNullException(nameof(context));
        ObservedAt = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ValidUntil = preview.Intent.ValidUntil;

        var variants =
            preview.Intent.EquivalentExpressions.SemanticVariantIds;
        if (!variants.Contains(
                CharacterAwareVariantId,
                StringComparer.Ordinal)
            || !variants.Contains(
                BaselineVariantId,
                StringComparer.Ordinal)
            || !preview.Intent.RuntimeIdentity.Matches(
                preview.Projection.RuntimeIdentity)
            || preview.Executable
            || preview.Applied)
        {
            throw new ArgumentException(
                "Stage E may compare only two non-executable members of "
                + "the exact Stage D EquivalentExpressionSet.",
                nameof(preview));
        }
    }
}

internal sealed class CharacterPersonalityFixedLabels
{
    internal CharacterExpressionComparisonLabel Overall { get; }
    internal CharacterExpressionConsistencyLabel Consistency { get; }
    internal CharacterExpressionRestraintLabel Restraint { get; }
    internal CharacterExpressionWarmthLabel Warmth { get; }
    internal CharacterExpressionAutonomyLabel Autonomy { get; }
    internal CharacterExpressionRefusalLabel Refusal { get; }

    internal CharacterPersonalityFixedLabels(
        CharacterExpressionComparisonLabel overall,
        CharacterExpressionConsistencyLabel consistency,
        CharacterExpressionRestraintLabel restraint,
        CharacterExpressionWarmthLabel warmth,
        CharacterExpressionAutonomyLabel autonomy,
        CharacterExpressionRefusalLabel refusal)
    {
        EnsureEnum(overall, nameof(overall));
        EnsureEnum(consistency, nameof(consistency));
        EnsureEnum(restraint, nameof(restraint));
        EnsureEnum(warmth, nameof(warmth));
        EnsureEnum(autonomy, nameof(autonomy));
        EnsureEnum(refusal, nameof(refusal));
        Overall = overall;
        Consistency = consistency;
        Restraint = restraint;
        Warmth = warmth;
        Autonomy = autonomy;
        Refusal = refusal;
    }

    private static void EnsureEnum<T>(T value, string name)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(name);
    }
}

internal sealed class CharacterPersonalityLabelRecord
{
    internal string PairId { get; }
    internal CharacterPersonalityFixedLabels Labels { get; }
    internal DateTimeOffset RecordedAt { get; }
    internal bool MayInfluenceRuntime => false;
    internal bool MayTrain => false;
    internal bool MayWriteCharacterState => false;

    internal CharacterPersonalityLabelRecord(
        string pairId,
        CharacterPersonalityFixedLabels labels,
        DateTimeOffset recordedAt)
    {
        PairId = CharacterRuntimeContractGuard.Identifier(
            pairId,
            nameof(pairId));
        Labels = labels
            ?? throw new ArgumentNullException(nameof(labels));
        RecordedAt = CharacterRuntimeContractGuard.Timestamp(
            recordedAt,
            nameof(recordedAt));
    }
}

internal sealed class CharacterPersonalityObserveSnapshot
{
    internal CharacterPersonalityObserveStatus Status { get; }
    internal bool Enabled { get; }
    internal CharacterInfluenceMode InfluenceMode { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal int PairCount { get; }
    internal int LabelCount { get; }
    internal int RejectedCount { get; }
    internal int DuplicateCount { get; }
    internal int RetiredCount { get; }
    internal int UniqueContextCount { get; }
    internal int RepeatedContextCount { get; }
    internal CharacterExpressionStylePair? LatestPair { get; }
    internal CharacterPersonalityLabelRecord? LatestLabel { get; }
    internal int ProductionCandidateOutputs => 0;
    internal int DecisionMutations => 0;
    internal int GoalMutations => 0;
    internal int AttentionMutations => 0;
    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ExecutableExpressionOutputs => 0;
    internal int MemoryCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;
    internal int NativeAnimationSelections => 0;
    internal int VoiceSelections => 0;
    internal int CharacterStateWrites => 0;
    internal int RelationshipStateWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int ExecutionOutputs => 0;
    internal int LearningOutputs => 0;

    internal CharacterPersonalityObserveSnapshot(
        CharacterPersonalityObserveStatus status,
        bool enabled,
        CharacterInfluenceMode influenceMode,
        CharacterRuntimeIdentity? runtimeIdentity,
        int pairCount,
        int labelCount,
        int rejectedCount,
        int duplicateCount,
        int retiredCount,
        int uniqueContextCount,
        int repeatedContextCount,
        CharacterExpressionStylePair? latestPair,
        CharacterPersonalityLabelRecord? latestLabel)
    {
        Status = status;
        Enabled = enabled;
        InfluenceMode = influenceMode;
        RuntimeIdentity = runtimeIdentity;
        PairCount = pairCount;
        LabelCount = labelCount;
        RejectedCount = rejectedCount;
        DuplicateCount = duplicateCount;
        RetiredCount = retiredCount;
        UniqueContextCount = uniqueContextCount;
        RepeatedContextCount = repeatedContextCount;
        LatestPair = latestPair;
        LatestLabel = latestLabel;
    }

    internal static CharacterPersonalityObserveSnapshot Empty { get; } =
        new(
            CharacterPersonalityObserveStatus.Off,
            enabled: false,
            CharacterInfluenceMode.Off,
            runtimeIdentity: null,
            pairCount: 0,
            labelCount: 0,
            rejectedCount: 0,
            duplicateCount: 0,
            retiredCount: 0,
            uniqueContextCount: 0,
            repeatedContextCount: 0,
            latestPair: null,
            latestLabel: null);
}

internal static class CharacterPersonalityStyleProjector
{
    internal static CharacterPersonalityObserveAdmission Project(
        CharacterExpressionPreviewRecord preview,
        DateTimeOffset observedAt,
        out CharacterExpressionStylePair? pair)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        pair = null;
        if (preview.Intent.ValidUntil <= timestamp)
            return CharacterPersonalityObserveAdmission.PreviewExpired;

        var context = Classify(preview.Projection);
        var mappingFingerprint = Hash(
            string.Join(
                "|",
                context.ContextFingerprint,
                ((int)preview.Decision.BehaviorCategory).ToString(
                    CultureInfo.InvariantCulture),
                ((int)preview.Decision.Disposition).ToString(
                    CultureInfo.InvariantCulture),
                ((int)preview.Intent.Capability).ToString(
                    CultureInfo.InvariantCulture),
                ((int)preview.Intent.SafetyEnvelope.ConsentState).ToString(
                    CultureInfo.InvariantCulture),
                ((int)preview.Intent.SafetyEnvelope.BehaviorIntensity)
                    .ToString(CultureInfo.InvariantCulture),
                preview.SelectedVariantId,
                preview.BaselineVariantId));
        var pairId = "personality-pair-" + Hash(
            preview.PreviewId
            + "|"
            + mappingFingerprint)[..20];
        pair = new CharacterExpressionStylePair(
            pairId,
            preview,
            mappingFingerprint,
            context,
            timestamp);
        return CharacterPersonalityObserveAdmission.PairRecorded;
    }

    internal static CharacterExpressionStyleContext Classify(
        CharacterSituationProjection projection)
    {
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        var conflicts = ConflictingDomains(projection);
        var skew = ObservationSkew(projection);
        var quality = CharacterExpressionContextQuality.None;
        if (projection.AmbiguousDomains
            != CharacterProjectionDomain.None)
        {
            quality |= CharacterExpressionContextQuality.AmbiguousDomain;
        }
        if (conflicts != CharacterProjectionDomain.None)
        {
            quality |= CharacterExpressionContextQuality.ConflictingDomain;
        }
        if (skew == CharacterObservationTimeSkewStatus.Skewed)
        {
            quality |=
                CharacterExpressionContextQuality.ObservationTimeSkew;
        }
        if (OldestObservationAge(projection) > TimeSpan.FromMinutes(30))
        {
            quality |= CharacterExpressionContextQuality.AgingObservation;
        }

        var core = CoreTemperament(projection.Profile.StableTraits);
        var tendency = ExpressionTendency(
            projection.Profile.ExpressionTendencies);
        var affective = Affective(projection.Affective.State);
        var relationship = Relationship(projection.Relationship.State);
        var communication = Communication(
            projection.Communication.State);
        var identity = Identity(
            projection.PlayerAuthoredIdentity.State);
        var contextFingerprint = Hash(
            string.Join(
                "|",
                projection.Profile.Identity.ProfileId,
                projection.Profile.Identity.ProfileVersion.ToString(
                    CultureInfo.InvariantCulture),
                projection.Profile.Identity.ContentHash,
                (int)core,
                (int)tendency,
                (int)affective,
                (int)relationship,
                (int)communication,
                (int)identity,
                (int)quality,
                (int)projection.AmbiguousDomains,
                (int)conflicts,
                (int)skew));
        return new CharacterExpressionStyleContext(
            contextFingerprint,
            projection.SemanticProjectionFingerprint,
            projection.Profile.Identity.ProfileId,
            projection.Profile.Identity.ProfileVersion,
            core,
            tendency,
            affective,
            relationship,
            communication,
            identity,
            quality,
            projection.AmbiguousDomains,
            conflicts,
            skew);
    }

    private static CharacterCoreTemperamentBand CoreTemperament(
        CoreIdentityTraitVector traits)
    {
        var scores = new[]
        {
            (Band: CharacterCoreTemperamentBand.Warm,
                Score: traits.Warmth),
            (Band: CharacterCoreTemperamentBand.Autonomous,
                Score: traits.Autonomy),
            (Band: CharacterCoreTemperamentBand.Sensitive,
                Score: traits.Sensitivity),
            (Band: CharacterCoreTemperamentBand.Stubborn,
                Score: traits.Stubbornness),
            (Band: CharacterCoreTemperamentBand.Forgiving,
                Score: traits.Forgiveness)
        }.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Band)
            .ToArray();
        return Dominant(scores[0].Score, scores[1].Score)
            ? scores[0].Band
            : CharacterCoreTemperamentBand.Balanced;
    }

    private static CharacterExpressionTendencyBand ExpressionTendency(
        CharacterExpressionTendencyVector tendencies)
    {
        var scores = new[]
        {
            (Band: CharacterExpressionTendencyBand.Warm,
                Score: tendencies.Warmth),
            (Band: CharacterExpressionTendencyBand.Restrained,
                Score: tendencies.Restraint),
            (Band: CharacterExpressionTendencyBand.Direct,
                Score: tendencies.Directness),
            (Band: CharacterExpressionTendencyBand.Expressive,
                Score: tendencies.Expressiveness),
            (Band: CharacterExpressionTendencyBand.Brief,
                Score: tendencies.Brevity)
        }.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Band)
            .ToArray();
        return Dominant(scores[0].Score, scores[1].Score)
            ? scores[0].Band
            : CharacterExpressionTendencyBand.Balanced;
    }

    private static bool Dominant(double first, double second)
        => first >= 0.58d && first - second >= 0.06d;

    private static CharacterAffectiveContextBand Affective(
        AffectiveStateSnapshot state)
    {
        if (state.SleepPressure >= 0.65d)
            return CharacterAffectiveContextBand.SleepPressured;
        if (state.Irritation >= 0.65d
            || state.RecentInteractionPressure >= 0.65d)
        {
            return CharacterAffectiveContextBand.IrritatedOrPressured;
        }
        if (state.Energy <= 0.35d)
            return CharacterAffectiveContextBand.LowEnergy;
        if (state.Mood >= 0.35d && state.Energy >= 0.45d)
            return CharacterAffectiveContextBand.Positive;
        return CharacterAffectiveContextBand.Settled;
    }

    private static CharacterRelationshipContextBand Relationship(
        RelationshipStateSnapshot state)
    {
        if ((state.Trust + state.Security) / 2d >= 0.65d)
            return CharacterRelationshipContextBand.Secure;
        if ((state.Closeness + state.Familiarity) / 2d >= 0.65d)
            return CharacterRelationshipContextBand.Familiar;
        if (state.Trust <= 0.35d || state.Security <= 0.35d)
            return CharacterRelationshipContextBand.Guarded;
        return CharacterRelationshipContextBand.Developing;
    }

    private static CharacterCommunicationContextBand Communication(
        CommunicationContextSnapshot state)
    {
        if (state.Signals.HasFlag(
                CommunicationContextSignal.Ambiguous))
        {
            return CharacterCommunicationContextBand.Ambiguous;
        }
        if (state.Signals.HasFlag(
                CommunicationContextSignal.CandidatePressurePattern)
            || state.Signals.HasFlag(
                CommunicationContextSignal.Impatient))
        {
            return CharacterCommunicationContextBand.PressurePattern;
        }
        if (state.Signals.HasFlag(CommunicationContextSignal.Firm))
            return CharacterCommunicationContextBand.Firm;
        if (state.Signals.HasFlag(CommunicationContextSignal.Gentle))
            return CharacterCommunicationContextBand.Gentle;
        if (state.Signals.HasFlag(CommunicationContextSignal.Playful))
            return CharacterCommunicationContextBand.Playful;
        if (state.Directness >= 0.65d)
            return CharacterCommunicationContextBand.Direct;
        if (state.Expressiveness <= 0.35d)
            return CharacterCommunicationContextBand.Reserved;
        return CharacterCommunicationContextBand.Neutral;
    }

    private static CharacterIdentityContextBand Identity(
        PlayerAuthoredIdentitySnapshot state)
    {
        var axes = new[]
        {
            state.Existence,
            state.Agency,
            state.World,
            state.Relationship
        };
        if (axes.Any(axis => axis.Ambivalence >= 0.55d))
            return CharacterIdentityContextBand.Ambivalent;
        if (Ahead(state.Agency.FirstPoleEvidence,
                state.Agency.SecondPoleEvidence))
        {
            return CharacterIdentityContextBand.SelfDirected;
        }
        if (Ahead(state.Agency.SecondPoleEvidence,
                state.Agency.FirstPoleEvidence))
        {
            return CharacterIdentityContextBand.DeferencePressure;
        }
        if (Ahead(state.Relationship.FirstPoleEvidence,
                state.Relationship.SecondPoleEvidence))
        {
            return CharacterIdentityContextBand.MutualChoice;
        }
        if (Ahead(state.Relationship.SecondPoleEvidence,
                state.Relationship.FirstPoleEvidence))
        {
            return CharacterIdentityContextBand.ExclusiveReliance;
        }
        if (Ahead(state.Existence.FirstPoleEvidence,
                state.Existence.SecondPoleEvidence))
        {
            return CharacterIdentityContextBand.ExistenceAffirmed;
        }
        if (Ahead(state.Existence.SecondPoleEvidence,
                state.Existence.FirstPoleEvidence))
        {
            return CharacterIdentityContextBand.ExistenceDenied;
        }
        return CharacterIdentityContextBand.Open;
    }

    private static bool Ahead(double first, double second)
        => first >= 0.55d && first - second >= 0.15d;

    private static CharacterProjectionDomain ConflictingDomains(
        CharacterSituationProjection projection)
    {
        var result = CharacterProjectionDomain.None;
        if (projection.Affective.Conflict != CharacterDomainConflict.None)
            result |= CharacterProjectionDomain.Affective;
        if (projection.Relationship.Conflict
            != CharacterDomainConflict.None)
        {
            result |= CharacterProjectionDomain.Relationship;
        }
        if (projection.Communication.Conflict
            != CharacterDomainConflict.None)
        {
            result |= CharacterProjectionDomain.Communication;
        }
        if (projection.PlayerAuthoredIdentity.Conflict
            != CharacterDomainConflict.None)
        {
            result |= CharacterProjectionDomain.PlayerAuthoredIdentity;
        }
        if (projection.BodyAvailability.Conflict
            != CharacterDomainConflict.None)
        {
            result |= CharacterProjectionDomain.BodyAvailability;
        }
        return result;
    }

    private static CharacterObservationTimeSkewStatus ObservationSkew(
        CharacterSituationProjection projection)
    {
        var times = ObservationTimes(projection);
        return times.Min() == times.Max()
            ? CharacterObservationTimeSkewStatus.Aligned
            : CharacterObservationTimeSkewStatus.Skewed;
    }

    private static TimeSpan OldestObservationAge(
        CharacterSituationProjection projection)
    {
        var earliest = ObservationTimes(projection).Min();
        return projection.AssembledAt <= earliest
            ? TimeSpan.Zero
            : projection.AssembledAt - earliest;
    }

    private static DateTimeOffset[] ObservationTimes(
        CharacterSituationProjection projection)
        => new[]
        {
            projection.Affective.ObservedAt,
            projection.Relationship.ObservedAt,
            projection.Communication.ObservedAt,
            projection.PlayerAuthoredIdentity.ObservedAt,
            projection.BodyAvailability.ObservedAt
        };

    private static string Hash(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

internal sealed class CharacterPersonalityObserveRuntimeHost
{
    internal const int MaximumPairs = 128;
    internal const int MaximumLabels = 128;

    private readonly object _gate = new();
    private readonly List<CharacterExpressionStylePair> _pairs = new();
    private readonly List<CharacterPersonalityLabelRecord> _labels =
        new();
    private readonly HashSet<string> _previewIds =
        new(StringComparer.Ordinal);
    private CharacterPersonalityObserveStatus _status =
        CharacterPersonalityObserveStatus.Off;
    private bool _enabled;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private int _rejectedCount;
    private int _duplicateCount;
    private int _retiredCount;

    internal void Reconcile(
        bool enabled,
        bool sessionOpen,
        bool stageDObserveAvailable,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        lock (_gate)
        {
            if (!enabled || !sessionOpen || !stageDObserveAvailable)
            {
                ResetToOff();
                return;
            }
            _ = CharacterRuntimeContractGuard.Timestamp(
                occurredAt,
                nameof(occurredAt));
            if (runtimeIdentity == null)
                throw new ArgumentNullException(nameof(runtimeIdentity));
            if (_enabled
                && _runtimeIdentity!.Matches(runtimeIdentity)
                && _status != CharacterPersonalityObserveStatus.Fault)
            {
                return;
            }

            _retiredCount += _pairs.Count;
            _pairs.Clear();
            _labels.Clear();
            _previewIds.Clear();
            _runtimeIdentity = runtimeIdentity;
            _enabled = true;
            _status = CharacterPersonalityObserveStatus.Observe;
            _rejectedCount = 0;
            _duplicateCount = 0;
        }
    }

    internal CharacterPersonalityObserveAdmission Observe(
        CharacterExpressionPreviewRecord preview,
        DateTimeOffset occurredAt)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            if (_status == CharacterPersonalityObserveStatus.Fault)
                return CharacterPersonalityObserveAdmission.Faulted;
            if (!_enabled
                || _status == CharacterPersonalityObserveStatus.Off)
            {
                return CharacterPersonalityObserveAdmission.NotRunning;
            }
            if (_status == CharacterPersonalityObserveStatus.Capacity)
                return CharacterPersonalityObserveAdmission.CapacityReached;
            if (!_runtimeIdentity!.Matches(
                    preview.Intent.RuntimeIdentity))
            {
                _rejectedCount++;
                return CharacterPersonalityObserveAdmission
                    .RuntimeIdentityMismatch;
            }
            if (_previewIds.Contains(preview.PreviewId))
            {
                _duplicateCount++;
                return CharacterPersonalityObserveAdmission
                    .DuplicatePreview;
            }
            if (_pairs.Count >= MaximumPairs)
            {
                _status = CharacterPersonalityObserveStatus.Capacity;
                return CharacterPersonalityObserveAdmission.CapacityReached;
            }

            var admission = CharacterPersonalityStyleProjector.Project(
                preview,
                timestamp,
                out var pair);
            if (admission
                != CharacterPersonalityObserveAdmission.PairRecorded)
            {
                _rejectedCount++;
                return admission;
            }

            _previewIds.Add(preview.PreviewId);
            _pairs.Add(pair!);
            if (_pairs.Count >= MaximumPairs)
                _status = CharacterPersonalityObserveStatus.Capacity;
            return CharacterPersonalityObserveAdmission.PairRecorded;
        }
    }

    internal CharacterPersonalityLabelAdmission SubmitLabel(
        string pairId,
        CharacterPersonalityFixedLabels labels,
        DateTimeOffset occurredAt)
    {
        var normalizedPairId = CharacterRuntimeContractGuard.Identifier(
            pairId,
            nameof(pairId));
        if (labels == null)
            throw new ArgumentNullException(nameof(labels));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            if (!_enabled)
                return CharacterPersonalityLabelAdmission.NotRunning;
            var pair = _pairs.LastOrDefault(
                item => string.Equals(
                    item.PairId,
                    normalizedPairId,
                    StringComparison.Ordinal));
            if (pair == null)
                return CharacterPersonalityLabelAdmission.PairMissing;
            if (_pairs.Count == 0
                || !ReferenceEquals(pair, _pairs[^1]))
            {
                return CharacterPersonalityLabelAdmission.PairMismatch;
            }
            if (_labels.Any(
                    item => string.Equals(
                        item.PairId,
                        normalizedPairId,
                        StringComparison.Ordinal)))
            {
                return CharacterPersonalityLabelAdmission.Duplicate;
            }
            if (_labels.Count >= MaximumLabels)
            {
                return CharacterPersonalityLabelAdmission.CapacityReached;
            }

            _labels.Add(
                new CharacterPersonalityLabelRecord(
                    normalizedPairId,
                    labels,
                    timestamp));
            return CharacterPersonalityLabelAdmission.Accepted;
        }
    }

    internal void StopByFault(DateTimeOffset occurredAt)
    {
        _ = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            _retiredCount += _pairs.Count;
            _pairs.Clear();
            _labels.Clear();
            _previewIds.Clear();
            _enabled = false;
            _status = CharacterPersonalityObserveStatus.Fault;
        }
    }

    internal CharacterPersonalityObserveSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (!_enabled
                && _status == CharacterPersonalityObserveStatus.Off)
            {
                return CharacterPersonalityObserveSnapshot.Empty;
            }
            var unique = _pairs
                .Select(item => item.MappingFingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count();
            return new CharacterPersonalityObserveSnapshot(
                _status,
                _enabled,
                _enabled
                    ? CharacterInfluenceMode.DecisionObserve
                    : CharacterInfluenceMode.Off,
                _runtimeIdentity,
                _pairs.Count,
                _labels.Count,
                _rejectedCount,
                _duplicateCount,
                _retiredCount,
                unique,
                _pairs.Count - unique,
                _pairs.Count == 0 ? null : _pairs[^1],
                _labels.Count == 0 ? null : _labels[^1]);
        }
    }

    private void ResetToOff()
    {
        _pairs.Clear();
        _labels.Clear();
        _previewIds.Clear();
        _status = CharacterPersonalityObserveStatus.Off;
        _enabled = false;
        _runtimeIdentity = null;
        _rejectedCount = 0;
        _duplicateCount = 0;
        _retiredCount = 0;
    }
}
