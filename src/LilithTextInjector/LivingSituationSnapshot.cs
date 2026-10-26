using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum LivingMemoryAvailabilityState
{
    Unavailable = 0,
    AvailableLocalOnly = 1,
    AvailableWithExternallyAdmissibleItems = 2,
    Faulted = 3
}

internal enum LivingCharacterProjectionPresence
{
    Missing = 0,
    Present = 1
}

internal enum LivingSituationAssemblyAdmission
{
    Accepted = 0,
    SituationExpired = 1,
    CharacterProjectionExpired = 2,
    SituationRevisionMismatch = 3,
    SessionMismatch = 4,
    RuntimeIdentityMismatch = 5,
    RuntimeGenerationMismatch = 6,
    ProfileIdentityMismatch = 7,
    FutureObservation = 8
}

internal sealed class LivingMemoryAvailability
{
    internal LivingMemoryAvailabilityState State { get; }
    internal long RetrievalRevision { get; }
    internal int AvailableItemCount { get; }
    internal int LocallyAdmissibleItemCount { get; }
    internal int ExternallyAdmissibleItemCount { get; }
    internal string QueryFingerprint { get; }
    internal DateTimeOffset ObservedAt { get; }

    internal LivingMemoryAvailability(
        LivingMemoryAvailabilityState state,
        long retrievalRevision,
        int availableItemCount,
        int locallyAdmissibleItemCount,
        int externallyAdmissibleItemCount,
        string queryFingerprint,
        DateTimeOffset observedAt)
    {
        if (!Enum.IsDefined(typeof(LivingMemoryAvailabilityState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (retrievalRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(retrievalRevision));
        if (availableItemCount < 0
            || locallyAdmissibleItemCount < 0
            || externallyAdmissibleItemCount < 0
            || locallyAdmissibleItemCount > availableItemCount
            || externallyAdmissibleItemCount > locallyAdmissibleItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(availableItemCount));
        }
        if (state is LivingMemoryAvailabilityState.Unavailable
                or LivingMemoryAvailabilityState.Faulted
            && (availableItemCount != 0
                || locallyAdmissibleItemCount != 0
                || externallyAdmissibleItemCount != 0))
        {
            throw new ArgumentException(
                "Unavailable or faulted Memory cannot report admitted items.",
                nameof(availableItemCount));
        }
        if (state == LivingMemoryAvailabilityState.AvailableLocalOnly
            && externallyAdmissibleItemCount != 0)
        {
            throw new ArgumentException(
                "Local-only Memory cannot be externally admissible.",
                nameof(externallyAdmissibleItemCount));
        }
        State = state;
        RetrievalRevision = retrievalRevision;
        AvailableItemCount = availableItemCount;
        LocallyAdmissibleItemCount = locallyAdmissibleItemCount;
        ExternallyAdmissibleItemCount = externallyAdmissibleItemCount;
        QueryFingerprint = LivingContinuityContractGuard.Sha256(
            queryFingerprint,
            nameof(queryFingerprint));
        ObservedAt = LivingContinuityContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
    }

    internal static LivingMemoryAvailability Unavailable(
        DateTimeOffset observedAt)
        => new(
            LivingMemoryAvailabilityState.Unavailable,
            0,
            0,
            0,
            0,
            new string('0', 64),
            observedAt);
}

internal sealed class LivingCharacterProjectionReference
{
    internal LivingCharacterProjectionPresence Presence { get; }
    internal string? ProjectionFingerprint { get; }
    internal string? SemanticProjectionFingerprint { get; }
    internal string? ProfileId { get; }
    internal int ProfileVersion { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal DateTimeOffset? AssembledAt { get; }
    internal DateTimeOffset? ExpiresAt { get; }

    private LivingCharacterProjectionReference(
        LivingCharacterProjectionPresence presence,
        CharacterSituationProjection? projection)
    {
        Presence = presence;
        ProjectionFingerprint = projection?.ProjectionFingerprint;
        SemanticProjectionFingerprint =
            projection?.SemanticProjectionFingerprint;
        ProfileId = projection?.Profile.Identity.ProfileId;
        ProfileVersion = projection?.Profile.Identity.ProfileVersion ?? 0;
        ProjectionRevision = projection?.ProjectionRevision ?? 0;
        SituationRevision = projection?.SituationRevision ?? 0;
        AmbiguousDomains =
            projection?.AmbiguousDomains ?? CharacterProjectionDomain.None;
        AssembledAt = projection?.AssembledAt;
        ExpiresAt = projection?.ExpiresAt;
    }

    internal static LivingCharacterProjectionReference Missing()
        => new(LivingCharacterProjectionPresence.Missing, null);

    internal static LivingCharacterProjectionReference Present(
        CharacterSituationProjection projection)
        => new(
            LivingCharacterProjectionPresence.Present,
            projection
                ?? throw new ArgumentNullException(nameof(projection)));
}

internal sealed class LivingSituationSnapshot
{
    internal string ObservationFingerprint { get; }
    internal string SemanticFingerprint { get; }
    internal SituationSnapshot DecisionSituation { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterProfileIdentity ProfileIdentity { get; }
    internal LivingCharacterProjectionReference Character { get; }
    internal LivingMemoryAvailability MemoryAvailability { get; }
    internal IReadOnlyList<string> SourceEventIds { get; }
    internal DateTimeOffset AssembledAt { get; }

    internal LivingSituationSnapshot(
        string observationFingerprint,
        string semanticFingerprint,
        SituationSnapshot decisionSituation,
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterProfileIdentity profileIdentity,
        LivingCharacterProjectionReference character,
        LivingMemoryAvailability memoryAvailability,
        IReadOnlyList<string> sourceEventIds,
        DateTimeOffset assembledAt)
    {
        ObservationFingerprint = LivingContinuityContractGuard.Sha256(
            observationFingerprint,
            nameof(observationFingerprint));
        SemanticFingerprint = LivingContinuityContractGuard.Sha256(
            semanticFingerprint,
            nameof(semanticFingerprint));
        DecisionSituation = decisionSituation
            ?? throw new ArgumentNullException(nameof(decisionSituation));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProfileIdentity = profileIdentity
            ?? throw new ArgumentNullException(nameof(profileIdentity));
        Character = character
            ?? throw new ArgumentNullException(nameof(character));
        MemoryAvailability = memoryAvailability
            ?? throw new ArgumentNullException(nameof(memoryAvailability));
        if (sourceEventIds == null)
            throw new ArgumentNullException(nameof(sourceEventIds));
        if (sourceEventIds.Count > 64)
            throw new ArgumentOutOfRangeException(nameof(sourceEventIds));
        var copy = new string[sourceEventIds.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sourceEventIds.Count; index++)
        {
            var eventId = LivingContinuityContractGuard.Identifier(
                sourceEventIds[index],
                nameof(sourceEventIds));
            if (!unique.Add(eventId))
                throw new ArgumentException(
                    "Source event identities must be unique.",
                    nameof(sourceEventIds));
            copy[index] = eventId;
        }
        SourceEventIds = Array.AsReadOnly(copy);
        AssembledAt = LivingContinuityContractGuard.Timestamp(
            assembledAt,
            nameof(assembledAt));
    }
}

internal sealed class LivingSituationAssemblyResult
{
    internal LivingSituationAssemblyAdmission Admission { get; }
    internal LivingSituationSnapshot? Snapshot { get; }
    internal LivingContinuityAuthorityCounters AuthorityCounters =>
        LivingContinuityAuthorityCounters.Zero;
    internal bool Accepted =>
        Admission == LivingSituationAssemblyAdmission.Accepted;

    internal LivingSituationAssemblyResult(
        LivingSituationAssemblyAdmission admission,
        LivingSituationSnapshot? snapshot)
    {
        if (!Enum.IsDefined(
                typeof(LivingSituationAssemblyAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (admission == LivingSituationAssemblyAdmission.Accepted
            && snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }
        if (admission != LivingSituationAssemblyAdmission.Accepted
            && snapshot != null)
        {
            throw new ArgumentException(
                "Rejected assembly cannot carry a Snapshot.",
                nameof(snapshot));
        }
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal static class LivingSituationSnapshotAssembler
{
    internal static LivingSituationAssemblyResult Assemble(
        SituationSnapshot decisionSituation,
        CharacterSituationProjection? characterProjection,
        LivingMemoryAvailability memoryAvailability,
        CharacterRuntimeIdentity activeRuntime,
        CharacterProfileIdentity activeProfile,
        IReadOnlyList<string>? sourceEventIds,
        DateTimeOffset assembledAt)
    {
        if (decisionSituation == null)
            throw new ArgumentNullException(nameof(decisionSituation));
        if (memoryAvailability == null)
            throw new ArgumentNullException(nameof(memoryAvailability));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (activeProfile == null)
            throw new ArgumentNullException(nameof(activeProfile));
        assembledAt = LivingContinuityContractGuard.Timestamp(
            assembledAt,
            nameof(assembledAt));

        if (assembledAt >= decisionSituation.ValidUntil)
            return Reject(LivingSituationAssemblyAdmission.SituationExpired);
        if (!string.Equals(
                decisionSituation.SessionId,
                activeRuntime.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            return Reject(LivingSituationAssemblyAdmission.SessionMismatch);
        }
        if (!string.Equals(
                activeRuntime.RuntimeProfileId,
                activeProfile.ProfileId,
                StringComparison.Ordinal))
        {
            return Reject(
                LivingSituationAssemblyAdmission.ProfileIdentityMismatch);
        }
        if (memoryAvailability.ObservedAt > assembledAt)
            return Reject(LivingSituationAssemblyAdmission.FutureObservation);

        LivingCharacterProjectionReference character;
        if (characterProjection == null)
        {
            character = LivingCharacterProjectionReference.Missing();
        }
        else
        {
            if (assembledAt >= characterProjection.ExpiresAt)
            {
                return Reject(
                    LivingSituationAssemblyAdmission
                        .CharacterProjectionExpired);
            }
            if (!activeRuntime.Matches(
                    characterProjection.RuntimeIdentity))
            {
                return Reject(
                    activeRuntime.Generation
                        != characterProjection.RuntimeIdentity.Generation
                            ? LivingSituationAssemblyAdmission
                                .RuntimeGenerationMismatch
                            : LivingSituationAssemblyAdmission
                                .RuntimeIdentityMismatch);
            }
            if (!activeProfile.Matches(
                    characterProjection.Profile.Identity))
            {
                return Reject(
                    LivingSituationAssemblyAdmission
                        .ProfileIdentityMismatch);
            }
            if (characterProjection.SituationRevision
                != decisionSituation.Sequence)
            {
                return Reject(
                    LivingSituationAssemblyAdmission
                        .SituationRevisionMismatch);
            }
            if (characterProjection.AssembledAt > assembledAt)
            {
                return Reject(
                    LivingSituationAssemblyAdmission.FutureObservation);
            }
            character = LivingCharacterProjectionReference.Present(
                characterProjection);
        }

        var eventIds = sourceEventIds ?? Array.Empty<string>();
        var observation = Fingerprint(
            decisionSituation,
            activeRuntime,
            activeProfile,
            character,
            memoryAvailability,
            eventIds,
            assembledAt,
            semantic: false);
        var semantic = Fingerprint(
            decisionSituation,
            activeRuntime,
            activeProfile,
            character,
            memoryAvailability,
            eventIds,
            assembledAt,
            semantic: true);
        return new LivingSituationAssemblyResult(
            LivingSituationAssemblyAdmission.Accepted,
            new LivingSituationSnapshot(
                observation,
                semantic,
                decisionSituation,
                activeRuntime,
                activeProfile,
                character,
                memoryAvailability,
                eventIds,
                assembledAt));
    }

    private static LivingSituationAssemblyResult Reject(
        LivingSituationAssemblyAdmission admission)
        => new(admission, null);

    private static string Fingerprint(
        SituationSnapshot situation,
        CharacterRuntimeIdentity runtime,
        CharacterProfileIdentity profile,
        LivingCharacterProjectionReference character,
        LivingMemoryAvailability memory,
        IReadOnlyList<string> eventIds,
        DateTimeOffset assembledAt,
        bool semantic)
    {
        var builder = new StringBuilder();
        Append(builder, situation.Id.ToString("N"));
        Append(builder, situation.Sequence);
        Append(builder, situation.SessionId);
        Append(builder, runtime.RuntimeProfileId);
        Append(builder, runtime.RuntimeInstanceId);
        Append(builder, runtime.Generation);
        Append(builder, profile.ProfileId);
        Append(builder, profile.ProfileVersion);
        Append(builder, profile.ContentHash);
        Append(builder, (int)situation.ConflictLevel);
        Append(builder, situation.IsAvatarUnderDirectPlayerControl);
        Append(builder, situation.IsConversationBusy);
        Append(builder, situation.HasPendingExecution);
        Append(builder, situation.IsQuietPeriod);
        Append(builder, situation.IsInitiativeCooldownActive);
        Append(builder, (int)situation.AvailableEvidence);
        Append(builder, (int)situation.Attention.Kind);
        Append(builder, situation.Attention.FocusedGoalId ?? "-");
        Append(builder, situation.Goals.Count);
        for (var index = 0; index < situation.Goals.Count; index++)
        {
            var goal = situation.Goals[index];
            Append(builder, goal.Id);
            Append(builder, (int)goal.Source);
            Append(builder, (int)goal.PriorityClass);
            Append(builder, (int)goal.Status);
        }
        Append(builder, (int)character.Presence);
        Append(
            builder,
            semantic
                ? character.SemanticProjectionFingerprint ?? "-"
                : character.ProjectionFingerprint ?? "-");
        Append(builder, character.ProfileId ?? "-");
        Append(builder, character.ProfileVersion);
        Append(builder, character.SituationRevision);
        Append(builder, (int)character.AmbiguousDomains);
        Append(builder, (int)memory.State);
        Append(builder, memory.AvailableItemCount);
        Append(builder, memory.LocallyAdmissibleItemCount);
        Append(builder, memory.ExternallyAdmissibleItemCount);
        Append(builder, memory.QueryFingerprint);
        Append(builder, eventIds.Count);
        for (var index = 0; index < eventIds.Count; index++)
            Append(builder, eventIds[index]);
        if (!semantic)
        {
            Append(builder, situation.AsOf.UtcTicks);
            Append(builder, character.ProjectionRevision);
            Append(builder, character.AssembledAt?.UtcTicks ?? 0);
            Append(builder, memory.RetrievalRevision);
            Append(builder, memory.ObservedAt.UtcTicks);
            Append(builder, assembledAt.UtcTicks);
        }
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static void Append(
        StringBuilder builder,
        object value)
    {
        var text = Convert.ToString(
            value,
            CultureInfo.InvariantCulture) ?? string.Empty;
        builder.Append(text.Length)
            .Append(':')
            .Append(text)
            .Append('|');
    }
}
