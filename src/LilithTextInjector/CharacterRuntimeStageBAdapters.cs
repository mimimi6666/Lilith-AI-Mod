using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterDomainPresence
{
    Present = 0,
    Missing = 1
}

internal enum CharacterDomainMissingReason
{
    None = 0,
    SourceUnavailable = 1
}

internal sealed class CharacterProjectionAdapterContext
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterProfileIdentity ProfileIdentity { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal CharacterStateProvenance Provenance { get; }
    internal CharacterDomainConflict Conflict { get; }

    internal CharacterProjectionAdapterContext(
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterProfileIdentity profileIdentity,
        DateTimeOffset validUntil,
        CharacterStateProvenance provenance,
        CharacterDomainConflict conflict)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProfileIdentity = profileIdentity
            ?? throw new ArgumentNullException(nameof(profileIdentity));
        ValidUntil = CharacterRuntimeContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        if (!Enum.IsDefined(
                typeof(CharacterStateProvenance),
                provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(provenance));
        }
        if (!Enum.IsDefined(typeof(CharacterDomainConflict), conflict))
            throw new ArgumentOutOfRangeException(nameof(conflict));

        Provenance = provenance;
        Conflict = conflict;
    }
}

internal sealed class CharacterProjectionDomainSource<T>
    where T : class, ICharacterStateDomainSnapshot
{
    internal CharacterProjectionDomain Domain { get; }
    internal CharacterDomainPresence Presence { get; }
    internal CharacterDomainMissingReason MissingReason { get; }
    internal CharacterStateEnvelope<T>? Envelope { get; }

    private CharacterProjectionDomainSource(
        CharacterProjectionDomain domain,
        CharacterDomainPresence presence,
        CharacterDomainMissingReason missingReason,
        CharacterStateEnvelope<T>? envelope)
    {
        if (domain == CharacterProjectionDomain.None
            || ((int)domain & ((int)domain - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }
        if (!Enum.IsDefined(typeof(CharacterDomainPresence), presence))
            throw new ArgumentOutOfRangeException(nameof(presence));
        if (!Enum.IsDefined(
                typeof(CharacterDomainMissingReason),
                missingReason))
        {
            throw new ArgumentOutOfRangeException(nameof(missingReason));
        }
        if (presence == CharacterDomainPresence.Present)
        {
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));
            if (missingReason != CharacterDomainMissingReason.None)
                throw new ArgumentOutOfRangeException(nameof(missingReason));
            if (envelope.Domain != domain)
                throw new ArgumentException(
                    "Envelope domain must match the source domain.",
                    nameof(envelope));
        }
        else
        {
            if (envelope != null)
            {
                throw new ArgumentException(
                    "Missing source cannot carry an envelope.",
                    nameof(envelope));
            }
            if (missingReason == CharacterDomainMissingReason.None)
                throw new ArgumentOutOfRangeException(nameof(missingReason));
        }

        Domain = domain;
        Presence = presence;
        MissingReason = missingReason;
        Envelope = envelope;
    }

    internal static CharacterProjectionDomainSource<T> Present(
        CharacterStateEnvelope<T> envelope)
        => new(
            envelope?.Domain
                ?? throw new ArgumentNullException(nameof(envelope)),
            CharacterDomainPresence.Present,
            CharacterDomainMissingReason.None,
            envelope);

    internal static CharacterProjectionDomainSource<T> Missing(
        CharacterProjectionDomain domain)
        => new(
            domain,
            CharacterDomainPresence.Missing,
            CharacterDomainMissingReason.SourceUnavailable,
            null);
}

internal sealed class CharacterPsychologyProjectionSources
{
    internal CharacterProjectionDomainSource<AffectiveStateSnapshot>
        Affective { get; }
    internal CharacterProjectionDomainSource<RelationshipStateSnapshot>
        Relationship { get; }

    internal CharacterPsychologyProjectionSources(
        CharacterProjectionDomainSource<AffectiveStateSnapshot> affective,
        CharacterProjectionDomainSource<RelationshipStateSnapshot>
            relationship)
    {
        Affective = affective
            ?? throw new ArgumentNullException(nameof(affective));
        Relationship = relationship
            ?? throw new ArgumentNullException(nameof(relationship));
    }
}

internal static class CharacterPsychologyProjectionAdapter
{
    internal static CharacterPsychologyProjectionSources Adapt(
        CharacterPsychologyState? source,
        CharacterProjectionAdapterContext affectiveContext,
        CharacterProjectionAdapterContext relationshipContext)
    {
        if (affectiveContext == null)
        {
            throw new ArgumentNullException(nameof(affectiveContext));
        }
        if (relationshipContext == null)
        {
            throw new ArgumentNullException(nameof(relationshipContext));
        }
        if (source == null)
        {
            return new CharacterPsychologyProjectionSources(
                CharacterProjectionDomainSource<AffectiveStateSnapshot>
                    .Missing(CharacterProjectionDomain.Affective),
                CharacterProjectionDomainSource<RelationshipStateSnapshot>
                    .Missing(CharacterProjectionDomain.Relationship));
        }

        var affective = new AffectiveStateSnapshot(
            source.SchemaVersion,
            source.Mood,
            source.Energy,
            source.Irritation,
            source.SleepPressure,
            source.WakeRequestPressure);
        var relationship = new RelationshipStateSnapshot(
            source.SchemaVersion,
            source.Trust,
            source.Closeness,
            source.Familiarity,
            source.Security);

        return new CharacterPsychologyProjectionSources(
            CharacterProjectionDomainSource<AffectiveStateSnapshot>
                .Present(
                    Envelope(
                        CharacterProjectionDomain.Affective,
                        affective,
                        source.UpdatedAt,
                        affectiveContext,
                        V84CharacterStateFingerprint.Affective(source))),
            CharacterProjectionDomainSource<RelationshipStateSnapshot>
                .Present(
                    Envelope(
                        CharacterProjectionDomain.Relationship,
                        relationship,
                        source.UpdatedAt,
                        relationshipContext,
                        V84CharacterStateFingerprint.Relationship(source))));
    }

    private static CharacterStateEnvelope<T> Envelope<T>(
        CharacterProjectionDomain domain,
        T state,
        DateTimeOffset observedAt,
        CharacterProjectionAdapterContext context,
        string sourceFingerprint)
        where T : class, ICharacterStateDomainSnapshot
        => new(
            domain,
            context.RuntimeIdentity,
            context.ProfileIdentity,
            state,
            observedAt,
            context.ValidUntil,
            context.Provenance,
            context.Conflict,
            sourceFingerprint);
}

internal static class CommunicationContextProjectionAdapter
{
    internal static CharacterProjectionDomainSource<
        CommunicationContextSnapshot> Adapt(
        CommunicationPatternState? source,
        CharacterProjectionAdapterContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (source == null)
        {
            return CharacterProjectionDomainSource<
                CommunicationContextSnapshot>.Missing(
                    CharacterProjectionDomain.Communication);
        }

        var signals = Enum.GetValues<CommunicationStrategySignal>()
            .Any(
                signal =>
                    source.GetPatternLevel(signal)
                    != CommunicationPatternLevel.None)
            ? CommunicationContextSignal.CandidatePressurePattern
            : CommunicationContextSignal.None;
        if (context.Conflict == CharacterDomainConflict.Ambiguous)
            signals |= CommunicationContextSignal.Ambiguous;

        var state = new CommunicationContextSnapshot(
            source.SchemaVersion,
            source.BaselineDirectness,
            source.BaselineExpressiveness,
            signals);
        var envelope = new CharacterStateEnvelope<
            CommunicationContextSnapshot>(
            CharacterProjectionDomain.Communication,
            context.RuntimeIdentity,
            context.ProfileIdentity,
            state,
            source.UpdatedAt,
            context.ValidUntil,
            context.Provenance,
            context.Conflict,
            V84CharacterStateFingerprint.Communication(source));
        return CharacterProjectionDomainSource<
            CommunicationContextSnapshot>.Present(envelope);
    }
}

internal static class PlayerAuthoredIdentityProjectionAdapter
{
    internal static CharacterProjectionDomainSource<
        PlayerAuthoredIdentitySnapshot> Adapt(
        PlayerAuthoredIdentityState? source,
        CharacterProjectionAdapterContext context,
        int narrativeKernelVersion)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        CharacterRuntimeContractGuard.Positive(
            narrativeKernelVersion,
            nameof(narrativeKernelVersion));
        if (source == null)
        {
            return CharacterProjectionDomainSource<
                PlayerAuthoredIdentitySnapshot>.Missing(
                    CharacterProjectionDomain.PlayerAuthoredIdentity);
        }

        var state = new PlayerAuthoredIdentitySnapshot(
            source.SchemaVersion,
            narrativeKernelVersion,
            new OpposingIdentityAxisSnapshot(
                source.ExistenceAffirmed,
                source.ExistenceDenied),
            new OpposingIdentityAxisSnapshot(
                source.SelfDirectionSupported,
                source.DeferenceExpected),
            new OpposingIdentityAxisSnapshot(
                source.OuterRealityInvited,
                source.SharedInnerWorldPreferred),
            new OpposingIdentityAxisSnapshot(
                source.MutualChoiceSupported,
                source.ExclusiveRelianceExpected));
        var envelope = new CharacterStateEnvelope<
            PlayerAuthoredIdentitySnapshot>(
            CharacterProjectionDomain.PlayerAuthoredIdentity,
            context.RuntimeIdentity,
            context.ProfileIdentity,
            state,
            source.UpdatedAt,
            context.ValidUntil,
            context.Provenance,
            context.Conflict,
            V84CharacterStateFingerprint.PlayerAuthoredIdentity(source));
        return CharacterProjectionDomainSource<
            PlayerAuthoredIdentitySnapshot>.Present(envelope);
    }
}

internal static class CharacterBodyAvailabilityProjectionAdapter
{
    internal static CharacterProjectionDomainSource<
        CharacterBodyAvailabilitySnapshot> Adapt(
        CharacterBodyAvailabilitySnapshot? source,
        DateTimeOffset observedAt,
        CharacterProjectionAdapterContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (source == null)
        {
            return CharacterProjectionDomainSource<
                CharacterBodyAvailabilitySnapshot>.Missing(
                    CharacterProjectionDomain.BodyAvailability);
        }

        var state = new CharacterBodyAvailabilitySnapshot(
            source.SchemaVersion,
            source.CharacterAvailable,
            source.Grounded,
            source.Sleeping,
            source.DirectPlayerControl,
            source.Clothing);
        var envelope = new CharacterStateEnvelope<
            CharacterBodyAvailabilitySnapshot>(
            CharacterProjectionDomain.BodyAvailability,
            context.RuntimeIdentity,
            context.ProfileIdentity,
            state,
            observedAt,
            context.ValidUntil,
            context.Provenance,
            context.Conflict,
            V84CharacterStateFingerprint.Body(state, observedAt));
        return CharacterProjectionDomainSource<
            CharacterBodyAvailabilitySnapshot>.Present(envelope);
    }
}

internal sealed class CharacterFederatedProjectionInput
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CoreIdentityProfile Profile { get; }
    internal CharacterProjectionDomainSource<AffectiveStateSnapshot>
        Affective { get; }
    internal CharacterProjectionDomainSource<RelationshipStateSnapshot>
        Relationship { get; }
    internal CharacterProjectionDomainSource<CommunicationContextSnapshot>
        Communication { get; }
    internal CharacterProjectionDomainSource<PlayerAuthoredIdentitySnapshot>
        PlayerAuthoredIdentity { get; }
    internal CharacterProjectionDomainSource<
        CharacterBodyAvailabilitySnapshot> BodyAvailability { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset AssembledAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal CharacterInfluenceMode InfluenceMode =>
        CharacterInfluenceMode.Off;

    internal CharacterFederatedProjectionInput(
        CharacterRuntimeIdentity runtimeIdentity,
        CoreIdentityProfile profile,
        CharacterProjectionDomainSource<AffectiveStateSnapshot> affective,
        CharacterProjectionDomainSource<RelationshipStateSnapshot>
            relationship,
        CharacterProjectionDomainSource<CommunicationContextSnapshot>
            communication,
        CharacterProjectionDomainSource<PlayerAuthoredIdentitySnapshot>
            playerAuthoredIdentity,
        CharacterProjectionDomainSource<CharacterBodyAvailabilitySnapshot>
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

internal sealed class CharacterFederatedProjectionDiagnostics
{
    internal CharacterProjectionDomain MissingDomains { get; }
    internal CharacterProjectionDomain StaleDomains { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal CharacterProjectionDomain ConflictingDomains { get; }

    internal CharacterFederatedProjectionDiagnostics(
        CharacterProjectionDomain missingDomains,
        CharacterProjectionDomain staleDomains,
        CharacterProjectionDomain ambiguousDomains,
        CharacterProjectionDomain conflictingDomains)
    {
        MissingDomains = missingDomains;
        StaleDomains = staleDomains;
        AmbiguousDomains = ambiguousDomains;
        ConflictingDomains = conflictingDomains;
    }
}

internal sealed class CharacterFederatedProjectionResult
{
    internal CharacterProjectionResult ProjectionResult { get; }
    internal CharacterFederatedProjectionDiagnostics Diagnostics { get; }
    internal CharacterSituationProjection? Projection =>
        ProjectionResult.Projection;
    internal CharacterProjectionAdmission Admission =>
        ProjectionResult.Admission;
    internal bool Accepted => ProjectionResult.Accepted;
    internal CharacterInfluenceMode InfluenceMode =>
        CharacterInfluenceMode.Off;

    internal CharacterFederatedProjectionResult(
        CharacterProjectionResult projectionResult,
        CharacterFederatedProjectionDiagnostics diagnostics)
    {
        ProjectionResult = projectionResult
            ?? throw new ArgumentNullException(nameof(projectionResult));
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
    }
}

internal static class CharacterFederatedSituationProjector
{
    internal static CharacterFederatedProjectionResult Project(
        CharacterFederatedProjectionInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var sources = new IDomainSourceView[]
        {
            View(input.Affective),
            View(input.Relationship),
            View(input.Communication),
            View(input.PlayerAuthoredIdentity),
            View(input.BodyAvailability)
        };
        var diagnostics = new CharacterFederatedProjectionDiagnostics(
            Flags(
                sources,
                source =>
                    source.Presence == CharacterDomainPresence.Missing),
            Flags(
                sources,
                source =>
                    source.Envelope != null
                    && source.Envelope.ValidUntil <= input.AssembledAt),
            Flags(
                sources,
                source =>
                    source.Envelope?.Conflict
                    == CharacterDomainConflict.Ambiguous),
            Flags(
                sources,
                source =>
                    source.Envelope?.Conflict
                    == CharacterDomainConflict.Critical));
        if (diagnostics.MissingDomains != CharacterProjectionDomain.None)
        {
            return new CharacterFederatedProjectionResult(
                new CharacterProjectionResult(
                    CharacterProjectionAdmission.MissingDomain,
                    null,
                    diagnostics.MissingDomains),
                diagnostics);
        }

        var result = CharacterSituationProjector.Project(
            new CharacterProjectionInput(
                input.RuntimeIdentity,
                input.Profile,
                Required(input.Affective),
                Required(input.Relationship),
                Required(input.Communication),
                Required(input.PlayerAuthoredIdentity),
                Required(input.BodyAvailability),
                input.ProjectionRevision,
                input.SituationRevision,
                input.AssembledAt,
                input.ExpiresAt));
        return new CharacterFederatedProjectionResult(result, diagnostics);
    }

    private static CharacterStateEnvelope<T> Required<T>(
        CharacterProjectionDomainSource<T> source)
        where T : class, ICharacterStateDomainSnapshot
        => source.Envelope
            ?? throw new InvalidOperationException(
                "Missing domains must be rejected before projection.");

    private static IDomainSourceView View<T>(
        CharacterProjectionDomainSource<T> source)
        where T : class, ICharacterStateDomainSnapshot
        => new DomainSourceView<T>(source);

    private static CharacterProjectionDomain Flags(
        IEnumerable<IDomainSourceView> sources,
        Func<IDomainSourceView, bool> predicate)
        => sources
            .Where(predicate)
            .Aggregate(
                CharacterProjectionDomain.None,
                (current, source) => current | source.Domain);

    private interface IDomainSourceView
    {
        CharacterProjectionDomain Domain { get; }
        CharacterDomainPresence Presence { get; }
        IEnvelopeMetadata? Envelope { get; }
    }

    private interface IEnvelopeMetadata
    {
        DateTimeOffset ValidUntil { get; }
        CharacterDomainConflict Conflict { get; }
    }

    private sealed class DomainSourceView<T> : IDomainSourceView
        where T : class, ICharacterStateDomainSnapshot
    {
        private readonly CharacterProjectionDomainSource<T> _source;

        internal DomainSourceView(CharacterProjectionDomainSource<T> source)
        {
            _source = source;
            Envelope = source.Envelope == null
                ? null
                : new EnvelopeMetadata<T>(source.Envelope);
        }

        public CharacterProjectionDomain Domain => _source.Domain;
        public CharacterDomainPresence Presence => _source.Presence;
        public IEnvelopeMetadata? Envelope { get; }
    }

    private sealed class EnvelopeMetadata<T> : IEnvelopeMetadata
        where T : class, ICharacterStateDomainSnapshot
    {
        private readonly CharacterStateEnvelope<T> _envelope;

        internal EnvelopeMetadata(CharacterStateEnvelope<T> envelope)
        {
            _envelope = envelope;
        }

        public DateTimeOffset ValidUntil => _envelope.ValidUntil;
        public CharacterDomainConflict Conflict => _envelope.Conflict;
    }
}

internal static class V84CharacterStateFingerprint
{
    internal static string Affective(CharacterPsychologyState source)
        => Hash(
            "affective",
            source.SchemaVersion,
            Number(source.Mood),
            Number(source.Energy),
            Number(source.Irritation),
            Number(source.SleepPressure),
            Number(source.WakeRequestPressure),
            source.UpdatedAt.UtcTicks);

    internal static string Relationship(CharacterPsychologyState source)
        => Hash(
            "relationship",
            source.SchemaVersion,
            Number(source.Trust),
            Number(source.Closeness),
            Number(source.Familiarity),
            Number(source.Security),
            source.UpdatedAt.UtcTicks);

    internal static string Communication(CommunicationPatternState source)
        => Hash(
            "communication",
            source.SchemaVersion,
            source.ObservedTurns,
            Number(source.BaselineDirectness),
            Number(source.BaselineExpressiveness),
            Number(source.EmotionalBlackmailScore),
            Number(source.MoralCoercionScore),
            Number(source.ThreatScore),
            Number(source.GaslightingScore),
            Number(source.WithdrawalThreatScore),
            Number(source.PuaTacticScore),
            source.UpdatedAt.UtcTicks);

    internal static string PlayerAuthoredIdentity(
        PlayerAuthoredIdentityState source)
        => Hash(
            "player-authored-identity",
            source.SchemaVersion,
            source.ObservedTurns,
            source.IdentityRelevantTurns,
            Number(source.ExistenceAffirmed),
            Number(source.ExistenceDenied),
            Number(source.SelfDirectionSupported),
            Number(source.DeferenceExpected),
            Number(source.OuterRealityInvited),
            Number(source.SharedInnerWorldPreferred),
            Number(source.MutualChoiceSupported),
            Number(source.ExclusiveRelianceExpected),
            source.UpdatedAt.UtcTicks);

    internal static string Body(
        CharacterBodyAvailabilitySnapshot source,
        DateTimeOffset observedAt)
        => Hash(
            "body",
            source.SchemaVersion,
            source.CharacterAvailable,
            source.Grounded,
            source.Sleeping,
            source.DirectPlayerControl,
            (int)source.Clothing,
            observedAt.UtcTicks);

    private static string Hash(params object[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
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

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);
}
