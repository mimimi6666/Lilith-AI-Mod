using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithProviderControlPlane;

internal enum ProviderStartupHealthPlanAdmission
{
    Invalid = 0,
    Accepted = 1,
    CapacityExceeded = 2,
    DuplicateConfiguredInstance = 3
}

internal enum ProviderStartupHealthCompletionAdmission
{
    Invalid = 0,
    Accepted = 1,
    AttemptNotScheduled = 2,
    AttemptNotActive = 3,
    DuplicateResult = 4,
    RetiredAttempt = 5,
    RunIdentityMismatch = 6,
    ResultIdentityMismatch = 7,
    InstanceRemoved = 8,
    ConfigurationGenerationMismatch = 9,
    ResultBeforeDispatch = 10,
    ResultExpired = 11,
    SchedulerClosed = 12
}

internal enum ProviderStartupHealthRetirementReason
{
    Invalid = 0,
    ConfigurationChanged = 1,
    InstanceRemoved = 2,
    ApplicationClosing = 3
}

internal enum ProviderStartupHealthSchedulerState
{
    Invalid = 0,
    Ready = 1,
    Running = 2,
    Completed = 3,
    Closed = 4
}

internal sealed class ProviderStartupHealthWorkItem
{
    internal ProviderStartupHealthRunId RunId { get; }
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderCatalogProfileId CatalogProfileId { get; }
    internal ProviderHealthObservationProvenance Provenance { get; }
    internal int ScheduleOrdinal { get; }
    internal int Wave { get; }
    internal int Lane { get; }
    internal int AttemptNumber => 1;
    internal DateTimeOffset NotBefore { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string StaggerKey { get; }
    internal string Fingerprint { get; }

    internal ProviderStartupHealthWorkItem(
        ProviderStartupHealthRunId runId,
        ProviderConfiguredInstance instance,
        int scheduleOrdinal,
        int wave,
        int lane,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string staggerKey)
    {
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        Provider = instance.Provider;
        InstanceId = instance.InstanceId;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        CatalogProfileId = instance.Descriptor.CatalogProfileId;
        Provenance = ProviderHealthObservationProvenance.BoundedStartupCatalog;
        ScheduleOrdinal = ProviderControlPlaneCoreGuard.NonNegative(
            scheduleOrdinal,
            nameof(scheduleOrdinal));
        Wave = ProviderControlPlaneCoreGuard.NonNegative(wave, nameof(wave));
        Lane = ProviderControlPlaneCoreGuard.NonNegative(lane, nameof(lane));
        if (Lane >= ProviderStartupHealthPlanner.MaximumConcurrency
            || Wave != ScheduleOrdinal / ProviderStartupHealthPlanner.MaximumConcurrency
            || Lane != ScheduleOrdinal % ProviderStartupHealthPlanner.MaximumConcurrency)
        {
            throw new ArgumentException(
                "Startup work must use the deterministic two-lane schedule.");
        }
        ProviderControlPlaneCoreGuard.Window(
            notBefore,
            expiresAt,
            nameof(expiresAt));
        NotBefore = notBefore;
        ExpiresAt = expiresAt;
        StaggerKey = ProviderControlPlaneCoreGuard.Sha256(
            staggerKey,
            nameof(staggerKey));
        AttemptId = new ProviderStartupHealthAttemptId(
            "health-"
            + ProviderControlPlaneCoreHash.Of(
                "D033/STARTUP-HEALTH-ATTEMPT-ID/1",
                RunId.Value,
                Provider.Fingerprint,
                InstanceId.Value,
                ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
                ProviderControlPlaneCoreHash.Number(ScheduleOrdinal))
                .Substring(0, 32).ToLowerInvariant());
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/STARTUP-HEALTH-WORK/1",
            RunId.Value,
            AttemptId.Value,
            Provider.Fingerprint,
            InstanceId.Value,
            ProviderControlPlaneCoreHash.Number(ConfigurationGeneration),
            CatalogProfileId.Value,
            ProviderControlPlaneCoreHash.Number((int)Provenance),
            ProviderControlPlaneCoreHash.Number(ScheduleOrdinal),
            ProviderControlPlaneCoreHash.Number(Wave),
            ProviderControlPlaneCoreHash.Number(Lane),
            "1",
            ProviderControlPlaneCoreHash.Timestamp(NotBefore),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt),
            StaggerKey);
    }
}

internal sealed class ProviderStartupHealthPlan
{
    internal ProviderStartupHealthRunId RunId { get; }
    internal long SourceStoreRevision { get; }
    internal DateTimeOffset StartsAt { get; }
    internal TimeSpan WaveStagger { get; }
    internal TimeSpan AttemptLifetime { get; }
    internal int MaximumConcurrency => ProviderStartupHealthPlanner.MaximumConcurrency;
    internal int RetryLimit => 0;
    internal IReadOnlyList<ProviderStartupHealthWorkItem> WorkItems { get; }
    internal string Fingerprint { get; }
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderStartupHealthPlan(
        ProviderStartupHealthRunId runId,
        long sourceStoreRevision,
        DateTimeOffset startsAt,
        TimeSpan waveStagger,
        TimeSpan attemptLifetime,
        IEnumerable<ProviderStartupHealthWorkItem> workItems)
    {
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        SourceStoreRevision = ProviderControlPlaneCoreGuard.NonNegative(
            sourceStoreRevision,
            nameof(sourceStoreRevision));
        StartsAt = ProviderControlPlaneCoreGuard.Timestamp(
            startsAt,
            nameof(startsAt));
        if (waveStagger <= TimeSpan.Zero
            || waveStagger > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(waveStagger));
        }
        if (attemptLifetime <= TimeSpan.Zero
            || attemptLifetime > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(attemptLifetime));
        }
        WaveStagger = waveStagger;
        AttemptLifetime = attemptLifetime;
        var frozen = (workItems ?? throw new ArgumentNullException(nameof(workItems)))
            .OrderBy(value => value.ScheduleOrdinal)
            .ToArray();
        if (frozen.Any(value => value == null)
            || frozen.Select(value => value.AttemptId.Value)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length
            || frozen.Select(value => value.InstanceId.Value)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length
            || !frozen.Select(value => value.ScheduleOrdinal)
                .SequenceEqual(Enumerable.Range(0, frozen.Length)))
        {
            throw new ArgumentException(
                "Startup work must be unique and consecutively ordered.",
                nameof(workItems));
        }
        WorkItems = new ReadOnlyCollection<ProviderStartupHealthWorkItem>(frozen);
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/STARTUP-HEALTH-PLAN/1",
            RunId.Value,
            ProviderControlPlaneCoreHash.Number(SourceStoreRevision),
            ProviderControlPlaneCoreHash.Timestamp(StartsAt),
            WaveStagger.Ticks.ToString(CultureInfo.InvariantCulture),
            AttemptLifetime.Ticks.ToString(CultureInfo.InvariantCulture),
            string.Join(",", frozen.Select(value => value.Fingerprint)));
    }
}

internal sealed class ProviderStartupHealthPlanResult
{
    internal ProviderStartupHealthPlanAdmission Admission { get; }
    internal ProviderStartupHealthPlan? Plan { get; }
    internal bool Accepted => Admission == ProviderStartupHealthPlanAdmission.Accepted;

    internal ProviderStartupHealthPlanResult(
        ProviderStartupHealthPlanAdmission admission,
        ProviderStartupHealthPlan? plan)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(admission, nameof(admission));
        if (Accepted != (plan != null))
        {
            throw new ArgumentException(
                "Only accepted plan results carry a plan.",
                nameof(plan));
        }
        Plan = plan;
    }
}

internal static class ProviderStartupHealthPlanner
{
    internal const int MaximumConcurrency = 2;
    internal const int MaximumConfiguredInstances = 128;

    internal static ProviderStartupHealthPlanResult Create(
        ProviderStartupHealthRunId runId,
        ProviderControlPlaneStoreSnapshot configuredStore,
        DateTimeOffset startsAt,
        TimeSpan waveStagger,
        TimeSpan attemptLifetime)
    {
        if (runId == null)
            throw new ArgumentNullException(nameof(runId));
        if (configuredStore == null)
            throw new ArgumentNullException(nameof(configuredStore));
        ProviderControlPlaneCoreGuard.Timestamp(startsAt, nameof(startsAt));
        if (waveStagger <= TimeSpan.Zero
            || attemptLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waveStagger));
        }

        var configured = configuredStore.Instances.ToArray();
        if (configured.Length > MaximumConfiguredInstances)
        {
            return new ProviderStartupHealthPlanResult(
                ProviderStartupHealthPlanAdmission.CapacityExceeded,
                null);
        }
        if (configured.Select(value => value.InstanceId.Value)
                .Distinct(StringComparer.Ordinal).Count()
            != configured.Length)
        {
            return new ProviderStartupHealthPlanResult(
                ProviderStartupHealthPlanAdmission.DuplicateConfiguredInstance,
                null);
        }

        var ordered = configured
            .Select(instance => new
            {
                Instance = instance,
                StaggerKey = ProviderControlPlaneCoreHash.Of(
                    "D033/STARTUP-HEALTH-STAGGER/1",
                    instance.Provider.Fingerprint,
                    instance.InstanceId.Value)
            })
            .OrderBy(value => value.StaggerKey, StringComparer.Ordinal)
            .ThenBy(value => value.Instance.InstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        var work = ordered.Select((value, index) =>
        {
            var wave = index / MaximumConcurrency;
            var notBefore = startsAt + TimeSpan.FromTicks(
                checked(waveStagger.Ticks * wave));
            return new ProviderStartupHealthWorkItem(
                runId,
                value.Instance,
                index,
                wave,
                index % MaximumConcurrency,
                notBefore,
                notBefore + attemptLifetime,
                value.StaggerKey);
        }).ToArray();
        return new ProviderStartupHealthPlanResult(
            ProviderStartupHealthPlanAdmission.Accepted,
            new ProviderStartupHealthPlan(
                runId,
                configuredStore.Revision,
                startsAt,
                waveStagger,
                attemptLifetime,
                work));
    }
}

internal sealed class ProviderStartupHealthResult
{
    internal ProviderStartupHealthWorkItem WorkItem { get; }
    internal ProviderRuntimeHealthState State { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal ProviderStartupHealthResult(
        ProviderStartupHealthWorkItem workItem,
        ProviderRuntimeHealthState state,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        WorkItem = workItem ?? throw new ArgumentNullException(nameof(workItem));
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        if (State is ProviderRuntimeHealthState.NotObserved
            or ProviderRuntimeHealthState.Scheduled
            or ProviderRuntimeHealthState.InFlight
            or ProviderRuntimeHealthState.ConfigurationChanged
            or ProviderRuntimeHealthState.InstanceRemoved
            or ProviderRuntimeHealthState.StaleResultRejected)
        {
            throw new ArgumentException(
                "A startup result requires an observed terminal condition.",
                nameof(state));
        }
        ProviderControlPlaneCoreGuard.Window(
            observedAt,
            expiresAt,
            nameof(expiresAt));
        if (observedAt < WorkItem.NotBefore
            || observedAt >= WorkItem.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        }
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/STARTUP-HEALTH-RESULT/1",
            WorkItem.Fingerprint,
            ProviderControlPlaneCoreHash.Number((int)State),
            ProviderControlPlaneCoreHash.Timestamp(ObservedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt));
    }
}

internal sealed class ProviderStartupHealthCompletionReceipt
{
    internal ProviderStartupHealthCompletionAdmission Admission { get; }
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal ProviderHealthObservation? Observation { get; }
    internal bool Accepted =>
        Admission == ProviderStartupHealthCompletionAdmission.Accepted;
    internal bool SchedulesRetry => false;
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderStartupHealthCompletionReceipt(
        ProviderStartupHealthCompletionAdmission admission,
        ProviderStartupHealthAttemptId attemptId,
        ProviderHealthObservation? observation)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(
            admission,
            nameof(admission));
        AttemptId = attemptId ?? throw new ArgumentNullException(nameof(attemptId));
        if (Accepted != (observation != null))
        {
            throw new ArgumentException(
                "Only accepted completions carry a health observation.",
                nameof(observation));
        }
        Observation = observation;
    }
}

internal sealed class ProviderStartupHealthRetirement
{
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderStartupHealthRetirementReason Reason { get; }
    internal DateTimeOffset RetiredAt { get; }
    internal bool SchedulesRetry => false;

    internal ProviderStartupHealthRetirement(
        ProviderStartupHealthWorkItem workItem,
        ProviderStartupHealthRetirementReason reason,
        DateTimeOffset retiredAt)
    {
        if (workItem == null)
            throw new ArgumentNullException(nameof(workItem));
        AttemptId = workItem.AttemptId;
        InstanceId = workItem.InstanceId;
        ConfigurationGeneration = workItem.ConfigurationGeneration;
        Reason = ProviderControlPlaneCoreGuard.Defined(reason, nameof(reason));
        RetiredAt = ProviderControlPlaneCoreGuard.Timestamp(
            retiredAt,
            nameof(retiredAt));
    }
}

internal sealed class ProviderStartupHealthSchedulerSnapshot
{
    internal ProviderStartupHealthSchedulerState State { get; }
    internal int Pending { get; }
    internal int Active { get; }
    internal int Completed { get; }
    internal int Retired { get; }
    internal int MaximumObservedConcurrency { get; }
    internal int RetryAttempts => 0;

    internal ProviderStartupHealthSchedulerSnapshot(
        ProviderStartupHealthSchedulerState state,
        int pending,
        int active,
        int completed,
        int retired,
        int maximumObservedConcurrency)
    {
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        Pending = ProviderControlPlaneCoreGuard.NonNegative(pending, nameof(pending));
        Active = ProviderControlPlaneCoreGuard.NonNegative(active, nameof(active));
        Completed = ProviderControlPlaneCoreGuard.NonNegative(
            completed,
            nameof(completed));
        Retired = ProviderControlPlaneCoreGuard.NonNegative(retired, nameof(retired));
        MaximumObservedConcurrency = ProviderControlPlaneCoreGuard.NonNegative(
            maximumObservedConcurrency,
            nameof(maximumObservedConcurrency));
        if (Active > ProviderStartupHealthPlanner.MaximumConcurrency
            || MaximumObservedConcurrency
                > ProviderStartupHealthPlanner.MaximumConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(active));
        }
    }
}

internal sealed class ProviderStartupHealthScheduler
{
    private readonly ProviderStartupHealthPlan _plan;
    private readonly List<ProviderStartupHealthWorkItem> _pending;
    private readonly Dictionary<string, ProviderStartupHealthWorkItem> _active =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);
    private bool _closed;
    private int _maximumObservedConcurrency;

    internal ProviderStartupHealthScheduler(ProviderStartupHealthPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _pending = plan.WorkItems.ToList();
    }

    internal ProviderStartupHealthSchedulerSnapshot GetSnapshot()
    {
        var state = _closed
            ? ProviderStartupHealthSchedulerState.Closed
            : _pending.Count == 0 && _active.Count == 0
                ? ProviderStartupHealthSchedulerState.Completed
                : _active.Count == 0
                    ? ProviderStartupHealthSchedulerState.Ready
                    : ProviderStartupHealthSchedulerState.Running;
        return new ProviderStartupHealthSchedulerSnapshot(
            state,
            _pending.Count,
            _active.Count,
            _completed.Count,
            _retired.Count,
            _maximumObservedConcurrency);
    }

    internal IReadOnlyList<ProviderStartupHealthWorkItem> TakeReady(
        DateTimeOffset now)
    {
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        if (_closed || _active.Count >= ProviderStartupHealthPlanner.MaximumConcurrency)
        {
            return Array.Empty<ProviderStartupHealthWorkItem>();
        }
        foreach (var expired in _pending
                     .Where(value => value.ExpiresAt <= now)
                     .ToArray())
        {
            Retire(expired);
        }
        var capacity = ProviderStartupHealthPlanner.MaximumConcurrency
            - _active.Count;
        var ready = _pending
            .Where(value => value.NotBefore <= now && value.ExpiresAt > now)
            .OrderBy(value => value.ScheduleOrdinal)
            .Take(capacity)
            .ToArray();
        foreach (var workItem in ready)
        {
            _pending.Remove(workItem);
            _active.Add(workItem.AttemptId.Value, workItem);
        }
        _maximumObservedConcurrency = Math.Max(
            _maximumObservedConcurrency,
            _active.Count);
        return new ReadOnlyCollection<ProviderStartupHealthWorkItem>(ready);
    }

    internal ProviderStartupHealthCompletionReceipt TryComplete(
        ProviderStartupHealthResult result,
        ProviderControlPlaneStoreSnapshot currentStore,
        DateTimeOffset receivedAt)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (currentStore == null)
            throw new ArgumentNullException(nameof(currentStore));
        ProviderControlPlaneCoreGuard.Timestamp(receivedAt, nameof(receivedAt));
        var attemptId = result.WorkItem.AttemptId;
        if (_closed)
            return Reject(ProviderStartupHealthCompletionAdmission.SchedulerClosed, attemptId);
        if (_completed.Contains(attemptId.Value))
            return Reject(ProviderStartupHealthCompletionAdmission.DuplicateResult, attemptId);
        if (_retired.Contains(attemptId.Value))
            return Reject(ProviderStartupHealthCompletionAdmission.RetiredAttempt, attemptId);
        if (!_active.TryGetValue(attemptId.Value, out var scheduled))
        {
            return Reject(
                _plan.WorkItems.Any(value => value.AttemptId.Equals(attemptId))
                    ? ProviderStartupHealthCompletionAdmission.AttemptNotActive
                    : ProviderStartupHealthCompletionAdmission.AttemptNotScheduled,
                attemptId);
        }
        if (!scheduled.RunId.Equals(result.WorkItem.RunId))
            return Reject(ProviderStartupHealthCompletionAdmission.RunIdentityMismatch, attemptId);
        if (!string.Equals(
                scheduled.Fingerprint,
                result.WorkItem.Fingerprint,
                StringComparison.Ordinal))
        {
            return Reject(ProviderStartupHealthCompletionAdmission.ResultIdentityMismatch, attemptId);
        }
        var current = currentStore.FindInstance(scheduled.InstanceId);
        if (current == null)
            return RetireAndReject(
                scheduled,
                ProviderStartupHealthCompletionAdmission.InstanceRemoved);
        if (!current.Provider.Equals(scheduled.Provider)
            || current.ConfigurationGeneration
                != scheduled.ConfigurationGeneration)
        {
            return RetireAndReject(
                scheduled,
                ProviderStartupHealthCompletionAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (result.ObservedAt < scheduled.NotBefore
            || receivedAt < result.ObservedAt)
        {
            return Reject(
                ProviderStartupHealthCompletionAdmission.ResultBeforeDispatch,
                attemptId);
        }
        if (result.ObservedAt >= scheduled.ExpiresAt
            || receivedAt >= scheduled.ExpiresAt)
        {
            return RetireAndReject(
                scheduled,
                ProviderStartupHealthCompletionAdmission.ResultExpired);
        }

        _active.Remove(attemptId.Value);
        _completed.Add(attemptId.Value);
        var observation = new ProviderHealthObservation(
            attemptId,
            scheduled.Provider,
            scheduled.InstanceId,
            scheduled.ConfigurationGeneration,
            result.State,
            scheduled.Provenance,
            result.ObservedAt,
            result.ExpiresAt);
        return new ProviderStartupHealthCompletionReceipt(
            ProviderStartupHealthCompletionAdmission.Accepted,
            attemptId,
            observation);
    }

    internal IReadOnlyList<ProviderStartupHealthRetirement> Reconcile(
        ProviderControlPlaneStoreSnapshot currentStore,
        DateTimeOffset now)
    {
        if (currentStore == null)
            throw new ArgumentNullException(nameof(currentStore));
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        if (_closed)
            return Array.Empty<ProviderStartupHealthRetirement>();

        var candidates = _pending.Concat(_active.Values).ToArray();
        var retirements = new List<ProviderStartupHealthRetirement>();
        foreach (var workItem in candidates)
        {
            var current = currentStore.FindInstance(workItem.InstanceId);
            ProviderStartupHealthRetirementReason? reason = current == null
                ? ProviderStartupHealthRetirementReason.InstanceRemoved
                : !current.Provider.Equals(workItem.Provider)
                  || current.ConfigurationGeneration
                      != workItem.ConfigurationGeneration
                    ? ProviderStartupHealthRetirementReason.ConfigurationChanged
                    : null;
            if (!reason.HasValue)
                continue;
            Retire(workItem);
            retirements.Add(new ProviderStartupHealthRetirement(
                workItem,
                reason.Value,
                now));
        }
        return new ReadOnlyCollection<ProviderStartupHealthRetirement>(
            retirements);
    }

    internal IReadOnlyList<ProviderStartupHealthRetirement> Close(
        DateTimeOffset now)
    {
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        if (_closed)
            return Array.Empty<ProviderStartupHealthRetirement>();
        var candidates = _pending.Concat(_active.Values)
            .OrderBy(value => value.ScheduleOrdinal)
            .ToArray();
        var retirements = candidates.Select(workItem =>
        {
            Retire(workItem);
            return new ProviderStartupHealthRetirement(
                workItem,
                ProviderStartupHealthRetirementReason.ApplicationClosing,
                now);
        }).ToArray();
        _closed = true;
        return new ReadOnlyCollection<ProviderStartupHealthRetirement>(retirements);
    }

    private ProviderStartupHealthCompletionReceipt RetireAndReject(
        ProviderStartupHealthWorkItem workItem,
        ProviderStartupHealthCompletionAdmission admission)
    {
        Retire(workItem);
        return Reject(admission, workItem.AttemptId);
    }

    private void Retire(ProviderStartupHealthWorkItem workItem)
    {
        _pending.Remove(workItem);
        _active.Remove(workItem.AttemptId.Value);
        _retired.Add(workItem.AttemptId.Value);
    }

    private static ProviderStartupHealthCompletionReceipt Reject(
        ProviderStartupHealthCompletionAdmission admission,
        ProviderStartupHealthAttemptId attemptId)
        => new(admission, attemptId, null);
}

internal sealed class ProviderStartupHealthReplayResult
{
    internal IReadOnlyList<ProviderStartupHealthAttemptId> DispatchOrder { get; }
    internal IReadOnlyList<ProviderStartupHealthCompletionAdmission> Completions { get; }
    internal int MaximumObservedConcurrency { get; }
    internal int RetryAttempts => 0;
    internal string DeterministicFingerprint { get; }
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderStartupHealthReplayResult(
        IEnumerable<ProviderStartupHealthAttemptId> dispatchOrder,
        IEnumerable<ProviderStartupHealthCompletionAdmission> completions,
        int maximumObservedConcurrency,
        string planFingerprint)
    {
        DispatchOrder = new ReadOnlyCollection<ProviderStartupHealthAttemptId>(
            (dispatchOrder ?? throw new ArgumentNullException(nameof(dispatchOrder)))
            .ToArray());
        Completions = new ReadOnlyCollection<ProviderStartupHealthCompletionAdmission>(
            (completions ?? throw new ArgumentNullException(nameof(completions)))
            .Select(value => ProviderControlPlaneCoreGuard.Defined(
                value,
                nameof(completions)))
            .ToArray());
        MaximumObservedConcurrency = ProviderControlPlaneCoreGuard.NonNegative(
            maximumObservedConcurrency,
            nameof(maximumObservedConcurrency));
        DeterministicFingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/STARTUP-HEALTH-REPLAY/1",
            ProviderControlPlaneCoreGuard.Sha256(
                planFingerprint,
                nameof(planFingerprint)),
            string.Join(",", DispatchOrder.Select(value => value.Value)),
            string.Join(",", Completions.Select(value =>
                ProviderControlPlaneCoreHash.Number((int)value))),
            ProviderControlPlaneCoreHash.Number(MaximumObservedConcurrency),
            "0");
    }
}

internal static class ProviderStartupHealthReplay
{
    internal static ProviderStartupHealthReplayResult Run(
        ProviderStartupHealthPlan plan,
        ProviderControlPlaneStoreSnapshot configuredStore,
        ProviderRuntimeHealthState terminalState,
        TimeSpan observationFreshness)
    {
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (configuredStore == null)
            throw new ArgumentNullException(nameof(configuredStore));
        if (observationFreshness <= TimeSpan.Zero
            || observationFreshness > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(observationFreshness));
        }
        var scheduler = new ProviderStartupHealthScheduler(plan);
        var dispatchOrder = new List<ProviderStartupHealthAttemptId>();
        var completions = new List<ProviderStartupHealthCompletionAdmission>();
        foreach (var wave in plan.WorkItems.GroupBy(value => value.Wave)
                     .OrderBy(value => value.Key))
        {
            var now = wave.Min(value => value.NotBefore);
            var ready = scheduler.TakeReady(now);
            foreach (var workItem in ready)
            {
                dispatchOrder.Add(workItem.AttemptId);
                var observedAt = workItem.NotBefore.AddTicks(1);
                var result = new ProviderStartupHealthResult(
                    workItem,
                    terminalState,
                    observedAt,
                    observedAt + observationFreshness);
                completions.Add(scheduler.TryComplete(
                    result,
                    configuredStore,
                    observedAt).Admission);
            }
        }
        return new ProviderStartupHealthReplayResult(
            dispatchOrder,
            completions,
            scheduler.GetSnapshot().MaximumObservedConcurrency,
            plan.Fingerprint);
    }
}
