using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LilithProviderControlPlane;

internal enum UnifiedProviderStartupHealthHostState
{
    Invalid = 0,
    Disabled = 1,
    Completed = 2,
    Cancelled = 3,
    PlanRejected = 4,
    Faulted = 5,
    ConcurrentRunRejected = 6
}

internal enum UnifiedProviderStartupEndpointAdmission
{
    Invalid = 0,
    Accepted = 1,
    Rejected = 2
}

internal enum UnifiedProviderStartupHealthResultAdmission
{
    Invalid = 0,
    Accepted = 1,
    EndpointRejected = 2,
    CredentialRejected = 3,
    ResultIdentityMismatch = 4,
    ResultTimingMismatch = 5,
    ConfigurationChanged = 6,
    InstanceRemoved = 7,
    ApplicationClosing = 8,
    Expired = 9,
    LateResultRejected = 10,
    ProbeFault = 11
}

internal enum UnifiedProviderStartupHealthReason
{
    Invalid = 0,
    CatalogHealthy = 1,
    CatalogDegraded = 2,
    ManualSelectionRequired = 3,
    AuthenticationRejected = 4,
    RateLimited = 5,
    EndpointRejected = 6,
    ServiceUnavailable = 7,
    NetworkFailure = 8,
    Timeout = 9,
    Cancelled = 10,
    ProtocolRejected = 11,
    ObservationUnsupported = 12,
    ConfigurationChanged = 13,
    InstanceRemoved = 14,
    ApplicationClosing = 15,
    ResultExpired = 16,
    LateResultRejected = 17,
    CredentialRejected = 18,
    ProbeFault = 19,
    ResultIdentityMismatch = 20,
    ResultTimingMismatch = 21
}

internal interface IUnifiedProviderStartupHealthSnapshotSource
{
    UnifiedProviderControlPlaneSnapshot Capture();
}

internal interface IUnifiedProviderStartupHealthCredentialSource
{
    (UnifiedProviderStoreAdmission Admission, UnifiedProviderCredentialLease? Lease)
        OpenCredential(UnifiedProviderConfiguredInstance instance);
}

internal interface IUnifiedProviderStartupEndpointPolicy
{
    UnifiedProviderStartupEndpointAdmission Admit(
        UnifiedProviderConfiguredInstance instance);
}

internal interface IUnifiedProviderStartupHealthProbe
{
    Task<ProviderCatalogNetworkResult> ObserveOnceAsync(
        ProviderCatalogNetworkTarget target,
        UnifiedProviderCredentialLease credentialLease,
        CancellationToken cancellationToken);
}

internal sealed class UnifiedProviderStoreStartupHealthSource :
    IUnifiedProviderStartupHealthSnapshotSource,
    IUnifiedProviderStartupHealthCredentialSource
{
    private readonly UnifiedProviderControlPlaneStore _store;

    internal UnifiedProviderStoreStartupHealthSource(
        UnifiedProviderControlPlaneStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public UnifiedProviderControlPlaneSnapshot Capture()
    {
        var loaded = _store.Load();
        return loaded.Accepted && loaded.Snapshot != null
            ? loaded.Snapshot
            : throw new InvalidOperationException(
                "The configured Provider snapshot is unavailable.");
    }

    public (UnifiedProviderStoreAdmission Admission, UnifiedProviderCredentialLease? Lease)
        OpenCredential(UnifiedProviderConfiguredInstance instance)
        => _store.OpenCredential(instance);
}

internal sealed class UnifiedProviderCatalogStartupHealthProbe :
    IUnifiedProviderStartupHealthProbe
{
    private readonly UnifiedProviderCatalogNetworkClient _client;

    internal UnifiedProviderCatalogStartupHealthProbe(
        UnifiedProviderCatalogNetworkClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<ProviderCatalogNetworkResult> ObserveOnceAsync(
        ProviderCatalogNetworkTarget target,
        UnifiedProviderCredentialLease credentialLease,
        CancellationToken cancellationToken)
        => _client.DiscoverConfiguredOnceAsync(
            target,
            credentialLease,
            cancellationToken);
}

internal sealed class UnifiedProviderStartupHealthRuntimeOptions
{
    internal static UnifiedProviderStartupHealthRuntimeOptions DefaultOff { get; } =
        new(
            enabled: false,
            waveStagger: TimeSpan.FromMilliseconds(25),
            attemptLifetime: TimeSpan.FromSeconds(10),
            resultFreshness: TimeSpan.FromMinutes(5),
            pollInterval: TimeSpan.FromMilliseconds(25));

    internal bool Enabled { get; }
    internal TimeSpan WaveStagger { get; }
    internal TimeSpan AttemptLifetime { get; }
    internal TimeSpan ResultFreshness { get; }
    internal TimeSpan PollInterval { get; }
    internal int MaximumConcurrency => ProviderStartupHealthPlanner.MaximumConcurrency;
    internal int RetryLimit => 0;
    internal int FallbackLimit => 0;

    internal UnifiedProviderStartupHealthRuntimeOptions(
        bool enabled,
        TimeSpan waveStagger,
        TimeSpan attemptLifetime,
        TimeSpan resultFreshness,
        TimeSpan pollInterval)
    {
        if (waveStagger <= TimeSpan.Zero || waveStagger > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(waveStagger));
        if (attemptLifetime <= TimeSpan.Zero
            || attemptLifetime > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(attemptLifetime));
        }
        if (resultFreshness <= TimeSpan.Zero
            || resultFreshness > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(resultFreshness));
        }
        if (pollInterval <= TimeSpan.Zero || pollInterval > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        Enabled = enabled;
        WaveStagger = waveStagger;
        AttemptLifetime = attemptLifetime;
        ResultFreshness = resultFreshness;
        PollInterval = pollInterval;
    }
}

internal sealed class UnifiedProviderStartupHealthObservation
{
    internal ProviderStartupHealthRunId RunId { get; }
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal string UnifiedInstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal ProviderRuntimeHealthState State { get; }
    internal UnifiedProviderStartupHealthReason Reason { get; }
    internal UnifiedProviderStartupHealthResultAdmission Admission { get; }
    internal ProviderStartupHealthCompletionAdmission? SchedulerAdmission { get; }
    internal ProviderCatalogNetworkAdmission? NetworkAdmission { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal int NetworkCalls { get; }
    internal bool LateResultRejected { get; }
    internal ProviderHealthPresentation Presentation { get; }
    internal bool RetainsRawBody => false;
    internal bool RetainsSecret => false;
    internal bool SelectsModel => false;
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal string Fingerprint { get; }

    internal UnifiedProviderStartupHealthObservation(
        ProviderStartupHealthWorkItem workItem,
        UnifiedProviderConfiguredInstance instance,
        ProviderRuntimeHealthState state,
        UnifiedProviderStartupHealthReason reason,
        UnifiedProviderStartupHealthResultAdmission admission,
        ProviderStartupHealthCompletionAdmission? schedulerAdmission,
        ProviderCatalogNetworkAdmission? networkAdmission,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls,
        bool lateResultRejected)
    {
        if (workItem == null)
            throw new ArgumentNullException(nameof(workItem));
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        Reason = ProviderControlPlaneCoreGuard.Defined(reason, nameof(reason));
        Admission = ProviderControlPlaneCoreGuard.Defined(admission, nameof(admission));
        if (schedulerAdmission.HasValue)
        {
            ProviderControlPlaneCoreGuard.Defined(
                schedulerAdmission.Value,
                nameof(schedulerAdmission));
        }
        if (networkAdmission.HasValue
            && (!Enum.IsDefined(typeof(ProviderCatalogNetworkAdmission), networkAdmission.Value)
                || networkAdmission.Value == ProviderCatalogNetworkAdmission.Invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(networkAdmission));
        }
        ProviderControlPlaneCoreGuard.Window(observedAt, expiresAt, nameof(expiresAt));
        if (networkCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        if (!string.Equals(
                workItem.InstanceId.Value,
                instance.InstanceId,
                StringComparison.Ordinal)
            || workItem.Provider.Kind != instance.ProviderKind
            || workItem.ConfigurationGeneration != instance.ConfigurationGeneration)
        {
            throw new ArgumentException(
                "The health observation must retain the scheduled instance identity.",
                nameof(instance));
        }
        RunId = workItem.RunId;
        AttemptId = workItem.AttemptId;
        Provider = workItem.Provider;
        UnifiedInstanceId = instance.InstanceId;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        ConfigurationFingerprint = instance.ConfigurationFingerprint;
        SchedulerAdmission = schedulerAdmission;
        NetworkAdmission = networkAdmission;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        NetworkCalls = networkCalls;
        LateResultRejected = lateResultRejected;
        Presentation = ProviderHealthPresentationPolicy.Map(State);
        Fingerprint = ProviderControlPlaneCoreHash.Of(
            "D033/UNIFIED-STARTUP-HEALTH-OBSERVATION/1",
            workItem.Fingerprint,
            ConfigurationFingerprint,
            ProviderControlPlaneCoreHash.Number((int)State),
            ProviderControlPlaneCoreHash.Number((int)Reason),
            ProviderControlPlaneCoreHash.Number((int)Admission),
            schedulerAdmission.HasValue
                ? ProviderControlPlaneCoreHash.Number((int)schedulerAdmission.Value)
                : string.Empty,
            networkAdmission.HasValue
                ? ProviderControlPlaneCoreHash.Number((int)networkAdmission.Value)
                : string.Empty,
            ProviderControlPlaneCoreHash.Timestamp(ObservedAt),
            ProviderControlPlaneCoreHash.Timestamp(ExpiresAt),
            ProviderControlPlaneCoreHash.Number(NetworkCalls),
            LateResultRejected ? "1" : "0");
    }

    internal bool IsCurrent(DateTimeOffset now)
        => now >= ObservedAt && now < ExpiresAt;
}

internal sealed class UnifiedProviderStartupHealthRunResult
{
    internal UnifiedProviderStartupHealthHostState State { get; }
    internal ProviderStartupHealthPlanAdmission? PlanAdmission { get; }
    internal IReadOnlyList<UnifiedProviderStartupHealthObservation> Results { get; }
    internal int MaximumObservedConcurrency { get; }
    internal int CredentialLeaseOpens { get; }
    internal int NetworkCalls { get; }
    internal int RetryAttempts => 0;
    internal int FallbackAttempts => 0;
    internal string SourceSnapshotFingerprint { get; }
    internal string PlanFingerprint { get; }
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal UnifiedProviderStartupHealthRunResult(
        UnifiedProviderStartupHealthHostState state,
        ProviderStartupHealthPlanAdmission? planAdmission,
        IEnumerable<UnifiedProviderStartupHealthObservation> results,
        int maximumObservedConcurrency,
        int credentialLeaseOpens,
        int networkCalls,
        string sourceSnapshotFingerprint,
        string planFingerprint)
    {
        State = ProviderControlPlaneCoreGuard.Defined(state, nameof(state));
        if (planAdmission.HasValue)
        {
            ProviderControlPlaneCoreGuard.Defined(
                planAdmission.Value,
                nameof(planAdmission));
        }
        PlanAdmission = planAdmission;
        var frozen = (results ?? throw new ArgumentNullException(nameof(results)))
            .OrderBy(value => value.AttemptId.Value, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Any(value => value == null)
            || frozen.Select(value => value.AttemptId.Value)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        {
            throw new ArgumentException(
                "Run results must contain one final record per attempt.",
                nameof(results));
        }
        Results = new ReadOnlyCollection<UnifiedProviderStartupHealthObservation>(frozen);
        MaximumObservedConcurrency = ProviderControlPlaneCoreGuard.NonNegative(
            maximumObservedConcurrency,
            nameof(maximumObservedConcurrency));
        if (MaximumObservedConcurrency > ProviderStartupHealthPlanner.MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(maximumObservedConcurrency));
        CredentialLeaseOpens = ProviderControlPlaneCoreGuard.NonNegative(
            credentialLeaseOpens,
            nameof(credentialLeaseOpens));
        NetworkCalls = ProviderControlPlaneCoreGuard.NonNegative(
            networkCalls,
            nameof(networkCalls));
        SourceSnapshotFingerprint = string.IsNullOrEmpty(sourceSnapshotFingerprint)
            ? string.Empty
            : ProviderControlPlaneCoreGuard.Sha256(
                sourceSnapshotFingerprint,
                nameof(sourceSnapshotFingerprint));
        PlanFingerprint = string.IsNullOrEmpty(planFingerprint)
            ? string.Empty
            : ProviderControlPlaneCoreGuard.Sha256(
                planFingerprint,
                nameof(planFingerprint));
    }
}

internal sealed class UnifiedProviderStartupHealthRuntimeHost
{
    private readonly IUnifiedProviderStartupHealthSnapshotSource _snapshots;
    private readonly IUnifiedProviderStartupHealthCredentialSource _credentials;
    private readonly IUnifiedProviderStartupHealthProbe _probe;
    private readonly IUnifiedProviderStartupEndpointPolicy? _endpointPolicy;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<UnifiedProviderStartupHealthObservation>? _onResult;
    private int _runActive;

    internal UnifiedProviderStartupHealthRuntimeHost(
        IUnifiedProviderStartupHealthSnapshotSource snapshots,
        IUnifiedProviderStartupHealthCredentialSource credentials,
        IUnifiedProviderStartupHealthProbe probe,
        IUnifiedProviderStartupEndpointPolicy? endpointPolicy = null,
        Func<DateTimeOffset>? clock = null,
        Action<UnifiedProviderStartupHealthObservation>? onResult = null)
    {
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _endpointPolicy = endpointPolicy;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _onResult = onResult;
    }

    internal async Task<UnifiedProviderStartupHealthRunResult> RunOnceAsync(
        ProviderStartupHealthRunId runId,
        UnifiedProviderStartupHealthRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (runId == null)
            throw new ArgumentNullException(nameof(runId));
        options ??= UnifiedProviderStartupHealthRuntimeOptions.DefaultOff;
        if (!options.Enabled)
        {
            return new UnifiedProviderStartupHealthRunResult(
                UnifiedProviderStartupHealthHostState.Disabled,
                null,
                Array.Empty<UnifiedProviderStartupHealthObservation>(),
                0,
                0,
                0,
                string.Empty,
                string.Empty);
        }

        if (Interlocked.CompareExchange(ref _runActive, 1, 0) != 0)
        {
            return new UnifiedProviderStartupHealthRunResult(
                UnifiedProviderStartupHealthHostState.ConcurrentRunRejected,
                null,
                Array.Empty<UnifiedProviderStartupHealthObservation>(),
                0,
                0,
                0,
                string.Empty,
                string.Empty);
        }

        var run = new RunExecutionState();
        try
        {
            return await RunEnabledOnceAsync(
                    runId,
                    options,
                    run,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _runActive, 0);
        }
    }

    private async Task<UnifiedProviderStartupHealthRunResult> RunEnabledOnceAsync(
        ProviderStartupHealthRunId runId,
        UnifiedProviderStartupHealthRuntimeOptions options,
        RunExecutionState run,
        CancellationToken cancellationToken)
    {

        UnifiedProviderControlPlaneSnapshot initialUnified;
        try
        {
            initialUnified = _snapshots.Capture();
        }
        catch
        {
            return new UnifiedProviderStartupHealthRunResult(
                UnifiedProviderStartupHealthHostState.Faulted,
                null,
                Array.Empty<UnifiedProviderStartupHealthObservation>(),
                0,
                0,
                0,
                string.Empty,
                string.Empty);
        }
        var initialCore = ProjectCoreSnapshot(initialUnified);
        // AttemptLifetime is an execution-slot deadline, not a queue-wait
        // deadline. The planner still needs a bounded envelope wide enough for
        // every deterministic two-lane wave to reach dispatch. Each active
        // attempt receives its own AttemptLifetime cancellation below.
        var waves = Math.Max(
            1,
            (initialCore.Instances.Count
             + ProviderStartupHealthPlanner.MaximumConcurrency - 1)
            / ProviderStartupHealthPlanner.MaximumConcurrency);
        var completionGrace = options.PollInterval + options.PollInterval;
        if (completionGrace < TimeSpan.FromSeconds(1))
            completionGrace = TimeSpan.FromSeconds(1);
        var maximumEnvelopeTicks = TimeSpan.FromMinutes(5).Ticks;
        var waveBudgetTicks = options.AttemptLifetime.Ticks >
                              (maximumEnvelopeTicks - completionGrace.Ticks) / waves
            ? maximumEnvelopeTicks - completionGrace.Ticks
            : options.AttemptLifetime.Ticks * waves;
        var queueEnvelopeTicks = Math.Min(
            maximumEnvelopeTicks,
            waveBudgetTicks + completionGrace.Ticks);
        var queueEnvelope = TimeSpan.FromTicks(queueEnvelopeTicks);
        var planResult = ProviderStartupHealthPlanner.Create(
            runId,
            initialCore,
            _clock(),
            options.WaveStagger,
            queueEnvelope);
        if (!planResult.Accepted)
        {
            return new UnifiedProviderStartupHealthRunResult(
                UnifiedProviderStartupHealthHostState.PlanRejected,
                planResult.Admission,
                Array.Empty<UnifiedProviderStartupHealthObservation>(),
                0,
                0,
                0,
                initialUnified.SnapshotFingerprint,
                string.Empty);
        }

        var plan = planResult.Plan!;
        var scheduler = new ProviderStartupHealthScheduler(plan);
        var scheduledInstances = initialUnified.Instances.ToDictionary(
            value => value.InstanceId,
            StringComparer.Ordinal);
        var active = new Dictionary<string, ActiveAttempt>(StringComparer.Ordinal);
        var terminal = new Dictionary<string, UnifiedProviderStartupHealthObservation>(
            StringComparer.Ordinal);
        var hostState = UnifiedProviderStartupHealthHostState.Completed;
        var closing = false;

        while (true)
        {
            var now = _clock();
            if (!closing && cancellationToken.IsCancellationRequested)
            {
                hostState = UnifiedProviderStartupHealthHostState.Cancelled;
                closing = true;
                foreach (var retirement in scheduler.Close(now))
                {
                    if (active.TryGetValue(retirement.AttemptId.Value, out var attempt))
                        attempt.Cancel();
                    Publish(terminal, RetirementObservation(
                        retirement,
                        plan.WorkItems.Single(value =>
                            value.AttemptId.Equals(retirement.AttemptId)),
                        scheduledInstances,
                        options.ResultFreshness));
                }
            }

            UnifiedProviderControlPlaneSnapshot? currentUnified = null;
            ProviderControlPlaneStoreSnapshot? currentCore = null;
            if (!closing)
            {
                try
                {
                    currentUnified = _snapshots.Capture();
                    currentCore = ProjectCoreSnapshot(currentUnified);
                }
                catch
                {
                    hostState = UnifiedProviderStartupHealthHostState.Faulted;
                    closing = true;
                    foreach (var retirement in scheduler.Close(now))
                    {
                        if (active.TryGetValue(retirement.AttemptId.Value, out var attempt))
                            attempt.Cancel();
                        Publish(terminal, RetirementObservation(
                            retirement,
                            plan.WorkItems.Single(value =>
                                value.AttemptId.Equals(retirement.AttemptId)),
                            scheduledInstances,
                            options.ResultFreshness));
                    }
                }
            }

            if (!closing && currentCore != null)
            {
                foreach (var retirement in scheduler.Reconcile(currentCore, now))
                {
                    if (active.TryGetValue(retirement.AttemptId.Value, out var attempt))
                        attempt.Cancel();
                    Publish(terminal, RetirementObservation(
                        retirement,
                        plan.WorkItems.Single(value =>
                            value.AttemptId.Equals(retirement.AttemptId)),
                        scheduledInstances,
                        options.ResultFreshness));
                }
            }

            foreach (var finished in active.Values
                         .Where(value => value.Task.IsCompleted)
                         .ToArray())
            {
                var outcome = await finished.Task.ConfigureAwait(false);
                active.Remove(outcome.WorkItem.AttemptId.Value);
                ProcessOutcome(
                    scheduler,
                    outcome,
                    currentUnified,
                    currentCore,
                    scheduledInstances,
                    terminal,
                    options,
                    closing);
                finished.Dispose();
            }

            if (closing)
            {
                if (active.Count == 0)
                    break;
                await Task.WhenAny(active.Values.Select(value => value.Task))
                    .ConfigureAwait(false);
                continue;
            }

            now = _clock();
            foreach (var expired in plan.WorkItems.Where(workItem =>
                         workItem.ExpiresAt <= now
                         && !active.ContainsKey(workItem.AttemptId.Value)
                         && !terminal.ContainsKey(workItem.AttemptId.Value)))
            {
                Publish(terminal, CreateObservation(
                    expired,
                    scheduledInstances[expired.InstanceId.Value],
                    ProviderRuntimeHealthState.Expired,
                    UnifiedProviderStartupHealthReason.ResultExpired,
                    UnifiedProviderStartupHealthResultAdmission.Expired,
                    null,
                    null,
                    now,
                    now + options.ResultFreshness,
                    0,
                    false));
            }

            // Retired-but-still-draining probes continue to occupy real execution
            // capacity. Do not let a Scheduler retirement transiently exceed two.
            if (!active.Values.Any(value => value.Retired))
            {
                foreach (var workItem in scheduler.TakeReady(now))
                {
                    var instance = scheduledInstances[workItem.InstanceId.Value];
                    var attempt = new ActiveAttempt(
                        workItem,
                        cancellationToken,
                        options.AttemptLifetime);
                    active.Add(workItem.AttemptId.Value, attempt);
                    attempt.Start(ExecuteAttemptAsync(
                        attempt,
                        instance,
                        options,
                        run));
                }
            }

            var schedulerSnapshot = scheduler.GetSnapshot();
            if (schedulerSnapshot.State == ProviderStartupHealthSchedulerState.Completed
                && active.Count == 0)
            {
                break;
            }

            var wait = options.PollInterval;
            if (active.Count == 0)
            {
                var next = plan.WorkItems
                    .Where(value => !terminal.ContainsKey(value.AttemptId.Value)
                                    && value.NotBefore > now)
                    .Select(value => value.NotBefore - now)
                    .DefaultIfEmpty(wait)
                    .Min();
                if (next > TimeSpan.Zero && next < wait)
                    wait = next;
            }
            var delay = Task.Delay(wait);
            if (active.Count == 0)
            {
                await delay.ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAny(
                        active.Values.Select(value => (Task)value.Task)
                            .Append(delay))
                    .ConfigureAwait(false);
            }
        }

        var schedulerFinal = scheduler.GetSnapshot();
        return new UnifiedProviderStartupHealthRunResult(
            hostState,
            planResult.Admission,
            terminal.Values,
            schedulerFinal.MaximumObservedConcurrency,
            Volatile.Read(ref run.CredentialLeaseOpens),
            terminal.Values.Sum(value => value.NetworkCalls),
            initialUnified.SnapshotFingerprint,
            plan.Fingerprint);
    }

    private async Task<AttemptOutcome> ExecuteAttemptAsync(
        ActiveAttempt attempt,
        UnifiedProviderConfiguredInstance instance,
        UnifiedProviderStartupHealthRuntimeOptions options,
        RunExecutionState run)
    {
        var observedAt = _clock();
        try
        {
            attempt.Token.ThrowIfCancellationRequested();
            var endpointAdmission = _endpointPolicy?.Admit(instance)
                ?? UnifiedProviderStartupEndpointAdmission.Rejected;
            if (endpointAdmission != UnifiedProviderStartupEndpointAdmission.Accepted)
            {
                return AttemptOutcome.EndpointRejected(
                    attempt.WorkItem,
                    observedAt,
                    observedAt + options.ResultFreshness);
            }

            attempt.Token.ThrowIfCancellationRequested();
            var opened = _credentials.OpenCredential(instance);
            Interlocked.Increment(ref run.CredentialLeaseOpens);
            if (opened.Admission != UnifiedProviderStoreAdmission.Accepted
                || opened.Lease == null)
            {
                opened.Lease?.Dispose();
                return AttemptOutcome.CredentialRejected(
                    attempt.WorkItem,
                    opened.Admission,
                    observedAt,
                    observedAt + options.ResultFreshness);
            }
            if (!attempt.AttachLease(opened.Lease))
            {
                return AttemptOutcome.Cancelled(
                    attempt.WorkItem,
                    observedAt,
                    observedAt + options.ResultFreshness);
            }

            var customAuthentication = instance.ProviderKind
                == ProviderControlKind.CustomOpenAICompatible
                    ? instance.CredentialConfigured
                        ? ProviderCustomAuthenticationKind.Bearer
                        : ProviderCustomAuthenticationKind.None
                    : ProviderCustomAuthenticationKind.ProfileDefault;
            var target = ProviderCatalogNetworkTarget.FromConfiguredInstance(
                instance,
                customAuthentication);
            var network = await _probe.ObserveOnceAsync(
                    target,
                    opened.Lease,
                    attempt.Token)
                .ConfigureAwait(false);
            return AttemptOutcome.FromNetwork(attempt.WorkItem, network);
        }
        catch (OperationCanceledException)
        {
            observedAt = _clock();
            return attempt.TimedOut
                ? AttemptOutcome.Timeout(
                    attempt.WorkItem,
                    observedAt,
                    observedAt + options.ResultFreshness)
                : AttemptOutcome.Cancelled(
                    attempt.WorkItem,
                    observedAt,
                    observedAt + options.ResultFreshness);
        }
        catch
        {
            observedAt = _clock();
            return AttemptOutcome.Faulted(
                attempt.WorkItem,
                observedAt,
                observedAt + options.ResultFreshness);
        }
        finally
        {
            attempt.ReleaseLease();
        }
    }

    private void ProcessOutcome(
        ProviderStartupHealthScheduler scheduler,
        AttemptOutcome outcome,
        UnifiedProviderControlPlaneSnapshot? currentUnified,
        ProviderControlPlaneStoreSnapshot? currentCore,
        IReadOnlyDictionary<string, UnifiedProviderConfiguredInstance> scheduledInstances,
        IDictionary<string, UnifiedProviderStartupHealthObservation> terminal,
        UnifiedProviderStartupHealthRuntimeOptions options,
        bool closing)
    {
        var workItem = outcome.WorkItem;
        var key = workItem.AttemptId.Value;
        var receivedAt = _clock();
        if (terminal.TryGetValue(key, out var retired))
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                retired.State,
                retired.Reason,
                UnifiedProviderStartupHealthResultAdmission.LateResultRejected,
                closing
                    ? ProviderStartupHealthCompletionAdmission.SchedulerClosed
                    : ProviderStartupHealthCompletionAdmission.RetiredAttempt,
                outcome.NetworkAdmission,
                retired.ObservedAt,
                retired.ExpiresAt,
                outcome.NetworkCalls,
                true));
            return;
        }

        if (currentUnified == null || currentCore == null)
            return;
        var current = currentUnified.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, workItem.InstanceId.Value, StringComparison.Ordinal));
        if (current == null)
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                ProviderRuntimeHealthState.InstanceRemoved,
                UnifiedProviderStartupHealthReason.InstanceRemoved,
                UnifiedProviderStartupHealthResultAdmission.InstanceRemoved,
                ProviderStartupHealthCompletionAdmission.InstanceRemoved,
                outcome.NetworkAdmission,
                receivedAt,
                receivedAt + options.ResultFreshness,
                outcome.NetworkCalls,
                true));
            return;
        }
        if (!current.MatchesGeneration(
                workItem.ConfigurationGeneration,
                scheduledInstances[workItem.InstanceId.Value].ConfigurationFingerprint))
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                ProviderRuntimeHealthState.ConfigurationChanged,
                UnifiedProviderStartupHealthReason.ConfigurationChanged,
                UnifiedProviderStartupHealthResultAdmission.ConfigurationChanged,
                ProviderStartupHealthCompletionAdmission
                    .ConfigurationGenerationMismatch,
                outcome.NetworkAdmission,
                receivedAt,
                receivedAt + options.ResultFreshness,
                outcome.NetworkCalls,
                true));
            return;
        }

        var timingMismatch = outcome.ObservedAt < workItem.NotBefore;
        var expired = outcome.ObservedAt >= workItem.ExpiresAt
                      || receivedAt >= workItem.ExpiresAt;
        var identityMismatch = outcome.NetworkResult != null
            && (outcome.NetworkResult.Provider != workItem.Provider.Kind
                || !string.Equals(
                    outcome.NetworkResult.InstanceId,
                    workItem.InstanceId.Value,
                    StringComparison.Ordinal)
                || outcome.NetworkResult.ConfigurationGeneration
                    != workItem.ConfigurationGeneration);
        var schedulerState = identityMismatch || timingMismatch
            ? ProviderRuntimeHealthState.ProtocolRejected
            : outcome.State;
        var schedulerObservedAt = outcome.ObservedAt;
        if (schedulerObservedAt < workItem.NotBefore)
            schedulerObservedAt = workItem.NotBefore;
        if (schedulerObservedAt >= workItem.ExpiresAt)
            schedulerObservedAt = workItem.ExpiresAt.AddTicks(-1);
        var schedulerExpiresAt = outcome.ExpiresAt > schedulerObservedAt
            ? outcome.ExpiresAt
            : schedulerObservedAt + options.ResultFreshness;
        var receipt = scheduler.TryComplete(
            new ProviderStartupHealthResult(
                workItem,
                schedulerState,
                schedulerObservedAt,
                schedulerExpiresAt),
            currentCore,
            receivedAt);

        if (receipt.Admission == ProviderStartupHealthCompletionAdmission.ResultExpired
            || expired)
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                ProviderRuntimeHealthState.Expired,
                UnifiedProviderStartupHealthReason.ResultExpired,
                UnifiedProviderStartupHealthResultAdmission.Expired,
                receipt.Admission,
                outcome.NetworkAdmission,
                outcome.ObservedAt,
                outcome.ExpiresAt,
                outcome.NetworkCalls,
                false));
            return;
        }
        if (identityMismatch)
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                ProviderRuntimeHealthState.StaleResultRejected,
                UnifiedProviderStartupHealthReason.ResultIdentityMismatch,
                UnifiedProviderStartupHealthResultAdmission.ResultIdentityMismatch,
                receipt.Admission,
                outcome.NetworkAdmission,
                outcome.ObservedAt,
                outcome.ExpiresAt,
                outcome.NetworkCalls,
                false));
            return;
        }
        if (timingMismatch)
        {
            Publish(terminal, CreateObservation(
                workItem,
                scheduledInstances[workItem.InstanceId.Value],
                ProviderRuntimeHealthState.StaleResultRejected,
                UnifiedProviderStartupHealthReason.ResultTimingMismatch,
                UnifiedProviderStartupHealthResultAdmission.ResultTimingMismatch,
                receipt.Admission,
                outcome.NetworkAdmission,
                outcome.ObservedAt,
                outcome.ExpiresAt,
                outcome.NetworkCalls,
                false));
            return;
        }
        Publish(terminal, CreateObservation(
            workItem,
            scheduledInstances[workItem.InstanceId.Value],
            outcome.State,
            outcome.Reason,
            outcome.Admission,
            receipt.Admission,
            outcome.NetworkAdmission,
            outcome.ObservedAt,
            outcome.ExpiresAt,
            outcome.NetworkCalls,
            false));
    }

    private static UnifiedProviderStartupHealthObservation RetirementObservation(
        ProviderStartupHealthRetirement retirement,
        ProviderStartupHealthWorkItem workItem,
        IReadOnlyDictionary<string, UnifiedProviderConfiguredInstance> instances,
        TimeSpan freshness)
    {
        if (retirement == null)
            throw new ArgumentNullException(nameof(retirement));
        if (workItem == null)
            throw new ArgumentNullException(nameof(workItem));
        var (state, reason, admission) = retirement.Reason switch
        {
            ProviderStartupHealthRetirementReason.ConfigurationChanged => (
                ProviderRuntimeHealthState.ConfigurationChanged,
                UnifiedProviderStartupHealthReason.ConfigurationChanged,
                UnifiedProviderStartupHealthResultAdmission.ConfigurationChanged),
            ProviderStartupHealthRetirementReason.InstanceRemoved => (
                ProviderRuntimeHealthState.InstanceRemoved,
                UnifiedProviderStartupHealthReason.InstanceRemoved,
                UnifiedProviderStartupHealthResultAdmission.InstanceRemoved),
            ProviderStartupHealthRetirementReason.ApplicationClosing => (
                ProviderRuntimeHealthState.Cancelled,
                UnifiedProviderStartupHealthReason.ApplicationClosing,
                UnifiedProviderStartupHealthResultAdmission.ApplicationClosing),
            _ => throw new ArgumentOutOfRangeException(nameof(retirement))
        };
        return CreateObservation(
            workItem,
            instances[retirement.InstanceId.Value],
            state,
            reason,
            admission,
            null,
            null,
            retirement.RetiredAt,
            retirement.RetiredAt + freshness,
            0,
            false);
    }

    private void Publish(
        IDictionary<string, UnifiedProviderStartupHealthObservation> terminal,
        UnifiedProviderStartupHealthObservation result)
    {
        terminal[result.AttemptId.Value] = result;
        _onResult?.Invoke(result);
    }

    private static UnifiedProviderStartupHealthObservation CreateObservation(
        ProviderStartupHealthWorkItem workItem,
        UnifiedProviderConfiguredInstance instance,
        ProviderRuntimeHealthState state,
        UnifiedProviderStartupHealthReason reason,
        UnifiedProviderStartupHealthResultAdmission admission,
        ProviderStartupHealthCompletionAdmission? schedulerAdmission,
        ProviderCatalogNetworkAdmission? networkAdmission,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls,
        bool lateResultRejected)
    {
        if (expiresAt <= observedAt)
            expiresAt = observedAt.AddTicks(1);
        return new UnifiedProviderStartupHealthObservation(
            workItem,
            instance,
            state,
            reason,
            admission,
            schedulerAdmission,
            networkAdmission,
            observedAt,
            expiresAt,
            networkCalls,
            lateResultRejected);
    }

    internal static ProviderControlPlaneStoreSnapshot ProjectCoreSnapshot(
        UnifiedProviderControlPlaneSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var instances = snapshot.Instances.Select(instance =>
        {
            var descriptor = ProviderControlPlaneDescriptor.FromRegistry(
                instance.ProviderKind);
            var instanceId = new ProviderConfiguredInstanceId(instance.InstanceId);
            ProviderControlPlaneCredentialState credentialState;
            ProviderControlPlaneCredentialReference? credentialReference;
            if (descriptor.CredentialRequirement
                == ProviderCredentialRequirement.NotRequired)
            {
                credentialState = ProviderControlPlaneCredentialState.NotRequired;
                credentialReference = null;
            }
            else if (instance.CredentialConfigured)
            {
                credentialState = ProviderControlPlaneCredentialState.ReferenceConfigured;
                credentialReference = new ProviderControlPlaneCredentialReference(
                    new ProviderCredentialReferenceId(
                        "credential-" + instance.ConfigurationFingerprint
                            .Substring(0, 32).ToLowerInvariant()),
                    descriptor.Provider,
                    instanceId,
                    instance.ConfigurationGeneration,
                    descriptor.Authentication);
            }
            else
            {
                credentialState = ProviderControlPlaneCredentialState.Missing;
                credentialReference = null;
            }
            return new ProviderConfiguredInstance(
                descriptor,
                instanceId,
                instance.ConfigurationGeneration,
                UnifiedProviderConfiguredInstance.Hash(
                    "D033/UNIFIED-STARTUP-ENDPOINT/1",
                    instance.BaseUri.AbsoluteUri,
                    instance.EndpointProfileId,
                    instance.WorkspaceId),
                credentialState,
                credentialReference,
                instance.CatalogObservedAt);
        }).ToArray();
        return new ProviderControlPlaneStoreSnapshot(
            snapshot.StoreGeneration,
            instances,
            Array.Empty<ProviderCatalogObservation>(),
            Array.Empty<ProviderModelSelection>(),
            Array.Empty<ProviderCapabilityEvidence>(),
            Array.Empty<ProviderFunctionAssignment>(),
            Array.Empty<ProviderHealthObservation>());
    }

    private sealed class ActiveAttempt : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenRegistration _externalCancellation;
        private readonly CancellationTokenRegistration _timeoutCancellation;
        private UnifiedProviderCredentialLease? _lease;
        private bool _cancelled;
        private bool _timedOut;

        internal ProviderStartupHealthWorkItem WorkItem { get; }
        internal CancellationToken Token => _cancellation.Token;
        internal Task<AttemptOutcome> Task { get; private set; } =
            System.Threading.Tasks.Task.FromException<AttemptOutcome>(
                new InvalidOperationException("Attempt has not started."));
        internal bool Retired { get; private set; }
        internal bool TimedOut
        {
            get
            {
                lock (_gate)
                    return _timedOut;
            }
        }

        internal ActiveAttempt(
            ProviderStartupHealthWorkItem workItem,
            CancellationToken cancellationToken,
            TimeSpan attemptLifetime)
        {
            WorkItem = workItem ?? throw new ArgumentNullException(nameof(workItem));
            if (attemptLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(attemptLifetime));
            _cancellation = new CancellationTokenSource();
            _timeout = new CancellationTokenSource();
            _externalCancellation = cancellationToken.Register(
                () => CancelCore(timedOut: false, retired: true));
            _timeoutCancellation = _timeout.Token.Register(
                () => CancelCore(timedOut: true, retired: false));
            _timeout.CancelAfter(attemptLifetime);
        }

        internal void Start(Task<AttemptOutcome> task)
            => Task = task ?? throw new ArgumentNullException(nameof(task));

        internal bool AttachLease(UnifiedProviderCredentialLease lease)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            lock (_gate)
            {
                if (_cancelled)
                {
                    lease.Dispose();
                    return false;
                }
                _lease = lease;
                return true;
            }
        }

        internal void Cancel()
            => CancelCore(timedOut: false, retired: true);

        private void CancelCore(bool timedOut, bool retired)
        {
            UnifiedProviderCredentialLease? lease;
            lock (_gate)
            {
                if (_cancelled)
                    return;
                _cancelled = true;
                _timedOut = timedOut;
                Retired = retired;
                lease = _lease;
                _lease = null;
            }
            lease?.Dispose();
            _cancellation.Cancel();
        }

        internal void ReleaseLease()
        {
            UnifiedProviderCredentialLease? lease;
            lock (_gate)
            {
                lease = _lease;
                _lease = null;
            }
            lease?.Dispose();
        }

        public void Dispose()
        {
            ReleaseLease();
            _externalCancellation.Dispose();
            _timeoutCancellation.Dispose();
            _timeout.Dispose();
            _cancellation.Dispose();
        }
    }

    private sealed class RunExecutionState
    {
        internal int CredentialLeaseOpens;
    }

    private sealed class AttemptOutcome
    {
        internal ProviderStartupHealthWorkItem WorkItem { get; }
        internal ProviderRuntimeHealthState State { get; }
        internal UnifiedProviderStartupHealthReason Reason { get; }
        internal UnifiedProviderStartupHealthResultAdmission Admission { get; }
        internal ProviderCatalogNetworkAdmission? NetworkAdmission { get; }
        internal ProviderCatalogNetworkResult? NetworkResult { get; }
        internal DateTimeOffset ObservedAt { get; }
        internal DateTimeOffset ExpiresAt { get; }
        internal int NetworkCalls { get; }

        private AttemptOutcome(
            ProviderStartupHealthWorkItem workItem,
            ProviderRuntimeHealthState state,
            UnifiedProviderStartupHealthReason reason,
            UnifiedProviderStartupHealthResultAdmission admission,
            ProviderCatalogNetworkAdmission? networkAdmission,
            ProviderCatalogNetworkResult? networkResult,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt,
            int networkCalls)
        {
            WorkItem = workItem;
            State = state;
            Reason = reason;
            Admission = admission;
            NetworkAdmission = networkAdmission;
            NetworkResult = networkResult;
            ObservedAt = observedAt;
            ExpiresAt = expiresAt;
            NetworkCalls = networkCalls;
        }

        internal static AttemptOutcome FromNetwork(
            ProviderStartupHealthWorkItem workItem,
            ProviderCatalogNetworkResult result)
        {
            var (state, reason) = Map(result);
            return new AttemptOutcome(
                workItem,
                state,
                reason,
                UnifiedProviderStartupHealthResultAdmission.Accepted,
                result.Admission,
                result,
                result.ObservedAt,
                result.ExpiresAt,
                result.NetworkCalls);
        }

        internal static AttemptOutcome EndpointRejected(
            ProviderStartupHealthWorkItem workItem,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt)
            => new(
                workItem,
                ProviderRuntimeHealthState.EndpointPolicyRejected,
                UnifiedProviderStartupHealthReason.EndpointRejected,
                UnifiedProviderStartupHealthResultAdmission.EndpointRejected,
                null,
                null,
                observedAt,
                expiresAt,
                0);

        internal static AttemptOutcome CredentialRejected(
            ProviderStartupHealthWorkItem workItem,
            UnifiedProviderStoreAdmission credentialAdmission,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt)
            => new(
                workItem,
                ProviderRuntimeHealthState.AuthenticationRejected,
                UnifiedProviderStartupHealthReason.CredentialRejected,
                UnifiedProviderStartupHealthResultAdmission.CredentialRejected,
                null,
                null,
                observedAt,
                expiresAt,
                0);

        internal static AttemptOutcome Cancelled(
            ProviderStartupHealthWorkItem workItem,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt)
            => new(
                workItem,
                ProviderRuntimeHealthState.Cancelled,
                UnifiedProviderStartupHealthReason.Cancelled,
                UnifiedProviderStartupHealthResultAdmission.Accepted,
                ProviderCatalogNetworkAdmission.Cancelled,
                null,
                observedAt,
                expiresAt,
                0);

        internal static AttemptOutcome Timeout(
            ProviderStartupHealthWorkItem workItem,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt)
            => new(
                workItem,
                ProviderRuntimeHealthState.Timeout,
                UnifiedProviderStartupHealthReason.Timeout,
                UnifiedProviderStartupHealthResultAdmission.Accepted,
                ProviderCatalogNetworkAdmission.Timeout,
                null,
                observedAt,
                expiresAt,
                0);

        internal static AttemptOutcome Faulted(
            ProviderStartupHealthWorkItem workItem,
            DateTimeOffset observedAt,
            DateTimeOffset expiresAt)
            => new(
                workItem,
                ProviderRuntimeHealthState.TemporaryNetworkFailure,
                UnifiedProviderStartupHealthReason.ProbeFault,
                UnifiedProviderStartupHealthResultAdmission.ProbeFault,
                null,
                null,
                observedAt,
                expiresAt,
                0);

        private static (ProviderRuntimeHealthState State,
            UnifiedProviderStartupHealthReason Reason) Map(
            ProviderCatalogNetworkResult result)
        {
            var reason = result.Health.Reason switch
            {
                ProviderHealthReason.CatalogHealthy =>
                    UnifiedProviderStartupHealthReason.CatalogHealthy,
                ProviderHealthReason.CatalogDegraded =>
                    UnifiedProviderStartupHealthReason.CatalogDegraded,
                ProviderHealthReason.ManualSelectionRequired =>
                    UnifiedProviderStartupHealthReason.ManualSelectionRequired,
                ProviderHealthReason.AuthenticationRejected =>
                    UnifiedProviderStartupHealthReason.AuthenticationRejected,
                ProviderHealthReason.RateLimited =>
                    UnifiedProviderStartupHealthReason.RateLimited,
                ProviderHealthReason.EndpointRejected =>
                    UnifiedProviderStartupHealthReason.EndpointRejected,
                ProviderHealthReason.ServiceUnavailable =>
                    UnifiedProviderStartupHealthReason.ServiceUnavailable,
                ProviderHealthReason.NetworkFailure =>
                    UnifiedProviderStartupHealthReason.NetworkFailure,
                ProviderHealthReason.Timeout =>
                    UnifiedProviderStartupHealthReason.Timeout,
                ProviderHealthReason.Cancelled =>
                    UnifiedProviderStartupHealthReason.Cancelled,
                ProviderHealthReason.ProtocolRejected =>
                    UnifiedProviderStartupHealthReason.ProtocolRejected,
                ProviderHealthReason.ObservationUnsupported =>
                    UnifiedProviderStartupHealthReason.ObservationUnsupported,
                _ => throw new ArgumentOutOfRangeException(nameof(result))
            };
            var state = result.Admission switch
            {
                ProviderCatalogNetworkAdmission.Accepted
                    when result.Health.Reason == ProviderHealthReason.CatalogDegraded =>
                    ProviderRuntimeHealthState.EmptyOrPartialCatalog,
                ProviderCatalogNetworkAdmission.Accepted =>
                    ProviderRuntimeHealthState.ReachableAndAdmitted,
                ProviderCatalogNetworkAdmission.CatalogObservationUnsupported
                    or ProviderCatalogNetworkAdmission.NotConfigured
                    or ProviderCatalogNetworkAdmission.CredentialRequired =>
                    ProviderRuntimeHealthState.ObservationUnsupported,
                ProviderCatalogNetworkAdmission.Cancelled =>
                    ProviderRuntimeHealthState.Cancelled,
                ProviderCatalogNetworkAdmission.Timeout =>
                    ProviderRuntimeHealthState.Timeout,
                ProviderCatalogNetworkAdmission.NetworkFailure
                    or ProviderCatalogNetworkAdmission.ServiceUnavailable =>
                    ProviderRuntimeHealthState.TemporaryNetworkFailure,
                ProviderCatalogNetworkAdmission.RateLimited =>
                    ProviderRuntimeHealthState.RateLimited,
                ProviderCatalogNetworkAdmission.AuthenticationRejected
                    or ProviderCatalogNetworkAdmission.CredentialMismatch =>
                    ProviderRuntimeHealthState.AuthenticationRejected,
                ProviderCatalogNetworkAdmission.ConfigurationRejected
                    or ProviderCatalogNetworkAdmission.RedirectRejected =>
                    ProviderRuntimeHealthState.EndpointPolicyRejected,
                ProviderCatalogNetworkAdmission.ResponseRejected
                    or ProviderCatalogNetworkAdmission.ResourceLimitExceeded =>
                    ProviderRuntimeHealthState.ProtocolRejected,
                _ => ProviderRuntimeHealthState.ProtocolRejected
            };
            return (state, reason);
        }
    }
}
