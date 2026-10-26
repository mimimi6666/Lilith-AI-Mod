using LilithRuntimeContracts;

namespace LilithRuntime;

internal interface ILilithDailyNoteGameHost : IAsyncDisposable
{
    Task<bool> OfferAsync(LilithDailyNoteOpportunity opportunity, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken);
    Task<LilithDailyNoteCommand?> PollAsync(string profileId, Guid gameEpoch, int waitMilliseconds, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken);
    CancellationToken GetDeliveryLeaseCancellation(LilithDailyNoteScope scope, LilithGameNotesPeerIdentity peer)
        => new(canceled: true);
}

internal sealed class LilithUnavailableDailyNoteGameHost : ILilithDailyNoteGameHost
{
    internal static readonly LilithUnavailableDailyNoteGameHost Instance = new();
    public Task<bool> OfferAsync(LilithDailyNoteOpportunity opportunity, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<LilithDailyNoteCommand?> PollAsync(string profileId, Guid gameEpoch, int waitMilliseconds, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken) => Task.FromResult<LilithDailyNoteCommand?>(null);
    public Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken) => Task.FromResult(false);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Generation-scoped transport/coordinator, not a second approval authority.
// All mutations serialize under _sync. Capability preparation precedes durable
// reservation; model/write effects still require Commit. Text is single-slot.
internal sealed class LilithDailyNoteCoordinator : ILilithDailyNoteGameHost
{
    private readonly object _sync = new();
    private readonly Guid _generation;
    private readonly LilithDailyNoteClock _clock;
    private readonly Func<LilithDailyNoteGoalStore> _createStore;
    private readonly ILilithDailyNoteNativePort _native;
    private readonly Func<bool> _serviceReady;
    private readonly Func<(string Provider, string Model)> _route;
    private readonly Func<Guid?> _getUiLeaseId;
    private readonly Func<Guid, bool> _isUiLeaseCurrent;
    private readonly Func<LilithDailyNoteScope, Guid?, bool> _registerActive;
    private readonly Action<LilithDailyNoteScope> _revokeActive;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _tasks = new();
    private readonly Dictionary<Guid, Work> _registered = new();
    private TaskCompletionSource<bool> _available = Signal();
    private LilithDailyNoteGoalStore? _store;
    private LilithDailyNoteGoalSnapshot? _state;
    private LilithDailyNoteOpportunity? _opportunity;
    private LilithGameNotesPeerIdentity _peer;
    private Active? _active;
    private Preparation? _preparing;
    private DateTimeOffset _nextPrepareUtc;
    private Work? _pending;
    private long _logicalSequence;
    private int _queryCount;
    private DateTimeOffset _nextQueryUtc;
    private bool _faulted;
    private bool _disposed;
    private readonly Action<LilithDailyNoteDiagnostic>? _publishDiagnostic;
    private LilithDailyNoteDiagnostic? _diagnostic;
    internal LilithDailyNoteDiagnostic? Diagnostic
    { get { lock (_sync) return _diagnostic; } }

    internal LilithDailyNoteGoalSnapshot? Snapshot
    { get { lock (_sync) return _state; } }

    internal LilithDailyNoteCoordinator(LilithRuntimeService runtime, LilithRuntimeSnapshot snapshot)
    {
        if (snapshot.State != LilithRuntimeState.Ready || !snapshot.GatewayReady || snapshot.GenerationId == Guid.Empty)
            throw new ArgumentException("Daily notes require an attested Runtime generation.");
        _generation = snapshot.GenerationId;
        _clock = new LilithDailyNoteClock();
        // Lazy: merely composing an Off product never opens a player directory.
        _createStore = () => new LilithDailyNoteGoalStore(snapshot.HermesHomePath, LilithDailyNoteProtocol.ProfileId, _clock.TimeZone);
        _native = new LilithDailyNoteNativeAdapter(new LilithRuntimeGameNotesGateway(runtime));
        _serviceReady = () => runtime.State == LilithRuntimeState.Ready
            && !string.IsNullOrEmpty(runtime.EffectiveProviderId) && !string.IsNullOrEmpty(runtime.EffectiveModelId);
        _route = runtime.EffectiveModelIdentity;
        _getUiLeaseId = runtime.DailyNoteApprovals.GetUiLeaseId;
        _isUiLeaseCurrent = runtime.DailyNoteApprovals.IsUiLeaseCurrent;
        _registerActive = runtime.DailyNoteApprovals.RegisterActive;
        _revokeActive = runtime.DailyNoteApprovals.RevokeActive;
        _publishDiagnostic = runtime.PublishDailyNoteDiagnostic;
        _native.ToolRequested += OnToolRequested;
    }

    internal LilithDailyNoteCoordinator(Guid generation, Func<LilithDailyNoteGoalStore> createStore,
        ILilithDailyNoteNativePort native, LilithDailyNoteClock clock, Func<bool>? serviceReady = null,
        Func<(string Provider, string Model)>? route = null, Func<Guid?>? getUiLeaseId = null,
        Func<Guid, bool>? isUiLeaseCurrent = null,
        Func<LilithDailyNoteScope, Guid?, bool>? registerActive = null, Action<LilithDailyNoteScope>? revokeActive = null,
        Action<LilithDailyNoteDiagnostic>? publishDiagnostic = null)
    {
        if (generation == Guid.Empty) throw new ArgumentException("Daily generation required.");
        _generation = generation; _createStore = createStore; _native = native; _clock = clock;
        _serviceReady = serviceReady ?? (() => true); _route = route ?? (() => ("synthetic", "synthetic"));
        _getUiLeaseId = getUiLeaseId ?? (() => null); _isUiLeaseCurrent = isUiLeaseCurrent ?? (_ => false);
        _registerActive = registerActive ?? ((_, _) => true); _revokeActive = revokeActive ?? (_ => { });
        _publishDiagnostic = publishDiagnostic;
        _native.ToolRequested += OnToolRequested;
    }

    public Task<bool> OfferAsync(LilithDailyNoteOpportunity opportunity, LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preparation? start = null;
        lock (_sync)
        {
            if (_disposed || _faulted || !peer.IsValid || opportunity.ProfileId != LilithDailyNoteProtocol.ProfileId) return Task.FromResult(false);
            var reading = _clock.Read(_state?.UtcHighWater ?? DateTimeOffset.MinValue);
            if (opportunity.ObservedAtUtc > reading.UtcNow || opportunity.ExpiresAtUtc <= reading.UtcNow) return Task.FromResult(false);
            if (_opportunity is { } previous && SamePeer(_peer, peer)
                && opportunity.Sequence <= previous.Sequence) return Task.FromResult(false);
            if (_opportunity is { } prior && (!SamePeer(_peer, peer) || prior.GameEpoch != opportunity.GameEpoch))
            {
                RevokeLocked();
                if (prior.GameEpoch == opportunity.GameEpoch) return Task.FromResult(false);
            }
            _peer = peer; _opportunity = opportunity;
            if (!opportunity.Enabled || !opportunity.Safe || !reading.Trusted)
            {
                RevokeLocked();
                ObserveLocked(!opportunity.Enabled ? LilithDailyNoteDiagnosticReason.Disabled
                    : !reading.Trusted ? LilithDailyNoteDiagnosticReason.ClockUntrusted : LilithDailyNoteDiagnosticReason.Unsafe, reading.UtcNow);
                return Task.FromResult(true);
            }
            if (_active is { } running)
            {
                if (_route() != running.Route || !_serviceReady() || !UiCurrent(running.Readiness)) RevokeLocked();
                return Task.FromResult(true); // A fresh heartbeat renews safety, never another model call.
            }
            if (_preparing is { } preparing)
            {
                if (_route() != preparing.Route || !_serviceReady()) RevokeLocked();
                return Task.FromResult(true); // Coalesce heartbeats; do not overlap bootstrap workers.
            }
            try
            {
                if (_store is null)
                {
                    _store = _createStore();
                    _state = _store.Initialize(ownerAuthorizedFirstUse: true);
                    _logicalSequence = _state.Current?.LastOpportunitySequence ?? 0;
                    reading = _clock.Read(_state.UtcHighWater);
                    if (!reading.Trusted) { RevokeLocked(); return Task.FromResult(true); }
                    CommitLocked(LilithDailyNoteGoal.ResumeAfterRestart(_state, Observation(reading.UtcNow, reading.Trusted)));
                }
                if (_state!.Current?.Phase is LilithDailyNoteGoalPhase.EvaluationReserved or LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Unknown)
                {
                    ObserveLocked(LilithDailyNoteDiagnosticReason.ReconciliationRequired, reading.UtcNow);
                    QueueQueryLocked(reading); return Task.FromResult(true);
                }
                if (!_serviceReady())
                {
                    ObserveLocked(LilithDailyNoteDiagnosticReason.ServiceUnavailable, reading.UtcNow);
                    return Task.FromResult(true);
                }
                // Preserve the last concrete failure throughout retry backoff.
                if (reading.UtcNow < _nextPrepareUtc) return Task.FromResult(true);
                var sequence = checked(Math.Max(_logicalSequence, _state.Current?.LastOpportunitySequence ?? 0) + 1);
                var transition = LilithDailyNoteGoal.TryBeginEvaluation(_state, Observation(reading.UtcNow, reading.Trusted, sequence));
                if (transition.Effect == LilithDailyNoteEffect.Evaluate)
                {
                    // Pure eligibility peek only. No attempt/goal is published until
                    // native readiness returns and the current safety is rechecked.
                    var request = new LilithDailyNotePreparation(LilithDailyNoteProtocol.ProfileId, _generation,
                        opportunity.GameEpoch, Guid.NewGuid(), opportunity.Locale, _getUiLeaseId());
                    start = _preparing = new(request, _peer, _route(), LocalDay(reading.UtcNow));
                    ObserveLocked(LilithDailyNoteDiagnosticReason.Preparing, reading.UtcNow);
                }
                else ObserveLocked(LilithDailyNoteDiagnosticReason.GoalIneligible, reading.UtcNow);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { ObserveLocked(LilithDailyNoteDiagnosticReason.StoreFault, reading.UtcNow); FaultLocked(); return Task.FromResult(false); }
        }
        if (start is not null) Track(RunPreparationAsync(start));
        return Task.FromResult(true);
    }

    private async Task RunPreparationAsync(Preparation preparation)
    {
        Active? start = null;
        var consumed = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, preparation.Peer.PeerExited);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            Task<LilithDailyNoteReadiness> preparing;
            lock (_sync)
            {
                var reading = _clock.Read(_state!.UtcHighWater);
                if (!PreparationCurrentLocked(preparation, reading))
                { ObserveLocked(LilithDailyNoteDiagnosticReason.PreparationRevoked, reading.UtcNow); return; }
                preparing = _native.PrepareAsync(preparation.Request, deadline.Token);
            }
            while (!preparing.IsCompleted)
            {
                lock (_sync)
                {
                    if (!PreparationCurrentLocked(preparation, _clock.Read(_state!.UtcHighWater))) RevokePreparationLocked(preparation);
                }
                await Task.WhenAny(preparing, Task.Delay(250, deadline.Token)).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
            }
            var ready = await preparing.WaitAsync(deadline.Token).ConfigureAwait(false);
            lock (_sync)
            {
                var reading = _clock.Read(_state!.UtcHighWater);
                LilithDailyNoteDiagnosticReason? rejected =
                    !PreparationCurrentLocked(preparation, reading) ? LilithDailyNoteDiagnosticReason.PreparationRevoked
                    : ready.Request != preparation.Request ? LilithDailyNoteDiagnosticReason.InvalidProof
                    : !ready.Ready ? LilithDailyNoteDiagnostic.NativeReason(ready.Code)
                    : ready.ExpiresAtUtc is not { } expires || expires <= reading.UtcNow
                        || expires - reading.UtcNow > TimeSpan.FromSeconds(5) ? LilithDailyNoteDiagnosticReason.ExpiredProof
                    : (ready.Provider, ready.Model) != preparation.Route ? LilithDailyNoteDiagnosticReason.RouteMismatch
                    : !UiCurrent(ready) ? LilithDailyNoteDiagnosticReason.UiLeaseExpired : null;
                if (rejected is { } reason) { ObserveLocked(reason, reading.UtcNow); return; }
                var sequence = checked(Math.Max(_logicalSequence, _state.Current?.LastOpportunitySequence ?? 0) + 1);
                var transition = LilithDailyNoteGoal.TryBeginEvaluation(_state, Observation(reading.UtcNow, reading.Trusted, sequence));
                if (transition.Effect != LilithDailyNoteEffect.Evaluate)
                { ObserveLocked(LilithDailyNoteDiagnosticReason.GoalIneligible, reading.UtcNow); return; }
                try { CommitLocked(transition); }
                catch { FaultLocked(); throw; }
                _logicalSequence = sequence;
                foreach (var registered in _registered.Values) registered.Dispose();
                _registered.Clear(); _pending = null; _queryCount = 0; _nextQueryUtc = default;
                var scope = Scope(transition.Attempt!, reading.UtcNow, delivery: true);
                start = _active = new(scope, transition.Attempt!, preparation.Route, ready);
                consumed = true;
                ObserveLocked(LilithDailyNoteDiagnosticReason.EvaluationReserved, reading.UtcNow);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Unavailable preparation never reserves/refunds a model attempt;
            // never forward exception text to diagnostics.
            lock (_sync)
            {
                ObserveLocked(preparation.Revoked ? LilithDailyNoteDiagnosticReason.PreparationRevoked
                    : error is OperationCanceledException or TimeoutException ? LilithDailyNoteDiagnosticReason.PreparationTimeout
                    : error is InvalidDataException or ArgumentException ? LilithDailyNoteDiagnosticReason.InvalidProof
                    : LilithDailyNoteDiagnosticReason.PreparationError, _clock.Read(_state?.UtcHighWater ?? DateTimeOffset.MinValue).UtcNow);
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_preparing == preparation)
                {
                    _preparing = null;
                    var reading = _clock.Read(_state?.UtcHighWater ?? DateTimeOffset.MinValue);
                    if (reading.Trusted) _nextPrepareUtc = consumed ? default : reading.UtcNow.AddSeconds(30);
                    if (!consumed && reading.Trusted && _diagnostic is { } diagnostic)
                        ObserveLocked(diagnostic.Reason, reading.UtcNow, _nextPrepareUtc);
                }
            }
            if (!consumed)
            {
                try { await CancelPreparationAsync(preparation).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
        }
        if (start is not null) Track(RunNativeAsync(start));
    }

    private bool PreparationCurrentLocked(Preparation preparation, LilithDailyNoteClockReading reading)
        => _preparing == preparation && !preparation.Revoked && SafeLocked(reading) && _serviceReady()
            && SamePeer(_peer, preparation.Peer) && _opportunity!.GameEpoch == preparation.Request.GameEpoch
            && _opportunity.Locale == preparation.Request.Locale && _route() == preparation.Route
            && LocalDay(reading.UtcNow) == preparation.Day;

    private DateOnly LocalDay(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, _clock.TimeZone).DateTime);
    private bool UiCurrent(LilithDailyNoteReadiness readiness)
        => !readiness.RequiresUi || readiness.UiLeaseId is { } ui && _isUiLeaseCurrent(ui);

    private void RevokePreparationLocked(Preparation preparation)
    {
        if (preparation.Revoked) return;
        preparation.Revoked = true;
        Track(CancelPreparationAsync(preparation));
    }

    private Task CancelPreparationAsync(Preparation preparation)
    {
        lock (_sync)
        {
            return preparation.CancellationTask ??= _native.CancelPreparationAsync(preparation.Request, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(4));
        }
    }

    public async Task<LilithDailyNoteCommand?> PollAsync(string profileId, Guid gameEpoch, int waitMilliseconds,
        LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        if (waitMilliseconds is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, peer.PeerExited, _lifetime.Token);
        if (waitMilliseconds > 0) timeout.CancelAfter(waitMilliseconds);
        do
        {
            Task wake;
            lock (_sync)
            {
                if (_disposed || _faulted || !peer.IsValid || profileId != LilithDailyNoteProtocol.ProfileId
                    || !SamePeer(_peer, peer) || _opportunity?.GameEpoch != gameEpoch) return null;
                var reading = _clock.Read(_state?.UtcHighWater ?? DateTimeOffset.MinValue);
                if (!SafeLocked(reading)) { RevokeLocked(); return null; }
                if (_pending is { } work && work.Command is { } command)
                {
                    if (work.Scope.ExpiresAtUtc <= reading.UtcNow) { _pending = null; work.Command = null; work.Cancel(); return null; }
                    work.PickedUpBy = peer; work.Command = null; _pending = null;
                    var pickupDeadline = work.Scope.ExpiresAtUtc < _opportunity!.ExpiresAtUtc ? work.Scope.ExpiresAtUtc : _opportunity.ExpiresAtUtc;
                    work.Cancellation.CancelAfter(pickupDeadline - reading.UtcNow);
                    work.PeerExit = peer.PeerExited.Register(work.Cancel);
                    return command;
                }
                wake = _available.Task;
            }
            if (waitMilliseconds == 0) return null;
            try { await wake.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        } while (!timeout.IsCancellationRequested);
        return null;
    }

    public CancellationToken GetDeliveryLeaseCancellation(LilithDailyNoteScope scope, LilithGameNotesPeerIdentity peer)
    {
        lock (_sync)
        {
            if (_disposed || _faulted || !peer.IsValid || !_registered.TryGetValue(scope.LeaseId, out var work)
                || work.Scope != scope || work.PickedUpBy is not { } picked || !SamePeer(picked, peer))
                return new(canceled: true);
            var reading = _clock.Read(_state!.UtcHighWater);
            if (!SafeLocked(reading) || _active is { } active && (active.Revoked || _route() != active.Route || !UiCurrent(active.Readiness))) RevokeLocked();
            return work.Cancellation.Token;
        }
    }

    public async Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result,
        LilithGameNotesPeerIdentity peer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool acknowledgeNative;
        lock (_sync)
        {
            // Revocation ends write authority, not exact late receipt accounting.
            if (_disposed || _faulted || !peer.IsValid || !_registered.TryGetValue(scope.LeaseId, out var work)
                || work.Scope != scope || work.PickedUpBy is not { } picked || !SamePeer(picked, peer)
                || result.OperationId != scope.OperationId || result.DeliveryId != scope.DeliveryId) return false;
            if (work.Result is { } previous) return SameResult(previous, result);
            var reading = _clock.Read(_state!.UtcHighWater);
            try
            {
                var transition = LilithDailyNoteGoal.ApplyEvidence(_state, work.Attempt,
                    new(LilithDailyNoteEvidenceKind.Receipt, result), Observation(reading.UtcNow, reading.Trusted));
                CommitLocked(transition);
                // A bad clock or mismatched artifact is not an accepted success.
                if (result.Status == LilithNoteInboxStatus.Delivered && _state!.LastSatisfiedDeliveryId != result.DeliveryId) return false;
                work.Result = result;
                work.Cancel();
                acknowledgeNative = _active?.Scope == scope;
            }
            catch (Exception error) when (error is not OutOfMemoryException) { FaultLocked(); return false; }
        }
        if (acknowledgeNative)
        {
            try { await _native.CompleteAsync(scope, result, _lifetime.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { /* Durable receipt already won. */ }
        }
        return true;
    }

    private void OnToolRequested(LilithDailyNoteCommand command)
    {
        LilithNoteInboxResult? refusal = null;
        lock (_sync)
        {
            if (_disposed || _faulted || _active is not { } active || active.Scope != command.Scope || active.ToolSeen) return;
            active.ToolSeen = true;
            var reading = _clock.Read(_state!.UtcHighWater);
            try
            {
                if (active.Revoked || !SafeLocked(reading) || reading.UtcNow >= active.Scope.ExpiresAtUtc
                    || _route() != active.Route || !_serviceReady() || !UiCurrent(active.Readiness))
                    refusal = new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Rejected, "daily.revoked");
                else if (command.Action == LilithNoteInboxAction.Deliver)
                {
                    var transition = LilithDailyNoteGoal.TryBeginDelivery(_state, active.Attempt, Observation(reading.UtcNow, reading.Trusted));
                    CommitLocked(transition);
                    if (transition.Effect == LilithDailyNoteEffect.Deliver)
                    { active.Attempt = transition.Attempt!; QueueLocked(new Work(command, active.Attempt, _lifetime.Token)); }
                    else refusal = new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Rejected, "daily.not_reserved");
                }
                else QueueLocked(new Work(command, active.Attempt, _lifetime.Token));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { FaultLocked(); refusal = new(command.Scope.OperationId, command.Scope.DeliveryId, LilithNoteInboxStatus.Unknown, "daily.ledger_unavailable"); }
        }
        if (refusal is not null) Track(AcknowledgeRefusalAsync(command.Scope, refusal));
    }

    private async Task AcknowledgeRefusalAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result)
    {
        try { await _native.CompleteAsync(scope, result, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    private async Task RunNativeAsync(Active active)
    {
        LilithDailyNoteNativeOutcome? outcome = null;
        var invoked = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var initial = _clock.Read(DateTimeOffset.MinValue);
        var remaining = active.Scope.ExpiresAtUtc - initial.UtcNow;
        deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
        try
        {
            Task<LilithDailyNoteNativeOutcome> evaluation;
            lock (_sync)
            {
                var reading = _clock.Read(_state!.UtcHighWater);
                if (active.Revoked || !SafeLocked(reading) || _route() != active.Route || !_serviceReady()
                    || active.Readiness.ExpiresAtUtc <= reading.UtcNow || !UiCurrent(active.Readiness)
                    || !_registerActive(active.Scope, active.Readiness.RequiresUi ? active.Readiness.UiLeaseId : null))
                { outcome = new("no_effect", "runtime.not_dispatched", null); return; }
                // Establish the invocation synchronously with the final local
                // owner check; Off cannot slip between this check and dispatch.
                evaluation = _native.EvaluateAsync(active.Scope, _opportunity!.Locale, active.Readiness.Token!.Value, deadline.Token);
                invoked = true;
            }
            while (!evaluation.IsCompleted)
            {
                bool revoked;
                lock (_sync)
                {
                    var reading = _clock.Read(_state!.UtcHighWater);
                    if (!SafeLocked(reading) || _route() != active.Route || !_serviceReady() || !UiCurrent(active.Readiness)
                        || _registered.TryGetValue(active.Scope.LeaseId, out var delivery)
                            && delivery.Result is null && delivery.Cancellation.IsCancellationRequested) RevokeLocked();
                    revoked = active.Revoked;
                }
                if (revoked)
                {
                    try { await _native.CancelAsync(active.Scope, _lifetime.Token).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
                    break; // Still await the single native worker; never overlap a replacement.
                }
                await Task.WhenAny(evaluation, Task.Delay(250, deadline.Token)).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
            }
            outcome = await evaluation.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally
        {
            lock (_sync)
            {
                if (_active == active)
                {
                    _revokeActive(active.Scope);
                    try
                    {
                        if (!_faulted && _state?.Current?.Attempt is { } attempt)
                        {
                            var reading = _clock.Read(_state.UtcHighWater);
                            var evidence = outcome is { Status: "no_effect" } && !active.ToolSeen
                                ? new LilithDailyNoteEvidence(LilithDailyNoteEvidenceKind.DefiniteNoEffect)
                                : new LilithDailyNoteEvidence(LilithDailyNoteEvidenceKind.Unknown);
                            // Native completion text (even a well-shaped artifact)
                            // cannot substitute for authenticated game CompleteAsync.
                            CommitLocked(LilithDailyNoteGoal.ApplyEvidence(_state, attempt, evidence, Observation(reading.UtcNow, reading.Trusted)));
                        }
                    }
                    catch (Exception error) when (error is not OutOfMemoryException) { _faulted = true; }
                    _active = null;
                    if (_pending?.Scope == active.Scope) { _pending.Command = null; _pending = null; }
                }
            }
            if (!invoked)
            {
                try { await _native.CancelPreparationAsync(active.Readiness.Request, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
        }
    }

    private void QueueQueryLocked(LilithDailyNoteClockReading reading)
    {
        if (_pending is not null || _queryCount >= 3 || reading.UtcNow < _nextQueryUtc || !reading.Trusted) return;
        var attempt = _state!.Current!.Attempt!;
        var scope = Scope(attempt, reading.UtcNow, delivery: false);
        QueueLocked(new Work(new(scope, LilithNoteInboxAction.Status, null, _opportunity!.Locale), attempt, _lifetime.Token));
        _queryCount++; _nextQueryUtc = reading.UtcNow.AddSeconds(30);
    }

    private void QueueLocked(Work work)
    {
        if (_pending is not null || _registered.Count >= 8) throw new InvalidOperationException("Daily command capacity exhausted.");
        _registered.Add(work.Scope.LeaseId, work); _pending = work;
        var signal = _available; _available = Signal(); signal.TrySetResult(true);
    }

    private LilithDailyNoteScope Scope(LilithDailyNoteAttempt attempt, DateTimeOffset now, bool delivery)
        => new(LilithDailyNoteProtocol.ProfileId, attempt.GoalId, attempt.AttemptId, attempt.DeliveryId, attempt.OperationId,
            _generation, _opportunity!.GameEpoch, Guid.NewGuid(), delivery ? _clock.DeliveryDeadline(now) : now.AddSeconds(60));

    private LilithDailyNoteObservation Observation(DateTimeOffset now, bool trusted, long? sequence = null)
        => new(LilithDailyNoteProtocol.ProfileId, _clock.TimeZone, now, trusted,
            _opportunity?.Enabled == true, _serviceReady(), _opportunity?.Safe == true,
            sequence ?? Math.Max(_logicalSequence, _state?.Current?.LastOpportunitySequence ?? 0));

    private bool SafeLocked(LilithDailyNoteClockReading reading)
        => !_disposed && !_faulted && reading.Trusted && _peer.IsValid
            && _opportunity is { Enabled: true, Safe: true } offer && offer.ExpiresAtUtc > reading.UtcNow;

    private void CommitLocked(LilithDailyNoteTransition transition)
    {
        if (transition.Effect == LilithDailyNoteEffect.None && transition.Next == _state) return;
        _state = _store!.Commit(_state!, transition).Next;
    }

    private void RevokeLocked()
    {
        if (_preparing is { } preparation) RevokePreparationLocked(preparation);
        if (_active is { } active) { active.Revoked = true; _revokeActive(active.Scope); }
        foreach (var work in _registered.Values) work.Cancel();
        if (_pending is { } pending) { pending.Command = null; _pending = null; }
    }

    private void ObserveLocked(LilithDailyNoteDiagnosticReason reason, DateTimeOffset atUtc, DateTimeOffset? retry = null)
    {
        _diagnostic = new(_generation, reason, atUtc, retry);
        // Observability must never decide whether an operation is admitted.
        try { _publishDiagnostic?.Invoke(_diagnostic); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    private void FaultLocked() { _faulted = true; RevokeLocked(); }
    private static bool SamePeer(LilithGameNotesPeerIdentity a, LilithGameNotesPeerIdentity b)
        => a.ProcessId == b.ProcessId && a.ProcessCreationTime == b.ProcessCreationTime;
    private static bool SameResult(LilithNoteInboxResult a, LilithNoteInboxResult b)
        => LilithNoteInboxProtocol.EncodeResult(a).GetRawText() == LilithNoteInboxProtocol.EncodeResult(b).GetRawText();
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Track(Task task)
    {
        lock (_sync) _tasks.Add(task);
        _ = task.ContinueWith(done => { _ = done.Exception; lock (_sync) _tasks.Remove(done); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        LilithDailyNoteScope? active;
        lock (_sync) { if (_disposed) return; _disposed = true; RevokeLocked(); active = _active?.Scope; }
        _native.ToolRequested -= OnToolRequested;
        if (active is not null)
        {
            try { await _native.CancelAsync(active, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        _lifetime.Cancel();
        Task[] tasks;
        lock (_sync) { tasks = _tasks.ToArray(); _available.TrySetResult(false); }
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        await _native.DisposeAsync().ConfigureAwait(false);
        lock (_sync) foreach (var work in _registered.Values) work.Dispose();
        _lifetime.Dispose();
    }

    private sealed class Preparation(LilithDailyNotePreparation request, LilithGameNotesPeerIdentity peer,
        (string Provider, string Model) route, DateOnly day)
    {
        internal readonly LilithDailyNotePreparation Request = request;
        internal readonly LilithGameNotesPeerIdentity Peer = peer;
        internal readonly (string Provider, string Model) Route = route;
        internal readonly DateOnly Day = day;
        internal bool Revoked;
        internal Task? CancellationTask;
    }
    private sealed class Active(LilithDailyNoteScope scope, LilithDailyNoteAttempt attempt, (string Provider, string Model) route,
        LilithDailyNoteReadiness readiness)
    {
        internal readonly LilithDailyNoteScope Scope = scope;
        internal LilithDailyNoteAttempt Attempt = attempt;
        internal readonly (string Provider, string Model) Route = route;
        internal readonly LilithDailyNoteReadiness Readiness = readiness;
        internal bool ToolSeen;
        internal bool Revoked;
    }
    private sealed class Work(LilithDailyNoteCommand command, LilithDailyNoteAttempt attempt, CancellationToken lifetime) : IDisposable
    {
        internal readonly LilithDailyNoteScope Scope = command.Scope;
        internal readonly LilithDailyNoteAttempt Attempt = attempt;
        internal LilithDailyNoteCommand? Command = command;
        internal LilithGameNotesPeerIdentity? PickedUpBy;
        internal LilithNoteInboxResult? Result;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        internal CancellationTokenRegistration PeerExit;
        internal void Cancel() => Cancellation.Cancel();
        public void Dispose() { Cancel(); PeerExit.Dispose(); Cancellation.Dispose(); }
    }
}
