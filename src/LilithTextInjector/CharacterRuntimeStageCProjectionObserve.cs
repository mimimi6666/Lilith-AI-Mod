using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterProjectionSourceHealth
{
    Loaded = 0,
    RecoveredPrevious = 1,
    Missing = 2,
    Corrupt = 3,
    RuntimeUnavailable = 4
}

internal enum CharacterProjectionObserveStatus
{
    Off = 0,
    ProjectionOnly = 1,
    Fault = 2,
    Capacity = 3
}

internal enum CharacterProjectionObserveAdmission
{
    NotRunning = 0,
    Recorded = 1,
    SemanticallyCoalesced = 2,
    RejectedProjectionRecorded = 3,
    CapacityReached = 4,
    Faulted = 5,
    FailureEpisodeCoalesced = 6
}

internal enum CharacterObservationTimeSkewStatus
{
    Incomplete = 0,
    Aligned = 1,
    Skewed = 2
}

internal sealed class CharacterRuntimeStageCSourceSet
{
    internal CharacterPsychologyState? Psychology { get; }
    internal CharacterProjectionSourceHealth PsychologyHealth { get; }
    internal CommunicationPatternState? Communication { get; }
    internal CharacterProjectionSourceHealth CommunicationHealth { get; }
    internal PlayerAuthoredIdentityState? PlayerAuthoredIdentity { get; }
    internal CharacterProjectionSourceHealth PlayerAuthoredIdentityHealth {
        get;
    }
    internal CharacterBodyAvailabilitySnapshot? BodyAvailability { get; }
    internal CharacterProjectionSourceHealth BodyAvailabilityHealth { get; }
    internal DateTimeOffset BodyObservedAt { get; }

    internal CharacterRuntimeStageCSourceSet(
        CharacterPsychologyState? psychology,
        CharacterProjectionSourceHealth psychologyHealth,
        CommunicationPatternState? communication,
        CharacterProjectionSourceHealth communicationHealth,
        PlayerAuthoredIdentityState? playerAuthoredIdentity,
        CharacterProjectionSourceHealth playerAuthoredIdentityHealth,
        CharacterBodyAvailabilitySnapshot? bodyAvailability,
        CharacterProjectionSourceHealth bodyAvailabilityHealth,
        DateTimeOffset bodyObservedAt)
    {
        ValidateSource(psychology, psychologyHealth, nameof(psychology));
        ValidateSource(
            communication,
            communicationHealth,
            nameof(communication));
        ValidateSource(
            playerAuthoredIdentity,
            playerAuthoredIdentityHealth,
            nameof(playerAuthoredIdentity));
        ValidateSource(
            bodyAvailability,
            bodyAvailabilityHealth,
            nameof(bodyAvailability));

        Psychology = psychology;
        PsychologyHealth = psychologyHealth;
        Communication = communication;
        CommunicationHealth = communicationHealth;
        PlayerAuthoredIdentity = playerAuthoredIdentity;
        PlayerAuthoredIdentityHealth = playerAuthoredIdentityHealth;
        BodyAvailability = bodyAvailability;
        BodyAvailabilityHealth = bodyAvailabilityHealth;
        BodyObservedAt = CharacterRuntimeContractGuard.Timestamp(
            bodyObservedAt,
            nameof(bodyObservedAt));
    }

    private static void ValidateSource<T>(
        T? source,
        CharacterProjectionSourceHealth health,
        string parameterName)
        where T : class
    {
        if (!Enum.IsDefined(
                typeof(CharacterProjectionSourceHealth),
                health))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        var shouldBePresent = health is
            CharacterProjectionSourceHealth.Loaded
            or CharacterProjectionSourceHealth.RecoveredPrevious;
        if (shouldBePresent != (source != null))
        {
            throw new ArgumentException(
                "Loaded or recovered source health requires a source, "
                + "while Missing, Corrupt and RuntimeUnavailable must "
                + "remain explicitly absent.",
                parameterName);
        }
    }
}

internal sealed class CharacterRuntimeStageCAssembly
{
    internal CharacterFederatedProjectionInput Input { get; }
    internal CharacterFederatedProjectionResult Result { get; }
    internal CharacterProjectionSourceHealth PsychologyHealth { get; }
    internal CharacterProjectionSourceHealth CommunicationHealth { get; }
    internal CharacterProjectionSourceHealth PlayerAuthoredIdentityHealth {
        get;
    }
    internal CharacterProjectionSourceHealth BodyAvailabilityHealth { get; }

    internal CharacterRuntimeStageCAssembly(
        CharacterFederatedProjectionInput input,
        CharacterFederatedProjectionResult result,
        CharacterRuntimeStageCSourceSet sources)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));
        PsychologyHealth = sources.PsychologyHealth;
        CommunicationHealth = sources.CommunicationHealth;
        PlayerAuthoredIdentityHealth =
            sources.PlayerAuthoredIdentityHealth;
        BodyAvailabilityHealth = sources.BodyAvailabilityHealth;
    }
}

internal static class CharacterRuntimeStageCProjectionAssembler
{
    internal static readonly TimeSpan AffectiveLifetime =
        TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan RelationshipLifetime =
        TimeSpan.FromDays(30);
    internal static readonly TimeSpan CommunicationLifetime =
        TimeSpan.FromDays(7);
    internal static readonly TimeSpan PlayerAuthoredIdentityLifetime =
        TimeSpan.FromDays(90);
    internal static readonly TimeSpan BodyAvailabilityLifetime =
        TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ProjectionLifetime =
        TimeSpan.FromSeconds(1);

    internal static CharacterRuntimeStageCAssembly Assemble(
        CharacterRuntimeIdentity runtimeIdentity,
        CoreIdentityProfile profile,
        CharacterRuntimeStageCSourceSet sources,
        long projectionRevision,
        long situationRevision,
        int narrativeKernelVersion,
        DateTimeOffset assembledAt)
    {
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));
        CharacterRuntimeContractGuard.Positive(
            narrativeKernelVersion,
            nameof(narrativeKernelVersion));

        var psychologyObservedAt =
            sources.Psychology?.UpdatedAt ?? assembledAt;
        var communicationObservedAt =
            sources.Communication?.UpdatedAt ?? assembledAt;
        var identityObservedAt =
            sources.PlayerAuthoredIdentity?.UpdatedAt ?? assembledAt;

        var psychology = CharacterPsychologyProjectionAdapter.Adapt(
            sources.Psychology,
            Context(
                runtimeIdentity,
                profile.Identity,
                psychologyObservedAt + AffectiveLifetime,
                sources.PsychologyHealth),
            Context(
                runtimeIdentity,
                profile.Identity,
                psychologyObservedAt + RelationshipLifetime,
                sources.PsychologyHealth));
        var communication =
            CommunicationContextProjectionAdapter.Adapt(
                sources.Communication,
                Context(
                    runtimeIdentity,
                    profile.Identity,
                    communicationObservedAt + CommunicationLifetime,
                    sources.CommunicationHealth));
        var identity =
            PlayerAuthoredIdentityProjectionAdapter.Adapt(
                sources.PlayerAuthoredIdentity,
                Context(
                    runtimeIdentity,
                    profile.Identity,
                    identityObservedAt
                        + PlayerAuthoredIdentityLifetime,
                    sources.PlayerAuthoredIdentityHealth),
                narrativeKernelVersion);
        var body = CharacterBodyAvailabilityProjectionAdapter.Adapt(
            sources.BodyAvailability,
            sources.BodyObservedAt,
            Context(
                runtimeIdentity,
                profile.Identity,
                sources.BodyObservedAt + BodyAvailabilityLifetime,
                sources.BodyAvailabilityHealth));
        var input = new CharacterFederatedProjectionInput(
            runtimeIdentity,
            profile,
            psychology.Affective,
            psychology.Relationship,
            communication,
            identity,
            body,
            projectionRevision,
            situationRevision,
            assembledAt,
            assembledAt + ProjectionLifetime);
        return new CharacterRuntimeStageCAssembly(
            input,
            CharacterFederatedSituationProjector.Project(input),
            sources);
    }

    private static CharacterProjectionAdapterContext Context(
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterProfileIdentity profileIdentity,
        DateTimeOffset validUntil,
        CharacterProjectionSourceHealth health)
        => new(
            runtimeIdentity,
            profileIdentity,
            validUntil,
            health == CharacterProjectionSourceHealth.RecoveredPrevious
                ? CharacterStateProvenance.MigratedLocalState
                : CharacterStateProvenance.LocalStateProjection,
            CharacterDomainConflict.None);
}

internal sealed class CharacterProjectionDomainObservation
{
    internal CharacterProjectionDomain Domain { get; }
    internal CharacterDomainPresence Presence { get; }
    internal CharacterProjectionSourceHealth SourceHealth { get; }
    internal DateTimeOffset? ObservedAt { get; }
    internal DateTimeOffset? ValidUntil { get; }
    internal CharacterStateProvenance? Provenance { get; }
    internal CharacterDomainConflict? Conflict { get; }
    internal string SourceRevision { get; }

    internal CharacterProjectionDomainObservation(
        CharacterProjectionDomain domain,
        CharacterDomainPresence presence,
        CharacterProjectionSourceHealth sourceHealth,
        DateTimeOffset? observedAt,
        DateTimeOffset? validUntil,
        CharacterStateProvenance? provenance,
        CharacterDomainConflict? conflict,
        string sourceRevision)
    {
        Domain = domain;
        Presence = presence;
        SourceHealth = sourceHealth;
        ObservedAt = observedAt;
        ValidUntil = validUntil;
        Provenance = provenance;
        Conflict = conflict;
        SourceRevision = sourceRevision ?? string.Empty;
    }
}

internal sealed class CharacterObservationTimeSkewDiagnostics
{
    internal CharacterObservationTimeSkewStatus Status { get; }
    internal CharacterProjectionDomain ObservedDomains { get; }
    internal DateTimeOffset? EarliestObservedAt { get; }
    internal DateTimeOffset? LatestObservedAt { get; }
    internal TimeSpan Span { get; }
    internal bool AtomicallyConsistent =>
        Status == CharacterObservationTimeSkewStatus.Aligned;

    internal CharacterObservationTimeSkewDiagnostics(
        CharacterObservationTimeSkewStatus status,
        CharacterProjectionDomain observedDomains,
        DateTimeOffset? earliestObservedAt,
        DateTimeOffset? latestObservedAt,
        TimeSpan span)
    {
        Status = status;
        ObservedDomains = observedDomains;
        EarliestObservedAt = earliestObservedAt;
        LatestObservedAt = latestObservedAt;
        Span = span;
    }

    internal static CharacterObservationTimeSkewDiagnostics From(
        IReadOnlyList<CharacterProjectionDomainObservation> domains)
    {
        if (domains == null)
            throw new ArgumentNullException(nameof(domains));
        var present = domains
            .Where(item => item.ObservedAt.HasValue)
            .ToArray();
        var observedDomains = present.Aggregate(
            CharacterProjectionDomain.None,
            (flags, item) => flags | item.Domain);
        if (present.Length == 0)
        {
            return new CharacterObservationTimeSkewDiagnostics(
                CharacterObservationTimeSkewStatus.Incomplete,
                CharacterProjectionDomain.None,
                null,
                null,
                TimeSpan.Zero);
        }

        var earliest = present.Min(item => item.ObservedAt!.Value);
        var latest = present.Max(item => item.ObservedAt!.Value);
        var complete = present.Length == 5;
        return new CharacterObservationTimeSkewDiagnostics(
            !complete
                ? CharacterObservationTimeSkewStatus.Incomplete
                : earliest == latest
                    ? CharacterObservationTimeSkewStatus.Aligned
                    : CharacterObservationTimeSkewStatus.Skewed,
            observedDomains,
            earliest,
            latest,
            latest - earliest);
    }
}

internal sealed class CharacterProjectionObserveRecord
{
    internal long Sequence { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal long ProjectionRevision { get; }
    internal long SituationRevision { get; }
    internal CharacterProjectionAdmission ProjectionAdmission { get; }
    internal CharacterProjectionDomain MissingDomains { get; }
    internal CharacterProjectionDomain StaleDomains { get; }
    internal CharacterProjectionDomain AmbiguousDomains { get; }
    internal CharacterProjectionDomain ConflictingDomains { get; }
    internal string ProjectionFingerprint { get; }
    internal string SemanticProjectionFingerprint { get; }
    internal string FailureEpisodeFingerprint { get; }
    internal CharacterSituationProjection? Projection { get; }
    internal IReadOnlyList<CharacterProjectionDomainObservation> Domains {
        get;
    }
    internal CharacterObservationTimeSkewDiagnostics ObservationTimeSkew {
        get;
    }
    internal DateTimeOffset AssembledAt { get; }
    internal DateTimeOffset FirstObservedAt { get; }
    internal DateTimeOffset LastObservedAt { get; }
    internal int CoalescedObservationCount { get; }
    internal int FailureEpisodeCount { get; }
    internal CharacterInfluenceMode InfluenceMode =>
        CharacterInfluenceMode.ProjectionOnly;
    internal bool Executable => false;

    internal CharacterProjectionObserveRecord(
        long sequence,
        CharacterRuntimeStageCAssembly assembly,
        DateTimeOffset observedAt,
        int coalescedObservationCount = 0,
        int failureEpisodeCount = 0,
        DateTimeOffset? firstObservedAt = null)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        var timestamp = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (coalescedObservationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coalescedObservationCount));
        }
        if (failureEpisodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureEpisodeCount));
        }

        var input = assembly.Input;
        var result = assembly.Result;
        Sequence = sequence;
        RuntimeIdentity = input.RuntimeIdentity;
        ProjectionRevision = input.ProjectionRevision;
        SituationRevision = input.SituationRevision;
        ProjectionAdmission = result.Admission;
        MissingDomains = result.Diagnostics.MissingDomains;
        StaleDomains = result.Diagnostics.StaleDomains;
        AmbiguousDomains = result.Diagnostics.AmbiguousDomains;
        ConflictingDomains = result.Diagnostics.ConflictingDomains;
        ProjectionFingerprint =
            result.Projection?.ProjectionFingerprint ?? string.Empty;
        SemanticProjectionFingerprint =
            result.Projection?.SemanticProjectionFingerprint
            ?? string.Empty;
        Projection = result.Projection;
        Domains = BuildDomains(assembly);
        FailureEpisodeFingerprint = result.Accepted
            ? string.Empty
            : BuildFailureEpisodeFingerprint(
                assembly,
                Domains);
        ObservationTimeSkew =
            CharacterObservationTimeSkewDiagnostics.From(Domains);
        AssembledAt = input.AssembledAt;
        FirstObservedAt = firstObservedAt.HasValue
            ? CharacterRuntimeContractGuard.Timestamp(
                firstObservedAt.Value,
                nameof(firstObservedAt))
            : timestamp;
        LastObservedAt = timestamp;
        CoalescedObservationCount = coalescedObservationCount;
        FailureEpisodeCount = result.Accepted
            ? 0
            : Math.Max(1, failureEpisodeCount);
    }

    internal CharacterProjectionObserveRecord CoalesceAccepted(
        CharacterRuntimeStageCAssembly assembly,
        DateTimeOffset observedAt)
        => new(
            Sequence,
            assembly,
            observedAt,
            CoalescedObservationCount + 1,
            failureEpisodeCount: 0,
            firstObservedAt: FirstObservedAt);

    internal CharacterProjectionObserveRecord CoalesceFailureEpisode(
        CharacterRuntimeStageCAssembly assembly,
        DateTimeOffset observedAt)
        => new(
            Sequence,
            assembly,
            observedAt,
            coalescedObservationCount: 0,
            failureEpisodeCount: FailureEpisodeCount + 1,
            firstObservedAt: FirstObservedAt);

    private static IReadOnlyList<CharacterProjectionDomainObservation>
        BuildDomains(CharacterRuntimeStageCAssembly assembly)
        => new[]
        {
            Domain(
                assembly.Input.Affective,
                assembly.PsychologyHealth),
            Domain(
                assembly.Input.Relationship,
                assembly.PsychologyHealth),
            Domain(
                assembly.Input.Communication,
                assembly.CommunicationHealth),
            Domain(
                assembly.Input.PlayerAuthoredIdentity,
                assembly.PlayerAuthoredIdentityHealth),
            Domain(
                assembly.Input.BodyAvailability,
                assembly.BodyAvailabilityHealth)
        };

    private static CharacterProjectionDomainObservation Domain<T>(
        CharacterProjectionDomainSource<T> source,
        CharacterProjectionSourceHealth health)
        where T : class, ICharacterStateDomainSnapshot
    {
        var envelope = source.Envelope;
        return new CharacterProjectionDomainObservation(
            source.Domain,
            source.Presence,
            health,
            envelope?.ObservedAt,
            envelope?.ValidUntil,
            envelope?.Provenance,
            envelope?.Conflict,
            envelope?.SourceFingerprint ?? string.Empty);
    }

    private static string BuildFailureEpisodeFingerprint(
        CharacterRuntimeStageCAssembly assembly,
        IReadOnlyList<CharacterProjectionDomainObservation> domains)
    {
        var input = assembly.Input;
        var result = assembly.Result;
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
        Append(builder, (int)result.Admission);
        Append(builder, (int)result.ProjectionResult.AffectedDomain);
        Append(builder, (int)result.Diagnostics.MissingDomains);
        Append(builder, (int)result.Diagnostics.StaleDomains);
        Append(builder, (int)result.Diagnostics.AmbiguousDomains);
        Append(builder, (int)result.Diagnostics.ConflictingDomains);
        foreach (var domain in domains)
        {
            Append(builder, (int)domain.Domain);
            Append(builder, (int)domain.Presence);
            Append(builder, (int)domain.SourceHealth);
            Append(builder, (int?)domain.Provenance ?? -1);
            Append(builder, (int?)domain.Conflict ?? -1);
        }
        AppendSource(builder, input.Affective);
        AppendSource(builder, input.Relationship);
        AppendSource(builder, input.Communication);
        AppendSource(builder, input.PlayerAuthoredIdentity);
        AppendSource(builder, input.BodyAvailability);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendSource<T>(
        StringBuilder builder,
        CharacterProjectionDomainSource<T> source)
        where T : class, ICharacterStateDomainSnapshot
    {
        Append(builder, (int)source.Domain);
        Append(builder, (int)source.Presence);
        Append(builder, (int)source.MissingReason);
        var envelope = source.Envelope;
        if (envelope == null)
            return;
        Append(builder, (int)envelope.Provenance);
        Append(builder, (int)envelope.Conflict);
        switch (envelope.State)
        {
            case AffectiveStateSnapshot state:
                Append(builder, state.SchemaVersion);
                Append(builder, state.Mood);
                Append(builder, state.Energy);
                Append(builder, state.Irritation);
                Append(builder, state.SleepPressure);
                Append(builder, state.RecentInteractionPressure);
                break;
            case RelationshipStateSnapshot state:
                Append(builder, state.SchemaVersion);
                Append(builder, state.Trust);
                Append(builder, state.Closeness);
                Append(builder, state.Familiarity);
                Append(builder, state.Security);
                break;
            case CommunicationContextSnapshot state:
                Append(builder, state.SchemaVersion);
                Append(builder, state.Directness);
                Append(builder, state.Expressiveness);
                Append(builder, (int)state.Signals);
                break;
            case PlayerAuthoredIdentitySnapshot state:
                Append(builder, state.SchemaVersion);
                Append(builder, state.NarrativeKernelVersion);
                AppendAxis(builder, state.Existence);
                AppendAxis(builder, state.Agency);
                AppendAxis(builder, state.World);
                AppendAxis(builder, state.Relationship);
                break;
            case CharacterBodyAvailabilitySnapshot state:
                Append(builder, state.SchemaVersion);
                Append(builder, state.CharacterAvailable);
                Append(builder, state.Grounded);
                Append(builder, state.Sleeping);
                Append(builder, state.DirectPlayerControl);
                Append(builder, (int)state.Clothing);
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported Character projection state domain.");
        }
    }

    private static void AppendAxis(
        StringBuilder builder,
        OpposingIdentityAxisSnapshot axis)
    {
        Append(builder, axis.FirstPoleEvidence);
        Append(builder, axis.SecondPoleEvidence);
        Append(builder, axis.DefinitionStrength);
        Append(builder, axis.Ambivalence);
    }

    private static void Append(StringBuilder builder, object value)
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
}

internal sealed class CharacterProjectionObserveSnapshot
{
    internal CharacterProjectionObserveStatus Status { get; }
    internal bool Enabled { get; }
    internal CharacterInfluenceMode InfluenceMode { get; }
    internal CharacterRuntimeIdentity? RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal int ObservationCount { get; }
    internal int AcceptedCount { get; }
    internal int RejectedCount { get; }
    internal int CoalescedCount { get; }
    internal int FailureEpisodeCoalescedCount { get; }
    internal int RetiredCount { get; }
    internal int RecordCount { get; }
    internal CharacterProjectionObserveRecord? Latest { get; }
    internal CharacterRuntimeStageAAuthorityCounters AuthorityCounters =>
        CharacterRuntimeStageAAuthorityCounters.Zero;
    internal int DecisionOutputs => 0;
    internal int ExpressionOutputs => 0;
    internal int CandidateOutputs => 0;
    internal int MemoryCalls => 0;
    internal int ReasoningCalls => 0;
    internal int ProviderCalls => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int PlayerDataWrites => 0;
    internal int PersistenceWrites => 0;
    internal int ExecutionOutputs => 0;

    internal CharacterProjectionObserveSnapshot(
        CharacterProjectionObserveStatus status,
        bool enabled,
        CharacterInfluenceMode influenceMode,
        CharacterRuntimeIdentity? runtimeIdentity,
        long configurationGeneration,
        int observationCount,
        int acceptedCount,
        int rejectedCount,
        int coalescedCount,
        int failureEpisodeCoalescedCount,
        int retiredCount,
        int recordCount,
        CharacterProjectionObserveRecord? latest)
    {
        Status = status;
        Enabled = enabled;
        InfluenceMode = influenceMode;
        RuntimeIdentity = runtimeIdentity;
        ConfigurationGeneration = configurationGeneration;
        ObservationCount = observationCount;
        AcceptedCount = acceptedCount;
        RejectedCount = rejectedCount;
        CoalescedCount = coalescedCount;
        FailureEpisodeCoalescedCount =
            failureEpisodeCoalescedCount;
        RetiredCount = retiredCount;
        RecordCount = recordCount;
        Latest = latest;
    }

    internal static CharacterProjectionObserveSnapshot Empty { get; } =
        new(
            CharacterProjectionObserveStatus.Off,
            enabled: false,
            CharacterInfluenceMode.Off,
            runtimeIdentity: null,
            configurationGeneration: 0,
            observationCount: 0,
            acceptedCount: 0,
            rejectedCount: 0,
            coalescedCount: 0,
            failureEpisodeCoalescedCount: 0,
            retiredCount: 0,
            recordCount: 0,
            latest: null);
}

internal sealed class CharacterProjectionObserveRuntimeHost
{
    internal const int MaximumRecords = 128;

    private readonly object _gate = new();
    private readonly List<CharacterProjectionObserveRecord> _records =
        new();
    private CharacterProjectionObserveStatus _status =
        CharacterProjectionObserveStatus.Off;
    private bool _enabled;
    private CharacterRuntimeIdentity? _runtimeIdentity;
    private long _configurationGeneration;
    private long _nextSequence = 1;
    private int _observationCount;
    private int _acceptedCount;
    private int _rejectedCount;
    private int _coalescedCount;
    private int _failureEpisodeCoalescedCount;
    private int _retiredCount;

    internal void Reconcile(
        bool enabled,
        bool sessionOpen,
        CharacterRuntimeIdentity? runtimeIdentity,
        DateTimeOffset occurredAt)
    {
        if (!enabled || !sessionOpen)
        {
            lock (_gate)
                ResetToOff();
            return;
        }
        _ = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));

        lock (_gate)
        {
            if (_enabled
                && _runtimeIdentity!.Matches(runtimeIdentity)
                && _status != CharacterProjectionObserveStatus.Fault)
            {
                return;
            }

            if (_enabled)
                _retiredCount += _records.Count;
            _records.Clear();
            _enabled = true;
            _runtimeIdentity = runtimeIdentity;
            _configurationGeneration++;
            _status = CharacterProjectionObserveStatus.ProjectionOnly;
            _nextSequence = 1;
            _observationCount = 0;
            _acceptedCount = 0;
            _rejectedCount = 0;
            _coalescedCount = 0;
            _failureEpisodeCoalescedCount = 0;
        }
    }

    internal CharacterProjectionObserveAdmission Observe(
        CharacterRuntimeStageCAssembly assembly,
        DateTimeOffset observedAt)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        _ = CharacterRuntimeContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));

        lock (_gate)
        {
            if (!_enabled
                || _status == CharacterProjectionObserveStatus.Off)
            {
                return CharacterProjectionObserveAdmission.NotRunning;
            }
            if (_status == CharacterProjectionObserveStatus.Fault)
                return CharacterProjectionObserveAdmission.Faulted;
            if (_status == CharacterProjectionObserveStatus.Capacity)
            {
                return CharacterProjectionObserveAdmission
                    .CapacityReached;
            }
            if (!_runtimeIdentity!.Matches(assembly.Input.RuntimeIdentity))
            {
                throw new InvalidOperationException(
                    "Stage C assembly runtime identity does not match "
                    + "the active Projection Observe host.");
            }

            _observationCount++;
            if (assembly.Result.Accepted)
                _acceptedCount++;
            else
                _rejectedCount++;

            var previous = _records.Count == 0
                ? null
                : _records[^1];
            if (assembly.Result.Accepted
                && previous != null
                && previous.SemanticProjectionFingerprint.Length > 0
                && string.Equals(
                    previous.SemanticProjectionFingerprint,
                    assembly.Result.Projection!
                        .SemanticProjectionFingerprint,
                    StringComparison.Ordinal))
            {
                _records[^1] =
                    previous.CoalesceAccepted(assembly, observedAt);
                _coalescedCount++;
                return CharacterProjectionObserveAdmission
                    .SemanticallyCoalesced;
            }
            if (!assembly.Result.Accepted
                && previous != null
                && previous.FailureEpisodeFingerprint.Length > 0)
            {
                var current =
                    new CharacterProjectionObserveRecord(
                        _nextSequence,
                        assembly,
                        observedAt);
                if (string.Equals(
                        previous.FailureEpisodeFingerprint,
                        current.FailureEpisodeFingerprint,
                        StringComparison.Ordinal))
                {
                    _records[^1] =
                        previous.CoalesceFailureEpisode(
                            assembly,
                            observedAt);
                    _failureEpisodeCoalescedCount++;
                    return CharacterProjectionObserveAdmission
                        .FailureEpisodeCoalesced;
                }
            }

            if (_records.Count >= MaximumRecords)
            {
                _status = CharacterProjectionObserveStatus.Capacity;
                return CharacterProjectionObserveAdmission
                    .CapacityReached;
            }

            _records.Add(
                new CharacterProjectionObserveRecord(
                    _nextSequence++,
                    assembly,
                    observedAt));
            if (_records.Count >= MaximumRecords)
                _status = CharacterProjectionObserveStatus.Capacity;
            return assembly.Result.Accepted
                ? CharacterProjectionObserveAdmission.Recorded
                : CharacterProjectionObserveAdmission
                    .RejectedProjectionRecorded;
        }
    }

    internal void StopByFault(DateTimeOffset occurredAt)
    {
        _ = CharacterRuntimeContractGuard.Timestamp(
            occurredAt,
            nameof(occurredAt));
        lock (_gate)
        {
            _retiredCount += _records.Count;
            _records.Clear();
            _enabled = false;
            _status = CharacterProjectionObserveStatus.Fault;
        }
    }

    internal CharacterProjectionObserveSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_status == CharacterProjectionObserveStatus.Off
                && !_enabled
                && _configurationGeneration == 0)
            {
                return CharacterProjectionObserveSnapshot.Empty;
            }

            return new CharacterProjectionObserveSnapshot(
                _status,
                _enabled,
                _enabled
                    ? CharacterInfluenceMode.ProjectionOnly
                    : CharacterInfluenceMode.Off,
                _runtimeIdentity,
                _configurationGeneration,
                _observationCount,
                _acceptedCount,
                _rejectedCount,
                _coalescedCount,
                _failureEpisodeCoalescedCount,
                _retiredCount,
                _records.Count,
                _records.Count == 0 ? null : _records[^1]);
        }
    }

    private void ResetToOff()
    {
        _records.Clear();
        _status = CharacterProjectionObserveStatus.Off;
        _enabled = false;
        _runtimeIdentity = null;
        _configurationGeneration = 0;
        _nextSequence = 1;
        _observationCount = 0;
        _acceptedCount = 0;
        _rejectedCount = 0;
        _coalescedCount = 0;
        _failureEpisodeCoalescedCount = 0;
        _retiredCount = 0;
    }
}
