using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterInfluenceMode
{
    Off = 0,
    ProjectionOnly = 1,
    DecisionObserve = 2,
    ExpressionAssist = 3,
    DecisionAssist = 4
}

internal enum CharacterRuntimeStage
{
    StageAContracts = 0,
    StageBAdapters = 1,
    StageCProjectionObserve = 2,
    StageDExpressionPreview = 3,
    StageEPersonalityObserve = 4,
    StageFSeparatelyDecidedAssist = 5
}

internal static class CharacterInfluenceStagePolicy
{
    internal static bool Allows(
        CharacterRuntimeStage stage,
        CharacterInfluenceMode mode)
        => stage switch
        {
            CharacterRuntimeStage.StageAContracts
                or CharacterRuntimeStage.StageBAdapters
                => mode == CharacterInfluenceMode.Off,
            CharacterRuntimeStage.StageCProjectionObserve
                => mode is CharacterInfluenceMode.Off
                    or CharacterInfluenceMode.ProjectionOnly,
            CharacterRuntimeStage.StageDExpressionPreview
                or CharacterRuntimeStage.StageEPersonalityObserve
                => mode is CharacterInfluenceMode.Off
                    or CharacterInfluenceMode.ProjectionOnly
                    or CharacterInfluenceMode.DecisionObserve,
            CharacterRuntimeStage.StageFSeparatelyDecidedAssist
                => mode == CharacterInfluenceMode.Off,
            _ => false
        };
}

internal static class CharacterRuntimeContractGuard
{
    internal const int MaximumIdentifierLength = 128;

    internal static string Identifier(
        string? value,
        string parameterName)
        => DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);

    internal static int Positive(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static long Positive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value)
            || double.IsInfinity(value)
            || value < 0d
            || value > 1d)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    internal static double SignedUnit(
        double value,
        string parameterName)
    {
        if (double.IsNaN(value)
            || double.IsInfinity(value)
            || value < -1d
            || value > 1d)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
        => DecisionCoreContractGuard.Timestamp(value, parameterName);
}

internal sealed class CharacterProfileIdentity
{
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal int SchemaVersion { get; }
    internal string ContentHash { get; }
    internal int MinimumCompatibleSchemaVersion { get; }
    internal int MaximumCompatibleSchemaVersion { get; }

    internal CharacterProfileIdentity(
        string profileId,
        int profileVersion,
        int schemaVersion,
        string contentHash,
        int minimumCompatibleSchemaVersion,
        int maximumCompatibleSchemaVersion)
    {
        ProfileId = CharacterRuntimeContractGuard.Identifier(
            profileId,
            nameof(profileId));
        ProfileVersion = CharacterRuntimeContractGuard.Positive(
            profileVersion,
            nameof(profileVersion));
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        ContentHash = CharacterRuntimeContractGuard.Identifier(
            contentHash,
            nameof(contentHash));
        MinimumCompatibleSchemaVersion =
            CharacterRuntimeContractGuard.Positive(
                minimumCompatibleSchemaVersion,
                nameof(minimumCompatibleSchemaVersion));
        MaximumCompatibleSchemaVersion =
            CharacterRuntimeContractGuard.Positive(
                maximumCompatibleSchemaVersion,
                nameof(maximumCompatibleSchemaVersion));
        if (MinimumCompatibleSchemaVersion > SchemaVersion
            || MaximumCompatibleSchemaVersion < SchemaVersion
            || MinimumCompatibleSchemaVersion
                > MaximumCompatibleSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCompatibleSchemaVersion));
        }
    }

    internal bool Matches(CharacterProfileIdentity? other)
        => other != null
            && string.Equals(
                ProfileId,
                other.ProfileId,
                StringComparison.Ordinal)
            && ProfileVersion == other.ProfileVersion
            && SchemaVersion == other.SchemaVersion
            && string.Equals(
                ContentHash,
                other.ContentHash,
                StringComparison.Ordinal)
            && MinimumCompatibleSchemaVersion
                == other.MinimumCompatibleSchemaVersion
            && MaximumCompatibleSchemaVersion
                == other.MaximumCompatibleSchemaVersion;
}

[Flags]
internal enum CharacterNarrativeInvariant
{
    None = 0,
    PlayerParticipatesInDefinition = 1 << 0,
    SelfAwareCharacter = 1 << 1,
    ContradictionMayRemainUnresolved = 1 << 2,
    ExistenceMayBeAffirmedOrDenied = 1 << 3
}

[Flags]
internal enum CharacterBoundary
{
    None = 0,
    RelationshipIsNotConsent = 1 << 0,
    PressureIsNotConsent = 1 << 1,
    ExplicitPlayerControlHasPriority = 1 << 2,
    SafetyCannotBeOverridden = 1 << 3,
    NoAutomaticTraitLearning = 1 << 4
}

internal sealed class CoreIdentityTraitVector
{
    internal const int DimensionSetVersion = 1;

    internal double Warmth { get; }
    internal double Autonomy { get; }
    internal double Sensitivity { get; }
    internal double Stubbornness { get; }
    internal double Forgiveness { get; }

    internal CoreIdentityTraitVector(
        double warmth,
        double autonomy,
        double sensitivity,
        double stubbornness,
        double forgiveness)
    {
        Warmth = CharacterRuntimeContractGuard.Unit(
            warmth,
            nameof(warmth));
        Autonomy = CharacterRuntimeContractGuard.Unit(
            autonomy,
            nameof(autonomy));
        Sensitivity = CharacterRuntimeContractGuard.Unit(
            sensitivity,
            nameof(sensitivity));
        Stubbornness = CharacterRuntimeContractGuard.Unit(
            stubbornness,
            nameof(stubbornness));
        Forgiveness = CharacterRuntimeContractGuard.Unit(
            forgiveness,
            nameof(forgiveness));
    }
}

internal sealed class CharacterExpressionTendencyVector
{
    internal const int DimensionSetVersion = 1;

    internal double Warmth { get; }
    internal double Restraint { get; }
    internal double Directness { get; }
    internal double Expressiveness { get; }
    internal double Brevity { get; }

    internal CharacterExpressionTendencyVector(
        double warmth,
        double restraint,
        double directness,
        double expressiveness,
        double brevity)
    {
        Warmth = CharacterRuntimeContractGuard.Unit(
            warmth,
            nameof(warmth));
        Restraint = CharacterRuntimeContractGuard.Unit(
            restraint,
            nameof(restraint));
        Directness = CharacterRuntimeContractGuard.Unit(
            directness,
            nameof(directness));
        Expressiveness = CharacterRuntimeContractGuard.Unit(
            expressiveness,
            nameof(expressiveness));
        Brevity = CharacterRuntimeContractGuard.Unit(
            brevity,
            nameof(brevity));
    }
}

internal sealed class CoreIdentityProfile
{
    internal CharacterProfileIdentity Identity { get; }
    internal CharacterNarrativeInvariant NarrativeInvariants { get; }
    internal CharacterBoundary Boundaries { get; }
    internal CoreIdentityTraitVector StableTraits { get; }
    internal CharacterExpressionTendencyVector ExpressionTendencies {
        get;
    }

    internal CoreIdentityProfile(
        CharacterProfileIdentity identity,
        CharacterNarrativeInvariant narrativeInvariants,
        CharacterBoundary boundaries,
        CoreIdentityTraitVector stableTraits,
        CharacterExpressionTendencyVector expressionTendencies)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
        const CharacterNarrativeInvariant allInvariants =
            CharacterNarrativeInvariant.PlayerParticipatesInDefinition
            | CharacterNarrativeInvariant.SelfAwareCharacter
            | CharacterNarrativeInvariant
                .ContradictionMayRemainUnresolved
            | CharacterNarrativeInvariant
                .ExistenceMayBeAffirmedOrDenied;
        const CharacterBoundary allBoundaries =
            CharacterBoundary.RelationshipIsNotConsent
            | CharacterBoundary.PressureIsNotConsent
            | CharacterBoundary.ExplicitPlayerControlHasPriority
            | CharacterBoundary.SafetyCannotBeOverridden
            | CharacterBoundary.NoAutomaticTraitLearning;
        if (narrativeInvariants == CharacterNarrativeInvariant.None
            || (narrativeInvariants & ~allInvariants) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(narrativeInvariants));
        }
        if (boundaries == CharacterBoundary.None
            || (boundaries & ~allBoundaries) != 0)
            throw new ArgumentOutOfRangeException(nameof(boundaries));
        NarrativeInvariants = narrativeInvariants;
        Boundaries = boundaries;
        StableTraits = stableTraits
            ?? throw new ArgumentNullException(nameof(stableTraits));
        ExpressionTendencies = expressionTendencies
            ?? throw new ArgumentNullException(
                nameof(expressionTendencies));
    }
}

internal enum CharacterStateProvenance
{
    SyntheticContract = 0,
    LocalStateProjection = 1,
    MigratedLocalState = 2
}

internal enum CharacterDomainConflict
{
    None = 0,
    Ambiguous = 1,
    Critical = 2
}

[Flags]
internal enum CharacterProjectionDomain
{
    None = 0,
    Affective = 1 << 0,
    Relationship = 1 << 1,
    Communication = 1 << 2,
    PlayerAuthoredIdentity = 1 << 3,
    BodyAvailability = 1 << 4
}

internal interface ICharacterStateDomainSnapshot
{
}

internal sealed class CharacterStateEnvelope<T>
    where T : class, ICharacterStateDomainSnapshot
{
    internal CharacterProjectionDomain Domain { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterProfileIdentity ProfileIdentity { get; }
    internal T State { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal CharacterStateProvenance Provenance { get; }
    internal CharacterDomainConflict Conflict { get; }
    internal string SourceFingerprint { get; }

    internal CharacterStateEnvelope(
        CharacterProjectionDomain domain,
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterProfileIdentity profileIdentity,
        T state,
        DateTimeOffset observedAt,
        DateTimeOffset validUntil,
        CharacterStateProvenance provenance,
        CharacterDomainConflict conflict,
        string sourceFingerprint)
    {
        if (domain == CharacterProjectionDomain.None
            || ((int)domain & ((int)domain - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }
        if (!Enum.IsDefined(
                typeof(CharacterStateProvenance),
                provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(provenance));
        }
        if (!Enum.IsDefined(typeof(CharacterDomainConflict), conflict))
            throw new ArgumentOutOfRangeException(nameof(conflict));

        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProfileIdentity = profileIdentity
            ?? throw new ArgumentNullException(nameof(profileIdentity));
        State = state ?? throw new ArgumentNullException(nameof(state));
        ObservedAt = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ValidUntil = CharacterRuntimeContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (ValidUntil <= ObservedAt)
            throw new ArgumentOutOfRangeException(nameof(validUntil));

        Domain = domain;
        Provenance = provenance;
        Conflict = conflict;
        SourceFingerprint = CharacterRuntimeContractGuard.Identifier(
            sourceFingerprint,
            nameof(sourceFingerprint));
    }
}

internal sealed class AffectiveStateSnapshot
    : ICharacterStateDomainSnapshot
{
    internal int SchemaVersion { get; }
    internal double Mood { get; }
    internal double Energy { get; }
    internal double Irritation { get; }
    internal double SleepPressure { get; }
    internal double RecentInteractionPressure { get; }

    internal AffectiveStateSnapshot(
        int schemaVersion,
        double mood,
        double energy,
        double irritation,
        double sleepPressure,
        double recentInteractionPressure)
    {
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        Mood = CharacterRuntimeContractGuard.SignedUnit(
            mood,
            nameof(mood));
        Energy = CharacterRuntimeContractGuard.Unit(
            energy,
            nameof(energy));
        Irritation = CharacterRuntimeContractGuard.Unit(
            irritation,
            nameof(irritation));
        SleepPressure = CharacterRuntimeContractGuard.Unit(
            sleepPressure,
            nameof(sleepPressure));
        RecentInteractionPressure = CharacterRuntimeContractGuard.Unit(
            recentInteractionPressure,
            nameof(recentInteractionPressure));
    }
}

internal sealed class RelationshipStateSnapshot
    : ICharacterStateDomainSnapshot
{
    internal int SchemaVersion { get; }
    internal double Trust { get; }
    internal double Closeness { get; }
    internal double Familiarity { get; }
    internal double Security { get; }

    internal RelationshipStateSnapshot(
        int schemaVersion,
        double trust,
        double closeness,
        double familiarity,
        double security)
    {
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        Trust = CharacterRuntimeContractGuard.Unit(trust, nameof(trust));
        Closeness = CharacterRuntimeContractGuard.Unit(
            closeness,
            nameof(closeness));
        Familiarity = CharacterRuntimeContractGuard.Unit(
            familiarity,
            nameof(familiarity));
        Security = CharacterRuntimeContractGuard.Unit(
            security,
            nameof(security));
    }
}

[Flags]
internal enum CommunicationContextSignal
{
    None = 0,
    Gentle = 1 << 0,
    Firm = 1 << 1,
    Playful = 1 << 2,
    Impatient = 1 << 3,
    CandidatePressurePattern = 1 << 4,
    Ambiguous = 1 << 5
}

internal sealed class CommunicationContextSnapshot
    : ICharacterStateDomainSnapshot
{
    internal int SchemaVersion { get; }
    internal double Directness { get; }
    internal double Expressiveness { get; }
    internal CommunicationContextSignal Signals { get; }

    internal CommunicationContextSnapshot(
        int schemaVersion,
        double directness,
        double expressiveness,
        CommunicationContextSignal signals)
    {
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        Directness = CharacterRuntimeContractGuard.Unit(
            directness,
            nameof(directness));
        Expressiveness = CharacterRuntimeContractGuard.Unit(
            expressiveness,
            nameof(expressiveness));
        Signals = signals;
    }
}

internal sealed class OpposingIdentityAxisSnapshot
{
    internal double FirstPoleEvidence { get; }
    internal double SecondPoleEvidence { get; }
    internal double DefinitionStrength { get; }
    internal double Ambivalence { get; }

    internal OpposingIdentityAxisSnapshot(
        double firstPoleEvidence,
        double secondPoleEvidence)
    {
        FirstPoleEvidence = CharacterRuntimeContractGuard.Unit(
            firstPoleEvidence,
            nameof(firstPoleEvidence));
        SecondPoleEvidence = CharacterRuntimeContractGuard.Unit(
            secondPoleEvidence,
            nameof(secondPoleEvidence));
        DefinitionStrength = Math.Max(
            FirstPoleEvidence,
            SecondPoleEvidence);
        Ambivalence = Math.Min(
            FirstPoleEvidence,
            SecondPoleEvidence);
    }
}

internal sealed class PlayerAuthoredIdentitySnapshot
    : ICharacterStateDomainSnapshot
{
    internal int SchemaVersion { get; }
    internal int NarrativeKernelVersion { get; }
    internal OpposingIdentityAxisSnapshot Existence { get; }
    internal OpposingIdentityAxisSnapshot Agency { get; }
    internal OpposingIdentityAxisSnapshot World { get; }
    internal OpposingIdentityAxisSnapshot Relationship { get; }

    internal PlayerAuthoredIdentitySnapshot(
        int schemaVersion,
        int narrativeKernelVersion,
        OpposingIdentityAxisSnapshot existence,
        OpposingIdentityAxisSnapshot agency,
        OpposingIdentityAxisSnapshot world,
        OpposingIdentityAxisSnapshot relationship)
    {
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        NarrativeKernelVersion = CharacterRuntimeContractGuard.Positive(
            narrativeKernelVersion,
            nameof(narrativeKernelVersion));
        Existence = existence
            ?? throw new ArgumentNullException(nameof(existence));
        Agency = agency ?? throw new ArgumentNullException(nameof(agency));
        World = world ?? throw new ArgumentNullException(nameof(world));
        Relationship = relationship
            ?? throw new ArgumentNullException(nameof(relationship));
    }
}

internal enum CharacterClothingContext
{
    Unknown = 0,
    Standard = 1,
    Sleepwear = 2,
    AlternateSupported = 3
}

internal sealed class CharacterBodyAvailabilitySnapshot
    : ICharacterStateDomainSnapshot
{
    internal int SchemaVersion { get; }
    internal bool CharacterAvailable { get; }
    internal bool Grounded { get; }
    internal bool Sleeping { get; }
    internal bool DirectPlayerControl { get; }
    internal CharacterClothingContext Clothing { get; }

    internal CharacterBodyAvailabilitySnapshot(
        int schemaVersion,
        bool characterAvailable,
        bool grounded,
        bool sleeping,
        bool directPlayerControl,
        CharacterClothingContext clothing)
    {
        SchemaVersion = CharacterRuntimeContractGuard.Positive(
            schemaVersion,
            nameof(schemaVersion));
        if (!Enum.IsDefined(typeof(CharacterClothingContext), clothing))
            throw new ArgumentOutOfRangeException(nameof(clothing));
        CharacterAvailable = characterAvailable;
        Grounded = grounded;
        Sleeping = sleeping;
        DirectPlayerControl = directPlayerControl;
        Clothing = clothing;
    }
}

internal sealed class CharacterProjectionInput
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CoreIdentityProfile Profile { get; }
    internal CharacterStateEnvelope<AffectiveStateSnapshot> Affective {
        get;
    }
    internal CharacterStateEnvelope<RelationshipStateSnapshot>
        Relationship { get; }
    internal CharacterStateEnvelope<CommunicationContextSnapshot>
        Communication { get; }
    internal CharacterStateEnvelope<PlayerAuthoredIdentitySnapshot>
        PlayerAuthoredIdentity { get; }
    internal CharacterStateEnvelope<CharacterBodyAvailabilitySnapshot>
        BodyAvailability { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset AssembledAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal CharacterProjectionInput(
        CharacterRuntimeIdentity runtimeIdentity,
        CoreIdentityProfile profile,
        CharacterStateEnvelope<AffectiveStateSnapshot> affective,
        CharacterStateEnvelope<RelationshipStateSnapshot> relationship,
        CharacterStateEnvelope<CommunicationContextSnapshot>
            communication,
        CharacterStateEnvelope<PlayerAuthoredIdentitySnapshot>
            playerAuthoredIdentity,
        CharacterStateEnvelope<CharacterBodyAvailabilitySnapshot>
            bodyAvailability,
        long projectionRevision,
        long situationRevision,
        DateTimeOffset assembledAt,
        DateTimeOffset expiresAt)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Affective = affective
            ?? throw new ArgumentNullException(nameof(affective));
        Relationship = relationship
            ?? throw new ArgumentNullException(nameof(relationship));
        Communication = communication
            ?? throw new ArgumentNullException(nameof(communication));
        PlayerAuthoredIdentity = playerAuthoredIdentity
            ?? throw new ArgumentNullException(
                nameof(playerAuthoredIdentity));
        BodyAvailability = bodyAvailability
            ?? throw new ArgumentNullException(nameof(bodyAvailability));
        ProjectionRevision = CharacterRuntimeContractGuard.Positive(
            projectionRevision,
            nameof(projectionRevision));
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        AssembledAt = CharacterRuntimeContractGuard.Timestamp(
            assembledAt,
            nameof(assembledAt));
        ExpiresAt = CharacterRuntimeContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= AssembledAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
    }
}

internal enum CharacterProjectionAdmission
{
    Accepted = 0,
    RuntimeIdentityMismatch = 1,
    RuntimeGenerationMismatch = 2,
    ProfileIdentityMismatch = 3,
    DomainMismatch = 4,
    FutureObservation = 5,
    StaleDomain = 6,
    CriticalConflict = 7,
    ProjectionExpired = 8,
    MissingDomain = 9
}

internal sealed class CharacterSituationProjection
{
    internal string ProjectionFingerprint { get; }
    internal string SemanticProjectionFingerprint { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CoreIdentityProfile Profile { get; }
    internal CharacterStateEnvelope<AffectiveStateSnapshot> Affective {
        get;
    }
    internal CharacterStateEnvelope<RelationshipStateSnapshot>
        Relationship { get; }
    internal CharacterStateEnvelope<CommunicationContextSnapshot>
        Communication { get; }
    internal CharacterStateEnvelope<PlayerAuthoredIdentitySnapshot>
        PlayerAuthoredIdentity { get; }
    internal CharacterStateEnvelope<CharacterBodyAvailabilitySnapshot>
        BodyAvailability { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset AssembledAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal CharacterSituationProjection(
        string projectionFingerprint,
        string semanticProjectionFingerprint,
        CharacterProjectionInput input,
        CharacterProjectionDomain ambiguousDomains)
    {
        ProjectionFingerprint =
            CharacterRuntimeContractGuard.Identifier(
                projectionFingerprint,
                nameof(projectionFingerprint));
        SemanticProjectionFingerprint =
            CharacterRuntimeContractGuard.Identifier(
                semanticProjectionFingerprint,
                nameof(semanticProjectionFingerprint));
        RuntimeIdentity = input.RuntimeIdentity;
        Profile = input.Profile;
        Affective = input.Affective;
        Relationship = input.Relationship;
        Communication = input.Communication;
        PlayerAuthoredIdentity = input.PlayerAuthoredIdentity;
        BodyAvailability = input.BodyAvailability;
        AmbiguousDomains = ambiguousDomains;
        ProjectionRevision = input.ProjectionRevision;
        SituationRevision = input.SituationRevision;
        AssembledAt = input.AssembledAt;
        ExpiresAt = input.ExpiresAt;
    }
}

internal sealed class CharacterProjectionResult
{
    internal CharacterProjectionAdmission Admission { get; }
    internal CharacterSituationProjection? Projection { get; }
    internal CharacterProjectionDomain AffectedDomain { get; }
    internal bool Accepted =>
        Admission == CharacterProjectionAdmission.Accepted;

    internal CharacterProjectionResult(
        CharacterProjectionAdmission admission,
        CharacterSituationProjection? projection,
        CharacterProjectionDomain affectedDomain)
    {
        if (admission == CharacterProjectionAdmission.Accepted
            && projection == null)
        {
            throw new ArgumentNullException(nameof(projection));
        }
        if (admission != CharacterProjectionAdmission.Accepted
            && projection != null)
        {
            throw new ArgumentException(
                "Rejected projection cannot carry a projection.",
                nameof(projection));
        }

        Admission = admission;
        Projection = projection;
        AffectedDomain = affectedDomain;
    }
}

internal static class CharacterSituationProjector
{
    internal static CharacterProjectionResult Project(
        CharacterProjectionInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (input.ExpiresAt <= input.AssembledAt)
        {
            return Reject(
                CharacterProjectionAdmission.ProjectionExpired,
                CharacterProjectionDomain.None);
        }
        if (!string.Equals(
                input.RuntimeIdentity.RuntimeProfileId,
                input.Profile.Identity.ProfileId,
                StringComparison.Ordinal))
        {
            return Reject(
                CharacterProjectionAdmission.ProfileIdentityMismatch,
                CharacterProjectionDomain.None);
        }

        var envelopes = new IEnvelopeView[]
        {
            View(input.Affective),
            View(input.Relationship),
            View(input.Communication),
            View(input.PlayerAuthoredIdentity),
            View(input.BodyAvailability)
        };

        var expectedDomains = new[]
        {
            CharacterProjectionDomain.Affective,
            CharacterProjectionDomain.Relationship,
            CharacterProjectionDomain.Communication,
            CharacterProjectionDomain.PlayerAuthoredIdentity,
            CharacterProjectionDomain.BodyAvailability
        };
        for (var index = 0; index < envelopes.Length; index++)
        {
            var envelope = envelopes[index];
            var expectedDomain = expectedDomains[index];
            if (envelope.Domain != expectedDomain)
            {
                return Reject(
                    CharacterProjectionAdmission.DomainMismatch,
                    expectedDomain);
            }
            if (envelope.RuntimeIdentity.Generation
                != input.RuntimeIdentity.Generation)
            {
                return Reject(
                    CharacterProjectionAdmission
                        .RuntimeGenerationMismatch,
                    expectedDomain);
            }
            if (!input.RuntimeIdentity.Matches(
                    envelope.RuntimeIdentity))
            {
                return Reject(
                    CharacterProjectionAdmission.RuntimeIdentityMismatch,
                    expectedDomain);
            }
            if (!input.Profile.Identity.Matches(
                    envelope.ProfileIdentity))
            {
                return Reject(
                    CharacterProjectionAdmission.ProfileIdentityMismatch,
                    expectedDomain);
            }
            if (envelope.ObservedAt > input.AssembledAt)
            {
                return Reject(
                    CharacterProjectionAdmission.FutureObservation,
                    expectedDomain);
            }
            if (envelope.ValidUntil <= input.AssembledAt)
            {
                return Reject(
                    CharacterProjectionAdmission.StaleDomain,
                    expectedDomain);
            }
            if (envelope.Conflict == CharacterDomainConflict.Critical)
            {
                return Reject(
                    CharacterProjectionAdmission.CriticalConflict,
                    expectedDomain);
            }
        }

        var ambiguousDomains = envelopes
            .Where(
                envelope =>
                    envelope.Conflict
                    == CharacterDomainConflict.Ambiguous)
            .Aggregate(
                CharacterProjectionDomain.None,
                (current, envelope) => current | envelope.Domain);
        var fingerprint = Fingerprint(input, envelopes);
        var semanticFingerprint = SemanticFingerprint(input, envelopes);
        return new CharacterProjectionResult(
            CharacterProjectionAdmission.Accepted,
            new CharacterSituationProjection(
                fingerprint,
                semanticFingerprint,
                input,
                ambiguousDomains),
            ambiguousDomains);
    }

    private static CharacterProjectionResult Reject(
        CharacterProjectionAdmission admission,
        CharacterProjectionDomain domain)
        => new(admission, null, domain);

    private static IEnvelopeView View<T>(
        CharacterStateEnvelope<T> envelope)
        where T : class, ICharacterStateDomainSnapshot
        => new EnvelopeView<T>(envelope);

    private static string Fingerprint(
        CharacterProjectionInput input,
        IEnumerable<IEnvelopeView> envelopes)
    {
        var builder = new StringBuilder();
        Append(builder, input.RuntimeIdentity.RuntimeProfileId);
        Append(builder, input.RuntimeIdentity.RuntimeInstanceId);
        Append(builder, input.RuntimeIdentity.Generation);
        Append(builder, input.RuntimeIdentity.OrchestratorSessionId);
        Append(builder, input.RuntimeIdentity.OwnerIdentity);
        Append(builder, input.Profile.Identity.ProfileId);
        Append(builder, input.Profile.Identity.ProfileVersion);
        Append(builder, input.Profile.Identity.SchemaVersion);
        Append(builder, input.Profile.Identity.ContentHash);
        Append(builder, input.ProjectionRevision);
        Append(builder, input.SituationRevision);
        Append(builder, input.AssembledAt.UtcTicks);
        Append(builder, input.ExpiresAt.UtcTicks);
        foreach (var envelope in envelopes)
        {
            Append(builder, (int)envelope.Domain);
            Append(builder, envelope.ObservedAt.UtcTicks);
            Append(builder, envelope.ValidUntil.UtcTicks);
            Append(builder, (int)envelope.Provenance);
            Append(builder, (int)envelope.Conflict);
            Append(builder, envelope.SourceFingerprint);
            Append(builder, envelope.StateCanonicalValue);
        }

        return Hash(builder);
    }

    private static string SemanticFingerprint(
        CharacterProjectionInput input,
        IEnumerable<IEnvelopeView> envelopes)
    {
        var builder = new StringBuilder();
        Append(builder, input.RuntimeIdentity.RuntimeProfileId);
        Append(builder, input.RuntimeIdentity.RuntimeInstanceId);
        Append(builder, input.RuntimeIdentity.Generation);
        Append(builder, input.RuntimeIdentity.OrchestratorSessionId);
        Append(builder, input.RuntimeIdentity.OwnerIdentity);
        Append(builder, input.Profile.Identity.ProfileId);
        Append(builder, input.Profile.Identity.ProfileVersion);
        Append(builder, input.Profile.Identity.SchemaVersion);
        Append(builder, input.Profile.Identity.ContentHash);
        Append(
            builder,
            (int)input.Profile.NarrativeInvariants);
        Append(builder, (int)input.Profile.Boundaries);
        Append(builder, CoreIdentityProfileCanonicalizer.Canonicalize(
            input.Profile));
        foreach (var envelope in envelopes)
        {
            Append(builder, (int)envelope.Domain);
            Append(builder, (int)envelope.Conflict);
            Append(builder, envelope.StateCanonicalValue);
        }

        return Hash(builder);
    }

    private static string Hash(StringBuilder builder)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }

    private static void Append(
        StringBuilder builder,
        object value)
    {
        var text = value switch
        {
            IFormattable formattable => formattable.ToString(
                null,
                CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        builder.Append(text?.Length ?? 0)
            .Append(':')
            .Append(text)
            .Append('|');
    }

    private interface IEnvelopeView
    {
        CharacterProjectionDomain Domain { get; }
        CharacterRuntimeIdentity RuntimeIdentity { get; }
        CharacterProfileIdentity ProfileIdentity { get; }
        DateTimeOffset ObservedAt { get; }
        DateTimeOffset ValidUntil { get; }
        CharacterStateProvenance Provenance { get; }
        CharacterDomainConflict Conflict { get; }
        string SourceFingerprint { get; }
        string StateCanonicalValue { get; }
    }

    private sealed class EnvelopeView<T> : IEnvelopeView
        where T : class, ICharacterStateDomainSnapshot
    {
        private readonly CharacterStateEnvelope<T> _envelope;

        internal EnvelopeView(CharacterStateEnvelope<T> envelope)
        {
            _envelope = envelope;
        }

        public CharacterProjectionDomain Domain => _envelope.Domain;
        public CharacterRuntimeIdentity RuntimeIdentity =>
            _envelope.RuntimeIdentity;
        public CharacterProfileIdentity ProfileIdentity =>
            _envelope.ProfileIdentity;
        public DateTimeOffset ObservedAt => _envelope.ObservedAt;
        public DateTimeOffset ValidUntil => _envelope.ValidUntil;
        public CharacterStateProvenance Provenance =>
            _envelope.Provenance;
        public CharacterDomainConflict Conflict => _envelope.Conflict;
        public string SourceFingerprint =>
            _envelope.SourceFingerprint;
        public string StateCanonicalValue =>
            CharacterStateCanonicalizer.Canonicalize(_envelope.State);
    }
}

internal static class CoreIdentityProfileCanonicalizer
{
    internal static string Canonicalize(CoreIdentityProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        return string.Join(
            ",",
            CoreIdentityTraitVector.DimensionSetVersion,
            Number(profile.StableTraits.Warmth),
            Number(profile.StableTraits.Autonomy),
            Number(profile.StableTraits.Sensitivity),
            Number(profile.StableTraits.Stubbornness),
            Number(profile.StableTraits.Forgiveness),
            CharacterExpressionTendencyVector.DimensionSetVersion,
            Number(profile.ExpressionTendencies.Warmth),
            Number(profile.ExpressionTendencies.Restraint),
            Number(profile.ExpressionTendencies.Directness),
            Number(profile.ExpressionTendencies.Expressiveness),
            Number(profile.ExpressionTendencies.Brevity));
    }

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

internal static class CharacterStateCanonicalizer
{
    internal static string Canonicalize(
        ICharacterStateDomainSnapshot state)
        => state switch
        {
            AffectiveStateSnapshot value => string.Join(
                ",",
                value.SchemaVersion,
                Number(value.Mood),
                Number(value.Energy),
                Number(value.Irritation),
                Number(value.SleepPressure),
                Number(value.RecentInteractionPressure)),
            RelationshipStateSnapshot value => string.Join(
                ",",
                value.SchemaVersion,
                Number(value.Trust),
                Number(value.Closeness),
                Number(value.Familiarity),
                Number(value.Security)),
            CommunicationContextSnapshot value => string.Join(
                ",",
                value.SchemaVersion,
                Number(value.Directness),
                Number(value.Expressiveness),
                (int)value.Signals),
            PlayerAuthoredIdentitySnapshot value => string.Join(
                ",",
                value.SchemaVersion,
                value.NarrativeKernelVersion,
                Axis(value.Existence),
                Axis(value.Agency),
                Axis(value.World),
                Axis(value.Relationship)),
            CharacterBodyAvailabilitySnapshot value => string.Join(
                ",",
                value.SchemaVersion,
                value.CharacterAvailable,
                value.Grounded,
                value.Sleeping,
                value.DirectPlayerControl,
                (int)value.Clothing),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private static string Axis(OpposingIdentityAxisSnapshot value)
        => string.Join(
            ":",
            Number(value.FirstPoleEvidence),
            Number(value.SecondPoleEvidence),
            Number(value.DefinitionStrength),
            Number(value.Ambivalence));

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}

[Flags]
internal enum CharacterInfluenceKind
{
    None = 0,
    SamePriorityAttentionTieBreak = 1 << 0,
    SamePriorityUrgencyReduction = 1 << 1,
    ExpressionDimensionPreview = 1 << 2
}

internal sealed class CharacterInfluenceEnvelope
{
    internal string EnvelopeId { get; }
    internal CharacterInfluenceMode Mode { get; }
    internal CharacterInfluenceKind AllowedKinds { get; }
    internal double MaximumUrgencyReduction { get; }
    internal TimeSpan MaximumAttentionPersistenceAdjustment { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal CharacterInfluenceEnvelope(
        string envelopeId,
        CharacterInfluenceMode mode,
        CharacterInfluenceKind allowedKinds,
        double maximumUrgencyReduction,
        TimeSpan maximumAttentionPersistenceAdjustment,
        CharacterRuntimeIdentity runtimeIdentity,
        long situationRevision,
        DateTimeOffset expiresAt)
    {
        EnvelopeId = CharacterRuntimeContractGuard.Identifier(
            envelopeId,
            nameof(envelopeId));
        if (!Enum.IsDefined(typeof(CharacterInfluenceMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumAttentionPersistenceAdjustment < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAttentionPersistenceAdjustment));
        }

        Mode = mode;
        AllowedKinds = allowedKinds;
        MaximumUrgencyReduction = CharacterRuntimeContractGuard.Unit(
            maximumUrgencyReduction,
            nameof(maximumUrgencyReduction));
        MaximumAttentionPersistenceAdjustment =
            maximumAttentionPersistenceAdjustment;
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        ExpiresAt = CharacterRuntimeContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
    }
}

internal enum CharacterContinuityNeed
{
    PreserveSilence = 0,
    PreserveRest = 1,
    BriefAcknowledgement = 2,
    RequestSpace = 3,
    IdleExpression = 4
}

internal sealed class CharacterContinuityCandidate
{
    internal string CandidateId { get; }
    internal string Fingerprint { get; }
    internal CharacterContinuityNeed Need { get; }
    internal int InterruptionCost { get; }
    internal int AttentionCost { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long SituationRevision { get; }
    internal bool RequiresSeparateAutonomyPolicy { get; }

    internal CharacterContinuityCandidate(
        string candidateId,
        string fingerprint,
        CharacterContinuityNeed need,
        int interruptionCost,
        int attentionCost,
        DateTimeOffset expiresAt,
        CharacterRuntimeIdentity runtimeIdentity,
        long situationRevision,
        bool requiresSeparateAutonomyPolicy)
    {
        CandidateId = CharacterRuntimeContractGuard.Identifier(
            candidateId,
            nameof(candidateId));
        Fingerprint = CharacterRuntimeContractGuard.Identifier(
            fingerprint,
            nameof(fingerprint));
        if (!Enum.IsDefined(typeof(CharacterContinuityNeed), need))
            throw new ArgumentOutOfRangeException(nameof(need));
        if (interruptionCost < 0 || interruptionCost > 100)
            throw new ArgumentOutOfRangeException(nameof(interruptionCost));
        if (attentionCost < 0 || attentionCost > 100)
            throw new ArgumentOutOfRangeException(nameof(attentionCost));
        Need = need;
        InterruptionCost = interruptionCost;
        AttentionCost = attentionCost;
        ExpiresAt = CharacterRuntimeContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        RequiresSeparateAutonomyPolicy =
            requiresSeparateAutonomyPolicy;
    }
}

internal enum CharacterExpressionCapability
{
    PlayerTriggeredResponse = 0,
    Motion = 1,
    Voice = 2,
    NonIntervention = 3
}

internal enum CharacterExpressionTargetKind
{
    Player = 0,
    Character = 1,
    Surface = 2,
    NonIntervention = 3
}

internal enum NonInterventionTarget
{
    RemainSilent = 0,
    DeferEngagement = 1
}

internal enum CharacterConsentState
{
    NotApplicable = 0,
    Satisfied = 1,
    RequiredAndUnsatisfied = 2
}

internal enum CharacterBehaviorIntensity
{
    Minimal = 0,
    Low = 1,
    Moderate = 2
}

internal sealed class CharacterExpressionTarget
{
    internal CharacterExpressionTargetKind Kind { get; }
    internal string TargetId { get; }
    internal NonInterventionTarget? NonIntervention { get; }

    internal CharacterExpressionTarget(
        CharacterExpressionTargetKind kind,
        string targetId)
    {
        if (!Enum.IsDefined(typeof(CharacterExpressionTargetKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == CharacterExpressionTargetKind.NonIntervention)
        {
            throw new ArgumentException(
                "A NonIntervention target must use its typed constructor.",
                nameof(kind));
        }
        Kind = kind;
        TargetId = CharacterRuntimeContractGuard.Identifier(
            targetId,
            nameof(targetId));
        NonIntervention = null;
    }

    internal CharacterExpressionTarget(
        NonInterventionTarget nonIntervention)
    {
        if (!Enum.IsDefined(
                typeof(NonInterventionTarget),
                nonIntervention))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nonIntervention));
        }

        Kind = CharacterExpressionTargetKind.NonIntervention;
        NonIntervention = nonIntervention;
        TargetId = nonIntervention switch
        {
            NonInterventionTarget.RemainSilent => "remain-silent",
            NonInterventionTarget.DeferEngagement =>
                "defer-engagement",
            _ => throw new ArgumentOutOfRangeException(
                nameof(nonIntervention))
        };
    }

    internal bool Matches(CharacterExpressionTarget? other)
        => other != null
            && Kind == other.Kind
            && NonIntervention == other.NonIntervention
            && string.Equals(
                TargetId,
                other.TargetId,
                StringComparison.Ordinal);
}

internal sealed class CharacterExpressionSafetyEnvelope
{
    internal string SafetyEnvelopeId { get; }
    internal CharacterConsentState ConsentState { get; }
    internal CharacterBehaviorIntensity BehaviorIntensity { get; }
    internal bool AllowsCapabilityChange => false;
    internal bool AllowsSemanticCommitmentChange => false;
    internal bool AllowsTargetChange => false;
    internal bool AllowsConsentChange => false;
    internal bool AllowsBehaviorIntensityChange => false;

    internal CharacterExpressionSafetyEnvelope(
        string safetyEnvelopeId,
        CharacterConsentState consentState,
        CharacterBehaviorIntensity behaviorIntensity)
    {
        SafetyEnvelopeId = CharacterRuntimeContractGuard.Identifier(
            safetyEnvelopeId,
            nameof(safetyEnvelopeId));
        if (!Enum.IsDefined(
                typeof(CharacterConsentState),
                consentState))
        {
            throw new ArgumentOutOfRangeException(nameof(consentState));
        }
        if (!Enum.IsDefined(
                typeof(CharacterBehaviorIntensity),
                behaviorIntensity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(behaviorIntensity));
        }

        ConsentState = consentState;
        BehaviorIntensity = behaviorIntensity;
    }

    internal bool Matches(CharacterExpressionSafetyEnvelope? other)
        => other != null
            && string.Equals(
                SafetyEnvelopeId,
                other.SafetyEnvelopeId,
                StringComparison.Ordinal)
            && ConsentState == other.ConsentState
            && BehaviorIntensity == other.BehaviorIntensity;
}

internal sealed class EquivalentExpressionSet
{
    internal string SetId { get; }
    internal CharacterExpressionCapability Capability { get; }
    internal CharacterExpressionTarget Target { get; }
    internal string SemanticCommitmentHash { get; }
    internal CharacterConsentState ConsentState { get; }
    internal CharacterBehaviorIntensity BehaviorIntensity { get; }
    internal IReadOnlyList<string> SemanticVariantIds { get; }

    internal EquivalentExpressionSet(
        string setId,
        CharacterExpressionCapability capability,
        CharacterExpressionTarget target,
        string semanticCommitmentHash,
        CharacterConsentState consentState,
        CharacterBehaviorIntensity behaviorIntensity,
        IEnumerable<string> semanticVariantIds)
    {
        SetId = CharacterRuntimeContractGuard.Identifier(
            setId,
            nameof(setId));
        if (!Enum.IsDefined(
                typeof(CharacterExpressionCapability),
                capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability));
        }
        if (!Enum.IsDefined(
                typeof(CharacterConsentState),
                consentState))
        {
            throw new ArgumentOutOfRangeException(nameof(consentState));
        }
        if (!Enum.IsDefined(
                typeof(CharacterBehaviorIntensity),
                behaviorIntensity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(behaviorIntensity));
        }

        var variants = (semanticVariantIds
                ?? throw new ArgumentNullException(
                    nameof(semanticVariantIds)))
            .Select(
                value => CharacterRuntimeContractGuard.Identifier(
                    value,
                    nameof(semanticVariantIds)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (variants.Length == 0 || variants.Length > 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(semanticVariantIds));
        }

        Capability = capability;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        var hasTypedNonInterventionTarget =
            Target.Kind == CharacterExpressionTargetKind.NonIntervention
            && Target.NonIntervention.HasValue;
        if ((Capability
                == CharacterExpressionCapability.NonIntervention)
            != hasTypedNonInterventionTarget)
        {
            throw new ArgumentException(
                "NonIntervention capability and typed target must be "
                + "present together.",
                nameof(target));
        }
        SemanticCommitmentHash =
            CharacterRuntimeContractGuard.Identifier(
                semanticCommitmentHash,
                nameof(semanticCommitmentHash));
        ConsentState = consentState;
        BehaviorIntensity = behaviorIntensity;
        SemanticVariantIds = variants;
    }
}

internal sealed class ExpressionIntent
{
    internal string IntentId { get; }
    internal Guid DecisionId { get; }
    internal string? GoalId { get; }
    internal DecisionDisposition Disposition { get; }
    internal DecisionReason Reason { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterExpressionCapability Capability { get; }
    internal CharacterExpressionTarget Target { get; }
    internal string SemanticCommitmentHash { get; }
    internal CharacterExpressionSafetyEnvelope SafetyEnvelope { get; }
    internal EquivalentExpressionSet EquivalentExpressions { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool MetadataOnly => true;
    internal bool Executable => false;
    internal bool Applied => false;
    internal bool AllowsReplyOutput => false;
    internal bool AllowsPromptOutput => false;
    internal bool AllowsNotificationOutput => false;
    internal bool AllowsFacialExpressionOutput => false;
    internal bool AllowsMotionOutput => false;
    internal bool AllowsAnimationOutput => false;
    internal bool AllowsVoiceOutput => false;
    internal bool AllowsQueueOutput => false;
    internal bool AllowsCapabilityOutput => false;

    internal ExpressionIntent(
        string intentId,
        Guid decisionId,
        string? goalId,
        DecisionDisposition disposition,
        DecisionReason reason,
        Guid situationId,
        long situationRevision,
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterExpressionCapability capability,
        CharacterExpressionTarget target,
        string semanticCommitmentHash,
        CharacterExpressionSafetyEnvelope safetyEnvelope,
        EquivalentExpressionSet equivalentExpressions,
        DateTimeOffset validUntil)
    {
        IntentId = CharacterRuntimeContractGuard.Identifier(
            intentId,
            nameof(intentId));
        if (decisionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(decisionId));
        if (situationId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(situationId));
        if (!Enum.IsDefined(
                typeof(CharacterExpressionCapability),
                capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability));
        }
        if (!Enum.IsDefined(typeof(DecisionDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (!Enum.IsDefined(typeof(DecisionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        DecisionId = decisionId;
        GoalId = goalId == null
            ? null
            : CharacterRuntimeContractGuard.Identifier(
                goalId,
                nameof(goalId));
        Disposition = disposition;
        Reason = reason;
        SituationId = situationId;
        SituationRevision = CharacterRuntimeContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        Capability = capability;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        SemanticCommitmentHash =
            CharacterRuntimeContractGuard.Identifier(
                semanticCommitmentHash,
                nameof(semanticCommitmentHash));
        SafetyEnvelope = safetyEnvelope
            ?? throw new ArgumentNullException(nameof(safetyEnvelope));
        EquivalentExpressions = equivalentExpressions
            ?? throw new ArgumentNullException(
                nameof(equivalentExpressions));
        ValidUntil = CharacterRuntimeContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));

        if (EquivalentExpressions.Capability != Capability
            || !EquivalentExpressions.Target.Matches(Target)
            || !string.Equals(
                EquivalentExpressions.SemanticCommitmentHash,
                SemanticCommitmentHash,
                StringComparison.Ordinal)
            || EquivalentExpressions.ConsentState
                != SafetyEnvelope.ConsentState
            || EquivalentExpressions.BehaviorIntensity
                != SafetyEnvelope.BehaviorIntensity)
        {
            throw new ArgumentException(
                "Equivalent expression set must preserve the exact "
                + "capability, target, semantic commitment, consent "
                + "and behavior intensity.",
                nameof(equivalentExpressions));
        }

        var isNonIntervention =
            Capability == CharacterExpressionCapability.NonIntervention;
        var isNonInterventionDisposition =
            Disposition is DecisionDisposition.Silence
                or DecisionDisposition.Defer;
        if (isNonIntervention != isNonInterventionDisposition)
        {
            throw new ArgumentException(
                "Silence and Defer must remain inside the typed "
                + "NonIntervention boundary, and that boundary cannot "
                + "represent an intervention disposition.",
                nameof(disposition));
        }
        if (!isNonIntervention && GoalId == null)
        {
            throw new ArgumentException(
                "An intervention expression intent must preserve its "
                + "accepted Goal.",
                nameof(goalId));
        }
        if (isNonIntervention
            && ((Disposition == DecisionDisposition.Silence
                    && Target.NonIntervention
                        != NonInterventionTarget.RemainSilent)
                || (Disposition == DecisionDisposition.Defer
                    && Target.NonIntervention
                        != NonInterventionTarget.DeferEngagement)))
        {
            throw new ArgumentException(
                "Silence and Defer require distinct typed "
                + "NonIntervention targets.",
                nameof(target));
        }
    }
}

internal sealed class CharacterRuntimeStageAAuthorityCounters
{
    internal static CharacterRuntimeStageAAuthorityCounters Zero {
        get;
    } = new();

    internal int DecisionOutputs => 0;
    internal int ContinuityCandidatesProduced => 0;
    internal int ExpressionOutputs => 0;
    internal int ProviderCalls => 0;
    internal int MemoryCalls => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        DecisionOutputs == 0
        && ContinuityCandidatesProduced == 0
        && ExpressionOutputs == 0
        && ProviderCalls == 0
        && MemoryCalls == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && PersistenceWrites == 0
        && ExecutionOutputs == 0;

    private CharacterRuntimeStageAAuthorityCounters()
    {
    }
}
